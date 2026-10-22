using System;
using System.Collections.Generic;

namespace PclRegionBypass
{
    /// <summary>
    ///     极简 x64 机器码发射器。只覆盖本项目用到的少量指令，
    ///     目的是让跳板桩代码可读、可复核，而不是手写十六进制字节。
    /// </summary>
    internal sealed class Asm
    {
        private struct Fixup
        {
            public int Pos;
            public string Label;
        }

        private readonly List<byte> _bytes = new List<byte>();
        private readonly Dictionary<string, int> _labels = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<Fixup> _fixups = new List<Fixup>();

        public int Position
        {
            get { return _bytes.Count; }
        }

        public void Label(string name)
        {
            _labels[name] = _bytes.Count;
        }

        private void Add(byte b)
        {
            _bytes.Add(b);
        }

        private void AddInt32(int v)
        {
            _bytes.Add((byte)v);
            _bytes.Add((byte)(v >> 8));
            _bytes.Add((byte)(v >> 16));
            _bytes.Add((byte)(v >> 24));
        }

        private void AddInt64(long v)
        {
            AddInt32((int)v);
            AddInt32((int)(v >> 32));
        }

        private void Rex(bool w, bool r, bool b)
        {
            byte rex = (byte)(0x40 | (w ? 0x08 : 0) | (r ? 0x04 : 0) | (b ? 0x01 : 0));
            if (rex != 0x40) Add(rex);
        }

        private void ModRmReg(int reg, int rm)
        {
            Add((byte)(0xC0 | ((reg & 7) << 3) | (rm & 7)));
        }

        private void ModRmDisp8(int reg, int baseReg, int disp8)
        {
            Add((byte)(0x40 | ((reg & 7) << 3) | (baseReg & 7)));
            Add((byte)disp8);
        }

        // ---- 指令 ----

        public void MovEax(int value)
        {
            Add(0xB8);
            AddInt32(value);
        }

        public void MovRax(long value)
        {
            Rex(true, false, false);
            Add(0xB8);
            AddInt64(value);
        }

        public void MovR10(long value)
        {
            // mov r10, imm64：寄存器号编码在 opcode 的低 3 位，高位由 REX.B 提供（不是 REX.R）
            Rex(true, false, true);
            Add(0xBA);
            AddInt64(value);
        }

        public void JmpRax()
        {
            Add(0xFF);
            Add(0xE0);
        }

        public void Ret()
        {
            Add(0xC3);
        }

        public void TestR64(int reg)
        {
            // test r/m64, r64 的两个操作数必须是同一个寄存器，因此 REX.R 与 REX.B 都要按 reg 设置
            Rex(true, reg >= 8, reg >= 8);
            Add(0x85);
            ModRmReg(reg, reg);
        }

        public void CmpEdxImm8(int value)
        {
            Add(0x83);
            Add(0xFA);
            Add((byte)value);
        }

        /// <summary>cmp qword ptr [baseReg + disp8], r10</summary>
        public void CmpQwordMemR10(int baseReg, int disp8)
        {
            Rex(true, true, baseReg >= 8);
            Add(0x39);
            ModRmDisp8(10, baseReg, disp8);
        }

        /// <summary>cmp word ptr [baseReg + disp8], 0</summary>
        public void CmpWordMemZero(int baseReg, int disp8)
        {
            Add(0x66);
            Rex(false, false, baseReg >= 8);
            Add(0x83);
            ModRmDisp8(7, baseReg, disp8);
            Add(0x00);
        }

        /// <summary>mov word ptr [baseReg + disp8], imm16</summary>
        public void MovWordMemImm16(int baseReg, int disp8, int value)
        {
            Add(0x66);
            Rex(false, false, baseReg >= 8);
            Add(0xC7);
            ModRmDisp8(0, baseReg, disp8);
            Add((byte)value);
            Add((byte)(value >> 8));
        }

        /// <summary>mov dword ptr [baseReg + disp8], imm32</summary>
        public void MovDwordMemImm32(int baseReg, int disp8, int value)
        {
            Rex(false, false, baseReg >= 8);
            Add(0xC7);
            ModRmDisp8(0, baseReg, disp8);
            AddInt32(value);
        }

        // ---- 标签跳转（统一使用 rel32，避免短跳范围问题）----

        private void JccNear(byte opcode2, string label)
        {
            Add(0x0F);
            Add(opcode2);
            _fixups.Add(new Fixup { Pos = _bytes.Count, Label = label });
            AddInt32(0);
        }

        public void Jz(string label)
        {
            JccNear(0x84, label);
        }

        public void Jne(string label)
        {
            JccNear(0x85, label);
        }

        public void Jb(string label)
        {
            JccNear(0x82, label);
        }

        public byte[] Build()
        {
            foreach (var fixup in _fixups)
            {
                int target;
                if (!_labels.TryGetValue(fixup.Label, out target))
                    throw new InvalidOperationException("未定义的汇编标签：" + fixup.Label);
                int rel = target - (fixup.Pos + 4);
                var raw = BitConverter.GetBytes(rel);
                for (int i = 0; i < 4; i++) _bytes[fixup.Pos + i] = raw[i];
            }

            return _bytes.ToArray();
        }
    }
}
