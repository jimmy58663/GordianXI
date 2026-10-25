// tests/Gordian.Engine.Tests/Ui/StockUiAuctionTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Network.Packets;
using Gordian.Core.Network.Search;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Ui;
using Gordian.Core.World;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Ui
{
    /// <summary>The stock Auction House windows (#90): open, browse, price history, bid, sell with the fee quote, Sales Status, cancel.</summary>
    public class StockUiAuctionTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        private const ushort FireCrystal = 4096, BronzeDagger = 16449, Hatchet = 1021, BronzeCap = 12448;

        private static readonly Dictionary<ushort, ItemRecord> Records = new()
        {
            [FireCrystal] = new ItemRecord { ItemId = FireCrystal, Name = "Fire Crystal", LogName = "fire crystal", StackSize = 12 },
            [BronzeDagger] = new ItemRecord { ItemId = BronzeDagger, Name = "Bronze Dagger", LogName = "bronze dagger", StackSize = 1 },
            [Hatchet] = new ItemRecord { ItemId = Hatchet, Name = "Hatchet", LogName = "hatchet", StackSize = 12, Flags = StockUiAuction.NoAuctionFlag },
            [BronzeCap] = new ItemRecord { ItemId = BronzeCap, Name = "Bronze Cap", LogName = "bronze cap", EquipSlotsMask = 1 << (int)EquipSlotId.Head, ItemType = 5 },
        };

        private static UiMenuDefinition List(string name, short x, short y, UiAnchor anchor, int count, short frameHelp = -1, int pitch = 16, short rowX = 16)
        {
            var buttons = new List<UiMenuButton>();
            for (int i = 1; i <= count; i++)
            {
                buttons.Add(new UiMenuButton
                {
                    ButtonId = (short)i, X = rowX, Y = (short)(6 + pitch * (i - 1)), Width = 88, Height = 16,
                    NavUp = (sbyte)(i == 1 ? 1 : i - 1), NavDown = (sbyte)(i == count ? i : i + 1), NavLeft = (sbyte)i, NavRight = (sbyte)i,
                    HelpTextId = (short)(900 + i),
                });
            }
            return new UiMenuDefinition { Name = name, Frame = new UiMenuFrame { X = x, Y = y, Width = 112, Height = (short)(10 + pitch * count), Anchor = anchor, HelpTextId = frameHelp }, Buttons = buttons };
        }

        private static UiResourceLibrary Library() => UiResourceLibrary.FromDefinitions(new[]
        {
            List(StockUiAuction.MainMenu, 384, 48, UiAnchor.TopRight, 3),
            List(StockUiAuction.CategoryMenu, 384, 48, UiAnchor.TopRight, 9),
            List("aucweapo", 384, 48, UiAnchor.TopRight, 15),
            List(StockUiAuction.ItemMenu, 384, 48, UiAnchor.TopRight, 3),
            List(StockUiAuction.SaleMenu, 384, 48, UiAnchor.TopRight, 2),
            List(StockUiAuction.YesNoMenu, 384, 48, UiAnchor.TopRight, 2),
            List(StockUiAuction.ListMenu, 16, 48, UiAnchor.TopLeft, 10, 3, 18, 0),
            List(StockUiAuction.HistoryMenu, 16, 48, UiAnchor.TopLeft, 10, 548, 18, 0),
            new UiMenuDefinition { Name = StockUiAuction.PriceMenu, Frame = new UiMenuFrame { X = 130, Y = 240, Width = 366, Height = 56, HelpTextId = 352 } },
            List(StockUiMenuEntries.MessageYesNoMenu, 130, 200, UiAnchor.TopLeft, 2),
            List(StockUiItems.ListMenu, 16, 48, UiAnchor.TopLeft, 10, 3, 18, 3),
        });

        private sealed class Harness
        {
            public readonly InventoryState Inventory = new();
            public readonly StockUiMenuController Menus = new() { Library = Library() };
            public readonly List<string> Notices = new();
            public readonly List<string> Sent = new();
            public readonly List<byte> ListsAsked = new();

            public Harness()
            {
                Menus.Inventory = Inventory;
                Menus.ItemLookup = id => Records.TryGetValue(id, out var r) ? r : null;
                Menus.MenuMessageText = id => id switch
                {
                    StockUiAuction.MessageBidSuccessful => "Your bid was successful.",
                    StockUiAuction.MessagePlaced => "Merchandise placed on auction.\nIf merchandise remains unsold after 30 weeks (Vana'diel time), it will be returned to your current residence.",
                    _ => null,
                };
                Menus.NoticePosted += Notices.Add;
                Menus.AuctionHouse = () => Inventory.AuctionHouse;
                Menus.AuctionOpenRequested = () => Record("workcheck");
                Menus.AuctionSalesRequested = () => Record("info");
                Menus.AuctionBid = (item, price, stack, slot) => Record($"bid {item} {price} {(stack ? "stack" : "single")} {slot}");
                Menus.AuctionAskCommit = (slot, item, price, stack) => Record($"askcommit {slot} {item} {price} {(stack ? "stack" : "single")}");
                Menus.AuctionListForSale = (saleSlot, slot, price, stack) => Record($"lotin {saleSlot} {slot} {price} {(stack ? "stack" : "single")}");
                Menus.AuctionCancelSale = slot => Record($"lotcancel {slot}");
                Menus.AuctionListRequested = category =>
                {
                    lock (ListsAsked) ListsAsked.Add(category);
                    IReadOnlyList<AuctionListItem> items = category == 2
                        ? new[] { new AuctionListItem(BronzeDagger, 3, 0) }
                        : new[] { new AuctionListItem(FireCrystal, 2, 5) };
                    return Task.FromResult<IReadOnlyList<AuctionListItem>?>(items);
                };
                Menus.AuctionHistoryRequested = (item, stack) => Task.FromResult<IReadOnlyList<AuctionHistoryEntry>?>(new[]
                {
                    new AuctionHistoryEntry(stack ? 1800u : 160u, 1_700_000_000, "Seller", "Buyer"),
                });
                Inventory.AuctionHouse.Opened += Menus.OnAuctionOpened;
                Inventory.AuctionHouse.SalesChanged += Menus.OnAuctionSalesChanged;
                Inventory.AuctionHouse.ResponseReceived += Menus.OnAuctionResponse;
                Inventory.SetContainerSizes(ContainerId.Inventory, 30, 30);
                Inventory.SetItem(ContainerId.Inventory, 0, InventoryState.GilItemId, 5000, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 1, FireCrystal, 12, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 2, BronzeDagger, 1, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 3, Hatchet, 1, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 4, BronzeCap, 1, ItemLockFlag.Normal);
                Inventory.SetEquip(EquipSlotId.Head, ContainerId.Inventory, 4);
            }

            private Task Record(string what)
            {
                lock (Sent) Sent.Add(what);
                return Task.CompletedTask;
            }

            public bool HasSent(string what)
            {
                lock (Sent) return Sent.Contains(what);
            }

            public void Server(AuctionCommand command, sbyte slot, sbyte result, ushort itemId = 0, uint price = 0, byte quantity = 1, byte stat = 3,
                uint paramStacks = 0, ushort paramWorkIndex = 0) =>
                Inventory.SetAuctionResponse(new AuctionResponse(command, slot, result, 0, itemId != 0 ? stat : (byte)0, 1, itemId, quantity, price, string.Empty,
                    ParamStacks: paramStacks, ParamWorkIndex: paramWorkIndex));

            public StockUiOpenMenu Open()
            {
                Server(AuctionCommand.Open, -1, 1);
                Assert.True(Menus.IsAuctionOpen);
                SpinUntil(() => HasSent("workcheck"));
                return Menus.Top!;
            }

            public void Choose(int button)
            {
                Menus.Top!.SelectedButtonId = button;
                Menus.Activate();
            }

            public void TypePrice(string digits)
            {
                Assert.True(Menus.MacroField.IsOpen);
                Menus.MacroField.InsertText(digits);
                Menus.MacroField.Commit();
            }
        }

        private static void SpinUntil(Func<bool> condition) =>
            Assert.True(System.Threading.SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(10)));

        [Fact]
        public void Mopen_OpensTheAuctionWindowAsTheRoot_AndSendsWorkCheck()
        {
            var h = new Harness();
            var root = h.Open();
            Assert.Equal(StockUiAuction.MainMenu, root.Name);
            Assert.Single(h.Menus.OpenMenus);
        }

        [Fact]
        public void Bid_BrowsesACategory_ShowsHistory_AndBidsAfterTheQuestion()
        {
            var h = new Harness();
            h.Open();
            h.Choose(StockUiAuction.BidButton);
            Assert.Equal(StockUiAuction.CategoryMenu, h.Menus.Top!.Name);
            h.Choose(1); // Weapons
            Assert.Equal("aucweapo", h.Menus.Top!.Name);
            h.Choose(2); // Daggers: category 2
            var list = h.Menus.Top!;
            Assert.Equal(StockUiAuctionView.List, list.AuctionView);
            Assert.Equal(StockUiAuction.BidTitle, list.AuctionTitle);
            Assert.Equal(StockUiAuction.HelpSelectItem, list.AuctionHelpId);
            SpinUntil(() => !list.AuctionLoading);
            Assert.Equal(new byte[] { 2 }, h.ListsAsked);
            var row = Assert.Single(list.AuctionRows);
            Assert.Equal((BronzeDagger, false, 3u), (row.ItemId, row.Stack, row.Listings));
            Assert.Equal(BronzeDagger, list.DescribedItem);

            h.Menus.Activate();
            Assert.Equal(StockUiAuction.ItemMenu, h.Menus.Top!.Name);
            Assert.False(h.Menus.Top!.IsGreyed(StockUiAuction.ItemBidButton));
            h.Choose(StockUiAuction.ItemHistoryButton);
            var history = h.Menus.Top!;
            Assert.Equal(StockUiAuctionView.History, history.AuctionView);
            Assert.Same(list, history.Parent); // Price History / Bid / Sort gives way: Cancel goes back to the list
            Assert.Equal(BronzeDagger, history.DescribedItem);
            SpinUntil(() => history.AuctionHistory.Count == 1);
            string date = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).ToLocalTime().ToString("yy/MM/dd", System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(new StockUiAuctionHistoryRow(date, "Seller", "Buyer", 160, 1_700_000_000), history.AuctionHistory[0]);
            h.Menus.Activate();
            Assert.Contains(StockUiAuction.HistoryLine(history.AuctionHistory[0]), h.Notices);
            h.Menus.CloseTop();
            Assert.Same(list, h.Menus.Top);

            h.Menus.Activate();
            h.Choose(StockUiAuction.ItemBidButton);
            Assert.Equal(StockUiAuctionView.Price, h.Menus.Top!.AuctionView);
            Assert.True(list.ShowsQuantity);
            h.TypePrice("1500");
            Assert.Equal(StockUiAuction.YesNoMenu, h.Menus.Top!.Name);
            Assert.Equal(StockUiAuction.HelpPlaceBid, h.Menus.Top!.AuctionHelpId);
            h.Choose(1); // Yes
            SpinUntil(() => h.HasSent($"bid {BronzeDagger} 1500 single 0"));
            SpinUntil(() => !h.Menus.OpenMenus.Any(m => m.AuctionView == StockUiAuctionView.Price));
            Assert.False(list.ShowsQuantity);

            h.Server(AuctionCommand.Bid, 0, 1, BronzeDagger, 1500);
            Assert.Contains("Your bid was successful.", h.Notices);
            SpinUntil(() => { lock (h.ListsAsked) return h.ListsAsked.Count == 2; });
        }

        [Fact]
        public void Bid_StackRowBidsAStack_AndRefusesMoreThanTheGil()
        {
            var h = new Harness();
            h.Open();
            h.Choose(StockUiAuction.BidButton);
            h.Choose(8); // Crystals: category 35
            var list = h.Menus.Top!;
            SpinUntil(() => list.AuctionRows.Count == 2);
            Assert.Equal(new[] { (false, 2u), (true, 5u) }, list.AuctionRows.Select(r => (r.Stack, r.Listings)));
            list.SelectedButtonId = 2;
            h.Menus.Activate();
            h.Choose(StockUiAuction.ItemBidButton);
            h.TypePrice("6000");
            Assert.Contains("You do not have enough gil.", h.Notices);
            Assert.True(h.Menus.MacroField.IsOpen); // the field opens again with the price
            Assert.Equal("6000", h.Menus.MacroField.Text);
        }

        [Fact]
        public void Sell_AsksTheFee_ThenListsInTheNextFreeSlot()
        {
            var h = new Harness();
            h.Open();
            h.Server(AuctionCommand.LotCancel, 0, 1, BronzeDagger, 900); // slot 0 is taken
            h.Choose(StockUiAuction.SellButton);
            var list = h.Menus.Top!;
            Assert.Equal(StockUiItemListKind.AuctionSell, list.ItemListKind);
            Assert.Equal(StockUiAuction.HelpSelectSellItem, list.AuctionHelpId);
            var hatchet = list.ItemRows.Single(r => r.ItemId == Hatchet);
            var cap = list.ItemRows.Single(r => r.ItemId == BronzeCap);
            Assert.True(hatchet.Greyed && cap.Greyed);

            // A full stack sells as the stack with no question: its stack Price History, then Price Set over it (image-17).
            list.SelectedButtonId = list.ItemRows.ToList().FindIndex(r => r.ItemId == FireCrystal) + 1;
            h.Menus.Activate();
            var price = h.Menus.Top!;
            Assert.Equal(StockUiAuctionView.Price, price.AuctionView);
            Assert.True(price.AuctionItem.Stack);
            Assert.Equal(352, price.AuctionHelpId);
            var history = price.Parent!;
            Assert.True(history.AuctionForSale);
            SpinUntil(() => history.AuctionHistory.Count == 1);
            Assert.Equal(1800u, history.AuctionHistory[0].Price);
            h.TypePrice("2000");
            SpinUntil(() => h.HasSent($"askcommit 1 {FireCrystal} 2000 stack"));

            // The fee in a Yes / No window and the log (image-18).
            h.Server(AuctionCommand.AskCommit, -1, 1, FireCrystal, 15, paramStacks: 0, paramWorkIndex: 1);
            Assert.Contains("The total transaction fee for a set of 12 items is 15 gil.", h.Notices);
            SpinUntil(() => h.Menus.Top!.Name == StockUiMenuEntries.MessageYesNoMenu);
            h.Choose(1);
            SpinUntil(() => h.HasSent("lotin 1 1 2000 stack"));

            h.Server(AuctionCommand.LotIn, -1, 1);
            Assert.Contains("Merchandise placed on auction.", h.Notices);
            Assert.Contains("If merchandise remains unsold after 30 weeks (Vana'diel time), it will be returned to your current residence.", h.Notices);
        }

        [Fact]
        public void Sell_ShortOfAStack_WarnsAndSellsASingle()
        {
            var h = new Harness();
            h.Inventory.SetItem(ContainerId.Inventory, 1, FireCrystal, 5, ItemLockFlag.Normal);
            h.Open();
            h.Choose(StockUiAuction.SellButton);
            var list = h.Menus.Top!;
            list.SelectedButtonId = list.ItemRows.ToList().FindIndex(r => r.ItemId == FireCrystal) + 1;
            h.Menus.Activate();
            Assert.Contains("You can only place a single item or a set of 12 such items on auction.", h.Notices);
            Assert.False(h.Menus.Top!.AuctionItem.Stack);

            // Cancel closes Price Set and the history opened with it.
            h.Menus.CloseTop();
            SpinUntil(() => ReferenceEquals(h.Menus.Top, list));
        }

        [Fact]
        public void Bid_IsGreyedWhenNoneAreListed()
        {
            var h = new Harness();
            h.Menus.AuctionListRequested = _ => Task.FromResult<IReadOnlyList<AuctionListItem>?>(new[] { new AuctionListItem(BronzeDagger, 0, 0) });
            h.Open();
            h.Choose(StockUiAuction.BidButton);
            h.Choose(1);
            h.Choose(2);
            var list = h.Menus.Top!;
            SpinUntil(() => list.AuctionRows.Count == 1);
            h.Menus.Activate();
            var item = h.Menus.Top!;
            Assert.True(item.IsGreyed(StockUiAuction.ItemBidButton));
            h.Choose(StockUiAuction.ItemBidButton);
            Assert.Same(item, h.Menus.Top);
        }

        [Fact]
        public void MovingAwayFromTheCounter_ClosesTheWindow()
        {
            var h = new Harness();
            h.Open();
            h.Menus.SetAuctionAnchor(new System.Numerics.Vector3(10, 0, 10));
            h.Menus.CheckAuctionDistance(new System.Numerics.Vector3(16, 5, 16));
            Assert.True(h.Menus.IsAuctionOpen);
            h.Menus.CheckAuctionDistance(new System.Numerics.Vector3(18, 0, 18));
            Assert.False(h.Menus.IsAuctionOpen);
            Assert.Empty(h.Menus.OpenMenus);
        }

        [Fact]
        public void ListTones_FollowTheJobAndTheWornGear()
        {
            var character = new StockUiEquipCharacter((byte)JobId.Warrior, 10);
            var worn = new HashSet<ushort> { BronzeCap };
            var cap = new ItemRecord { ItemId = BronzeCap, EquipSlotsMask = 1, JobsMask = 1u << (int)JobId.Warrior, Level = 1 };
            var sword = new ItemRecord { ItemId = 1, EquipSlotsMask = 1, JobsMask = 1u << (int)JobId.Warrior, Level = 5 };
            var high = new ItemRecord { ItemId = 2, EquipSlotsMask = 1, JobsMask = 1u << (int)JobId.Warrior, Level = 50 };
            var mage = new ItemRecord { ItemId = 3, EquipSlotsMask = 1, JobsMask = 1u << (int)JobId.WhiteMage, Level = 1 };
            Assert.Equal(StockUiAuctionTone.Equipped, StockUiAuction.ToneOf(cap, character, worn));
            Assert.Equal(StockUiAuctionTone.Equippable, StockUiAuction.ToneOf(sword, character, worn));
            Assert.Equal(StockUiAuctionTone.Plain, StockUiAuction.ToneOf(high, character, worn));
            Assert.Equal(StockUiAuctionTone.Plain, StockUiAuction.ToneOf(mage, character, worn));
            Assert.Equal(StockUiAuctionTone.Plain, StockUiAuction.ToneOf(Records[FireCrystal], character, worn));
            Assert.Equal(new[] { StockUiAuctionTone.Plain, StockUiAuctionTone.Stack },
                StockUiAuction.ListRows(new[] { new AuctionListItem(FireCrystal, 0, 7) }, id => Records[id], r => StockUiAuction.ToneOf(r, character, worn)).Select(r => r.Tone));
            Assert.Equal(("", "[7]"), (StockUiAuction.ListingsText(0), StockUiAuction.ListingsText(7)));
        }

        [Fact]
        public void HistoryLine_PrintsTheSaleInFull()
        {
            var row = StockUiAuction.HistoryRows(new[] { new AuctionHistoryEntry(10, 1_760_685_476, "Gemini", "Gemini") }, TimeZoneInfo.Utc)[0];
            Assert.Equal("25/10/17", row.Date);
            Assert.Equal("(2025, Oct. 17 7:17:56) Gemini\u2192Gemini [10G]", StockUiAuction.HistoryLine(row, TimeZoneInfo.Utc));
        }

        [Fact]
        public void Sell_GreyedItem_SaysWhy()
        {
            var h = new Harness();
            h.Open();
            h.Choose(StockUiAuction.SellButton);
            var list = h.Menus.Top!;
            list.SelectedButtonId = list.ItemRows.ToList().FindIndex(r => r.ItemId == Hatchet) + 1;
            h.Menus.Activate();
            Assert.Contains("That item cannot be placed on auction.", h.Notices);
        }

        [Fact]
        public void SalesStatus_ListsTheSlots_AndCancelsASale()
        {
            var h = new Harness();
            h.Open();
            h.Choose(StockUiAuction.SalesButton);
            SpinUntil(() => h.HasSent("info"));
            var list = h.Menus.Top!;
            Assert.Equal(StockUiAuctionView.Sales, list.AuctionView);
            Assert.Empty(list.AuctionRows);

            h.Server(AuctionCommand.Info, -1, 1);
            h.Server(AuctionCommand.LotCancel, 0, 1, FireCrystal, 2000, quantity: 0);
            h.Server(AuctionCommand.LotCancel, 1, 1, BronzeDagger, 900);
            Assert.Equal(new[] { (FireCrystal, true, 2000u, (sbyte)0), (BronzeDagger, false, 900u, (sbyte)1) },
                list.AuctionRows.Select(r => (r.ItemId, r.Stack, r.Price, r.SaleSlot)));

            list.SelectedButtonId = 2;
            h.Menus.Activate();
            Assert.Equal(StockUiAuction.SaleMenu, h.Menus.Top!.Name);
            h.Choose(StockUiAuction.SaleCancelButton);
            Assert.Equal(StockUiMenuEntries.MessageYesNoMenu, h.Menus.Top!.Name);
            h.Choose(1);
            SpinUntil(() => h.HasSent("lotcancel 1"));
            h.Server(AuctionCommand.LotCancel, 1, 0);
            Assert.Single(list.AuctionRows);
        }

        [Fact]
        public void ResultMessages_FollowXiPackets()
        {
            AuctionResponse R(AuctionCommand c, byte result) => new(c, -1, (sbyte)result, 0, 0, 0, 0, 0, 0, string.Empty);
            Assert.Null(StockUiAuction.ResultMessage(R(AuctionCommand.Open, 1), null));
            Assert.Equal("Please try again in a little while.", StockUiAuction.ResultMessage(R(AuctionCommand.Info, 0xF6), null));
            Assert.Equal(StockUiAuction.SevenItemsMessage, StockUiAuction.ResultMessage(R(AuctionCommand.AskCommit, 0xF3), null));
            Assert.Equal(StockUiAuction.CannotBidMessage, StockUiAuction.ResultMessage(R(AuctionCommand.Bid, 0xE5), null));
            Assert.Equal("Your bid failed.", StockUiAuction.ResultMessage(R(AuctionCommand.Bid, 0xC5), null));
            Assert.Equal(StockUiAuction.CannotRemoveMessage, StockUiAuction.ResultMessage(R(AuctionCommand.LotCancel, 0xE5), null));
            Assert.Equal(StockUiAuction.UnavailableMessage, StockUiAuction.ResultMessage(R(AuctionCommand.Bid, 0xF5), null));
            Assert.Null(StockUiAuction.ResultMessage(R(AuctionCommand.LotCheck, 1), null));
        }

        [Theory]
        [InlineData("1,500 G", 1500u)]
        [InlineData("", 0u)]
        [InlineData("9999999999", 999_999_999u)]
        public void ParsePrice_KeepsDigits(string text, uint price) => Assert.Equal(price, StockUiAuction.ParsePrice(text));

        private static uint Keys(uint cap, params StockUiPriceKey[] keys)
        {
            uint price = 0;
            int digit = 0;
            foreach (var key in keys) (price, digit) = StockUiAuction.StepPrice(price, digit, key, cap);
            return price;
        }

        [Fact]
        public void StepPrice_FollowsRetailsDigitCursor()
        {
            const StockUiPriceKey L = StockUiPriceKey.Left, U = StockUiPriceKey.Up, D = StockUiPriceKey.Down, R = StockUiPriceKey.Right;
            // The maintainer's round 1 examples: 1000 is Left x3, Up; with 10 gil a second Left gives 10; with 11 gil Left, Up, Up gives 11.
            Assert.Equal(1000u, Keys(StockUiAuction.MaxPrice, L, L, L, U));
            Assert.Equal(10u, Keys(10, L, L));
            Assert.Equal(11u, Keys(11, L, U, U));
            Assert.Equal(1500u, Keys(StockUiAuction.MaxPrice, L, L, L, U, R, U, U, U, U, U));
            Assert.Equal(900u, Keys(StockUiAuction.MaxPrice, L, L, L, U, R, D));
            Assert.Equal(0u, Keys(StockUiAuction.MaxPrice, U, U, R));
            Assert.Equal(StockUiAuction.MaxPrice, Keys(StockUiAuction.MaxPrice, L, L, L, L, L, L, L, L, L));
            Assert.Equal("0000", StockUiAuction.PriceDigitsText(0, 3));
            Assert.Equal("1500", StockUiAuction.PriceDigitsText(1500, 2));
        }

        [Fact]
        public void ZoneChange_ClosesTheWindow()
        {
            var h = new Harness();
            h.Open();
            h.Menus.CloseAuctionHouse();
            Assert.False(h.Menus.IsAuctionOpen);
            Assert.False(h.Inventory.AuctionHouse.IsOpen);
            Assert.Empty(h.Menus.OpenMenus);
        }

        [Fact]
        public void Categories_CoverEveryLeafOnce()
        {
            var leaves = StockUiAuction.AllCategories().Where(c => c.Opens == null).Select(c => (int)c.Category).ToList();
            Assert.Equal(leaves.Count, leaves.Distinct().Count());
            // LandSandBoat's aH numbers 1-65 less 27 (unused).
            Assert.Equal(Enumerable.Range(1, 65).Where(c => c != 27), leaves.OrderBy(c => c));
        }

        /// <summary>The retail menu DAT's Auction House windows (skipped without the install).</summary>
        [Fact]
        public void RetailMenus_AuctionWindowsResolve()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            if (library == null) return;
            var help = rm.GetDMsgTable(DMsgCategory.MenuHelp);
            string Help(int id) => help!.Records[id].PrimaryText;

            Assert.True(library.TryGetMenu(StockUiAuction.MainMenu, out var main));
            Assert.Equal("View all merchandise up for auction.", Help(main.FindButton(StockUiAuction.BidButton)!.HelpTextId));
            Assert.Equal("Place unwanted items on auction.", Help(main.FindButton(StockUiAuction.SellButton)!.HelpTextId));
            Assert.Equal("Check your items currently placed on auction.", Help(main.FindButton(StockUiAuction.SalesButton)!.HelpTextId));
            Assert.True(library.TryGetMenu(StockUiAuction.ItemMenu, out var item));
            Assert.Equal("Place a bid on this merchandise.", Help(item.FindButton(StockUiAuction.ItemBidButton)!.HelpTextId));
            Assert.True(library.TryGetMenu(StockUiAuction.SaleMenu, out var sale));
            Assert.Equal("Remove merchandise from auction.", Help(sale.FindButton(StockUiAuction.SaleCancelButton)!.HelpTextId));
            foreach (var name in new[] { StockUiAuction.ListMenu, StockUiAuction.HistoryMenu, StockUiAuction.PriceMenu, StockUiAuction.YesNoMenu })
            {
                Assert.True(library.TryGetMenu(name, out _), name);
            }
            Assert.Equal("Place this bid?", Help(StockUiAuction.HelpPlaceBid));
            Assert.Equal("Set the reserve price.", Help(StockUiAuction.HelpSetReserve));

            // Every category button exists, and a leaf's help names what it lists.
            foreach (var category in StockUiAuction.AllCategories())
            {
                string menu = new[] { StockUiAuction.CategoryMenu, "aucweapo", "aucammo", "aucarmor", "aucmagic", "aucmater", "aucfood", "aucmeals", "aucitem" }
                    .First(m => StockUiAuction.TryGetCategory(m, category.Button, out var c) && c == category);
                Assert.True(library.TryGetMenu(menu, out var definition), menu);
                Assert.NotNull(definition.FindButton(category.Button));
            }
            Assert.True(library.TryGetMenu("aucweapo", out var weapons));
            Assert.Equal("Daggers, knives, etc.", Help(weapons.FindButton(2)!.HelpTextId));
            Assert.True(library.TryGetMenu("aucitem", out var others));
            Assert.Equal("View miscellaneous items 3 on auction.", Help(others.FindButton(8)!.HelpTextId));
        }
    }
}
