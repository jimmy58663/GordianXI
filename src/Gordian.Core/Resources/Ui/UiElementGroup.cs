// src/Gordian.Core/Resources/Ui/UiElementGroup.cs
using System;
using System.Collections.Generic;

namespace Gordian.Core.Resources.Ui
{
    /// <summary>
    /// A point of a UI sprite's destination quad, in layout pixels relative to the image origin.
    /// </summary>
    public readonly record struct UiPoint(short X, short Y);

    /// <summary>
    /// A vertex colour stored as bytes R, G, B, A (confirmed by the HP-number colours: 7F 7F 40 7F yellow,
    /// 7F 60 40 7F orange, 7F 40 40 7F red). UI colours use the client's half-scale convention: 0x80 is full
    /// intensity / fully opaque, so 0x7F7F7F7F draws the texel unchanged.
    /// </summary>
    public readonly record struct UiColor(byte R, byte G, byte B, byte A)
    {
        public static readonly UiColor Neutral = new(0x80, 0x80, 0x80, 0x80);
    }

    /// <summary>
    /// Blend mode of a UI sprite part.
    /// </summary>
    public enum UiBlendMode : byte
    {
        Alpha = 0,
        Add = 1,
        Subtract = 2,
    }

    /// <summary>
    /// One textured quad of a UI image: a destination quad (corners TL, TR, BL, BR), a source rectangle on the named
    /// texture, and per-corner colours.
    /// </summary>
    public sealed class UiSpritePart
    {
        public UiPoint TopLeft { get; init; }
        public UiPoint TopRight { get; init; }
        public UiPoint BottomLeft { get; init; }
        public UiPoint BottomRight { get; init; }

        public ushort SourceWidth { get; init; }
        public ushort SourceHeight { get; init; }
        public ushort SourceX { get; init; }
        public ushort SourceY { get; init; }

        /// <summary>
        /// Byte +0x18 of the part (1 on the finger cursor, 0 elsewhere); meaning not yet decoded.
        /// </summary>
        public byte Flags { get; init; }

        public UiColor ColorTopLeft { get; init; }
        public UiColor ColorTopRight { get; init; }
        public UiColor ColorBottomLeft { get; init; }
        public UiColor ColorBottomRight { get; init; }

        /// <summary>
        /// Bytes +0x29..+0x2C preceding the texture name (little-endian u32; bytes e.g. 01 00 01 01). The second byte
        /// is the blend mode (<see cref="BlendMode"/>); the others are not yet decoded.
        /// </summary>
        public uint TextureAttributes { get; init; }

        /// <summary>
        /// How the part combines with what is behind it: the attributes' second byte. 2 darkens (text shadows, the
        /// band behind window titles, separator lines: 6.4k parts), matching the dark title band of a Windower
        /// capture; 1 is taken to be additive; 0 is ordinary alpha blending.
        /// </summary>
        public UiBlendMode BlendMode => (UiBlendMode)((TextureAttributes >> 8) & 0xFF) is var mode && mode <= UiBlendMode.Subtract ? mode : UiBlendMode.Alpha;

        /// <summary>
        /// The sampled texture's 16-character resource id (8-character category + 8-character name, space padded,
        /// category case varies: "MENU    yubi    ", "menu    hfr1    ").
        /// </summary>
        public string TextureName { get; init; } = string.Empty;
    }

    /// <summary>
    /// One image of a UI element group: the parts drawn together (a window frame, a button label, a font glyph).
    /// </summary>
    public sealed class UiImage
    {
        public IReadOnlyList<UiSpritePart> Parts { get; init; } = Array.Empty<UiSpritePart>();
    }

    /// <summary>
    /// Decoded FFXI DAT Section 0x31 UiElementGroup: a named set of images built from textured quads.
    /// Menus (Section 0x30) reference its images by group name and image index.
    /// </summary>
    public sealed class UiElementGroup
    {
        /// <summary>Section FourCC (e.g. "wind").</summary>
        public string DatId { get; init; } = string.Empty;

        /// <summary>Resource id category, trimmed (e.g. "menu", "font", "anc").</summary>
        public string Category { get; init; } = string.Empty;

        /// <summary>Resource id name, trimmed (e.g. "windowps", "fontshp", "yubi").</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>The textures the group's parts sample, as 16-character resource ids.</summary>
        public IReadOnlyList<string> TextureNames { get; init; } = Array.Empty<string>();

        public IReadOnlyList<UiImage> Images { get; init; } = Array.Empty<UiImage>();

        public override string ToString() => $"UiElementGroup [{Category}/{Name}] ({Images.Count} images)";
    }
}
