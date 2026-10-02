// src/Gordian.App/Graphics/ScenePostProcess.cs
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Veldrid;
using Veldrid.SPIRV;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// The cutscene post-process of the 3D scene (#205): the scene routines' motion blur (op 0x0E) and cross-dissolve
    /// (op 0x10). While a cutscene camera or either effect runs, the scene is drawn into an offscreen target; each frame
    /// is then composited with the previous composited frame (the blur: a share of it, tinted and zoomed, stays on
    /// screen) and with the frame held at a dissolve's start (fading out over its duration), kept as the next frame's
    /// history and copied to the real target. The stock UI and its screen tints draw on top afterwards, as before.
    /// </summary>
    public sealed class ScenePostProcess : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct PostParams
        {
            public Vector4 Tint;   // rgb: the previous frame's tint; a: how much of it stays (0 = no blur)
            public Vector4 Params; // x: the previous frame's zoom; y: dissolve opacity; z: 1 = plain copy; w: 1 = flip V
            public const uint SizeInBytes = 32;
        }

        private const string VertexGlsl = @"#version 450
layout(set = 0, binding = 0) uniform PostParams
{
    vec4 Tint;
    vec4 Params;
};
layout(location = 0) out vec2 fsin_Uv;
void main()
{
    // One triangle covering the screen.
    vec2 corner = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
    fsin_Uv = vec2(corner.x, Params.w > 0.5 ? 1.0 - corner.y : corner.y);
    gl_Position = vec4(corner * 2.0 - 1.0, 0.0, 1.0);
}";

        private const string FragmentGlsl = @"#version 450
layout(set = 0, binding = 0) uniform PostParams
{
    vec4 Tint;
    vec4 Params;
};
layout(set = 0, binding = 1) uniform texture2D SceneTex;
layout(set = 0, binding = 2) uniform texture2D HistoryTex;
layout(set = 0, binding = 3) uniform texture2D DissolveTex;
layout(set = 0, binding = 4) uniform sampler LinearSampler;
layout(location = 0) in vec2 fsin_Uv;
layout(location = 0) out vec4 fsout_Color;
void main()
{
    vec3 color = texture(sampler2D(SceneTex, LinearSampler), fsin_Uv).rgb;
    if (Params.z < 0.5)
    {
        if (Tint.a > 0.0)
        {
            // The previous frame, zoomed about the centre (a zoom below 1 enlarges it: the trail spreads outward).
            vec2 historyUv = vec2(0.5) + (fsin_Uv - vec2(0.5)) * Params.x;
            vec3 history = texture(sampler2D(HistoryTex, LinearSampler), historyUv).rgb * Tint.rgb;
            color = mix(color, clamp(history, 0.0, 1.0), Tint.a);
        }
        if (Params.y > 0.0)
        {
            color = mix(color, texture(sampler2D(DissolveTex, LinearSampler), fsin_Uv).rgb, Params.y);
        }
    }
    fsout_Color = vec4(color, 1.0);
}";

        private readonly GraphicsDevice _gd;
        private readonly ResourceLayout _layout;
        private readonly Pipeline _pipeline;
        private readonly Shader[] _shaders;
        private readonly DeviceBuffer _compositeParams;
        private readonly DeviceBuffer _copyParams;
        private readonly bool _flipV;

        private uint _width;
        private uint _height;
        private PixelFormat _format;
        private Texture? _sceneColor;
        private Texture? _depth;
        private Framebuffer? _sceneFramebuffer;
        private Texture? _sceneDepth;
        private readonly Texture?[] _history = new Texture?[2];
        private readonly Framebuffer?[] _historyFramebuffers = new Framebuffer?[2];
        private Texture? _dissolveFrame;
        private readonly ResourceSet?[] _compositeSets = new ResourceSet?[2];
        private readonly ResourceSet?[] _copySets = new ResourceSet?[2];
        private int _current;
        private bool _historyValid;
        private bool _dissolveValid;
        private int _dissolveSequence;

        public ScenePostProcess(GraphicsDevice gd, OutputDescription outputs)
        {
            _gd = gd;
            var factory = gd.ResourceFactory;
            _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("PostParams", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
                new ResourceLayoutElementDescription("SceneTex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("HistoryTex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("DissolveTex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("LinearSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
            // The pipeline keeps using its shaders (D3D11): they live as long as it does.
            _shaders = factory.CreateFromSpirv(
                new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(VertexGlsl), "main"),
                new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(FragmentGlsl), "main"));
            _pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleOverrideBlend,
                DepthStencilState = DepthStencilStateDescription.Disabled,
                RasterizerState = new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, depthClipEnabled: true, scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _layout },
                ShaderSet = new ShaderSetDescription(Array.Empty<VertexLayoutDescription>(), _shaders),
                Outputs = outputs
            });
            _compositeParams = factory.CreateBuffer(new BufferDescription(PostParams.SizeInBytes, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            _copyParams = factory.CreateBuffer(new BufferDescription(PostParams.SizeInBytes, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            // The triangle's corner (0, 0) is the bottom left in clip space; where texture rows start at the top, V flips
            // (and flips back where clip space Y points down).
            _flipV = gd.IsUvOriginTopLeft != gd.IsClipSpaceYInverted;
        }

        /// <summary>
        /// The offscreen framebuffer to draw this frame's scene into, sized and formatted like <paramref name="target"/>.
        /// Its depth attachment is the target's own, so what draws on the target afterwards and tests the scene's depth
        /// (the name plates, cut by walls and models in front) sees this frame's scene, not a stale one. A frame drawn
        /// without it leaves no history: a blur or dissolve that starts after it starts from that frame.
        /// </summary>
        public Framebuffer BeginScene(Framebuffer target)
        {
            var color = target.ColorTargets[0].Target;
            EnsureTargets(target.Width, target.Height, color.Format);
            var depth = target.DepthTarget?.Target ?? _depth!;
            if (!ReferenceEquals(depth, _sceneDepth))
            {
                // The swapchain's depth texture is replaced on a resize.
                _sceneFramebuffer?.Dispose();
                _sceneFramebuffer = _gd.ResourceFactory.CreateFramebuffer(new FramebufferDescription(depth, _sceneColor!));
                _sceneDepth = depth;
            }
            return _sceneFramebuffer!;
        }

        /// <summary>Forgets the history: the next frame drawn through <see cref="BeginScene"/> starts without blur.</summary>
        public void Invalidate()
        {
            _historyValid = false;
            _dissolveValid = false;
        }

        /// <summary>
        /// Records the composite of the scene drawn into <see cref="BeginScene"/>'s framebuffer, with the blur and the
        /// dissolve, into <paramref name="target"/>. <paramref name="frames"/> (60 Hz frames since the last one) keeps the
        /// blur's trail the same length at any frame rate.
        /// </summary>
        public void Composite(CommandList commandList, Framebuffer target, Gordian.Core.Events.SceneBlur blur, (int Sequence, float Opacity) dissolve, float frames)
        {
            int previous = _current;
            int next = 1 - _current;

            // A new dissolve holds the frame before it: the last composited one.
            if (dissolve.Sequence != 0 && dissolve.Sequence != _dissolveSequence)
            {
                _dissolveSequence = dissolve.Sequence;
                _dissolveValid = _historyValid;
                if (_historyValid) commandList.CopyTexture(_history[previous]!, _dissolveFrame!);
            }

            // The command's values are per 60 Hz frame: at another frame rate each frame keeps amount^frames of the
            // previous one, zoomed zoom^frames.
            float amount = _historyValid && blur.IsActive ? MathF.Pow(blur.Amount, Math.Max(frames, 0.01f)) : 0f;
            var composite = new PostParams
            {
                Tint = new Vector4(blur.Tint, amount),
                Params = new Vector4(MathF.Pow(blur.Zoom, Math.Max(frames, 0.01f)), _dissolveValid ? Math.Clamp(dissolve.Opacity, 0f, 1f) : 0f, 0f, _flipV ? 1f : 0f)
            };
            var copy = new PostParams { Tint = Vector4.Zero, Params = new Vector4(1f, 0f, 1f, _flipV ? 1f : 0f) };
            commandList.UpdateBuffer(_compositeParams, 0, ref composite);
            commandList.UpdateBuffer(_copyParams, 0, ref copy);

            commandList.SetFramebuffer(_historyFramebuffers[next]!);
            commandList.SetPipeline(_pipeline);
            commandList.SetGraphicsResourceSet(0, _compositeSets[previous]!);
            commandList.Draw(3);

            commandList.SetFramebuffer(target);
            commandList.SetGraphicsResourceSet(0, _copySets[next]!);
            commandList.Draw(3);

            _current = next;
            _historyValid = true;
            if (dissolve.Sequence == 0) _dissolveValid = false;
        }

        private void EnsureTargets(uint width, uint height, PixelFormat format)
        {
            if (_sceneColor != null && width == _width && height == _height && format == _format) return;
            DisposeTargets();
            _width = width;
            _height = height;
            _format = format;
            var factory = _gd.ResourceFactory;
            var usage = TextureUsage.RenderTarget | TextureUsage.Sampled;
            _sceneColor = factory.CreateTexture(TextureDescription.Texture2D(width, height, 1, 1, format, usage));
            _depth = factory.CreateTexture(TextureDescription.Texture2D(width, height, 1, 1, PixelFormat.R32_Float, TextureUsage.DepthStencil));
            for (int i = 0; i < 2; i++)
            {
                _history[i] = factory.CreateTexture(TextureDescription.Texture2D(width, height, 1, 1, format, usage));
                _historyFramebuffers[i] = factory.CreateFramebuffer(new FramebufferDescription(_depth, _history[i]!));
            }
            _dissolveFrame = factory.CreateTexture(TextureDescription.Texture2D(width, height, 1, 1, format, TextureUsage.Sampled));
            for (int i = 0; i < 2; i++)
            {
                // Composite: this frame's scene over history i (the previous result), into the other history target.
                _compositeSets[i] = factory.CreateResourceSet(new ResourceSetDescription(_layout,
                    _compositeParams, _sceneColor, _history[i]!, _dissolveFrame, _gd.LinearSampler));
                // Copy: history i (this frame's result) to the real target.
                _copySets[i] = factory.CreateResourceSet(new ResourceSetDescription(_layout,
                    _copyParams, _history[i]!, _history[i]!, _dissolveFrame, _gd.LinearSampler));
            }
            _current = 0;
            Invalidate();
        }

        private void DisposeTargets()
        {
            for (int i = 0; i < 2; i++)
            {
                _compositeSets[i]?.Dispose();
                _copySets[i]?.Dispose();
                _historyFramebuffers[i]?.Dispose();
                _history[i]?.Dispose();
                _compositeSets[i] = null;
                _copySets[i] = null;
                _historyFramebuffers[i] = null;
                _history[i] = null;
            }
            _sceneFramebuffer?.Dispose();
            _sceneColor?.Dispose();
            _depth?.Dispose();
            _dissolveFrame?.Dispose();
            _sceneFramebuffer = null;
            _sceneDepth = null;
            _sceneColor = null;
            _depth = null;
            _dissolveFrame = null;
        }

        public void Dispose()
        {
            DisposeTargets();
            _pipeline.Dispose();
            foreach (var shader in _shaders) shader.Dispose();
            _layout.Dispose();
            _compositeParams.Dispose();
            _copyParams.Dispose();
        }
    }
}
