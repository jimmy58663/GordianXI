// tests/Gordian.Core.Tests/Network/PartyPacketTests.cs
using System;
using System.Linq;
using System.Buffers.Binary;
using System.Text;
using System.Threading.Tasks;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    public class PartyPacketTests
    {
        [Fact]
        public void S2C_0x0DC_GroupSolicitReq_DecodesPartyInvite()
        {
            byte[] payload = new byte[32];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x01020304); // UniqueNo
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 0x0042);     // ActIndex
            payload[6] = 0; // AnonFlag
            payload[7] = (byte)PartyKind.Party; // Kind

            byte[] nameBytes = Encoding.ASCII.GetBytes("Tarudrake");
            nameBytes.CopyTo(payload.AsSpan(8)); // sName (16 bytes)

            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(24, 2), 1); // RaceNo

            var req = new S2C_0x0DC_GroupSolicitReq(payload);

            Assert.True(req.IsValid);
            Assert.Equal(0x01020304u, req.UniqueNo);
            Assert.Equal(0x0042, req.ActIndex);
            Assert.Equal(PartyKind.Party, req.Kind);
            Assert.Equal(1, req.RaceNo);
            Assert.Equal("Tarudrake", req.GetInviterName());
        }

        [Fact]
        public void S2C_0x0DE_GroupSolicitNo_DecodesInviteReset()
        {
            byte[] payload = new byte[4] { 0x02, 0, 0, 0 };
            var no = new S2C_0x0DE_GroupSolicitNo(payload);

            Assert.True(no.IsValid);
            Assert.Equal(2, no.Reason);
        }

        [Fact]
        public void S2C_0x0C8_GroupTbl_DecodesMemberList()
        {
            byte[] payload = new byte[244];
            payload[0] = (byte)PartyKind.Party;

            // Member 0: Leader
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 1001);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8, 2), 10);
            payload[10] = 0x04; // PartyLeaderFlg (bit 2)
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(12, 2), 240); // Zone 240

            // Member 1: Member
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), 1002);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(20, 2), 11);
            payload[22] = 0x00;
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(24, 2), 240);

            var tbl = new S2C_0x0C8_GroupTbl(payload);

            Assert.True(tbl.IsValid);
            Assert.Equal(PartyKind.Party, tbl.Kind);
            Assert.Equal(20, tbl.EntryCount);
            Assert.Equal(1001u, tbl.GetEntryUniqueNo(0));
            Assert.Equal(10, tbl.GetEntryActIndex(0));
            Assert.True(tbl.IsEntryLeader(0));
            Assert.Equal(240, tbl.GetEntryZoneNo(0));

            Assert.Equal(1002u, tbl.GetEntryUniqueNo(1));
            Assert.Equal(11, tbl.GetEntryActIndex(1));
            Assert.False(tbl.IsEntryLeader(1));
        }

        [Fact]
        public void S2C_0x0DD_GroupList_DecodesDetailedMemberInfo()
        {
            byte[] payload = new byte[64];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 1001); // UniqueNo
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 950);  // Hp
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 420);  // Mp
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 1000); // Tp
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), 0x04); // GAttr (Leader bit)
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(20, 2), 10);  // ActIndex
            payload[22] = 0; // MemberNumber
            payload[23] = 0; // MoghouseFlg
            payload[24] = 0; // Kind
            payload[25] = 100; // Hpp
            payload[26] = 95;  // Mpp
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(28, 2), 240); // ZoneNo
            payload[30] = (byte)JobId.Warrior; // MainJob
            payload[31] = 75; // MainJobLevel
            payload[32] = (byte)JobId.Ninja;   // SubJob
            payload[33] = 37; // SubJobLevel

            byte[] nameBytes = Encoding.ASCII.GetBytes("Tarudrake");
            nameBytes.CopyTo(payload.AsSpan(36));

            var list = new S2C_0x0DD_GroupList(payload);

            Assert.True(list.IsValid);
            Assert.Equal(1001u, list.UniqueNo);
            Assert.Equal(950u, list.Hp);
            Assert.Equal(420u, list.Mp);
            Assert.Equal(1000u, list.Tp);
            Assert.True(list.IsPartyLeader);
            Assert.Equal(10, list.ActIndex);
            Assert.Equal(100, list.Hpp);
            Assert.Equal(95, list.Mpp);
            Assert.Equal(240, list.ZoneNo);
            Assert.Equal(JobId.Warrior, list.MainJob);
            Assert.Equal(75, list.MainJobLevel);
            Assert.Equal(JobId.Ninja, list.SubJob);
            Assert.Equal(37, list.SubJobLevel);
            Assert.Equal("Tarudrake", list.GetName());
        }

        [Fact]
        public void PartyPacketBuilder_BuildGroupSolicitReq_CreatesValidPacket()
        {
            byte[] packet = PartyPacketBuilder.BuildGroupSolicitReq(12345, 42, PartyKind.Party, 101);

            Assert.Equal(12, packet.Length);
            ushort header = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
            ushort seq = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2, 2));

            Assert.Equal(0x06E, header & 0x01FF);
            Assert.Equal(3, (header >> 9) & 0x7F); // 3 words = 12 bytes
            Assert.Equal(101, seq);

            uint targetId = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4, 4));
            ushort targetIndex = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(8, 2));
            byte kind = packet[10];

            Assert.Equal(12345u, targetId);
            Assert.Equal(42, targetIndex);
            Assert.Equal((byte)PartyKind.Party, kind);
        }

        [Fact]
        public void PartyPacketBuilder_BuildGroupSolicitRes_CreatesAcceptAndDecline()
        {
            byte[] acceptPacket = PartyPacketBuilder.BuildGroupSolicitRes(true, 102);
            Assert.Equal(8, acceptPacket.Length);
            ushort acceptHeader = BinaryPrimitives.ReadUInt16LittleEndian(acceptPacket.AsSpan(0, 2));
            Assert.Equal(0x074, acceptHeader & 0x01FF);
            Assert.Equal(1, acceptPacket[4]); // Res = 1

            byte[] declinePacket = PartyPacketBuilder.BuildGroupSolicitRes(false, 103);
            Assert.Equal(8, declinePacket.Length);
            Assert.Equal(0, declinePacket[4]); // Res = 0
        }

        [Fact]
        public void PartyPacketBuilder_BuildGroupLeave_CreatesValidPacket()
        {
            byte[] packet = PartyPacketBuilder.BuildGroupLeave(PartyKind.Party, 104);
            Assert.Equal(8, packet.Length);
            ushort header = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
            Assert.Equal(0x06F, header & 0x01FF);
            Assert.Equal(0, packet[4]); // Kind = Party
        }

        [Fact]
        public void PartyPacketBuilder_BuildGroupBreakup_CreatesValidPacket()
        {
            byte[] packet = PartyPacketBuilder.BuildGroupBreakup(PartyKind.Party, 105);
            Assert.Equal(8, packet.Length);
            ushort header = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
            Assert.Equal(0x070, header & 0x01FF);
            Assert.Equal(0, packet[4]); // Kind = Party
        }

        [Fact]
        public void PartyPacketBuilder_BuildGroupStrike_CreatesValidPacket()
        {
            byte[] packet = PartyPacketBuilder.BuildGroupStrike(54321, 99, "KickedPlayer", 0, 106);
            Assert.Equal(28, packet.Length);
            ushort header = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
            Assert.Equal(0x071, header & 0x01FF);
            Assert.Equal(7, (header >> 9) & 0x7F); // 7 words = 28 bytes

            uint targetId = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4, 4));
            ushort targetIndex = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(8, 2));
            Assert.Equal(54321u, targetId);
            Assert.Equal(99, targetIndex);

            string name = Encoding.ASCII.GetString(packet.AsSpan(12, 12));
            Assert.Equal("KickedPlayer", name);
        }

        [Fact]
        public async Task PartyPacketModule_RoutesInbound0x0DC_SetsPendingInviteAndFiresEvent()
        {
            var partyState = new PartyState();
            byte[]? sentBytes = null;
            var module = new PartyPacketModule(partyState, (data, _) =>
            {
                sentBytes = data.ToArray();
                return Task.CompletedTask;
            });

            var dispatcher = new PacketDispatcher();
            module.Register(dispatcher);

            PartyInvite? receivedInvite = null;
            partyState.InviteReceived += inv => receivedInvite = inv;

            // Simulate server sending 0x0DC
            byte[] payload = new byte[32];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 9001);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 44);
            payload[7] = (byte)PartyKind.Party;
            Encoding.ASCII.GetBytes("Tarudrake").CopyTo(payload.AsSpan(8));

            var header = new PacketHeader(0x0DC, 8, 1);
            dispatcher.Dispatch(header, payload);

            Assert.NotNull(receivedInvite);
            Assert.Equal("Tarudrake", receivedInvite.InviterName);
            Assert.Equal(9001u, receivedInvite.InviterId);
            Assert.Equal(44, receivedInvite.InviterTargetIndex);
            Assert.True(partyState.HasPendingInvite);

            // Test AcceptInviteAsync sends 0x074 (Res=1) and clears invite
            await module.AcceptInviteAsync();

            Assert.NotNull(sentBytes);
            Assert.Equal(8, sentBytes.Length);
            Assert.Equal(1, sentBytes[4]); // Res = 1 (Accept)
            Assert.False(partyState.HasPendingInvite);
        }

        [Fact]
        public async Task PartyPacketModule_DeclineInvite_SendsDeclinePacketAndClearsInvite()
        {
            var partyState = new PartyState();
            byte[]? sentBytes = null;
            var module = new PartyPacketModule(partyState, (data, _) =>
            {
                sentBytes = data.ToArray();
                return Task.CompletedTask;
            });

            partyState.SetPendingInvite(new PartyInvite(9001, 44, "Tarudrake", PartyKind.Party, DateTime.UtcNow));
            Assert.True(partyState.HasPendingInvite);

            await module.DeclineInviteAsync();

            Assert.NotNull(sentBytes);
            Assert.Equal(8, sentBytes.Length);
            Assert.Equal(0, sentBytes[4]); // Res = 0 (Decline)
            Assert.False(partyState.HasPendingInvite);
        }
    
        [Fact]
        public void PartyVitals_SurviveRosterUpdates_AndFollowGroupAttr()
        {
            var party = new PartyState();
            var localPlayer = new LocalPlayerState { ServerId = 1001 };
            var dispatcher = new PacketDispatcher();
            Task Send(ReadOnlyMemory<byte> chunk, bool urgent) => Task.CompletedTask;
            new PartyPacketModule(party, Send).Register(dispatcher);
            new EntityPacketModule(new WorldState(), localPlayer, Send, null, party).Register(dispatcher);

            party.UpsertMember(new PartyMember { ServerId = 1001, Name = "Cybin", Hp = 1658, Mp = 571, Hpp = 100, Mpp = 100 });
            party.UpsertMember(new PartyMember { ServerId = 1002, Name = "Tarudrake", Hp = 9999, Mp = 2794, Hpp = 100, Mpp = 100 });

            // A group table (0x0C8) carries the roster only; it must not zero anyone's vitals.
            byte[] table = new byte[244];
            table[0] = (byte)PartyKind.Party;
            BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(4, 4), 1001);
            table[10] = 0x04;
            BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(16, 4), 1002);
            dispatcher.Dispatch(new PacketHeader(S2C_0x0C8_GroupTbl.PacketId, (ushort)(table.Length + 4), 1), table);

            var tarudrake = party.Members.Single(m => m.ServerId == 1002);
            Assert.Equal(9999u, tarudrake.Hp);
            Assert.True(party.Members.Single(m => m.ServerId == 1001).IsLeader);

            // A member's HP change arrives as 0x0DF.
            byte[] attr = new byte[36];
            BinaryPrimitives.WriteUInt32LittleEndian(attr.AsSpan(0, 4), 1002);
            BinaryPrimitives.WriteUInt32LittleEndian(attr.AsSpan(4, 4), 1409);
            BinaryPrimitives.WriteUInt32LittleEndian(attr.AsSpan(8, 4), 254);
            BinaryPrimitives.WriteUInt32LittleEndian(attr.AsSpan(12, 4), 300);
            attr[18] = 14;
            attr[19] = 9;
            dispatcher.Dispatch(new PacketHeader(S2C_0x0DF_GroupAttr.PacketId, (ushort)(attr.Length + 4), 2), attr);

            tarudrake = party.Members.Single(m => m.ServerId == 1002);
            Assert.Equal((1409u, 254u, 300u, (byte)14, (byte)9), (tarudrake.Hp, tarudrake.Mp, tarudrake.Tp, tarudrake.Hpp, tarudrake.Mpp));
            Assert.False(party.UpdateVitals(9999, 1, 1, 1, 1, 1)); // not a member
        }

        private static byte[] GroupTable(params (uint Id, byte Flags)[] entries)
        {
            byte[] table = new byte[244];
            table[0] = (byte)PartyKind.Alliance;
            for (int i = 0; i < entries.Length; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(4 + i * 12, 4), entries[i].Id);
                table[4 + i * 12 + 6] = entries[i].Flags;
            }
            return table;
        }

        [Fact]
        public void AllianceTable_GroupsMembersByParty_FlagsLeaders_AndDropsLeavers()
        {
            var party = new PartyState();
            var dispatcher = new PacketDispatcher();
            new PartyPacketModule(party, (_, _) => Task.CompletedTask).Register(dispatcher);

            // LSB orders the table by party (partyflag bits 0-1), then join time; bit 2 = party leader, bit 3 = alliance leader.
            byte[] table = GroupTable((1001, 0x0C), (1002, 0x00), (2001, 0x05), (2002, 0x01), (3001, 0x06));
            dispatcher.Dispatch(new PacketHeader(S2C_0x0C8_GroupTbl.PacketId, (ushort)(table.Length + 4), 1), table);

            var own = party.GetPartyMembers(0);
            Assert.Equal(new uint[] { 1001, 1002 }, own.Select(m => m.ServerId));
            Assert.True(own[0].IsLeader && own[0].IsAllianceLeader);
            Assert.False(own[1].IsLeader || own[1].IsAllianceLeader);

            var second = party.GetPartyMembers(1);
            Assert.Equal(new uint[] { 2001, 2002 }, second.Select(m => m.ServerId));
            Assert.Equal(new byte[] { 0, 1 }, second.Select(m => m.MemberNumber)); // slots count within each party
            Assert.True(second[0].IsLeader && !second[0].IsAllianceLeader);
            Assert.Equal(3001u, party.GetPartyMembers(2).Single().ServerId);

            // The next table no longer lists 2002: they left the alliance.
            table = GroupTable((1001, 0x0C), (1002, 0x00), (2001, 0x05), (3001, 0x06));
            dispatcher.Dispatch(new PacketHeader(S2C_0x0C8_GroupTbl.PacketId, (ushort)(table.Length + 4), 2), table);
            Assert.DoesNotContain(party.Members, m => m.ServerId == 2002);
            Assert.Equal(4, party.Members.Count);
        }

        [Fact]
        public void GroupList_CarriesPartyNumberAndAllianceLeader()
        {
            byte[] payload = new byte[52];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 2001);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), 0x0E); // PartyNo 2, party + alliance leader
            payload[22] = 3;
            var list = new S2C_0x0DD_GroupList(payload);
            Assert.Equal(2, list.PartyNumber);
            Assert.True(list.IsPartyLeader);
            Assert.True(list.IsAllianceLeader);
            Assert.Equal(3, list.MemberNumber);
        }

        [Fact]
        public void GroupEffects_StoreEachMembersStatusIds()
        {
            var party = new PartyState();
            var dispatcher = new PacketDispatcher();
            new EntityPacketModule(new WorldState(), new LocalPlayerState { ServerId = 1001 }, (_, _) => Task.CompletedTask, null, party).Register(dispatcher);
            party.UpsertMember(new PartyMember { ServerId = 1002, Name = "Cybin" });

            // One member entry: buffs 40 (Protect), 0x2A + high bits 1 (0x12A), then empty slots (0xFF).
            byte[] payload = new byte[S2C_0x076_GroupEffects.MemberEntrySize * 5];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 1002);
            BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(8, 8), 1UL << 2);
            payload.AsSpan(16, 32).Fill(0xFF);
            payload[16] = 40;
            payload[17] = 0x2A;
            dispatcher.Dispatch(new PacketHeader(S2C_0x076_GroupEffects.PacketId, (ushort)(payload.Length + 4), 1), payload);

            Assert.Equal(new ushort[] { 40, 0x12A }, party.Members.Single().StatusEffectIds);
        }
}
}
