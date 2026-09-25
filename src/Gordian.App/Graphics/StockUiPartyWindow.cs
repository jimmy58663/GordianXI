// src/Gordian.App/Graphics/StockUiPartyWindow.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// One party window row's contents.
    /// </summary>
    /// <param name="HpPercent">0-100.</param>
    public readonly record struct PartyRowVitals(string Name, int Hp, int HpPercent, int Mp, int MpPercent, int Tp, bool IsLeader);

    /// <summary>
    /// Draws the party window rows (name, HP/MP numbers and gauges, leader marker) inside a "ptw0".."ptw6" frame.
    /// <para>
    /// The DAT authors only the frame and one 106 x 14 button per row; the row contents are composed by the client
    /// from the "gauge" texture and the "fontshp" font. Placement, sizes and gauge tints were measured from a
    /// Windower capture at 1:1 scale (2026-09-25, 2560 x 1440): the name sits at the row origin; the HP number is
    /// right-aligned at row x + 87 on the name line and the MP number at row x + 96, 10 pixels lower; text is
    /// fontshp at 7/8 size ("Tarudra" 49.5 px vs 57 authored) (glyphs carry their own dark outline), names cut to fit with ".."; the HP gauge is a
    /// 5 x 5 knob at row x + 25, 7 pixels below the name, then the strip's middle stretched to 64 pixels and its right cap; the MP gauge is the
    /// unstretched strip at row x + 48, 15 pixels below. Only the gauge middles are tinted (the knob and end caps
    /// keep their olive/grey texels).
    /// </para>
    /// </summary>
    public static class StockUiPartyWindow
    {
        public const float TextScale = 0.875f;

        private const string GaugeTexture = "gauge";

        // Gauge strip in the 64 x 64 "gauge" texture: rows 8-15; knob x 0-8, strip x 9-63 (left cap 9-12,
        // tinted middle 13-58, right cap 59-63). The party-leader ball is at (33, 16) 15 x 15.
        private const float StripTop = 8, StripHeight = 8;
        private const float KnobX = 0, KnobWidth = 9;
        private const float LeftCapX = 9, LeftCapWidth = 4;
        private const float MiddleX = 13, MiddleWidth = 46;
        private const float RightCapX = 59, RightCapWidth = 5;
        private const float LeaderX = 33, LeaderY = 16, LeaderSize = 15;

        /// <summary>HP gauge tint (half scale), from the capture's full-HP fill (255, 155, 155).</summary>
        public static readonly UiColor HpGaugeColor = new(0x9A, 0x4E, 0x4E, 0x80);

        /// <summary>MP gauge tint (half scale), from the capture's full-MP fill (213, 244, 154).</summary>
        public static readonly UiColor MpGaugeColor = new(0x7A, 0x7A, 0x4D, 0x80);

        /// <summary>TP number tint for the opt-in TP display (not in the legacy client).</summary>
        public static readonly UiColor TpColor = new(0x60, 0x70, 0x80, 0x80);

        /// <summary>
        /// Depleted gauge share: an estimate (no capture below full yet) that darkens the tinted middle.
        /// </summary>
        public static readonly UiColor EmptyGaugeColor = new(0x20, 0x20, 0x28, 0x80);

        private static readonly UiColor Neutral = new(0x80, 0x80, 0x80, 0x80);

        /// <summary>
        /// HP number colours by remaining HP, as the "hpcol" group authors them: white, yellow, orange, red.
        /// The thresholds (75 / 50 / 25 percent) follow the retail party list.
        /// </summary>
        public static UiColor HpNumberColor(int hpPercent) => hpPercent switch
        {
            >= 75 => new UiColor(0x7F, 0x7F, 0x7F, 0x7F),
            >= 50 => new UiColor(0x7F, 0x7F, 0x40, 0x7F),
            >= 25 => new UiColor(0x7F, 0x60, 0x40, 0x7F),
            _ => new UiColor(0x7F, 0x40, 0x40, 0x7F),
        };

        public static void Draw(StockUiRenderer renderer, UiFont font, UiMenuDefinition menu, StockUiPlacement placement,
            IReadOnlyList<PartyRowVitals> rows, bool showTp)
        {
            float s = placement.Scale;
            float textScale = s * TextScale;
            for (int i = 0; i < rows.Count && i < menu.Buttons.Count; i++)
            {
                var row = rows[i];
                var button = menu.Buttons[i];
                float rx = placement.X + button.X * s;
                float ry = placement.Y + button.Y * s;

                // Gauges first: the numbers overlap them.
                DrawGauge(renderer, rx + 25 * s, ry + 7 * s, 64, withKnob: true, HpGaugeColor, row.HpPercent, s);
                DrawGauge(renderer, rx + 48 * s, ry + 15 * s, MiddleWidth, withKnob: false, MpGaugeColor, row.MpPercent, s);

                if (row.IsLeader)
                {
                    // 8 pixels visible (the 15-pixel texture ball has a transparent margin, ~13/15 of it is ball),
                    // centred 1 pixel inside the frame's left edge, 12 pixels below the row's top.
                    float size = 9.25f * s;
                    renderer.DrawTextureRect(GaugeTexture, LeaderX, LeaderY, LeaderSize, LeaderSize,
                        placement.X + 1 * s - size / 2, ry + 12 * s - size / 2, size, size, Neutral);
                }

                renderer.DrawText(font, FitName(font, row.Name), rx, ry, textScale);
                DrawRightAligned(renderer, font, row.Hp.ToString(CultureInfo.InvariantCulture), rx + 87 * s, ry, textScale, HpNumberColor(row.HpPercent));
                DrawRightAligned(renderer, font, row.Mp.ToString(CultureInfo.InvariantCulture), rx + 96 * s, ry + 10 * s, textScale, null);

                if (showTp)
                {
                    // The space under the name, left of the HP knob, is empty in the retail layout.
                    renderer.DrawText(font, row.Tp.ToString(CultureInfo.InvariantCulture), rx + 1 * s, ry + 10 * s, textScale * 0.85f, TpColor);
                }
            }
        }

        /// <summary>
        /// Width (layout pixels at <see cref="TextScale"/>) a name may take: up to the left edge of a four-digit HP
        /// number right-aligned at row x + 87.
        /// </summary>
        public static float NameLimit(UiFont font) => 87 - font.MeasureWidth("9999") * TextScale - 1;

        /// <summary>
        /// Retail keeps the longest prefix of a too-wide name that fits the limit and appends ".." after it (the dots
        /// may run into the HP number): "Tarudrake" shows as "Tarudra.." beside 9999 HP.
        /// </summary>
        public static string FitName(UiFont font, string name)
        {
            float limit = NameLimit(font);
            if (font.MeasureWidth(name) * TextScale <= limit) return name;
            for (int length = name.Length - 1; length > 0; length--)
            {
                if (font.MeasureWidth(name.AsSpan(0, length)) * TextScale <= limit) return string.Concat(name.AsSpan(0, length), "..");
            }
            return "..";
        }

        private static void DrawRightAligned(StockUiRenderer renderer, UiFont font, string text, float right, float y, float scale, UiColor? color)
        {
            float width = font.MeasureWidth(text) * scale;
            renderer.DrawText(font, text, right - width, y, scale, color);
        }

        /// <summary>
        /// Draws a gauge strip whose tinted middle spans <paramref name="middleWidth"/> layout pixels (46 = unstretched),
        /// filled to <paramref name="percent"/>.
        /// </summary>
        private static void DrawGauge(StockUiRenderer renderer, float x, float y, float middleWidth, bool withKnob, UiColor color, int percent, float s)
        {
            float pen = x;
            float h = StripHeight * s;
            if (withKnob)
            {
                // The HP gauge starts with the knob drawn at about 5 x 5 over the strip's left end (no left cap).
                renderer.DrawTextureRect(GaugeTexture, KnobX, StripTop, KnobWidth, StripHeight, pen, y + 2 * s, 5 * s, 5 * s, Neutral);
                pen += 4 * s;
            }
            else
            {
                renderer.DrawTextureRect(GaugeTexture, LeftCapX, StripTop, LeftCapWidth, StripHeight, pen, y, LeftCapWidth * s, h, Neutral);
                pen += LeftCapWidth * s;
            }

            float fill = Math.Clamp(percent, 0, 100) / 100.0f;
            float filled = middleWidth * fill;
            if (filled > 0)
            {
                renderer.DrawTextureRect(GaugeTexture, MiddleX, StripTop, MiddleWidth * fill, StripHeight, pen, y, filled * s, h, color);
            }
            if (fill < 1)
            {
                renderer.DrawTextureRect(GaugeTexture, MiddleX + MiddleWidth * fill, StripTop, MiddleWidth * (1 - fill), StripHeight,
                    pen + filled * s, y, (middleWidth - filled) * s, h, EmptyGaugeColor);
            }
            pen += middleWidth * s;
            renderer.DrawTextureRect(GaugeTexture, RightCapX, StripTop, RightCapWidth, StripHeight, pen, y, RightCapWidth * s, h, Neutral);
        }
    }
}
