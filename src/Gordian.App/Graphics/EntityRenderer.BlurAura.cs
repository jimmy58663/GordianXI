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
    /// drift scaled by the layer count. Not reproduced: the background smear (retail blurs and slightly darkens what is
    /// behind the halo), which needs the scene behind the actor as a texture.
    /// </para>
    /// </summary>
    public sealed partial class EntityRenderer
    {
        /// <summary>Shell width per unit of a ring's radius, in yalms.</summary>
        public const float AuraWidthPerRadius = 0.0005f;

        /// <summary>Largest screen drift of a ring's ghost copy, in NDC (about 3 px at 1080p), at 11 layers.</summary>
        public const float AuraGhostOffset = 0.0035f;

        /// <summary>A ring's strength per unit of its alpha byte.</summary>
        public const float AuraStrengthPerAlpha = 1.0f / 96.0f;

        /// <summary>Rim exponent per unit of the section's falloff (400 for most models).</summary>
        public const float AuraRimPerFalloff = 1.0f / 120.0f;

        /// <summary>Shimmer phase speed per unit of the section's frequency byte, in radians per second.</summary>
        public const float AuraShimmerRate = 0.6f;

        /// <summary>Draws the 0x5E blur aura of avatars and other models that carry one (on by default).</summary>
        public bool EnableBlurAura { get; set; } = true;

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
            if (_auraDraws.Count == 0) return 0;
            EnsureAuraPipeline();

            int draws = 0;
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

            var skinnedVertexLayout = new VertexLayoutDescription(
                new VertexElementDescription("Position0", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3, 0),
                new VertexElementDescription("Position1", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3, 12),
                new VertexElementDescription("Normal0", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3, 24),
                new VertexElementDescription("Normal1", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3, 36),
                new VertexElementDescription("Weights", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2, 48),
                new VertexElementDescription("Joints", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2, 56),
                new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2, 64),
                new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Byte4_Norm, 72));

            // Additive (One, One), depth tested against the scene and the actors but not written: the body hides the shell's
            // far side, the near side shows only at the outline.
            var additive = new BlendStateDescription(RgbaFloat.Black,
                new BlendAttachmentDescription(true, BlendFactor.One, BlendFactor.One, BlendFunction.Add, BlendFactor.Zero, BlendFactor.One, BlendFunction.Add));
            _auraPipeline = CreatePipeline(new[] { _sceneLayout, _textureLayout, _jointPaletteLayout }, skinnedVertexLayout, _auraShaders, additive, depthWrite: false);
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
        }
    }
}
