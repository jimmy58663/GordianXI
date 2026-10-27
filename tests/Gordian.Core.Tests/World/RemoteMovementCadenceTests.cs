// tests/Gordian.Core.Tests/World/RemoteMovementCadenceTests.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.World
{
    /// <summary>
    /// #359: remote movement playback against LandSandBoat's Release cadence. A moving monster's 0x00E arrives every 0.4 s
    /// (the server's logic tick; 10th-90th percentile 0.31-0.49 s in the maintainer's 2026-10-23 to 2026-10-26 logs), it
    /// covers speed / 17 yalms per second (0.94 yalm per update at speed 40), and when it stops the updates simply stop.
    /// </summary>
    public class RemoteMovementCadenceTests
    {
        private const double Frame = 1.0 / 60.0;
        private const float StepPerUpdate = 40f / 17f * 0.4f; // 0.94 yalm

        private sealed class Mob
        {
            public readonly WorldEntity Entity = new(0x01067104, 260, EntityType.Monster) { Position = Vector3.Zero, Speed = 40 };
            public readonly List<(double Time, float X, bool Translating)> Frames = new();
            public double Now = 1000.0;
            public float X;
            private readonly Random _rng = new(359);

            /// <summary>Sends <paramref name="updates"/> moving updates about 0.4 s apart (jittered 0.31-0.49 s), rendering between.</summary>
            public void Walk(int updates, double jitter = 0.09)
            {
                for (int i = 0; i < updates; i++)
                {
                    double gap = 0.4 + ((_rng.NextDouble() * 2.0) - 1.0) * jitter;
                    X += (float)(StepPerUpdate * gap / 0.4);
                    Entity.Speed = 40;
                    Entity.StopOnArrival = false;
                    Entity.AddServerSample(new Vector3(X, 0f, 0f), isMoving: true, movTime: 0, direction: 0, Now);
                    Render(gap);
                }
            }

            public void Render(double seconds)
            {
                int frames = (int)Math.Round(seconds / Frame);
                for (int i = 0; i < frames; i++)
                {
                    Now += Frame;
                    Entity.InterpolatePosition((float)Frame, Now);
                    Frames.Add((Now, Entity.Position.X, Entity.IsTranslating));
                }
            }
        }

        [Fact]
        public void ReleaseCadence_IsLearned()
        {
            var mob = new Mob();
            mob.Walk(12);

            Assert.InRange(mob.Entity.UpdateIntervalSeconds, 0.35f, 0.45f);
            Assert.InRange(mob.Entity.StopTimeoutSeconds, 0.8f, 1.0f);
            // The mob covers speed / 17, not speed / 10, yalms per second.
            Assert.InRange(mob.Entity.GroundSpeedYalms, 2.0f, 2.7f);
            Assert.InRange(mob.Entity.PlaybackDelaySeconds, 0.4f, 0.8f);
        }

        [Fact]
        public void DebugCadence_IsLearnedFromTheFirstGap()
        {
            var entity = new WorldEntity(0x01067105, 261, EntityType.Monster) { Position = Vector3.Zero, Speed = 40 };
            double now = 1000.0;
            for (int i = 1; i <= 4; i++)
            {
                entity.AddServerSample(new Vector3(i * 1.0f, 0f, 0f), isMoving: true, movTime: 0, direction: 0, now);
                now += 1.4;
            }

            Assert.Equal(1.4f, entity.UpdateIntervalSeconds, 2);
            Assert.InRange(entity.StopTimeoutSeconds, 2.8f, 3.0f);
        }

        [Fact]
        public void ReleaseCadence_WalksContinuouslyWithoutSpeedWobble()
        {
            var mob = new Mob();
            mob.Walk(25);

            int first = mob.Frames.FindIndex(f => f.Translating);
            Assert.True(first >= 0, "never moved");
            // Continuous from the first update: the move from rest is anchored one update interval back.
            Assert.True(first < 60, $"started {first} frames after the first update");
            for (int i = first; i < mob.Frames.Count; i++)
            {
                Assert.True(mob.Frames[i].Translating, $"paused at frame {i}");
            }

            // Once settled (after the first 3 s), the played pace stays near the true 2.35 yalms per second: a starvation
            // buffer wider than the delay's margin slowed every update cycle down and sped it back up.
            for (int i = first + 180; i < mob.Frames.Count; i++)
            {
                float pace = (mob.Frames[i].X - mob.Frames[i - 1].X) * 60f;
                Assert.InRange(pace, 1.5f, 3.3f);
            }
        }

        [Fact]
        public void SilentStop_EndsTheMoveAndDoesNotGrowTheDelay()
        {
            var mob = new Mob();
            mob.Walk(10);
            float finalX = mob.X;

            // The server stops sending: the mob stops where its last update put it, idles, and the delay does not grow
            // toward the 3 s cap (it reached it after every pause before #359).
            mob.Render(5.0);
            Assert.Equal(finalX, mob.Entity.Position.X, 3);
            Assert.False(mob.Entity.IsTranslating);
            Assert.Equal(0, mob.Entity.Speed);
            Assert.True(mob.Entity.PlaybackDelaySeconds < 1.2f, $"delay grew to {mob.Entity.PlaybackDelaySeconds:F2} s");
        }

        [Fact]
        public void MoveAfterPause_StartsPromptlyAtGroundPace()
        {
            var mob = new Mob();
            mob.Walk(10);
            mob.Render(5.0);
            int pauseEnd = mob.Frames.Count;
            float restX = mob.Entity.Position.X;

            mob.Walk(10);

            // The next move plays well under a second after its first update, not 3 s late.
            int start = mob.Frames.FindIndex(pauseEnd, f => f.Translating);
            Assert.True(start >= 0, "never moved again");
            Assert.True((start - pauseEnd) * Frame < 0.7, $"started {(start - pauseEnd) * Frame:F2} s after the update");

            // Its first step plays at about ground pace, not stretched over the whole delay.
            float firstSecond = mob.Frames[Math.Min(mob.Frames.Count - 1, start + 60)].X - restX;
            Assert.InRange(firstSecond, 1.6f, 3.2f);

            // And no stop happens mid-move once it has started.
            int last = mob.Frames.FindLastIndex(f => f.Translating);
            for (int i = start; i <= last; i++)
            {
                Assert.True(mob.Frames[i].Translating, $"paused {(i - start) * Frame:F2} s into the move");
            }
        }

        [Fact]
        public void FirstEverMove_DoesNotSnap()
        {
            var mob = new Mob();
            mob.Walk(1);

            // The first update of a mob that has never moved plays its step instead of jumping to it.
            Assert.True(mob.Frames[0].X < StepPerUpdate * 0.5f, $"jumped to {mob.Frames[0].X:F2} on the first frame");
            mob.Render(1.0);
            Assert.Equal(mob.X, mob.Frames[^1].X, 3);
        }

        [Fact]
        public void CapturedReleaseMob_ResumesAfterEachPauseWithAShortDelay()
        {
            // Thunder Elemental (0x0100100C), gordian_system_20261025.log 20:42:29.089-20:42:46.633 (Release server): 0x00E
            // arrival time (s from the first), X, Z. The 10.4 s gap is a pause with no stop update; the old playback logged
            // delay=3.00s on the first update after it and showed the mob 2-3.5 yalms behind its server position.
            (double T, float X, float Z)[] capture =
            {
                (0.000, 2.48f, 0.24f), (0.361, 3.40f, 0.45f), (0.751, 4.32f, 0.65f), (1.252, 5.24f, 0.86f),
                (1.555, 6.16f, 1.07f), (2.003, 7.08f, 1.27f), (2.354, 7.98f, 1.50f),
                (12.762, 7.38f, 2.22f), (13.137, 6.79f, 2.95f), (13.574, 6.19f, 3.68f), (14.014, 5.59f, 4.41f),
                (14.344, 5.38f, 4.62f), (14.764, 6.19f, 4.14f), (15.150, 7.00f, 3.66f), (15.561, 7.79f, 3.15f),
                (16.018, 7.93f, 3.06f), (16.359, 7.13f, 2.56f), (16.768, 6.33f, 2.08f), (17.149, 5.52f, 1.59f),
                (17.544, 4.71f, 1.11f),
            };
            var entity = new WorldEntity(0x0100100C, 12, EntityType.Monster) { Position = new Vector3(2.39f, 0f, 1.18f), Speed = 40 };
            const double origin = 1000.0;
            double now = origin;
            var behind = new List<float>();
            float delayAfterPause = 0f;
            for (int i = 0; i < capture.Length; i++)
            {
                var (t, x, z) = capture[i];
                while (now < origin + t)
                {
                    now += Frame;
                    entity.InterpolatePosition((float)Frame, now);
                    if (t > 13.0) behind.Add(Vector3.Distance(entity.Position, entity.TargetPosition));
                }
                entity.AddServerSample(new Vector3(x, 0f, z), isMoving: true, movTime: 0, direction: 0, now);
                if (i == 7) delayAfterPause = entity.PlaybackDelaySeconds;
                if (i == 6)
                {
                    // In the pause: stopped at its last position and idle well before the next move.
                    double pauseEnd = origin + capture[7].T - 1.0;
                    while (now < pauseEnd)
                    {
                        now += Frame;
                        entity.InterpolatePosition((float)Frame, now);
                    }
                    Assert.Equal(7.98f, entity.Position.X, 2);
                    Assert.Equal(0, entity.Speed);
                    entity.Speed = 40; // the next 0x00E carries the speed again (EntityPacketModule)
                }
            }

            Assert.True(delayAfterPause < 0.8f, $"delay after the pause was {delayAfterPause:F2} s");
            // Shown within about two updates of its server position (0.94 yalm each), not 2-3.5 yalms behind.
            behind.Sort();
            float median = behind[behind.Count / 2];
            Assert.True(median < 1.6f, $"median {median:F2} yalms behind");
        }

        [Fact]
        public void LateUpdateWithinTheTimeout_IsNotAStop()
        {
            var mob = new Mob();
            mob.Walk(10, jitter: 0.0);
            // One update 0.75 s late (below the 0.9 s timeout): still the same move.
            mob.Render(0.35);
            Assert.Equal(40, mob.Entity.Speed);
            mob.Walk(5, jitter: 0.0);

            Assert.Equal(40, mob.Entity.Speed);
            Assert.InRange(mob.Entity.UpdateIntervalSeconds, 0.39f, 0.41f);
        }
    }
}
