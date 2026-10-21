// src/Gordian.App/Graphics/StockUiTextBoxes.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws text into a window's text box: lines laid out by <see cref="StockUiTextLayout"/> (wrapped to the box's width by the
    /// font that draws them), drawn one under the other and clipped to the box, so no line of an item description, key item
    /// text or info window can run past its frame. The info windows use the log font (16 px lines) when it is loaded, the
    /// menu font otherwise.
    /// </summary>
    public static class StockUiTextBoxes
    {
        private static readonly UiColor White = new(0x80, 0x80, 0x80, 0x80);

        /// <summary>The measure of the font text boxes draw with: the log font when loaded, else the menu font (layout px).</summary>
        public static Func<string, float> Measure(UiFont font, StockUiLogFont? logFont) =>
            logFont != null ? t => logFont.MeasureWidth(t) : t => font.MeasureWidth(t);

        /// <summary>The line pitch of the font text boxes draw with (layout px).</summary>
        public static float LinePitch(UiFont font, StockUiLogFont? logFont) => logFont != null ? StockUiLogFont.CellHeight : font.LineHeight;

        /// <summary>
        /// Draws <paramref name="lines"/> from (<paramref name="x"/>, <paramref name="y"/>) (screen px) one pitch apart, clipped to
        /// a box <paramref name="width"/> x <paramref name="height"/> layout px wide and tall at scale <paramref name="scale"/>;
        /// lines past the box's height are left out. <paramref name="linePitch"/> overrides the font's pitch (the shop info's 14 px).
        /// </summary>
        public static void DrawLines(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, IReadOnlyList<string> lines,
            float x, float y, float width, float height, float scale, UiColor? color = null, float? linePitch = null)
        {
            if (lines.Count == 0 || width <= 0 || height <= 0) return;
            float pitch = linePitch ?? LinePitch(font, logFont);
            int fit = Math.Min(lines.Count, Math.Max(1, StockUiTextLayout.LinesIn(height, pitch)));
            renderer.SetClip(x, y, width * scale, height * scale);
            for (int i = 0; i < fit; i++)
            {
                float ly = y + i * pitch * scale;
                if (logFont != null) logFont.Draw(renderer, lines[i], x, ly, scale, color ?? White);
                else renderer.DrawText(font, lines[i], x, ly, scale, color);
            }
            renderer.ClearClip();
        }

        /// <summary>
        /// Wraps <paramref name="text"/> to the box (<see cref="StockUiTextLayout.Wrap"/>) and draws it there
        /// (<see cref="DrawLines"/>); returns the lines drawn.
        /// </summary>
        public static int DrawWrapped(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, string? text,
            float x, float y, float width, float height, float scale, UiColor? color = null, bool keepBlankLines = false)
        {
            int maxLines = Math.Max(1, StockUiTextLayout.LinesIn(height, LinePitch(font, logFont)));
            var lines = StockUiTextLayout.Wrap(text, Measure(font, logFont), width, maxLines, keepBlankLines);
            DrawLines(renderer, font, logFont, lines, x, y, width, height, scale, color);
            return lines.Count;
        }
    }
}
