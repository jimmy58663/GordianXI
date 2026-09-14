// src/Gordian.Core/World/WorldState.cs
using System;
using System.Collections.Generic;
using System.Numerics;

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
        private readonly SpatialPartitionGrid _grid = new SpatialPartitionGrid();

        public SpatialPartitionGrid Grid => _grid;
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
            // or cos(heading) along X and sin(heading) along Z
            float dx = MathF.Cos(heading) * distance;
            float dz = MathF.Sin(heading) * distance;

            return new Vector3(entity.Position.X + dx, entity.Position.Y, entity.Position.Z + dz);
        }
    }
}
