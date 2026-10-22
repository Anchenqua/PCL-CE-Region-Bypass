using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace PclRegionBypass
{
    /// <summary>一次「启动 + 打补丁」的会话。</summary>
    internal sealed class LaunchSession
    {
        public readonly ManualResetEventSlim CoreReady = new ManualResetEventSlim(false);

        public int ProcessId;
        public string ExePath;
        public string CommandLine;
        public Exception Error;
        public ProcessBypass Bypass;

        private IntPtr _processHandle;

        internal void SetHandle(IntPtr handle)
        {
            _processHandle = handle;
        }

        internal IntPtr Handle
        {
            get { return _processHandle; }
        }

        internal void CloseHandle()
        {
            if (_processHandle != IntPtr.Zero && _processHandle != new IntPtr(-1))
                Win32.CloseHandle(_processHandle);
            _processHandle = IntPtr.Zero;
        }

        /// <summary>等启动器退出并返回它的退出码（命令行模式下原样透传）。</summary>
        public int WaitForExitCode()
        {
            if (_processHandle == IntPtr.Zero || _processHandle == new IntPtr(-1)) return 0;
            if (Win32.WaitForSingleObject(_processHandle, Win32.INFINITE) != 0) return 0;
            uint code;
            return Win32.GetExitCodeProcess(_processHandle, out code) ? (int)code : 0;
        }
    }

    internal static class Launcher
    {
        /// <summary>
        ///     启动启动器并异步完成三批钩子。CoreReady 在第一批（地区名 / 地区 ID）生效后置位。
        /// </summary>
        public static LaunchSession Launch(string exePath, string arguments, bool noDelay, bool keepWatching,
            bool registryHooks, Action<string> log)
        {
            if (log == null) log = delegate { };
            if (!File.Exists(exePath)) throw new BypassException("找不到启动器：" + exePath);

            var session = new LaunchSession();
            session.ExePath = exePath;
            session.CommandLine = arguments ?? string.Empty;

            var startupInfo = new Win32.STARTUPINFO();
            startupInfo.cb = Marshal.SizeOf(typeof(Win32.STARTUPINFO));
            // 让子进程继承我们的标准输入输出：控制台模式下能看到启动器自己的输出
            startupInfo.dwFlags = 0x00000100; // STARTF_USESTDHANDLES
            startupInfo.hStdInput = Win32.GetStdHandle(-10);
            startupInfo.hStdOutput = Win32.GetStdHandle(-11);
            startupInfo.hStdError = Win32.GetStdHandle(-12);

            var commandLine = new StringBuilder();
            commandLine.Append('"').Append(exePath).Append('"');
            if (!string.IsNullOrEmpty(arguments)) commandLine.Append(' ').Append(arguments);

            Win32.PROCESS_INFORMATION processInformation;
            string workingDirectory = Path.GetDirectoryName(exePath);
            if (!Win32.CreateProcessW(exePath, commandLine, IntPtr.Zero, IntPtr.Zero, true, 0, IntPtr.Zero,
                    workingDirectory, ref startupInfo, out processInformation))
                throw new BypassException("启动失败：" + Win32.LastError());

            session.ProcessId = processInformation.dwProcessId;
            session.SetHandle(processInformation.hProcess);
            Win32.CloseHandle(processInformation.hThread);

            log("已启动：" + exePath + " (PID " + session.ProcessId + ")");

            var worker = new Thread(delegate()
            {
                try
                {
                    WaitForLoader(session.ProcessId, 20000);

                    var bypass = new ProcessBypass(session.Handle, session.ProcessId, log);
                    bypass.Prepare();
                    bypass.InstallCore();
                    session.Bypass = bypass;
                    session.CoreReady.Set();
                    log("第一批钩子已生效：下载 / 安装 / 补全 / 整合包功能已解锁。");

                    if (registryHooks) StartRegistryHookThread(bypass, log);

                    if (noDelay)
                    {
                        bypass.InstallLocale();
                    }
                    else
                    {
                        // 等主界面出现后再挂语言相关钩子：
                        // 启动阶段保持真实的 zh-CN，PCL 的「跟随系统」语言设置才不会跳成英文。
                        bool shown = WaitForMainWindow(session.ProcessId, 60000);
                        bypass.InstallLocale();
                        log(shown ? "主界面已就绪，语言相关钩子已补上。" : "等待主界面超时，已直接补上语言相关钩子。");
                    }

                    if (keepWatching) Watch(session, registryHooks, log);
                    else session.CloseHandle();
                }
                catch (Exception ex)
                {
                    session.Error = ex;
                    session.CoreReady.Set();
                    log("失败：" + ex.Message);
                    session.CloseHandle();
                }
            });
            worker.IsBackground = true;
            worker.Name = "pcl-bypass-worker";
            worker.Start();

            return session;
        }

        /// <summary>给已经在运行的启动器打补丁。</summary>
        public static int Attach(int processId, bool registryHooks, Action<string> log)
        {
            if (log == null) log = delegate { };
            IntPtr handle = Win32.OpenProcess(Win32.PROCESS_FULL_ACCESS, false, processId);
            if (handle == IntPtr.Zero) throw new BypassException("打开进程失败：" + Win32.LastError());

            try
            {
                WaitForLoader(processId, 10000);
                var bypass = new ProcessBypass(handle, processId, log);
                bypass.Prepare();
                bypass.InstallCore();
                bypass.InstallLocale();
                if (registryHooks) InstallRegistryWithRetry(bypass, 5000, log);
                return processId;
            }
            finally
            {
                Win32.CloseHandle(handle);
            }
        }

        private static void StartRegistryHookThread(ProcessBypass bypass, Action<string> log)
        {
            var thread = new Thread(delegate()
            {
                try
                {
                    if (InstallRegistryWithRetry(bypass, 30000, log)) return;
                    log("警告：目标进程未加载 advapi32.dll，DeviceRegion 屏蔽未启用；" +
                        "若本机 OOBE 设备区域为中国大陆，仍可能被判为受限。");
                }
                catch (Exception ex)
                {
                    log("注册表钩子挂载失败：" + ex.Message);
                }
            });
            thread.IsBackground = true;
            thread.Name = "pcl-bypass-registry";
            thread.Start();
        }

        /// <summary>等 advapi32.dll 被目标进程加载后立刻挂注册表钩子（PCL 启动早期尚未加载它）。</summary>
        private static bool InstallRegistryWithRetry(ProcessBypass bypass, int timeoutMs, Action<string> log)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (bypass.TryInstallRegistry()) return true;
                Thread.Sleep(150);
            }

            return false;
        }

        /// <summary>启动器自身被更新后可能重启；盯着同名同路径的新进程继续打补丁。</summary>
        private static void Watch(LaunchSession session, bool registryHooks, Action<string> log)
        {
            string name = Path.GetFileNameWithoutExtension(session.ExePath);
            var patched = new HashSet<int>();
            patched.Add(session.ProcessId);

            while (true)
            {
                Thread.Sleep(1000);

                Process[] processes;
                try
                {
                    processes = Process.GetProcessesByName(name);
                }
                catch
                {
                    continue;
                }

                foreach (Process process in processes)
                {
                    try
                    {
                        if (patched.Contains(process.Id)) continue;
                        if (!SamePath(process, session.ExePath)) continue;

                        patched.Add(process.Id);
                        log("检测到新的启动器进程 PID " + process.Id + "，正在补丁…");
                        Attach(process.Id, registryHooks, log);
                        log("PID " + process.Id + " 补丁完成。");
                    }
                    catch (Exception ex)
                    {
                        log("补丁新进程失败：" + ex.Message);
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
        }

        private static bool SamePath(Process process, string expected)
        {
            string path;
            try
            {
                path = process.MainModule == null ? null : process.MainModule.FileName;
            }
            catch
            {
                return false; // 权限不足，跳过
            }

            if (path == null) return false;
            return string.Equals(Path.GetFullPath(path), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase);
        }

        public static bool DeviceRegionRequiresHook()
        {
            try
            {
                RegionSignals signals = RegionPolicyPort.ReadWindowsSignals();
                return signals.DeviceRegionAvailable &&
                       signals.DeviceRegionGeoId == RegionPolicyPort.MainlandChinaGeoId;
            }
            catch
            {
                return false;
            }
        }

        private static void WaitForLoader(int processId, int timeoutMs)
        {
            IntPtr handle = Win32.OpenProcess(Win32.PROCESS_QUERY_INFORMATION | Win32.PROCESS_VM_READ, false,
                processId);
            if (handle == IntPtr.Zero) return;
            try
            {
                var watch = Stopwatch.StartNew();
                while (watch.ElapsedMilliseconds < timeoutMs)
                {
                    if (Modules.Find(Modules.List(handle), "kernel32.dll") != IntPtr.Zero) return;
                    Thread.Sleep(10);
                }
            }
            finally
            {
                Win32.CloseHandle(handle);
            }
        }

        public static bool WaitForMainWindow(int processId, int timeoutMs)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (HasVisibleTopLevelWindow(processId)) return true;
                Thread.Sleep(100);
            }

            return false;
        }

        private static bool HasVisibleTopLevelWindow(int processId)
        {
            bool found = false;
            Win32.EnumWindows(delegate(IntPtr window, IntPtr parameter)
            {
                int ownerProcess;
                Win32.GetWindowThreadProcessId(window, out ownerProcess);
                if (ownerProcess != processId) return true;
                if (!Win32.IsWindowVisible(window)) return true;
                if (Win32.GetWindow(window, Win32.GW_OWNER) != IntPtr.Zero) return true;
                found = true;
                return false;
            }, IntPtr.Zero);
            return found;
        }
    }
}
