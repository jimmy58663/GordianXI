// src/Gordian.Core/Network/Lobby/LobbyClientPackets.cs
using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Gordian.Core.Network.LandSandBoat;
using Gordian.Core.Network.Retail;

namespace Gordian.Core.Network.Lobby
{
    /// <summary>
    /// The lobby's key modifier as the client keeps it: seeded by the server's <c>0x05</c>; each request that carries a
    /// session hash uses M0 + 1 + 2n, n being the number of such requests already sent (spec v2.1.19 section 5.5). With
    /// no character management that makes 0x1F +1, 0x24 +3, 0x07 +5. Retail checks it; LandSandBoat ignores the hashes.
    /// </summary>
    public sealed class LobbyKeyModifier
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
    /// The lobby requests (port 54001) as the legacy client (FFXiMain.dll) builds them, for retail and LandSandBoat alike:
    /// the header's +0x0C is the MD5 of the whole packet, and each request carries the session hash MD5(lobby key +
    /// LE32 modifier) in its body. On LandSandBoat xiloader overwrites the header's +0x0C with its session hash as the
    /// packet leaves (<c>XiloaderLobbyHandoff</c>), and the server ignores the body hashes, the AuthCode and the
    /// selection digest. Replies (the 0x20 character list, the 0x23 world list, 0x0B) are read with
    /// <see cref="LobbyPackets"/>.
    /// <para>
    /// Protocol referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v2.1.19, sections 4.5,
    /// 5.5 and 5.9 (test vectors 5 and 11); the shared layouts from LandSandBoat (src/login/view_session.cpp) and
    /// XiPackets (lobby/).
    /// </para>
    /// </summary>
    public static class LobbyClientPackets
    {
        /// <summary>The 0x26 <c>ClientCode</c> every captured (English) client sent.</summary>
        public const uint ClientCodeEnglish = 5;

        /// <summary>The 0x26 <c>AuthCode</c> field (+0x34..+0x73): the 52-byte token and 12 zero bytes.</summary>
        public const int AuthCodeFieldLength = 64;

        /// <summary>The constant at 0x07 +0x44, repeated in the selection digest.</summary>
        public const uint DllTableIndex = 3;

        /// <summary>The 0x26 expansion mask: every expansion installed.</summary>
        public const uint ExpansionMask = 0x00000FFF;

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

        /// <summary>Whether a packet's MD5 matches.</summary>
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
        /// C2S 0x26 (0x98): ClientCode 5 at +0x2C, the 52-byte AuthCode at +0x34 followed by 12 zero bytes, the client
        /// version at +0x74, 0xFF at +0x83, the expansion mask at +0x84.
        /// </summary>
        public static void WriteLobbyLogin(Span<byte> packet, ReadOnlySpan<byte> authCode, string clientVersion, uint expansions = ExpansionMask)
        {
            if (authCode.Length != PolAuthCode.TokenLength) throw new ArgumentException("The AuthCode token is 52 bytes.", nameof(authCode));
            packet = packet[..LobbyPackets.LobbyLoginSize];
            WriteHeader(packet, LobbyCommand.RequestLobbyLogin);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[0x2C..], ClientCodeEnglish);
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
        /// LE32(content id)). LandSandBoat reads only the content id and the name.
        /// </summary>
        public static void WriteSelectCharacter(Span<byte> packet, uint contentId, uint characterId, string name, ReadOnlySpan<byte> sessionHash,
            ReadOnlySpan<byte> authCodeField, uint clientChecksum)
        {
            if (authCodeField.Length != AuthCodeFieldLength) throw new ArgumentException("The AuthCode field is 64 bytes.", nameof(authCodeField));
            packet = packet[..LobbyPackets.SelectCharacterSize];
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
        /// C2S 0x22 (0x60) name check: content id, name, world name, the session hash at +0x30 and <c>00</c>, 14 x
        /// <c>FF</c>, <c>00</c> at +0x50 (spec section 4.5.10, verified against capture #5).
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
        /// C2S 0x21 (0x90) creation: content id, the session hash at +0x20 and the 96-byte character info at +0x30 (spec
        /// section 4.5.10; the info's field layout is XiPackets' and LandSandBoat's, not checked against retail).
        /// </summary>
        public static void WriteCreateCharacter(Span<byte> packet, uint contentId, in LobbyCharacterCreation creation, ReadOnlySpan<byte> sessionHash)
        {
            packet = packet[..LobbyPackets.CreateCharacterSize];
            LobbyPackets.WriteCreateCharacter(packet, default, contentId, creation);
            sessionHash[..16].CopyTo(packet[0x20..]);
            Seal(packet);
        }

        /// <summary>
        /// C2S 0x14 (0x34) deletion: content id, the character id's low 16 bits, the session hash at +0x24 (spec section
        /// 4.5.10, from XiPackets; not seen on a retail wire).
        /// </summary>
        public static void WriteDeleteCharacter(Span<byte> packet, uint contentId, uint characterId, ReadOnlySpan<byte> sessionHash)
        {
            packet = packet[..LobbyPackets.DeleteCharacterSize];
            LobbyPackets.WriteDeleteCharacter(packet, default, contentId, characterId & 0xFFFF);
            sessionHash[..16].CopyTo(packet[0x24..]);
            Seal(packet);
        }

        /// <summary>
        /// C2S 0x28 (0x44) rename: content id, the character id's low 16 bits, the new name, the session hash at +0x34
        /// (spec section 4.5.10, from XiPackets; not seen on a retail wire).
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
    }
}
