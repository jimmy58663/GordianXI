// tests/Gordian.Engine.Tests/Graphics/ActorEffectsTests.cs
using System;
using System.Linq;
using System.Numerics;
using Gordian.Engine.Graphics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Gordian.Tests.Shared;
using Gordian.Engine.Tests.Resources;
using Xunit;

namespace Gordian.Engine.Tests.Graphics
{
    public class ActorEffectsTests
    {
        /// <summary>
        /// A model DAT like the Home Point's: an auto-running actor-attached generator (idle) and one started only by the
        /// routine <c>bind</c>, both drawing particle mesh <c>umi1</c>.
        /// </summary>
        private static byte[] BuildModelDat() => ZoneWaterGeneratorTests.Concat(
            ZoneWaterGeneratorTests.BuildChunk(DatSectionType.ParticleMesh, ParticleMeshBuilder.BuildParticleMeshPayload("effect  umi1", 0x40), "umi1"),
            ZoneWaterGeneratorTests.BuildChunk(DatSectionType.ParticleGenerator,
                ZoneWaterGeneratorTests.BuildSyntheticWaterGeneratorPayload("idl0", "umi1", new Vector3(0f, -1f, 0f), Vector2.Zero,
                    maxLifeSpan: 120, autoRun: true, attach: ParticleAttachType.SourceActor), "idl0"),
            ZoneWaterGeneratorTests.BuildChunk(DatSectionType.ParticleGenerator,
                ZoneWaterGeneratorTests.BuildSyntheticWaterGeneratorPayload("act0", "umi1", Vector3.Zero, Vector2.Zero,
                    maxLifeSpan: 30, attach: ParticleAttachType.TargetActor), "act0"),
            ZoneWaterGeneratorTests.BuildChunk(DatSectionType.EffectRoutine,
                EffectRoutineBuilder.BuildRoutinePayload(40, ("act0", 10, 5)), "bind"));

        [Fact]
        public void Load_KeepsActorAttachedGeneratorsAndRoutines()
        {
            var effects = ActorEffectLoader.Load(BuildModelDat());

            Assert.NotNull(effects);
            Assert.Equal(new[] { "act0", "idl0" }, effects!.Layers.Select(l => l.Name).OrderBy(n => n).ToArray());
            var bind = Assert.Contains("bind", effects.Routines);
            Assert.Equal("act0", Assert.Single(bind.Spawns).Template.Definition.DatId);
        }

        /// <summary>
        /// A model or scene DAT's particle mesh colours are used as authored (0x40 alpha stays 0x40); only zone DATs double
        /// them (#208: Port Jeuno 324's blink cards measured about 2 x their colour alpha in retail).
        /// </summary>
        [Fact]
        public void Load_KeepsAuthoredMeshColours()
        {
            var effects = ActorEffectLoader.Load(BuildModelDat())!;

            var vertex = effects.Layers.SelectMany(l => l.MeshGroups).First(g => g.TextureName.Length > 0).Vertices[0];
            Assert.Equal(0x40u, vertex.ColorRgba >> 24);
            Assert.Equal(0x30u, vertex.ColorRgba & 0xFF);
        }

        [Fact]
        public void ZoneParse_KeepsRoutineStartedActorGenerators_AsAttachedEmitters()
        {
            // Read as a zone DAT (#232): act0 (TargetActor), which the routine BIND starts, waits as an actor-attached
            // emitter for the entity a trigger names; idl0 auto-runs on an actor no zone routine names, so it is skipped.
            var zone = ZoneDataLoader.ParseZoneContainer(BuildModelDat(), zoneId: 4);

            var layer = Assert.Single(zone.EffectLayers);
            Assert.Equal("act0", layer.Name);
            Assert.True(layer.IsActorAttached);
            Assert.Equal(ParticleAttachType.TargetActor, layer.AttachType);
            Assert.Empty(zone.ActorRoutines);
        }

        [Fact]
        public void Load_ReturnsNullWithoutGenerators()
        {
            byte[] meshOnly = ZoneWaterGeneratorTests.BuildChunk(DatSectionType.ParticleMesh,
                ParticleMeshBuilder.BuildParticleMeshPayload("effect  umi1", 0x40), "umi1");

            Assert.Null(ActorEffectLoader.Load(meshOnly));
        }

        [Fact]
        public void Instance_RunsIdleGeneratorsAndPlaysRoutinesOnDemand()
        {
            var instance = new ActorEffectInstance(ActorEffectLoader.Load(BuildModelDat())!);
            var frame = new ZoneParticleFrame(new Vector3(0f, -1f, 5f), 0.5f, Vector3.One);
            var idle = instance.Emitters.Single(e => e.Layer.Name == "idl0").Emitter;
            var action = instance.Emitters.Single(e => e.Layer.Name == "act0").Emitter;

            instance.Update(2f, frame);
            Assert.NotEmpty(idle.Particles);
            Assert.Empty(action.Particles);

            // A routine command's delay is the wait after it, so bind starts act0 at once.
            Assert.True(instance.Play("BIND"));
            instance.Update(2f, frame);
            Assert.NotEmpty(action.Particles);

            Assert.False(instance.Play("aper"));
        }

        [Theory]
        [InlineData(ParticleAttachType.SourceActor, 5)]
        [InlineData(ParticleAttachType.SourceActorWeapon, 5)]
        [InlineData(ParticleAttachType.None, 5)]
        [InlineData(ParticleAttachType.TargetActor, 9)]
        [InlineData(ParticleAttachType.TargetActorSourceFacing, 9)]
        [InlineData(ParticleAttachType.TargetToSourceBasis, 9)]
        public void GetJointReference_PicksTheSideOfTheAttachType(ParticleAttachType attach, int expected)
        {
            var generator = new ParticleGeneratorDefinition { AttachType = attach, AttachedJoint0 = 5, AttachedJoint1 = 9 };

            Assert.Equal(expected, ActorEffectInstance.GetJointReference(generator));
        }

        [Fact]
        public void ResolveJointReference_OffsetsTheJointByItsPose()
        {
            var skeleton = new Skeleton(
                new[] { new SkeletonJoint(-1, Quaternion.Identity, Vector3.Zero), new SkeletonJoint(0, Quaternion.Identity, Vector3.Zero) },
                new[] { new JointReference(0, Vector3.Zero), new JointReference(1, new Vector3(1f, 0f, 0f)) });
            var translations = new[] { Vector3.Zero, new Vector3(0f, -2f, 0f) };
            var rotations = new[] { Quaternion.Identity, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f) };
            var scales = new[] { Vector3.One, new Vector3(2f) };

            Vector3 point = Skeleton.ResolveJointReference(skeleton, translations, rotations, scales, 1);

            // Offset (1, 0, 0) scaled by 2 and turned 90 degrees about Y lands at (0, 0, -2), from the joint at (0, -2, 0).
            Assert.Equal(0f, point.X, 4);
            Assert.Equal(-2f, point.Y, 4);
            Assert.Equal(-2f, point.Z, 4);
            Assert.Equal(Vector3.Zero, Skeleton.ResolveJointReference(skeleton, translations, rotations, scales, 7));
            Assert.Equal(Vector3.Zero, Skeleton.ResolveJointReference(null, translations, rotations, scales, 1));
        }
            // ── retail model DATs (#257) ─────────────────────────────────────────────

        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        private static ActorEffectSet? RetailEffects(int fileId)
        {
            if (!System.IO.Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            return rm.LoadDatBytesByFileId(fileId) is { } bytes ? ActorEffectLoader.Load(bytes) : null;
        }

        private static readonly ZoneParticleFrame RetailFrame = new(new Vector3(0f, -1f, 5f), 0.5f, Vector3.One);

        /// <summary>Synthesis Focuser II (model 2335): idle shows no orbs; its blue state routine starts the blue ones only.</summary>
        [Fact]
        public void SynthesisFocuser_IsDarkWhenIdle_AndItsStateRoutineLightsOneColour()
        {
            if (RetailEffects(52630) is not { } effects) return;
            var orbs = effects.Layers.Where(l => l.Name.Length == 4 && "byr".Contains(l.Name[0]) && l.Name[1] is 'g' or 'f' or 'c').ToList();
            Assert.Equal(15, orbs.Count);
            Assert.All(orbs, l => Assert.Contains(l.Emitter!, effects.StateTriggered));

            var instance = new ActorEffectInstance(effects);
            for (int i = 0; i < 120; i++) instance.Update(1f, RetailFrame);
            Assert.All(instance.Emitters.Where(e => orbs.Contains(e.Layer)), e => Assert.Empty(e.Emitter.Particles));

            Assert.True(instance.Play("bslp"));
            for (int i = 0; i < 30; i++) instance.Update(1f, RetailFrame);
            Assert.Contains(instance.Emitters, e => e.Layer.Name == "bgp0" && e.Emitter.Particles.Count > 0);
            Assert.All(instance.Emitters.Where(e => e.Layer.Name[0] is 'y' or 'r' && orbs.Contains(e.Layer)), e => Assert.Empty(e.Emitter.Particles));
        }

        /// <summary>The Synergy Furnace (model 2348) shows no synthesis effect at rest, as in retail (Whitegate, 2026-10-17).</summary>
        [Fact]
        public void SynergyFurnace_ShowsNothingAtRest()
        {
            if (RetailEffects(52643) is not { } effects) return;
            Assert.NotEmpty(effects.StateTriggered);
            var instance = new ActorEffectInstance(effects);
            for (int i = 0; i < 120; i++) instance.Update(1f, RetailFrame);
            Assert.All(instance.Emitters, e => Assert.Empty(e.Emitter.Particles));
        }

        /// <summary>
        /// The Yahse Hunting Grounds campfire (model 2411, #425) burns at rest: its idle state routine ids0 links s000 / s001,
        /// which start the flames. Before, its state-driven generators waited for a routine nothing played, and the campfire
        /// was not drawn at all (its skin mesh is a placeholder).
        /// </summary>
        [Fact]
        public void Campfire_BurnsAtRest()
        {
            if (!System.IO.Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            // As the game loads it: the flames hon0 sample the shared fire sprite hi12.
            var effects = rm.GetActorEffects(2411)!;
            Assert.Equal(new[] { "s000", "s001" }, effects.IdleStateLinks);
            var instance = new ActorEffectInstance(effects);
            for (int i = 0; i < 60; i++) instance.Update(1f, RetailFrame);
            Assert.Contains(instance.Emitters, e => e.Layer.Name == "hon0" && e.Emitter.Particles.Count > 0);
            Assert.Contains(instance.Emitters, e => e.Layer.Name == "kag0" && e.Emitter.Particles.Count > 0); // the log pile
        }

        [Fact]
        public void ReadRoutineLinks_ListsSameDatLinksInOrder()
        {
            byte[] payload = new byte[0x20 + 0x10 + 0x10 + 8];
            BitConverter.GetBytes(0x20 + 16).CopyTo(payload, 0x14);
            void Link(int at, string id)
            {
                payload[at] = 0x03;
                payload[at + 1] = 4;
                System.Text.Encoding.ASCII.GetBytes(id).CopyTo(payload, at + 8);
            }
            Link(0x20, "s000");
            Link(0x30, "s1");
            var links = new System.Collections.Generic.List<string>();
            ActorEffectLoader.ReadRoutineLinks(payload, links);
            Assert.Equal(new[] { "s000", "s1" }, links);
        }

        /// <summary>The Home Point crystal (model 51) has no state routines: its idle layers still emit from spawn.</summary>
        [Fact]
        public void HomePoint_KeepsItsIdleLayers()
        {
            if (RetailEffects(1351) is not { } effects) return;
            Assert.Empty(effects.StateTriggered);
            var instance = new ActorEffectInstance(effects);
            for (int i = 0; i < 30; i++) instance.Update(1f, RetailFrame);
            Assert.Contains(instance.Emitters, e => e.Layer.Name == "bnd0" && e.Emitter.Particles.Count > 0);
        }
    
        /// <summary>
        /// A weapon DAT shaped like the Afterglow weapons' (#8): <c>!in1</c> starts <c>auon</c>, which repeats <c>lop0</c>
        /// (56 frames: <c>gp00</c> for 50, particles living 30).
        /// </summary>
        private static byte[] BuildAfterglowDat() => ZoneWaterGeneratorTests.Concat(
            ZoneWaterGeneratorTests.BuildChunk(DatSectionType.ParticleMesh, ParticleMeshBuilder.BuildParticleMeshPayload("effect  pi1 ", 0x40), "pi1"),
            ZoneWaterGeneratorTests.BuildChunk(DatSectionType.ParticleGenerator,
                ZoneWaterGeneratorTests.BuildSyntheticWaterGeneratorPayload("gp00", "pi1", Vector3.Zero, Vector2.Zero,
                    maxLifeSpan: 30, attach: ParticleAttachType.SourceActor), "gp00"),
            ZoneWaterGeneratorTests.BuildChunk(DatSectionType.EffectRoutine, EffectRoutineBuilder.BuildRoutinePayload(56, ("gp00", 0, 50)), "lop0"),
            ZoneWaterGeneratorTests.BuildChunk(DatSectionType.EffectRoutine,
                EffectRoutineBuilder.BuildPayload(0, new() { EffectRoutineBuilder.Command(0x73, 0, 0, "lop0") }), "auon"),
            ZoneWaterGeneratorTests.BuildChunk(DatSectionType.EffectRoutine,
                EffectRoutineBuilder.BuildPayload(0, new() { EffectRoutineBuilder.Command(0x03, 0, 0, "auon") }), "!in1"));

        [Fact]
        public void WeaponEquipRoutine_LeadsToItsRepeatingLoop()
        {
            var effects = ActorEffectLoader.Load(BuildAfterglowDat())!;

            var loop = Assert.Single(effects.EquippedRoutines);
            Assert.Equal(("lop0", true), (loop.RoutineId, loop.Repeats));
            Assert.Empty(ActorEffectLoader.Load(BuildModelDat())!.EquippedRoutines);
        }

        /// <summary>
        /// The equipped loop keeps the glow alive past its first 50-frame window (one play alone would leave nothing after
        /// frame 80), and <see cref="ActorEffectInstance.Stop"/> lets the last particles finish their lives.
        /// </summary>
        [Fact]
        public void WeaponEquipLoop_KeepsEmittingUntilStopped()
        {
            var instance = new ActorEffectInstance(ActorEffectLoader.Load(BuildAfterglowDat())!);
            var frame = new ZoneParticleFrame(new Vector3(0f, -1f, 5f), 0.5f, Vector3.One);
            for (int i = 0; i < 300; i++) instance.Update(1f, frame);
            Assert.True(instance.HasParticles);

            instance.Stop();
            Assert.True(instance.IsStopping);
            for (int i = 0; i < 40; i++) instance.Update(1f, frame);
            Assert.False(instance.HasParticles);
        }

        /// <summary>
        /// The retail Afterglow weapons (#8): Ragnarok's 119 Afterglow model for the Hume male (main model 546, file 63677)
        /// carries <c>gp00</c> / <c>fr00</c> and loops <c>lop0</c> while equipped; the plain Ragnarok (319) carries none.
        /// </summary>
        [Fact]
        public void RetailAfterglowWeapons_LoopTheirGlowWhileEquipped()
        {
            if (!System.IO.Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var effects = rm.GetGearEffects(CharacterRace.HumeMale, CharacterSlot.Main, 546);
            Assert.NotNull(effects);
            Assert.Equal(("lop0", true), (effects!.EquippedRoutines[0].RoutineId, effects.EquippedRoutines[0].Repeats));
            Assert.Equal(new[] { "fr00", "gp00" }, effects.Layers.Select(l => l.Name).OrderBy(n => n).ToArray());
            Assert.Null(rm.GetGearEffects(CharacterRace.HumeMale, CharacterSlot.Main, 319));
            // Sub and ranged Afterglow models name their routines !in3 / !in2.
            Assert.Equal("lop1", rm.GetGearEffects(CharacterRace.HumeMale, CharacterSlot.Sub, 556)!.EquippedRoutines[0].RoutineId);
            Assert.Equal("lop2", rm.GetGearEffects(CharacterRace.HumeMale, CharacterSlot.Ranged, 103)!.EquippedRoutines[0].RoutineId);

            var instance = new ActorEffectInstance(effects);
            for (int i = 0; i < 240; i++) instance.Update(1f, RetailFrame);
            Assert.Contains(instance.Emitters, e => e.Layer.Name == "gp00" && e.Emitter.Particles.Count > 0);
        }
}
}
