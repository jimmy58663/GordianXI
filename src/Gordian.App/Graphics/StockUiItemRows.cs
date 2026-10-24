// src/Gordian.App/Graphics/StockUiItemRows.cs
using System;
using System.Globalization;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Where an item row's parts sit, in layout px from the x the row is drawn at: the 16 x 16 icon, the name after it, the
    /// stack count over the icon's bottom right (its right edge and the font cell's bottom), and the right-hand column's inset
    /// from the right edge the caller gives.
    /// </summary>
    public readonly record struct StockUiItemRowStyle(float IconX, float IconSize, float NameX, float CountRight, float CountBottom, float RightInset)
    {
        /// <summary>
        /// The Items, Equipment and synthesis lists (<c>inventor</c>, retail captures 2026-10-11): drawn from the row button's
        /// origin, the icon in its 16 x 16 slot box there, the name from 19 px in, the count's right edge 15 px into the box and
        /// its cell's bottom 17 px down (the digit ends about 1 px above the icon's bottom; lowered after the maintainer's
        /// round 1, 2026-10-12).
        /// </summary>
        public static readonly StockUiItemRowStyle ItemList = new(0, 16, 19, 15, 17, 8);

        /// <summary>
        /// The shop and Treasure Pool lists (<c>shop</c>, <c>loot</c>): drawn from the frame's left edge, the 16 x 16 icon box at
        /// x 3, the name from x 22, the price or time left right-aligned 8 px from the frame's right edge (Tier 2 chunk 6c,
        /// the Treasure Pool's retail screenshots 2026-10-07).
        /// </summary>
        public static readonly StockUiItemRowStyle FramedList = new(3, 16, 22, 15, 17, 8);
    }

    /// <summary>
    /// One item row of a list, the layout the Items, Equipment, synthesis, shop and Treasure Pool lists share (#418): the
    /// item's icon, its name centred on the row's height in the menu font, the stack count over the icon in the small font,
    /// and an optional right-aligned column (a price, the time left). The caller chooses the row's place, colours and
    /// clipping, so a list (or later a <c>gordian.ui</c> addon) can draw rows from any frame.
    /// </summary>
    public static class StockUiItemRows
    {
        /// <summary>
        /// Draws a row at (<paramref name="x"/>, <paramref name="y"/>) (screen px) <paramref name="rowHeight"/> layout px tall;
        /// <paramref name="count"/> is drawn over the icon when given, <paramref name="right"/> right-aligned before
        /// <paramref name="rightEdge"/> (screen px) less the style's inset.
        /// </summary>
        public static void Draw(StockUiRenderer renderer, UiFont font, Func<ushort, ItemRecord?>? itemLookup, in StockUiItemRowStyle style,
            float x, float y, float rowHeight, float scale, ushort itemId, string name, UiColor? tint = null, int? count = null,
            string? right = null, float rightEdge = 0)
        {
            float s = scale;
            float iconX = x + style.IconX * s;
            if (itemId != 0) StockUiMenuWindow.DrawItemIcon(renderer, itemLookup, itemId, iconX, y, style.IconSize * s);
            if (count is { } n)
            {
                float ts = s * StockUiPartyWindow.TextScale;
                string text = n.ToString(CultureInfo.InvariantCulture);
                renderer.DrawText(font, text, iconX + style.CountRight * s - font.MeasureWidth(text) * ts, y + style.CountBottom * s - font.LineHeight * ts, ts);
            }
            float textY = TextY(font, y, rowHeight, s);
            renderer.DrawText(font, name, x + style.NameX * s, textY, s, tint);
            if (!string.IsNullOrEmpty(right))
            {
                renderer.DrawText(font, right, rightEdge - style.RightInset * s - font.MeasureWidth(right) * s, textY, s, tint);
            }
        }

        /// <summary>The menu font's line centred on a row <paramref name="rowHeight"/> layout px tall at screen y <paramref name="y"/>.</summary>
        public static float TextY(UiFont font, float y, float rowHeight, float scale) => y + (rowHeight * scale - font.LineHeight * scale) * 0.5f;
    }

    /// <summary>
    /// An equipment grid's items (#418: the Equipment, Equipment Sets and check windows' 4 x 4 slot grids, and the synthesis
    /// grid's slots): each slot button's item as its 32 x 32 icon over the button, from a per-window lookup of the item a
    /// button holds. Slot labels and marks (the red X of a removed set piece, the synthesis counts) stay the window's own.
    /// </summary>
    public static class StockUiItemGrid
    {
        /// <summary>The icon size over a slot button (the slot art is the empty slot's look).</summary>
        public const float SlotIconSize = 32;

        /// <summary>
        /// Draws the icon of <paramref name="itemIn"/>(button id) over every button of <paramref name="grid"/> drawn at
        /// <paramref name="at"/> that holds an item (0 = empty); <paramref name="drawn"/> is told of each slot drawn with its
        /// screen place, for the window's own marks.
        /// </summary>
        public static void Draw(StockUiRenderer renderer, Func<ushort, ItemRecord?>? itemLookup, UiMenuDefinition grid, StockUiPlacement at,
            Func<int, ushort> itemIn, Action<UiMenuButton, ushort, float, float>? drawn = null)
        {
            float s = at.Scale;
            foreach (var button in grid.Buttons)
            {
                ushort item = itemIn(button.ButtonId);
                float x = at.X + button.X * s, y = at.Y + button.Y * s;
                if (item != 0) StockUiMenuWindow.DrawItemIcon(renderer, itemLookup, item, x, y, SlotIconSize * s);
                drawn?.Invoke(button, item, x, y);
            }
        }
    }
}
