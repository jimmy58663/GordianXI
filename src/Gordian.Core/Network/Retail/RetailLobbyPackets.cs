// src/Gordian.Core/Network/Retail/RetailLobbyPackets.cs
using System;
using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using Gordian.Core.Network.LandSandBoat;

namespace Gordian.Core.Network.Retail
{
    /// <summary>A decoded retail <c>0x0B</c> character select reply: where the map server is and the token its key uses.</summary>
    public readonly record struct RetailMapHandoff(IPAddress MapServer, int MapPort, uint MapToken, uint ZoneId, uint InstanceId);

    /// <summary>
    /// The retail lobby's key modifier: seeded by the server's <c>0x05</c>, then +1 for <c>0x1F</c> (character list),
    /// +3 for <c>0x24</c> (world list), +5 for <c>0x07</c> (select). Each request after the seed advances it by 2, which is
    /// the same stepping as LandSandBoat's lobby key counter (one step per request and one per reply).
    /// </summary>
    public sealed class RetailLobbyModifier
    {
        private uint _next;

        /// <summary>Whether the server's seed has arrived.</summary>
        public bool IsSeeded { get; private set; }

        /// <summary>Takes the <c>0x05</c> seed M0; the next request uses M0 + 1.</summary>
        public void Seed(uint modifierSeed)
        {
            _next = unchecked(modifierSeed + 1);
            IsSeeded = true;
        }

        /// <summary>The modifier for the next request, then steps past that request and its reply.</summary>
        public uint Take()
        {
            if (!IsSeeded) throw new InvalidOperationException("The lobby has not sent its key modifier (0x05) yet.");
            uint value = _next;
            _next = unchecked(_next + 2);
            return value;
        }
    }

    /// <summary>
    /// The retail FFXI lobby's packets (<c>ffxi00.pol.com:54001</c>). Retail puts an MD5 of the whole packet in the
    /// header where LandSandBoat expects the session hash, and has no separate data channel: the character and world
    /// lists come back on this connection. Layouts LandSandBoat shares (the 0x20 character list, the 0x23 world list) are
    /// read with <see cref="LobbyPackets"/>.
    /// <para>
    /// Protocol referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v1.2.0, sections 4.5,
    /// 5.5 and 5.6.
    /// </para>
    /// </summary>
    public static class RetailLobbyPackets
    {
        public const string Host = "ffxi00.pol.com";
        public const int Port = 54001;

        /// <summary>The 0x26 client platform (Windows retail).</summary>
        public const uint ClientPlatformWindows = 5;

        /// <summary>The 64-byte block of 0x26 (+0x34..+0x73, the account id first) that 0x07's selection digest repeats.</summary>
        public const int ReservedTokenLength = 64;

        public const int SelectCharacterSize = 0x58;
        public const int MapHandoffSize = 0x4C;

        /// <summary>Writes the header (size, "IXFF", command) and leaves the digest field zero.</summary>
        public static void WriteHeader(Span<byte> packet, byte command)
        {
            packet.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(packet, (uint)packet.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[4..], LobbyPackets.Terminator);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[8..], command);
        }

        /// <summary>Writes the packet's MD5 (over the whole packet with +0x0C..+0x1B zeroed) into +0x0C.</summary>
        public static void Seal(Span<byte> packet)
        {
            packet.Slice(0x0C, 16).Clear();
            Span<byte> digest = stackalloc byte[16];
            MD5.HashData(packet, digest);
            digest.CopyTo(packet[0x0C..]);
        }

        /// <summary>Whether a received packet's MD5 matches.</summary>
        public static bool Verify(ReadOnlySpan<byte> packet)
        {
            if (packet.Length < LobbyPackets.HeaderSize) return false;
            Span<byte> copy = packet.Length <= 4096 ? stackalloc byte[packet.Length] : new byte[packet.Length];
            packet.CopyTo(copy);
            copy.Slice(0x0C, 16).Clear();
            Span<byte> digest = stackalloc byte[16];
            MD5.HashData(copy, digest);
            return digest.SequenceEqual(packet.Slice(0x0C, 16));
        }

        /// <summary>
        /// C2S 0x26 (0x98): platform, account id, the rest of the 64-byte reserved block, the client version, 0xFF, the
        /// installed expansions. <paramref name="reservedTail"/> is the block after the account id (60 bytes); its
        /// contents are a spec gap, so callers pass zeros until it is specified.
        /// </summary>
        public static void WriteLobbyLogin(Span<byte> packet, uint ffxiAccountId, ReadOnlySpan<byte> reservedTail, string clientVersion,
            uint expansions = LobbyPackets.AllExpansions)
        {
            packet = packet[..LobbyPackets.LobbyLoginSize];
            WriteHeader(packet, LobbyCommand.RequestLobbyLogin);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[0x2C..], ClientPlatformWindows);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[0x34..], ffxiAccountId);
            if (!reservedTail.IsEmpty) reservedTail[..Math.Min(60, reservedTail.Length)].CopyTo(packet[0x38..]);
            PolpPackets.WriteAscii(packet.Slice(0x74, 15), clientVersion);
            packet[0x83] = 0xFF;
            BinaryPrimitives.WriteUInt32LittleEndian(packet[0x84..], expansions);
            Seal(packet);
        }

        /// <summary>The 64-byte reserved block of a written 0x26 (+0x34..+0x73).</summary>
        public static ReadOnlySpan<byte> ReservedToken(ReadOnlySpan<byte> lobbyLogin) => lobbyLogin.Slice(0x34, ReservedTokenLength);

        /// <summary>A 0x2C request carrying only the session hash at +0x1C: 0x1F (character list) or 0x24 (world list).</summary>
        public static void WriteHashOnlyRequest(Span<byte> packet, byte command, ReadOnlySpan<byte> sessionHash)
        {
            packet = packet[..0x2C];
            WriteHeader(packet, command);
            sessionHash[..16].CopyTo(packet[0x1C..]);
            Seal(packet);
        }

        /// <summary>
        /// C2S 0x07 (0x58): content id, character id, name, the session hash, and the selection digest MD5(reserved
        /// block[64] + hash[16] + LE32 content id + LE32 character id + 4 zero bytes).
        /// </summary>
        public static void WriteSelectCharacter(Span<byte> packet, uint contentId, uint characterId, string name, ReadOnlySpan<byte> sessionHash,
            ReadOnlySpan<byte> reservedToken)
        {
            if (reservedToken.Length != ReservedTokenLength) throw new ArgumentException("The reserved block is 64 bytes.", nameof(reservedToken));
            packet = packet[..SelectCharacterSize];
            WriteHeader(packet, LobbyCommand.RequestSelectChr);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[0x1C..], contentId);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[0x20..], characterId);
            PolpPackets.WriteAscii(packet.Slice(0x24, 16), name);
            sessionHash[..16].CopyTo(packet[0x34..]);

            Span<byte> selection = stackalloc byte[0x5C];
            selection.Clear();
            reservedToken.CopyTo(selection);
            sessionHash[..16].CopyTo(selection[64..]);
            BinaryPrimitives.WriteUInt32LittleEndian(selection[80..], contentId);
            BinaryPrimitives.WriteUInt32LittleEndian(selection[84..], characterId);
            MD5.HashData(selection, packet[0x48..]);
            Seal(packet);
        }

        /// <summary>
        /// S2C 0x0B as the spec lays it out (0x4C): map server address +0x38, port +0x3C, map token +0x40, zone +0x44,
        /// instance +0x48. <b>Open question:</b> LandSandBoat and XiPackets read a 0x48-byte 0x0B whose +0x40 is the
        /// search server's address; the research side is confirming which is right.
        /// </summary>
        public static bool TryParseMapHandoff(ReadOnlySpan<byte> packet, out RetailMapHandoff handoff)
        {
            handoff = default;
            if (packet.Length < MapHandoffSize || LobbyPackets.ReadCommand(packet) != LobbyCommand.ResponseNextLogin) return false;
            uint address = BinaryPrimitives.ReadUInt32LittleEndian(packet[0x38..]);
            handoff = new RetailMapHandoff(
                new IPAddress(address),
                BinaryPrimitives.ReadUInt16LittleEndian(packet[0x3C..]),
                BinaryPrimitives.ReadUInt32LittleEndian(packet[0x40..]),
                BinaryPrimitives.ReadUInt32LittleEndian(packet[0x44..]),
                BinaryPrimitives.ReadUInt32LittleEndian(packet[0x48..]));
            return true;
        }

        /// <summary>
        /// The map server's 20-byte raw key: the lobby session key and the little-endian map token. The map session cipher
        /// (<c>LegacyBlowfishCryptoSuite.InitializeKey</c>) MD5s a 20-byte key, which gives the spec's MD5(key + token).
        /// </summary>
        public static byte[] MapRawKey(ReadOnlySpan<byte> lobbySessionKey, uint mapToken) => PolHashes.KeyWithCounter(lobbySessionKey, mapToken);
    }
}
