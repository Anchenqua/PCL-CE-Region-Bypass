using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace PclRegionBypass
{
    /// <summary>
    ///     本进程内的 PE 导出表解析。只用来求「导出函数相对模块基址的 RVA」，
    ///     拿到 RVA 后再套上目标进程里同名模块的基址，就得到目标进程内的真实地址。
    /// </summary>
    internal static class PeExports
    {
        public static bool TryGetExportRva(IntPtr moduleBase, string functionName, out int functionRva)
        {
            functionRva = 0;
            if (moduleBase == IntPtr.Zero) return false;

            int lfanew = Marshal.ReadInt32(moduleBase, 0x3C);
            IntPtr nt = Add(moduleBase, lfanew);
            short magic = Marshal.ReadInt16(nt, 24);
            bool pe32Plus = magic == 0x20B;
            int dataDirectoryOffset = 24 + (pe32Plus ? 112 : 96);

            int exportRva = Marshal.ReadInt32(nt, dataDirectoryOffset);
            if (exportRva == 0) return false;

            IntPtr exportDirectory = Add(moduleBase, exportRva);
            int numberOfNames = Marshal.ReadInt32(exportDirectory, 0x18);
            int addressOfFunctions = Marshal.ReadInt32(exportDirectory, 0x1C);
            int addressOfNames = Marshal.ReadInt32(exportDirectory, 0x20);
            int addressOfNameOrdinals = Marshal.ReadInt32(exportDirectory, 0x24);

            for (int i = 0; i < numberOfNames; i++)
            {
                int nameRva = Marshal.ReadInt32(Add(moduleBase, addressOfNames), i * 4);
                string name = ReadAnsiZ(Add(moduleBase, nameRva));
                if (!string.Equals(name, functionName, StringComparison.Ordinal)) continue;

                int ordinal = Marshal.ReadInt16(Add(moduleBase, addressOfNameOrdinals), i * 2);
                functionRva = Marshal.ReadInt32(Add(moduleBase, addressOfFunctions), ordinal * 4);
                return functionRva != 0;
            }

            return false;
        }

        /// <summary>
        ///     判断某个导出是不是「转发到别的模块」的桩，并尽量解出它最终指向的真实地址（本进程视图）。
        /// </summary>
        public static bool TryResolveRealTarget(IntPtr moduleBase, int functionRva, out IntPtr realAddress,
            out bool isForwarder)
        {
            realAddress = IntPtr.Zero;
            isForwarder = false;
            IntPtr address = Add(moduleBase, functionRva);

            // 转发桩的标准形态：48 FF 25 disp32  (jmp qword ptr [rip+disp32])，共 7 字节，
            // 槽位里是加载器修正后的真实函数地址，直接读出来即可，不需要反汇编。
            if (Marshal.ReadByte(address, 0) == 0x48 && Marshal.ReadByte(address, 1) == 0xFF &&
                Marshal.ReadByte(address, 2) == 0x25)
            {
                isForwarder = true;
                int displacement = Marshal.ReadInt32(address, 3);
                IntPtr slot = Add(address, 7 + displacement);
                realAddress = new IntPtr(Marshal.ReadInt64(slot));
                return realAddress != IntPtr.Zero;
            }

            realAddress = address;
            return false;
        }

        public static IntPtr Add(IntPtr address, int offset)
        {
            return new IntPtr(address.ToInt64() + offset);
        }

        private static string ReadAnsiZ(IntPtr address)
        {
            var builder = new StringBuilder(64);
            for (int i = 0; i < 256; i++)
            {
                byte value = Marshal.ReadByte(address, i);
                if (value == 0) break;
                builder.Append((char)value);
            }

            return builder.ToString();
        }
    }

    /// <summary>
    ///     把「本进程里的地址」换算成「目标进程里的地址」：
    ///     同一份 DLL 在两个进程中的模块基址不同（ASLR），必须按 模块+RVA 重新拼。
    /// </summary>
    internal sealed class AddressTranslator
    {
        private readonly List<ModuleEntry> _self;
        private readonly List<ModuleEntry> _child;

        public AddressTranslator(List<ModuleEntry> self, List<ModuleEntry> child)
        {
            _self = self;
            _child = child;
        }

        public bool TryTranslate(IntPtr selfAddress, out IntPtr childAddress)
        {
            childAddress = IntPtr.Zero;
            long address = selfAddress.ToInt64();
            foreach (var module in _self)
            {
                long start = module.Base.ToInt64();
                if (address < start || address >= start + module.Size) continue;

                IntPtr remoteBase = Modules.Find(_child, module.Name);
                if (remoteBase == IntPtr.Zero) return false;

                childAddress = new IntPtr(remoteBase.ToInt64() + (address - start));
                return true;
            }

            return false;
        }
    }
}
