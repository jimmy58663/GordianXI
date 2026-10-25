// src/Gordian.App/Graphics/StockUiAuctionWindow.cs
using System;
using System.Globalization;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Draws the Auction House windows' client content (#90) on the retail frames (<see cref="StockUiAuction"/>): the category
    /// list and the Sales Status list in <c>auclist</c> (the shop's row layout: the icon in the frame's slot at x 3, the name
    /// from x 22, a right column 8 px from the frame's edge) with the item described under the list, the Price History in
    /// <c>auchisto</c>, and the price in <c>moneyctr</c>'s number field. PROVISIONAL (not in the capture at a readable size):
    /// the right columns (the number of listings; a sale's price), the history's columns and "Downloading..." while the search
    /// server answers.
    /// </summary>
    internal static class StockUiAuctionWindow
    {
        /// <summary>The Price Set field (the red bar of <c>moneyctr</c>), read from the frame's composite: x 32-116, y 22-37.</summary>
        private const float PriceFieldRight = 113, PriceFieldY = 22, PriceFieldHeight = 15;

        /// <summary>"G" after the field and the "/999,999,999 G" cap line under it (image-17: x 120, and x 127 at y 41).</summary>
        private const float PriceGilX = 120, PriceCapX = 127, PriceCapY = 41;

        /// <summary>
        /// The history columns in the small text (image-13, at 1:1): the date from x 4, the seller from 74, the arrow at 160,
        /// the buyer from 174, the price right-aligned 16 px in. Retail's date runs into the seller in a smaller font; a gap is
        /// kept here (round 1, N3).
        /// </summary>
        private const float HistoryDateX = 4, HistorySellerX = 74, HistoryArrowX = 160, HistoryBuyerX = 174, HistoryPriceInset = 16;

        /// <summary>Where the empty or loading text stands in a list (image-10, image-12: from (36, 26)).</summary>
        private const float EmptyTextX = 36, EmptyTextY = 26;

        /// <summary>The listings count's right edge, 8 px in from the frame's (image-15).</summary>
        private const float ListingsInset = 10;

        /// <summary>Retail's name tones in a category list (half-scale RGBA, sampled from image-11 and image-15).</summary>
        private static readonly UiColor EquippableTint = new(0x80, 0x80, 0x55, 0x80), EquippedTint = new(0x58, 0x80, 0x64, 0x80),
            StackTint = new(0x80, 0x4C, 0x70, 0x80);

        /// <summary>The pink of "No applicable entries." (image-12: 255, 140, 171).</summary>
        internal static readonly UiColor NoticeTint = new(0x80, 0x46, 0x56, 0x80);

        /// <summary>The price's digits (image-17: a light orange), the one under the cursor white.</summary>
        private static readonly UiColor PriceTint = new(0x80, 0x68, 0x50, 0x80);

        /// <summary>The item info's top left under the list: the shop's item info place (16, 240) less the list's (16, 48).</summary>
        private const float InfoOffsetY = 192;

        public static void Draw(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, long timestamp)
        {
            switch (menu.AuctionView)
            {
                case StockUiAuctionView.List:
                case StockUiAuctionView.Sales:
                    DrawList(renderer, library, font, logFont, menu, placement, timestamp);
                    break;
                case StockUiAuctionView.History:
                    DrawHistory(renderer, library, font, logFont, menu, placement, timestamp);
                    break;
                case StockUiAuctionView.Price:
                    DrawPrice(renderer, library, font, menu, placement);
                    break;
            }
        }

        private static void DrawList(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, long timestamp)
        {
            float s = placement.Scale;
            var definition = menu.Menu;
            var frame = definition.Frame;
            if (definition.FindButton(1) is not { } firstRow) return;
            float pitch = StockUiMenuWindow.RowPitch(firstRow, definition.FindButton(2));
            float first = StockUiMenuWindow.ScrolledFirstRow(menu, timestamp);
            float areaY = placement.Y + firstRow.Y * s;
            if (menu.AuctionRows.Count == 0)
            {
                DrawEmpty(renderer, font, logFont, placement, menu.AuctionLoading ? null
                    : menu.AuctionView == StockUiAuctionView.Sales ? StockUiAuction.NoSalesText : StockUiAuction.NoEntriesText);
            }
            else
            {
                renderer.SetClip(placement.X, areaY, frame.Width * s, menu.VisibleRows * pitch * s);
                int selected = menu.EntryIndex(menu.SelectedButtonId);
                int from = Math.Max(0, (int)Math.Floor(first) - 1), to = Math.Min(menu.AuctionRows.Count - 1, (int)Math.Ceiling(first) + menu.VisibleRows);
                float rightEdge = placement.X + frame.Width * s;
                float ts = s * StockUiPartyWindow.TextScale;
                for (int i = from; i <= to; i++)
                {
                    var row = menu.AuctionRows[i];
                    float ry = areaY + (i - first) * pitch * s;
                    // Items none of are listed are not greyed (round 1, N2); the name's tone says equippable, worn or stack.
                    UiColor? tint = i == selected ? StockUiMenuWindow.SelectedGlyphTint : row.Tone switch
                    {
                        StockUiAuctionTone.Equippable => EquippableTint,
                        StockUiAuctionTone.Equipped => EquippedTint,
                        StockUiAuctionTone.Stack => StackTint,
                        _ => null,
                    };
                    string? right = menu.AuctionView == StockUiAuctionView.Sales ? StockUiShop.FormatPrice(row.Price) : null;
                    StockUiItemRows.Draw(renderer, font, menu.ItemLookup, StockUiItemRowStyle.FramedList, placement.X, ry, firstRow.Height, s,
                        row.ItemId, row.Name, tint, count: row.Stack ? row.StackSize : null, right: right, rightEdge: rightEdge);
                    if (menu.AuctionView == StockUiAuctionView.List && StockUiAuction.ListingsText(row.Listings) is { Length: > 0 } listings)
                    {
                        // "[7]" in the small text at the row's right (image-15); nothing when none are listed.
                        float ly = StockUiItemRows.TextY(font, ry, firstRow.Height, s) + (font.LineHeight * s - font.LineHeight * ts) * 0.5f;
                        renderer.DrawText(font, listings, rightEdge - ListingsInset * s - font.MeasureWidth(listings) * ts, ly, ts);
                    }
                }
                renderer.ClearClip();
                if (menu.CanScroll) StockUiMenuWindow.DrawScrollbar(renderer, placement, frame, first, menu.Rows.Count, menu.VisibleRows);
            }
            if (!menu.ShowsQuantity)
            {
                // The capture (0:55-1:20): the item under the cursor is described under the list.
                StockUiItemInfoWindow.Draw(renderer, library, font, logFont, menu, placement with { Y = placement.Y + InfoOffsetY * s }, timestamp, emptyWhenNone: true);
            }
        }

        /// <summary>"Downloading..." while the search server answers, else a list's empty text in retail's pink.</summary>
        private static void DrawEmpty(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, StockUiPlacement placement, string? empty)
        {
            float s = placement.Scale;
            float x = placement.X + EmptyTextX * s, y = placement.Y + EmptyTextY * s;
            if (empty == null) StockUiTextBoxes.DrawLine(renderer, font, logFont, "Downloading...", x, y, s);
            else if (logFont != null) logFont.Draw(renderer, empty, x, y + s, s, NoticeTint);
            else renderer.DrawText(font, empty, x, y, s, NoticeTint);
        }

        /// <summary>
        /// Price History (image-13): each sale in the small text, the date, the seller, an arrow, the buyer and the price; the
        /// item described under the window (image-12, image-13). A sale's history (image-17) names the item on its first row.
        /// </summary>
        private static void DrawHistory(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, long timestamp)
        {
            float s = placement.Scale;
            var definition = menu.Menu;
            var frame = definition.Frame;
            if (definition.FindButton(1) is not { } firstRow) return;
            float pitch = StockUiMenuWindow.RowPitch(firstRow, definition.FindButton(2));
            float first = StockUiMenuWindow.ScrolledFirstRow(menu, timestamp);
            float areaY = placement.Y + firstRow.Y * s;
            float x = placement.X + firstRow.X * s;
            int offset = 0;
            if (menu.AuctionForSale)
            {
                var item = menu.AuctionItem;
                renderer.DrawText(font, item.Stack ? $"{item.Name} x{item.StackSize}" : item.Name, x + HistoryDateX * s,
                    StockUiItemRows.TextY(font, areaY, firstRow.Height, s), s);
                offset = 1;
            }
            else
            {
                StockUiItemInfoWindow.Draw(renderer, library, font, logFont, menu, placement with { Y = placement.Y + InfoOffsetY * s }, timestamp, emptyWhenNone: true);
            }
            if (menu.AuctionHistory.Count == 0)
            {
                DrawEmpty(renderer, font, logFont, offset == 0 ? placement : placement with { Y = placement.Y + pitch * s },
                    menu.AuctionLoading ? null : StockUiAuction.NoEntriesText);
                return;
            }
            float ts = s * StockUiPartyWindow.TextScale;
            renderer.SetClip(placement.X, areaY, frame.Width * s, menu.VisibleRows * pitch * s);
            int from = Math.Max(0, (int)Math.Floor(first) - 1), to = Math.Min(menu.AuctionHistory.Count - 1, (int)Math.Ceiling(first) + menu.VisibleRows);
            for (int i = from; i <= to; i++)
            {
                var row = menu.AuctionHistory[i];
                float ry = areaY + (i + offset - first) * pitch * s + (firstRow.Height * s - font.LineHeight * ts) * 0.5f;
                renderer.DrawText(font, row.Date, x + HistoryDateX * s, ry, ts);
                renderer.DrawText(font, row.Seller, x + HistorySellerX * s, ry, ts);
                DrawArrow(renderer, font, x + HistoryArrowX * s, ry, ts);
                renderer.DrawText(font, row.Buyer, x + HistoryBuyerX * s, ry, ts);
                string price = StockUiShop.FormatPrice(row.Price);
                renderer.DrawText(font, price, placement.X + (frame.Width - HistoryPriceInset) * s - font.MeasureWidth(price) * ts, ry, ts);
            }
            renderer.ClearClip();
            if (menu.CanScroll) StockUiMenuWindow.DrawScrollbar(renderer, placement, frame, first, menu.Rows.Count, menu.VisibleRows);
        }

        /// <summary>
        /// The "→" between seller and buyer: the menu font has no arrow glyph, so its "-" and "&gt;" are drawn into one
        /// (PROVISIONAL: retail's glyph is a thin arrow in the same font, image-13).
        /// </summary>
        private static void DrawArrow(StockUiRenderer renderer, UiFont font, float x, float y, float scale)
        {
            renderer.DrawText(font, "-", x, y, scale);
            renderer.DrawText(font, ">", x + font.MeasureWidth("-") * scale * 0.6f, y, scale);
        }

        /// <summary>
        /// Price Set (image-17): the price right-aligned in the red field with the digit under the cursor in white, "G" after
        /// it and the most the field takes ("/999,999,999 G") under it; "Current Gil" (<c>money</c>, 16,240) beside it.
        /// </summary>
        private static void DrawPrice(StockUiRenderer renderer, UiResourceLibrary library, UiFont font, StockUiOpenMenu menu, StockUiPlacement placement)
        {
            float s = placement.Scale;
            var frame = menu.Menu.Frame;
            string typed = menu.MacroField is { IsOpen: true } field ? field.Text : string.Empty;
            uint price = StockUiAuction.ParsePrice(typed);
            string text = StockUiAuction.PriceDigitsText(price, menu.AuctionPriceDigit);
            float y = placement.Y + PriceFieldY * s + (PriceFieldHeight * s - font.LineHeight * s) * 0.5f;
            float right = placement.X + PriceFieldRight * s;
            float x = right - font.MeasureWidth(text) * s;
            int cursor = text.Length - 1 - Math.Clamp(menu.AuctionPriceDigit, 0, text.Length - 1);
            for (int i = 0; i < text.Length; i++)
            {
                string digit = text[i].ToString();
                renderer.DrawText(font, digit, x, y, s, i == cursor ? StockUiMenuWindow.White : PriceTint);
                x += font.MeasureWidth(digit) * s;
            }
            renderer.DrawText(font, "G", placement.X + PriceGilX * s, y, s);
            float ts = s * StockUiPartyWindow.TextScale;
            uint cap = menu.PriceFor == StockUiAuctionPriceFor.Bid && menu.Inventory != null
                ? Math.Min(StockUiShop.Gil(menu.Inventory), StockUiAuction.MaxPrice) : StockUiAuction.MaxPrice;
            renderer.DrawText(font, "/" + StockUiShop.FormatPrice(cap), placement.X + PriceCapX * s, placement.Y + PriceCapY * s, ts);
            if (menu.Inventory != null && library.TryGetMenu(StockUiItems.GilMenu, out var gil))
            {
                StockUiMenuWindow.DrawGilWindow(renderer, font, gil, StockUiWindowPlacement.Beside(placement, frame, gil.Frame), StockUiShop.Gil(menu.Inventory));
            }
        }

        /// <summary>
        /// The help bar's title and help line over the top Auction House window (the capture: the window's title in the title box,
        /// the selected entry's help line), or null when the top window is not one.
        /// </summary>
        public static (string Title, string? Help)? HelpBar(StockUiOpenMenu top, Func<int, string?>? helpText)
        {
            if (!top.IsAuctionWindow && top.ItemListKind != StockUiItemListKind.AuctionSell) return null;
            string title = top.AuctionView switch
            {
                StockUiAuctionView.List => top.AuctionTitle ?? StockUiAuction.BidTitle,
                StockUiAuctionView.History => StockUiAuction.HistoryTitle,
                StockUiAuctionView.Price => StockUiAuction.PriceTitle,
                _ => StockUiAuction.AuctionTitle,
            };
            int helpId = top.SelectedButton is { HelpTextId: >= 0 } button && top.AuctionView == StockUiAuctionView.None ? button.HelpTextId
                : top.AuctionView == StockUiAuctionView.History ? top.Menu.Frame.HelpTextId
                : top.AuctionView == StockUiAuctionView.Sales && top.SelectedAuctionRow is { } sale ? StockUiAuction.SaleHelp(sale.Stat)
                : top.AuctionHelpId;
            return (title, helpId >= 0 ? helpText?.Invoke(helpId) : null);
        }
    }
}
