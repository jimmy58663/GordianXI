// tests/Gordian.Engine.Tests/Ui/StockUiTradeTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Ui;
using Gordian.Core.World;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Ui
{
    /// <summary>The trade windows (#486): player trade (C2S 0x032 / 0x033 / 0x034, S2C 0x021-0x025) and NPC trade (C2S 0x036).</summary>
    public class StockUiTradeTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        private const ushort FireCrystal = 4096, Seashell = 1193, BronzeCap = 12448, Signet = 9999, Chestnut = 639;
        private const uint PartnerId = 0x0102_0304, NpcId = 0x0110_0042, MonsterId = 0x0110_0099;
        private const ushort PartnerIndex = 0x405, NpcIndex = 0x42;

        private static readonly Dictionary<ushort, ItemRecord> Records = new()
        {
            [FireCrystal] = new ItemRecord { ItemId = FireCrystal, Name = "Fire Crystal", LogName = "fire crystal", StackSize = 12 },
            [Seashell] = new ItemRecord { ItemId = Seashell, Name = "Seashell", LogName = "seashell", StackSize = 12 },
            [BronzeCap] = new ItemRecord { ItemId = BronzeCap, Name = "Bronze Cap", LogName = "bronze cap", EquipSlotsMask = 1 << (int)EquipSlotId.Head, ItemType = 5 },
            [Signet] = new ItemRecord { ItemId = Signet, Name = "Quest Letter", LogName = "quest letter", Flags = StockUiTrade.ExclusiveFlag },
            [Chestnut] = new ItemRecord { ItemId = Chestnut, Name = "Ronfaure Chestnut", LogName = "Ronfaure chestnut", StackSize = 12 },
        };

        private static UiMenuDefinition List(string name, short x, short y, UiAnchor anchor, int count)
        {
            var buttons = new List<UiMenuButton>();
            for (int i = 1; i <= count; i++)
            {
                buttons.Add(new UiMenuButton
                {
                    ButtonId = (short)i, X = 16, Y = (short)(6 + 16 * (i - 1)), Width = 88, Height = 16,
                    NavUp = (sbyte)(i == 1 ? count : i - 1), NavDown = (sbyte)(i == count ? 1 : i + 1), NavLeft = (sbyte)i, NavRight = (sbyte)i,
                });
            }
            return new UiMenuDefinition { Name = name, Frame = new UiMenuFrame { X = x, Y = y, Width = 112, Height = (short)(10 + 16 * count), Anchor = anchor }, Buttons = buttons };
        }

        private static UiMenuDefinition ItemList()
        {
            var buttons = new List<UiMenuButton>();
            for (int i = 1; i <= StockUiItems.ListRows; i++)
            {
                buttons.Add(new UiMenuButton
                {
                    ButtonId = (short)i, X = 3, Y = (short)(6 + 18 * (i - 1)), Width = 132, Height = 16,
                    NavUp = (sbyte)(i == 1 ? 1 : i - 1), NavDown = (sbyte)(i == StockUiItems.ListRows ? i : i + 1), NavLeft = (sbyte)i, NavRight = (sbyte)i,
                });
            }
            return new UiMenuDefinition { Name = StockUiItems.ListMenu, Frame = new UiMenuFrame { X = 16, Y = 48, Width = 182, Height = 190, HelpTextId = 3 }, Buttons = buttons };
        }

        /// <summary>"trade" / "handover" as the DAT authors them: slots 1-4 over 5-8, OK (9), Cancel (10), the gil field (11).</summary>
        private static UiMenuDefinition TradeFrame(string name)
        {
            var buttons = new List<UiMenuButton>();
            for (int i = 1; i <= 8; i++)
            {
                int c = (i - 1) % 4, r = (i - 1) / 4;
                buttons.Add(new UiMenuButton
                {
                    ButtonId = (short)i, X = (short)(7 + 34 * c), Y = (short)(22 + 34 * r), Width = 32, Height = 32,
                    NavUp = (sbyte)(r == 0 ? 11 : i - 4), NavDown = (sbyte)(r == 1 ? i : i + 4), NavLeft = (sbyte)(c == 0 ? i : i - 1),
                    NavRight = (sbyte)(c == 3 ? (r == 0 ? 9 : 10) : i + 1),
                });
            }
            buttons.Add(new UiMenuButton { ButtonId = 9, X = 143, Y = 22, Width = 34, Height = 34, NavUp = 11, NavDown = 10, NavLeft = 4, NavRight = 9 });
            buttons.Add(new UiMenuButton { ButtonId = 10, X = 143, Y = 56, Width = 34, Height = 34, NavUp = 9, NavDown = 10, NavLeft = 8, NavRight = 10 });
            buttons.Add(new UiMenuButton { ButtonId = 11, X = 76, Y = 7, Width = 84, Height = 16, NavUp = 11, NavDown = 9, NavLeft = 11, NavRight = 11 });
            return new UiMenuDefinition { Name = name, Frame = new UiMenuFrame { X = 16, Y = 48, Width = 182, Height = 94 }, Buttons = buttons };
        }

        private static UiResourceLibrary Library() => UiResourceLibrary.FromDefinitions(new[]
        {
            List(StockUiMenuEntries.MainMenu, 384, 48, UiAnchor.TopRight, 12),
            ItemList(),
            TradeFrame(StockUiTrade.Menu),
            TradeFrame(StockUiTrade.NpcMenu),
            new UiMenuDefinition
            {
                Name = StockUiShop.QuantityMenu,
                Frame = new UiMenuFrame { X = 16, Y = 240, Width = 112, Height = 56, HelpTextId = 268 },
                Buttons = new List<UiMenuButton> { new() { ButtonId = StockUiShop.QuantityField, X = 34, Y = 22, Width = 24, Height = 16, NavUp = 1, NavDown = 1, NavLeft = 1, NavRight = 1 } },
            },
            new UiMenuDefinition { Name = StockUiAuction.PriceMenu, Frame = new UiMenuFrame { X = 130, Y = 240, Width = 366, Height = 56, HelpTextId = 352 } },
            List(StockUiMenuEntries.MessageYesNoMenu, 130, 200, UiAnchor.TopLeft, 2),
        });

        private sealed class Harness
        {
            public readonly InventoryState Inventory = new();
            public readonly StockUiMenuController Menus = new() { Library = Library() };
            public readonly List<string> Notices = new();
            public readonly List<string> Errors = new();
            public readonly List<string> Sent = new();
            public StockUiTradeTarget? Target;
            public DateTime Now = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

            public readonly Dictionary<uint, StockUiTradeTarget> Known = new()
            {
                [PartnerId] = new StockUiTradeTarget(PartnerId, PartnerIndex, "Cybin", StockUiTradeTargetKind.Player),
                [0x0103_0000] = new StockUiTradeTarget(0x0103_0000, 0x406, "Tarudrake", StockUiTradeTargetKind.Player),
                [NpcId] = new StockUiTradeTarget(NpcId, NpcIndex, "Rycharde", StockUiTradeTargetKind.Npc),
                [MonsterId] = new StockUiTradeTarget(MonsterId, 0x99, "Wild Rabbit", StockUiTradeTargetKind.Other),
            };

            public Harness()
            {
                Menus.Inventory = Inventory;
                Menus.ItemLookup = id => Records.TryGetValue(id, out var r) ? r : null;
                Menus.MenuMessageText = id => id == StockUiTrade.AlreadyPlacedMenuMessage ? "Already placed in trade slot." : null;
                Menus.NoticePosted += Notices.Add;
                Menus.ErrorPosted += Errors.Add;
                Menus.TradeClock = () => Now;
                Menus.TradeTargetOf = id => id is { } serverId ? Known.TryGetValue(serverId, out var t) ? t : null : Target;
                Menus.TradeRequest = (id, index) => Record($"0x032 {id:X8} {index:X}");
                Menus.TradeRespond = (kind, counter) => Record($"0x033 {kind} {counter}");
                Menus.TradeOffer = (count, item, slot, index) => Record($"0x034 {count} {item} {slot} {index}");
                Menus.NpcTrade = (id, index, items) => Record($"0x036 {id:X8} {index:X} {string.Join(" ", items.Select(e => $"{e.Slot}:{e.Count}"))}");
                Inventory.TradeRequested += Menus.OnTradeRequested;
                Inventory.TradeResultReceived += Menus.OnTradeResult;
                Inventory.TradeChanged += Menus.OnTradeChanged;
                Inventory.SetContainerSizes(ContainerId.Inventory, 30, 30);
                Inventory.SetItem(ContainerId.Inventory, 0, InventoryState.GilItemId, 5000, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 1, FireCrystal, 9, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 2, Seashell, 12, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 3, BronzeCap, 1, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 4, Signet, 1, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 5, Chestnut, 1, ItemLockFlag.Normal);
                Inventory.SetEquip(EquipSlotId.Head, ContainerId.Inventory, 3);
            }

            private Task Record(string packet)
            {
                lock (Sent) Sent.Add(packet);
                return Task.CompletedTask;
            }

            /// <summary>Main menu > Trade with <paramref name="target"/> targeted.</summary>
            public void MainMenuTrade(StockUiTradeTarget? target)
            {
                Target = target;
                Assert.True(Menus.OpenMainMenu());
                Menus.Top!.SelectedButtonId = 7; // Trade
                Menus.Activate();
            }

            /// <summary>The server opens the trade (S2C 0x022 Start) and the window comes up.</summary>
            public StockUiOpenMenu Open()
            {
                Inventory.SetTradeStatus(TradeResultKind.Start, PartnerId, PartnerIndex);
                var window = Menus.TradeWindow;
                Assert.NotNull(window);
                return window!;
            }

            public static void Select(StockUiOpenMenu list, ushort itemId)
            {
                int index = list.ItemRows.ToList().FindIndex(r => r.ItemId == itemId);
                Assert.True(index >= 0, $"item {itemId} not listed");
                list.FirstRow = 0;
                list.SelectedButtonId = index + 1;
            }

            /// <summary>Confirm on a slot, then on an item, then (for a stack) the count.</summary>
            public void Place(StockUiOpenMenu window, int slot, ushort itemId, uint? count = null)
            {
                window.SelectedButtonId = slot;
                Menus.Activate();
                var list = Menus.Top!;
                Assert.Equal(StockUiItemListKind.Picker, list.ItemListKind);
                Select(list, itemId);
                Menus.Activate();
                if (count is { } n)
                {
                    var prompt = Menus.Top!;
                    Assert.True(prompt.IsQuantity);
                    prompt.Quantity = n;
                    Menus.Activate();
                }
            }

            public async Task AnswerPromptAsync(bool yes)
            {
                await WaitFor(() => Menus.Top?.Name == StockUiMenuEntries.MessageYesNoMenu);
                Menus.Top!.SelectedButtonId = yes ? 1 : 2;
                Menus.Activate();
            }
        }

        private static async Task WaitFor(Func<bool> condition)
        {
            for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(5);
            Assert.True(condition());
        }

        [Fact]
        public void MainMenuAndCommandMenuTrade_AreWired()
        {
            Assert.True(StockUiMenuEntries.TryGet(StockUiMenuEntries.MainMenu, 7, out var entry));
            Assert.Equal(StockUiMenuCommand.Trade, entry.Command);
            var rows = StockUiCommandMenu.Compose(new StockUiTargetContext(StockUiTargetKind.Player, PartnerId, "Cybin"));
            Assert.Equal(StockUiMenuCommand.Trade, rows.Single(r => r.Label == StockUiCommandMenu.Trade).Entry.Command);
        }

        [Fact]
        public void Trade_WithNoTargetOrAMonster_IsRefusedWithRetailLines()
        {
            var h = new Harness();
            h.MainMenuTrade(null);
            Assert.Equal("Select a target to trade with.", h.Notices.Last());
            h.Menus.CloseAll();
            h.MainMenuTrade(h.Known[MonsterId]);
            Assert.Equal("You cannot trade with that target.", h.Errors.Last());
            Assert.Empty(h.Sent);
        }

        [Fact]
        public void Trade_OnAPlayer_SendsTheRequest_AndAgainSaysAlreadySent()
        {
            var h = new Harness();
            h.MainMenuTrade(h.Known[PartnerId]);
            Assert.Equal(new[] { "0x032 01020304 405" }, h.Sent);
            Assert.Empty(h.Menus.OpenMenus);
            h.MainMenuTrade(h.Known[PartnerId]);
            Assert.Equal("You have already sent a trade request to Cybin.", h.Errors.Last());
            Assert.Single(h.Sent);
            // After LandSandBoat's 60 s the request may go out again.
            h.Now += TimeSpan.FromSeconds(61);
            h.MainMenuTrade(h.Known[PartnerId]);
            Assert.Equal(2, h.Sent.Count);
        }

        [Fact]
        public async Task Trade_OnAnotherPlayerWhileARequestIsPending_AsksToCancelItFirst()
        {
            var h = new Harness();
            h.MainMenuTrade(h.Known[PartnerId]);
            h.MainMenuTrade(h.Known[0x0103_0000]);
            await h.AnswerPromptAsync(yes: true);
            await WaitFor(() => h.Sent.Count == 3);
            Assert.Equal(new[] { "0x032 01020304 405", "0x033 Cancel 0", "0x032 01030000 406" }, h.Sent);
        }

        [Fact]
        public void IncomingRequest_PostsTheLine_AndTradeAcceptsItWhateverIsTargeted()
        {
            var h = new Harness();
            h.Inventory.ReceiveTradeRequest(PartnerId, PartnerIndex);
            Assert.Equal("Cybin wishes to trade with you.", h.Notices.Last());
            h.MainMenuTrade(null); // no target needed (round 1)
            Assert.Equal(new[] { "0x033 Start 0" }, h.Sent);
        }

        [Fact]
        public void Start_OpensTheTradeWindowAsTheRoot_WithTheInventoryBeside()
        {
            var h = new Harness();
            h.MainMenuTrade(h.Known[PartnerId]);
            Assert.True(h.Menus.OpenMainMenu());
            var window = h.Open();
            Assert.Same(window, h.Menus.OpenMenus.Single());
            Assert.Equal(StockUiTrade.Menu, window.Name);
            Assert.Equal("Cybin", window.TradePartner.Name);
            Assert.Equal(1, window.SelectedButtonId);
            Assert.Equal(new StockUiItemHelpBar("Trade", null, null, -1), window.ItemHelpBar);
            // Worn and Ex items cannot go to a player.
            Assert.True(window.CompanionRows.Single(r => r.ItemId == BronzeCap).Greyed);
            var ex = window.CompanionRows.Single(r => r.ItemId == Signet);
            Assert.True(ex.Greyed);
            Assert.Equal("You cannot trade the quest letter to Cybin.", ex.GreyedReason);
            Assert.False(window.CompanionRows.Single(r => r.ItemId == Seashell).Greyed);
        }

        [Fact]
        public void PlacingAStack_AsksTheCount_SendsTradeSlotOne_AndShowsOnceTheServerTakesIt()
        {
            var h = new Harness();
            var window = h.Open();
            h.Place(window, 1, Seashell, 5);
            Assert.Equal(new[] { "0x034 5 1193 2 1" }, h.Sent);
            Assert.Same(window, h.Menus.Top);
            Assert.Equal(2, window.SelectedButtonId);
            Assert.Null(window.TradeSlots[0]); // not until S2C 0x025
            h.Inventory.SetMyTradeItem(1, Seashell, 5, 2);
            Assert.Equal(new StockUiTradeSlot(Seashell, 5, 2), window.TradeSlots[0]);
            window.SelectedButtonId = 1;
            Assert.Equal(Seashell, window.DescribedItem);
            var placed = window.CompanionRows.Single(r => r.ItemId == Seashell);
            Assert.True(placed.Picked);
            Assert.Equal("Already placed in trade slot.", placed.GreyedReason);

            // A single item goes in at once.
            h.Place(window, 2, Chestnut);
            Assert.Equal("0x034 1 639 5 2", h.Sent.Last());

            // Confirm on a filled slot takes it back out.
            window.SelectedButtonId = 1;
            h.Menus.Activate();
            Assert.Equal("0x034 0 1193 2 1", h.Sent.Last());
        }

        [Fact]
        public void PartnerOffer_FillsTheirSide_GilFromTradeSlotZero()
        {
            var h = new Harness();
            var window = h.Open();
            h.Inventory.SetPartnerTradeItem(3, FireCrystal, 4, ReadOnlySpan<byte>.Empty, 7);
            h.Inventory.SetPartnerTradeItem(0, StockUiTrade.GilItemId, 1200, ReadOnlySpan<byte>.Empty, 8);
            Assert.Equal(FireCrystal, window.PartnerSlots[2]!.Value.ItemId);
            Assert.Equal(4u, window.PartnerSlots[2]!.Value.Count);
            Assert.Equal(1200u, window.PartnerGil);
            h.Inventory.SetPartnerTradeItem(3, StockUiTrade.GilItemId, 0, ReadOnlySpan<byte>.Empty, 9);
            Assert.Null(window.PartnerSlots[2]);
        }

        [Fact]
        public void Cursor_GoesDownIntoThePartnersSlots_ToDescribeTheirItems()
        {
            var h = new Harness();
            h.Menus.TradeOwnName = () => "Gemini";
            var window = h.Open();
            Assert.Equal("Gemini", window.TradeOwnName);
            h.Inventory.SetPartnerTradeItem(2, FireCrystal, 4, ReadOnlySpan<byte>.Empty, 1);
            // Our bottom row leads down into their top row; their slots have no art of their own and no OK / Cancel.
            var ours = window.Menu.FindButton(6)!;
            Assert.Equal(StockUiTrade.PartnerSlotBase + 5, ours.NavDown);
            Assert.Equal(StockUiTrade.PartnerSlotBase + 3, window.Menu.FindButton(StockUiTrade.CancelButton)!.NavDown);
            var theirs = window.Menu.FindButton(StockUiTrade.PartnerSlotBase + 1)!;
            Assert.Equal(6, theirs.NavUp);
            Assert.Equal(StockUiTrade.PartnerY + 22, window.Menu.Frame.Y + theirs.Y);
            Assert.Empty(theirs.Shapes);
            Assert.Null(window.Menu.FindButton(StockUiTrade.PartnerSlotBase + StockUiTrade.SlotCount));
            window.SelectedButtonId = StockUiTrade.PartnerSlotBase + 1;
            Assert.Equal(FireCrystal, window.DescribedItem);
            h.Menus.Activate(); // their slots do nothing on Confirm
            Assert.Same(window, h.Menus.Top);
            Assert.Empty(h.Sent);
        }

        [Fact]
        public void Ok_SendsMakeWithTheTradeCounter_AndAChangeWithdrawsBothOks()
        {
            var h = new Harness();
            var window = h.Open();
            h.Inventory.SetPartnerTradeItem(1, FireCrystal, 2, ReadOnlySpan<byte>.Empty, 3);
            window.SelectedButtonId = StockUiTrade.OkButton;
            h.Menus.Activate();
            Assert.Equal(new[] { "0x033 Make 3" }, h.Sent);
            Assert.True(window.TradeAccepted);
            h.Menus.Activate();
            Assert.Single(h.Sent); // once until the trade changes

            h.Inventory.SetTradeStatus(TradeResultKind.Make, PartnerId, PartnerIndex);
            Assert.True(window.PartnerAccepted);
            h.Inventory.SetPartnerTradeItem(2, Seashell, 1, ReadOnlySpan<byte>.Empty, 4);
            Assert.False(window.PartnerAccepted);
            Assert.False(window.TradeAccepted);
        }

        [Fact]
        public void Results_CloseTheWindowWithTheirLines()
        {
            var h = new Harness();
            h.Open();
            h.Inventory.SetTradeStatus(TradeResultKind.End, PartnerId, PartnerIndex);
            Assert.Null(h.Menus.TradeWindow);
            Assert.Equal("Trade complete.", h.Notices.Last());

            h.Open();
            h.Inventory.SetTradeStatus(TradeResultKind.Cancel, PartnerId, PartnerIndex);
            Assert.Null(h.Menus.TradeWindow);
            // The partner's Cancel prints what the retail client printed for ours (round 1, 2026-10-31).
            Assert.Equal("You cannot trade with Cybin at this time.", h.Errors.Last());

            h.MainMenuTrade(h.Known[PartnerId]);
            h.Inventory.SetTradeStatus(TradeResultKind.ErrYouTrade, PartnerId, PartnerIndex);
            Assert.Equal("You cannot trade with Cybin at this time.", h.Errors.Last());
            // The refused request is no longer pending: asking again sends it.
            h.MainMenuTrade(h.Known[PartnerId]);
            Assert.Equal(2, h.Sent.Count(p => p.StartsWith("0x032", StringComparison.Ordinal)));
        }

        [Fact]
        public async Task Cancel_AsksFirst_ThenSendsCancelAndCloses()
        {
            var h = new Harness();
            var window = h.Open();
            h.Menus.CloseTop();
            await h.AnswerPromptAsync(yes: false);
            await WaitFor(() => ReferenceEquals(h.Menus.Top, window));
            Assert.Empty(h.Sent);

            window.SelectedButtonId = StockUiTrade.CancelButton;
            h.Menus.Activate();
            await h.AnswerPromptAsync(yes: true);
            await WaitFor(() => h.Sent.Count == 1);
            Assert.Equal("0x033 Cancel 0", h.Sent.Single());
            Assert.Null(h.Menus.TradeWindow);
        }

        [Fact]
        public void Gil_IsTypedInThePriceWindow_AndGoesOutOnTradeSlotZero()
        {
            var h = new Harness();
            var window = h.Open();
            window.SelectedButtonId = StockUiTrade.GilButton;
            h.Menus.Activate();
            Assert.Equal(StockUiAuction.PriceMenu, h.Menus.Top!.Name);
            Assert.True(h.Menus.MacroField.IsOpen);
            h.Menus.MacroField.InsertText("1500");
            h.Menus.MacroField.Commit();
            Assert.Equal(new[] { "0x034 1500 65535 0 0" }, h.Sent);
            Assert.Same(window, h.Menus.Top);
            h.Inventory.SetMyTradeItem(0, StockUiTrade.GilItemId, 1500, 0);
            Assert.Equal(1500u, window.TradeGil);
        }

        [Fact]
        public void NpcTrade_OpensTheHandOverWindow_AndOkSendsItemTransferWithTheGilFirst()
        {
            var h = new Harness();
            h.MainMenuTrade(h.Known[NpcId]);
            var window = h.Menus.TradeWindow!;
            Assert.Equal(StockUiTrade.NpcMenu, window.Name);
            Assert.True(window.IsNpcTrade);
            Assert.Same(h.Menus.OpenMenus[0], window.Parent);
            // Ex items may go to an NPC (quest items).
            Assert.False(window.CompanionRows.Single(r => r.ItemId == Signet).Greyed);

            h.Place(window, 1, Signet);
            h.Place(window, 2, Seashell, 3);
            Assert.Equal(new StockUiTradeSlot(Signet, 1, 4), window.TradeSlots[0]);
            Assert.Equal(new StockUiTradeSlot(Seashell, 3, 2), window.TradeSlots[1]);
            window.SelectedButtonId = StockUiTrade.GilButton;
            h.Menus.Activate();
            h.Menus.MacroField.InsertText("100");
            h.Menus.MacroField.Commit();
            Assert.Equal(100u, window.TradeGil);
            Assert.Empty(h.Sent);

            window.SelectedButtonId = StockUiTrade.OkButton;
            h.Menus.Activate();
            Assert.Equal(new[] { "0x036 01100042 42 0:100 4:1 2:3" }, h.Sent);
            // The windows close back to the main menu.
            Assert.Equal(StockUiMenuEntries.MainMenu, h.Menus.Top!.Name);
        }

        [Fact]
        public void NpcTrade_GilIsCappedAtTheGilHeld_AndAnEmptyOkSendsNothing()
        {
            var h = new Harness();
            h.MainMenuTrade(h.Known[NpcId]);
            var window = h.Menus.TradeWindow!;
            window.SelectedButtonId = StockUiTrade.OkButton;
            h.Menus.Activate();
            Assert.Empty(h.Sent);
            window.SelectedButtonId = StockUiTrade.GilButton;
            h.Menus.Activate();
            h.Menus.MacroField.InsertText("999999");
            h.Menus.MacroField.Commit();
            Assert.Equal(5000u, window.TradeGil);
        }

        [Fact]
        public void ZoneChange_ClosesTheWindow()
        {
            var h = new Harness();
            h.Open();
            h.Menus.CloseTrade();
            Assert.Null(h.Menus.TradeWindow);
        }

        [Fact]
        public void TradePackets_MatchLandSandBoatsLayout()
        {
            Span<byte> buf = stackalloc byte[64];
            Assert.Equal(12, InventoryPacketBuilders.BuildTradeList(buf, 1, 1500, StockUiTrade.GilItemId, 0, StockUiTrade.GilTradeIndex));
            Assert.Equal(new byte[] { 0xDC, 0x05, 0, 0, 0xFF, 0xFF, 0, 0 }, buf.Slice(4, 8).ToArray());
            var entries = StockUiTrade.NpcEntries(new StockUiTradeSlot?[] { null, new StockUiTradeSlot(Seashell, 3, 2), null, null, null, null, null, null }, 100);
            Assert.Equal(64, InventoryPacketBuilders.BuildItemTransfer(buf, 1, NpcId, NpcIndex, entries));
            Assert.Equal(100u, BitConverter.ToUInt32(buf.Slice(8, 4)));     // ItemNumTbl[0]: the gil
            Assert.Equal(3u, BitConverter.ToUInt32(buf.Slice(12, 4)));      // ItemNumTbl[1]
            Assert.Equal(0, buf[4 + 44]);                                    // PropertyItemIndexTbl[0]: gil is index 0
            Assert.Equal(2, buf[4 + 45]);
            Assert.Equal(NpcIndex, BitConverter.ToUInt16(buf.Slice(4 + 54, 2)));
            Assert.Equal(2, buf[4 + 56]);                                    // ItemNum
        }

        [Fact]
        public void RetailTable_TradeLinesMatchTheFallbacks()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var messages = new Gordian.Engine.Events.ClientMessageController(new Gordian.Core.Resources.Tables.ClientMessageTables(rm.LoadDatBytesByFileId), _ => null);
            foreach (int id in new[] { StockUiTrade.WishesToTrade, StockUiTrade.AlreadyRequested, StockUiTrade.CannotTradeNow, StockUiTrade.CannotTradeTarget,
                StockUiTrade.SelectTarget, StockUiTrade.TradeComplete, StockUiTrade.AlreadyInWindow })
            {
                var lines = messages.FormatTableMessage(id, Array.Empty<int>(), new[] { "Cybin" });
                Assert.NotNull(lines);
                Assert.Equal(StockUiTrade.FallbackText(id, "Cybin"), string.Join(" ", lines!).Trim());
            }
        }

        [Fact]
        public void RetailMenus_TradeWindowsResolve()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            Assert.NotNull(library);
            foreach (string name in new[] { StockUiTrade.Menu, StockUiTrade.NpcMenu })
            {
                Assert.True(library!.TryGetMenu(name, out var menu));
                Assert.Equal((16, 48, 182, 94), ((int)menu.Frame.X, (int)menu.Frame.Y, (int)menu.Frame.Width, (int)menu.Frame.Height));
                for (int id = 1; id <= StockUiTrade.SlotCount; id++) Assert.Equal((32, 32), ((int)menu.FindButton(id)!.Width, (int)menu.FindButton(id)!.Height));
                Assert.NotNull(menu.FindButton(StockUiTrade.OkButton));
                Assert.NotNull(menu.FindButton(StockUiTrade.CancelButton));
                Assert.Equal((76, 7, 84), ((int)menu.FindButton(StockUiTrade.GilButton)!.X, (int)menu.FindButton(StockUiTrade.GilButton)!.Y, (int)menu.FindButton(StockUiTrade.GilButton)!.Width));
                // OK's lit alternate (kind 3) is what the window draws once OK is pressed.
                Assert.Contains(menu.FindButton(StockUiTrade.OkButton)!.Shapes, s => s.Kind == 3);
            }
            Assert.True(library!.TryGetMenu(StockUiAuction.PriceMenu, out _));
        }
    }
}
