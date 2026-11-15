// src/Gordian.App/Graphics/GpuSharedResources.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using Gordian.Core.Diagnostics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Core.World;
using NeoVeldrid;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// The GPU objects every viewport window on one <see cref="GraphicsDevice"/> shares (#300): the resource layouts the
    /// zone and entity pipelines bind, the zone point-light table, the texture caches, the GPU copies of entity models and
    /// the resident zones (<see cref="ZoneResidencyCache"/>, #322). Per-window state (pipelines, command lists, per-frame
    /// uniform buffers, particle simulation) stays with each window's renderers.
    /// <para>
    /// Everything here is used under <see cref="GpuLock"/>: the render loops of all windows draw one frame at a time under
    /// it, and background zone uploads take it per chunk, so a resource shared by two windows (a generator mesh's uniform
    /// buffer, the light table) is written and drawn by one frame's command list before the next window's frame starts.
    /// </para>
    /// </summary>
    public sealed class GpuSharedResources : IDisposable
    {
        private bool _disposed;

        public GpuSharedResources(GraphicsDevice gd, object? gpuLock = null)
        {
            Device = gd ?? throw new ArgumentNullException(nameof(gd));
            GpuLock = gpuLock ?? new object();
            var factory = gd.ResourceFactory;

            // Set 0: scene uniforms (world, view, projection, lights, fog, eye, weather and sky parameters).
            SceneLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("ZoneSceneUniforms", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment)));

            // Set 1: diffuse texture and sampler.
            TextureLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("uTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("uSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

            // Set 2 (zones): the frame's point-light table and the placement's light slots.
            LightLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("PointLightTable", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
                new ResourceLayoutElementDescription("PointLightRefs", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment)));

            // Set 2 (skinned entities): the joint palette.
            JointPaletteLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("JointPalette", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            LightTableBuffer = factory.CreateBuffer(new BufferDescription(PointLightTableLayout.SizeInBytes, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            NoLightRefsBuffer = CreateLightRefsBuffer(Array.Empty<int>());
            NoLightSet = factory.CreateResourceSet(new ResourceSetDescription(LightLayout, LightTableBuffer, NoLightRefsBuffer));

            ZoneTextures = new GpuTextureCache(gd, TextureLayout);
            EntityTextures = new GpuTextureCache(gd, TextureLayout);
            Zones = new ZoneResidencyCache(this);
        }

        public GraphicsDevice Device { get; }

        private readonly System.Collections.Concurrent.ConcurrentDictionary<ZoneTerrainRenderer, byte> _renderers = new(ReferenceEqualityComparer.Instance);

        /// <summary>The zone renderers (windows) drawing on this device (#576: the zones they read count as in use).</summary>
        internal IReadOnlyCollection<ZoneTerrainRenderer> Renderers => (IReadOnlyCollection<ZoneTerrainRenderer>)_renderers.Keys;

        internal void RegisterRenderer(ZoneTerrainRenderer renderer) => _renderers.TryAdd(renderer, 0);

        internal void UnregisterRenderer(ZoneTerrainRenderer renderer) => _renderers.TryRemove(renderer, out _);

        /// <summary>The lock every use of the device's immediate context and of these shared objects is made under.</summary>
        public object GpuLock { get; }

        public ResourceLayout SceneLayout { get; }
        public ResourceLayout TextureLayout { get; }
        public ResourceLayout LightLayout { get; }
        public ResourceLayout JointPaletteLayout { get; }

        /// <summary>The zone point-light table (set 2, binding 0): rewritten in each frame's command list before its draws.</summary>
        public DeviceBuffer LightTableBuffer { get; }
        internal DeviceBuffer NoLightRefsBuffer { get; }

        /// <summary>Set 2 for a placement no zone light reaches.</summary>
        public ResourceSet NoLightSet { get; }

        /// <summary>Textures of zones, their generators and actor / scene effects, keyed by source (DAT and section).</summary>
        public GpuTextureCache ZoneTextures { get; }

        /// <summary>Textures of entity models, keyed by source.</summary>
        public GpuTextureCache EntityTextures { get; }

        /// <summary>The GPU zones resident on this device (#322).</summary>
        public ZoneResidencyCache Zones { get; }

        /// <summary>
        /// GPU copies of entity models, keyed by the model instance (<see cref="EntityRenderer"/>), shared by every window.
        /// </summary>
        internal ConcurrentDictionary<EntityModel, EntityRenderer.GpuEntityModel> EntityModels { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// GPU copies of entity mesh pieces, keyed by the piece (#573): a gear part's pieces are shared by every character
        /// wearing it, so each is uploaded once; <see cref="EntityModels"/> refer to them. Freed by
        /// <see cref="SweepEntityModels"/> once no GPU model draws them.
        /// </summary>
        internal ConcurrentDictionary<AnimatedMeshGroup, EntityRenderer.GpuSubmesh> EntityMeshes { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>ResourceManager.CacheGeneration <see cref="EntityModels"/> was filled under.</summary>
        internal int EntityModelGeneration { get; set; }

        /// <summary>
        /// A placement's light-slot uniform: four zero-based light-table slots, -1 for none.
        /// </summary>
        public DeviceBuffer CreateLightRefsBuffer(int[] slots)
        {
            var refs = new int[4] { -1, -1, -1, -1 };
            for (int i = 0; i < Math.Min(4, slots.Length); i++) refs[i] = slots[i];
            var buffer = Device.ResourceFactory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
            Device.UpdateBuffer(buffer, 0, refs);
            return buffer;
        }

        /// <summary>How often <see cref="SweepEntityModels"/> runs.</summary>
        internal static readonly TimeSpan EntityModelSweepInterval = TimeSpan.FromSeconds(10);

        private long _lastEntityModelSweep;
        private readonly HashSet<EntityModel> _liveModels = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// Drops entity models no entity has used for <see cref="ResourceManager.EntityModelIdleSeconds"/> (#572), at most every
        /// <see cref="EntityModelSweepInterval"/> whichever window calls it: first from the resource manager's cache, then the
        /// GPU copies, disposing their buffers. A GPU copy counts as used when drawn or when its model is worn by one of the entities
        /// <paramref name="liveEntities"/> lists (every logged-in character's world, so actors off screen or in a character not
        /// on screen keep theirs). Rider and chair copies are not in the resource manager's cache and go when not drawn.
        /// Call under <see cref="GpuLock"/>, from a render loop.
        /// </summary>
        internal void SweepEntityModels(ResourceManager? resources, Func<IEnumerable<WorldEntity>> liveEntities)
        {
            long now = Stopwatch.GetTimestamp();
            if (_lastEntityModelSweep != 0 && Stopwatch.GetElapsedTime(_lastEntityModelSweep, now) < EntityModelSweepInterval) return;
            _lastEntityModelSweep = now;

            _liveModels.Clear();
            int evicted = resources?.EvictIdleEntityModels(liveEntities(), ResourceManager.EntityModelIdleSeconds, _liveModels) ?? 0;

            long idleTicks = (long)(ResourceManager.EntityModelIdleSeconds * Stopwatch.Frequency);
            int released = 0;
            foreach (var pair in EntityModels)
            {
                if (_liveModels.Contains(pair.Key))
                {
                    pair.Value.LastUsed = now;
                    continue;
                }
                // A model the resource manager dropped goes at once (#573: under the part budget it may drop models sooner
                // than the idle time); others (riders, mounts, chairs) when not drawn for the idle time.
                if (!pair.Key.IsReleased && now - pair.Value.LastUsed <= idleTicks) continue;
                if (EntityModels.TryRemove(pair))
                {
                    pair.Value.Dispose();
                    released++;
                }
            }
            _liveModels.Clear();

            // The pieces no remaining GPU model draws.
            int freedPieces = 0;
            if (released > 0)
            {
                var drawn = new HashSet<EntityRenderer.GpuSubmesh>(ReferenceEqualityComparer.Instance);
                foreach (var model in EntityModels.Values)
                {
                    foreach (var submesh in model.Submeshes) drawn.Add(submesh);
                }
                foreach (var pair in EntityMeshes)
                {
                    if (drawn.Contains(pair.Value) || !EntityMeshes.TryRemove(pair)) continue;
                    pair.Value.Dispose();
                    freedPieces++;
                }
            }

            if (evicted > 0 || released > 0)
            {
                GordianLog.Debug(LogCategories.Graphics,
                    $"Entity model sweep: dropped {evicted} cached models, {released} GPU models and {freedPieces} GPU pieces; {resources?.CachedEntityModelCount ?? 0} cached, {EntityModels.Count} GPU models, {EntityMeshes.Count} GPU pieces.");
            }
        }

        /// <summary>Frees the GPU copies of every entity model (a VFS reload rebuilt the models from the new files).</summary>
        internal void ClearEntityModels()
        {
            foreach (var model in EntityModels.Values) model.Dispose();
            EntityModels.Clear();
            foreach (var submesh in EntityMeshes.Values) submesh.Dispose();
            EntityMeshes.Clear();
        }

        /// <summary>
        /// A VFS reload or <c>/reloaddats</c> (#374): frees every entity model and the entity textures, so models and
        /// textures are built again from the new files. The textures are keyed by DAT source, which a reread DAT keeps, so
        /// without this the rebuilt models drew the old pixels. They upload again as each model is next drawn.
        /// </summary>
        internal void ReloadEntityResources()
        {
            ClearEntityModels();
            EntityTextures.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (GpuLock)
            {
                Zones.Dispose();
                ClearEntityModels();
                ZoneTextures.Dispose();
                EntityTextures.Dispose();
                NoLightSet.Dispose();
                NoLightRefsBuffer.Dispose();
                LightTableBuffer.Dispose();
                JointPaletteLayout.Dispose();
                LightLayout.Dispose();
                TextureLayout.Dispose();
                SceneLayout.Dispose();
            }
        }
    }
}
