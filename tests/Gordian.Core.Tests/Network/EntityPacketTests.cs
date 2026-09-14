// tests/Gordian.Core.Tests/Network/EntityPacketTests.cs
using System;
using System.Buffers.Binary;
using System.Text;
using Gordian.Core.Network.Packets;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    public class EntityPacketTests
    {
        [Fact]
        public void S2C_0x00D_CharPc_DecodesValidPayload()
        {
            byte[] payload = new byte[0x70];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x01020304);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 0x0123);
            payload[6] = (byte)(EntityUpdateFlags.Position | EntityUpdateFlags.General | EntityUpdateFlags.Model | EntityUpdateFlags.Name);
            payload[7] = 128; // dir
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(8, 4), 10.5f);  // X
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(12, 4), 30.5f); // Z
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(16, 4), 20.5f); // Y
            payload[24] = 50;  // Speed
            payload[25] = 50;  // SpeedBase
            payload[26] = 95;  // Hpp
            payload[27] = 1;   // ServerStatus

            // Flags1: SeekingParty (bit 11), Anon (bit 12), Away (bit 14), Linkshell (bit 17), GM Lv 3 (bits 24..26)
            uint flags1 = (1 << 11) | (1 << 12) | (1 << 14) | (1 << 17) | (3 << 24);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(28, 4), flags1);

            // Flags2: LS colors R=128, G=64, B=32, Charmed (bit 27)
            uint flags2 = 128 | (64 << 8) | (32 << 16) | (1 << 27);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(32, 4), flags2);

            // Flags3: Trust (bit 0), Mentor (bit 24), NewPlayer (bit 23)
            uint flags3 = 1 | (1 << 23) | (1 << 24);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(36, 4), flags3);

            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(40, 4), 0xDEADBEEF); // BtTargetId
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(44, 2), 42); // CostumeId
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(56, 2), 0x0567); // PetActorIndex

            // GrapIDTbl at 0x44 (72 bytes - 4 header = 68 in payload)
            for (int i = 0; i < 9; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0x44 + (i * 2), 2), (ushort)(0x1000 + i));
            }

            // Name at 0x56 (0x5A - 4 = 0x56)
            byte[] nameBytes = Encoding.ASCII.GetBytes("TestPlayer");
            nameBytes.CopyTo(payload.AsSpan(0x56));

            var pc = new S2C_0x00D_CharPc(payload);

            Assert.True(pc.IsValid);
            Assert.False(pc.IsDespawn);
            Assert.True(pc.HasPosition);
            Assert.True(pc.HasModel);
            Assert.True(pc.HasName);

            Assert.Equal(0x01020304u, pc.UniqueNo);
            Assert.Equal(0x0123, pc.ActorIndex);
            Assert.Equal(128, pc.Direction);
            Assert.Equal(10.5f, pc.X);
            Assert.Equal(20.5f, pc.Y);
            Assert.Equal(30.5f, pc.Z);
            Assert.Equal(50, pc.Speed);
            Assert.Equal(95, pc.Hpp);
            Assert.Equal(0xDEADBEEFu, pc.BtTargetId);
            Assert.Equal(42, pc.CostumeId);
            Assert.Equal(0x0567, pc.PetActorIndex);

            Assert.True(pc.IsSeekingParty);
            Assert.True(pc.IsAnonymous);
            Assert.True(pc.IsAway);
            Assert.True(pc.HasLinkshell);
            Assert.Equal(3, pc.GmLevel);

            Assert.Equal(128, pc.LsColorR);
            Assert.Equal(64, pc.LsColorG);
            Assert.Equal(32, pc.LsColorB);
            Assert.True(pc.IsCharmed);

            Assert.True(pc.IsTrust);
            Assert.True(pc.IsMentor);
            Assert.True(pc.IsNewPlayer);

            Span<ushort> grap = stackalloc ushort[9];
            Assert.True(pc.TryGetGrapIdTable(grap));
            Assert.Equal(0x1000, grap[0]);
            Assert.Equal(0x1008, grap[8]);

            Assert.Equal("TestPlayer", pc.GetName());
        }

        [Fact]
        public void S2C_0x00D_CharPc_HandlesDespawnAndInvalidPayload()
        {
            byte[] despawnPayload = new byte[0x30];
            BinaryPrimitives.WriteUInt32LittleEndian(despawnPayload.AsSpan(0, 4), 0x55);
            BinaryPrimitives.WriteUInt16LittleEndian(despawnPayload.AsSpan(4, 2), 0x10);
            despawnPayload[6] = (byte)EntityUpdateFlags.Despawn;

            var pcDespawn = new S2C_0x00D_CharPc(despawnPayload);
            Assert.True(pcDespawn.IsValid);
            Assert.True(pcDespawn.IsDespawn);

            byte[] shortPayload = new byte[10];
            var pcInvalid = new S2C_0x00D_CharPc(shortPayload);
            Assert.False(pcInvalid.IsValid);
        }

        [Fact]
        public void S2C_0x00E_CharNpc_DecodesValidPayload()
        {
            byte[] payload = new byte[0x50];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x20000001);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 25);
            payload[6] = (byte)(EntityUpdateFlags.Position | EntityUpdateFlags.Name);
            payload[7] = 64;
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(8, 4), 100.0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(12, 4), 300.0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(16, 4), 200.0f);
            payload[24] = 40; // Speed
            payload[26] = 100; // Hpp
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(40, 4), 0x11223344); // ClaimId
            payload[44] = (byte)EntitySubKind.Elevator;

            // Name at 0x30 (0x34 - 4 = 0x30)
            Encoding.ASCII.GetBytes("Elevator_A").CopyTo(payload.AsSpan(0x30));

            var npc = new S2C_0x00E_CharNpc(payload);
            Assert.True(npc.IsValid);
            Assert.False(npc.IsDespawn);
            Assert.True(npc.HasPosition);
            Assert.True(npc.HasName);

            Assert.Equal(0x20000001u, npc.UniqueNo);
            Assert.Equal(25, npc.ActorIndex);
            Assert.Equal(64, npc.Direction);
            Assert.Equal(100.0f, npc.X);
            Assert.Equal(200.0f, npc.Y);
            Assert.Equal(300.0f, npc.Z);
            Assert.Equal(40, npc.Speed);
            Assert.Equal(100, npc.Hpp);
            Assert.Equal(0x11223344u, npc.ClaimId);
            Assert.Equal(EntitySubKind.Elevator, npc.SubKind);
            Assert.Equal("Elevator_A", npc.GetName());
        }

        [Fact]
        public void S2C_0x037_CharStatus_DecodesValidPayload()
        {
            byte[] payload = new byte[0x60];
            payload[0] = 1; // Buff icon 1
            payload[1] = 2; // Buff icon 2
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(32, 4), 0x7777); // UniqueNo
            // Flags0: HPP at bits 16..23
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(36, 4), 85 << 16);
            // Flags1: Speed at bits 0..11
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(40, 4), 50);
            payload[45] = 10; // R
            payload[46] = 20; // G
            payload[47] = 30; // B
            // Flags2: PetIndex at bits 3..18
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(48, 4), 77 << 3);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(56, 4), 1800); // Dead counter seconds
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(64, 2), 5); // CostumeId
            payload[0x57] = 2; // MountId
            payload[0x58] = 0x7B; // WardrobeMask

            var status = new S2C_0x037_CharStatus(payload);
            Assert.True(status.IsValid);
            Assert.Equal(0x7777u, status.UniqueNo);
            Assert.Equal(85, status.Hpp);
            Assert.Equal(50, status.Speed);
            Assert.Equal(10, status.LsColorR);
            Assert.Equal(20, status.LsColorG);
            Assert.Equal(30, status.LsColorB);
            Assert.Equal(77, status.PetActorIndex);
            Assert.Equal(1800u, status.DeadCounterSeconds);
            Assert.Equal(5, status.CostumeId);
            Assert.Equal(2, status.MountId);
            Assert.Equal(0x7B, status.WardrobeMask);
            Assert.Equal(1, status.BuffStatus[0]);
            Assert.Equal(2, status.BuffStatus[1]);
        }

        [Fact]
        public void S2C_0x061_CliStatus_DecodesValidPayload()
        {
            byte[] payload = new byte[0x64];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 1450); // HP
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 620);  // MP
            payload[8] = (byte)JobId.Warrior;
            payload[9] = 99;
            payload[10] = (byte)JobId.Ninja;
            payload[11] = 49;
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(12, 2), 4500); // ExpNow
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(14, 2), 10000); // ExpNext

            // bp_base at 16..29: STR, DEX, VIT, AGI, INT, MND, CHR
            for (int i = 0; i < 7; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(16 + (i * 2), 2), (ushort)(70 + i));
            }

            // bp_adj at 30..43
            for (int i = 0; i < 7; i++)
            {
                BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(30 + (i * 2), 2), (short)(10 + i));
            }

            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(44, 2), 520); // Attack
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(46, 2), 480); // Defense

            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(64, 2), 101); // Title
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(66, 2), 10);  // Rank
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(70, 2), 245); // HomePointZone
            payload[76] = 2; // Nation
            payload[78] = 5; // SuperiorLevel
            payload[81] = 119; // ItemLevel

            var stats = new S2C_0x061_CliStatus(payload);
            Assert.True(stats.IsValid);
            Assert.Equal(1450, stats.HpMax);
            Assert.Equal(620, stats.MpMax);
            Assert.Equal(JobId.Warrior, stats.MainJob);
            Assert.Equal(99, stats.MainJobLevel);
            Assert.Equal(JobId.Ninja, stats.SubJob);
            Assert.Equal(49, stats.SubJobLevel);
            Assert.Equal(4500, stats.ExpNow);
            Assert.Equal(10000, stats.ExpNext);
            Assert.Equal(520, stats.Attack);
            Assert.Equal(480, stats.Defense);
            Assert.Equal(70, stats.GetBaseStat(0));
            Assert.Equal(10, stats.GetStatModifier(0));
            Assert.Equal(101, stats.TitleId);
            Assert.Equal(10, stats.Rank);
            Assert.Equal(245, stats.HomePointZone);
            Assert.Equal(2, stats.Nation);
            Assert.Equal(5, stats.SuperiorLevel);
            Assert.Equal(119, stats.ItemLevel);
        }

        [Fact]
        public void S2C_0x062_CliStatus2_DecodesValidPayload()
        {
            byte[] payload = new byte[252];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 45); // Recast 0
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 90); // Recast 1

            // Skill 0: Sword skill 424 | 0x8000 (capped)
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(124, 2), 424 | 0x8000);
            // Skill 1: Shield skill 350 (uncapped)
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(126, 2), 350);

            var skills = new S2C_0x062_CliStatus2(payload);
            Assert.True(skills.IsValid);
            Assert.Equal(45u, skills.GetCommandRecast(0));
            Assert.Equal(90u, skills.GetCommandRecast(1));
            Assert.Equal(424, skills.GetSkillLevel(0));
            Assert.True(skills.IsSkillCapped(0));
            Assert.Equal(350, skills.GetSkillLevel(1));
            Assert.False(skills.IsSkillCapped(1));
        }

        [Fact]
        public void S2C_0x076_GroupEffects_DecodesPartyBuffs()
        {
            byte[] payload = new byte[S2C_0x076_GroupEffects.MemberEntrySize * 2];
            // Member 0
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x101);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 1);
            payload[16] = 5; // Buff 0
            // Member 1
            int m1Offset = S2C_0x076_GroupEffects.MemberEntrySize;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(m1Offset, 4), 0x102);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(m1Offset + 4, 2), 2);
            payload[m1Offset + 16] = 9; // Buff 0

            var effects = new S2C_0x076_GroupEffects(payload);
            Assert.True(effects.IsValid);
            Assert.Equal(2, effects.MemberCount);
            Assert.Equal(0x101u, effects.GetMemberUniqueNo(0));
            Assert.Equal(1, effects.GetMemberActorIndex(0));
            Assert.Equal(5, effects.GetMemberBuffs(0)[0]);
            Assert.Equal(0x102u, effects.GetMemberUniqueNo(1));
            Assert.Equal(2, effects.GetMemberActorIndex(1));
            Assert.Equal(9, effects.GetMemberBuffs(1)[0]);
        }

        [Fact]
        public void S2C_0x077_EntityVis_DecodesVisibilityList()
        {
            byte[] payload = new byte[4 + (4 * 3)];
            payload[0] = 1; // Flags
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0x501);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 0x502);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 0x503);

            var vis = new S2C_0x077_EntityVis(payload);
            Assert.True(vis.IsValid);
            Assert.Equal(1, vis.Flags);
            Assert.Equal(3, vis.Count);
            Assert.Equal(0x501u, vis.GetUniqueNo(0));
            Assert.Equal(0x502u, vis.GetUniqueNo(1));
            Assert.Equal(0x503u, vis.GetUniqueNo(2));
        }

        [Fact]
        public void OutboundBuilders_BuildValidPackets()
        {
            // 1. ClStat (0x00F)
            byte[] clStat = EntityOutboundPackets.BuildClStat(new uint[] { 1, 2, 3 }, sequenceId: 0x11);
            Assert.Equal(EntityOutboundPackets.ClStatSubPacketSize, clStat.Length);
            ushort headerWord = BinaryPrimitives.ReadUInt16LittleEndian(clStat.AsSpan(0, 2));
            Assert.Equal(0x00F, headerWord & 0x1FF);
            Assert.Equal(9, headerWord >> 9);
            Assert.Equal(0x11, BinaryPrimitives.ReadUInt16LittleEndian(clStat.AsSpan(2, 2)));
            Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(clStat.AsSpan(4, 4)));

            // 2. CharReq (0x016)
            byte[] charReq = EntityOutboundPackets.BuildCharReq(actIndex: 250, sequenceId: 0x22);
            Assert.Equal(EntityOutboundPackets.CharReqSubPacketSize, charReq.Length);
            headerWord = BinaryPrimitives.ReadUInt16LittleEndian(charReq.AsSpan(0, 2));
            Assert.Equal(0x016, headerWord & 0x1FF);
            Assert.Equal(2, headerWord >> 9);
            Assert.Equal(0x22, BinaryPrimitives.ReadUInt16LittleEndian(charReq.AsSpan(2, 2)));
            Assert.Equal(250, BinaryPrimitives.ReadUInt16LittleEndian(charReq.AsSpan(4, 2)));

            // 3. CharReq2 (0x017)
            byte[] charReq2 = EntityOutboundPackets.BuildCharReq2(actIndex: 300, uniqueNo2: 0xABC, uniqueNo3: 0xDEF, flg: 1, flg2: 2, sequenceId: 0x33);
            Assert.Equal(EntityOutboundPackets.CharReq2SubPacketSize, charReq2.Length);
            headerWord = BinaryPrimitives.ReadUInt16LittleEndian(charReq2.AsSpan(0, 2));
            Assert.Equal(0x017, headerWord & 0x1FF);
            Assert.Equal(5, headerWord >> 9);
            Assert.Equal(0x33, BinaryPrimitives.ReadUInt16LittleEndian(charReq2.AsSpan(2, 2)));
            Assert.Equal(300, BinaryPrimitives.ReadUInt16LittleEndian(charReq2.AsSpan(4, 2)));
            Assert.Equal(0xABCu, BinaryPrimitives.ReadUInt32LittleEndian(charReq2.AsSpan(8, 4)));
            Assert.Equal(0xDEFu, BinaryPrimitives.ReadUInt32LittleEndian(charReq2.AsSpan(12, 4)));
        }
    }
}
