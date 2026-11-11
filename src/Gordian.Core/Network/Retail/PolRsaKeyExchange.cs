// src/Gordian.Core/Network/Retail/PolRsaKeyExchange.cs
using System;
using System.Numerics;
using System.Security.Cryptography;

namespace Gordian.Core.Network.Retail
{
    /// <summary>
    /// The client side of the Core IRC key exchange: an ephemeral 256-bit RSA key pair (two 128-bit primes, public
    /// exponent 65535). The client sends its modulus N; the server answers with C = M^e mod N, where M is a PKCS#1 v1.5
    /// type-2 block around the 8-byte session key. N goes out little-endian and C comes back big-endian.
    /// <para>
    /// Protocol referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v2.1.19, section 4.2.2
    /// steps 2-4, section 5.1 item 1 and test vector 10.
    /// </para>
    /// </summary>
    public sealed class PolRsaKeyExchange
    {
        /// <summary>The fixed public exponent.</summary>
        public const int PublicExponent = 65535;

        /// <summary>The modulus, the ciphertext and the decrypted block are 32 bytes.</summary>
        public const int ValueLength = 32;

        /// <summary>The session key at the end of the decrypted block.</summary>
        public const int SessionKeyLength = 8;

        private const int PrimeBytes = 16;

        // 65535 = 3 x 5 x 17 x 257, so p - 1 must not be divisible by any of them for e to be invertible.
        private static readonly int[] ExponentFactors = [3, 5, 17, 257];

        private readonly BigInteger _modulus;
        private readonly BigInteger _privateExponent;

        /// <summary>A fresh key pair from the system random number generator.</summary>
        public PolRsaKeyExchange() : this(GeneratePrime(), GeneratePrime()) { }

        private PolRsaKeyExchange(BigInteger p, BigInteger q)
        {
            if (p == q) throw new ArgumentException("The two primes must differ.");
            _modulus = p * q;
            if (_modulus.GetByteCount(isUnsigned: true) != ValueLength) throw new ArgumentException("The modulus must be 256 bits.");
            var phi = (p - 1) * (q - 1);
            if (BigInteger.GreatestCommonDivisor(PublicExponent, phi) != 1) throw new ArgumentException("65535 is not invertible for these primes.");
            _privateExponent = ModInverse(PublicExponent, phi);

            Modulus = new byte[ValueLength];
            _modulus.TryWriteBytes(Modulus, out _, isUnsigned: true, isBigEndian: false);
        }

        /// <summary>A key pair from known primes (tests replay a capture whose modulus was factored).</summary>
        internal static PolRsaKeyExchange FromPrimes(BigInteger p, BigInteger q) => new(p, q);

        private static BigInteger GeneratePrime()
        {
            Span<byte> bytes = stackalloc byte[PrimeBytes];
            while (true)
            {
                RandomNumberGenerator.Fill(bytes);
                bytes[PrimeBytes - 1] |= 0xC0; // top two bits: the product of two such primes is a full 256 bits
                bytes[0] |= 1;
                var candidate = new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
                if (!FitsExponent(candidate)) continue;
                if (IsProbablePrime(candidate)) return candidate;
            }
        }

        private static bool FitsExponent(BigInteger p)
        {
            foreach (int f in ExponentFactors) if ((p - 1) % f == 0) return false;
            return true;
        }

        /// <summary>The 32-byte modulus N, little-endian, as the <c>USER</c> token carries it.</summary>
        public byte[] Modulus { get; }

        /// <summary>
        /// Decrypts the server's 32-byte big-endian ciphertext and returns the 8-byte session key K8: the block's last 8
        /// bytes, reversed. Throws <see cref="CryptographicException"/> when the block is not PKCS#1 v1.5 type 2
        /// (00 02, 21 non-zero bytes, 00, key).
        /// </summary>
        public byte[] DecryptSessionKey(ReadOnlySpan<byte> ciphertextBigEndian)
        {
            if (ciphertextBigEndian.Length != ValueLength) throw new ArgumentException("The ciphertext is 32 bytes.", nameof(ciphertextBigEndian));
            var c = new BigInteger(ciphertextBigEndian, isUnsigned: true, isBigEndian: true);
            if (c >= _modulus) throw new CryptographicException("The server's ciphertext is not below the modulus.");

            Span<byte> block = stackalloc byte[ValueLength];
            block.Clear();
            var m = BigInteger.ModPow(c, _privateExponent, _modulus);
            int length = m.GetByteCount(isUnsigned: true);
            m.TryWriteBytes(block[(ValueLength - length)..], out _, isUnsigned: true, isBigEndian: true);

            const int separator = ValueLength - SessionKeyLength - 1;
            bool valid = block[0] == 0x00 && block[1] == 0x02 && block[separator] == 0x00;
            for (int i = 2; i < separator; i++) valid &= block[i] != 0;
            if (!valid) throw new CryptographicException("The decrypted key block is not PKCS#1 v1.5 type 2.");

            var key = new byte[SessionKeyLength];
            for (int i = 0; i < SessionKeyLength; i++) key[i] = block[ValueLength - 1 - i];
            return key;
        }

        /// <summary>Encrypts a 32-byte big-endian block with this modulus (what the server does; for tests and fakes).</summary>
        public static byte[] Encrypt(ReadOnlySpan<byte> modulusLittleEndian, ReadOnlySpan<byte> blockBigEndian)
        {
            var n = new BigInteger(modulusLittleEndian, isUnsigned: true, isBigEndian: false);
            var m = new BigInteger(blockBigEndian, isUnsigned: true, isBigEndian: true);
            var c = BigInteger.ModPow(m, PublicExponent, n);
            var result = new byte[ValueLength];
            int length = c.GetByteCount(isUnsigned: true);
            c.TryWriteBytes(result.AsSpan(ValueLength - length), out _, isUnsigned: true, isBigEndian: true);
            return result;
        }

        /// <summary>A PKCS#1 v1.5 type-2 block around <paramref name="key8"/> (key bytes in K8 order), big-endian, random padding.</summary>
        public static byte[] WrapSessionKey(ReadOnlySpan<byte> key8)
        {
            if (key8.Length != SessionKeyLength) throw new ArgumentException("The session key is 8 bytes.", nameof(key8));
            var block = new byte[ValueLength];
            block[1] = 0x02;
            Span<byte> padding = block.AsSpan(2, ValueLength - SessionKeyLength - 3);
            RandomNumberGenerator.Fill(padding);
            for (int i = 0; i < padding.Length; i++) if (padding[i] == 0) padding[i] = 1;
            for (int i = 0; i < SessionKeyLength; i++) block[ValueLength - 1 - i] = key8[i];
            return block;
        }

        private static BigInteger ModInverse(BigInteger a, BigInteger m)
        {
            BigInteger oldR = a, r = m, oldS = 1, s = 0;
            while (r != 0)
            {
                var quotient = oldR / r;
                (oldR, r) = (r, oldR - quotient * r);
                (oldS, s) = (s, oldS - quotient * s);
            }
            return ((oldS % m) + m) % m;
        }

        /// <summary>Miller-Rabin with 40 random bases, after trial division by small primes.</summary>
        private static bool IsProbablePrime(BigInteger n)
        {
            if (n < 2) return false;
            foreach (int small in SmallPrimes)
            {
                if (n == small) return true;
                if (n % small == 0) return false;
            }
            var d = n - 1;
            int s = 0;
            while (d.IsEven) { d >>= 1; s++; }

            Span<byte> bytes = stackalloc byte[PrimeBytes];
            for (int round = 0; round < 40; round++)
            {
                BigInteger a;
                do
                {
                    RandomNumberGenerator.Fill(bytes);
                    a = new BigInteger(bytes, isUnsigned: true) % n;
                } while (a < 2 || a > n - 2);

                var x = BigInteger.ModPow(a, d, n);
                if (x == 1 || x == n - 1) continue;
                bool composite = true;
                for (int r = 1; r < s; r++)
                {
                    x = BigInteger.ModPow(x, 2, n);
                    if (x == n - 1) { composite = false; break; }
                }
                if (composite) return false;
            }
            return true;
        }

        private static readonly int[] SmallPrimes =
            [2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61, 67, 71, 73, 79, 83, 89, 97, 101, 103, 107, 109, 113, 127, 131, 137, 139, 149, 151, 157, 163, 167, 173, 179, 181, 191, 193, 197, 199, 211, 223, 227, 229, 233, 239, 241, 251, 257];
    }
}
