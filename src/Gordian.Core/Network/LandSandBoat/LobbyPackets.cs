// src/Gordian.Core/Network/LandSandBoat/LobbyPackets.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Gordian.Core.Network.LandSandBoat
{
    /// <summary>
    /// Lobby (xi_view, TCP 54001) commands. Every view packet starts with the 28-byte header: u32 packet size, the
    /// "IXFF" terminator, u32 command, 16-byte identifier.
    /// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets, lobby/Header.md and the
    /// per-packet pages) and LandSandBoat (https://github.com/LandSandBoat/server, src/login/view_session.cpp).
    /// </summary>
    public static class LobbyCommand
    {
        public const byte ResponseOk = 0x03;
        public const byte ResponseError = 0x04;
        public const byte ResponseKey = 0x05;
        public const byte RequestSelectChr = 0x07;
        public const byte ResponseNextLogin = 0x0B;
        public const byte RequestDeleteChr = 0x14;
        public const byte RequestGetChr = 0x1F;
        public const byte ResponseChrInfo2 = 0x20;
        public const byte RequestCreateChr = 0x21;
        public const byte RequestCreateChrPre = 0x22;
        public const byte ResponseWorldList = 0x23;
        public const byte RequestQueryWorldList = 0x24;
        public const byte RequestLobbyLogin = 0x26;
        public const byte RequestRenameChr = 0x28;
    }

    /// <summary>
    /// LandSandBoat's data channel (xi_data, TCP 54230) commands. Not part of the retail protocol: they stand in for
    /// what PlayOnline does for the retail client (LandSandBoat <c>src/login/data_session.cpp</c>; xiloader
    /// <c>src/network.cpp</c> drives the same channel). The first byte is the command; there is no header.
    /// </summary>
    public static class LobbyDataCommand
    {
        /// <summary>Server: "send your account id" (5 bytes), sent after a view <c>0x1F</c> request.</summary>
        public const byte RequestAccount = 0x01;
        /// <summary>Server: "send the session key" (5 bytes), sent after a view <c>0x07</c> select.</summary>
        public const byte RequestKey = 0x02;
        /// <summary>Server: the account's character id list (0x148 bytes; count at byte 1, 16-byte entries from 16).</summary>
        public const byte CharacterIdList = 0x03;
        /// <summary>Client: account id, search server address and session hash (28 bytes).</summary>
        public const byte Account = 0xA1;
        /// <summary>Client: the 20-byte session key (28 bytes).</summary>
        public const byte Key = 0xA2;
        /// <summary>Client: session hash announcement (28 bytes), the first packet on the channel.</summary>
        public const byte SessionHash = 0xFE;
    }

    /// <summary>
    /// One content id (character slot) of the lobby character list (S2C 0x20 <c>lpkt_chr_info_sub2</c>): the slot's
    /// ids and status, and the character's look and progress (<c>TC_OPERATION_MAKE</c>).
    /// </summary>
    /// <param name="Slot">1-based position in the list, free slots counted (the select screen's slot).</param>
    /// <param name="ContentId"><c>ffxi_id</c>: the content id. LandSandBoat uses the character id here.</param>
    /// <param name="ServerId">The in-game server id: <c>ffxi_id_world | ffxi_id_world_tbl &lt;&lt; 16</c>.</param>
    /// <param name="Face">The combined face (0-15: face 1A..8B), as the world packets carry it in the model id's low byte.</param>
    /// <param name="Equipment">Model ids head, body, hands, legs, feet, main, sub with the slot in the top nibble
    /// (0x1000 head ... 0x7000 sub), as the world packets' GrapIDTbl[1..7].</param>
    /// <param name="JobLevels">The job levels of <c>job_lev</c> (index = job id 1-15; slot 0 is unused).</param>
    public sealed record LobbyCharacter(
        int Slot,
        uint ContentId,
        uint ServerId,
        ushort WorldId,
        ushort Status,
        bool RenameRequired,
        bool RaceChangeAvailable,
        string Name,
        string WorldName,
        byte Race,
        byte MainJob,
        byte SubJob,
        byte MainJobLevel,
        byte Face,
        byte Nation,
        byte Size,
        ushort ZoneId,
        LobbyEquipment Equipment,
        LobbyJobLevels JobLevels)
    {
        /// <summary>Status 1: playable, or free to create a character in.</summary>
        public const ushort StatusAvailable = 1;

        /// <summary>A free content id: LandSandBoat fills those with a one-space name; retail sends an empty one.</summary>
        public bool IsEmpty => string.IsNullOrWhiteSpace(Name);

        /// <summary>The world packets' GrapIDTbl[0] for this character: <c>(race &lt;&lt; 8) | face</c>.</summary>
        public ushort FaceModel => (ushort)((Race << 8) | Face);
    }

    /// <summary>A character's seven model ids (head .. sub) in the lobby list, kept inline (no heap array per character).</summary>
    [System.Runtime.CompilerServices.InlineArray(Count)]
    public struct LobbyEquipment
    {
        public const int Count = 7;
        private ushort _element0;

        public static LobbyEquipment From(ReadOnlySpan<ushort> models)
        {
            var equipment = new LobbyEquipment();
            for (int i = 0; i < Count && i < models.Length; i++) equipment[i] = models[i];
            return equipment;
        }
    }

    /// <summary>The 16 <c>job_lev</c> bytes of a lobby list entry, kept inline.</summary>
    [System.Runtime.CompilerServices.InlineArray(Count)]
    public struct LobbyJobLevels
    {
        public const int Count = 16;
        private byte _element0;
    }

    /// <summary>One world of the lobby world list (S2C 0x23 <c>lpkt_world_name</c>).</summary>
    public readonly record struct LobbyWorld(uint Number, string Name);

    /// <summary>
    /// What the lobby needs to create a character (C2S 0x21 <c>character_info</c>, the populated fields of
    /// <c>TC_OPERATION_MAKE</c>).
    /// </summary>
    /// <param name="Race">1 Hume male ... 8 Galka.</param>
    /// <param name="Face">Face 0-7 (1..8).</param>
    /// <param name="Hair">Hair colour 0 (A) or 1 (B).</param>
    /// <param name="MainJob">1-6 (Warrior ... Thief).</param>
    /// <param name="Size">0 small, 1 medium, 2 large.</param>
    /// <param name="Nation">0 San d'Oria, 1 Bastok, 2 Windurst.</param>
    public readonly record struct LobbyCharacterCreation(string Name, byte Race, byte Face, byte Hair, byte MainJob, byte Size, byte Nation)
    {
        /// <summary>The combined face id (0-15) the world packets and LandSandBoat use: <c>face * 2 + hair</c>.</summary>
        public byte CombinedFace => (byte)((Face << 1) | (Hair & 1));

        /// <summary>The client-computed GrapIDTbl[0]: <c>hair | 2 * (face | race &lt;&lt; 7)</c> (XiPackets CharacterInfo.md).</summary>
        public ushort FaceModel => (ushort)((Hair & 1) | (2 * (Face | (Race << 7))));
    }

    /// <summary>
    /// The lobby's S2C 0x0B: where the selected character's map server is. The addresses are the IPv4 bytes as sent,
    /// read as a little-endian u32 (127.0.0.1 = 0x0100007F), which is what <see cref="System.Net.IPAddress(long)"/> takes.
    /// </summary>
    public readonly record struct LobbyNextLogin(uint ContentId, uint ServerId, string Name, uint ServerIndex,
        uint ServerAddress, uint ServerPort, uint CacheAddress, uint CachePort);

    /// <summary>
    /// Builders and parsers for the lobby packets. Builders write into caller-provided buffers sized by the
    /// <c>*Size</c> constants; parsers read spans. Layouts referenced from XiPackets
    /// (https://github.com/atom0s/XiPackets, lobby/) and LandSandBoat (https://github.com/LandSandBoat/server,
    /// src/login/login_packets.h, view_session.cpp, data_session.cpp, login_helpers.cpp).
    /// <para>
    /// <b>Differs from XiPackets:</b> the header identifier is the MD5 of the packet on retail; LandSandBoat instead
    /// looks the session up by those 16 bytes (<c>loginHelpers::getHashFromPacket</c> reads offset 12), so every
    /// request carries the session hash there. The <c>passwd</c> fields (an MD5 of PlayOnline's session password) are
    /// left zero: LandSandBoat does not read them.
    /// </para>
    /// </summary>
    public static class LobbyPackets
    {
        /// <summary>"IXFF" read as a little-endian u32.</summary>
        public const uint Terminator = 0x46465849;
        public const int HeaderSize = 28;
        public const int SessionHashLength = 16;
        public const int NameLength = 16;

        public const int LobbyLoginSize = 0x98;
        public const int GetCharactersSize = 0x2C;
        public const int QueryWorldListSize = 0x2C;
        public const int SelectCharacterSize = 0x58;
        public const int DeleteCharacterSize = 0x34;
        public const int CreateCharacterPreSize = 0x60;
        public const int CreateCharacterSize = 0x90;
        public const int RenameCharacterSize = 0x44;

        /// <summary>The data channel packets the client sends (0xFE, 0xA1, 0xA2) are 28 bytes.</summary>
        public const int DataPacketSize = 28;
        /// <summary>The data channel's character id list (0x03).</summary>
        public const int DataCharacterListSize = 0x148;
        /// <summary>The data channel's 0x01 / 0x02 prompts.</summary>
        public const int DataPromptSize = 5;

        /// <summary>One <c>lpkt_chr_info_sub2</c> entry of the 0x20 list, and where the entries start.</summary>
        public const int CharacterEntrySize = 140, CharacterEntriesOffset = 32;
        /// <summary>One <c>lpkt_world_name</c> entry of the 0x23 list, and where the entries start.</summary>
        public const int WorldEntrySize = 20, WorldEntriesOffset = 32;
        /// <summary>The <c>TC_OPERATION_MAKE</c> block's size and its offset inside a 0x20 entry and a 0x21 request.</summary>
        public const int CharacterInfoSize = 96, CharacterInfoInEntry = 44, CharacterInfoInCreate = 48;

        /// <summary>The <c>unknown0000</c> value retail always sends in 0x07 (XiPackets RequestSelectChr.md).</summary>
        public const uint SelectChecksumIndex = 3;

        /// <summary>
        /// Every expansion bit (base game through Seekers of Adoulin, bits 0-11) for <c>excode_client</c>: the expansions
        /// this client can show. LandSandBoat does not read the field.
        /// </summary>
        public const uint AllExpansions = 0x0FFF;

        /// <summary>Writes the 28-byte header: size, terminator, command and the session hash as the identifier.</summary>
        public static void WriteHeader(Span<byte> packet, byte command, ReadOnlySpan<byte> sessionHash)
        {
            packet.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(packet, (uint)packet.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[4..], Terminator);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[8..], command);
            CopyHash(sessionHash, packet.Slice(12, SessionHashLength));
        }

        /// <summary>C2S 0x26 RequestLobbyLogin (0x98): the client version at 0x74 and the installed expansions at 0x84.</summary>
        public static void WriteLobbyLogin(Span<byte> packet, ReadOnlySpan<byte> sessionHash, string clientVersion, uint expansions = AllExpansions)
        {
            packet = packet[..LobbyLoginSize];
            WriteHeader(packet, LobbyCommand.RequestLobbyLogin, sessionHash);
            WriteAscii(packet.Slice(0x74, 16), clientVersion);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[0x84..], expansions);
        }

        /// <summary>C2S 0x1F RequestGetChr (0x2C).</summary>
        public static void WriteGetCharacters(Span<byte> packet, ReadOnlySpan<byte> sessionHash) =>
            WriteHeader(packet[..GetCharactersSize], LobbyCommand.RequestGetChr, sessionHash);

        /// <summary>C2S 0x24 RequestQueryWorldList (0x2C).</summary>
        public static void WriteQueryWorldList(Span<byte> packet, ReadOnlySpan<byte> sessionHash) =>
            WriteHeader(packet[..QueryWorldListSize], LobbyCommand.RequestQueryWorldList, sessionHash);

        /// <summary>C2S 0x07 RequestSelectChr (0x58): content id, server id, name; the checksum index 3 at 0x44.</summary>
        public static void WriteSelectCharacter(Span<byte> packet, ReadOnlySpan<byte> sessionHash, uint contentId, uint serverId, string name)
        {
            packet = packet[..SelectCharacterSize];
            WriteHeader(packet, LobbyCommand.RequestSelectChr, sessionHash);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[28..], contentId);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[32..], serverId);
            WriteAscii(packet.Slice(36, NameLength - 1), name);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[0x44..], SelectChecksumIndex);
        }

        /// <summary>C2S 0x14 RequestDeleteChr (0x34): content id and server id.</summary>
        public static void WriteDeleteCharacter(Span<byte> packet, ReadOnlySpan<byte> sessionHash, uint contentId, uint serverId)
        {
            packet = packet[..DeleteCharacterSize];
            WriteHeader(packet, LobbyCommand.RequestDeleteChr, sessionHash);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[28..], contentId);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[32..], serverId);
        }

        /// <summary>C2S 0x22 RequestCreateChrPre (0x60): the free slot's content id, the name and the world name.</summary>
        public static void WriteCreateCharacterPre(Span<byte> packet, ReadOnlySpan<byte> sessionHash, uint contentId, string name, string worldName)
        {
            packet = packet[..CreateCharacterPreSize];
            WriteHeader(packet, LobbyCommand.RequestCreateChrPre, sessionHash);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[28..], contentId);
            WriteAscii(packet.Slice(32, NameLength - 1), name);
            WriteAscii(packet.Slice(64, NameLength - 1), worldName);
        }

        /// <summary>
        /// C2S 0x21 RequestCreateChr (0x90): the free slot's content id and the populated <c>TC_OPERATION_MAKE</c>
        /// fields (race, job, face, nation, hair, size, GrapIDTbl[0], main job level 1). LandSandBoat reads race at 48,
        /// job at 50, nation at 54, size at 57 and the combined face from GrapIDTbl[0]'s low byte at 60.
        /// </summary>
        public static void WriteCreateCharacter(Span<byte> packet, ReadOnlySpan<byte> sessionHash, uint contentId, in LobbyCharacterCreation creation)
        {
            packet = packet[..CreateCharacterSize];
            WriteHeader(packet, LobbyCommand.RequestCreateChr, sessionHash);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[28..], contentId);
            var info = packet.Slice(CharacterInfoInCreate, CharacterInfoSize);
            BinaryPrimitives.WriteUInt16LittleEndian(info, creation.Race);
            info[2] = creation.MainJob;
            BinaryPrimitives.WriteUInt16LittleEndian(info[4..], creation.Face);
            info[6] = creation.Nation;
            info[8] = creation.Hair;
            info[9] = creation.Size;
            BinaryPrimitives.WriteUInt16LittleEndian(info[12..], creation.FaceModel);
            info[29] = 1; // mjob_level
        }

        /// <summary>C2S 0x28 RequestRenameChr (0x44): content id, server id and the new name.</summary>
        public static void WriteRenameCharacter(Span<byte> packet, ReadOnlySpan<byte> sessionHash, uint contentId, uint serverId, string newName)
        {
            packet = packet[..RenameCharacterSize];
            WriteHeader(packet, LobbyCommand.RequestRenameChr, sessionHash);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[28..], contentId);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[32..], serverId);
            WriteAscii(packet.Slice(36, NameLength - 1), newName);
        }

        /// <summary>Data channel 0xFE: the session hash at 12 (28 bytes).</summary>
        public static void WriteDataSessionHash(Span<byte> packet, ReadOnlySpan<byte> sessionHash)
        {
            packet = packet[..DataPacketSize];
            packet.Clear();
            packet[0] = LobbyDataCommand.SessionHash;
            CopyHash(sessionHash, packet.Slice(12, SessionHashLength));
        }

        /// <summary>Data channel 0xA1: account id at 1, search server address at 5, session hash at 12 (28 bytes).</summary>
        public static void WriteDataAccount(Span<byte> packet, uint accountId, uint searchServerAddress, ReadOnlySpan<byte> sessionHash)
        {
            packet = packet[..DataPacketSize];
            packet.Clear();
            packet[0] = LobbyDataCommand.Account;
            BinaryPrimitives.WriteUInt32LittleEndian(packet[1..], accountId);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[5..], searchServerAddress);
            CopyHash(sessionHash, packet.Slice(12, SessionHashLength));
        }

        /// <summary>Data channel 0xA2: the 20-byte session key at 1 and the character id at 21 (28 bytes).</summary>
        public static void WriteDataKey(Span<byte> packet, ReadOnlySpan<byte> key, uint characterId)
        {
            packet = packet[..DataPacketSize];
            packet.Clear();
            packet[0] = LobbyDataCommand.Key;
            key[..Math.Min(20, key.Length)].CopyTo(packet.Slice(1, 20));
            BinaryPrimitives.WriteUInt32LittleEndian(packet[21..], characterId);
        }

        /// <summary>The command of a view packet (offset 8), or 0 when the packet is too short or lacks the terminator.</summary>
        public static byte ReadCommand(ReadOnlySpan<byte> packet) =>
            packet.Length >= 12 && BinaryPrimitives.ReadUInt32LittleEndian(packet[4..]) == Terminator ? packet[8] : (byte)0;

        /// <summary>The declared size of a view packet (offset 0).</summary>
        public static uint ReadSize(ReadOnlySpan<byte> packet) => packet.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(packet) : 0;

        /// <summary>S2C 0x04 ResponseError: the error code at 32 (the client adds 3000 to show it).</summary>
        public static int ReadErrorCode(ReadOnlySpan<byte> packet) =>
            packet.Length >= 36 ? (int)BinaryPrimitives.ReadUInt32LittleEndian(packet[32..]) : packet.Length >= 34 ? BinaryPrimitives.ReadUInt16LittleEndian(packet[32..]) : 0;

        /// <summary>S2C 0x05 ResponseKey: the server's expansions (<c>excode_server</c>) and features (<c>excode_server2</c>).</summary>
        public static (uint Key, uint Expansions, uint Features) ReadKey(ReadOnlySpan<byte> packet) =>
            packet.Length >= 40
                ? (BinaryPrimitives.ReadUInt32LittleEndian(packet[28..]), BinaryPrimitives.ReadUInt32LittleEndian(packet[32..]), BinaryPrimitives.ReadUInt32LittleEndian(packet[36..]))
                : (0u, 0u, 0u);

        /// <summary>
        /// S2C 0x20 ResponseChrInfo2: every content id the packet lists (count at 28, at most 16), free slots included.
        /// Model ids are normalized to the world packets' convention: LandSandBoat sends the raw look values (no slot
        /// nibble) and only the combined face in GrapIDTbl[0], retail sends the full ids.
        /// <para><b>Differs from XiPackets:</b> LandSandBoat's <c>data_session.cpp</c> fills <c>face_no</c>,
        /// <c>hair_no</c> and GrapIDTbl[0] all with the combined face (0-15) and GrapIDTbl[1..7] without the 0x1000 ...
        /// 0x7000 slot bits; XiPackets has face_no 0-7, hair_no and GrapIDTbl[0] = <c>hair | 2 * (face | race &lt;&lt; 7)</c>.</para>
        /// </summary>
        public static List<LobbyCharacter> ParseCharacterList(ReadOnlySpan<byte> packet)
        {
            var characters = new List<LobbyCharacter>();
            if (packet.Length < CharacterEntriesOffset) return characters;
            int count = (int)Math.Min(16u, BinaryPrimitives.ReadUInt32LittleEndian(packet[28..]));
            for (int i = 0; i < count; i++)
            {
                int at = CharacterEntriesOffset + i * CharacterEntrySize;
                if (at + CharacterEntrySize > packet.Length) break;
                characters.Add(ParseCharacterEntry(packet.Slice(at, CharacterEntrySize), i + 1));
            }
            return characters;
        }

        private static LobbyCharacter ParseCharacterEntry(ReadOnlySpan<byte> entry, int slot)
        {
            uint contentId = BinaryPrimitives.ReadUInt32LittleEndian(entry);
            uint serverId = BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]) | ((uint)entry[11] << 16);
            ushort worldId = BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]);
            ushort status = BinaryPrimitives.ReadUInt16LittleEndian(entry[8..]);
            byte flags = entry[10];
            string name = ReadAscii(entry.Slice(12, NameLength));
            string world = ReadAscii(entry.Slice(28, NameLength));

            var info = entry.Slice(CharacterInfoInEntry, CharacterInfoSize);
            byte race = (byte)BinaryPrimitives.ReadUInt16LittleEndian(info);
            ushort grap0 = BinaryPrimitives.ReadUInt16LittleEndian(info[12..]);
            // Retail: GrapIDTbl[0] = (race << 8) | combined face. LandSandBoat: the combined face only (also in face_no).
            byte face = grap0 >= 0x100 ? (byte)(grap0 & 0xFF) : (byte)(grap0 != 0 ? grap0 : BinaryPrimitives.ReadUInt16LittleEndian(info[4..]));
            var equipment = new LobbyEquipment();
            for (int s = 0; s < LobbyEquipment.Count; s++)
            {
                ushort model = BinaryPrimitives.ReadUInt16LittleEndian(info[(14 + s * 2)..]);
                equipment[s] = model < 0x1000 ? (ushort)(model | ((s + 1) << 12)) : model;
            }
            ushort zone = (ushort)(info[28] | ((info[35] & 1) << 8));
            var jobLevels = new LobbyJobLevels();
            info.Slice(56, LobbyJobLevels.Count).CopyTo(jobLevels);

            return new LobbyCharacter(slot, contentId, serverId, worldId, status,
                RenameRequired: (flags & 0x01) != 0, RaceChangeAvailable: (flags & 0x02) != 0,
                name, world, race, MainJob: info[2], SubJob: info[3], MainJobLevel: info[29], face, Nation: info[6], Size: info[9],
                zone, equipment, jobLevels);
        }

        /// <summary>S2C 0x23 ResponseWorldList: the worlds (count at 28, 20-byte entries from 32).</summary>
        public static List<LobbyWorld> ParseWorldList(ReadOnlySpan<byte> packet)
        {
            var worlds = new List<LobbyWorld>();
            if (packet.Length < WorldEntriesOffset) return worlds;
            int count = (int)Math.Min(64u, BinaryPrimitives.ReadUInt32LittleEndian(packet[28..]));
            for (int i = 0; i < count; i++)
            {
                int at = WorldEntriesOffset + i * WorldEntrySize;
                if (at + WorldEntrySize > packet.Length) break;
                worlds.Add(new LobbyWorld(BinaryPrimitives.ReadUInt32LittleEndian(packet[at..]), ReadAscii(packet.Slice(at + 4, NameLength))));
            }
            return worlds;
        }

        /// <summary>S2C 0x0B ResponseNextLogin (0x48): the map server and search (cache) server of the selected character.</summary>
        public static bool TryParseNextLogin(ReadOnlySpan<byte> packet, out LobbyNextLogin next)
        {
            next = default;
            if (packet.Length < 0x48 || ReadCommand(packet) != LobbyCommand.ResponseNextLogin) return false;
            next = new LobbyNextLogin(
                BinaryPrimitives.ReadUInt32LittleEndian(packet[28..]),
                BinaryPrimitives.ReadUInt32LittleEndian(packet[32..]),
                ReadAscii(packet.Slice(36, NameLength)),
                BinaryPrimitives.ReadUInt32LittleEndian(packet[52..]),
                BinaryPrimitives.ReadUInt32LittleEndian(packet[56..]),
                BinaryPrimitives.ReadUInt32LittleEndian(packet[60..]),
                BinaryPrimitives.ReadUInt32LittleEndian(packet[64..]),
                BinaryPrimitives.ReadUInt32LittleEndian(packet[68..]));
            return true;
        }

        /// <summary>How long a data channel message is, from its first byte (0 when unknown).</summary>
        public static int DataMessageLength(byte first) => first switch
        {
            LobbyDataCommand.RequestAccount or LobbyDataCommand.RequestKey => DataPromptSize,
            LobbyDataCommand.CharacterIdList => DataCharacterListSize,
            0x24 => 0x24, // a ResponseError written to the data socket (its size byte comes first)
            _ => 0,
        };

        private static void CopyHash(ReadOnlySpan<byte> sessionHash, Span<byte> destination)
        {
            sessionHash[..Math.Min(SessionHashLength, sessionHash.Length)].CopyTo(destination);
        }

        private static void WriteAscii(Span<byte> destination, string? text)
        {
            if (string.IsNullOrEmpty(text)) return;
            int length = Math.Min(destination.Length, text.Length);
            for (int i = 0; i < length; i++)
            {
                char c = text[i];
                destination[i] = c < 0x80 ? (byte)c : (byte)'?';
            }
        }

        /// <summary>A NUL-terminated ASCII field, trailing spaces trimmed (LandSandBoat marks a free slot with one space).</summary>
        public static string ReadAscii(ReadOnlySpan<byte> field)
        {
            int end = field.IndexOf((byte)0);
            if (end < 0) end = field.Length;
            return Encoding.ASCII.GetString(field[..end]).TrimEnd(' ');
        }
    }
}
