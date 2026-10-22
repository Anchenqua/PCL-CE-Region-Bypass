using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PclRegionBypass
{
    /// <summary>
    ///     跳板桩代码。每个桩都放在目标进程里的代码洞中，
    ///     函数入口被改写成 5 字节的 E9 相对跳转，指向这里。
    ///     桩里不调用任何外部函数，所以不需要保存/恢复寄存器。
    /// </summary>
    internal static class Stubs
    {
        /// <summary>GetUserDefaultGeoName(char* name, int cch) → 写入 "US"，返回 3（含结尾的 0）。</summary>
        public static byte[] GeoName()
        {
            var asm = new Asm();
            asm.MovEax(3);
            asm.TestR64(1); // rcx = name
            asm.Jz("ret");
            asm.CmpEdxImm8(3); // edx = cch
            asm.Jb("ret");
            asm.MovWordMemImm16(1, 0, 'U');
            asm.MovWordMemImm16(1, 2, 'S');
            asm.MovWordMemImm16(1, 4, 0);
            asm.Label("ret");
            asm.Ret();
            return asm.Build();
        }

        /// <summary>GetUserDefaultLocaleName(char* name, int cch) → 写入 "en-US"，返回 6。</summary>
        public static byte[] LocaleName()
        {
            var asm = new Asm();
            asm.MovEax(6);
            asm.TestR64(1); // rcx = name
            asm.Jz("ret");
            asm.CmpEdxImm8(6); // edx = cch
            asm.Jb("ret");
            WriteUtf16(asm, 1, 0, "en-US");
            asm.Label("ret");
            asm.Ret();
            return asm.Build();
        }

        /// <summary>
        ///     GetUserPreferredUILanguages(uint flags, uint* count, char* buffer, uint* chars)
        ///     → 报告 1 种语言 "en-US"，供缓冲区 6 个字符。
        /// </summary>
        public static byte[] PreferredUiLanguages()
        {
            var asm = new Asm();
            asm.MovEax(1);
            asm.TestR64(9); // r9 = chars
            asm.Jz("ret");
            asm.MovDwordMemImm32(9, 0, 6);
            asm.TestR64(2); // rdx = count
            asm.Jz("no_count");
            asm.MovDwordMemImm32(2, 0, 1);
            asm.Label("no_count");
            asm.TestR64(8); // r8 = buffer
            asm.Jz("ret");
            WriteUtf16(asm, 8, 0, "en-US");
            asm.Label("ret");
            asm.Ret();
            return asm.Build();
        }

        /// <summary>GetUserGeoID(int geoClass) → 244（美国，GEOCLASS_NATION 的 ISO 数字码）。</summary>
        public static byte[] GeoId()
        {
            var asm = new Asm();
            asm.MovEax(244);
            asm.Ret();
            return asm.Build();
        }

        /// <summary>
        ///     注册表读取钩子：值名等于 "DeviceRegion" 时报告「不存在」（ERROR_FILE_NOT_FOUND），
        ///     其余情况原样跳回真实实现，不影响启动器的其它注册表访问。
        /// </summary>
        /// <param name="valueNameRegister">
        ///     值名所在参数的寄存器编号（按 x64 通用寄存器编号：1=rcx, 2=rdx, 8=r8, 9=r9）。
        ///     RegQueryValueExW 是第 2 个参数 → rdx(2)；RegGetValueW 是第 3 个参数 → r8(8)。
        /// </param>
        /// <param name="realTarget">目标进程里真实实现的地址。</param>
        public static byte[] RegistryRead(int valueNameRegister, IntPtr realTarget)
        {
            const string valueName = "DeviceRegion";
            if (valueName.Length != 12)
                throw new ArgumentException("值名长度必须是 12，桩代码里按 3 个 64 位常量比较", "valueName");

            var asm = new Asm();
            asm.TestR64(valueNameRegister);
            asm.Jz("pass");

            // "DeviceRegion" = 12 个宽字符 = 3 个 64 位常量 + 结尾的 0
            for (int chunk = 0; chunk < 3; chunk++)
            {
                asm.MovR10(Pack4(valueName, chunk * 4));
                asm.CmpQwordMemR10(valueNameRegister, chunk * 8);
                asm.Jne("pass");
            }

            asm.CmpWordMemZero(valueNameRegister, 24);
            asm.Jne("pass");

            asm.MovEax(2); // ERROR_FILE_NOT_FOUND
            asm.Ret();

            asm.Label("pass");
            asm.MovRax(realTarget.ToInt64());
            asm.JmpRax();
            return asm.Build();
        }

        /// <summary>把 4 个宽字符按小端顺序打包成一个 64 位常量，避免手算十六进制出错。</summary>
        private static long Pack4(string text, int index)
        {
            long value = 0;
            for (int i = 0; i < 4; i++) value |= (long)text[index + i] << (16 * i);
            return value;
        }

        private static void WriteUtf16(Asm asm, int baseRegister, int offset, string text)
        {
            for (int i = 0; i < text.Length; i++)
                asm.MovWordMemImm16(baseRegister, offset + i * 2, text[i]);
            asm.MovWordMemImm16(baseRegister, offset + text.Length * 2, 0);
        }
    }
}
