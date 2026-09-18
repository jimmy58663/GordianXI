// src/Gordian.App/Graphics/ZoneTerrainRenderer.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Gordian.Core.Diagnostics;
using Gordian.Core.Graphics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
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

        private readonly List<GpuSubmesh> _zoneSubmeshes = new();
        private readonly List<GpuSubmesh> _fallbackSubmeshes = new();
        private IReadOnlyDictionary<string, DecodedTexture>? _activeDecodedTextures;

        private bool _disposed;

        // Telemetry counters
        public int DrawCalls { get; private set; }
        public int CulledMeshes { get; private set; }
        public int VisibleMeshes { get; private set; }
        public int TotalVertices { get; private set; }

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
            uint height)
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

            // Ground plane (80x80 yalms, tiled UVs, vertex colored)
            var planeVerts = new MeshVertex[]
            {
                new(new Vector3(-40, 0, -40), Vector3.UnitY, new Vector2(0, 0), 0xFFB0B0B0),
                new(new Vector3( 40, 0, -40), Vector3.UnitY, new Vector2(8, 0), 0xFFB0B0B0),
                new(new Vector3( 40, 0,  40), Vector3.UnitY, new Vector2(8, 8), 0xFFB0B0B0),
                new(new Vector3(-40, 0,  40), Vector3.UnitY, new Vector2(0, 8), 0xFFB0B0B0),
            };

            ushort[] planeIndices = { 0, 1, 2, 0, 2, 3 };

            var planeVb = factory.CreateBuffer(new BufferDescription((uint)(planeVerts.Length * 36), BufferUsage.VertexBuffer));
            _gd.UpdateBuffer(planeVb, 0, planeVerts);

            var planeIb = factory.CreateBuffer(new BufferDescription((uint)(planeIndices.Length * sizeof(ushort)), BufferUsage.IndexBuffer));
            _gd.UpdateBuffer(planeIb, 0, planeIndices);

            _fallbackSubmeshes.Add(new GpuSubmesh
            {
                TextureName = string.Empty,
                VertexBuffer = planeVb,
                IndexBuffer = planeIb,
                IndexCount = (uint)planeIndices.Length,
                MinBounds = new Vector3(-40, -0.1f, -40),
                MaxBounds = new Vector3(40, 0.1f, 40)
            });

            // Central landmark crystal pyramid
            var pyramidVerts = new MeshVertex[]
            {
                // Apex
                new(new Vector3( 0, 3.5f, 0), Vector3.UnitY, new Vector2(0.5f, 1.0f), 0xFFFFFFFF),
                // Base
                new(new Vector3(-1.5f, 0, -1.5f), new Vector3(-1, 0.5f, -1), new Vector2(0, 0), 0xFF60A0E0),
                new(new Vector3( 1.5f, 0, -1.5f), new Vector3( 1, 0.5f, -1), new Vector2(1, 0), 0xFF60A0E0),
                new(new Vector3( 1.5f, 0,  1.5f), new Vector3( 1, 0.5f,  1), new Vector2(1, 1), 0xFF60A0E0),
                new(new Vector3(-1.5f, 0,  1.5f), new Vector3(-1, 0.5f,  1), new Vector2(0, 1), 0xFF60A0E0),
            };

            ushort[] pyramidIndices =
            {
                0, 1, 2, // North face
                0, 2, 3, // East face
                0, 3, 4, // South face
                0, 4, 1  // West face
            };

            var pyrVb = factory.CreateBuffer(new BufferDescription((uint)(pyramidVerts.Length * 36), BufferUsage.VertexBuffer));
            _gd.UpdateBuffer(pyrVb, 0, pyramidVerts);

            var pyrIb = factory.CreateBuffer(new BufferDescription((uint)(pyramidIndices.Length * sizeof(ushort)), BufferUsage.IndexBuffer));
            _gd.UpdateBuffer(pyrIb, 0, pyramidIndices);

            _fallbackSubmeshes.Add(new GpuSubmesh
            {
                TextureName = string.Empty,
                VertexBuffer = pyrVb,
                IndexBuffer = pyrIb,
                IndexCount = (uint)pyramidIndices.Length,
                MinBounds = new Vector3(-1.5f, 0, -1.5f),
                MaxBounds = new Vector3(1.5f, 3.5f, 1.5f)
            });
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
