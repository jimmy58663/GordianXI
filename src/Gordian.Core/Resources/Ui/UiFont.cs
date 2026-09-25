// src/Gordian.Core/Resources/Ui/UiFont.cs
using System;

namespace Gordian.Core.Resources.Ui
{
    /// <summary>
    /// A bitmap font backed by a UI element group whose images are glyphs in character order: image i draws
    /// character <see cref="FirstCharacter"/> + i (the "fontshp" group starts at ASCII 0x20, space, and continues
    /// past 0x7E into special icons). Each glyph's quad already carries its offset within the line, and its right
    /// edge is the pen advance (retail labels in "windowps" place consecutive glyphs edge to edge).
    /// </summary>
    public sealed class UiFont
    {
        public const string DefaultGroupName = "fontshp";

        public UiElementGroup Group { get; }
        public int FirstCharacter { get; }

        /// <summary>Height of a text line in layout pixels (the tallest glyph's bottom edge).</summary>
        public int LineHeight { get; }

        public UiFont(UiElementGroup group, int firstCharacter = 0x20)
        {
            Group = group ?? throw new ArgumentNullException(nameof(group));
            FirstCharacter = firstCharacter;

            int lineHeight = 0;
            int last = Math.Min(group.Images.Count, 0x7F - firstCharacter);
            for (int i = 0; i < last; i++)
            {
                foreach (var part in group.Images[i].Parts) lineHeight = Math.Max(lineHeight, part.BottomRight.Y);
            }
            LineHeight = lineHeight;
        }

        /// <summary>Loads the stock UI font from a library, or null when the group is missing.</summary>
        public static UiFont? FromLibrary(UiResourceLibrary library) =>
            library.TryGetGroup(DefaultGroupName, out var group) ? new UiFont(group) : null;

        public bool TryGetGlyph(char c, out UiImage glyph)
        {
            int index = c - FirstCharacter;
            if (index >= 0 && index < Group.Images.Count && Group.Images[index].Parts.Count > 0)
            {
                glyph = Group.Images[index];
                return true;
            }
            glyph = null!;
            return false;
        }

        /// <summary>Pen advance of one glyph in layout pixels (0 when the character has no glyph).</summary>
        public int GetAdvance(char c)
        {
            if (!TryGetGlyph(c, out var glyph)) return 0;
            int advance = 0;
            foreach (var part in glyph.Parts) advance = Math.Max(advance, part.TopRight.X);
            return advance;
        }

        /// <summary>Width of a single line of text in layout pixels.</summary>
        public int MeasureWidth(ReadOnlySpan<char> text)
        {
            int width = 0;
            foreach (char c in text) width += GetAdvance(c);
            return width;
        }
    }
}
