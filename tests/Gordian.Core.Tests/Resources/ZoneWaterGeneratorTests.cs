// tests/Gordian.Core.Tests/Resources/ZoneWaterGeneratorTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class ZoneWaterGeneratorTests
    {
        [Theory]
        [InlineData("shi1", true)]
        [InlineData("shi2", true)]
        [InlineData("shi5", true)]
        [InlineData("hum1", true)]
        [InlineData("humt", true)]
        [InlineData("mizu", true)]
        [InlineData("hna0", true)]
        [InlineData("tree", false)]
        [InlineData("rock", false)]
        public void IsWaterGenerator_IdentifiesWaterParticleGenerators(string name, bool expected)
        {
            bool actual = ZoneDataLoader.IsWaterGenerator(name);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void ParseZoneContainer_InfiniteLifeGeneratorBecomesWorldEffectLayer()
        {
            // Section 0x2E mesh "rip1" drawn by an infinite-life (max life span 0) generator "shi1" with UV scroll.
            byte[] meshSection = BuildChunk(DatSectionType.ZoneMesh, BuildSyntheticZoneMeshPayload("rip1", "umi1"), "rip1");
            byte[] genPayload = BuildSyntheticWaterGeneratorPayload("shi1", "rip1", new Vector3(10f, 2f, 20f), new Vector2(-0.01f, -0.03f));
            byte[] genSection = BuildChunk(DatSectionType.ParticleGenerator, genPayload, "shi1");

            var zone = ZoneDataLoader.ParseZoneContainer(Concat(meshSection, genSection), zoneId: 4,
                outTextures: new Dictionary<string, DecodedTexture>(StringComparer.OrdinalIgnoreCase));

            // Drawn as a generator-driven world effect at the generator's display-space position.
            var effect = Assert.Single(zone.EffectLayers);
            Assert.True(effect.IsWorldEffect);
            Assert.False(effect.IsParticleMesh);
            Assert.Equal("rip1", effect.DatId);
            Assert.Equal("umi1", effect.TextureName);
            Assert.Equal(new Vector2(-0.01f, -0.03f), effect.UVScroll);
            Assert.Equal(new Vector3(-10f, -2f, 20f), effect.Position);
            Assert.NotEmpty(effect.MeshGroups);
        }

        [Fact]
        public void ParseZoneContainer_GeneratorLinkedToParticleMeshBecomesWorldEffectLayer()
        {
            byte[] meshSection = BuildChunk(DatSectionType.ParticleMesh,
                ParticleMeshDecoderTests.BuildParticleMeshPayload("effect  umi1", 0x40), "umi1");
            byte[] genSection = BuildChunk(DatSectionType.ParticleGenerator,
                BuildSyntheticWaterGeneratorPayload("umi1", "umi1", Vector3.Zero, new Vector2(0f, -0.0008f)), "umi1");

            var zone = ZoneDataLoader.ParseZoneContainer(Concat(meshSection, genSection), zoneId: 4);

            var effect = Assert.Single(zone.EffectLayers);
            Assert.True(effect.IsParticleMesh);
            Assert.Equal("effect  umi1", effect.TextureName);
            Assert.Equal(new Vector2(0f, -0.0008f), effect.UVScroll);
        }

        [Fact]
        public void ParseZoneContainer_FiniteLifeGeneratorIsNotInstancedStatically()
        {
            byte[] meshSection = BuildChunk(DatSectionType.ZoneMesh, BuildSyntheticZoneMeshPayload("rip1", "umi1"), "rip1");
            byte[] genPayload = BuildSyntheticWaterGeneratorPayload("shi1", "rip1", Vector3.Zero, Vector2.Zero, maxLifeSpan: 300);
            byte[] genSection = BuildChunk(DatSectionType.ParticleGenerator, genPayload, "shi1");

            var zone = ZoneDataLoader.ParseZoneContainer(Concat(meshSection, genSection), zoneId: 4);

            // Not auto-running and no routine starts it: nothing to run (and a static copy of a surf particle would be wrong).
            Assert.Empty(zone.EffectLayers);
        }

        [Fact]
        public void ParseZoneContainer_AutoRunFiniteLifeGeneratorBecomesParticleEmitter()
        {
            byte[] meshSection = BuildChunk(DatSectionType.ZoneMesh, BuildSyntheticZoneMeshPayload("rip1", "umi1"), "rip1");
            byte[] genPayload = BuildSyntheticWaterGeneratorPayload("uma1", "rip1", new Vector3(-360f, 0f, -418f), Vector2.Zero, maxLifeSpan: 500, autoRun: true);
            byte[] genSection = BuildChunk(DatSectionType.ParticleGenerator, genPayload, "uma1");

            var zone = ZoneDataLoader.ParseZoneContainer(Concat(meshSection, genSection), zoneId: 4);

            var effect = Assert.Single(zone.EffectLayers);
            Assert.NotNull(effect.Emitter);
            Assert.Null(effect.Emitter.Schedule);
            Assert.Equal(new Vector3(-360f, 0f, -418f), effect.Emitter.RawBasePosition);
        }

        [Fact]
        public void ParseZoneContainer_RoutineStartedGeneratorBecomesScheduledEmitter()
        {
            byte[] meshSection = BuildChunk(DatSectionType.ZoneMesh, BuildSyntheticZoneMeshPayload("rip1", "umi1"), "rip1");
            byte[] genSection = BuildChunk(DatSectionType.ParticleGenerator,
                BuildSyntheticWaterGeneratorPayload("kwa1", "rip1", Vector3.Zero, Vector2.Zero, maxLifeSpan: 500), "kwa1");
            // Bibiki Bay's umi2/s000 loops on completion (op 0x01 in its third list): the client starts it on zone load.
            byte[] routineSection = BuildChunk(DatSectionType.EffectRoutine,
                EffectRoutineDecoderTests.WithLoopOnComplete(EffectRoutineDecoderTests.BuildRoutinePayload(2669, ("kwa1", 985, 498), ("kwa2", 1684, 598))), "s000");

            var zone = ZoneDataLoader.ParseZoneContainer(Concat(meshSection, genSection, routineSection), zoneId: 4);

            var effect = Assert.Single(zone.EffectLayers);
            Assert.NotNull(effect.Emitter);
            Assert.Equal(new[] { new EffectRoutineSpawn("kwa1", 0, 498) }, effect.Emitter.Schedule);
            Assert.Equal(2669, effect.Emitter.ScheduleLoopFrames);
        }

        [Fact]
        public void ParseZoneContainer_OnDemandRoutineLeavesItsGeneratorIdleForTheMapScheduler()
        {
            // The same routine without the loop flag (Alzadaal's portal 1pa2, #210): it waits for a map scheduler.
            byte[] meshSection = BuildChunk(DatSectionType.ZoneMesh, BuildSyntheticZoneMeshPayload("rip1", "umi1"), "rip1");
            byte[] genSection = BuildChunk(DatSectionType.ParticleGenerator,
                BuildSyntheticWaterGeneratorPayload("kwa1", "rip1", Vector3.Zero, Vector2.Zero, maxLifeSpan: 500), "kwa1");
            byte[] routineSection = BuildChunk(DatSectionType.EffectRoutine,
                EffectRoutineDecoderTests.BuildRoutinePayload(2669, ("kwa1", 985, 498)), "1pa2");

            var zone = ZoneDataLoader.ParseZoneContainer(Concat(meshSection, genSection, routineSection), zoneId: 72);

            var effect = Assert.Single(zone.EffectLayers);
            Assert.NotNull(effect.Emitter);
            Assert.Null(effect.Emitter.Schedule);
            var (_, routine) = Assert.Single(zone.MapRoutines.Find("1pa2"));
            Assert.Equal(2669, routine.TotalFrames);
            Assert.True(zone.MapRoutines.TryResolveGenerator(string.Empty, "kwa1", out var template));
            Assert.Same(effect.Emitter, template);
        }

        [Fact]
        public void ParseZoneContainer_NeverExpiringGeneratorOfAnOnDemandRoutineWaitsAsAnIdleEmitter()
        {
            // Alzadaal's portal glow g0b1 (max life span 0, not auto-running) is spawned only by the idle routine 1pa1
            // (0 frames, on demand): it must not be drawn until 1pa1 plays, and must go with 1pak's kill (#225).
            byte[] meshSection = BuildChunk(DatSectionType.ZoneMesh, BuildSyntheticZoneMeshPayload("rip1", "umi1"), "rip1");
            byte[] genSection = BuildChunk(DatSectionType.ParticleGenerator,
                BuildSyntheticWaterGeneratorPayload("g0b1", "rip1", new Vector3(1f, 2f, 3f), Vector2.Zero, maxLifeSpan: 0), "g0b1");
            byte[] routineSection = BuildChunk(DatSectionType.EffectRoutine, EffectRoutineDecoderTests.BuildRoutinePayload(0, ("g0b1", 0, 0)), "1pa1");

            var zone = ZoneDataLoader.ParseZoneContainer(Concat(meshSection, genSection, routineSection), zoneId: 72);

            var effect = Assert.Single(zone.EffectLayers);
            Assert.NotNull(effect.Emitter);
            Assert.Null(effect.Emitter.Schedule);
            Assert.True(zone.MapRoutines.TryResolveGenerator(string.Empty, "g0b1", out var template));
            Assert.Same(effect.Emitter, template);

            // Nothing is drawn until the routine plays; then one never-expiring particle, until a kill.
            var emitter = new Gordian.Core.Graphics.ZoneParticleEmitter(effect.Emitter);
            var frame = new Gordian.Core.Graphics.ZoneParticleFrame(Vector3.Zero, 0.5f, Vector3.One);
            emitter.Update(600f, frame);
            Assert.Empty(emitter.Particles);
            var player = new Gordian.Core.Graphics.ZoneRoutinePlayer(zone.MapRoutines);
            Assert.True(player.Play("1pa1"));
            player.Update(1f, t => ReferenceEquals(t, effect.Emitter) ? emitter : null);
            emitter.Update(600f, frame);
            var particle = Assert.Single(emitter.Particles);
            Assert.Equal(float.PositiveInfinity, particle.MaxAge);
            emitter.Kill();
            Assert.Empty(emitter.Particles);
        }

        [Fact]
        public void ParseZoneContainer_NeverExpiringGeneratorOfAZoneLoadRoutineStaysAStaticLayer()
        {
            // The same generator started by a routine that loops on completion keeps the static layer (#225 item 2).
            byte[] meshSection = BuildChunk(DatSectionType.ZoneMesh, BuildSyntheticZoneMeshPayload("rip1", "umi1"), "rip1");
            byte[] genSection = BuildChunk(DatSectionType.ParticleGenerator,
                BuildSyntheticWaterGeneratorPayload("umi1", "rip1", Vector3.Zero, Vector2.Zero, maxLifeSpan: 0), "umi1");
            byte[] routineSection = BuildChunk(DatSectionType.EffectRoutine,
                EffectRoutineDecoderTests.WithLoopOnComplete(EffectRoutineDecoderTests.BuildRoutinePayload(2669, ("umi1", 0, 0))), "s000");

            var zone = ZoneDataLoader.ParseZoneContainer(Concat(meshSection, genSection, routineSection), zoneId: 4);

            var effect = Assert.Single(zone.EffectLayers);
            Assert.Null(effect.Emitter);
            Assert.False(effect.IsParticleMesh);
        }

        [Fact]
        public void ParseZoneContainer_MatchesRoutinesAndGeneratorsByTheirFullDirectoryPath()
        {
            // s_pa/effe/nami and s_pa/door/_030/nami hold generators of the same names with their own lop0 schedules
            // (#81): matched by the leaf name, each generator took both folders' spawns and emitted twice.
            byte[] mesh = BuildChunk(DatSectionType.ZoneMesh, BuildSyntheticZoneMeshPayload("rip1", "umi1"), "rip1");
            byte[] Dir(string name) => BuildChunk(DatSectionType.Directory, Array.Empty<byte>(), name);
            byte[] end = BuildChunk(DatSectionType.End, Array.Empty<byte>(), "end");
            byte[] gen = BuildChunk(DatSectionType.ParticleGenerator, BuildSyntheticWaterGeneratorPayload("yk01", "rip1", Vector3.Zero, Vector2.Zero, maxLifeSpan: 500), "yk01");
            // A command's delay is the wait after it: a leading dummy spawn puts yk01 at its delay.
            byte[] Loop(ushort delay) => BuildChunk(DatSectionType.EffectRoutine,
                EffectRoutineDecoderTests.WithLoopOnComplete(EffectRoutineDecoderTests.BuildRoutinePayload(1000, ("none", delay, 0), ("yk01", 0, 100))), "lop0");

            var zone = ZoneDataLoader.ParseZoneContainer(Concat(mesh,
                Dir("s_pa"), Dir("effe"), Dir("nami"), gen, Loop(0), end, end,
                Dir("door"), Dir("_030"), Dir("nami"), gen, Loop(50), end, end, end, end), zoneId: 3);

            Assert.Equal(2, zone.EffectLayers.Count);
            foreach (var layer in zone.EffectLayers)
            {
                var spawn = Assert.Single(layer.Emitter!.Schedule!);
                Assert.Equal(1000, layer.Emitter.ScheduleLoopFrames);
                Assert.Equal("yk01", spawn.GeneratorId);
            }
            Assert.Contains(zone.EffectLayers, l => l.Emitter!.Schedule![0].StartFrame == 0);
            Assert.Contains(zone.EffectLayers, l => l.Emitter!.Schedule![0].StartFrame == 50);
        }

        [Fact]
        public void ParseZoneContainer_TimedRoutineSchedulesItsGeneratorOnTheVanadielClock()
        {
            // The ducks kamo/s001 (#81): op 0x52 10:30-11:24, replayed 18-129.6 s apart, spawning kamo for 5,400 frames.
            byte[] mesh = BuildChunk(DatSectionType.ZoneMesh, BuildSyntheticZoneMeshPayload("rip1", "umi1"), "rip1");
            byte[] gen = BuildChunk(DatSectionType.ParticleGenerator, BuildSyntheticWaterGeneratorPayload("kamo", "rip1", Vector3.Zero, Vector2.Zero, maxLifeSpan: 300), "kamo");
            byte[] routine = BuildChunk(DatSectionType.EffectRoutine, EffectRoutineDecoderTests.BuildPayload(5400, new List<byte[]>
            {
                EffectRoutineDecoderTests.TimedReplayCommand(1512000, 1641600, 129600, 18000),
                EffectRoutineDecoderTests.Command(0x02, 5400, 5400, "kamo"),
            }), "s001");

            var zone = ZoneDataLoader.ParseZoneContainer(Concat(mesh, gen, routine), zoneId: 240);

            var effect = Assert.Single(zone.EffectLayers);
            var spawn = Assert.Single(effect.Emitter!.Schedule!);
            Assert.Equal(0, effect.Emitter.ScheduleLoopFrames); // not a loop at its own length any more
            Assert.NotNull(spawn.Timer);
            Assert.Equal("10:30-11:24", Assert.Single(spawn.Timer.Windows).ToString());

            var emitter = new Gordian.Core.Graphics.ZoneParticleEmitter(effect.Emitter);
            for (int i = 0; i < 300; i++) emitter.Update(1f, new Gordian.Core.Graphics.ZoneParticleFrame(Vector3.Zero, 9f / 24f, Vector3.One));
            Assert.Empty(emitter.Particles);
            emitter.Update(1f, new Gordian.Core.Graphics.ZoneParticleFrame(Vector3.Zero, 11f / 24f, Vector3.One));
            Assert.NotEmpty(emitter.Particles);
        }

        [Fact]
        public void ParseZoneContainer_ChildGeneratorGetsAChildOnlyEmitterLinkedToItsParent()
        {
            byte[] meshSection = BuildChunk(DatSectionType.ZoneMesh, BuildSyntheticZoneMeshPayload("rip1", "umi1"), "rip1");
            byte[] parentSection = BuildChunk(DatSectionType.ParticleGenerator,
                BuildSyntheticWaterGeneratorPayload("par1", "rip1", Vector3.Zero, Vector2.Zero, maxLifeSpan: 100, autoRun: true, onceChildId: "kid1"), "par1");
            // The child is not auto-running, so it only exists as the parent's child.
            byte[] childSection = BuildChunk(DatSectionType.ParticleGenerator,
                BuildSyntheticWaterGeneratorPayload("kid1", "rip1", Vector3.Zero, Vector2.Zero, maxLifeSpan: 50), "kid1");

            var zone = ZoneDataLoader.ParseZoneContainer(Concat(meshSection, parentSection, childSection), zoneId: 4);

            Assert.Equal(2, zone.EffectLayers.Count);
            var parent = Assert.Single(zone.EffectLayers, l => l.Name == "par1");
            var child = Assert.Single(zone.EffectLayers, l => l.Name == "kid1");
            Assert.False(parent.Emitter!.ChildOnly);
            Assert.True(child.Emitter!.ChildOnly);
            Assert.Same(child.Emitter, parent.Emitter.Children["kid1"]);
        }

        internal static byte[] Concat(params byte[][] sections)
        {
            var all = new List<byte>();
            foreach (var section in sections) all.AddRange(section);
            return all.ToArray();
        }

        private static byte[] BuildSyntheticZoneMeshPayload(string meshName, string texName)
        {
            int sm = 0x20;
            int numVerts = 3;
            int vertStride = 36;
            int numIndices = 3;

            int totalSize = sm + 20 + (numVerts * vertStride) + 4 + (numIndices * 2) + 16;
            byte[] payload = new byte[totalSize];

            Encoding.ASCII.GetBytes(meshName).CopyTo(payload.AsSpan(0x10, Math.Min(16, meshName.Length)));
            Encoding.ASCII.GetBytes(texName).CopyTo(payload.AsSpan(sm, Math.Min(16, texName.Length)));

            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(sm + 16, 2), (ushort)numVerts);

            int vStart = sm + 20;
            for (int v = 0; v < numVerts; v++)
            {
                int vo = vStart + (v * vertStride);
                BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(vo, 4), v * 5.0f);
                BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(vo + 4, 4), 0.0f);
                BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(vo + 8, 4), v * 5.0f);
            }

            int idxHeader = vStart + (numVerts * vertStride);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(idxHeader, 2), (ushort)numIndices);

            int idxStart = idxHeader + 4;
            for (int i = 0; i < numIndices; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(idxStart + (i * 2), 2), (ushort)i);
            }

            return payload;
        }

        internal static byte[] BuildSyntheticWaterGeneratorPayload(string datId, string linkedId, Vector3 basePos, Vector2 uvScroll, ushort maxLifeSpan = 0, bool autoRun = false, string? onceChildId = null, ParticleAttachType attach = ParticleAttachType.None)
        {
            // Generator payload:
            // Header (128 bytes): +0x48 = DatId (4 chars), +0x70 = Section 1 stream offset, +0x78 = Section 3 stream offset
            // Opcode 0x01 (StandardParticleSetup): config dword (op 0x01, len 10 dwords = 40B), +0x0C=linkedId(4), +0x14=pos(12), +0x21=linkedType(1)
            int op1Offset = 128;
            int childOpSize = onceChildId != null ? 12 : 0;
            int op27Offset = op1Offset + 40 + childOpSize;
            int op28Offset = op27Offset + 8;

            byte[] payload = new byte[128 + 40 + childOpSize + 8 + 8 + 4];
            Encoding.ASCII.GetBytes(datId.PadRight(4).Substring(0, 4)).CopyTo(payload.AsSpan(0x48));

            // Stream offsets relative to section start (including 16B chunk header):
            // streamOffsets[1] = Section 2 (Initializers) at 16 + op1Offset
            // streamOffsets[2] = Section 3 (Particle Updaters) at 16 + op27Offset
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x74, 4), (uint)(16 + op1Offset));
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x78, 4), (uint)(16 + op27Offset));

            // Opcode 0x01: 40 bytes = 10 dwords
            uint op1Config = 0x01 | ((uint)10 << 8);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(op1Offset, 4), op1Config);
            Encoding.ASCII.GetBytes(linkedId.PadRight(4).Substring(0, 4)).CopyTo(payload.AsSpan(op1Offset + 12));
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(op1Offset + 20, 4), basePos.X);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(op1Offset + 24, 4), basePos.Y);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(op1Offset + 28, 4), basePos.Z);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(op1Offset + 34, 2), maxLifeSpan);
            if (autoRun) payload[0x69] = 0x10; // generator flags: auto-run
            payload[0] = (byte)attach; // attach flags: attach type in the low 4 bits
            payload[op1Offset + 33] = (byte)ParticleLinkedDataType.StaticMesh;

            if (onceChildId != null)
            {
                // Opcode 0x3C OnceChildGeneratorSetup: 3 dwords = header, reserved, child DatId.
                int childOffset = op1Offset + 40;
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(childOffset, 4), 0x3Cu | (3u << 8));
                Encoding.ASCII.GetBytes(onceChildId.PadRight(4).Substring(0, 4)).CopyTo(payload.AsSpan(childOffset + 8));
            }

            // Opcode 0x27: Axis U scroll, 8 bytes = 2 dwords
            uint op27Config = 0x27 | ((uint)2 << 8);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(op27Offset, 4), op27Config);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(op27Offset + 4, 4), uvScroll.X);

            // Opcode 0x28: Axis V scroll, 8 bytes = 2 dwords
            uint op28Config = 0x28 | ((uint)2 << 8);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(op28Offset, 4), op28Config);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(op28Offset + 4, 4), uvScroll.Y);

            return payload;
        }

        internal static byte[] BuildChunk(DatSectionType type, byte[] payload, string datId = "test")
        {
            int total = (16 + payload.Length + 15) & ~15;
            byte[] chunk = new byte[total];

            Encoding.ASCII.GetBytes(datId.PadRight(4).Substring(0, 4)).CopyTo(chunk.AsSpan(0, 4));
            uint units = (uint)(total / 16);
            uint meta = ((uint)type & 0x7F) | ((units & 0x7FFFF) << 7);
            BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4, 4), meta);

            payload.CopyTo(chunk.AsSpan(16));
            return chunk;
        }
    }
}
