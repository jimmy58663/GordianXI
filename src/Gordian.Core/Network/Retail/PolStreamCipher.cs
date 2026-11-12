// src/Gordian.Core/Network/Retail/PolStreamCipher.cs
using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using Gordian.Core.Network.Crypto;

namespace Gordian.Core.Network.Retail
{
    /// <summary>
    /// 16-round Blowfish on 32-bit halves with the standard round function. <see cref="FromKeyBytes"/> is Schneier's
    /// standard cipher (fractional-pi tables and key schedule); the PlayOnline session cipher builds its tables its own
    /// way (<see cref="PolStreamCipher"/>) and uses only the rounds.
    /// <para>
    /// Algorithm per Bruce Schneier's published Blowfish; the initial tables are the ones the map session cipher already
    /// holds (<see cref="LegacyBlowfishCryptoSuite"/>).
    /// </para>
    /// </summary>
    internal sealed class StandardBlowfish
    {
        private readonly uint[] _p;
        private readonly uint[] _s;

        private StandardBlowfish(uint[] p, uint[] s)
        {
            _p = p;
            _s = s;
        }

        /// <summary>The standard Blowfish key schedule for a byte key (big-endian words, cycled).</summary>
        public static StandardBlowfish FromKeyBytes(ReadOnlySpan<byte> key)
        {
            if (key.IsEmpty) throw new ArgumentException("The key is empty.", nameof(key));
            var p = new uint[18];
            var s = new uint[1024];
            ReadOnlySpan<byte> tables = LegacyBlowfishCryptoSuite.FfxiSubkey;
            for (int i = 0; i < 18; i++) p[i] = BinaryPrimitives.ReadUInt32LittleEndian(tables[(i * 4)..]);
            for (int i = 0; i < 1024; i++) s[i] = BinaryPrimitives.ReadUInt32LittleEndian(tables[(72 + i * 4)..]);

            int k = 0;
            for (int i = 0; i < 18; i++)
            {
                uint w = 0;
                for (int j = 0; j < 4; j++)
                {
                    w = (w << 8) | key[k];
                    k = (k + 1) % key.Length;
                }
                p[i] ^= w;
            }

            var cipher = new StandardBlowfish(p, s);
            uint l = 0, r = 0;
            for (int i = 0; i < 18; i += 2)
            {
                cipher.Encrypt(ref l, ref r);
                p[i] = l;
                p[i + 1] = r;
            }
            for (int i = 0; i < 1024; i += 2)
            {
                cipher.Encrypt(ref l, ref r);
                s[i] = l;
                s[i + 1] = r;
            }
            return cipher;
        }

        /// <summary>
        /// The PlayOnline session key setup from the 8-byte key K8: a 4096-byte buffer grown from K8 by repeated digests
        /// of everything before (the first standard MD5, the rest MD5 from a zero chaining state), P loaded from it at a
        /// 1-byte stride and S at a 4-byte stride (both big-endian), P XORed with the buffer's first 18 words, then P and S
        /// refilled in pairs by encrypting the zero block with the tables as they stand, without chaining.
        /// </summary>
        public static StandardBlowfish FromPolSessionKey(ReadOnlySpan<byte> key8)
        {
            if (key8.Length != 8) throw new ArgumentException("The PlayOnline session key is 8 bytes.", nameof(key8));
            byte[] buffer = new byte[4096];
            key8.CopyTo(buffer);
            Span<byte> digest = stackalloc byte[16];
            for (int position = 8; position < buffer.Length;)
            {
                if (position == 8) MD5.HashData(buffer.AsSpan(0, 8), digest);
                else ZeroStateMd5.Hash(buffer.AsSpan(0, position), digest);
                int count = Math.Min(16, buffer.Length - position);
                digest[..count].CopyTo(buffer.AsSpan(position));
                position += count;
            }

            var p = new uint[18];
            var s = new uint[1024];
            for (int i = 0; i < 18; i++) p[i] = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(i));
            for (int i = 0; i < 1024; i++) s[i] = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(i * 4));
            for (int i = 0; i < 18; i++) p[i] ^= BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(i * 4));

            var cipher = new StandardBlowfish(p, s);
            for (int i = 0; i < 18; i += 2)
            {
                uint l = 0, r = 0;
                cipher.Encrypt(ref l, ref r);
                p[i] = l;
                p[i + 1] = r;
            }
            for (int i = 0; i < 1024; i += 2)
            {
                uint l = 0, r = 0;
                cipher.Encrypt(ref l, ref r);
                s[i] = l;
                s[i + 1] = r;
            }
            return cipher;
        }

        internal uint P(int index) => _p[index];
        internal uint S(int box, int index) => _s[box * 256 + index];

        private uint F(uint x) =>
            ((_s[x >> 24] + _s[256 + ((x >> 16) & 0xFF)]) ^ _s[512 + ((x >> 8) & 0xFF)]) + _s[768 + (x & 0xFF)];

        /// <summary>Encrypts one block held as its left and right 32-bit halves.</summary>
        public void Encrypt(ref uint left, ref uint right)
        {
            uint l = left, r = right;
            for (int i = 0; i < 16; i++)
            {
                l ^= _p[i];
                r ^= F(l);
                (l, r) = (r, l);
            }
            (l, r) = (r, l);
            r ^= _p[16];
            l ^= _p[17];
            left = l;
            right = r;
        }
    }

    /// <summary>
    /// MD5 (RFC 1321) with its chaining state starting at (0, 0, 0, 0) instead of the standard constants; padding, length
    /// and compression are unchanged. Only the PlayOnline session key setup uses it.
    /// </summary>
    internal static class ZeroStateMd5
    {
        private static readonly int[] Shifts =
        [
            7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22,
            5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20,
            4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23,
            6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21,
        ];

        private static readonly uint[] Sines = BuildSines();

        private static uint[] BuildSines()
        {
            var table = new uint[64];
            for (int i = 0; i < 64; i++) table[i] = (uint)(long)Math.Floor(Math.Abs(Math.Sin(i + 1)) * 4294967296.0);
            return table;
        }

        public static void Hash(ReadOnlySpan<byte> data, Span<byte> digest)
        {
            uint a0 = 0, b0 = 0, c0 = 0, d0 = 0;
            int padded = (data.Length + 9 + 63) / 64 * 64;
            byte[] message = new byte[padded];
            data.CopyTo(message);
            message[data.Length] = 0x80;
            BinaryPrimitives.WriteUInt64LittleEndian(message.AsSpan(padded - 8), (ulong)data.Length * 8);

            Span<uint> m = stackalloc uint[16];
            for (int chunk = 0; chunk < padded; chunk += 64)
            {
                for (int i = 0; i < 16; i++) m[i] = BinaryPrimitives.ReadUInt32LittleEndian(message.AsSpan(chunk + i * 4));
                uint a = a0, b = b0, c = c0, d = d0;
                for (int i = 0; i < 64; i++)
                {
                    uint f;
                    int g;
                    if (i < 16) { f = (b & c) | (~b & d); g = i; }
                    else if (i < 32) { f = (d & b) | (~d & c); g = (5 * i + 1) % 16; }
                    else if (i < 48) { f = b ^ c ^ d; g = (3 * i + 5) % 16; }
                    else { f = c ^ (b | ~d); g = 7 * i % 16; }
                    f = f + a + Sines[i] + m[g];
                    a = d;
                    d = c;
                    c = b;
                    b += BitOperations.RotateLeft(f, Shifts[i]);
                }
                a0 += a;
                b0 += b;
                c0 += c;
                d0 += d;
            }
            BinaryPrimitives.WriteUInt32LittleEndian(digest, a0);
            BinaryPrimitives.WriteUInt32LittleEndian(digest[4..], b0);
            BinaryPrimitives.WriteUInt32LittleEndian(digest[8..], c0);
            BinaryPrimitives.WriteUInt32LittleEndian(digest[12..], d0);
        }
    }

    /// <summary>
    /// PlayOnline's session stream cipher: Blowfish in 64-bit output feedback mode, keyed from the Core IRC RSA exchange
    /// and used by the IRC session and every <c>:51220</c> connection of one login. The IV is the first 8 bytes of the
    /// client's RSA modulus (little-endian words). A frame that starts the keystream over (<c>reset_iv = 1</c>) calls
    /// <see cref="Reset"/> first; its continuation (<c>reset_iv = 0</c>) carries on from where the last
    /// <see cref="Transform"/> stopped.
    /// <para>
    /// Protocol referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v2.1.19, section 5.1
    /// items 2 and 3 and test vector 6. Encrypting and decrypting are the same operation, CRLF rule included: a byte
    /// stays as it is when it, or the byte it would become, is CR or LF, so IRC lines keep their line ends on the wire.
    /// The rule applies to the binary <c>:51220</c> frames too.
    /// </para>
    /// </summary>
    public sealed class PolStreamCipher
    {
        private readonly StandardBlowfish _blowfish;
        private readonly uint _ivLow, _ivHigh;
        private uint _low, _high;
        private int _offset;

        /// <summary>Keys the cipher from the 8-byte session key and the 8-byte IV (the first 8 bytes of the client's modulus).</summary>
        public PolStreamCipher(ReadOnlySpan<byte> key8, ReadOnlySpan<byte> iv8)
            : this(StandardBlowfish.FromPolSessionKey(key8), iv8) { }

        private PolStreamCipher(StandardBlowfish blowfish, ReadOnlySpan<byte> iv8)
        {
            if (iv8.Length < 8) throw new ArgumentException("The IV is 8 bytes.", nameof(iv8));
            _blowfish = blowfish;
            _ivLow = BinaryPrimitives.ReadUInt32LittleEndian(iv8);
            _ivHigh = BinaryPrimitives.ReadUInt32LittleEndian(iv8[4..]);
            Reset();
        }

        internal StandardBlowfish Blowfish => _blowfish;

        /// <summary>
        /// A second keystream over the same key and IV. Every frame restarts the keystream, so two users of one session
        /// (the IRC keepalive and the <c>:51220</c> exchanges) each take their own state instead of sharing one.
        /// </summary>
        public PolStreamCipher Fork()
        {
            Span<byte> iv = stackalloc byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(iv, _ivLow);
            BinaryPrimitives.WriteUInt32LittleEndian(iv[4..], _ivHigh);
            return new PolStreamCipher(_blowfish, iv);
        }

        /// <summary>Starts the keystream over from the IV (<c>reset_iv = 1</c>).</summary>
        public void Reset()
        {
            _low = _ivLow;
            _high = _ivHigh;
            _offset = 0;
        }

        /// <summary>Encrypts or decrypts <paramref name="data"/> in place, continuing the keystream.</summary>
        public void Transform(Span<byte> data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                if ((_offset & 7) == 0) _blowfish.Encrypt(ref _low, ref _high);
                uint word = (_offset & 4) != 0 ? _high : _low;
                byte ks = (byte)(word >> (8 * (_offset & 3)));
                _offset++;

                byte input = data[i];
                byte output = (byte)(input ^ ks);
                if (input is 0x0A or 0x0D || output is 0x0A or 0x0D) output = input;
                data[i] = output;
            }
        }

        /// <summary>Starts the keystream over, then transforms <paramref name="data"/>: one whole <c>reset_iv = 1</c> frame.</summary>
        public void TransformFrame(Span<byte> data)
        {
            Reset();
            Transform(data);
        }
    }
}
