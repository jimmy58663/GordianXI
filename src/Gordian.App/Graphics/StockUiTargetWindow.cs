// src/Gordian.App/Graphics/StockUiTargetWindow.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Gordian.Core.World;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// How the target window colours a target's name.
    /// </summary>
    public enum TargetNameKind
    {
        /// <summary>Players, NPCs and objects.</summary>
        Neutral,

        /// <summary>A monster nobody has claimed.</summary>
        UnclaimedMonster,

        /// <summary>A monster claimed by you or your party.</summary>
        ClaimedByParty,

        /// <summary>A monster claimed by someone else.</summary>
        ClaimedByOther,
    }

    /// <summary>
    /// Draws the target window ("targetwi", 112 x 44 above the party window) and the status-icon row.
    /// <para>
    /// The DAT authors only the target window's frame and title; the client adds the target's name and HP gauge.
    /// Measured from Windower captures (2026-09-25): the name at (3, 13) in fontshp at 7/8 size, the HP gauge (the
    /// party window's) at (28, 28) with no number. Name colours: unclaimed monsters pale yellow, monsters claimed by
    /// you or your party/alliance red (both sampled); monsters claimed by others pink (sampled from an alliance capture).
    /// </para>
    /// <para>
    /// Status icons use the "buff" menu grid (frame (142, 48), top-left; 36 slots of 24 x 24 at a 26-pixel pitch,
    /// nine per row), which matches the icon row of the captures; icons come from <see cref="StatusIconLibrary"/>.
    /// </para>
    /// </summary>
    public static class StockUiTargetWindow
    {
        public static readonly UiColor NeutralNameColor = new(0x7F, 0x7F, 0x7F, 0x7F);
        public static readonly UiColor UnclaimedNameColor = new(0x7F, 0x7F, 0x6F, 0x7F);
        public static readonly UiColor ClaimedNameColor = new(0x7F, 0x48, 0x48, 0x7F);
        /// <summary>Pink (240, 122, 180 in a retail alliance capture of "Tiamat"; half scale 78 3D 5A).</summary>
        public static readonly UiColor OtherClaimNameColor = new(0x78, 0x3D, 0x5A, 0x7F);

        private static readonly UiColor Neutral = new(0x80, 0x80, 0x80, 0x80);

        public static UiColor NameColor(TargetNameKind kind) => kind switch
        {
            TargetNameKind.UnclaimedMonster => UnclaimedNameColor,
            TargetNameKind.ClaimedByParty => ClaimedNameColor,
            TargetNameKind.ClaimedByOther => OtherClaimNameColor,
            _ => NeutralNameColor,
        };

        /// <summary>
        /// Classifies a target: monsters by who holds the claim, everything else neutral.
        /// </summary>
        public static TargetNameKind Classify(WorldEntity target, uint localServerId, IReadOnlyCollection<uint> partyServerIds)
        {
            if (target.Type != EntityType.Monster) return TargetNameKind.Neutral;
            uint claim = target.ClaimServerId;
            if (claim == 0) return TargetNameKind.UnclaimedMonster;
            if (claim == localServerId) return TargetNameKind.ClaimedByParty;
            foreach (uint id in partyServerIds)
            {
                if (id == claim) return TargetNameKind.ClaimedByParty;
            }
            return TargetNameKind.ClaimedByOther;
        }

        /// <summary>Left edge of the name's first line (layout pixels from the window's left edge).</summary>
        public const float NameX = 3;

        /// <summary>Top of a name that fits on one line (measured with the HP gauge shown, 2026-09-25).</summary>
        public const float NameY = 13;

        /// <summary>
        /// Top of a wrapped name's first line: 6 pixels above <see cref="NameY"/> (retail captures of "Synthesis Focuser
        /// II", "Door: Chocobo Stables" and "Door: Jeuno Duty-Free", 2026-10-04, all without the gauge; baselines 17
        /// pixels below the top of the "Target" title against 23 for a one-line name).
        /// </summary>
        public const float WrappedNameY = 7;

        /// <summary>Distance between the tops of a wrapped name's lines (12 pixels in the same captures).</summary>
        public const float LinePitch = 12;

        /// <summary>Each line after the first starts this much further right than the one before it (same captures).</summary>
        public const float LineIndent = 4;

        /// <summary>
        /// Width (layout pixels, at <see cref="StockUiPartyWindow.TextScale"/>) the name's first line may take; each
        /// following line has <see cref="LineIndent"/> less, so all lines end at the same right edge. Retail keeps
        /// "Mewk Chorosap" (95.4) on one line and wraps "Treasure Coffer" (104.1), so the limit lies between them; 98 is
        /// the 112-pixel frame width in unscaled font pixels (112 x 7/8). Provisional within that range.
        /// </summary>
        public const float NameWidth = 98;

        /// <summary>Lines a name may take with the HP gauge shown (the gauge at y 28 leaves room for two).</summary>
        public const int MaxLinesWithGauge = 2;

        /// <summary>Lines a name may take without the HP gauge (retail: "Door:" / "Chocobo" / "Stables").</summary>
        public const int MaxLinesWithoutGauge = 3;

        /// <summary>
        /// Draws the target's name and, when <paramref name="showGauge"/> is set, its HP gauge. Retail draws no gauge
        /// for an NPC without a name plate (<see cref="Gordian.Core.Ui.NamePlateStyle.ShowsTargetHealthBar"/>, #259); a
        /// long name wraps onto up to two lines with the gauge or three without it (<see cref="WrapName(UiFont, string, bool)"/>, #258).
        /// </summary>
        public static void Draw(StockUiRenderer renderer, UiFont font, UiMenuDefinition menu, StockUiPlacement placement,
            string name, int hpPercent, TargetNameKind kind, bool showGauge = true)
        {
            float s = placement.Scale;
            if (showGauge)
            {
                StockUiPartyWindow.DrawGauge(renderer, placement.X + 28 * s, placement.Y + 28 * s, 64, withKnob: true,
                    StockUiPartyWindow.HpGaugeColor, hpPercent, s);
            }

            var lines = WrapName(font, name, showGauge);
            float top = lines.Count > 1 ? WrappedNameY : NameY;
            for (int i = 0; i < lines.Count; i++)
            {
                renderer.DrawText(font, lines[i], placement.X + (NameX + i * LineIndent) * s, placement.Y + (top + i * LinePitch) * s,
                    s * StockUiPartyWindow.TextScale, NameColor(kind));
            }
        }

        /// <summary>
        /// The lines the target window prints a name on: up to <see cref="MaxLinesWithGauge"/> with the HP gauge,
        /// <see cref="MaxLinesWithoutGauge"/> without it.
        /// </summary>
        public static IReadOnlyList<string> WrapName(UiFont font, string name, bool showGauge) =>
            WrapName(text => font.MeasureWidth(text) * StockUiPartyWindow.TextScale, name, NameWidth, LineIndent,
                showGauge ? MaxLinesWithGauge : MaxLinesWithoutGauge);

        /// <summary>Measures text in layout pixels.</summary>
        public delegate float TextMeasure(ReadOnlySpan<char> text);

        /// <summary>
        /// Wraps a name the way the retail target window does (captures 2026-10-04, #258):
        /// <list type="bullet">
        /// <item>A name that fits in <paramref name="width"/> stays on one line.</item>
        /// <item>Otherwise the first line ends at the name's first break: "Synthesis" / "Focuser II", and "Door:" /
        /// "Chocobo" / "Stables" although "Door: Chocobo" is no wider than "Mewk Chorosap", which retail keeps whole.</item>
        /// <item>The rest is filled greedily, each line <paramref name="indent"/> narrower than the one before: "Jeuno
        /// Duty-" / "Free".</item>
        /// <item>A line can break at a space (dropped) or after a hyphen (kept). A piece with no break that fits is cut
        /// at the last character that fits.</item>
        /// <item>What does not fit on the last allowed line is cut with "..", as the one-line window did before.</item>
        /// </list>
        /// </summary>
        public static IReadOnlyList<string> WrapName(TextMeasure measure, string name, float width, float indent, int maxLines)
        {
            var lines = new List<string>(3);
            string rest = name.Trim();
            if (rest.Length == 0) return lines;
            if (maxLines < 1) maxLines = 1;

            for (int line = 0; rest.Length > 0; line++)
            {
                float limit = width - line * indent;
                if (measure(rest) <= limit)
                {
                    lines.Add(rest);
                    break;
                }
                if (line == maxLines - 1)
                {
                    lines.Add(Truncate(measure, rest, limit));
                    break;
                }

                int end = line == 0 ? FirstBreak(rest) : LastBreakThatFits(measure, rest, limit);
                if (end <= 0 || measure(rest.AsSpan(0, end).TrimEnd()) > limit) end = HardBreak(measure, rest, limit);
                lines.Add(rest[..end].TrimEnd());
                rest = rest[end..].TrimStart();
            }
            return lines;
        }

        /// <summary>The end of the first line-break opportunity: just before a space, or just after a hyphen.</summary>
        private static int FirstBreak(string text)
        {
            for (int i = 1; i < text.Length; i++)
            {
                if (text[i] == ' ') return i;
                if (text[i - 1] == '-') return i;
            }
            return 0;
        }

        /// <summary>The end of the last line-break opportunity whose line fits in <paramref name="limit"/>; 0 when none does.</summary>
        private static int LastBreakThatFits(TextMeasure measure, string text, float limit)
        {
            int best = 0;
            for (int i = 1; i < text.Length; i++)
            {
                bool opportunity = text[i] == ' ' || text[i - 1] == '-';
                if (!opportunity) continue;
                if (measure(text.AsSpan(0, i).TrimEnd()) > limit) break;
                best = i;
            }
            return best;
        }

        /// <summary>The longest prefix (at least one character) that fits in <paramref name="limit"/>.</summary>
        private static int HardBreak(TextMeasure measure, string text, float limit)
        {
            int end = 1;
            while (end < text.Length && measure(text.AsSpan(0, end + 1)) <= limit) end++;
            return end;
        }

        /// <summary>
        /// The longest prefix that fits with ".." after it (the dots may overhang, as in the party rows'
        /// <see cref="StockUiPartyWindow.FitName(UiFont, string, float)"/>).
        /// </summary>
        private static string Truncate(TextMeasure measure, string text, float limit)
        {
            for (int length = text.Length - 1; length > 0; length--)
            {
                if (measure(text.AsSpan(0, length)) <= limit) return string.Concat(text.AsSpan(0, length).TrimEnd(), "..");
            }
            return "..";
        }

        /// <summary>
        /// Locked on: draws the "targetlo" menu's overlay (windowps image 212: red corner brackets over the frame,
        /// "Locked" between two arrow key-tops along the bottom edge, glow bars at both sides) over the target window.
        /// </summary>
        public static void DrawLockOverlay(StockUiRenderer renderer, UiResourceLibrary library, StockUiPlacement placement)
        {
            if (!library.TryGetMenu("targetlo", out var locked)) return;
            float s = placement.Scale;
            foreach (var button in locked.Buttons)
            {
                foreach (var shape in button.Shapes)
                {
                    if (shape.Kind == 0 && library.TryGetImage(shape, out var image))
                    {
                        renderer.DrawImage(image, placement.X + button.X * s, placement.Y + button.Y * s, s);
                    }
                }
            }
        }

        /// <summary>Seconds per step of the target cursor's shading cycle.</summary>
        public const double CursorStepSeconds = 0.067;

        /// <summary>
        /// The target cursor: the "anc_s" cursor sprite (a diamond and an arrowhead) turned to point down, its tip at
        /// <paramref name="tip"/> (screen pixels), drawn at <paramref name="scale"/> screen pixels per layout pixel like
        /// the rest of the UI. Retail shades it through the group's six frames and back, about 67 ms a step (a 0.8 s
        /// cycle, measured from a capture, 2026-09-26); <paramref name="timestamp"/> is a Stopwatch timestamp.
        /// </summary>
        /// <summary>
        /// The frame of an animated cursor group ("anc_s" and its siblings) at a Stopwatch timestamp: the frames play
        /// forward then backward at <see cref="CursorStepSeconds"/> per step.
        /// </summary>
        public static UiImage SelectCursorFrame(UiElementGroup group, long timestamp)
        {
            int frames = group.Images.Count;
            int step = (int)(timestamp / (Stopwatch.Frequency * CursorStepSeconds) % (2 * frames));
            return group.Images[step < frames ? step : 2 * frames - 1 - step];
        }

        public static void DrawCursor(StockUiRenderer renderer, UiResourceLibrary library, Vector2 tip, float scale, long timestamp)
        {
            if (!library.TryGetGroup("anc_s", out var group) || group.Images.Count == 0) return;
            var image = SelectCursorFrame(group, timestamp);
            if (image.Parts.Count == 0) return;

            // The authored sprite points right; its tip is the rightmost point, halfway down.
            float right = float.MinValue, top = float.MaxValue, bottom = float.MinValue;
            foreach (var part in image.Parts)
            {
                right = Math.Max(right, Math.Max(part.TopRight.X, part.BottomRight.X));
                top = Math.Min(top, Math.Min(part.TopLeft.Y, part.TopRight.Y));
                bottom = Math.Max(bottom, Math.Max(part.BottomLeft.Y, part.BottomRight.Y));
            }
            var transform = Matrix3x2.CreateTranslation(-right, -(top + bottom) * 0.5f) *
                            Matrix3x2.CreateRotation(MathF.PI / 2) *
                            Matrix3x2.CreateScale(scale) *
                            Matrix3x2.CreateTranslation(tip);
            renderer.DrawImage(image, transform);
        }

        /// <summary>
        /// The selection cursor over status icon <paramref name="index"/> (keypad + / gamepad Y cycle, #52): the target
        /// cursor's arrow pointing down at the icon's top edge, and the icon's slot outlined. How retail marks the
        /// selected icon is not captured yet (provisional look).
        /// </summary>
        public static void DrawStatusCursor(StockUiRenderer renderer, UiResourceLibrary library, UiMenuDefinition grid, StockUiPlacement placement,
            int index, long timestamp)
        {
            if (index < 0 || index >= grid.Buttons.Count) return;
            float s = placement.Scale;
            var slot = grid.Buttons[index];
            float x = placement.X + slot.X * s, y = placement.Y + slot.Y * s, w = slot.Width * s, h = slot.Height * s;
            var edge = new UiColor(0x80, 0x70, 0x40, 0x80);
            float t = Math.Max(1, s);
            renderer.DrawTextureRect("gauge", 30, 12, 1, 1, x - t, y - t, w + 2 * t, t, edge);
            renderer.DrawTextureRect("gauge", 30, 12, 1, 1, x - t, y + h, w + 2 * t, t, edge);
            renderer.DrawTextureRect("gauge", 30, 12, 1, 1, x - t, y, t, h, edge);
            renderer.DrawTextureRect("gauge", 30, 12, 1, 1, x + w, y, t, h, edge);
            DrawCursor(renderer, library, new Vector2(x + w * 0.5f, y - 2 * s), s, timestamp);
        }

        /// <summary>
        /// The screen rectangle the first <paramref name="count"/> status icon slots of the "buff" grid cover at a
        /// placement (all of them when <paramref name="count"/> is 0 or more than the grid has); the grid's authored
        /// frame is only a strip, the icons are its buttons. Falls back to the frame when the grid has no buttons.
        /// </summary>
        public static (float X, float Y, float Width, float Height) StatusGridExtent(UiMenuDefinition grid, StockUiPlacement placement, int count)
        {
            float s = placement.Scale;
            int slots = count <= 0 || count > grid.Buttons.Count ? grid.Buttons.Count : count;
            if (slots == 0) return (placement.X, placement.Y, grid.Frame.Width * s, grid.Frame.Height * s);
            float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
            for (int i = 0; i < slots; i++)
            {
                var slot = grid.Buttons[i];
                left = Math.Min(left, slot.X);
                top = Math.Min(top, slot.Y);
                right = Math.Max(right, slot.X + slot.Width);
                bottom = Math.Max(bottom, slot.Y + slot.Height);
            }
            return (placement.X + left * s, placement.Y + top * s, (right - left) * s, (bottom - top) * s);
        }

        public static void DrawStatusIcons(StockUiRenderer renderer, StatusIconLibrary icons, UiMenuDefinition grid,
            StockUiPlacement placement, IReadOnlyList<ushort> statusIds)
        {
            float s = placement.Scale;
            for (int i = 0; i < statusIds.Count && i < grid.Buttons.Count; i++)
            {
                if (!icons.TryGetIcon(statusIds[i], out DecodedTexture icon)) continue;
                var slot = grid.Buttons[i];
                renderer.DrawTexture($"status:{statusIds[i]}", icon, placement.X + slot.X * s, placement.Y + slot.Y * s,
                    slot.Width * s, slot.Height * s, Neutral);
            }
        }
    }
}
