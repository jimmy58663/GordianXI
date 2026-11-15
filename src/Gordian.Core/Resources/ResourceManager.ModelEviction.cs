// src/Gordian.Core/Resources/ResourceManager.ModelEviction.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Gordian.Core.Diagnostics;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Gordian.Core.World;

namespace Gordian.Core.Resources
{
    /// <summary>How a request for an entity's model came out (<see cref="ResourceManager.RequestEntityModel"/>).</summary>
    public enum EntityModelRequest
    {
        /// <summary>The entity has no model to draw (no model reference, an unregistered id, missing files).</summary>
        None,

        /// <summary>The entity's current model is ready.</summary>
        Ready,

        /// <summary>The entity's model is being built on a worker; the model given, if any, is its previous look.</summary>
        Pending,
    }

    /// <summary>The entity model caches at a glance, for the performance overlay (#573).</summary>
    public readonly record struct EntityModelCacheStats(
        int CharacterModels,
        int MonsterModels,
        int Parts,
        long PartBytes,
        long PartBudgetBytes,
        int MotionSets,
        int PendingBuilds);

    /// <summary>
    /// The model an entity was last given, so a frame finds it again without a lookup (#573): valid while the entity's
    /// model id and look are the ones it was resolved for, under the same cache generation, and the model is still cached.
    /// </summary>
    internal sealed record EntityModelBinding(
        ResourceManager Owner,
        int Generation,
        uint ModelId,
        ResourceManager.CharacterLookKey? Look,
        ResourceManager.CachedEntityModel? Entry);

    public sealed partial class ResourceManager
    {
        /// <summary>
        /// A player or monster model that no entity has used for this long is dropped from the cache by
        /// <see cref="EvictIdleEntityModels"/> (#572); so is a gear part no cached model uses (#573).
        /// </summary>
        public const double EntityModelIdleSeconds = 60.0;

        /// <summary>
        /// The default <see cref="EntityPartBudgetBytes"/>. Provisional: to be set from the part memory measured in a busy
        /// retail city (#573).
        /// </summary>
        public const long DefaultEntityPartBudgetBytes = 512L * 1024 * 1024;

        private long _entityPartBudgetBytes = DefaultEntityPartBudgetBytes;

        /// <summary>
        /// How much memory the shared gear parts may hold (#573, Graphics &amp; Viewport settings) before the sweep frees
        /// parts and models no live entity uses, least recently used first. Parts worn by live entities are never freed,
        /// so a limit set too low costs rebuilds, never missing models. Applies at the next sweep.
        /// </summary>
        public long EntityPartBudgetBytes
        {
            get => Interlocked.Read(ref _entityPartBudgetBytes);
            set => Interlocked.Exchange(ref _entityPartBudgetBytes, Math.Max(0, value));
        }

        /// <summary>
        /// A cached model, built on first use (on the caller's thread, or on a worker by <see cref="StartBuild"/>), and when an
        /// entity last used it (<see cref="Stopwatch"/> timestamp).
        /// </summary>
        internal sealed class CachedEntityModel
        {
            private readonly Lazy<EntityModel?> _model;
            private readonly StrongBox<int>? _pendingBuilds;
            private long _lastUsed = Stopwatch.GetTimestamp();
            private int _started;

            /// <param name="pendingBuilds">Counts this entry while a worker builds it (<see cref="EntityModelCacheStats.PendingBuilds"/>).</param>
            public CachedEntityModel(Func<EntityModel?> build, string label, StrongBox<int>? pendingBuilds = null)
            {
                _pendingBuilds = pendingBuilds;
                _model = new Lazy<EntityModel?>(() =>
                {
                    try
                    {
                        return build();
                    }
                    catch (Exception ex)
                    {
                        GordianLog.Error(LogCategories.Res, $"Building entity model {label} failed: {ex}");
                        return null;
                    }
                }, LazyThreadSafetyMode.ExecutionAndPublication);
            }

            public CachedEntityModel(EntityModel? model) : this(() => model, string.Empty) { _ = _model.Value; }

            /// <summary>Whether the model is built (or found missing).</summary>
            public bool IsReady => _model.IsValueCreated;

            /// <summary>The model, built now on this thread if no one has (waits for a worker already building it).</summary>
            public EntityModel? Model => _model.Value;

            public bool IsReleased { get; set; }

            public long LastUsed => Volatile.Read(ref _lastUsed);
            public void Touch(long now) => Volatile.Write(ref _lastUsed, now);

            /// <summary>Builds the model on a worker unless it is built or being built; one build per entry.</summary>
            public void StartBuild()
            {
                if (_model.IsValueCreated || Interlocked.Exchange(ref _started, 1) != 0) return;
                if (_pendingBuilds != null) Interlocked.Increment(ref _pendingBuilds.Value);
                ThreadPool.UnsafeQueueUserWorkItem(static entry =>
                {
                    _ = entry._model.Value;
                    if (entry._pendingBuilds != null) Interlocked.Decrement(ref entry._pendingBuilds.Value);
                }, this, preferLocal: false);
            }
        }

        /// <summary>
        /// A player or equipped NPC's look: race, face and the model id of each gear slot (head through ranged, the low 12
        /// bits of its grap id, which is all the build reads). Two characters share a model only when they look exactly alike.
        /// </summary>
        internal readonly record struct CharacterLookKey(
            CharacterRace Race, byte Face, ushort Head, ushort Body, ushort Hands, ushort Legs, ushort Feet, ushort Main, ushort Sub, ushort Ranged)
        {
            /// <summary>The look of <paramref name="entity"/>, or null for an entity without a race.</summary>
            public static CharacterLookKey? Of(WorldEntity entity)
            {
                var grap = entity.Appearance.GrapIdTable;
                if (grap == null || grap.Length == 0) return null;
                var race = (CharacterRace)(byte)(grap[0] >> 8);
                if (race == CharacterRace.Unknown) return null;
                return new CharacterLookKey(race, (byte)grap[0], Slot(grap, 1), Slot(grap, 2), Slot(grap, 3), Slot(grap, 4),
                    Slot(grap, 5), Slot(grap, 6), Slot(grap, 7), Slot(grap, 8));
            }

            private static ushort Slot(ushort[] grap, int index) => index < grap.Length ? (ushort)(grap[index] & 0x0FFF) : (ushort)0;

            /// <summary>The grap table the build takes: face in slot 0, then the slots.</summary>
            public ushort[] ToGrapTable() => [(ushort)(((int)Race << 8) | Face), Head, Body, Hands, Legs, Feet, Main, Sub, Ranged];

            public override string ToString() => $"PC_{Race}_{Face}_{Head}-{Body}-{Hands}-{Legs}-{Feet}-{Main}-{Sub}-{Ranged}";
        }

        // Replaced whole by ClearCache, so a build still running on a worker finishes against the old files and is dropped.
        private volatile CharacterModelBuilder? _characterBuilder;
        private readonly ConcurrentDictionary<CharacterLookKey, CachedEntityModel> _characterModels = new();
        private readonly ConcurrentDictionary<uint, CachedEntityModel> _monsterModels = new();
        private readonly StrongBox<int> _pendingBuilds = new();

        private CharacterModelBuilder CharacterBuilder
        {
            get
            {
                var builder = _characterBuilder;
                if (builder != null) return builder;
                Interlocked.CompareExchange(ref _characterBuilder, new CharacterModelBuilder(LoadDatBytes, LoadDatBytesByFileId), null);
                return _characterBuilder!;
            }
        }

        private void ClearEntityModelCaches()
        {
            foreach (var entry in _characterModels.Values) entry.IsReleased = true;
            foreach (var entry in _monsterModels.Values) entry.IsReleased = true;
            _characterModels.Clear();
            _monsterModels.Clear();
            _characterBuilder = null;
        }

        private CachedEntityModel CharacterEntry(CharacterLookKey look)
        {
            if (_characterModels.TryGetValue(look, out var entry)) return entry;
            var builder = CharacterBuilder;
            return _characterModels.GetOrAdd(look, k => new CachedEntityModel(() =>
                builder.Build(k.Race, k.Face, k.ToGrapTable()), k.ToString(), _pendingBuilds));
        }

        private CachedEntityModel MonsterEntry(uint modelId)
        {
            if (_monsterModels.TryGetValue(modelId, out var entry)) return entry;
            return _monsterModels.GetOrAdd(modelId, id => new CachedEntityModel(() =>
            {
                var model = EntityModelLoader.LoadMonsterModel(id, LoadDatBytesByFileId);
                if (model == null)
                {
                    int fileId = CharacterEquipmentResolver.GetMonsterFileId(id);
                    bool resolved = _fileTable.TryResolve(fileId, out string relPath);
                    GordianLog.Debug(LogCategories.Res, $"TryLoadEntityModel: Monster modelId={id}, fileId={fileId}, ftable={_fileTable.Count} entries, resolved={resolved}, relPath='{relPath}'");
                }
                return model;
            }, $"Monster_{id}", _pendingBuilds));
        }

        /// <summary>
        /// The entity's binding when it still names its current model: same owner, cache generation, model id and look,
        /// and the model still cached.
        /// </summary>
        private EntityModelBinding? CurrentBinding(WorldEntity entity, uint modelId, CharacterLookKey? look)
        {
            var binding = entity.ModelBinding;
            if (binding == null || !ReferenceEquals(binding.Owner, this) || binding.Generation != CacheGeneration
                || binding.ModelId != modelId || binding.Look != look || binding.Entry is { IsReleased: true }) return null;
            return binding;
        }

        /// <summary>
        /// Resolves the entity's model entry, building it on this thread when <paramref name="wait"/>, else starting a worker
        /// build. A monster id comes first; an unregistered one (3193-3499) draws nothing, a costumed player included (#83);
        /// a monster whose file is missing falls back to the character look.
        /// </summary>
        private CachedEntityModel? ResolveEntry(uint modelId, CharacterLookKey? look, bool wait, out bool pending)
        {
            pending = false;
            if (modelId > 0)
            {
                if (CharacterEquipmentResolver.GetMonsterFileId(modelId) == CharacterEquipmentResolver.NoModelFileId) return null;
                var monster = MonsterEntry(modelId);
                if (!wait && !monster.IsReady)
                {
                    monster.StartBuild();
                    pending = true;
                    return null;
                }
                if (monster.Model != null) return monster;
            }

            if (look is not { } key) return null;
            var character = CharacterEntry(key);
            if (!wait && !character.IsReady)
            {
                character.StartBuild();
                pending = true;
                return null;
            }
            return character;
        }

        /// <summary>
        /// Attempts to load or stitch an EntityModel for a WorldEntity (Player, NPC, Monster, Trust), building it on this
        /// thread if needed. A player model is built from the shared race base, motion set and gear parts (#573), and the
        /// entity keeps it, so a frame that asks again does no lookup until its model id or look changes.
        /// </summary>
        public bool TryLoadEntityModel(WorldEntity entity, out EntityModel? model)
        {
            model = null;
            if (entity == null) return false;
            uint modelId = entity.Appearance.ModelId;
            var look = CharacterLookKey.Of(entity);
            if (CurrentBinding(entity, modelId, look) is { } binding)
            {
                if (binding.Entry is not { } bound) return false;
                bound.Touch(Stopwatch.GetTimestamp());
                model = bound.Model;
                return model != null;
            }

            int generation = CacheGeneration;
            var entry = ResolveEntry(modelId, look, wait: true, out _);
            entity.ModelBinding = new EntityModelBinding(this, generation, modelId, look, entry);
            entry?.Touch(Stopwatch.GetTimestamp());
            model = entry?.Model;
            return model != null;
        }

        /// <summary>
        /// The entity's model without waiting for a build (#573): a model not built yet is built on a worker, and until it is
        /// ready the entity keeps its previous look (a gear change) or has none (a newly seen entity, drawn a few frames
        /// later). Builds of the same look or monster are shared. The renderer asks this every frame for every entity but
        /// the local player; <see cref="TryLoadEntityModel"/> waits instead.
        /// </summary>
        public EntityModelRequest RequestEntityModel(WorldEntity entity, out EntityModel? model)
        {
            model = null;
            if (entity == null) return EntityModelRequest.None;
            uint modelId = entity.Appearance.ModelId;
            var look = CharacterLookKey.Of(entity);
            if (CurrentBinding(entity, modelId, look) is { } binding)
            {
                if (binding.Entry is not { } bound) return EntityModelRequest.None;
                bound.Touch(Stopwatch.GetTimestamp());
                model = bound.Model;
                return model != null ? EntityModelRequest.Ready : EntityModelRequest.None;
            }

            int generation = CacheGeneration;
            var entry = ResolveEntry(modelId, look, wait: false, out bool pending);
            if (pending)
            {
                // The previous look until the new one is ready, if it is still cached under this generation.
                var previous = entity.ModelBinding;
                if (previous != null && ReferenceEquals(previous.Owner, this) && previous.Generation == generation
                    && previous.Entry is { IsReleased: false, IsReady: true } old)
                {
                    old.Touch(Stopwatch.GetTimestamp());
                    model = old.Model;
                }
                return EntityModelRequest.Pending;
            }

            entity.ModelBinding = new EntityModelBinding(this, generation, modelId, look, entry);
            entry?.Touch(Stopwatch.GetTimestamp());
            model = entry?.Model;
            return model != null ? EntityModelRequest.Ready : EntityModelRequest.None;
        }

        // Mount and chair models: few, keyed by name, never evicted.
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
        public int CachedEntityModelCount => _characterModels.Count + _monsterModels.Count + _entityModelCache.Count;

        /// <summary>The entity model caches at a glance (#573).</summary>
        public EntityModelCacheStats EntityModelStats
        {
            get
            {
                var builder = _characterBuilder;
                return new EntityModelCacheStats(_characterModels.Count, _monsterModels.Count, builder?.PartCount ?? 0,
                    builder?.PartBytes ?? 0, EntityPartBudgetBytes, builder?.MotionSetCount ?? 0, Volatile.Read(ref _pendingBuilds.Value));
            }
        }

        /// <summary>
        /// Drops the player and monster models no entity has used for <paramref name="idleSeconds"/> (#572), then the gear
        /// parts and motion sets no remaining model uses (#573). While the parts hold more than
        /// <see cref="EntityPartBudgetBytes"/>, models no live entity wears go sooner, least recently used first, to free
        /// their parts. A model counts as used when it is drawn and when one of <paramref name="liveEntities"/> wears it, so
        /// an actor off screen or in a character not on screen keeps its model and parts. Mount and chair models are few and
        /// stay. Returns how many models were dropped; the models in use are added to <paramref name="liveModels"/> when given.
        /// </summary>
        public int EvictIdleEntityModels(IEnumerable<WorldEntity> liveEntities, double idleSeconds = EntityModelIdleSeconds,
            ICollection<EntityModel>? liveModels = null)
        {
            long now = Stopwatch.GetTimestamp();
            var live = new HashSet<CachedEntityModel>(ReferenceEqualityComparer.Instance);
            foreach (var entity in liveEntities)
            {
                uint modelId = entity.Appearance.ModelId;
                var look = CharacterLookKey.Of(entity);
                if (CurrentBinding(entity, modelId, look)?.Entry is { } bound) MarkLive(bound);
                if (modelId > 0 && _monsterModels.TryGetValue(modelId, out var monster)) MarkLive(monster);
                if (look is { } key && _characterModels.TryGetValue(key, out var character)) MarkLive(character);
            }

            void MarkLive(CachedEntityModel entry)
            {
                entry.Touch(now);
                if (live.Add(entry) && entry.IsReady && entry.Model is { } model) liveModels?.Add(model);
            }

            long idleTicks = (long)(idleSeconds * Stopwatch.Frequency);
            int evicted = EvictIdle(_characterModels, live, now, idleTicks) + EvictIdle(_monsterModels, live, now, idleTicks);

            var builder = _characterBuilder;
            if (builder == null) return evicted;

            // Over the part budget: drop the models no live entity wears, oldest first, until their parts would fit.
            long budget = EntityPartBudgetBytes;
            long bytes = builder.PartBytes;
            if (bytes > budget)
            {
                var references = new Dictionary<CharacterPart, int>(ReferenceEqualityComparer.Instance);
                var candidates = new List<KeyValuePair<CharacterLookKey, CachedEntityModel>>();
                foreach (var pair in _characterModels)
                {
                    if (!pair.Value.IsReady || pair.Value.Model is not { } model) continue;
                    foreach (var part in model.Parts) references[part] = references.GetValueOrDefault(part) + 1;
                    if (!live.Contains(pair.Value)) candidates.Add(pair);
                }
                candidates.Sort((a, b) => a.Value.LastUsed.CompareTo(b.Value.LastUsed));
                foreach (var pair in candidates)
                {
                    if (bytes <= budget) break;
                    if (!_characterModels.TryRemove(pair)) continue;
                    pair.Value.IsReleased = true;
                    MarkReleased(pair.Value);
                    evicted++;
                    foreach (var part in pair.Value.Model!.Parts)
                    {
                        if (--references[part] == 0) bytes -= part.ApproximateBytes;
                    }
                }
            }

            int freed = builder.EvictParts(CachedCharacterModels(), idleSeconds, budget);
            if (freed > 0)
            {
                GordianLog.Debug(LogCategories.Res,
                    $"Entity model sweep: freed {freed} gear parts; {builder.PartCount} parts ({builder.PartBytes / (1024 * 1024)} MB of {budget / (1024 * 1024)} MB), {builder.MotionSetCount} motion sets.");
            }
            return evicted;
        }

        private IEnumerable<EntityModel> CachedCharacterModels()
        {
            foreach (var entry in _characterModels.Values)
            {
                if (entry.IsReady && entry.Model is { } model) yield return model;
            }
        }

        private static int EvictIdle<TKey>(ConcurrentDictionary<TKey, CachedEntityModel> cache, HashSet<CachedEntityModel> live, long now, long idleTicks)
            where TKey : notnull
        {
            int evicted = 0;
            foreach (var pair in cache)
            {
                // A model still building is left to finish; one no entity asks for again idles out later.
                if (!pair.Value.IsReady || live.Contains(pair.Value) || now - pair.Value.LastUsed <= idleTicks) continue;
                if (!cache.TryRemove(pair)) continue;
                pair.Value.IsReleased = true;
                MarkReleased(pair.Value);
                evicted++;
            }
            return evicted;
        }

        private static void MarkReleased(CachedEntityModel entry)
        {
            if (entry.IsReady && entry.Model is { } model) model.IsReleased = true;
        }
    }
}
