// src/Gordian.App/Graphics/StockUiBlueMagicWindow.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws Blue Magic's Set Spells windows (#363) on the retail DAT frames (<see cref="StockUiBlueMagic"/>), as the
    /// maintainer's retail captures lay them out (image-20 to image-22 of the 2026-10-24 round 1, at 1:1): the status column
    /// (the Equipment window's <c>persona</c>, 16,48), the twenty slots in <c>bluequip</c> (the set number in the small text,
    /// the spell's icon and name in the log font), the learned spells in <c>bluinven</c> beside it ("Type ▶" on the first
    /// row, "(Remove All)" in yellow, set spells green), the points in <c>bluepoin</c> under the baked "Total Points/Max" and
    /// the spell under the cursor in <c>bluehelp</c>, grown to its lines: the name with "(family - type)" at the right, the help
    /// wrapped, the stat bonus, the job trait and its points, then level, MP cost and Blue Magic Points.
    /// </summary>
    internal static class StockUiBlueMagicWindow
    {
        private const float NumberX = 4, IconX = 24, NameX = 43, ValueRightInset = 8, ValueY = 30;

        /// <summary>The learned list: names from x 18 (icons from 0), "Type ▶" right-aligned 22 px in (image-20).</summary>
        private const float LearnedNameX = 18, TypeRightInset = 22;

        /// <summary>The details: the name at (3, 4), the other lines from x 15, a line every 14 px, the family 20 px in from the right.</summary>
        private const float DetailX = 3, DetailIndentX = 15, DetailTopY = 4, DetailPitch = 14, DetailRightInset = 20, DetailBottomPad = 6;

        /// <summary>Retail's tones (half-scale RGBA, sampled from image-20): "(Remove All)" yellow, a set spell green.</summary>
        private static readonly UiColor RemoveAllTint = new(0x80, 0x80, 0x58, 0x80), SetTint = new(0x58, 0x80, 0x64, 0x80);

        public static void Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, long timestamp)
        {
            if (menu.BlueMagicView == StockUiBlueMagicView.Slots)
            {
                StockUiItemWindow.DrawStats(renderer, library, font, menu, placement);
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
            float ts = s * StockUiPartyWindow.TextScale;
            library.TryGetGroup(StockUiActionMenu.IconGroup, out var icons);
            renderer.SetClip(placement.X, areaY, definition.Frame.Width * s, menu.VisibleRows * pitch * s);
            int selected = menu.EntryIndex(menu.SelectedButtonId);
            int from = Math.Max(0, (int)Math.Floor(first) - 1), to = Math.Min(menu.BlueSlots.Count - 1, (int)Math.Ceiling(first) + menu.VisibleRows);
            for (int i = from; i <= to; i++)
            {
                var slot = menu.BlueSlots[i];
                float ry = areaY + (i - first) * pitch * s;
                // The set numbers are all alike in retail (image-20); a slot the level does not open yet is greyed.
                UiColor number = slot.Locked ? StockUiMenuWindow.ActionGreyedTint : StockUiMenuWindow.White;
                renderer.DrawText(font, slot.Slot.ToString("00", CultureInfo.InvariantCulture), areaX + NumberX * s,
                    ry + (firstRow.Height * s - font.LineHeight * ts) * 0.5f, ts, number);
                if (slot.SpellId == 0) continue;
                if (icons != null && slot.Icon >= 0 && slot.Icon < icons.Images.Count) StockUiMenuWindow.DrawIcon(renderer, icons.Images[slot.Icon], areaX + IconX * s, ry, 16 * s);
                Line(renderer, font, logFont, slot.Name, areaX + NameX * s, ry, s, i == selected ? StockUiMenuWindow.ActionSelectedTint : StockUiMenuWindow.White);
            }
            renderer.ClearClip();
            if (menu.CanScroll) StockUiMenuWindow.DrawScrollbar(renderer, placement, definition.Frame, first, menu.Rows.Count, menu.VisibleRows);
        }

        /// <summary>The learned list's rows over a <c>bluinven</c> frame: "Type ▶", "(Remove All)", then each spell's icon and name.</summary>
        private static void DrawLearned(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, UiMenuDefinition definition,
            IReadOnlyList<StockUiBlueSpellRow> rows, StockUiPlacement placement, float first, int selected)
        {
            float s = placement.Scale;
            if (definition.FindButton(1) is not { } firstRow) return;
            float pitch = StockUiMenuWindow.RowPitch(firstRow, definition.FindButton(2));
            int visible = 0;
            foreach (var b in definition.Buttons) if (StockUiMenuController.IsSelectable(b)) visible++;
            float areaX = placement.X + firstRow.X * s, areaY = placement.Y + firstRow.Y * s;
            float ts = s * StockUiPartyWindow.TextScale;
            library.TryGetGroup(StockUiActionMenu.IconGroup, out var icons);
            renderer.SetClip(placement.X, areaY, definition.Frame.Width * s, visible * pitch * s);
            int from = Math.Max(0, (int)Math.Floor(first) - 1), to = Math.Min(rows.Count - 1, (int)Math.Ceiling(first) + visible);
            for (int i = from; i <= to; i++)
            {
                var row = rows[i];
                float ry = areaY + (i - first) * pitch * s;
                if (row.TypeHeader)
                {
                    // "Type ▶" right-aligned in the small text (image-20); the menu font has no ▶, so ">" stands in.
                    string type = row.Name + " >";
                    renderer.DrawText(font, type, placement.X + (definition.Frame.Width - TypeRightInset) * s - font.MeasureWidth(type) * ts,
                        ry + (firstRow.Height * s - font.LineHeight * ts) * 0.5f, ts, i == selected ? StockUiMenuWindow.ActionSelectedTint : StockUiMenuWindow.White);
                    continue;
                }
                UiColor tint = i == selected ? StockUiMenuWindow.ActionSelectedTint
                    : row.RemoveAll ? RemoveAllTint
                    : row.IsSet ? SetTint
                    : row.GreyedReason != null ? StockUiMenuWindow.ActionGreyedTint : StockUiMenuWindow.White;
                if (icons != null && row.Icon >= 0 && row.Icon < icons.Images.Count) StockUiMenuWindow.DrawIcon(renderer, icons.Images[row.Icon], areaX, ry, 16 * s);
                Line(renderer, font, logFont, row.Name, areaX + LearnedNameX * s, ry, s, tint);
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

        /// <summary>The lines the details window shows for a spell (image-20), its name and "(family - type)" first.</summary>
        internal static List<string> DetailLines(StockUiBlueSpellRow spell, Func<string, float> measure, float wrapWidth)
        {
            var lines = new List<string>();
            if (spell.Help is { Length: > 0 } help) lines.AddRange(StockUiTextLayout.Wrap(help, measure, wrapWidth));
            var fact = StockUiBlueSpellFacts.Of(spell.SpellId);
            if (fact?.Bonus is { } bonus) lines.Add(bonus);
            if (fact?.Trait is { } trait) lines.Add(string.Create(CultureInfo.InvariantCulture, $"Job Trait: {trait} ({fact.Value.TraitPoints})"));
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"Lv.{spell.Level}   MP cost {spell.MpCost}   Blue Magic Points {spell.Points}"));
            return lines;
        }

        /// <summary>"(Aquans - Magical)" after the name, or null when the family is not known.</summary>
        internal static string? FamilyText(ushort spellId) =>
            StockUiBlueSpellFacts.Of(spellId) is { } fact && StockUiBlueSpellFacts.FamilyName(fact.Family) is { } family
                ? fact.Type is { } type ? $"({family} - {type})" : $"({family})"
                : null;

        /// <summary>The spell under the cursor in <c>bluehelp</c>, the window grown down to fit its lines (round 1, N13).</summary>
        private static void DrawDetail(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement)
        {
            if (!library.TryGetMenu(StockUiBlueMagic.DetailMenu, out var detail)) return;
            var at = StockUiWindowPlacement.Beside(placement, menu.Menu.Frame, detail.Frame);
            float s = at.Scale;
            ushort id = menu.BlueDescribedSpell;
            StockUiBlueSpellRow? found = null;
            if (id != 0)
            {
                foreach (var row in menu.BlueAllSpells)
                {
                    if (row.SpellId == id) found = row;
                }
            }
            if (found is not { } spell)
            {
                renderer.DrawMenu(detail, at, includeButtons: false, opaqueBody: true);
                return;
            }
            float width = detail.Frame.Width;
            var lines = DetailLines(spell, t => StockUiTextBoxes.MeasureLine(font, logFont, t), width - DetailIndentX - DetailX * 2);
            float height = Math.Max(detail.Frame.Height, DetailTopY + (lines.Count + 1) * DetailPitch + DetailBottomPad);
            renderer.DrawMenu(detail, at, includeButtons: false, opaqueBody: true, frameHeight: height);
            float y = at.Y + DetailTopY * s;
            Line(renderer, font, logFont, spell.Name, at.X + DetailX * s, y, s, StockUiMenuWindow.White);
            if (FamilyText(spell.SpellId) is { } family)
            {
                Line(renderer, font, logFont, family, at.X + (width - DetailRightInset) * s - StockUiTextBoxes.MeasureLine(font, logFont, family) * s, y, s,
                    StockUiMenuWindow.White);
            }
            foreach (string line in lines)
            {
                y += DetailPitch * s;
                Line(renderer, font, logFont, line, at.X + DetailIndentX * s, y, s, StockUiMenuWindow.White);
            }
        }

        private static void Line(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, string text, float x, float y, float s, UiColor color)
        {
            if (logFont != null) logFont.Draw(renderer, text, x, y + s, s, color);
            else renderer.DrawText(font, text, x, y, s, color);
        }

        /// <summary>
        /// The help bar over the Set Spells windows: "Blue Magic" and the window's help (643 / 637; 650 on "Type ▶", image-22),
        /// "Refine by Type" and 650 over the family window (image-21); null for other windows.
        /// </summary>
        public static (string Title, string? Help)? HelpBar(StockUiOpenMenu top, Func<int, string?>? helpText)
        {
            if (top.Name.Equals(StockUiBlueMagic.TypeMenu, StringComparison.OrdinalIgnoreCase))
            {
                return (StockUiBlueMagic.TypeTitle, helpText?.Invoke(StockUiBlueMagic.HelpType));
            }
            if (!top.IsBlueMagicWindow) return null;
            int index = top.EntryIndex(top.SelectedButtonId);
            bool onType = top.BlueMagicView == StockUiBlueMagicView.Learned && index >= 0 && index < top.BlueSpells.Count && top.BlueSpells[index].TypeHeader;
            int help = onType ? StockUiBlueMagic.HelpType
                : top.Menu.Frame.HelpTextId >= 0 ? top.Menu.Frame.HelpTextId
                : top.BlueMagicView == StockUiBlueMagicView.Slots ? StockUiBlueMagic.HelpSetSpells : StockUiBlueMagic.HelpLearned;
            return (StockUiBlueMagic.Title, helpText?.Invoke(help));
        }
    }
}
