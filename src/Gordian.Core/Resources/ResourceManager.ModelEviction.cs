// src/Gordian.Core/Resources/ResourceManager.ModelEviction.cs
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Gordian.Core.World;

namespace Gordian.Core.Resources
{
    public sealed partial class ResourceManager
    {
        /// <summary>
        /// A player or monster model that no entity has used for this long is dropped from the cache by
        /// <see cref="EvictIdleEntityModels"/> (#572). Rebuilding one that comes back takes about 40 ms.
        /// </summary>
        public const double EntityModelIdleSeconds = 60.0;

        /// <summary>A cached model and when an entity last used it (<see cref="Stopwatch"/> timestamp).</summary>
        private sealed class CachedEntityModel(EntityModel? model)
        {
            private long _lastUsed = Stopwatch.GetTimestamp();

            public EntityModel? Model { get; } = model;
            public long LastUsed => Volatile.Read(ref _lastUsed);
            public void Touch(long now) => Volatile.Write(ref _lastUsed, now);
        }

        private bool TryGetCachedModel(string key, out EntityModel? model)
        {
            if (_entityModelCache.TryGetValue(key, out var entry))
            {
                entry.Touch(Stopwatch.GetTimestamp());
                model = entry.Model;
                return true;
            }
            model = null;
            return false;
        }

        private void CacheModel(string key, EntityModel? model) => _entityModelCache[key] = new CachedEntityModel(model);

        /// <summary>Models currently cached (player, monster, mount and chair).</summary>
        public int CachedEntityModelCount => _entityModelCache.Count;

        private const string MonsterKeyPrefix = "Monster_";
        private const string CharacterKeyPrefix = "PC_";

        private static string MonsterModelKey(uint modelId) => $"{MonsterKeyPrefix}{modelId}";

        /// <summary>
        /// The cache key of a player or equipped NPC: race, face and every grap id, so two characters share a model only when
        /// they look exactly alike. Null for an entity without a race or grap table.
        /// </summary>
        private static string? CharacterModelKey(WorldEntity entity)
        {
            var grap = entity.Appearance.GrapIdTable;
            var race = (CharacterRace)(byte)((entity.Appearance.FaceModel >> 8) & 0xFF);
            if (race == CharacterRace.Unknown || grap == null || grap.Length == 0) return null;
            ushort face = (ushort)(entity.Appearance.FaceModel & 0xFF);
            return $"{CharacterKeyPrefix}{race}_{face}_{string.Join('-', grap)}";
        }

        /// <summary>
        /// Drops the player and monster models no entity has used for <paramref name="idleSeconds"/> (#572). Every model is
        /// cached per race, face and gear set, so on a busy server each passing player added one, about 12 MB with its
        /// animations, for the life of the session. A model counts as used when it is drawn (each <see cref="TryLoadEntityModel"/>)
        /// and when one of <paramref name="liveEntities"/> wears it, so an actor off screen or in a character not on screen keeps
        /// its model. Mount and chair models are few and stay. Returns how many models were dropped; the models in use are added
        /// to <paramref name="liveModels"/> when given (the GPU cache keeps their copies).
        /// </summary>
        public int EvictIdleEntityModels(IEnumerable<WorldEntity> liveEntities, double idleSeconds = EntityModelIdleSeconds,
            ICollection<EntityModel>? liveModels = null)
        {
            long now = Stopwatch.GetTimestamp();
            foreach (var entity in liveEntities)
            {
                if (entity.Appearance.ModelId > 0) TouchLive(MonsterModelKey(entity.Appearance.ModelId), now, liveModels);
                if (CharacterModelKey(entity) is { } key) TouchLive(key, now, liveModels);
            }

            long idleTicks = (long)(idleSeconds * Stopwatch.Frequency);
            int evicted = 0;
            foreach (var pair in _entityModelCache)
            {
                if (!pair.Key.StartsWith(CharacterKeyPrefix, System.StringComparison.OrdinalIgnoreCase) &&
                    !pair.Key.StartsWith(MonsterKeyPrefix, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (now - pair.Value.LastUsed <= idleTicks) continue;
                if (_entityModelCache.TryRemove(pair)) evicted++;
            }
            return evicted;
        }

        private void TouchLive(string key, long now, ICollection<EntityModel>? liveModels)
        {
            if (!_entityModelCache.TryGetValue(key, out var entry)) return;
            entry.Touch(now);
            if (entry.Model != null) liveModels?.Add(entry.Model);
        }
    }
}
