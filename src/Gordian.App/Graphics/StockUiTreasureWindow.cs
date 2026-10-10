// src/Gordian.App/Graphics/StockUiTreasureWindow.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws the Treasure Pool list's client content (#143) over the <c>loot</c> DAT frame (whose ten 16 x 16 icon boxes
    /// are frame art): per row the item's icon in the box, its name after it and the time left before the item is given
    /// out on the right; the selected item's description in the item info window above the log; the Spoils Options window
    /// (<c>lnowin</c>) at the top right; and the "roll" column beside the party and alliance windows
    /// (<see cref="DrawRollColumn"/>). Laid out after the maintainer's retail screenshots (2026-10-07).
    /// </summary>
    public static class StockUiTreasureWindow
    {
        /// <summary>The row layout: icon box at x 3 (16 x 16), the name from x 22, the countdown right-aligned 8 px from the frame's right edge.</summary>
        private const float IconX = 3, IconSize = 16, NameX = 22, RightInset = 8;

        /// <summary>The info window: the 32 x 32 icon at (8,12), log-font lines from (48,4), 16 px apart (as the retail screenshot).</summary>
        private const float InfoIconX = 8, InfoIconY = 12, InfoIconSize = 32, InfoTextX = 48, InfoTextY = 4, InfoRightInset = 8;

        /// <summary>Row name colours (half scale): white until you lot, orange-red after a lot (sampled (255, 148, 103)), grey after a pass.</summary>
        private static readonly UiColor LottedTint = new(0x80, 0x4A, 0x34, 0x80);
        private static readonly UiColor PassedTint = new(0x48, 0x48, 0x48, 0x80);
        private static readonly UiColor White = new(0x80, 0x80, 0x80, 0x80);

        /// <summary>The row name's colour for the local player's entry.</summary>
        public static UiColor? RowTint(TreasureEntryKind entry) => entry switch
        {
            TreasureEntryKind.Lot => LottedTint,
            TreasureEntryKind.Pass => PassedTint,
            _ => null,
        };

        public static void Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, StockUiScreen screen = default)
        {
            float s = placement.Scale;
            var definition = menu.Menu;
            var frame = definition.Frame;
            var rows = menu.TreasureRows;
            var pool = menu.TreasurePool;
            float right = placement.X + (frame.Width - RightInset) * s;
            for (int i = 0; i < rows.Count; i++)
            {
                var button = definition.FindButton(i + 1);
                if (button == null) break;
                var row = rows[i];
                float y = placement.Y + button.Y * s;
                float textY = y + (button.Height * s - font.LineHeight * s) * 0.5f;
                StockUiMenuWindow.DrawItemIcon(renderer, menu, row.ItemId, placement.X + IconX * s, y, IconSize * s);
                var tint = RowTint(row.Entry);
                renderer.DrawText(font, row.Name, placement.X + NameX * s, textY, s, tint);
                if (pool?.GetSlot(row.Slot) is { } slot)
                {
                    string left = StockUiTreasurePool.FormatTimeLeft(pool.GetRemaining(slot));
                    renderer.DrawText(font, left, right - font.MeasureWidth(left) * s, textY, s, tint);
                }
            }

            if (menu.SelectedTreasureRow is { } selected) DrawDescription(renderer, library, font, logFont, menu, selected.ItemId, placement, screen);
        }

        /// <summary>
        /// The selected item's description alone (retail screenshots): its icon, its name, then the DAT description wrapped in
        /// the log font, in the item info window sized to the lines (<c>iteminfo</c>, <c>item4inf</c> ...), its bottom on
        /// Window 1's top edge (the screen's bottom-left place while the log is hidden).
        /// </summary>
        private static void DrawDescription(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont,
            StockUiOpenMenu menu, ushort itemId, StockUiPlacement placement, StockUiScreen screen)
        {
            var record = menu.ItemLookup?.Invoke(itemId);
            if (record == null) return;
            float s = placement.Scale;
            Func<string, float> measure = logFont != null ? t => logFont.MeasureWidth(t) : t => font.MeasureWidth(t);
            string name = StockUiShop.LongName(record);
            if (name.Length > 0 && char.IsLower(name[0])) name = char.ToUpperInvariant(name[0]) + name[1..];
            var lines = new List<string> { name };
            lines.AddRange(Wrap(measure, record.Description ?? string.Empty, 366 - InfoTextX - InfoRightInset, 11));
            int count = Math.Clamp(lines.Count, 3, 12);
            string frameName = count <= 3 ? "iteminfo" : count <= 9 ? $"item{count}inf" : $"item{count}in";
            if (!library.TryGetMenu(frameName, out var info) && !library.TryGetMenu("iteminfo", out info)) return;

            float h = info.Frame.Height * s;
            float x = placement.X + (info.Frame.X - menu.Menu.Frame.X) * s;
            float y = screen.Window1Top is { } top ? top - h
                : screen.Height > 0 ? screen.Height - h - (448 - 296) * s
                : placement.Y + (240 - menu.Menu.Frame.Y) * s;
            var at = new StockUiPlacement(x, Math.Max(0, y), s, false);
            renderer.DrawMenu(info, at, includeButtons: false, opaqueBody: true);
            StockUiMenuWindow.DrawItemIcon(renderer, menu, itemId, at.X + InfoIconX * s, at.Y + InfoIconY * s, InfoIconSize * s);
            for (int i = 0; i < lines.Count && i < count; i++)
            {
                float ly = at.Y + (InfoTextY + i * StockUiLogFont.CellHeight) * s;
                if (logFont != null) logFont.Draw(renderer, lines[i], at.X + InfoTextX * s, ly, s, White);
                else renderer.DrawText(font, lines[i], at.X + InfoTextX * s, ly, s);
            }
        }

        /// <summary>Word-wraps text to a width (unscaled), keeping the DAT's line breaks; at most <paramref name="maxLines"/> lines.</summary>
        internal static List<string> Wrap(Func<string, float> measure, string text, float width, int maxLines)
        {
            var lines = new List<string>();
            foreach (string paragraph in text.Replace("\r", string.Empty).Split('\n'))
            {
                string current = string.Empty;
                foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    string candidate = current.Length == 0 ? word : current + " " + word;
                    if (current.Length > 0 && measure(candidate) > width)
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
        /// The "roll" column (retail screenshots, 2026-10-07, 1:1): while the Treasure Pool list is open, each member's entry
        /// on the selected item stands in a reddish box 26 x 13 px just left of its party (or alliance) row, level with the
        /// name (<see cref="StockUiTreasurePool.RollText"/>: the lot, "---" after a pass, "?" before either); the label
        /// "roll" in gold sits above the column at the window's top edge, and the pool's chest icon on the window's top
        /// border 9 px in.
        /// </summary>
        public static void DrawRollColumn(StockUiRenderer renderer, UiFont font, UiMenuDefinition window, StockUiPlacement placement,
            IReadOnlyList<string> texts, bool label)
        {
            float s = placement.Scale;
            float ts = s * StockUiPartyWindow.TextScale;
            float boxX = placement.X - RollBoxLeft * s;
            if (label)
            {
                renderer.DrawText(font, "roll", boxX, placement.Y - 2 * s, ts, RollLabelTint);
                if (ChestImage < font.Group.Images.Count)
                {
                    renderer.DrawImage(font.Group.Images[ChestImage], placement.X + 9 * s, placement.Y - 5 * s, ts);
                }
            }
            for (int i = 0; i < texts.Count && i < window.Buttons.Count; i++)
            {
                var button = window.Buttons[i];
                float y = placement.Y + button.Y * s;
                renderer.DrawTextureRect(BoxTexture, BoxSourceX, BoxSourceY, 1, 1, boxX, y, RollBoxWidth * s, RollBoxHeight * s, RollBoxTint);
                string text = texts[i];
                float tx = boxX + (RollBoxWidth * s - font.MeasureWidth(text) * ts) * 0.5f;
                renderer.DrawText(font, text, tx, y + (RollBoxHeight * s - font.LineHeight * ts) * 0.5f, ts);
            }
        }

        private const float RollBoxLeft = 27, RollBoxWidth = 26, RollBoxHeight = 13;

        /// <summary>The box: the white centre of <c>colorbal</c> tinted the sampled (183, 113, 102), mostly opaque.</summary>
        private const string BoxTexture = "colorbal";
        private const float BoxSourceX = 7, BoxSourceY = 23;
        private static readonly UiColor RollBoxTint = new(0x5C, 0x38, 0x33, 0x68);

        /// <summary>The "roll" label's gold (sampled (198, 150, 29)).</summary>
        private static readonly UiColor RollLabelTint = new(0x63, 0x4B, 0x0E, 0x80);

        /// <summary>The <c>fontshp</c> image of the treasure chest (ustatsHD (128,32)).</summary>
        private const int ChestImage = 122;
    }
}
