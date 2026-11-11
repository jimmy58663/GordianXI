// src/Gordian.Core/Network/Retail/PolBase32.cs
using System;
using System.Buffers.Binary;
using System.Net;

namespace Gordian.Core.Network.Retail
{
    /// <summary>
    /// PlayOnline's 5-bit text encoding: a permuted 32-character alphabet over an MSB-first bit stream (RFC 4648
    /// packing, no padding). The Core IRC gatekeeper redirect, the session host's greeting and the <c>001</c> payload use
    /// it.
    /// <para>
    /// Protocol referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v2.1.18, sections
    /// 4.2.1 and 4.2.2 (test vector 12).
    /// </para>
    /// </summary>
    public static class PolBase32
    {
        /// <summary>The alphabet; index 0 is <c>N</c>.</summary>
        public const string Alphabet = "N43OVHBJ1Y2C0WSXED5QFILRZMUTAPGK";

        /// <summary>The 5-bit value of <paramref name="c"/>, or -1 when it is not in the alphabet.</summary>
        public static int ValueOf(char c) => Alphabet.IndexOf(c);

        /// <summary>Encodes <paramref name="data"/> as an MSB-first bit stream, padding the last character with zero bits.</summary>
        public static string Encode(ReadOnlySpan<byte> data)
        {
            int length = (data.Length * 8 + 4) / 5;
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
    }

    /// <summary>
    /// The 25-byte payload of a Core IRC <c>300 *</c> token: the gatekeeper's redirect and the session host's greeting
    /// share it. On the wire it is 40 <see cref="PolBase32"/> characters and a 4-character checksum suffix the client
    /// ignores.
    /// <para>
    /// Layout referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v2.1.18, section 4.2.1
    /// (test vector 12) and section 4.2.2 step 1.
    /// </para>
    /// </summary>
    /// <param name="ServerTimestamp">+0x00 (BE): the server's Unix time. The redirect's anchors the server clock; the greeting's goes into the AuthCode.</param>
    /// <param name="ClientAddress">+0x04 (BE): the client's address as the server sees it.</param>
    /// <param name="RedirectAddress">+0x08 (BE): the Core IRC session host (meaningful in the redirect).</param>
    /// <param name="RedirectPort">+0x0C (BE): the session host's TCP port (meaningful in the redirect).</param>
    /// <param name="ClientPort">+0x14 (BE): the client's port on this connection as the server sees it.</param>
    public readonly record struct PolCoreToken(uint ServerTimestamp, IPAddress ClientAddress, IPAddress RedirectAddress, ushort RedirectPort, ushort ClientPort)
    {
        public const int BodyLength = 40;
        public const int SuffixLength = 4;
        public const int PayloadLength = 25;

        /// <summary>Decodes a token's 40-character body; the suffix after it is not read.</summary>
        public static PolCoreToken Parse(string token)
        {
            ArgumentNullException.ThrowIfNull(token);
            if (token.Length < BodyLength) throw new FormatException("The Core IRC token is shorter than 40 characters.");
            Span<byte> data = stackalloc byte[PayloadLength];
            if (!PolBase32.TryDecode(token.AsSpan(0, BodyLength), data)) throw new FormatException("The Core IRC token is not PlayOnline Base32.");
            return new PolCoreToken(
                BinaryPrimitives.ReadUInt32BigEndian(data),
                new IPAddress(data.Slice(4, 4)),
                new IPAddress(data.Slice(8, 4)),
                BinaryPrimitives.ReadUInt16BigEndian(data[0x0C..]),
                BinaryPrimitives.ReadUInt16BigEndian(data[0x14..]));
        }
    }

    /// <summary>
    /// The 9-character form of a POL ID sent in <c>NICK</c>: <c>'U'</c> and the ID run through a reversible bit
    /// diffusion, both sides written in a custom Base36 alphabet.
    /// <para>
    /// Algorithm referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v2.1.18, section 4.2.2
    /// step 5 and test vector 1.
    /// </para>
    /// </summary>
    public static class PolIdEncoding
    {
        /// <summary>The Base36 alphabet; index 0 is <c>E</c>.</summary>
        public const string Alphabet = "EFKAOYMJVNGTDSWBQLPCIRHZXU6328401795";

        private const ulong ValueMask = 0x1FFFFFFFFFFUL;
        private const ulong MarkerBits = 3UL << 46;

        /// <summary>Whether <paramref name="polId"/> has the issued form: 4 letters, then 4 digits.</summary>
        public static bool IsValid(string? polId)
        {
            if (polId is not { Length: 8 }) return false;
            for (int i = 0; i < 4; i++) if (!char.IsAsciiLetter(polId[i])) return false;
            for (int i = 4; i < 8; i++) if (!char.IsAsciiDigit(polId[i])) return false;
            return true;
        }

        /// <summary>Encodes a POL ID (any letter case) into the 9-character <c>NICK</c> field.</summary>
        public static string Encode(string polId)
        {
            ArgumentNullException.ThrowIfNull(polId);
            string id = polId.Trim().ToUpperInvariant();
            if (!IsValid(id)) throw new ArgumentException("A POL ID is 4 letters followed by 4 digits.", nameof(polId));
            ulong value = Diffuse((ReadBase36(id) & ValueMask) | MarkerBits, reverse: false) & ~MarkerBits;
            return "U" + WriteBase36(value);
        }

        /// <summary>Recovers the POL ID from a 9-character <c>NICK</c> field.</summary>
        public static string Decode(string encoded)
        {
            ArgumentNullException.ThrowIfNull(encoded);
            if (encoded.Length != 9 || encoded[0] != 'U') throw new FormatException("An encoded POL ID is 'U' and 8 characters.");
            ulong value = Diffuse(ReadBase36(encoded[1..]) | MarkerBits, reverse: true) & ~MarkerBits;
            return WriteBase36(value);
        }

        /// <summary>The five rounds val ^= (val &gt;&gt; 8) &amp; (0xFF &lt;&lt; 8a), a = 4..0; reversed they undo it.</summary>
        private static ulong Diffuse(ulong value, bool reverse)
        {
            for (int i = 0; i < 5; i++)
            {
                int a = reverse ? i : 4 - i;
                value ^= (value >> 8) & (0xFFUL << (8 * a));
            }
            return value;
        }

        private static ulong ReadBase36(string text)
        {
            ulong value = 0;
            foreach (char c in text)
            {
                int digit = Alphabet.IndexOf(c);
                if (digit < 0) throw new FormatException($"'{c}' is not a POL ID character.");
                value = value * 36 + (ulong)digit;
            }
            return value;
        }

        private static string WriteBase36(ulong value)
        {
            Span<char> chars = stackalloc char[8];
            for (int i = 7; i >= 0; i--)
            {
                chars[i] = Alphabet[(int)(value % 36)];
                value /= 36;
            }
            return new string(chars);
        }
    }
}
