// tests/Gordian.Core.Tests/Graphics/WeatherRoutinePlayerTests.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Graphics;
using Gordian.Core.Resources.Graphics;
using Xunit;

namespace Gordian.Core.Tests.Graphics
{
    public class WeatherRoutinePlayerTests
    {
        private static readonly ZoneParticleFrame Frame = new(Vector3.Zero, 0.5f, Vector3.One);

        /// <summary>
        /// A strike generator: not auto-running, one 30-frame particle per start.
        /// </summary>
        private static ZoneEmitterTemplate Strike()
        {
            var def = new ParticleGeneratorDefinition
            {
                DatId = "kka1",
                FramesPerEmission = 100,
                Setup = new StandardParticleSetup { MaxLifeSpan = 30, LinkedDataType = ParticleLinkedDataType.StaticMesh }
            };
            def.Initializers.Add(new ParticleOpcode(0x01, 0, Array.Empty<uint>()));
            return new ZoneEmitterTemplate(def, new Dictionary<ushort, KeyFrameCurve>(), isWeather: true);
        }

        private static (WeatherRoutinePlayer Player, ZoneParticleEmitter Emitter) Setup()
        {
            var template = Strike();
            var group = new WeatherRoutineGroup("thdr", "kmi1");
            group.Routines.Add(new WeatherRoutineVariant("s000", 50, new[] { new WeatherRoutineSpawn(template, 10, 0) }));
            var player = new WeatherRoutinePlayer(new[] { group }) { MinGapFrames = 100, MaxGapFrames = 100 };
            return (player, new ZoneParticleEmitter(template));
        }

        [Fact]
        public void Strike_FiresOnceAfterTheGapAndItsStartFrame()
        {
            var (player, emitter) = Setup();

            player.Update(99f, "thdr", _ => emitter);
            emitter.Update(20f, Frame);
            Assert.Empty(emitter.Particles);

            player.Update(1f, "thdr", _ => emitter);   // gap elapsed: routine starts, spawn at frame 10
            emitter.Update(5f, Frame);
            Assert.Empty(emitter.Particles);
            emitter.Update(6f, Frame);
            Assert.Single(emitter.Particles);

            emitter.Update(200f, Frame);               // a single emission, not a stream
            Assert.Empty(emitter.Particles);
        }

        [Fact]
        public void Strikes_OnlyPlayUnderTheirWeather()
        {
            var (player, emitter) = Setup();

            player.Update(1000f, "fine", _ => emitter);
            emitter.Update(20f, Frame);

            Assert.Empty(emitter.Particles);
        }
    }
}
