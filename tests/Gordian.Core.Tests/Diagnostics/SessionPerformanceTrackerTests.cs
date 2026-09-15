// tests/Gordian.Core.Tests/Diagnostics/SessionPerformanceTrackerTests.cs
using System;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Xunit;

namespace Gordian.Core.Tests.Diagnostics
{
    public sealed class SessionPerformanceTrackerTests
    {
        [Fact]
        public void InitialState_ReturnsZeroCounters()
        {
            var tracker = new SessionPerformanceTracker();
            var snapshot = tracker.GetSnapshot();

            Assert.Equal(0, snapshot.PacketsReceivedTotal);
            Assert.Equal(0, snapshot.PacketsSentTotal);
            Assert.Equal(0, snapshot.BytesReceivedTotal);
            Assert.Equal(0, snapshot.BytesSentTotal);
            Assert.Equal(0, snapshot.SequenceDiscrepancies);
            Assert.True(snapshot.ManagedHeapSizeBytes > 0);
        }

        [Fact]
        public async Task InboundAndOutboundCounters_AccumulateAccurately()
        {
            var tracker = new SessionPerformanceTracker();

            tracker.RecordInboundDatagram(128);
            tracker.RecordInboundPacket();
            tracker.RecordInboundPackets(4);

            tracker.RecordOutboundDatagram(256);
            tracker.RecordOutboundPacket();
            tracker.RecordOutboundPackets(2);

            tracker.RecordSequenceDiscrepancy();

            // Wait for rate interval window
            await Task.Delay(260);

            var snapshot = tracker.GetSnapshot();

            Assert.Equal(5, snapshot.PacketsReceivedTotal);
            Assert.Equal(128, snapshot.BytesReceivedTotal);
            Assert.Equal(3, snapshot.PacketsSentTotal);
            Assert.Equal(256, snapshot.BytesSentTotal);
            Assert.Equal(1, snapshot.SequenceDiscrepancies);
            Assert.True(snapshot.KilobytesReceivedPerSecond > 0);
            Assert.True(snapshot.KilobytesSentPerSecond > 0);
            Assert.True(snapshot.ManagedHeapMegaBytes > 0);
        }

        [Fact]
        public void ConcurrentRecording_HandlesMultiThreadedIncrements()
        {
            var tracker = new SessionPerformanceTracker();
            const int iterations = 1000;

            Parallel.For(0, iterations, _ =>
            {
                tracker.RecordInboundDatagram(10);
                tracker.RecordInboundPacket();
                tracker.RecordOutboundDatagram(20);
                tracker.RecordOutboundPacket();
            });

            var snapshot = tracker.GetSnapshot();

            Assert.Equal(iterations, snapshot.PacketsReceivedTotal);
            Assert.Equal(iterations * 10, snapshot.BytesReceivedTotal);
            Assert.Equal(iterations, snapshot.PacketsSentTotal);
            Assert.Equal(iterations * 20, snapshot.BytesSentTotal);
        }

        [Fact]
        public void RecordDispatchLatencyTicks_ComputesMicrosecondsAccurately()
        {
            var tracker = new SessionPerformanceTracker();

            // Record 1 millisecond (1000 microseconds) worth of ticks
            long oneMilliTicks = System.Diagnostics.Stopwatch.Frequency / 1000;
            tracker.RecordDispatchLatencyTicks(oneMilliTicks);

            var snapshot = tracker.GetSnapshot();

            // Should be approximately 1000 microseconds (+- 10 microseconds for integer division rounding)
            Assert.InRange(snapshot.LastDispatchLatencyMicroseconds, 990, 1010);
            Assert.InRange(snapshot.AverageDispatchLatencyMicroseconds, 990, 1010);

            // Record a second dispatch of 2000 microseconds
            long twoMilliTicks = (System.Diagnostics.Stopwatch.Frequency / 1000) * 2;
            tracker.RecordDispatchLatencyTicks(twoMilliTicks);

            snapshot = tracker.GetSnapshot();
            Assert.InRange(snapshot.LastDispatchLatencyMicroseconds, 1990, 2010);
            // Average of 1000 and 2000 is 1500
            Assert.InRange(snapshot.AverageDispatchLatencyMicroseconds, 1490, 1510);
        }

        [Fact]
        public async Task RollingAverage_AccumulatesAcrossInterval()
        {
            var tracker = new SessionPerformanceTracker();

            tracker.RecordInboundPackets(10);
            tracker.RecordOutboundPackets(5);
            tracker.RecordInboundDatagram(1024);
            tracker.RecordOutboundDatagram(512);

            // Wait 1.1s so 30-second rolling sample is recorded
            await Task.Delay(1100);

            var snapshot = tracker.GetSnapshot();

            Assert.True(snapshot.PacketsReceived30SecAverage > 0);
            Assert.True(snapshot.PacketsSent30SecAverage > 0);
            Assert.True(snapshot.KilobytesReceived30SecAverage > 0);
            Assert.True(snapshot.KilobytesSent30SecAverage > 0);
        }
    }
}
