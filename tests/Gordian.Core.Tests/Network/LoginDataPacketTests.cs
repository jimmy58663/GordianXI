// tests/Gordian.Core.Tests/Network/LoginDataPacketTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Gordian.Core.Actions;
using Gordian.Core.Config;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    /// <summary>
    /// The login-time data packets (#115): S2C 0x0AE / 0x0AD / 0x08E / 0x044 laid out as XiPackets and LandSandBoat
    /// describe them, the state they fill, and the byte-exact C2S 0x0C1 / 0x0D8 / 0x102 / 0x11B / 0x114 builders.
    /// </summary>
    public class LoginDataPacketTests
    {
        private static void AssertHeader(byte[] packet, ushort opcode, int length, int sequence = 0)
        {
            Assert.Equal(length, packet.Length);
            Assert.True(PacketHeader.TryParse(packet, out var header));
            Assert.Equal(opcode, header.PacketId);
            Assert.Equal(packet.Length, header.TotalSize);
            Assert.Equal(sequence, header.SequenceId);
        }

        private sealed class Fixture
        {
            public ProgressionState Progression { get; } = new();
            public LocalPlayerState Local { get; } = new();
            public List<byte[]> Sent { get; } = new();
            public PacketDispatcher Dispatcher { get; } = new();
            public LoginDataPacketModule Module { get; }

            public Fixture()
            {
                Module = new LoginDataPacketModule(Progression, Local, (data, _) =>
                {
                    Sent.Add(data.ToArray());
                    return Task.CompletedTask;
                });
                Module.Register(Dispatcher);
            }

            public void Receive(ushort id, byte[] payload) => Assert.True(Dispatcher.Dispatch(new PacketHeader(id, payload.Length + 4, 1), payload));

            public void SetJobs(JobId main, JobId sub)
            {
                var payload = new byte[0x88];
                payload[4] = (byte)main;
                payload[7] = (byte)sub;
                Local.UpdateFromJobInfo(new S2C_0x01B_JobInfo(payload));
            }
        }

        // ---- S2C 0x0AE mount data ----

        [Fact]
        public void MountData_BitsAreMountsInDatOrder()
        {
            // 12-byte packet: 8-byte table. Chocobo (0), Raptor (1), Red Raptor (0x1E), Noble Chocobo (0x21), Phuabo (0x23).
            var payload = new byte[8];
            payload[0] = 0b0000_0011;
            payload[3] = 0b0100_0000;
            payload[4] = 0b0000_1010;

            var f = new Fixture();
            int raised = 0;
            f.Progression.MountsUpdated += () => raised++;
            f.Receive(0x0AE, payload);

            Assert.Equal(1, raised);
            Assert.True(f.Progression.HasMountData);
            Assert.Equal(new[] { 0, 1, 0x1E, 0x21, 0x23 }, f.Progression.GetUnlockedMounts());
            Assert.True(f.Progression.HasMount(0x21));
            Assert.False(f.Progression.HasMount(2));
            Assert.False(f.Progression.HasMount(64));
            Assert.False(f.Progression.HasMount(-1));
        }

        [Fact]
        public void MountData_ShortPayloadIsIgnored()
        {
            var mounts = new S2C_0x0AE_MountData(new byte[7]);
            Assert.False(mounts.IsValid);
        }

        // ---- S2C 0x0AD dungeon (Moblin Maze Mongers) ----

        [Fact]
        public void Dungeon_VouchersAndRunesMapToItemIds()
        {
            // 132-byte packet: Vouchers[8] at 0, Runes[64] at 8, 56 unused bytes.
            var payload = new byte[128];
            payload[0] = 0b0000_0101;   // vouchers 0 and 2
            payload[8 + 63] = 0b1000_0000; // rune 511
            payload[8 + 1] = 0b0000_0001;  // rune 8

            var f = new Fixture();
            f.Receive(0x0AD, payload);

            Assert.Equal(new ushort[] { 28736, 28738 }, f.Progression.GetMazeVoucherItemIds());
            Assert.Equal(new ushort[] { 28808, 29311 }, f.Progression.GetMazeRuneItemIds());
            Assert.True(f.Progression.HasMazeVoucher(2));
            Assert.False(f.Progression.HasMazeVoucher(1));
            Assert.True(f.Progression.HasMazeRune(511));
        }

        [Fact]
        public void Dungeon_AcceptsThePayloadWithoutItsUnusedTail()
        {
            Assert.True(new S2C_0x0AD_Dungeon(new byte[72]).IsValid);
            Assert.False(new S2C_0x0AD_Dungeon(new byte[71]).IsValid);
        }

        // ---- S2C 0x08E Alter Ego points ----

        [Fact]
        public void AlterEgoPoints_StoresPointsUpgradesAndCosts()
        {
            // 104-byte packet: u16 points, 2 padding, count[32] at 4, u16 next[32] at 36.
            var payload = new byte[100];
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), 1234);
            payload[4 + (int)AlterEgoCategory.Str] = 7;
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(36 + (int)AlterEgoCategory.Str * 2, 2), 3);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(36 + (int)AlterEgoCategory.MagicSkills * 2, 2), 5);

            var f = new Fixture();
            int raised = 0;
            f.Progression.AlterEgoPointsUpdated += () => raised++;
            f.Receive(0x08E, payload);

            Assert.Equal(1, raised);
            Assert.Equal(1234, f.Progression.AlterEgoPoints);
            Assert.Equal(7, f.Progression.GetAlterEgoUpgrade(AlterEgoCategory.Str));
            Assert.Equal(0, f.Progression.GetAlterEgoUpgrade(AlterEgoCategory.Dex));
            Assert.Equal(3, f.Progression.GetAlterEgoNextCost(AlterEgoCategory.Str));
            Assert.Equal(5, f.Progression.GetAlterEgoNextCost(AlterEgoCategory.MagicSkills));
        }

        [Fact]
        public void AlterEgoPoints_ShortPayloadIsIgnored()
        {
            Assert.False(new S2C_0x08E_AlterEgoPoints(new byte[99]).IsValid);
        }

        // ---- S2C 0x044 extended job ----

        private static byte[] BluPayload(bool subJob, params byte[] spells)
        {
            var payload = new byte[156];
            payload[0] = (byte)JobId.BlueMage;
            payload[1] = subJob ? (byte)1 : (byte)0;
            spells.CopyTo(payload, 4);
            return payload;
        }

        [Fact]
        public void ExtendedJob_BlueMageSpellsGoToTheMainJob()
        {
            var f = new Fixture();
            f.SetJobs(JobId.BlueMage, JobId.Warrior);
            int raised = 0;
            f.Local.ExtendedJobUpdated += () => raised++;

            // Slot 0: Venom Shell (513 - 512 = 1); slot 2: spell 0x3C (572).
            f.Receive(0x044, BluPayload(false, 1, 0, 0x3C));

            Assert.Equal(1, raised);
            var data = f.Local.MainJobData;
            Assert.NotNull(data);
            Assert.True(data!.IsBlueMage);
            Assert.False(data.IsSubJob);
            Assert.Equal(20, data.BlueSpells.Length);
            Assert.Equal(513, data.GetBlueSpellId(0));
            Assert.Equal(0, data.GetBlueSpellId(1));
            Assert.Equal(572, data.GetBlueSpellId(2));
            Assert.Null(f.Local.SubJobData);
        }

        [Fact]
        public void ExtendedJob_SubJobPacketGoesToTheSubJob()
        {
            var f = new Fixture();
            f.SetJobs(JobId.Warrior, JobId.BlueMage);
            f.Receive(0x044, BluPayload(true, 5));

            Assert.Null(f.Local.MainJobData);
            Assert.Equal(517, f.Local.SubJobData!.GetBlueSpellId(0));
        }

        [Fact]
        public void ExtendedJob_PacketForAnotherJobIsIgnored()
        {
            var f = new Fixture();
            f.SetJobs(JobId.Warrior, JobId.Monk);
            int raised = 0;
            f.Local.ExtendedJobUpdated += () => raised++;
            f.Receive(0x044, BluPayload(false, 1));

            Assert.Equal(0, raised);
            Assert.Null(f.Local.MainJobData);
        }

        [Fact]
        public void ExtendedJob_StoredDataHidesAfterAJobChange()
        {
            var f = new Fixture();
            f.SetJobs(JobId.BlueMage, JobId.Warrior);
            f.Receive(0x044, BluPayload(false, 1));
            Assert.NotNull(f.Local.MainJobData);

            f.SetJobs(JobId.Puppetmaster, JobId.Warrior);
            Assert.Null(f.Local.MainJobData);
        }

        [Fact]
        public void ExtendedJob_PuppetmasterAutomatonLayout()
        {
            // LandSandBoat 0x044_extended_job_pup.h, payload offsets.
            var p = new byte[156];
            p[0] = (byte)JobId.Puppetmaster;
            p[1] = 0;
            p[4] = 1;  // Harlequin head
            p[5] = 0x20; // frame
            p[6] = 0x81; // attachment slot 1
            p[17] = 0x99; // attachment slot 12
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(20, 4), 0x0000_003F);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(24, 4), 0x0000_000F);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(52 + 4, 4), 0x0000_0002); // attachment 33
            Encoding.ASCII.GetBytes("Luneth").CopyTo(p, 84);
            ushort[] words = { 300, 320, 100, 120, 150, 160, 140, 150, 130, 140 };
            for (int i = 0; i < words.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(100 + i * 2, 2), words[i]);
            for (int s = 0; s < 7; s++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(124 + s * 4, 2), (ushort)(50 + s));
                BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(126 + s * 4, 2), (short)(s - 3));
            }
            p[152] = 4;

            var f = new Fixture();
            f.SetJobs(JobId.Puppetmaster, JobId.Warrior);
            f.Receive(0x044, p);

            var a = f.Local.MainJobData!.Automaton;
            Assert.NotNull(a);
            Assert.Equal(1, a!.Head);
            Assert.Equal(0x20, a.Frame);
            Assert.Equal(0x81, a.Attachments[0]);
            Assert.Equal(0x99, a.Attachments[11]);
            Assert.Equal(0x3Fu, a.UnlockedHeads);
            Assert.Equal(0x0Fu, a.UnlockedFrames);
            Assert.True(a.IsAttachmentUnlocked(33));
            Assert.False(a.IsAttachmentUnlocked(32));
            Assert.Equal("Luneth", a.Name);
            Assert.Equal(300, a.Hp);
            Assert.Equal(320, a.MaxHp);
            Assert.Equal(100, a.Mp);
            Assert.Equal(120, a.MaxMp);
            Assert.Equal(150, a.MeleeSkill);
            Assert.Equal(160, a.MeleeSkillCap);
            Assert.Equal(140, a.RangedSkill);
            Assert.Equal(150, a.RangedSkillCap);
            Assert.Equal(130, a.MagicSkill);
            Assert.Equal(140, a.MagicSkillCap);
            Assert.Equal(new ushort[] { 50, 51, 52, 53, 54, 55, 56 }, a.Stats);
            Assert.Equal(new short[] { -3, -2, -1, 0, 1, 2, 3 }, a.StatBonuses);
            Assert.Equal(4, a.ElementalCapacityBonus);
            Assert.Empty(f.Local.MainJobData.BlueSpells);
        }

        [Fact]
        public void ExtendedJob_MonstrosityGoesToTheMainJob()
        {
            var p = new byte[156];
            p[0] = S2C_0x044_ExtendedJob.MonstrosityJobNo;
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4, 2), 27);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(8, 2), 0x0103);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(8 + 11 * 2, 2), 0xFFFF);

            var f = new Fixture();
            f.SetJobs(JobId.Warrior, JobId.Monk);
            f.Receive(0x044, p);

            var mon = f.Local.MainJobData!.Monstrosity;
            Assert.NotNull(mon);
            Assert.Equal(27, mon!.Species);
            Assert.Equal(0x0103, mon.Instincts[0]);
            Assert.Equal(0xFFFF, mon.Instincts[11]);
        }

        [Fact]
        public void ExtendedJob_UnknownJobKeepsItsRawData()
        {
            var p = new byte[156];
            p[0] = (byte)JobId.Warrior;
            p[10] = 0xAB;

            var f = new Fixture();
            f.Receive(0x044, p); // jobs unknown yet: accepted

            var data = f.Local.MainJobData!;
            Assert.Null(data.Automaton);
            Assert.Null(data.Monstrosity);
            Assert.Equal(0xAB, data.RawPayload[10]);
        }

        // ---- C2S builders ----

        [Fact]
        public void BuildAlterEgoUpgrade_Is8BytesWithTheKind()
        {
            // 0x0C1 | (2 << 9) = 0x04C1
            Assert.Equal(new byte[] { 0xC1, 0x04, 0x03, 0x00, 0x0A, 0x00, 0, 0 },
                LoginDataPacketBuilder.BuildAlterEgoUpgrade(AlterEgoCategory.Str, 3));
        }

        [Fact]
        public void BuildDungeonParam_Is40Bytes()
        {
            var data = new byte[30];
            for (int i = 0; i < data.Length; i++) data[i] = (byte)(i + 1);
            byte[] packet = LoginDataPacketBuilder.BuildDungeonParam(0x01020304, 0x0456, 0x1234, 0x56, data, 2);

            AssertHeader(packet, 0x0D8, 40, 2);
            Assert.Equal(0x0456, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4)));
            Assert.Equal(0x1234, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(6)));
            Assert.Equal(0x56, packet[8]);
            Assert.Equal(new byte[3], packet[9..12]);
            Assert.Equal(0x01020304u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(12)));
            Assert.Equal(data[..24], packet[16..40]); // Data is capped at 24 bytes
        }

        [Fact]
        public void BuildSetBlueSpell_Is164BytesWithTheSpellInItsSlot()
        {
            byte[] packet = LoginDataPacketBuilder.BuildSetBlueSpell(3, 0x2A, subJob: true, 5);

            AssertHeader(packet, 0x102, 164, 5);
            Assert.Equal(0x2A, packet[4]);
            Assert.Equal(0, packet[5]);
            Assert.Equal(16, packet[8]);
            Assert.Equal(1, packet[9]);
            for (int i = 0; i < 20; i++) Assert.Equal(i == 3 ? 0x2A : 0, packet[12 + i]);
            Assert.All(packet[32..], b => Assert.Equal(0, b));
        }

        [Fact]
        public void BuildRemoveBlueSpell_SendsSpellIdZeroAndTheRemovedSpell()
        {
            byte[] packet = LoginDataPacketBuilder.BuildRemoveBlueSpell(19, 0x11, subJob: false);
            Assert.Equal(0, packet[4]);
            Assert.Equal(0, packet[9]);
            Assert.Equal(0x11, packet[12 + 19]);
            Assert.Throws<ArgumentOutOfRangeException>(() => LoginDataPacketBuilder.BuildRemoveBlueSpell(20, 1, false));
        }

        [Fact]
        public void BuildEquipAutomatonPart_UsesThePuppetmasterLayout()
        {
            byte[] packet = LoginDataPacketBuilder.BuildEquipAutomatonPart(AutomatonSlot.Attachment1, 0x81, subJob: false, 1);

            AssertHeader(packet, 0x102, 164, 1);
            Assert.Equal(0x81, packet[4]);
            Assert.Equal(18, packet[8]);
            Assert.Equal(0, packet[9]);
            for (int i = 0; i < 14; i++) Assert.Equal(i == 2 ? 0x81 : 0, packet[12 + i]);

            byte[] head = LoginDataPacketBuilder.BuildEquipAutomatonPart(AutomatonSlot.Head, 3, subJob: true);
            Assert.Equal(3, head[4]);
            Assert.Equal(3, head[12]);
            Assert.Equal(1, head[9]);
        }

        [Fact]
        public void BuildRemoveAutomatonAttachment_OnlyForAttachments()
        {
            byte[] packet = LoginDataPacketBuilder.BuildRemoveAutomatonAttachment(AutomatonSlot.Attachment12, 0x99, subJob: false);
            Assert.Equal(0, packet[4]);
            Assert.Equal(0x99, packet[12 + 13]);
            Assert.Throws<ArgumentOutOfRangeException>(() => LoginDataPacketBuilder.BuildRemoveAutomatonAttachment(AutomatonSlot.Frame, 1, false));
        }

        [Fact]
        public void BuildMasteryDisplay_Is8Bytes()
        {
            // 0x11B | (2 << 9) = 0x051B
            Assert.Equal(new byte[] { 0x1B, 0x05, 0x04, 0x00, 1, 0, 0, 0 }, LoginDataPacketBuilder.BuildMasteryDisplay(true, 4));
            Assert.Equal(new byte[] { 0x1B, 0x05, 0x00, 0x00, 0, 0, 0, 0 }, LoginDataPacketBuilder.BuildMasteryDisplay(false));
        }

        [Fact]
        public void BuildMapMarkers_IsTheHeaderOnly()
        {
            // 0x114 | (1 << 9) = 0x0314
            Assert.Equal(new byte[] { 0x14, 0x03, 0x06, 0x00 }, LoginDataPacketBuilder.BuildMapMarkers(6));
        }

        [Fact]
        public async Task Module_SendMethodsUseIncreasingSequences()
        {
            var f = new Fixture();
            await f.Module.SendAlterEgoUpgradeAsync(AlterEgoCategory.MaxHp);
            await f.Module.SendMapMarkersRequestAsync();

            Assert.Equal(2, f.Sent.Count);
            AssertHeader(f.Sent[0], 0x0C1, 8, 1);
            AssertHeader(f.Sent[1], 0x114, 4, 2);
        }

        // ---- /jobmasterdisp ----

        [Theory]
        [InlineData("/jobmasterdisp on", ChatCommandResultKind.JobMasterDisplay, RestMode.On)]
        [InlineData("/jobmasterdisp off", ChatCommandResultKind.JobMasterDisplay, RestMode.Off)]
        [InlineData("/jobmasterdisp", ChatCommandResultKind.LocalNotice, RestMode.Toggle)]
        [InlineData("/jobmasterdisp maybe", ChatCommandResultKind.LocalNotice, RestMode.Toggle)]
        public void Router_JobMasterDisplayNeedsOnOrOff(string input, ChatCommandResultKind kind, RestMode mode)
        {
            var result = ChatCommandRouter.Parse(input);
            Assert.Equal(kind, result.Kind);
            Assert.Equal(mode, result.Rest);
        }

        [Fact]
        public async Task Command_JobMasterDisplaySendsC2S0x11B()
        {
            var sent = new List<byte[]>();
            Task Capture(ReadOnlyMemory<byte> mem, bool urgent)
            {
                sent.Add(mem.ToArray());
                return Task.CompletedTask;
            }

            var profile = new SessionProfile();
            var world = new WorldState();
            var local = new LocalPlayerState();
            var service = new PlayerActionService(
                profile, world, local, new CombatPacketModule(new CombatState(), local, Capture),
                new ChatPacketModule(Capture), new PartyPacketModule(new PartyState(), Capture),
                new EntityPacketModule(world, local, Capture), new LifecyclePacketModule(profile, Capture), Capture)
            {
                LoginDataModule = new LoginDataPacketModule(new ProgressionState(), local, Capture)
            };

            var result = await service.ExecuteCommandAsync("/jobmasterdisp off");

            Assert.True(result.Success);
            byte[] packet = Assert.Single(sent);
            AssertHeader(packet, 0x11B, 8, 1);
            Assert.Equal(0, packet[4]);
        }
    }
}
