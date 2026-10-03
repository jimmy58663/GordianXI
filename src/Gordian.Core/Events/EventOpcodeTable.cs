// src/Gordian.Core/Events/EventOpcodeTable.cs
using System;
using System.Collections.Generic;

namespace Gordian.Core.Events
{
    /// <summary>
    /// Byte lengths of the event VM opcodes, so the interpreter can step over the ones it does not run (camera,
    /// animation, scheduler and map opcodes) and keep a talk event on track to its message and choice opcodes.
    /// <para>
    /// Lengths referenced from the XiEvents opcode notes (https://github.com/atom0s/XiEvents, "OpCodes/"), whose
    /// pseudo code advances the exec pointer by a fixed amount per opcode, or per the sub-case byte that follows
    /// the opcode, and from xi-tools (https://github.com/vekien/xi-tools, "docs/events/typed_opcodes.md") where the
    /// XiEvents notes lack an opcode or a sub-case. Checked by walking every event entry of the 296 retail zones with
    /// event DATs (209,144 entries, 2026-09-28): 99.29% step exactly to the next event's offset with these lengths;
    /// the rest hold inline data (strings, sub-case 0x80 records) the notes do not describe.
    /// </para>
    /// </summary>
    public static class EventOpcodeTable
    {
        private static readonly Dictionary<byte, int> Fixed = new()
        {
            [0x00] = 1, [0x01] = 3, [0x02] = 8, [0x03] = 5, [0x04] = 3, [0x05] = 3, [0x06] = 3, [0x07] = 5, [0x08] = 5, [0x09] = 5,
            [0x0A] = 5, [0x0B] = 3, [0x0C] = 3, [0x0D] = 5, [0x0E] = 5, [0x0F] = 5, [0x10] = 5, [0x11] = 5, [0x12] = 3, [0x13] = 5,
            [0x14] = 5, [0x15] = 5, [0x16] = 7, [0x17] = 7, [0x18] = 7, [0x19] = 5, [0x1A] = 3, [0x1B] = 1, [0x1C] = 3, [0x1D] = 3,
            [0x1E] = 5, [0x20] = 2, [0x21] = 1, [0x22] = 2, [0x23] = 1, [0x24] = 7, [0x25] = 1, [0x26] = 1, [0x27] = 7, [0x28] = 7,
            [0x29] = 7, [0x2A] = 6, [0x2B] = 7, [0x2C] = 13, [0x2D] = 13, [0x2E] = 1, [0x2F] = 6, [0x30] = 1, [0x32] = 3, [0x33] = 2,
            [0x34] = 3, [0x35] = 3, [0x36] = 7, [0x37] = 9, [0x38] = 3, [0x39] = 3, [0x3A] = 7, [0x3B] = 11, [0x3C] = 7, [0x3D] = 7,
            [0x3E] = 7, [0x3F] = 7, [0x40] = 9, [0x41] = 9, [0x42] = 1, [0x43] = 2, [0x44] = 5, [0x45] = 17, [0x48] = 3, [0x49] = 7,
            [0x4A] = 9, [0x4B] = 7, [0x4C] = 1, [0x4D] = 1, [0x4E] = 6, [0x4F] = 3, [0x50] = 13, [0x51] = 13, [0x52] = 15, [0x53] = 13,
            [0x54] = 13, [0x55] = 15, [0x56] = 5, [0x57] = 3, [0x58] = 1, [0x5B] = 15, [0x5D] = 5, [0x5E] = 5, [0x61] = 2, [0x62] = 17,
            [0x63] = 3, [0x64] = 11, [0x65] = 11, [0x66] = 15, [0x67] = 5, [0x68] = 1, [0x69] = 4, [0x6A] = 7, [0x6B] = 9, [0x6C] = 9,
            [0x6D] = 7, [0x6E] = 7, [0x6F] = 1, [0x70] = 1, [0x73] = 11, [0x74] = 2, [0x76] = 5, [0x77] = 5, [0x78] = 1, [0x7B] = 5,
            [0x7C] = 6, [0x7D] = 3, [0x7F] = 1, [0x80] = 5, [0x81] = 6, [0x82] = 7, [0x83] = 3, [0x84] = 1, [0x85] = 1, [0x86] = 6,
            [0x87] = 2, [0x88] = 2, [0x89] = 3, [0x8A] = 1, [0x8B] = 25, [0x8D] = 5, [0x8E] = 1, [0x8F] = 1, [0x90] = 1, [0x91] = 3,
            [0x92] = 6, [0x93] = 3, [0x94] = 6, [0x95] = 3, [0x96] = 1, [0x97] = 5, [0x98] = 1, [0x99] = 5, [0x9A] = 1, [0x9B] = 1,
            [0x9C] = 3, [0x9E] = 2, [0x9F] = 17, [0xA0] = 15, [0xA1] = 15, [0xA2] = 15, [0xA3] = 15, [0xA4] = 2, [0xA5] = 2, [0xA8] = 6,
            [0xA9] = 3, [0xAA] = 17, [0xAD] = 12, [0xAF] = 8, [0xB0] = 12, [0xB1] = 4, [0xB5] = 4, [0xB8] = 27, [0xB9] = 8, [0xBA] = 13,
            [0xBB] = 17, [0xBC] = 15, [0xBD] = 15, [0xBE] = 3, [0xC0] = 3, [0xC1] = 5, [0xC3] = 7, [0xC4] = 12, [0xC5] = 17, [0xC6] = 15,
            [0xC7] = 15, [0xC8] = 7, [0xC9] = 1, [0xCD] = 17, [0xCE] = 15, [0xCF] = 15, [0xD0] = 17, [0xD1] = 15, [0xD2] = 15, [0xD3] = 6,
            [0xD5] = 17, [0xD6] = 15, [0xD7] = 15, [0xD9] = 2,
        };

        /// <summary>Lengths that depend on the sub-case byte after the opcode.</summary>
        private static readonly Dictionary<byte, Dictionary<byte, int>> BySubCase = new()
        {
            [0x1F] = new() { [0] = 8, [1] = 2 },
            [0x31] = new() { [0] = 10, [1] = 2 },
            [0x46] = new() { [0] = 2, [1] = 2, [2] = 4 },
            [0x47] = new() { [0] = 10, [1] = 2 },
            [0x5A] = new() { [0] = 8, [1] = 2 },
            [0x59] = new() { [0] = 4, [1] = 8, [2] = 4, [3] = 8, [4] = 8, [5] = 7, [6] = 6, [7] = 4, [8] = 8 },
            [0x5C] = Range(0, 7, 4, Range(0x80, 0x87, 6, new() { [0xA0] = 6, [0xA1] = 6 })),
            [0x5F] = new() { [0] = 2, [1] = 2, [2] = 6, [3] = 16, [4] = 16, [5] = 18, [6] = 18, [7] = 14 },
            [0x60] = Range(3, 0xFF, 2, new() { [0] = 4, [1] = 4, [2] = 6 }),
            [0x71] = new()
            {
                [0] = 2, [1] = 2, [2] = 2, [3] = 4, [0x10] = 4, [0x11] = 4, [0x13] = 4, [0x12] = 6, [0x20] = 16, [0x21] = 2, [0x30] = 4,
                [0x31] = 4, [0x32] = 6, [0x40] = 4, [0x41] = 8, [0x50] = 4, [0x51] = 2, [0x52] = 4, [0x53] = 2, [0x54] = 10, [0x55] = 4,
            },
            [0x72] = new() { [0] = 4, [1] = 6 },
            [0x75] = new() { [0] = 4, [1] = 2, [2] = 2 },
            [0x79] = new() { [0] = 10, [1] = 12, [2] = 10 },
            [0x7E] = new() { [0] = 6, [1] = 6, [2] = 6, [3] = 16, [4] = 6, [5] = 6, [6] = 18, [7] = 8, [8] = 6 },
            [0x7A] = new() { [0] = 6, [1] = 7, [2] = 6, [3] = 2, [4] = 8, [5] = 6 },
            [0x8C] = new() { [0] = 8, [1] = 2, [2] = 12, [3] = 10, [4] = 10, [5] = 14 },
            [0x9D] = new()
            {
                [0] = 8, [1] = 8, [2] = 6, [3] = 8, [4] = 8, [5] = 8, [6] = 8, [7] = 6, [8] = 23, [9] = 9, [0xA] = 10, [0xB] = 10, [0xC] = 8, [0xD] = 10,
                [0xE] = 10, [0xF] = 10, [0x10] = 10,
            },
            [0xA6] = new() { [0] = 2, [1] = 2, [2] = 4 },
            [0xA7] = new() { [0] = 2, [1] = 4 },
            [0xAB] = Range(0, 0x13, 2, new() { [0x11] = 4, [0x14] = 4, [0x15] = 4, [0x16] = 4, [0x17] = 4, [0x18] = 4, [0x19] = 2, [0x1A] = 2, [0x1B] = 6, [0x1C] = 6 }),
            [0xAC] = new() { [0] = 4, [1] = 4, [2] = 6, [3] = 6, [4] = 8 },
            [0xAE] = new() { [0] = 6, [1] = 8, [2] = 8, [3] = 8, [4] = 8, [5] = 10, [6] = 6, [7] = 10, [8] = 10 },
            [0xB2] = new() { [0] = 4, [1] = 2 },
            [0xB3] = new() { [0] = 4, [1] = 14, [2] = 2, [3] = 4, [4] = 4, [5] = 18, [6] = 4, [7] = 4, [8] = 2, [9] = 4 },
            [0xB4] = new()
            {
                [0] = 20, [1] = 6, [2] = 6, [3] = 2, [4] = 6, [5] = 3, [6] = 3, [7] = 4, [8] = 2, [9] = 4, [0xA] = 4, [0xB] = 2, [0xC] = 4,
                [0xD] = 2, [0xE] = 2, [0xF] = 6, [0x10] = 6, [0x11] = 6, [0x12] = 6, [0x13] = 20, [0x14] = 12, [0x15] = 2,
            },
            [0xB6] = Range(0, 0x0A, 4, new()
            {
                [0xB] = 20, [0xC] = 4, [0xD] = 14, [0xE] = 16, [0xF] = 4, [0x10] = 2, [0x11] = 4, [0x12] = 2, [0x13] = 2, [0x14] = 6, [0x15] = 6,
            }),
            [0xB7] = new() { [0] = 10, [1] = 8, [2] = 8, [3] = 8, [4] = 8 },
            [0xBF] = new() { [0] = 8, [0x20] = 10, [0x40] = 10, [0x60] = 8 },
            [0xC2] = Range(3, 0xFF, 2, new() { [0] = 2, [1] = 4, [2] = 6 }),
            [0xCC] = new() { [0] = 10, [1] = 10, [3] = 10, [2] = 14, [0x10] = 6, [0x11] = 4, [0x20] = 4 },
            [0xD4] = new() { [0] = 8, [1] = 8, [2] = 8, [3] = 6, [4] = 12, [5] = 12 },
            [0xD8] = new() { [0] = 6, [1] = 8, [2] = 8, [3] = 8, [4] = 12 },
        };

        private static Dictionary<byte, int> Range(int from, int to, int length, Dictionary<byte, int> into)
        {
            for (int i = from; i <= to; i++) into.TryAdd((byte)i, length);
            return into;
        }

        /// <summary>
        /// The length in bytes of the opcode at <paramref name="pc"/>, or 0 when it is unknown (or its sub-case is).
        /// </summary>
        public static int GetLength(ReadOnlySpan<byte> code, int pc)
        {
            if (pc < 0 || pc >= code.Length) return 0;
            byte op = code[pc];
            if (Fixed.TryGetValue(op, out int length)) return length;
            if (BySubCase.TryGetValue(op, out var cases) && pc + 1 < code.Length && cases.TryGetValue(code[pc + 1], out length)) return length;
            return 0;
        }
    }
}
