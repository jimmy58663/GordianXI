// src/Gordian.Core/Network/Retail/PolCoreBase64.cs
using System;

namespace Gordian.Core.Network.Retail
{
    /// <summary>
    /// The polcore 6-bit encoding of the Core <c>NICK</c> line's client metadata and the client's <c>USER</c> token: the
    /// bytes are read as one MSB-first bit string and each 6 bits become a character of a custom 64-character alphabet
    /// (so each 3 bytes become 4 characters). It is not RFC 4648 Base64 and has no padding characters; a length that is
    /// not a multiple of 3 ends with a partial character whose low bits the encoder fills with zeros.
    /// <para>
    /// Protocol referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v1.4.0, section 4.2.2
    /// step 4 (alphabet), and v1.5.0 section 5.2 and test vector 9 (the 47-character <c>USER</c> token, retail capture 3).
    /// </para>
    /// </summary>
    public static class PolCoreBase64
    {
        /// <summary>The alphabet; index 0 is <c>T</c>.</summary>
        public const string Alphabet = "TSG8IncW3HFKokOg79qzeCmZs2yBYEQVAUxR5rbwi4P@jMDLtpvad0f_J1hlN6uX";

        /// <summary>The number of characters that hold <paramref name="byteCount"/> bytes.</summary>
        public static int EncodedLength(int byteCount) => (byteCount * 8 + 5) / 6;

        /// <summary>Encodes <paramref name="data"/>, padding the last character's low bits with zeros.</summary>
        public static string Encode(ReadOnlySpan<byte> data)
        {
            int length = EncodedLength(data.Length);
            Span<char> text = length <= 256 ? stackalloc char[length] : new char[length];
            int acc = 0, bits = 0, o = 0;
            foreach (byte b in data)
            {
                acc = (acc << 8) | b;
                bits += 8;
                while (bits >= 6)
                {
                    bits -= 6;
                    text[o++] = Alphabet[(acc >> bits) & 63];
                }
                acc &= (1 << bits) - 1;
            }
            if (bits > 0) text[o] = Alphabet[(acc << (6 - bits)) & 63];
            return new string(text);
        }

        /// <summary>
        /// Decodes the first <c>destination.Length</c> bytes of <paramref name="text"/>; bits past them are ignored. False
        /// when the text is too short or has a character outside the alphabet.
        /// </summary>
        public static bool TryDecode(ReadOnlySpan<char> text, Span<byte> destination)
        {
            if (text.Length < EncodedLength(destination.Length)) return false;
            int acc = 0, bits = 0, o = 0;
            foreach (char c in text)
            {
                if (o == destination.Length) break;
                int value = Alphabet.IndexOf(c);
                if (value < 0) return false;
                acc = (acc << 6) | value;
                bits += 6;
                if (bits >= 8)
                {
                    bits -= 8;
                    destination[o++] = (byte)(acc >> bits);
                    acc &= (1 << bits) - 1;
                }
            }
            return o == destination.Length;
        }
    }

    /// <summary>
    /// The <c>NICK</c> line's client metadata: a 32-character body that every retail capture sends unchanged (24 bytes
    /// in <see cref="PolCoreBase64"/>, starting <c>00 00 00 02 01 01</c>), then a 4-character checksum of the line
    /// before it.
    /// <para>
    /// Referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v2.1.18, section 4.2.2 step 5
    /// (the constant body and the suffix rule).
    /// </para>
    /// </summary>
    public static class PolClientMeta
    {
        /// <summary>The constant 32-character body.</summary>
        public const string Body = "TTTTTAISTTTTTTTTTTTTTHBy5pjMnc0G"; // the capture value is redacted from history; this is the synthetic ABCD1234 body

        public const int SuffixLength = 4;

        /// <summary>
        /// The suffix over the line bytes before it (<c>"NICK "</c> through the body, 80 bytes): the sum of those bytes
        /// read as little-endian 32-bit words (any last 1-3 bytes as a zero-padded word), as four 6-bit slices from bit 31
        /// down (shifts 26, 20, 14, 8), each plus 0x3F.
        /// <b>Differs from spec v2.1.18:</b> section 4.2.2 step 5 says a sum of single bytes, but 80 ASCII bytes cannot
        /// reach its own example sum 0xAA1353EA; the word sum gives exactly that and the captured suffix.
        /// </summary>
        public static string Suffix(ReadOnlySpan<byte> lineBeforeSuffix)
        {
            uint sum = PolHashes.AdditiveChecksum(lineBeforeSuffix);
            Span<char> chars = stackalloc char[SuffixLength];
            for (int k = 0; k < SuffixLength; k++) chars[k] = (char)(((sum >> (26 - 6 * k)) & 0x3F) + 0x3F);
            return new string(chars);
        }
    }
}
