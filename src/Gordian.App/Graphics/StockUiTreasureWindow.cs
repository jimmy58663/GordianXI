// src/Gordian.App/Graphics/StockUiTreasureWindow.cs
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws the Treasure Pool list's client content (#143) over the <c>loot</c> DAT frame (whose ten 16 x 16 icon boxes
    /// are frame art): per row the item's icon in the box, its name after it and the time left before the item is given
    /// out on the right; and under the list the <c>iteminfo</c> window for the selected item (icon, name, the highest lot
    /// and your own). A passed row is greyed. PROVISIONAL placements, see <see cref="StockUiTreasurePool"/>.
    /// </summary>
    public static class StockUiTreasureWindow
    {
        /// <summary>The row layout: icon box at x 3 (16 x 16), the name from x 22, the countdown right-aligned 8 px from the frame's right edge.</summary>
        private const float IconX = 3, IconSize = 16, NameX = 22, RightInset = 8;

        /// <summary>The info window's layout, as the shop's: the 32 x 32 icon at (8,12), text from x 48, y 8, 14 px apart.</summary>
        private const float InfoIconX = 8, InfoIconY = 12, InfoIconSize = 32, InfoTextX = 48, InfoTextY = 8, InfoLinePitch = 14;

        /// <summary>Half-scale grey of a row you passed on.</summary>
        private static readonly UiColor PassedTint = new(0x48, 0x48, 0x48, 0x80);

        public static void Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiOpenMenu menu, StockUiPlacement placement)
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
                UiColor? tint = button.ButtonId == menu.SelectedButtonId ? StockUiMenuWindow.SelectedGlyphTint
                    : row.Entry == TreasureEntryKind.Pass ? PassedTint : null;
                renderer.DrawText(font, row.Name, placement.X + NameX * s, textY, s, tint);
                if (pool?.GetSlot(row.Slot) is { } slot)
                {
                    string left = StockUiTreasurePool.FormatTimeLeft(pool.GetRemaining(slot));
                    renderer.DrawText(font, left, right - font.MeasureWidth(left) * s, textY, s, tint);
                }
            }

            if (menu.SelectedTreasureRow is not { } selected || !library.TryGetMenu(StockUiTreasurePool.InfoMenu, out var info)) return;
            var at = new StockUiPlacement(placement.X + (info.Frame.X - frame.X) * s, placement.Y + (info.Frame.Y - frame.Y) * s, s, false);
            renderer.DrawMenu(info, at, includeButtons: false, opaqueBody: true);
            StockUiMenuWindow.DrawItemIcon(renderer, menu, selected.ItemId, at.X + InfoIconX * s, at.Y + InfoIconY * s, InfoIconSize * s);
            var record = menu.ItemLookup?.Invoke(selected.ItemId);
            string name = record != null ? StockUiShop.LongName(record) : selected.Name;
            if (selected.Count > 1) name = $"{name} x{selected.Count}";
            float x = at.X + InfoTextX * s, ty = at.Y + InfoTextY * s;
            renderer.DrawText(font, name, x, ty, s);
            renderer.DrawText(font, StockUiTreasurePool.DescribeLeader(selected), x, ty + InfoLinePitch * s, s);
            renderer.DrawText(font, StockUiTreasurePool.DescribeEntry(selected), x, ty + 2 * InfoLinePitch * s, s);
        }
    }
}
