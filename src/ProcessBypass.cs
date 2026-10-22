using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace PclRegionBypass
{
    internal sealed class BypassException : Exception
    {
        public BypassException(string message) : base(message)
        {
        }
    }

    /// <summary>
    ///     针对单个目标进程的补丁会话：解析导出 → 在目标进程里申请代码洞 → 写入跳板桩 → 改写函数入口。
    ///     分三批挂载：
    ///       Core     —— 地区名 / 地区 ID（无副作用，启动前就能挂）
    ///       Locale   —— 系统区域 / UI 语言（必须等启动器界面初始化完，否则会连带改掉进程的 CultureInfo）
    ///       Registry —— DeviceRegion 值名屏蔽（必须等目标进程加载了 advapi32.dll）
    /// </summary>
    internal sealed class ProcessBypass
    {
        private const int PatchSize = 5; // E9 rel32
        private const int StubHeaderSize = 8;

        // 代码洞里每个桩前面放 8 字节签名：再次附加到同一进程时据此判断「已经打过补丁」，
        // 否则会把已经被改写的入口当成转发桩，算出指向自身的透传地址而陷入死循环。
        private static readonly byte[] StubSignature = Encoding.ASCII.GetBytes("PCLRBPS1");

        private sealed class Hook
        {
            public string Module;
            public string Function;
            public string Description;
            public bool Deferred;
            public bool AlreadyPatched;
            public IntPtr Target;
            public byte[] Stub;
            public IntPtr StubAddress;
            public bool Patched;
        }

        private readonly IntPtr _process;
        private readonly int _processId;
        private readonly Action<string> _log;
        private readonly object _freezeLock = new object();
        private readonly List<Hook> _kernelHooks = new List<Hook>();
        private IntPtr _cave;
        private int _caveUsed;
        private bool _prepared;
        private bool _coreInstalled;
        private bool _localeInstalled;
        private bool _registryInstalled;

        public ProcessBypass(IntPtr process, int processId, Action<string> log)
        {
            _process = process;
            _processId = processId;
            _log = log ?? delegate { };
        }

        public int ProcessId
        {
            get { return _processId; }
        }

        public bool CoreInstalled
        {
            get { return _coreInstalled; }
        }

        public bool LocaleInstalled
        {
            get { return _localeInstalled; }
        }

        public bool RegistryInstalled
        {
            get { return _registryInstalled; }
        }

        /// <summary>解析 kernel32 导出、分配代码洞、写好跳板桩（暂不改写入口）。</summary>
        public void Prepare()
        {
            if (_prepared) return;

            var remoteModules = Modules.List(_process);
            if (Modules.Find(remoteModules, "kernel32.dll") == IntPtr.Zero)
                throw new BypassException("目标进程尚未加载 kernel32.dll，无法定位钩子");

            var translator = new AddressTranslator(Modules.List(Win32.GetCurrentProcess()), remoteModules);

            _kernelHooks.Add(CreateHook("kernel32.dll", "GetUserDefaultGeoName",
                "地区名 (GetUserDefaultGeoName) → US", false, delegate(IntPtr ignored) { return Stubs.GeoName(); }, false,
                remoteModules, translator));
            _kernelHooks.Add(CreateHook("kernel32.dll", "GetUserGeoID",
                "地区 ID (GetUserGeoID) → 244/US", false, delegate(IntPtr ignored) { return Stubs.GeoId(); }, false,
                remoteModules, translator));
            _kernelHooks.Add(CreateHook("kernel32.dll", "GetUserDefaultLocaleName",
                "系统区域 (GetUserDefaultLocaleName) → en-US", true,
                delegate(IntPtr ignored) { return Stubs.LocaleName(); }, false, remoteModules,
                translator));
            _kernelHooks.Add(CreateHook("kernel32.dll", "GetUserPreferredUILanguages",
                "UI 语言 (GetUserPreferredUILanguages) → en-US", true,
                delegate(IntPtr ignored) { return Stubs.PreferredUiLanguages(); }, false,
                remoteModules, translator));

            var targets = new List<IntPtr>();
            foreach (var hook in _kernelHooks)
                if (!hook.AlreadyPatched) targets.Add(hook.Target);

            if (targets.Count > 0)
            {
                EnsureCave(targets);
                foreach (var hook in _kernelHooks) ReserveStub(hook);
            }

            _prepared = true;
            _log("已准备 " + _kernelHooks.Count + " 个跳板桩。");
        }

        private void EnsureCave(IList<IntPtr> targets)
        {
            if (_cave != IntPtr.Zero) return;

            _cave = AllocateCave(targets, 4096);
            if (_cave == IntPtr.Zero)
                throw new BypassException("无法在目标进程内申请代码洞：" + Win32.LastError());
            _log("代码洞位于 0x" + _cave.ToInt64().ToString("X"));
        }

        /// <summary>第一批：地区名 / 地区 ID。这些信号不影响启动期的语言判定，可以立刻挂。</summary>
        public void InstallCore()
        {
            if (!_prepared) throw new BypassException("Prepare() 尚未执行");
            if (_coreInstalled) return;

            var hooks = new List<Hook>();
            foreach (var hook in _kernelHooks)
                if (!hook.Deferred) hooks.Add(hook);

            WithThreadsFrozen(delegate
            {
                foreach (var hook in hooks) Patch(hook);
            });

            _coreInstalled = true;
            foreach (var hook in hooks) _log((hook.AlreadyPatched ? "  · " : "  ✓ ") + hook.Description);
        }

        /// <summary>
        ///     第二批：系统区域 / UI 语言。
        ///     必须等启动器界面已经初始化，否则 .NET 会把进程的 CultureInfo 也认成 en-US，
        ///     PCL 的界面语言设为「跟随系统」时会跳成英文。
        /// </summary>
        public void InstallLocale()
        {
            if (!_prepared) throw new BypassException("Prepare() 尚未执行");
            if (_localeInstalled) return;

            var hooks = new List<Hook>();
            foreach (var hook in _kernelHooks)
                if (hook.Deferred) hooks.Add(hook);

            WithThreadsFrozen(delegate
            {
                foreach (var hook in hooks) Patch(hook);
            });

            _localeInstalled = true;
            foreach (var hook in hooks) _log((hook.AlreadyPatched ? "  · " : "  ✓ ") + hook.Description);
        }

        /// <summary>
        ///     第三批：屏蔽 DeviceRegion 注册表读取。目标进程必须已经加载 advapi32.dll，
        ///     否则返回 false，调用方可以稍后重试。
        /// </summary>
        public bool TryInstallRegistry()
        {
            if (_registryInstalled) return true;
            if (!_prepared) throw new BypassException("Prepare() 尚未执行");

            var remoteModules = Modules.List(_process);
            if (Modules.Find(remoteModules, "advapi32.dll") == IntPtr.Zero) return false;

            var translator = new AddressTranslator(Modules.List(Win32.GetCurrentProcess()), remoteModules);

            var hooks = new List<Hook>();
            hooks.Add(CreateHook("advapi32.dll", "RegQueryValueExW",
                "设备区域值 DeviceRegion 读取 → 「不存在」 (RegQueryValueExW)", false,
                delegate(IntPtr real) { return Stubs.RegistryRead(2, real); }, true, remoteModules,
                translator));
            hooks.Add(CreateHook("advapi32.dll", "RegGetValueW",
                "设备区域值 DeviceRegion 读取 → 「不存在」 (RegGetValueW)", false,
                delegate(IntPtr real) { return Stubs.RegistryRead(8, real); }, true, remoteModules,
                translator));

            var pending = new List<IntPtr>();
            foreach (var hook in hooks)
                if (!hook.AlreadyPatched) pending.Add(hook.Target);

            if (pending.Count > 0)
            {
                EnsureCave(pending);
                foreach (var hook in hooks)
                {
                    if (!hook.AlreadyPatched && !FitsInRange(_cave.ToInt64(), hook.Target))
                        throw new BypassException("代码洞距离 " + hook.Function + " 太远，无法写入相对跳转");
                    ReserveStub(hook);
                }
            }

            WithThreadsFrozen(delegate
            {
                foreach (var hook in hooks) Patch(hook);
            });

            _registryInstalled = true;
            foreach (var hook in hooks) _log((hook.AlreadyPatched ? "  · " : "  ✓ ") + hook.Description);
            return true;
        }

        private void ReserveStub(Hook hook)
        {
            if (hook.AlreadyPatched) return;
            int needed = StubHeaderSize + hook.Stub.Length;
            if (_caveUsed + needed + 16 > 4096) throw new BypassException("代码洞空间不足");
            hook.StubAddress = PeExports.Add(_cave, _caveUsed);
            WriteBytes(hook.StubAddress, StubSignature);
            WriteBytes(PeExports.Add(hook.StubAddress, StubHeaderSize), hook.Stub);
            _caveUsed += Align(needed, 16);
        }

        /// <summary>入口是否已经是本工具的 5 字节 E9 跳转（指向带签名的桩）。</summary>
        private bool IsAlreadyPatched(IntPtr target)
        {
            byte[] head = ReadBytes(target, PatchSize);
            if (head[0] != 0xE9) return false;

            int relative = BitConverter.ToInt32(head, 1);
            long stub = target.ToInt64() + PatchSize + relative;
            long headerAddress = stub - StubHeaderSize;
            if (headerAddress < 0) return false;

            byte[] signature = ReadBytes(new IntPtr(headerAddress), StubHeaderSize);
            for (int i = 0; i < StubHeaderSize; i++)
                if (signature[i] != StubSignature[i]) return false;
            return true;
        }

        private Hook CreateHook(string module, string function, string description, bool deferred,
            Func<IntPtr, byte[]> stubFactory, bool requiresRealTarget, List<ModuleEntry> remoteModules,
            AddressTranslator translator)
        {
            IntPtr selfModule = Win32.GetModuleHandleW(module);
            if (selfModule == IntPtr.Zero) selfModule = Win32.LoadLibraryW(module);
            if (selfModule == IntPtr.Zero) throw new BypassException("无法加载 " + module);

            int rva;
            if (!PeExports.TryGetExportRva(selfModule, function, out rva))
                throw new BypassException(module + " 未导出 " + function);

            IntPtr remoteModule = Modules.Find(remoteModules, module);
            if (remoteModule == IntPtr.Zero) throw new BypassException("目标进程未加载 " + module);

            IntPtr target = PeExports.Add(remoteModule, rva);

            var hook = new Hook
            {
                Module = module,
                Function = function,
                Description = description,
                Deferred = deferred,
                Target = target
            };

            if (IsAlreadyPatched(target))
            {
                hook.AlreadyPatched = true;
                hook.Description = description + "（已打过补丁，跳过）";
                return hook;
            }

            IntPtr realTarget = target;
            bool forwarder;
            PeExports.TryResolveRealTarget(selfModule, rva, out realTarget, out forwarder);
            if (!forwarder && requiresRealTarget)
                throw new BypassException(function +
                                          " 不是转发桩，无法安全地取得它的真实实现（入口可能已被别的程序改写）。请重启启动器后再试。");
            if (forwarder && !translator.TryTranslate(realTarget, out realTarget))
                throw new BypassException("无法把 " + function + " 的真实实现地址映射到目标进程");

            hook.Stub = stubFactory(realTarget);
            return hook;
        }

        private IntPtr AllocateCave(IList<IntPtr> targets, int size)
        {
            long baseAddress = targets[0].ToInt64();
            for (int step = 1; step <= 0x2000; step++)
            {
                for (int direction = 1; direction >= -1; direction -= 2)
                {
                    long candidate = Align(baseAddress + direction * (long)step * 0x10000L, 0x10000);
                    if (candidate <= 0x10000) continue;
                    if (!FitsInRange(candidate, targets)) continue;

                    IntPtr allocated = Win32.VirtualAllocEx(_process, new IntPtr(candidate), (UIntPtr)size,
                        Win32.MEM_COMMIT | Win32.MEM_RESERVE, Win32.PAGE_EXECUTE_READWRITE);
                    if (allocated != IntPtr.Zero) return allocated;
                }
            }

            return IntPtr.Zero;
        }

        private static bool FitsInRange(long candidate, IList<IntPtr> targets)
        {
            foreach (var target in targets)
                if (!FitsInRange(candidate, target))
                    return false;
            return true;
        }

        private static bool FitsInRange(long candidate, IntPtr target)
        {
            return Math.Abs(candidate - target.ToInt64()) <= 0x7FF00000L;
        }

        private void Patch(Hook hook)
        {
            if (hook.Patched) return;
            if (hook.AlreadyPatched)
            {
                hook.Patched = true;
                return;
            }

            long relative = hook.StubAddress.ToInt64() + StubHeaderSize - (hook.Target.ToInt64() + PatchSize);
            if (relative < int.MinValue || relative > int.MaxValue)
                throw new BypassException("跳转距离超出 rel32 范围：" + hook.Function);

            var code = new byte[PatchSize];
            code[0] = 0xE9;
            var raw = BitConverter.GetBytes((int)relative);
            Array.Copy(raw, 0, code, 1, 4);

            uint oldProtect;
            if (!Win32.VirtualProtectEx(_process, hook.Target, (UIntPtr)PatchSize, Win32.PAGE_EXECUTE_READWRITE,
                    out oldProtect))
                throw new BypassException("修改 " + hook.Function + " 内存属性失败：" + Win32.LastError());

            WriteBytes(hook.Target, code);

            uint ignored;
            Win32.VirtualProtectEx(_process, hook.Target, (UIntPtr)PatchSize, oldProtect, out ignored);
            Win32.FlushInstructionCache(_process, hook.Target, (UIntPtr)PatchSize);

            byte[] verify = ReadBytes(hook.Target, PatchSize);
            for (int i = 0; i < PatchSize; i++)
                if (verify[i] != code[i])
                    throw new BypassException("校验失败：" + hook.Function + " 的入口字节未写入");

            hook.Patched = true;
        }

        private void WriteBytes(IntPtr address, byte[] data)
        {
            UIntPtr written;
            if (!Win32.WriteProcessMemory(_process, address, data, (UIntPtr)(ulong)data.Length, out written))
                throw new BypassException("写入目标进程失败：" + Win32.LastError());
        }

        private byte[] ReadBytes(IntPtr address, int length)
        {
            var buffer = new byte[length];
            UIntPtr read;
            if (!Win32.ReadProcessMemory(_process, address, buffer, (UIntPtr)(ulong)length, out read))
                throw new BypassException("读取目标进程失败：" + Win32.LastError());
            return buffer;
        }

        private void WithThreadsFrozen(Action action)
        {
            // 注册表批次与语言批次跑在不同线程里，冻结/改写必须串行，避免交叉改写
            lock (_freezeLock)
            {
                WithThreadsFrozenCore(action);
            }
        }

        private void WithThreadsFrozenCore(Action action)
        {
            var suspended = new List<IntPtr>();
            IntPtr snapshot = Win32.CreateToolhelp32Snapshot(Win32.TH32CS_SNAPTHREAD, 0);
            if (snapshot != IntPtr.Zero && snapshot != new IntPtr(-1))
            {
                try
                {
                    var entry = new Win32.THREADENTRY32();
                    entry.dwSize = Marshal.SizeOf(typeof(Win32.THREADENTRY32));
                    if (Win32.Thread32First(snapshot, ref entry))
                    {
                        do
                        {
                            if (entry.th32OwnerProcessID != _processId) continue;
                            IntPtr thread = Win32.OpenThread(Win32.THREAD_SUSPEND_RESUME, false,
                                entry.th32ThreadID);
                            if (thread == IntPtr.Zero) continue;
                            if (Win32.SuspendThread(thread) == 0xFFFFFFFF)
                            {
                                Win32.CloseHandle(thread);
                                continue;
                            }

                            suspended.Add(thread);
                        } while (Win32.Thread32Next(snapshot, ref entry));
                    }
                }
                finally
                {
                    Win32.CloseHandle(snapshot);
                }
            }

            try
            {
                action();
            }
            finally
            {
                foreach (var thread in suspended)
                {
                    Win32.ResumeThread(thread);
                    Win32.CloseHandle(thread);
                }
            }
        }

        private static int Align(int value, int alignment)
        {
            return (value + alignment - 1) / alignment * alignment;
        }

        private static long Align(long value, long alignment)
        {
            return value / alignment * alignment;
        }
    }
}
