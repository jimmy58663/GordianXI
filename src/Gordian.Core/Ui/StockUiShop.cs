// src/Gordian.Core/Ui/StockUiShop.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Core.World;

namespace Gordian.Core.Ui
{
    /// <summary>Which of the shop's two lists a window shows.</summary>
    public enum StockUiShopSide
    {
        Buy,
        Sell,
    }

    /// <summary>
    /// One row of the shop's Buy or Sell list: the item, its unit price (the shop's price on the Buy list; the
    /// appraisal, once known, on the Sell list), how many are held (Sell list), and where it lives: the shop slot
    /// (<see cref="ShopIndex"/>, sent in 0x083) or the inventory slot (<see cref="Slot"/>, sent in 0x084).
    /// <see cref="Greyed"/> rows cannot be sold (the item's NoSale flag); Confirm on them posts why.
    /// </summary>
    public readonly record struct StockUiShopRow(ushort ItemId, string Name, uint Price, uint Count, int ShopIndex, byte Slot,
        int StackSize, bool Greyed = false);

    /// <summary>
    /// The NPC shop (Tier 2 chunk 6c): what the DAT windows are, how the packets map to them, and the row lists.
    /// <para>
    /// Retail opens the small "shopmain" window (bottom-left, 16,256, 112 x 40: Buy / Sell) when the server's 0x03E
    /// arrives after talking to a merchant; "shop" (16,48, 256 x 190) is the list window, ten 132 x 16 rows at an 18 px
    /// pitch, each with a 16 x 16 icon slot at x 3, its cursor the "anc_shop" group (the gold arrow plus a highlight
    /// bar over the row). "itemctrl" (16,240, 112 x 56) is the quantity control ("All" and "1" arrows either side of
    /// the number, + above and - below); "money" (16,240) is the "Current Gil" window and "iteminfo" (16,240,
    /// 366 x 56) the selected item's description with its 32 x 32 icon. Where retail puts the last three around the
    /// list is not captured yet (the issue's recording shows the Buy/Sell window and the list only): here the gil
    /// window sits under the list and the item info beside it, and the quantity control replaces the gil window.
    /// </para>
    /// <para>
    /// Packets (the maintainer's retail capture, 2026-09-28, buying and selling Ronfaure Chestnuts, item 639):
    /// S2C 0x03E (ShopListNum) opens, 0x03C lists 12-byte entries (price, item id, shop slot; flags 0x89 on the last
    /// packet), 0x052 mode 0 follows and no event runs. Buying sends C2S 0x083 (count, ShopNo, shop slot): the
    /// capture's ShopNo bytes are 00 04 for shop list 4, so it is sent as ShopListNum &lt;&lt; 8 (LandSandBoat ignores
    /// it); the server answers with 0x01E gil, 0x020 the item and 0x03F (slot, state, count). Selling sends 0x084
    /// (count 1, item id, inventory slot) for the appraisal, answered by 0x03D (unit price, slot); confirming sends
    /// 0x084 again with the chosen count and 0x085 (SellFlag 1), answered by 0x03D, 0x020, 0x01E and the 0x009
    /// standard message 232 ("You sell ..."). Protocol referenced from LandSandBoat
    /// (https://github.com/LandSandBoat/server, packets 0x03c-0x03f and 0x083-0x085).
    /// </para>
    /// </summary>
    public static class StockUiShop
    {
        /// <summary>The Buy / Sell window (its "shopbuy"/"shopsell" siblings are the same frame with Cancel).</summary>
        public const string MenuName = "shopmain";
        public const int BuyButton = 1, SellButton = 2;

        /// <summary>The item list window: ten rows with icon slots.</summary>
        public const string ListMenu = "shop";
        public const int ListRows = 10;

        /// <summary>The quantity control: the number field is button 1, "All" 2, "1" 3, - 4, + 5.</summary>
        public const string QuantityMenu = "itemctrl";
        public const int QuantityField = 1, QuantityAllButton = 2, QuantityOneButton = 3, QuantityDownButton = 4, QuantityUpButton = 5;

        /// <summary>The "Current Gil" window drawn under the list, and the item description window beside it.</summary>
        public const string GilMenu = "money";
        public const string InfoMenu = "iteminfo";

        /// <summary>
        /// The layout rectangle the list's companion windows take under it: "money" / "itemctrl" at (16,240) 112 x 56
        /// and "iteminfo" beside them (x 130, 366 wide), so 16..496 by 240..296. It covers the Buy / Sell window
        /// (16,256, 112 x 40), which the HUD therefore hides while a list is open, as retail replaces a parent window
        /// whose corner a later one takes.
        /// </summary>
        public const int CompanionTop = 240, CompanionBottom = 296, CompanionRight = 496;

        /// <summary>Gil is inventory slot 0, item id 65535 (the 0x01E after a purchase or sale updates its count).</summary>
        public const ushort GilItemId = InventoryState.GilItemId;

        /// <summary>The item flag that bars selling to vendors (LandSandBoat's ItemFlag::NoSale).</summary>
        public const uint NoSaleFlag = 0x1000;

        /// <summary>The value 0x083 carries as ShopNo for a shop list number (the capture's 00 04 for list 4).</summary>
        public static ushort ShopNo(ushort shopListNum) => unchecked((ushort)(shopListNum << 8));

        /// <summary>A gil amount with thousands separators.</summary>
        public static string FormatGil(uint gil) => gil.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>A price or the gil in hand as the shop windows print it: "99,997,058 G" (the maintainer's capture, 2026-09-28).</summary>
        public static string FormatPrice(uint gil) => FormatGil(gil) + " G";

        /// <summary>The name the item info window shows: the full log name ("Ronfaure chestnut", "Bunch of San d'Orian grapes"), else the short one.</summary>
        public static string LongName(ItemRecord record) => string.IsNullOrWhiteSpace(record.LogName) ? record.Name : record.LogName;

        /// <summary>The plural log name ("Ronfaure chestnuts"), else the long name with an "s".</summary>
        public static string PluralName(ItemRecord record) => string.IsNullOrWhiteSpace(record.LogPlural) ? LongName(record) + "s" : record.LogPlural;

        /// <summary>
        /// A count of an item as retail's shop lines print it: "a Ronfaure chestnut", "12 Ronfaure chestnuts" (the
        /// maintainer's capture, 2026-09-28). The article is "an" before a vowel.
        /// </summary>
        public static string DescribeCount(ItemRecord record, uint count)
        {
            if (count != 1) return $"{FormatGil(count)} {PluralName(record)}";
            string name = LongName(record);
            bool vowel = name.Length > 0 && "aeiouAEIOU".IndexOf(name[0]) >= 0;
            return (vowel ? "an " : "a ") + name;
        }

        /// <summary>
        /// Retail's line after a purchase, printed by the client on 0x03F ("You buy 12 Ronfaure chestnuts from the
        /// shop."; the server sends no message for it), and after a sale (0x009 message 232, "You sell a Ronfaure
        /// chestnut to the shop."). From the maintainer's capture, 2026-09-28.
        /// </summary>
        public static string BuyMessage(ItemRecord record, uint count) => $"You buy {DescribeCount(record, count)} from the shop.";
        public static string SellMessage(ItemRecord record, uint count) => $"You sell {DescribeCount(record, count)} to the shop.";

        /// <summary>The character's gil: the count of inventory slot 0 when it holds item 65535, else 0.</summary>
        public static uint Gil(InventoryState? inventory) => inventory?.Gil ?? 0;

        /// <summary>An item's record, or a stand-in with its name resolved from the item DATs (stack size 1).</summary>
        public static ItemRecord Lookup(Func<ushort, ItemRecord?>? lookup, ushort itemId)
        {
            var record = lookup?.Invoke(itemId);
            return record ?? new ItemRecord { ItemId = itemId, Name = ItemNameResolver.Resolve(itemId), StackSize = 1 };
        }

        /// <summary>The Buy list: the shop's items in shop-slot order.</summary>
        public static List<StockUiShopRow> BuyRows(IReadOnlyList<ShopItemEntry> items, Func<ushort, ItemRecord?>? lookup)
        {
            var rows = new List<StockUiShopRow>(items.Count);
            foreach (var item in items)
            {
                var record = Lookup(lookup, item.ItemId);
                rows.Add(new StockUiShopRow(item.ItemId, record.Name, item.Price, 0, item.ShopIndex, 0, Math.Max(1, (int)record.StackSize)));
            }
            rows.Sort((a, b) => a.ShopIndex.CompareTo(b.ShopIndex));
            return rows;
        }

        /// <summary>
        /// The Sell list: the inventory's items in slot order (gil left out), those flagged NoSale greyed. The unit
        /// price is 0 until the server appraises a row.
        /// </summary>
        public static List<StockUiShopRow> SellRows(InventoryState? inventory, Func<ushort, ItemRecord?>? lookup)
        {
            var rows = new List<StockUiShopRow>();
            if (inventory == null) return rows;
            var items = inventory.SnapshotItems(Network.Packets.ContainerId.Inventory);
            Array.Sort(items, (a, b) => a.Slot.CompareTo(b.Slot));
            foreach (var item in items)
            {
                if (item.Slot == 0 || item.ItemId == GilItemId || item.ItemId == 0 || item.Count == 0) continue;
                var record = Lookup(lookup, item.ItemId);
                bool noSale = (record.Flags & NoSaleFlag) != 0;
                rows.Add(new StockUiShopRow(item.ItemId, record.Name, 0, item.Count, 0, item.Slot, Math.Max(1, (int)record.StackSize), noSale));
            }
            return rows;
        }

        /// <summary>
        /// How many of a Buy row the character can take at once: the stack size, capped by the gil in hand (at
        /// least 1 so the prompt can open and say why it fails).
        /// </summary>
        public static uint MaxBuyCount(in StockUiShopRow row, uint gil)
        {
            uint byGil = row.Price == 0 ? (uint)row.StackSize : gil / row.Price;
            return Math.Max(1, Math.Min((uint)row.StackSize, byGil));
        }
    }
}
