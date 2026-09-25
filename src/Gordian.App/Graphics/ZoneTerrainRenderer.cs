// src/Gordian.App/Graphics/ZoneTerrainRenderer.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Gordian.Core.Diagnostics;
using Gordian.Core.Graphics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.World;
using Gordian.Core.World.Collision;
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

        // Moving platform (elevator) parts, drawn at their platform's live height.
        private readonly List<GpuSubmesh> _platformSubmeshes = new();
        private readonly Dictionary<string, ZoneSceneUniform> _subEnvironmentUniforms = new(StringComparer.OrdinalIgnoreCase);
        private PlatformHeight[] _platformHeights = Array.Empty<PlatformHeight>();

        /// <summary>
        /// The displayed session's world, whose elevator entities drive the zone's moving platforms.
        /// </summary>
        public WorldState? World { get; set; }

        // Sub-environment lighting (indoor areas such as Metalworks' ev01/ev02): one scene uniform and set per id.
        private readonly Dictionary<string, (DeviceBuffer Buffer, ResourceSet Set)> _subEnvironmentScenes =
            new(StringComparer.OrdinalIgnoreCase);
        private ResourceSet _waterResourceSet = null!;
        private Pipeline _pipeline = null!;
        private Pipeline _terrainBlendPipeline = null!;
        private Pipeline _cutoutPipeline = null!;
        private Pipeline _blendPipeline = null!;
        private Pipeline _waterPipeline = null!;
        private Pipeline _weatherSkyPipeline = null!;
        private Pipeline _weatherSkyAdditivePipeline = null!;
        private Pipeline _weatherSkyReverseSubtractPipeline = null!;
        // World-space zone effects: [straight alpha, additive, reverse subtract] x [no depth write, depth write]
        private readonly Pipeline[,] _effectPipelines = new Pipeline[3, 2];
        private Pipeline _lensFlarePipeline = null!;
        private readonly List<(GpuWeatherSkySubmesh Mesh, ZoneSceneUniform Uniform, ResourceSet Texture)> _pendingLensFlares = new();
        private readonly Dictionary<ParticleAttachType, (Vector3 Eye, Vector3 Direction, bool Visible)> _lightSourceVisibility = new();
        private CommandList _commandList = null!;
        private GpuTextureCache _textureCache = null!;
        private EntityRenderer? _entityRenderer;
        public EntityRenderer? EntityRenderer => _entityRenderer;
        private SkyDomeRenderer? _skyDomeRenderer;
        public SkyDomeRenderer? SkyDomeRenderer => _skyDomeRenderer;
        public ZoneGeometry? LoadedZone { get; private set; }

        private readonly List<GpuSubmesh> _zoneSubmeshes = new();
        private readonly List<GpuWeatherSkySubmesh> _weatherSkySubmeshes = new();
        private readonly List<GpuWeatherSkySubmesh> _effectSubmeshes = new();
        private readonly List<(GpuWeatherSkySubmesh Mesh, float Distance)> _effectDrawList = new();
        private readonly Dictionary<WeatherSkyLayer, ZoneParticleEmitter> _emitters = new(ReferenceEqualityComparer.Instance);
        private bool _emittersWarm;
        private ResourceLayout _lightLayout = null!;
        private DeviceBuffer _lightTableBuffer = null!;
        private DeviceBuffer _noLightRefsBuffer = null!;
        private ResourceSet _noLightSet = null!;
        private readonly float[] _lightTable = new float[PointLightTableLayout.SizeInBytes / sizeof(float)];
        private readonly List<(WeatherSkyLayer Layer, ZoneParticleEmitter Emitter)> _pointLights = new();
        private string _effectWeather = "fine";

        /// <summary>
        /// Point-light falloff exponent: a light's contribution is color x power x (1 - distance / range) ^ exponent. The
        /// client's attenuation is not documented in any available reference; 2 matched Windower captures of Southern
        /// San d'Oria's wall lamps at night (2026-09-24).
        /// </summary>
        public float PointLightFalloffExponent { get; set; } = 2.0f;

        /// <summary>
        /// Scale from a light's power (theta x theta multiplier) to its intensity; 1 matched the same captures.
        /// </summary>
        public float PointLightPowerScale { get; set; } = 1.0f;

        /// <summary>
        /// Brightness of a saturated point light. Each light's colour is its half-range colour x2 x power (theta x
        /// theta multiplier), clamped to 1 per channel, times this strength. Calibrated against two Windower captures
        /// (2026-09-25): Southern San d'Oria's auction-house lamps at 22:03 (gold, power 2 via their clock curve) and
        /// Bastok Metalworks' always-on interior lights (orange, power 12-40, which saturate to a pale yellow-white);
        /// the clamp reproduces both zones' colour balance, where scaling by power made Metalworks 6-20x too bright and
        /// orange. The client's exact light pipeline is not decoded.
        /// </summary>
        public float PointLightStrength { get; set; } = 0.75f;
        private readonly Dictionary<ZoneEmitterTemplate, ZoneParticleEmitter> _emittersByTemplate = new(ReferenceEqualityComparer.Instance);
        private WeatherRoutinePlayer? _weatherRoutines;
        private Vector3 _viewerFloorProbe = new(float.NaN);
        private bool _viewerInSubEnvironment;
        private const float EmitterWarmupFrames = 1200.0f;
        private readonly List<GpuSubmesh> _fallbackSubmeshes = new();
        private GpuSubmesh? _groundPlaneSubmesh;
        private GpuSubmesh? _oceanWaterSubmesh;
        private IReadOnlyDictionary<string, DecodedTexture>? _activeDecodedTextures;
        private float _cloudAccumulatedTime;
        private Vector2 _waterScrollVelocity = new(0.012f, -0.016f);

        private bool _disposed;

        /// <summary>
        /// Controls whether the synthetic sea-level ocean water plane is rendered in outdoor zones with sea-level elevation.
        /// Off by default: the legacy client has no such plane and draws only the zone's own water geometry (Ctrl+F9 toggles it).
        /// </summary>
        public bool EnableOceanWaterPlane { get; set; }

        /// <summary>
        /// Indicates whether the ocean water plane GPU geometry is currently allocated.
        /// </summary>
        public bool HasOceanWaterPlane => _oceanWaterSubmesh != null;

        /// <summary>
        /// Controls whether the weather's Section 0x05 cloud-shell generators (e.g. cld_fine, suny, clod) are rendered in Pass 0b.
        /// </summary>
        public bool EnableWeatherClouds { get; set; } = true;

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
        /// Controls whether the weather's sun generators (daytime glow, sunset disc and corona) and sun lens flares are rendered.
        /// </summary>
        public bool EnableCelestialSun { get; set; } = true;

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

        /// <summary>
        /// Number of GPU submeshes streamed for world-space zone effects (sea surfaces).
        /// </summary>
        public int EffectSubmeshCount => _effectSubmeshes.Count;

        /// <summary>
        /// Controls whether world-space zone effects driven by Section 0x05 generators (sea surfaces) are rendered.
        /// </summary>
        public bool EnableZoneEffects { get; set; } = true;
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

            /// <summary>
            /// Point-light binding (set 2) for a placement lit by zone lights; null uses the renderer's unlit set.
            /// </summary>
            public ResourceSet? LightSet { get; init; }
            public DeviceBuffer? LightRefsBuffer { get; init; }

            /// <summary>
            /// The placement's sub-environment (e.g. <c>ev01</c>); empty for the zone's outdoor environment.
            /// </summary>
            public string EnvironmentId { get; init; } = string.Empty;

            /// <summary>
            /// The moving platform (elevator) this part belongs to; empty for static scenery.
            /// </summary>
            public string PlatformId { get; init; } = string.Empty;

            public void Dispose()
            {
                VertexBuffer?.Dispose();
                IndexBuffer?.Dispose();
                LightSet?.Dispose();
                LightRefsBuffer?.Dispose();
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

            /// <summary>
            /// Largest vertex distance from the mesh origin, for culling scaled particle draws.
            /// </summary>
            public float BoundingRadius { get; init; }
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

            // Set 2: zone point lights (the frame's light table and the placement's light slots)
            _lightLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("PointLightTable", ResourceKind.UniformBuffer, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("PointLightRefs", ResourceKind.UniformBuffer, ShaderStages.Fragment)));
            _lightTableBuffer = factory.CreateBuffer(new BufferDescription(PointLightTableLayout.SizeInBytes, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            _noLightRefsBuffer = CreateLightRefsBuffer(Array.Empty<int>());
            _noLightSet = factory.CreateResourceSet(new ResourceSetDescription(_lightLayout, _lightTableBuffer, _noLightRefsBuffer));

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
                Encoding.UTF8.GetBytes(ZoneShaders.FragmentShaderBlendGlsl),
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
            var vsZoneEffectDesc = new ShaderDescription(
                ShaderStages.Vertex,
                Encoding.UTF8.GetBytes(ZoneShaders.VertexShaderZoneEffectGlsl),
                "main");
            Shader[] zoneEffectShaders = factory.CreateFromSpirv(vsZoneEffectDesc, fsWeatherSkyDesc);

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
                ResourceLayouts = new[] { _sceneLayout, _textureLayout, _lightLayout },
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
                ResourceLayouts = new[] { _sceneLayout, _textureLayout, _lightLayout },
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
                ResourceLayouts = new[] { _sceneLayout, _textureLayout, _lightLayout },
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
                ResourceLayouts = new[] { _sceneLayout, _textureLayout, _lightLayout },
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
                ResourceLayouts = new[] { _sceneLayout, _textureLayout, _lightLayout },
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

            // 9c. Reverse-subtract Weather Sky Pipeline (Src_One_RevSub: premultiplied source darkens the sky, e.g. sunset cloud bands)
            var weatherSkyReverseSubtractDesc = weatherSkyPipelineDesc;
            weatherSkyReverseSubtractDesc.BlendState = new BlendStateDescription(
                RgbaFloat.Black,
                new BlendAttachmentDescription(
                    blendEnabled: true,
                    sourceColorFactor: BlendFactor.One,
                    destinationColorFactor: BlendFactor.One,
                    colorFunction: BlendFunction.ReverseSubtract,
                    sourceAlphaFactor: BlendFactor.One,
                    destinationAlphaFactor: BlendFactor.One,
                    alphaFunction: BlendFunction.ReverseSubtract));
            _weatherSkyReverseSubtractPipeline = factory.CreateGraphicsPipeline(weatherSkyReverseSubtractDesc);

            // 9e. World-space zone effect pipelines (sea surfaces): the weather-sky blend states with real depth, testing
            // against the scene and writing depth only when the generator's depth mask asks for it.
            var effectBlendStates = new[]
            {
                weatherSkyPipelineDesc.BlendState,
                weatherSkyAdditiveDesc.BlendState,
                weatherSkyReverseSubtractDesc.BlendState
            };
            for (int blend = 0; blend < effectBlendStates.Length; blend++)
            {
                for (int depthWrite = 0; depthWrite < 2; depthWrite++)
                {
                    var effectDesc = weatherSkyPipelineDesc;
                    effectDesc.BlendState = effectBlendStates[blend];
                    effectDesc.DepthStencilState = new DepthStencilStateDescription(
                        depthTestEnabled: true,
                        depthWriteEnabled: depthWrite == 1,
                        comparisonKind: ComparisonKind.LessEqual);
                    effectDesc.ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, zoneEffectShaders);
                    _effectPipelines[blend, depthWrite] = factory.CreateGraphicsPipeline(effectDesc);
                }
            }

            // 9d. Lens-flare Pipeline: screen-space additive sprites drawn over the finished scene, no depth test
            var lensFlareDesc = weatherSkyAdditiveDesc;
            lensFlareDesc.DepthStencilState = DepthStencilStateDescription.Disabled;
            _lensFlarePipeline = factory.CreateGraphicsPipeline(lensFlareDesc);

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
            LoadedZone = zone;
            CreateSubEnvironmentScenes(zone);
            _lightSourceVisibility.Clear();
            var envWaterUv = zone?.EnvironmentData?.WaterUVScroll;
            _waterScrollVelocity = (envWaterUv.HasValue && envWaterUv.Value != Vector2.Zero)
                ? envWaterUv.Value
                : new Vector2(0.012f, -0.016f);
            _activeDecodedTextures = textures;

            if (zone == null || (zone.MeshGroups.Count == 0 && zone.WeatherSkyLayers.Count == 0 && zone.EffectLayers.Count == 0))
            {
                return;
            }

            var factory = _gd.ResourceFactory;
            int vertCount = 0;

            var allGroups = new List<(MeshGroup Group, string PlatformId)>(zone.MeshGroups.Count);
            foreach (var group in zone.MeshGroups) allGroups.Add((group, string.Empty));
            foreach (var (platformId, parts) in zone.MovingPlatformGroups)
            {
                foreach (var part in parts) allGroups.Add((part, platformId));
            }

            for (int i = 0; i < allGroups.Count; i++)
            {
                var (group, platformId) = allGroups[i];
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

                DeviceBuffer? lightRefs = null;
                ResourceSet? lightSet = null;
                if (group.PointLightSlots.Length > 0)
                {
                    lightRefs = CreateLightRefsBuffer(group.PointLightSlots);
                    lightSet = factory.CreateResourceSet(new ResourceSetDescription(_lightLayout, _lightTableBuffer, lightRefs));
                }

                (platformId.Length > 0 ? _platformSubmeshes : _zoneSubmeshes).Add(new GpuSubmesh
                {
                    PlatformId = platformId,
                    LightRefsBuffer = lightRefs,
                    LightSet = lightSet,
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
                    UVScroll = group.UVScroll,
                    EnvironmentId = group.EnvironmentId
                });

                vertCount += group.Vertices.Length;
            }

            // Stream Section 0x05 / WeatherSky dynamic cloud layers and celestial discs
            if (zone.WeatherSkyLayers.Count > 0)
            {
                for (int i = 0; i < zone.WeatherSkyLayers.Count; i++)
                {
                    vertCount += UploadGeneratorLayer(zone.WeatherSkyLayers[i], _weatherSkySubmeshes);
                }

                // Painter's order: sky generators draw in authored DAT order within their weather (the daytime sun glow
                // precedes the clouds that veil it); stable, so each layer keeps its submesh order.
                var authoredOrder = _weatherSkySubmeshes.OrderBy(s => s.Layer.AuthoredOrder).ToList();
                _weatherSkySubmeshes.Clear();
                _weatherSkySubmeshes.AddRange(authoredOrder);
            }

            // Stream world-space zone effects (sea surfaces, and the meshes of surf / wave-crest particle emitters)
            for (int i = 0; i < zone.EffectLayers.Count; i++)
            {
                var effectLayer = zone.EffectLayers[i];
                vertCount += UploadGeneratorLayer(effectLayer, _effectSubmeshes);
                if (effectLayer.Emitter != null)
                {
                    _emitters[effectLayer] = new ZoneParticleEmitter(effectLayer.Emitter, seed: i);
                }
            }

            // Parents spawn into their child generators' emitters.
            var emittersByTemplate = _emittersByTemplate;
            emittersByTemplate.Clear();
            foreach (var emitter in _emitters.Values) emittersByTemplate[emitter.Template] = emitter;
            foreach (var emitter in _emitters.Values)
            {
                emitter.ChildResolver = template => emittersByTemplate.TryGetValue(template, out var child) ? child : null;
            }
            _weatherRoutines = zone.WeatherRoutineGroups.Count > 0 ? new WeatherRoutinePlayer(zone.WeatherRoutineGroups) : null;
            _pointLights.Clear();
            foreach (var (layer, emitter) in _emitters)
            {
                if (layer.PointLightSlot >= 0 && layer.PointLightSlot < PointLightTableLayout.Slots) _pointLights.Add((layer, emitter));
            }
            _emittersWarm = false;
            _viewerFloorProbe = new Vector3(float.NaN);

            TotalVertices = vertCount;
            GordianLog.Info("Graphics", $"Streamed {zone.MeshGroups.Count} zone submeshes, {_weatherSkySubmeshes.Count} weather sky submeshes and {_effectSubmeshes.Count} zone effect submeshes ({TotalVertices} vertices) to GPU.");
        }

        /// <summary>
        /// A placement's light-slot uniform: four zero-based light-table slots, -1 for none.
        /// </summary>
        private DeviceBuffer CreateLightRefsBuffer(int[] slots)
        {
            var refs = new int[4] { -1, -1, -1, -1 };
            for (int i = 0; i < Math.Min(4, slots.Length); i++) refs[i] = slots[i];
            var buffer = _gd.ResourceFactory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
            _gd.UpdateBuffer(buffer, 0, refs);
            return buffer;
        }

        /// <summary>
        /// Uploads a Section 0x05 generator layer's meshes (sky layer or world effect), one GPU submesh with its own
        /// uniform buffer per mesh group. Returns the number of vertices streamed.
        /// </summary>
        private int UploadGeneratorLayer(WeatherSkyLayer layer, List<GpuWeatherSkySubmesh> target)
        {
            var factory = _gd.ResourceFactory;
            int vertCount = 0;
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

                float radiusSquared = 0.0f;
                foreach (var vertex in group.Vertices) radiusSquared = MathF.Max(radiusSquared, vertex.Position.LengthSquared());

                target.Add(new GpuWeatherSkySubmesh
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
                    CardIndex = layer.IsSpriteSheet || layer.IsLensFlare ? g : -1,
                    BoundingRadius = MathF.Sqrt(radiusSquared),
                    VertexBuffer = vb,
                    IndexBuffer = ib,
                    UniformBuffer = ub,
                    ResourceSet = rSet,
                    IndexCount = (uint)ushortIndices.Length
                });

                vertCount += group.Vertices.Length;
            }
            return vertCount;
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
                WeatherParams = Vector4.Zero,
                MoonColor = new Vector4(environment.MoonColor, 1.0f)
            };

            // 2. Select submesh list (loaded zone or fallback scene)
            var activeSubmeshes = _zoneSubmeshes.Count > 0 ? _zoneSubmeshes : _fallbackSubmeshes;

            // 3. Record Render Commands
            _commandList.Begin();
            _commandList.SetFramebuffer(targetFramebuffer ?? _gd.SwapchainFramebuffer);

            // Update Scene Uniform Buffer within command stream
            _commandList.UpdateBuffer(_sceneUniformBuffer, 0, ref sceneUniform);
            UpdateSubEnvironmentScenes(sceneUniform, environment);
            _platformHeights = World != null
                ? MovingPlatforms.Evaluate(LoadedZone?.Collision, World, VanaTime.GetEarthSecondsSinceEpoch(DateTime.UtcNow))
                : Array.Empty<PlatformHeight>();

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

                // Sky layers come from the zone's directory for the active weather; zones that author no directory for an
                // elemental weather (rain, snow, thdr, ...) fall back to its canonical category (clod, suny, fine, mist).
                // Celestial bodies are weather-scoped too: e.g. only fine/suny author star and moon, so overcast skies have none.
                string skyWeather = ResolveLayerWeather(_weatherSkySubmeshes, activeWeather);

                for (int i = 0; i < _weatherSkySubmeshes.Count; i++)
                {
                    var skyMesh = _weatherSkySubmeshes[i];
                    bool isCelestial = skyMesh.IsCelestial;
                    bool isStardust = isCelestial && skyMesh.LayerName.Contains("stardust", StringComparison.OrdinalIgnoreCase);
                    bool isMoon = isCelestial && skyMesh.AttachType == ParticleAttachType.Moon;
                    bool isSun = skyMesh.AttachType == ParticleAttachType.Sun;

                    if (isCelestial && !EnableWeatherCelestialBodies) continue;
                    if (!isCelestial && !EnableWeatherClouds) continue;
                    if (isSun && !EnableCelestialSun) continue;
                    if (isMoon && !EnableCelestialMoon) continue;
                    if (isStardust && !EnableMilkyWay) continue;

                    var weatherIds = skyMesh.Layer.WeatherIds;
                    if (weatherIds.Count > 0 && !weatherIds.Contains(skyWeather)) continue;

                    if (DrawSkyGenerator(skyMesh, camera, sunDir, sceneUniform, dayOfWeek, moonPhaseIndex, dayFraction, ref currentSkyPipeline))
                    {
                        draws++;
                        visible++;
                    }
                }
            }

            // Zone particle emitters (surf, weather, lamp lights) advance before the world draws, so this frame's point
            // lights shine on the terrain.
            if (EnableZoneEffects && _emitters.Count > 0)
            {
                UpdateZoneEmitters(camera, environment, deltaSeconds);
            }
            UploadPointLights();

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
            ResourceSet? currentSceneSet = null;
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

                var sceneSet = SceneSetFor(submesh);
                if (currentBoundPipeline != targetPipeline)
                {
                    _commandList.SetPipeline(targetPipeline);
                    currentBoundPipeline = targetPipeline;
                    currentSceneSet = null;
                }
                if (!ReferenceEquals(currentSceneSet, sceneSet))
                {
                    _commandList.SetGraphicsResourceSet(0, sceneSet);
                    currentSceneSet = sceneSet;
                }

                visible++;

                // Bind Texture Resource Set
                var texSet = _textureCache.GetOrCreateResourceSet(submesh.TextureName, _activeDecodedTextures);
                _commandList.SetGraphicsResourceSet(1, texSet);
                _commandList.SetGraphicsResourceSet(2, submesh.LightSet ?? _noLightSet);

                _commandList.SetVertexBuffer(0, submesh.VertexBuffer);
                _commandList.SetIndexBuffer(submesh.IndexBuffer, IndexFormat.UInt16);
                _commandList.DrawIndexed(submesh.IndexCount, 1, 0, 0, 0);
                draws++;
            }

            DrawMovingPlatforms(sceneUniform, frustum, ref draws, ref visible, ref culled);

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
                _commandList.SetGraphicsResourceSet(2, _noLightSet);
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
                _entityRenderer.RenderEntities(_commandList, camera, environment, entities, resourceManager, deltaSeconds, localPlayerServerId, isLocalPlayerEngaged, localPlayerDisplayPos, LoadedZone?.Collision, _platformHeights);
                draws += _entityRenderer.DrawCalls;
                visible += _entityRenderer.VisibleEntities;
                culled += _entityRenderer.CulledEntities;
            }

            // Pass 3: Translucent Water, Translucent Foliage & Fog Planes (IsWater == true || (IsBlend == true && IsFoliage == true))
            // Rendered with depth testing enabled and depth writing DISABLED so ocean/rivers composite over seabed and wading entities.
            // Water submeshes use _waterPipeline with linear W-scaled depth bias to eliminate distance z-fighting over shallow seabed.
            // Generator effects advance at 60 frames per second; static water meshes do not scroll.
            float effectFrames = _cloudAccumulatedTime * 60.0f;
            var waterUniform = sceneUniform;
            _commandList.UpdateBuffer(_waterUniformBuffer, 0, ref waterUniform);
            Vector2 currentBoundWaterUv = Vector2.Zero;

            Pipeline? currentBoundBlendPipeline = null;
            ResourceSet? currentBlendSceneSet = null;
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
                var targetSet0 = submesh.IsWater ? _waterResourceSet : SceneSetFor(submesh);
                if (currentBoundBlendPipeline != targetPipeline)
                {
                    _commandList.SetPipeline(targetPipeline);
                    currentBoundBlendPipeline = targetPipeline;
                    currentBlendSceneSet = null;
                }
                if (!ReferenceEquals(currentBlendSceneSet, targetSet0))
                {
                    _commandList.SetGraphicsResourceSet(0, targetSet0);
                    currentBlendSceneSet = targetSet0;
                }

                if (submesh.IsWater)
                {
                    // Section 0x05 UV scroll is authored per effect frame
                    Vector2 targetUv = submesh.UVScroll * effectFrames;
                    if (targetUv != currentBoundWaterUv)
                    {
                        waterUniform.WeatherParams = new Vector4(targetUv.X, targetUv.Y, _cloudAccumulatedTime, 0.0f);
                        _commandList.UpdateBuffer(_waterUniformBuffer, 0, ref waterUniform);
                        currentBoundWaterUv = targetUv;
                    }
                }

                visible++;

                // Every surface, water included, samples its own authored texture
                var texSet = _textureCache.GetOrCreateResourceSet(submesh.TextureName, _activeDecodedTextures);
                _commandList.SetGraphicsResourceSet(1, texSet);
                _commandList.SetGraphicsResourceSet(2, submesh.LightSet ?? _noLightSet);

                _commandList.SetVertexBuffer(0, submesh.VertexBuffer);
                _commandList.SetIndexBuffer(submesh.IndexBuffer, IndexFormat.UInt16);
                _commandList.DrawIndexed(submesh.IndexCount, 1, 0, 0, 0);
                draws++;
            }

            // Pass 3a: World-space zone effects (sea surfaces, sunset glints on the water) from Section 0x05 generators.
            // Depth-writing surfaces (e.g. Bibiki Bay's open sea) draw first, then the rest back to front.
            if (EnableZoneEffects && _effectSubmeshes.Count > 0)
            {
                string effectWeather = ResolveLayerWeather(_effectSubmeshes, environment.WeatherId ?? "fine");
                Vector3 effectSunDir = Vector3.Normalize(environment.SunDirection);
                int effectDayOfWeek = VanaTime.GetDayOfWeekIndex(DateTime.UtcNow);
                int effectMoonPhase = VanaTime.GetMoonPhaseIndex(DateTime.UtcNow);
                float effectDayFraction = environment.TimeOfDayHours / 24.0f;

                _effectDrawList.Clear();
                foreach (var effectMesh in _effectSubmeshes)
                {
                    var weatherIds = effectMesh.Layer.WeatherIds;
                    if (weatherIds.Count > 0 && !weatherIds.Contains(effectWeather)) continue;
                    // Camera-following weather effects sit at their base offset from the eye.
                    float sortDistance = effectMesh.FollowCamera
                        ? effectMesh.BasePosition.Length()
                        : Vector3.Distance(camera.Position, effectMesh.BasePosition);
                    _effectDrawList.Add((effectMesh, sortDistance));
                }
                _effectDrawList.Sort((a, b) =>
                    a.Mesh.Layer.DepthWrite != b.Mesh.Layer.DepthWrite
                        ? (a.Mesh.Layer.DepthWrite ? -1 : 1)
                        : b.Distance.CompareTo(a.Distance));


                Pipeline? currentEffectPipeline = null;
                foreach (var (effectMesh, _) in _effectDrawList)
                {
                    if (_emitters.TryGetValue(effectMesh.Layer, out var emitter))
                    {
                        draws += DrawEmitterParticles(effectMesh, emitter, camera, sceneUniform, ref currentEffectPipeline);
                        visible++;
                    }
                    else if (DrawSkyGenerator(effectMesh, camera, effectSunDir, sceneUniform, effectDayOfWeek, effectMoonPhase, effectDayFraction, ref currentEffectPipeline))
                    {
                        draws++;
                        visible++;
                    }
                }
                currentBoundBlendPipeline = null;
            }

            // Pass 3b: Optional synthetic sea-level ocean water plane (enhancement, not in the legacy client)
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
                Vector2 defaultWaterUv = _waterScrollVelocity * _cloudAccumulatedTime;
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
                _commandList.SetGraphicsResourceSet(2, _noLightSet);

                _commandList.SetVertexBuffer(0, _oceanWaterSubmesh.VertexBuffer);
                _commandList.SetIndexBuffer(_oceanWaterSubmesh.IndexBuffer, IndexFormat.UInt16);
                _commandList.DrawIndexed(_oceanWaterSubmesh.IndexCount, 1, 0, 0, 0);
                draws++;
                visible++;
            }

            // Pass 4: Lens flares (sun and moon), screen-space over the finished scene
            if (_pendingLensFlares.Count > 0)
            {
                _commandList.SetPipeline(_lensFlarePipeline);
                foreach (var (flareMesh, flareUniform, flareTexture) in _pendingLensFlares)
                {
                    var uniform = flareUniform;
                    _commandList.UpdateBuffer(flareMesh.UniformBuffer, 0, ref uniform);
                    _commandList.SetGraphicsResourceSet(0, flareMesh.ResourceSet);
                    _commandList.SetGraphicsResourceSet(1, flareTexture);
                    _commandList.SetVertexBuffer(0, flareMesh.VertexBuffer);
                    _commandList.SetIndexBuffer(flareMesh.IndexBuffer, IndexFormat.UInt16);
                    _commandList.DrawIndexed(flareMesh.IndexCount, 1, 0, 0, 0);
                    draws++;
                }
                _pendingLensFlares.Clear();
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
        /// Draws a Section 0x05 sky generator layer (cloud shells, stars, Milky Way, moon disc and halo, pole star,
        /// moon lens flare) with its authored placement, motion, blend function, fog and texture factor.
        /// Returns false if it contributes nothing this frame.
        /// Placement and color rules referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer,
        /// ui/js/particle/runtime.js and ui/js/particleDrawer.js, after xim Particle / GLDrawer).
        /// </summary>
        private bool DrawSkyGenerator(
            GpuWeatherSkySubmesh skyMesh,
            ViewportCamera camera,
            Vector3 sunDir,
            ZoneSceneUniform sceneUniform,
            int dayOfWeek,
            int moonPhaseIndex,
            float dayFraction,
            ref Pipeline? currentPipeline)
        {
            var layer = skyMesh.Layer;
            if (layer.IsSpriteSheet)
            {
                int card = layer.IsMoonPhaseSpriteSheet ? Math.Min(moonPhaseIndex, layer.MeshGroups.Count - 1) : 0;
                if (skyMesh.CardIndex != card) return false;
            }

            Vector4 textureFactor = ComputeSkyTextureFactor(layer, dayOfWeek, moonPhaseIndex, dayFraction);
            if (textureFactor.W <= 0.001f) return false;

            // Generator effects advance at 60 frames per second.
            float frames = _cloudAccumulatedTime * 60.0f;

            // Sun and moon generators ride the celestial orbit 900 yalms out; the moon is opposite the sun.
            Vector3 lightDirection = skyMesh.AttachType == ParticleAttachType.Moon ? -sunDir : sunDir;
            bool attachedToLight = skyMesh.AttachType is ParticleAttachType.Sun or ParticleAttachType.Moon;
            Vector3 center = attachedToLight
                ? camera.Position + lightDirection * 900.0f
                : skyMesh.FollowCamera ? camera.Position + skyMesh.BasePosition : skyMesh.BasePosition;
            center += ToDisplay(EvaluateClockVector(layer.ClockPositionCurves, dayFraction));
            Vector3 scale = EvaluateClockScale(layer.ClockScaleCurves, skyMesh.Scale, dayFraction);

            if (layer.IsWorldEffect)
            {
                // Distance fade toward the generator, then the client's opaque snap for blended particle meshes.
                if (layer.FadeFar > 0.0f || layer.FadeNear > 0.0f)
                {
                    textureFactor.W *= ZoneParticleEmitter.FallOff(Vector3.Distance(camera.Position, center), layer.FadeNear, layer.FadeFar);
                }
                if (layer.IsParticleMesh && layer.BlendFunc == ParticleBlendFunc.SrcInvSrcAdd && textureFactor.W >= 127.0f / 255.0f)
                {
                    textureFactor.W = 1.0f;
                }
                if (textureFactor.W <= 0.001f) return false;
            }

            Matrix4x4 world = Matrix4x4.Identity;
            Vector2 uvOrFlareCenter = skyMesh.UVScroll * frames;
            float layerType = 3.0f;
            if (layer.IsLensFlare)
            {
                float offset = skyMesh.CardIndex < layer.FlareOffsets.Count ? layer.FlareOffsets[skyMesh.CardIndex] : 0.0f;
                if (!TryComputeFlareCenter(center, camera.ViewMatrix * camera.ProjectionMatrix, offset, out uvOrFlareCenter)) return false;
                if (!IsLightSourceVisible(skyMesh.AttachType, camera.Position, lightDirection)) return false;
                layerType = 4.0f;
            }
            else if (layer.IsSpriteSheet)
            {
                world = CreateCameraFacingCardMatrix(camera, center, scale);
            }
            else if (attachedToLight)
            {
                world = CreateCelestialDiscMatrix(camera, center, scale);
            }
            else
            {
                // Generator rotation is authored in raw DAT axes; the (-x, -y, z) display flip negates X and Y rotations.
                Vector3 rotation = layer.Rotation + layer.RotationVelocity * frames;
                world = Matrix4x4.CreateScale(scale) *
                        Matrix4x4.CreateRotationX(-rotation.X) *
                        Matrix4x4.CreateRotationY(-rotation.Y) *
                        Matrix4x4.CreateRotationZ(rotation.Z) *
                        Matrix4x4.CreateTranslation(center);
            }

            return SubmitGeneratorDraw(skyMesh, world, uvOrFlareCenter, layerType, textureFactor, sceneUniform, ref currentPipeline);
        }

        /// <summary>
        /// Draws every live particle of a surf / wave-crest emitter with its own transform, texture factor and UV offset.
        /// Returns the number of draw calls issued.
        /// </summary>
        private int DrawEmitterParticles(GpuWeatherSkySubmesh skyMesh, ZoneParticleEmitter emitter, ViewportCamera camera, ZoneSceneUniform sceneUniform, ref Pipeline? currentPipeline)
        {
            var layer = skyMesh.Layer;
            int drawn = 0;
            var billboard = emitter.Template.Definition.Setup?.BillBoardType ?? ParticleBillBoardType.None;
            var frustum = camera.Frustum;
            foreach (var particle in emitter.Particles)
            {
                if (particle.IsExpired || particle.IsOcclusionProbe) continue;
                if (skyMesh.CardIndex >= 0 && skyMesh.CardIndex != particle.SpriteIndex) continue;
                Vector4 textureFactor = Vector4.Clamp(particle.TextureFactor, Vector4.Zero, Vector4.One);
                if (layer.IsParticleMesh && layer.BlendFunc == ParticleBlendFunc.SrcInvSrcAdd && textureFactor.W >= 127.0f / 255.0f)
                {
                    textureFactor.W = 1.0f;
                }
                if (textureFactor.W <= 0.001f) continue;

                // Particle rotation is in raw DAT axes; the (-x, -y, z) display flip negates X and Y rotations.
                Vector3 rotation = particle.NegateRotationY ? particle.Rotation with { Y = -particle.Rotation.Y } : particle.Rotation;
                Matrix4x4 local = Matrix4x4.CreateScale(particle.Scale) *
                                  Matrix4x4.CreateRotationX(-rotation.X) *
                                  Matrix4x4.CreateRotationY(-rotation.Y) *
                                  Matrix4x4.CreateRotationZ(rotation.Z);
                // Billboards replace the orientation after the particle's own rotation and scale: XYZ faces the camera,
                // XZ keeps world up and turns only about it (xim applies both to the model-view's upper 3x3).
                Matrix4x4 facing = billboard switch
                {
                    ParticleBillBoardType.XYZ => CreateBillboardBasis(camera.Right, camera.Up, camera.Forward),
                    ParticleBillBoardType.XZ => CreateBillboardBasis(camera.Right, Vector3.UnitY, camera.Forward),
                    _ => Matrix4x4.Identity
                };
                // Movement billboards turn toward the particle's travel (horizontal travel only for MovementHorizontal),
                // Camera billboards toward the eye; batched particles ignore movement orientation.
                Vector3? direction = billboard switch
                {
                    ParticleBillBoardType.Movement when particle.SubOffsets == null => particle.LastMovement,
                    ParticleBillBoardType.MovementHorizontal when particle.SubOffsets == null => particle.LastMovement with { Y = 0.0f },
                    ParticleBillBoardType.Camera => ToDisplay(camera.Position) - particle.WorldPosition,
                    _ => null
                };
                if (direction is { } towards) local *= ToDisplayRotation(ZoneParticleEmitter.CreateDirectionOrientation(towards));

                float radius = skyMesh.BoundingRadius * MathF.Max(MathF.Abs(particle.Scale.X), MathF.Max(MathF.Abs(particle.Scale.Y), MathF.Abs(particle.Scale.Z)));
                Matrix4x4 oriented = local * facing;
                Vector3 position = ToDisplay(particle.WorldPosition);
                var subOffsets = particle.SubOffsets;
                int drawCount = subOffsets?.Length ?? 1;
                for (int i = 0; i < drawCount; i++)
                {
                    // Sub-particle offsets are raw DAT-space world translations.
                    Vector3 center = subOffsets == null ? position : position + ToDisplay(subOffsets[i]);
                    if (!frustum.IntersectsSphere(center, radius)) continue;
                    if (SubmitGeneratorDraw(skyMesh, oriented * Matrix4x4.CreateTranslation(center), particle.TexCoordTranslate, 3.0f, textureFactor, sceneUniform, ref currentPipeline))
                    {
                        drawn++;
                    }
                }
            }
            return drawn;
        }

        /// <summary>
        /// Converts a raw DAT-space linear transform to display space (the (-x, -y, z) flip on both sides).
        /// </summary>
        private static Matrix4x4 ToDisplayRotation(Matrix4x4 raw)
        {
            var flip = Matrix4x4.CreateScale(-1.0f, -1.0f, 1.0f);
            return flip * raw * flip;
        }

        /// <summary>
        /// Binds the pipeline for a generator layer's blend mode (sky or world effect) and draws one of its meshes with the
        /// given transform, UV offset (or lens-flare centre) and texture factor. Lens flares are deferred to the flare pass.
        /// </summary>
        private bool SubmitGeneratorDraw(
            GpuWeatherSkySubmesh skyMesh,
            Matrix4x4 world,
            Vector2 uvOrFlareCenter,
            float layerType,
            Vector4 textureFactor,
            ZoneSceneUniform sceneUniform,
            ref Pipeline? currentPipeline)
        {
            var layer = skyMesh.Layer;
            // Shader blend output: 0 = straight alpha, 1 = premultiplied (additive / reverse subtract), 2 = darken by alpha.
            (Pipeline pipeline, float blendOutput) = layer.BlendFunc switch
            {
                ParticleBlendFunc.SrcInvSrcAdd or ParticleBlendFunc.OneZero => (_weatherSkyPipeline, 0.0f),
                ParticleBlendFunc.ZeroInvSrcAdd => (_weatherSkyPipeline, 2.0f),
                ParticleBlendFunc.SrcOneRevSub => (_weatherSkyReverseSubtractPipeline, 1.0f),
                _ => (_weatherSkyAdditivePipeline, 1.0f)
            };
            if (layer.IsWorldEffect)
            {
                int blendIndex = layer.BlendFunc switch
                {
                    ParticleBlendFunc.SrcInvSrcAdd or ParticleBlendFunc.OneZero or ParticleBlendFunc.ZeroInvSrcAdd => 0,
                    ParticleBlendFunc.SrcOneRevSub => 2,
                    _ => 1
                };
                pipeline = _effectPipelines[blendIndex, layer.DepthWrite ? 1 : 0];
            }
            if (currentPipeline != pipeline)
            {
                _commandList.SetPipeline(pipeline);
                currentPipeline = pipeline;
            }

            var layerUniform = sceneUniform;
            layerUniform.World = world;
            layerUniform.WeatherParams = new Vector4(uvOrFlareCenter.X, uvOrFlareCenter.Y, blendOutput, layerType);
            layerUniform.SkyTextureFactor = textureFactor;
            // Additive layers fog toward black so distant haze never glows (xim computeLightingParams).
            layerUniform.SkyLayerParams = new Vector4(
                layer.FogEnabled ? 1.0f : 0.0f,
                layer.BlendFunc == ParticleBlendFunc.SrcOneAdd ? 1.0f : 0.0f,
                layer.IsWorldEffect && layer.LightingEnabled ? 1.0f : 0.0f,
                0.0f);

            ResourceSet texSet = string.IsNullOrWhiteSpace(skyMesh.TextureName)
                ? _textureCache.NeutralResourceSet
                : _textureCache.GetOrCreateResourceSet(skyMesh.TextureName, _activeDecodedTextures);

            if (layer.IsLensFlare)
            {
                _pendingLensFlares.Add((skyMesh, layerUniform, texSet));
                return true;
            }

            _commandList.UpdateBuffer(skyMesh.UniformBuffer, 0, ref layerUniform);
            _commandList.SetGraphicsResourceSet(0, skyMesh.ResourceSet);
            _commandList.SetGraphicsResourceSet(1, texSet);
            _commandList.SetVertexBuffer(0, skyMesh.VertexBuffer);
            _commandList.SetIndexBuffer(skyMesh.IndexBuffer, IndexFormat.UInt16);
            _commandList.DrawIndexed(skyMesh.IndexCount, 1, 0, 0, 0);
            return true;
        }

        private static Vector3 ToDisplay(Vector3 raw) => new(-raw.X, -raw.Y, raw.Z);

        /// <summary>
        /// Advances every zone emitter on the 60 Hz effect clock; the first frame after a zone load pre-warms them so the
        /// shoreline is not empty while the first waves roll in. Weather emitters run only under their weather.
        /// </summary>
        private void UpdateZoneEmitters(ViewportCamera camera, ZoneEnvironmentSettings environment, float deltaSeconds)
        {
            _effectWeather = ResolveLayerWeather(_effectSubmeshes, environment.WeatherId ?? "fine");
            var frame = new ZoneParticleFrame(ToDisplay(camera.Position), environment.TimeOfDayHours / 24.0f, StrongestLight(environment),
                ToDisplay(camera.Forward), VanaTime.GetDayOfWeekIndex(DateTime.UtcNow), VanaTime.GetMoonPhaseIndex(DateTime.UtcNow),
                IsViewerInSubEnvironment(camera.Position));
            float emitterFrames = _emittersWarm ? Math.Clamp(deltaSeconds, 0.0f, 0.25f) * 60.0f : EmitterWarmupFrames;
            // Lightning strikes and other short weather routines start their generators at random.
            _weatherRoutines?.Update(_emittersWarm ? emitterFrames : 0.0f, _effectWeather,
                template => _emittersByTemplate.TryGetValue(template, out var triggered) ? triggered : null);
            foreach (var (emitterLayer, emitter) in _emitters)
            {
                // Weather emitters run only under their weather; a weather change starts them afresh.
                if (emitterLayer.WeatherIds.Count > 0 && !emitterLayer.WeatherIds.Contains(_effectWeather))
                {
                    if (emitter.Particles.Count > 0) emitter.Particles.Clear();
                    continue;
                }
                emitter.Update(emitterFrames, frame);
            }
            _emittersWarm = true;
        }

        /// <summary>
        /// Fills the frame's point-light table from the running light generators: each slot takes its generator's live
        /// particle (display-space position, range x range multiplier, and the saturated colour described at
        /// <see cref="PointLightStrength"/>).
        /// </summary>
        private void UploadPointLights()
        {
            Array.Clear(_lightTable);
            _lightTable[0] = PointLightFalloffExponent;
            _lightTable[1] = PointLightPowerScale;
            const int positionBase = 4;
            const int colorBase = 4 + PointLightTableLayout.Slots * 4;
            if (EnableZoneEffects)
            {
                foreach (var (layer, emitter) in _pointLights)
                {
                    if (layer.WeatherIds.Count > 0 && !layer.WeatherIds.Contains(_effectWeather)) continue;
                    ZoneParticle? light = null;
                    foreach (var particle in emitter.Particles)
                    {
                        if (!particle.IsExpired) { light = particle; break; }
                    }
                    if (light == null) continue;

                    float range = light.LightRange * light.LightRangeMultiplier;
                    var factor = light.TextureFactor;
                    float power = light.LightTheta * light.LightThetaMultiplier * light.ColorMultiplier.W;
                    if (range <= 0.0f || power <= 0.0f) continue;

                    int slot = layer.PointLightSlot;
                    var position = ToDisplay(light.WorldPosition);
                    _lightTable[positionBase + slot * 4] = position.X;
                    _lightTable[positionBase + slot * 4 + 1] = position.Y;
                    _lightTable[positionBase + slot * 4 + 2] = position.Z;
                    _lightTable[positionBase + slot * 4 + 3] = range;
                    // The client saturates each light's colour x power per channel: a strong light washes out toward
                    // white instead of scaling its hue, so power sets how pale a light is, not how bright.
                    var lightColor = Vector3.Min(2.0f * power * new Vector3(light.Color.X, light.Color.Y, light.Color.Z), Vector3.One)
                                     * PointLightStrength;
                    _lightTable[colorBase + slot * 4] = lightColor.X;
                    _lightTable[colorBase + slot * 4 + 1] = lightColor.Y;
                    _lightTable[colorBase + slot * 4 + 2] = lightColor.Z;
                    _lightTable[colorBase + slot * 4 + 3] = 1.0f;
                }
            }
            _commandList.UpdateBuffer(_lightTableBuffer, 0, _lightTable);
        }

        /// <summary>
        /// True while the floor under the camera belongs to a placement linked to a sub-environment (a cave or interior).
        /// The client picks the viewer's environment from the floor under the camera; re-cast only when the eye moves.
        /// </summary>
        private bool IsViewerInSubEnvironment(Vector3 eye)
        {
            if (LoadedZone == null) return false;
            if (!(Vector3.DistanceSquared(eye, _viewerFloorProbe) < 0.25f))
            {
                _viewerFloorProbe = eye;
                var floor = ZoneRaycaster.FindFloor(LoadedZone, eye, 500.0f);
                _viewerInSubEnvironment = floor != null && !string.IsNullOrEmpty(floor.EnvironmentId);
            }
            return _viewerInSubEnvironment;
        }

        /// <summary>
        /// The stronger of the model sun and moon light colors, for daylight-tinted particles.
        /// </summary>
        private static Vector3 StrongestLight(ZoneEnvironmentSettings environment)
        {
            var sun = environment.ModelSunColor;
            var moon = environment.ModelMoonColor;
            return sun.X + sun.Y + sun.Z >= moon.X + moon.Y + moon.Z ? sun : moon;
        }

        /// <summary>
        /// The weather directory whose generator layers draw for the active weather: the weather itself when the zone
        /// authors layers for it, otherwise its canonical category (clod, suny, fine, mist).
        /// </summary>
        private static string ResolveLayerWeather(List<GpuWeatherSkySubmesh> layers, string activeWeather)
        {
            foreach (var candidate in layers)
            {
                if (candidate.Layer.WeatherIds.Contains(activeWeather)) return activeWeather;
            }
            return VanaTime.GetCanonicalWeatherCategory(activeWeather);
        }

        private static Vector3 EvaluateClockScale(KeyFrameCurve?[]? curves, Vector3 scale, float dayFraction)
        {
            if (curves == null) return scale;
            float t = Math.Clamp(dayFraction, 0.0f, 1.0f);
            return new Vector3(
                curves[0]?.Evaluate(t) ?? scale.X,
                curves[1]?.Evaluate(t) ?? scale.Y,
                curves[2]?.Evaluate(t) ?? scale.Z);
        }

        /// <summary>
        /// All-or-nothing visibility of the sun or moon for its lens flare, standing in for the client's occlusion query:
        /// a ray from the eye toward the light against the zone's opaque geometry, re-cast only when the eye or the light
        /// has moved noticeably.
        /// </summary>
        private bool IsLightSourceVisible(ParticleAttachType source, Vector3 eye, Vector3 direction)
        {
            if (LoadedZone == null) return true;
            if (_lightSourceVisibility.TryGetValue(source, out var cached) &&
                Vector3.DistanceSquared(cached.Eye, eye) < 0.25f &&
                Vector3.Dot(cached.Direction, direction) > 0.99999f)
            {
                return cached.Visible;
            }

            bool visible = !ZoneRaycaster.IsOccluded(LoadedZone, eye, direction, 900.0f);
            _lightSourceVisibility[source] = (eye, direction, visible);
            return visible;
        }

        private static Vector3 EvaluateClockVector(KeyFrameCurve?[]? curves, float dayFraction)
        {
            if (curves == null) return Vector3.Zero;
            float t = Math.Clamp(dayFraction, 0.0f, 1.0f);
            return new Vector3(curves[0]?.Evaluate(t) ?? 0.0f, curves[1]?.Evaluate(t) ?? 0.0f, curves[2]?.Evaluate(t) ?? 0.0f);
        }

        /// <summary>
        /// Generator texture factor: base color (with R/G/B replaced by any time-of-day color curves), modulate-2x by
        /// the weekday and moon-phase tints, alpha scaled by the time-of-day clock curve, clamped to [0, 1].
        /// </summary>
        internal static Vector4 ComputeSkyTextureFactor(WeatherSkyLayer layer, int dayOfWeek, int moonPhaseIndex, float dayFraction)
        {
            Vector4 factor = layer.BaseColor;
            if (layer.ClockColorCurves is { } colorCurves)
            {
                float t = Math.Clamp(dayFraction, 0.0f, 1.0f);
                if (colorCurves[0] != null) factor.X = colorCurves[0]!.Evaluate(t);
                if (colorCurves[1] != null) factor.Y = colorCurves[1]!.Evaluate(t);
                if (colorCurves[2] != null) factor.Z = colorCurves[2]!.Evaluate(t);
            }
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
        /// Screen position (NDC) of one lens-flare sprite: the light source's projected position scaled along the line
        /// through the screen centre (offset 0 on the source, 0.5 at the centre, 1 opposite). Returns false when the
        /// source is behind the camera or well off-screen, where the client draws no flare.
        /// Flare layout referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer,
        /// ui/js/particleDrawer.js drawLensFlares, after xim).
        /// </summary>
        internal static bool TryComputeFlareCenter(Vector3 sourceWorld, Matrix4x4 viewProjection, float offset, out Vector2 center)
        {
            center = Vector2.Zero;
            Vector4 clip = Vector4.Transform(new Vector4(sourceWorld, 1.0f), viewProjection);
            if (clip.W <= 0.0f) return false;

            var ndc = new Vector2(clip.X / clip.W, clip.Y / clip.W);
            if (MathF.Abs(ndc.X) > 1.6f || MathF.Abs(ndc.Y) > 1.6f) return false;

            center = ndc * (1.0f - 2.0f * offset);
            return true;
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
        /// <summary>
        /// Maps display-local particle axes (raw DAT axes with X and Y flipped) onto a camera-facing frame: local X to
        /// camera left, local Y to <paramref name="up"/>, local Z to the view direction.
        /// </summary>
        private static Matrix4x4 CreateBillboardBasis(Vector3 right, Vector3 up, Vector3 forward) => new(
            -right.X, -right.Y, -right.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            forward.X, forward.Y, forward.Z, 0f,
            0f, 0f, 0f, 1f);

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

        private void CreateSubEnvironmentScenes(ZoneGeometry? zone)
        {
            foreach (var (buffer, set) in _subEnvironmentScenes.Values)
            {
                set.Dispose();
                buffer.Dispose();
            }
            _subEnvironmentScenes.Clear();
            if (zone?.EnvironmentData == null) return;

            var factory = _gd.ResourceFactory;
            foreach (string id in zone.EnvironmentData.SubEnvironmentIds)
            {
                var buffer = factory.CreateBuffer(new BufferDescription(ZoneSceneUniform.SizeInBytes, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
                var set = factory.CreateResourceSet(new ResourceSetDescription(_sceneLayout, buffer));
                _subEnvironmentScenes[id] = (buffer, set);
            }
        }

        /// <summary>
        /// Fills each sub-environment's scene uniform: the frame's camera and fog with that environment's own lights
        /// (indoor sun and ambient, no moon), as the legacy client lights each placement by its linked environment.
        /// </summary>
        private void UpdateSubEnvironmentScenes(ZoneSceneUniform sceneUniform, ZoneEnvironmentSettings environment)
        {
            var data = LoadedZone?.EnvironmentData;
            if (data == null) return;
            foreach (var (id, scene) in _subEnvironmentScenes)
            {
                var uniform = sceneUniform;
                var keyframe = data.InterpolateSubEnvironment(id, environment.TimeOfDayHours, environment.WeatherId);
                if (keyframe != null)
                {
                    var lighting = new ZoneEnvironmentSettings();
                    lighting.ApplyKeyframe(keyframe);
                    uniform.SunDirection = new Vector4(lighting.SunDirection, 0.0f);
                    uniform.SunColor = new Vector4(lighting.SunColor, 1.0f);
                    uniform.AmbientColor = new Vector4(lighting.AmbientColor, 1.0f);
                    uniform.MoonColor = new Vector4(lighting.MoonColor, 1.0f);
                }
                _commandList.UpdateBuffer(scene.Buffer, 0, ref uniform);
                _subEnvironmentUniforms[id] = uniform;
            }
        }

        /// <summary>
        /// Draws the moving platforms' parts offset by each platform's live height (a per-draw world translation in the
        /// scene uniform of the part's environment, restored afterwards).
        /// </summary>
        private void DrawMovingPlatforms(ZoneSceneUniform sceneUniform, BoundingFrustum frustum, ref int draws, ref int visible, ref int culled)
        {
            if (_platformSubmeshes.Count == 0) return;
            foreach (var submesh in _platformSubmeshes)
            {
                float offset = 0.0f;
                foreach (var platform in _platformHeights)
                {
                    if (platform.Platform.Id == submesh.PlatformId) { offset = platform.Offset; break; }
                }

                // Internal +Y is down; display space is (-X, -Y, Z).
                var shift = new Vector3(0.0f, -offset, 0.0f);
                if (!frustum.IntersectsBox(submesh.MinBounds + shift, submesh.MaxBounds + shift))
                {
                    culled++;
                    continue;
                }

                bool sub = submesh.EnvironmentId.Length > 0 && _subEnvironmentScenes.TryGetValue(submesh.EnvironmentId, out _);
                var buffer = sub ? _subEnvironmentScenes[submesh.EnvironmentId].Buffer : _sceneUniformBuffer;
                var baseUniform = sub && _subEnvironmentUniforms.TryGetValue(submesh.EnvironmentId, out var subUniform) ? subUniform : sceneUniform;
                var moved = baseUniform;
                moved.World = Matrix4x4.CreateTranslation(shift);
                _commandList.UpdateBuffer(buffer, 0, ref moved);

                _commandList.SetPipeline(submesh.IsBlend ? _terrainBlendPipeline : submesh.IsFoliage ? _cutoutPipeline : _pipeline);
                _commandList.SetGraphicsResourceSet(0, SceneSetFor(submesh));
                _commandList.SetGraphicsResourceSet(1, _textureCache.GetOrCreateResourceSet(submesh.TextureName, _activeDecodedTextures));
                _commandList.SetGraphicsResourceSet(2, submesh.LightSet ?? _noLightSet);
                _commandList.SetVertexBuffer(0, submesh.VertexBuffer);
                _commandList.SetIndexBuffer(submesh.IndexBuffer, IndexFormat.UInt16);
                _commandList.DrawIndexed(submesh.IndexCount, 1, 0, 0, 0);
                _commandList.UpdateBuffer(buffer, 0, ref baseUniform);
                draws++;
                visible++;
            }
        }

        private ResourceSet SceneSetFor(GpuSubmesh submesh) =>
            submesh.EnvironmentId.Length > 0 && _subEnvironmentScenes.TryGetValue(submesh.EnvironmentId, out var scene)
                ? scene.Set
                : _sceneResourceSet;

        private void ClearZoneSubmeshes()
        {
            for (int i = 0; i < _zoneSubmeshes.Count; i++)
            {
                _zoneSubmeshes[i].Dispose();
            }
            _zoneSubmeshes.Clear();
            foreach (var submesh in _platformSubmeshes) submesh.Dispose();
            _platformSubmeshes.Clear();
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

            for (int i = 0; i < _effectSubmeshes.Count; i++)
            {
                _effectSubmeshes[i].Dispose();
            }
            _effectSubmeshes.Clear();
            _emitters.Clear();
            _emittersByTemplate.Clear();
            _weatherRoutines = null;
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
            _weatherSkyReverseSubtractPipeline?.Dispose();
            foreach (var effectPipeline in _effectPipelines) effectPipeline?.Dispose();
            _lensFlarePipeline?.Dispose();
            _sceneResourceSet?.Dispose();
            _waterResourceSet?.Dispose();
            _sceneLayout?.Dispose();
            _textureLayout?.Dispose();
            _noLightSet?.Dispose();
            _noLightRefsBuffer?.Dispose();
            _lightTableBuffer?.Dispose();
            _lightLayout?.Dispose();
            CreateSubEnvironmentScenes(null);
            _sceneUniformBuffer?.Dispose();
            _waterUniformBuffer?.Dispose();
        }
    }
}
