using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PclRegionBypass
{
    internal static class Program
    {
        private static readonly string Version = AppInfo.Version;
        private const string MutexName = @"Local\PCLRegionBypass.SingleInstance";
        private const string ShowEventName = @"Local\PCLRegionBypass.ShowWindow";

        [STAThread]
        private static int Main(string[] args)
        {
            var options = Options.Parse(args);

            if (options.ShowHelp)
            {
                OpenConsole();
                PrintHelp();
                return 0;
            }

            if (options.SetAutoStart.HasValue)
            {
                OpenConsole();
                return SetAutoStart(options.SetAutoStart.Value);
            }

            if (options.Gui || args.Length == 0)
            {
                return RunGui(options);
            }

            OpenConsole();
            try
            {
                if (options.Inspect)
                {
                    return Inspect();
                }

                if (options.AttachProcessId > 0)
                {
                    Console.WriteLine("正在给已运行的进程打补丁：PID " + options.AttachProcessId);
                    Launcher.Attach(options.AttachProcessId,
                        options.ForceRegistryHooks || Launcher.DeviceRegionRequiresHook(), Console.WriteLine);
                    Console.WriteLine("完成。");
                    return 0;
                }

                string exePath = options.ExecutablePath;
                if (string.IsNullOrEmpty(exePath)) exePath = Locator.FindLauncher(null);
                if (string.IsNullOrEmpty(exePath))
                {
                    Console.Error.WriteLine("找不到 PCL-CE，请用 --exe <路径> 指定 PCL2_CE_Release_x64.exe");
                    return 1;
                }

                Console.WriteLine("PCL-CE 地区限制解除器 v" + Version);
                Console.WriteLine("目标：" + exePath);
                Console.WriteLine();

                LaunchSession session = Launcher.Launch(exePath, options.PassthroughArguments, options.NoDelay,
                    true, options.ForceRegistryHooks || Launcher.DeviceRegionRequiresHook(), Console.WriteLine);

                session.CoreReady.Wait(30000);
                if (session.Error != null)
                {
                    Console.Error.WriteLine("补丁失败：" + session.Error);
                    return 1;
                }

                Console.WriteLine();
                Console.WriteLine("补丁已生效。按 Ctrl+C 结束监视。");
                return session.WaitForExitCode();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("错误：" + ex.Message);
                return 1;
            }
        }

        // ---------- 图形界面 ----------

        private static int RunGui(Options options)
        {
            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // 已经在托盘里跑着了：把已有窗口叫出来，本次直接退出
                    SignalExistingInstance();
                    return 0;
                }

                EventWaitHandle showEvent;
                try
                {
                    showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                }
                catch
                {
                    showEvent = null;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                Settings.Load();
                bool startHidden = options.Silent || Settings.SilentStart;

                try
                {
                    using (showEvent)
                    {
                        Application.Run(new MainForm(options.ExecutablePath, startHidden, showEvent));
                    }
                }
                finally
                {
                    // 释放单实例名，避免下次启动被误判为「已运行」
                }

                return 0;
            }
        }

        private static void SignalExistingInstance()
        {
            try
            {
                using (EventWaitHandle existing = EventWaitHandle.OpenExisting(ShowEventName))
                    existing.Set();
            }
            catch
            {
                // 老版本实例没有监听器，忽略
            }
        }

        private static int SetAutoStart(bool enabled)
        {
            try
            {
                AutoStart.SetEnabled(enabled);
                bool state = AutoStart.IsEnabled();
                Console.WriteLine("开机启动：" + (state ? "已开启" : "已关闭"));
                if (state) Console.WriteLine("启动项：" + AutoStart.CurrentCommand());
                return state == enabled ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("设置开机启动失败：" + ex.Message);
                return 1;
            }
        }

        // ---------- 诊断 ----------

        private static int Inspect()
        {
            RegionSignals signals = RegionPolicyPort.ReadWindowsSignals();
            RegionAssessment assessment = RegionPolicyPort.Evaluate(signals);

            Console.WriteLine("本机地区信号（PCL-CE 的判定依据）：");
            Console.WriteLine("  GetUserDefaultGeoName      = " + Quote(signals.GeoName) +
                              (signals.GeoNameAvailable ? "  [可用]" : "  [不可用]"));
            Console.WriteLine("  GetUserDefaultLocaleName   = " + Quote(signals.UserLocaleName) +
                              (signals.UserLocaleAvailable ? "  [可用]" : "  [不可用]"));
            Console.WriteLine("  GetUserPreferredUILanguages= " + Join(signals.PreferredUiLanguages) +
                              (signals.PreferredUiLanguagesAvailable ? "  [可用]" : "  [不可用]"));
            Console.WriteLine("  TimeZoneInfo.Local.Id      = " + Quote(signals.TimeZoneId) +
                              (signals.TimeZoneAvailable ? "  [可用]" : "  [不可用]"));
            Console.WriteLine("  HKLM DeviceRegion          = " +
                              (signals.DeviceRegionAvailable
                                  ? signals.DeviceRegionGeoId + "（45 = 中国大陆）"
                                  : "（不存在）"));
            Console.WriteLine("  开机启动                    = " + (AutoStart.IsEnabled() ? "已开启" : "未开启") +
                              (AutoStart.IsEnabled() && !AutoStart.PointsToCurrentExecutable()
                                  ? "（启动项指向别的路径：" + AutoStart.ParsedExecutable() + "）"
                                  : ""));
            Console.WriteLine("  静默启动                    = " + (Settings.SilentStart ? "已开启" : "未开启") +
                              "，关闭行为 = " + Settings.CloseAction);
            Console.WriteLine();
            Console.WriteLine("判定：" + assessment.ClassificationText + "，原因 " + assessment.Reason);
            Console.WriteLine("是否受限：" + (assessment.IsRestricted ? "是 → 下载/安装/补全被禁用" : "否"));
            return 0;
        }

        private static string Quote(string value)
        {
            return value == null ? "(null)" : "\"" + value + "\"";
        }

        private static string Join(IList<string> values)
        {
            if (values == null || values.Count == 0) return "(空)";
            var builder = new StringBuilder();
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) builder.Append(", ");
                builder.Append(values[i]);
            }

            return builder.ToString();
        }

        private static void PrintHelp()
        {
            Console.WriteLine("PCL-CE 地区限制解除器 v" + Version);
            Console.WriteLine();
            Console.WriteLine("用法：");
            Console.WriteLine("  PCLRegionBypass.exe                    打开图形界面（已在托盘运行时唤出已有窗口）");
            Console.WriteLine("  PCLRegionBypass.exe --silent           静默启动：不显示窗口，直接进托盘");
            Console.WriteLine("  PCLRegionBypass.exe --inspect          查看本机地区信号、判定与启动项状态");
            Console.WriteLine("  PCLRegionBypass.exe --exe <path>       启动指定启动器并解除限制（命令行模式）");
            Console.WriteLine("  PCLRegionBypass.exe --attach <pid>     给正在运行的启动器打补丁");
            Console.WriteLine("  PCLRegionBypass.exe --set-autostart on|off   开关开机启动");
            Console.WriteLine();
            Console.WriteLine("选项：");
            Console.WriteLine("  --no-delay    立刻挂上全部钩子（会连带影响进程的区域性，仅调试用）");
            Console.WriteLine("  --registry-hooks  强制挂载 DeviceRegion 注册表钩子（默认仅在检测到设备区域=中国大陆时挂）");
            Console.WriteLine("  --gui         强制打开图形界面");
            Console.WriteLine("  --autostart   开机启动时由启动项传入，标记用");
            Console.WriteLine("  -- <args>     其后的参数原样传给启动器");
            Console.WriteLine();
            Console.WriteLine("原理：在启动器进程内把 6 个地区/语言相关的 Win32 查询函数改写为返回美国值，");
            Console.WriteLine("      必要时再拦截 DeviceRegion 注册表读取。不修改任何文件。");
        }

        private static void OpenConsole()
        {
            // 输出被重定向到管道/文件时（脚本调用），不要再抢 console，否则结果会跑到新窗口里。
            IntPtr standardOutput = GetStdHandle(-11);
            if (standardOutput != IntPtr.Zero && standardOutput != new IntPtr(-1))
            {
                try
                {
                    var writer = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
                    writer.AutoFlush = true;
                    Console.SetOut(writer);
                    var errorWriter = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false));
                    errorWriter.AutoFlush = true;
                    Console.SetError(errorWriter);
                }
                catch
                {
                    // 某些宿主不允许替换输出流，忽略
                }

                return;
            }

            if (AttachConsole(-1)) return;
            AllocConsole();
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int stdHandle);
    }

    internal sealed class Options
    {
        public bool Gui;
        public bool Inspect;
        public bool ShowHelp;
        public bool NoDelay;
        public bool Silent;
        public bool ForceRegistryHooks;
        public int AttachProcessId;
        public string ExecutablePath;
        public string PassthroughArguments = string.Empty;
        public bool? SetAutoStart;

        public static Options Parse(string[] args)
        {
            var options = new Options();
            var passthrough = new List<string>();
            bool afterSeparator = false;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (afterSeparator)
                {
                    passthrough.Add(arg);
                    continue;
                }

                switch (arg.ToLowerInvariant())
                {
                    case "--":
                        afterSeparator = true;
                        break;
                    case "--gui":
                        options.Gui = true;
                        break;
                    case "--silent":
                    case "--tray":
                        options.Silent = true;
                        options.Gui = true; // 静默启动本身就是 GUI 模式，只是不显示窗口
                        break;
                    case "--autostart":
                        break; // 启动项传入的标记，行为由「静默启动」设置决定
                    case "--inspect":
                        options.Inspect = true;
                        break;
                    case "-h":
                    case "--help":
                    case "/?":
                        options.ShowHelp = true;
                        break;
                    case "--no-delay":
                        options.NoDelay = true;
                        break;
                    case "--registry-hooks":
                        options.ForceRegistryHooks = true;
                        break;
                    case "--exe":
                        if (i + 1 < args.Length) options.ExecutablePath = args[++i];
                        break;
                    case "--attach":
                        int pid;
                        if (i + 1 < args.Length && int.TryParse(args[++i], out pid)) options.AttachProcessId = pid;
                        break;
                    case "--set-autostart":
                        if (i + 1 < args.Length)
                        {
                            string value = args[++i].ToLowerInvariant();
                            options.SetAutoStart = value == "on" || value == "1" || value == "true";
                        }

                        break;
                    default:
                        if (arg.StartsWith("-"))
                        {
                            Console.Error.WriteLine("未知参数：" + arg);
                            options.ShowHelp = true;
                        }
                        else
                        {
                            options.ExecutablePath = arg;
                        }

                        break;
                }
            }

            options.PassthroughArguments = string.Join(" ", passthrough.ToArray());
            return options;
        }
    }
}
