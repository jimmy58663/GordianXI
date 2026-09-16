// src/Gordian.Core/Diagnostics/MemoryHealthTracker.cs
using System;
using System.Diagnostics;
using System.Threading;

namespace Gordian.Core.Diagnostics
{
    /// <summary>
    /// Immutable snapshot of managed runtime memory and garbage collector telemetry.
    /// </summary>
    public readonly record struct MemoryHealthSnapshot(
        long ManagedHeapSizeBytes,
        long PeakHeapSizeBytes,
        long TotalAllocatedBytes,
        double AllocationVelocityBytesPerSecond,
        int Gen0Collections,
        int Gen1Collections,
        int Gen2Collections,
        double Gen0CollectionsPerSecond,
        double Gen1CollectionsPerSecond,
        double Gen2CollectionsPerSecond,
        double MemoryLoadPercentage,
        double PauseDurationPercentage,
        long FragmentedBytes,
        long TotalAvailableMemoryBytes,
        DateTime TimestampUtc
    )
    {
        /// <summary>
        /// Gets current managed heap size formatted in Megabytes.
        /// </summary>
        public double ManagedHeapMegaBytes => ManagedHeapSizeBytes / (1024.0 * 1024.0);

        /// <summary>
        /// Gets peak managed heap size formatted in Megabytes.
        /// </summary>
        public double PeakHeapMegaBytes => PeakHeapSizeBytes / (1024.0 * 1024.0);

        /// <summary>
        /// Gets allocation velocity formatted in Megabytes per second.
        /// </summary>
        public double AllocationVelocityMegaBytesPerSecond => AllocationVelocityBytesPerSecond / (1024.0 * 1024.0);

        /// <summary>
        /// Gets total available system memory formatted in Megabytes.
        /// </summary>
        public double TotalAvailableMemoryMegaBytes => TotalAvailableMemoryBytes / (1024.0 * 1024.0);
    }

    /// <summary>
    /// Thread-safe performance tracker for .NET 10 managed heap, GC pressure,
    /// and allocation velocity metrics. Operates with zero heap allocations in the monitoring path.
    /// </summary>
    public sealed class MemoryHealthTracker
    {
        private long _peakHeapSizeBytes;

        private long _lastSampleTicks;
        private long _lastAllocatedBytes;
        private int _lastGen0;
        private int _lastGen1;
        private int _lastGen2;

        private double _allocationRateBytesPerSec;
        private double _gen0RatePerSec;
        private double _gen1RatePerSec;
        private double _gen2RatePerSec;

        private readonly object _sampleLock = new object();

        public MemoryHealthTracker()
        {
            _lastSampleTicks = Stopwatch.GetTimestamp();
            _lastAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
            _lastGen0 = GC.CollectionCount(0);
            _lastGen1 = GC.CollectionCount(1);
            _lastGen2 = GC.CollectionCount(2);
            long initialHeap = GC.GetTotalMemory(forceFullCollection: false);
            _peakHeapSizeBytes = initialHeap;
        }

        /// <summary>
        /// Captures and computes the latest memory health metrics and rolling allocation velocities.
        /// </summary>
        public MemoryHealthSnapshot GetSnapshot()
        {
            long currentTicks = Stopwatch.GetTimestamp();
            long currentHeap = GC.GetTotalMemory(forceFullCollection: false);
            long totalAllocated = GC.GetTotalAllocatedBytes(precise: false);
            int gen0 = GC.CollectionCount(0);
            int gen1 = GC.CollectionCount(1);
            int gen2 = GC.CollectionCount(2);

            // Update peak heap size atomically
            long currentPeak = Interlocked.Read(ref _peakHeapSizeBytes);
            while (currentHeap > currentPeak)
            {
                long prev = Interlocked.CompareExchange(ref _peakHeapSizeBytes, currentHeap, currentPeak);
                if (prev == currentPeak) break;
                currentPeak = prev;
            }

            // Obtain GC runtime metrics
            GCMemoryInfo gcInfo = GC.GetGCMemoryInfo();
            double memoryLoad = gcInfo.TotalAvailableMemoryBytes > 0
                ? (double)gcInfo.MemoryLoadBytes / gcInfo.TotalAvailableMemoryBytes * 100.0
                : 0.0;
            double pausePercentage = gcInfo.PauseTimePercentage;
            long fragmentedBytes = gcInfo.FragmentedBytes;
            long totalAvailable = gcInfo.TotalAvailableMemoryBytes;

            lock (_sampleLock)
            {
                double elapsedSeconds = (double)(currentTicks - _lastSampleTicks) / Stopwatch.Frequency;
                if (elapsedSeconds >= 0.25) // Update rate window every 250ms minimum
                {
                    long deltaAllocated = totalAllocated - _lastAllocatedBytes;
                    int deltaGen0 = gen0 - _lastGen0;
                    int deltaGen1 = gen1 - _lastGen1;
                    int deltaGen2 = gen2 - _lastGen2;

                    _allocationRateBytesPerSec = deltaAllocated > 0 ? deltaAllocated / elapsedSeconds : 0.0;
                    _gen0RatePerSec = deltaGen0 > 0 ? deltaGen0 / elapsedSeconds : 0.0;
                    _gen1RatePerSec = deltaGen1 > 0 ? deltaGen1 / elapsedSeconds : 0.0;
                    _gen2RatePerSec = deltaGen2 > 0 ? deltaGen2 / elapsedSeconds : 0.0;

                    _lastAllocatedBytes = totalAllocated;
                    _lastGen0 = gen0;
                    _lastGen1 = gen1;
                    _lastGen2 = gen2;
                    _lastSampleTicks = currentTicks;
                }

                return new MemoryHealthSnapshot(
                    ManagedHeapSizeBytes: currentHeap,
                    PeakHeapSizeBytes: Interlocked.Read(ref _peakHeapSizeBytes),
                    TotalAllocatedBytes: totalAllocated,
                    AllocationVelocityBytesPerSecond: _allocationRateBytesPerSec,
                    Gen0Collections: gen0,
                    Gen1Collections: gen1,
                    Gen2Collections: gen2,
                    Gen0CollectionsPerSecond: _gen0RatePerSec,
                    Gen1CollectionsPerSecond: _gen1RatePerSec,
                    Gen2CollectionsPerSecond: _gen2RatePerSec,
                    MemoryLoadPercentage: memoryLoad,
                    PauseDurationPercentage: pausePercentage,
                    FragmentedBytes: fragmentedBytes,
                    TotalAvailableMemoryBytes: totalAvailable,
                    TimestampUtc: DateTime.UtcNow
                );
            }
        }
    }
}
