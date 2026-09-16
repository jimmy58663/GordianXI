// src/Gordian.Core/Diagnostics/SpatialPerformanceTracker.cs
using System;
using System.Diagnostics;
using System.Threading;

namespace Gordian.Core.Diagnostics
{
    /// <summary>
    /// Type of spatial partition query executed against the uniform grid.
    /// </summary>
    public enum SpatialQueryType
    {
        Radius = 0,
        Nearest = 1,
        Cone = 2
    }

    /// <summary>
    /// Type of spatial partition grid mutation.
    /// </summary>
    public enum SpatialOperationType
    {
        InsertOrUpdate = 0,
        Remove = 1,
        Clear = 2
    }

    /// <summary>
    /// Immutable point-in-time snapshot of 3D spatial partition query and mutation performance.
    /// </summary>
    public readonly record struct SpatialPerformanceSnapshot(
        long TotalQueries,
        long RadiusQueries,
        double RadiusAvgMicroseconds,
        double RadiusPeakMicroseconds,
        long NearestQueries,
        double NearestAvgMicroseconds,
        double NearestPeakMicroseconds,
        long ConeQueries,
        double ConeAvgMicroseconds,
        double ConePeakMicroseconds,
        long TotalUpdates,
        double UpdateAvgMicroseconds,
        double UpdatePeakMicroseconds,
        long TotalRemoves,
        double RemoveAvgMicroseconds,
        double RemovePeakMicroseconds,
        double OverallAvgQueryMicroseconds,
        double OverallPeakQueryMicroseconds,
        DateTime TimestampUtc
    );

    /// <summary>
    /// High-performance, lock-free telemetry tracker for 3D uniform spatial partition queries and mutations.
    /// Utilizes atomic updates and Stopwatch timestamps to operate with zero allocations.
    /// </summary>
    public sealed class SpatialPerformanceTracker
    {
        // Query counters and durations
        private long _radiusCount;
        private long _radiusTicksTotal;
        private long _radiusPeakTicks;

        private long _nearestCount;
        private long _nearestTicksTotal;
        private long _nearestPeakTicks;

        private long _coneCount;
        private long _coneTicksTotal;
        private long _conePeakTicks;

        // Mutation counters and durations
        private long _updateCount;
        private long _updateTicksTotal;
        private long _updatePeakTicks;

        private long _removeCount;
        private long _removeTicksTotal;
        private long _removePeakTicks;

        /// <summary>
        /// Records high-resolution elapsed ticks for a spatial query.
        /// </summary>
        public void RecordQuery(SpatialQueryType type, long elapsedTicks)
        {
            if (elapsedTicks < 0) return;

            switch (type)
            {
                case SpatialQueryType.Radius:
                    Interlocked.Increment(ref _radiusCount);
                    Interlocked.Add(ref _radiusTicksTotal, elapsedTicks);
                    UpdatePeak(ref _radiusPeakTicks, elapsedTicks);
                    break;
                case SpatialQueryType.Nearest:
                    Interlocked.Increment(ref _nearestCount);
                    Interlocked.Add(ref _nearestTicksTotal, elapsedTicks);
                    UpdatePeak(ref _nearestPeakTicks, elapsedTicks);
                    break;
                case SpatialQueryType.Cone:
                    Interlocked.Increment(ref _coneCount);
                    Interlocked.Add(ref _coneTicksTotal, elapsedTicks);
                    UpdatePeak(ref _conePeakTicks, elapsedTicks);
                    break;
            }
        }

        /// <summary>
        /// Records high-resolution elapsed ticks for a spatial grid mutation.
        /// </summary>
        public void RecordOperation(SpatialOperationType type, long elapsedTicks)
        {
            if (elapsedTicks < 0) return;

            switch (type)
            {
                case SpatialOperationType.InsertOrUpdate:
                    Interlocked.Increment(ref _updateCount);
                    Interlocked.Add(ref _updateTicksTotal, elapsedTicks);
                    UpdatePeak(ref _updatePeakTicks, elapsedTicks);
                    break;
                case SpatialOperationType.Remove:
                    Interlocked.Increment(ref _removeCount);
                    Interlocked.Add(ref _removeTicksTotal, elapsedTicks);
                    UpdatePeak(ref _removePeakTicks, elapsedTicks);
                    break;
            }
        }

        private static void UpdatePeak(ref long peakField, long value)
        {
            long current = Interlocked.Read(ref peakField);
            while (value > current)
            {
                long prev = Interlocked.CompareExchange(ref peakField, value, current);
                if (prev == current) break;
                current = prev;
            }
        }

        private static double TicksToMicroseconds(long ticks) =>
            (double)ticks * 1_000_000.0 / Stopwatch.Frequency;

        /// <summary>
        /// Captures an immutable snapshot of all spatial partition performance metrics.
        /// </summary>
        public SpatialPerformanceSnapshot GetSnapshot()
        {
            long rCount = Interlocked.Read(ref _radiusCount);
            long rTicks = Interlocked.Read(ref _radiusTicksTotal);
            long rPeak = Interlocked.Read(ref _radiusPeakTicks);

            long nCount = Interlocked.Read(ref _nearestCount);
            long nTicks = Interlocked.Read(ref _nearestTicksTotal);
            long nPeak = Interlocked.Read(ref _nearestPeakTicks);

            long cCount = Interlocked.Read(ref _coneCount);
            long cTicks = Interlocked.Read(ref _coneTicksTotal);
            long cPeak = Interlocked.Read(ref _conePeakTicks);

            long uCount = Interlocked.Read(ref _updateCount);
            long uTicks = Interlocked.Read(ref _updateTicksTotal);
            long uPeak = Interlocked.Read(ref _updatePeakTicks);

            long remCount = Interlocked.Read(ref _removeCount);
            long remTicks = Interlocked.Read(ref _removeTicksTotal);
            long remPeak = Interlocked.Read(ref _removePeakTicks);

            long totalQueries = rCount + nCount + cCount;
            long totalQueryTicks = rTicks + nTicks + cTicks;
            long maxQueryPeak = Math.Max(rPeak, Math.Max(nPeak, cPeak));

            double rAvg = rCount > 0 ? TicksToMicroseconds(rTicks) / rCount : 0.0;
            double nAvg = nCount > 0 ? TicksToMicroseconds(nTicks) / nCount : 0.0;
            double cAvg = cCount > 0 ? TicksToMicroseconds(cTicks) / cCount : 0.0;
            double uAvg = uCount > 0 ? TicksToMicroseconds(uTicks) / uCount : 0.0;
            double remAvg = remCount > 0 ? TicksToMicroseconds(remTicks) / remCount : 0.0;

            double overallAvg = totalQueries > 0 ? TicksToMicroseconds(totalQueryTicks) / totalQueries : 0.0;

            return new SpatialPerformanceSnapshot(
                TotalQueries: totalQueries,
                RadiusQueries: rCount,
                RadiusAvgMicroseconds: rAvg,
                RadiusPeakMicroseconds: TicksToMicroseconds(rPeak),
                NearestQueries: nCount,
                NearestAvgMicroseconds: nAvg,
                NearestPeakMicroseconds: TicksToMicroseconds(nPeak),
                ConeQueries: cCount,
                ConeAvgMicroseconds: cAvg,
                ConePeakMicroseconds: TicksToMicroseconds(cPeak),
                TotalUpdates: uCount,
                UpdateAvgMicroseconds: uAvg,
                UpdatePeakMicroseconds: TicksToMicroseconds(uPeak),
                TotalRemoves: remCount,
                RemoveAvgMicroseconds: remAvg,
                RemovePeakMicroseconds: TicksToMicroseconds(remPeak),
                OverallAvgQueryMicroseconds: overallAvg,
                OverallPeakQueryMicroseconds: TicksToMicroseconds(maxQueryPeak),
                TimestampUtc: DateTime.UtcNow
            );
        }
    }
}
