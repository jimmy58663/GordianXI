// src/Gordian.Core/Network/Retail/PolAuthCode.cs
using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Gordian.Core.Network.Retail
{
    /// <summary>
    /// The 52-byte AuthCode token of the lobby's C2S 0x26 (+0x34): a 40-byte block (a digest of the lobby key, the Core
    /// greeting's server time and the Core session key, plus a context block holding the client's address) encrypted
    /// with a fixed-key 64-bit block cipher, chained by XOR and given four folded header bytes.
    /// <para>
    /// Algorithm referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v2.1.19, section 4.5.2
    /// (steps 1-6) and test vector 13.
    /// </para>
    /// </summary>
    public static class PolAuthCode
    {
        public const int TokenLength = 52;
        public const int ContextLength = 20;
        private const int BlockLength = 40;

        /// <summary>The cipher's fixed 8-byte key (spec appendix <c>CONST_AUTH_CODE_KEY</c>).</summary>
        private static ReadOnlySpan<byte> Key => [0xCB, 0x83, 0x24, 0xB7, 0xBA, 0x3E, 0x9E, 0x0A];

        private static readonly ulong[] Subkeys = BuildSubkeys();

        /// <summary>
        /// The token for a login: the lobby session key, the Core greeting's server time, the Core session key K8 and the
        /// <c>001</c> routing token. The context block is protocol version 1, the routing token and 12 zero bytes.
        /// </summary>
        public static byte[] Generate(ReadOnlySpan<byte> lobbySessionKey, uint greetingTimestamp, ReadOnlySpan<byte> sessionKey8, ReadOnlySpan<byte> routingToken)
        {
            if (routingToken.Length != 6) throw new ArgumentException("The routing token is 6 bytes.", nameof(routingToken));
            Span<byte> context = stackalloc byte[ContextLength];
            context.Clear();
            BinaryPrimitives.WriteUInt16LittleEndian(context, 1);
            routingToken.CopyTo(context[2..]);
            return GenerateWithContext(lobbySessionKey, greetingTimestamp, sessionKey8, context);
        }

        /// <summary>The length of D28, the MD5 preimage: the lobby key, the greeting time and K8.</summary>
        public const int PreimageLength = 28;

        /// <summary>
        /// D28 = lobby session key (16) + LE32(Core greeting server time) + K8 (8) (spec v2.1.22 section 4.5.2 step 1,
        /// section 9 item 8). Its MD5 is H16, the token's first step.
        /// </summary>
        public static void WritePreimage(Span<byte> preimage, ReadOnlySpan<byte> lobbySessionKey, uint greetingTimestamp, ReadOnlySpan<byte> sessionKey8)
        {
            lobbySessionKey[..16].CopyTo(preimage);
            BinaryPrimitives.WriteUInt32LittleEndian(preimage[16..], greetingTimestamp);
            sessionKey8[..8].CopyTo(preimage[20..]);
        }

        /// <summary>The token from an explicit 20-byte context block (tests replay a capture's own block).</summary>
        internal static byte[] GenerateWithContext(ReadOnlySpan<byte> lobbySessionKey, uint greetingTimestamp, ReadOnlySpan<byte> sessionKey8, ReadOnlySpan<byte> context, byte seed = 0)
        {
            if (lobbySessionKey.Length != 16) throw new ArgumentException("The lobby session key is 16 bytes.", nameof(lobbySessionKey));
            if (sessionKey8.Length != 8) throw new ArgumentException("The Core session key is 8 bytes.", nameof(sessionKey8));
            if (context.Length != ContextLength) throw new ArgumentException("The context block is 20 bytes.", nameof(context));

            // Step 1: H = MD5(lobby key[16] + LE32(greeting time) + K8[8]).
            Span<byte> preimage = stackalloc byte[PreimageLength];
            WritePreimage(preimage, lobbySessionKey, greetingTimestamp, sessionKey8);
            Span<byte> h = stackalloc byte[16];
            MD5.HashData(preimage, h);

            // Step 2: the 40-byte block.
            Span<byte> block = stackalloc byte[BlockLength];
            block.Clear();
            block[0] = (byte)(h[13] ^ h[3]);
            block[1] = seed;
            context.CopyTo(block[4..]);
            h.CopyTo(block[24..]);

            // Step 3: five blocks and the checksum trailer, each at its byte counter.
            var token = new byte[TokenLength];
            Span<byte> cipher = token.AsSpan(4, 48);
            uint checksum = 0;
            for (int b = 0; b < 5; b++)
            {
                checksum += (uint)block[8 * b] + block[8 * b + 4];
                EncryptBlock(block.Slice(8 * b, 8), cipher.Slice(8 * b, 8), (ulong)(8 * b));
            }
            Span<byte> trailer = stackalloc byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(trailer, BlockLength);
            BinaryPrimitives.WriteUInt32LittleEndian(trailer[4..], checksum);
            EncryptBlock(trailer, cipher.Slice(40, 8), BlockLength);

            // Step 4: running XOR from byte 5.
            for (int i = 5; i < TokenLength; i++) token[i] ^= token[i - 1];

            // Step 5: the folded header bytes.
            token[0] = (byte)(token[14] ^ seed);
            token[1] = (byte)(token[12] ^ token[6]);
            token[2] = (byte)(token[10] ^ token[8]);
            token[3] = (byte)(token[13] ^ token[9]);
            return token;
        }

        private static ulong[] BuildSubkeys()
        {
            uint k0 = BinaryPrimitives.ReadUInt32LittleEndian(Key);
            uint k1 = BinaryPrimitives.ReadUInt32LittleEndian(Key[4..]);
            k0 = (k0 << 8) | (k0 >> 24);
            k1 = (k1 >> 16) | (k1 << 16);
            Span<byte> ctx = stackalloc byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(ctx, k1);
            BinaryPrimitives.WriteUInt32LittleEndian(ctx[4..], k0);
            ctx[0] = (byte)(ctx[0] + 0x45);
            for (int i = 1; i < 8; i++) ctx[i] = (byte)((ctx[i - 1] << 2) ^ (ctx[i] + ctx[i - 1] - 0x2C) ^ 0x45);

            var subkeys = new ulong[32];
            subkeys[0] = BinaryPrimitives.ReadUInt64LittleEndian(ctx);
            for (int r = 1; r < 32; r++) subkeys[r] = unchecked(subkeys[r - 1] * 5);
            return subkeys;
        }

        /// <summary>Encrypts one 8-byte block at byte counter <paramref name="counter"/> (spec section 4.5.2 step 3).</summary>
        private static void EncryptBlock(ReadOnlySpan<byte> input, Span<byte> output, ulong counter)
        {
            ulong sk = Subkeys[(counter >> 3) % 32];
            // The input's first word becomes the high half.
            ulong l = BinaryPrimitives.ReadUInt32LittleEndian(input);
            ulong r = BinaryPrimitives.ReadUInt32LittleEndian(input[4..]);
            l ^= sk >> 32;
            r ^= sk & 0xFFFFFFFF;
            ulong cVal = unchecked(((counter << 30) | (counter << 20) | (counter << 10) | counter) + 0xA1652347UL);
            l ^= cVal >> 32;
            r ^= cVal & 0xFFFFFFFF;
            ulong w = unchecked(((l << 32) | r) + sk);
            BinaryPrimitives.WriteUInt64LittleEndian(output, w);

            Span<byte> skBytes = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(skBytes, sk);
            byte dl = (byte)(((counter >> 3) & 0xFF) ^ 0x45);
            for (int i = 0; i < 8; i++)
            {
                byte cl = output[i];
                for (int j = 0; j < 8; j++) cl = (byte)(cl + skBytes[j] + 0x88);
                cl ^= dl;
                output[i] = cl;
                dl = cl;
            }
        }
    }

    /// <summary>
    /// <c>ClientFileChecksum</c>: a 32-bit shifted sum over every byte of a file as stored on disk. Each byte is sign
    /// extended and shifted left by 4k bits (mod 32), with k counting 0..period and wrapping. The lobby's 0x07 selection
    /// digest carries it over <c>FFXiMain.dll</c> with period 0x13, so it is computed from the install, never shipped.
    /// <para>
    /// Algorithm referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v2.1.19, section 5.9
    /// and test vector 14.
    /// </para>
    /// </summary>
    public static class ClientFileChecksum
    {
        /// <summary>The period the 0x07 <c>ClientChecksum</c> uses over <c>FFXiMain.dll</c>.</summary>
        public const int FfxiMainPeriod = 0x13;

        public const string FfxiMainFileName = "FFXiMain.dll";

        public static uint Compute(ReadOnlySpan<byte> data, int period)
        {
            uint acc = 0;
            int k = 0;
            foreach (byte b in data)
            {
                if (k > period) k = 0;
                acc = unchecked(acc + ((uint)(sbyte)b << (4 * k % 32)));
                k++;
            }
            return acc;
        }

        /// <summary>The checksum of <c>FFXiMain.dll</c> in the FFXI install folder.</summary>
        public static uint ComputeFfxiMain(string ffxiRoot) =>
            Compute(System.IO.File.ReadAllBytes(System.IO.Path.Combine(ffxiRoot, FfxiMainFileName)), FfxiMainPeriod);
    }
}
