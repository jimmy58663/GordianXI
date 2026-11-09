// src/Gordian.Core/Network/Retail/PolDiffieHellman.cs
using System;
using System.Numerics;
using System.Security.Cryptography;

namespace Gordian.Core.Network.Retail
{
    /// <summary>
    /// The 256-bit Diffie-Hellman exchange of the PlayOnline Core IRC session: the client's key pair, the shared secret
    /// and the 8-byte stream cipher key taken from it.
    /// <para>
    /// Protocol referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v1.2.0, sections 4.2.2
    /// and 5.1 (domain parameters p and g = 67, key extraction). The parameters were reviewed and approved for commit by
    /// the maintainer (2026-11-08).
    /// </para>
    /// </summary>
    public sealed class PolDiffieHellman
    {
        /// <summary>The prime modulus p, 32 bytes big-endian.</summary>
        internal static ReadOnlySpan<byte> PrimeBytes =>
        [
            0xD5, 0xB4, 0xAA, 0xF9, 0xE7, 0x58, 0xF0, 0xC2, 0x79, 0xA5, 0xF3, 0xA0, 0x21, 0xE5, 0x05, 0x26,
            0x2F, 0x9F, 0x2F, 0xC0, 0x2B, 0xEC, 0xE0, 0x42, 0x5D, 0xB1, 0xD0, 0x04, 0xB2, 0xD3, 0x34, 0xF9,
        ];

        /// <summary>The generator g.</summary>
        internal const int Generator = 67;

        /// <summary>The length of public values and the shared secret.</summary>
        public const int ValueLength = 32;

        private static readonly BigInteger Prime = new(PrimeBytes, isUnsigned: true, isBigEndian: true);

        private readonly BigInteger _private;

        /// <summary>The client's public value A = g^a mod p, 32 bytes big-endian.</summary>
        public byte[] PublicValue { get; }

        /// <summary>A new key pair with a random private exponent.</summary>
        public PolDiffieHellman() : this(RandomNumberGenerator.GetBytes(ValueLength)) { }

        /// <summary>A key pair from a given private exponent (32 bytes big-endian), for tests.</summary>
        internal PolDiffieHellman(ReadOnlySpan<byte> privateExponent)
        {
            _private = new BigInteger(privateExponent, isUnsigned: true, isBigEndian: true) % (Prime - 1);
            if (_private < 2) _private += 2;
            PublicValue = ToFixed(BigInteger.ModPow(Generator, _private, Prime));
        }

        /// <summary>The shared secret S = B^a mod p for the server's public value (32 bytes big-endian).</summary>
        public byte[] ComputeSharedSecret(ReadOnlySpan<byte> serverPublicValue)
        {
            if (serverPublicValue.Length != ValueLength)
                throw new ArgumentException("The server's public value is 32 bytes.", nameof(serverPublicValue));
            var b = new BigInteger(serverPublicValue, isUnsigned: true, isBigEndian: true);
            if (b <= 1 || b >= Prime - 1) throw new CryptographicException("The server's Diffie-Hellman value is out of range.");
            return ToFixed(BigInteger.ModPow(b, _private, Prime));
        }

        /// <summary>
        /// The 8-byte stream cipher key: the secret's last 8 bytes in reverse order (S[31], S[30] ... S[24]).
        /// </summary>
        public static byte[] ExtractKey(ReadOnlySpan<byte> sharedSecret)
        {
            if (sharedSecret.Length != ValueLength) throw new ArgumentException("The shared secret is 32 bytes.", nameof(sharedSecret));
            var key = new byte[8];
            for (int i = 0; i < 8; i++) key[i] = sharedSecret[31 - i];
            return key;
        }

        internal static byte[] ToFixed(BigInteger value)
        {
            var result = new byte[ValueLength];
            byte[] raw = value.ToByteArray(isUnsigned: true, isBigEndian: true);
            raw.CopyTo(result, ValueLength - raw.Length);
            return result;
        }
    }
}
