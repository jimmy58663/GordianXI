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
    /// Draws the Items window (#351) and the Equipment window (#352) client content over their DAT frames, laid out after
    /// the maintainer's retail captures (2026-10-11, 2560 x 1440, the UI 1:1; measured on 3-10x crops):
    /// <list type="bullet">
    /// <item>the item list (<c>inventor</c>): per row the item's icon in the row's 16 x 16 slot box with the count of a stack
    /// over the icon's bottom right (a small white digit, 2 px in from the box's right edge), the name from 19 px after the
    /// row's origin; worn items' names light green (sampled peak (183, 255, 213)), the row under the cursor pale yellow
    /// (peak (255, 255, 220)); the scrollbar at the right;</item>
    /// <item>the help bar at the top of the screen (<c>titlewin</c> "Items" with the fraction "29/30" after it, <c>helpwind</c>
    /// the help of the window taking input), as the check window's;</item>
    /// <item>the description window: at the bottom left (on Window 1's top edge, or the screen's bottom while the log is
    /// hidden) while the list takes input, right under the list while Use / Drop is open, beside the count box at the
    /// bottom while a count is chosen; under the grid at x 130 in the Equipment window;</item>
    /// <item>the Equipment grid: the worn items' icons over the slots (the slot labels stay under them: weapon icons are
    /// transparent and show "Main" through them), the stats side (<c>persona</c>), the "Current Gil" window under it, the
    /// Attack / Defense values and the eight element marks drawn bright with their values, and the storage's equipment in
    /// the list beside the grid, headed "Storage Options ▶".</item>
    /// </list>
    /// </summary>
    public static class StockUiItemWindow
    {
        /// <summary>The row layout: the 16 x 16 slot box at the row's origin, the name from 19 px in.</summary>
        private const float IconSize = 16, NameInset = 19;

        /// <summary>
        /// The count over the icon: right edge 15 px into the box, the font cell's bottom 17 px down so the digit ends about
        /// 1 px above the icon's bottom (retail capture: the "4" of Wind Crystal ends near the box's bottom; lowered after the
        /// maintainer's round 1, 2026-10-12).
        /// </summary>
        private const float CountRight = 15, CountBottom = 17;

        /// <summary>The description window's top under a list or the grid (layout y 240, the DAT's item info place).</summary>
        public const float DescriptionTop = 240;

        /// <summary>
        /// Gap between the screen's bottom and a window placed at the bottom while the log is hidden (PROVISIONAL: the
        /// capture shows the description close to the bottom; its exact margin depends on how the capture was cropped).
        /// </summary>
        public const float BottomMargin = 16;

        /// <summary>Text inside the description window: log font lines 16 px apart from (48,4), the 32 x 32 icon at (8,12).</summary>
        private const float InfoIconX = 8, InfoIconY = 12, InfoIconSize = 32, InfoTextX = 48, InfoTextY = 4, InfoRightInset = 8;

        /// <summary>
        /// The stats side (<c>persona</c>, retail capture 2026-10-11): the four top lines at y 6, 18, 31, 43 (12.5 px apart) from x 5; HP, MP
        /// "current/max" right-aligned at 104; TP right-aligned at 75; each stat's base right-aligned at 64 and its bonus
        /// from 66 on the baked rows (HP y 58 ... CHR y 174).
        /// </summary>
        private static readonly float[] TopLinesY = { 6, 18, 31, 43 };
        private static readonly float[] StatRowsY = { 58, 70, 82, 96, 109, 122, 135, 148, 161, 174 };
        private const float StatsTextX = 5, VitalsRight = 104, TpRight = 75, BaseRight = 64, BonusX = 66;

        private static readonly UiColor White = new(0x80, 0x80, 0x80, 0x80);
        private static readonly UiColor GreyTint = new(0x48, 0x48, 0x48, 0x80);

        /// <summary>Worn items' names (retail capture: peak (183, 255, 213), light green).</summary>
        private static readonly UiColor EquippedTint = new(0x5C, 0x80, 0x6A, 0x80);

        /// <summary>The row under the cursor (retail capture: peak (255, 255, 220), pale yellow).</summary>
        private static readonly UiColor SelectedRowTint = new(0x80, 0x80, 0x6E, 0x80);

        /// <summary>The item picked to move in Manual sort (orange, as the selected label look; PROVISIONAL).</summary>
        private static readonly UiColor PickedTint = new(0x80, 0x60, 0x2E, 0x80);

        /// <summary>The stats side's main job (peak (58, 161, 214), blue) and bonuses (peak (110, 212, 116), green).</summary>
        private static readonly UiColor MainJobTint = new(0x1D, 0x50, 0x6B, 0x80), BonusTint = new(0x37, 0x6A, 0x3A, 0x80),
            MalusTint = new(0x80, 0x40, 0x40, 0x80);

        /// <summary>Where a window goes at the bottom: on Window 1's top edge, or the screen's bottom (less <see cref="BottomMargin"/>).</summary>
        public static float BottomOf(StockUiScreen screen, float scale) =>
            screen.Window1Top ?? (screen.Height > 0 ? screen.Height - BottomMargin * scale : 0);

        /// <summary>An item list: rows, scrollbar, the help bar, the bin's companion inventory list and the description.</summary>
        public static void DrawList(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, long timestamp, StockUiScreen screen)
        {
            float s = placement.Scale;
            var definition = menu.Menu;
            var frame = definition.Frame;
            float first = menu.FirstRow;
            if (menu.ScrollFrom != menu.FirstRow)
            {
                double t = (timestamp - menu.ScrollStartedAt) / (double)Stopwatch.Frequency / StockUiMenuController.ScrollDuration.TotalSeconds;
                if (t < 1) first = menu.ScrollFrom + (menu.FirstRow - menu.ScrollFrom) * (float)Math.Max(0, t);
            }
            int picked = menu.ManualSortFrom ?? -1;
            DrawRows(renderer, library, font, menu, definition, placement, menu.ItemRows, first, menu.EntryIndex(menu.SelectedButtonId), picked);

            if (menu.ItemListKind == StockUiItemListKind.RecycleBin && library.TryGetMenu(StockUiItems.ListMenu, out var inventor))
            {
                // The inventory beside the bin (retail capture: the two lists side by side, the inventory not taking the cursor).
                var at = new StockUiPlacement(placement.X + (StockUiItems.RecycleInventoryX - frame.X) * s, placement.Y, s, false);
                DrawCompanion(renderer, library, font, menu, inventor, at, menu.CompanionRows);
            }

            if (menu.ItemHelpBar is { } bar) DrawHelpBar(renderer, library, font, logFont, menu, bar, placement, screen);
            if (menu.ItemListKind == StockUiItemListKind.Equipment) DrawDescription(renderer, library, font, logFont, menu, placement, timestamp,
                StockUiItems.EquipDescriptionX, placement.Y + (DescriptionTop - frame.Y) * s);
            else if (menu.ShowsQuantity) DrawDescription(renderer, library, font, logFont, menu, placement, timestamp,
                StockUiItems.EquipDescriptionX, null, screen);
            else if (menu.HasItemActionOver) DrawDescription(renderer, library, font, logFont, menu, placement, timestamp,
                frame.X, placement.Y + (frame.Height + 2) * s, screen, aboveLog: true);
            else DrawDescription(renderer, library, font, logFont, menu, placement, timestamp, frame.X, null, screen);
        }

        /// <summary>Draws item rows over a list frame's row buttons (clipped to the rows, sliding while the list scrolls).</summary>
        private static void DrawRows(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiOpenMenu menu, UiMenuDefinition definition,
            StockUiPlacement placement, IReadOnlyList<StockUiItemRow> rows, float first, int selected, int picked)
        {
            if (definition.FindButton(1) is not { } firstRow || rows.Count == 0) return;
            float s = placement.Scale;
            var frame = definition.Frame;
            var secondRow = definition.FindButton(2);
            float pitch = secondRow != null && secondRow.Y > firstRow.Y ? secondRow.Y - firstRow.Y : firstRow.Height;
            int visible = 0;
            foreach (var b in definition.Buttons) if (StockUiMenuController.IsSelectable(b)) visible++;
            float areaX = placement.X + firstRow.X * s, areaY = placement.Y + firstRow.Y * s;
            renderer.SetClip(placement.X, areaY, frame.Width * s, visible * pitch * s);
            float ts = s * StockUiPartyWindow.TextScale;
            int from = Math.Max(0, (int)Math.Floor(first) - 1), to = Math.Min(rows.Count - 1, (int)Math.Ceiling(first) + visible);
            for (int i = from; i <= to; i++)
            {
                var row = rows[i];
                float ry = areaY + (i - first) * pitch * s;
                float textY = ry + (firstRow.Height * s - font.LineHeight * s) * 0.5f;
                if (row.IsStorageOptions)
                {
                    DrawStorageOptions(renderer, library, font, areaX, ry, firstRow.Height, s);
                    continue;
                }
                if (row.ItemId != 0) StockUiMenuWindow.DrawItemIcon(renderer, menu, row.ItemId, areaX, ry, IconSize * s);
                if (row.ShowsCount)
                {
                    string count = row.Count.ToString(CultureInfo.InvariantCulture);
                    renderer.DrawText(font, count, areaX + CountRight * s - font.MeasureWidth(count) * ts, ry + CountBottom * s - font.LineHeight * ts, ts);
                }
                UiColor? tint = i == picked ? PickedTint : i == selected ? SelectedRowTint : row.Greyed ? GreyTint : row.Equipped ? EquippedTint : null;
                renderer.DrawText(font, row.Name, areaX + NameInset * s, textY, s, tint);
            }
            renderer.ClearClip();
            if (rows.Count > visible) StockUiMenuWindow.DrawScrollbar(renderer, placement, frame, first, rows.Count, visible);
        }

        /// <summary>
        /// The Equipment list's first row (retail capture 2026-10-11): "Storage Options" in the menus' bold italic (the title
        /// font) from 37 px into the row, then the right page arrow (comwin #32).
        /// </summary>
        private static void DrawStorageOptions(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, float x, float y, float rowHeight, float s)
        {
            const float TextX = 34, ArrowGap = 3;
            var titles = StockUiTitleText.For(library);
            float width = titles.Measure(StockUiItems.StorageOptionsText, font);
            float textY = y + (rowHeight - 11) * 0.5f * s;
            titles.Draw(renderer, StockUiItems.StorageOptionsText, x + TextX * s, textY, s, font);
            if (library.TryGetGroup("comwin", out var comwin) && comwin.Images.Count > 32)
            {
                renderer.DrawImage(comwin.Images[32], x + (TextX + width + ArrowGap) * s, y + (rowHeight - 9) * 0.5f * s, s);
            }
        }

        /// <summary>A list drawn beside another window without the cursor: its frame, slot boxes and rows from the first.</summary>
        private static void DrawCompanion(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiOpenMenu menu, UiMenuDefinition inventor,
            StockUiPlacement at, IReadOnlyList<StockUiItemRow> rows)
        {
            renderer.DrawMenu(inventor, at, includeButtons: true, opaqueBody: true);
            DrawRows(renderer, library, font, menu, inventor, at, rows, 0, -1, -1);
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

        /// <summary>
        /// The Equipment grid: the worn items' icons, the stats side and gil at its left, Attack / Defense and the resistances,
        /// the storage's equipment beside it (until Confirm on a slot opens the slot's list there), the help bar and the slot's
        /// item description under the grid.
        /// </summary>
        public static void DrawEquipment(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, long timestamp, StockUiScreen screen)
        {
            float s = placement.Scale;
            var definition = menu.Menu;
            var frame = definition.Frame;
            foreach (var button in definition.Buttons)
            {
                ushort item = menu.EquippedItemIn(button.ButtonId);
                if (item != 0) StockUiMenuWindow.DrawItemIcon(renderer, menu, item, placement.X + button.X * s, placement.Y + button.Y * s, 32 * s);
            }
            DrawStats(renderer, library, font, menu, placement);
            DrawCombatStats(renderer, font, menu, placement);
            if (library.TryGetMenu(StockUiItems.GilMenu, out var gil) && menu.Inventory is { } inventory)
            {
                // "Current Gil" under the stats side (retail capture: 16,240 as the DAT authors it), the amount right-aligned.
                var at = new StockUiPlacement(placement.X + (gil.Frame.X - frame.X) * s, placement.Y + (gil.Frame.Y - frame.Y) * s, s, false);
                renderer.DrawMenu(gil, at, includeButtons: false, opaqueBody: true);
                string amount = StockUiShop.FormatPrice(inventory.Gil);
                renderer.DrawText(font, amount, at.X + (gil.Frame.Width - 8) * s - font.MeasureWidth(amount) * s, at.Y + 30 * s, s);
            }
            if (!menu.IsListOpen && library.TryGetMenu(StockUiItems.ListMenu, out var inventor))
            {
                var at = new StockUiPlacement(placement.X + (StockUiItems.EquipListX - frame.X) * s, placement.Y + (inventor.Frame.Y - frame.Y) * s, s, false);
                DrawCompanion(renderer, library, font, menu, inventor, at, menu.CompanionRows);
            }
            if (menu.ItemHelpBar is { } bar) DrawHelpBar(renderer, library, font, logFont, menu, bar, placement, screen);
            if (!menu.IsListOpen) DrawDescription(renderer, library, font, logFont, menu, placement, timestamp, StockUiItems.EquipDescriptionX,
                placement.Y + (DescriptionTop - frame.Y) * s);
        }

        /// <summary>The stats side (<c>persona</c>, authored at 16,48 left of the grid), as the retail capture lays it out.</summary>
        private static void DrawStats(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiOpenMenu menu, StockUiPlacement placement)
        {
            if (!library.TryGetMenu(StockUiItems.StatsMenu, out var persona)) return;
            float s = placement.Scale;
            var frame = menu.Menu.Frame;
            var at = new StockUiPlacement(placement.X + (persona.Frame.X - frame.X) * s, placement.Y + (persona.Frame.Y - frame.Y) * s, s, false);
            renderer.DrawMenu(persona, at, includeButtons: false, opaqueBody: true);
            if (menu.CharacterStats?.Invoke() is not { } stats) return;
            float ts = s * StockUiPartyWindow.TextScale;
            void Left(int line, string text, UiColor? tint = null) =>
                renderer.DrawText(font, text, at.X + StatsTextX * s, at.Y + TopLinesY[line] * s, ts, tint);
            void Right(float right, float y, string text, UiColor? tint = null) =>
                renderer.DrawText(font, text, at.X + right * s - font.MeasureWidth(text) * ts, at.Y + y * s, ts, tint);
            Left(0, stats.Name);
            Left(1, stats.MainJob, MainJobTint);
            Left(2, stats.SubJob);
            Left(3, stats.ItemLevelLine);
            var inv = CultureInfo.InvariantCulture;
            Right(VitalsRight, StatRowsY[0], string.Create(inv, $"{stats.Hp}/{stats.MaxHp}"));
            Right(VitalsRight, StatRowsY[1], string.Create(inv, $"{stats.Mp}/{stats.MaxMp}"));
            Right(TpRight, StatRowsY[2], stats.Tp.ToString(inv));
            for (int i = 0; i < 7 && i < stats.BaseStats.Count; i++)
            {
                float y = StatRowsY[3 + i];
                Right(BaseRight, y, stats.BaseStats[i].ToString(inv));
                int bonus = i < stats.StatBonuses.Count ? stats.StatBonuses[i] : 0;
                if (bonus != 0)
                {
                    renderer.DrawText(font, string.Create(inv, $"{(bonus > 0 ? "+" : "")}{bonus}"), at.X + BonusX * s, at.Y + y * s, ts, bonus > 0 ? BonusTint : MalusTint);
                }
            }
        }

        /// <summary>
        /// The grid frame (<c>frames</c> #13) bakes in "Attack" (4..47, y 148) and "Defense" (98..142) under the slots and two
        /// rows of four faint element marks (<c>colorbal</c>, 10 x 10) at x 6, 50, 94, 138 and y 162 / 176, coloured (read from
        /// the texture) Fire, Wind, Thunder, Light over Ice, Earth, Water, Dark. Retail (capture 2026-10-11) draws the marks
        /// bright and the values right after them: Attack from x 46, Defense from 142, each resistance from the mark's x + 12.
        /// </summary>
        private static readonly (float X, float Y, int Element, float SourceX, float SourceY)[] ElementMarks =
        {
            (6, 162, 0, 32, 1), (50, 162, 2, 16, 1), (94, 162, 4, 48, 16), (138, 162, 6, 0, 16),
            (6, 176, 1, 48, 1), (50, 176, 3, 16, 16), (94, 176, 5, 0, 1), (138, 176, 7, 32, 16),
        };

        private const float AttackX = 46, DefenseX = 142, CombatY = 150, ResistInset = 12;

        private static void DrawCombatStats(StockUiRenderer renderer, UiFont font, StockUiOpenMenu menu, StockUiPlacement placement)
        {
            float s = placement.Scale;
            float ts = s * StockUiPartyWindow.TextScale;
            foreach (var (x, y, _, sx, sy) in ElementMarks)
            {
                renderer.DrawTextureRect("colorbal", sx, sy, 14, 14, placement.X + x * s, placement.Y + y * s, 10 * s, 10 * s, White);
            }
            if (menu.CharacterStats?.Invoke() is not { } stats) return;
            var inv = CultureInfo.InvariantCulture;
            renderer.DrawText(font, stats.Attack.ToString(inv), placement.X + AttackX * s, placement.Y + CombatY * s, ts);
            renderer.DrawText(font, stats.Defense.ToString(inv), placement.X + DefenseX * s, placement.Y + CombatY * s, ts);
            if (stats.Resistances is not { } resistances) return;
            foreach (var (x, y, element, _, _) in ElementMarks)
            {
                if (element >= resistances.Count) continue;
                renderer.DrawText(font, resistances[element].ToString(inv), placement.X + (x + ResistInset) * s, placement.Y + y * s, ts);
            }
        }

        /// <summary>
        /// The help bar (retail captures 2026-10-11): the title in <c>titlewin</c> (16,16) in the log font with the fraction after
        /// it, the numerator right-aligned at x 91 six px down, its bottom just over the "/" (lowered from 3 after the maintainer's
        /// round 2, 2026-10-12, so the gaps between the "/" and each number match), and "/" plus the denominator from x 88 at y 12, both in
        /// the small font; the help line in <c>helpwind</c> (130,16) stretched toward the screen's right edge.
        /// </summary>
        private static void DrawHelpBar(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiItemHelpBar bar, StockUiPlacement placement, StockUiScreen screen)
        {
            const float BarTextX = 8, HelpRightMargin = 16, NumeratorRight = 91, NumeratorY = 6, DenominatorX = 88, DenominatorY = 12;
            float s = placement.Scale;
            var frame = menu.Menu.Frame;
            if (library.TryGetMenu(StockUiCheck.TitleMenu, out var title))
            {
                var at = new StockUiPlacement(placement.X + (title.Frame.X - frame.X) * s, placement.Y + (title.Frame.Y - frame.Y) * s, s, false);
                renderer.DrawMenu(title, at, includeButtons: false, opaqueBody: true);
                DrawLine(renderer, font, logFont, bar.Title, at.X + BarTextX * s, at.Y + (title.Frame.Height - StockUiLogFont.CellHeight) * 0.5f * s, s);
                if (bar.Numerator is { } numerator && bar.Denominator is { } denominator)
                {
                    float ts = s * StockUiPartyWindow.TextScale;
                    string top = numerator.ToString(CultureInfo.InvariantCulture);
                    renderer.DrawText(font, top, at.X + NumeratorRight * s - font.MeasureWidth(top) * ts, at.Y + NumeratorY * s, ts);
                    renderer.DrawText(font, "/" + denominator.ToString(CultureInfo.InvariantCulture), at.X + DenominatorX * s, at.Y + DenominatorY * s, ts);
                }
            }
            if (library.TryGetMenu(StockUiCheck.HelpMenu, out var help))
            {
                var at = new StockUiPlacement(placement.X + (help.Frame.X - frame.X) * s, placement.Y + (help.Frame.Y - frame.Y) * s, s, false);
                float? width = screen.Width > 0 ? Math.Max(help.Frame.Width, (screen.Width - at.X) / s - HelpRightMargin) : null;
                renderer.DrawMenu(help, at, includeButtons: false, opaqueBody: true, frameWidth: width);
                string? text = bar.HelpTextId >= 0 ? menu.ItemHelpText(bar.HelpTextId) : null;
                if (!string.IsNullOrEmpty(text))
                {
                    DrawLine(renderer, font, logFont, text.Replace("\n", " "), at.X + BarTextX * s, at.Y + (help.Frame.Height - StockUiLogFont.CellHeight) * 0.5f * s, s);
                }
            }
        }

        /// <summary>
        /// The described item's page in the item info window sized to its lines at layout x <paramref name="layoutX"/>: at
        /// <paramref name="top"/> (screen px) when given, else with its bottom at <see cref="BottomOf"/>.
        /// </summary>
        private static void DrawDescription(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, long timestamp, float layoutX, float? top, StockUiScreen screen = default, bool aboveLog = false)
        {
            var pages = menu.DescriptionPages;
            if (pages.Count == 0) return;
            var page = pages[Math.Clamp(menu.DescriptionPage, 0, pages.Count - 1)];
            if (!library.TryGetMenu(StockUiItemDescription.FrameFor(page.LineCount), out var info)) return;
            float s = placement.Scale;
            var frame = menu.Menu.Frame;
            float x = placement.X + (layoutX - frame.X) * s;
            float y = top ?? (screen.Height > 0 || screen.Window1Top != null
                ? BottomOf(screen, s) - info.Frame.Height * s
                : placement.Y + (DescriptionTop - frame.Y) * s);
            // Anything at the bottom left sits on Window 1 and rises with it (the maintainer's notes, 2026-10-12).
            if (aboveLog && screen.Window1Top is { } logTop) y = Math.Min(y, logTop - info.Frame.Height * s);
            DrawDescriptionPage(renderer, library, font, logFont, menu, menu.DescribedItem, page, new StockUiPlacement(x, Math.Max(0, y), s, false), info, timestamp);
        }

        /// <summary>
        /// One description page (<see cref="StockUiItemDescriptionPage"/>) in the item info window sized to its lines, placed at
        /// layout (the info window's authored x, <see cref="DescriptionTop"/>) relative to a window drawn at
        /// <paramref name="placement"/> whose frame is <paramref name="frame"/>. Shared with the check window.
        /// </summary>
        public static void DrawDescriptionPage(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont,
            StockUiOpenMenu menu, ushort itemId, StockUiItemDescriptionPage page, StockUiPlacement placement, UiMenuFrame frame, long timestamp)
        {
            float s = placement.Scale;
            if (!library.TryGetMenu(StockUiItemDescription.FrameFor(page.LineCount), out var info)) return;
            var at = new StockUiPlacement(placement.X + (info.Frame.X - frame.X) * s, placement.Y + (DescriptionTop - frame.Y) * s, s, false);
            DrawDescriptionPage(renderer, library, font, logFont, menu, itemId, page, at, info, timestamp);
        }

        /// <summary>One description page in the given item info window at a screen place: the icon, the lines, the footer and page arrow.</summary>
        private static void DrawDescriptionPage(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont,
            StockUiOpenMenu menu, ushort itemId, StockUiItemDescriptionPage page, StockUiPlacement at, UiMenuDefinition info, long timestamp)
        {
            float s = at.Scale;
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
        /// The Items list's options window (<c>itmsort2</c>, "+ : Sort": Auto, Manual, Recycle Bin) at its authored place
        /// while the list takes input (retail capture 2026-10-11).
        /// </summary>
        public static void DrawOptions(StockUiRenderer renderer, UiResourceLibrary library, StockUiPlacement placement)
        {
            if (!library.TryGetMenu(StockUiItems.OptionsMenu, out var options)) return;
            renderer.DrawMenu(options, placement, includeButtons: true, opaqueBody: true);
        }
    }
}
