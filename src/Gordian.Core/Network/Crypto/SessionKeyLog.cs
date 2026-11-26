// src/Gordian.Core/Network/Crypto/SessionKeyLog.cs
using System;
using System.Buffers.Binary;

namespace Gordian.Core.Network.Crypto
{
    /// <summary>
    /// How logs name a session key without writing it (#640). Logs are shared in issues and test rounds, so no log line,
    /// at any level, carries key bytes: only the 32-bit fingerprint of bytes 0-15 and, for the 20-byte map key, the
    /// counter word at bytes 16-19, which LandSandBoat's connect server logs as "key value" and "key counter".
    /// </summary>
    public static class SessionKeyLog
    {
        /// <summary>
        /// FNV-1a (32-bit) of key bytes 0-15: the fingerprint LandSandBoat's connect server logs as "key value". Four
        /// bytes of a 16-byte key's hash give nothing usable for the key itself.
        /// </summary>
        public static uint Fingerprint(ReadOnlySpan<byte> key)
        {
            uint hash = 2166136261u;
            int n = Math.Min(16, key.Length);
            for (int i = 0; i < n; i++)
            {
                hash = unchecked((hash ^ key[i]) * 16777619u);
            }
            return hash;
        }

        /// <summary>
        /// The log label for <paramref name="key"/>: <c>key value XXXXXXXX</c>, plus <c>key counter XXXXXXXX</c> (bytes
        /// 16-19, little-endian) for a 20-byte key, and the length for any other size. Never the key bytes.
        /// </summary>
        public static string Describe(ReadOnlySpan<byte> key)
        {
            if (key.IsEmpty) return "no key";
            if (key.Length == 20)
                return $"key value {Fingerprint(key):X8}, key counter {BinaryPrimitives.ReadUInt32LittleEndian(key.Slice(16, 4)):X8}";
            return $"key value {Fingerprint(key):X8}, {key.Length} bytes";
        }
    }
}
