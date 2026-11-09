// src/Gordian.Core/Network/Retail/PolBase32.cs
using System;

namespace Gordian.Core.Network.Retail
{
    /// <summary>
    /// PlayOnline's 5-bit text encoding: '0'..'5' are 0..5 and 'A'..'Z' are 6..31, read as an MSB-first bit stream.
    /// The Core IRC tokens (<c>300 *</c> greetings, the <c>USER</c> line, the <c>001</c> payload) and the encoded POL ID
    /// in <c>NICK</c> use it.
    /// <para>
    /// Protocol referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v1.2.0, sections
    /// 4.2.1, 4.2.2 and 5.8 (test vector 1). The spec's token lengths (41 characters for 24 bytes, 57 for 35) are longer
    /// than the bits need, so <see cref="Decode"/> takes the byte count it wants and ignores the characters left over.
    /// </para>
    /// </summary>
    public static class PolBase32
    {
        private const string Alphabet = "012345ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        /// <summary>The 5-bit value of <paramref name="c"/>, or -1 when it is not in the alphabet.</summary>
        public static int ValueOf(char c) => c switch
        {
            >= '0' and <= '5' => c - '0',
            >= 'A' and <= 'Z' => c - 'A' + 6,
            _ => -1,
        };

        /// <summary>
        /// Encodes <paramref name="data"/> as an MSB-first bit stream, padding the last character with zero bits, into at
        /// least <paramref name="minimumLength"/> characters (further characters are '0', all-zero bits).
        /// </summary>
        public static string Encode(ReadOnlySpan<byte> data, int minimumLength = 0)
        {
            int length = Math.Max((data.Length * 8 + 4) / 5, minimumLength);
            Span<char> chars = length <= 256 ? stackalloc char[length] : new char[length];
            for (int i = 0; i < length; i++)
            {
                int value = 0;
                for (int bit = 0; bit < 5; bit++)
                {
                    int index = i * 5 + bit;
                    int b = index / 8 < data.Length ? (data[index / 8] >> (7 - index % 8)) & 1 : 0;
                    value = (value << 1) | b;
                }
                chars[i] = Alphabet[value];
            }
            return new string(chars);
        }

        /// <summary>
        /// Decodes the first <paramref name="destination"/>.Length bytes of an MSB-first bit stream. Returns false when the
        /// text is too short or holds a character outside the alphabet in the bits read.
        /// </summary>
        public static bool TryDecode(ReadOnlySpan<char> text, Span<byte> destination)
        {
            int bitsNeeded = destination.Length * 8;
            if (text.Length * 5 < bitsNeeded) return false;
            destination.Clear();
            int charsNeeded = (bitsNeeded + 4) / 5;
            for (int i = 0; i < charsNeeded; i++)
            {
                int value = ValueOf(text[i]);
                if (value < 0) return false;
                for (int bit = 0; bit < 5; bit++)
                {
                    int index = i * 5 + bit;
                    if (index >= bitsNeeded) break;
                    if (((value >> (4 - bit)) & 1) != 0) destination[index / 8] |= (byte)(0x80 >> (index % 8));
                }
            }
            return true;
        }

        /// <summary>Decodes <paramref name="byteCount"/> bytes; throws <see cref="FormatException"/> when the text cannot hold them.</summary>
        public static byte[] Decode(ReadOnlySpan<char> text, int byteCount)
        {
            byte[] result = new byte[byteCount];
            if (!TryDecode(text, result)) throw new FormatException($"Not a PlayOnline Base32 string of {byteCount} bytes.");
            return result;
        }

        /// <summary>
        /// The 9-character form of a POL ID sent in <c>NICK</c>: the 8-character ID read as a Base36 number (0-9, A-Z, any
        /// case) and written MSB-first in 5-bit characters (spec v1.2.0 section 4.2.2, test vector 1: TEST0001 -> 232JR2NC1).
        /// </summary>
        public static string EncodePolId(string polId)
        {
            ArgumentNullException.ThrowIfNull(polId);
            string id = polId.Trim().ToUpperInvariant();
            if (id.Length != 8) throw new ArgumentException("A POL ID is 8 characters.", nameof(polId));
            ulong value = 0;
            foreach (char c in id)
            {
                int digit = c switch
                {
                    >= '0' and <= '9' => c - '0',
                    >= 'A' and <= 'Z' => c - 'A' + 10,
                    _ => throw new ArgumentException("A POL ID holds only letters and digits.", nameof(polId)),
                };
                value = value * 36 + (ulong)digit;
            }

            Span<char> chars = stackalloc char[9];
            for (int i = 0; i < 9; i++) chars[i] = Alphabet[(int)((value >> (5 * (8 - i))) & 31)];
            return new string(chars);
        }
    }
}
