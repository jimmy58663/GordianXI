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
    }
}
