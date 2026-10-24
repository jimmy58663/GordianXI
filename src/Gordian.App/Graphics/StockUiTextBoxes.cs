// src/Gordian.App/Graphics/StockUiTextBoxes.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Resources.Tables;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws text into a window's text box: lines laid out by <see cref="StockUiTextLayout"/> (wrapped to the box's width by the
    /// font that draws them), drawn one under the other and clipped to the box, so no line of an item description, key item
    /// text or info window can run past its frame. The info windows use the log font (16 px lines) when it is loaded, the
    /// menu font otherwise.
    /// <para>
    /// An element icon in the text (<see cref="ElementGlyphs"/>: an item description's 0xEF 0x1F ... 0xEF 0x26) is drawn as the
    /// element's own 16 x 16 icon from the UI DAT (<c>elfire</c>, <c>elice</c>, <c>elwind</c>, <c>elearth</c>, <c>elthunde</c>,
    /// <c>elwater</c>, <c>elholy</c>, <c>eldark</c>) on the line, as retail draws "[icon]+20" (#417), and measured as
    /// <see cref="IconAdvance"/> so the wrap leaves room for it.
    /// </para>
    /// </summary>
    public static class StockUiTextBoxes
    {
        private static readonly UiColor White = new(0x80, 0x80, 0x80, 0x80);

        /// <summary>The element icon textures in <see cref="ElementGlyphs"/> order (Fire ... Dark).</summary>
        private static readonly string[] ElementIconTextures = { "elfire", "elice", "elwind", "elearth", "elthunde", "elwater", "elholy", "eldark" };

        /// <summary>
        /// The pen advance of an element icon (layout px): the 16 px icon, a log row tall (the Carrier's Sash matched retail
        /// in-game, 2026-10-24).
        /// </summary>
        public const float IconAdvance = 16;

        /// <summary>The measure of the font text boxes draw with: the log font when loaded, else the menu font (layout px), element icons included.</summary>
        public static Func<string, float> Measure(UiFont font, StockUiLogFont? logFont) => t => MeasureLine(font, logFont, t);

        /// <summary>The line pitch of the font text boxes draw with (layout px).</summary>
        public static float LinePitch(UiFont font, StockUiLogFont? logFont) => logFont != null ? StockUiLogFont.CellHeight : font.LineHeight;

        /// <summary>The width of one line in the font text boxes draw with (layout px), element icons included.</summary>
        public static float MeasureLine(UiFont font, StockUiLogFont? logFont, string text)
        {
            if (!ElementGlyphs.HasIcons(text)) return logFont != null ? logFont.MeasureWidth(text) : font.MeasureWidth(text);
            float width = 0;
            int start = 0;
            for (int i = 0; i <= text.Length; i++)
            {
                if (i < text.Length && ElementGlyphs.ElementOf(text[i]) < 0) continue;
                var run = text.AsSpan(start, i - start);
                width += logFont != null ? logFont.MeasureWidth(run) : font.MeasureWidth(run);
                if (i < text.Length) width += IconAdvance;
                start = i + 1;
            }
            return width;
        }

        /// <summary>
        /// Draws one line, unclipped, at (<paramref name="x"/>, <paramref name="y"/>) (screen px) in the log font when it is
        /// loaded, else the menu font: the one "log font, else menu font" line the help bars, footers and comments share.
        /// </summary>
        public static void DrawLine(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, string text, float x, float y, float scale,
            UiColor? color = null)
        {
            if (!ElementGlyphs.HasIcons(text))
            {
                DrawRun(renderer, font, logFont, text, x, y, scale, color);
                return;
            }
            // Text runs between the icons, each icon drawn a row tall where the pen stands.
            float pen = x;
            int start = 0;
            float rowHeight = LinePitch(font, logFont);
            for (int i = 0; i <= text.Length; i++)
            {
                int element = i < text.Length ? ElementGlyphs.ElementOf(text[i]) : -1;
                if (i < text.Length && element < 0) continue;
                pen = DrawRun(renderer, font, logFont, text.AsSpan(start, i - start), pen, y, scale, color);
                if (element >= 0)
                {
                    float size = Math.Min(IconAdvance, rowHeight);
                    renderer.DrawTextureRect(ElementIconTextures[element], 0, 0, 16, 16, pen, y + (rowHeight - size) * 0.5f * scale,
                        size * scale, size * scale, White);
                    pen += IconAdvance * scale;
                }
                start = i + 1;
            }
        }

        /// <summary>A run of plain text in the log font or the menu font; returns the pen after it.</summary>
        private static float DrawRun(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, ReadOnlySpan<char> text, float x, float y, float scale,
            UiColor? color)
        {
            if (text.IsEmpty) return x;
            return logFont != null ? logFont.Draw(renderer, text, x, y, scale, color ?? White) : renderer.DrawText(font, text, x, y, scale, color);
        }

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
                DrawLine(renderer, font, logFont, lines[i], x, y + i * pitch * scale, scale, color);
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
