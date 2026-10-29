// src/Gordian.App/Graphics/StockUiItemInfoWindow.cs
using System;
using System.Diagnostics;
using System.Globalization;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// The item info window, one layout for every window that describes an item (#418): Items, Equipment, Equipment Sets,
    /// synthesis, the check window, the Treasure Pool and the shop / guild shop's Buy and Sell lists. Until #418 the
    /// Treasure Pool and the shop each drew their own copy (a fixed-height window in the menu font, the short name, no level
    /// line and no paging; #417). Laid out after the maintainer's retail captures (2026-10-07 check, 2026-10-11 Items):
    /// <list type="bullet">
    /// <item>the window is sized to the page's lines (<see cref="StockUiItemDescription.FrameFor"/>: <c>iteminfo</c> up to
    /// three, <c>item4inf</c> ... <c>item12in</c>, 8 + 16 n px tall, 366 wide);</item>
    /// <item>the 32 x 32 icon at (8,12); the lines in the log font 16 px apart from (48,4), clipped to the window
    /// (<see cref="StockUiTextBoxes"/>);</item>
    /// <item>a stack's size over the icon's bottom right in pale yellow <c>fontshp</c> (the Auction House's bid question on a
    /// stack of Fire crystals, round 2 of the 2026-10-24 test, image-27, #464);</item>
    /// <item>the footer ("&lt;Item Level:119&gt;") right-aligned 8 px from the right edge under the lines, with the log's
    /// page-wait arrow (<c>kaipage</c>) after it while another page follows;</item>
    /// <item>without an item (an empty grid slot, the guild Sell list's "Select from your inventory.") the empty
    /// <c>iteminfo</c> window with its bare slot box (the synthesis video and the guild shop's image-5, 2026-10-16).</item>
    /// </list>
    /// The window takes plain inputs (an item id, its page and an item lookup), not a menu, so other windows and later the
    /// <c>gordian.ui</c> addon API can draw it the same way. Where it goes is the caller's: <see cref="HeightOf"/> gives its
    /// height for a window placed by its bottom (<see cref="StockUiWindowPlacement.TopOnLog"/>).
    /// </summary>
    public static class StockUiItemInfoWindow
    {
        /// <summary>The window's top under a list or a grid: layout y 240, the DAT's item info place.</summary>
        public const float DefaultTop = 240;

        private const float IconX = 8, IconY = 12, IconSize = 32, TextX = 48, TextY = 4, RightInset = 8;

        /// <summary>
        /// The stack count's place, measured on image-27 (UI 1:1 on the 2556-px capture): the digits' ink (9 px tall, the
        /// font's own size) ends 2 px right of the icon and on its bottom row, in (226, 226, 134) with the font's dark outline.
        /// These are the font cell's right and bottom edges that put the ink there (checked offscreen).
        /// </summary>
        private const float CountRight = IconX + IconSize + 3, CountBottom = IconY + IconSize + 3;

        /// <summary>The count's colour, (226, 226, 134) at the 0x80 half scale.</summary>
        internal static readonly UiColor CountTint = new(0x71, 0x71, 0x43, 0x80);

        /// <summary>The window for a page (sized to its lines), or the empty <c>iteminfo</c> window without one; null when the UI DAT lacks it.</summary>
        public static UiMenuDefinition? FrameOf(UiResourceLibrary library, StockUiItemDescriptionPage? page)
        {
            string name = page != null ? StockUiItemDescription.FrameFor(page.LineCount) : StockUiShop.InfoMenu;
            if (library.TryGetMenu(name, out var info) || library.TryGetMenu(StockUiShop.InfoMenu, out info)) return info;
            return null;
        }

        /// <summary>The window's height in screen px at <paramref name="scale"/> for a page (0 when the UI DAT lacks it).</summary>
        public static float HeightOf(UiResourceLibrary library, StockUiItemDescriptionPage? page, float scale) =>
            FrameOf(library, page) is { } info ? info.Frame.Height * scale : 0;

        /// <summary>
        /// Draws the item info window with its top left at <paramref name="at"/>: the item's icon (with
        /// <paramref name="stackCount"/> over it when given) and the page's lines, footer and page arrow, or the empty window
        /// when <paramref name="page"/> is null.
        /// </summary>
        public static void Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont,
            Func<ushort, ItemRecord?>? itemLookup, ushort itemId, StockUiItemDescriptionPage? page, StockUiPlacement at, long timestamp,
            int? stackCount = null)
        {
            if (FrameOf(library, page) is not { } info) return;
            float s = at.Scale;
            if (page == null)
            {
                renderer.DrawMenu(info, at, includeButtons: true, opaqueBody: true);
                return;
            }
            renderer.DrawMenu(info, at, includeButtons: false, opaqueBody: true);
            StockUiMenuWindow.DrawItemIcon(renderer, itemLookup, itemId, at.X + IconX * s, at.Y + IconY * s, IconSize * s);
            if (stackCount is { } count) DrawCount(renderer, font, count, at);
            // The text box: from (48,4) to 8 px before the right edge, the window's height (StockUiTextBoxes clips to it).
            StockUiTextBoxes.DrawLines(renderer, font, logFont, page.Lines, at.X + TextX * s, at.Y + TextY * s,
                info.Frame.Width - TextX - RightInset, info.Frame.Height - TextY, s);
            if (page.Footer.Length == 0 && !page.HasMore) return;
            float footerY = at.Y + (TextY + page.Lines.Count * StockUiLogFont.CellHeight) * s;
            float right = at.X + (info.Frame.Width - RightInset) * s;
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
            StockUiTextBoxes.DrawLine(renderer, font, logFont, page.Footer, right - StockUiTextBoxes.MeasureLine(font, logFont, page.Footer) * s, footerY, s);
        }

        /// <summary>
        /// Draws the description a window shows (<see cref="StockUiOpenMenu.DescribedItem"/> on its
        /// <see cref="StockUiOpenMenu.ShownDescriptionPage"/>) with its top left at <paramref name="at"/>; the empty window when
        /// <paramref name="emptyWhenNone"/> and nothing is described, else nothing.
        /// </summary>
        public static void Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont,
            StockUiOpenMenu menu, StockUiPlacement at, long timestamp, bool emptyWhenNone = false)
        {
            var page = menu.ShownDescriptionPage;
            if (page == null && !emptyWhenNone) return;
            Draw(renderer, library, font, logFont, menu.ItemLookup, menu.DescribedItem, page, at, timestamp, menu.DescribedStackCount);
        }

        /// <summary>The stack count right-aligned on the icon's bottom right, in <c>fontshp</c> with its own outline.</summary>
        private static void DrawCount(StockUiRenderer renderer, UiFont font, int count, StockUiPlacement at)
        {
            float s = at.Scale;
            string text = count.ToString(CultureInfo.InvariantCulture);
            float right = at.X + CountRight * s, bottom = at.Y + CountBottom * s;
            renderer.DrawText(font, text, right - font.MeasureWidth(text) * s, bottom - font.LineHeight * s, s, CountTint);
        }
    }
}
