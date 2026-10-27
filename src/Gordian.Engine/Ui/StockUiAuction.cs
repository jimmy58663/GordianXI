// src/Gordian.Engine/Ui/StockUiAuction.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using Gordian.Core.Network.Packets;
using Gordian.Core.Network.Search;
using Gordian.Core.Resources.Models;
using Gordian.Core.World;

namespace Gordian.Engine.Ui
{
    /// <summary>What an Auction House window shows (<see cref="StockUiOpenMenu.AuctionView"/>).</summary>
    public enum StockUiAuctionView
    {
        None,

        /// <summary>A category's items (<c>auclist</c>, title "Bid"), from the search server.</summary>
        List,

        /// <summary>An item's last ten sales (<c>auchisto</c>, "Price History"), from the search server.</summary>
        History,

        /// <summary>The character's listings (<c>auclist</c> as the Sales Status list), from S2C 0x04C.</summary>
        Sales,

        /// <summary>The price entry (<c>moneyctr</c>, "Price Set") for a bid or a sale.</summary>
        Price,
    }

    /// <summary>What a Price Set entry is for.</summary>
    public enum StockUiAuctionPriceFor
    {
        Bid,
        Sell,
    }

    /// <summary>
    /// How a category list draws an item's name (the maintainer's round 1 capture image-11, 2026-10-24): yellow when the
    /// current job and level can equip it, green when one is worn, white otherwise; a stack row is pink (image-15).
    /// </summary>
    public enum StockUiAuctionTone
    {
        Plain,
        Equippable,
        Equipped,
        Stack,
    }

    /// <summary>A Price Set arrow: Up / Down change the digit under the cursor, Left / Right move the cursor (or reach All / 0).</summary>
    public enum StockUiPriceKey
    {
        Up,
        Down,
        Left,
        Right,
    }

    /// <summary>
    /// One row of an Auction House list: the item, whether the row is its stack, how many listings the category list counts
    /// for it, and for a Sales Status row the sale slot, the asking price and the parcel's state.
    /// </summary>
    public readonly record struct StockUiAuctionRow(ushort ItemId, string Name, bool Stack, int StackSize, uint Listings,
        sbyte SaleSlot = -1, uint Price = 0, AuctionParcelStat Stat = AuctionParcelStat.None, byte InventorySlot = 0,
        StockUiAuctionTone Tone = StockUiAuctionTone.Plain);

    /// <summary>One sale of an item's Price History: the date, the seller, the buyer, the price and the sale's time (Unix seconds).</summary>
    public readonly record struct StockUiAuctionHistoryRow(string Date, string Seller, string Buyer, uint Price, uint Time = 0);

    /// <summary>A category entry of an Auction House category menu: the AH category it lists, or the menu it opens.</summary>
    public readonly record struct StockUiAuctionCategory(int Button, byte Category, string? Opens, string Name);

    /// <summary>
    /// The stock Auction House windows (#90), all from the English menu DAT (ROM/119/51), titles ROM/165/76, help ROM/165/75,
    /// messages ROM/165/72; the order and the layout follow the maintainer's retail capture <c>Aucion_House.mp4</c> (2026-10-16,
    /// Southern San d'Oria, index on the issue):
    /// <list type="bullet">
    /// <item><c>auc1</c> (top right, 112 x 58, title 18 "Auction"): Bid (help 515 "View all merchandise up for auction."),
    /// Sell (516), Sales Status (517). The server opens it (S2C 0x04C MOPEN) when the Auction Counter is talked to, and the client
    /// answers with C2S 0x04E WorkCheck.</item>
    /// <item><c>auc2</c>: the nine categories; Weapons, Armor, Magic Scrolls, Materials, Food and Others open their own menus
    /// (<c>aucweapo</c>, <c>aucarmor</c>, <c>aucmagic</c>, <c>aucmater</c>, <c>aucfood</c> with <c>aucmeals</c>, <c>aucitem</c>,
    /// and <c>aucammo</c> under Weapons); a leaf lists its items in <c>auclist</c> (16,48, 366 x 190, ten 16 px rows at an 18 px
    /// pitch, cursor <c>anc_auct</c>) with the item described under it.</item>
    /// <item><c>auc3</c> over a listed item: Price History (<c>auchisto</c>, "Sales data for the last ten transactions"), Bid
    /// (the price in <c>moneyctr</c>, then <c>aucyn</c> "Place this bid?") and Sort (<c>wepsort</c> / <c>armsort</c>, not built).</item>
    /// <item>Sell: the inventory in the item list (help 54), the price in <c>moneyctr</c> (help 55 "Set the reserve price."), C2S
    /// AskCommit, the fee the server quotes in the log, then <c>aucyn</c> (help 56 "Place this merchandise on auction?") and LotIn.</item>
    /// <item>Sales Status: C2S Info, the listings in <c>auclist</c>; <c>auc4</c> over one: Price History, Cancel sale (help 530;
    /// message 58 "Remove merchandise from auction?", then LotCancel).</item>
    /// </list>
    /// The category numbers are the search server's (LandSandBoat <c>sql/item_basic.sql</c>, the <c>aH</c> column: 1 hand-to-hand
    /// ... 65 Misc. 3), mapped here to the DAT buttons by their help lines.
    /// </summary>
    public static class StockUiAuction
    {
        public const string MainMenu = "auc1", CategoryMenu = "auc2", ItemMenu = "auc3", SaleMenu = "auc4";
        public const string ListMenu = "auclist", HistoryMenu = "auchisto", PriceMenu = "moneyctr", YesNoMenu = "aucyn";

        public const int BidButton = 1, SellButton = 2, SalesButton = 3;
        public const int ItemHistoryButton = 1, ItemBidButton = 2, ItemSortButton = 3;
        public const int SaleHistoryButton = 1, SaleCancelButton = 2;

        /// <summary>The window titles (ROM/165/76): 18, 121, 88, 119.</summary>
        public const string AuctionTitle = "Auction", BidTitle = "Bid", PriceTitle = "Price Set", HistoryTitle = "Price History";

        /// <summary>The help lines (ROM/165/75) the windows show where the DAT button carries none.</summary>
        public const int HelpSelectSellItem = 54, HelpSetReserve = 55, HelpPlaceOnAuction = 56, HelpSpecifyBid = 58, HelpPlaceBid = 59,
            HelpSelectBidItem = 554, HelpSaleOnAuction = 67, HelpSelectItem = 3;

        /// <summary>
        /// Texts the round 1 captures show that the menu tables (ROM/165/72, 75) do not hold: an empty Price History (image-12)
        /// and an empty Sales Status (image-10), both in the pink of retail's notices.
        /// </summary>
        public const string NoEntriesText = "No applicable entries.", NoSalesText = "You have no items up for auction.";

        /// <summary>The menus' messages (ROM/165/72) the Auction House prints.</summary>
        public const int MessagePlaced = 54, MessagePlaceFailed = 55, MessageBidSuccessful = 56, MessageBidFailed = 57,
            MessageRemovePrompt = 58, MessageNoFeeGil = 61, MessageClosed = 62, MessageTryAgain = 45;

        /// <summary>The highest price the server takes (LandSandBoat <c>c2s/0x04e_auc.cpp</c>: 1 to 999,999,999).</summary>
        public const uint MaxPrice = 999_999_999;

        /// <summary>The item flag that keeps an item off the Auction House (LandSandBoat <c>ItemFlag::NoAuction</c>).</summary>
        public const uint NoAuctionFlag = 0x0040;

        /// <summary>Messages XiPackets gives for 0x04C result codes (world/server/0x004C) that ROM/165/72 does not hold.</summary>
        public const string SevenItemsMessage = "You can only place seven items on auction at once.",
            CannotRemoveMessage = "You cannot remove that item from auction.",
            CannotBidMessage = "You cannot bid on that merchandise.",
            UnavailableMessage = "Auction house services are currently unavailable.";

        private static readonly Dictionary<string, StockUiAuctionCategory[]> Categories = new(StringComparer.OrdinalIgnoreCase)
        {
            [CategoryMenu] = new StockUiAuctionCategory[]
            {
                new(1, 0, "aucweapo", "Weapons"), new(2, 0, "aucarmor", "Armor"), new(3, 0, "aucmagic", "Magic Scrolls"),
                new(4, 33, null, "Medicines"), new(5, 34, null, "Furnishings"), new(6, 0, "aucmater", "Materials"),
                new(7, 0, "aucfood", "Food"), new(8, 35, null, "Crystals"), new(9, 0, "aucitem", "Others"),
            },
            ["aucweapo"] = new StockUiAuctionCategory[]
            {
                new(1, 1, null, "Hand-to-Hand"), new(2, 2, null, "Daggers"), new(3, 3, null, "Swords"), new(4, 4, null, "Great Swords"),
                new(5, 5, null, "Axes"), new(6, 6, null, "Great Axes"), new(7, 7, null, "Scythes"), new(8, 8, null, "Polearms"),
                new(9, 9, null, "Katana"), new(10, 10, null, "Great Katana"), new(11, 11, null, "Clubs"), new(12, 12, null, "Staves"),
                new(13, 13, null, "Ranged"), new(14, 14, null, "Instruments"), new(15, 0, "aucammo", "Ammo&Misc."),
            },
            ["aucammo"] = new StockUiAuctionCategory[]
            {
                new(1, 15, null, "Ammunition"), new(2, 47, null, "Fishing Gear"), new(3, 48, null, "Pet Items"), new(4, 62, null, "Grips"),
            },
            ["aucarmor"] = new StockUiAuctionCategory[]
            {
                new(1, 16, null, "Shields"), new(2, 17, null, "Head"), new(3, 22, null, "Neck"), new(4, 18, null, "Body"),
                new(5, 19, null, "Hands"), new(6, 23, null, "Waist"), new(7, 20, null, "Legs"), new(8, 21, null, "Feet"),
                new(9, 26, null, "Back"), new(10, 24, null, "Earrings"), new(11, 25, null, "Rings"),
            },
            ["aucmagic"] = new StockUiAuctionCategory[]
            {
                new(1, 28, null, "White Magic"), new(2, 29, null, "Black Magic"), new(3, 32, null, "Songs"), new(4, 31, null, "Ninjutsu"),
                new(5, 30, null, "Summoning"), new(6, 60, null, "Dice"), new(7, 45, null, "Geomancy"),
            },
            ["aucmater"] = new StockUiAuctionCategory[]
            {
                new(1, 38, null, "Smithing"), new(2, 39, null, "Goldsmithing"), new(3, 40, null, "Clothcraft"), new(4, 41, null, "Leathercraft"),
                new(5, 42, null, "Bonecraft"), new(6, 43, null, "Woodworking"), new(7, 44, null, "Alchemy"), new(8, 63, null, "Alchemy 2"),
            },
            ["aucfood"] = new StockUiAuctionCategory[]
            {
                new(1, 0, "aucmeals", "Meals"), new(2, 59, null, "Ingredients"), new(3, 51, null, "Fish"),
            },
            ["aucmeals"] = new StockUiAuctionCategory[]
            {
                new(1, 52, null, "Meat & Eggs"), new(2, 53, null, "Seafood"), new(3, 54, null, "Vegetables"), new(4, 55, null, "Soups"),
                new(5, 56, null, "Breads & Rice"), new(6, 57, null, "Sweets"), new(7, 58, null, "Drinks"),
            },
            ["aucitem"] = new StockUiAuctionCategory[]
            {
                new(1, 46, null, "Misc."), new(2, 50, null, "Beastmen-made"), new(3, 36, null, "Cards"), new(4, 49, null, "Ninja Tools"),
                new(5, 37, null, "Cursed Items"), new(6, 61, null, "Automaton"), new(7, 64, null, "Misc. 2"), new(8, 65, null, "Misc. 3"),
            },
        };

        /// <summary>Whether <paramref name="menuName"/> is one of the category menus.</summary>
        public static bool IsCategoryMenu(string menuName) => Categories.ContainsKey(menuName);

        /// <summary>The category entry of a category menu's button.</summary>
        public static bool TryGetCategory(string menuName, int button, out StockUiAuctionCategory category)
        {
            category = default;
            if (!Categories.TryGetValue(menuName, out var entries)) return false;
            foreach (var entry in entries)
            {
                if (entry.Button != button) continue;
                category = entry;
                return true;
            }
            return false;
        }

        /// <summary>Every leaf category number of the menus (for tests).</summary>
        public static IEnumerable<StockUiAuctionCategory> AllCategories()
        {
            foreach (var entries in Categories.Values)
            {
                foreach (var entry in entries) yield return entry;
            }
        }

        /// <summary>
        /// A category list's rows: each item once as a single, and its stack as a second row when it stacks, with the number
        /// of listings the search server counts for each (retail shows "[7]", and nothing for none, image-15) and the tone its
        /// name is drawn in (<see cref="ToneOf"/>; stacks pink).
        /// </summary>
        public static List<StockUiAuctionRow> ListRows(IReadOnlyList<AuctionListItem> items, Func<ushort, ItemRecord?>? lookup,
            Func<ItemRecord, StockUiAuctionTone>? tone = null)
        {
            var rows = new List<StockUiAuctionRow>(items.Count);
            foreach (var item in items)
            {
                if (item.ItemId == 0) continue;
                var record = StockUiShop.Lookup(lookup, item.ItemId);
                int stack = Math.Max(1, (int)record.StackSize);
                rows.Add(new StockUiAuctionRow(item.ItemId, record.Name, false, stack, item.SingleCount, Tone: tone?.Invoke(record) ?? StockUiAuctionTone.Plain));
                if (stack > 1) rows.Add(new StockUiAuctionRow(item.ItemId, record.Name, true, stack, item.StackCount, Tone: StockUiAuctionTone.Stack));
            }
            return rows;
        }

        /// <summary>
        /// The tone of a single's name: green when an item of that kind is worn, yellow when it is equipment the main job and
        /// level can wear (<see cref="StockUiItems.CannotEquipReason"/>), white otherwise.
        /// </summary>
        public static StockUiAuctionTone ToneOf(ItemRecord record, StockUiEquipCharacter character, IReadOnlySet<ushort> worn)
        {
            if (worn.Contains((ushort)record.ItemId)) return StockUiAuctionTone.Equipped;
            if (record.EquipSlotsMask == 0 || record.JobsMask == 0 || character.MainJob == 0) return StockUiAuctionTone.Plain;
            return StockUiItems.CannotEquipReason(record, character) == null ? StockUiAuctionTone.Equippable : StockUiAuctionTone.Plain;
        }

        /// <summary>The listings count right of a list row: "[7]", nothing when there are none (image-15).</summary>
        public static string ListingsText(uint listings) => listings == 0 ? string.Empty : $"[{listings.ToString(CultureInfo.InvariantCulture)}]";

        /// <summary>The Sales Status rows: the filled sale slots in slot order.</summary>
        public static List<StockUiAuctionRow> SalesRows(IReadOnlyList<AuctionSale?> sales, Func<ushort, ItemRecord?>? lookup)
        {
            var rows = new List<StockUiAuctionRow>();
            foreach (var sale in sales)
            {
                if (sale == null) continue;
                var record = StockUiShop.Lookup(lookup, sale.ItemId);
                rows.Add(new StockUiAuctionRow(sale.ItemId, record.Name, sale.Stack, Math.Max(1, (int)record.StackSize), 0,
                    sale.Slot, sale.Price, sale.Stat));
            }
            return rows;
        }

        /// <summary>
        /// The Price History rows, newest first as the server sends them; the date "yy/MM/dd" (image-13: "26/10/17") of the
        /// server's Unix time in <paramref name="zone"/> (the local zone when null).
        /// </summary>
        public static List<StockUiAuctionHistoryRow> HistoryRows(IReadOnlyList<AuctionHistoryEntry> sales, TimeZoneInfo? zone = null)
        {
            var rows = new List<StockUiAuctionHistoryRow>(sales.Count);
            foreach (var sale in sales)
            {
                string date = sale.Data == 0 ? string.Empty : SaleTime(sale.Data, zone).ToString("yy/MM/dd", CultureInfo.InvariantCulture);
                rows.Add(new StockUiAuctionHistoryRow(date, sale.Seller, sale.Buyer, sale.Price, sale.Data));
            }
            return rows;
        }

        private static DateTime SaleTime(uint unix, TimeZoneInfo? zone) =>
            TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(unix), zone ?? TimeZoneInfo.Local).DateTime;

        /// <summary>
        /// The log line Confirm on a Price History entry prints (image-14): "(2026, Oct. 17 7:17:56) Gemini→Gemini [10G]".
        /// PROVISIONAL: the other months' abbreviations are not captured.
        /// </summary>
        public static string HistoryLine(StockUiAuctionHistoryRow row, TimeZoneInfo? zone = null)
        {
            var inv = CultureInfo.InvariantCulture;
            string when = row.Date;
            if (row.Time != 0)
            {
                var t = SaleTime(row.Time, zone);
                when = $"{t.Year.ToString(inv)}, {t.ToString("MMM", inv)}. {t.Day.ToString(inv)} {t.ToString("H:mm:ss", inv)}";
            }
            return $"({when}) {row.Seller}→{row.Buyer} [{row.Price.ToString(inv)}G]";
        }

        /// <summary>Whether an inventory item can go on the Auction House: not gil, not worn, not flagged NoAuction.</summary>
        public static StockUiItemRow SellRow(StockUiItemRow row, Func<ushort, ItemRecord?>? lookup)
        {
            var record = lookup?.Invoke(row.ItemId);
            if (row.Equipped) return row with { Greyed = true, GreyedReason = "You cannot place equipped items on auction." };
            if (record != null && (record.Flags & NoAuctionFlag) != 0) return row with { Greyed = true, GreyedReason = "That item cannot be placed on auction." };
            return row;
        }

        /// <summary>
        /// The help line of a Sales Status row by its parcel state (ROM/165/75 60-68): sold, returned, on auction (67) and the
        /// pending states.
        /// </summary>
        public static int SaleHelp(AuctionParcelStat stat) => stat switch
        {
            AuctionParcelStat.SoldOutPend or AuctionParcelStat.SoldPendCheck => 60,
            AuctionParcelStat.SendBackPend or AuctionParcelStat.BackPendCheck => 61,
            AuctionParcelStat.SoldOut or AuctionParcelStat.SoldPost or AuctionParcelStat.SoldPostCheck => 62,
            AuctionParcelStat.SendBack or AuctionParcelStat.BackPost or AuctionParcelStat.BackPostCheck => 63,
            AuctionParcelStat.SoldPostChecked => 64,
            AuctionParcelStat.BackPostChecked => 65,
            AuctionParcelStat.CancelDone => 66,
            _ => HelpSaleOnAuction,
        };

        /// <summary>The short state word a Sales Status row shows right of its price (PROVISIONAL: retail's wording is not captured).</summary>
        public static string SaleState(AuctionParcelStat stat) => stat switch
        {
            AuctionParcelStat.SoldOut or AuctionParcelStat.SoldPost or AuctionParcelStat.SoldOutPend or AuctionParcelStat.SoldPendCheck
                or AuctionParcelStat.SoldPostCheck or AuctionParcelStat.SoldPostChecked => "Sold",
            AuctionParcelStat.SendBack or AuctionParcelStat.BackPost or AuctionParcelStat.SendBackPend or AuctionParcelStat.BackPendCheck
                or AuctionParcelStat.BackPostCheck or AuctionParcelStat.BackPostChecked => "Returned",
            _ => "On auction",
        };

        /// <summary>
        /// The fee the server quotes, shown in the Yes / No window and the log before the listing is confirmed: a stack's as
        /// image-18 words it ("The total transaction fee for a set of 12 items is 4 gil."), a single's as the first capture's log
        /// (0:20-0:45, "You have to pay a transaction fee of 1 gil."; PROVISIONAL that it is the single's wording).
        /// </summary>
        public static string FeeMessage(uint fee, int stackSize = 1) => stackSize > 1
            ? $"The total transaction fee for a set of {stackSize.ToString(CultureInfo.InvariantCulture)} items is {StockUiShop.FormatGil(fee)} gil."
            : $"You have to pay a transaction fee of {StockUiShop.FormatGil(fee)} gil.";

        /// <summary>
        /// A bid's question (round 2, image-27: "Make a bid of 110 gil" / "for the fire crystals?" on two lines, a stack's log
        /// plural; a single takes its log name, "for the fire crystal?", confirmed in-game in round 3). Neither ROM/165/72 nor ROM/165/75 holds it.
        /// </summary>
        public static string BidPrompt(uint price, string logName) => $"Make a bid of {StockUiShop.FormatGil(price)} gil\nfor the {logName}?";

        /// <summary>The warning a stackable item short of a full stack prints when picked to sell (image-16); it then sells as a single.</summary>
        public static string PartialStackMessage(int stackSize) =>
            $"You can only place a single item or a set of {stackSize.ToString(CultureInfo.InvariantCulture)} such items on auction.";

        /// <summary>Parses a typed price: its digits, capped at <see cref="MaxPrice"/>; 0 when there are none.</summary>
        public static uint ParsePrice(string text)
        {
            ulong value = 0;
            foreach (char c in text)
            {
                if (c is < '0' or > '9') continue;
                value = Math.Min(MaxPrice, value * 10 + (ulong)(c - '0'));
            }
            return (uint)value;
        }

        /// <summary>The most digits a price has (999,999,999).</summary>
        public const int PriceDigits = 9;

        /// <summary>
        /// Price Set's arrows, as the maintainer described retail's (round 1, N4): the cursor sits on a digit (0 = ones); Up /
        /// Down add or take that digit's place value; Left moves the cursor one digit up (a zero shows there), and when that
        /// digit would be worth more than <paramref name="cap"/> (the gil in hand for a bid) the price becomes the cap, the
        /// frame's "All" arrow; Right moves the cursor down, and on the ones clears the price, the "0" arrow. No step passes
        /// the cap (11 gil: Left, Up, Up gives 11). PROVISIONAL: Down below zero and Right are not described.
        /// </summary>
        public static (uint Price, int Digit) StepPrice(uint price, int digit, StockUiPriceKey key, uint cap)
        {
            cap = Math.Min(cap, MaxPrice);
            digit = Math.Clamp(digit, 0, PriceDigits - 1);
            ulong place = Place(digit);
            switch (key)
            {
                case StockUiPriceKey.Up:
                    price = (uint)Math.Min(cap, price + place);
                    break;
                case StockUiPriceKey.Down:
                    price = price >= place ? (uint)(price - place) : 0;
                    break;
                case StockUiPriceKey.Left:
                    if (digit + 1 >= PriceDigits || Place(digit + 1) > cap) price = cap;
                    else digit++;
                    break;
                case StockUiPriceKey.Right:
                    if (digit > 0) digit--;
                    else price = 0;
                    break;
            }
            return (Math.Min(price, cap), digit);
        }

        private static ulong Place(int digit)
        {
            ulong place = 1;
            for (int i = 0; i < digit; i++) place *= 10;
            return place;
        }

        /// <summary>The price as the field shows it: its digits, zero-padded to the cursor's digit (Left x3 from 0 shows "0000").</summary>
        public static string PriceDigitsText(uint price, int digit) =>
            price.ToString(CultureInfo.InvariantCulture).PadLeft(Math.Clamp(digit, 0, PriceDigits - 1) + 1, '0');

        /// <summary>
        /// The message a 0x04C answer prints, or null for none: the ROM/165/72 texts by id (<paramref name="message"/>), else
        /// XiPackets' per-command list (world/server/0x004C).
        /// </summary>
        public static string? ResultMessage(AuctionResponse response, Func<int, string?>? message)
        {
            string M(int id, string fallback) => message?.Invoke(id) is { Length: > 0 } text ? text : fallback;
            var code = response.ResultCode;
            if (code == AuctionResultCode.ServiceUnavailable) return UnavailableMessage;
            switch (response.Command)
            {
                case AuctionCommand.Open:
                    if (code == AuctionResultCode.Success) return null;
                    return code == AuctionResultCode.Limit ? M(MessageTryAgain, "Please try again in a little while.")
                        : M(MessageClosed, "Auction house is temporarily closed for trading.");
                case AuctionCommand.Info when code == AuctionResultCode.Limit:
                    return M(MessageTryAgain, "Please try again in a little while.");
                case AuctionCommand.AskCommit when code == AuctionResultCode.LastSlot:
                    return SevenItemsMessage;
                case AuctionCommand.AskCommit when code == AuctionResultCode.NotHaveGil:
                    return M(MessageNoFeeGil, "You do not have enough gil to pay the transaction fee.");
                case AuctionCommand.AskCommit when code != AuctionResultCode.Success:
                    return M(MessagePlaceFailed, "Failed to place merchandise on auction.");
                case AuctionCommand.LotIn:
                    return code == AuctionResultCode.Success ? M(MessagePlaced, "Merchandise placed on auction.")
                        : code == AuctionResultCode.Interim ? null
                        : M(MessagePlaceFailed, "Failed to place merchandise on auction.");
                case AuctionCommand.LotCancel when code is AuctionResultCode.AlreadyHaveLore or AuctionResultCode.ProbInventoryFull:
                    return CannotRemoveMessage;
                case AuctionCommand.Bid:
                    return code == AuctionResultCode.Success ? M(MessageBidSuccessful, "Your bid was successful.")
                        : code is AuctionResultCode.AlreadyHaveLore or AuctionResultCode.ProbInventoryFull ? CannotBidMessage
                        : M(MessageBidFailed, "Your bid failed.");
                default:
                    return code == AuctionResultCode.OnAuction ? M(MessageClosed, "Auction house is temporarily closed for trading.") : null;
            }
        }
    }
}
