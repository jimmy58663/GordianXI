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
            Assert.True(System.Threading.SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(2)));

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
            Assert.Equal("Daggers", list.AuctionTitle);
            SpinUntil(() => !list.AuctionLoading);
            Assert.Equal(new byte[] { 2 }, h.ListsAsked);
            var row = Assert.Single(list.AuctionRows);
            Assert.Equal((BronzeDagger, false, 3u), (row.ItemId, row.Stack, row.Listings));
            Assert.Equal(BronzeDagger, list.DescribedItem);

            h.Menus.Activate();
            Assert.Equal(StockUiAuction.ItemMenu, h.Menus.Top!.Name);
            h.Choose(StockUiAuction.ItemHistoryButton);
            var history = h.Menus.Top!;
            Assert.Equal(StockUiAuctionView.History, history.AuctionView);
            SpinUntil(() => history.AuctionHistory.Count == 1);
            Assert.Equal(new StockUiAuctionHistoryRow("23/11/14", "Seller", "Buyer", 160), history.AuctionHistory[0]);
            h.Menus.CloseTop();

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

            // A full stack asks whether the stack goes on auction.
            list.SelectedButtonId = list.ItemRows.ToList().FindIndex(r => r.ItemId == FireCrystal) + 1;
            h.Menus.Activate();
            Assert.Equal(StockUiMenuEntries.MessageYesNoMenu, h.Menus.Top!.Name);
            h.Choose(1);
            SpinUntil(() => h.Menus.Top!.AuctionView == StockUiAuctionView.Price);
            Assert.True(h.Menus.Top!.AuctionItem.Stack);
            h.TypePrice("2000");
            SpinUntil(() => h.HasSent($"askcommit 1 {FireCrystal} 2000 stack"));

            h.Server(AuctionCommand.AskCommit, -1, 1, FireCrystal, 15, paramStacks: 0, paramWorkIndex: 1);
            Assert.Contains("You have to pay a transaction fee of 15 gil.", h.Notices);
            Assert.Equal(StockUiAuction.YesNoMenu, h.Menus.Top!.Name);
            Assert.Equal(StockUiAuction.HelpPlaceOnAuction, h.Menus.Top!.AuctionHelpId);
            h.Choose(1);
            SpinUntil(() => h.HasSent("lotin 1 1 2000 stack"));

            h.Server(AuctionCommand.LotIn, -1, 1);
            Assert.Contains("Merchandise placed on auction.", h.Notices);
            Assert.Contains("If merchandise remains unsold after 30 weeks (Vana'diel time), it will be returned to your current residence.", h.Notices);
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

        [Theory]
        [InlineData(0u, 1u)]
        [InlineData(99u, 1u)]
        [InlineData(1234u, 100u)]
        [InlineData(15000u, 1000u)]
        public void PriceStep_GrowsWithThePrice(uint price, uint step) => Assert.Equal(step, StockUiAuction.PriceStep(price));

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
