// src/Gordian.Core/Network/Retail/RetailLobbyPackets.cs
using System;
using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using Gordian.Core.Network.LandSandBoat;

namespace Gordian.Core.Network.Retail
{
    /// <summary>
    /// A decoded retail <c>0x0B</c> character select reply: the map server (UDP), the search server, and the 4 bytes at
    /// +0x20 the first map packet sends back as its <c>UniqueNo</c>.
    /// </summary>
    public readonly record struct RetailMapHandoff(IPAddress MapServer, int MapPort, IPAddress SearchServer, int SearchPort, uint UniqueNo);

    /// <summary>
    /// The retail lobby's key modifier: seeded by the server's <c>0x05</c>; each request that carries a session hash uses
    /// M0 + 1 + 2n, n being the number of such requests already sent (spec v2.1.19 section 5.5). With no character
    /// management that makes 0x1F +1, 0x24 +3, 0x07 +5. The map key is the 0x07 modifier + 4 (section 5.6).
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
    /// Protocol referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v2.1.19, sections 4.5,
    /// 4.6, 5.5 and 5.6 (test vectors 5, 8 and 11).
    /// </para>
    /// </summary>
    public static class RetailLobbyPackets
    {
        public const string Host = "ffxi00.pol.com";
        public const int Port = 54001;

        /// <summary>The 0x26 <c>ClientCode</c> (Windows retail).</summary>
        public const uint ClientPlatformWindows = 5;

        /// <summary>The 0x26 <c>AuthCode</c> field (+0x34..+0x73): the 52-byte token and 12 zero bytes.</summary>
        public const int AuthCodeFieldLength = 64;

        /// <summary>The constant at 0x07 +0x44, repeated in the selection digest.</summary>
        public const uint DllTableIndex = 3;

        /// <summary>The 0x26 expansion mask every captured login sends.</summary>
        public const uint ExpansionMask = 0x00000FFF;

        public const int SelectCharacterSize = 0x58;
        public const int MapHandoffSize = 0x48;

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
        /// C2S 0x26 (0x98): ClientCode 5 at +0x2C, the 52-byte AuthCode (<see cref="PolAuthCode"/>) at +0x34 followed by
        /// 12 zero bytes, the client version from <c>patch.cfg</c> at +0x74, 0xFF at +0x83, the expansion mask at +0x84.
        /// </summary>
        public static void WriteLobbyLogin(Span<byte> packet, ReadOnlySpan<byte> authCode, string clientVersion, uint expansions = ExpansionMask)
        {
            if (authCode.Length != PolAuthCode.TokenLength) throw new ArgumentException("The AuthCode token is 52 bytes.", nameof(authCode));
            packet = packet[..LobbyPackets.LobbyLoginSize];
            WriteHeader(packet, LobbyCommand.RequestLobbyLogin);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[0x2C..], ClientPlatformWindows);
            authCode.CopyTo(packet[0x34..]);
            PolpPackets.WriteAscii(packet.Slice(0x74, 15), clientVersion);
            packet[0x83] = 0xFF;
            BinaryPrimitives.WriteUInt32LittleEndian(packet[0x84..], expansions);
            Seal(packet);
        }

        /// <summary>The 64-byte AuthCode field of a written 0x26 (+0x34..+0x73), as the selection digest repeats it.</summary>
        public static ReadOnlySpan<byte> AuthCodeField(ReadOnlySpan<byte> lobbyLogin) => lobbyLogin.Slice(0x34, AuthCodeFieldLength);

        /// <summary>A 0x2C request carrying only the session hash at +0x1C: 0x1F (character list) or 0x24 (world list).</summary>
        public static void WriteHashOnlyRequest(Span<byte> packet, byte command, ReadOnlySpan<byte> sessionHash)
        {
            packet = packet[..0x2C];
            WriteHeader(packet, command);
            sessionHash[..16].CopyTo(packet[0x1C..]);
            Seal(packet);
        }

        /// <summary>
        /// C2S 0x07 (0x58): content id, the low 16 bits of the list slot's character id, name, the session hash, the
        /// constant 3, and the selection digest MD5(AuthCode field[64] + hash[16] + LE32(3) + LE32(client checksum) +
        /// LE32(content id)). The client checksum is <see cref="ClientFileChecksum"/> over the install's FFXiMain.dll.
        /// </summary>
        public static void WriteSelectCharacter(Span<byte> packet, uint contentId, uint characterId, string name, ReadOnlySpan<byte> sessionHash,
            ReadOnlySpan<byte> authCodeField, uint clientChecksum)
        {
            if (authCodeField.Length != AuthCodeFieldLength) throw new ArgumentException("The AuthCode field is 64 bytes.", nameof(authCodeField));
            packet = packet[..SelectCharacterSize];
            WriteHeader(packet, LobbyCommand.RequestSelectChr);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[0x1C..], contentId);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[0x20..], characterId & 0xFFFF);
            PolpPackets.WriteAscii(packet.Slice(0x24, 16), name);
            sessionHash[..16].CopyTo(packet[0x34..]);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[0x44..], DllTableIndex);
            SelectionDigest(authCodeField, sessionHash, clientChecksum, contentId).CopyTo(packet[0x48..]);
            Seal(packet);
        }

        /// <summary>
        /// C2S 0x22 (0x60) name check: LandSandBoat's layout (content id, name, world name) plus the session hash at +0x30
        /// and <c>00</c>, 14 × <c>FF</c>, <c>00</c> at +0x50 (spec section 4.5.10, verified against capture #5).
        /// </summary>
        public static void WriteCheckName(Span<byte> packet, uint contentId, string name, string worldName, ReadOnlySpan<byte> sessionHash)
        {
            packet = packet[..LobbyPackets.CreateCharacterPreSize];
            LobbyPackets.WriteCreateCharacterPre(packet, default, contentId, name, worldName);
            sessionHash[..16].CopyTo(packet[0x30..]);
            packet.Slice(0x51, 14).Fill(0xFF);
            Seal(packet);
        }

        /// <summary>
        /// C2S 0x21 (0x90) creation: content id, the session hash at +0x20 and the 96-byte character info at +0x30, written
        /// as for LandSandBoat (spec section 4.5.10; the info's field layout is XiPackets', not checked against retail).
        /// </summary>
        public static void WriteCreateCharacter(Span<byte> packet, uint contentId, in LobbyCharacterCreation creation, ReadOnlySpan<byte> sessionHash)
        {
            packet = packet[..LobbyPackets.CreateCharacterSize];
            LobbyPackets.WriteCreateCharacter(packet, default, contentId, creation);
            sessionHash[..16].CopyTo(packet[0x20..]);
            Seal(packet);
        }

        /// <summary>
        /// C2S 0x14 (0x34) deletion: content id, the in-world character id's low 16 bits, the session hash at +0x24 (spec
        /// section 4.5.10, from XiPackets; not seen on a retail wire).
        /// </summary>
        public static void WriteDeleteCharacter(Span<byte> packet, uint contentId, uint characterId, ReadOnlySpan<byte> sessionHash)
        {
            packet = packet[..LobbyPackets.DeleteCharacterSize];
            LobbyPackets.WriteDeleteCharacter(packet, default, contentId, characterId & 0xFFFF);
            sessionHash[..16].CopyTo(packet[0x24..]);
            Seal(packet);
        }

        /// <summary>
        /// C2S 0x28 (0x44) rename: content id, the character id's low 16 bits, the new name, the session hash at +0x34 (spec
        /// section 4.5.10, from XiPackets; not seen on a retail wire).
        /// </summary>
        public static void WriteRenameCharacter(Span<byte> packet, uint contentId, uint characterId, string newName, ReadOnlySpan<byte> sessionHash)
        {
            packet = packet[..LobbyPackets.RenameCharacterSize];
            LobbyPackets.WriteRenameCharacter(packet, default, contentId, characterId & 0xFFFF, newName);
            sessionHash[..16].CopyTo(packet[0x34..]);
            Seal(packet);
        }

        /// <summary>The 0x07 selection digest over its 92-byte preimage (spec section 4.5.8, test vector 11).</summary>
        public static byte[] SelectionDigest(ReadOnlySpan<byte> authCodeField, ReadOnlySpan<byte> sessionHash, uint clientChecksum, uint contentId)
        {
            Span<byte> preimage = stackalloc byte[0x5C];
            authCodeField[..AuthCodeFieldLength].CopyTo(preimage);
            sessionHash[..16].CopyTo(preimage[64..]);
            BinaryPrimitives.WriteUInt32LittleEndian(preimage[80..], DllTableIndex);
            BinaryPrimitives.WriteUInt32LittleEndian(preimage[84..], clientChecksum);
            BinaryPrimitives.WriteUInt32LittleEndian(preimage[88..], contentId);
            return MD5.HashData(preimage);
        }

        /// <summary>
        /// S2C 0x0B (0x48): the 4 bytes at +0x20 (character id low and an unknown word, sent back in the first map packet),
        /// the map server address at +0x38 (first octet first) and UDP port at +0x3C, the search server address at +0x40
        /// and port at +0x44 (spec section 4.5.9).
        /// </summary>
        public static bool TryParseMapHandoff(ReadOnlySpan<byte> packet, out RetailMapHandoff handoff)
        {
            handoff = default;
            if (packet.Length < MapHandoffSize || LobbyPackets.ReadCommand(packet) != LobbyCommand.ResponseNextLogin) return false;
            handoff = new RetailMapHandoff(
                new IPAddress(packet.Slice(0x38, 4)),
                (int)BinaryPrimitives.ReadUInt32LittleEndian(packet[0x3C..]),
                new IPAddress(packet.Slice(0x40, 4)),
                (int)BinaryPrimitives.ReadUInt32LittleEndian(packet[0x44..]),
                BinaryPrimitives.ReadUInt32LittleEndian(packet[0x20..]));
            return true;
        }

        /// <summary>
        /// The map server's 20-byte raw key: the lobby session key and LE32(the 0x07 modifier + 4). Its MD5 is the map key
        /// (spec section 5.6, test vector 5). The map session cipher (<c>LegacyBlowfishCryptoSuite.InitializeKey</c>) MD5s
        /// a 20-byte key, so this goes straight in, the same way LandSandBoat's 20-byte key does; that cipher is the FFXI
        /// Blowfish variant (signed key bytes, key cut at its first zero byte) the spec describes.
        /// </summary>
        public static byte[] MapRawKey(ReadOnlySpan<byte> lobbySessionKey, uint selectModifier) =>
            PolHashes.KeyWithCounter(lobbySessionKey, unchecked(selectModifier + 4));

        /// <summary>The 16-byte map key: MD5 of <see cref="MapRawKey"/>.</summary>
        public static byte[] MapKey(ReadOnlySpan<byte> lobbySessionKey, uint selectModifier) => MD5.HashData(MapRawKey(lobbySessionKey, selectModifier));
    }

    /// <summary>
    /// The retail map server's first datagram: 136 unencrypted bytes, a 28-byte transport header, the 92-byte C2S 0x00A
    /// and the MD5 of that packet.
    /// <para>
    /// Layout referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v2.1.19, section 4.6.1
    /// and test vector 8.
    /// </para>
    /// </summary>
    public static class RetailMapPackets
    {
        public const int TransportHeaderSize = 28;
        public const int LoginPacketSize = 92;
        public const int FirstDatagramSize = TransportHeaderSize + LoginPacketSize + 16;

        /// <summary>
        /// The 0x00A <c>Ticket</c>: MD5(the 15-byte account field as sent, all zeros + the 16-byte map key). Whether retail
        /// cuts a map key at a zero byte here is untested; the key is used as it is.
        /// </summary>
        public static byte[] Ticket(ReadOnlySpan<byte> mapKey)
        {
            Span<byte> buffer = stackalloc byte[15 + 16];
            buffer[..15].Clear();
            mapKey[..16].CopyTo(buffer[15..]);
            return MD5.HashData(buffer);
        }

        /// <summary>
        /// The 136-byte first datagram. The transport header is the observed one (client sequence 1; the other bytes are
        /// not explained, spec section 9 item 7). The 0x00A sends a single space as the name, an empty account field, the
        /// <c>UniqueNo</c> from 0x0B, the ticket, "WIN", language 1 and 0xFFFF at +0x5A.
        /// </summary>
        public static byte[] BuildFirstDatagram(uint uniqueNo, ReadOnlySpan<byte> mapKey)
        {
            var datagram = new byte[FirstDatagramSize];
            ReadOnlySpan<byte> header = [0x01, 0x00, 0x00, 0x00, 0xC0, 0x02, 0x00, 0x00, 0x77, 0x00, 0x00, 0x00];
            header.CopyTo(datagram);
            ReadOnlySpan<byte> headerTail = [0x24, 0xE8, 0xFE, 0x76];
            headerTail.CopyTo(datagram.AsSpan(24));

            Span<byte> packet = datagram.AsSpan(TransportHeaderSize, LoginPacketSize);
            BinaryPrimitives.WriteUInt16LittleEndian(packet, (ushort)(0x00A | ((LoginPacketSize / 4) << 9)));
            BinaryPrimitives.WriteUInt16LittleEndian(packet[2..], 1);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[0x0C..], uniqueNo);
            packet[0x22] = (byte)' ';
            Ticket(mapKey).CopyTo(packet[0x40..]);
            "WIN"u8.CopyTo(packet[0x54..]);
            BinaryPrimitives.WriteUInt16LittleEndian(packet[0x58..], 1);
            BinaryPrimitives.WriteUInt16LittleEndian(packet[0x5A..], 0xFFFF);
            byte sum = 0;
            foreach (byte b in packet[0x08..]) sum += b;
            packet[0x04] = sum;

            MD5.HashData(packet, datagram.AsSpan(TransportHeaderSize + LoginPacketSize));
            return datagram;
        }
    }
}
