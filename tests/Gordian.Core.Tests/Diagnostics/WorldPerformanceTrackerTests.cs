// tests/Gordian.Core.Tests/Diagnostics/WorldPerformanceTrackerTests.cs
using System;
using System.Numerics;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Diagnostics
{
    public sealed class WorldPerformanceTrackerTests
    {
        [Fact]
        public void SpatialPerformanceTracker_RecordsQueriesAndPeaksAccurately()
        {
            var tracker = new SpatialPerformanceTracker();

            long freq = System.Diagnostics.Stopwatch.Frequency;
            long tenMicroTicks = freq / 100_000; // 10 microseconds
            long twentyMicroTicks = freq / 50_000; // 20 microseconds

            tracker.RecordQuery(SpatialQueryType.Radius, tenMicroTicks);
            tracker.RecordQuery(SpatialQueryType.Radius, twentyMicroTicks);
            tracker.RecordQuery(SpatialQueryType.Nearest, tenMicroTicks);
            tracker.RecordQuery(SpatialQueryType.Cone, twentyMicroTicks);
            tracker.RecordOperation(SpatialOperationType.InsertOrUpdate, tenMicroTicks);
            tracker.RecordOperation(SpatialOperationType.Remove, tenMicroTicks);

            var snapshot = tracker.GetSnapshot();

            Assert.Equal(4, snapshot.TotalQueries);
            Assert.Equal(2, snapshot.RadiusQueries);
            Assert.Equal(1, snapshot.NearestQueries);
            Assert.Equal(1, snapshot.ConeQueries);
            Assert.Equal(1, snapshot.TotalUpdates);
            Assert.Equal(1, snapshot.TotalRemoves);

            Assert.InRange(snapshot.RadiusPeakMicroseconds, 19, 21);
            Assert.InRange(snapshot.RadiusAvgMicroseconds, 14, 16);
            Assert.InRange(snapshot.NearestAvgMicroseconds, 9, 11);
            Assert.InRange(snapshot.ConeAvgMicroseconds, 19, 21);
            Assert.InRange(snapshot.OverallAvgQueryMicroseconds, 14, 16);
        }

        [Fact]
        public void SpatialPartitionGrid_TracksQueryDurationsAutomatically()
        {
            var grid = new SpatialPartitionGrid(cellSize: 10.0f);

            var e1 = new WorldEntity(1, 1, EntityType.Player) { Position = new Vector3(0, 0, 0) };
            var e2 = new WorldEntity(2, 2, EntityType.Monster) { Position = new Vector3(5, 0, 5) };

            grid.InsertOrUpdate(e1);
            grid.InsertOrUpdate(e2);

            // Execute queries
            var radiusResults = grid.GetEntitiesInRadius(Vector3.Zero, 10f);
            var nearestResult = grid.GetNearestEntity(Vector3.Zero);
            var coneResults = grid.GetEntitiesInCone(Vector3.Zero, new Vector3(1, 0, 0), 60f, 20f);

            grid.Remove(e1);

            var snapshot = grid.Performance.GetSnapshot();

            Assert.Equal(2, snapshot.TotalUpdates);
            Assert.Equal(1, snapshot.TotalRemoves);
            // Notice: GetNearestEntity and GetEntitiesInCone also query in radius internally,
            // so radius query count is at least 1.
            Assert.True(snapshot.RadiusQueries >= 1);
            Assert.Equal(1, snapshot.NearestQueries);
            Assert.Equal(1, snapshot.ConeQueries);
            Assert.True(snapshot.TotalQueries >= 3);
            Assert.True(snapshot.OverallAvgQueryMicroseconds >= 0);
        }

        [Fact]
        public void DeadReckoningTracker_RecordsCycleStatistics()
        {
            var tracker = new DeadReckoningTracker();
            long freq = System.Diagnostics.Stopwatch.Frequency;
            long fiftyMicroTicks = freq / 20_000; // 50 microseconds
            long hundredMicroTicks = freq / 10_000; // 100 microseconds

            tracker.RecordCycle(10, fiftyMicroTicks);
            tracker.RecordCycle(20, hundredMicroTicks);

            var snapshot = tracker.GetSnapshot();

            Assert.Equal(2, snapshot.TotalCycles);
            Assert.Equal(30, snapshot.TotalEntitiesEvaluated);
            Assert.Equal(15.0, snapshot.AverageEntitiesPerCycle);
            Assert.InRange(snapshot.LastCycleMicroseconds, 98, 102);
            Assert.InRange(snapshot.AverageCycleMicroseconds, 73, 77);
            Assert.InRange(snapshot.PeakCycleMicroseconds, 98, 102);
        }

        [Fact]
        public void WorldState_ExtrapolateAll_UpdatesMovingEntitiesAndTelemetry()
        {
            var world = new WorldState();

            var stationary = new WorldEntity(1, 1, EntityType.Player)
            {
                Position = new Vector3(0, 0, 0),
                Direction = 0,
                Speed = 0
            };

            var moving = new WorldEntity(2, 2, EntityType.Player)
            {
                Position = new Vector3(0, 0, 0),
                Direction = 0, // East (+X)
                Speed = 50     // 5 yalms/sec
            };

            world.UpsertEntity(stationary);
            world.UpsertEntity(moving);

            int evaluated = world.ExtrapolateAll(TimeSpan.FromSeconds(1.0), updateSpatialGrid: true);

            Assert.Equal(2, evaluated);
            Assert.Equal(Vector3.Zero, stationary.Position);
            Assert.InRange(moving.Position.X, 4.9f, 5.1f);

            var perf = world.Performance.GetSnapshot(world.Count);
            Assert.Equal(2, perf.ActiveEntityCount);
            Assert.Equal(1, perf.DeadReckoning.TotalCycles);
            Assert.Equal(2, perf.DeadReckoning.TotalEntitiesEvaluated);
            Assert.True(perf.DeadReckoning.AverageCycleMicroseconds >= 0);
        }

        [Fact]
        public void WorldState_BenchmarkDeadReckoning_ProducesDetailedMetrics()
        {
            var world = new WorldState();

            for (uint i = 1; i <= 20; i++)
            {
                world.UpsertEntity(new WorldEntity(i, (ushort)i, EntityType.Monster)
                {
                    Position = new Vector3(i, 0, i),
                    Direction = (byte)(i * 10),
                    Speed = 50
                });
            }

            var result = world.BenchmarkDeadReckoning(iterations: 50, TimeSpan.FromMilliseconds(250), updateSpatialGrid: true);

            Assert.Equal(50, result.Iterations);
            Assert.Equal(20, result.EntityCount);
            Assert.True(result.TotalElapsedMilliseconds > 0);
            Assert.True(result.AvgCycleMicroseconds >= 0);
            Assert.True(result.MinCycleMicroseconds <= result.AvgCycleMicroseconds);
            Assert.True(result.MaxCycleMicroseconds >= result.AvgCycleMicroseconds);
            Assert.True(result.P95CycleMicroseconds <= result.MaxCycleMicroseconds);
            Assert.True(result.P99CycleMicroseconds <= result.MaxCycleMicroseconds);
            Assert.True(result.ThroughputEntitiesPerSecond > 0);
        }

        [Fact]
        public void WorldState_BenchmarkSyntheticEntities_HandlesHighDensity()
        {
            // Benchmark 200 synthetic entities across 20 iterations
            var result = WorldState.BenchmarkSyntheticEntities(
                entityCount: 200,
                iterations: 20,
                elapsed: TimeSpan.FromMilliseconds(250)
            );

            Assert.Equal(20, result.Iterations);
            Assert.Equal(200, result.EntityCount);
            Assert.True(result.TotalElapsedMilliseconds > 0);
            Assert.True(result.AvgCycleMicroseconds > 0);
            Assert.True(result.ThroughputEntitiesPerSecond > 0);
        }
    }
}
