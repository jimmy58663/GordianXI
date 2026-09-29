// src/Gordian.Core/Network/Packets/LinkshellNameCodec.cs

using System;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// The 6-bit packed linkshell name used in S2C 0x0CC <c>encodedLsName</c> (and 0x0C9 <c>sComLinkName</c>): up to 20
    /// characters in an MSB-first bit stream, 6 bits each. 1-26 are a-z, 27-52 A-Z, 53-62 0-9; 63 (all ones) or 0 ends
    /// the name. The retail client unpacks it with <c>FromCompressStr</c>.
    /// Format referenced from XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x00CC and 0x00C9) and
    /// LandSandBoat (https://github.com/LandSandBoat/server, src/common/utils.cpp EncodeStringLinkshell, whose output
    /// linkshell.cpp LoadLinkshell stores as the name 0x0CC sends).
    /// </summary>
    public static class LinkshellNameCodec
    {
        public const int MaxLength = 20;
        private const int BitsPerChar = 6;
        private const int Terminator = 0x3F;

        public static string Decode(ReadOnlySpan<byte> encoded)
        {
            Span<char> chars = stackalloc char[MaxLength];
            int count = 0;
            int totalBits = encoded.Length * 8;
            for (int i = 0; i < MaxLength && (i + 1) * BitsPerChar <= totalBits; i++)
            {
                int value = ReadBits(encoded, i * BitsPerChar);
                if (value is 0 or Terminator) break;
                char c = ToChar(value);
                if (c != '\0') chars[count++] = c;
            }
            return new string(chars[..count]);
        }

        /// <summary>
        /// Packs <paramref name="name"/> into <paramref name="target"/> (cleared first), followed by a terminator when
        /// it fits. Characters outside a-z, A-Z and 0-9 are skipped. Returns false when the name is longer than
        /// <see cref="MaxLength"/> or does not fit.
        /// </summary>
        public static bool TryEncode(string name, Span<byte> target)
        {
            target.Clear();
            int bit = 0;
            int written = 0;
            foreach (char c in name)
            {
                int value = FromChar(c);
                if (value == 0) continue;
                if (written == MaxLength || bit + BitsPerChar > target.Length * 8) return false;
                WriteBits(target, bit, value);
                bit += BitsPerChar;
                written++;
            }
            if (bit + BitsPerChar <= target.Length * 8) WriteBits(target, bit, Terminator);
            return true;
        }

        private static char ToChar(int value) => value switch
        {
            >= 1 and <= 26 => (char)('a' + value - 1),
            >= 27 and <= 52 => (char)('A' + value - 27),
            >= 53 and <= 62 => (char)('0' + value - 53),
            _ => '\0'
        };

        private static int FromChar(char c) => c switch
        {
            >= 'a' and <= 'z' => c - 'a' + 1,
            >= 'A' and <= 'Z' => c - 'A' + 27,
            >= '0' and <= '9' => c - '0' + 53,
            _ => 0
        };

        private static int ReadBits(ReadOnlySpan<byte> data, int bitOffset)
        {
            int value = 0;
            for (int b = 0; b < BitsPerChar; b++)
            {
                int p = bitOffset + b;
                value = (value << 1) | ((data[p >> 3] >> (7 - (p & 7))) & 1);
            }
            return value;
        }

        private static void WriteBits(Span<byte> data, int bitOffset, int value)
        {
            for (int b = 0; b < BitsPerChar; b++)
            {
                int p = bitOffset + b;
                if (((value >> (BitsPerChar - 1 - b)) & 1) != 0) data[p >> 3] |= (byte)(0x80 >> (p & 7));
            }
        }
    }
}
