// src/Gordian.Core/Diagnostics/DeadReckoningTracker.cs
using System;
using System.Diagnostics;
using System.Threading;

namespace Gordian.Core.Diagnostics
{
    /// <summary>
    /// Immutable point-in-time snapshot of entity dead-reckoning cycle performance metrics.
    /// </summary>
    public readonly record struct DeadReckoningSnapshot(
        long TotalCycles,
        long TotalEntitiesEvaluated,
        double LastCycleMicroseconds,
        double AverageCycleMicroseconds,
        double PeakCycleMicroseconds,
        double AverageEntitiesPerCycle,
        DateTime TimestampUtc
    );

    /// <summary>
    /// Detailed results from a dead-reckoning benchmark execution run.
    /// </summary>
    public sealed record DeadReckoningBenchmarkResult(
        int Iterations,
        int EntityCount,
        double TotalElapsedMilliseconds,
        double MinCycleMicroseconds,
        double MaxCycleMicroseconds,
        double AvgCycleMicroseconds,
        double P95CycleMicroseconds,
        double P99CycleMicroseconds,
        double ThroughputEntitiesPerSecond
    );

    /// <summary>
    /// High-performance telemetry tracker for entity dead-reckoning extrapolation passes and benchmarking.
    /// Uses lock-free atomic counters for zero allocation in the simulation loop.
    /// </summary>
    public sealed class DeadReckoningTracker
    {
        private long _totalCycles;
        private long _totalEntitiesEvaluated;
        private long _lastCycleTicks;
        private long _totalCycleTicks;
        private long _peakCycleTicks;

        /// <summary>
        /// Records an executed dead-reckoning cycle.
        /// </summary>
        public void RecordCycle(int entityCount, long elapsedTicks)
        {
            if (elapsedTicks < 0) return;

            Interlocked.Increment(ref _totalCycles);
            if (entityCount > 0)
            {
                Interlocked.Add(ref _totalEntitiesEvaluated, entityCount);
            }
            Interlocked.Exchange(ref _lastCycleTicks, elapsedTicks);
            Interlocked.Add(ref _totalCycleTicks, elapsedTicks);

            long currentPeak = Interlocked.Read(ref _peakCycleTicks);
            while (elapsedTicks > currentPeak)
            {
                long prev = Interlocked.CompareExchange(ref _peakCycleTicks, elapsedTicks, currentPeak);
                if (prev == currentPeak) break;
                currentPeak = prev;
            }
        }

        private static double TicksToMicroseconds(long ticks) =>
            (double)ticks * 1_000_000.0 / Stopwatch.Frequency;

        /// <summary>
        /// Captures an immutable snapshot of dead-reckoning performance metrics.
        /// </summary>
        public DeadReckoningSnapshot GetSnapshot()
        {
            long cycles = Interlocked.Read(ref _totalCycles);
            long entities = Interlocked.Read(ref _totalEntitiesEvaluated);
            long lastTicks = Interlocked.Read(ref _lastCycleTicks);
            long totalTicks = Interlocked.Read(ref _totalCycleTicks);
            long peakTicks = Interlocked.Read(ref _peakCycleTicks);

            double lastUs = TicksToMicroseconds(lastTicks);
            double avgUs = cycles > 0 ? TicksToMicroseconds(totalTicks) / cycles : 0.0;
            double peakUs = TicksToMicroseconds(peakTicks);
            double avgEntities = cycles > 0 ? (double)entities / cycles : 0.0;

            return new DeadReckoningSnapshot(
                TotalCycles: cycles,
                TotalEntitiesEvaluated: entities,
                LastCycleMicroseconds: lastUs,
                AverageCycleMicroseconds: avgUs,
                PeakCycleMicroseconds: peakUs,
                AverageEntitiesPerCycle: avgEntities,
                TimestampUtc: DateTime.UtcNow
            );
        }
    }
}
