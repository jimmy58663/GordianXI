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
layout(location = 3) in float Depth;

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
    gl_Position = vec4(Position * ScreenTransform.xy + ScreenTransform.zw, Depth, 1.0);
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

            /// <summary>Clip-space depth (z / w); only depth-tested batches compare it.</summary>
            public float Depth;

            public const uint SizeInBytes = 24;
        }

        private readonly record struct Batch(ResourceSet Texture, UiBlendMode Blend, int FirstVertex, int VertexCount, UiClip? Clip, bool DepthTested);

        /// <summary>
        /// While set, quads are drawn at this clip-space depth (z / w of the 3D pass's projection) and tested against
        /// the scene's depth buffer without writing it, so walls and models in front hide them pixel by pixel (in-world
        /// name plates). Null = the ordinary overlay. Has no effect when the framebuffer has no depth attachment.
        /// </summary>
        public float? Depth { get; set; }

        /// <summary>
        /// Multiplies the opacity of every quad drawn while set, 0-1 (an event's interface fade); reset to 1 by
        /// <see cref="Begin"/>.
        /// </summary>
        public float Opacity { get; set; } = 1f;

        /// <summary>Height of a menu window's title band in layout pixels; the body below it is drawn opaque (see <see cref="DrawMenu"/>).</summary>
        public const float MenuBandHeight = 20;

        private UiClip? _clip;

        /// <summary>Clips every quad drawn until <see cref="ClearClip"/> to a screen rectangle (scrolling lists).</summary>
        public void SetClip(float x, float y, float width, float height)
        {
            int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
            _clip = new UiClip(x0, y0, Math.Max(0, (int)MathF.Ceiling(x + width) - x0), Math.Max(0, (int)MathF.Ceiling(y + height) - y0));
        }

        public void ClearClip() => _clip = null;

        /// <summary>
        /// Texels trimmed from every edge of each source rectangle drawn while set (0 = none). Sprites scaled up with
        /// bilinear filtering sample half a texel past their rectangle, which picks up the neighbouring cell of the
        /// atlas as faint lines along the quad's edges; a half-texel inset keeps the samples inside (name plates).
        /// </summary>
        public float TexelInset { get; set; }

        private readonly GraphicsDevice _gd;
        private readonly Pipeline[] _pipelines;

        /// <summary>The same pipelines with the depth test on (write off), for <see cref="Depth"/>; null without a depth attachment.</summary>
        private readonly Pipeline[]? _depthPipelines;
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
                new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Byte4_Norm),
                new VertexElementDescription("Depth", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float1));

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
            Pipeline[] CreatePipelines(DepthStencilStateDescription depth)
            {
                var pipelines = new Pipeline[blends.Length];
                for (int i = 0; i < blends.Length; i++)
                {
                    pipelines[i] = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
                    {
                        BlendState = blends[i],
                        DepthStencilState = depth,
                        RasterizerState = new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise,
                            depthClipEnabled: false, scissorTestEnabled: true),
                        PrimitiveTopology = PrimitiveTopology.TriangleList,
                        ResourceLayouts = new[] { _uniformLayout, _textureLayout },
                        ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, _shaders),
                        Outputs = outputs,
                    });
                }
                return pipelines;
            }
            _pipelines = CreatePipelines(DepthStencilStateDescription.Disabled);
            if (outputs.DepthAttachment != null)
            {
                // The 3D pass clears depth to 1 and tests LessEqual; the UI tests the same way and never writes it.
                _depthPipelines = CreatePipelines(new DepthStencilStateDescription(depthTestEnabled: true, depthWriteEnabled: false, ComparisonKind.LessEqual));
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
            Depth = null;
            TexelInset = 0;
            Opacity = 1f;
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

            float inset = srcWidth > 2 * TexelInset && srcHeight > 2 * TexelInset ? TexelInset : 0;
            float u0 = (part.SourceX + inset) / texWidth, v0 = (part.SourceY + inset) / texHeight;
            float u1 = (part.SourceX + srcWidth - inset) / texWidth, v1 = (part.SourceY + srcHeight - inset) / texHeight;
            ApplyFlips(part, ref u0, ref v0, ref u1, ref v1);

            Vector2 pTL = Point(part.TopLeft, x, y, scale, stretch), pTR = Point(part.TopRight, x, y, scale, stretch);
            Vector2 pBL = Point(part.BottomLeft, x, y, scale, stretch), pBR = Point(part.BottomRight, x, y, scale, stretch);
            if (inset > 0)
            {
                // Trim the quad by the same share as the source so the sprite keeps its size per texel.
                float fx = inset / srcWidth, fy = inset / srcHeight;
                Vector2 At(float a, float b) => Vector2.Lerp(Vector2.Lerp(pTL, pTR, a), Vector2.Lerp(pBL, pBR, a), b);
                (pTL, pTR, pBL, pBR) = (At(fx, fy), At(1 - fx, fy), At(fx, 1 - fy), At(1 - fx, 1 - fy));
            }

            var tl = new UiVertex { Position = pTL, TexCoord = new Vector2(u0, v0), Color = Pack(part.ColorTopLeft, tint) };
            var tr = new UiVertex { Position = pTR, TexCoord = new Vector2(u1, v0), Color = Pack(part.ColorTopRight, tint) };
            var bl = new UiVertex { Position = pBL, TexCoord = new Vector2(u0, v1), Color = Pack(part.ColorBottomLeft, tint) };
            var br = new UiVertex { Position = pBR, TexCoord = new Vector2(u1, v1), Color = Pack(part.ColorBottomRight, tint) };

            AddQuad(set, part.BlendMode, tl, tr, bl, br);
        }

        /// <summary>
        /// Draws every part of an image with its layout points mapped to screen pixels by <paramref name="transform"/>
        /// (for sprites the client turns or places freely, such as the target cursor).
        /// </summary>
        public void DrawImage(UiImage image, Matrix3x2 transform, UiColor? tint = null)
        {
            if (_library == null) return;
            foreach (var part in image.Parts)
            {
                if (!TryGetTextureSet(part.TextureName, out var set, out float texWidth, out float texHeight)) continue;
                float u0 = part.SourceX / texWidth, v0 = part.SourceY / texHeight;
                float u1 = (part.SourceX + part.SourceWidth) / texWidth, v1 = (part.SourceY + part.SourceHeight) / texHeight;
                ApplyFlips(part, ref u0, ref v0, ref u1, ref v1);
                Vector2 P(UiPoint p) => Vector2.Transform(new Vector2(p.X, p.Y), transform);
                AddQuad(set, part.BlendMode,
                    new UiVertex { Position = P(part.TopLeft), TexCoord = new Vector2(u0, v0), Color = Pack(part.ColorTopLeft, tint) },
                    new UiVertex { Position = P(part.TopRight), TexCoord = new Vector2(u1, v0), Color = Pack(part.ColorTopRight, tint) },
                    new UiVertex { Position = P(part.BottomLeft), TexCoord = new Vector2(u0, v1), Color = Pack(part.ColorBottomLeft, tint) },
                    new UiVertex { Position = P(part.BottomRight), TexCoord = new Vector2(u1, v1), Color = Pack(part.ColorBottomRight, tint) });
            }
        }

        /// <summary>
        /// A part's flags mirror its texture: bit 0 horizontally, bit 1 vertically. The lock-on overlay (windowps image
        /// 212) draws its four corner brackets from one bracket texel rect with flags 0/1/2/3, and its left arrow as the
        /// right arrow with bit 0.
        /// </summary>
        private static void ApplyFlips(UiSpritePart part, ref float u0, ref float v0, ref float u1, ref float v1)
        {
            if ((part.Flags & 0x01) != 0) (u0, u1) = (u1, u0);
            if ((part.Flags & 0x02) != 0) (v0, v1) = (v1, v0);
        }

        private void AddQuad(ResourceSet set, UiBlendMode blend, UiVertex tl, UiVertex tr, UiVertex bl, UiVertex br)
        {
            int first = _vertices.Count;
            bool depthTested = Depth.HasValue && _depthPipelines != null;
            float depth = depthTested ? Depth!.Value : 0.0f;
            tl.Depth = depth; tr.Depth = depth; bl.Depth = depth; br.Depth = depth;
            _vertices.Add(tl); _vertices.Add(tr); _vertices.Add(bl);
            _vertices.Add(tr); _vertices.Add(br); _vertices.Add(bl);

            if (_batches.Count > 0 && ReferenceEquals(_batches[^1].Texture, set) && _batches[^1].Blend == blend && _batches[^1].Clip == _clip
                && _batches[^1].DepthTested == depthTested)
            {
                var last = _batches[^1];
                _batches[^1] = last with { VertexCount = last.VertexCount + 6 };
            }
            else
            {
                _batches.Add(new Batch(set, blend, first, 6, _clip, depthTested));
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

            float inset = TexelInset;
            if (inset > 0 && srcWidth > 2 * inset && srcHeight > 2 * inset)
            {
                float dx = width * inset / srcWidth, dy = height * inset / srcHeight;
                x += dx; y += dy; width -= 2 * dx; height -= 2 * dy;
            }
            else
            {
                inset = 0;
            }
            float u0 = (srcX + inset) / texWidth, v0 = (srcY + inset) / texHeight;
            float u1 = (srcX + srcWidth - inset) / texWidth, v1 = (srcY + srcHeight - inset) / texHeight;
            uint cl = Pack(left, null), cr = Pack(right, null);
            AddQuad(set, blend,
                new UiVertex { Position = new Vector2(x, y), TexCoord = new Vector2(u0, v0), Color = cl },
                new UiVertex { Position = new Vector2(x + width, y), TexCoord = new Vector2(u1, v0), Color = cr },
                new UiVertex { Position = new Vector2(x, y + height), TexCoord = new Vector2(u0, v1), Color = cl },
                new UiVertex { Position = new Vector2(x + width, y + height), TexCoord = new Vector2(u1, v1), Color = cr });
        }

        /// <summary>
        /// Draws a rectangle (texture pixels) of a texture that does not come from the UI library (the log font's glyph
        /// atlas), cached under <paramref name="cacheKey"/>.
        /// </summary>
        public void DrawTextureRegion(string cacheKey, DecodedTexture texture, float srcX, float srcY, float srcWidth, float srcHeight,
            float x, float y, float width, float height, UiColor color)
        {
            if (width <= 0 || height <= 0) return;
            if (!_textures.TryGetValue(cacheKey, out var entry))
            {
                entry = Upload(texture);
                _textures[cacheKey] = entry;
            }
            if (entry is not { } e) return;
            float u0 = srcX / texture.Width, v0 = srcY / texture.Height, u1 = (srcX + srcWidth) / texture.Width, v1 = (srcY + srcHeight) / texture.Height;
            uint c = Pack(color, null);
            AddQuad(e.Set, UiBlendMode.Alpha,
                new UiVertex { Position = new Vector2(x, y), TexCoord = new Vector2(u0, v0), Color = c },
                new UiVertex { Position = new Vector2(x + width, y), TexCoord = new Vector2(u1, v0), Color = c },
                new UiVertex { Position = new Vector2(x, y + height), TexCoord = new Vector2(u0, v1), Color = c },
                new UiVertex { Position = new Vector2(x + width, y + height), TexCoord = new Vector2(u1, v1), Color = c });
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
        /// <paramref name="topGap"/> (layout pixels from the left edge) leaves a break in the top line, where retail
        /// writes a log window's title over it.
        /// </summary>
        public void DrawWindowBorder(float x, float y, float width, float height, float scale, (float Start, float End)? topGap = null)
        {
            float fade = Math.Min(BorderFade, width / 2);
            bool top = true;
            foreach (float edgeY in new[] { y, y + (height - BorderThickness) * scale })
            {
                float h = BorderThickness * scale;
                DrawTextureRect(BorderTexture, 0, 0, fade, BorderThickness, x, edgeY, fade * scale, h, BorderClear, BorderColor, UiBlendMode.Add);
                float from = fade, to = width - fade;
                if (top && topGap is { } gap && gap.End > from && gap.Start < to)
                {
                    DrawBorderSpan(x, edgeY, from, Math.Max(from, gap.Start), h, scale);
                    DrawBorderSpan(x, edgeY, Math.Min(to, gap.End), to, h, scale);
                }
                else
                {
                    DrawBorderSpan(x, edgeY, from, to, h, scale);
                }
                DrawTextureRect(BorderTexture, width - fade, 0, fade, BorderThickness, x + (width - fade) * scale, edgeY, fade * scale, h, BorderColor, BorderClear, UiBlendMode.Add);
                top = false;
            }
        }

        private void DrawBorderSpan(float x, float edgeY, float from, float to, float h, float scale)
        {
            if (to <= from) return;
            DrawTextureRect(BorderTexture, from, 0, to - from, BorderThickness, x + from * scale, edgeY, (to - from) * scale, h, BorderColor, BorderColor, UiBlendMode.Add);
        }

        /// <summary>
        /// Draws a menu window at a placement: the frame's plain images (reference kind 0), then each button's.
        /// Frames are the persistent windows; buttons draw their unselected state. <paramref name="frameWidth"/>
        /// widens or narrows the frame (layout pixels): its right half moves and parts spanning the middle stretch;
        /// <paramref name="frameHeight"/> does the same vertically (its bottom half moves). With
        /// <paramref name="opaqueBody"/> the background below <paramref name="opaqueTop"/> is drawn opaque (menus keep
        /// their translucent title band; 0 makes the whole body opaque). <paramref name="topBorderGap"/> breaks the
        /// top border line for a title.
        /// </summary>
        public void DrawMenu(UiMenuDefinition menu, StockUiPlacement placement, bool includeButtons = true, float? frameWidth = null,
            Predicate<UiSpritePart>? excludeFramePart = null, bool opaqueBody = false, float? frameHeight = null,
            float opaqueTop = MenuBandHeight, (float Start, float End)? topBorderGap = null, UiImage? frameImage = null)
        {
            if (_library == null || placement.Hidden) return;
            var stretch = frameWidth is { } w ? UiStretch.Horizontal(menu.Frame.Width, w) : default;
            if (frameHeight is { } fh)
            {
                stretch = frameWidth is null
                    ? new UiStretch(float.MaxValue, 0, menu.Frame.Height * 0.5f, fh - menu.Frame.Height)
                    : stretch with { PivotY = menu.Frame.Height * 0.5f, ExtraY = fh - menu.Frame.Height };
            }
            float borderWidth = frameWidth ?? menu.Frame.Width;
            float borderHeight = frameHeight ?? menu.Frame.Height;
            // A client-built frame image (a composed command menu) stands in for the frame's kind-0 references.
            if (frameImage != null)
            {
                DrawFrameImage(frameImage);
            }
            else
            {
                foreach (var shape in menu.Frame.Shapes)
                {
                    if (shape.Kind == 0 && _library.TryGetImage(shape, out var image)) DrawFrameImage(image);
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

            void DrawFrameImage(UiImage image)
            {
                // Background ("newtex") parts, then the client's border lines, then the rest (title and its band).
                bool hasBackground = false;
                foreach (var part in image.Parts)
                {
                    if (!IsBackground(part)) continue;
                    DrawPart(part, placement.X, placement.Y, placement.Scale, null, stretch);
                    // Menu windows: retail's body below the title band is opaque (nothing shows through it, only
                    // the band lets the scene through, per captures 2026-09-26), although the DAT authors the same
                    // 0x40 -> 0x7F alpha gradient as the translucent HUD windows. The body is drawn again with full
                    // alpha and the authored colour gradient, leaving the band as authored.
                    if (opaqueBody) DrawBodyPlate(part, placement.X, placement.Y, placement.Scale, stretch, opaqueTop);
                    hasBackground = true;
                }
                if (hasBackground) DrawWindowBorder(placement.X, placement.Y, borderWidth, borderHeight, placement.Scale, topBorderGap);
                foreach (var part in image.Parts)
                {
                    if (IsBackground(part) || excludeFramePart?.Invoke(part) == true) continue;
                    DrawPart(part, placement.X, placement.Y, placement.Scale, null, stretch);
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
            _commandList.SetFullScissorRects();
            _commandList.SetVertexBuffer(0, _vertexBuffer);
            (UiBlendMode Blend, bool DepthTested)? current = null;
            UiClip? currentClip = null;
            foreach (var batch in _batches)
            {
                if (current != (batch.Blend, batch.DepthTested))
                {
                    var pipelines = batch.DepthTested ? _depthPipelines! : _pipelines;
                    _commandList.SetPipeline(pipelines[(int)batch.Blend]);
                    _commandList.SetGraphicsResourceSet(0, _uniformSet);
                    current = (batch.Blend, batch.DepthTested);
                }
                if (batch.Clip != currentClip)
                {
                    if (batch.Clip is { } clip)
                    {
                        int cx = Math.Clamp(clip.X, 0, (int)width), cy = Math.Clamp(clip.Y, 0, (int)height);
                        int cw = Math.Clamp(clip.X + clip.Width, cx, (int)width) - cx, ch = Math.Clamp(clip.Y + clip.Height, cy, (int)height) - cy;
                        _commandList.SetScissorRect(0, (uint)cx, (uint)cy, (uint)Math.Max(1, cw), (uint)Math.Max(1, ch));
                    }
                    else
                    {
                        _commandList.SetFullScissorRects();
                    }
                    currentClip = batch.Clip;
                }
                _commandList.SetGraphicsResourceSet(1, batch.Texture);
                _commandList.Draw((uint)batch.VertexCount, 1, (uint)batch.FirstVertex, 0);
            }
            _commandList.End();
            _gd.SubmitCommands(_commandList);
        }

        private static bool IsBackground(UiSpritePart part) =>
            UiResourceLibrary.TrimResourceName(part.TextureName).Equals("newtex", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Draws the part of a window background below <paramref name="opaqueTop"/> (layout pixels) opaque, with the
        /// part's colour gradient interpolated at the split so the band and the body meet seamlessly.
        /// </summary>
        private void DrawBodyPlate(UiSpritePart part, float x, float y, float scale, UiStretch stretch, float opaqueTop)
        {
            if (_library == null || !TryGetTextureSet(part.TextureName, out var set, out float texWidth, out float texHeight)) return;
            float quadWidth = part.TopRight.X - part.TopLeft.X, quadHeight = part.BottomLeft.Y - part.TopLeft.Y;
            if (quadHeight <= opaqueTop || quadWidth <= 0) return;
            float srcWidth = part.SourceWidth, srcHeight = part.SourceHeight;
            if (stretch.ExtraX != 0 && part.TopLeft.X < stretch.PivotX && part.TopRight.X >= stretch.PivotX)
                srcWidth += stretch.ExtraX * srcWidth / quadWidth;
            if (stretch.ExtraY != 0 && part.TopLeft.Y < stretch.PivotY && part.BottomLeft.Y >= stretch.PivotY)
                srcHeight += stretch.ExtraY * srcHeight / quadHeight;
            float u0 = part.SourceX / texWidth, v0 = part.SourceY / texHeight;
            float u1 = (part.SourceX + srcWidth) / texWidth, v1 = (part.SourceY + srcHeight) / texHeight;
            ApplyFlips(part, ref u0, ref v0, ref u1, ref v1);

            var tl = Point(part.TopLeft, x, y, scale, stretch);
            var tr = Point(part.TopRight, x, y, scale, stretch);
            var bl = Point(part.BottomLeft, x, y, scale, stretch);
            var br = Point(part.BottomRight, x, y, scale, stretch);
            float t = Math.Clamp(opaqueTop * scale / Math.Max(1, bl.Y - tl.Y), 0, 1);
            float vMid = v0 + (v1 - v0) * t;
            AddQuad(set, part.BlendMode,
                new UiVertex { Position = Vector2.Lerp(tl, bl, t), TexCoord = new Vector2(u0, vMid), Color = Pack(LerpOpaque(part.ColorTopLeft, part.ColorBottomLeft, t), null) },
                new UiVertex { Position = Vector2.Lerp(tr, br, t), TexCoord = new Vector2(u1, vMid), Color = Pack(LerpOpaque(part.ColorTopRight, part.ColorBottomRight, t), null) },
                new UiVertex { Position = bl, TexCoord = new Vector2(u0, v1), Color = Pack(LerpOpaque(part.ColorBottomLeft, part.ColorBottomLeft, 0), null) },
                new UiVertex { Position = br, TexCoord = new Vector2(u1, v1), Color = Pack(LerpOpaque(part.ColorBottomRight, part.ColorBottomRight, 0), null) });
        }

        private static UiColor LerpOpaque(UiColor a, UiColor b, float t) =>
            new((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t), 0x80);

        private static Vector2 Point(UiPoint p, float x, float y, float scale, UiStretch stretch)
        {
            float px = p.X >= stretch.PivotX ? p.X + stretch.ExtraX : p.X;
            float py = p.Y >= stretch.PivotY ? p.Y + stretch.ExtraY : p.Y;
            return new Vector2(x + px * scale, y + py * scale);
        }

        private uint Pack(UiColor c, UiColor? tint)
        {
            int r = c.R, g = c.G, b = c.B, a = c.A;
            if (tint is { } t)
            {
                r = Math.Min(255, r * t.R / 0x80);
                g = Math.Min(255, g * t.G / 0x80);
                b = Math.Min(255, b * t.B / 0x80);
                a = Math.Min(255, a * t.A / 0x80);
            }
            if (Opacity < 1f) a = (int)MathF.Round(a * Math.Max(0f, Opacity));
            return (uint)(r | (g << 8) | (b << 16) | (a << 24));
        }

        /// <summary>A 1 x 1 white texel for solid quads.</summary>
        private static readonly DecodedTexture WhiteTexel = new("gordian:white", 1, 1, new byte[] { 255, 255, 255, 255 });

        /// <summary>
        /// Multiplies the whole screen drawn so far by a colour (1 = unchanged, 0 = black), for an event's screen fades:
        /// a black quad whose opacity darkens toward the colour, or a white one added for a colour above 1. A tinted
        /// colour is drawn as its grey average.
        /// </summary>
        public void DrawScreenTint(float width, float height, Vector3 multiplier)
        {
            float level = (multiplier.X + multiplier.Y + multiplier.Z) / 3f;
            if (MathF.Abs(level - 1f) < 0.002f || width <= 0 || height <= 0) return;
            if (!_textures.TryGetValue(WhiteTexel.Name, out var entry))
            {
                entry = Upload(WhiteTexel);
                _textures[WhiteTexel.Name] = entry;
            }
            if (entry is not { } e) return;
            // The shader doubles the vertex colour: an alpha byte of 127.5 is full opacity.
            float alpha = level < 1f ? 1f - Math.Max(0f, level) : Math.Min(1f, level - 1f);
            byte shade = level < 1f ? (byte)0 : (byte)0x80;
            uint c = (uint)(shade | (shade << 8) | (shade << 16) | ((int)MathF.Round(alpha * 127.5f) << 24));
            AddQuad(e.Set, level < 1f ? UiBlendMode.Alpha : UiBlendMode.Add,
                new UiVertex { Position = new Vector2(0, 0), TexCoord = Vector2.Zero, Color = c },
                new UiVertex { Position = new Vector2(width, 0), TexCoord = Vector2.Zero, Color = c },
                new UiVertex { Position = new Vector2(0, height), TexCoord = Vector2.Zero, Color = c },
                new UiVertex { Position = new Vector2(width, height), TexCoord = Vector2.Zero, Color = c });
        }

        /// <summary>
        /// Adds <paramref name="color"/> (0-1 per channel) over the whole screen: an event's 0x72 flash or white fade
        /// (<see cref="Gordian.Core.Events.EventPresentation.SceneFlash"/>). Nothing is drawn while it is black.
        /// </summary>
        public void DrawScreenFlash(float width, float height, Vector3 color)
        {
            if (color.X + color.Y + color.Z < 0.004f || width <= 0 || height <= 0) return;
            if (!_textures.TryGetValue(WhiteTexel.Name, out var entry))
            {
                entry = Upload(WhiteTexel);
                _textures[WhiteTexel.Name] = entry;
            }
            if (entry is not { } e) return;
            // The shader doubles the vertex colour: 127.5 is full scale.
            static uint Channel(float value) => (uint)MathF.Round(Math.Clamp(value, 0f, 1f) * 127.5f);
            uint c = Channel(color.X) | (Channel(color.Y) << 8) | (Channel(color.Z) << 16) | (0x80u << 24);
            AddQuad(e.Set, UiBlendMode.Add,
                new UiVertex { Position = new Vector2(0, 0), TexCoord = Vector2.Zero, Color = c },
                new UiVertex { Position = new Vector2(width, 0), TexCoord = Vector2.Zero, Color = c },
                new UiVertex { Position = new Vector2(0, height), TexCoord = Vector2.Zero, Color = c },
                new UiVertex { Position = new Vector2(width, height), TexCoord = Vector2.Zero, Color = c });
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
            if (_depthPipelines != null) foreach (var pipeline in _depthPipelines) pipeline.Dispose();
            foreach (var shader in _shaders) shader.Dispose();
            _uniformSet.Dispose();
            _uniformBuffer.Dispose();
            _sampler.Dispose();
            _textureLayout.Dispose();
            _uniformLayout.Dispose();
            GordianLog.Debug("UI", "Stock UI renderer disposed.");
        }
    }

    /// <summary>A screen-space scissor rectangle (pixels) for the stock UI renderer's batches.</summary>
    public readonly record struct UiClip(int X, int Y, int Width, int Height);
}
