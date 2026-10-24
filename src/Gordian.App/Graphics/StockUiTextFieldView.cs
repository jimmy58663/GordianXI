// src/Gordian.App/Graphics/StockUiTextFieldView.cs
using System;
using System.Diagnostics;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws a <see cref="StockUiTextField"/> being typed in (#400): its text scrolled to keep the caret in view and the
    /// blinking caret. The one drawing the macro editor and an equipment set's Change Name share.
    /// </summary>
    public static class StockUiTextFieldView
    {
        private const double CaretBlinkSeconds = 0.5;
        private static readonly UiColor White = new(0x80, 0x80, 0x80, 0x80);

        /// <summary>
        /// Draws the field's text with its left edge at <paramref name="x"/> and its top at <paramref name="y"/> (screen pixels,
        /// <paramref name="scale"/> the UI scale); <paramref name="maxWidth"/> is the room in authored pixels, past which the
        /// text scrolls left so the caret stays in view.
        /// </summary>
        public static void Draw(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, StockUiTextField field, float x, float y, float scale,
            long timestamp, float maxWidth = float.MaxValue)
        {
            string text = field.Text;
            int caret = Math.Clamp(field.Caret, 0, text.Length);
            Func<string, float> measure = logFont != null ? t => logFont.MeasureWidth(t) : t => font.MeasureWidth(t);
            float caretX = measure(text[..caret]);
            float shift = caretX > maxWidth ? caretX - maxWidth : 0;
            DrawLine(renderer, font, logFont, text, x - shift * scale, y, scale);
            bool on = (timestamp / (long)(Stopwatch.Frequency * CaretBlinkSeconds)) % 2 == 0;
            if (on)
            {
                float height = logFont != null ? StockUiLogFont.CellHeight - 2 : font.LineHeight;
                renderer.DrawTextureRect("colorbal", 7, 23, 1, 1, x + (caretX - shift) * scale, y + 1 * scale, 1 * scale, height * scale, White);
            }
        }

        private static void DrawLine(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, string text, float x, float y, float scale)
        {
            if (text.Length == 0) return;
            if (logFont != null) logFont.Draw(renderer, text, x, y, scale, White);
            else renderer.DrawText(font, text, x, y + 2 * scale, scale);
        }
    }
}
