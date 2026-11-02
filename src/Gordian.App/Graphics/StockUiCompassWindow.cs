// src/Gordian.App/Graphics/StockUiCompassWindow.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>One radar dot: its offset from the ring's centre (layout pixels) and what it stands for.</summary>
    public readonly record struct StockUiCompassDot(Vector2 Offset, StockUiCompass.DotKind Kind);

    /// <summary>
    /// Draws retail's compass above the log (#485; layout and evidence on <see cref="StockUiCompass"/>): the menu DAT's
    /// <c>compass</c> ring flattened and turned with the camera, the radar dots, the four letters, then the day's orb, the
    /// time and the map square under it.
    /// </summary>
    public static class StockUiCompassWindow
    {
        /// <summary>
        /// The NPC and player dots' colours (half scale), from the turned capture's green NPC dot (63, 139, 94) and blue
        /// player dot (51, 61, 93) over the ring, taken as drawn at the DAT dot's alpha (0x60, 75%). Monsters use the DAT's
        /// own red dot (#5) as authored.
        /// </summary>
        public static readonly UiColor NpcDotColor = new(0x20, 0x52, 0x33, 0x60), PlayerDotColor = new(0x19, 0x20, 0x35, 0x60);

        /// <summary>How far below fontshp's glyph top its digits' bottom row ends (measured offscreen at full size).</summary>
        private const float DigitBottom = 10;

        // A 4 x 4 white dot with soft corners, tinted per kind (the captures' dots are 4 px with dimmer corners).
        private static readonly DecodedTexture Dot = MakeDot();

        private static DecodedTexture MakeDot()
        {
            var rgba = new byte[4 * 4 * 4];
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    int i = (y * 4 + x) * 4;
                    rgba[i] = rgba[i + 1] = rgba[i + 2] = 255;
                    bool corner = (x == 0 || x == 3) && (y == 0 || y == 3);
                    rgba[i + 3] = corner ? (byte)96 : (byte)255;
                }
            }
            return new DecodedTexture("gordian:compass-dot", 4, 4, rgba);
        }

        /// <summary>
        /// Draws the compass with its text line's bottom at <paramref name="lineBottom"/> (screen pixels), at
        /// <paramref name="scale"/> screen pixels per layout pixel. Returns false when the DAT has no <c>compass</c> group.
        /// </summary>
        public static bool Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, float cameraBearing,
            int dayOfWeek, string line, IReadOnlyList<StockUiCompassDot> dots, float lineBottom, float scale)
        {
            if (!library.TryGetGroup(StockUiCompass.GroupName, out var group) || group.Images.Count <= StockUiCompass.DotImage) return false;
            var center = new Vector2(StockUiCompass.CenterX * scale, lineBottom - StockUiCompass.CenterRise * scale);

            renderer.DrawImage(group.Images[StockUiCompass.RingImage], StockUiCompass.RingTransform(center, cameraBearing, scale));

            float dot = StockUiCompass.DotSize * scale;
            foreach (var d in dots)
            {
                var at = center + d.Offset * scale;
                if (d.Kind == StockUiCompass.DotKind.Monster)
                {
                    // The DAT dot is 6 px with its outline; drawn so its 4 px middle matches the others.
                    renderer.DrawImage(group.Images[StockUiCompass.DotImage], at.X, at.Y, scale);
                }
                else
                {
                    var color = d.Kind == StockUiCompass.DotKind.Player ? PlayerDotColor : NpcDotColor;
                    renderer.DrawTexture(Dot.Name, Dot, at.X - dot / 2, at.Y - dot / 2, dot, dot, color);
                }
            }

            foreach (var (image, bearing) in StockUiCompass.Letters)
            {
                var at = center + StockUiCompass.Project(bearing, StockUiCompass.LetterRadius, cameraBearing) * scale;
                renderer.DrawImage(group.Images[image], at.X, at.Y, scale);
            }

            var (cellX, cellY) = StockUiCompass.DayOrbCell(dayOfWeek);
            float orb = StockUiCompass.OrbSize * scale;
            renderer.DrawTextureRect("colorbal", cellX, cellY, 16, 16, StockUiCompass.OrbX * scale,
                lineBottom - StockUiCompass.OrbRise * scale - orb * 0.5f, orb, orb, new UiColor(0x80, 0x80, 0x80, 0x80));
            // fontshp's digits run from 1 px to 10 px below the glyph's top; their bottom row sits on the line's bottom.
            float textScale = scale * StockUiCompass.TextScale;
            renderer.DrawText(font, line, StockUiCompass.TextX * scale, lineBottom - DigitBottom * textScale, textScale);
            return true;
        }
    }
}
