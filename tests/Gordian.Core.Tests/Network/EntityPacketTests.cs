// tests/Gordian.Core.Tests/Network/EntityPacketTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
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
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(12, 4), 20.5f); // Y (Elevation)
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(16, 4), 30.5f); // Z (North/South)
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
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(12, 4), 200.0f); // Y (Elevation)
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(16, 4), 300.0f); // Z (North/South)
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

            // 4. CliStatus (0x061)
            byte[] cliStatus = EntityOutboundPackets.BuildCliStatus(unknown00: 0, sequenceId: 0x44);
            Assert.Equal(EntityOutboundPackets.CliStatusSubPacketSize, cliStatus.Length);
            headerWord = BinaryPrimitives.ReadUInt16LittleEndian(cliStatus.AsSpan(0, 2));
            Assert.Equal(0x061, headerWord & 0x1FF);
            Assert.Equal(2, headerWord >> 9);
            Assert.Equal(0x44, BinaryPrimitives.ReadUInt16LittleEndian(cliStatus.AsSpan(2, 2)));
            Assert.Equal(0, cliStatus[4]);
        }

        [Fact]
        public void S2C_0x01B_JobInfo_DecodesValidPayload()
        {
            byte[] payload = new byte[128];
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), 0x0101); // FaceNo / race
            payload[4] = (byte)JobId.Paladin; // MainJob (PLD = 7)
            payload[5] = 1; // HairNo
            payload[6] = 2; // Size
            payload[7] = (byte)JobId.Warrior; // SubJob (WAR = 1)
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 0xFFFF); // GetJobFlag

            // job_lev (offset 12..27)
            payload[12 + (int)JobId.Paladin] = 75;
            payload[12 + (int)JobId.Warrior] = 37;

            // bp_base at 28..41
            for (int i = 0; i < 7; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(28 + (i * 2), 2), (ushort)(60 + i));
            }

            // bp_adj at 42..55
            for (int i = 0; i < 7; i++)
            {
                BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(42 + (i * 2), 2), (short)(5 + i));
            }

            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(56, 4), 1250); // HpMax
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(60, 4), 450);  // MpMax
            payload[64] = 1; // SubJobUnlockedFlag

            // job_lev2 at 68..91
            payload[68 + (int)JobId.Paladin] = 75;
            payload[68 + (int)JobId.Warrior] = 37;

            var jobInfo = new S2C_0x01B_JobInfo(payload);
            Assert.True(jobInfo.IsValid);
            Assert.Equal(JobId.Paladin, jobInfo.MainJob);
            Assert.Equal(75, jobInfo.MainJobLevel);
            Assert.Equal(JobId.Warrior, jobInfo.SubJob);
            Assert.Equal(37, jobInfo.SubJobLevel);
            Assert.Equal(1250, jobInfo.HpMax);
            Assert.Equal(450, jobInfo.MpMax);
            Assert.Equal(60, jobInfo.GetBaseStat(0));
            Assert.Equal(5, jobInfo.GetStatModifier(0));
            Assert.Equal(75, jobInfo.GetJobLevel(JobId.Paladin));
            Assert.Equal(37, jobInfo.GetJobLevel(JobId.Warrior));
        }

        [Fact]
        public void S2C_0x0DF_GroupAttr_DecodesValidPayload()
        {
            byte[] payload = new byte[36];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x12345678); // UniqueNo
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 980);         // Hp
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 320);         // Mp
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 1500);       // Tp
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(16, 2), 0x400);      // ActIndex
            payload[18] = 88; // Hpp
            payload[19] = 71; // Mpp
            payload[20] = 0;  // Kind
            payload[21] = 0;  // MoghouseFlag
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(22, 2), 101); // ZoneNo
            payload[28] = (byte)JobId.WhiteMage; // MainJob (WHM = 3)
            payload[29] = 75;                    // MainJobLevel
            payload[30] = (byte)JobId.BlackMage; // SubJob (BLM = 4)
            payload[31] = 37;                    // SubJobLevel

            var groupAttr = new S2C_0x0DF_GroupAttr(payload);
            Assert.True(groupAttr.IsValid);
            Assert.Equal(0x12345678u, groupAttr.UniqueNo);
            Assert.Equal(980u, groupAttr.Hp);
            Assert.Equal(320u, groupAttr.Mp);
            Assert.Equal(1500u, groupAttr.Tp);
            Assert.Equal(0x400, groupAttr.ActorIndex);
            Assert.Equal(88, groupAttr.Hpp);
            Assert.Equal(71, groupAttr.Mpp);
            Assert.Equal(JobId.WhiteMage, groupAttr.MainJob);
            Assert.Equal(75, groupAttr.MainJobLevel);
            Assert.Equal(JobId.BlackMage, groupAttr.SubJob);
            Assert.Equal(37, groupAttr.SubJobLevel);
        }

        [Fact]
        public void LocalPlayerState_UpdateFromJobInfo_PopulatesJobsAndStats()
        {
            var state = new Gordian.Core.World.LocalPlayerState();
            byte[] payload = new byte[128];
            payload[4] = (byte)JobId.RedMage;
            payload[7] = (byte)JobId.BlackMage;
            payload[68 + (int)JobId.RedMage] = 75;
            payload[68 + (int)JobId.BlackMage] = 37;
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(56, 4), 1000);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(60, 4), 600);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(28, 2), 65); // STR

            bool vitalsFired = false;
            bool statsFired = false;
            state.VitalsUpdated += () => vitalsFired = true;
            state.StatsUpdated += () => statsFired = true;

            var jobInfo = new S2C_0x01B_JobInfo(payload);
            state.UpdateFromJobInfo(jobInfo);

            Assert.True(vitalsFired);
            Assert.True(statsFired);
            Assert.Equal(JobId.RedMage, state.MainJob);
            Assert.Equal(75, state.MainJobLevel);
            Assert.Equal(JobId.BlackMage, state.SubJob);
            Assert.Equal(37, state.SubJobLevel);
            Assert.Equal(1000, state.MaxHp);
            Assert.Equal(600, state.MaxMp);
            Assert.Equal(65, state.BaseStats[0]);
        }

        [Fact]
        public void LocalPlayerState_UpdateFromGroupAttr_PopulatesVitals()
        {
            var state = new Gordian.Core.World.LocalPlayerState();
            byte[] payload = new byte[36];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 850);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 300);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 2200);
            payload[18] = 85; // HPP
            payload[28] = (byte)JobId.Thief;
            payload[29] = 99;
            payload[30] = (byte)JobId.Ninja;
            payload[31] = 49;

            var groupAttr = new S2C_0x0DF_GroupAttr(payload);
            state.UpdateFromGroupAttr(groupAttr);

            Assert.Equal(850, state.CurrentHp);
            Assert.Equal(300, state.CurrentMp);
            Assert.Equal(2200, state.CurrentTp);
            Assert.Equal(85, state.Hpp);
            Assert.Equal(JobId.Thief, state.MainJob);
            Assert.Equal(99, state.MainJobLevel);
            Assert.Equal(JobId.Ninja, state.SubJob);
            Assert.Equal(49, state.SubJobLevel);
        }

        [Fact]
        public void LocalPlayerState_FallbackHpEstimation_CalculatesFromMaxHpAndHpp()
        {
            var state = new Gordian.Core.World.LocalPlayerState();

            // Set HPP to 88% via CharStatus
            byte[] charStatusPayload = new byte[96];
            BinaryPrimitives.WriteUInt32LittleEndian(charStatusPayload.AsSpan(36, 4), 88u << 16); // Flags0: HPP at bits 16..23
            var charStatus = new S2C_0x037_CharStatus(charStatusPayload);
            state.UpdateFromCharStatus(charStatus);

            Assert.Equal(88, state.Hpp);
            Assert.Equal(0, state.CurrentHp); // MaxHp not yet known

            // Now receive 0x01B with MaxHp = 1000
            byte[] jobInfoPayload = new byte[128];
            BinaryPrimitives.WriteInt32LittleEndian(jobInfoPayload.AsSpan(56, 4), 1000);
            var jobInfo = new S2C_0x01B_JobInfo(jobInfoPayload);
            state.UpdateFromJobInfo(jobInfo);

            // Fallback estimation should set CurrentHp = 1000 * 88 / 100 = 880
            Assert.Equal(880, state.CurrentHp);
            Assert.Equal(1000, state.MaxHp);
        }

        [Fact]
        public void EntityPacketModule_NamelessPc_TriggersCharReqPacket()
        {
            var world = new WorldState();
            var localPlayer = new LocalPlayerState();
            var sentPackets = new List<byte[]>();
            var dispatcher = new PacketDispatcher();

            var module = new EntityPacketModule(world, localPlayer, (chunk, enc) =>
            {
                sentPackets.Add(chunk.ToArray());
                return Task.CompletedTask;
            });
            module.Register(dispatcher);

            // PC update (0x00D) without Name flag (only Position and General)
            byte[] payload = new byte[0x70];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x01020304);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 0x0123);
            payload[6] = (byte)(EntityUpdateFlags.Position | EntityUpdateFlags.General);
            payload[26] = 100; // Hpp

            dispatcher.Dispatch(new PacketHeader(0x00D, (ushort)(payload.Length + 4), 1), payload);

            // Verify an outbound C2S 0x016 (CharReq) was triggered for ActIndex 0x0123
            Assert.NotEmpty(sentPackets);
            var charReq = sentPackets[0];
            ushort packetId = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(charReq.AsSpan(0, 2)) & 0x1FF);
            Assert.Equal(0x016, packetId);
            ushort requestedIndex = BinaryPrimitives.ReadUInt16LittleEndian(charReq.AsSpan(4, 2));
            Assert.Equal(0x0123, requestedIndex);
        }

        [Fact]
        public void EntityPacketModule_DeltaPositionUpdate_PreservesCachedHpp()
        {
            var world = new WorldState();
            var localPlayer = new LocalPlayerState();
            var dispatcher = new PacketDispatcher();
            var module = new EntityPacketModule(world, localPlayer, (chunk, enc) => Task.CompletedTask);
            module.Register(dispatcher);

            // 1. Initial PC update with General flag and HPP = 85
            byte[] p1 = new byte[0x70];
            BinaryPrimitives.WriteUInt32LittleEndian(p1.AsSpan(0, 4), 0x01020304);
            BinaryPrimitives.WriteUInt16LittleEndian(p1.AsSpan(4, 2), 0x0123);
            p1[6] = (byte)(EntityUpdateFlags.Position | EntityUpdateFlags.General);
            p1[26] = 85; // Hpp
            dispatcher.Dispatch(new PacketHeader(0x00D, (ushort)(p1.Length + 4), 1), p1);

            Assert.True(world.TryGetByServerId(0x01020304, out var ent));
            Assert.Equal(85, ent!.Hpp);

            // 2. Subsequent position delta update where General flag is 0 and HPP is 0
            byte[] p2 = new byte[0x70];
            BinaryPrimitives.WriteUInt32LittleEndian(p2.AsSpan(0, 4), 0x01020304);
            BinaryPrimitives.WriteUInt16LittleEndian(p2.AsSpan(4, 2), 0x0123);
            p2[6] = (byte)EntityUpdateFlags.Position; // Position only
            p2[26] = 0; // Hpp is 0 on position deltas
            dispatcher.Dispatch(new PacketHeader(0x00D, (ushort)(p2.Length + 4), 2), p2);

            // Entity Hpp should still be 85!
            Assert.Equal(85, ent.Hpp);
        }

        [Fact]
        public void EntityPacketModule_RateLimitsCharReq()
        {
            var world = new WorldState();
            var localPlayer = new LocalPlayerState();
            var sentPackets = new List<byte[]>();
            var dispatcher = new PacketDispatcher();
            var module = new EntityPacketModule(world, localPlayer, (chunk, enc) =>
            {
                sentPackets.Add(chunk.ToArray());
                return Task.CompletedTask;
            });
            module.Register(dispatcher);

            byte[] payload = new byte[0x70];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x01020304);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 0x0123);
            payload[6] = (byte)EntityUpdateFlags.Position;

            // Dispatch twice immediately
            dispatcher.Dispatch(new PacketHeader(0x00D, (ushort)(payload.Length + 4), 1), payload);
            dispatcher.Dispatch(new PacketHeader(0x00D, (ushort)(payload.Length + 4), 2), payload);

            // Only 1 CharReq packet should have been sent due to rate-limiting
            Assert.Single(sentPackets);
        }

        [Fact]
        public void S2C_0x00E_CharNpc_DecodesEquippedLook()
        {
            // 0x48 byte packet => 0x44 byte payload
            byte[] payload = new byte[0x44];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x01000002);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 42); // ActorIndex
            payload[6] = (byte)(EntityUpdateFlags.Position | EntityUpdateFlags.Name);

            // look_t at offset 0x2C:
            // 0x2C: size = 1 (MODEL_EQUIPPED)
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0x2C, 2), 1);
            // 0x2E: face = 2
            payload[0x2E] = 2;
            // 0x2F: race = 3 (ElvaanMale)
            payload[0x2F] = 3;
            // 0x30..0x3F: head, body, hands, legs, feet, main, sub, ranged
            ushort[] expectedSlots = { 101, 202, 303, 404, 505, 606, 707, 808 };
            for (int i = 0; i < 8; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0x30 + (i * 2), 2), expectedSlots[i]);
            }

            var npc = new S2C_0x00E_CharNpc(payload);
            Assert.True(npc.IsValid);
            Assert.Equal(1, npc.LookSize);
            Assert.True(npc.IsEquippedLook);
            Assert.Equal(0u, npc.GetModelId());

            Assert.True(npc.TryGetEquippedLook(out byte race, out byte face, out ushort[] grapTable));
            Assert.Equal(3, race);
            Assert.Equal(2, face);
            Assert.Equal(9, grapTable.Length);
            Assert.Equal((3 << 8) | 2, grapTable[0]); // FaceModel
            for (int i = 0; i < 8; i++)
            {
                Assert.Equal(expectedSlots[i], grapTable[i + 1]);
            }
        }

        [Fact]
        public void S2C_0x00E_CharNpc_DecodesStandardMonsterModelId()
        {
            byte[] payload = new byte[0x34];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x01000003);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 99);
            payload[6] = (byte)EntityUpdateFlags.Position;

            // look_t at offset 0x2C:
            // 0x2C: size = 0 (MODEL_STANDARD)
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0x2C, 2), 0);
            // 0x2E: modelid = 120 (Rabbit)
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0x2E, 2), 120);

            var npc = new S2C_0x00E_CharNpc(payload);
            Assert.True(npc.IsValid);
            Assert.Equal(0, npc.LookSize);
            Assert.False(npc.IsEquippedLook);
            Assert.Equal(120u, npc.GetModelId());
            Assert.False(npc.TryGetEquippedLook(out _, out _, out _));
        }

        [Fact]
        public void EntityPacketModule_HandleCharNpc_PopulatesEquippedLookOnEntity()
        {
            var world = new WorldState();
            var localPlayer = new LocalPlayerState();
            var dispatcher = new PacketDispatcher();
            var module = new EntityPacketModule(world, localPlayer, (c, e) => Task.CompletedTask);
            module.Register(dispatcher);

            byte[] payload = new byte[0x44];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x01000004);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 55); // ActorIndex
            payload[6] = (byte)(EntityUpdateFlags.Position | EntityUpdateFlags.Name);

            // look_t: size = 1 (MODEL_EQUIPPED), race = 1 (HumeMale), face = 5
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0x2C, 2), 1);
            payload[0x2E] = 5;
            payload[0x2F] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0x30, 2), 10); // Head
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0x32, 2), 20); // Body

            dispatcher.Dispatch(new PacketHeader(S2C_0x00E_CharNpc.PacketId, (ushort)(payload.Length + 4), 1), payload);

            Assert.True(world.TryGetByTargetIndex(55, out var entity));
            Assert.NotNull(entity);
            Assert.Equal(0u, entity.Appearance.ModelId);
            Assert.Equal(9, entity.Appearance.GrapIdTable.Length);
            Assert.Equal((1 << 8) | 5, entity.Appearance.FaceModel);
            Assert.Equal(10, entity.Appearance.Head);
            Assert.Equal(20, entity.Appearance.Body);
        }

        [Fact]
        public void S2C_0x00E_CharNpc_DoorEntity_ReturnsZeroModelId()
        {
            // MODEL_DOOR has LookSize == 2 — should never produce a monster model ID
            byte[] payload = new byte[0x34];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x01000005);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 200);

            // look_t: size = 2 (MODEL_DOOR)
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0x2C, 2), 2);
            // 0x2E: set to a non-zero value that should NOT be interpreted as modelId
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0x2E, 2), 9999);

            var npc = new S2C_0x00E_CharNpc(payload);
            Assert.Equal(2, npc.LookSize);
            Assert.False(npc.IsEquippedLook);
            Assert.Equal(0u, npc.GetModelId()); // Must return 0, not 9999
        }

        [Fact]
        public void S2C_0x00E_CharNpc_ChocoboEntity_IsEquippedLook()
        {
            // MODEL_CHOCOBO has LookSize == 7 — uses the full look_t equipped format
            byte[] payload = new byte[0x44];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x01000006);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 201);

            // look_t: size = 7 (MODEL_CHOCOBO), face = 1, race = 4
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0x2C, 2), 7);
            payload[0x2E] = 1; // face
            payload[0x2F] = 4; // race

            var npc = new S2C_0x00E_CharNpc(payload);
            Assert.Equal(7, npc.LookSize);
            Assert.True(npc.IsEquippedLook); // Chocobo uses equipped look format
            Assert.Equal(0u, npc.GetModelId()); // Not a monster model ID
            Assert.True(npc.TryGetEquippedLook(out byte race, out byte face, out _));
            Assert.Equal(4, race);
            Assert.Equal(1, face);
        }
    }
}
