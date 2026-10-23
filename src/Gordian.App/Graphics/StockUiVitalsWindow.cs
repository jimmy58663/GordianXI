// src/Gordian.App/Graphics/StockUiVitalsWindow.cs
using System;
using System.Globalization;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// The player's own HP / MP / TP block that retail draws at the top left, under the help bar, while engaged (#391; the
    /// maintainer's capture of 2026-10-13 described in the issue): the menu DAT's <c>gaugewin</c> frame (16,48, 128 x 44, the
    /// "about 125 px" block) with three rows, each a bold italic label, a gauge and the value right-aligned. The macro palette
    /// moves right by <see cref="StockUiMacroWindow.CombatShift"/> to make room.
    /// <para>
    /// The labels are the <c>font</c> texture glyphs the Status window's frame (<c>statuswi</c>, <c>windowps</c> #8) bakes in for
    /// its HP / MP / TP rows (9 x 11 cells H (31,39), M (80,39), T (31,52), P (113,39)), white as the capture shows. The gauges
    /// are the party window's strips (HP and MP tints as there, the capture's pink and pale green). <b>Provisional:</b> the
    /// capture is not in the repository, so the rows' placement inside the frame (label at x 6, gauge at x 26, value ending at
    /// x 122, rows 14 px apart from y 2), the TP gauge's tint (a pale blue) and its scale (full at 3000) are estimates; the row
    /// of three small round icons under the block is not drawn.
    /// </para>
    /// </summary>
    public static class StockUiVitalsWindow
    {
        public const string MenuName = "gaugewin";

        /// <summary>TP gauge tint (half scale): pale blue. Provisional, from the issue's description of the capture.</summary>
        public static readonly UiColor TpGaugeColor = new(0x4C, 0x66, 0x80, 0x80);

        /// <summary>The TP the gauge counts as full.</summary>
        public const int FullTp = 3000;

        /// <summary>
        /// At this much TP a weapon skill can be used and the TP gauge flashes between <see cref="TpGaugeColor"/> and
        /// <see cref="TpFlashColor"/> (the maintainer's round 1 note: "flashing when over 1000 TP"). <b>Provisional:</b> the
        /// flash rate (<see cref="TpFlashPeriod"/> seconds a cycle) and the bright colour; neither is found in the data.
        /// </summary>
        public const int WeaponSkillTp = 1000;

        public const double TpFlashPeriod = 0.8;
        public static readonly UiColor TpFlashColor = new(0xA0, 0xCC, 0xFF, 0x80);

        /// <summary>The TP gauge's colour at <paramref name="seconds"/>: steady below <see cref="WeaponSkillTp"/>, pulsing above.</summary>
        public static UiColor TpColorAt(int tp, double seconds)
        {
            if (tp < WeaponSkillTp) return TpGaugeColor;
            float t = 0.5f - 0.5f * (float)Math.Cos(2 * Math.PI * seconds / TpFlashPeriod);
            byte Mix(byte a, byte b) => (byte)(a + (b - a) * t);
            return new UiColor(Mix(TpGaugeColor.R, TpFlashColor.R), Mix(TpGaugeColor.G, TpFlashColor.G), Mix(TpGaugeColor.B, TpFlashColor.B), 0x80);
        }

        private const float LabelX = 6, GaugeX = 26, ValueRight = 122, FirstRowY = 2, RowPitch = 14, GaugeDy = 2, GlyphWidth = 9, GlyphHeight = 11;

        // Glyph cells in the "font" texture, from the Status frame's HP / MP / TP rows.
        private static readonly (float X, float Y) GlyphH = (31, 39), GlyphM = (80, 39), GlyphT = (31, 52), GlyphP = (113, 39);

        private static readonly UiColor White = new(0x80, 0x80, 0x80, 0x80);

        /// <summary>The block's rows' gauge fill, 0-100.</summary>
        public static int Percent(int value, int max) => max > 0 ? Math.Clamp((int)((long)value * 100 / max), 0, 100) : 0;

        /// <summary>
        /// Draws the block at its authored place; returns where, or null when the menu DAT has no <c>gaugewin</c>.
        /// </summary>
        public static StockUiPlacement? Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font,
            int hp, int maxHp, int mp, int maxMp, int tp, float scale, uint width, uint height, double seconds = 0)
        {
            if (!library.TryGetMenu(MenuName, out var menu)) return null;
            var f = menu.Frame;
            var at = StockUiLayout.Place(f.Anchor, f.X, f.Y, f.Width, f.Height, scale, width, height);
            renderer.DrawMenu(menu, at, includeButtons: false);

            DrawRow(renderer, font, at, 0, GlyphH, hp, Percent(hp, maxHp), StockUiPartyWindow.HpGaugeColor, scale);
            DrawRow(renderer, font, at, 1, GlyphM, mp, Percent(mp, maxMp), StockUiPartyWindow.MpGaugeColor, scale);
            DrawRow(renderer, font, at, 2, GlyphT, tp, Percent(tp, FullTp), TpColorAt(tp, seconds), scale);
            return at;
        }

        private static void DrawRow(StockUiRenderer renderer, UiFont font, StockUiPlacement at, int row, (float X, float Y) first, int value,
            int percent, UiColor gauge, float s)
        {
            float y = at.Y + (FirstRowY + row * RowPitch) * s;
            renderer.DrawTextureRect("font", first.X, first.Y, GlyphWidth, GlyphHeight, at.X + LabelX * s, y, GlyphWidth * s, GlyphHeight * s, White);
            renderer.DrawTextureRect("font", GlyphP.X, GlyphP.Y, GlyphWidth, GlyphHeight, at.X + (LabelX + GlyphWidth) * s, y, GlyphWidth * s, GlyphHeight * s, White);
            StockUiPartyWindow.DrawGauge(renderer, at.X + GaugeX * s, y + GaugeDy * s, 64, withKnob: true, gauge, percent, s);

            string text = value.ToString(CultureInfo.InvariantCulture);
            float textScale = s * StockUiPartyWindow.TextScale;
            renderer.DrawText(font, text, at.X + ValueRight * s - font.MeasureWidth(text) * textScale, y + 1 * s, textScale);
        }
    }
}
