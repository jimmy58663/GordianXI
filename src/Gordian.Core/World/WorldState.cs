using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Gordian.Core.Diagnostics;

namespace Gordian.Core.World
{
    /// <summary>
    /// Thread-safe active game world container maintaining tracked entities,
    /// dual ServerId / TargetIndex indexing, 3D spatial partitioning, and dead-reckoning extrapolation.
    /// </summary>
    public sealed class WorldState
    {
        private readonly object _syncRoot = new object();
        private readonly Dictionary<uint, WorldEntity> _byServerId = new Dictionary<uint, WorldEntity>();
        private readonly Dictionary<ushort, WorldEntity> _byTargetIndex = new Dictionary<ushort, WorldEntity>();
        private readonly WorldPerformanceTracker _performance = new WorldPerformanceTracker();
        private readonly SpatialPartitionGrid _grid;

        /// <summary>
        /// Gets the real-time spatial query and dead-reckoning cycle performance telemetry tracker.
        /// </summary>
        public WorldPerformanceTracker Performance => _performance;

        public SpatialPartitionGrid Grid => _grid;

        public WorldState()
        {
            _grid = new SpatialPartitionGrid(performance: _performance.Spatial);
        }
        public int Count
        {
            get
            {
                lock (_syncRoot) return _byServerId.Count;
            }
        }

        public event Action<WorldEntity>? EntitySpawned;
        public event Action<WorldEntity>? EntityUpdated;
        public event Action<WorldEntity>? EntityDespawned;
        public event Action? WorldCleared;
        public event Action<ushort>? ZoneChanged;
        public event Action<string>? WeatherChanged;

        private ushort _currentZoneId;
        public ushort CurrentZoneId
        {
            get
            {
                lock (_syncRoot) return _currentZoneId;
            }
            set
            {
                bool changed = false;
                lock (_syncRoot)
                {
                    if (_currentZoneId != value)
                    {
                        _currentZoneId = value;
                        changed = true;
                    }
                }
                if (changed)
                {
                    ZoneChanged?.Invoke(value);
                }
            }
        }

        private ushort _weatherNumber;
        private string _weatherId = "fine";

        public ushort WeatherNumber
        {
            get
            {
                lock (_syncRoot) return _weatherNumber;
            }
        }

        public string WeatherId
        {
            get
            {
                lock (_syncRoot) return _weatherId;
            }
            set
            {
                bool changed = false;
                lock (_syncRoot)
                {
                    if (_weatherId != value)
                    {
                        _weatherId = value;
                        changed = true;
                    }
                }
                if (changed)
                {
                    WeatherChanged?.Invoke(value);
                }
            }
        }

        public void UpdateWeather(ushort weatherNumber)
        {
            bool changed = false;
            string weatherId = VanaTime.GetWeatherId(weatherNumber);
            lock (_syncRoot)
            {
                if (_weatherNumber != weatherNumber || _weatherId != weatherId)
                {
                    _weatherNumber = weatherNumber;
                    _weatherId = weatherId;
                    changed = true;
                }
            }
            if (changed)
            {
                GordianLog.Info("WORLD", $"Weather updated to #{weatherNumber} ('{weatherId}')");
                WeatherChanged?.Invoke(weatherId);
            }
        }

        /// <summary>
        /// Inserts a newly discovered entity or updates an existing one in thread-safe fashion.
        /// </summary>
        public void UpsertEntity(WorldEntity entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            bool isNew = false;

            lock (_syncRoot)
            {
                if (_byServerId.TryGetValue(entity.ServerId, out var existing))
                {
                    // Update indices in case TargetIndex shifted
                    if (existing.TargetIndex != entity.TargetIndex)
                    {
                        _byTargetIndex.Remove(existing.TargetIndex);
                        existing.TargetIndex = entity.TargetIndex;
                    }
                    _byTargetIndex[entity.TargetIndex] = existing;
                }
                else
                {
                    _byServerId[entity.ServerId] = entity;
                    _byTargetIndex[entity.TargetIndex] = entity;
                    isNew = true;
                }

                _grid.InsertOrUpdate(entity);
            }

            if (isNew)
            {
                EntitySpawned?.Invoke(entity);
            }
            else
            {
                EntityUpdated?.Invoke(entity);
            }
        }

        /// <summary>
        /// Removes an entity by Server ID (e.g. on despawn or zone transition).
        /// </summary>
        public bool RemoveEntity(uint serverId)
        {
            WorldEntity? removed = null;
            lock (_syncRoot)
            {
                if (_byServerId.Remove(serverId, out removed))
                {
                    _byTargetIndex.Remove(removed.TargetIndex);
                    _grid.Remove(removed);
                }
            }

            if (removed != null)
            {
                removed.IsSpawned = false;
                EntityDespawned?.Invoke(removed);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Removes an entity by zone Target Index (Actor Index).
        /// </summary>
        public bool RemoveEntityByTargetIndex(ushort targetIndex)
        {
            uint serverId = 0;
            lock (_syncRoot)
            {
                if (_byTargetIndex.TryGetValue(targetIndex, out var entity))
                {
                    serverId = entity.ServerId;
                }
            }

            return serverId != 0 && RemoveEntity(serverId);
        }

        /// <summary>
        /// Attempts to retrieve an entity by Server ID.
        /// </summary>
        public bool TryGetByServerId(uint serverId, out WorldEntity? entity)
        {
            lock (_syncRoot)
            {
                return _byServerId.TryGetValue(serverId, out entity);
            }
        }

        /// <summary>
        /// Attempts to retrieve an entity by zone Target Index (Actor Index).
        /// </summary>
        public bool TryGetByTargetIndex(ushort targetIndex, out WorldEntity? entity)
        {
            lock (_syncRoot)
            {
                return _byTargetIndex.TryGetValue(targetIndex, out entity);
            }
        }

        /// <summary>
        /// Attempts to retrieve an entity by character name (case-insensitive).
        /// </summary>
        public bool TryGetByName(string name, out WorldEntity? entity)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                entity = null;
                return false;
            }

            lock (_syncRoot)
            {
                foreach (var e in _byServerId.Values)
                {
                    if (string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        entity = e;
                        return true;
                    }
                }
            }

            entity = null;
            return false;
        }

        /// <summary>
        /// Retrieves a snapshot array of all active entities.
        /// </summary>
        public WorldEntity[] GetAllEntities()
        {
            lock (_syncRoot)
            {
                var array = new WorldEntity[_byServerId.Count];
                _byServerId.Values.CopyTo(array, 0);
                return array;
            }
        }

        /// <summary>
        /// Gets an enumerable sequence of all active entities.
        /// </summary>
        public IEnumerable<WorldEntity> Entities => GetAllEntities();

        /// <summary>
        /// Retrieves all entities within a 3D radius.
        /// </summary>
        public List<WorldEntity> GetEntitiesInRadius(Vector3 center, float radius)
        {
            return _grid.GetEntitiesInRadius(center, radius);
        }

        /// <summary>
        /// Retrieves the closest entity to the specified position.
        /// </summary>
        public WorldEntity? GetNearestEntity(Vector3 center, EntityType? filter = null, float maxSearchRadius = 100.0f)
        {
            return _grid.GetNearestEntity(center, filter, maxSearchRadius);
        }

        /// <summary>
        /// Clears all entities in the zone (e.g. upon zone transition).
        /// </summary>
        public void Clear()
        {
            lock (_syncRoot)
            {
                _byServerId.Clear();
                _byTargetIndex.Clear();
                _grid.Clear();
            }
            WorldCleared?.Invoke();
        }

        /// <summary>
        /// Calculates dead-reckoning projected coordinates given elapsed time and entity velocity.
        /// Standard FFXI player run speed (Speed = 50) corresponds to 5.0 yalms/sec (0.1 yalm/s per unit).
        /// </summary>
        public static Vector3 ProjectPosition(WorldEntity entity, TimeSpan elapsed)
        {
            ArgumentNullException.ThrowIfNull(entity);
            if (entity.Speed == 0 || elapsed <= TimeSpan.Zero)
            {
                return entity.Position;
            }

            // Units: Speed * 0.1 yalms/sec
            float speedYalmsPerSec = entity.Speed * 0.1f;
            float distance = speedYalmsPerSec * (float)elapsed.TotalSeconds;

            float heading = entity.HeadingRadians;
            // In FFXI coordinates, 0 is East (+X), 64 is South (+Z), 128 is West (-X), 192 is North (-Z)
            // or cos(heading) along X and sin(heading) along Z (with Y as vertical Elevation)
            float dx = MathF.Cos(heading) * distance;
            float dz = MathF.Sin(heading) * distance;

            return new Vector3(entity.Position.X + dx, entity.Position.Y, entity.Position.Z + dz);
        }

        /// <summary>
        /// Executes a dead-reckoning extrapolation cycle across all active entities in the world,
        /// updating their positions according to their velocity, heading, and the elapsed time interval.
        /// Records cycle duration telemetry to the Performance tracker.
        /// </summary>
        /// <param name="elapsed">The elapsed simulation time since the last update tick.</param>
        /// <param name="updateSpatialGrid">Whether to update spatial hash grid cell positions for moved entities.</param>
        /// <returns>The number of active entities evaluated during the cycle.</returns>
        public int ExtrapolateAll(TimeSpan elapsed, bool updateSpatialGrid = false)
        {
            long startTicks = Stopwatch.GetTimestamp();
            int evaluatedCount = 0;
            try
            {
                WorldEntity[] entities = GetAllEntities();
                evaluatedCount = entities.Length;

                for (int i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];
                    if (entity.Speed > 0 && elapsed > TimeSpan.Zero)
                    {
                        var newPos = ProjectPosition(entity, elapsed);
                        entity.Position = newPos;

                        if (updateSpatialGrid)
                        {
                            _grid.InsertOrUpdate(entity);
                        }
                    }
                }

                return evaluatedCount;
            }
            finally
            {
                long elapsedTicks = Stopwatch.GetTimestamp() - startTicks;
                _performance.DeadReckoning.RecordCycle(evaluatedCount, elapsedTicks);
            }
        }

        /// <summary>
        /// Benchmarks dead-reckoning cycle performance across a specified number of simulation iterations.
        /// </summary>
        public DeadReckoningBenchmarkResult BenchmarkDeadReckoning(int iterations, TimeSpan elapsed, bool updateSpatialGrid = false)
        {
            if (iterations <= 0) throw new ArgumentOutOfRangeException(nameof(iterations), "Iterations must be positive.");

            WorldEntity[] entities = GetAllEntities();
            int entityCount = entities.Length;
            double[] cycleMicroseconds = new double[iterations];

            long totalStartTicks = Stopwatch.GetTimestamp();

            for (int i = 0; i < iterations; i++)
            {
                long cycleStartTicks = Stopwatch.GetTimestamp();

                for (int j = 0; j < entities.Length; j++)
                {
                    var entity = entities[j];
                    if (entity.Speed > 0 && elapsed > TimeSpan.Zero)
                    {
                        var newPos = ProjectPosition(entity, elapsed);
                        entity.Position = newPos;

                        if (updateSpatialGrid)
                        {
                            _grid.InsertOrUpdate(entity);
                            // Execute localized radius query to benchmark spatial lookup pipeline
                            _grid.GetEntitiesInRadius(newPos, 15.0f);
                        }
                    }
                }

                long cycleElapsedTicks = Stopwatch.GetTimestamp() - cycleStartTicks;
                double cycleUs = (double)cycleElapsedTicks * 1_000_000.0 / Stopwatch.Frequency;
                cycleMicroseconds[i] = cycleUs;
                _performance.DeadReckoning.RecordCycle(entityCount, cycleElapsedTicks);
            }

            long totalElapsedTicks = Stopwatch.GetTimestamp() - totalStartTicks;
            double totalElapsedMs = (double)totalElapsedTicks * 1000.0 / Stopwatch.Frequency;

            Array.Sort(cycleMicroseconds);
            double minUs = cycleMicroseconds[0];
            double maxUs = cycleMicroseconds[^1];

            double sumUs = 0;
            for (int i = 0; i < cycleMicroseconds.Length; i++) sumUs += cycleMicroseconds[i];
            double avgUs = sumUs / iterations;

            int p95Idx = Math.Min(iterations - 1, (int)Math.Floor(iterations * 0.95));
            int p99Idx = Math.Min(iterations - 1, (int)Math.Floor(iterations * 0.99));
            double p95Us = cycleMicroseconds[p95Idx];
            double p99Us = cycleMicroseconds[p99Idx];

            double totalSeconds = totalElapsedMs / 1000.0;
            double throughput = totalSeconds > 0 ? (entityCount * (double)iterations) / totalSeconds : 0;

            return new DeadReckoningBenchmarkResult(
                Iterations: iterations,
                EntityCount: entityCount,
                TotalElapsedMilliseconds: totalElapsedMs,
                MinCycleMicroseconds: minUs,
                MaxCycleMicroseconds: maxUs,
                AvgCycleMicroseconds: avgUs,
                P95CycleMicroseconds: p95Us,
                P99CycleMicroseconds: p99Us,
                ThroughputEntitiesPerSecond: throughput
            );
        }

        /// <summary>
        /// Populates this world state with synthetic entities for simulation testing and benchmarks.
        /// </summary>
        public void PopulateSyntheticEntities(int entityCount)
        {
            var rand = new Random(42);

            for (uint i = 1; i <= (uint)entityCount; i++)
            {
                var type = (i % 3) switch
                {
                    0 => EntityType.Player,
                    1 => EntityType.Monster,
                    _ => EntityType.Npc
                };

                var entity = new WorldEntity(i, (ushort)i, type)
                {
                    Name = $"Synthetic_{i}",
                    Position = new Vector3(
                        (float)(rand.NextDouble() * 500.0 - 250.0),
                        0f,
                        (float)(rand.NextDouble() * 500.0 - 250.0)
                    ),
                    Direction = (byte)rand.Next(0, 256),
                    Speed = (byte)(i % 5 == 0 ? 0 : 50) // 80% moving at speed 50
                };
                UpsertEntity(entity);
            }
        }

        /// <summary>
        /// Executes a synthetic load benchmark by populating the world with the specified number of entities
        /// and running multiple dead-reckoning cycle iterations.
        /// </summary>
        public static DeadReckoningBenchmarkResult BenchmarkSyntheticEntities(int entityCount, int iterations, TimeSpan elapsed)
        {
            var testWorld = new WorldState();
            testWorld.PopulateSyntheticEntities(entityCount);
            return testWorld.BenchmarkDeadReckoning(iterations, elapsed, updateSpatialGrid: true);
        }
    }
}
