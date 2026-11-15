// src/Gordian.App/Graphics/ZoneModelCache.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Gordian.Core.Diagnostics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// The zone-derived models drawn apart from their zone, one GPU copy per device shared by every viewport window (#585),
    /// as <see cref="ZoneResidencyCache"/> shares zones: ship models at a dock (a ship zone's vessel), building interiors and
    /// Mog House furniture. A window drawing one holds a reference (<see cref="Acquire"/> / <see cref="TryAcquire"/>, given
    /// back with <see cref="Release"/>). On the app's shared device (<see cref="ReleaseCpuCopies"/>) the CPU copy of an
    /// interior or a furnishing is freed once uploaded; a ship model is read from a copy of its ship zone of its own, dropped
    /// after the upload. A model whose GPU copy went is read again from its DAT. Copies no window holds go with the zone
    /// cache purge (<see cref="Purge"/>): an interior when its zone is purged, a ship model or a furnishing once idle.
    /// </summary>
    public sealed class ZoneModelCache : IDisposable
    {
        /// <summary>What a shared model is.</summary>
        public enum ModelKind
        {
            /// <summary>A ship zone's vessel drawn as a ship at a dock; the id is the ship zone.</summary>
            Vessel,

            /// <summary>A building interior; the id is its sub-area.</summary>
            Interior,

            /// <summary>A Mog House furnishing; the id is its item.</summary>
            Furniture,
        }

        /// <summary>A shared model's key: its kind, id and the ResourceManager cache generation it was read under.</summary>
        public readonly record struct ModelKey(ModelKind Kind, int Id, int Generation);

        /// <summary>A model's GPU copy: its submeshes and the textures they draw with, uploaded with it. Never changed once built.</summary>
        public sealed class SharedModel : IDisposable
        {
            internal SharedModel(ModelKey key, int zoneId, IReadOnlyDictionary<string, DecodedTexture> textures)
            {
                Key = key;
                ZoneId = zoneId;
                Textures = textures;
            }

            public ModelKey Key { get; }

            /// <summary>The zone it belongs to: the ship zone of a vessel, the parent zone of an interior, 0 for furniture.</summary>
            public int ZoneId { get; }

            internal List<ZoneTerrainRenderer.GpuSubmesh> Submeshes { get; } = new();

            /// <summary>
            /// Each submesh's own light-table slots. An interior's submeshes are uploaded without them: each window moves an
            /// interior's lights past its zone's (<c>ZoneTerrainRenderer.GetInterior</c>) and binds its own light set.
            /// </summary>
            internal List<int[]> PointLightSlots { get; } = new();

            /// <summary>The textures the submeshes draw with (a vessel: those its submeshes name; else the model's own).</summary>
            public IReadOnlyDictionary<string, DecodedTexture> Textures { get; }

            /// <summary>GPU memory held: buffers and the textures uploaded with it.</summary>
            public long Bytes { get; internal set; }

            public bool IsDisposed { get; private set; }

            /// <summary>Frees the buffers (call under the device lock); the textures are the texture cache's to evict.</summary>
            public void Dispose()
            {
                if (IsDisposed) return;
                IsDisposed = true;
                foreach (var submesh in Submeshes) submesh.Dispose();
            }
        }

        private sealed class Entry
        {
            public required SharedModel Model;
            public int References;
            public long LastUse;
        }

        private readonly GpuSharedResources _shared;
        private readonly object _gate = new();
        private readonly Dictionary<ModelKey, Entry> _entries = new();
        private int _latestGeneration;
        private bool _disposed;

        internal ZoneModelCache(GpuSharedResources shared) => _shared = shared;

        /// <summary>
        /// Whether an interior's or a furnishing's CPU copy is freed once uploaded (#585, as <see cref="ZoneResidencyCache.ReleaseCpuCopies"/>):
        /// on for the app's shared device, off for a private one (offscreen tests), which may share a ResourceManager with another.
        /// </summary>
        public bool ReleaseCpuCopies { get; set; }

        /// <summary>The number of shared models on the GPU.</summary>
        public int Count
        {
            get { lock (_gate) return _entries.Count; }
        }

        /// <summary>The bytes the shared models hold (buffers and textures).</summary>
        public long ResidentBytes
        {
            get { lock (_gate) return _entries.Values.Sum(e => e.Model.Bytes); }
        }

        /// <summary>How many models were uploaded since the device was made (diagnostics: one per model, however many windows draw it).</summary>
        public int Uploads { get; private set; }

        /// <summary>The keys on the GPU now (tests and diagnostics).</summary>
        public IReadOnlyList<ModelKey> Keys
        {
            get { lock (_gate) return _entries.Keys.ToList(); }
        }

        /// <summary>Whether a model is on the GPU.</summary>
        public bool IsResident(ModelKey key)
        {
            lock (_gate) return _entries.ContainsKey(key);
        }

        /// <summary>The GPU copy of a model with a reference taken, uploading nothing; false when it is not on the GPU.</summary>
        public bool TryAcquire(ModelKey key, out SharedModel? model)
        {
            model = null;
            lock (_gate)
            {
                if (_disposed || !_entries.TryGetValue(key, out var entry)) return false;
                entry.References++;
                entry.LastUse = Stopwatch.GetTimestamp();
                model = entry.Model;
                return true;
            }
        }

        /// <summary>
        /// The GPU copy of a model with a reference taken, uploading it first when it is not on the GPU: its mesh groups and
        /// every texture in <paramref name="textures"/>, under the device lock. Null when it is not on the GPU and its CPU copy
        /// was released (the caller reads it again and retries). With <see cref="ReleaseCpuCopies"/>, <paramref name="releaseCpu"/>
        /// runs once the upload is in. <paramref name="ownLightSlots"/> false uploads without the groups' light slots (interiors).
        /// </summary>
        public SharedModel? Acquire(ModelKey key, int zoneId, IReadOnlyList<MeshGroup> groups, IReadOnlyDictionary<string, DecodedTexture> textures,
            bool ownLightSlots = true, Action? releaseCpu = null)
        {
            ArgumentNullException.ThrowIfNull(groups);
            ArgumentNullException.ThrowIfNull(textures);
            if (TryAcquire(key, out var existing)) return existing;
            if (groups.Any(g => g.IsRenderDataReleased) || textures.Values.Any(t => t != null && t.PixelsReleased)) return null;

            SharedModel built;
            // The whole upload and its entry are made under the device lock, so a zone or model leaving the GPU (which
            // takes the lock before it picks the texture uploads to keep) never evicts a texture this model is uploading.
            lock (_shared.GpuLock)
            {
                if (TryAcquire(key, out existing)) return existing;
                ObjectDisposedException.ThrowIf(_disposed, this);
                built = new SharedModel(key, zoneId, textures);
                try
                {
                    long bytes = 0;
                    foreach (var group in groups)
                    {
                        if (group.Vertices.Length == 0 || group.Indices.Length == 0) continue;
                        var submesh = ZoneTerrainRenderer.UploadSubmesh(_shared, group, pointLightSlots: ownLightSlots ? null : Array.Empty<int>());
                        built.Submeshes.Add(submesh);
                        built.PointLightSlots.Add(group.PointLightSlots);
                        bytes += submesh.VertexBuffer.SizeInBytes + submesh.IndexBuffer.SizeInBytes;
                    }
                    foreach (var texture in textures.Values)
                    {
                        if (texture == null) continue;
                        _shared.ZoneTextures.GetOrCreateResourceSet(texture);
                        bytes += (long)Math.Max(1, texture.Width) * Math.Max(1, texture.Height) * 4;
                    }
                    built.Bytes = bytes;
                }
                catch
                {
                    built.Dispose();
                    throw;
                }

                lock (_gate)
                {
                    if (key.Generation > _latestGeneration) _latestGeneration = key.Generation;
                    _entries[key] = new Entry { Model = built, References = 1, LastUse = Stopwatch.GetTimestamp() };
                    Uploads++;
                }
            }
            GordianLog.Info(LogCategories.Graphics, $"Uploaded {Describe(key)} to the GPU: {built.Submeshes.Count} submeshes, {textures.Count} textures, {built.Bytes >> 10} KB (shared by every window).");
            if (ReleaseCpuCopies) releaseCpu?.Invoke();
            DropStale();
            return built;
        }

        /// <summary>Gives back a reference from <see cref="Acquire"/> or <see cref="TryAcquire"/>.</summary>
        public void Release(SharedModel? model)
        {
            if (model == null) return;
            lock (_gate)
            {
                if (_entries.TryGetValue(model.Key, out var entry) && ReferenceEquals(entry.Model, model))
                {
                    entry.References = Math.Max(0, entry.References - 1);
                    entry.LastUse = Stopwatch.GetTimestamp();
                }
            }
            DropStale();
        }

        /// <summary>
        /// The zones of the interiors a window holds: in use for <c>ZoneCachePurger</c>, replacing the ship zones the windows
        /// read before #585. A held ship model does not keep its ship zone: it was read from a copy of its own.
        /// </summary>
        public IReadOnlyList<int> ZonesInUse
        {
            get
            {
                lock (_gate)
                {
                    return _entries.Values.Where(e => e.References > 0 && e.Model.ZoneId > 0 && e.Model.Key.Kind == ModelKind.Interior)
                        .Select(e => e.Model.ZoneId).Distinct().ToList();
                }
            }
        }

        /// <summary>
        /// Drops the copies no window holds that the zone cache purge (#576) frees: ship models of the dropped zones, the
        /// dropped interiors, and ship models and furnishings no window has drawn for <paramref name="idleSeconds"/>.
        /// Copies a window holds stay. Returns how many were dropped.
        /// </summary>
        public int Purge(IEnumerable<int> droppedZoneIds, IEnumerable<int> droppedInteriorIds, double idleSeconds = ResourceManager.ZoneIdleSeconds)
        {
            ArgumentNullException.ThrowIfNull(droppedZoneIds);
            ArgumentNullException.ThrowIfNull(droppedInteriorIds);
            var zones = new HashSet<int>(droppedZoneIds);
            var interiors = new HashSet<int>(droppedInteriorIds);
            long now = Stopwatch.GetTimestamp();
            long idleTicks = (long)(idleSeconds * Stopwatch.Frequency);
            return Drop(entry =>
            {
                var key = entry.Model.Key;
                bool idle = key.Kind != ModelKind.Interior && now - entry.LastUse >= idleTicks;
                return key.Kind switch
                {
                    ModelKind.Vessel => zones.Contains(key.Id) || idle,
                    ModelKind.Interior => interiors.Contains(key.Id),
                    _ => idle,
                };
            }, "no window draws it");
        }

        /// <summary>Drops copies read before a VFS reload (an older cache generation) once no window holds them.</summary>
        private void DropStale()
        {
            int latest;
            lock (_gate) latest = _latestGeneration;
            Drop(entry => entry.Model.Key.Generation < latest, "read before a VFS reload");
        }

        private int Drop(Func<Entry, bool> select, string reason)
        {
            var dropped = new List<SharedModel>();
            lock (_shared.GpuLock)
            {
                lock (_gate)
                {
                    if (_disposed) return 0;
                    foreach (var (key, entry) in _entries.ToList())
                    {
                        if (entry.References > 0 || !select(entry)) continue;
                        _entries.Remove(key);
                        dropped.Add(entry.Model);
                    }
                }
                if (dropped.Count == 0) return 0;

                // Uploads a kept model or a resident zone draws with stay (uploads are shared by DAT source).
                var keep = new List<DecodedTexture>();
                CollectTextures(keep);
                _shared.Zones.CollectTextures(keep);
                foreach (var model in dropped)
                {
                    int textures = _shared.ZoneTextures.Evict(model.Textures.Values, keep);
                    model.Dispose();
                    GordianLog.Info(LogCategories.Graphics, $"Unloaded {Describe(model.Key)} from the GPU ({model.Bytes >> 10} KB, {textures} textures; {reason}).");
                }
            }
            return dropped.Count;
        }

        /// <summary>Adds the textures the models on the GPU draw with (kept when a zone leaves the GPU).</summary>
        internal void CollectTextures(List<DecodedTexture> textures)
        {
            lock (_gate)
            {
                foreach (var entry in _entries.Values) textures.AddRange(entry.Model.Textures.Values);
            }
        }

        private static string Describe(ModelKey key) => key.Kind switch
        {
            ModelKind.Vessel => $"the ship model of zone {key.Id}",
            ModelKind.Interior => $"sub-area {key.Id}'s interior",
            _ => $"furniture {key.Id}",
        };

        public void Dispose()
        {
            List<SharedModel> all;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                all = _entries.Values.Select(e => e.Model).ToList();
                _entries.Clear();
            }
            lock (_shared.GpuLock)
            {
                foreach (var model in all) model.Dispose();
            }
        }
    }
}
