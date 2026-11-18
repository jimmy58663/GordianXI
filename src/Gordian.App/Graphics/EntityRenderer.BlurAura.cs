// src/Gordian.App/Graphics/EntityRenderer.BlurAura.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Gordian.Core.Resources.Models;
using NeoVeldrid;
using NeoVeldrid.SPIRV;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// The blur aura pass (#82): actors whose model carries Section 0x5E (<see cref="BlurAura"/>) get a glow that follows
    /// their silhouette, drawn after all actors. Each ring of the section draws the skinned body once more as a shell pushed
    /// out along its normals by the ring's radius, tinted with the ring's colour and strength, visible only toward the
    /// outline (a rim term whose exponent is the section's falloff) and added to the frame. Each ring is offset on screen by
    /// a small drift that cycles at the section's shimmer frequency, which gives the faint offset ghost copies seen in
    /// retail.
    /// <para>
    /// Calibrated by eye against the maintainer's retail capture <c>SMN_Avatars.mp4</c> (2026-10-17, night): Garuda's
    /// halo 10-20 px at 2550x1434 with her body about 3 yalms tall on screen (about 0.03-0.06 yalm, her rings' radii
    /// 60-130), Fenrir's rim 3-6 px further away. The client's exact use of the fields is not decoded: GordianXI's mapping
    /// is width = radius x <see cref="AuraWidthPerRadius"/>, strength = alpha / 128, rim exponent = falloff / 100, ghost
    /// drift scaled by the layer count.
    /// </para>
    /// <para>
    /// The edge blur: retail blurs the avatar's own edges, soft and a little blocky, bleeding a few pixels outward over a
    /// slightly darker band (<c>SMN_Avatars.mp4</c>, Garuda at 18 s; round 1 of 2026-11-18 rejected a smear of the
    /// background around her). When the scene is drawn offscreen (<see cref="SceneColor"/>: <c>ZoneTerrainRenderer</c> draws
    /// a frame with an aura through its post-process target, as the swapchain cannot be sampled), the pass copies the scene
    /// drawn so far, the avatar included, and draws each aura's shell once more, wider (<see cref="AuraSmearWidthScale"/> x
    /// its widest ring), showing that copy blurred on a block grid by the rim term
    /// (<see cref="BlurAuraShaders.SmearFragmentShaderGlsl"/>). Calibrated by eye, like the glow.
    /// </para>
    /// </summary>
    public sealed partial class EntityRenderer
    {
        /// <summary>Shell width per unit of a ring's radius, in yalms.</summary>
        public const float AuraWidthPerRadius = 0.00015f;

        /// <summary>Largest screen drift of a ring's ghost copy, in NDC (about 3 px at 1080p), at 11 layers.</summary>
        public const float AuraGhostOffset = 0.0012f;

        /// <summary>A ring's strength per unit of its alpha byte.</summary>
        public const float AuraStrengthPerAlpha = 1.0f / 96.0f;

        /// <summary>Rim exponent per unit of the section's falloff (400 for most models).</summary>
        public const float AuraRimPerFalloff = 1.0f / 60.0f;

        /// <summary>Shimmer phase speed per unit of the section's frequency byte, in radians per second.</summary>
        public const float AuraShimmerRate = 0.6f;

        /// <summary>Draws the 0x5E blur aura of avatars and other models that carry one (on by default).</summary>
        public bool EnableBlurAura { get; set; } = true;

        /// <summary>The smear shell's width against the aura's widest ring (the glow's own width).</summary>
        public const float AuraSmearWidthScale = 3.0f;

        /// <summary>The edge blur's radius in pixels per unit of the widest ring's radius, at 1440 lines (Garuda: 130 is 11.7 px).</summary>
        public const float AuraSmearPixelsPerRadius = 0.09f;

        /// <summary>The edge blur's block (pixelation) size against its radius.</summary>
        public const float AuraSmearBlockPerBlur = 0.3f;

        /// <summary>The edge blur's opacity per unit of the rings' mean alpha (Garuda 11: 0.69, Fenrir 16: 0.9), at most <see cref="AuraSmearMaxOpacity"/>.</summary>
        public const float AuraSmearOpacityPerAlpha = 1.0f / 16.0f;

        /// <summary>The most the edge blur covers the scene under it.</summary>
        public const float AuraSmearMaxOpacity = 0.9f;

        /// <summary>How much the edge blur darkens what it shows: 1, none. Darker values drew a hard ring at the band's outer edge offscreen.</summary>
        public const float AuraSmearDarken = 1.0f;

        /// <summary>Blurs and darkens the scene behind auras (#82) when <see cref="SceneColor"/> is given (on by default).</summary>
        public bool EnableAuraSmear { get; set; } = true;

        /// <summary>
        /// The colour target the scene is being drawn into this frame, when it can be sampled (an offscreen target), else
        /// null: no smear. Set by the zone renderer before <see cref="RenderEntities"/>.
        /// </summary>
        public Texture? SceneColor { get; set; }

        /// <summary>Whether the last frame drew a blur aura (the zone renderer then draws the next one offscreen for the smear).</summary>
        public bool DrewBlurAura { get; private set; }

        private Texture? _auraSceneCopy;
        private ResourceLayout? _auraSceneLayout;
        private ResourceSet? _auraSceneSet;
        private Pipeline? _auraSmearPipeline;
        private Shader[]? _auraSmearShaders;

        private readonly record struct AuraDraw(ZoneSceneUniform Uniform, ResourceSet Palette, GpuEntityModel Model, int HiddenWeapons, BlurAura Aura, uint ServerId);

        private readonly List<AuraDraw> _auraDraws = new();
        private Pipeline? _auraPipeline;
        private Shader[]? _auraShaders;
        private float _auraClock;

        private void QueueBlurAura(in ZoneSceneUniform uniform, ResourceSet palette, GpuEntityModel model, int hiddenWeapons, BlurAura aura, uint serverId)
        {
            if (!EnableBlurAura || aura.Rings.Count == 0) return;
            _auraDraws.Add(new AuraDraw(uniform, palette, model, hiddenWeapons, aura, serverId));
        }

        /// <summary>
        /// The smear's shell for an aura: (no drift, width, rim exponent) as <see cref="AuraRingParams"/>, its opacity and its
        /// blur radius in pixels for a target <paramref name="targetHeight"/> lines high.
        /// </summary>
        internal static (Vector4 Shell, float Opacity, float BlurPixels, float BlockPixels) AuraSmearParams(BlurAura aura, uint targetHeight)
        {
            float widest = 0f, alphas = 0f;
            foreach (var ring in aura.Rings)
            {
                widest = MathF.Max(widest, ring.Radius);
                alphas += ring.Alpha;
            }
            float meanAlpha = aura.Rings.Count > 0 ? alphas / aura.Rings.Count : 0f;
            // A soft rim (falloff / 240): the band reaches well inside the silhouette, so the edges themselves blur.
            var shell = new Vector4(0f, 0f, widest * AuraWidthPerRadius * AuraSmearWidthScale, Math.Max(aura.Falloff * AuraRimPerFalloff * 0.25f, 0.5f));
            float opacity = Math.Clamp(meanAlpha * AuraSmearOpacityPerAlpha, 0f, AuraSmearMaxOpacity);
            float blur = MathF.Max(1f, widest * AuraSmearPixelsPerRadius * targetHeight / 1440f);
            return (shell, opacity, blur, MathF.Max(1f, MathF.Round(blur * AuraSmearBlockPerBlur)));
        }

        /// <summary>The ring's shell parameters this frame: (screen drift x, y, width, rim exponent).</summary>
        internal static Vector4 AuraRingParams(BlurAura aura, int ring, float clock)
        {
            var r = aura.Rings[ring];
            float phase = clock * aura.Frequency * AuraShimmerRate + ring * 2.4f;
            float drift = AuraGhostOffset * Math.Clamp(aura.Layers / 11.0f, 0.0f, 2.0f);
            return new Vector4(MathF.Cos(phase) * drift, MathF.Sin(phase * 1.3f) * drift,
                r.Radius * AuraWidthPerRadius, Math.Max(aura.Falloff * AuraRimPerFalloff, 0.5f));
        }

        private int DrawBlurAuras(CommandList cl, float deltaSeconds)
        {
            _auraClock = (_auraClock + deltaSeconds) % 3600.0f;
            DrewBlurAura = _auraDraws.Count > 0;
            if (_auraDraws.Count == 0) return 0;
            EnsureAuraPipeline();

            int draws = 0;
            if (EnableAuraSmear && SceneColor is { } scene && PrepareAuraSceneCopy(cl, scene))
            {
                // The smear first, the glow over it.
                cl.SetPipeline(_auraSmearPipeline!);
                foreach (var draw in _auraDraws)
                {
                    var uniform = draw.Uniform;
                    var (shell, opacity, blur, block) = AuraSmearParams(draw.Aura, scene.Height);
                    if (opacity <= 0f) continue;
                    uniform.WeatherParams = shell;
                    uniform.SkyTextureFactor = new Vector4(AuraSmearDarken, AuraSmearDarken, AuraSmearDarken, opacity);
                    uniform.SkyLayerParams = new Vector4(0f, 0f, block, blur);
                    cl.UpdateBuffer(_entityUniformBuffer, 0, ref uniform);
                    cl.SetGraphicsResourceSet(0, _entityResourceSet);
                    cl.SetGraphicsResourceSet(2, draw.Palette);
                    cl.SetGraphicsResourceSet(3, _auraSceneSet!);
                    draws += DrawSubmeshes(cl, draw.Model, draw.HiddenWeapons);
                }
            }

            cl.SetPipeline(_auraPipeline!);
            foreach (var draw in _auraDraws)
            {
                var uniform = draw.Uniform;
                // Hashing the server id spreads the drift phase so two avatars do not shimmer in step.
                float clock = _auraClock + (draw.ServerId % 97) * 0.37f;
                for (int i = 0; i < draw.Aura.Rings.Count; i++)
                {
                    var ring = draw.Aura.Rings[i];
                    uniform.WeatherParams = AuraRingParams(draw.Aura, i, clock);
                    uniform.SkyTextureFactor = new Vector4(ring.Color, ring.Alpha * AuraStrengthPerAlpha);
                    uniform.SkyLayerParams = new Vector4(draw.Aura.BaseColor.X, draw.Aura.BaseColor.Y, draw.Aura.BaseColor.Z, 0.0f);
                    cl.UpdateBuffer(_entityUniformBuffer, 0, ref uniform);
                    cl.SetGraphicsResourceSet(0, _entityResourceSet);
                    cl.SetGraphicsResourceSet(2, draw.Palette);
                    draws += DrawSubmeshes(cl, draw.Model, draw.HiddenWeapons);
                }
            }
            _auraDraws.Clear();
            return draws;
        }

        private void EnsureAuraPipeline()
        {
            if (_auraPipeline != null) return;
            var factory = _gd.ResourceFactory;
            _auraShaders = factory.CreateFromSpirv(
                new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(BlurAuraShaders.SkinnedVertexShaderGlsl), "main"),
                new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(BlurAuraShaders.FragmentShaderGlsl), "main"));

            var skinnedVertexLayout = AuraVertexLayout;

            // Additive (One, One), depth tested against the scene and the actors but not written: the body hides the shell's
            // far side, the near side shows only at the outline.
            var additive = new BlendStateDescription(RgbaFloat.Black,
                new BlendAttachmentDescription(true, BlendFactor.One, BlendFactor.One, BlendFunction.Add, BlendFactor.Zero, BlendFactor.One, BlendFunction.Add));
            _auraPipeline = CreatePipeline(new[] { _sceneLayout, _textureLayout, _jointPaletteLayout }, skinnedVertexLayout, _auraShaders, additive, depthWrite: false);
        }

        /// <summary>The skinned vertex layout the aura shells are drawn with (the actors' own).</summary>
        private static VertexLayoutDescription AuraVertexLayout => new VertexLayoutDescription(
                new VertexElementDescription("Position0", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3, 0),
                new VertexElementDescription("Position1", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3, 12),
                new VertexElementDescription("Normal0", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3, 24),
                new VertexElementDescription("Normal1", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3, 36),
                new VertexElementDescription("Weights", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2, 48),
                new VertexElementDescription("Joints", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2, 56),
                new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2, 64),
                new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Byte4_Norm, 72));

        /// <summary>Copies the scene drawn so far for the smear to sample.</summary>
        private bool PrepareAuraSceneCopy(CommandList cl, Texture scene)
        {
            var factory = _gd.ResourceFactory;
            if (_auraSceneCopy == null || _auraSceneCopy.Width != scene.Width || _auraSceneCopy.Height != scene.Height || _auraSceneCopy.Format != scene.Format)
            {
                _auraSceneSet?.Dispose();
                _auraSceneCopy?.Dispose();
                _auraSceneCopy = factory.CreateTexture(TextureDescription.Texture2D(scene.Width, scene.Height, 1, 1, scene.Format, TextureUsage.Sampled));
                _auraSceneLayout ??= factory.CreateResourceLayout(new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("SceneTex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                    new ResourceLayoutElementDescription("SceneSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
                _auraSceneSet = factory.CreateResourceSet(new ResourceSetDescription(_auraSceneLayout, _auraSceneCopy, _gd.LinearSampler));
            }
            if (_auraSmearPipeline == null)
            {
                _auraSmearShaders = factory.CreateFromSpirv(
                    new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(BlurAuraShaders.SkinnedVertexShaderGlsl), "main"),
                    new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(BlurAuraShaders.SmearFragmentShaderGlsl), "main"));
                _auraSmearPipeline = CreatePipeline(new[] { _sceneLayout, _textureLayout, _jointPaletteLayout, _auraSceneLayout! }, AuraVertexLayout,
                    _auraSmearShaders, BlendStateDescription.SingleAlphaBlend, depthWrite: false);
            }
            cl.CopyTexture(scene, _auraSceneCopy);
            return true;
        }

        private void DisposeBlurAura()
        {
            _auraPipeline?.Dispose();
            _auraPipeline = null;
            if (_auraShaders != null)
            {
                foreach (var shader in _auraShaders) shader.Dispose();
                _auraShaders = null;
            }
            _auraSmearPipeline?.Dispose();
            _auraSmearPipeline = null;
            if (_auraSmearShaders != null)
            {
                foreach (var shader in _auraSmearShaders) shader.Dispose();
                _auraSmearShaders = null;
            }
            _auraSceneSet?.Dispose();
            _auraSceneSet = null;
            _auraSceneCopy?.Dispose();
            _auraSceneCopy = null;
            _auraSceneLayout?.Dispose();
            _auraSceneLayout = null;
        }
    }
}
