// src/Gordian.App/Graphics/StockUiMenuWindow.cs
using System;
using System.Diagnostics;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws one open stock menu (see <see cref="StockUiMenuController"/>): the DAT frame and button label sprites,
    /// the marker of the option in effect, a prompt's message, and the animated cursor. The cursor is the frame's
    /// kind-6 element-group reference ("anc_s": the six-frame gold arrow, 32 x 20, authored to the left of its
    /// origin), drawn at the selected button's origin plus the frame's and button's cursor offsets; it plays
    /// forward then backward at the target cursor's 67 ms step.
    /// </summary>
    public static class StockUiMenuWindow
    {
        // Retail underlines the option in effect on config pages with a red bar the width of the button, just under
        // it (seen in a retail capture, 2026-09-26). Drawn from the white centre of the "colorbal" ball, tinted red.
        private const string BarTexture = "colorbal";
        private const float BarSourceX = 7, BarSourceY = 23, BarThickness = 2;
        private static readonly UiColor BarColor = new(0x80, 0x18, 0x18, 0x80);

        /// <summary>
        /// Draws one open menu: the frame, its buttons' label sprites (the page arrows only on a paged menu; the
        /// selected entry tinted orange, glyphs and capsule, as retail does), the red bar under the
        /// option in effect, a prompt's message, and the animated DAT cursor (the frame's kind-6 reference, "anc_s" on
        /// list menus) at the selected button's origin plus the cursor offsets.
        /// </summary>
        public static void Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont? font, StockUiOpenMenu menu, StockUiPlacement placement,
            long timestamp)
        {
            var definition = menu.Menu;
            var frame = definition.Frame;
            float s = placement.Scale;
            // Config pages with sliders: the frame art of some pages bakes in a sample fill (conf5w1's first bar
            // shows about 20%); it is left out so the fill drawn for the value is the only one.
            renderer.DrawMenu(definition, placement, includeButtons: false,
                excludeFramePart: menu.SliderFractions.Count > 0 ? IsSliderFill : null, opaqueBody: true);

            foreach (var button in definition.Buttons)
            {
                bool outsideFrame = button.X < 0 || button.X >= frame.Width;
                if (outsideFrame && menu.PageRing.Count <= 1) continue; // page arrows

                // The selected entry is tinted orange by the client (retail): the glyphs a light orange, the capsule
                // a deeper one; the darkening glyph shadows keep their colour. A button's alternate (kind 4) image (Synthesis, Party, Gamepad) is
                // not the selected look; its meaning is still unknown, so it is not drawn.
                bool isSelected = button.ButtonId == menu.SelectedButtonId;
                float bx = placement.X + button.X * s, by = placement.Y + button.Y * s;
                foreach (var shape in button.Shapes)
                {
                    if (shape.Kind != 0 || !library.TryGetImage(shape, out var image)) continue;
                    if (!isSelected)
                    {
                        renderer.DrawImage(image, bx, by, s);
                        continue;
                    }
                    foreach (var part in image.Parts)
                    {
                        UiColor? tint = part.BlendMode != UiBlendMode.Alpha ? null : IsGlyph(part) ? SelectedGlyphTint : SelectedCapsuleTint;
                        renderer.DrawPart(part, bx, by, s, tint);
                    }
                    break;
                }

                if (menu.SliderFractions.TryGetValue(button.ButtonId, out float fraction) && fraction > 0)
                {
                    // The bar is authored in the frame (a translucent "gauge" strip 192 wide, knobs at both ends); the
                    // value is the same strip's rows 1-6 drawn opaque and tinted light blue from the bar's left edge,
                    // 12 tall inside the 16-tall bar (a retail capture, 2026-09-26; "framesus" #103 is the same
                    // strip with the tint (64, 96, 127), the capture's fill reads a little more lavender).
                    renderer.DrawTextureRect(SliderTexture, SliderFillSourceX, SliderFillSourceY, SliderFillSourceWidth, SliderFillSourceHeight,
                        bx, by + SliderFillTop * s, button.Width * fraction * s, SliderFillHeight * s, SliderFillTint);
                }

                if (menu.IsMarked(button.ButtonId) && menu.Rows.Count == 0)
                {
                    renderer.DrawTextureRect(BarTexture, BarSourceX, BarSourceY, 1, 1, bx, by + button.Height * s,
                        button.Width * s, BarThickness * s, BarColor);
                }
            }

            if (font != null && menu.IsQuery && definition.FindButton(1) is { } firstQueryRow)
            {
                DrawQuery(renderer, font, menu, placement, firstQueryRow, timestamp);
            }
            else if (font != null && menu.Rows.Count > 0 && menu.VisibleRows > 0 && definition.FindButton(1) is { } firstRow)
            {
                // List pages (Chat Filters): the DAT rows are invisible hit regions; the client draws each row's
                // state ball ("framesus" #88 ON / #89 OFF at the row's origin; #83 is a "Hold" state whose meaning
                // is not decoded) and its text 34 px in, as retail captures show (2026-09-26). The list slides
                // smoothly between entries (ScrollFrom -> FirstRow over ScrollDuration), clipped to the rows' area,
                // and a scrollbar on the right edge shows the position.
                library.TryGetGroup(StateGroup, out var states);
                var secondRow = definition.FindButton(2);
                float pitch = secondRow != null && secondRow.Y > firstRow.Y ? secondRow.Y - firstRow.Y : firstRow.Height;
                float first = menu.FirstRow;
                if (menu.ScrollFrom != menu.FirstRow)
                {
                    double t = (timestamp - menu.ScrollStartedAt) / (double)Stopwatch.Frequency / StockUiMenuController.ScrollDuration.TotalSeconds;
                    if (t < 1) first = menu.ScrollFrom + (menu.FirstRow - menu.ScrollFrom) * (float)Math.Max(0, t);
                }
                float areaX = placement.X + firstRow.X * s, areaY = placement.Y + firstRow.Y * s;
                float areaH = menu.VisibleRows * pitch * s;
                // The state balls' text starts a few pixels left of the row origin, so the clip starts at the window's edge.
                renderer.SetClip(placement.X + ListClipInset * s, areaY, (frame.Width - ListClipInset) * s, areaH);
                int selectedEntry = menu.EntryIndex(menu.SelectedButtonId);
                int from = Math.Max(0, (int)Math.Floor(first) - 1), to = Math.Min(menu.Rows.Count - 1, (int)Math.Ceiling(first) + menu.VisibleRows);
                for (int i = from; i <= to; i++)
                {
                    var row = menu.Rows[i];
                    float ry = areaY + (i - first) * pitch * s;
                    int stateImage = row.Marked ? StateOnImage : StateOffImage;
                    if (states != null && stateImage < states.Images.Count) renderer.DrawImage(states.Images[stateImage], areaX, ry, s);
                    renderer.DrawText(font, row.Text, areaX + ListRowTextInset * s, ry + (firstRow.Height * s - font.LineHeight * s) * 0.5f, s,
                        i == selectedEntry ? SelectedGlyphTint : null);
                }
                renderer.ClearClip();
                if (menu.CanScroll) DrawScrollbar(renderer, placement, frame, first, menu.Rows.Count, menu.VisibleRows);
            }

            if (menu.Message is { Length: > 0 } message && font != null)
            {
                float textWidth = font.MeasureWidth(message) * s;
                float x = placement.X + Math.Max(8 * s, (frame.Width * s - textWidth) * 0.5f);
                renderer.DrawText(font, message, x, placement.Y + 24 * s, s);
            }

            var selected = menu.SelectedButton;
            if (selected != null) DrawMenuCursor(renderer, library, frame, selected, placement, timestamp);
        }

        /// <summary>
        /// Where a query's comment lines start (layout px from the window's top-left) and their pitch, and how far
        /// into a row its text starts: a retail recording (2026-09-28, 1:1) has the question 14 px in at y 8 and the
        /// options 37 px in (the cursor arrow sits in the 28 px before the row buttons).
        /// </summary>
        private const float QueryCommentX = 14, QueryCommentY = 8, QueryCommentPitch = 16, QueryOptionTextInset = 9;

        /// <summary>
        /// An event query (Tier 2 chunk 6): the question's lines at the top, then one option per row over the
        /// invisible row buttons (the "query" DAT window authors three 20 px rows; the controller sizes the window to
        /// the rows shown). The selected option is tinted like any selected label; long lists scroll three at a time as
        /// the Chat Filters list does, with the same scrollbar (a retail recording, 2026-09-28).
        /// </summary>
        private static void DrawQuery(StockUiRenderer renderer, UiFont font, StockUiOpenMenu menu, StockUiPlacement placement,
            UiMenuButton firstRow, long timestamp)
        {
            float s = placement.Scale;
            for (int i = 0; i < menu.Comments.Count; i++)
            {
                renderer.DrawText(font, menu.Comments[i], placement.X + QueryCommentX * s, placement.Y + (QueryCommentY + i * QueryCommentPitch) * s, s);
            }
            if (menu.Rows.Count == 0 || menu.VisibleRows == 0) return;

            var definition = menu.Menu;
            var secondRow = definition.FindButton(2);
            float pitch = secondRow != null && secondRow.Y > firstRow.Y ? secondRow.Y - firstRow.Y : firstRow.Height;
            float first = menu.FirstRow;
            if (menu.ScrollFrom != menu.FirstRow)
            {
                double t = (timestamp - menu.ScrollStartedAt) / (double)Stopwatch.Frequency / StockUiMenuController.ScrollDuration.TotalSeconds;
                if (t < 1) first = menu.ScrollFrom + (menu.FirstRow - menu.ScrollFrom) * (float)Math.Max(0, t);
            }
            float areaX = placement.X + firstRow.X * s, areaY = placement.Y + firstRow.Y * s;
            float areaH = menu.VisibleRows * pitch * s;
            renderer.SetClip(placement.X, areaY, definition.Frame.Width * s, areaH);
            int selectedEntry = menu.EntryIndex(menu.SelectedButtonId);
            int from = Math.Max(0, (int)Math.Floor(first) - 1), to = Math.Min(menu.Rows.Count - 1, (int)Math.Ceiling(first) + menu.VisibleRows);
            for (int i = from; i <= to; i++)
            {
                float ry = areaY + (i - first) * pitch * s;
                renderer.DrawText(font, menu.Rows[i].Text, areaX + QueryOptionTextInset * s, ry + (firstRow.Height * s - font.LineHeight * s) * 0.5f, s,
                    i == selectedEntry ? SelectedGlyphTint : null);
            }
            renderer.ClearClip();
            if (menu.CanScroll) DrawScrollbar(renderer, placement, definition.Frame, first, menu.Rows.Count, menu.VisibleRows);
        }

        /// <summary>
        /// Half-scale tint of the selected entry's glyphs: a retail capture's highlighted label peaks at FFC05C, a
        /// white glyph at about (1.0, 0.75, 0.36).
        /// </summary>
        private static readonly UiColor SelectedGlyphTint = new(0x80, 0x60, 0x2E, 0x80);

        /// <summary>
        /// Half-scale tint of the selected entry's capsule and any other alpha-blended part: the same capture's capsule
        /// goes from (127, 133, 186) unselected to (133, 87, 48) selected, about (1.05, 0.65, 0.26).
        /// </summary>
        private static readonly UiColor SelectedCapsuleTint = new(0x86, 0x53, 0x21, 0x80);

        /// <summary>
        /// The glyph parts of a label: alpha-blended parts sampling a font sheet ("font", "menu2fon", "mn6font"...)
        /// or the key-cap digits ("keytop"; a retail capture shows a selected Window Type digit tinted whole).
        /// Other alpha parts (the "buttonto" capsule) take the capsule tint; the darkening glyph shadows are left alone.
        /// </summary>
        private static bool IsGlyph(UiSpritePart part)
        {
            if (part.BlendMode != UiBlendMode.Alpha) return false;
            string texture = UiResourceLibrary.TrimResourceName(part.TextureName);
            return texture.Contains("fon", StringComparison.OrdinalIgnoreCase) || texture.StartsWith("keytop", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Slider fill: rows 1-6 of the "gauge" strip, tinted light blue (retail: (194, 194, 255) at the middle).</summary>
        private const string SliderTexture = "gauge";
        private const float SliderFillSourceX = 0, SliderFillSourceY = 1, SliderFillSourceWidth = 64, SliderFillSourceHeight = 6;
        private const float SliderFillTop = 1, SliderFillHeight = 12;
        private static readonly UiColor SliderFillTint = new(0x68, 0x60, 0x84, 0x80);

        /// <summary>
        /// A frame part that is an authored sample of a slider fill (the orange block, gauge texels 48,16, that
        /// "conf5w1"/"conf5w2" bake into their first bar).
        /// </summary>
        private static bool IsSliderFill(UiSpritePart part) =>
            part.SourceX == 48 && part.SourceY == 16 && part.SourceWidth == 16
            && UiResourceLibrary.TrimResourceName(part.TextureName).Equals(SliderTexture, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Retail's list scrollbar (captures, 2026-09-26): a 6 px track straddling the window's right edge from the
        /// title band's bottom to the window's bottom, the translucent gauge strip; the thumb is pale pink-white
        /// (255, 221, 228), as long as the visible share of the list and placed at the first entry's share.
        /// </summary>
        private const float ScrollbarWidth = 6, ScrollbarInset = 4;
        private static readonly UiColor ScrollTrackTint = new(0x80, 0x80, 0x80, 0x30);
        private static readonly UiColor ScrollThumbTint = new(0x93, 0x6E, 0x73, 0x80);

        private static void DrawScrollbar(StockUiRenderer renderer, StockUiPlacement placement, UiMenuFrame frame, float first, int total, int visible)
        {
            float s = placement.Scale;
            float x = placement.X + (frame.Width - ScrollbarInset) * s;
            float y = placement.Y + StockUiRenderer.MenuBandHeight * s;
            float trackHeight = (frame.Height - StockUiRenderer.MenuBandHeight) * s;
            if (trackHeight <= 0 || total <= 0) return;
            renderer.DrawTextureRect(SliderTexture, 30, 3, 1, 1, x, y, ScrollbarWidth * s, trackHeight, ScrollTrackTint);
            float thumbHeight = trackHeight * Math.Min(visible, total) / total;
            float thumbY = y + trackHeight * Math.Clamp(first, 0, total) / total;
            renderer.DrawTextureRect(SliderTexture, 30, 12, 1, 1, x, thumbY, ScrollbarWidth * s, thumbHeight, ScrollThumbTint);
        }

        /// <summary>The Chat Filters state balls live in the frames group ("frames" resolves to "framesus").</summary>
        private const string StateGroup = "frames";
        private const int StateOnImage = 88, StateOffImage = 89;
        private const float ListRowTextInset = 34;
        private const float ListClipInset = 8;

        private const string DefaultCursorGroup = "anc_s";

        private static readonly UiColor PointerColor = new(0x80, 0x80, 0x80, 0x80);

        /// <summary>
        /// The pointer over a clickable entry (retail hides the system arrow there and draws a grey ring over the
        /// arrow's tip): the arrow, then the ring, at one screen pixel per image pixel like the system cursor, the
        /// arrow's tip at the pointer (<see cref="StockUiPointerArt"/>).
        /// </summary>
        public static void DrawHoverPointer(StockUiRenderer renderer, float x, float y)
        {
            const int size = StockUiPointerArt.Size;
            float ax = MathF.Round(x) - StockUiPointerArt.HotspotX, ay = MathF.Round(y) - StockUiPointerArt.HotspotY;
            renderer.DrawTextureRegion(StockUiPointerArt.Arrow.Name, StockUiPointerArt.Arrow, 0, 0, size, size, ax, ay, size, size, PointerColor);
            float rx = MathF.Round(x + StockUiPointerArt.RingOffsetX - size / 2f), ry = MathF.Round(y + StockUiPointerArt.RingOffsetY - size / 2f);
            renderer.DrawTextureRegion(StockUiPointerArt.Ring.Name, StockUiPointerArt.Ring, 0, 0, size, size, rx, ry, size, size, PointerColor);
        }

        public static void DrawMenuCursor(StockUiRenderer renderer, UiResourceLibrary library, UiMenuFrame frame, UiMenuButton button,
            StockUiPlacement placement, long timestamp)
        {
            string groupName = DefaultCursorGroup;
            foreach (var shape in frame.Shapes)
            {
                if (shape.Kind == 6) { groupName = shape.GroupId; break; }
            }
            if (!library.TryGetGroup(groupName, out var group) || group.Images.Count == 0) return;
            var image = StockUiTargetWindow.SelectCursorFrame(group, timestamp);
            float x = placement.X + (frame.CursorOffsetX + button.X + button.CursorOffsetX) * placement.Scale;
            float y = placement.Y + (frame.CursorOffsetY + button.Y + button.CursorOffsetY) * placement.Scale;
            renderer.DrawImage(image, x, y, placement.Scale);
        }
    }
}
