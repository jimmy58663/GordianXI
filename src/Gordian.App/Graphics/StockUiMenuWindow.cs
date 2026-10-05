// src/Gordian.App/Graphics/StockUiMenuWindow.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
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
            long timestamp, (float X, float Y) companionShift = default, StockUiLogFont? logFont = null)
        {
            var definition = menu.Menu;
            var frame = definition.Frame;
            float s = placement.Scale;
            // Config pages with sliders: the frame art of some pages bakes in a sample fill (conf5w1's first bar
            // shows about 20%); it is left out so the fill drawn for the value is the only one.
            renderer.DrawMenu(definition, placement, includeButtons: false,
                excludeFramePart: menu.SliderFractions.Count > 0 ? IsSliderFill : null, opaqueBody: true, frameImage: menu.FrameImage,
                // The Font Colors list has no title band (retail screenshots, 2026-10-04): opaque to its top edge.
                opaqueTop: menu.ListKind == StockUiListKind.FontColors ? 0 : StockUiRenderer.MenuBandHeight);

            foreach (var button in definition.Buttons)
            {
                bool outsideFrame = button.X < 0 || button.X >= frame.Width;
                if (outsideFrame && menu.PageRing.Count <= 1) continue; // page arrows

                // The selected entry is tinted orange by the client (retail): the glyphs a light orange, the capsule
                // a deeper one; the darkening glyph shadows keep their colour. A button's alternate (kind 4) image is
                // the greyed look where the controller says so (Invite you cannot send, chat modes without a
                // linkshell or Unity); elsewhere (Synthesis, Party, Gamepad) its meaning is still unknown, so it is not drawn.
                bool isSelected = button.ButtonId == menu.SelectedButtonId;
                float bx = placement.X + button.X * s, by = placement.Y + button.Y * s;
                if (TryGetLabel(library, button, menu.IsGreyed(button.ButtonId), out var label))
                {
                    if (!isSelected)
                    {
                        renderer.DrawImage(label, bx, by, s);
                    }
                    else
                    {
                        foreach (var part in label.Parts)
                        {
                            UiColor? tint = part.BlendMode != UiBlendMode.Alpha ? null : IsGlyph(part) ? SelectedGlyphTint : SelectedCapsuleTint;
                            renderer.DrawPart(part, bx, by, s, tint);
                        }
                    }
                }

                if (font != null && menu.SideTexts.TryGetValue(button.ButtonId, out var side) && side.Text.Length > 0)
                {
                    // Client text beside a label (the chat-mode list: the tell partner after Tell's red arrow, "No
                    // Linkshell" / "No Unity" beside greyed modes), centred on the row.
                    renderer.DrawText(font, side.Text, placement.X + side.X * s, by + (button.Height * s - font.LineHeight * s) * 0.5f, s);
                }

                if (menu.SliderFractions.TryGetValue(button.ButtonId, out float fraction) && fraction > 0)
                {
                    // The bar is authored in the frame (a translucent "gauge" strip 192 wide, knobs at both ends); the
                    // value is the same strip's rows 1-6 drawn opaque and tinted light blue from the bar's left edge,
                    // 12 tall inside the 16-tall bar (a retail capture, 2026-09-26; "framesus" #103 is the same
                    // strip with the tint (64, 96, 127), the capture's fill reads a little more lavender).
                    var track = menu.ConfigPage?.SliderTrack ?? (0, button.Width, SliderFillTop, SliderFillHeight);
                    renderer.DrawTextureRect(SliderTexture, SliderFillSourceX, SliderFillSourceY, SliderFillSourceWidth, SliderFillSourceHeight,
                        bx + track.Left * s, by + track.FillTop * s, track.Width * fraction * s, track.FillHeight * s, SliderTint(menu, button.ButtonId));
                }

                if (menu.IsMarked(button.ButtonId) && menu.Rows.Count == 0)
                {
                    renderer.DrawTextureRect(BarTexture, BarSourceX, BarSourceY, 1, 1, bx, by + button.Height * s,
                        button.Width * s, BarThickness * s, BarColor);
                }
            }

            if (font != null && menu.IsShopList && definition.FindButton(1) is { } firstShopRow)
            {
                DrawShopList(renderer, library, font, menu, placement, firstShopRow, timestamp, companionShift);
            }
            else if (font != null && menu.IsQuantity)
            {
                DrawQuantity(renderer, library, font, menu, placement);
            }
            else if (font != null && menu.IsQuery && definition.FindButton(1) is { } firstQueryRow)
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
                bool colorRows = menu.ListKind == StockUiListKind.FontColors;
                for (int i = from; i <= to; i++)
                {
                    var row = menu.Rows[i];
                    float ry = areaY + (i - first) * pitch * s;
                    float textY = ry + (firstRow.Height * s - font.LineHeight * s) * 0.5f;
                    if (colorRows)
                    {
                        // Font Colors rows: the message type in white in the log font, no state ball (retail
                        // screenshots, 2026-10-04: upright text like the log's).
                        if (logFont != null) logFont.Draw(renderer, row.Text, areaX + FontColorRowTextInset * s, ry, s, White);
                        else renderer.DrawText(font, row.Text, areaX + FontColorRowTextInset * s, textY, s, row.Color);
                        continue;
                    }
                    int stateImage = row.Marked ? StateOnImage : StateOffImage;
                    if (states != null && stateImage < states.Images.Count) renderer.DrawImage(states.Images[stateImage], areaX, ry, s);
                    renderer.DrawText(font, row.Text, areaX + ListRowTextInset * s, textY, s,
                        i == selectedEntry ? SelectedGlyphTint : null);
                }
                renderer.ClearClip();
                if (menu.CanScroll) DrawScrollbar(renderer, placement, frame, first, menu.Rows.Count, menu.VisibleRows);
                if (colorRows) DrawFontColorBox(renderer, library, font, logFont, menu, placement);
            }

            if (font != null && menu.SampleText is { Length: > 0 } sample)
            {
                // The colour editor: the row's sample in the colour of its sliders, where the box above the list shows
                // it, cut with ".." when it does not fit before the bars' labels.
                float sampleY = placement.Y + (frame.Height * s - font.LineHeight * s) * 0.5f;
                float sampleWidth = SampleTextRight - SampleTextX;
                Func<string, float> measure = logFont != null ? t => logFont.MeasureWidth(t) : t => font.MeasureWidth(t);
                string shown = sample;
                while (shown.Length > 1 && measure(shown) > sampleWidth) shown = shown[..^1];
                if (shown.Length < sample.Length) shown = shown.TrimEnd() + "..";
                if (logFont != null) logFont.Draw(renderer, shown, placement.X + SampleTextX * s, placement.Y + (frame.Height - StockUiLogFont.CellHeight) * 0.5f * s, s, menu.SampleColor);
                else renderer.DrawText(font, shown, placement.X + SampleTextX * s, sampleY, s, menu.SampleColor);
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
        /// The shop list's row layout (the "shop" frame, Tier 2 chunk 6c): the DAT authors ten invisible 132 x 16 rows
        /// at an 18 px pitch with a 16 x 16 icon slot at x 3; the name follows the slot and the price (Buy) or the
        /// count held (Sell) is right-aligned inside the frame. The item's 32 x 32 DAT icon is drawn into the slot.
        /// Not captured yet: retail's exact text insets and how it prints the price.
        /// </summary>
        private const float ShopIconX = 3, ShopIconSize = 16, ShopRowTextX = 22, ShopRowRightInset = 8;

        /// <summary>Half-scale tint of a row that cannot be sold (the item's NoSale flag): a mid grey.</summary>
        private static readonly UiColor GreyedTextTint = new(0x48, 0x48, 0x48, 0x80);

        /// <summary>
        /// The item info window: its 32 x 32 icon slot is authored at (8,12); the long name and the description's lines
        /// follow it (the maintainer's in-game capture, 2026-09-28: name, then two description lines).
        /// </summary>
        private const float InfoIconX = 8, InfoIconY = 12, InfoIconSize = 32, InfoTextX = 48, InfoTextY = 8, InfoLinePitch = 14, InfoRightInset = 8;

        /// <summary>
        /// The DAT authors "money", "itemctrl" and "iteminfo" all at (16,240), right under the list, and that is where
        /// retail draws them (in-game check 2026-09-28): the gil window, or the quantity prompt in its place, at the
        /// list's left and the item info window beside it, 2 px right of the gil window's 112 px.
        /// </summary>
        private const float InfoOffsetX = 114;

        /// <summary>The "Current Gil" window: the amount sits under the label band, right-aligned.</summary>
        private const float GilTextY = 30, GilRightInset = 8;

        /// <summary>Item icons decoded from the item DATs, by item id (null when the record has none).</summary>
        private static readonly ConcurrentDictionary<ushort, DecodedTexture?> ItemIcons = new();

        private static DecodedTexture? ItemIcon(StockUiOpenMenu menu, ushort itemId)
        {
            if (itemId == 0) return null;
            if (ItemIcons.TryGetValue(itemId, out var cached)) return cached;
            var record = menu.ItemLookup?.Invoke(itemId);
            var pixels = record?.IconRgbaPixels;
            var icon = pixels != null && pixels.Length >= 32 * 32 * 4 ? new DecodedTexture($"item{itemId}", 32, 32, pixels) : null;
            ItemIcons[itemId] = icon;
            return icon;
        }

        private static void DrawItemIcon(StockUiRenderer renderer, StockUiOpenMenu menu, ushort itemId, float x, float y, float size)
        {
            var icon = ItemIcon(menu, itemId);
            if (icon != null) renderer.DrawTexture($"item:{itemId}", icon, x, y, size, size, PointerColor);
        }

        /// <summary>
        /// A shop list (Buy or Sell): one item per row over the invisible row buttons, an icon in the slot, the name,
        /// and the price or count at the right; long lists scroll like the other lists, with the scrollbar. The
        /// "Current Gil" window is drawn under the list and the item info window beside it (their authored places
        /// relative to the list's; where retail puts them is not captured yet).
        /// </summary>
        private static void DrawShopList(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiOpenMenu menu,
            StockUiPlacement placement, UiMenuButton firstRow, long timestamp, (float X, float Y) companionShift)
        {
            float s = placement.Scale;
            var definition = menu.Menu;
            var secondRow = definition.FindButton(2);
            float pitch = secondRow != null && secondRow.Y > firstRow.Y ? secondRow.Y - firstRow.Y : firstRow.Height;
            float first = menu.FirstRow;
            if (menu.ScrollFrom != menu.FirstRow)
            {
                double t = (timestamp - menu.ScrollStartedAt) / (double)Stopwatch.Frequency / StockUiMenuController.ScrollDuration.TotalSeconds;
                if (t < 1) first = menu.ScrollFrom + (menu.FirstRow - menu.ScrollFrom) * (float)Math.Max(0, t);
            }
            var frame = definition.Frame;
            float areaY = placement.Y + firstRow.Y * s;
            float areaH = menu.VisibleRows * pitch * s;
            if (menu.ShopRows.Count > 0 && menu.VisibleRows > 0)
            {
                renderer.SetClip(placement.X, areaY, frame.Width * s, areaH);
                int selectedEntry = menu.EntryIndex(menu.SelectedButtonId);
                int from = Math.Max(0, (int)Math.Floor(first) - 1), to = Math.Min(menu.ShopRows.Count - 1, (int)Math.Ceiling(first) + menu.VisibleRows);
                float rightEdge = placement.X + (frame.Width - ShopRowRightInset) * s;
                for (int i = from; i <= to; i++)
                {
                    var row = menu.ShopRows[i];
                    float ry = areaY + (i - first) * pitch * s;
                    float textY = ry + (firstRow.Height * s - font.LineHeight * s) * 0.5f;
                    DrawItemIcon(renderer, menu, row.ItemId, placement.X + ShopIconX * s, ry, ShopIconSize * s);
                    UiColor? tint = i == selectedEntry ? SelectedGlyphTint : row.Greyed ? GreyedTextTint : null;
                    renderer.DrawText(font, row.Name, placement.X + ShopRowTextX * s, textY, s, tint);
                    // The price ("29 G"); a Sell row shows it once the server has appraised the row.
                    string right = menu.ShopSide == StockUiShopSide.Buy || row.Price > 0 ? StockUiShop.FormatPrice(row.Price) : string.Empty;
                    if (right.Length > 0) renderer.DrawText(font, right, rightEdge - font.MeasureWidth(right) * s, textY, s, tint);
                }
                renderer.ClearClip();
                if (menu.CanScroll) DrawScrollbar(renderer, placement, frame, first, menu.ShopRows.Count, menu.VisibleRows);
            }

            // The gil window and the item info sit right under the list (companionShift is the HUD's displacement of
            // the list, if any); an open quantity prompt stands in the gil window's place.
            if (!menu.ShowsQuantity) DrawGilWindow(renderer, library, font, menu, placement, companionShift);
            if (menu.SelectedShopRow is { } selected) DrawInfoWindow(renderer, library, font, menu, placement, selected, companionShift);
        }

        /// <summary>The "Current Gil" window ("money", authored under the list) with the amount right-aligned under its label.</summary>
        private static void DrawGilWindow(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiOpenMenu menu, StockUiPlacement placement,
            (float X, float Y) shift)
        {
            if (!library.TryGetMenu(StockUiShop.GilMenu, out var gil) || !library.TryGetMenu(StockUiShop.ListMenu, out var list)) return;
            float s = placement.Scale;
            var at = new StockUiPlacement(placement.X + (gil.Frame.X - list.Frame.X) * s + shift.X, placement.Y + (gil.Frame.Y - list.Frame.Y) * s + shift.Y, s, false);
            renderer.DrawMenu(gil, at, includeButtons: false, opaqueBody: true);
            string amount = StockUiShop.FormatPrice(menu.Gil);
            renderer.DrawText(font, amount, at.X + (gil.Frame.Width - GilRightInset) * s - font.MeasureWidth(amount) * s, at.Y + GilTextY * s, s);
        }

        /// <summary>
        /// The item info window ("iteminfo", beside the gil window): the item's 32 x 32 icon, its long name, then the
        /// DAT description wrapped to the lines that fit (retail shows no price here and the window does not change
        /// while a count is chosen; in-game check 2026-09-28).
        /// </summary>
        private static void DrawInfoWindow(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiOpenMenu menu,
            StockUiPlacement placement, in StockUiShopRow row, (float X, float Y) shift)
        {
            if (!library.TryGetMenu(StockUiShop.InfoMenu, out var info) || !library.TryGetMenu(StockUiShop.ListMenu, out var list)
                || !library.TryGetMenu(StockUiShop.GilMenu, out var gil)) return;
            float s = placement.Scale;
            float originX = placement.X + (gil.Frame.X - list.Frame.X) * s + shift.X;
            float originY = placement.Y + (gil.Frame.Y - list.Frame.Y) * s + shift.Y;
            var at = new StockUiPlacement(originX + InfoOffsetX * s, originY, s, false);
            renderer.DrawMenu(info, at, includeButtons: false, opaqueBody: true);
            DrawItemIcon(renderer, menu, row.ItemId, at.X + InfoIconX * s, at.Y + InfoIconY * s, InfoIconSize * s);
            var record = menu.ItemLookup?.Invoke(row.ItemId);
            float x = at.X + InfoTextX * s, y = at.Y + InfoTextY * s;
            renderer.DrawText(font, record != null ? StockUiShop.LongName(record) : row.Name, x, y, s);
            int width = (int)(info.Frame.Width - InfoTextX - InfoRightInset);
            int maxLines = (int)((info.Frame.Height - InfoTextY - InfoLinePitch) / InfoLinePitch);
            string? description = record?.Description;
            if (string.IsNullOrWhiteSpace(description) || maxLines <= 0) return;
            foreach (string wrapped in Wrap(font, description, width, maxLines))
            {
                y += InfoLinePitch * s;
                renderer.DrawText(font, wrapped, x, y, s);
            }
        }

        /// <summary>Word-wraps text to a layout width (unscaled), keeping the DAT's own line breaks; at most <paramref name="maxLines"/> lines.</summary>
        internal static List<string> Wrap(UiFont font, string text, int width, int maxLines)
        {
            var lines = new List<string>();
            foreach (string paragraph in text.Replace("\r", string.Empty).Split('\n'))
            {
                string current = string.Empty;
                foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    string candidate = current.Length == 0 ? word : current + " " + word;
                    if (current.Length > 0 && font.MeasureWidth(candidate) > width)
                    {
                        lines.Add(current);
                        if (lines.Count >= maxLines) return lines;
                        current = word;
                    }
                    else
                    {
                        current = candidate;
                    }
                }
                if (current.Length > 0) lines.Add(current);
                if (lines.Count >= maxLines) return lines;
            }
            return lines;
        }

        /// <summary>
        /// The quantity control's number field ("itemctrl" button 1, 24 x 16 at (34,22)) prints "1 /12": the count
        /// right-aligned inside the field, then "/" and the total running past its right edge towards the "1" arrow
        /// (the maintainer's in-game capture, 2026-09-28).
        /// </summary>
        private const float QuantityCountRight = 15, QuantityTotalX = 19;

        /// <summary>
        /// The quantity prompt: the DAT frame carries the arrows ("All" / "1" either side, + above, - below) and the
        /// number field; the chosen count and the total are drawn in the field. The item info window beside it is the
        /// list's, unchanged.
        /// </summary>
        private static void DrawQuantity(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiOpenMenu menu, StockUiPlacement placement)
        {
            float s = placement.Scale;
            if (menu.Menu.FindButton(StockUiShop.QuantityField) is not { } field) return;
            string count = menu.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string total = "/" + menu.QuantityTotal.ToString(System.Globalization.CultureInfo.InvariantCulture);
            float y = placement.Y + field.Y * s + (field.Height * s - font.LineHeight * s) * 0.5f;
            renderer.DrawText(font, count, placement.X + (field.X + QuantityCountRight) * s - font.MeasureWidth(count) * s, y, s);
            renderer.DrawText(font, total, placement.X + (field.X + QuantityTotalX) * s, y, s);
        }

        /// <summary>
        /// A button's label image: its kind-4 alternate when it is greyed and the DAT has one, else its kind-0 image.
        /// </summary>
        private static bool TryGetLabel(UiResourceLibrary library, UiMenuButton button, bool greyed, out UiImage image)
        {
            if (greyed)
            {
                foreach (var shape in button.Shapes)
                {
                    if (shape.Kind == 4 && library.TryGetImage(shape, out image)) return true;
                }
            }
            foreach (var shape in button.Shapes)
            {
                if (shape.Kind == 0 && library.TryGetImage(shape, out image)) return true;
            }
            image = null!;
            return false;
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
        /// The colour editor's bars fill in their channel's colour (retail screenshots, 2026-10-05: a dark red R bar,
        /// green G bar, navy B bar); every other slider takes the light blue fill.
        /// </summary>
        private static UiColor SliderTint(StockUiOpenMenu menu, int buttonId)
        {
            if (!menu.Name.Equals(StockUiConfigPages.FontColorEditPage, StringComparison.OrdinalIgnoreCase)) return SliderFillTint;
            return buttonId switch
            {
                1 => new UiColor(0x60, 0x08, 0x08, 0x80),
                2 => new UiColor(0x08, 0x60, 0x08, 0x80),
                _ => new UiColor(0x08, 0x08, 0x38, 0x80),
            };
        }

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

        /// <summary>The Font Colors list's text inset (its rows start at the frame's edge; retail screenshot, 2026-10-04: about 30 px).</summary>
        private const float FontColorRowTextInset = 30;

        /// <summary>
        /// Where the sample text sits in the Font Colors box (<c>textcol2</c>) and the colour editor (<c>textcol3</c>,
        /// the same 366 x 56 rectangle): 16 px in, centred on the window's height (retail screenshot, 2026-10-04); in the
        /// editor it stops before the "R G B" labels at x ~200.
        /// </summary>
        private const float SampleTextX = 16, SampleTextRight = 172;

        /// <summary>
        /// The box above a Font Colors list (<c>textcol2</c>, authored 58 px above <c>textcol1</c>): its frame, and the
        /// selected row's sample in the row's colour.
        /// </summary>
        private static void DrawFontColorBox(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont,
            StockUiOpenMenu list, StockUiPlacement placement)
        {
            if (!library.TryGetMenu(StockUiConfigPages.FontColorSampleBox, out var box)) return;
            float s = placement.Scale;
            var listFrame = list.Menu.Frame;
            var at = new StockUiPlacement(placement.X + (box.Frame.X - listFrame.X) * s, placement.Y + (box.Frame.Y - listFrame.Y) * s, s, false);
            renderer.DrawMenu(box, at, includeButtons: false, opaqueBody: true);
            if (list.SelectedFontColorSample is not { } sample || sample.Text.Length == 0) return;
            if (logFont != null) logFont.Draw(renderer, sample.Text, at.X + SampleTextX * s, at.Y + (box.Frame.Height - StockUiLogFont.CellHeight) * 0.5f * s, s, sample.Color);
            else renderer.DrawText(font, sample.Text, at.X + SampleTextX * s, at.Y + (box.Frame.Height * s - font.LineHeight * s) * 0.5f, s, sample.Color);
        }
        private const float ListClipInset = 8;

        private const string DefaultCursorGroup = "anc_s";

        private static readonly UiColor PointerColor = new(0x80, 0x80, 0x80, 0x80);
        private static readonly UiColor White = new(0x80, 0x80, 0x80, 0x80);

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

        /// <summary>Draws a label in the selected entry's orange tint (glyphs light, capsule deeper), as the menus do.</summary>
        internal static void DrawSelectedImage(StockUiRenderer renderer, UiImage label, float x, float y, float scale)
        {
            foreach (var part in label.Parts)
            {
                UiColor? tint = part.BlendMode != UiBlendMode.Alpha ? null : IsGlyph(part) ? SelectedGlyphTint : SelectedCapsuleTint;
                renderer.DrawPart(part, x, y, scale, tint);
            }
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
