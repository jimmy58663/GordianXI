// src/Gordian.App/Graphics/StockUiPartyWindow.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// One party (or alliance) window row's contents.
    /// </summary>
    /// <param name="HpPercent">0-100.</param>
    /// <param name="StatusIds">Status effect ids in icon order, for the opt-in party status icons (null = none known).</param>
    public readonly record struct PartyRowVitals(string Name, int Hp, int HpPercent, int Mp, int MpPercent, int Tp, bool IsLeader,
        bool IsAllianceLeader = false, IReadOnlyList<ushort>? StatusIds = null);

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
    /// <para>
    /// Alliance windows ("raid1"/"raid2", 16-pixel rows) carry the same name, HP number and HP gauge at the same
    /// offsets from each row, and no MP (measured from 1:1 retail captures of an alliance, 2026-09-26). The leader
    /// balls are the "colorbal" texture's yellow (party leader) and white (alliance leader) balls, which match the
    /// captures' greenish yellow (DFF44D) and cream white (D5EEEC); the alliance leader shows both, white in the usual
    /// place and yellow beside it.
    /// </para>
    /// </summary>
    public static class StockUiPartyWindow
    {
        public const float TextScale = 0.875f;

        private const string GaugeTexture = "gauge";

        // Gauge strip in the 64 x 64 "gauge" texture: rows 8-15; knob x 0-8, strip x 9-63 (left cap 9-12,
        // tinted middle 13-58, right cap 59-63).
        private const float StripTop = 8, StripHeight = 8;
        private const float KnobX = 0, KnobWidth = 9;
        private const float LeftCapX = 9, LeftCapWidth = 4;
        private const float MiddleX = 13, MiddleWidth = 46;
        private const float RightCapX = 59, RightCapWidth = 5;

        // Leader balls in the 64 x 64 "colorbal" texture: 16 x 16 cells (the ball fills 14 of them), white at (0, 16),
        // yellow at (16, 16).
        private const string BallTexture = "colorbal";
        private const float BallCell = 16, WhiteBallX = 0, YellowBallX = 16, BallY = 16;

        /// <summary>Screen size of a ball cell so the ball shows 8 pixels across (retail ~6; 8 by request).</summary>
        private const float BallSize = 8 * BallCell / 14;

        /// <summary>How far right of the alliance leader's white ball its yellow party-leader ball sits.</summary>
        private const float SecondBallOffset = 7;

        /// <summary>Opt-in party status icons: drawn this size, this far from the window's side, one row per member.</summary>
        public const float StatusIconSize = 16, StatusIconGap = 2;

        /// <summary>HP gauge tint (half scale), from the capture's full-HP fill (255, 155, 155).</summary>
        public static readonly UiColor HpGaugeColor = new(0x9A, 0x4E, 0x4E, 0x80);

        /// <summary>MP gauge tint (half scale), from the capture's full-MP fill (213, 244, 154).</summary>
        public static readonly UiColor MpGaugeColor = new(0x7A, 0x7A, 0x4D, 0x80);

        /// <summary>TP number tint for the opt-in TP display (not in the legacy client).</summary>
        public static readonly UiColor TpColor = new(0x60, 0x70, 0x80, 0x80);

        /// <summary>
        /// Depleted gauge share: a light lavender (137, 144, 187 over the strip's near-white texels in a Windower
        /// capture of a damaged target).
        /// </summary>
        public static readonly UiColor EmptyGaugeColor = new(0x4E, 0x48, 0x5E, 0x80);

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

                DrawLeaderBalls(renderer, row, placement.X, ry, s);

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
        /// Draws alliance window rows ("raid1"/"raid2"): name, HP number and HP gauge at the party rows' offsets, no MP.
        /// </summary>
        public static void DrawAllianceRows(StockUiRenderer renderer, UiFont font, UiMenuDefinition menu, StockUiPlacement placement,
            IReadOnlyList<PartyRowVitals> rows)
        {
            float s = placement.Scale;
            float textScale = s * TextScale;
            for (int i = 0; i < rows.Count && i < menu.Buttons.Count; i++)
            {
                var row = rows[i];
                var button = menu.Buttons[i];
                float rx = placement.X + button.X * s;
                float ry = placement.Y + button.Y * s;

                DrawGauge(renderer, rx + 25 * s, ry + 7 * s, 64, withKnob: true, HpGaugeColor, row.HpPercent, s);
                DrawLeaderBalls(renderer, row, placement.X, ry, s);
                renderer.DrawText(font, FitName(font, row.Name), rx, ry, textScale);
                DrawRightAligned(renderer, font, row.Hp.ToString(CultureInfo.InvariantCulture), rx + 87 * s, ry, textScale, HpNumberColor(row.HpPercent));
            }
        }

        /// <summary>
        /// Opt-in enhancement (not in the legacy client): each row's status icons in a line beside the party window,
        /// on the chosen side, the first icon nearest the window, centred on the row's name line.
        /// </summary>
        public static void DrawStatusIcons(StockUiRenderer renderer, StatusIconLibrary icons, UiMenuDefinition menu,
            StockUiPlacement placement, IReadOnlyList<PartyRowVitals> rows, PartyStatusIconSide side)
        {
            float s = placement.Scale;
            float size = StatusIconSize * s;
            float step = size;
            float start = side == PartyStatusIconSide.Right
                ? placement.X + (menu.Frame.Width + StatusIconGap) * s
                : placement.X - (StatusIconGap + StatusIconSize) * s;
            if (side == PartyStatusIconSide.Left) step = -step;

            for (int i = 0; i < rows.Count && i < menu.Buttons.Count; i++)
            {
                var ids = rows[i].StatusIds;
                if (ids == null) continue;
                float y = placement.Y + (menu.Buttons[i].Y - 1) * s;
                float x = start;
                foreach (ushort id in ids)
                {
                    if (!icons.TryGetIcon(id, out var icon)) continue;
                    renderer.DrawTexture($"status:{id}", icon, x, y, size, size, Neutral);
                    x += step;
                }
            }
        }

        /// <summary>
        /// The leader markers left of a row, centred 1 pixel inside the frame's left edge and 12 pixels below the row's
        /// top: yellow for the party leader; the alliance leader gets white there and yellow beside it.
        /// </summary>
        private static void DrawLeaderBalls(StockUiRenderer renderer, PartyRowVitals row, float frameX, float rowY, float s)
        {
            if (!row.IsLeader && !row.IsAllianceLeader) return;
            float size = BallSize * s;
            float x = frameX + 1 * s - size / 2, y = rowY + 12 * s - size / 2;
            if (row.IsAllianceLeader)
            {
                renderer.DrawTextureRect(BallTexture, WhiteBallX, BallY, BallCell, BallCell, x, y, size, size, Neutral);
                x += SecondBallOffset * s;
            }
            renderer.DrawTextureRect(BallTexture, YellowBallX, BallY, BallCell, BallCell, x, y, size, size, Neutral);
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
        public static string FitName(UiFont font, string name) => FitName(font, name, NameLimit(font));

        /// <summary>Fits a name into <paramref name="limit"/> layout pixels at <see cref="TextScale"/>.</summary>
        public static string FitName(UiFont font, string name, float limit)
        {
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
        internal static void DrawGauge(StockUiRenderer renderer, float x, float y, float middleWidth, bool withKnob, UiColor color, int percent, float s)
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
