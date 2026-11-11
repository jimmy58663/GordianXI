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
    /// The <c>NICK</c> line's client metadata: a 32-character body (24 bytes in <see cref="PolCoreBase64"/>) computed
    /// from the POL ID, then a 4-character checksum of the line before it. The body is <c>00 00 00 02 01</c>, the region
    /// byte (1 for a US English client), 10 zero bytes, and the first 8 bytes of MD5(64 zero bytes + the POL ID token + 10
    /// zero bytes); the POL ID token is 8 bytes the POL ID's characters are mixed into (<see cref="PolIdToken"/>).
    /// <para>
    /// Referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v2.1.19, section 4.2.2 step 5
    /// and test vector 16.
    /// </para>
    /// </summary>
    public static class PolClientMeta
    {
        public const int BodyBytes = 24;
        public const int SuffixLength = 4;

        /// <summary>Byte 5 of the body: the client's region (1 = US / English, the only value captured).</summary>
        public const byte RegionUsEnglish = 0x01;

        /// <summary>The 32-character body for a POL ID.</summary>
        public static string Body(string polId, byte region = RegionUsEnglish)
        {
            Span<byte> body = stackalloc byte[BodyBytes];
            body.Clear();
            body[3] = 0x02;
            body[4] = 0x01;
            body[5] = region;
            Span<byte> buffer = stackalloc byte[64 + 8 + 10];
            buffer.Clear();
            PolIdToken(polId).CopyTo(buffer[64..]);
            Span<byte> digest = stackalloc byte[16];
            System.Security.Cryptography.MD5.HashData(buffer, digest);
            digest[..8].CopyTo(body[16..]);
            return PolCoreBase64.Encode(body);
        }

        /// <summary>
        /// The 8-byte POL ID token: the ID's 8 ASCII characters (upper case) XOR-chained with bit 0x80 set where bit 0x02
        /// is, then a byte sum and a product (64-bit), and a 32-bit LCG stepped (byte - 0x30) times per byte above 0x30;
        /// LE32((sum + product) low word XOR lcg) + LE32(its high word).
        /// </summary>
        public static byte[] PolIdToken(string polId)
        {
            ArgumentNullException.ThrowIfNull(polId);
            string id = polId.Trim().ToUpperInvariant();
            if (!PolIdEncoding.IsValid(id)) throw new ArgumentException("A POL ID is 4 letters followed by 4 digits.", nameof(polId));
            Span<byte> b = stackalloc byte[8];
            for (int i = 0; i < 8; i++) b[i] = (byte)id[i];
            for (int i = 1; i < 8; i++)
            {
                b[i] ^= b[i - 1];
                if ((b[i] & 0x02) != 0) b[i] |= 0x80;
            }

            ulong sum = 1, product = 0x00007048860DDF79UL;
            for (int i = 1; i < 8; i++)
            {
                sum += b[i];
                product = unchecked(product * ((ulong)b[i - 1] * b[i]));
            }

            uint lcg = 0;
            for (int i = 0; i < 8; i++)
                for (int step = 0x30; step < b[i]; step++) lcg = unchecked(lcg * 0x425F0CBD + 0x7F4F);

            ulong total = unchecked(sum + product);
            var token = new byte[8];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(token, (uint)total ^ lcg);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(token.AsSpan(4), (uint)(total >> 32));
            return token;
        }

        /// <summary>
        /// The suffix over the line bytes before it (<c>"NICK "</c> through the body, 80 bytes): the sum of those bytes
        /// read as little-endian 32-bit words (any last 1-3 bytes as a zero-padded word), as four 6-bit slices from bit 31
        /// down (shifts 26, 20, 14, 8), each plus 0x3F.
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
