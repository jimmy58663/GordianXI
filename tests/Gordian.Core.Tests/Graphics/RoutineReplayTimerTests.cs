// tests/Gordian.Core.Tests/Graphics/RoutineReplayTimerTests.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Graphics;
using Gordian.Core.Resources.Graphics;
using Xunit;

namespace Gordian.Core.Tests.Graphics
{
    /// <summary>
    /// The op 0x52 time gates of zone routines (#81): a routine with a <see cref="TimedReplayWindow"/> plays only inside
    /// its Vana'diel clock window, when the window opens and then after each interval.
    /// </summary>
    public class RoutineReplayTimerTests
    {
        private const float Minute = 1f / (24f * 60f);

        [Fact]
        public void Window_WrapsPastMidnight_AndTakesTimesModuloTheDay()
        {
            // Carpenters' Landing kmi1/s001: 23:15-27:00 is 23:15-03:00 next day.
            var wraps = new TimedReplayWindow(3348000, 3888000, 108000, 3600);
            Assert.Equal("23:15-03:00", wraps.ToString());
            Assert.True(wraps.Contains(23.5f / 24f));
            Assert.True(wraps.Contains(1f / 24f));
            Assert.False(wraps.Contains(12f / 24f));
            Assert.False(wraps.Contains(3f / 24f));

            // kmi1/s003: 33:45-07:30 is 09:45 to 07:30 the next day (its end field puzzled #81 before the modulo).
            var late = new TimedReplayWindow(4860000, 1080000, 108000, 3600);
            Assert.Equal("09:45-07:30", late.ToString());
            Assert.True(late.Contains(10f / 24f));
            Assert.True(late.Contains(2f / 24f));
            Assert.False(late.Contains(8f / 24f));

            // The ducks kamo/s001: 10:30-11:24.
            var ducks = new TimedReplayWindow(1512000, 1641600, 129600, 18000);
            Assert.Equal("10:30-11:24", ducks.ToString());
            Assert.True(ducks.Contains(10.5f / 24f + Minute));
            Assert.False(ducks.Contains(11.5f / 24f));
        }

        [Fact]
        public void State_FiresWhenTheWindowOpens_ThenEveryInterval_AndNeverOutside()
        {
            // Windurst's kaza/tim1: 00:00-00:01 with a fixed 3,600 ms interval (216 frames).
            var timer = new RoutineReplayTimer(new[] { new TimedReplayWindow(0, 2400, 3600, 3600) }, seed: 7);
            var state = new RoutineReplayTimer.State(timer);

            Assert.False(state.Advance(1f, 12f / 24f));
            Assert.False(state.Advance(1f, 23.99f / 24f));
            Assert.True(state.Advance(1f, 0.1f * Minute)); // the window opens
            Assert.False(state.Advance(100f, 0.2f * Minute));
            Assert.False(state.Advance(100f, 0.3f * Minute));
            Assert.True(state.Advance(17f, 0.4f * Minute)); // 216 frames = 3,600 ms later
            Assert.False(state.Advance(1f, 2f * Minute)); // closed again
            Assert.True(state.Advance(1f, 0.5f * Minute)); // and opens anew
        }

        [Fact]
        public void States_WithTheSameSeed_FireTogether()
        {
            var timer = new RoutineReplayTimer(new[] { new TimedReplayWindow(0, 3456000, 108000, 3600) }, seed: 42);
            var a = new RoutineReplayTimer.State(timer);
            var b = new RoutineReplayTimer.State(timer);
            int fires = 0;
            for (int frame = 0; frame < 60 * 600; frame++)
            {
                bool fa = a.Advance(1f, 0.5f), fb = b.Advance(1f, 0.5f);
                Assert.Equal(fa, fb);
                if (fa) fires++;
            }
            // 600 s at 3.6-108 s apart: a handful of replays, never one a frame.
            Assert.InRange(fires, 6, 170);
        }

        private static ZoneEmitterTemplate Duck(RoutineReplayTimer timer)
        {
            var def = new ParticleGeneratorDefinition
            {
                DatId = "kamo",
                FramesPerEmission = 1000,
                Setup = new StandardParticleSetup { MaxLifeSpan = 300, LinkedDataType = ParticleLinkedDataType.StaticMesh }
            };
            def.Initializers.Add(new ParticleOpcode(0x01, 0, Array.Empty<uint>()));
            var schedule = new[] { new EffectRoutineSpawn("kamo", 30, 100) { Timer = timer } };
            return new ZoneEmitterTemplate(def, new Dictionary<ushort, KeyFrameCurve>(), schedule, scheduleLoopFrames: 0);
        }

        [Fact]
        public void Emitter_StartsItsTimedSpawnsOnlyInsideTheWindow()
        {
            var timer = new RoutineReplayTimer(new[] { new TimedReplayWindow(1512000, 1641600, 129600, 129600) }, seed: 1);
            var emitter = new ZoneParticleEmitter(Duck(timer));
            var morning = new ZoneParticleFrame(Vector3.Zero, 9f / 24f, Vector3.One);
            var duckTime = new ZoneParticleFrame(Vector3.Zero, 10.75f / 24f, Vector3.One);

            for (int i = 0; i < 400; i++) emitter.Update(1f, morning);
            Assert.Empty(emitter.Particles); // 09:00: outside 10:30-11:24, nothing although the loop clock passed frame 30

            emitter.Update(1f, duckTime);
            Assert.Empty(emitter.Particles); // the spawn sits 30 frames into the routine
            emitter.Update(40f, duckTime);
            Assert.Single(emitter.Particles);

            for (int i = 0; i < 400; i++) emitter.Update(1f, duckTime); // the particle (300 frames) expires; no replay before 129.6 s
            Assert.Empty(emitter.Particles);
        }
    }
}
