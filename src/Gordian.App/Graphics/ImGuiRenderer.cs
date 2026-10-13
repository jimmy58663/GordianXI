// src/Gordian.App/Graphics/ImGuiRenderer.cs
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using Hexa.NET.ImGui;
using NeoVeldrid;
using NeoVeldrid.SPIRV;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws Dear ImGui's output with NeoVeldrid (Tier 3, #30): no maintained ImGui renderer exists for NeoVeldrid, so this
    /// one is written clean-room from Dear ImGui's documented renderer backend contract (imgui.h, docs/BACKENDS.md, MIT,
    /// https://github.com/ocornut/imgui): textured, vertex-coloured triangles in screen pixels, one indexed draw per
    /// <c>ImDrawCmd</c> clipped to its rectangle, with <c>VtxOffset</c> honoured (<c>ImGuiBackendFlags_RendererHasVtxOffset</c>)
    /// and the 1.92 texture protocol (<c>ImGuiBackendFlags_RendererHasTextures</c>): ImGui asks for each texture
    /// (the font atlas) to be created, updated in rectangles or destroyed through <c>ImDrawData::Textures</c>.
    /// <para>All calls are made on the render thread under the device's GPU lock.</para>
    /// </summary>
    public sealed unsafe class ImGuiRenderer : IDisposable
    {
        private const string VertexShaderGlsl = @"#version 450

layout(location = 0) in vec2 Position;
layout(location = 1) in vec2 TexCoord;
layout(location = 2) in vec4 Color;

layout(location = 0) out vec2 fsin_TexCoord;
layout(location = 1) out vec4 fsin_Color;

layout(set = 0, binding = 0) uniform ImGuiUniforms
{
    vec4 ScreenTransform;
};

void main()
{
    fsin_TexCoord = TexCoord;
    fsin_Color = Color;
    gl_Position = vec4(Position * ScreenTransform.xy + ScreenTransform.zw, 0.0, 1.0);
}
";

        private const string FragmentShaderGlsl = @"#version 450

layout(location = 0) in vec2 fsin_TexCoord;
layout(location = 1) in vec4 fsin_Color;

layout(location = 0) out vec4 fsout_Color;

layout(set = 1, binding = 0) uniform texture2D uTexture;
layout(set = 1, binding = 1) uniform sampler uSampler;

void main()
{
    fsout_Color = fsin_Color * texture(sampler2D(uTexture, uSampler), fsin_TexCoord);
}
";

        /// <summary>Size of <see cref="ImDrawVert"/>: position, UV (two float2) and a packed RGBA colour.</summary>
        private const uint VertexSize = 20;

        private readonly GraphicsDevice _gd;
        private readonly Shader[] _shaders;
        private readonly ResourceLayout _uniformLayout;
        private readonly ResourceLayout _textureLayout;
        private readonly DeviceBuffer _uniformBuffer;
        private readonly ResourceSet _uniformSet;
        private readonly Sampler _sampler;
        private readonly Pipeline _pipeline;
        private readonly CommandList _commandList;
        private DeviceBuffer _vertexBuffer;
        private DeviceBuffer _indexBuffer;

        private readonly Dictionary<ulong, (Texture Texture, ResourceSet Set)> _textures = new();
        private ulong _nextTextureId = 1;
        private bool _disposed;

        /// <summary>Indexed draws submitted in the last frame.</summary>
        public int LastDrawCalls { get; private set; }

        public ImGuiRenderer(GraphicsDevice gd, OutputDescription outputs)
        {
            _gd = gd ?? throw new ArgumentNullException(nameof(gd));
            var factory = gd.ResourceFactory;

            _uniformLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("ImGuiUniforms", ResourceKind.UniformBuffer, ShaderStages.Vertex)));
            _textureLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("uTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("uSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
            _uniformBuffer = factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            _uniformSet = factory.CreateResourceSet(new ResourceSetDescription(_uniformLayout, _uniformBuffer));
            _sampler = factory.CreateSampler(new SamplerDescription(
                SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp,
                SamplerFilter.MinLinear_MagLinear_MipLinear, null, 0, 0, 0, 0, SamplerBorderColor.TransparentBlack));

            _shaders = factory.CreateFromSpirv(
                new ShaderDescription(ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes(VertexShaderGlsl), "main"),
                new ShaderDescription(ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(FragmentShaderGlsl), "main"));

            var vertexLayout = new VertexLayoutDescription(
                new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Byte4_Norm));

            // Straight alpha over the scene; the destination alpha keeps ImGui's documented one / one-minus-source-alpha.
            var blend = new BlendStateDescription(RgbaFloat.Black, new BlendAttachmentDescription(true,
                BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha, BlendFunction.Add,
                BlendFactor.One, BlendFactor.InverseSourceAlpha, BlendFunction.Add));
            _pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = blend,
                DepthStencilState = DepthStencilStateDescription.Disabled,
                RasterizerState = new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise,
                    depthClipEnabled: false, scissorTestEnabled: true),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _uniformLayout, _textureLayout },
                ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, _shaders),
                Outputs = outputs,
            });

            _vertexBuffer = factory.CreateBuffer(new BufferDescription(4096 * VertexSize, BufferUsage.VertexBuffer | BufferUsage.Dynamic));
            _indexBuffer = factory.CreateBuffer(new BufferDescription(8192 * sizeof(ushort), BufferUsage.IndexBuffer | BufferUsage.Dynamic));
            _commandList = factory.CreateCommandList();
        }

        /// <summary>
        /// Applies the frame's texture requests and draws <paramref name="drawData"/> (from <c>ImGui.GetDrawData()</c> after
        /// <c>ImGui.Render()</c>) over the framebuffer's current contents.
        /// </summary>
        public void Render(ImDrawDataPtr drawData, Framebuffer framebuffer)
        {
            LastDrawCalls = 0;
            if (_disposed || drawData.IsNull) return;
            ref var textures = ref drawData.Textures;
            for (int i = 0; i < textures.Size; i++) UpdateTexture(textures.Data[i]);

            int totalVertices = drawData.TotalVtxCount, totalIndices = drawData.TotalIdxCount;
            uint width = framebuffer.Width, height = framebuffer.Height;
            if (totalVertices == 0 || totalIndices == 0 || width == 0 || height == 0) return;

            EnsureBuffer(ref _vertexBuffer, (uint)totalVertices * VertexSize, BufferUsage.VertexBuffer);
            EnsureBuffer(ref _indexBuffer, (uint)totalIndices * sizeof(ushort), BufferUsage.IndexBuffer);

            Vector2 origin = drawData.DisplayPos;
            Vector2 size = drawData.DisplaySize;
            if (size.X <= 0 || size.Y <= 0) return;
            Vector2 fbScale = drawData.FramebufferScale;
            if (fbScale.X <= 0 || fbScale.Y <= 0) fbScale = Vector2.One;
            // ImGui's display rectangle -> clip space; Vulkan's clip space has Y pointing down.
            float sx = 2f / size.X;
            float sy = _gd.IsClipSpaceYInverted ? 2f / size.Y : -2f / size.Y;
            var transform = new Vector4(sx, sy, -1f - origin.X * sx, (_gd.IsClipSpaceYInverted ? -1f : 1f) - origin.Y * sy);

            _commandList.Begin();
            _commandList.UpdateBuffer(_uniformBuffer, 0, transform);
            ref var lists = ref drawData.CmdLists;
            uint vertexOffset = 0, indexOffset = 0;
            for (int n = 0; n < lists.Size; n++)
            {
                var list = lists.Data[n];
                ref var vertices = ref list.VtxBuffer;
                ref var indices = ref list.IdxBuffer;
                _commandList.UpdateBuffer(_vertexBuffer, vertexOffset * VertexSize, (IntPtr)vertices.Data, (uint)vertices.Size * VertexSize);
                _commandList.UpdateBuffer(_indexBuffer, indexOffset * sizeof(ushort), (IntPtr)indices.Data, (uint)indices.Size * sizeof(ushort));
                vertexOffset += (uint)vertices.Size;
                indexOffset += (uint)indices.Size;
            }

            _commandList.SetFramebuffer(framebuffer);
            _commandList.SetFullViewports();
            _commandList.SetPipeline(_pipeline);
            _commandList.SetGraphicsResourceSet(0, _uniformSet);
            _commandList.SetVertexBuffer(0, _vertexBuffer);
            _commandList.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);

            int draws = 0;
            vertexOffset = 0;
            indexOffset = 0;
            ResourceSet? boundTexture = null;
            for (int n = 0; n < lists.Size; n++)
            {
                var list = lists.Data[n];
                ref var commands = ref list.CmdBuffer;
                for (int c = 0; c < commands.Size; c++)
                {
                    ImDrawCmd* cmd = commands.Data + c;
                    // No user callbacks are registered by GordianXI's overlays (ResetRenderState included); skip any.
                    if (cmd->UserCallback != null || cmd->ElemCount == 0) continue;

                    Vector4 clip = cmd->ClipRect;
                    float x0 = Math.Max(0f, (clip.X - origin.X) * fbScale.X), y0 = Math.Max(0f, (clip.Y - origin.Y) * fbScale.Y);
                    float x1 = Math.Min(width, (clip.Z - origin.X) * fbScale.X), y1 = Math.Min(height, (clip.W - origin.Y) * fbScale.Y);
                    if (x1 <= x0 || y1 <= y0) continue;

                    ulong id = cmd->TexRef.TexData != null ? cmd->TexRef.TexData->TexID.Handle : cmd->TexRef.TexID.Handle;
                    if (!_textures.TryGetValue(id, out var texture)) continue;
                    if (!ReferenceEquals(boundTexture, texture.Set))
                    {
                        _commandList.SetGraphicsResourceSet(1, texture.Set);
                        boundTexture = texture.Set;
                    }
                    _commandList.SetScissorRect(0, (uint)x0, (uint)y0, (uint)(x1 - x0), (uint)(y1 - y0));
                    _commandList.DrawIndexed(cmd->ElemCount, 1, cmd->IdxOffset + indexOffset, (int)(cmd->VtxOffset + vertexOffset), 0);
                    draws++;
                }
                vertexOffset += (uint)list.VtxBuffer.Size;
                indexOffset += (uint)list.IdxBuffer.Size;
            }
            _commandList.End();
            _gd.SubmitCommands(_commandList);
            LastDrawCalls = draws;
        }

        private void EnsureBuffer(ref DeviceBuffer buffer, uint bytes, BufferUsage usage)
        {
            if (buffer.SizeInBytes >= bytes) return;
            buffer.Dispose();
            uint capacity = Math.Max(bytes, buffer.SizeInBytes * 2);
            buffer = _gd.ResourceFactory.CreateBuffer(new BufferDescription(capacity, usage | BufferUsage.Dynamic));
        }

        /// <summary>Handles one texture's request: create (upload it all), update (upload its changed rectangles) or destroy.</summary>
        private void UpdateTexture(ImTextureDataPtr tex)
        {
            switch (tex.Status)
            {
                case ImTextureStatus.WantCreate:
                {
                    var texture = _gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
                        (uint)tex.Width, (uint)tex.Height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
                    var set = _gd.ResourceFactory.CreateResourceSet(new ResourceSetDescription(_textureLayout, texture, _sampler));
                    ulong id = _nextTextureId++;
                    _textures[id] = (texture, set);
                    Upload(tex, texture, 0, 0, tex.Width, tex.Height);
                    tex.SetTexID(new ImTextureID(id));
                    tex.SetStatus(ImTextureStatus.Ok);
                    break;
                }
                case ImTextureStatus.WantUpdates:
                {
                    if (_textures.TryGetValue(tex.TexID.Handle, out var entry))
                    {
                        ref var updates = ref tex.Updates;
                        if (updates.Size > 0)
                        {
                            for (int i = 0; i < updates.Size; i++)
                            {
                                var r = updates.Data[i];
                                Upload(tex, entry.Texture, r.X, r.Y, r.W, r.H);
                            }
                        }
                        else
                        {
                            var r = tex.UpdateRect;
                            Upload(tex, entry.Texture, r.X, r.Y, r.W, r.H);
                        }
                    }
                    tex.SetStatus(ImTextureStatus.Ok);
                    break;
                }
                case ImTextureStatus.WantDestroy when tex.UnusedFrames > 0:
                    DestroyTexture(tex);
                    break;
            }
        }

        private void DestroyTexture(ImTextureDataPtr tex)
        {
            if (_textures.Remove(tex.TexID.Handle, out var entry))
            {
                entry.Set.Dispose();
                entry.Texture.Dispose();
            }
            tex.SetTexID(ImTextureID.Null);
            tex.SetStatus(ImTextureStatus.Destroyed);
        }

        /// <summary>Copies a rectangle of the texture's pixels (RGBA32, or Alpha8 widened to white) into the GPU texture.</summary>
        private void Upload(ImTextureDataPtr tex, Texture texture, int x, int y, int w, int h)
        {
            if (w <= 0 || h <= 0 || tex.Pixels == null) return;
            int bytes = w * h * 4;
            byte[] rented = ArrayPool<byte>.Shared.Rent(bytes);
            try
            {
                bool alpha8 = tex.Format == ImTextureFormat.Alpha8;
                int pitch = tex.GetPitch();
                int bpp = tex.BytesPerPixel;
                for (int row = 0; row < h; row++)
                {
                    byte* source = tex.Pixels + (y + row) * pitch + x * bpp;
                    var destination = rented.AsSpan(row * w * 4, w * 4);
                    if (!alpha8)
                    {
                        new ReadOnlySpan<byte>(source, w * 4).CopyTo(destination);
                        continue;
                    }
                    for (int px = 0; px < w; px++)
                    {
                        destination[px * 4] = 255;
                        destination[px * 4 + 1] = 255;
                        destination[px * 4 + 2] = 255;
                        destination[px * 4 + 3] = source[px];
                    }
                }
                fixed (byte* data = rented)
                {
                    _gd.UpdateTexture(texture, (IntPtr)data, (uint)bytes, (uint)x, (uint)y, 0, (uint)w, (uint)h, 1, 0, 0);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        /// <summary>
        /// Releases the GPU textures of every texture ImGui still holds and marks them destroyed (call with the owning
        /// context current, before destroying it).
        /// </summary>
        public void DestroyTextures(ImVector<ImTextureDataPtr> textures)
        {
            for (int i = 0; i < textures.Size; i++)
            {
                var tex = textures.Data[i];
                if (tex.Status != ImTextureStatus.Destroyed) DestroyTexture(tex);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var entry in _textures.Values)
            {
                entry.Set.Dispose();
                entry.Texture.Dispose();
            }
            _textures.Clear();
            _commandList.Dispose();
            _vertexBuffer.Dispose();
            _indexBuffer.Dispose();
            _pipeline.Dispose();
            _sampler.Dispose();
            _uniformSet.Dispose();
            _uniformBuffer.Dispose();
            _textureLayout.Dispose();
            _uniformLayout.Dispose();
            foreach (var shader in _shaders) shader.Dispose();
        }
    }
}
