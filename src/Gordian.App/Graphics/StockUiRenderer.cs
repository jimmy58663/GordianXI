// src/Gordian.App/Graphics/StockUiRenderer.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Gordian.Core.Diagnostics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Veldrid;
using Veldrid.SPIRV;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Resizes an image around pivot lines (layout pixels): points at or past a pivot move by the extra amount, so
    /// parts spanning the pivot stretch and parts beyond it shift. The default (no extra) leaves images as authored.
    /// </summary>
    public readonly record struct UiStretch(float PivotX, float ExtraX, float PivotY, float ExtraY)
    {
        /// <summary>Resizes an image authored <paramref name="authoredWidth"/> wide to <paramref name="width"/>.</summary>
        public static UiStretch Horizontal(float authoredWidth, float width) => new(authoredWidth * 0.5f, width - authoredWidth, float.MaxValue, 0);
    }

    /// <summary>
    /// Tier 2 renderer for the stock FFXI 2D UI: draws UI element group images (Section 0x31 parts) and text as
    /// batched screen-space quads over the finished 3D scene.
    /// <para>
    /// Colour follows the client's half-scale convention: vertex colour 0x80 is 1.0, so a part's colour doubles
    /// before it modulates the texel (0x7F7F7F7F draws the texel unchanged). Texture alpha is normalized on upload:
    /// the DXT3 UI sheets store alpha at half scale (peak 0x88) while DXT1 sheets are 0/0xFF, and the client treats
    /// both as full opacity. Source rectangles can run past their texture (window backgrounds tile), so sampling
    /// wraps.
    /// </para>
    /// </summary>
    public sealed class StockUiRenderer : IDisposable
    {
        public const string VertexShaderGlsl = @"#version 450

layout(location = 0) in vec2 Position;
layout(location = 1) in vec2 TexCoord;
layout(location = 2) in vec4 Color;

layout(location = 0) out vec2 fsin_TexCoord;
layout(location = 1) out vec4 fsin_Color;

layout(set = 0, binding = 0) uniform StockUiUniforms
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

        public const string FragmentShaderGlsl = @"#version 450

layout(location = 0) in vec2 fsin_TexCoord;
layout(location = 1) in vec4 fsin_Color;

layout(location = 0) out vec4 fsout_Color;

layout(set = 1, binding = 0) uniform texture2D uTexture;
layout(set = 1, binding = 1) uniform sampler uSampler;

void main()
{
    vec4 texel = texture(sampler2D(uTexture, uSampler), fsin_TexCoord);
    fsout_Color = min(texel * fsin_Color * 2.0, vec4(1.0));
}
";

        [StructLayout(LayoutKind.Sequential)]
        private struct UiVertex
        {
            public Vector2 Position;
            public Vector2 TexCoord;
            public uint Color;

            public const uint SizeInBytes = 20;
        }

        private readonly record struct Batch(ResourceSet Texture, UiBlendMode Blend, int FirstVertex, int VertexCount);

        private readonly GraphicsDevice _gd;
        private readonly Pipeline[] _pipelines;
        private readonly ResourceLayout _uniformLayout;
        private readonly ResourceLayout _textureLayout;
        private readonly DeviceBuffer _uniformBuffer;
        private readonly ResourceSet _uniformSet;
        private readonly Sampler _sampler;
        private readonly CommandList _commandList;
        private readonly Shader[] _shaders;
        private DeviceBuffer _vertexBuffer;
        private uint _vertexCapacity;

        private readonly Dictionary<string, (Texture Texture, TextureView View, ResourceSet Set)?> _textures = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<UiVertex> _vertices = new();
        private readonly List<Batch> _batches = new();
        private UiResourceLibrary? _library;
        private bool _disposed;

        /// <summary>Quads submitted in the last frame.</summary>
        public int LastQuadCount { get; private set; }

        public StockUiRenderer(GraphicsDevice gd, OutputDescription outputs)
        {
            _gd = gd ?? throw new ArgumentNullException(nameof(gd));
            var factory = gd.ResourceFactory;

            _uniformLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("StockUiUniforms", ResourceKind.UniformBuffer, ShaderStages.Vertex)));
            _textureLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("uTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("uSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
            _uniformBuffer = factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            _uniformSet = factory.CreateResourceSet(new ResourceSetDescription(_uniformLayout, _uniformBuffer));

            // Bilinear with wrap addressing: the UI is scaled from its 512 x 448 layout and backgrounds tile.
            _sampler = factory.CreateSampler(new SamplerDescription(
                SamplerAddressMode.Wrap, SamplerAddressMode.Wrap, SamplerAddressMode.Wrap,
                SamplerFilter.MinLinear_MagLinear_MipPoint, null, 0, 0, 0, 0, SamplerBorderColor.TransparentBlack));

            _shaders = factory.CreateFromSpirv(
                new ShaderDescription(ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes(VertexShaderGlsl), "main"),
                new ShaderDescription(ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(FragmentShaderGlsl), "main"));

            var vertexLayout = new VertexLayoutDescription(
                new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Byte4_Norm));

            // One pipeline per part blend mode: alpha, additive, and darkening (destination minus source).
            var blends = new[]
            {
                BlendStateDescription.SingleAlphaBlend,
                new BlendStateDescription(RgbaFloat.Black, new BlendAttachmentDescription(true,
                    BlendFactor.SourceAlpha, BlendFactor.One, BlendFunction.Add,
                    BlendFactor.Zero, BlendFactor.One, BlendFunction.Add)),
                new BlendStateDescription(RgbaFloat.Black, new BlendAttachmentDescription(true,
                    BlendFactor.SourceAlpha, BlendFactor.One, BlendFunction.ReverseSubtract,
                    BlendFactor.Zero, BlendFactor.One, BlendFunction.Add)),
            };
            _pipelines = new Pipeline[blends.Length];
            for (int i = 0; i < blends.Length; i++)
            {
                _pipelines[i] = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
                {
                    BlendState = blends[i],
                    DepthStencilState = DepthStencilStateDescription.Disabled,
                    RasterizerState = new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise,
                        depthClipEnabled: false, scissorTestEnabled: false),
                    PrimitiveTopology = PrimitiveTopology.TriangleList,
                    ResourceLayouts = new[] { _uniformLayout, _textureLayout },
                    ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, _shaders),
                    Outputs = outputs,
                });
            }

            _vertexCapacity = 6 * 1024;
            _vertexBuffer = factory.CreateBuffer(new BufferDescription(_vertexCapacity * UiVertex.SizeInBytes, BufferUsage.VertexBuffer | BufferUsage.Dynamic));
            _commandList = factory.CreateCommandList();
        }

        /// <summary>
        /// Starts a frame's UI. Textures come from the given library (the GPU cache is dropped when it changes).
        /// </summary>
        public void Begin(UiResourceLibrary library)
        {
            if (!ReferenceEquals(library, _library))
            {
                ReleaseTextures();
                _library = library;
            }
            _vertices.Clear();
            _batches.Clear();
        }

        /// <summary>
        /// Draws every part of an image with its top-left layout origin at (x, y) screen pixels, scaled by
        /// <paramref name="scale"/> screen pixels per layout pixel. <paramref name="tint"/> multiplies each part's
        /// colour (half-scale: 0x80 = unchanged).
        /// </summary>
        public void DrawImage(UiImage image, float x, float y, float scale, UiColor? tint = null, UiStretch stretch = default)
        {
            foreach (var part in image.Parts) DrawPart(part, x, y, scale, tint, stretch);
        }

        public void DrawPart(UiSpritePart part, float x, float y, float scale, UiColor? tint = null, UiStretch stretch = default)
        {
            if (_library == null || !TryGetTextureSet(part.TextureName, out var set, out float texWidth, out float texHeight)) return;

            // A part that spans the stretch pivot grows and samples proportionally more of its (wrapping) texture,
            // so tiled backgrounds keep their texel density instead of smearing.
            float srcWidth = part.SourceWidth, srcHeight = part.SourceHeight;
            float quadWidth = part.TopRight.X - part.TopLeft.X, quadHeight = part.BottomLeft.Y - part.TopLeft.Y;
            if (stretch.ExtraX != 0 && part.TopLeft.X < stretch.PivotX && part.TopRight.X >= stretch.PivotX && quadWidth > 0)
                srcWidth += stretch.ExtraX * srcWidth / quadWidth;
            if (stretch.ExtraY != 0 && part.TopLeft.Y < stretch.PivotY && part.BottomLeft.Y >= stretch.PivotY && quadHeight > 0)
                srcHeight += stretch.ExtraY * srcHeight / quadHeight;

            float u0 = part.SourceX / texWidth, v0 = part.SourceY / texHeight;
            float u1 = (part.SourceX + srcWidth) / texWidth, v1 = (part.SourceY + srcHeight) / texHeight;

            var tl = new UiVertex { Position = Point(part.TopLeft, x, y, scale, stretch), TexCoord = new Vector2(u0, v0), Color = Pack(part.ColorTopLeft, tint) };
            var tr = new UiVertex { Position = Point(part.TopRight, x, y, scale, stretch), TexCoord = new Vector2(u1, v0), Color = Pack(part.ColorTopRight, tint) };
            var bl = new UiVertex { Position = Point(part.BottomLeft, x, y, scale, stretch), TexCoord = new Vector2(u0, v1), Color = Pack(part.ColorBottomLeft, tint) };
            var br = new UiVertex { Position = Point(part.BottomRight, x, y, scale, stretch), TexCoord = new Vector2(u1, v1), Color = Pack(part.ColorBottomRight, tint) };

            AddQuad(set, part.BlendMode, tl, tr, bl, br);
        }

        private void AddQuad(ResourceSet set, UiBlendMode blend, UiVertex tl, UiVertex tr, UiVertex bl, UiVertex br)
        {
            int first = _vertices.Count;
            _vertices.Add(tl); _vertices.Add(tr); _vertices.Add(bl);
            _vertices.Add(tr); _vertices.Add(br); _vertices.Add(bl);

            if (_batches.Count > 0 && ReferenceEquals(_batches[^1].Texture, set) && _batches[^1].Blend == blend)
            {
                var last = _batches[^1];
                _batches[^1] = last with { VertexCount = last.VertexCount + 6 };
            }
            else
            {
                _batches.Add(new Batch(set, blend, first, 6));
            }
        }

        /// <summary>
        /// Draws a rectangle of a UI texture (texture pixels) into a screen rectangle, modulated by a half-scale colour.
        /// For elements the client composes itself rather than from element groups (gauges, markers).
        /// </summary>
        public void DrawTextureRect(string textureName, float srcX, float srcY, float srcWidth, float srcHeight,
            float x, float y, float width, float height, UiColor color) =>
            DrawTextureRect(textureName, srcX, srcY, srcWidth, srcHeight, x, y, width, height, color, color, UiBlendMode.Alpha);

        /// <summary>
        /// As <see cref="DrawTextureRect(string, float, float, float, float, float, float, float, float, UiColor)"/>,
        /// with the colour graded from the left edge to the right edge.
        /// </summary>
        public void DrawTextureRect(string textureName, float srcX, float srcY, float srcWidth, float srcHeight,
            float x, float y, float width, float height, UiColor left, UiColor right, UiBlendMode blend = UiBlendMode.Alpha)
        {
            if (_library == null || width <= 0 || height <= 0) return;
            if (!TryGetTextureSet(textureName, out var set, out float texWidth, out float texHeight)) return;

            float u0 = srcX / texWidth, v0 = srcY / texHeight, u1 = (srcX + srcWidth) / texWidth, v1 = (srcY + srcHeight) / texHeight;
            uint cl = Pack(left, null), cr = Pack(right, null);
            AddQuad(set, blend,
                new UiVertex { Position = new Vector2(x, y), TexCoord = new Vector2(u0, v0), Color = cl },
                new UiVertex { Position = new Vector2(x + width, y), TexCoord = new Vector2(u1, v0), Color = cr },
                new UiVertex { Position = new Vector2(x, y + height), TexCoord = new Vector2(u0, v1), Color = cl },
                new UiVertex { Position = new Vector2(x + width, y + height), TexCoord = new Vector2(u1, v1), Color = cr });
        }

        /// <summary>
        /// Draws a whole texture that does not come from the UI library (status icons), cached under
        /// <paramref name="cacheKey"/> (prefix it so it cannot collide with library texture names).
        /// </summary>
        public void DrawTexture(string cacheKey, DecodedTexture texture, float x, float y, float width, float height, UiColor color)
        {
            if (width <= 0 || height <= 0) return;
            if (!_textures.TryGetValue(cacheKey, out var entry))
            {
                entry = Upload(texture);
                _textures[cacheKey] = entry;
            }
            if (entry is not { } e) return;
            uint c = Pack(color, null);
            AddQuad(e.Set, UiBlendMode.Alpha,
                new UiVertex { Position = new Vector2(x, y), TexCoord = new Vector2(0, 0), Color = c },
                new UiVertex { Position = new Vector2(x + width, y), TexCoord = new Vector2(1, 0), Color = c },
                new UiVertex { Position = new Vector2(x, y + height), TexCoord = new Vector2(0, 1), Color = c },
                new UiVertex { Position = new Vector2(x + width, y + height), TexCoord = new Vector2(1, 1), Color = c });
        }

        // Window border: the skin's "hfr1" strip (rows 0-2: dark, light, dark) along the frame's top and bottom edges,
        // added onto the background (the bottom line reads lavender, 205/206/246 over a 32/23/72 background) at about
        // 85% and fading out over 16 pixels at each end; measured from Windower captures of the party window. The DAT
        // authors only the background and title, so the client draws this itself.
        private const string BorderTexture = "hfr1";
        private const float BorderFade = 16, BorderThickness = 3;
        private static readonly UiColor BorderColor = new(0x80, 0x80, 0x80, 0x6C);
        private static readonly UiColor BorderClear = new(0x80, 0x80, 0x80, 0x00);

        /// <summary>
        /// Draws the client's window border lines along the top and bottom of a frame (layout width/height).
        /// </summary>
        public void DrawWindowBorder(float x, float y, float width, float height, float scale)
        {
            float fade = Math.Min(BorderFade, width / 2);
            foreach (float edgeY in new[] { y, y + (height - BorderThickness) * scale })
            {
                float h = BorderThickness * scale;
                DrawTextureRect(BorderTexture, 0, 0, fade, BorderThickness, x, edgeY, fade * scale, h, BorderClear, BorderColor, UiBlendMode.Add);
                DrawTextureRect(BorderTexture, fade, 0, width - 2 * fade, BorderThickness, x + fade * scale, edgeY, (width - 2 * fade) * scale, h, BorderColor, BorderColor, UiBlendMode.Add);
                DrawTextureRect(BorderTexture, width - fade, 0, fade, BorderThickness, x + (width - fade) * scale, edgeY, fade * scale, h, BorderColor, BorderClear, UiBlendMode.Add);
            }
        }

        /// <summary>
        /// Draws a menu window at a placement: the frame's plain images (reference kind 0), then each button's.
        /// Frames are the persistent windows; buttons draw their unselected state. <paramref name="frameWidth"/>
        /// widens or narrows the frame (layout pixels): its right half moves and parts spanning the middle stretch.
        /// </summary>
        public void DrawMenu(UiMenuDefinition menu, StockUiPlacement placement, bool includeButtons = true, float? frameWidth = null)
        {
            if (_library == null || placement.Hidden) return;
            var stretch = frameWidth is { } w ? UiStretch.Horizontal(menu.Frame.Width, w) : default;
            float borderWidth = frameWidth ?? menu.Frame.Width;
            foreach (var shape in menu.Frame.Shapes)
            {
                if (shape.Kind != 0 || !_library.TryGetImage(shape, out var image)) continue;

                // Background ("newtex") parts, then the client's border lines, then the rest (title and its band).
                bool hasBackground = false;
                foreach (var part in image.Parts)
                {
                    if (!IsBackground(part)) continue;
                    DrawPart(part, placement.X, placement.Y, placement.Scale, null, stretch);
                    hasBackground = true;
                }
                if (hasBackground) DrawWindowBorder(placement.X, placement.Y, borderWidth, menu.Frame.Height, placement.Scale);
                foreach (var part in image.Parts)
                {
                    if (!IsBackground(part)) DrawPart(part, placement.X, placement.Y, placement.Scale, null, stretch);
                }
            }
            if (!includeButtons) return;
            foreach (var button in menu.Buttons)
            {
                float bx = placement.X + button.X * placement.Scale, by = placement.Y + button.Y * placement.Scale;
                foreach (var shape in button.Shapes)
                {
                    if (shape.Kind == 0 && _library.TryGetImage(shape, out var image)) DrawImage(image, bx, by, placement.Scale);
                }
            }
        }

        /// <summary>
        /// Draws a line of text with the stock font; (x, y) is the line's top-left in screen pixels.
        /// Returns the pen position after the last glyph.
        /// </summary>
        public float DrawText(UiFont font, ReadOnlySpan<char> text, float x, float y, float scale, UiColor? color = null)
        {
            // Glyphs carry their own dark outline in the font texture; no extra shadow is drawn.
            float pen = x;
            foreach (char c in text)
            {
                if (font.TryGetGlyph(c, out var glyph)) DrawImage(glyph, pen, y, scale, color);
                pen += font.GetAdvance(c) * scale;
            }
            return pen;
        }

        /// <summary>
        /// Submits the frame's UI to the framebuffer (drawn over whatever it already holds).
        /// </summary>
        public void End(Framebuffer framebuffer, uint width, uint height)
        {
            LastQuadCount = _vertices.Count / 6;
            if (_disposed || _vertices.Count == 0 || width == 0 || height == 0) return;

            EnsureVertexCapacity((uint)_vertices.Count);
            _gd.UpdateBuffer(_vertexBuffer, 0, CollectionsMarshal.AsSpan(_vertices));

            // Screen pixels -> NDC; Vulkan clip space has Y pointing down.
            float yScale = _gd.IsClipSpaceYInverted ? 2.0f / height : -2.0f / height;
            float yOffset = _gd.IsClipSpaceYInverted ? -1.0f : 1.0f;
            _gd.UpdateBuffer(_uniformBuffer, 0, new Vector4(2.0f / width, yScale, -1.0f, yOffset));

            _commandList.Begin();
            _commandList.SetFramebuffer(framebuffer);
            _commandList.SetFullViewports();
            _commandList.SetVertexBuffer(0, _vertexBuffer);
            UiBlendMode? current = null;
            foreach (var batch in _batches)
            {
                if (batch.Blend != current)
                {
                    _commandList.SetPipeline(_pipelines[(int)batch.Blend]);
                    _commandList.SetGraphicsResourceSet(0, _uniformSet);
                    current = batch.Blend;
                }
                _commandList.SetGraphicsResourceSet(1, batch.Texture);
                _commandList.Draw((uint)batch.VertexCount, 1, (uint)batch.FirstVertex, 0);
            }
            _commandList.End();
            _gd.SubmitCommands(_commandList);
        }

        private static bool IsBackground(UiSpritePart part) =>
            UiResourceLibrary.TrimResourceName(part.TextureName).Equals("newtex", StringComparison.OrdinalIgnoreCase);

        private static Vector2 Point(UiPoint p, float x, float y, float scale, UiStretch stretch)
        {
            float px = p.X >= stretch.PivotX ? p.X + stretch.ExtraX : p.X;
            float py = p.Y >= stretch.PivotY ? p.Y + stretch.ExtraY : p.Y;
            return new Vector2(x + px * scale, y + py * scale);
        }

        private static uint Pack(UiColor c, UiColor? tint)
        {
            int r = c.R, g = c.G, b = c.B, a = c.A;
            if (tint is { } t)
            {
                r = Math.Min(255, r * t.R / 0x80);
                g = Math.Min(255, g * t.G / 0x80);
                b = Math.Min(255, b * t.B / 0x80);
                a = Math.Min(255, a * t.A / 0x80);
            }
            return (uint)(r | (g << 8) | (b << 16) | (a << 24));
        }

        private bool TryGetTextureSet(string textureName, out ResourceSet set, out float width, out float height)
        {
            set = null!;
            width = height = 0;
            string name = UiResourceLibrary.TrimResourceName(textureName);
            if (!_textures.TryGetValue(name, out var entry))
            {
                entry = _library != null && _library.TryGetTexture(name, out var decoded) ? Upload(decoded) : null;
                _textures[name] = entry;
            }
            if (entry is not { } e) return false;
            set = e.Set;
            width = e.Texture.Width;
            height = e.Texture.Height;
            return true;
        }

        private (Texture, TextureView, ResourceSet) Upload(DecodedTexture decoded)
        {
            var pixels = NormalizeAlpha(decoded.RgbaPixels);
            var factory = _gd.ResourceFactory;
            var texture = factory.CreateTexture(TextureDescription.Texture2D((uint)decoded.Width, (uint)decoded.Height, 1, 1,
                PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
            _gd.UpdateTexture(texture, pixels, 0, 0, 0, (uint)decoded.Width, (uint)decoded.Height, 1, 0, 0);
            var view = factory.CreateTextureView(texture);
            var set = factory.CreateResourceSet(new ResourceSetDescription(_textureLayout, view, _sampler));
            return (texture, view, set);
        }

        /// <summary>
        /// Brings a half-scale alpha texture (peak alpha at most 0x88, e.g. the DXT3 font sheets) to full scale; other
        /// textures are returned unchanged.
        /// </summary>
        public static byte[] NormalizeAlpha(byte[] rgba)
        {
            int peak = 0;
            for (int i = 3; i < rgba.Length; i += 4) peak = Math.Max(peak, rgba[i]);
            if (peak == 0 || peak > 0x88) return rgba;

            var copy = (byte[])rgba.Clone();
            for (int i = 3; i < copy.Length; i += 4) copy[i] = (byte)Math.Min(255, copy[i] * 2);
            return copy;
        }

        private void EnsureVertexCapacity(uint count)
        {
            if (count <= _vertexCapacity) return;
            while (_vertexCapacity < count) _vertexCapacity *= 2;
            _vertexBuffer.Dispose();
            _vertexBuffer = _gd.ResourceFactory.CreateBuffer(new BufferDescription(_vertexCapacity * UiVertex.SizeInBytes, BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        }

        private void ReleaseTextures()
        {
            foreach (var entry in _textures.Values)
            {
                if (entry is not { } e) continue;
                e.Set.Dispose();
                e.View.Dispose();
                e.Texture.Dispose();
            }
            _textures.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ReleaseTextures();
            _vertexBuffer.Dispose();
            _commandList.Dispose();
            foreach (var pipeline in _pipelines) pipeline.Dispose();
            foreach (var shader in _shaders) shader.Dispose();
            _uniformSet.Dispose();
            _uniformBuffer.Dispose();
            _sampler.Dispose();
            _textureLayout.Dispose();
            _uniformLayout.Dispose();
            GordianLog.Debug("UI", "Stock UI renderer disposed.");
        }
    }
}
