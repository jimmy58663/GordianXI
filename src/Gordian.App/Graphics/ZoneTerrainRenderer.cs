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
        private DeviceBuffer _waterUniformBuffer = null!;
        private ResourceLayout _sceneLayout = null!;
        private ResourceLayout _textureLayout = null!;
        private ResourceSet _sceneResourceSet = null!;
        private ResourceSet _waterResourceSet = null!;
        private Pipeline _pipeline = null!;
        private Pipeline _terrainBlendPipeline = null!;
        private Pipeline _cutoutPipeline = null!;
        private Pipeline _blendPipeline = null!;
        private Pipeline _waterPipeline = null!;
        private Pipeline _weatherSkyPipeline = null!;
        private Pipeline _weatherSkyAdditivePipeline = null!;
        private CommandList _commandList = null!;
        private GpuTextureCache _textureCache = null!;
        private EntityRenderer? _entityRenderer;
        public EntityRenderer? EntityRenderer => _entityRenderer;
        private SkyDomeRenderer? _skyDomeRenderer;
        public SkyDomeRenderer? SkyDomeRenderer => _skyDomeRenderer;
        public ZoneGeometry? LoadedZone { get; private set; }

        private readonly List<GpuSubmesh> _zoneSubmeshes = new();
        private readonly List<GpuWeatherSkySubmesh> _weatherSkySubmeshes = new();
        private readonly List<GpuSubmesh> _fallbackSubmeshes = new();
        private GpuSubmesh? _groundPlaneSubmesh;
        private GpuSubmesh? _oceanWaterSubmesh;
        private IReadOnlyDictionary<string, DecodedTexture>? _activeDecodedTextures;
        private float _cloudAccumulatedTime;
        private Vector2 _waterScrollVelocity = new(0.012f, -0.016f);

        private bool _disposed;

        /// <summary>
        /// Controls whether the base sea-level ocean water plane is rendered in outdoor zones with sea-level elevation.
        /// </summary>
        public bool EnableOceanWaterPlane { get; set; } = true;

        /// <summary>
        /// Indicates whether the ocean water plane GPU geometry is currently allocated.
        /// </summary>
        public bool HasOceanWaterPlane => _oceanWaterSubmesh != null;

        /// <summary>
        /// Controls whether Section 0x05 dynamic weather cloud layers (e.g. cld_fine, suny, clod)
        /// are rendered drifting across the sky dome in Pass 0b. Defaults to false to isolate base dome.
        /// </summary>
        public bool EnableWeatherClouds { get; set; } = false;

        /// <summary>
        /// Controls whether raw Section 0x05 celestial particle generator shells (sunsphere, star, moonsphere)
        /// and celestial discs are rendered in Pass 0b. Enabled for Chunk 3 (Celestial Night Sky).
        /// </summary>
        public bool EnableWeatherCelestialBodies { get; set; } = true;

        /// <summary>
        /// Controls whether the lunar disc and halo billboards are rendered at night.
        /// Enabled for Chunk 3 (Celestial Night Sky).
        /// </summary>
        public bool EnableCelestialMoon { get; set; } = true;

        /// <summary>
        /// Controls whether the stardust shell (faint background starfield, textured with star_rivstar02) is rendered.
        /// </summary>
        public bool EnableMilkyWay { get; set; } = true;

        /// <summary>
        /// Controls whether the solar disc and radiance flare are rendered during daytime.
        /// Defaults to false until Chunk 5 (Solar & Horizon Alignment).
        /// </summary>
        public bool EnableCelestialSun { get; set; } = false;

        /// <summary>
        /// Controls whether raw celestial disc billboards (sun/moon) are rendered.
        /// Kept for backward compatibility.
        /// </summary>
        public bool EnableCelestialDiscs { get; set; } = false;

        /// <summary>
        /// Indicates whether the ocean water plane was rendered during the most recent frame.
        /// </summary>
        public bool IsOceanWaterPlaneActive { get; private set; }

        // Telemetry counters
        public int DrawCalls { get; private set; }
        public int CulledMeshes { get; private set; }
        public int VisibleMeshes { get; private set; }
        public int TotalVertices { get; private set; }
        public int LoadedZoneSubmeshCount => _zoneSubmeshes.Count;
        public int WeatherSkySubmeshCount => _weatherSkySubmeshes.Count;
        public Vector3 FirstSubmeshMinBounds => _zoneSubmeshes.Count > 0 ? _zoneSubmeshes[0].MinBounds : Vector3.Zero;
        public Vector3 FirstSubmeshMaxBounds => _zoneSubmeshes.Count > 0 ? _zoneSubmeshes[0].MaxBounds : Vector3.Zero;

        private sealed class GpuSubmesh : IDisposable
        {
            public string Name { get; init; } = string.Empty;
            public string TextureName { get; init; } = string.Empty;
            public DeviceBuffer VertexBuffer { get; init; } = null!;
            public DeviceBuffer IndexBuffer { get; init; } = null!;
            public uint IndexCount { get; init; }
            public Vector3 MinBounds { get; init; }
            public Vector3 MaxBounds { get; init; }
            public bool IsBlend { get; init; }
            public bool NoCull { get; init; }
            public bool IsFoliage { get; init; }
            public bool IsWater { get; init; }
            public Vector2 UVScroll { get; init; }

            public void Dispose()
            {
                VertexBuffer?.Dispose();
                IndexBuffer?.Dispose();
            }
        }

        private sealed class GpuWeatherSkySubmesh : IDisposable
        {
            public string Name { get; init; } = string.Empty;
            public string LayerName { get; init; } = string.Empty;
            public string? WeatherId { get; init; }
            public bool IsCelestial { get; init; }
            public ParticleAttachType AttachType { get; init; }
            public Vector2 UVScroll { get; init; }
            public Vector3 BasePosition { get; init; }
            public Vector3 Scale { get; init; } = Vector3.One;
            public bool FollowCamera { get; init; }
            public string TextureName { get; init; } = string.Empty;
            public WeatherSkyLayer Layer { get; init; } = null!;
            public int CardIndex { get; init; } = -1;
            public DeviceBuffer VertexBuffer { get; init; } = null!;
            public DeviceBuffer IndexBuffer { get; init; } = null!;
            public DeviceBuffer UniformBuffer { get; init; } = null!;
            public ResourceSet ResourceSet { get; init; } = null!;
            public uint IndexCount { get; init; }

            public void Dispose()
            {
                VertexBuffer?.Dispose();
                IndexBuffer?.Dispose();
                UniformBuffer?.Dispose();
                ResourceSet?.Dispose();
            }
        }

        public ZoneTerrainRenderer(GraphicsDevice gd)
        {
            _gd = gd ?? throw new ArgumentNullException(nameof(gd));
            InitializePipeline();
            _entityRenderer = new EntityRenderer(_gd);
            BuildFallbackScene();
            BuildOceanWaterPlane();
        }

        private void InitializePipeline()
        {
            var factory = _gd.ResourceFactory;

            // 1. Scene & Water Uniform Buffers (std140, ZoneSceneUniform.SizeInBytes)
            _sceneUniformBuffer = factory.CreateBuffer(new BufferDescription(
                ZoneSceneUniform.SizeInBytes,
                BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            _waterUniformBuffer = factory.CreateBuffer(new BufferDescription(
                ZoneSceneUniform.SizeInBytes,
                BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            // 2. Resource Layouts
            // Set 0: Scene Uniforms (World, View, Proj, Sun, Ambient, Fog, Eye, WeatherParams)
            _sceneLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("ZoneSceneUniforms", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment)));

            // Set 1: Diffuse Texture + Bilinear Sampler
            _textureLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("uTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("uSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

            _sceneResourceSet = factory.CreateResourceSet(new ResourceSetDescription(_sceneLayout, _sceneUniformBuffer));
            _waterResourceSet = factory.CreateResourceSet(new ResourceSetDescription(_sceneLayout, _waterUniformBuffer));
            _textureCache = new GpuTextureCache(_gd, _textureLayout);

            // 3. Shaders (SPIR-V cross-compilation for Opaque, Cutout Foliage, Blended surfaces, and Weather Sky)
            var vsDesc = new ShaderDescription(
                ShaderStages.Vertex,
                Encoding.UTF8.GetBytes(ZoneShaders.VertexShaderGlsl),
                "main");
            var fsOpaqueDesc = new ShaderDescription(
                ShaderStages.Fragment,
                Encoding.UTF8.GetBytes(ZoneShaders.FragmentShaderOpaqueGlsl),
                "main");
            var fsCutoutDesc = new ShaderDescription(
                ShaderStages.Fragment,
                Encoding.UTF8.GetBytes(ZoneShaders.FragmentShaderCutoutGlsl),
                "main");
            var fsBlendDesc = new ShaderDescription(
                ShaderStages.Fragment,
                Encoding.UTF8.GetBytes(ZoneShaders.FragmentShaderBlendGlsl),
                "main");

            var vsDecalDesc = new ShaderDescription(
                ShaderStages.Vertex,
                Encoding.UTF8.GetBytes(ZoneShaders.VertexShaderDecalGlsl),
                "main");
            var vsWaterDesc = new ShaderDescription(
                ShaderStages.Vertex,
                Encoding.UTF8.GetBytes(ZoneShaders.VertexShaderWaterGlsl),
                "main");
            var vsWeatherSkyDesc = new ShaderDescription(
                ShaderStages.Vertex,
                Encoding.UTF8.GetBytes(ZoneShaders.VertexShaderWeatherSkyGlsl),
                "main");

            var fsWaterDesc = new ShaderDescription(
                ShaderStages.Fragment,
                Encoding.UTF8.GetBytes(ZoneShaders.FragmentShaderWaterGlsl),
                "main");
            var fsWeatherSkyDesc = new ShaderDescription(
                ShaderStages.Fragment,
                Encoding.UTF8.GetBytes(ZoneShaders.FragmentShaderWeatherSkyGlsl),
                "main");

            Shader[] opaqueShaders = factory.CreateFromSpirv(vsDesc, fsOpaqueDesc);
            Shader[] decalShaders = factory.CreateFromSpirv(vsDecalDesc, fsBlendDesc);
            Shader[] cutoutShaders = factory.CreateFromSpirv(vsDesc, fsCutoutDesc);
            Shader[] blendShaders = factory.CreateFromSpirv(vsDesc, fsBlendDesc);
            Shader[] waterShaders = factory.CreateFromSpirv(vsWaterDesc, fsWaterDesc);
            Shader[] weatherSkyShaders = factory.CreateFromSpirv(vsWeatherSkyDesc, fsWeatherSkyDesc);

            // 4. Vertex Layout (36-byte MeshVertex stride: Pos(12) + Norm(12) + UV(8) + Color(4))
            var vertexLayout = new VertexLayoutDescription(
                new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3, 0),
                new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3, 12),
                new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2, 24),
                new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Byte4_Norm, 32));

            // Same 36-byte MeshVertex buffers, minus Normal, which the weather-sky vertex shader does not read.
            var weatherSkyVertexLayout = new VertexLayoutDescription(36,
                new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3, 0),
                new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2, 24),
                new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Byte4_Norm, 32));

            // 5. Opaque Graphics Pipeline (no discard; early-Z depth testing for solid ground & mountains)
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
                ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, opaqueShaders),
                Outputs = _gd.SwapchainFramebuffer.OutputDescription
            };
            _pipeline = factory.CreateGraphicsPipeline(pipelineDesc);

            // 6. Blended Terrain Surfaces & Decals Graphics Pipeline (multi-texture sand/grass/cliff transitions)
            // Authored with 0x8000 blend flag in FFXI; rendered using VertexShaderDecalGlsl with a linear
            // W-scaled depth bias (matching D3DRS_ZBIAS / polygonOffset(-5, 1)) so decals win the depth compare
            // against the coincident base terrain without flicker.
            // Following authentic FFXI architecture (xi-model-viewer / xim GLDrawer.drawXim):
            // Decals NEVER write depth (depthWriteEnabled = false). This prevents decals from occluding
            // objects drawn on top of them (dock posts, placed props) or corrupting entity depth (character feet).
            var terrainBlendPipelineDesc = new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleAlphaBlend,
                DepthStencilState = new DepthStencilStateDescription(
                    depthTestEnabled: true,
                    depthWriteEnabled: false,
                    comparisonKind: ComparisonKind.LessEqual),
                RasterizerState = new RasterizerStateDescription(
                    cullMode: FaceCullMode.None,
                    fillMode: PolygonFillMode.Solid,
                    frontFace: FrontFace.Clockwise,
                    depthClipEnabled: true,
                    scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _sceneLayout, _textureLayout },
                ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, decalShaders),
                Outputs = _gd.SwapchainFramebuffer.OutputDescription
            };
            _terrainBlendPipeline = factory.CreateGraphicsPipeline(terrainBlendPipelineDesc);

            // 7. Cutout Foliage Graphics Pipeline (4.0 * vColor.a * tex.a < 0.375 discard; depth writing enabled)
            var cutoutPipelineDesc = new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleOverrideBlend,
                DepthStencilState = new DepthStencilStateDescription(
                    depthTestEnabled: true,
                    depthWriteEnabled: true,
                    comparisonKind: ComparisonKind.LessEqual),
                RasterizerState = new RasterizerStateDescription(
                    cullMode: FaceCullMode.None,
                    fillMode: PolygonFillMode.Solid,
                    frontFace: FrontFace.Clockwise,
                    depthClipEnabled: true,
                    scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _sceneLayout, _textureLayout },
                ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, cutoutShaders),
                Outputs = _gd.SwapchainFramebuffer.OutputDescription
            };
            _cutoutPipeline = factory.CreateGraphicsPipeline(cutoutPipelineDesc);

            // 7. Alpha-Blended Graphics Pipeline (for translucent water foam, decals, fog, surf)
            var blendPipelineDesc = new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleAlphaBlend,
                DepthStencilState = new DepthStencilStateDescription(
                    depthTestEnabled: true,
                    depthWriteEnabled: false,
                    comparisonKind: ComparisonKind.LessEqual),
                RasterizerState = new RasterizerStateDescription(
                    cullMode: FaceCullMode.None,
                    fillMode: PolygonFillMode.Solid,
                    frontFace: FrontFace.Clockwise,
                    depthClipEnabled: true,
                    scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _sceneLayout, _textureLayout },
                ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, blendShaders),
                Outputs = _gd.SwapchainFramebuffer.OutputDescription
            };
            _blendPipeline = factory.CreateGraphicsPipeline(blendPipelineDesc);
            
            // 8. Alpha-Blended Water Graphics Pipeline (translucent water surfaces with linear W-scaled depth bias)
            // Authored with VertexShaderWaterGlsl (z - 0.00025 * w) to cleanly win depth testing over shallow seabed
            // and eliminate distance z-fighting / dry sand patch holes.
            var waterPipelineDesc = new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleAlphaBlend,
                DepthStencilState = new DepthStencilStateDescription(
                    depthTestEnabled: true,
                    depthWriteEnabled: false,
                    comparisonKind: ComparisonKind.LessEqual),
                RasterizerState = new RasterizerStateDescription(
                    cullMode: FaceCullMode.None,
                    fillMode: PolygonFillMode.Solid,
                    frontFace: FrontFace.Clockwise,
                    depthClipEnabled: true,
                    scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _sceneLayout, _textureLayout },
                ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, waterShaders),
                Outputs = _gd.SwapchainFramebuffer.OutputDescription
            };
            _waterPipeline = factory.CreateGraphicsPipeline(waterPipelineDesc);

            // 9. Alpha-Blended Weather Sky & Celestial Discs Graphics Pipeline (dynamic clouds, sun, moon, stars)
            // Rendered at far plane depth (clipPos.w * 0.9998 / 0.9997) with depth write disabled and depth test LessEqual.
            var weatherSkyPipelineDesc = new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleAlphaBlend,
                DepthStencilState = new DepthStencilStateDescription(
                    depthTestEnabled: true,
                    depthWriteEnabled: false,
                    comparisonKind: ComparisonKind.LessEqual),
                RasterizerState = new RasterizerStateDescription(
                    cullMode: FaceCullMode.None,
                    fillMode: PolygonFillMode.Solid,
                    frontFace: FrontFace.Clockwise,
                    depthClipEnabled: true,
                    scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _sceneLayout, _textureLayout },
                ShaderSet = new ShaderSetDescription(new[] { weatherSkyVertexLayout }, weatherSkyShaders),
                Outputs = _gd.SwapchainFramebuffer.OutputDescription
            };
            _weatherSkyPipeline = factory.CreateGraphicsPipeline(weatherSkyPipelineDesc);

            // 9b. Additive Weather Sky Pipeline (stars, sun, luminous celestial bodies)
            var weatherSkyAdditiveDesc = weatherSkyPipelineDesc;
            weatherSkyAdditiveDesc.BlendState = new BlendStateDescription(
                RgbaFloat.Black,
                new BlendAttachmentDescription(
                    blendEnabled: true,
                    sourceColorFactor: BlendFactor.One,
                    destinationColorFactor: BlendFactor.One,
                    colorFunction: BlendFunction.Add,
                    sourceAlphaFactor: BlendFactor.One,
                    destinationAlphaFactor: BlendFactor.One,
                    alphaFunction: BlendFunction.Add));
            _weatherSkyAdditivePipeline = factory.CreateGraphicsPipeline(weatherSkyAdditiveDesc);

            _skyDomeRenderer = new SkyDomeRenderer(_gd, _sceneLayout, _gd.SwapchainFramebuffer.OutputDescription);
            _commandList = factory.CreateCommandList();
        }

        /// <summary>
        /// Loads a ZoneGeometry model and its decoded textures into GPU buffers.
        /// </summary>
        public void LoadZone(ZoneGeometry? zone, IReadOnlyDictionary<string, DecodedTexture>? textures = null)
        {
            ClearZoneSubmeshes();
            ClearWeatherSkySubmeshes();
            var envWaterUv = zone?.EnvironmentData?.WaterUVScroll;
            _waterScrollVelocity = (envWaterUv.HasValue && envWaterUv.Value != Vector2.Zero)
                ? envWaterUv.Value
                : new Vector2(0.012f, -0.016f);
            _activeDecodedTextures = textures;

            if (zone == null || (zone.MeshGroups.Count == 0 && zone.WeatherSkyLayers.Count == 0))
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
                    Name = group.Name,
                    TextureName = group.TextureName,
                    VertexBuffer = vb,
                    IndexBuffer = ib,
                    IndexCount = (uint)ushortIndices.Length,
                    MinBounds = group.MinBounds,
                    MaxBounds = group.MaxBounds,
                    IsBlend = group.IsBlend,
                    NoCull = group.NoCull,
                    IsFoliage = group.IsFoliage || group.Name.StartsWith("_"),
                    IsWater = group.IsWater || ZoneDefDecoder.IsWaterMesh(group.Name, group.TextureName),
                    UVScroll = group.UVScroll
                });

                vertCount += group.Vertices.Length;
            }

            // Stream Section 0x05 / WeatherSky dynamic cloud layers and celestial discs
            if (zone.WeatherSkyLayers.Count > 0)
            {
                for (int i = 0; i < zone.WeatherSkyLayers.Count; i++)
                {
                    var layer = zone.WeatherSkyLayers[i];
                    for (int g = 0; g < layer.MeshGroups.Count; g++)
                    {
                        var group = layer.MeshGroups[g];
                        if (group.Vertices.Length == 0 || group.Indices.Length == 0) continue;

                        var vb = factory.CreateBuffer(new BufferDescription(
                            (uint)(group.Vertices.Length * 36),
                            BufferUsage.VertexBuffer));
                        _gd.UpdateBuffer(vb, 0, group.Vertices);

                        var ushortIndices = new ushort[group.Indices.Length];
                        for (int idx = 0; idx < group.Indices.Length; idx++)
                        {
                            ushortIndices[idx] = (ushort)group.Indices[idx];
                        }

                        var ib = factory.CreateBuffer(new BufferDescription(
                            (uint)(ushortIndices.Length * sizeof(ushort)),
                            BufferUsage.IndexBuffer));
                        _gd.UpdateBuffer(ib, 0, ushortIndices);

                        var ub = factory.CreateBuffer(new BufferDescription(
                            ZoneSceneUniform.SizeInBytes,
                            BufferUsage.UniformBuffer | BufferUsage.Dynamic));
                        var rSet = factory.CreateResourceSet(new ResourceSetDescription(_sceneLayout, ub));

                        _weatherSkySubmeshes.Add(new GpuWeatherSkySubmesh
                        {
                            Name = group.Name,
                            LayerName = layer.Name,
                            WeatherId = layer.WeatherId,
                            IsCelestial = layer.IsCelestial,
                            AttachType = layer.AttachType,
                            UVScroll = layer.UVScroll,
                            BasePosition = layer.Position,
                            Scale = layer.Scale,
                            FollowCamera = layer.FollowCamera,
                            TextureName = group.TextureName,
                            Layer = layer,
                            CardIndex = layer.IsMoonPhaseSpriteSheet ? g : -1,
                            VertexBuffer = vb,
                            IndexBuffer = ib,
                            UniformBuffer = ub,
                            ResourceSet = rSet,
                            IndexCount = (uint)ushortIndices.Length
                        });

                        vertCount += group.Vertices.Length;
                    }
                }

                // Draw order: Stars (1, additive) -> Celestial discs / Moon / Sun (2, unlit/alpha) -> Clouds (3, translucent alpha)
                // Ensures soft translucent clouds composite over celestial bodies and stars.
                static int GetSkyDrawPriority(GpuWeatherSkySubmesh s)
                {
                    if (s.Name.Contains("star", StringComparison.OrdinalIgnoreCase)) return 1;
                    if (s.IsCelestial || s.AttachType == ParticleAttachType.Sun || s.AttachType == ParticleAttachType.Moon) return 2;
                    return 3;
                }
                _weatherSkySubmeshes.Sort((a, b) => GetSkyDrawPriority(a).CompareTo(GetSkyDrawPriority(b)));
            }

            TotalVertices = vertCount;
            GordianLog.Info("Graphics", $"Streamed {zone.MeshGroups.Count} zone submeshes and {_weatherSkySubmeshes.Count} weather sky submeshes ({TotalVertices} vertices) to GPU.");
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
            ResourceManager? resourceManager = null,
            uint localPlayerServerId = 0,
            bool isLocalPlayerEngaged = false,
            Vector3? localPlayerDisplayPos = null,
            bool present = true,
            Framebuffer? targetFramebuffer = null)
        {
            if (_disposed || _gd == null || (_gd.MainSwapchain == null && targetFramebuffer == null)) return;

            // 1. Update Uniform Buffer
            float aspect = Math.Max(0.1f, (float)width / Math.Max(1, height));
            camera.AspectRatio = aspect;
            _cloudAccumulatedTime += deltaSeconds;

            float fogFar = (environment.FogEnabled && environment.FogEnd > environment.FogStart) ? environment.FogEnd : -1.0f;
            float fogRange = Math.Max(0.001f, fogFar - environment.FogStart);
            var sceneUniform = new ZoneSceneUniform
            {
                World = Matrix4x4.Identity,
                View = camera.ViewMatrix,
                Projection = camera.ProjectionMatrix,
                SunDirection = new Vector4(environment.SunDirection, 0.0f),
                SunColor = new Vector4(environment.SunColor, 1.0f),
                AmbientColor = new Vector4(environment.AmbientColor, 1.0f),
                FogColor = environment.FogColor,
                FogParams = new Vector4(environment.FogStart, fogFar, 1.0f / fogRange, environment.FogDensity),
                EyePosition = new Vector4(camera.Position, 1.0f),
                WeatherParams = Vector4.Zero
            };

            // 2. Select submesh list (loaded zone or fallback scene)
            var activeSubmeshes = _zoneSubmeshes.Count > 0 ? _zoneSubmeshes : _fallbackSubmeshes;

            // 3. Record Render Commands
            _commandList.Begin();
            _commandList.SetFramebuffer(targetFramebuffer ?? _gd.SwapchainFramebuffer);

            // Update Scene Uniform Buffer within command stream
            _commandList.UpdateBuffer(_sceneUniformBuffer, 0, ref sceneUniform);

            // Clear to atmospheric clear/horizon color for authentic FFXI horizon blending
            _commandList.ClearColorTarget(0, new RgbaFloat(
                environment.ClearColor.X,
                environment.ClearColor.Y,
                environment.ClearColor.Z,
                1.0f));
            _commandList.ClearDepthStencil(1.0f);

            int draws = 0;
            int culled = 0;
            int visible = 0;

            // Pass 0: Celestial Sky Dome (rendered at camera eye with depth writing disabled)
            if (_skyDomeRenderer != null)
            {
                _skyDomeRenderer.UpdateDome(environment);
                if (_skyDomeRenderer.HasGeometry)
                {
                    _skyDomeRenderer.Render(_commandList, _sceneResourceSet);
                    draws++;
                }
            }

            // Pass 0b: Weather Sky Layers & Celestial Discs (dynamic clouds, sun, moon, stars)
            // Rendered with depth testing enabled and depth writing disabled at far-plane projection (clipPos.w * 0.9998 / 0.9997).
            // Terrain geometry drawn in Pass 1 will naturally occlude these elements, while they render in front of the sky dome.
            if ((EnableWeatherClouds || EnableWeatherCelestialBodies) && _weatherSkySubmeshes.Count > 0 && !environment.Indoors)
            {
                Pipeline? currentSkyPipeline = null;
                string activeWeather = environment.WeatherId ?? "fine";
                Vector3 sunDir = Vector3.Normalize(environment.SunDirection);
                Vector3 moonDir = -sunDir;

                int dayOfWeek = VanaTime.GetDayOfWeekIndex(DateTime.UtcNow);
                int moonPhaseIndex = VanaTime.GetMoonPhaseIndex(DateTime.UtcNow);
                float dayFraction = environment.TimeOfDayHours / 24.0f;

                for (int i = 0; i < _weatherSkySubmeshes.Count; i++)
                {
                    var skyMesh = _weatherSkySubmeshes[i];
                    bool isCelestial = skyMesh.IsCelestial;
                    bool isStardust = isCelestial && skyMesh.LayerName.Contains("stardust", StringComparison.OrdinalIgnoreCase);
                    bool isMoon = isCelestial && skyMesh.AttachType == ParticleAttachType.Moon;
                    bool isSun = isCelestial && (skyMesh.AttachType == ParticleAttachType.Sun || ((skyMesh.Name.Contains("sun", StringComparison.OrdinalIgnoreCase) || skyMesh.LayerName.Contains("sun", StringComparison.OrdinalIgnoreCase)) && !skyMesh.Name.StartsWith("suny", StringComparison.OrdinalIgnoreCase) && !skyMesh.LayerName.StartsWith("suny", StringComparison.OrdinalIgnoreCase)));

                    if (isCelestial && !EnableWeatherCelestialBodies) continue;
                    if (!isCelestial && !EnableWeatherClouds) continue;
                    if (isSun && !EnableCelestialSun && !EnableCelestialDiscs) continue;
                    if (isMoon && !EnableCelestialMoon && !EnableCelestialDiscs) continue;
                    if (isStardust && !EnableMilkyWay) continue;

                    // Authentic FFXI Weather Gating:
                    // Dynamic cloud layers render according to active weather.
                    // For elemental and storm weathers (rain, snow, thdr, etc.), retail zones author cloud layers under
                    // canonical categories (clod, suny, fine, mist). Celestial bodies (sun, moon, stars) apply universally.
                    if (!isCelestial)
                    {
                        string canonicalWeather = VanaTime.GetCanonicalWeatherCategory(activeWeather);
                        bool matchesWeather = !string.IsNullOrEmpty(skyMesh.WeatherId) &&
                            (string.Equals(skyMesh.WeatherId, activeWeather, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(skyMesh.WeatherId, canonicalWeather, StringComparison.OrdinalIgnoreCase));

                        if (!matchesWeather)
                        {
                            continue;
                        }
                    }

                    if (isSun && sunDir.Y <= 0.0f) continue; // Sun below horizon
                    if (isMoon && moonDir.Y <= 0.0f) continue; // Moon below horizon

                    if (isCelestial && !isSun)
                    {
                        if (DrawCelestialGenerator(skyMesh, camera, moonDir, sceneUniform, dayOfWeek, moonPhaseIndex, dayFraction, ref currentSkyPipeline))
                        {
                            draws++;
                            visible++;
                        }
                        continue;
                    }

                    // Select pipeline: the sun disc is additive; clouds use alpha blending
                    Pipeline targetSkyPipeline = isSun ? _weatherSkyAdditivePipeline : _weatherSkyPipeline;

                    if (currentSkyPipeline != targetSkyPipeline)
                    {
                        _commandList.SetPipeline(targetSkyPipeline);
                        currentSkyPipeline = targetSkyPipeline;
                    }

                    // Determine world position
                    Vector3 centerPos;
                    if (skyMesh.AttachType == ParticleAttachType.Sun)
                    {
                        centerPos = camera.Position + sunDir * 900.0f;
                    }
                    else if (skyMesh.FollowCamera)
                    {
                        centerPos = camera.Position + skyMesh.BasePosition;
                    }
                    else
                    {
                        centerPos = skyMesh.BasePosition;
                    }

                    Matrix4x4 worldMatrix = skyMesh.AttachType == ParticleAttachType.Sun
                        ? CreateCelestialDiscMatrix(camera, centerPos, skyMesh.Scale)
                        : Matrix4x4.CreateScale(skyMesh.Scale) * Matrix4x4.CreateTranslation(centerPos);

                    // Compute scrolling UV offset based on UVScroll velocity and elapsed time (scaled to 60 FPS effect rate)
                    Vector2 uvOffset = isSun
                        ? Vector2.Zero
                        : (skyMesh.UVScroll * 60.0f) * _cloudAccumulatedTime;

                    // Reject cloud meshes with no authored texture.
                    // Exception: Sun disc geometry is intentionally untextured and colored via shader / vertex colors.
                    if (string.IsNullOrWhiteSpace(skyMesh.TextureName) && !isSun)
                    {
                        continue;
                    }

                    // Resolve texture with keyword fallback
                    string texName = skyMesh.TextureName;
                    if ((string.IsNullOrWhiteSpace(texName) || _activeDecodedTextures == null || !_activeDecodedTextures.ContainsKey(texName)) && _activeDecodedTextures != null)
                    {
                        if (isSun)
                        {
                            texName = FindTextureKey(_activeDecodedTextures, "sundisc", "sun_disc") ?? string.Empty;
                        }
                        else if (skyMesh.Name.Contains("cld", StringComparison.OrdinalIgnoreCase) || skyMesh.Name.Contains("fine", StringComparison.OrdinalIgnoreCase))
                        {
                            texName = FindTextureKey(_activeDecodedTextures, "fine_a01", "fine", "cld") ?? texName;
                        }
                        else if (skyMesh.Name.Contains("suny", StringComparison.OrdinalIgnoreCase))
                        {
                            texName = FindTextureKey(_activeDecodedTextures, "suny_a01", "suny") ?? texName;
                        }
                        else if (skyMesh.Name.Contains("clod", StringComparison.OrdinalIgnoreCase) || skyMesh.Name.Contains("mist", StringComparison.OrdinalIgnoreCase))
                        {
                            texName = FindTextureKey(_activeDecodedTextures, "clod_a01", "clod", "mist") ?? texName;
                        }
                    }

                    // Cloud meshes need their genuine texture (never the default checkerboard on sky shells);
                    // the untextured sun disc binds the default texture.
                    if (string.IsNullOrWhiteSpace(texName) || (_activeDecodedTextures != null && !_activeDecodedTextures.ContainsKey(texName)))
                    {
                        if (isSun)
                        {
                            texName = string.Empty; // Bound to DefaultResourceSet below
                        }
                        else if (_activeDecodedTextures != null)
                        {
                            string? match = !string.IsNullOrWhiteSpace(texName) ? FindTextureKey(_activeDecodedTextures, texName) : null;
                            if (match != null)
                            {
                                texName = match;
                            }
                            else
                            {
                                continue;
                            }
                        }
                        else
                        {
                            continue;
                        }
                    }

                    // WeatherParams: xy = UV scroll offset, z = elapsed time, w = layer type (2.0 = sun, 1.0 = clouds)
                    var layerUniform = sceneUniform;
                    layerUniform.World = worldMatrix;
                    layerUniform.WeatherParams = new Vector4(uvOffset.X, uvOffset.Y, _cloudAccumulatedTime, isSun ? 2.0f : 1.0f);

                    _commandList.UpdateBuffer(skyMesh.UniformBuffer, 0, ref layerUniform);
                    _commandList.SetGraphicsResourceSet(0, skyMesh.ResourceSet);
                    var texSet = _textureCache.GetOrCreateResourceSet(texName, _activeDecodedTextures);
                    _commandList.SetGraphicsResourceSet(1, texSet);

                    _commandList.SetVertexBuffer(0, skyMesh.VertexBuffer);
                    _commandList.SetIndexBuffer(skyMesh.IndexBuffer, IndexFormat.UInt16);
                    _commandList.DrawIndexed(skyMesh.IndexCount, 1, 0, 0, 0);

                    draws++;
                    visible++;
                }
            }

            _commandList.SetPipeline(_pipeline);
            _commandList.SetGraphicsResourceSet(0, _sceneResourceSet);

            var frustum = camera.Frustum;

            // Pass 1: World Geometry in Authored DAT Order (Solid Opaque, Decal Blends, Cutout Foliage)
            // Following authentic FFXI architecture (xi-model-viewer / xim GLDrawer.drawXim):
            // Terrain base, ground decals, placed props (docks, buildings), and cutout foliage are rendered
            // in authored DAT order. Decals composite over the surfaces drawn before them with depth writing
            // DISABLED. Placed structures and props render after decals, writing authoritative depth and
            // permanently preventing decals from creeping over props or corrupting depth buffers at any distance.
            Pipeline? currentBoundPipeline = null;
            for (int i = 0; i < activeSubmeshes.Count; i++)
            {
                var submesh = activeSubmeshes[i];
                // Water and translucent foliage/fog planes are deferred to the translucent water pass (Pass 3)
                if (submesh.IsWater || (submesh.IsBlend && submesh.IsFoliage)) continue;

                // Frustum Culling
                if (!frustum.IntersectsBox(submesh.MinBounds, submesh.MaxBounds))
                {
                    culled++;
                    continue;
                }

                Pipeline targetPipeline;
                if (submesh.IsBlend)
                {
                    // Blended terrain decals (sand/grass/cliff transitions, path overlays)
                    targetPipeline = _terrainBlendPipeline;
                }
                else if (submesh.IsFoliage)
                {
                    // Cutout foliage (palm trees, vines, grates) with alpha-test discard and depth write
                    targetPipeline = _cutoutPipeline;
                }
                else
                {
                    // Solid opaque terrain, rocks, placed structures, dock posts
                    targetPipeline = _pipeline;
                }

                if (currentBoundPipeline != targetPipeline)
                {
                    _commandList.SetPipeline(targetPipeline);
                    _commandList.SetGraphicsResourceSet(0, _sceneResourceSet);
                    currentBoundPipeline = targetPipeline;
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
                _commandList.UpdateBuffer(_sceneUniformBuffer, 0, ref groundUniform);

                if (currentBoundPipeline != _pipeline)
                {
                    _commandList.SetPipeline(_pipeline);
                    _commandList.SetGraphicsResourceSet(0, _sceneResourceSet);
                    currentBoundPipeline = _pipeline;
                }

                var texSet = _textureCache.GetOrCreateResourceSet(string.Empty, _activeDecodedTextures);
                _commandList.SetGraphicsResourceSet(1, texSet);
                _commandList.SetVertexBuffer(0, _groundPlaneSubmesh.VertexBuffer);
                _commandList.SetIndexBuffer(_groundPlaneSubmesh.IndexBuffer, IndexFormat.UInt16);
                _commandList.DrawIndexed(_groundPlaneSubmesh.IndexCount, 1, 0, 0, 0);
                draws++;
                visible++;

                // Restore identity world matrix for subsequent passes
                _commandList.UpdateBuffer(_sceneUniformBuffer, 0, ref sceneUniform);
            }

            // Pass 2: Live 3D entity models & modular equipment (drawn on top of terrain/foliage, behind blended water)
            if (_entityRenderer != null && entities != null)
            {
                _entityRenderer.RenderEntities(_commandList, camera, environment, entities, resourceManager, deltaSeconds, localPlayerServerId, isLocalPlayerEngaged, localPlayerDisplayPos);
                draws += _entityRenderer.DrawCalls;
                visible += _entityRenderer.VisibleEntities;
                culled += _entityRenderer.CulledEntities;
            }

            // Pass 3: Translucent Water, Translucent Foliage & Fog Planes (IsWater == true || (IsBlend == true && IsFoliage == true))
            // Rendered with depth testing enabled and depth writing DISABLED so ocean/rivers composite over seabed and wading entities.
            // Water submeshes use _waterPipeline with linear W-scaled depth bias to eliminate distance z-fighting over shallow seabed.
            Vector2 defaultWaterUv = _waterScrollVelocity * _cloudAccumulatedTime;
            var waterUniform = sceneUniform;
            waterUniform.WeatherParams = new Vector4(defaultWaterUv.X, defaultWaterUv.Y, _cloudAccumulatedTime, 0.0f);
            _commandList.UpdateBuffer(_waterUniformBuffer, 0, ref waterUniform);
            Vector2 currentBoundWaterUv = defaultWaterUv;

            Pipeline? currentBoundBlendPipeline = null;
            for (int i = 0; i < activeSubmeshes.Count; i++)
            {
                var submesh = activeSubmeshes[i];
                if (!submesh.IsWater && (!submesh.IsBlend || !submesh.IsFoliage)) continue;

                // Frustum Culling
                if (!frustum.IntersectsBox(submesh.MinBounds, submesh.MaxBounds))
                {
                    culled++;
                    continue;
                }

                var targetPipeline = submesh.IsWater ? _waterPipeline : _blendPipeline;
                var targetSet0 = submesh.IsWater ? _waterResourceSet : _sceneResourceSet;
                if (currentBoundBlendPipeline != targetPipeline)
                {
                    _commandList.SetPipeline(targetPipeline);
                    _commandList.SetGraphicsResourceSet(0, targetSet0);
                    currentBoundBlendPipeline = targetPipeline;
                }

                if (submesh.IsWater)
                {
                    Vector2 targetUv;
                    if (submesh.UVScroll != Vector2.Zero)
                    {
                        // Scale per-frame UVScroll from DAT effect generators to authentic calm ocean speeds (max ~0.035/sec)
                        float vx = Math.Clamp(submesh.UVScroll.X * 30.0f, -0.035f, 0.035f);
                        float vy = Math.Clamp(submesh.UVScroll.Y * 30.0f, -0.035f, 0.035f);
                        targetUv = new Vector2(vx, vy) * _cloudAccumulatedTime;
                    }
                    else
                    {
                        targetUv = defaultWaterUv;
                    }

                    if (targetUv != currentBoundWaterUv)
                    {
                        waterUniform.WeatherParams = new Vector4(targetUv.X, targetUv.Y, _cloudAccumulatedTime, 0.0f);
                        _commandList.UpdateBuffer(_waterUniformBuffer, 0, ref waterUniform);
                        currentBoundWaterUv = targetUv;
                    }
                }

                visible++;

                // Bind Texture Resource Set
                ResourceSet texSet;
                bool hasDedicatedWaterTex = !string.IsNullOrEmpty(submesh.TextureName) &&
                    ZoneDefDecoder.IsWaterMesh(string.Empty, submesh.TextureName);

                if (submesh.IsWater && !hasDedicatedWaterTex)
                {
                    texSet = _textureCache.GetOrCreateWaterResourceSet(_activeDecodedTextures);
                }
                else
                {
                    texSet = _textureCache.GetOrCreateResourceSet(submesh.TextureName, _activeDecodedTextures);
                }
                _commandList.SetGraphicsResourceSet(1, texSet);

                _commandList.SetVertexBuffer(0, submesh.VertexBuffer);
                _commandList.SetIndexBuffer(submesh.IndexBuffer, IndexFormat.UInt16);
                _commandList.DrawIndexed(submesh.IndexCount, 1, 0, 0, 0);
                draws++;
            }

            // Pass 3b: Base Sea-Level Ocean Water Plane
            // Rendered at sea level (Y = 0.0) with depth testing enabled and depth writing DISABLED.
            // Translucent ocean water composites over seabed, reefs, and wading entities while
            // dry land / island beaches (Y > 0) naturally occlude it.
            // Uses _waterPipeline with W-scaled depth bias to stably overlay seabed without distance z-fighting.
            bool shouldRenderOcean = EnableOceanWaterPlane &&
                                     !environment.Indoors &&
                                     _oceanWaterSubmesh != null &&
                                     HasSeaLevelGeometry(activeSubmeshes);

            IsOceanWaterPlaneActive = shouldRenderOcean;
            if (shouldRenderOcean && _oceanWaterSubmesh != null)
            {
                if (currentBoundWaterUv != defaultWaterUv)
                {
                    waterUniform.WeatherParams = new Vector4(defaultWaterUv.X, defaultWaterUv.Y, _cloudAccumulatedTime, 0.0f);
                    _commandList.UpdateBuffer(_waterUniformBuffer, 0, ref waterUniform);
                    currentBoundWaterUv = defaultWaterUv;
                }

                if (currentBoundBlendPipeline != _waterPipeline)
                {
                    _commandList.SetPipeline(_waterPipeline);
                    _commandList.SetGraphicsResourceSet(0, _waterResourceSet);
                    currentBoundBlendPipeline = _waterPipeline;
                }

                // Sample native DAT water texture if present, or procedural ocean wave texture
                var waterTexSet = _textureCache.GetOrCreateWaterResourceSet(_activeDecodedTextures);
                _commandList.SetGraphicsResourceSet(1, waterTexSet);

                _commandList.SetVertexBuffer(0, _oceanWaterSubmesh.VertexBuffer);
                _commandList.SetIndexBuffer(_oceanWaterSubmesh.IndexBuffer, IndexFormat.UInt16);
                _commandList.DrawIndexed(_oceanWaterSubmesh.IndexCount, 1, 0, 0, 0);
                draws++;
                visible++;
            }

            _commandList.End();

            // 4. Submit & Present
            _gd.SubmitCommands(_commandList);
            if (present)
            {
                _gd.SwapBuffers();
            }

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

        private void BuildOceanWaterPlane()
        {
            var factory = _gd.ResourceFactory;

            const int quads = 32;
            const int vertsPerSide = quads + 1; // 33
            const float halfSize = 2000.0f;
            const float totalSize = halfSize * 2.0f; // 4000 yalms
            const float step = totalSize / quads; // 125 yalms per quad
            const float tileUv = 200.0f; // 1 tile every 20 yalms (~20 yalms per wave ripple repeat, matching retail FFXI)

            var vertices = new MeshVertex[vertsPerSide * vertsPerSide];
            // Neutral PS2 modulate2x diffuse (R=128, G=128, B=128) preserving authentic DAT texture colors;
            // Calibrated alpha A=50 yields effective alpha = clamp(4.0 * (50/255) * (127/255)) ~= 0.39 (~39% opacity)
            // ensuring seabed sand, reefs, and wading characters show through with clean definition.
            const uint oceanColorRgba = 128 | (128 << 8) | (128 << 16) | (50 << 24);

            for (int iz = 0; iz < vertsPerSide; iz++)
            {
                float z = -halfSize + iz * step;
                float v = (float)iz / quads * tileUv;

                for (int ix = 0; ix < vertsPerSide; ix++)
                {
                    float x = -halfSize + ix * step;
                    float u = (float)ix / quads * tileUv;

                    int vIdx = iz * vertsPerSide + ix;
                    vertices[vIdx] = new MeshVertex(
                        new Vector3(x, 0.0f, z),
                        Vector3.UnitY,
                        new Vector2(u, v),
                        oceanColorRgba);
                }
            }

            ushort[] indices = new ushort[quads * quads * 6];
            int iIdx = 0;

            for (int iz = 0; iz < quads; iz++)
            {
                for (int ix = 0; ix < quads; ix++)
                {
                    ushort topLeft = (ushort)(iz * vertsPerSide + ix);
                    ushort topRight = (ushort)(topLeft + 1);
                    ushort bottomLeft = (ushort)((iz + 1) * vertsPerSide + ix);
                    ushort bottomRight = (ushort)(bottomLeft + 1);

                    // Clockwise front-facing triangles viewed from +Y
                    indices[iIdx++] = topLeft;
                    indices[iIdx++] = topRight;
                    indices[iIdx++] = bottomRight;

                    indices[iIdx++] = topLeft;
                    indices[iIdx++] = bottomRight;
                    indices[iIdx++] = bottomLeft;
                }
            }

            var vb = factory.CreateBuffer(new BufferDescription(
                (uint)(vertices.Length * 36),
                BufferUsage.VertexBuffer));
            _gd.UpdateBuffer(vb, 0, vertices);

            var ib = factory.CreateBuffer(new BufferDescription(
                (uint)(indices.Length * sizeof(ushort)),
                BufferUsage.IndexBuffer));
            _gd.UpdateBuffer(ib, 0, indices);

            _oceanWaterSubmesh = new GpuSubmesh
            {
                Name = "ocean_water_plane",
                TextureName = "ocean_water",
                VertexBuffer = vb,
                IndexBuffer = ib,
                IndexCount = (uint)indices.Length,
                MinBounds = new Vector3(-halfSize, -10.0f, -halfSize),
                MaxBounds = new Vector3(halfSize, 10.0f, halfSize),
                IsWater = true,
                IsBlend = true
            };
        }

        private bool HasSeaLevelGeometry(List<GpuSubmesh> submeshes)
        {
            if (submeshes.Count == 0 || _zoneSubmeshes.Count == 0)
            {
                return true; // Fallback scene / empty zone defaults to active ocean
            }

            for (int i = 0; i < submeshes.Count; i++)
            {
                var s = submeshes[i];
                if (s.IsWater) return true;
                if (s.MinBounds.Y <= 1.0f) return true;
            }

            return false;
        }

        /// <summary>
        /// Draws a celestial Section 0x05 generator layer (stars, Milky Way, moon disc, moon halo) with its
        /// authored placement, blend mode and texture factor. Returns false if it contributes nothing this frame.
        /// Placement and color rules referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer,
        /// ui/js/particle/runtime.js and ui/js/particleDrawer.js, after xim Particle / GLDrawer).
        /// </summary>
        private bool DrawCelestialGenerator(
            GpuWeatherSkySubmesh skyMesh,
            ViewportCamera camera,
            Vector3 moonDir,
            ZoneSceneUniform sceneUniform,
            int dayOfWeek,
            int moonPhaseIndex,
            float dayFraction,
            ref Pipeline? currentPipeline)
        {
            var layer = skyMesh.Layer;
            if (skyMesh.CardIndex >= 0 && skyMesh.CardIndex != Math.Min(moonPhaseIndex, layer.MeshGroups.Count - 1)) return false;

            Vector4 textureFactor = ComputeCelestialTextureFactor(layer, dayOfWeek, moonPhaseIndex, dayFraction);
            if (textureFactor.W <= 0.001f) return false;

            Matrix4x4 world;
            if (skyMesh.AttachType == ParticleAttachType.Moon)
            {
                Vector3 center = camera.Position + moonDir * 900.0f;
                world = skyMesh.CardIndex >= 0
                    ? CreateCameraFacingCardMatrix(camera, center, skyMesh.Scale)
                    : CreateCelestialDiscMatrix(camera, center, skyMesh.Scale);
            }
            else
            {
                // Generator rotation is authored in raw DAT axes; the (-x, -y, z) display flip negates X and Y rotations.
                Vector3 center = skyMesh.FollowCamera ? camera.Position + skyMesh.BasePosition : skyMesh.BasePosition;
                world = Matrix4x4.CreateScale(skyMesh.Scale) *
                        Matrix4x4.CreateRotationX(-layer.Rotation.X) *
                        Matrix4x4.CreateRotationY(-layer.Rotation.Y) *
                        Matrix4x4.CreateRotationZ(layer.Rotation.Z) *
                        Matrix4x4.CreateTranslation(center);
            }

            // Blend 0x04 is Src_InvSrc_Add (alpha); everything else here is authored additive (0x08 Src_One_Add).
            bool additive = layer.BlendMode != 0x04;
            Pipeline pipeline = additive ? _weatherSkyAdditivePipeline : _weatherSkyPipeline;
            if (currentPipeline != pipeline)
            {
                _commandList.SetPipeline(pipeline);
                currentPipeline = pipeline;
            }

            var layerUniform = sceneUniform;
            layerUniform.World = world;
            layerUniform.WeatherParams = new Vector4(0.0f, 0.0f, additive ? 1.0f : 0.0f, 3.0f);
            layerUniform.SkyTextureFactor = textureFactor;

            ResourceSet texSet = string.IsNullOrWhiteSpace(skyMesh.TextureName)
                ? _textureCache.WhiteResourceSet
                : _textureCache.GetOrCreateResourceSet(skyMesh.TextureName, _activeDecodedTextures);

            _commandList.UpdateBuffer(skyMesh.UniformBuffer, 0, ref layerUniform);
            _commandList.SetGraphicsResourceSet(0, skyMesh.ResourceSet);
            _commandList.SetGraphicsResourceSet(1, texSet);
            _commandList.SetVertexBuffer(0, skyMesh.VertexBuffer);
            _commandList.SetIndexBuffer(skyMesh.IndexBuffer, IndexFormat.UInt16);
            _commandList.DrawIndexed(skyMesh.IndexCount, 1, 0, 0, 0);
            return true;
        }

        /// <summary>
        /// Generator texture factor: base color, modulate-2x by the weekday and moon-phase tints,
        /// alpha scaled by the time-of-day clock curve, clamped to [0, 1].
        /// </summary>
        internal static Vector4 ComputeCelestialTextureFactor(WeatherSkyLayer layer, int dayOfWeek, int moonPhaseIndex, float dayFraction)
        {
            Vector4 factor = layer.BaseColor;
            if (layer.DayOfWeekColors is { Length: > 0 } dayColors)
            {
                factor *= dayColors[Math.Clamp(dayOfWeek, 0, dayColors.Length - 1)] * 2.0f;
            }
            if (layer.MoonPhaseColors is { Length: > 0 } phaseColors)
            {
                factor *= phaseColors[Math.Clamp(moonPhaseIndex, 0, phaseColors.Length - 1)] * 2.0f;
            }
            if (layer.ClockAlphaCurve != null)
            {
                factor.W *= layer.ClockAlphaCurve.Evaluate(Math.Clamp(dayFraction, 0.0f, 1.0f));
            }
            return Vector4.Clamp(factor, Vector4.Zero, Vector4.One);
        }

        /// <summary>
        /// Orients a Section 0x2E celestial disc (authored facing local -X) toward the camera.
        /// </summary>
        private static Matrix4x4 CreateCelestialDiscMatrix(ViewportCamera camera, Vector3 center, Vector3 scale)
        {
            Vector3 toCamera = Vector3.Normalize(camera.Position - center);
            Vector3 sourceNormal = new Vector3(-1f, 0f, 0f);
            float dot = Vector3.Dot(sourceNormal, toCamera);
            Quaternion rot;
            if (dot > 0.9999f)
            {
                rot = Quaternion.Identity;
            }
            else if (dot < -0.9999f)
            {
                rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
            }
            else
            {
                Vector3 cross = Vector3.Cross(sourceNormal, toCamera);
                rot = Quaternion.Normalize(new Quaternion(cross.X, cross.Y, cross.Z, 1.0f + dot));
            }
            return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(center);
        }

        /// <summary>
        /// XYZ billboard for a sprite-sheet card stored in display axes (-x, -y, z): the card's image-left
        /// (raw -X) maps to screen left and image-top (raw -Y) to screen top, as xim's LOOK_AT_NEG_Z basis does.
        /// </summary>
        private static Matrix4x4 CreateCameraFacingCardMatrix(ViewportCamera camera, Vector3 center, Vector3 scale)
        {
            Vector3 right = camera.Right * -scale.X;
            Vector3 up = camera.Up * scale.Y;
            Vector3 forward = camera.Forward * scale.Z;
            return new Matrix4x4(
                right.X, right.Y, right.Z, 0f,
                up.X, up.Y, up.Z, 0f,
                forward.X, forward.Y, forward.Z, 0f,
                center.X, center.Y, center.Z, 1f);
        }

        private static string? FindTextureKey(IReadOnlyDictionary<string, DecodedTexture> textures, params string[] keywords)
        {
            foreach (var kw in keywords)
            {
                foreach (var key in textures.Keys)
                {
                    if (key.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    {
                        return key;
                    }
                }
            }
            return null;
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

        private void ClearWeatherSkySubmeshes()
        {
            for (int i = 0; i < _weatherSkySubmeshes.Count; i++)
            {
                _weatherSkySubmeshes[i].Dispose();
            }
            _weatherSkySubmeshes.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            ClearZoneSubmeshes();
            ClearWeatherSkySubmeshes();

            for (int i = 0; i < _fallbackSubmeshes.Count; i++)
            {
                _fallbackSubmeshes[i].Dispose();
            }
            _fallbackSubmeshes.Clear();
            _groundPlaneSubmesh?.Dispose();
            _groundPlaneSubmesh = null;
            _oceanWaterSubmesh?.Dispose();
            _oceanWaterSubmesh = null;

            _entityRenderer?.Dispose();
            _skyDomeRenderer?.Dispose();
            _textureCache?.Dispose();
            _commandList?.Dispose();
            _pipeline?.Dispose();
            _terrainBlendPipeline?.Dispose();
            _cutoutPipeline?.Dispose();
            _blendPipeline?.Dispose();
            _waterPipeline?.Dispose();
            _weatherSkyPipeline?.Dispose();
            _weatherSkyAdditivePipeline?.Dispose();
            _sceneResourceSet?.Dispose();
            _waterResourceSet?.Dispose();
            _sceneLayout?.Dispose();
            _textureLayout?.Dispose();
            _sceneUniformBuffer?.Dispose();
            _waterUniformBuffer?.Dispose();
        }
    }
}
