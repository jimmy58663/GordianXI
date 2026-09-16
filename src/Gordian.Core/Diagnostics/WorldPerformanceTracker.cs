// src/Gordian.Core/Diagnostics/WorldPerformanceTracker.cs
using System;

namespace Gordian.Core.Diagnostics
{
    /// <summary>
    /// Combined immutable snapshot of world state performance, spatial partitioning metrics,
    /// and dead-reckoning cycle diagnostics.
    /// </summary>
    public readonly record struct WorldPerformanceSnapshot(
        int ActiveEntityCount,
        SpatialPerformanceSnapshot Spatial,
        DeadReckoningSnapshot DeadReckoning,
        DateTime TimestampUtc
    );

    /// <summary>
    /// Unified telemetry aggregator for world state, encapsulating spatial partitioning
    /// performance and entity dead-reckoning cycle tracking.
    /// </summary>
    public sealed class WorldPerformanceTracker
    {
        public SpatialPerformanceTracker Spatial { get; } = new SpatialPerformanceTracker();
        public DeadReckoningTracker DeadReckoning { get; } = new DeadReckoningTracker();

        /// <summary>
        /// Captures an aggregated snapshot of world performance metrics.
        /// </summary>
        public WorldPerformanceSnapshot GetSnapshot(int activeEntityCount)
        {
            return new WorldPerformanceSnapshot(
                ActiveEntityCount: activeEntityCount,
                Spatial: Spatial.GetSnapshot(),
                DeadReckoning: DeadReckoning.GetSnapshot(),
                TimestampUtc: DateTime.UtcNow
            );
        }
    }
}
