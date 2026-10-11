// src/Gordian.App/Graphics/GpuSharedResources.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Gordian.Core.Resources.Models;
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
                new ResourceLayoutElementDescription("PointLightTable", ResourceKind.UniformBuffer, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("PointLightRefs", ResourceKind.UniformBuffer, ShaderStages.Fragment)));

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

        /// <summary>Frees the GPU copies of every entity model (a VFS reload rebuilt the models from the new files).</summary>
        internal void ClearEntityModels()
        {
            foreach (var model in EntityModels.Values) model.Dispose();
            EntityModels.Clear();
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
