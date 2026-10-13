// src/Gordian.Core/Network/LandSandBoat/LsbSessionKeys.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using Gordian.Core.Network.Crypto;

namespace Gordian.Core.Network.LandSandBoat
{
    /// <summary>How a LandSandBoat server builds the map session key it stores at character select.</summary>
    public enum LsbKeyStyle
    {
        /// <summary>
        /// LandSandBoat since <c>393e176732</c> (Oct 2026): bytes 0-15 are the MD5 of the auth session hash, bytes 16-19
        /// the lobby key counter + 3 as a little-endian u32.
        /// </summary>
        Counter,

        /// <summary>
        /// LandSandBoat <c>01e5b447e1</c> .. <c>393e176732</c> (Sep-Oct 2026): bytes 0-15 the MD5 of the session hash,
        /// bytes 16-19 the loader's key with byte 16 raised by the old per-operation increments.
        /// </summary>
        HashedLegacy,

        /// <summary>
        /// LandSandBoat before <c>01e5b447e1</c>: the loader's 20-byte key with byte 16 raised by 6 after a creation, 4
        /// per deletion and per rename, 1 per refused "already logged in" selection (single-byte add, no carry).
        /// </summary>
        Legacy,
    }

    /// <summary>One map session key GordianXI may need at the map server, with the scheme that produced it.</summary>
    public sealed record LsbSessionKeyCandidate(LsbKeyStyle Style, byte[] Key)
    {
        /// <summary>The FNV-1a fingerprint of key bytes 0-15 (LandSandBoat logs the same as "key value").</summary>
        public uint Fingerprint => LsbSessionKeys.Fingerprint(Key);

        /// <summary>Bytes 16-19 as a little-endian u32 (LandSandBoat logs it as "key counter").</summary>
        public uint CounterWord => Key.Length >= 20 ? BinaryPrimitives.ReadUInt32LittleEndian(Key.AsSpan(16, 4)) : 0;

        /// <summary>A label for logs: the style, the fingerprint and the counter word, never the key itself.</summary>
        public string Describe() => $"{Style} (key value {Fingerprint:X8}, key counter {CounterWord:X8})";

        public SessionKeyCandidate ToCryptoCandidate() => new(Style.ToString(), Key);
    }

    /// <summary>
    /// The client side of LandSandBoat's lobby key counter: the number the retail client steps on certain lobby requests
    /// and replies and hashes into its map session key. LandSandBoat follows it on the server since <c>393e176732</c>
    /// ("Follow the client's lobby key counter instead of guessing it"), so GordianXI has to step it the same way for
    /// everything it actually exchanges with the view server.
    /// <list type="bullet">
    /// <item>+1 when the client sends view request 0x07, 0x14, 0x1F, 0x21, 0x22, 0x24, 0x28 or 0x2B.</item>
    /// <item>The 0x05 reply (to 0x26) sets it to the u32 key at offset 0x1C of that reply + 1.</item>
    /// <item>+1 for each reply 0x03, 0x0B, 0x20 or 0x23 the view server writes through its session.</item>
    /// <item>+1 for an error reply 0x04 only when the last request was 0x22, 0x24, 0x28 or 0x2B.</item>
    /// </list>
    /// Not counted: replies the server writes on the data channel (0x01, 0x02, the 0x03 id list) and the 0x0B select
    /// reply, which LandSandBoat writes directly to the socket and accounts for in the "+3" at select instead.
    /// Counter rules referenced from LandSandBoat (https://github.com/LandSandBoat/server, src/login/view_session.cpp
    /// <c>stepsKeyOnRequest</c> / <c>stepsKeyOnReply</c> / <c>stepsKeyOnError</c> / <c>view_session::do_write</c>).
    /// </summary>
    public sealed class LobbyKeyCounter
    {
        /// <summary>The counter value; 0 until the server's 0x05 reply has been seen (LandSandBoat's "no counter").</summary>
        public uint Value { get; private set; }

        /// <summary>The last view request command sent (decides whether an error reply steps the counter).</summary>
        public byte LastRequest { get; private set; }

        /// <summary>
        /// Records a view request sent to the server. With <paramref name="deferStep"/> the step waits for
        /// <see cref="ConfirmDeferredRequest"/>: LandSandBoat can lose a 0x1F (it then never reads it, so never steps),
        /// and only the data channel's 0x01 prompt shows that it was read.
        /// </summary>
        public void OnRequest(byte command, bool deferStep = false)
        {
            LastRequest = command;
            if (!deferStep && StepsOnRequest(command)) Value++;
        }

        /// <summary>Counts a deferred request once the server is known to have read it (one 0x01 prompt per 0x1F).</summary>
        public void ConfirmDeferredRequest() => Value++;

        /// <summary>Records a view reply (the whole packet, command at offset 8).</summary>
        public void OnReply(ReadOnlySpan<byte> packet)
        {
            if (packet.Length < 12) return;
            byte command = packet[8];
            if (command == LobbyCommand.ResponseKey)
            {
                // The client starts over from the key in every 0x05, then steps once for the reply itself.
                uint key = packet.Length >= 0x20 ? BinaryPrimitives.ReadUInt32LittleEndian(packet[0x1C..]) : 0u;
                Value = unchecked(key + 1);
            }
            else if (StepsOnReply(command) || (command == LobbyCommand.ResponseError && StepsOnError(LastRequest)))
            {
                Value++;
            }
        }

        public static bool StepsOnRequest(byte command) => command is 0x07 or 0x14 or 0x1F or 0x21 or 0x22 or 0x24 or 0x28 or 0x2B;

        public static bool StepsOnReply(byte command) => command is 0x03 or 0x0B or 0x20 or 0x23;

        public static bool StepsOnError(byte lastRequest) => lastRequest is 0x22 or 0x24 or 0x28 or 0x2B;
    }

    /// <summary>
    /// Map session key derivations for LandSandBoat's three key schemes (see <see cref="LsbKeyStyle"/>).
    /// Key derivation referenced from LandSandBoat (https://github.com/LandSandBoat/server, src/login/data_session.cpp
    /// 0xA2, commits 01e5b447e1 "Derive the map session key from the login session" and 393e176732 "Follow the client's
    /// lobby key counter instead of guessing it").
    /// </summary>
    public static class LsbSessionKeys
    {
        public const int KeyLength = 20;

        /// <summary>The steps the counter takes after 0xA2: one for the 0x0B select reply and two at zone connect.</summary>
        public const uint CounterStepsAfterSelect = 3;

        /// <summary>
        /// The counter-style key: MD5 of the 16-byte auth session hash in bytes 0-15, then <paramref name="counter"/> + 3
        /// (u32 LE) in bytes 16-19. With a zero counter LandSandBoat keeps the loader's bytes 16-19 (it logs "no lobby
        /// key counter"), so this does the same with <paramref name="loaderKey"/>.
        /// </summary>
        public static byte[] DeriveCounterKey(ReadOnlySpan<byte> sessionHash, uint counter, ReadOnlySpan<byte> loaderKey)
        {
            var key = new byte[KeyLength];
            loaderKey[..Math.Min(KeyLength, loaderKey.Length)].CopyTo(key);
            MD5.HashData(sessionHash, key.AsSpan(0, 16));
            if (counter != 0)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(key.AsSpan(16, 4), unchecked(counter + CounterStepsAfterSelect));
            }
            return key;
        }

        /// <summary>
        /// The old-style key: the loader's key with byte 16 raised by <paramref name="increment"/> (a single-byte add,
        /// no carry, as LandSandBoat's <c>key3[16] += ...</c>). With <paramref name="sessionHash"/> given, bytes 0-15
        /// are its MD5 (the <see cref="LsbKeyStyle.HashedLegacy"/> scheme).
        /// </summary>
        public static byte[] DeriveLegacyKey(ReadOnlySpan<byte> loaderKey, int increment, ReadOnlySpan<byte> sessionHash = default)
        {
            var key = new byte[KeyLength];
            loaderKey[..Math.Min(KeyLength, loaderKey.Length)].CopyTo(key);
            if (!sessionHash.IsEmpty) MD5.HashData(sessionHash, key.AsSpan(0, 16));
            key[16] = unchecked((byte)(key[16] + increment));
            return key;
        }

        /// <summary>FNV-1a (32-bit) of key bytes 0-15: the fingerprint LandSandBoat's connect server logs as "key value".</summary>
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
        /// Every key the map server may use, newest scheme first and without duplicates: the counter key, the hashed
        /// legacy key and the legacy key.
        /// </summary>
        public static IReadOnlyList<LsbSessionKeyCandidate> BuildCandidates(ReadOnlySpan<byte> sessionHash, uint counter,
            ReadOnlySpan<byte> loaderKey, int legacyIncrement)
        {
            var list = new List<LsbSessionKeyCandidate>(3);
            void Add(LsbKeyStyle style, byte[] key)
            {
                foreach (var existing in list)
                {
                    if (existing.Key.AsSpan().SequenceEqual(key)) return;
                }
                list.Add(new LsbSessionKeyCandidate(style, key));
            }

            if (sessionHash.Length == 16)
            {
                Add(LsbKeyStyle.Counter, DeriveCounterKey(sessionHash, counter, loaderKey));
                Add(LsbKeyStyle.HashedLegacy, DeriveLegacyKey(loaderKey, legacyIncrement, sessionHash));
            }
            Add(LsbKeyStyle.Legacy, DeriveLegacyKey(loaderKey, legacyIncrement));
            return list;
        }
    }
}
