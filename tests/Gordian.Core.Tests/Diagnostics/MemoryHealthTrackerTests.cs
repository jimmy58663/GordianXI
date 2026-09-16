// tests/Gordian.Core.Tests/Diagnostics/MemoryHealthTrackerTests.cs
using System;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Xunit;

namespace Gordian.Core.Tests.Diagnostics
{
    public sealed class MemoryHealthTrackerTests
    {
        [Fact]
        public void InitialSnapshot_PopulatesBaselineMetrics()
        {
            var tracker = new MemoryHealthTracker();
            var snapshot = tracker.GetSnapshot();

            Assert.True(snapshot.ManagedHeapSizeBytes > 0);
            Assert.True(snapshot.ManagedHeapMegaBytes > 0);
            Assert.True(snapshot.PeakHeapSizeBytes >= snapshot.ManagedHeapSizeBytes);
            Assert.True(snapshot.PeakHeapMegaBytes > 0);
            Assert.True(snapshot.TotalAllocatedBytes > 0);
            Assert.True(snapshot.Gen0Collections >= 0);
            Assert.True(snapshot.Gen1Collections >= 0);
            Assert.True(snapshot.Gen2Collections >= 0);
            Assert.True(snapshot.TimestampUtc <= DateTime.UtcNow);
        }

        [Fact]
        public async Task AllocationVelocity_CalculatesRateAccurately()
        {
            var tracker = new MemoryHealthTracker();

            // Allocate some heap memory to produce allocation velocity
            byte[][] buffers = new byte[100][];
            for (int i = 0; i < buffers.Length; i++)
            {
                buffers[i] = new byte[100 * 1024]; // 100 KB each = 10 MB total
            }

            // Wait for 260ms window
            await Task.Delay(260);

            var snapshot = tracker.GetSnapshot();

            Assert.True(snapshot.TotalAllocatedBytes > 0);
            Assert.True(snapshot.AllocationVelocityBytesPerSecond > 0, "Allocation velocity should be greater than zero after heap allocation.");
            Assert.True(snapshot.AllocationVelocityMegaBytesPerSecond > 0);

            GC.KeepAlive(buffers);
        }

        [Fact]
        public void MultiThreadedAccess_ProducesConsistentSnapshots()
        {
            var tracker = new MemoryHealthTracker();
            const int threadCount = 8;
            const int iterationsPerThread = 50;

            Parallel.For(0, threadCount, _ =>
            {
                for (int i = 0; i < iterationsPerThread; i++)
                {
                    var snapshot = tracker.GetSnapshot();
                    Assert.True(snapshot.ManagedHeapSizeBytes > 0);
                    Assert.True(snapshot.PeakHeapSizeBytes >= snapshot.ManagedHeapSizeBytes);
                }
            });
        }
    }
}
