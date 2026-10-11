// src/Gordian.App/Graphics/StockUiItemWindow.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws the Items window (#351) and the Equipment window (#352) client content over their DAT frames, and the item
    /// description window they share with the check window (#64):
    /// <list type="bullet">
    /// <item>the item list (<c>inventor</c>; both windows): per row the item's icon in the row's slot box, its name after
    /// it and, for stackable items, the count right-aligned; worn items in the equipped colour, greyed rows grey; the list
    /// scrolls with the shared scrollbar; the storage name and "used/total" on the top border;</item>
    /// <item>the Equipment grid (<c>equip</c>): the worn items' icons over their slots and the stats side (<c>persona</c>:
    /// the name and jobs at the top, then max HP, max MP, TP and STR ... CHR with the equipment bonus);</item>
    /// <item>the description of the item under the cursor (<see cref="StockUiItemDescription"/>) in the item info window
    /// sized to its lines, under the window at layout y 240, as the check window's, paged with the gamepad's X.</item>
    /// </list>
    /// PROVISIONAL (not checked against retail captures): the row insets and count place, the header, the equipped tint,
    /// the stats side's values and the description's place.
    /// </summary>
    public static class StockUiItemWindow
    {
        /// <summary>The row layout: the 16 x 16 slot box at the row's origin, the name from 19 px in, the count 10 px from the frame's right edge.</summary>
        private const float IconSize = 16, NameInset = 19, CountRightInset = 10;

        /// <summary>The header on the top border: storage name 8 px in, "used/total" right-aligned 10 px from the edge, at the title band's height.</summary>
        private const float HeaderInset = 8, HeaderY = -4;

        /// <summary>The description window's top (layout y), under the list or the grid, as the check window's.</summary>
        public const float DescriptionTop = 240;

        /// <summary>Text inside the description window: log font lines 16 px apart from (48,4), the 32 x 32 icon at (8,12).</summary>
        private const float InfoIconX = 8, InfoIconY = 12, InfoIconSize = 32, InfoTextX = 48, InfoTextY = 4, InfoRightInset = 8;

        /// <summary>The stats side: values right-aligned 8 px from <c>persona</c>'s right edge on its baked rows (HP y 58 ... CHR y 174).</summary>
        private static readonly float[] StatRowsY = { 58, 70, 82, 96, 109, 122, 135, 148, 161, 174 };
        private const float StatsRightInset = 6, StatsNameY = 10, StatsJobsY = 26, StatsTextX = 6;

        private static readonly UiColor White = new(0x80, 0x80, 0x80, 0x80);
        private static readonly UiColor GreyTint = new(0x48, 0x48, 0x48, 0x80);

        /// <summary>Worn items' names: a pale blue-white (PROVISIONAL; retail's equipped colour is not captured).</summary>
        private static readonly UiColor EquippedTint = new(0x60, 0x70, 0x80, 0x80);

        /// <summary>A bonus stat in blue, a malus in red (PROVISIONAL: as the retail status window colours them).</summary>
        private static readonly UiColor BonusTint = new(0x50, 0x68, 0x80, 0x80), MalusTint = new(0x80, 0x40, 0x40, 0x80);

        /// <summary>An item list: rows, scrollbar, header and the selected item's description.</summary>
        public static void DrawList(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, long timestamp)
        {
            float s = placement.Scale;
            var definition = menu.Menu;
            var frame = definition.Frame;
            if (definition.FindButton(1) is not { } firstRow) return;
            var secondRow = definition.FindButton(2);
            float pitch = secondRow != null && secondRow.Y > firstRow.Y ? secondRow.Y - firstRow.Y : firstRow.Height;
            float first = menu.FirstRow;
            if (menu.ScrollFrom != menu.FirstRow)
            {
                double t = (timestamp - menu.ScrollStartedAt) / (double)Stopwatch.Frequency / StockUiMenuController.ScrollDuration.TotalSeconds;
                if (t < 1) first = menu.ScrollFrom + (menu.FirstRow - menu.ScrollFrom) * (float)Math.Max(0, t);
            }
            var rows = menu.ItemRows;
            float areaX = placement.X + firstRow.X * s, areaY = placement.Y + firstRow.Y * s;
            float areaH = menu.VisibleRows * pitch * s;
            float right = placement.X + (frame.Width - CountRightInset) * s;
            if (rows.Count > 0 && menu.VisibleRows > 0)
            {
                renderer.SetClip(placement.X, areaY, frame.Width * s, areaH);
                int selected = menu.EntryIndex(menu.SelectedButtonId);
                int from = Math.Max(0, (int)Math.Floor(first) - 1), to = Math.Min(rows.Count - 1, (int)Math.Ceiling(first) + menu.VisibleRows);
                for (int i = from; i <= to; i++)
                {
                    var row = rows[i];
                    float ry = areaY + (i - first) * pitch * s;
                    float textY = ry + (firstRow.Height * s - font.LineHeight * s) * 0.5f;
                    if (row.ItemId != 0) StockUiMenuWindow.DrawItemIcon(renderer, menu, row.ItemId, areaX, ry, IconSize * s);
                    UiColor? tint = i == selected ? StockUiMenuWindow.SelectedGlyphTint : row.Greyed ? GreyTint : row.Equipped ? EquippedTint : null;
                    renderer.DrawText(font, row.Name, areaX + NameInset * s, textY, s, tint);
                    if (row.ShowsCount)
                    {
                        string count = row.Count.ToString(CultureInfo.InvariantCulture);
                        renderer.DrawText(font, count, right - font.MeasureWidth(count) * s, textY, s, tint);
                    }
                }
                renderer.ClearClip();
                if (menu.CanScroll) StockUiMenuWindow.DrawScrollbar(renderer, placement, frame, first, rows.Count, menu.VisibleRows);
            }

            // The header on the top border: which storage, and how full it is (the Equipment list names its slot).
            float ts = s * StockUiPartyWindow.TextScale;
            string title = menu.ItemListKind == StockUiItemListKind.Equipment ? SlotName(menu.ItemEquipSlot) : StockUiItems.ContainerName(menu.ItemContainer);
            renderer.DrawText(font, title, placement.X + HeaderInset * s, placement.Y + HeaderY * s, ts);
            if (menu.ItemListKind == StockUiItemListKind.Items && menu.ItemFill.Total > 0)
            {
                string fill = string.Create(CultureInfo.InvariantCulture, $"{menu.ItemFill.Used}/{menu.ItemFill.Total}");
                renderer.DrawText(font, fill, right - font.MeasureWidth(fill) * ts, placement.Y + HeaderY * s, ts);
            }

            if (!menu.ShowsQuantity) DrawDescription(renderer, library, font, logFont, menu, placement, timestamp);
        }

        private static string SlotName(EquipSlotId slot) => slot switch
        {
            EquipSlotId.Ranged => "Range",
            EquipSlotId.Ear1 => "Left Ear",
            EquipSlotId.Ear2 => "Right Ear",
            EquipSlotId.Ring1 => "Left Ring",
            EquipSlotId.Ring2 => "Right Ring",
            _ => slot.ToString(),
        };

        /// <summary>Whether a grid button's slot holds a worn item (its label is then left out under the icon).</summary>
        public static bool HoldsItem(StockUiOpenMenu menu, int buttonId) => menu.EquippedItemIn(buttonId) != 0;

        /// <summary>The Equipment grid: the worn items' icons, the stats side at its left, and the slot's item description.</summary>
        public static void DrawEquipment(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, long timestamp)
        {
            float s = placement.Scale;
            var definition = menu.Menu;
            foreach (var button in definition.Buttons)
            {
                ushort item = menu.EquippedItemIn(button.ButtonId);
                if (item != 0) StockUiMenuWindow.DrawItemIcon(renderer, menu, item, placement.X + button.X * s, placement.Y + button.Y * s, 32 * s);
            }
            DrawStats(renderer, library, font, menu, placement);
            if (!menu.IsListOpen) DrawDescription(renderer, library, font, logFont, menu, placement, timestamp);
        }

        /// <summary>The stats side (<c>persona</c>, authored at 16,48 left of the grid): name and jobs at the top, then the values on its baked rows.</summary>
        private static void DrawStats(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiOpenMenu menu, StockUiPlacement placement)
        {
            if (!library.TryGetMenu(StockUiItems.StatsMenu, out var persona)) return;
            float s = placement.Scale;
            var frame = menu.Menu.Frame;
            var at = new StockUiPlacement(placement.X + (persona.Frame.X - frame.X) * s, placement.Y + (persona.Frame.Y - frame.Y) * s, s, false);
            renderer.DrawMenu(persona, at, includeButtons: false, opaqueBody: true);
            if (menu.CharacterStats?.Invoke() is not { } stats) return;
            float ts = s * StockUiPartyWindow.TextScale;
            renderer.DrawText(font, stats.Name, at.X + StatsTextX * s, at.Y + StatsNameY * s, ts);
            renderer.DrawText(font, stats.Jobs, at.X + StatsTextX * s, at.Y + StatsJobsY * s, ts);
            float right = at.X + (persona.Frame.Width - StatsRightInset) * s;
            void Value(int row, string text, UiColor? tint = null)
            {
                if (row >= StatRowsY.Length) return;
                renderer.DrawText(font, text, right - font.MeasureWidth(text) * ts, at.Y + StatRowsY[row] * s, ts, tint);
            }
            Value(0, stats.MaxHp.ToString(CultureInfo.InvariantCulture));
            Value(1, stats.MaxMp.ToString(CultureInfo.InvariantCulture));
            Value(2, stats.Tp.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < 7 && i < stats.BaseStats.Count; i++)
            {
                int bonus = i < stats.StatBonuses.Count ? stats.StatBonuses[i] : 0;
                string text = bonus == 0
                    ? stats.BaseStats[i].ToString(CultureInfo.InvariantCulture)
                    : string.Create(CultureInfo.InvariantCulture, $"{stats.BaseStats[i]}{(bonus > 0 ? "+" : "")}{bonus}");
                Value(3 + i, text, bonus > 0 ? BonusTint : bonus < 0 ? MalusTint : null);
            }
        }

        /// <summary>The described item's page in the item info window at layout (its authored x, <see cref="DescriptionTop"/>).</summary>
        private static void DrawDescription(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, long timestamp)
        {
            var pages = menu.DescriptionPages;
            if (pages.Count == 0) return;
            var page = pages[Math.Clamp(menu.DescriptionPage, 0, pages.Count - 1)];
            DrawDescriptionPage(renderer, library, font, logFont, menu, menu.DescribedItem, page, placement, menu.Menu.Frame, timestamp);
        }

        /// <summary>
        /// One description page (<see cref="StockUiItemDescriptionPage"/>) in the item info window sized to its lines, placed at
        /// layout (the info window's authored x, <see cref="DescriptionTop"/>) relative to a window drawn at
        /// <paramref name="placement"/> whose frame is <paramref name="frame"/>: the 32 x 32 icon, the lines in the log font,
        /// and the footer with the page arrow at the bottom right. Shared by the check, Items and Equipment windows.
        /// </summary>
        public static void DrawDescriptionPage(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont,
            StockUiOpenMenu menu, ushort itemId, StockUiItemDescriptionPage page, StockUiPlacement placement, UiMenuFrame frame, long timestamp)
        {
            float s = placement.Scale;
            if (!library.TryGetMenu(StockUiItemDescription.FrameFor(page.LineCount), out var info)) return;
            var at = new StockUiPlacement(placement.X + (info.Frame.X - frame.X) * s, placement.Y + (DescriptionTop - frame.Y) * s, s, false);
            renderer.DrawMenu(info, at, includeButtons: false, opaqueBody: true);
            StockUiMenuWindow.DrawItemIcon(renderer, menu, itemId, at.X + InfoIconX * s, at.Y + InfoIconY * s, InfoIconSize * s);
            for (int i = 0; i < page.Lines.Count; i++)
            {
                DrawLine(renderer, font, logFont, page.Lines[i], at.X + InfoTextX * s, at.Y + (InfoTextY + i * StockUiLogFont.CellHeight) * s, s);
            }
            if (page.Footer.Length == 0 && !page.HasMore) return;
            float footerY = at.Y + (InfoTextY + page.Lines.Count * StockUiLogFont.CellHeight) * s;
            float right = at.X + (info.Frame.Width - InfoRightInset) * s;
            if (page.HasMore && library.TryGetGroup("kaipage", out var arrow) && arrow.Images.Count > 0)
            {
                // The page arrow after the footer: the log's page-wait arrow, stepped like the target cursor.
                int step = (int)(timestamp / (Stopwatch.Frequency * StockUiTargetWindow.CursorStepSeconds));
                int cycle = Math.Max(1, 2 * arrow.Images.Count - 2);
                int index = step % cycle;
                if (index >= arrow.Images.Count) index = cycle - index;
                right -= 12 * s;
                renderer.DrawImage(arrow.Images[index], right + 2 * s, footerY + 3 * s, s);
            }
            float width = logFont != null ? logFont.MeasureWidth(page.Footer) : font.MeasureWidth(page.Footer);
            DrawLine(renderer, font, logFont, page.Footer, right - width * s, footerY, s);
        }

        private static void DrawLine(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, string text, float x, float y, float s)
        {
            if (logFont != null) logFont.Draw(renderer, text, x, y, s, White);
            else renderer.DrawText(font, text, x, y, s);
        }

        /// <summary>
        /// The Items list's options window (<c>itmsortw</c>, "+ : Sort": Auto, Manual, Switch) at its authored place while the
        /// list takes input, as the Treasure Pool's Spoils Options stands beside its list; Manual is drawn greyed.
        /// </summary>
        public static void DrawOptions(StockUiRenderer renderer, UiResourceLibrary library, StockUiPlacement placement)
        {
            if (!library.TryGetMenu(StockUiItems.OptionsMenu, out var options)) return;
            renderer.DrawMenu(options, placement, includeButtons: false, opaqueBody: true);
            float s = placement.Scale;
            foreach (var button in options.Buttons)
            {
                bool greyed = button.ButtonId == StockUiItems.ManualSortButton;
                UiImage? label = null;
                foreach (var shape in button.Shapes)
                {
                    if (shape.Kind == (greyed ? 4 : 0) && library.TryGetImage(shape, out var image)) { label = image; break; }
                }
                if (label != null) renderer.DrawImage(label, placement.X + button.X * s, placement.Y + button.Y * s, s);
            }
        }
    }
}
