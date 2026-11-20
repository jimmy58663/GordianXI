// src/Gordian.App/Graphics/EntityRenderer.BlurAura.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Gordian.Core.Resources.Graphics;
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
    /// The edge blur (round 4): retail lays a soft, wide copy of the avatar's own colours around her
    /// (<c>AvatarEdgeBlur_RetailVsGordian_2026-11-19.mp4</c>, retail beside GordianXI at night): over the dark sky Garuda's
    /// wings get a bright yellow-green halo of 10-15 px at 1072 lines, over the snow her dark claws spill out as a dark,
    /// see-through copy; there is no light rim. Rounds 1 and 3 blurred the scene around her (sky and snow included) and did
    /// not match. When the scene is drawn offscreen (<see cref="SceneFramebuffer"/>: <c>ZoneTerrainRenderer</c> draws a frame
    /// with an aura through its post-process target, as the swapchain cannot be sampled), the pass copies the scene drawn so
    /// far, draws the avatars alone into a half-size layer that keeps only their own pixels from that copy (premultiplied by
    /// the aura's opacity), blurs the layer with a two-pass Gaussian, and lays it over the scene around the avatar, the body
    /// itself kept sharp but for <see cref="AuraBlurBodyMix"/> (<see cref="BlurAuraShaders"/>). The half-size layer stretched
    /// back up gives the slight blockiness the maintainer saw. Then the additive glow is not drawn: it is only the fallback
    /// where the scene cannot be sampled (the lobby). Calibrated by eye, like the glow.
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

        /// <summary>The edge blur's Gaussian sigma in pixels per unit of the widest ring's radius, at 1440 lines (Garuda: 130 is 10.4 px).</summary>
        public const float AuraBlurSigmaPerRadius = 0.08f;

        /// <summary>The edge blur's opacity per unit of the rings' mean alpha (Garuda 11: 0.69, Fenrir 16: 0.9), at most <see cref="AuraBlurMaxOpacity"/>.</summary>
        public const float AuraBlurOpacityPerAlpha = 1.0f / 16.0f;

        /// <summary>The most the edge blur covers the scene under it.</summary>
        public const float AuraBlurMaxOpacity = 0.9f;

        /// <summary>How much of the blurred copy is laid over the avatar's own body (0: the body stays sharp).</summary>
        public const float AuraBlurBodyMix = 0.3f;

        /// <summary>
        /// How much the blurred copy is strengthened where it is laid on (up to opaque): a plain blur of the silhouette is
        /// half covered at the outline and fades fast, where retail's halo is strong for several pixels out.
        /// </summary>
        public const float AuraBlurGain = 1.6f;

        /// <summary>The edge blur's layer is drawn at the scene's size divided by this.</summary>
        public const uint AuraBlurDownscale = 2;

        /// <summary>Draws the avatar edge blur (#82) when <see cref="SceneFramebuffer"/> is given (on by default).</summary>
        public bool EnableAuraSmear { get; set; } = true;

        /// <summary>
        /// The framebuffer the scene is being drawn into this frame, when its colour can be sampled (an offscreen target),
        /// else null: no edge blur. Set by the zone renderer before <see cref="RenderEntities"/>.
        /// </summary>
        public Framebuffer? SceneFramebuffer { get; set; }

        /// <summary>Whether the last frame drew a blur aura (the zone renderer then draws the next one offscreen for the edge blur).</summary>
        public bool DrewBlurAura { get; private set; }

        private Texture? _auraSceneCopy;
        private ResourceLayout? _auraSceneLayout;
        private ResourceSet? _auraSceneSet;
        private Texture? _auraLayer, _auraLayerDepth, _auraBlurH, _auraBlurV;
        private Framebuffer? _auraLayerFramebuffer, _auraBlurHFramebuffer, _auraBlurVFramebuffer;
        private ResourceLayout? _auraPostLayout;
        private ResourceSet? _auraBlurHSet, _auraBlurVSet, _auraCompositeSet;
        private DeviceBuffer? _auraBlurHParams, _auraBlurVParams, _auraCompositeParams;
        private Pipeline? _auraLayerPipeline, _auraBlurPipeline, _auraCompositePipeline;
        private Shader[]? _auraLayerShaders, _auraBlurShaders, _auraCompositeShaders;
        private OutputDescription? _auraCompositeOutputs;

        private readonly record struct AuraDraw(ZoneSceneUniform Uniform, ResourceSet Palette, GpuEntityModel Model, int HiddenWeapons, BlurAura Aura, uint ServerId, Vector4 ScreenBounds);

        private readonly List<AuraDraw> _auraDraws = new();
        private Pipeline? _auraPipeline;
        private Shader[]? _auraShaders;
        private float _auraClock;

        /// <summary>How far past its joints an actor's mesh may reach (wing tips, tail, fur), in yalms, for its screen bounds.</summary>
        public const float AuraBoundsMarginYalms = 0.75f;

        private void QueueBlurAura(in ZoneSceneUniform uniform, ResourceSet palette, GpuEntityModel model, int hiddenWeapons, BlurAura aura, uint serverId,
            SkeletonPoseEvaluator.EvaluatedPose? pose)
        {
            if (!EnableBlurAura || aura.Rings.Count == 0) return;
            _auraDraws.Add(new AuraDraw(uniform, palette, model, hiddenWeapons, aura, serverId,
                AuraScreenBounds(pose?.Translations, uniform.World, uniform.View * uniform.Projection)));
        }

        /// <summary>
        /// The NDC rectangle (min x, min y, max x, max y) an actor covers: its posed joints, each grown by
        /// <see cref="AuraBoundsMarginYalms"/> in every direction, projected. The whole screen when a point is behind the eye
        /// or there is no pose.
        /// </summary>
        internal static Vector4 AuraScreenBounds(Vector3[]? joints, Matrix4x4 world, Matrix4x4 viewProjection)
        {
            var full = new Vector4(-1f, -1f, 1f, 1f);
            if (joints == null || joints.Length == 0) return full;
            float m = AuraBoundsMarginYalms;
            var min = new Vector2(float.MaxValue);
            var max = new Vector2(float.MinValue);
            foreach (var joint in joints)
            {
                var centre = Vector3.Transform(joint, world);
                for (int corner = 0; corner < 8; corner++)
                {
                    var p = centre + new Vector3((corner & 1) != 0 ? m : -m, (corner & 2) != 0 ? m : -m, (corner & 4) != 0 ? m : -m);
                    var clip = Vector4.Transform(new Vector4(p, 1f), viewProjection);
                    if (clip.W <= 0.01f) return full;
                    var ndc = new Vector2(clip.X, clip.Y) / clip.W;
                    min = Vector2.Min(min, ndc);
                    max = Vector2.Max(max, ndc);
                }
            }
            return new Vector4(Math.Clamp(min.X, -1f, 1f), Math.Clamp(min.Y, -1f, 1f), Math.Clamp(max.X, -1f, 1f), Math.Clamp(max.Y, -1f, 1f));
        }

        /// <summary>
        /// The pixel rectangle (x, y, width, height; rows from the top) of an NDC rectangle on a <paramref name="width"/> x
        /// <paramref name="height"/> target, grown by <paramref name="padPixels"/>; empty when it is off the target.
        /// </summary>
        internal static (uint X, uint Y, uint Width, uint Height) AuraPixelRect(Vector4 ndc, uint width, uint height, float padPixels, bool clipYInverted)
        {
            float x0 = (ndc.X + 1f) * 0.5f * width - padPixels;
            float x1 = (ndc.Z + 1f) * 0.5f * width + padPixels;
            float top = clipYInverted ? (ndc.Y + 1f) * 0.5f : (1f - ndc.W) * 0.5f;
            float bottom = clipYInverted ? (ndc.W + 1f) * 0.5f : (1f - ndc.Y) * 0.5f;
            float y0 = top * height - padPixels, y1 = bottom * height + padPixels;
            uint ix0 = (uint)Math.Clamp(MathF.Floor(x0), 0f, width), ix1 = (uint)Math.Clamp(MathF.Ceiling(x1), 0f, width);
            uint iy0 = (uint)Math.Clamp(MathF.Floor(y0), 0f, height), iy1 = (uint)Math.Clamp(MathF.Ceiling(y1), 0f, height);
            return ix1 <= ix0 || iy1 <= iy0 ? (0, 0, 0, 0) : (ix0, iy0, ix1 - ix0, iy1 - iy0);
        }

        /// <summary>
        /// The edge blur of an aura: its opacity and its Gaussian sigma in pixels for a target <paramref name="targetHeight"/>
        /// lines high.
        /// </summary>
        internal static (float Opacity, float SigmaPixels) AuraBlurParams(BlurAura aura, uint targetHeight)
        {
            float widest = 0f, alphas = 0f;
            foreach (var ring in aura.Rings)
            {
                widest = MathF.Max(widest, ring.Radius);
                alphas += ring.Alpha;
            }
            float meanAlpha = aura.Rings.Count > 0 ? alphas / aura.Rings.Count : 0f;
            float opacity = Math.Clamp(meanAlpha * AuraBlurOpacityPerAlpha, 0f, AuraBlurMaxOpacity);
            float sigma = MathF.Max(1f, widest * AuraBlurSigmaPerRadius * targetHeight / 1440f);
            return (opacity, sigma);
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
            if (EnableAuraSmear && SceneFramebuffer is { } scene && scene.ColorTargets.Count > 0)
            {
                // The edge blur alone: retail draws no light rim, so the glow is only the fallback where the scene cannot be
                // sampled (the lobby).
                draws += DrawAuraEdgeBlur(cl, scene);
                _auraDraws.Clear();
                return draws;
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

        [StructLayout(LayoutKind.Sequential)]
        private struct AuraPostParams
        {
            public Vector4 Params;
        }

        /// <summary>
        /// The edge blur (#82 round 4): the avatars alone into a half-size layer (their own pixels of the scene drawn so far,
        /// premultiplied by the aura's opacity), a two-pass Gaussian of it, and the blurred layer laid over the scene.
        /// Leaves <paramref name="scene"/> bound.
        /// </summary>
        private int DrawAuraEdgeBlur(CommandList cl, Framebuffer scene)
        {
            var color = scene.ColorTargets[0].Target;
            // Only the screen the avatars cover, grown by the blur's reach, is copied, blurred and laid over (round 5: several
            // summoners' avatars share the one layer and blur; a far one costs only its own rectangle).
            float reach = 1f;
            var ndc = new Vector4(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
            foreach (var draw in _auraDraws)
            {
                reach = MathF.Max(reach, 3f * AuraBlurParams(draw.Aura, color.Height).SigmaPixels);
                ndc = new Vector4(MathF.Min(ndc.X, draw.ScreenBounds.X), MathF.Min(ndc.Y, draw.ScreenBounds.Y),
                    MathF.Max(ndc.Z, draw.ScreenBounds.Z), MathF.Max(ndc.W, draw.ScreenBounds.W));
            }
            var rect = AuraPixelRect(ndc, color.Width, color.Height, reach, _gd.IsClipSpaceYInverted);
            if (rect.Width == 0) return 0;

            EnsureAuraEdgeBlurResources(scene, color);
            cl.CopyTexture(color, rect.X, rect.Y, 0, 0, 0, _auraSceneCopy!, rect.X, rect.Y, 0, 0, 0, rect.Width, rect.Height, 1, 1);

            float sigma = 1f, maxOpacity = 0f;
            int draws = 0;
            cl.SetFramebuffer(_auraLayerFramebuffer!);
            cl.SetFullViewports();
            cl.SetFullScissorRects();
            cl.ClearColorTarget(0, RgbaFloat.Clear);
            cl.ClearDepthStencil(1f);
            cl.SetPipeline(_auraLayerPipeline!);
            foreach (var draw in _auraDraws)
            {
                var (opacity, drawSigma) = AuraBlurParams(draw.Aura, color.Height);
                if (opacity <= 0f) continue;
                sigma = MathF.Max(sigma, drawSigma);
                maxOpacity = MathF.Max(maxOpacity, opacity);
                var uniform = draw.Uniform;
                uniform.WeatherParams = new Vector4(0f, 0f, 0f, 1f);
                uniform.SkyTextureFactor = new Vector4(1f, 1f, 1f, opacity);
                uniform.SkyLayerParams = new Vector4(AuraBlurDownscale, 0f, 0f, 0f);
                cl.UpdateBuffer(_entityUniformBuffer, 0, ref uniform);
                cl.SetGraphicsResourceSet(0, _entityResourceSet);
                cl.SetGraphicsResourceSet(2, draw.Palette);
                cl.SetGraphicsResourceSet(3, _auraSceneSet!);
                draws += DrawSubmeshes(cl, draw.Model, draw.HiddenWeapons);
            }

            if (maxOpacity > 0f)
            {
                float layerSigma = sigma / AuraBlurDownscale;
                var h = new AuraPostParams { Params = new Vector4(1f, 0f, layerSigma, 0f) };
                var v = new AuraPostParams { Params = new Vector4(0f, 1f, layerSigma, AuraBlurGain) };
                var c = new AuraPostParams { Params = new Vector4(color.Width, color.Height, maxOpacity, AuraBlurBodyMix) };
                cl.UpdateBuffer(_auraBlurHParams!, 0, ref h);
                cl.UpdateBuffer(_auraBlurVParams!, 0, ref v);
                cl.UpdateBuffer(_auraCompositeParams!, 0, ref c);

                uint d = AuraBlurDownscale;
                uint hx = rect.X / d, hy = rect.Y / d;
                uint hw = Math.Min((rect.Width + d - 1) / d + 1, _auraBlurH!.Width - hx), hh = Math.Min((rect.Height + d - 1) / d + 1, _auraBlurH.Height - hy);
                cl.SetFramebuffer(_auraBlurHFramebuffer!);
                cl.SetFullViewports();
                cl.SetScissorRect(0, hx, hy, hw, hh);
                cl.SetPipeline(_auraBlurPipeline!);
                cl.SetGraphicsResourceSet(0, _auraBlurHSet!);
                cl.Draw(3);
                cl.SetFramebuffer(_auraBlurVFramebuffer!);
                cl.SetFullViewports();
                cl.SetScissorRect(0, hx, hy, hw, hh);
                cl.SetGraphicsResourceSet(0, _auraBlurVSet!);
                cl.Draw(3);
                draws += 2;
            }

            cl.SetFramebuffer(scene);
            cl.SetFullViewports();
            cl.SetFullScissorRects();
            if (maxOpacity > 0f)
            {
                cl.SetPipeline(_auraCompositePipeline!);
                cl.SetScissorRect(0, rect.X, rect.Y, rect.Width, rect.Height);
                cl.SetGraphicsResourceSet(0, _auraCompositeSet!);
                cl.Draw(3);
                cl.SetFullScissorRects();
                draws++;
            }
            return draws;
        }

        /// <summary>Creates (or resizes) the scene copy, the half-size layer and blur targets and the edge blur's pipelines.</summary>
        private void EnsureAuraEdgeBlurResources(Framebuffer scene, Texture color)
        {
            var factory = _gd.ResourceFactory;
            if (_auraSceneCopy == null || _auraSceneCopy.Width != color.Width || _auraSceneCopy.Height != color.Height || _auraSceneCopy.Format != color.Format)
            {
                DisposeAuraEdgeBlurTargets();
                _auraSceneCopy = factory.CreateTexture(TextureDescription.Texture2D(color.Width, color.Height, 1, 1, color.Format, TextureUsage.Sampled));
                _auraSceneLayout ??= factory.CreateResourceLayout(new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("SceneTex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                    new ResourceLayoutElementDescription("SceneSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
                _auraSceneSet = factory.CreateResourceSet(new ResourceSetDescription(_auraSceneLayout, _auraSceneCopy, _gd.LinearSampler));

                uint w = Math.Max(1u, color.Width / AuraBlurDownscale), hgt = Math.Max(1u, color.Height / AuraBlurDownscale);
                const PixelFormat layerFormat = PixelFormat.R8_G8_B8_A8_UNorm;
                _auraLayer = factory.CreateTexture(TextureDescription.Texture2D(w, hgt, 1, 1, layerFormat, TextureUsage.RenderTarget | TextureUsage.Sampled));
                _auraLayerDepth = factory.CreateTexture(TextureDescription.Texture2D(w, hgt, 1, 1, PixelFormat.R32_Float, TextureUsage.DepthStencil));
                _auraBlurH = factory.CreateTexture(TextureDescription.Texture2D(w, hgt, 1, 1, layerFormat, TextureUsage.RenderTarget | TextureUsage.Sampled));
                _auraBlurV = factory.CreateTexture(TextureDescription.Texture2D(w, hgt, 1, 1, layerFormat, TextureUsage.RenderTarget | TextureUsage.Sampled));
                _auraLayerFramebuffer = factory.CreateFramebuffer(new FramebufferDescription(_auraLayerDepth, _auraLayer));
                _auraBlurHFramebuffer = factory.CreateFramebuffer(new FramebufferDescription(null, _auraBlurH));
                _auraBlurVFramebuffer = factory.CreateFramebuffer(new FramebufferDescription(null, _auraBlurV));

                _auraPostLayout ??= factory.CreateResourceLayout(new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("AuraPostParams", ResourceKind.UniformBuffer, ShaderStages.Fragment),
                    new ResourceLayoutElementDescription("TexA", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                    new ResourceLayoutElementDescription("TexB", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                    new ResourceLayoutElementDescription("PostSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
                _auraBlurHParams ??= factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
                _auraBlurVParams ??= factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
                _auraCompositeParams ??= factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
                _auraBlurHSet = factory.CreateResourceSet(new ResourceSetDescription(_auraPostLayout, _auraBlurHParams, _auraLayer, _auraLayer, _gd.LinearSampler));
                _auraBlurVSet = factory.CreateResourceSet(new ResourceSetDescription(_auraPostLayout, _auraBlurVParams, _auraBlurH, _auraBlurH, _gd.LinearSampler));
                _auraCompositeSet = factory.CreateResourceSet(new ResourceSetDescription(_auraPostLayout, _auraCompositeParams, _auraBlurV, _auraLayer, _gd.LinearSampler));
            }

            if (_auraLayerPipeline == null)
            {
                _auraLayerShaders = factory.CreateFromSpirv(
                    new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(BlurAuraShaders.SkinnedVertexShaderGlsl), "main"),
                    new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(BlurAuraShaders.LayerFragmentShaderGlsl), "main"));
                _auraLayerPipeline = CreatePipeline(new[] { _sceneLayout, _textureLayout, _jointPaletteLayout, _auraSceneLayout! }, AuraVertexLayout,
                    _auraLayerShaders, BlendStateDescription.SingleOverrideBlend, depthWrite: true, _auraLayerFramebuffer!.OutputDescription);
            }
            if (_auraBlurPipeline == null)
            {
                _auraBlurShaders = factory.CreateFromSpirv(
                    new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(BlurAuraShaders.FullscreenVertexShaderGlsl), "main"),
                    new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(BlurAuraShaders.BlurFragmentShaderGlsl), "main"));
                _auraBlurPipeline = CreateAuraPostPipeline(_auraBlurShaders, BlendStateDescription.SingleOverrideBlend, _auraBlurHFramebuffer!.OutputDescription);
            }
            var sceneOutputs = scene.OutputDescription;
            if (_auraCompositePipeline == null || !_auraCompositeOutputs.Equals(sceneOutputs))
            {
                _auraCompositePipeline?.Dispose();
                _auraCompositeShaders ??= factory.CreateFromSpirv(
                    new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(BlurAuraShaders.FullscreenVertexShaderGlsl), "main"),
                    new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(BlurAuraShaders.CompositeFragmentShaderGlsl), "main"));
                // Premultiplied alpha over the scene.
                var over = new BlendStateDescription(RgbaFloat.Black,
                    new BlendAttachmentDescription(true, BlendFactor.One, BlendFactor.InverseSourceAlpha, BlendFunction.Add, BlendFactor.Zero, BlendFactor.One, BlendFunction.Add));
                _auraCompositePipeline = CreateAuraPostPipeline(_auraCompositeShaders, over, sceneOutputs);
                _auraCompositeOutputs = sceneOutputs;
            }
        }

        private Pipeline CreateAuraPostPipeline(Shader[] shaders, BlendStateDescription blend, OutputDescription outputs) =>
            _gd.ResourceFactory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = blend,
                DepthStencilState = DepthStencilStateDescription.Disabled,
                RasterizerState = new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, depthClipEnabled: true, scissorTestEnabled: true),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _auraPostLayout! },
                ShaderSet = new ShaderSetDescription(Array.Empty<VertexLayoutDescription>(), shaders),
                Outputs = outputs
            });

        private void DisposeAuraEdgeBlurTargets()
        {
            _auraBlurHSet?.Dispose(); _auraBlurHSet = null;
            _auraBlurVSet?.Dispose(); _auraBlurVSet = null;
            _auraCompositeSet?.Dispose(); _auraCompositeSet = null;
            _auraLayerFramebuffer?.Dispose(); _auraLayerFramebuffer = null;
            _auraBlurHFramebuffer?.Dispose(); _auraBlurHFramebuffer = null;
            _auraBlurVFramebuffer?.Dispose(); _auraBlurVFramebuffer = null;
            _auraLayer?.Dispose(); _auraLayer = null;
            _auraLayerDepth?.Dispose(); _auraLayerDepth = null;
            _auraBlurH?.Dispose(); _auraBlurH = null;
            _auraBlurV?.Dispose(); _auraBlurV = null;
            _auraSceneSet?.Dispose(); _auraSceneSet = null;
            _auraSceneCopy?.Dispose(); _auraSceneCopy = null;
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
            foreach (var pipeline in new[] { _auraLayerPipeline, _auraBlurPipeline, _auraCompositePipeline }) pipeline?.Dispose();
            _auraLayerPipeline = _auraBlurPipeline = _auraCompositePipeline = null;
            foreach (var set in new[] { _auraLayerShaders, _auraBlurShaders, _auraCompositeShaders })
                if (set != null) foreach (var shader in set) shader.Dispose();
            _auraLayerShaders = _auraBlurShaders = _auraCompositeShaders = null;
            DisposeAuraEdgeBlurTargets();
            _auraBlurHParams?.Dispose(); _auraBlurHParams = null;
            _auraBlurVParams?.Dispose(); _auraBlurVParams = null;
            _auraCompositeParams?.Dispose(); _auraCompositeParams = null;
            _auraPostLayout?.Dispose(); _auraPostLayout = null;
            _auraSceneLayout?.Dispose();
            _auraSceneLayout = null;
        }
    }
}
