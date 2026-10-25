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
        private const float PriceFieldRight = 112, PriceFieldY = 22, PriceFieldHeight = 15;

        /// <summary>Where the item's name stands in the Price Set window, right of the "0" arrow.</summary>
        private const float PriceNameX = 172;

        /// <summary>The history columns: date, seller, buyer (x from the row's left), price right-aligned 8 px in.</summary>
        private const float HistoryDateX = 8, HistorySellerX = 70, HistoryBuyerX = 180;

        /// <summary>Where the empty or loading text stands in a list (as the empty spell lists, from (11, 20)).</summary>
        private const float EmptyTextX = 11, EmptyTextY = 20;

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
                    DrawHistory(renderer, font, logFont, menu, placement, timestamp);
                    break;
                case StockUiAuctionView.Price:
                    DrawPrice(renderer, font, menu, placement);
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
                string text = menu.AuctionLoading ? "Downloading..." : menu.AuctionView == StockUiAuctionView.Sales ? "No merchandise on auction." : "No merchandise.";
                StockUiTextBoxes.DrawLine(renderer, font, logFont, text, placement.X + EmptyTextX * s, placement.Y + EmptyTextY * s, s);
            }
            else
            {
                renderer.SetClip(placement.X, areaY, frame.Width * s, menu.VisibleRows * pitch * s);
                int selected = menu.EntryIndex(menu.SelectedButtonId);
                int from = Math.Max(0, (int)Math.Floor(first) - 1), to = Math.Min(menu.AuctionRows.Count - 1, (int)Math.Ceiling(first) + menu.VisibleRows);
                float rightEdge = placement.X + frame.Width * s;
                for (int i = from; i <= to; i++)
                {
                    var row = menu.AuctionRows[i];
                    float ry = areaY + (i - first) * pitch * s;
                    bool none = menu.AuctionView == StockUiAuctionView.List && row.Listings == 0;
                    UiColor? tint = i == selected ? StockUiMenuWindow.SelectedGlyphTint : none ? StockUiMenuWindow.GreyedTextTint : null;
                    string right = menu.AuctionView == StockUiAuctionView.Sales
                        ? StockUiShop.FormatPrice(row.Price)
                        : row.Listings.ToString(CultureInfo.InvariantCulture);
                    StockUiItemRows.Draw(renderer, font, menu.ItemLookup, StockUiItemRowStyle.FramedList, placement.X, ry, firstRow.Height, s,
                        row.ItemId, row.Name, tint, count: row.Stack ? row.StackSize : null, right: right, rightEdge: rightEdge);
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

        private static void DrawHistory(StockUiRenderer renderer, UiFont font, StockUiLogFont? logFont, StockUiOpenMenu menu,
            StockUiPlacement placement, long timestamp)
        {
            float s = placement.Scale;
            var definition = menu.Menu;
            var frame = definition.Frame;
            if (definition.FindButton(1) is not { } firstRow) return;
            float pitch = StockUiMenuWindow.RowPitch(firstRow, definition.FindButton(2));
            float first = StockUiMenuWindow.ScrolledFirstRow(menu, timestamp);
            if (menu.AuctionHistory.Count == 0)
            {
                string text = menu.AuctionLoading ? "Downloading..." : "No sales data.";
                StockUiTextBoxes.DrawLine(renderer, font, logFont, text, placement.X + EmptyTextX * s, placement.Y + EmptyTextY * s, s);
                return;
            }
            float areaY = placement.Y + firstRow.Y * s;
            renderer.SetClip(placement.X, areaY, frame.Width * s, menu.VisibleRows * pitch * s);
            int from = Math.Max(0, (int)Math.Floor(first) - 1), to = Math.Min(menu.AuctionHistory.Count - 1, (int)Math.Ceiling(first) + menu.VisibleRows);
            for (int i = from; i <= to; i++)
            {
                var row = menu.AuctionHistory[i];
                float ry = areaY + (i - first) * pitch * s + (firstRow.Height * s - font.LineHeight * s) * 0.5f;
                float x = placement.X + firstRow.X * s;
                renderer.DrawText(font, row.Date, x + HistoryDateX * s, ry, s);
                renderer.DrawText(font, row.Seller, x + HistorySellerX * s, ry, s);
                renderer.DrawText(font, row.Buyer, x + HistoryBuyerX * s, ry, s);
                string price = StockUiShop.FormatPrice(row.Price);
                renderer.DrawText(font, price, placement.X + (frame.Width - 8) * s - font.MeasureWidth(price) * s, ry, s);
            }
            renderer.ClearClip();
            if (menu.CanScroll) StockUiMenuWindow.DrawScrollbar(renderer, placement, frame, first, menu.Rows.Count, menu.VisibleRows);
        }

        /// <summary>The price right-aligned in the red field with the caret after it while typed, the item's name right of the field.</summary>
        private static void DrawPrice(StockUiRenderer renderer, UiFont font, StockUiOpenMenu menu, StockUiPlacement placement)
        {
            float s = placement.Scale;
            string typed = menu.MacroField is { IsOpen: true } field ? field.Text : string.Empty;
            uint price = StockUiAuction.ParsePrice(typed);
            string text = typed.Length == 0 ? "0" : StockUiShop.FormatGil(price);
            bool editing = menu.MacroField is { IsOpen: true };
            float y = placement.Y + PriceFieldY * s + (PriceFieldHeight * s - font.LineHeight * s) * 0.5f;
            float right = placement.X + PriceFieldRight * s;
            if (editing) text += "_";
            renderer.DrawText(font, text, right - font.MeasureWidth(text) * s, y, s);
            var item = menu.AuctionItem;
            string name = item.Stack ? $"{item.Name} x{item.StackSize}" : item.Name;
            renderer.DrawText(font, name, placement.X + PriceNameX * s, y, s);
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
