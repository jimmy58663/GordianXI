// tests/Gordian.Core.Tests/World/WorldStateTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.World
{
    public class WorldStateTests
    {
        [Fact]
        public void WorldState_TracksEntitiesAndSupportsDualIndexing()
        {
            var world = new WorldState();
            var spawnedEvents = new List<WorldEntity>();
            var updatedEvents = new List<WorldEntity>();
            var despawnedEvents = new List<WorldEntity>();

            world.EntitySpawned += e => spawnedEvents.Add(e);
            world.EntityUpdated += e => updatedEvents.Add(e);
            world.EntityDespawned += e => despawnedEvents.Add(e);

            var player = new PlayerEntity(0x1001, 10)
            {
                Name = "Hero",
                Position = new Vector3(5f, 0f, 10f)
            };

            world.UpsertEntity(player);

            Assert.Equal(1, world.Count);
            Assert.Single(spawnedEvents);
            Assert.Same(player, spawnedEvents[0]);

            Assert.True(world.TryGetByServerId(0x1001, out var foundByServerId));
            Assert.Same(player, foundByServerId);

            Assert.True(world.TryGetByTargetIndex(10, out var foundByTargetIndex));
            Assert.Same(player, foundByTargetIndex);

            // Shift TargetIndex (e.g. zone repacking)
            var playerMovedIndex = new PlayerEntity(0x1001, 20)
            {
                Name = "Hero",
                Position = new Vector3(10f, 0f, 15f)
            };
            world.UpsertEntity(playerMovedIndex);

            Assert.Equal(1, world.Count);
            Assert.Single(updatedEvents);
            Assert.False(world.TryGetByTargetIndex(10, out _));
            Assert.True(world.TryGetByTargetIndex(20, out _));

            // Remove entity
            Assert.True(world.RemoveEntity(0x1001));
            Assert.Equal(0, world.Count);
            Assert.Single(despawnedEvents);
            Assert.False(world.TryGetByServerId(0x1001, out _));
            Assert.False(world.TryGetByTargetIndex(20, out _));
        }

        [Fact]
        public void SpatialPartitionGrid_PerformsFastQueriesAndFilters()
        {
            var grid = new SpatialPartitionGrid(cellSize: 10.0f);

            var e1 = new WorldEntity(1, 1, EntityType.Player) { Position = new Vector3(0f, 0f, 0f) };
            var e2 = new WorldEntity(2, 2, EntityType.Monster) { Position = new Vector3(5f, 0f, 5f) };
            var e3 = new WorldEntity(3, 3, EntityType.Npc) { Position = new Vector3(50f, 0f, 50f) };

            grid.InsertOrUpdate(e1);
            grid.InsertOrUpdate(e2);
            grid.InsertOrUpdate(e3);

            // Radius search
            var inRadius = grid.GetEntitiesInRadius(new Vector3(0f, 0f, 0f), 10.0f);
            Assert.Equal(2, inRadius.Count);
            Assert.Contains(e1, inRadius);
            Assert.Contains(e2, inRadius);
            Assert.DoesNotContain(e3, inRadius);

            // Nearest entity
            var nearest = grid.GetNearestEntity(new Vector3(45f, 0f, 45f));
            Assert.NotNull(nearest);
            Assert.Equal(3u, nearest.ServerId);

            // Nearest with filter
            var nearestMonster = grid.GetNearestEntity(new Vector3(0f, 0f, 0f), filter: EntityType.Monster);
            Assert.NotNull(nearestMonster);
            Assert.Equal(2u, nearestMonster.ServerId);

            // Cone search (forward along +X)
            var forward = new Vector3(1f, 0f, 0f);
            var inCone = grid.GetEntitiesInCone(new Vector3(0f, 0f, 0f), forward, maxAngleDegrees: 60.0f, maxDistance: 20.0f);
            Assert.Contains(e2, inCone); // (5, 0, 5) is 45 degrees from (1, 0, 0)
            Assert.DoesNotContain(e3, inCone); // Too far

            // Entity movement across cells
            e1.Position = new Vector3(100f, 0f, 100f);
            grid.InsertOrUpdate(e1);

            var inOldCell = grid.GetEntitiesInRadius(new Vector3(0f, 0f, 0f), 2.0f);
            Assert.Empty(inOldCell);

            var inNewCell = grid.GetEntitiesInRadius(new Vector3(100f, 0f, 100f), 2.0f);
            Assert.Single(inNewCell);
            Assert.Same(e1, inNewCell[0]);
        }

        [Fact]
        public void WorldState_DeadReckoning_ProjectsPositionCorrectly()
        {
            var entity = new WorldEntity(1, 1, EntityType.Player)
            {
                Position = new Vector3(0f, 0f, 0f),
                Direction = 0, // 0 radians = East (+X)
                Speed = 50     // 5.0 yalms/sec
            };

            var projected = WorldState.ProjectPosition(entity, TimeSpan.FromSeconds(2.0));

            // Should be 10 yalms forward along X
            Assert.Equal(10.0f, projected.X, 2);
            Assert.Equal(0.0f, projected.Y, 2);
            Assert.Equal(0.0f, projected.Z, 2);

            // Zero speed shouldn't move
            entity.Speed = 0;
            var stationary = WorldState.ProjectPosition(entity, TimeSpan.FromSeconds(2.0));
            Assert.Equal(Vector3.Zero, stationary);
        }

        [Fact]
        public void LocalPlayerState_UpdatesVitalsAndStatsFromPackets()
        {
            var state = new LocalPlayerState();
            bool vitalsFired = false;
            bool statsFired = false;
            bool buffsFired = false;

            state.VitalsUpdated += () => vitalsFired = true;
            state.StatsUpdated += () => statsFired = true;
            state.BuffsUpdated += () => buffsFired = true;

            // Simulate CliStatus
            byte[] cliPayload = new byte[0x64];
            BinaryPrimitives.WriteInt32LittleEndian(cliPayload.AsSpan(0, 4), 1200); // HP
            BinaryPrimitives.WriteInt32LittleEndian(cliPayload.AsSpan(4, 4), 400);  // MP
            cliPayload[8] = (byte)JobId.Paladin;
            cliPayload[9] = 75;
            BinaryPrimitives.WriteInt16LittleEndian(cliPayload.AsSpan(44, 2), 350); // Attack
            BinaryPrimitives.WriteInt16LittleEndian(cliPayload.AsSpan(46, 2), 420); // Defense

            var cliStatus = new S2C_0x061_CliStatus(cliPayload);
            state.UpdateFromCliStatus(cliStatus);

            Assert.True(statsFired);
            Assert.Equal(1200, state.MaxHp);
            Assert.Equal(400, state.MaxMp);
            Assert.Equal(JobId.Paladin, state.MainJob);
            Assert.Equal(75, state.MainJobLevel);
            Assert.Equal(350, state.Attack);
            Assert.Equal(420, state.Defense);

            // Update vitals
            state.UpdateVitals(600, 200, 1000);
            Assert.True(vitalsFired);
            Assert.Equal(600, state.CurrentHp);
            Assert.Equal(50, state.Hpp); // 600 / 1200 = 50%
            Assert.Equal(1000, state.CurrentTp);

            // Simulate CharStatus (Buffs)
            byte[] statusPayload = new byte[0x60];
            statusPayload[0] = 33; // Haste buff
            statusPayload[1] = 40; // Protect buff

            var charStatus = new S2C_0x037_CharStatus(statusPayload);
            state.UpdateFromCharStatus(charStatus);

            Assert.True(buffsFired);
            Assert.True(state.HasStatusEffect(33));
            Assert.True(state.HasStatusEffect(40));
            Assert.False(state.HasStatusEffect(99));
        }

        [Fact]
        public async Task EntityPacketModule_CoordinatesInboundAndOutboundPackets()
        {
            var world = new WorldState();
            var localPlayer = new LocalPlayerState();
            var dispatcher = new PacketDispatcher();
            var sentPackets = new List<byte[]>();

            Task SendCallback(ReadOnlyMemory<byte> mem, bool isHighPriority)
            {
                sentPackets.Add(mem.ToArray());
                return Task.CompletedTask;
            }

            var module = new EntityPacketModule(world, localPlayer, SendCallback);
            module.Register(dispatcher);

            // 1. Dispatch S2C 0x00D (PC update)
            byte[] pcPayload = new byte[0x70];
            BinaryPrimitives.WriteUInt32LittleEndian(pcPayload.AsSpan(0, 4), 0x778899);
            BinaryPrimitives.WriteUInt16LittleEndian(pcPayload.AsSpan(4, 2), 15);
            pcPayload[6] = (byte)(EntityUpdateFlags.Position | EntityUpdateFlags.Name);
            BinaryPrimitives.WriteSingleLittleEndian(pcPayload.AsSpan(8, 4), 25.0f);
            BinaryPrimitives.WriteSingleLittleEndian(pcPayload.AsSpan(12, 4), 35.0f);
            BinaryPrimitives.WriteSingleLittleEndian(pcPayload.AsSpan(16, 4), 15.0f);
            Encoding.ASCII.GetBytes("Kupo").CopyTo(pcPayload.AsSpan(0x56));

            var pcHeader = new PacketHeader(0x00D, (ushort)(pcPayload.Length + 4), 1);
            dispatcher.Dispatch(pcHeader, pcPayload);

            Assert.Equal(1, world.Count);
            Assert.True(world.TryGetByServerId(0x778899, out var foundPc));
            Assert.NotNull(foundPc);
            Assert.Equal("Kupo", foundPc.Name);
            Assert.Equal(25.0f, foundPc.Position.X);

            // 2. Dispatch S2C 0x00E (NPC update)
            byte[] npcPayload = new byte[0x50];
            BinaryPrimitives.WriteUInt32LittleEndian(npcPayload.AsSpan(0, 4), 0x334455);
            BinaryPrimitives.WriteUInt16LittleEndian(npcPayload.AsSpan(4, 2), 2000); // Monster index
            npcPayload[6] = (byte)(EntityUpdateFlags.Position | EntityUpdateFlags.Name);
            BinaryPrimitives.WriteSingleLittleEndian(npcPayload.AsSpan(8, 4), 50.0f);
            BinaryPrimitives.WriteSingleLittleEndian(npcPayload.AsSpan(12, 4), 60.0f);
            BinaryPrimitives.WriteSingleLittleEndian(npcPayload.AsSpan(16, 4), 10.0f);
            Encoding.ASCII.GetBytes("WildRabbit").CopyTo(npcPayload.AsSpan(0x30));

            var npcHeader = new PacketHeader(0x00E, (ushort)(npcPayload.Length + 4), 2);
            dispatcher.Dispatch(npcHeader, npcPayload);

            Assert.Equal(2, world.Count);
            Assert.True(world.TryGetByServerId(0x334455, out var foundNpc));
            Assert.NotNull(foundNpc);
            Assert.Equal(EntityType.Monster, foundNpc.Type);
            Assert.Equal("WildRabbit", foundNpc.Name);

            // 3. Outbound request
            await module.RequestEntityInfoAsync(2000);
            Assert.Single(sentPackets);
            ushort outboundId = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(sentPackets[0].AsSpan(0, 2)) & 0x1FF);
            Assert.Equal(0x016, outboundId);
        }

        [Fact]
        public void WorldEntity_InterpolatePosition_SmoothlyAdvancesTowardsTarget()
        {
            var entity = new WorldEntity(0x555, 100, EntityType.Monster)
            {
                Position = new Vector3(10f, 0f, 10f),
                TargetPosition = new Vector3(12f, 0f, 10f),
                Speed = 40 // 4.0 yalms/sec
            };

            // At 60 FPS (dt = 1/60s = ~0.0167s)
            float dt = 1.0f / 60.0f;
            entity.InterpolatePosition(dt);

            // Position should have advanced towards (12, 0, 10), but not yet reached it
            Assert.True(entity.Position.X > 10.0f);
            Assert.True(entity.Position.X < 12.0f);
            Assert.Equal(10.0f, entity.Position.Z);

            // After advancing enough time (~1.5s > 1.35s duration), it should reach TargetPosition exactly
            for (int i = 0; i < 90; i++)
            {
                entity.InterpolatePosition(dt);
            }
            Assert.Equal(12.0f, entity.Position.X);
            Assert.Equal(10.0f, entity.Position.Z);
        }

        [Fact]
        public void WorldEntity_InterpolatePosition_TravelsAtEntitySpeed()
        {
            var entity = new WorldEntity(0x556, 101, EntityType.Monster)
            {
                Position = new Vector3(10f, 0f, 10f),
                TargetPosition = new Vector3(15f, 0f, 10f),
                Speed = 40 // 4.0 yalms/sec
            };

            // 0.5s at 60 FPS covers 2 yalms at 4 yalms/sec
            float dt = 1.0f / 60.0f;
            for (int i = 0; i < 30; i++)
            {
                entity.InterpolatePosition(dt);
            }

            Assert.Equal(12.0f, entity.Position.X, 2);
        }

        [Fact]
        public void WorldEntity_SlowNpcWalk_PlaysContinuouslyAtObservedPace()
        {
            // Captured NPC pattern: walk speed 40 on the wire, but it advances only ~1 yalm per ~1.4s update
            var entity = new WorldEntity(0x560, 105, EntityType.Npc) { Position = new Vector3(0f, 0f, 0f), Speed = 40 };
            double now = 500.0;
            float[] gaps = { 1.40f, 1.35f, 1.44f, 1.33f, 1.47f, 1.35f, 1.38f, 1.52f };
            var translating = new List<bool>();
            float x = 0f;
            foreach (float gap in gaps)
            {
                x += 1.0f;
                entity.AddServerSample(new Vector3(x, 0f, 0f), isMoving: true, movTime: 0, direction: 0, now);
                int frames = (int)MathF.Round(gap * 60f);
                for (int i = 0; i < frames; i++)
                {
                    now += 1.0 / 60.0;
                    entity.InterpolatePosition(1.0f / 60.0f, now);
                    translating.Add(entity.IsTranslating);
                }
            }

            // Once it starts walking it keeps walking at the observed pace instead of dashing 1 yalm and idling
            int first = translating.IndexOf(true);
            Assert.True(first >= 0);
            Assert.DoesNotContain(false, translating.GetRange(first, translating.Count - first));
        }

        [Fact]
        public void WorldEntity_LateSample_ContinuesFromHeldPositionWithoutPopping()
        {
            var entity = new WorldEntity(0x561, 106, EntityType.Npc) { Position = new Vector3(0f, 0f, 0f), Speed = 40 };
            double now = 500.0;
            entity.AddServerSample(new Vector3(2f, 0f, 0f), isMoving: true, movTime: 0, direction: 0, now);
            for (int i = 0; i < 300; i++) // 5s: playback catches up to the only sample and holds there
            {
                now += 1.0 / 60.0;
                entity.InterpolatePosition(1.0f / 60.0f, now);
            }
            Assert.Equal(2f, entity.Position.X, 3);

            // A sample far behind the playback clock is played from the held position at movement speed, not jumped to
            entity.AddServerSample(new Vector3(6f, 0f, 0f), isMoving: true, movTime: 0, direction: 0, now);
            now += 1.0 / 60.0;
            entity.InterpolatePosition(1.0f / 60.0f, now);
            Assert.InRange(entity.Position.X, 2f, 2.2f);
        }

        [Fact]
        public void WorldEntity_HeadingDerivation_FacesDirectionOfTravelWhileMoving()
        {
            var entity = new WorldEntity(0x557, 102, EntityType.Player)
            {
                Position = new Vector3(10f, 0f, 10f),
                TargetPosition = new Vector3(10f, 0f, 20f), // Moving North (+Z)
                Speed = 40
            };

            // Interpolate a step
            entity.InterpolatePosition(0.1f);

            // On the wire, North (+Z) is Direction 192 (270 degrees, 3pi/2 radians)
            Assert.Equal(192, entity.Direction);
            // Render heading is smoothed along the shortest arc, so it may be unwrapped below zero; compare modulo a full turn.
            float renderHeading = ((entity.RenderHeadingRadians % (MathF.PI * 2.0f)) + (MathF.PI * 2.0f)) % (MathF.PI * 2.0f);
            Assert.InRange(renderHeading, 3.5f, 5.5f);

            // Now turn and move South (-Z)
            entity.TargetPosition = new Vector3(10f, 0f, 0f);
            entity.InterpolatePosition(0.1f);

            // South (-Z) is Direction 64 (90 degrees, pi/2 radians)
            Assert.Equal(64, entity.Direction);
        }

        [Fact]
        public void WorldEntity_Monster_DoesNotExtrapolatePastDestination()
        {
            var entity = new WorldEntity(0x558, 103, EntityType.Monster)
            {
                Position = new Vector3(0f, 0f, 0f),
                TargetPosition = new Vector3(10f, 0f, 0f),
                Speed = 40,
                LastMovTime = 2000
            };

            // Advance 3.0s, past the 2.5s the 10-yalm trip takes at 4 yalms/sec
            for (int i = 0; i < 180; i++) // 180 frames at 60 FPS = 3.0s
            {
                entity.InterpolatePosition(1.0f / 60.0f);
            }

            // Monsters must clamp at destination (10.0) without extrapolating into the void
            Assert.Equal(10.0f, entity.Position.X, 2);
        }

        [Fact]
        public void WorldEntity_Monster_PreservesWireDirectionEvenWhileMoving()
        {
            var entity = new WorldEntity(0x559, 104, EntityType.Monster)
            {
                Position = new Vector3(0f, 0f, 0f),
                TargetPosition = new Vector3(10f, 0f, 0f),
                Speed = 40,
                Direction = 128 // Facing West according to server wire packet
            };

            entity.InterpolatePosition(0.1f);

            // Server wire direction must NOT be overwritten by travel vector
            Assert.Equal(128, entity.Direction);
        }

        [Fact]
        public void WorldEntity_RunningPlayer_NeverPassesLatestServerPosition()
        {
            var entity = new WorldEntity(0x101, 50, EntityType.Player)
            {
                Position = new Vector3(0f, 0f, 0f),
                TargetPosition = new Vector3(5f, 0f, 0f), // Moving East (+X)
                Speed = 50,
                LastMovTime = 38000
            };

            // Run well past the 1.0s the trip takes, as if the next update were late
            for (int i = 0; i < 90; i++)
            {
                entity.InterpolatePosition(1.0f / 60.0f);
            }

            // Holds at the server position instead of coasting beyond it (which forced a run back on the next update)
            Assert.Equal(5.0f, entity.Position.X);
            // East (+X) is Direction 0
            Assert.Equal(0, entity.Direction);
        }

        [Fact]
        public void WorldEntity_EngagedPlayerStrafing_ClassifiesSideFromHeading()
        {
            // Engaged players keep their server facing and strafe; facing +X, travel toward -Z is to their right
            // (the same side a local character strafes to with E).
            var entity = new WorldEntity(0x558, 103, EntityType.Player)
            {
                Position = new Vector3(0f, 0f, 0f),
                TargetPosition = new Vector3(0f, 0f, -5f),
                Direction = 0,
                AnimationState = 1,
                Speed = 40
            };

            entity.InterpolatePosition(0.1f);

            Assert.Equal(0, entity.Direction);
            Assert.Equal(LocomotionDirection.Right, entity.LocomotionDirection);
        }

        [Fact]
        public void WorldEntity_Stationary_DoesNotOverwriteWireDirection()
        {
            var entity = new WorldEntity(0x102, 51, EntityType.Npc)
            {
                Position = new Vector3(5f, 0f, 5f),
                TargetPosition = new Vector3(5f, 0f, 5f),
                Speed = 0,
                LastMovTime = 1,
                Direction = 192 // Facing North
            };

            entity.InterpolatePosition(0.1f);

            // Should preserve wire direction when stopped
            Assert.Equal(192, entity.Direction);
        }
    }
}
