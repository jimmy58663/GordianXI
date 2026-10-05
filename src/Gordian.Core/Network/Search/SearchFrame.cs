// src/Gordian.Core/Network/Search/SearchFrame.cs
using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Gordian.Core.Network.Crypto;

namespace Gordian.Core.Network.Search
{
    /// <summary>
    /// The framing and encryption of one search (cache) server packet, in both directions. A frame is
    /// <c>[u16 length][u16 0]["IXFF"][payload][MD5 hash, 16][key seed, 4]</c>; the payload and the hash are enciphered in 8-byte
    /// Blowfish blocks (the world server's Blowfish, key schedule and ECB mode) starting at byte 8, as many whole blocks as fit
    /// before the seed. The MD5 hash covers the plain payload.
    /// <para>
    /// The Blowfish key is not the world session key but the MD5 of a 20- or 24-byte string: a constant 16 bytes, the 4-byte seed
    /// the client puts at the end of its request, and for the answer the 4 bytes of the request's payload just before its hash.
    /// A request is therefore keyed by <c>MD5(constant + seed)</c> and every answer to it by
    /// <c>MD5(constant + seed + request payload tail)</c>.
    /// </para>
    /// Wire format referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>src/search/search_handler.cpp</c>
    /// (<c>decrypt</c>, <c>encrypt</c>, <c>validatePacket</c>) and <c>search_handler.h</c> (the constant); XiPackets
    /// (https://github.com/atom0s/XiPackets), <c>cache/README.md</c> only names the server.
    /// </summary>
    public static class SearchFrame
    {
        /// <summary>The bytes before the payload: length, 2 bytes of zero and the magic.</summary>
        public const int HeaderSize = 8;

        /// <summary>The MD5 hash and the key seed after the payload.</summary>
        public const int TrailerSize = 16 + 4;

        /// <summary>The magic after the length: the ASCII text <c>IXFF</c>.</summary>
        public const uint Magic = 0x46465849;

        /// <summary>The fixed first 16 bytes of the Blowfish key string.</summary>
        private static ReadOnlySpan<byte> KeyConstant => new byte[]
        {
            0x30, 0x73, 0x3D, 0x6D, 0x3C, 0x31, 0x49, 0x5A, 0x32, 0x7A, 0x42, 0x43, 0x63, 0x38, 0x7B, 0x7E
        };

        /// <summary>
        /// What a request leaves behind for reading its answers: the key seed sent and the request's last 4 plain payload
        /// bytes before the hash. <see cref="IsEmpty"/> for a default value.
        /// </summary>
        public readonly record struct AnswerKey(uint Seed, uint PayloadTail)
        {
            public bool IsEmpty => Seed == 0 && PayloadTail == 0;
        }

        /// <summary>
        /// Wraps a request <paramref name="body"/> (the 8 header bytes, which this fills, then the payload) into a frame:
        /// hashes, enciphers and appends <paramref name="seed"/>. The body is at least 12 bytes and its payload length a
        /// multiple of 8 (the server enciphers whole 8-byte blocks of payload and hash).
        /// </summary>
        public static byte[] EncryptRequest(ReadOnlySpan<byte> body, uint seed, out AnswerKey answerKey)
        {
            if (body.Length < HeaderSize + 4 || ((body.Length - HeaderSize) & 7) != 0)
            {
                throw new ArgumentException("A search request body is 8 header bytes plus a payload that is a multiple of 8 bytes.", nameof(body));
            }

            int payloadLength = body.Length - HeaderSize;
            int total = body.Length + TrailerSize;
            var frame = new byte[total];
            body.CopyTo(frame);
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(0, 2), (ushort)total);
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4, 4), Magic);

            uint tail = BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(body.Length - 4, 4));
            answerKey = new AnswerKey(seed, tail);

            using (var suite = NewSuite(seed, null))
            {
                suite.EncryptAndSign(frame, HeaderSize, payloadLength);
            }
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(total - 4, 4), seed);
            return frame;
        }

        /// <summary>
        /// Deciphers an answer <paramref name="frame"/> in place and checks its hash and length. On success
        /// <paramref name="content"/> is the plain bytes from the start of the frame to the hash (the 8 header bytes included, so
        /// the offsets of the formats apply as written).
        /// </summary>
        public static bool TryDecryptResponse(Span<byte> frame, AnswerKey answerKey, out ReadOnlySpan<byte> content)
        {
            content = default;
            if (frame.Length < HeaderSize + TrailerSize) return false;
            if (BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(0, 2)) != frame.Length) return false;

            using var suite = NewSuite(answerKey.Seed, answerKey.PayloadTail);
            if (!suite.TryDecryptAndVerify(frame.Slice(0, frame.Length - 4), HeaderSize, out _)) return false;

            content = frame.Slice(0, frame.Length - TrailerSize);
            return true;
        }

        private static LegacyBlowfishCryptoSuite NewSuite(uint seed, uint? payloadTail)
        {
            Span<byte> keyString = stackalloc byte[24];
            KeyConstant.CopyTo(keyString);
            BinaryPrimitives.WriteUInt32LittleEndian(keyString.Slice(16, 4), seed);
            int length = 20;
            if (payloadTail.HasValue)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(keyString.Slice(20, 4), payloadTail.Value);
                length = 24;
            }

            Span<byte> key = stackalloc byte[16];
            MD5.HashData(keyString.Slice(0, length), key);
            var suite = new LegacyBlowfishCryptoSuite();
            suite.InitializeKey(key);
            return suite;
        }

        /// <summary>
        /// Wraps an answer payload the way the server does (the inverse of <see cref="TryDecryptResponse"/>). The server side
        /// has no use in a client; this exists to test the client against frames it did not build itself, and for
        /// local test servers. <paramref name="content"/> is the plain bytes from the frame start to the end of the data
        /// (the 8 header bytes are filled here); <paramref name="padding"/> zero bytes go between it and the hash.
        /// </summary>
        public static byte[] EncryptResponse(ReadOnlySpan<byte> content, AnswerKey answerKey, int padding = 0)
        {
            int total = content.Length + padding + TrailerSize;
            var frame = new byte[total];
            content.CopyTo(frame);
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(0, 2), (ushort)total);
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4, 4), Magic);

            using (var suite = NewSuite(answerKey.Seed, answerKey.PayloadTail))
            {
                suite.EncryptAndSign(frame, HeaderSize, total - HeaderSize - TrailerSize);
            }
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(total - 4, 4), answerKey.Seed);
            return frame;
        }

        /// <summary>
        /// The server's view of a request frame (for tests and local servers): deciphers it in place and returns the plain
        /// body and the <see cref="AnswerKey"/> it answers with. False when the length or the hash is wrong.
        /// </summary>
        public static bool TryDecryptRequest(Span<byte> frame, out AnswerKey answerKey)
        {
            answerKey = default;
            if (frame.Length < 28) return false;
            if (BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(0, 2)) != frame.Length) return false;

            uint seed = BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(frame.Length - 4, 4));
            using var suite = NewSuite(seed, null);
            if (!suite.TryDecryptAndVerify(frame.Slice(0, frame.Length - 4), HeaderSize, out _)) return false;

            answerKey = new AnswerKey(seed, BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(frame.Length - 24, 4)));
            return true;
        }
    }
}
