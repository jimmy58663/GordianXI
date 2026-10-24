// src/Gordian.App/Graphics/ButtonGlyphArt.cs
using System;
using System.Collections.Concurrent;
using System.Reflection;
using Gordian.Core.Resources.Graphics;
using Gordian.Engine.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// The button pictures of the menu key hints (#360): the Xbox A / B / X / Y and the PlayStation Triangle / Circle / Square /
    /// Cross from the free Xbox and PS4 button packs by Arks (CC BY 4.0, see THIRD_PARTY_NOTICES.md), the maintainer's picks
    /// (Xbox variant 3, PS4 variant 2). Only these eight glyphs ship, resized from the 480 px originals to 48 and 96 px by a box
    /// filter and embedded in the assembly (<c>Assets/ButtonGlyphs</c>); the size nearest above the drawn size is used. A glyph
    /// that cannot be read leaves the hint to the drawn fallback (the Xbox discs, the DAT symbols).
    /// </summary>
    public static class ButtonGlyphArt
    {
        /// <summary>The hint glyph's size in layout pixels (the DAT slot is 16; this fits the room before the ":" and above the first row).</summary>
        public const float Size = 20;

        /// <summary>Where the glyph sits in the frame: its top-left, so it spans x 1 to 21 and y 2 to 22.</summary>
        public const float Left = 1, Top = 2;

        private static readonly ConcurrentDictionary<string, DecodedTexture?> Cache = new();

        /// <summary>The embedded art for <paramref name="hint"/> sized for <paramref name="drawnPixels"/>, with the key to cache its upload under.</summary>
        public static bool TryGet(StockUiHint hint, float drawnPixels, out DecodedTexture texture, out string cacheKey)
        {
            texture = null!;
            cacheKey = string.Empty;
            string? name = hint.HasXboxFace ? "xbox_" + char.ToLowerInvariant(hint.XboxFace)
                : hint.HasSprite ? SymbolOf(hint.SpriteX, hint.SpriteY) : null;
            if (name == null) return false;
            int px = drawnPixels <= 48 ? 48 : 96;
            cacheKey = $"buttonart:{name}_{px}";
            var loaded = Cache.GetOrAdd(cacheKey, _ => Load($"{name}_{px}.png"));
            if (loaded == null) return false;
            texture = loaded;
            return true;
        }

        /// <summary>The PlayStation symbol of a <c>marubatu</c> cell (see <see cref="StockUiButtonHints.FaceSprite"/>).</summary>
        private static string? SymbolOf(int x, int y) => (x, y) switch
        {
            (16, 16) => "ps_triangle",
            (0, 0) => "ps_circle",
            (0, 16) => "ps_square",
            (16, 0) => "ps_cross",
            _ => null,
        };

        private static DecodedTexture? Load(string file)
        {
            using var stream = typeof(ButtonGlyphArt).Assembly.GetManifestResourceStream("ButtonGlyphs." + file);
            if (stream == null) return null;
            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            return PngRgba.Decode(bytes) is { } png ? new DecodedTexture("buttonart", png.Width, png.Height, png.Rgba) : null;
        }
    }
}
