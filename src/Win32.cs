using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace PclRegionBypass
{
    internal static class Win32
    {
        public const uint PROCESS_CREATE_THREAD = 0x0002;
        public const uint PROCESS_QUERY_INFORMATION = 0x0400;
        public const uint PROCESS_VM_OPERATION = 0x0008;
        public const uint PROCESS_VM_WRITE = 0x0020;
        public const uint PROCESS_VM_READ = 0x0010;
        public const uint PROCESS_SUSPEND_RESUME = 0x0800;

        public const uint PROCESS_FULL_ACCESS = PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION |
                                               PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ |
                                               PROCESS_SUSPEND_RESUME;

        public const uint CREATE_SUSPENDED = 0x00000004;
        public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

        public const uint MEM_COMMIT = 0x1000;
        public const uint MEM_RESERVE = 0x2000;
        public const uint PAGE_EXECUTE_READWRITE = 0x40;
        public const uint PAGE_EXECUTE_READ = 0x20;

        public const uint LIST_MODULES_ALL = 0x03;
        public const uint TH32CS_SNAPMODULE = 0x00000008;
        public const uint TH32CS_SNAPTHREAD = 0x00000004;
        public const uint THREAD_SUSPEND_RESUME = 0x0002;
        public const uint THREAD_QUERY_INFORMATION = 0x0040;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MODULEINFO
        {
            public IntPtr lpBaseOfDll;
            public uint SizeOfImage;
            public IntPtr EntryPoint;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MODULEENTRY32
        {
            public int dwSize;
            public int th32ModuleID;
            public int th32ProcessID;
            public int GlblcntUsage;
            public int ProccntUsage;
            public IntPtr modBaseAddr;
            public int modBaseSize;
            public IntPtr hModule;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szModule;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExePath;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct THREADENTRY32
        {
            public int dwSize;
            public int cntUsage;
            public int th32ThreadID;
            public int th32OwnerProcessID;
            public int tpBasePri;
            public int tpDeltaPri;
            public int dwFlags;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateProcessW(string appName, StringBuilder commandLine, IntPtr processAttributes,
            IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment,
            string currentDirectory, ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint ResumeThread(IntPtr thread);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint SuspendThread(IntPtr thread);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenThread(uint access, bool inherit, int threadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr CreateToolhelp32Snapshot(uint flags, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool Thread32First(IntPtr snapshot, ref THREADENTRY32 entry);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool Thread32Next(IntPtr snapshot, ref THREADENTRY32 entry);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size, uint allocationType,
            uint protect);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool VirtualProtectEx(IntPtr process, IntPtr address, UIntPtr size, uint newProtect,
            out uint oldProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, UIntPtr size,
            out UIntPtr written);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, UIntPtr size,
            out UIntPtr read);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumProcessModulesEx(IntPtr process, [Out] IntPtr[] modules, int size,
            out int needed, uint filter);

        [DllImport("psapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int GetModuleBaseNameW(IntPtr process, IntPtr module, StringBuilder name, int size);

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetModuleInformation(IntPtr process, IntPtr module, out MODULEINFO info, int size);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr GetModuleHandleW(string name);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr LoadLibraryW(string name);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        public static extern int GetCurrentThreadId();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetStdHandle(int stdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

        public const uint INFINITE = 0xFFFFFFFF;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr GetProcAddress(IntPtr module, string name);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowThreadProcessId(IntPtr hWnd, out int processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint command);

        public const uint GW_OWNER = 4;

        public static string LastError()
        {
            int code = Marshal.GetLastWin32Error();
            return "0x" + code.ToString("X8") + " " + new Win32Exception(code).Message;
        }
    }

    internal sealed class ModuleEntry
    {
        public string Name;
        public IntPtr Base;
        public int Size;
    }

    internal static class Modules
    {
        public static List<ModuleEntry> List(IntPtr process)
        {
            var result = new List<ModuleEntry>();
            var handles = new IntPtr[1024];
            int needed;
            if (!Win32.EnumProcessModulesEx(process, handles, handles.Length * IntPtr.Size, out needed,
                    Win32.LIST_MODULES_ALL))
                return result;

            int count = needed / IntPtr.Size;
            if (count > handles.Length) count = handles.Length;

            var buffer = new StringBuilder(260);
            for (int i = 0; i < count; i++)
            {
                buffer.Length = 0;
                if (Win32.GetModuleBaseNameW(process, handles[i], buffer, buffer.Capacity) <= 0) continue;
                Win32.MODULEINFO info;
                if (!Win32.GetModuleInformation(process, handles[i], out info, Marshal.SizeOf(typeof(Win32.MODULEINFO))))
                    continue;
                result.Add(new ModuleEntry
                {
                    Name = buffer.ToString(),
                    Base = info.lpBaseOfDll,
                    Size = (int)info.SizeOfImage
                });
            }

            return result;
        }

        public static IntPtr Find(List<ModuleEntry> modules, string name)
        {
            foreach (var module in modules)
                if (string.Equals(module.Name, name, StringComparison.OrdinalIgnoreCase))
                    return module.Base;
            return IntPtr.Zero;
        }

        public static IntPtr FindSelf(string name)
        {
            return Win32.GetModuleHandleW(name);
        }
    }
}
