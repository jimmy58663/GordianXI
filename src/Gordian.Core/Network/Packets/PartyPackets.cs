// src/Gordian.Core/Network/Packets/PartyPackets.cs
using System;
using System.Buffers.Binary;
using System.Text;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// Party or Alliance structural category indicator.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/enums/party_kind.h).
    /// </summary>
    public enum PartyKind : byte
    {
        Party = 0,
        Alliance = 1
    }

    /// <summary>
    /// S2C 0x0DC (GP_SERV_COMMAND_GROUP_SOLICIT_REQ): Inbound Party or Alliance Invitation.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x0dc_group_solicit_req.h).
    /// </summary>
    public readonly ref struct S2C_0x0DC_GroupSolicitReq
    {
        public const ushort PacketId = 0x0DC;

        public uint UniqueNo { get; }
        public ushort ActIndex { get; }
        public byte AnonFlag { get; }
        public PartyKind Kind { get; }
        public ushort RaceNo { get; }
        public bool IsValid { get; }

        private readonly ReadOnlySpan<byte> _payload;

        public S2C_0x0DC_GroupSolicitReq(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < 28)
            {
                UniqueNo = 0;
                ActIndex = 0;
                AnonFlag = 0;
                Kind = PartyKind.Party;
                RaceNo = 0;
                IsValid = false;
                return;
            }

            UniqueNo = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            ActIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
            AnonFlag = payload[6];
            Kind = (PartyKind)payload[7];
            RaceNo = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(24, 2));
            IsValid = true;
        }

        /// <summary>
        /// Gets the inviter's character name.
        /// </summary>
        public string GetInviterName()
        {
            if (!IsValid || _payload.Length < 24) return string.Empty;
            var nameSlice = _payload.Slice(8, 16);
            int nullIdx = nameSlice.IndexOf((byte)0);
            if (nullIdx >= 0) nameSlice = nameSlice.Slice(0, nullIdx);
            return Encoding.ASCII.GetString(nameSlice);
        }
    }

    /// <summary>
    /// S2C 0x0DE (GP_SERV_COMMAND_GROUP_SOLICIT_NO): Party invite cancelled, rejected, or cleared.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x0de_group_solicit_no.h).
    /// </summary>
    public readonly ref struct S2C_0x0DE_GroupSolicitNo
    {
        public const ushort PacketId = 0x0DE;

        public byte Reason { get; }
        public bool IsValid { get; }

        public S2C_0x0DE_GroupSolicitNo(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 1)
            {
                Reason = 0;
                IsValid = false;
                return;
            }

            Reason = payload[0];
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x0C8 (GP_SERV_COMMAND_GROUP_TBL): Party &amp; Alliance Member Table.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x0c8_group_tbl.h).
    /// </summary>
    public readonly ref struct S2C_0x0C8_GroupTbl
    {
        public const ushort PacketId = 0x0C8;
        public const int EntrySize = 12;

        public PartyKind Kind { get; }
        public int EntryCount { get; }
        public bool IsValid { get; }

        private readonly ReadOnlySpan<byte> _payload;

        public S2C_0x0C8_GroupTbl(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < 4)
            {
                Kind = PartyKind.Party;
                EntryCount = 0;
                IsValid = false;
                return;
            }

            Kind = (PartyKind)payload[0];
            EntryCount = Math.Min(20, (payload.Length - 4) / EntrySize);
            IsValid = true;
        }

        public uint GetEntryUniqueNo(int index)
        {
            if (index < 0 || index >= EntryCount) return 0;
            return BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(4 + (index * EntrySize), 4));
        }

        public ushort GetEntryActIndex(int index)
        {
            if (index < 0 || index >= EntryCount) return 0;
            return BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(4 + (index * EntrySize) + 4, 2));
        }

        public byte GetEntryFlags(int index)
        {
            if (index < 0 || index >= EntryCount) return 0;
            return _payload[4 + (index * EntrySize) + 6];
        }

        public bool IsEntryLeader(int index)
        {
            return (GetEntryFlags(index) & 0x04) != 0; // bit 2: PartyLeaderFlg
        }

        /// <summary>The alliance party the entry belongs to (bits 0-1: PartyNo, 0-2).</summary>
        public byte GetEntryPartyNumber(int index) => (byte)(GetEntryFlags(index) & 0x03);

        /// <summary>Bit 3: AllianceLeaderFlg.</summary>
        public bool IsEntryAllianceLeader(int index) => (GetEntryFlags(index) & 0x08) != 0;

        public ushort GetEntryZoneNo(int index)
        {
            if (index < 0 || index >= EntryCount) return 0;
            return BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(4 + (index * EntrySize) + 8, 2));
        }
    }

    /// <summary>
    /// S2C 0x0DD (GP_SERV_COMMAND_GROUP_LIST): Detailed Party Member Information.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x0dd_group_list.h).
    /// </summary>
    public readonly ref struct S2C_0x0DD_GroupList
    {
        public const ushort PacketId = 0x0DD;

        public uint UniqueNo { get; }
        public uint Hp { get; }
        public uint Mp { get; }
        public uint Tp { get; }
        public uint GAttr { get; }
        public ushort ActIndex { get; }
        public byte MemberNumber { get; }
        public byte MoghouseFlg { get; }
        public byte Kind { get; }
        public byte Hpp { get; }
        public byte Mpp { get; }
        public ushort ZoneNo { get; }
        public JobId MainJob { get; }
        public byte MainJobLevel { get; }
        public JobId SubJob { get; }
        public byte SubJobLevel { get; }
        public byte MasterJobLevel { get; }
        public byte MasterJobFlags { get; }
        public bool IsValid { get; }

        public bool IsPartyLeader => (GAttr & 0x04) != 0;

        /// <summary>The alliance party the member belongs to (GAttr bits 0-1: PartyNo, 0-2).</summary>
        public byte PartyNumber => (byte)(GAttr & 0x03);

        /// <summary>GAttr bit 3: AllianceLeaderFlg.</summary>
        public bool IsAllianceLeader => (GAttr & 0x08) != 0;

        private readonly ReadOnlySpan<byte> _payload;

        public S2C_0x0DD_GroupList(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < 52)
            {
                UniqueNo = 0;
                Hp = 0;
                Mp = 0;
                Tp = 0;
                GAttr = 0;
                ActIndex = 0;
                MemberNumber = 0;
                MoghouseFlg = 0;
                Kind = 0;
                Hpp = 0;
                Mpp = 0;
                ZoneNo = 0;
                MainJob = JobId.None;
                MainJobLevel = 0;
                SubJob = JobId.None;
                SubJobLevel = 0;
                MasterJobLevel = 0;
                MasterJobFlags = 0;
                IsValid = false;
                return;
            }

            UniqueNo = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            Hp = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
            Mp = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(8, 4));
            Tp = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(12, 4));
            GAttr = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(16, 4));
            ActIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(20, 2));
            MemberNumber = payload[22];
            MoghouseFlg = payload[23];
            Kind = payload[24];
            Hpp = payload[25];
            Mpp = payload[26];
            ZoneNo = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(28, 2));
            MainJob = (JobId)payload[30];
            MainJobLevel = payload[31];
            SubJob = (JobId)payload[32];
            SubJobLevel = payload[33];
            MasterJobLevel = payload[34];
            MasterJobFlags = payload[35];
            IsValid = true;
        }

        public string GetName()
        {
            if (!IsValid || _payload.Length < 52) return string.Empty;
            var nameSlice = _payload.Slice(36, Math.Min(16, _payload.Length - 36));
            int nullIdx = nameSlice.IndexOf((byte)0);
            if (nullIdx >= 0) nameSlice = nameSlice.Slice(0, nullIdx);
            return Encoding.ASCII.GetString(nameSlice);
        }
    }

    /// <summary>
    /// Builder for C2S Party &amp; Alliance packets.
    /// Protocol specifications referenced from LandSandBoat (https://github.com/LandSandBoat/server/tree/base/src/map/packets/c2s).
    /// </summary>
    public static class PartyPacketBuilder
    {
        /// <summary>
        /// Builds C2S 0x06E (GP_CLI_COMMAND_GROUP_SOLICIT_REQ): Send Party or Alliance Invite.
        /// </summary>
        public static byte[] BuildGroupSolicitReq(uint targetServerId, ushort targetIndex, PartyKind kind = PartyKind.Party, ushort sequenceId = 0)
        {
            // Total size: 12 bytes (Header: 4, Payload: 8)
            var packet = new byte[12];
            ushort headerWord = (ushort)(0x06E | (3 << 9)); // 3 * 4 = 12 bytes
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);

            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), targetServerId);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(8, 2), targetIndex);
            packet[10] = (byte)kind;
            packet[11] = 0; // padding

            return packet;
        }

        /// <summary>
        /// Builds C2S 0x074 (GP_CLI_COMMAND_GROUP_SOLICIT_RES): Respond to Party or Alliance Invite.
        /// Res: 1 = Accept, 0 = Decline.
        /// </summary>
        public static byte[] BuildGroupSolicitRes(bool accept, ushort sequenceId = 0)
        {
            // Total size: 8 bytes (Header: 4, Payload: 4)
            var packet = new byte[8];
            ushort headerWord = (ushort)(0x074 | (2 << 9)); // 2 * 4 = 8 bytes
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);

            packet[4] = accept ? (byte)1 : (byte)0;
            packet[5] = 0;
            packet[6] = 0;
            packet[7] = 0;

            return packet;
        }

        /// <summary>
        /// Builds C2S 0x06F (GP_CLI_COMMAND_GROUP_LEAVE): Leave current Party or Alliance.
        /// </summary>
        public static byte[] BuildGroupLeave(PartyKind kind = PartyKind.Party, ushort sequenceId = 0)
        {
            // Total size: 8 bytes (Header: 4, Payload: 4)
            var packet = new byte[8];
            ushort headerWord = (ushort)(0x06F | (2 << 9)); // 2 * 4 = 8 bytes
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);

            packet[4] = (byte)kind;
            packet[5] = 0;
            packet[6] = 0;
            packet[7] = 0;

            return packet;
        }

        /// <summary>
        /// Builds C2S 0x070 (GP_CLI_COMMAND_GROUP_BREAKUP): Breakup / Disband current Party or Alliance.
        /// </summary>
        public static byte[] BuildGroupBreakup(PartyKind kind = PartyKind.Party, ushort sequenceId = 0)
        {
            // Total size: 8 bytes (Header: 4, Payload: 4)
            var packet = new byte[8];
            ushort headerWord = (ushort)(0x070 | (2 << 9)); // 2 * 4 = 8 bytes
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);

            packet[4] = (byte)kind;
            packet[5] = 0;
            packet[6] = 0;
            packet[7] = 0;

            return packet;
        }

        /// <summary>
        /// Builds C2S 0x071 (GP_CLI_COMMAND_GROUP_STRIKE): Kick / Strike member from Party or Alliance.
        /// </summary>
        public static byte[] BuildGroupStrike(uint targetServerId, ushort targetIndex, string name, byte kind = 0, ushort sequenceId = 0)
        {
            // Total size: 28 bytes (Header: 4, Payload: 24)
            var packet = new byte[28];
            ushort headerWord = (ushort)(0x071 | (7 << 9)); // 7 * 4 = 28 bytes
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);

            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), targetServerId);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(8, 2), targetIndex);
            packet[10] = kind;
            packet[11] = 0;

            if (!string.IsNullOrEmpty(name))
            {
                byte[] nameBytes = Encoding.ASCII.GetBytes(name);
                int copyLen = Math.Min(nameBytes.Length, 15);
                Array.Copy(nameBytes, 0, packet, 12, copyLen);
            }

            return packet;
        }

        /// <summary>
        /// Builds C2S 0x076 (GP_CLI_COMMAND_GROUP_LIST_REQ): Request updated party list from server.
        /// </summary>
        public static byte[] BuildGroupListReq(byte kind = 0, ushort sequenceId = 0)
        {
            var packet = new byte[8];
            ushort headerWord = (ushort)(0x076 | (2 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);
            packet[4] = kind;
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x077 (GP_CLI_COMMAND_GROUP_CHANGE2): Change group settings (leader, level sync, etc.).
        /// </summary>
        public static byte[] BuildGroupChange2(string name, byte kind, byte changeKind, ushort sequenceId = 0)
        {
            // Size: 24 bytes (Header: 4, Name: 16, Kind: 1, ChangeKind: 1, Pad: 2)
            var packet = new byte[24];
            ushort headerWord = (ushort)(0x077 | (6 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);

            if (!string.IsNullOrEmpty(name))
            {
                byte[] nameBytes = Encoding.ASCII.GetBytes(name);
                int copyLen = Math.Min(nameBytes.Length, 15);
                Array.Copy(nameBytes, 0, packet, 4, copyLen);
            }

            packet[20] = kind;
            packet[21] = changeKind;
            return packet;
        }

        /// <summary>
        /// Builds C2S 0x11C (GP_CLI_COMMAND_PARTY_REQUEST): Request to join player's party (/partyrequestcmd).
        /// </summary>
        public static byte[] BuildPartyRequest(uint targetServerId, ushort targetIndex, byte kind = 0, ushort sequenceId = 0)
        {
            // Size: 16 bytes (Header: 4, UniqueNo: 4, ActIndex: 2, Kind: 1, Pad: 5)
            var packet = new byte[16];
            ushort headerWord = (ushort)(0x11C | (4 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), sequenceId);

            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), targetServerId);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(8, 2), targetIndex);
            packet[10] = kind;
            return packet;
        }
    }

    /// <summary>
    /// S2C 0x0E0 (GP_SERV_COMMAND_GROUP_COMLINK): Equipped Linkshell slot status.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x0e0_group_comlink.h).
    /// </summary>
    public readonly ref struct S2C_0x0E0_GroupComlink
    {
        public const ushort PacketId = 0x0E0;

        public byte LinkshellNum { get; }
        public byte ItemIndex { get; }
        public byte Category { get; }
        public bool IsValid { get; }

        public S2C_0x0E0_GroupComlink(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 3)
            {
                LinkshellNum = 0;
                ItemIndex = 0;
                Category = 0;
                IsValid = false;
                return;
            }

            LinkshellNum = payload[0];
            ItemIndex = payload[1];
            Category = payload[2];
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x0E2 (GP_SERV_COMMAND_GROUP_LIST2): Secondary party member list structure.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x0e2_group_list2.h).
    /// </summary>
    public readonly ref struct S2C_0x0E2_GroupList2
    {
        public const ushort PacketId = 0x0E2;

        public uint UniqueNo { get; }
        public uint Hp { get; }
        public uint Mp { get; }
        public uint Tp { get; }
        public uint GAttr { get; }
        public ushort ActIndex { get; }
        public byte MemberNumber { get; }
        public byte MoghouseFlg { get; }
        public byte Kind { get; }
        public byte Hpp { get; }
        public byte Mpp { get; }
        public ushort ZoneNo { get; }
        public JobId MainJob { get; }
        public byte MainJobLevel { get; }
        public JobId SubJob { get; }
        public byte SubJobLevel { get; }
        public bool IsValid { get; }

        private readonly ReadOnlySpan<byte> _payload;

        public S2C_0x0E2_GroupList2(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
            if (payload.Length < 44)
            {
                UniqueNo = 0;
                Hp = 0;
                Mp = 0;
                Tp = 0;
                GAttr = 0;
                ActIndex = 0;
                MemberNumber = 0;
                MoghouseFlg = 0;
                Kind = 0;
                Hpp = 0;
                Mpp = 0;
                ZoneNo = 0;
                MainJob = JobId.None;
                MainJobLevel = 0;
                SubJob = JobId.None;
                SubJobLevel = 0;
                IsValid = false;
                return;
            }

            UniqueNo = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            Hp = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
            Mp = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(8, 4));
            Tp = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(12, 4));
            GAttr = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(16, 4));
            ActIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(20, 2));
            MemberNumber = payload[22];
            MoghouseFlg = payload[23];
            Kind = payload[24];
            Hpp = payload[25];
            Mpp = payload[26];
            ZoneNo = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(28, 2));
            MainJob = (JobId)payload[30];
            MainJobLevel = payload[31];
            SubJob = (JobId)payload[32];
            SubJobLevel = payload[33];
            IsValid = true;
        }

        public string GetName()
        {
            if (!IsValid || _payload.Length < 52) return string.Empty;
            var nameSlice = _payload.Slice(36, Math.Min(16, _payload.Length - 36));
            int nullIdx = nameSlice.IndexOf((byte)0);
            if (nullIdx >= 0) nameSlice = nameSlice.Slice(0, nullIdx);
            return Encoding.ASCII.GetString(nameSlice);
        }
    }

    /// <summary>
    /// S2C 0x11D (GP_SERV_COMMAND_PARTYREQ): Party seeker search response or join notification.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x11d_partyreq.h).
    /// </summary>
    public readonly ref struct S2C_0x11D_PartyReq
    {
        public const ushort PacketId = 0x11D;

        public uint UniqueNo { get; }
        public ushort ActIndex { get; }
        public byte Result { get; }
        public bool IsValid { get; }

        public S2C_0x11D_PartyReq(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 7)
            {
                UniqueNo = 0;
                ActIndex = 0;
                Result = 0;
                IsValid = false;
                return;
            }

            UniqueNo = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            ActIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
            Result = payload[6];
            IsValid = true;
        }
    }
}

