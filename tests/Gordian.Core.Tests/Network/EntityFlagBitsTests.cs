// tests/Gordian.Core.Tests/Network/EntityFlagBitsTests.cs
using System;
using System.Buffers.Binary;
using System.Threading.Tasks;
using Gordian.Core.Input;
using Gordian.Core.Network.Packets;
using Gordian.Core.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    /// <summary>
    /// The S2C 0x00D / 0x00E entity flag bits decoded for #334 and the dead-monster state for #327. Bit names and effects
    /// from XiPackets world/server/0x000D and 0x000E; the byte positions LandSandBoat writes from its entity_update.cpp
    /// and char_update.cpp. See docs/reference/entity-flags.md.
    /// </summary>
    public class EntityFlagBitsTests
    {
        private const EntityUpdateFlags PositionAndGeneral = EntityUpdateFlags.Position | EntityUpdateFlags.General;

        private static byte[] NpcPayload(uint flags0 = 0, uint flags1 = 0, uint flags2 = 0, uint flags3 = 0)
        {
            byte[] payload = new byte[0x48];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20, 4), flags0);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(28, 4), flags1);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(32, 4), flags2);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(36, 4), flags3);
            return payload;
        }

        [Fact]
        public void CharNpc_DecodesEachFlagBit()
        {
            Assert.False(new S2C_0x00E_CharNpc(NpcPayload()).IsSleeping);

            Assert.True(new S2C_0x00E_CharNpc(NpcPayload(flags0: 1u << 16)).IsKing);
            Assert.True(new S2C_0x00E_CharNpc(NpcPayload(flags1: 1u << 2)).IsSleeping);
            Assert.True(new S2C_0x00E_CharNpc(NpcPayload(flags1: 1u << 19)).IsTargetOff);
            Assert.True(new S2C_0x00E_CharNpc(NpcPayload(flags1: 1u << 30)).EasesHeading);
            Assert.True(new S2C_0x00E_CharNpc(NpcPayload(flags2: 1u << 25)).HidesShadow);
            Assert.True(new S2C_0x00E_CharNpc(NpcPayload(flags2: 1u << 29)).HasProperName);
            Assert.True(new S2C_0x00E_CharNpc(NpcPayload(flags2: 1u << 30)).IsPlural);
            Assert.True(new S2C_0x00E_CharNpc(NpcPayload(flags2: 1u << 31)).IsAutoTargetOnly);
            Assert.True(new S2C_0x00E_CharNpc(NpcPayload(flags3: 1u << 2)).IsPetSpawning);
            Assert.True(new S2C_0x00E_CharNpc(NpcPayload(flags3: 1u << 3)).IsPetKill);
            Assert.True(new S2C_0x00E_CharNpc(NpcPayload(flags3: 1u << 4)).IsMotionStopped);
            Assert.True(new S2C_0x00E_CharNpc(NpcPayload(flags3: 1u << 5)).IsPriorityDrawn);
            Assert.True(new S2C_0x00E_CharNpc(NpcPayload(flags3: 1u << 7)).IsOcclusionExempt);
            Assert.True(new S2C_0x00E_CharNpc(NpcPayload(flags3: 1u << 31)).IsHalfTransparent);

            // Neighbouring bits stay clear.
            var all = new S2C_0x00E_CharNpc(NpcPayload(flags1: ~((1u << 2) | (1u << 19) | (1u << 30))));
            Assert.False(all.IsSleeping || all.IsTargetOff || all.EasesHeading);
        }

        /// <summary>
        /// LandSandBoat writes the facetarget as the u16 at packet 0x1A (payload 0x16) holding <c>m_TargID &lt;&lt; 1</c>, the
        /// hitbox at packet 0x25, the name prefix at 0x27, the Terror / Pso'Xja / Trust bits at 0x28 and the name visibility
        /// at 0x2B (entity_update.cpp).
        /// </summary>
        [Fact]
        public void CharNpc_DecodesLandSandBoatBytePositions()
        {
            byte[] payload = new byte[0x48];
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0x16), (ushort)(0x1234 << 1));
            payload[0x21] = 8;    // packet 0x25: a living mob's hitbox
            payload[0x23] = 0x20; // packet 0x27: name prefix 0x20 (a named NM)
            payload[0x24] = 0x30; // packet 0x28: Terror 0x10 | Pso'Xja 0x20
            payload[0x27] = 0x80; // packet 0x2B: name_vis ghost_phase
            var npc = new S2C_0x00E_CharNpc(payload);

            Assert.Equal(0x1234, npc.FaceTargetIndex);
            Assert.Equal(8, npc.ModelHitboxSize);
            Assert.Equal(0.8f, npc.ModelHitboxRadius, 3);
            Assert.True(npc.HasProperName);
            Assert.False(npc.IsPlural);
            Assert.True(npc.IsMotionStopped);
            Assert.True(npc.IsPriorityDrawn);
            Assert.True(npc.IsHalfTransparent);
            Assert.False(npc.IsAutoTargetOnly);
            Assert.False(npc.IsKing);
        }

        [Fact]
        public void CharPc_DecodesItsFlagBits()
        {
            byte[] payload = new byte[0x70];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20, 4), 0x0400u << 17);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(28, 4), (1u << 2) | (1u << 19) | (1u << 30));
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(32, 4), 1u << 25);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(36, 4), (1u << 4) | (1u << 5));
            var pc = new S2C_0x00D_CharPc(payload);

            Assert.Equal(0x0400, pc.FaceTargetIndex);
            Assert.True(pc.IsSleeping);
            Assert.True(pc.IsTargetOff);
            Assert.True(pc.EasesHeading);
            Assert.True(pc.HidesShadow);
            Assert.True(pc.IsMotionStopped);
            Assert.True(pc.IsPriorityDrawn);
        }

        private sealed class Harness
        {
            public readonly WorldState World = new();
            private readonly PacketDispatcher _dispatcher = new();

            public Harness() => new EntityPacketModule(World, new LocalPlayerState(), (c, e) => Task.CompletedTask).Register(_dispatcher);

            public WorldEntity Send(uint id, ushort index, EntityUpdateFlags flags, byte status = 1, byte hpp = 100, byte serverStatus = 0,
                uint flags0 = 0, uint flags1High = 0, uint flags2 = 0, uint flags3 = 0, string name = "Mandragora")
            {
                byte[] payload = new byte[0x48];
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), id);
                BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), index);
                payload[6] = (byte)(flags | EntityUpdateFlags.Name);
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20, 4), flags0);
                payload[26] = hpp;
                payload[27] = serverStatus;
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(28, 4), flags1High | status);
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(32, 4), flags2);
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(36, 4), flags3);
                System.Text.Encoding.ASCII.GetBytes(name).CopyTo(payload.AsSpan(0x30));
                _dispatcher.Dispatch(new PacketHeader(S2C_0x00E_CharNpc.PacketId, (ushort)(payload.Length + 4), 1), payload);
                Assert.True(World.TryGetByServerId(id, out var entity));
                return entity!;
            }
        }

        /// <summary>
        /// #327, LandSandBoat's death sequence: the mob spawns at status Update (MonsterFlag) with HP and the 0.8 hitbox,
        /// then CDeathState sends server status 3 with HP 0 and the hitbox byte 0, the status byte unchanged. The name greys
        /// (ncol #9) and the monster can no longer be targeted; the fade to Disappear hides it.
        /// </summary>
        [Fact]
        public void DeadMonster_GreysTheNameAndCannotBeTargeted()
        {
            var h = new Harness();
            var mob = h.Send(0x01004010, 16, PositionAndGeneral, flags2: 8u << 8);
            Assert.Equal(EntityType.Monster, mob.Type);
            Assert.False(mob.IsDeadBattleEntity);
            Assert.True(TargetCycling.IsTargetable(mob));
            Assert.Equal(NamePlateColor.UnclaimedMonster, NamePlateStyle.Color(mob, mob.NamePlate, 0, Array.Empty<uint>(), Array.Empty<uint>()));
            Assert.Equal(0.8f, mob.ModelHitboxRadius, 3);

            h.Send(0x01004010, 16, EntityUpdateFlags.General, hpp: 0, serverStatus: WorldEntity.StatusDead);
            Assert.True(mob.IsDeadBattleEntity);
            Assert.False(TargetCycling.IsTargetable(mob));
            Assert.Equal(NamePlateColor.Dead, NamePlateStyle.Color(mob, mob.NamePlate, 0, Array.Empty<uint>(), Array.Empty<uint>()));
            Assert.True(NamePlateStyle.ShowsName(mob, mob.NamePlate)); // the grey name stays up while it falls
            Assert.Equal(0f, mob.ModelHitboxRadius);

            // A dead player keeps being targetable (Raise) and keeps its colour.
            var player = new PlayerEntity(0x00010001, 1024) { Name = "Tarudrake", AnimationState = WorldEntity.StatusDead };
            Assert.False(player.IsDeadBattleEntity);
            Assert.True(TargetCycling.IsTargetable(player));
        }

        [Fact]
        public void SleepFlag_HidesAndUntargets_ButAnEventStillDrawsItsActor()
        {
            var h = new Harness();
            // LandSandBoat status 4 (status_4) sets only SleepFlag.
            var npc = h.Send(0x01004020, 32, PositionAndGeneral, status: 4, name: "Mahol");
            Assert.True(npc.IsSleeping);
            Assert.False(npc.IsHidden);
            Assert.False(npc.IsDrawn);
            Assert.False(TargetCycling.IsTargetable(npc));
            Assert.False(NamePlateStyle.ShowsName(npc, npc.NamePlate));

            npc.IsInEvent = true;
            Assert.True(npc.IsDrawn);
            Assert.True(NamePlateStyle.ShowsName(npc, npc.NamePlate));

            // Back to Normal: drawn and targetable again.
            npc.IsInEvent = false;
            h.Send(0x01004020, 32, EntityUpdateFlags.Position, status: 0, name: "Mahol");
            Assert.False(npc.IsSleeping);
            Assert.True(npc.IsDrawn);
            Assert.True(TargetCycling.IsTargetable(npc));
        }

        [Fact]
        public void TargetOffAndAutoTargetOnly_CannotBeTargeted()
        {
            var h = new Harness();
            // LandSandBoat entity_flags.untargetable (0x800 at packet 0x21) lands on Flags1 bit 19.
            var mob = h.Send(0x01004030, 48, PositionAndGeneral, flags1High: 0x800u << 8);
            Assert.True(mob.IsTargetOff);
            Assert.True(mob.IsDrawn);
            Assert.False(TargetCycling.IsTargetable(mob));

            var lurker = h.Send(0x01004031, 49, PositionAndGeneral, flags2: 1u << 31);
            Assert.True(lurker.IsAutoTargetOnly);
            Assert.False(lurker.IsDrawn);
            Assert.False(TargetCycling.IsTargetable(lurker));
            Assert.False(NamePlateStyle.ShowsName(lurker, lurker.NamePlate));
        }

        [Fact]
        public void GeneralOnlyFlags_SurviveAPositionOnlyUpdate_AndFaceTargetFollowsThePosition()
        {
            var h = new Harness();
            var npc = h.Send(0x01004040, 64, PositionAndGeneral, status: 0, flags0: 0x0123u << 17,
                flags2: (1u << 29) | (1u << 30), flags3: (1u << 4) | (1u << 31), name: "Rainemard");
            Assert.Equal(0x0123, npc.FaceTargetIndex);
            Assert.True(npc.HasProperName);
            Assert.True(npc.IsPlural);
            Assert.True(npc.IsMotionStopped);
            Assert.True(npc.IsHalfTransparent);

            h.Send(0x01004040, 64, EntityUpdateFlags.Position, status: 0, flags0: 0x0045u << 17, name: "Rainemard");
            Assert.Equal(0x0045, npc.FaceTargetIndex);
            Assert.True(npc.HasProperName);
            Assert.True(npc.IsMotionStopped);
            Assert.True(npc.IsHalfTransparent);

            // A General-only update leaves the facetarget alone (LandSandBoat writes it only with the position).
            h.Send(0x01004040, 64, EntityUpdateFlags.General, status: 0, name: "Rainemard");
            Assert.Equal(0x0045, npc.FaceTargetIndex);
            Assert.False(npc.IsMotionStopped);
        }

        [Fact]
        public void NamedFlag_DropsTheArticle()
        {
            var h = new Harness();
            var plain = h.Send(0x01004050, 80, PositionAndGeneral, name: "Mandragora");
            var named = h.Send(0x01004051, 81, PositionAndGeneral, flags2: 1u << 29, name: "Leaping_Lizzy");
            Assert.True(plain.TakesArticle);
            Assert.False(named.TakesArticle);
            Assert.False(new WorldEntity(1, 1, EntityType.Npc) { Name = "Mahol" }.TakesArticle);
        }

        /// <summary>
        /// Without TurnFlag a standing entity turns to its new heading at once; with it the turn is eased (XiPackets TurnFlag).
        /// </summary>
        [Fact]
        public void TurnFlag_EasesTheTurn_OtherwiseAStandingEntitySnaps()
        {
            var snapper = new WorldEntity(1, 1, EntityType.Npc) { Direction = 0 };
            snapper.Direction = 64;
            snapper.InterpolatePosition(1f / 60f);
            Assert.Equal(snapper.HeadingRadians, snapper.RenderHeadingRadians, 4);

            var easer = new WorldEntity(2, 2, EntityType.Npc) { EasesHeading = true, Direction = 64 };
            easer.InterpolatePosition(1f / 60f);
            Assert.True(easer.RenderHeadingRadians > 0f && easer.RenderHeadingRadians < easer.HeadingRadians);
        }
    }
}
