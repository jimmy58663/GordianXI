// src/Gordian.App/Graphics/EntityRenderer.Shadow.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using NeoVeldrid;
using NeoVeldrid.SPIRV;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Actor shadows (#7 round 1, F1): each skinned actor's body is flattened onto the ground plane at its feet along the
    /// light it is lit by (the sun, the moon when the sun is down, or an indoor environment's light) and drawn as a dark
    /// translucent silhouette after all actors. Retail draws such a silhouette (Bibiki Bay captures, 2026-10-17: legs and
    /// sword visible in the shadow) and in the maintainer's Valkurm capture <c>image-6.png</c> (16:26, round 1 notes
    /// TESTING-2026-10-18) a Tarutaru's shadow darkens the sand to about 0.72 of its lit value, hence
    /// <see cref="ShadowOpacity"/>. The shadow pass writes depth at the plane so overlapping body parts darken only once;
    /// it is a plane at the feet pulled toward the camera by <see cref="ShadowEyePull"/>, so it rides over gentle slopes; on steep ground part of it still sinks in. Actors with the server's ShadowFlag
    /// (<c>WorldEntity.HidesShadow</c>) or an event fade cast none.
    /// </summary>
    public sealed partial class EntityRenderer
    {
        /// <summary>How much a shadow darkens the ground (0.28: the sand at 0.72 of its lit value).</summary>
        public const float ShadowOpacity = 0.28f;

        /// <summary>The light is never taken lower than this elevation (sine), so shadows stay at most 1.33x the body height (retail Valkurm 16:26, image-6.png: short shadows with the sun at 0.40).</summary>
        public const float ShadowMinElevation = 0.6f;

        /// <summary>
        /// How far (yalms) the flattened shadow is pulled toward the camera, so it shows over ground up to that far above
        /// or below the plane at the feet instead of sinking into dunes and slopes.
        /// </summary>
        public const float ShadowEyePull = 0.4f;

        /// <summary>Draws actor shadows (on by default).</summary>
        public bool EnableActorShadows { get; set; } = true;

        private readonly record struct ShadowDraw(ZoneSceneUniform Uniform, ResourceSet Palette, GpuEntityModel Model, int HiddenWeapons);

        private readonly List<ShadowDraw> _shadowDraws = new();
        private Pipeline? _shadowPipeline;
        private Shader[]? _shadowShaders;

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

        private void QueueShadow(in ZoneSceneUniform uniform, ResourceSet palette, GpuEntityModel model, int hiddenWeapons, float groundHeight)
        {
            if (!EnableActorShadows) return;
            var shadow = uniform;
            var l = ShadowLightDirection(new Vector3(uniform.SunDirection.X, uniform.SunDirection.Y, uniform.SunDirection.Z));
            shadow.SunDirection = new Vector4(l, groundHeight);
            shadow.WeatherParams = new Vector4(ShadowOpacity, ShadowEyePull, 0.0f, 0.0f);
            _shadowDraws.Add(new ShadowDraw(shadow, palette, model, hiddenWeapons));
        }

        private int DrawShadows(CommandList cl)
        {
            if (_shadowDraws.Count == 0) return 0;
            EnsureShadowPipeline();
            int draws = 0;
            cl.SetPipeline(_shadowPipeline!);
            foreach (var draw in _shadowDraws)
            {
                var uniform = draw.Uniform;
                cl.UpdateBuffer(_entityUniformBuffer, 0, ref uniform);
                cl.SetGraphicsResourceSet(0, _entityResourceSet);
                cl.SetGraphicsResourceSet(2, draw.Palette);
                draws += DrawSubmeshes(cl, draw.Model, draw.HiddenWeapons);
            }
            _shadowDraws.Clear();
            return draws;
        }

        private void EnsureShadowPipeline()
        {
            if (_shadowPipeline != null) return;
            var factory = _gd.ResourceFactory;
            _shadowShaders = factory.CreateFromSpirv(
                new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(ActorShadowShaders.SkinnedVertexShaderGlsl), "main"),
                new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(ActorShadowShaders.FragmentShaderGlsl), "main"));

            var skinnedVertexLayout = new VertexLayoutDescription(
                new VertexElementDescription("Position0", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3, 0),
                new VertexElementDescription("Position1", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3, 12),
                new VertexElementDescription("Normal0", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3, 24),
                new VertexElementDescription("Normal1", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3, 36),
                new VertexElementDescription("Weights", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2, 48),
                new VertexElementDescription("Joints", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2, 56),
                new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2, 64),
                new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Byte4_Norm, 72));

            // Depth tested with Less and written: every triangle lies in the same plane, so where body parts overlap only the
            // first one darkens the ground.
            _shadowPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleAlphaBlend,
                DepthStencilState = new DepthStencilStateDescription(depthTestEnabled: true, depthWriteEnabled: true, comparisonKind: ComparisonKind.Less),
                RasterizerState = new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, depthClipEnabled: true, scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _sceneLayout, _textureLayout, _jointPaletteLayout },
                ShaderSet = new ShaderSetDescription(new[] { skinnedVertexLayout }, _shadowShaders),
                Outputs = _outputs
            });
        }

        private void DisposeShadows()
        {
            _shadowPipeline?.Dispose();
            _shadowPipeline = null;
            if (_shadowShaders != null)
            {
                foreach (var shader in _shadowShaders) shader.Dispose();
                _shadowShaders = null;
            }
        }
    }
}
