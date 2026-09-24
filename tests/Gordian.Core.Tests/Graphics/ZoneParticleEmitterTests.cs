// tests/Gordian.Core.Tests/Graphics/ZoneParticleEmitterTests.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Graphics;
using Gordian.Core.Resources.Graphics;
using Xunit;

namespace Gordian.Core.Tests.Graphics
{
    public class ZoneParticleEmitterTests
    {
        private static readonly ZoneParticleFrame Frame = new(Vector3.Zero, 0.5f, Vector3.One);

        /// <summary>
        /// A surf-like generator: emits every <paramref name="framesPerEmission"/> frames, particles live
        /// <paramref name="life"/> frames, start at alpha 0 (0x80 grey) and fade in over their life via a progress curve.
        /// </summary>
        private static ParticleGeneratorDefinition Surf(ushort life = 100, ushort framesPerEmission = 50, bool autoRun = true)
        {
            var def = new ParticleGeneratorDefinition
            {
                DatId = "surf",
                AutoRun = autoRun,
                FramesPerEmission = framesPerEmission,
                Setup = new StandardParticleSetup { MaxLifeSpan = life, LinkedDataType = ParticleLinkedDataType.StaticMesh }
            };
            def.Initializers.Add(new ParticleOpcode(0x01, 0, Array.Empty<uint>()));
            def.Initializers.Add(new ParticleOpcode(0x02, 8, new[] { 0u, 0u, BitConverter.SingleToUInt32Bits(-0.01f) }));
            def.Initializers.Add(new ParticleOpcode(0x0F, 0, new[] { BitConverter.SingleToUInt32Bits(20f), BitConverter.SingleToUInt32Bits(1f), BitConverter.SingleToUInt32Bits(20f) }));
            def.Initializers.Add(new ParticleOpcode(0x16, 0, new[] { 0x00808080u }));
            def.KeyFrameLinks[2] = new ParticleKeyFrameLink("fade", 1);
            def.Updaters.Add(new ParticleOpcode(0x02, 8, Array.Empty<uint>()));
            def.Updaters.Add(new ParticleOpcode(0x1B, 2, Array.Empty<uint>()));
            def.Updaters.Add(new ParticleOpcode(0x28, 0, new[] { BitConverter.SingleToUInt32Bits(0.001f) }));
            return def;
        }

        private static ZoneEmitterTemplate Template(ParticleGeneratorDefinition def, IReadOnlyList<EffectRoutineSpawn>? schedule = null, int loop = 0)
        {
            // Fade: first keyframe replaced by the particle's initial alpha (0), up to 0.5 at the end of life.
            var fade = new KeyFrameCurve("fade", new[] { new KeyFrameEntry(0f, 0.9f), new KeyFrameEntry(1f, 0.5f) });
            return new ZoneEmitterTemplate(def, new Dictionary<ushort, KeyFrameCurve> { [2] = fade }, schedule, loop);
        }

        [Fact]
        public void AutoRunGenerator_EmitsAtItsCadenceAndExpiresParticles()
        {
            var emitter = new ZoneParticleEmitter(Template(Surf(life: 100, framesPerEmission: 50)));

            emitter.Update(1f, Frame);
            Assert.Single(emitter.Particles);

            emitter.Update(60f, Frame); // second emission at ~frame 50
            Assert.Equal(2, emitter.Particles.Count);

            emitter.Update(60f, Frame); // first particle (age ~121) has expired, a third was emitted at ~100
            Assert.Equal(2, emitter.Particles.Count);
            Assert.All(emitter.Particles, p => Assert.True(p.Age < 100f));
        }

        [Fact]
        public void Particle_AppliesInitializersAndProgressUpdaters()
        {
            var emitter = new ZoneParticleEmitter(Template(Surf(life: 100, framesPerEmission: 1000)));

            emitter.Update(1f, Frame);
            emitter.Update(49f, Frame);
            var particle = Assert.Single(emitter.Particles);

            Assert.Equal(new Vector3(20f, 1f, 20f), particle.Scale);
            Assert.Equal(0.5019608f, particle.Color.X, 4);
            // Moving toward -Z at 0.01 per frame.
            Assert.Equal(-0.01f * particle.Age, particle.LocalOffset.Z, 3);
            // Alpha ramps from the particle's initial 0 toward 0.5, not from the curve's authored 0.9.
            Assert.Equal(0.5f * particle.Progress, particle.Color.W, 3);
            // Constant V scroll per frame.
            Assert.Equal(0.001f * particle.Age, particle.TexCoordTranslate.Y, 4);
        }

        [Fact]
        public void RepeatExpirationHandler_LoopsTheParticleInsteadOfExpiring()
        {
            var def = Surf(life: 100, framesPerEmission: 10000);
            def.ExpirationHandlers.Add(0x05);
            var emitter = new ZoneParticleEmitter(Template(def));

            emitter.Update(250f, Frame);

            var particle = Assert.Single(emitter.Particles);
            Assert.True(particle.Age < 100f);
        }

        [Fact]
        public void GeneratorCull_StopsEmissionBeyondMaxEmitDistance()
        {
            var def = Surf();
            def.MaxEmitDistance = 100f;
            def.Setup!.BasePosition = new Vector3(0f, 0f, 500f);
            var emitter = new ZoneParticleEmitter(Template(def));

            emitter.Update(200f, Frame);

            Assert.Empty(emitter.Particles);
        }

        [Fact]
        public void RoutineScheduledGenerator_EmitsOncePerArmingAndRearmsOnLoop()
        {
            // Non-auto-running: a looping 300-frame routine starts it at frame 100 with a 0-frame window.
            var def = Surf(life: 50, framesPerEmission: 10, autoRun: false);
            var schedule = new[] { new EffectRoutineSpawn("surf", 100, 0) };
            var emitter = new ZoneParticleEmitter(Template(def, schedule, loop: 300));

            emitter.Update(90f, Frame);
            Assert.Empty(emitter.Particles);

            emitter.Update(20f, Frame); // armed at 100: exactly one particle despite the 10-frame cadence
            Assert.Single(emitter.Particles);

            emitter.Update(100f, Frame); // it expired and the window is closed
            Assert.Empty(emitter.Particles);

            emitter.Update(200f, Frame); // routine looped: armed again at 400
            Assert.Single(emitter.Particles);
        }

        [Fact]
        public void DrawDistanceUpdater_FadesByDistanceToTheParticle()
        {
            var def = Surf(life: 1000, framesPerEmission: 10000);
            def.Updaters.Add(new ParticleOpcode(0x2E, 0, new[] { BitConverter.SingleToUInt32Bits(10f), BitConverter.SingleToUInt32Bits(30f), 0u }));
            def.Setup!.BasePosition = new Vector3(20f, 0f, 0f);
            var emitter = new ZoneParticleEmitter(Template(def));

            emitter.Update(1f, Frame); // emitted
            emitter.Update(1f, Frame); // first update runs the updaters

            // Camera at the origin, particle ~20 away: halfway through the 10..30 fade.
            Assert.Equal(0.5f, Assert.Single(emitter.Particles).ColorMultiplier.W, 2);
        }

        [Theory]
        [InlineData(10f, 20f, 40f, 1.0f)]
        [InlineData(30f, 20f, 40f, 0.5f)]
        [InlineData(50f, 20f, 40f, 0.0f)]
        [InlineData(10f, 0f, 0f, 0.0f)]
        public void FallOff_MatchesClientDistanceFade(float distance, float near, float far, float expected)
        {
            Assert.Equal(expected, ZoneParticleEmitter.FallOff(distance, near, far), 3);
        }
    }
}
