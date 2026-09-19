// src/Gordian.App/Graphics/ZoneTerrainRenderer.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Gordian.Core.Diagnostics;
using Gordian.Core.Graphics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.World;
using Veldrid;
using Veldrid.SPIRV;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Hardware-accelerated 3D zone terrain and world geometry renderer.
    /// Manages GPU vertex/index buffer streaming, frustum culling, directional sun/moon lighting,
    /// and authentic FFXI distance fog.
    /// Clean-room implementation referencing FFXI rendering pipeline conventions.
    /// </summary>
    public sealed class ZoneTerrainRenderer : IDisposable
    {
        private readonly GraphicsDevice _gd;
        private DeviceBuffer _sceneUniformBuffer = null!;
        private ResourceLayout _sceneLayout = null!;
        private ResourceLayout _textureLayout = null!;
        private ResourceSet _sceneResourceSet = null!;
        private Pipeline _pipeline = null!;
        private CommandList _commandList = null!;
        private GpuTextureCache _textureCache = null!;
        private EntityRenderer? _entityRenderer;
        public EntityRenderer? EntityRenderer => _entityRenderer;

        private readonly List<GpuSubmesh> _zoneSubmeshes = new();
        private readonly List<GpuSubmesh> _fallbackSubmeshes = new();
        private GpuSubmesh? _groundPlaneSubmesh;
        private IReadOnlyDictionary<string, DecodedTexture>? _activeDecodedTextures;

        private bool _disposed;

        // Telemetry counters
        public int DrawCalls { get; private set; }
        public int CulledMeshes { get; private set; }
        public int VisibleMeshes { get; private set; }
        public int TotalVertices { get; private set; }
        public int LoadedZoneSubmeshCount => _zoneSubmeshes.Count;
        public Vector3 FirstSubmeshMinBounds => _zoneSubmeshes.Count > 0 ? _zoneSubmeshes[0].MinBounds : Vector3.Zero;
        public Vector3 FirstSubmeshMaxBounds => _zoneSubmeshes.Count > 0 ? _zoneSubmeshes[0].MaxBounds : Vector3.Zero;

        private sealed class GpuSubmesh : IDisposable
        {
            public string TextureName { get; init; } = string.Empty;
            public DeviceBuffer VertexBuffer { get; init; } = null!;
            public DeviceBuffer IndexBuffer { get; init; } = null!;
            public uint IndexCount { get; init; }
            public Vector3 MinBounds { get; init; }
            public Vector3 MaxBounds { get; init; }

            public void Dispose()
            {
                VertexBuffer?.Dispose();
                IndexBuffer?.Dispose();
            }
        }

        public ZoneTerrainRenderer(GraphicsDevice gd)
        {
            _gd = gd ?? throw new ArgumentNullException(nameof(gd));
            InitializePipeline();
            _entityRenderer = new EntityRenderer(_gd);
            BuildFallbackScene();
        }

        private void InitializePipeline()
        {
            var factory = _gd.ResourceFactory;

            // 1. Scene Uniform Buffer (std140: 288 bytes)
            _sceneUniformBuffer = factory.CreateBuffer(new BufferDescription(
                288,
                BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            // 2. Resource Layouts
            // Set 0: Scene Uniforms (World, View, Proj, Sun, Ambient, Fog, Eye)
            _sceneLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("ZoneSceneUniforms", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment)));

            // Set 1: Diffuse Texture + Bilinear Sampler
            _textureLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("uTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("uSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

            _sceneResourceSet = factory.CreateResourceSet(new ResourceSetDescription(_sceneLayout, _sceneUniformBuffer));
            _textureCache = new GpuTextureCache(_gd, _textureLayout);

            // 3. Shaders (SPIR-V cross-compilation)
            var vsDesc = new ShaderDescription(
                ShaderStages.Vertex,
                Encoding.UTF8.GetBytes(ZoneShaders.VertexShaderGlsl),
                "main");
            var fsDesc = new ShaderDescription(
                ShaderStages.Fragment,
                Encoding.UTF8.GetBytes(ZoneShaders.FragmentShaderGlsl),
                "main");

            Shader[] shaders = factory.CreateFromSpirv(vsDesc, fsDesc);

            // 4. Vertex Layout (36-byte MeshVertex stride: Pos(12) + Norm(12) + UV(8) + Color(4))
            var vertexLayout = new VertexLayoutDescription(
                new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Byte4_Norm));

            // 5. Graphics Pipeline
            var pipelineDesc = new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleOverrideBlend,
                DepthStencilState = new DepthStencilStateDescription(
                    depthTestEnabled: true,
                    depthWriteEnabled: true,
                    comparisonKind: ComparisonKind.LessEqual),
                RasterizerState = new RasterizerStateDescription(
                    cullMode: FaceCullMode.None, // FFXI double-sided foliage & terrain
                    fillMode: PolygonFillMode.Solid,
                    frontFace: FrontFace.Clockwise,
                    depthClipEnabled: true,
                    scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _sceneLayout, _textureLayout },
                ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, shaders),
                Outputs = _gd.SwapchainFramebuffer.OutputDescription
            };

            _pipeline = factory.CreateGraphicsPipeline(pipelineDesc);
            _commandList = factory.CreateCommandList();
        }

        /// <summary>
        /// Loads a ZoneGeometry model and its decoded textures into GPU buffers.
        /// </summary>
        public void LoadZone(ZoneGeometry? zone, IReadOnlyDictionary<string, DecodedTexture>? textures = null)
        {
            ClearZoneSubmeshes();
            _activeDecodedTextures = textures;

            if (zone == null || zone.MeshGroups.Count == 0)
            {
                return;
            }

            var factory = _gd.ResourceFactory;
            int vertCount = 0;

            for (int i = 0; i < zone.MeshGroups.Count; i++)
            {
                var group = zone.MeshGroups[i];
                if (group.Vertices.Length == 0 || group.Indices.Length == 0) continue;

                var vb = factory.CreateBuffer(new BufferDescription(
                    (uint)(group.Vertices.Length * 36),
                    BufferUsage.VertexBuffer));
                _gd.UpdateBuffer(vb, 0, group.Vertices);

                // Convert int[] indices to ushort[]
                var ushortIndices = new ushort[group.Indices.Length];
                for (int idx = 0; idx < group.Indices.Length; idx++)
                {
                    ushortIndices[idx] = (ushort)group.Indices[idx];
                }

                var ib = factory.CreateBuffer(new BufferDescription(
                    (uint)(ushortIndices.Length * sizeof(ushort)),
                    BufferUsage.IndexBuffer));
                _gd.UpdateBuffer(ib, 0, ushortIndices);

                _zoneSubmeshes.Add(new GpuSubmesh
                {
                    TextureName = group.TextureName,
                    VertexBuffer = vb,
                    IndexBuffer = ib,
                    IndexCount = (uint)ushortIndices.Length,
                    MinBounds = group.MinBounds,
                    MaxBounds = group.MaxBounds
                });

                vertCount += group.Vertices.Length;
            }

            TotalVertices = vertCount;
            GordianLog.Info("Graphics", $"Streamed {zone.MeshGroups.Count} zone submeshes ({TotalVertices} vertices) to GPU.");
        }

        /// <summary>
        /// Renders one frame of the 3D zone terrain with lighting and fog.
        /// </summary>
        public void Render(
            ViewportCamera camera,
            ZoneEnvironmentSettings environment,
            float deltaSeconds,
            uint width,
            uint height,
            IEnumerable<WorldEntity>? entities = null,
            ResourceManager? resourceManager = null)
        {
            if (_disposed || _gd == null || _gd.MainSwapchain == null) return;

            // 1. Update Uniform Buffer
            float aspect = Math.Max(0.1f, (float)width / Math.Max(1, height));
            camera.AspectRatio = aspect;

            float fogRange = Math.Max(0.001f, environment.FogEnd - environment.FogStart);
            var sceneUniform = new ZoneSceneUniform
            {
                World = Matrix4x4.Identity,
                View = camera.ViewMatrix,
                Projection = camera.ProjectionMatrix,
                SunDirection = new Vector4(environment.SunDirection, 0.0f),
                SunColor = new Vector4(environment.SunColor, 1.0f),
                AmbientColor = new Vector4(environment.AmbientColor, 1.0f),
                FogColor = environment.FogColor,
                FogParams = new Vector4(environment.FogStart, environment.FogEnd, 1.0f / fogRange, environment.FogDensity),
                EyePosition = new Vector4(camera.Position, 1.0f)
            };

            _gd.UpdateBuffer(_sceneUniformBuffer, 0, ref sceneUniform);

            // 2. Select submesh list (loaded zone or fallback scene)
            var activeSubmeshes = _zoneSubmeshes.Count > 0 ? _zoneSubmeshes : _fallbackSubmeshes;

            // 3. Record Render Commands
            _commandList.Begin();
            _commandList.SetFramebuffer(_gd.SwapchainFramebuffer);

            // Clear to atmospheric fog color for authentic FFXI horizon blending
            _commandList.ClearColorTarget(0, new RgbaFloat(
                environment.FogColor.X,
                environment.FogColor.Y,
                environment.FogColor.Z,
                1.0f));
            _commandList.ClearDepthStencil(1.0f);

            _commandList.SetPipeline(_pipeline);
            _commandList.SetGraphicsResourceSet(0, _sceneResourceSet);

            int draws = 0;
            int culled = 0;
            int visible = 0;

            var frustum = camera.Frustum;

            for (int i = 0; i < activeSubmeshes.Count; i++)
            {
                var submesh = activeSubmeshes[i];

                // Frustum Culling
                if (!frustum.IntersectsBox(submesh.MinBounds, submesh.MaxBounds))
                {
                    culled++;
                    continue;
                }

                visible++;

                // Bind Texture Resource Set
                var texSet = _textureCache.GetOrCreateResourceSet(submesh.TextureName, _activeDecodedTextures);
                _commandList.SetGraphicsResourceSet(1, texSet);

                _commandList.SetVertexBuffer(0, submesh.VertexBuffer);
                _commandList.SetIndexBuffer(submesh.IndexBuffer, IndexFormat.UInt16);
                _commandList.DrawIndexed(submesh.IndexCount, 1, 0, 0, 0);
                draws++;
            }

            // If no terrain geometry was drawn (e.g. unplaced zone submeshes or out-of-bounds),
            // render the adaptive ground plane centered under the player so character stands on solid ground.
            if ((_zoneSubmeshes.Count == 0 || draws == 0) && _groundPlaneSubmesh != null)
            {
                float groundY = camera.Target.Y - 1.3f;
                var groundWorld = Matrix4x4.CreateTranslation(new Vector3(camera.Target.X, groundY, camera.Target.Z));
                var groundUniform = sceneUniform;
                groundUniform.World = groundWorld;
                _gd.UpdateBuffer(_sceneUniformBuffer, 0, ref groundUniform);

                var texSet = _textureCache.GetOrCreateResourceSet(string.Empty, _activeDecodedTextures);
                _commandList.SetGraphicsResourceSet(1, texSet);
                _commandList.SetVertexBuffer(0, _groundPlaneSubmesh.VertexBuffer);
                _commandList.SetIndexBuffer(_groundPlaneSubmesh.IndexBuffer, IndexFormat.UInt16);
                _commandList.DrawIndexed(_groundPlaneSubmesh.IndexCount, 1, 0, 0, 0);
                draws++;
                visible++;
            }

            // Render live 3D entity models & modular equipment
            if (_entityRenderer != null && entities != null)
            {
                _entityRenderer.RenderEntities(_commandList, camera, environment, entities, resourceManager);
                draws += _entityRenderer.DrawCalls;
                visible += _entityRenderer.VisibleEntities;
                culled += _entityRenderer.CulledEntities;
            }

            _commandList.End();

            // 4. Submit & Present
            _gd.SubmitCommands(_commandList);
            _gd.SwapBuffers();

            DrawCalls = draws;
            CulledMeshes = culled;
            VisibleMeshes = visible;
        }

        private void BuildFallbackScene()
        {
            var factory = _gd.ResourceFactory;

            // Adaptive ground plane (2000x2000 yalms, tiled UVs, neutral stone/ground tint)
            // Sized to match camera FarClip (1000 yalms) so ground seamlessly meets distance fog
            var planeVerts = new MeshVertex[]
            {
                new(new Vector3(-1000, 0, -1000), Vector3.UnitY, new Vector2(0, 0), 0xFFB0B0B0),
                new(new Vector3( 1000, 0, -1000), Vector3.UnitY, new Vector2(100, 0), 0xFFB0B0B0),
                new(new Vector3( 1000, 0,  1000), Vector3.UnitY, new Vector2(100, 100), 0xFFB0B0B0),
                new(new Vector3(-1000, 0,  1000), Vector3.UnitY, new Vector2(0, 100), 0xFFB0B0B0),
            };

            ushort[] planeIndices = { 0, 1, 2, 0, 2, 3 };

            var planeVb = factory.CreateBuffer(new BufferDescription((uint)(planeVerts.Length * 36), BufferUsage.VertexBuffer));
            _gd.UpdateBuffer(planeVb, 0, planeVerts);

            var planeIb = factory.CreateBuffer(new BufferDescription((uint)(planeIndices.Length * sizeof(ushort)), BufferUsage.IndexBuffer));
            _gd.UpdateBuffer(planeIb, 0, planeIndices);

            _groundPlaneSubmesh = new GpuSubmesh
            {
                TextureName = string.Empty,
                VertexBuffer = planeVb,
                IndexBuffer = planeIb,
                IndexCount = (uint)planeIndices.Length,
                MinBounds = new Vector3(-1000, -500f, -1000),
                MaxBounds = new Vector3(1000, 500f, 1000)
            };
            // Note: _groundPlaneSubmesh is not added to static _fallbackSubmeshes so that
            // it dynamically renders at the player's elevation (groundY) rather than fixed at Y=0.
        }

        private void ClearZoneSubmeshes()
        {
            for (int i = 0; i < _zoneSubmeshes.Count; i++)
            {
                _zoneSubmeshes[i].Dispose();
            }
            _zoneSubmeshes.Clear();
            _textureCache?.Clear();
            TotalVertices = 0;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            ClearZoneSubmeshes();

            for (int i = 0; i < _fallbackSubmeshes.Count; i++)
            {
                _fallbackSubmeshes[i].Dispose();
            }
            _fallbackSubmeshes.Clear();
            _groundPlaneSubmesh = null;

            _entityRenderer?.Dispose();
            _textureCache?.Dispose();
            _commandList?.Dispose();
            _pipeline?.Dispose();
            _sceneResourceSet?.Dispose();
            _sceneLayout?.Dispose();
            _textureLayout?.Dispose();
            _sceneUniformBuffer?.Dispose();
        }
    }
}
