// src/Gordian.Core/Network/Retail/PolHashes.cs
using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Gordian.Core.Network.Retail
{
    /// <summary>
    /// The checksums and credential hashes of retail login. Protocol referenced from the GordianXI retail login spec,
    /// docs/network/retail-login-spec.md v2.1.19: sections 4.1.1 (CRC-32), 4.2.2 (POL password), 4.3.3 (challenge MD5),
    /// 5.3 (additive checksum), 5.4 (Square Enix password), 5.5 (lobby session hash), 5.6 (map key) and 5.7 (x-MD5-pol);
    /// test vectors in section 5.8.
    /// </summary>
    public static class PolHashes
    {
        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        /// <summary>CRC-32 (IEEE 802.3, reflected polynomial 0xEDB88320, initial and final XOR 0xFFFFFFFF): the POLP checksum.</summary>
        public static uint Crc32(ReadOnlySpan<byte> data)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        /// <summary>
        /// The <c>:51220</c> additive checksum over <paramref name="data"/>: the sum of its little-endian 32-bit words, the
        /// last 1-3 bytes counted as a zero-padded word.
        /// </summary>
        public static uint AdditiveChecksum(ReadOnlySpan<byte> data)
        {
            uint acc = 0;
            int whole = data.Length & ~3;
            for (int i = 0; i < whole; i += 4) acc += BinaryPrimitives.ReadUInt32LittleEndian(data[i..]);
            if ((data.Length & 3) != 0)
            {
                Span<byte> tail = stackalloc byte[4];
                tail.Clear();
                data[whole..].CopyTo(tail);
                acc += BinaryPrimitives.ReadUInt32LittleEndian(tail);
            }
            return acc;
        }

        /// <summary>Writes the additive checksum of all but the last 4 bytes of <paramref name="payload"/> into those 4 bytes.</summary>
        public static void SealAdditive(Span<byte> payload) =>
            BinaryPrimitives.WriteUInt32LittleEndian(payload[^4..], AdditiveChecksum(payload[..^4]));

        /// <summary>Whether the last 4 bytes of <paramref name="payload"/> are the additive checksum of the rest.</summary>
        public static bool VerifyAdditive(ReadOnlySpan<byte> payload) =>
            payload.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(payload[^4..]) == AdditiveChecksum(payload[..^4]);

        /// <summary>
        /// The time-bound Square Enix password digest: SHA-1(lowercase hex SHA-1(password) + the server time rounded down to
        /// the minute, in decimal).
        /// </summary>
        public static byte[] SquareEnixPasswordDigest(string password, long serverUnixSeconds)
        {
            string h1 = Convert.ToHexStringLower(SHA1.HashData(Encoding.ASCII.GetBytes(password ?? string.Empty)));
            string minute = (serverUnixSeconds / 60 * 60).ToString(CultureInfo.InvariantCulture);
            return SHA1.HashData(Encoding.ASCII.GetBytes(h1 + minute));
        }

        /// <summary>
        /// The <c>:51220</c> request header's digest: MD5(POL ID (8 ASCII characters) + the plaintext POL password +
        /// LE32(this connection's challenge seed)) (spec v2.1.19 section 4.3.3, test vector 15). Whether retail changes the
        /// case of either first is not known; issued POL IDs are upper case, so the ID is sent upper case.
        /// </summary>
        public static byte[] ChallengeDigest(string polId, string polPassword, uint challengeSeed)
        {
            byte[] id = Encoding.ASCII.GetBytes(polId.Trim().ToUpperInvariant());
            byte[] pass = Encoding.ASCII.GetBytes(polPassword ?? string.Empty);
            byte[] buffer = new byte[id.Length + pass.Length + 4];
            id.CopyTo(buffer, 0);
            pass.CopyTo(buffer, id.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(id.Length + pass.Length), challengeSeed);
            return MD5.HashData(buffer);
        }

        /// <summary>A 16-byte key followed by a little-endian counter: the lobby session hash input and the map server's raw key.</summary>
        public static byte[] KeyWithCounter(ReadOnlySpan<byte> key16, uint counter)
        {
            if (key16.Length != 16) throw new ArgumentException("The key is 16 bytes.", nameof(key16));
            var buffer = new byte[20];
            key16.CopyTo(buffer);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16), counter);
            return buffer;
        }

        /// <summary>The lobby session hash: MD5(LobbySessionKey[16] + LE32(modifier)).</summary>
        public static byte[] LobbySessionHash(ReadOnlySpan<byte> lobbySessionKey, uint modifier) =>
            MD5.HashData(KeyWithCounter(lobbySessionKey, modifier));

        /// <summary>
        /// The x-MD5-pol digest's <c>userName</c>: the client's address as the server sees it, <c>"a.b.c.d:port"</c> in
        /// decimal, in <see cref="PolCoreBase64"/>.
        /// </summary>
        public static string PmlUserName(ReadOnlySpan<byte> routingToken)
        {
            ushort port = BinaryPrimitives.ReadUInt16LittleEndian(routingToken);
            string address = $"{routingToken[5]}.{routingToken[4]}.{routingToken[3]}.{routingToken[2]}";
            return PolCoreBase64.Encode(Encoding.ASCII.GetBytes($"{address}:{port.ToString(CultureInfo.InvariantCulture)}"));
        }

        /// <summary>
        /// The x-MD5-pol digest response (not RFC 2617: no cnonce, nc or qop, and the nonce comes last):
        /// MD5(HA1 ":" HA2 ":" nonce) with HA1 = MD5(userName ":" realm ":" B64(K8)) and HA2 = MD5("GET:" uri), all as
        /// lowercase hex; B64 is <see cref="PolCoreBase64"/>.
        /// </summary>
        public static string PmlDigestResponse(string userName, string realm, ReadOnlySpan<byte> sessionKey, string uri, string nonce)
        {
            static string Md5Hex(string s) => Convert.ToHexStringLower(MD5.HashData(Encoding.ASCII.GetBytes(s)));
            string ha1 = Md5Hex($"{userName}:{realm}:{PolCoreBase64.Encode(sessionKey)}");
            string ha2 = Md5Hex($"GET:{uri}");
            return Md5Hex($"{ha1}:{ha2}:{nonce}");
        }
    }
}
