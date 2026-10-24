// src/Gordian.App/Graphics/EntityRenderer.Shadow.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using NeoVeldrid;
using NeoVeldrid.SPIRV;

namespace Gordian.App.Graphics
{
    /// <summary>Which actor shadows are drawn: the in-game Config &gt; Misc. "Shadows" row (StockUiSettingKey.Shadows).</summary>
    public enum ActorShadowMode
    {
        /// <summary>Every actor casts its silhouette.</summary>
        High = 0,

        /// <summary>The local player casts its silhouette; other PCs, NPCs and monsters a plain dark circle (image-12.png).</summary>
        Normal = 1,

        /// <summary>No actor shadows.</summary>
        Off = 2
    }

    /// <summary>
    /// Actor shadows (#7, rounds 1-2). A silhouette shadow is the skinned body flattened onto the plane at the actor's feet
    /// along the light it is lit by (the sun, the moon when the sun is down, or an interior's light); a blob shadow is a
    /// soft disc at the feet. Retail draws both: silhouettes in the Bibiki Bay captures (2026-10-17: legs and sword visible)
    /// and on the High setting, circles for others on Normal (maintainer's captures <c>image-11.png</c>,
    /// <c>image-12.png</c>, TESTING-2026-10-18 round 2), where the shadow leaves the floor at about half its brightness
    /// (<see cref="ShadowOpacity"/>; 33 vs 67 and 26 vs 46 in the two captures).
    /// <para>
    /// Drawing: round 1 blended the silhouette directly and wrote depth so overlapping parts darkened once; the coplanar
    /// depths flickered, and ground overlays drawn later (Bibiki Bay's world-effect layers) covered the shadow (round 2
    /// F11, <c>image-14.png</c>). Now, after the water and world-effect passes (<c>ZoneTerrainRenderer</c> calls
    /// <see cref="DrawQueuedShadows"/>), every shadow is drawn three times without writing depth: first resetting the colour
    /// target's alpha to 1 where it falls, then keeping the smallest "light left" (1 - opacity) in that alpha, then
    /// multiplying the colour by it and restoring the alpha to 1. A pixel covered by several body parts or actors darkens
    /// once, by the darkest shadow over it. The shadow is pulled <see cref="ShadowEyePull"/> toward the camera so it stays
    /// over ground rising or dipping around the feet. Actors with the server's ShadowFlag (<c>WorldEntity.HidesShadow</c>)
    /// or an event fade cast none.
    /// </para>
    /// </summary>
    public sealed partial class EntityRenderer
    {
        /// <summary>How much a shadow darkens the ground: the floor under it keeps half its brightness (image-11/12.png).</summary>
        public const float ShadowOpacity = 0.5f;

        /// <summary>How dark a Normal-setting circle is at its centre (it fades toward the rim).</summary>
        public const float ShadowDiscOpacity = 0.45f;

        /// <summary>A circle's radius as a share of the actor's height (overhead point), clamped to 0.25-2.5 yalms.</summary>
        public const float ShadowDiscRadiusPerHeight = 0.22f;

        /// <summary>The light is never taken lower than this elevation (sine), so shadows stay at most 1.33x the body height (retail Valkurm 16:26, image-6.png: short shadows with the sun at 0.40).</summary>
        public const float ShadowMinElevation = 0.6f;

        /// <summary>
        /// How far (yalms) the flattened shadow is pulled toward the camera, so it shows over ground up to that far above
        /// or below the plane at the feet instead of sinking into dunes and slopes.
        /// </summary>
        public const float ShadowEyePull = 0.4f;

        /// <summary>Which shadows are drawn (Config &gt; Misc. "Shadows"; High until the setting is read).</summary>
        public ActorShadowMode ShadowMode { get; set; } = ActorShadowMode.High;

        /// <summary>Draws actor shadows at all (offscreen checks turn it off).</summary>
        public bool EnableActorShadows { get; set; } = true;

        private readonly record struct ShadowDraw(ZoneSceneUniform Uniform, ResourceSet? Palette, GpuEntityModel? Model, int HiddenWeapons);

        private readonly List<ShadowDraw> _shadowDraws = new();
        private Pipeline?[]? _silhouettePipelines;
        private Pipeline?[]? _discPipelines;
        private Shader[]? _silhouetteShaders;
        private Shader[]? _discShaders;
        private DeviceBuffer? _discVertices;
        private uint _discVertexCount;

        /// <summary>
        /// The direction a shadow is cast along (toward the light, display space, unit length): the sun while it is up,
        /// else the moon (opposite the sun), raised to at least <see cref="ShadowMinElevation"/>.
        /// </summary>
        internal static Vector3 ShadowLightDirection(Vector3 sunDirection)
        {
            var l = sunDirection.LengthSquared() > 1e-8f ? Vector3.Normalize(sunDirection) : Vector3.UnitY;
            if (l.Y < 0.0f) l = -l;
            if (l.Y < ShadowMinElevation)
            {
                var flat = new Vector2(l.X, l.Z);
                float len = flat.Length();
                float horizontal = MathF.Sqrt(1.0f - ShadowMinElevation * ShadowMinElevation);
                l = len > 1e-6f
                    ? new Vector3(l.X / len * horizontal, ShadowMinElevation, l.Z / len * horizontal)
                    : Vector3.UnitY;
            }
            return l;
        }

        /// <summary>Whether an actor casts its silhouette (true), a circle (false) or nothing (null) under a mode.</summary>
        internal static bool? CastsSilhouette(ActorShadowMode mode, bool isLocalPlayer) => mode switch
        {
            ActorShadowMode.Off => null,
            ActorShadowMode.Normal => isLocalPlayer,
            _ => true
        };

        /// <summary>
        /// How fast an actor's shadow follows a change of its light (seconds to close about two thirds of the way): walking
        /// into or out of a building swaps the light from the street's sun to the room's, and retail eases the shadow's
        /// length between them (#68 round 1: ours snapped).
        /// </summary>
        public const float ShadowLightEaseSeconds = 0.6f;

        private readonly Dictionary<uint, Vector3> _shadowLights = new();

        /// <summary>Eases an actor's shadow light toward <paramref name="target"/> (both unit length).</summary>
        internal static Vector3 EaseShadowLight(Vector3 current, Vector3 target, float deltaSeconds)
        {
            float t = 1.0f - MathF.Exp(-Math.Max(0.0f, deltaSeconds) / ShadowLightEaseSeconds);
            var eased = Vector3.Lerp(current, target, t);
            return eased.LengthSquared() > 1e-8f ? Vector3.Normalize(eased) : target;
        }

        private void QueueShadow(in ZoneSceneUniform uniform, ResourceSet? palette, GpuEntityModel model, int hiddenWeapons,
            Vector3 feet, bool isLocalPlayer, float actorHeight, uint serverId, float deltaSeconds)
        {
            if (!EnableActorShadows) return;
            bool? silhouette = CastsSilhouette(ShadowMode, isLocalPlayer);
            if (silhouette == null) return;
            var shadow = uniform;
            var l = ShadowLightDirection(new Vector3(uniform.SunDirection.X, uniform.SunDirection.Y, uniform.SunDirection.Z));
            if (_shadowLights.TryGetValue(serverId, out var previous)) l = EaseShadowLight(previous, l, deltaSeconds);
            _shadowLights[serverId] = l;
            shadow.SunDirection = new Vector4(l, feet.Y);
            if (silhouette == true && palette != null)
            {
                shadow.WeatherParams = new Vector4(ShadowOpacity, ShadowEyePull, 0.0f, 0.0f);
                _shadowDraws.Add(new ShadowDraw(shadow, palette, model, hiddenWeapons));
            }
            else
            {
                float radius = Math.Clamp(actorHeight * ShadowDiscRadiusPerHeight, 0.25f, 2.5f);
                shadow.World = Matrix4x4.CreateTranslation(feet);
                shadow.WeatherParams = new Vector4(ShadowDiscOpacity, ShadowEyePull, 0.0f, radius);
                _shadowDraws.Add(new ShadowDraw(shadow, null, null, 0));
            }
        }

        /// <summary>
        /// Draws the shadows queued by the last <see cref="RenderEntities"/> (called after the zone's water and world-effect
        /// passes, so ground overlays do not cover them) and clears the queue. Returns the draw calls made.
        /// </summary>
        public int DrawQueuedShadows(CommandList cl)
        {
            if (_shadowDraws.Count == 0) return 0;
            EnsureShadowPipelines();
            int draws = 0;
            // 0: alpha := 1 under every shadow; 1: alpha := min(alpha, light left); 2: colour *= alpha, alpha := 1.
            for (int stage = 0; stage < 3; stage++)
            {
                foreach (var draw in _shadowDraws)
                {
                    var uniform = draw.Uniform;
                    uniform.WeatherParams.Z = stage == 1 ? 1.0f : 0.0f;
                    cl.UpdateBuffer(_entityUniformBuffer, 0, ref uniform);
                    if (draw.Model != null && draw.Palette != null)
                    {
                        cl.SetPipeline(_silhouettePipelines![stage]!);
                        cl.SetGraphicsResourceSet(0, _entityResourceSet);
                        cl.SetGraphicsResourceSet(2, draw.Palette);
                        draws += DrawSubmeshes(cl, draw.Model, draw.HiddenWeapons);
                    }
                    else
                    {
                        cl.SetPipeline(_discPipelines![stage]!);
                        cl.SetGraphicsResourceSet(0, _entityResourceSet);
                        cl.SetVertexBuffer(0, _discVertices!);
                        cl.Draw(_discVertexCount);
                        draws++;
                    }
                }
            }
            _shadowDraws.Clear();
            return draws;
        }

        private static readonly BlendStateDescription[] ShadowStages =
        {
            // Reset: colour untouched, alpha := 1.
            new(RgbaFloat.Black, new BlendAttachmentDescription(true, BlendFactor.Zero, BlendFactor.One, BlendFunction.Add, BlendFactor.One, BlendFactor.Zero, BlendFunction.Add)),
            // Mask: colour untouched, alpha := min(alpha, light left).
            new(RgbaFloat.Black, new BlendAttachmentDescription(true, BlendFactor.Zero, BlendFactor.One, BlendFunction.Add, BlendFactor.One, BlendFactor.One, BlendFunction.Minimum)),
            // Apply: colour *= destination alpha, alpha := 1 (so a second shadow over the same pixel multiplies by 1).
            new(RgbaFloat.Black, new BlendAttachmentDescription(true, BlendFactor.Zero, BlendFactor.DestinationAlpha, BlendFunction.Add, BlendFactor.One, BlendFactor.Zero, BlendFunction.Add)),
        };

        private Pipeline CreateShadowPipeline(ResourceLayout[] layouts, VertexLayoutDescription vertexLayout, Shader[] shaders, BlendStateDescription blend) =>
            _gd.ResourceFactory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = blend,
                DepthStencilState = new DepthStencilStateDescription(depthTestEnabled: true, depthWriteEnabled: false, comparisonKind: ComparisonKind.LessEqual),
                RasterizerState = new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, depthClipEnabled: true, scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = layouts,
                ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, shaders),
                Outputs = _outputs
            });

        private void EnsureShadowPipelines()
        {
            if (_silhouettePipelines != null) return;
            var factory = _gd.ResourceFactory;
            _silhouetteShaders = factory.CreateFromSpirv(
                new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(ActorShadowShaders.SkinnedVertexShaderGlsl), "main"),
                new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(ActorShadowShaders.FragmentShaderGlsl), "main"));
            _discShaders = factory.CreateFromSpirv(
                new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(ActorShadowShaders.DiscVertexShaderGlsl), "main"),
                new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(ActorShadowShaders.DiscFragmentShaderGlsl), "main"));

            var skinnedVertexLayout = new VertexLayoutDescription(
                new VertexElementDescription("Position0", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3, 0),
                new VertexElementDescription("Position1", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3, 12),
                new VertexElementDescription("Normal0", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3, 24),
                new VertexElementDescription("Normal1", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3, 36),
                new VertexElementDescription("Weights", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2, 48),
                new VertexElementDescription("Joints", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2, 56),
                new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2, 64),
                new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Byte4_Norm, 72));
            var discVertexLayout = new VertexLayoutDescription(
                new VertexElementDescription("Corner", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2, 0));

            _silhouettePipelines = new Pipeline?[3];
            _discPipelines = new Pipeline?[3];
            for (int stage = 0; stage < 3; stage++)
            {
                _silhouettePipelines[stage] = CreateShadowPipeline(new[] { _sceneLayout, _textureLayout, _jointPaletteLayout }, skinnedVertexLayout, _silhouetteShaders, ShadowStages[stage]);
                _discPipelines[stage] = CreateShadowPipeline(new[] { _sceneLayout }, discVertexLayout, _discShaders, ShadowStages[stage]);
            }

            // A 24-sided disc of unit radius as a triangle list.
            const int segments = 24;
            var corners = new Vector2[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * MathF.Tau / segments, a1 = (i + 1) * MathF.Tau / segments;
                corners[i * 3] = Vector2.Zero;
                corners[i * 3 + 1] = new Vector2(MathF.Cos(a0), MathF.Sin(a0));
                corners[i * 3 + 2] = new Vector2(MathF.Cos(a1), MathF.Sin(a1));
            }
            _discVertexCount = (uint)corners.Length;
            _discVertices = factory.CreateBuffer(new BufferDescription((uint)(corners.Length * 8), BufferUsage.VertexBuffer));
            _gd.UpdateBuffer(_discVertices, 0, corners);
        }

        private void DisposeShadows()
        {
            foreach (var set in new[] { _silhouettePipelines, _discPipelines })
            {
                if (set == null) continue;
                foreach (var pipeline in set) pipeline?.Dispose();
            }
            _silhouettePipelines = null;
            _discPipelines = null;
            foreach (var shaders in new[] { _silhouetteShaders, _discShaders })
            {
                if (shaders == null) continue;
                foreach (var shader in shaders) shader.Dispose();
            }
            _silhouetteShaders = null;
            _discShaders = null;
            _discVertices?.Dispose();
            _discVertices = null;
        }
    }
}
