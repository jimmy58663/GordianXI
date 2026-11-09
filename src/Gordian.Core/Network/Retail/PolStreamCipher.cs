// src/Gordian.Core/Network/Retail/PolStreamCipher.cs
using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Gordian.Core.Network.Crypto;

namespace Gordian.Core.Network.Retail
{
    /// <summary>
    /// Standard 16-round Blowfish (the fractional-pi tables, Schneier's key schedule) on 32-bit halves. The PlayOnline
    /// stream cipher (<see cref="PolStreamCipher"/>) runs it to make its keystream.
    /// <para>
    /// Algorithm per the GordianXI retail login spec, docs/network/retail-login-spec.md v1.2.0, section 5.1 (standard
    /// Blowfish, as published by Bruce Schneier); the initial tables are the ones the map session cipher already holds
    /// (<see cref="LegacyBlowfishCryptoSuite"/>).
    /// </para>
    /// </summary>
    internal sealed class StandardBlowfish
    {
        private readonly uint[] _p = new uint[18];
        private readonly uint[] _s = new uint[1024];

        /// <summary>
        /// Builds the cipher from key words: P[i] is XORed with <paramref name="keyWords"/>[i mod n], then the usual
        /// 521-encryption key schedule runs from (0, 0). A byte key of 4n bytes read as big-endian words is the
        /// standard Blowfish key.
        /// </summary>
        public StandardBlowfish(ReadOnlySpan<uint> keyWords)
        {
            if (keyWords.IsEmpty) throw new ArgumentException("At least one key word is required.", nameof(keyWords));
            ReadOnlySpan<byte> tables = LegacyBlowfishCryptoSuite.FfxiSubkey;
            for (int i = 0; i < 18; i++) _p[i] = BinaryPrimitives.ReadUInt32LittleEndian(tables[(i * 4)..]);
            for (int i = 0; i < 1024; i++) _s[i] = BinaryPrimitives.ReadUInt32LittleEndian(tables[(72 + i * 4)..]);

            for (int i = 0; i < 18; i++) _p[i] ^= keyWords[i % keyWords.Length];

            uint l = 0, r = 0;
            for (int i = 0; i < 18; i += 2)
            {
                Encrypt(ref l, ref r);
                _p[i] = l;
                _p[i + 1] = r;
            }
            for (int i = 0; i < 1024; i += 2)
            {
                Encrypt(ref l, ref r);
                _s[i] = l;
                _s[i + 1] = r;
            }
        }

        /// <summary>The standard Blowfish key schedule for a byte key (big-endian words, cycled).</summary>
        public static StandardBlowfish FromKeyBytes(ReadOnlySpan<byte> key)
        {
            if (key.IsEmpty) throw new ArgumentException("The key is empty.", nameof(key));
            Span<uint> words = stackalloc uint[18];
            int k = 0;
            for (int i = 0; i < 18; i++)
            {
                uint w = 0;
                for (int j = 0; j < 4; j++)
                {
                    w = (w << 8) | key[k];
                    k = (k + 1) % key.Length;
                }
                words[i] = w;
            }
            return new StandardBlowfish(words);
        }

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
    /// PlayOnline's session stream cipher: Blowfish in 64-bit output feedback mode, keyed from the Core IRC
    /// Diffie-Hellman exchange and shared by the IRC session and every <c>:51220</c> connection of one login. A frame
    /// that starts the keystream over (<c>reset_iv = 1</c>) calls <see cref="Reset"/> first; its continuation
    /// (<c>reset_iv = 0</c>) carries on from where the last <see cref="Transform"/> stopped.
    /// <para>
    /// Protocol referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v1.2.0, section 5.1:
    /// the MD5 key expansion, the initial IV <c>52 7E 58 C7 D4 C3 9A 32</c> and the CRLF rule. Encrypting and decrypting
    /// are the same operation, CRLF rule included: a byte stays as it is when it, or the byte it would become, is CR or LF,
    /// so IRC lines keep their line ends on the wire.
    /// </para>
    /// </summary>
    public sealed class PolStreamCipher
    {
        /// <summary>The initial feedback register, as its two little-endian words (bytes 52 7E 58 C7 D4 C3 9A 32).</summary>
        internal const uint IvLow = 0xC7587E52, IvHigh = 0x329AC3D4;

        private readonly StandardBlowfish _blowfish;
        private uint _low, _high;
        private int _offset;

        /// <summary>Keys the cipher from the 8-byte key taken from the Diffie-Hellman secret (<see cref="PolDiffieHellman.ExtractKey"/>).</summary>
        public PolStreamCipher(ReadOnlySpan<byte> key8)
        {
            if (key8.Length != 8) throw new ArgumentException("The PlayOnline session key is 8 bytes.", nameof(key8));
            _blowfish = new StandardBlowfish(ExpandKey(key8));
            Reset();
        }

        /// <summary>
        /// The 10 key words: MD5(K8) and MD5(MD5(K8)) as little-endian words, then K8's two little-endian words.
        /// </summary>
        internal static uint[] ExpandKey(ReadOnlySpan<byte> key8)
        {
            Span<byte> digest1 = stackalloc byte[16];
            Span<byte> digest2 = stackalloc byte[16];
            MD5.HashData(key8, digest1);
            MD5.HashData(digest1, digest2);
            var words = new uint[10];
            for (int i = 0; i < 4; i++)
            {
                words[i] = BinaryPrimitives.ReadUInt32LittleEndian(digest1[(i * 4)..]);
                words[4 + i] = BinaryPrimitives.ReadUInt32LittleEndian(digest2[(i * 4)..]);
            }
            words[8] = BinaryPrimitives.ReadUInt32LittleEndian(key8);
            words[9] = BinaryPrimitives.ReadUInt32LittleEndian(key8[4..]);
            return words;
        }

        /// <summary>Starts the keystream over from the IV (<c>reset_iv = 1</c>).</summary>
        public void Reset()
        {
            _low = IvLow;
            _high = IvHigh;
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
