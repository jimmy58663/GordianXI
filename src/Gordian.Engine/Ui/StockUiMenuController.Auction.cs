// src/Gordian.Engine/Ui/StockUiMenuController.Auction.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.Packets;
using Gordian.Core.Network.Search;
using Gordian.Core.Resources.Ui;
using Gordian.Core.World;
using Gordian.Engine.Input;

namespace Gordian.Engine.Ui
{
    public sealed partial class StockUiOpenMenu
    {
        /// <summary>What an Auction House list, history or price window shows (#90); <see cref="StockUiAuctionView.None"/> otherwise.</summary>
        public StockUiAuctionView AuctionView { get; init; }

        /// <summary>A list's rows (category items or listings), parallel to <see cref="Rows"/>.</summary>
        public IReadOnlyList<StockUiAuctionRow> AuctionRows { get; internal set; } = Array.Empty<StockUiAuctionRow>();

        /// <summary>A Price History window's sales, parallel to <see cref="Rows"/>.</summary>
        public IReadOnlyList<StockUiAuctionHistoryRow> AuctionHistory { get; internal set; } = Array.Empty<StockUiAuctionHistoryRow>();

        /// <summary>The item a history, an item / sale menu or a price window is about.</summary>
        public StockUiAuctionRow AuctionItem { get; init; }

        /// <summary>The category a list shows (the search server's number); 0 for the Sales Status list.</summary>
        public byte AuctionCategory { get; init; }

        /// <summary>The help bar's title over this window (the category's name over its list).</summary>
        public string? AuctionTitle { get; init; }

        /// <summary>What a Price Set window is for.</summary>
        public StockUiAuctionPriceFor PriceFor { get; init; }

        /// <summary>Whether the window waits for the search server ("Downloading" in retail, the capture at 1:50).</summary>
        public bool AuctionLoading { get; internal set; }

        /// <summary>The help line (ROM/165/75) the help bar shows over a window whose selected button has none; -1 for none.</summary>
        public int AuctionHelpId { get; internal set; } = -1;

        /// <summary>A category list or the Sales Status list.</summary>
        public bool IsAuctionList => AuctionView is StockUiAuctionView.List or StockUiAuctionView.Sales;

        /// <summary>Any Auction House window (the DAT's <c>auc*</c> menus and the client's lists): they keep their authored places.</summary>
        public bool IsAuctionWindow => AuctionView != StockUiAuctionView.None || Name.StartsWith("auc", StringComparison.OrdinalIgnoreCase);

        /// <summary>The list row under the cursor, if any.</summary>
        public StockUiAuctionRow? SelectedAuctionRow
        {
            get
            {
                int index = EntryIndex(SelectedButtonId);
                return IsAuctionList && index >= 0 && index < AuctionRows.Count ? AuctionRows[index] : null;
            }
        }
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>C2S 0x04E WorkCheck, sent when the Auction House window opens.</summary>
        public Func<Task>? AuctionOpenRequested { get; set; }

        /// <summary>C2S 0x04E Info (the Sales Status window).</summary>
        public Func<Task>? AuctionSalesRequested { get; set; }

        /// <summary>A category's items from the search server; null when it did not answer.</summary>
        public Func<byte, Task<IReadOnlyList<AuctionListItem>?>>? AuctionListRequested { get; set; }

        /// <summary>An item's price history (item, stack) from the search server; null when it did not answer.</summary>
        public Func<ushort, bool, Task<IReadOnlyList<AuctionHistoryEntry>?>>? AuctionHistoryRequested { get; set; }

        /// <summary>C2S 0x04E Bid (item, price, stack, sale slot).</summary>
        public Func<ushort, uint, bool, sbyte, Task>? AuctionBid { get; set; }

        /// <summary>C2S 0x04E AskCommit (inventory slot, item, price, stack).</summary>
        public Func<byte, ushort, uint, bool, Task>? AuctionAskCommit { get; set; }

        /// <summary>C2S 0x04E LotIn (sale slot, inventory slot, price, stack).</summary>
        public Func<sbyte, byte, uint, bool, Task>? AuctionListForSale { get; set; }

        /// <summary>C2S 0x04E LotCancel (sale slot).</summary>
        public Func<sbyte, Task>? AuctionCancelSale { get; set; }

        /// <summary>The session's Auction House cache (the sale slots), or null.</summary>
        public Func<AuctionHouseState?>? AuctionHouse { get; set; }

        private volatile bool _auctionOpen;
        private StockUiOpenMenu? _priceMenu;
        private bool _priceCommitted;
        private (StockUiAuctionRow Item, uint Price, bool Stack)? _pendingSale;

        /// <summary>Whether the Auction House window is open.</summary>
        public bool IsAuctionOpen => _auctionOpen;

        /// <summary>
        /// The server opened the Auction House (S2C 0x04C MOPEN): <c>auc1</c> opens as the root, closing whatever menus were open,
        /// as the shops do, and C2S WorkCheck goes out.
        /// </summary>
        public void OnAuctionOpened()
        {
            StockUiOpenMenu[] closed;
            StockUiOpenMenu? root;
            lock (_sync)
            {
                closed = _open;
                foreach (var menu in closed) Remember(menu);
                _open = Array.Empty<StockUiOpenMenu>();
                root = Push(StockUiAuction.MainMenu, null, Array.Empty<string>(), null, null);
                _auctionOpen = root != null;
                _pendingSale = null;
            }
            foreach (var menu in closed)
            {
                menu.Cancelled = true;
                menu.Prompt?.TrySetResult(false);
                menu.QueryCompleted?.Invoke(255);
            }
            if (root == null) return;
            Changed?.Invoke();
            if (AuctionOpenRequested is { } open) _ = SendAsync(open, "Auction House open");
        }

        /// <summary>A zone change (or the window's last Cancel) ends the Auction House: its windows close.</summary>
        public void CloseAuctionHouse()
        {
            if (!_auctionOpen) return;
            _auctionOpen = false;
            _pendingSale = null;
            StockUiOpenMenu? root = null;
            foreach (var menu in _open)
            {
                if (menu.Name.Equals(StockUiAuction.MainMenu, StringComparison.OrdinalIgnoreCase)) root = menu;
            }
            if (MacroField.IsOpen && _priceMenu != null) MacroField.Cancel();
            _priceMenu = null;
            if (root != null) CloseMenu(root);
            AuctionHouse?.Invoke()?.Close();
        }

        /// <summary>
        /// An Auction House answer (S2C 0x04C): its message goes to the log (ROM/165/72, XiPackets for the rest); a fee quote for the
        /// sale being placed asks "Place this merchandise on auction?"; a bid or a listing refreshes the open list.
        /// </summary>
        public void OnAuctionResponse(AuctionResponse response)
        {
            if (StockUiAuction.ResultMessage(response, MenuMessageText) is { Length: > 0 } message)
            {
                foreach (string line in message.Split('\n')) NoticePosted?.Invoke(line);
            }
            if (!_auctionOpen) return;
            switch (response.Command)
            {
                case AuctionCommand.AskCommit:
                    OnFeeQuoted(response);
                    break;
                case AuctionCommand.Bid when response.ResultCode == AuctionResultCode.Success:
                    RefreshAuctionLists();
                    break;
                case AuctionCommand.LotIn when response.ResultCode == AuctionResultCode.Success:
                    CloseSellFlow();
                    break;
            }
        }

        /// <summary>The sale slots changed: open Sales Status lists take the new rows.</summary>
        public void OnAuctionSalesChanged()
        {
            if (!_auctionOpen) return;
            bool changed = false;
            lock (_sync)
            {
                foreach (var menu in _open)
                {
                    if (menu.AuctionView != StockUiAuctionView.Sales) continue;
                    FillAuctionRows(menu, StockUiAuction.SalesRows(AuctionHouse?.Invoke()?.SnapshotSales() ?? Array.Empty<AuctionSale?>(), ItemLookup));
                    menu.AuctionLoading = false;
                    changed = true;
                }
            }
            if (changed) Changed?.Invoke();
        }

        /// <summary>Confirm on an Auction House window; false when the top window is not one.</summary>
        private bool TryActivateAuction(StockUiOpenMenu top, UiMenuButton? button)
        {
            if (!_auctionOpen) return false;
            if (top.AuctionView == StockUiAuctionView.Price)
            {
                if (MacroField.IsOpen) MacroField.Commit();
                return true;
            }
            if (top.IsAuctionList)
            {
                ActivateAuctionRow(top);
                return true;
            }
            if (top.AuctionView == StockUiAuctionView.History) return true;
            if (button == null) return false;
            string name = top.Name;
            if (name.Equals(StockUiAuction.MainMenu, StringComparison.OrdinalIgnoreCase))
            {
                switch (button.ButtonId)
                {
                    case StockUiAuction.BidButton:
                        lock (_sync) Push(StockUiAuction.CategoryMenu, top, Array.Empty<string>(), null, null);
                        break;
                    case StockUiAuction.SellButton:
                        OpenAuctionSellList(top);
                        break;
                    case StockUiAuction.SalesButton:
                        OpenSalesStatus(top);
                        break;
                }
                return true;
            }
            if (StockUiAuction.TryGetCategory(name, button.ButtonId, out var category))
            {
                if (category.Opens != null) lock (_sync) Push(category.Opens, top, Array.Empty<string>(), null, null);
                else OpenAuctionList(top, category);
                return true;
            }
            if (name.Equals(StockUiAuction.ItemMenu, StringComparison.OrdinalIgnoreCase))
            {
                switch (button.ButtonId)
                {
                    case StockUiAuction.ItemHistoryButton:
                        OpenPriceHistory(top, top.AuctionItem);
                        break;
                    case StockUiAuction.ItemBidButton:
                        OpenPriceSet(top, top.AuctionItem, StockUiAuctionPriceFor.Bid);
                        break;
                    default:
                        NoticePosted?.Invoke("Sorting the Auction House list is not available yet.");
                        break;
                }
                return true;
            }
            if (name.Equals(StockUiAuction.SaleMenu, StringComparison.OrdinalIgnoreCase))
            {
                if (button.ButtonId == StockUiAuction.SaleHistoryButton) OpenPriceHistory(top, top.AuctionItem);
                else AskCancelSale(top, top.AuctionItem);
                return true;
            }
            return false;
        }

        /// <summary>A leaf category: <c>auclist</c> with the category's name as its title, filled when the search server answers.</summary>
        private void OpenAuctionList(StockUiOpenMenu parent, StockUiAuctionCategory category)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiAuction.ListMenu, out var definition)) return;
            var list = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null, VisibleRowsOf(definition))
            {
                AuctionView = StockUiAuctionView.List,
                AuctionCategory = category.Category,
                AuctionTitle = category.Name,
                ItemLookup = ItemLookup,
            };
            list.AuctionLoading = true;
            list.AuctionHelpId = StockUiAuction.HelpSelectBidItem;
            list.SelectedButtonId = 1;
            lock (_sync) PushOpen(list);
            Changed?.Invoke();
            _ = LoadAuctionListAsync(list);
        }

        private async Task LoadAuctionListAsync(StockUiOpenMenu list)
        {
            if (AuctionListRequested is not { } request)
            {
                NoticePosted?.Invoke("The Auction House list is not available in this session.");
                return;
            }
            IReadOnlyList<AuctionListItem>? items = null;
            try
            {
                items = await request(list.AuctionCategory).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                GordianLog.Warning("AUCTION", $"Category {list.AuctionCategory} list failed: {ex.Message}");
            }
            lock (_sync)
            {
                list.AuctionLoading = false;
                FillAuctionRows(list, StockUiAuction.ListRows(items ?? Array.Empty<AuctionListItem>(), ItemLookup));
            }
            if (items == null) NoticePosted?.Invoke(StockUiAuction.UnavailableMessage);
            Changed?.Invoke();
        }

        /// <summary>Puts rows into a list (under the lock), keeping the cursor on a row that exists.</summary>
        private static void FillAuctionRows(StockUiOpenMenu menu, List<StockUiAuctionRow> rows)
        {
            var listRows = new List<StockUiListRow>(rows.Count);
            foreach (var row in rows) listRows.Add(new StockUiListRow(listRows.Count + 1, row.Name, false));
            menu.AuctionRows = rows;
            menu.Rows = listRows;
            ClampRows(menu);
        }

        private static void ClampRows(StockUiOpenMenu menu)
        {
            int maxFirst = Math.Max(0, menu.Rows.Count - menu.VisibleRows);
            if (menu.FirstRow > maxFirst)
            {
                menu.FirstRow = maxFirst;
                menu.ScrollFrom = maxFirst;
            }
            int lastRow = Math.Max(1, Math.Min(menu.VisibleRows, menu.Rows.Count - menu.FirstRow));
            if (menu.SelectedButtonId < 1 || menu.SelectedButtonId > lastRow) menu.SelectedButtonId = Math.Min(Math.Max(1, menu.SelectedButtonId), lastRow);
        }

        /// <summary>After a purchase the category lists are asked for again (their counts changed; PROVISIONAL).</summary>
        private void RefreshAuctionLists()
        {
            foreach (var menu in _open)
            {
                if (menu.AuctionView == StockUiAuctionView.List) _ = LoadAuctionListAsync(menu);
            }
        }

        /// <summary>Confirm on a list row: <c>auc3</c> (Price History, Bid, Sort) over a category list, <c>auc4</c> over a listing.</summary>
        private void ActivateAuctionRow(StockUiOpenMenu list)
        {
            if (list.SelectedAuctionRow is not { } row) return;
            var library = _library;
            string name = list.AuctionView == StockUiAuctionView.Sales ? StockUiAuction.SaleMenu : StockUiAuction.ItemMenu;
            if (library == null || !library.TryGetMenu(name, out var definition)) return;
            var menu = new StockUiOpenMenu(definition, list, Array.Empty<string>(), null) { AuctionItem = row };
            menu.SelectedButtonId = FirstSelectable(definition);
            lock (_sync) PushOpen(menu);
            Changed?.Invoke();
        }

        /// <summary>Price History: <c>auchisto</c> with the item's last ten sales (singles or stacks, as the row is).</summary>
        private void OpenPriceHistory(StockUiOpenMenu parent, StockUiAuctionRow item)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiAuction.HistoryMenu, out var definition)) return;
            var history = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null, VisibleRowsOf(definition))
            {
                AuctionView = StockUiAuctionView.History,
                AuctionItem = item,
                ItemLookup = ItemLookup,
            };
            history.AuctionLoading = true;
            history.SelectedButtonId = 1;
            lock (_sync) PushOpen(history);
            Changed?.Invoke();
            _ = LoadHistoryAsync(history);
        }

        private async Task LoadHistoryAsync(StockUiOpenMenu history)
        {
            IReadOnlyList<AuctionHistoryEntry>? sales = null;
            if (AuctionHistoryRequested is { } request)
            {
                try
                {
                    sales = await request(history.AuctionItem.ItemId, history.AuctionItem.Stack).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    GordianLog.Warning("AUCTION", $"Price history of item {history.AuctionItem.ItemId} failed: {ex.Message}");
                }
            }
            lock (_sync)
            {
                history.AuctionLoading = false;
                var rows = StockUiAuction.HistoryRows(sales ?? Array.Empty<AuctionHistoryEntry>());
                var listRows = new List<StockUiListRow>(rows.Count);
                foreach (var row in rows) listRows.Add(new StockUiListRow(listRows.Count + 1, row.Seller, false));
                history.AuctionHistory = rows;
                history.Rows = listRows;
                ClampRows(history);
            }
            if (sales == null) NoticePosted?.Invoke(StockUiAuction.UnavailableMessage);
            Changed?.Invoke();
        }

        /// <summary>Sell: the inventory in the item list ("Select the merchandise you wish to place on auction.").</summary>
        private void OpenAuctionSellList(StockUiOpenMenu parent)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiItems.ListMenu, out var definition)) return;
            var list = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null, VisibleRowsOf(definition))
            {
                ItemListKind = StockUiItemListKind.AuctionSell,
                ItemContainer = ContainerId.Inventory,
                ItemLookup = ItemLookup,
                ItemOwner = this,
            };
            list.AuctionHelpId = StockUiAuction.HelpSelectSellItem;
            lock (_sync)
            {
                FillItemRows(list);
                list.SelectedButtonId = 1;
                PushOpen(list);
            }
            Changed?.Invoke();
        }

        /// <summary>The Sell list's rows: the inventory, worn and NoAuction items greyed.</summary>
        private List<StockUiItemRow> AuctionSellRows()
        {
            var rows = OrderedRows(ContainerId.Inventory);
            for (int i = 0; i < rows.Count; i++) rows[i] = StockUiAuction.SellRow(rows[i], ItemLookup);
            return rows;
        }

        /// <summary>
        /// Confirm on an item to sell: a full stack of a stackable item asks whether the stack goes on auction (PROVISIONAL: the
        /// capture's log names the rule, not the question), then the price.
        /// </summary>
        private void PickAuctionSale(StockUiOpenMenu list, StockUiItemRow row)
        {
            if (row.Greyed)
            {
                NoticePosted?.Invoke(row.GreyedReason ?? $"{row.Name} cannot be placed on auction.");
                return;
            }
            var item = new StockUiAuctionRow(row.ItemId, row.Name, false, row.StackSize, 0, InventorySlot: row.Slot);
            if (row.StackSize > 1 && row.Count >= row.StackSize)
            {
                OpenYesNo("Place the whole stack on auction?", defaultYes: true, answer =>
                {
                    if (answer is { } stack) OpenPriceSet(list, item with { Stack = stack }, StockUiAuctionPriceFor.Sell);
                });
                return;
            }
            OpenPriceSet(list, item, StockUiAuctionPriceFor.Sell);
        }

        /// <summary>
        /// Price Set (<c>moneyctr</c>, title 88, help 352 / 55 / 58): the price is typed in the shared text field (digits); for the
        /// gamepad the frame's arrows work as drawn: + / - (Up / Down) step it (<see cref="StockUiAuction.PriceStep"/>), "All" (Left)
        /// puts in all the gil in hand for a bid, "0" (Right) clears it. Confirm keeps it, Cancel closes the window.
        /// </summary>
        private void OpenPriceSet(StockUiOpenMenu parent, StockUiAuctionRow item, StockUiAuctionPriceFor purpose, uint price = 0)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiAuction.PriceMenu, out var definition)) return;
            var menu = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null)
            {
                AuctionView = StockUiAuctionView.Price,
                AuctionItem = item,
                PriceFor = purpose,
                ItemLookup = ItemLookup,
                MacroField = MacroField,
            };
            menu.AuctionHelpId = purpose == StockUiAuctionPriceFor.Bid ? StockUiAuction.HelpSpecifyBid : StockUiAuction.HelpSetReserve;
            if (MacroField.IsOpen) MacroField.Commit();
            lock (_sync)
            {
                PushOpen(menu);
                _priceMenu = menu;
                _priceCommitted = false;
                // The list's item info gives way to the Price Set window at the bottom (as the shop's does to its count box).
                if (ListUnder(menu) is { } list) list.ShowsQuantity = true;
            }
            OpenPriceField(menu, price);
            Changed?.Invoke();
        }

        private void OpenPriceField(StockUiOpenMenu menu, uint price)
        {
            MacroField.Open(price == 0 ? string.Empty : price.ToString(System.Globalization.CultureInfo.InvariantCulture), 9, (text, _) =>
            {
                _priceCommitted = true;
                ConfirmPrice(menu, StockUiAuction.ParsePrice(text));
            }, escapeKeepsText: false, verticalMoves: false);
        }

        /// <summary>
        /// The Price Set window's input while it is open: Confirm keeps the price, Cancel closes the window, Up / Down step the
        /// price, Left / Right are the "All" and "0" arrows; a field the keyboard closed (Escape) closes the window on the next tick. Returns
        /// true while the window has the input.
        /// </summary>
        private bool ProcessAuctionPrice(InputState input)
        {
            if (_auctionOpen && !Array.Exists(_open, m => m.Name.Equals(StockUiAuction.MainMenu, StringComparison.OrdinalIgnoreCase)))
            {
                // Cancel on the Auction window (or any close of it) ends the Auction House; the next MOPEN opens it again.
                _auctionOpen = false;
                _pendingSale = null;
                AuctionHouse?.Invoke()?.Close();
            }
            var menu = _priceMenu;
            if (menu == null) return false;
            if (Array.IndexOf(_open, menu) < 0)
            {
                _priceMenu = null;
                _pendingSale = null;
                if (MacroField.IsOpen) MacroField.Cancel();
                return false;
            }
            if (!MacroField.IsOpen)
            {
                if (!_priceCommitted) ClosePriceSet(menu);
                return false;
            }
            if (!ReferenceEquals(Top, menu)) return false;
            if (input.WasActionTriggered(InputAction.Confirm)) MacroField.Commit();
            else if (input.WasActionTriggered(InputAction.Cancel)) ClosePriceSet(menu);
            else if (input.WasActionTriggered(InputAction.MenuUp) || input.WasActionTriggered(InputAction.MenuDown))
            {
                uint price = StockUiAuction.ParsePrice(MacroField.Text);
                uint step = StockUiAuction.PriceStep(price);
                price = input.WasActionTriggered(InputAction.MenuUp)
                    ? (uint)Math.Min(StockUiAuction.MaxPrice, (ulong)price + step)
                    : price > step ? price - step : 1;
                MacroField.Cancel();
                OpenPriceField(menu, price);
                Changed?.Invoke();
            }
            else if (input.WasActionTriggered(InputAction.MenuLeft) || input.WasActionTriggered(InputAction.MenuRight))
            {
                // The frame's "All" arrow (left) and "0" arrow (right): all the gil in hand for a bid (a sale keeps its price), or none.
                uint price = input.WasActionTriggered(InputAction.MenuRight) ? 0
                    : menu.PriceFor == StockUiAuctionPriceFor.Bid ? Math.Min(Gil, StockUiAuction.MaxPrice) : StockUiAuction.ParsePrice(MacroField.Text);
                MacroField.Cancel();
                OpenPriceField(menu, price);
                Changed?.Invoke();
            }
            return true;
        }

        private void ClosePriceSet(StockUiOpenMenu menu)
        {
            if (MacroField.IsOpen) MacroField.Cancel();
            _priceMenu = null;
            if (ListUnder(menu) is { } list) list.ShowsQuantity = false;
            CloseMenu(menu);
        }

        /// <summary>The Auction House list a window was opened over, if any.</summary>
        private static StockUiOpenMenu? ListUnder(StockUiOpenMenu menu)
        {
            for (var at = menu.Parent; at != null; at = at.Parent)
            {
                if (at.IsAuctionList) return at;
            }
            return null;
        }

        /// <summary>A price was kept: a bid asks "Place this bid?" (<c>aucyn</c>), a sale asks the server for the fee (AskCommit).</summary>
        private void ConfirmPrice(StockUiOpenMenu menu, uint price)
        {
            var item = menu.AuctionItem;
            if (price == 0)
            {
                // Nothing typed: the field opens again (the server takes 1 to 999,999,999).
                _priceCommitted = false;
                OpenPriceField(menu, 0);
                return;
            }
            if (menu.PriceFor == StockUiAuctionPriceFor.Bid)
            {
                if (Gil < price)
                {
                    _priceCommitted = false;
                    NoticePosted?.Invoke("You do not have enough gil.");
                    OpenPriceField(menu, price);
                    return;
                }
                AskAuctionYesNo(StockUiAuction.HelpPlaceBid, yes =>
                {
                    ClosePriceSet(menu);
                    if (!yes) return;
                    if (AuctionBid is not { } bid)
                    {
                        NoticePosted?.Invoke("Bidding is not available in this session.");
                        return;
                    }
                    sbyte slot = Math.Max((sbyte)0, AuctionHouse?.Invoke()?.NextFreeSlot ?? 0);
                    GordianLog.Info("AUCTION", $"Bidding {price} gil on {(item.Stack ? "a stack of " : string.Empty)}{item.Name} (item {item.ItemId}).");
                    _ = SendAsync(() => bid(item.ItemId, price, item.Stack, slot), "Auction bid");
                });
                return;
            }
            if (AuctionHouse?.Invoke()?.NextFreeSlot is < 0)
            {
                ClosePriceSet(menu);
                NoticePosted?.Invoke(StockUiAuction.SevenItemsMessage);
                return;
            }
            if (AuctionAskCommit is not { } ask)
            {
                ClosePriceSet(menu);
                NoticePosted?.Invoke("Selling is not available in this session.");
                return;
            }
            _pendingSale = (item, price, item.Stack);
            GordianLog.Info("AUCTION", $"Asking the fee of selling {(item.Stack ? "a stack of " : string.Empty)}{item.Name} (slot {item.InventorySlot}) for {price} gil.");
            _ = SendAsync(() => ask(item.InventorySlot, item.ItemId, price, item.Stack), "Auction fee");
        }

        /// <summary>The fee came (AskCommit): the log shows it and <c>aucyn</c> asks "Place this merchandise on auction?"; Yes sends LotIn.</summary>
        private void OnFeeQuoted(AuctionResponse response)
        {
            if (_pendingSale is not { } sale) return;
            if (response.ResultCode != AuctionResultCode.Success)
            {
                _pendingSale = null;
                if (_priceMenu is { } failed) ClosePriceSet(failed);
                return;
            }
            NoticePosted?.Invoke(StockUiAuction.FeeMessage(response.Price));
            AskAuctionYesNo(StockUiAuction.HelpPlaceOnAuction, yes =>
            {
                _pendingSale = null;
                if (_priceMenu is { } menu) ClosePriceSet(menu);
                if (!yes) return;
                sbyte slot = AuctionHouse?.Invoke()?.NextFreeSlot ?? 0;
                if (slot < 0)
                {
                    NoticePosted?.Invoke(StockUiAuction.SevenItemsMessage);
                    return;
                }
                if (Gil < response.Price)
                {
                    NoticePosted?.Invoke(MenuText(StockUiAuction.MessageNoFeeGil, "You do not have enough gil to pay the transaction fee."));
                    return;
                }
                if (AuctionListForSale is not { } list) return;
                GordianLog.Info("AUCTION", $"Placing {sale.Item.Name} (slot {sale.Item.InventorySlot}) on auction in sale slot {slot} for {sale.Price} gil.");
                _ = SendAsync(() => list(slot, sale.Item.InventorySlot, sale.Price, sale.Stack), "Auction listing");
            });
        }

        /// <summary>After a listing the sell windows close back to the Auction menu (PROVISIONAL).</summary>
        private void CloseSellFlow()
        {
            StockUiOpenMenu? sellList = null;
            foreach (var menu in _open)
            {
                if (menu.ItemListKind == StockUiItemListKind.AuctionSell) sellList = menu;
            }
            if (_priceMenu is { } price) ClosePriceSet(price);
            if (sellList != null)
            {
                lock (_sync) FillItemRows(sellList);
                Changed?.Invoke();
            }
        }

        /// <summary>Sales Status: C2S Info and the listings in <c>auclist</c>, filled as the slot replies come.</summary>
        private void OpenSalesStatus(StockUiOpenMenu parent)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiAuction.ListMenu, out var definition)) return;
            var list = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null, VisibleRowsOf(definition))
            {
                AuctionView = StockUiAuctionView.Sales,
                AuctionTitle = StockUiAuction.AuctionTitle,
                ItemLookup = ItemLookup,
            };
            list.SelectedButtonId = 1;
            lock (_sync)
            {
                FillAuctionRows(list, StockUiAuction.SalesRows(AuctionHouse?.Invoke()?.SnapshotSales() ?? Array.Empty<AuctionSale?>(), ItemLookup));
                PushOpen(list);
            }
            Changed?.Invoke();
            if (AuctionSalesRequested is { } request) _ = SendAsync(request, "Auction sales status");
        }

        /// <summary>Cancel sale: "Remove merchandise from auction?" (ROM/165/72 #58), then LotCancel.</summary>
        private void AskCancelSale(StockUiOpenMenu saleMenu, StockUiAuctionRow sale)
        {
            OpenYesNo(MenuText(StockUiAuction.MessageRemovePrompt, "Remove merchandise from auction?"), defaultYes: false, answer =>
            {
                if (answer != true) return;
                CloseMenu(saleMenu);
                if (AuctionCancelSale is not { } cancel || sale.SaleSlot < 0) return;
                GordianLog.Info("AUCTION", $"Removing {sale.Name} from auction (sale slot {sale.SaleSlot}).");
                _ = SendAsync(() => cancel(sale.SaleSlot), "Auction cancel");
            });
        }

        /// <summary>The Auction House's own Yes / No window (<c>aucyn</c>, top right) with a help line; cancelling answers No.</summary>
        private void AskAuctionYesNo(int helpId, Action<bool> answered)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            StockUiOpenMenu? prompt;
            lock (_sync)
            {
                prompt = Push(StockUiAuction.YesNoMenu, Top, Array.Empty<string>(), null, tcs);
                if (prompt != null)
                {
                    prompt.SelectedButtonId = 1;
                    prompt.AuctionHelpId = helpId;
                }
            }
            if (prompt == null)
            {
                answered(false);
                return;
            }
            _ = tcs.Task.ContinueWith(t => answered(!prompt.Cancelled && t.Result), TaskScheduler.Default);
            Changed?.Invoke();
        }
    }
}
