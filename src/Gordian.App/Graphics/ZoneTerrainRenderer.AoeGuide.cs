// src/Gordian.App/Graphics/ZoneTerrainRenderer.AoeGuide.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Gordian.Engine.Graphics;
using NeoVeldrid;
using NeoVeldrid.SPIRV;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// The area-of-effect range guide (#356): while the sub-target cursor is up for an action covering a circle around its
    /// target (<c>Gordian.Engine.Actions.AreaOfEffectGuide</c>), the zone's solid meshes are drawn a second time with a
    /// shader that marks the circle on them. The mark is worked out from each pixel's own position, so it lies on the
    /// geometry and climbs walls and stair faces, as retail's does.
    /// <para>
    /// Retail capture on local LSB (2026-11-08, issue #356; Retail Captures <c>356-*.png</c>, <c>356-field-horde-lullaby-pulse.mp4</c>):
    /// <list type="bullet">
    /// <item>Band: a violet-blue line about (89-91, 87-93, 206-214) on shaded floor (about (87,81,66)) and
    /// (96-120, 98-125, 244-255) on bright floor, with a 1-2 px soft edge, 7-8 px wide at 1440p whatever the radius (so
    /// fixed in screen pixels, not yalms), wider (11 px) where it crosses a wall at a grazing angle. Both floors fit one
    /// unlit blend, <see cref="AoeGuideBandAlpha"/> of <see cref="AoeGuideBandColor"/> over the scene (red: 0.24 x 87 +
    /// 0.76 x 91 = 90 on shaded floor, 0.24 x 170 + 0.76 x 91 = 110 on bright). Drawn
    /// <see cref="AoeGuideBandHalfWidth1440"/> pixels each side of the circle, measured with the pixel's own rate of change
    /// of distance, which widens it on grazing walls the same way.</item>
    /// <item>Fill: a 16-sided polygon pulse that grows from the centre a little past the band and fades, every
    /// <see cref="AoeGuidePulseSeconds"/>. At its front the floor goes from (87,81,66) to (79,79,104) and walls from
    /// (43,36,32) to (47,48,80): about <see cref="AoeGuidePulseAlpha"/> of <see cref="AoeGuideFillColor"/>. Vertical faces
    /// take it more strongly (<see cref="AoeGuideWallBoost"/>). Behind the front it fades back toward the centre.</item>
    /// <item>The scene outside the circle is dimmed by about 5 % (<see cref="AoeGuideOutsideDim"/>).</item>
    /// </list>
    /// Not reproduced: retail draws the band over characters standing on it too (Claude's legs at 18 yalms); this pass
    /// covers the zone's own solid meshes (not foliage cut-outs, decals, doors, moving platforms or actors).
    /// </para>
    /// </summary>
    public sealed partial class ZoneTerrainRenderer
    {
        /// <summary>Half the band's width in pixels at 1440 px high (the capture: 7-8 px wide); scaled with the target's height.</summary>
        public const float AoeGuideBandHalfWidth1440 = 3.75f;

        /// <summary>One cycle of the fill pulse (retail: it restarts every ~31.4 frames at 30 fps).</summary>
        public const float AoeGuidePulseSeconds = 1.05f;

        /// <summary>The band's blend over the scene (fitted to the shaded and bright floor readings).</summary>
        public const float AoeGuideBandAlpha = 0.76f;

        /// <summary>The colour the band blends toward.</summary>
        public static readonly Vector3 AoeGuideBandColor = new(91 / 255f, 93 / 255f, 255 / 255f);

        /// <summary>The pulse front's blend on the floor (fitted to (87,81,66) to (79,79,104)).</summary>
        public const float AoeGuidePulseAlpha = 0.25f;

        /// <summary>The colour the pulse blends toward.</summary>
        public static readonly Vector3 AoeGuideFillColor = new(55 / 255f, 73 / 255f, 218 / 255f);

        /// <summary>How much more strongly vertical faces take the pulse.</summary>
        public const float AoeGuideWallBoost = 1.6f;

        /// <summary>How much the scene outside the circle darkens while the guide is up.</summary>
        public const float AoeGuideOutsideDim = 0.05f;

        /// <summary>The guide to draw this frame: the entity it is centred on and its radius in yalms; null for none.</summary>
        public (uint CenterServerId, float Radius)? AoeGuide { get; set; }

        /// <summary>A fixed display-space centre instead of an entity's (offscreen verification).</summary>
        public Vector3? AoeGuideCenterOverride { get; set; }

        /// <summary>The pulse's phase override in [0, 1) (offscreen verification); null runs it on the clock.</summary>
        public float? AoeGuidePhaseOverride { get; set; }

        [StructLayout(LayoutKind.Sequential)]
        private struct AoeGuideParams
        {
            public Vector4 Center;     // xyz: display-space centre, w: radius (yalms)
            public Vector4 Params;     // x: pulse phase 0-1, y: band half width (px), z: outside dim, w: pulse alpha
            public Vector4 BandColor;  // rgb: band colour, a: band alpha
            public Vector4 FillColor;  // rgb: pulse colour, a: wall boost
            public const uint SizeInBytes = 64;
        }

        private const string AoeGuideVertexGlsl = @"#version 450
layout(location = 0) in vec3 Position;
layout(location = 0) out vec3 fsin_WorldPos;
layout(set = 0, binding = 0) uniform ZoneSceneUniforms
{
    mat4 World;
    mat4 View;
    mat4 Projection;
    vec4 SunDirection;
    vec4 SunColor;
    vec4 AmbientColor;
    vec4 FogColor;
    vec4 FogParams;
    vec4 EyePosition;
    vec4 WeatherParams;
    vec4 SkyTextureFactor;
    vec4 SkyLayerParams;
    vec4 MoonColor;
    vec4 TerrainTone;
};
void main()
{
    vec4 worldPos = World * vec4(Position, 1.0);
    fsin_WorldPos = worldPos.xyz;
    vec4 clipPos = Projection * View * worldPos;
    // The decal bias, so the second draw of the same triangles wins the depth compare.
    gl_Position = vec4(clipPos.xy, clipPos.z - 0.00015 * clipPos.w, clipPos.w);
}";

        private const string AoeGuideFragmentGlsl = @"#version 450
layout(location = 0) in vec3 fsin_WorldPos;
layout(location = 0) out vec4 fsout_Color;
layout(set = 1, binding = 0) uniform AoeGuideParams
{
    vec4 Center;
    vec4 Params;
    vec4 BandColor;
    vec4 FillColor;
};
const float PI = 3.14159265;
const float SIDES = 16.0;
void main()
{
    float radius = Center.w;
    vec2 offset = fsin_WorldPos.xz - Center.xz;
    float d = length(offset);
    float perPixel = max(fwidth(d), 1e-4);

    // The pulse: a 16-sided polygon whose apothem grows from the centre a little past the band, fading as it goes.
    float sector = 2.0 * PI / SIDES;
    float angle = atan(offset.y, offset.x);
    float a = mod(angle + 0.5 * sector, sector) - 0.5 * sector;
    float p = d * cos(a);
    float phase = Params.x;
    float front = phase * 1.1 * radius;
    float fill = 0.0;
    if (front > 1e-3)
    {
        float edge = 1.0 - smoothstep(front - perPixel, front + perPixel, p);
        float ramp = clamp(p / front, 0.0, 1.0);
        fill = Params.w * ramp * ramp * edge * (1.0 - smoothstep(0.7, 1.0, phase));
        vec3 faceNormal = normalize(cross(dFdx(fsin_WorldPos), dFdy(fsin_WorldPos)));
        fill = min(fill * mix(1.0, FillColor.a, 1.0 - abs(faceNormal.y)), 1.0);
    }

    // Under the band: the pulse, and outside the circle the dim (black) under what is left of the pulse.
    vec3 color = FillColor.rgb;
    float alpha = fill;
    if (d > radius)
    {
        alpha = Params.z + fill - Params.z * fill;
        color = alpha > 0.0 ? FillColor.rgb * (fill / alpha) : vec3(0.0);
    }

    // The band, a fixed number of pixels each side of the circle, soft over the last pixel.
    float px = abs(d - radius) / perPixel;
    float band = BandColor.a * (1.0 - smoothstep(Params.y - 0.75, Params.y + 0.75, px));
    float total = alpha + band - alpha * band;
    if (total <= 0.0) discard;
    fsout_Color = vec4((color * alpha * (1.0 - band) + BandColor.rgb * band) / total, total);
}";

        private ResourceLayout? _aoeGuideLayout;
        private DeviceBuffer? _aoeGuideBuffer;
        private ResourceSet? _aoeGuideSet;
        private Pipeline? _aoeGuidePipeline;
        private Shader[]? _aoeGuideShaders;
        private double _aoeGuideClock;
        private bool _aoeGuideWasShown;

        private void CreateAoeGuideResources(ResourceFactory factory)
        {
            _aoeGuideLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("AoeGuideParams", ResourceKind.UniformBuffer, ShaderStages.Fragment)));
            _aoeGuideBuffer = factory.CreateBuffer(new BufferDescription(AoeGuideParams.SizeInBytes, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            _aoeGuideSet = factory.CreateResourceSet(new ResourceSetDescription(_aoeGuideLayout, _aoeGuideBuffer));
            _aoeGuideShaders = factory.CreateFromSpirv(
                new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(AoeGuideVertexGlsl), "main"),
                new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(AoeGuideFragmentGlsl), "main"));
            // The zone's 36-byte MeshVertex, position only: an input the shader does not read is dropped from the compiled
            // shader, and the layout must not name it (as the weather-sky layout leaves out Normal).
            var vertexLayout = new VertexLayoutDescription(36,
                new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3, 0));
            _aoeGuidePipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleAlphaBlend,
                DepthStencilState = new DepthStencilStateDescription(depthTestEnabled: true, depthWriteEnabled: false, comparisonKind: ComparisonKind.LessEqual),
                RasterizerState = new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise,
                    depthClipEnabled: true, scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _sceneLayout, _aoeGuideLayout },
                ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, _aoeGuideShaders),
                Outputs = _outputs
            });
        }

        private void DisposeAoeGuideResources()
        {
            _aoeGuidePipeline?.Dispose();
            if (_aoeGuideShaders != null) foreach (var shader in _aoeGuideShaders) shader.Dispose();
            _aoeGuideSet?.Dispose();
            _aoeGuideBuffer?.Dispose();
            _aoeGuideLayout?.Dispose();
        }

        /// <summary>Where the guide is centred this frame (display space), or null when there is no guide or its entity is not drawn.</summary>
        private Vector3? AoeGuideCenter()
        {
            if (AoeGuide is not { } guide) return null;
            if (AoeGuideCenterOverride is { } fixedCenter) return fixedCenter;
            return _entityRenderer != null && _entityRenderer.ActorAnchors.TryGetValue(guide.CenterServerId, out var anchor)
                ? anchor.ModelToWorld.Translation
                : null;
        }

        /// <summary>Draws the guide over the zone's solid meshes in view; returns the draw count.</summary>
        private int DrawAoeGuide(List<GpuSubmesh> submeshes, BoundingFrustum frustum, float deltaSeconds, uint height)
        {
            if (_aoeGuidePipeline == null || AoeGuide is not { } guide || AoeGuideCenter() is not { } center)
            {
                _aoeGuideWasShown = false;
                return 0;
            }
            // The pulse starts from the centre when the guide comes up.
            if (!_aoeGuideWasShown) _aoeGuideClock = 0;
            _aoeGuideWasShown = true;
            _aoeGuideClock += Math.Clamp(deltaSeconds, 0f, 0.25f);
            float phase = AoeGuidePhaseOverride ?? (float)(_aoeGuideClock / AoeGuidePulseSeconds % 1.0);

            // The capture's 7-8 px band was at 1440 px high; keep its share of the screen at other heights, at least a pixel each side.
            float halfWidth = MathF.Max(1f, AoeGuideBandHalfWidth1440 * height / 1440f);
            var parameters = new AoeGuideParams
            {
                Center = new Vector4(center, guide.Radius),
                Params = new Vector4(phase, halfWidth, AoeGuideOutsideDim, AoeGuidePulseAlpha),
                BandColor = new Vector4(AoeGuideBandColor, AoeGuideBandAlpha),
                FillColor = new Vector4(AoeGuideFillColor, AoeGuideWallBoost),
            };
            _commandList.UpdateBuffer(_aoeGuideBuffer, 0, ref parameters);
            _commandList.SetPipeline(_aoeGuidePipeline);
            _commandList.SetGraphicsResourceSet(1, _aoeGuideSet);
            ResourceSet? currentSceneSet = null;
            int draws = 0;
            foreach (var submesh in submeshes)
            {
                // Solid meshes only: cut-outs would need their texture's alpha, and decals lie on solid meshes drawn here.
                if (submesh.Pass != ZoneSubmeshPass.Opaque) continue;
                if (submesh.SubAreaLink != 0 && IsInteriorShown(submesh.SubAreaLink)) continue;
                if (!PassesZoneVisibility(submesh) || !frustum.IntersectsBox(submesh.MinBounds, submesh.MaxBounds)) continue;
                var sceneSet = SceneSetFor(submesh);
                if (!ReferenceEquals(sceneSet, currentSceneSet))
                {
                    _commandList.SetGraphicsResourceSet(0, sceneSet);
                    currentSceneSet = sceneSet;
                }
                _commandList.SetVertexBuffer(0, submesh.VertexBuffer);
                _commandList.SetIndexBuffer(submesh.IndexBuffer, IndexFormat.UInt16);
                _commandList.DrawIndexed(submesh.IndexCount, 1, 0, 0, 0);
                draws++;
            }
            return draws;
        }
    }
}
