// tests/Gordian.Core.Tests/Graphics/ActorEffectsTests.cs
using System;
using System.Linq;
using System.Numerics;
using Gordian.Core.Graphics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.Tests.Resources;
using Xunit;

namespace Gordian.Core.Tests.Graphics
{
    public class ActorEffectsTests
    {
        /// <summary>
        /// A model DAT like the Home Point's: an auto-running actor-attached generator (idle) and one started only by the
        /// routine <c>bind</c>, both drawing particle mesh <c>umi1</c>.
        /// </summary>
        private static byte[] BuildModelDat() => ZoneWaterGeneratorTests.Concat(
            ZoneWaterGeneratorTests.BuildChunk(DatSectionType.ParticleMesh, ParticleMeshDecoderTests.BuildParticleMeshPayload("effect  umi1", 0x40), "umi1"),
            ZoneWaterGeneratorTests.BuildChunk(DatSectionType.ParticleGenerator,
                ZoneWaterGeneratorTests.BuildSyntheticWaterGeneratorPayload("idl0", "umi1", new Vector3(0f, -1f, 0f), Vector2.Zero,
                    maxLifeSpan: 120, autoRun: true, attach: ParticleAttachType.SourceActor), "idl0"),
            ZoneWaterGeneratorTests.BuildChunk(DatSectionType.ParticleGenerator,
                ZoneWaterGeneratorTests.BuildSyntheticWaterGeneratorPayload("act0", "umi1", Vector3.Zero, Vector2.Zero,
                    maxLifeSpan: 30, attach: ParticleAttachType.TargetActor), "act0"),
            ZoneWaterGeneratorTests.BuildChunk(DatSectionType.EffectRoutine,
                EffectRoutineDecoderTests.BuildRoutinePayload(40, ("act0", 10, 5)), "bind"));

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
                ParticleMeshDecoderTests.BuildParticleMeshPayload("effect  umi1", 0x40), "umi1");

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
    }
}
