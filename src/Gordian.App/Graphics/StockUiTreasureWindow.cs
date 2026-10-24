// src/Gordian.App/Graphics/StockUiTreasureWindow.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;

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
        /// <summary>Row name colours (half scale): white until you lot, orange-red after a lot (sampled (255, 148, 103)), grey after a pass.</summary>
        private static readonly UiColor LottedTint = new(0x80, 0x4A, 0x34, 0x80);
        private static readonly UiColor PassedTint = new(0x48, 0x48, 0x48, 0x80);

        /// <summary>The row name's colour for the local player's entry.</summary>
        public static UiColor? RowTint(TreasureEntryKind entry) => entry switch
        {
            TreasureEntryKind.Lot => LottedTint,
            TreasureEntryKind.Pass => PassedTint,
            _ => null,
        };

        public static void Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, StockUiScreen screen = default, long timestamp = 0)
        {
            float s = placement.Scale;
            var definition = menu.Menu;
            var frame = definition.Frame;
            var rows = menu.TreasureRows;
            var pool = menu.TreasurePool;
            float right = placement.X + frame.Width * s;
            for (int i = 0; i < rows.Count; i++)
            {
                var button = definition.FindButton(i + 1);
                if (button == null) break;
                var row = rows[i];
                float y = placement.Y + button.Y * s;
                // The icon box (frame art), the name, and the time left before the item is given out on the right.
                string? left = pool?.GetSlot(row.Slot) is { } slot ? StockUiTreasurePool.FormatTimeLeft(pool.GetRemaining(slot)) : null;
                StockUiItemRows.Draw(renderer, font, menu.ItemLookup, StockUiItemRowStyle.FramedList, placement.X, y, button.Height, s,
                    row.ItemId, row.Name, RowTint(row.Entry), right: left, rightEdge: right);
            }

            DrawDescription(renderer, library, font, logFont, menu, placement, screen, timestamp);
        }

        /// <summary>
        /// The selected item's description in the item info window (<see cref="StockUiItemInfoWindow"/>, the same pages as the
        /// Items window's, turned with X), its bottom on Window 1's top edge (retail screenshots; while the log is hidden, the
        /// list's own bottom-left place, 152 px above the screen's bottom).
        /// </summary>
        private static void DrawDescription(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont,
            StockUiOpenMenu menu, StockUiPlacement placement, StockUiScreen screen, long timestamp)
        {
            var page = menu.ShownDescriptionPage;
            if (page == null) return;
            float s = placement.Scale;
            float h = StockUiItemInfoWindow.HeightOf(library, page, s);
            var frame = menu.Menu.Frame;
            var at = StockUiWindowPlacement.Beside(placement, frame, StockUiItemInfoWindow.FrameOf(library, page)?.Frame.X ?? frame.X, StockUiItemInfoWindow.DefaultTop);
            float y = StockUiWindowPlacement.TopOnLog(screen, s, h, at.Y, screen.Height - h - HiddenLogBottom * s);
            StockUiItemInfoWindow.Draw(renderer, library, font, logFont, menu, at with { Y = y }, timestamp);
        }

        /// <summary>The description's bottom above the screen's bottom while the log is hidden (layout 448 - 296).</summary>
        private const float HiddenLogBottom = 448 - 296;

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
