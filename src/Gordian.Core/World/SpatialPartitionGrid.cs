// src/Gordian.Core/World/SpatialPartitionGrid.cs
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gordian.Core.World
{
    /// <summary>
    /// High-performance 3D uniform spatial partitioning hash grid.
    /// Provides fast, thread-safe O(1) spatial binning and localized queries
    /// (radius, nearest entity, and directional field-of-view cone searches).
    /// </summary>
    public sealed class SpatialPartitionGrid
    {
        public const float DefaultCellSize = 10.0f;

        private readonly float _cellSize;
        private readonly float _inverseCellSize;
        private readonly object _lock = new object();

        private readonly Dictionary<(int X, int Y, int Z), HashSet<WorldEntity>> _grid =
            new Dictionary<(int X, int Y, int Z), HashSet<WorldEntity>>();

        private readonly Dictionary<uint, (int X, int Y, int Z)> _entityCellMap =
            new Dictionary<uint, (int X, int Y, int Z)>();

        public SpatialPartitionGrid(float cellSize = DefaultCellSize)
        {
            if (cellSize <= 0f) throw new ArgumentOutOfRangeException(nameof(cellSize), "Cell size must be positive.");
            _cellSize = cellSize;
            _inverseCellSize = 1.0f / cellSize;
        }

        private (int X, int Y, int Z) GetCellCoord(Vector3 pos)
        {
            return (
                (int)MathF.Floor(pos.X * _inverseCellSize),
                (int)MathF.Floor(pos.Y * _inverseCellSize),
                (int)MathF.Floor(pos.Z * _inverseCellSize)
            );
        }

        /// <summary>
        /// Inserts or updates an entity's cell position in the grid.
        /// </summary>
        public void InsertOrUpdate(WorldEntity entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            var newCell = GetCellCoord(entity.Position);

            lock (_lock)
            {
                if (_entityCellMap.TryGetValue(entity.ServerId, out var oldCell))
                {
                    if (oldCell == newCell) return; // Still in the same cell

                    if (_grid.TryGetValue(oldCell, out var oldBucket))
                    {
                        oldBucket.Remove(entity);
                        if (oldBucket.Count == 0)
                        {
                            _grid.Remove(oldCell);
                        }
                    }
                }

                if (!_grid.TryGetValue(newCell, out var newBucket))
                {
                    newBucket = new HashSet<WorldEntity>();
                    _grid[newCell] = newBucket;
                }

                newBucket.Add(entity);
                _entityCellMap[entity.ServerId] = newCell;
            }
        }

        /// <summary>
        /// Removes an entity from the spatial grid.
        /// </summary>
        public bool Remove(WorldEntity entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            lock (_lock)
            {
                if (_entityCellMap.Remove(entity.ServerId, out var cell))
                {
                    if (_grid.TryGetValue(cell, out var bucket))
                    {
                        bucket.Remove(entity);
                        if (bucket.Count == 0)
                        {
                            _grid.Remove(cell);
                        }
                    }
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Clears all spatial partitions.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _grid.Clear();
                _entityCellMap.Clear();
            }
        }

        /// <summary>
        /// Retrieves all entities within a 3D spherical radius from the given center.
        /// </summary>
        public List<WorldEntity> GetEntitiesInRadius(Vector3 center, float radius)
        {
            var results = new List<WorldEntity>();
            float radiusSq = radius * radius;

            int minX = (int)MathF.Floor((center.X - radius) * _inverseCellSize);
            int maxX = (int)MathF.Floor((center.X + radius) * _inverseCellSize);
            int minY = (int)MathF.Floor((center.Y - radius) * _inverseCellSize);
            int maxY = (int)MathF.Floor((center.Y + radius) * _inverseCellSize);
            int minZ = (int)MathF.Floor((center.Z - radius) * _inverseCellSize);
            int maxZ = (int)MathF.Floor((center.Z + radius) * _inverseCellSize);

            lock (_lock)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        for (int z = minZ; z <= maxZ; z++)
                        {
                            if (_grid.TryGetValue((x, y, z), out var bucket))
                            {
                                foreach (var entity in bucket)
                                {
                                    if (Vector3.DistanceSquared(center, entity.Position) <= radiusSq)
                                    {
                                        results.Add(entity);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Retrieves the closest entity to the center point, optionally filtered by EntityType.
        /// </summary>
        public WorldEntity? GetNearestEntity(Vector3 center, EntityType? filter = null, float maxSearchRadius = 100.0f)
        {
            var candidates = GetEntitiesInRadius(center, maxSearchRadius);
            WorldEntity? nearest = null;
            float nearestDistSq = float.MaxValue;

            foreach (var entity in candidates)
            {
                if (filter.HasValue && entity.Type != filter.Value) continue;

                float distSq = Vector3.DistanceSquared(center, entity.Position);
                if (distSq < nearestDistSq)
                {
                    nearestDistSq = distSq;
                    nearest = entity;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Retrieves entities within a directional 3D vision cone.
        /// </summary>
        public List<WorldEntity> GetEntitiesInCone(Vector3 origin, Vector3 forward, float maxAngleDegrees, float maxDistance)
        {
            var results = new List<WorldEntity>();
            float forwardLen = forward.Length();
            if (forwardLen <= 0.0001f) return results;

            Vector3 normalizedForward = forward / forwardLen;
            float minDot = MathF.Cos((maxAngleDegrees * MathF.PI) / 180.0f);

            var candidates = GetEntitiesInRadius(origin, maxDistance);
            foreach (var entity in candidates)
            {
                Vector3 toEntity = entity.Position - origin;
                float dist = toEntity.Length();
                if (dist <= 0.0001f)
                {
                    results.Add(entity);
                    continue;
                }

                Vector3 dir = toEntity / dist;
                float dot = Vector3.Dot(normalizedForward, dir);
                if (dot >= minDot)
                {
                    results.Add(entity);
                }
            }

            return results;
        }
    }
}
