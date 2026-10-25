// src/Gordian.App/Graphics/StockUiBlueMagicWindow.cs
using System;
using System.Globalization;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws Blue Magic's Set Spells windows (#363) on the retail DAT frames (<see cref="StockUiBlueMagic"/>): the twenty slots
    /// in <c>bluequip</c> (the set number coloured by the level the slot opens at, the spell's icon and name in the log font, as
    /// the spell lists draw theirs), the learned spells in <c>bluinven</c> beside it (the points right-aligned), the points in
    /// <c>bluepoin</c> under the baked "Total Points/Max" and the spell under the cursor in <c>bluehelp</c>. PROVISIONAL (the
    /// capture image-38 is not at hand here): the number colours, the columns and the details' lines; the status column the
    /// capture shows left of the slots is not drawn.
    /// </summary>
    internal static class StockUiBlueMagicWindow
    {
        private const float NumberX = 4, IconX = 24, NameX = 43, PointsRightInset = 10, ValueRightInset = 8, ValueY = 30;
        private const float DetailX = 8, DetailNameY = 4, DetailHelpY = 20, DetailStatsY = 36;

        /// <summary>The set numbers' colours by the level their slot opens at (half-scale RGBA, 0x80 = 1.0).</summary>
        private static UiColor NumberColor(int unlockLevel) => unlockLevel switch
        {
            <= 1 => new UiColor(0x80, 0x80, 0x80, 0x80),
            <= 11 => new UiColor(0x60, 0x78, 0x80, 0x80),
            <= 21 => new UiColor(0x60, 0x80, 0x60, 0x80),
            <= 31 => new UiColor(0x80, 0x80, 0x50, 0x80),
            <= 41 => new UiColor(0x80, 0x68, 0x40, 0x80),
            <= 51 => new UiColor(0x80, 0x50, 0x50, 0x80),
            <= 61 => new UiColor(0x70, 0x58, 0x80, 0x80),
            _ => new UiColor(0x80, 0x60, 0x78, 0x80),
        };

        public static void Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, long timestamp)
        {
            if (menu.BlueMagicView == StockUiBlueMagicView.Slots)
            {
                DrawSlots(renderer, library, font, logFont, menu, placement, timestamp);
                if (!menu.BlueLearnedOpen && library.TryGetMenu(StockUiBlueMagic.LearnedMenu, out var learned))
                {
                    var at = StockUiWindowPlacement.Beside(placement, menu.Menu.Frame, learned.Frame);
                    renderer.DrawMenu(learned, at, includeButtons: false, opaqueBody: true);
                    DrawLearned(renderer, library, font, logFont, learned, menu.BlueSpells, at, 0, -1);
                }
                if (library.TryGetMenu(StockUiBlueMagic.PointsMenu, out var points))
                {
                    DrawPoints(renderer, font, menu, points, StockUiWindowPlacement.Beside(placement, menu.Menu.Frame, points.Frame));
                }
                if (!menu.BlueLearnedOpen) DrawDetail(renderer, library, font, logFont, menu, placement);
            }
            else if (menu.BlueMagicView == StockUiBlueMagicView.Learned)
            {
                DrawLearned(renderer, library, font, logFont, menu.Menu, menu.BlueSpells, placement,
                    StockUiMenuWindow.ScrolledFirstRow(menu, timestamp), menu.EntryIndex(menu.SelectedButtonId));
                if (menu.CanScroll) StockUiMenuWindow.DrawScrollbar(renderer, placement, menu.Menu.Frame, StockUiMenuWindow.ScrolledFirstRow(menu, timestamp),
                    menu.Rows.Count, menu.VisibleRows);
                DrawDetail(renderer, library, font, logFont, menu, placement);
            }
        }

        private static void DrawSlots(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, long timestamp)
        {
            float s = placement.Scale;
            var definition = menu.Menu;
            if (definition.FindButton(1) is not { } firstRow) return;
            float pitch = StockUiMenuWindow.RowPitch(firstRow, definition.FindButton(2));
            float first = StockUiMenuWindow.ScrolledFirstRow(menu, timestamp);
            float areaX = placement.X + firstRow.X * s, areaY = placement.Y + firstRow.Y * s;
            library.TryGetGroup(StockUiActionMenu.IconGroup, out var icons);
            renderer.SetClip(placement.X, areaY, definition.Frame.Width * s, menu.VisibleRows * pitch * s);
            int selected = menu.EntryIndex(menu.SelectedButtonId);
            int from = Math.Max(0, (int)Math.Floor(first) - 1), to = Math.Min(menu.BlueSlots.Count - 1, (int)Math.Ceiling(first) + menu.VisibleRows);
            for (int i = from; i <= to; i++)
            {
                var slot = menu.BlueSlots[i];
                float ry = areaY + (i - first) * pitch * s;
                UiColor number = slot.Locked ? StockUiMenuWindow.ActionGreyedTint : NumberColor(slot.UnlockLevel);
                Line(renderer, font, logFont, slot.Slot.ToString("00", CultureInfo.InvariantCulture), areaX + NumberX * s, ry, s, number);
                if (slot.SpellId == 0) continue;
                if (icons != null && slot.Icon >= 0 && slot.Icon < icons.Images.Count) StockUiMenuWindow.DrawIcon(renderer, icons.Images[slot.Icon], areaX + IconX * s, ry, 16 * s);
                Line(renderer, font, logFont, slot.Name, areaX + NameX * s, ry, s, i == selected ? StockUiMenuWindow.ActionSelectedTint : StockUiMenuWindow.White);
            }
            renderer.ClearClip();
            if (menu.CanScroll) StockUiMenuWindow.DrawScrollbar(renderer, placement, definition.Frame, first, menu.Rows.Count, menu.VisibleRows);
        }

        /// <summary>The learned list's rows over a <c>bluinven</c> frame: the icon, the name, the points right-aligned.</summary>
        private static void DrawLearned(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, UiMenuDefinition definition,
            System.Collections.Generic.IReadOnlyList<StockUiBlueSpellRow> rows, StockUiPlacement placement, float first, int selected)
        {
            float s = placement.Scale;
            if (definition.FindButton(1) is not { } firstRow) return;
            float pitch = StockUiMenuWindow.RowPitch(firstRow, definition.FindButton(2));
            int visible = 0;
            foreach (var b in definition.Buttons) if (StockUiMenuController.IsSelectable(b)) visible++;
            float areaX = placement.X + firstRow.X * s, areaY = placement.Y + firstRow.Y * s;
            library.TryGetGroup(StockUiActionMenu.IconGroup, out var icons);
            renderer.SetClip(placement.X, areaY, definition.Frame.Width * s, visible * pitch * s);
            int from = Math.Max(0, (int)Math.Floor(first) - 1), to = Math.Min(rows.Count - 1, (int)Math.Ceiling(first) + visible);
            float right = placement.X + (definition.Frame.Width - PointsRightInset) * s;
            for (int i = from; i <= to; i++)
            {
                var row = rows[i];
                float ry = areaY + (i - first) * pitch * s;
                UiColor tint = i == selected ? StockUiMenuWindow.ActionSelectedTint
                    : row.IsSet || row.GreyedReason != null ? StockUiMenuWindow.ActionGreyedTint : StockUiMenuWindow.White;
                if (icons != null && row.Icon >= 0 && row.Icon < icons.Images.Count) StockUiMenuWindow.DrawIcon(renderer, icons.Images[row.Icon], areaX, ry, 16 * s);
                Line(renderer, font, logFont, row.Name, areaX + (row.RemoveAll ? 0 : 19) * s, ry, s, tint);
                if (row.RemoveAll) continue;
                string points = row.Points.ToString(CultureInfo.InvariantCulture);
                Line(renderer, font, logFont, points, right - StockUiTextBoxes.MeasureLine(font, logFont, points) * s, ry, s, tint);
            }
            renderer.ClearClip();
        }

        /// <summary>"used/max" right-aligned under the frame's "Total Points/Max".</summary>
        private static void DrawPoints(StockUiRenderer renderer, UiFont font, StockUiOpenMenu menu, UiMenuDefinition points, StockUiPlacement at)
        {
            float s = at.Scale;
            renderer.DrawMenu(points, at, includeButtons: false, opaqueBody: true);
            string text = $"{menu.BluePoints.Used}/{menu.BluePoints.Max}";
            renderer.DrawText(font, text, at.X + (points.Frame.Width - ValueRightInset) * s - font.MeasureWidth(text) * s, at.Y + ValueY * s, s);
        }

        /// <summary>The spell under the cursor in <c>bluehelp</c>: its name, its help line, its level, MP cost and Blue Magic Points.</summary>
        private static void DrawDetail(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement)
        {
            if (!library.TryGetMenu(StockUiBlueMagic.DetailMenu, out var detail)) return;
            var at = StockUiWindowPlacement.Beside(placement, menu.Menu.Frame, detail.Frame);
            float s = at.Scale;
            renderer.DrawMenu(detail, at, includeButtons: false, opaqueBody: true);
            ushort id = menu.BlueDescribedSpell;
            if (id == 0) return;
            StockUiBlueSpellRow? found = null;
            foreach (var row in menu.BlueSpells)
            {
                if (row.SpellId == id) found = row;
            }
            if (found is not { } spell) return;
            Line(renderer, font, logFont, spell.Name, at.X + DetailX * s, at.Y + DetailNameY * s, s, StockUiMenuWindow.White);
            if (spell.Help is { Length: > 0 } help)
            {
                var lines = StockUiTextLayout.Wrap(help, t => StockUiTextBoxes.MeasureLine(font, logFont, t), detail.Frame.Width - 2 * DetailX, 1);
                if (lines.Count > 0) Line(renderer, font, logFont, lines[0], at.X + DetailX * s, at.Y + DetailHelpY * s, s, StockUiMenuWindow.White);
            }
            string stats = $"Lv.{spell.Level}   MP Cost {spell.MpCost}   Blue Magic Points {spell.Points}";
            Line(renderer, font, logFont, stats, at.X + DetailX * s, at.Y + DetailStatsY * s, s, StockUiMenuWindow.White);
        }

        private static void Line(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, string text, float x, float y, float s, UiColor color)
        {
            if (logFont != null) logFont.Draw(renderer, text, x, y + s, s, color);
            else renderer.DrawText(font, text, x, y, s, color);
        }

        /// <summary>The help bar over the Set Spells windows: "Blue Magic" and the frame's help (643 / 637), or null for other windows.</summary>
        public static (string Title, string? Help)? HelpBar(StockUiOpenMenu top, Func<int, string?>? helpText)
        {
            if (!top.IsBlueMagicWindow) return null;
            int help = top.Menu.Frame.HelpTextId >= 0 ? top.Menu.Frame.HelpTextId
                : top.BlueMagicView == StockUiBlueMagicView.Slots ? StockUiBlueMagic.HelpSetSpells : StockUiBlueMagic.HelpLearned;
            return (StockUiBlueMagic.Title, helpText?.Invoke(help));
        }
    }
}
