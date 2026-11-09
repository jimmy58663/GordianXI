// src/Gordian.Core/Network/Retail/PolCoreBase64.cs
using System;
using System.Security.Cryptography;

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
    /// The 24-byte client metadata the <c>NICK</c> line carries (32 characters once encoded): three zero bytes, the
    /// protocol version 2, the subsystem flags 1, the connection state 1 (always 1 in retail captures), then 18 bytes
    /// of session and execution nonces. The server checks only the shape, so the nonces are random. <c>02 01 01</c>
    /// encodes to <c>TAIS</c>, the observed <c>TTTTTAIS...</c> prefix.
    /// <para>
    /// Layout referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v1.5.0 (layout and
    /// alphabet, section 4.2.2 step 4) and v1.3.0 (the server does not check the nonces).
    /// </para>
    /// </summary>
    public static class PolClientMeta
    {
        public const int Length = 24;
        public const int EncodedLength = Length / 3 * 4;
        public const byte ProtocolVersion = 0x02;
        public const byte Flags = 0x01;
        public const byte Byte5 = 0x01;

        /// <summary>Writes the fixed header and the given 18 nonce bytes.</summary>
        public static void Write(Span<byte> meta, ReadOnlySpan<byte> nonces)
        {
            if (nonces.Length != Length - 6) throw new ArgumentException("The nonces are 18 bytes.", nameof(nonces));
            meta = meta[..Length];
            meta[..6].Clear();
            meta[3] = ProtocolVersion;
            meta[4] = Flags;
            meta[5] = Byte5;
            nonces.CopyTo(meta[6..]);
        }

        /// <summary>A fresh encoded metadata field with random nonces.</summary>
        public static string Create()
        {
            Span<byte> nonces = stackalloc byte[Length - 6];
            RandomNumberGenerator.Fill(nonces);
            Span<byte> meta = stackalloc byte[Length];
            Write(meta, nonces);
            return PolCoreBase64.Encode(meta);
        }
    }
}
