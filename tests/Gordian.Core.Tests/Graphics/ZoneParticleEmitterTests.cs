// tests/Gordian.Core.Tests/Graphics/ZoneParticleEmitterTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
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

        private static uint IdArg(string id) =>
            BitConverter.ToUInt32(System.Text.Encoding.ASCII.GetBytes(id.PadRight(4, '\0')), 0);

        private static uint[] Args(params float[] values) => Array.ConvertAll(values, BitConverter.SingleToUInt32Bits);

        /// <summary>
        /// Wires a parent and child emitter the way the renderer does.
        /// </summary>
        private static (ZoneParticleEmitter Parent, ZoneParticleEmitter Child) ParentAndChild(
            ParticleGeneratorDefinition parentDef, ParticleGeneratorDefinition childDef, string childId)
        {
            var childTemplate = new ZoneEmitterTemplate(childDef, new Dictionary<ushort, KeyFrameCurve>(), childOnly: true);
            var parentTemplate = Template(parentDef);
            parentTemplate.Children[childId] = childTemplate;
            var parent = new ZoneParticleEmitter(parentTemplate);
            var child = new ZoneParticleEmitter(childTemplate);
            parent.ChildResolver = t => ReferenceEquals(t, childTemplate) ? child : null;
            child.ChildResolver = _ => null;
            return (parent, child);
        }

        private static ParticleGeneratorDefinition ChildDef(bool copyParentPosition = true)
        {
            var def = Surf(life: 1000, framesPerEmission: 30);
            def.Setup!.BasePosition = new Vector3(0f, 1f, 0f);
            if (copyParentPosition) def.Initializers.Add(new ParticleOpcode(0x45, 0, Array.Empty<uint>()));
            return def;
        }

        [Fact]
        public void OnceChildGenerator_BurstsAtBirthFromTheParentPosition()
        {
            var parentDef = Surf(life: 100, framesPerEmission: 10000);
            parentDef.Setup!.BasePosition = new Vector3(50f, 0f, 0f);
            parentDef.Initializers.Add(new ParticleOpcode(0x3C, 0, new[] { 0u, IdArg("kid1") }));
            var (parent, child) = ParentAndChild(parentDef, ChildDef(), "kid1");

            parent.Update(1f, Frame);

            var spawned = Assert.Single(child.Particles);
            // Child base (0, 1, 0) offset from the parent's world position (50, 0, 0).
            Assert.Equal(new Vector3(50f, 1f, 0f), spawned.Origin);
        }

        [Fact]
        public void ChildWithoutParentPositionCopy_StartsAtItsOwnBase()
        {
            var parentDef = Surf(life: 100, framesPerEmission: 10000);
            parentDef.Setup!.BasePosition = new Vector3(50f, 0f, 0f);
            parentDef.Initializers.Add(new ParticleOpcode(0x3C, 0, new[] { 0u, IdArg("kid1") }));
            var (parent, child) = ParentAndChild(parentDef, ChildDef(copyParentPosition: false), "kid1");

            parent.Update(1f, Frame);

            Assert.Equal(new Vector3(0f, 1f, 0f), Assert.Single(child.Particles).Origin);
        }

        [Fact]
        public void ExpirationChildHandler_BurstsWhereTheParentDied()
        {
            var parentDef = Surf(life: 50, framesPerEmission: 10000);
            parentDef.ExpirationHandlers.Add(0x01);
            parentDef.ExpirationOpcodes.Add(new ParticleOpcode(0x01, 0, new[] { 0u, IdArg("kid1") }));
            var (parent, child) = ParentAndChild(parentDef, ChildDef(), "kid1");

            parent.Update(20f, Frame);
            Assert.Empty(child.Particles);

            parent.Update(40f, Frame); // parent expires at ~50
            Assert.Empty(parent.Particles);
            var spawned = Assert.Single(child.Particles);
            Assert.True(spawned.Origin.Z < 0f); // the parent had drifted toward -Z before dying
        }

        [Fact]
        public void ChildStream_EmitsAtTheChildCadenceWhileFollowingTheParent()
        {
            var parentDef = Surf(life: 100, framesPerEmission: 10000);
            parentDef.Initializers.Add(new ParticleOpcode(0x44, 20, new[] { 0u, IdArg("kid1") }));
            parentDef.Updaters.Add(new ParticleOpcode(0x33, 20, Array.Empty<uint>()));
            var (parent, child) = ParentAndChild(parentDef, ChildDef(copyParentPosition: false), "kid1");

            parent.Update(1f, Frame);   // parent born
            parent.Update(64f, Frame);  // child cadence 30: emits at ~0, 30, 60

            Assert.Equal(3, child.Particles.Count);
            // Transform-following stream: spawned relative to the parent's position at that moment.
            Assert.Contains(child.Particles, c => c.Origin.Z < -0.5f);

            parent.Update(100f, Frame); // parent dead, window closed: no more children
            int afterDeath = child.Particles.Count;
            parent.Update(60f, Frame);
            Assert.Equal(afterDeath, child.Particles.Count);
        }

        [Fact]
        public void SpriteSheetFrameUpdater_StepsThroughCardsOverLife()
        {
            var def = Surf(life: 100, framesPerEmission: 10000);
            def.Updaters.Add(new ParticleOpcode(0x0D, 0, Array.Empty<uint>()));
            var emitter = new ZoneParticleEmitter(new ZoneEmitterTemplate(def, new Dictionary<ushort, KeyFrameCurve>(), spriteFrameCount: 4));

            emitter.Update(1f, Frame);
            emitter.Update(1f, Frame);
            Assert.Equal(0, Assert.Single(emitter.Particles).SpriteIndex);

            emitter.Update(60f, Frame); // age 61, progress 0.61: floor(5 * 0.61) = 3
            Assert.Equal(3, emitter.Particles[0].SpriteIndex);
        }

        [Fact]
        public void SphericalPositionVariance_ScattersOnTheAuthoredShell()
        {
            var def = Surf(life: 1000, framesPerEmission: 1);
            // Medium variance: no radius variance, base radius 5, unit radius scale.
            def.Initializers.Add(new ParticleOpcode(0x07, 0, Args(0f, 5f, 1f, 1f, 1f, 0f, 0f)));
            var emitter = new ZoneParticleEmitter(Template(def), seed: 7);

            emitter.Update(20f, Frame);

            Assert.True(emitter.Particles.Count > 10);
            Assert.All(emitter.Particles, p => Assert.Equal(5f, p.InitialPosition.Length(), 3));
            Assert.True(emitter.Particles.Select(p => p.InitialPosition).Distinct().Count() > 1);
        }

        [Fact]
        public void IncrementalRotation_TurnsEachSuccessiveParticleOneMoreStep()
        {
            var def = Surf(life: 1000, framesPerEmission: 10);
            def.Initializers.Add(new ParticleOpcode(0x3B, 0, Args(0f, 0.5f, 0f)));
            var emitter = new ZoneParticleEmitter(Template(def));

            emitter.Update(25f, Frame);

            Assert.Equal(new[] { 0.5f, 1.0f, 1.5f }, emitter.Particles.Select(p => p.Rotation.Y));
            Assert.All(emitter.Particles, p => Assert.True(p.NegateRotationY));
        }

        [Fact]
        public void VelocityVariance_AppliesToTheTransformAtItsSlot()
        {
            var def = Surf(life: 1000, framesPerEmission: 10000);
            def.Initializers.Add(new ParticleOpcode(0x12, 30, Args(0.1f, 0f, 0f)));   // scale velocity
            def.Initializers.Add(new ParticleOpcode(0x13, 30, Args(0.05f, 0f, 0f)));  // scale velocity variance
            def.Updaters.Add(new ParticleOpcode(0x08, 30, Array.Empty<uint>()));
            var emitter = new ZoneParticleEmitter(Template(def), seed: 3);

            emitter.Update(1f, Frame);
            emitter.Update(10f, Frame);

            // Scale X grows at 0.1 +/- 0.05 per frame from 20.
            float growth = (emitter.Particles[0].Scale.X - 20f) / 10f;
            Assert.InRange(growth, 0.05f, 0.15f);
            Assert.NotEqual(0.1f, growth, 4);
        }

        /// <summary>
        /// A rain-like weather generator: batched, <paramref name="particlesPerEmission"/> authored particles scattered on
        /// a 10-yalm shell, falling at 0.5 per frame, with the given camera placement flags.
        /// </summary>
        private static ParticleGeneratorDefinition Rain(byte particlesPerEmission = 149, bool batched = true,
            bool followCamera = false, bool cameraAnchored = false)
        {
            var def = Surf(life: 60, framesPerEmission: 20);
            def.Batched = batched;
            def.ParticlesPerEmission = particlesPerEmission;
            def.Setup!.FollowCamera = followCamera;
            def.Setup.CameraAttachedBasePosition = cameraAnchored;
            def.Setup.BasePosition = new Vector3(0f, -30f, 0f);
            def.Initializers.Add(new ParticleOpcode(0x06, 0, Args(0f, 10f)));
            def.Initializers.Add(new ParticleOpcode(0x08, 8, Args(0.25f)));
            return def;
        }

        private static ZoneParticleEmitter WeatherEmitter(ParticleGeneratorDefinition def) =>
            new(new ZoneEmitterTemplate(def, new Dictionary<ushort, KeyFrameCurve>(), isWeather: true), seed: 5);

        [Fact]
        public void BatchedWeatherGenerator_EmitsOneParticleCarryingAThirdOfTheDoubledCountAsSubParticles()
        {
            var emitter = WeatherEmitter(Rain(particlesPerEmission: 149));

            emitter.Update(1f, Frame);

            var particle = Assert.Single(emitter.Particles);
            Assert.NotNull(particle.SubOffsets);
            Assert.Equal(149 * 2 / 3 + 1, particle.SubOffsets!.Length);
            // The shell scatters the sub-particles, not the particle itself.
            Assert.Equal(Vector3.Zero, particle.InitialPosition);
            Assert.All(particle.SubOffsets, o => Assert.Equal(10f, o.Length(), 3));
        }

        [Fact]
        public void UnbatchedWeatherGenerator_EmitsAThirdOfItsAuthoredParticles()
        {
            var emitter = WeatherEmitter(Rain(particlesPerEmission: 29, batched: false));

            emitter.Update(1f, Frame);

            Assert.Equal(29 / 3 + 1, emitter.Particles.Count);
            Assert.All(emitter.Particles, p => Assert.Null(p.SubOffsets));
        }

        [Fact]
        public void BatchedSubParticles_DriftAlongTheirRelativeVelocity()
        {
            var emitter = WeatherEmitter(Rain());
            emitter.Update(1f, Frame);
            var particle = emitter.Particles[0];
            var before = particle.SubOffsets!.ToArray();

            emitter.Update(4f, Frame);

            // 0.25 per frame outward from the spawn shell.
            for (int i = 0; i < before.Length; i++)
            {
                Assert.Equal(10f + 0.25f * 4f, particle.SubOffsets![i].Length(), 3);
                Assert.Equal(Vector3.Normalize(before[i]).X, Vector3.Normalize(particle.SubOffsets[i]).X, 3);
            }
        }

        [Fact]
        public void CameraFollowingGenerator_TracksTheCameraPlusItsBase()
        {
            var emitter = WeatherEmitter(Rain(followCamera: true));
            emitter.Update(1f, new ZoneParticleFrame(new Vector3(100f, 0f, 50f), 0.5f, Vector3.One));
            Assert.Equal(new Vector3(100f, -30f, 50f), emitter.Particles[0].Origin);

            emitter.Update(1f, new ZoneParticleFrame(new Vector3(110f, 5f, 50f), 0.5f, Vector3.One));
            Assert.Equal(new Vector3(110f, -25f, 50f), emitter.Particles[0].Origin);
        }

        [Fact]
        public void CameraAnchoredGenerator_StaysWhereTheCameraWasAtBirth()
        {
            var emitter = WeatherEmitter(Rain(cameraAnchored: true));
            emitter.Update(1f, new ZoneParticleFrame(new Vector3(100f, 0f, 50f), 0.5f, Vector3.One));
            emitter.Update(1f, new ZoneParticleFrame(new Vector3(200f, 0f, 50f), 0.5f, Vector3.One));

            Assert.Equal(new Vector3(100f, -30f, 50f), emitter.Particles[0].Origin);
        }

        [Fact]
        public void CameraOrientedSpawnShell_FacesAlongTheCameraView()
        {
            var def = Surf(life: 1000, framesPerEmission: 1000);
            // Full variance: base radius 4 along +X with no tilt variance, camera oriented.
            def.Initializers.Add(new ParticleOpcode(0x1F, 0, new[]
            {
                0u, BitConverter.SingleToUInt32Bits(4f), BitConverter.SingleToUInt32Bits(1f), BitConverter.SingleToUInt32Bits(0f),
                BitConverter.SingleToUInt32Bits(1f), 0u, 0u, 0u, 0u, 1u, 0u
            }));
            var emitter = new ZoneParticleEmitter(Template(def), seed: 1);

            // Radius scale (1, 0, 1) keeps the shell in the XZ plane; facing +X turns the plane's Z onto world X.
            emitter.Update(1f, new ZoneParticleFrame(Vector3.Zero, 0.5f, Vector3.One, CameraRawForward: Vector3.UnitX));
            var offset = emitter.Particles[0].InitialPosition;

            Assert.Equal(4f, offset.Length(), 3);
            Assert.Equal(0f, offset.Y, 3);
            Assert.Equal(new Vector3(0f, 0f, -1f), ZoneParticleEmitter.AxisBillboard(Vector3.UnitX, Vector3.UnitX));
        }

        [Fact]
        public void DaylightBasedColorAdjuster_TintsTheBirthColorByTheStrongestLight()
        {
            var def = Surf(life: 1000, framesPerEmission: 1000);
            def.Initializers.Add(new ParticleOpcode(0x90, 0, Array.Empty<uint>()));
            var emitter = new ZoneParticleEmitter(Template(def));

            emitter.Update(1f, new ZoneParticleFrame(Vector3.Zero, 0.5f, new Vector3(0.5f, 0.25f, 1f)));

            var color = emitter.Particles[0].Color;
            Assert.Equal(0.5019608f * 0.5f, color.X, 4);
            Assert.Equal(0.5019608f * 0.25f, color.Y, 4);
            Assert.Equal(0.5019608f, color.Z, 4);
        }

        [Fact]
        public void Particle_RecordsItsLastMovementForMovementBillboards()
        {
            var emitter = new ZoneParticleEmitter(Template(Surf(life: 1000, framesPerEmission: 1000)));
            emitter.Update(1f, Frame);
            emitter.Update(2f, Frame);

            Assert.Equal(new Vector3(0f, 0f, -0.02f), emitter.Particles[0].LastMovement);
        }

        [Theory]
        [InlineData(5f, 0f)]
        [InlineData(15f, 0.5f)]
        [InlineData(30f, 1f)]
        [InlineData(60f, 0.5f)]
        [InlineData(80f, 0f)]
        public void DoubleRangeWeight_FadesInNearAndOutFar(float distance, float expected)
        {
            Assert.Equal(expected, ZoneParticleEmitter.DoubleRangeWeight(distance, 10f, 20f, 50f, 70f), 3);
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
