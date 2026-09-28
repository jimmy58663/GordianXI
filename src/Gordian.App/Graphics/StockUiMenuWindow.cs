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
            long timestamp)
        {
            var definition = menu.Menu;
            var frame = definition.Frame;
            float s = placement.Scale;
            // Config pages with sliders: the frame art of some pages bakes in a sample fill (conf5w1's first bar
            // shows about 20%); it is left out so the fill drawn for the value is the only one.
            renderer.DrawMenu(definition, placement, includeButtons: false,
                excludeFramePart: menu.SliderFractions.Count > 0 ? IsSliderFill : null, opaqueBody: true, frameImage: menu.FrameImage);

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
                    renderer.DrawTextureRect(SliderTexture, SliderFillSourceX, SliderFillSourceY, SliderFillSourceWidth, SliderFillSourceHeight,
                        bx, by + SliderFillTop * s, button.Width * fraction * s, SliderFillHeight * s, SliderFillTint);
                }

                if (menu.IsMarked(button.ButtonId) && menu.Rows.Count == 0)
                {
                    renderer.DrawTextureRect(BarTexture, BarSourceX, BarSourceY, 1, 1, bx, by + button.Height * s,
                        button.Width * s, BarThickness * s, BarColor);
                }
            }

            if (font != null && menu.IsShopList && definition.FindButton(1) is { } firstShopRow)
            {
                DrawShopList(renderer, library, font, menu, placement, firstShopRow, timestamp);
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
        /// The shop list's row layout (the "shop" frame, Tier 2 chunk 6c): the DAT authors ten invisible 132 x 16 rows
        /// at an 18 px pitch with a 16 x 16 icon slot at x 3; the name follows the slot and the price (Buy) or the
        /// count held (Sell) is right-aligned inside the frame. The item's 32 x 32 DAT icon is drawn into the slot.
        /// Not captured yet: retail's exact text insets and how it prints the price.
        /// </summary>
        private const float ShopIconX = 3, ShopIconSize = 16, ShopRowTextX = 22, ShopRowRightInset = 8;

        /// <summary>Half-scale tint of a row that cannot be sold (the item's NoSale flag): a mid grey.</summary>
        private static readonly UiColor GreyedTextTint = new(0x48, 0x48, 0x48, 0x80);

        /// <summary>The item info window: its 32 x 32 icon slot is authored at (8,12); the text starts after it.</summary>
        private const float InfoIconX = 8, InfoIconY = 12, InfoIconSize = 32, InfoTextX = 48, InfoTextY = 8, InfoLinePitch = 14, InfoRightInset = 8;

        /// <summary>
        /// The DAT authors "money", "itemctrl" and "iteminfo" all at (16,240), under the list; which retail shows where
        /// is not captured yet. Here the gil window (or the quantity prompt in its place) keeps that spot and the item
        /// info window sits beside it, 2 px right of the gil window's 112 px.
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
            StockUiPlacement placement, UiMenuButton firstRow, long timestamp)
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
                    string right = menu.ShopSide == StockUiShopSide.Buy ? StockUiShop.FormatGil(row.Price)
                        : row.Count > 1 ? row.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
                    if (right.Length > 0) renderer.DrawText(font, right, rightEdge - font.MeasureWidth(right) * s, textY, s, tint);
                }
                renderer.ClearClip();
                if (menu.CanScroll) DrawScrollbar(renderer, placement, frame, first, menu.ShopRows.Count, menu.VisibleRows);
            }

            if (menu.ShowsQuantity) return; // the prompt stands where the gil window goes and draws the item info itself
            DrawGilWindow(renderer, library, font, menu, placement);
            if (menu.SelectedShopRow is { } selected)
            {
                string line = menu.ShopSide == StockUiShopSide.Buy
                    ? $"{StockUiShop.FormatGil(selected.Price)} gil"
                    : selected.Greyed ? "This item cannot be sold." : $"Have: {selected.Count}";
                DrawInfoWindow(renderer, library, font, menu, placement, selected, line, describe: true);
            }
        }

        /// <summary>The "Current Gil" window ("money", authored under the list) with the amount right-aligned under its label.</summary>
        private static void DrawGilWindow(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiOpenMenu menu, StockUiPlacement placement)
        {
            if (!library.TryGetMenu(StockUiShop.GilMenu, out var gil) || !library.TryGetMenu(StockUiShop.ListMenu, out var list)) return;
            float s = placement.Scale;
            var at = new StockUiPlacement(placement.X + (gil.Frame.X - list.Frame.X) * s, placement.Y + (gil.Frame.Y - list.Frame.Y) * s, s, false);
            renderer.DrawMenu(gil, at, includeButtons: false, opaqueBody: true);
            string amount = StockUiShop.FormatGil(menu.Gil);
            renderer.DrawText(font, amount, at.X + (gil.Frame.Width - GilRightInset) * s - font.MeasureWidth(amount) * s, at.Y + GilTextY * s, s);
        }

        /// <summary>
        /// The item info window ("iteminfo", authored beside the gil window): the item's 32 x 32 icon, its name, then
        /// <paramref name="line"/> and, when <paramref name="describe"/> is set, the DAT description wrapped to the
        /// window (the lines that fit under the name).
        /// </summary>
        private static void DrawInfoWindow(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiOpenMenu menu,
            StockUiPlacement placement, in StockUiShopRow row, string line, bool describe)
        {
            if (!library.TryGetMenu(StockUiShop.InfoMenu, out var info) || !library.TryGetMenu(StockUiShop.ListMenu, out var list)
                || !library.TryGetMenu(StockUiShop.GilMenu, out var gil)) return;
            float s = placement.Scale;
            // Beside the gil window under the list, or beside the quantity prompt that has taken the gil window's place.
            float originX = menu.IsQuantity ? placement.X : placement.X + (gil.Frame.X - list.Frame.X) * s;
            float originY = menu.IsQuantity ? placement.Y : placement.Y + (gil.Frame.Y - list.Frame.Y) * s;
            var at = new StockUiPlacement(originX + InfoOffsetX * s, originY, s, false);
            renderer.DrawMenu(info, at, includeButtons: false, opaqueBody: true);
            DrawItemIcon(renderer, menu, row.ItemId, at.X + InfoIconX * s, at.Y + InfoIconY * s, InfoIconSize * s);
            float x = at.X + InfoTextX * s, y = at.Y + InfoTextY * s;
            renderer.DrawText(font, row.Name, x, y, s);
            y += InfoLinePitch * s;
            renderer.DrawText(font, line, x, y, s);
            if (!describe) return;
            int width = (int)(info.Frame.Width - InfoTextX - InfoRightInset);
            int maxLines = (int)((info.Frame.Height - InfoTextY - 2 * InfoLinePitch) / InfoLinePitch);
            string? description = menu.ItemLookup?.Invoke(row.ItemId)?.Description;
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

        /// <summary>The quantity control's number field ("itemctrl" button 1, 24 x 16 at (34,22)): the count right-aligned inside it.</summary>
        private const float QuantityFieldRightInset = 3;

        /// <summary>
        /// The quantity prompt: the DAT frame carries the arrows ("All" / "1" either side, + above, - below) and the
        /// number field; the chosen count is drawn in the field, and the item info window beside the prompt shows
        /// the item with the unit price and the total.
        /// </summary>
        private static void DrawQuantity(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiOpenMenu menu, StockUiPlacement placement)
        {
            float s = placement.Scale;
            if (menu.Menu.FindButton(StockUiShop.QuantityField) is { } field)
            {
                string count = menu.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture);
                float x = placement.X + (field.X + field.Width - QuantityFieldRightInset) * s - font.MeasureWidth(count) * s;
                float y = placement.Y + field.Y * s + (field.Height * s - font.LineHeight * s) * 0.5f;
                renderer.DrawText(font, count, x, y, s);
            }
            var row = menu.QuantityRow;
            string unit = StockUiShop.FormatGil(menu.UnitPrice), total = StockUiShop.FormatGil(menu.TotalPrice);
            string line = menu.ShopSide == StockUiShopSide.Buy
                ? $"{unit} gil x {menu.Quantity} = {total} gil"
                : $"Sells for {unit} gil x {menu.Quantity} = {total} gil";
            DrawInfoWindow(renderer, library, font, menu, placement, row, line, describe: false);
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
