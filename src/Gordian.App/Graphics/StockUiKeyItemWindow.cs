// src/Gordian.App/Graphics/StockUiKeyItemWindow.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws the client content of the Key Items window (#91) and the Equipment Sets windows (#92) over their DAT frames.
    /// PROVISIONAL throughout (no retail capture yet; see <see cref="StockUiKeyItems"/> and <see cref="StockUiEquipSets"/>):
    /// <list type="bullet">
    /// <item>Key Items: one row per category (its name, and the number held right-aligned) or per held key item (its name;
    /// new ones in yellow after a "!" marker), in the log font as the Magic and Abilities lists draw their names; the
    /// selected row in the menus' orange, categories with nothing held greyed; the scrollbar; the help bar ("Key Items"
    /// and the main menu's help); the description of the key item under the cursor at the bottom left, in the item info
    /// window sized to its lines.</item>
    /// <item>Equipment Sets: the set names over <c>mcres20</c>'s capsules (the page's twenty sets) and its page arrows;
    /// beside the options, and in the grid while it is edited, the set's items as icons over <c>mcresed</c>'s slots,
    /// "Remove piece" slots with a red X.</item>
    /// </list>
    /// </summary>
    public static class StockUiKeyItemWindow
    {
        private static readonly UiColor White = new(0x80, 0x80, 0x80, 0x80);
        private static readonly UiColor GreyTint = new(0x4F, 0x4F, 0x65, 0x80);
        private static readonly UiColor NewTint = new(0x80, 0x80, 0x30, 0x80);
        private static readonly UiColor RemoveTint = new(0x80, 0x18, 0x18, 0x80);

        /// <summary>Rows: text from 0 px into the row button (the cursor arrow sits left of it), the count ending 12 px before the frame's right edge.</summary>
        private const float RowTextInset = 0, ValueRightPad = 14, MarkerWidth = 10;

        /// <summary>An empty list's text, as the action lists write theirs.</summary>
        private const float EmptyTextX = 11, EmptyTextY = 20;

        /// <summary>Text in the description window: log font lines 16 px apart from (8, 4) (no icon, unlike the item info window's 48).</summary>
        private const float InfoTextX = 8, InfoTextY = 4;

        public static void Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, long timestamp, StockUiScreen screen)
        {
            if (menu.IsKeyItemWindow) DrawKeyItems(renderer, library, font, logFont, menu, placement, timestamp, screen);
            else if (menu.IsEquipSetList) DrawSetList(renderer, library, font, menu, placement);
            else if (menu.IsEquipSetOptions && !menu.IsEquipSetGridOpen && library.TryGetMenu(StockUiEquipSets.GridMenu, out var grid))
            {
                // The set's grid beside its options (PROVISIONAL), at its authored place relative to the options'.
                var frame = menu.Menu.Frame;
                var at = new StockUiPlacement(placement.X + (grid.Frame.X - frame.X) * placement.Scale, placement.Y + (grid.Frame.Y - frame.Y) * placement.Scale, placement.Scale, false);
                renderer.DrawMenu(grid, at, includeButtons: true, opaqueBody: true);
                DrawSetGrid(renderer, font, menu, grid, at);
            }
            else if (menu.IsEquipSetGrid) DrawSetGrid(renderer, font, menu, menu.Menu, placement);
        }

        private static void Text(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, string text, float x, float y, float s, UiColor color, float rowHeight)
        {
            if (logFont != null) logFont.Draw(renderer, text, x, y + 1 * s, s, color);
            else renderer.DrawText(font, text, x, y + (rowHeight * s - font.LineHeight * s) * 0.5f, s, color);
        }

        private static float Measure(UiFont font, StockUiLogFont? logFont, string text) => logFont != null ? logFont.MeasureWidth(text) : font.MeasureWidth(text);

        private static void DrawKeyItems(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, long timestamp, StockUiScreen screen)
        {
            float s = placement.Scale;
            var definition = menu.Menu;
            var frame = definition.Frame;
            if (screen.Width > 0 && screen.Height > 0)
            {
                StockUiMenuWindow.DrawHelpBar(renderer, library, font, logFont, StockUiKeyItems.Title, menu.ItemHelpText(StockUiKeyItems.HelpKeyItems), s, (uint)screen.Width, (uint)screen.Height);
            }
            if (menu.Rows.Count == 0)
            {
                if (menu.EmptyText is { } empty) Text(renderer, font, logFont, empty, placement.X + EmptyTextX * s, placement.Y + EmptyTextY * s, s, White, 16);
                return;
            }
            if (definition.FindButton(1) is not { } firstRow) return;
            var secondRow = definition.FindButton(2);
            float pitch = secondRow != null && secondRow.Y > firstRow.Y ? secondRow.Y - firstRow.Y : firstRow.Height;
            float first = menu.FirstRow;
            if (menu.ScrollFrom != menu.FirstRow)
            {
                double t = (timestamp - menu.ScrollStartedAt) / (double)Stopwatch.Frequency / StockUiMenuController.ScrollDuration.TotalSeconds;
                if (t < 1) first = menu.ScrollFrom + (menu.FirstRow - menu.ScrollFrom) * (float)Math.Max(0, t);
            }
            float areaY = placement.Y + firstRow.Y * s;
            renderer.SetClip(placement.X, areaY, frame.Width * s, menu.VisibleRows * pitch * s);
            int selected = menu.EntryIndex(menu.SelectedButtonId);
            int from = Math.Max(0, (int)Math.Floor(first) - 1), to = Math.Min(menu.Rows.Count - 1, (int)Math.Ceiling(first) + menu.VisibleRows);
            float textX = placement.X + (firstRow.X + RowTextInset) * s;
            float valueRight = placement.X + (frame.Width - ValueRightPad) * s;
            for (int i = from; i <= to; i++)
            {
                var row = menu.Rows[i];
                float ry = areaY + (i - first) * pitch * s;
                bool greyed = menu.IsKeyItemCategories && menu.IsKeyItemCategoryEmpty(i);
                UiColor color = i == selected ? StockUiMenuWindow.SelectedGlyphTint : greyed ? GreyTint : row.Marked && menu.IsKeyItemList ? NewTint : White;
                float x = textX;
                if (row.Marked)
                {
                    // New key items (and the categories holding some) carry the marker before the name.
                    Text(renderer, font, logFont, StockUiKeyItems.NewMarker, x - MarkerWidth * s, ry, s, NewTint, firstRow.Height);
                }
                Text(renderer, font, logFont, row.Text, x, ry, s, color, firstRow.Height);
                if (row.Value is { Length: > 0 } value)
                {
                    Text(renderer, font, logFont, value, valueRight - Measure(font, logFont, value) * s, ry, s, color, firstRow.Height);
                }
            }
            renderer.ClearClip();
            if (menu.CanScroll) StockUiMenuWindow.DrawScrollbar(renderer, placement, frame, first, menu.Rows.Count, menu.VisibleRows);

            // The description of the key item under the cursor, at the bottom left on Window 1 (as the Items window's).
            var lines = menu.KeyItemDescriptionLines;
            if (lines.Count == 0 || !library.TryGetMenu(StockUiItemDescription.FrameFor(lines.Count), out var info)) return;
            float x0 = placement.X + (info.Frame.X - frame.X) * s;
            float y0 = screen.Height > 0 || screen.Window1Top != null
                ? StockUiItemWindow.BottomOf(screen, s) - info.Frame.Height * s
                : placement.Y + (StockUiItemWindow.DescriptionTop - frame.Y) * s;
            var at = new StockUiPlacement(x0, Math.Max(0, y0), s, false);
            renderer.DrawMenu(info, at, includeButtons: false, opaqueBody: true);
            for (int i = 0; i < lines.Count; i++)
            {
                float ly = at.Y + (InfoTextY + i * StockUiLogFont.CellHeight) * s;
                if (logFont != null) logFont.Draw(renderer, lines[i], at.X + InfoTextX * s, ly, s, White);
                else renderer.DrawText(font, lines[i], at.X + InfoTextX * s, ly, s);
            }
        }

        /// <summary>The page's set names over the capsules, and the page arrows.</summary>
        private static void DrawSetList(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiOpenMenu menu, StockUiPlacement placement)
        {
            float s = placement.Scale;
            var definition = menu.Menu;
            var book = menu.EquipSetBook;
            if (book == null) return;
            int selected = menu.EntryIndex(menu.SelectedButtonId);
            for (int b = 1; b <= menu.VisibleRows; b++)
            {
                int index = menu.FirstRow + b - 1;
                if (index >= Gordian.Core.World.EquipmentSetBook.SetCount || definition.FindButton(b) is not { } button) continue;
                var set = book.Get(index + 1);
                UiColor color = index == selected ? StockUiMenuWindow.SelectedGlyphTint : set.IsEmpty ? GreyTint : White;
                float y = placement.Y + button.Y * s + (button.Height * s - font.LineHeight * s) * 0.5f;
                renderer.DrawText(font, set.Name, placement.X + (button.X + StockUiEquipSets.NameInset) * s, y, s, color);
            }
            // The DAT's page arrows (buttons 21 / 22, outside the frame): the generic drawing shows them only for the main
            // menu's pages, so they are drawn here, each while there is a page that way.
            int pageIndex = menu.FirstRow / StockUiEquipSets.VisibleRows;
            foreach (var (id, show) in new[] { (StockUiEquipSets.PreviousPageButton, pageIndex > 0), (StockUiEquipSets.NextPageButton, pageIndex < Gordian.Core.World.EquipmentSetBook.PageCount - 1) })
            {
                if (!show || definition.FindButton(id) is not { } arrow) continue;
                foreach (var shape in arrow.Shapes)
                {
                    if (shape.Kind != 0 || !library.TryGetImage(shape, out var image)) continue;
                    renderer.DrawImage(image, placement.X + arrow.X * s, placement.Y + arrow.Y * s, s);
                    break;
                }
            }
        }

        /// <summary>The set's items as 32 x 32 icons over the grid's slots; "Remove piece" slots with a red X.</summary>
        private static void DrawSetGrid(StockUiRenderer renderer, UiFont font, StockUiOpenMenu menu, UiMenuDefinition grid, StockUiPlacement at)
        {
            float s = at.Scale;
            foreach (var button in grid.Buttons)
            {
                float x = at.X + button.X * s, y = at.Y + button.Y * s;
                ushort item = menu.EquipSetItemIn(button.ButtonId);
                if (item != 0) StockUiMenuWindow.DrawItemIcon(renderer, menu, item, x, y, 32 * s);
                else if (menu.IsEquipSetRemoved(button.ButtonId))
                {
                    const string Mark = "X";
                    float w = font.MeasureWidth(Mark) * 2 * s;
                    renderer.DrawText(font, Mark, x + (button.Width * s - w) * 0.5f, y + (button.Height * s - font.LineHeight * 2 * s) * 0.5f, 2 * s, RemoveTint);
                }
            }
        }
    }
}
