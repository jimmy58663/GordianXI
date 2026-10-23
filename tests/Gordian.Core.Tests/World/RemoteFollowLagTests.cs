// tests/Gordian.Core.Tests/World/RemoteFollowLagTests.cs
using System;
using System.Numerics;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.World
{
    /// <summary>
    /// #59: a remote character running for a long time (a follower) is played back a steady time behind its real
    /// position, whatever its move counter's rate or the staleness of the packet that started the move.
    /// </summary>
    public class RemoteFollowLagTests
    {
        private const double Speed = 5.0;

        /// <summary>
        /// Runs a straight 120 s run, sending 0x00D every <paramref name="interval"/> s with samples up to
        /// <paramref name="staleMax"/> s old, and returns the mean display lag (seconds behind the true position) over the
        /// windows 10-30 s and 90-120 s.
        /// </summary>
        private static (double Early, double Late) MeanLag(double interval, double staleMax, double counterDrift, double firstStale)
        {
            var rng = new Random(59);
            var entity = new WorldEntity(0x100, 5, EntityType.Player) { Position = Vector3.Zero, Speed = 50 };
            const double origin = 1000.0;
            entity.AddServerSample(Vector3.Zero, isMoving: false, movTime: 1, direction: 0, origin);

            double now = 0, nextPacket = 0.2, early = 0, late = 0;
            int earlyCount = 0, lateCount = 0;
            bool first = true;
            while (now < 120.0)
            {
                now += 1.0 / 60.0;
                if (now >= nextPacket)
                {
                    double sampled = Math.Max(0.0, now - (first ? firstStale : rng.NextDouble() * staleMax));
                    ushort movTime = (ushort)(9 + (sampled * 60.0 * (1.0 + counterDrift)));
                    entity.AddServerSample(new Vector3((float)(sampled * Speed), 0f, 0f), isMoving: true, movTime, direction: 0, origin + now);
                    first = false;
                    nextPacket += interval;
                }
                entity.InterpolatePosition(1.0f / 60.0f, origin + now);
                double lag = now - (entity.Position.X / Speed);
                if (now >= 10 && now < 30) { early += lag; earlyCount++; }
                if (now >= 90) { late += lag; lateCount++; }
            }
            return (early / earlyCount, late / lateCount);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.03)]  // counter runs fast: stamps used to drift into the future, the lag grew to 3 s by 60-120 s
        [InlineData(-0.03)] // counter runs slow
        public void LongRun_LagStaysSteady_WhateverTheCounterRate(double drift)
        {
            var (early, late) = MeanLag(interval: 0.4, staleMax: 0.3, drift, firstStale: 0.0);
            Assert.InRange(early, 0.3, 1.2);
            Assert.InRange(late, 0.3, 1.2);
            Assert.True(Math.Abs(late - early) < 0.2, $"lag moved from {early:F2} s to {late:F2} s");
        }

        [Fact]
        public void StaleMoveStart_DoesNotDelayTheWholeMove()
        {
            // The first packet of the move was 1.2 s old: later, fresher packets bring the stamps back to real time.
            var (_, late) = MeanLag(interval: 0.4, staleMax: 0.3, counterDrift: 0.0, firstStale: 1.2);
            Assert.InRange(late, 0.3, 1.2);
        }
    }
}
