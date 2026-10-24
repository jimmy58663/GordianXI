// src/Gordian.Core/Ui/StockUiMenuController.Guild.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;

namespace Gordian.Engine.Ui
{
    /// <summary>
    /// The guild shop window (#330): the English menu DAT's <c>guildsho</c> (bottom-left 16,256, 112 x 40, frames #20: Buy
    /// windowps #149, Sell #150), the same place and size as the NPC shop's <c>shopmain</c>; the lists use the shop's
    /// <c>shop</c> frame, the count prompt its <c>itemctrl</c>. Packets (XiPackets world/client/0x00AA-0x00AD and
    /// world/server/0x0082-0x0086; LandSandBoat c2s/0x0aa_guild_buy ... and s2c/0x082_guild_buy ...): talking to a guild
    /// merchant inside its hours sends S2C 0x086 (open); Buy asks for the stock with C2S 0x0AB (answer S2C 0x083: item,
    /// price, stock, maximum) and buys with 0x0AA (item, count; answer 0x082); Sell asks with 0x0AD what the guild takes
    /// (answer 0x085) and sells one inventory stack with 0x0AC (item, slot, count; answer 0x084). PROVISIONAL (no retail
    /// capture of the Buy list): its columns (here the item and its price, as the NPC shop; the stock is not drawn), and that
    /// the list is asked for again after each purchase or sale to show the new stock.
    /// <para>
    /// The Sell list (the maintainer's retail capture, round 1 image-5, 2026-10-18; help bar "Guild Shop"): "Select from your
    /// inventory." as the first row, then every item the guild buys with its price right-aligned ("Dhalmel Hide 300 G",
    /// "Manticore Hide 1,140 G"), whether the character holds it or not, in the guild's order, under it the "Current Gil"
    /// window and the item info. The item rows are drawn lavender (greyed); here those the character holds and the guild still
    /// takes are drawn white and sell from their largest stack on Confirm (PROVISIONAL), and the first row opens the inventory's
    /// items the guild buys (PROVISIONAL: what retail's first row opens is not captured).
    /// </para>
    /// </summary>
    public static class StockUiGuildShop
    {
        public const string MenuName = "guildsho";
        public const int BuyButton = 1, SellButton = 2;

        /// <summary>Help text 51 (ROM/165/75): "Select an item from the guild's buy list."</summary>
        public const int HelpBuyList = 51;

        /// <summary>The Sell list's first row (image-5); Confirm opens the inventory's items the guild buys.</summary>
        public const string SelectFromInventoryText = "Select from your inventory.";

        /// <summary>The Buy list: the guild's stock in its order; rows out of stock greyed.</summary>
        public static List<StockUiShopRow> BuyRows(IReadOnlyList<GuildItemEntry> items, Func<ushort, Gordian.Core.Resources.Models.ItemRecord?>? lookup)
        {
            var rows = new List<StockUiShopRow>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.ItemId == 0) continue;
                var record = StockUiShop.Lookup(lookup, item.ItemId);
                rows.Add(new StockUiShopRow(item.ItemId, record.Name, (uint)Math.Max(0, item.Price), item.Stock, i, 0,
                    Math.Max(1, (int)record.StackSize), Greyed: item.Stock == 0));
            }
            return rows;
        }

        /// <summary>
        /// The Sell list (image-5): "Select from your inventory." (item 0, shop index -1), then every item of the guild's list
        /// with the guild's price and the largest stack the inventory holds of it (C2S 0x0AC sells from one slot); items not
        /// held, or whose guild stock is full, greyed.
        /// </summary>
        public static List<StockUiShopRow> SellRows(IReadOnlyList<GuildItemEntry> items, InventoryState? inventory, Func<ushort, Gordian.Core.Resources.Models.ItemRecord?>? lookup)
        {
            var rows = new List<StockUiShopRow> { new(0, SelectFromInventoryText, 0, 0, -1, 0, 1) };
            var held = inventory?.SnapshotItems(ContainerId.Inventory) ?? Array.Empty<InventoryItem>();
            for (int i = 0; i < items.Count; i++)
            {
                var entry = items[i];
                if (entry.ItemId == 0) continue;
                byte slot = 0;
                uint count = 0;
                foreach (var item in held)
                {
                    if (item.Slot == 0 || item.ItemId != entry.ItemId || item.Count <= count) continue;
                    slot = item.Slot;
                    count = item.Count;
                }
                var record = StockUiShop.Lookup(lookup, entry.ItemId);
                rows.Add(new StockUiShopRow(entry.ItemId, record.Name, (uint)Math.Max(0, entry.Price), count, i, slot,
                    Math.Max(1, (int)record.StackSize), Greyed: slot == 0 || IsFull(entry)));
            }
            return rows;
        }

        /// <summary>Whether the guild's stock of an item it buys is full (it takes no more).</summary>
        public static bool IsFull(GuildItemEntry entry) => entry.Max > 0 && entry.Stock >= entry.Max;

        /// <summary>
        /// The inventory's items the guild buys (the Sell list's "Select from your inventory."), in slot order; those whose guild
        /// stock is full greyed.
        /// </summary>
        public static List<StockUiItemRow> InventoryRows(IReadOnlyList<GuildItemEntry> items, List<StockUiItemRow> inventoryRows)
        {
            var rows = new List<StockUiItemRow>();
            foreach (var row in inventoryRows)
            {
                GuildItemEntry? accepted = null;
                foreach (var entry in items) if (entry.ItemId == row.ItemId) accepted = entry;
                if (accepted is not { } a || row.Equipped) continue;
                rows.Add(IsFull(a) ? row with { Greyed = true, GreyedReason = "The guild would not buy that from you. Its stock of that item is full." } : row);
            }
            return rows;
        }

        /// <summary>The most of a Buy row the character can take at once: the stack, the guild's stock and the gil in hand (at least 1).</summary>
        public static uint MaxBuyCount(in StockUiShopRow row, uint gil)
        {
            uint byGil = row.Price == 0 ? (uint)row.StackSize : gil / row.Price;
            return Math.Max(1, Math.Min(Math.Min((uint)row.StackSize, row.Count), Math.Min(byGil, 99u)));
        }
    }

    public sealed partial class StockUiOpenMenu
    {
        /// <summary>A guild shop list or count prompt (rather than the NPC shop's).</summary>
        public bool IsGuild { get; init; }
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>C2S 0x0AB: asks for the guild's stock.</summary>
        public Func<Task>? GuildRequestBuyList { get; set; }

        /// <summary>C2S 0x0AD: asks what the guild buys.</summary>
        public Func<Task>? GuildRequestSellList { get; set; }

        /// <summary>C2S 0x0AA: buys (item id, count).</summary>
        public Func<ushort, byte, Task>? GuildBuy { get; set; }

        /// <summary>C2S 0x0AC: sells (item id, inventory slot, count).</summary>
        public Func<ushort, byte, byte, Task>? GuildSell { get; set; }

        private volatile bool _guildOpen;

        /// <summary>Whether the guild shop window is open.</summary>
        public bool IsGuildShopOpen => _guildOpen;

        /// <summary>
        /// The guild's status arrived (S2C 0x086): an open guild opens the Buy / Sell window as the root, closing whatever menus
        /// were open, as the NPC shop does; a closed guild only prints its hours (the log line).
        /// </summary>
        public void OnGuildStatus(ShopOpenStatus status)
        {
            if (status != ShopOpenStatus.Open) return;
            StockUiOpenMenu[] closed;
            StockUiOpenMenu? root;
            lock (_sync)
            {
                closed = _open;
                foreach (var menu in closed) Remember(menu);
                _open = Array.Empty<StockUiOpenMenu>();
                root = Push(StockUiGuildShop.MenuName, null, Array.Empty<string>(), null, null);
                _guildOpen = root != null;
            }
            foreach (var menu in closed)
            {
                menu.Cancelled = true;
                menu.Prompt?.TrySetResult(false);
                menu.QueryCompleted?.Invoke(255);
            }
            if (root != null) Changed?.Invoke();
        }

        /// <summary>Ends the guild shop (a zone change): its windows close; there is no packet for it.</summary>
        public void CloseGuildShop()
        {
            if (!_guildOpen) return;
            _guildOpen = false;
            StockUiOpenMenu? root = null;
            foreach (var menu in _open)
            {
                if (menu.Name.Equals(StockUiGuildShop.MenuName, StringComparison.OrdinalIgnoreCase)) root = menu;
            }
            if (root != null) CloseMenu(root);
        }

        /// <summary>Buy / Sell: asks the server for the list and opens it (it fills when the answer arrives).</summary>
        private void OpenGuildList(StockUiShopSide side, StockUiOpenMenu parent)
        {
            var request = side == StockUiShopSide.Buy ? GuildRequestBuyList : GuildRequestSellList;
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiShop.ListMenu, out var definition)) return;
            var list = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null, VisibleRowsOf(definition))
            {
                ShopSide = side,
                IsGuild = true,
                ItemLookup = ItemLookup,
            };
            lock (_sync)
            {
                FillGuildRows(list);
                list.SelectedButtonId = 1;
                PushOpen(list);
            }
            Changed?.Invoke();
            if (request == null)
            {
                NoticePosted?.Invoke("The guild shop is not available in this session.");
                return;
            }
            GordianLog.Info("GUILD", side == StockUiShopSide.Buy ? "Asking for the guild's stock." : "Asking what the guild buys.");
            _ = SendAsync(request, "Guild list");
        }

        /// <summary>Rebuilds a guild list's rows (under the lock), keeping the cursor on a row that exists.</summary>
        private void FillGuildRows(StockUiOpenMenu menu)
        {
            var inventory = Inventory;
            var shopRows = menu.ShopSide == StockUiShopSide.Buy
                ? StockUiGuildShop.BuyRows(inventory?.SnapshotGuildBuyList() ?? Array.Empty<GuildItemEntry>(), ItemLookup)
                : StockUiGuildShop.SellRows(inventory?.SnapshotGuildSellList() ?? Array.Empty<GuildItemEntry>(), inventory, ItemLookup);
            var rows = new List<StockUiListRow>(shopRows.Count);
            foreach (var row in shopRows) rows.Add(new StockUiListRow(rows.Count + 1, row.Name, false));
            menu.ShopRows = shopRows;
            menu.Rows = rows;
            menu.Gil = Gil;
            int maxFirst = Math.Max(0, rows.Count - menu.VisibleRows);
            if (menu.FirstRow > maxFirst)
            {
                menu.FirstRow = maxFirst;
                menu.ScrollFrom = maxFirst;
            }
            int lastRow = Math.Max(1, Math.Min(menu.VisibleRows, rows.Count - menu.FirstRow));
            if (menu.SelectedButtonId < 1 || menu.SelectedButtonId > lastRow) menu.SelectedButtonId = lastRow;
        }

        /// <summary>A guild list arrived (S2C 0x083 / 0x085 complete) or the items changed: open guild lists take the new rows.</summary>
        public void OnGuildListChanged()
        {
            if (!_guildOpen) return;
            bool changed = false;
            lock (_sync)
            {
                foreach (var menu in _open)
                {
                    if (menu.IsGuild && menu.IsShopList)
                    {
                        FillGuildRows(menu);
                        changed = true;
                    }
                    else if (menu.ItemListKind == StockUiItemListKind.GuildSell)
                    {
                        FillItemRows(menu);
                        changed = true;
                    }
                    else if (menu.IsGuild && menu.IsQuantity)
                    {
                        menu.Gil = Gil;
                    }
                }
            }
            if (changed) Changed?.Invoke();
        }

        /// <summary>A purchase or sale was answered (S2C 0x082 / 0x084): the open list is asked for again to show the new stock (PROVISIONAL).</summary>
        public void OnGuildTransaction()
        {
            if (!_guildOpen) return;
            StockUiShopSide? side = null;
            foreach (var menu in _open)
            {
                if (menu.IsGuild && menu.IsShopList) side = menu.ShopSide;
            }
            var request = side == StockUiShopSide.Buy ? GuildRequestBuyList : side == StockUiShopSide.Sell ? GuildRequestSellList : null;
            if (request != null) _ = SendAsync(request, "Guild list");
        }

        /// <summary>Confirm on a guild row: the count prompt (Buy: the stack, the stock and the gil; Sell: the stack held).</summary>
        private void ActivateGuildRow(StockUiOpenMenu list)
        {
            string? notice = null;
            bool changed = false;
            lock (_sync)
            {
                if (!ReferenceEquals(Top, list) || list.SelectedShopRow is not { } row) return;
                if (list.ShopSide == StockUiShopSide.Buy)
                {
                    if (row.Count == 0) notice = $"The guild is out of {row.Name}.";
                    else if (Gil < row.Price) notice = "You do not have enough gil.";
                    else changed = OpenGuildQuantity(list, row, StockUiGuildShop.MaxBuyCount(row, Gil)) != null;
                }
                else if (row.ShopIndex < 0)
                {
                    changed = OpenGuildInventory(list) != null;
                }
                else if (row.Greyed)
                {
                    notice = row.Slot == 0 ? $"You do not have any {StockUiShop.PluralName(StockUiShop.Lookup(ItemLookup, row.ItemId))}."
                        : "The guild would not buy that from you. Its stock of that item is full.";
                }
                else
                {
                    changed = OpenGuildQuantity(list, row, Math.Max(1, Math.Min(row.Count, 99u))) != null;
                }
            }
            if (notice != null) NoticePosted?.Invoke(notice);
            if (changed) Changed?.Invoke();
        }

        /// <summary>"Select from your inventory.": the inventory's items the guild buys, in the item list (under the lock).</summary>
        private StockUiOpenMenu? OpenGuildInventory(StockUiOpenMenu sellList)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiItems.ListMenu, out var definition)) return null;
            var list = new StockUiOpenMenu(definition, sellList, Array.Empty<string>(), null, VisibleRowsOf(definition))
            {
                ItemListKind = StockUiItemListKind.GuildSell,
                ItemContainer = ContainerId.Inventory,
                ItemLookup = ItemLookup,
                ItemOwner = this,
                IsGuild = true,
                ShopSide = null,
            };
            FillItemRows(list);
            list.SelectedButtonId = 1;
            PushOpen(list);
            return list;
        }

        /// <summary>Confirm on an inventory item to sell to the guild: the count prompt, at most what the slot holds.</summary>
        private void PickGuildSale(StockUiOpenMenu list, StockUiItemRow row)
        {
            if (row.Greyed)
            {
                NoticePosted?.Invoke(row.GreyedReason ?? $"{row.Name} cannot be sold.");
                return;
            }
            uint price = 0;
            foreach (var entry in Inventory?.SnapshotGuildSellList() ?? Array.Empty<GuildItemEntry>())
            {
                if (entry.ItemId == row.ItemId) price = (uint)Math.Max(0, entry.Price);
            }
            var shopRow = new StockUiShopRow(row.ItemId, row.Name, price, row.Count, 0, row.Slot, row.StackSize);
            bool opened;
            lock (_sync) opened = OpenGuildQuantity(list, shopRow, Math.Max(1, Math.Min(row.Count, 99u)), StockUiShopSide.Sell) != null;
            if (opened) Changed?.Invoke();
        }

        private StockUiOpenMenu? OpenGuildQuantity(StockUiOpenMenu list, in StockUiShopRow row, uint max, StockUiShopSide? side = null)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiShop.QuantityMenu, out var definition)) return null;
            var menu = new StockUiOpenMenu(definition, list, Array.Empty<string>(), null)
            {
                IsQuantity = true,
                IsGuild = true,
                ShopSide = side ?? list.ShopSide,
                QuantityRow = row,
                QuantityMax = Math.Max(1, max),
                QuantityTotal = (side ?? list.ShopSide) == StockUiShopSide.Buy ? (uint)Math.Max(1, row.StackSize) : Math.Max(1, row.Count),
                UnitPrice = row.Price,
                ItemLookup = ItemLookup,
            };
            menu.Quantity = 1;
            menu.Gil = Gil;
            menu.SelectedButtonId = StockUiShop.QuantityField;
            list.ShowsQuantity = true;
            PushOpen(menu);
            return menu;
        }

        /// <summary>Confirm on the guild count prompt: C2S 0x0AA (buy) or 0x0AC (sell).</summary>
        private void RunGuildQuantity(StockUiOpenMenu prompt)
        {
            byte count = (byte)Math.Clamp(prompt.Quantity, 1u, 99u);
            var row = prompt.QuantityRow;
            lock (_sync)
            {
                if (ReferenceEquals(Top, prompt)) _open = _open[..^1];
                if (prompt.Parent is { } list) list.ShowsQuantity = false;
            }
            Changed?.Invoke();
            if (prompt.ShopSide == StockUiShopSide.Buy)
            {
                if (Gil < count * prompt.UnitPrice)
                {
                    NoticePosted?.Invoke("You do not have enough gil.");
                    return;
                }
                if (GuildBuy is not { } buy)
                {
                    NoticePosted?.Invoke("Buying is not available in this session.");
                    return;
                }
                GordianLog.Info("GUILD", $"Buying {count} x {row.Name} (item {row.ItemId}) from the guild at {row.Price} gil each.");
                _ = SendAsync(() => buy(row.ItemId, count), "Guild purchase");
                return;
            }
            if (GuildSell is not { } sell)
            {
                NoticePosted?.Invoke("Selling is not available in this session.");
                return;
            }
            GordianLog.Info("GUILD", $"Selling {count} x {row.Name} (item {row.ItemId}, slot {row.Slot}) to the guild at {row.Price} gil each.");
            _ = SendAsync(() => sell(row.ItemId, row.Slot, count), "Guild sale");
        }
    }
}
