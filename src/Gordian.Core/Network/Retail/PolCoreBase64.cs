// src/Gordian.Core/Network/Retail/PolCoreBase64.cs
using System;
using System.Security.Cryptography;

namespace Gordian.Core.Network.Retail
{
    /// <summary>
    /// The 6-bit encoding of the Core <c>NICK</c> line's client metadata: each 3 bytes, read big-endian as 24 bits, become
    /// 4 characters (bits 18-23, 12-17, 6-11, 0-5) from a custom 64-character alphabet. It is not RFC 4648 Base64 and has
    /// no padding.
    /// <para>
    /// Protocol referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v1.4.0, section 4.2.2
    /// step 4.
    /// </para>
    /// </summary>
    public static class PolCoreBase64
    {
        /// <summary>The alphabet; index 0 is <c>T</c>.</summary>
        public const string Alphabet = "TSG8IncW3HFKokOg79qzeCmZs2yBYEQVAUxR5rbwi4P@jMDLtpvad0f_J1hlN6uX";

        /// <summary>Encodes <paramref name="data"/>, whose length must be a multiple of 3.</summary>
        public static string Encode(ReadOnlySpan<byte> data)
        {
            if (data.Length % 3 != 0) throw new ArgumentException("The data length must be a multiple of 3.", nameof(data));
            Span<char> text = data.Length <= 192 ? stackalloc char[data.Length / 3 * 4] : new char[data.Length / 3 * 4];
            for (int i = 0, o = 0; i < data.Length; i += 3, o += 4)
            {
                int group = (data[i] << 16) | (data[i + 1] << 8) | data[i + 2];
                text[o] = Alphabet[(group >> 18) & 63];
                text[o + 1] = Alphabet[(group >> 12) & 63];
                text[o + 2] = Alphabet[(group >> 6) & 63];
                text[o + 3] = Alphabet[group & 63];
            }
            return new string(text);
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
