// tests/Gordian.Core.Tests/Ui/StockUiSynthesisTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Animation;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Ui
{
    /// <summary>The Synthesis windows, the History and the guild shop window (#330), and the synthesis posture (#315).</summary>
    public class StockUiSynthesisTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        private const ushort FireCrystal = 4096, WindCrystal = 4098, LightningCrystal = 4100, ZincOre = 642, Seashell = 1193,
            ZincOxide = 4150, BronzeCap = 12448, ShellPowder = 4152, Chestnut = 639;

        private static readonly Dictionary<ushort, ItemRecord> Records = new()
        {
            [FireCrystal] = new ItemRecord { ItemId = FireCrystal, Name = "Fire Crystal", LogName = "fire crystal", StackSize = 12 },
            [WindCrystal] = new ItemRecord { ItemId = WindCrystal, Name = "Wind Crystal", LogName = "wind crystal", StackSize = 12 },
            [LightningCrystal] = new ItemRecord { ItemId = LightningCrystal, Name = "Lightng. Crystal", LogName = "lightning crystal", StackSize = 12 },
            [ZincOre] = new ItemRecord { ItemId = ZincOre, Name = "Zinc Ore", LogName = "chunk of zinc ore", LogPlural = "chunks of zinc ore", StackSize = 12 },
            [Seashell] = new ItemRecord { ItemId = Seashell, Name = "Seashell", LogName = "seashell", StackSize = 12 },
            [ZincOxide] = new ItemRecord { ItemId = ZincOxide, Name = "Zinc Oxide", LogName = "pinch of zinc oxide", StackSize = 12 },
            [ShellPowder] = new ItemRecord { ItemId = ShellPowder, Name = "Shell Powder", LogName = "ponze of shell powder", StackSize = 12 },
            [BronzeCap] = new ItemRecord { ItemId = BronzeCap, Name = "Bronze Cap", LogName = "bronze cap", EquipSlotsMask = 1 << (int)EquipSlotId.Head, ItemType = 5 },
            [Chestnut] = new ItemRecord { ItemId = Chestnut, Name = "Ronfaure Chestnut", LogName = "Ronfaure chestnut", StackSize = 12 },
        };

        private static UiMenuDefinition List(string name, short x, short y, UiAnchor anchor, int count, short help = -1)
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
            return new UiMenuDefinition { Name = name, Frame = new UiMenuFrame { X = x, Y = y, Width = 112, Height = (short)(10 + 16 * count), Anchor = anchor, HelpTextId = help }, Buttons = buttons };
        }

        /// <summary>An "inventor"-like list: ten rows at an 18 px pitch, the end rows linked to themselves.</summary>
        private static UiMenuDefinition ItemList(string name, short help)
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
            return new UiMenuDefinition { Name = name, Frame = new UiMenuFrame { X = 16, Y = 48, Width = 182, Height = 190, Anchor = UiAnchor.TopLeft, HelpTextId = help }, Buttons = buttons };
        }

        /// <summary>"tskill1" as the DAT authors it: slots 1-4 over 5-8, OK (9) and Cancel (10) at the right.</summary>
        private static UiMenuDefinition Grid()
        {
            var buttons = new List<UiMenuButton>();
            for (int i = 1; i <= 8; i++)
            {
                int c = (i - 1) % 4, r = (i - 1) / 4;
                buttons.Add(new UiMenuButton
                {
                    ButtonId = (short)i, X = (short)(7 + 34 * c), Y = (short)(22 + 34 * r), Width = 32, Height = 32,
                    NavUp = (sbyte)(r == 0 ? i : i - 4), NavDown = (sbyte)(r == 1 ? i : i + 4), NavLeft = (sbyte)(c == 0 ? i : i - 1),
                    NavRight = (sbyte)(c == 3 ? (r == 0 ? 9 : 10) : i + 1),
                });
            }
            buttons.Add(new UiMenuButton { ButtonId = 9, X = 143, Y = 22, Width = 34, Height = 34, NavUp = 9, NavDown = 10, NavLeft = 4, NavRight = 9, HelpTextId = 493 });
            buttons.Add(new UiMenuButton { ButtonId = 10, X = 143, Y = 56, Width = 34, Height = 34, NavUp = 9, NavDown = 10, NavLeft = 8, NavRight = 10, HelpTextId = 379 });
            return new UiMenuDefinition { Name = StockUiSynthesis.GridMenu, Frame = new UiMenuFrame { X = 16, Y = 48, Width = 182, Height = 94, Anchor = UiAnchor.TopLeft, HelpTextId = 492 }, Buttons = buttons };
        }

        private static UiMenuDefinition QuantityFrame() => new()
        {
            Name = StockUiShop.QuantityMenu,
            Frame = new UiMenuFrame { X = 16, Y = 240, Width = 112, Height = 56, Anchor = UiAnchor.TopLeft, HelpTextId = 268 },
            Buttons = new List<UiMenuButton>
            {
                new() { ButtonId = StockUiShop.QuantityField, X = 34, Y = 22, Width = 24, Height = 16, NavUp = 1, NavDown = 1, NavLeft = 1, NavRight = 1 },
            },
        };

        private static UiResourceLibrary Library() => UiResourceLibrary.FromDefinitions(new[]
        {
            List(StockUiMenuEntries.MainMenu, 384, 48, UiAnchor.TopRight, 12),
            List(StockUiSynthesis.Menu, 384, 48, UiAnchor.TopRight, 2),
            ItemList(StockUiItems.ListMenu, 3),
            ItemList(StockUiSynthesis.HistoryListMenu, 3),
            Grid(),
            List(StockUiSynthesis.OptionsMenu, 384, 48, UiAnchor.TopRight, 2),
            List(StockUiItems.ActionMenu, 384, 48, UiAnchor.TopRight, 2),
            QuantityFrame(),
            List(StockUiGuildShop.MenuName, 16, 256, UiAnchor.BottomLeft, 2),
            ItemList(StockUiShop.ListMenu, -1),
        });

        private sealed class Harness
        {
            public readonly InventoryState Inventory = new();
            public readonly StockUiMenuController Menus = new() { Library = Library() };
            public readonly SynthesisHistory History = new();
            public readonly List<string> Notices = new();
            public readonly List<string> Errors = new();
            public readonly List<(ushort Crystal, byte Slot, (ushort ItemId, byte Slot)[] Items)> Synths = new();
            public readonly List<string> GuildSent = new();
            public bool Synthesizing;

            public Harness()
            {
                Menus.Inventory = Inventory;
                Menus.ItemLookup = id => Records.TryGetValue(id, out var r) ? r : null;
                Menus.MenuHelpText = id => id == StockUiSynthesis.HelpSelectMaterials ? "Select materials for synthesis." : null;
                Menus.NoticePosted += Notices.Add;
                Menus.ErrorPosted += Errors.Add;
                Menus.SynthesisHistory = () => History;
                Menus.IsSynthesizing = () => Synthesizing;
                Menus.SynthesisRequested = (crystal, slot, items) =>
                {
                    Synths.Add((crystal, slot, items));
                    return Task.FromResult(true);
                };
                Menus.GuildRequestBuyList = () => { GuildSent.Add("buylist"); return Task.CompletedTask; };
                Menus.GuildRequestSellList = () => { GuildSent.Add("selllist"); return Task.CompletedTask; };
                Menus.GuildBuy = (item, count) => { GuildSent.Add($"buy {item} {count}"); return Task.CompletedTask; };
                Menus.GuildSell = (item, slot, count) => { GuildSent.Add($"sell {item} {slot} {count}"); return Task.CompletedTask; };
                Inventory.SetContainerSizes(ContainerId.Inventory, 30, 30);
                Inventory.SetItem(ContainerId.Inventory, 0, InventoryState.GilItemId, 5000, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 1, FireCrystal, 9, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 2, WindCrystal, 11, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 3, LightningCrystal, 8, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 4, ZincOre, 9, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 5, Seashell, 12, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 6, BronzeCap, 1, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 7, Seashell, 2, ItemLockFlag.Normal);
                Inventory.SetEquip(EquipSlotId.Head, ContainerId.Inventory, 6);
            }

            public StockUiOpenMenu OpenSynthesisMenu()
            {
                Assert.True(Menus.OpenMainMenu());
                Menus.Top!.SelectedButtonId = 10; // Synthesis
                Menus.Activate();
                Assert.Equal(StockUiSynthesis.Menu, Menus.Top!.Name);
                return Menus.Top!;
            }

            public StockUiOpenMenu OpenGrid(ushort crystal)
            {
                var menu = OpenSynthesisMenu();
                menu.SelectedButtonId = StockUiSynthesis.CrystalSynthesisButton;
                Menus.Activate();
                var list = Menus.Top!;
                Select(list, crystal);
                Menus.Activate();
                Assert.True(Menus.Top!.IsSynthesisGrid);
                return Menus.Top!;
            }

            public static void Select(StockUiOpenMenu list, ushort itemId)
            {
                int index = list.ItemRows.ToList().FindIndex(r => r.ItemId == itemId);
                Assert.True(index >= 0, $"item {itemId} not listed");
                list.FirstRow = 0;
                list.SelectedButtonId = index + 1;
            }

            /// <summary>Confirm on a grid slot, then on a material, then (for a stack) the count.</summary>
            public void Place(StockUiOpenMenu grid, int slot, ushort itemId, uint? count = null)
            {
                grid.SelectedButtonId = slot;
                Menus.Activate();
                var list = Menus.Top!;
                Assert.Equal(StockUiItemListKind.SynthesisMaterials, list.ItemListKind);
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
        }

        [Fact]
        public void MainMenuSynthesis_OpensCrystalSynthesisAndHistory()
        {
            var h = new Harness();
            var menu = h.OpenSynthesisMenu();
            Assert.True(StockUiMenuEntries.TryGet(StockUiSynthesis.Menu, 1, out var crystal));
            Assert.Equal(StockUiMenuCommand.SynthesisCrystals, crystal.Command);
            Assert.True(StockUiMenuEntries.TryGet(StockUiSynthesis.Menu, 2, out var history));
            Assert.Equal(StockUiMenuCommand.SynthesisHistory, history.Command);
            Assert.Same(h.Menus.OpenMenus[0], menu.Parent);
        }

        [Fact]
        public void CrystalSynthesis_ListsOnlyTheCrystals_WithItemsCountOverTheInventorySize()
        {
            var h = new Harness();
            var menu = h.OpenSynthesisMenu();
            menu.SelectedButtonId = StockUiSynthesis.CrystalSynthesisButton;
            h.Menus.Activate();
            var list = h.Menus.Top!;
            Assert.Equal(StockUiItemListKind.Crystals, list.ItemListKind);
            Assert.Equal(new ushort[] { FireCrystal, WindCrystal, LightningCrystal }, list.ItemRows.Select(r => r.ItemId));
            // "Items 3/30 Select an item." (Crystal_Synthesis.png).
            Assert.Equal(new StockUiItemHelpBar("Items", 3, 30, StockUiItems.HelpSelectItem), list.ItemHelpBar);
            // The right of the screen is left empty: the Synthesis menu there is hidden.
            Assert.True(list.OverlapsAuthored(menu));
        }

        [Fact]
        public void Grid_ShowsTheCrystalAndTheInventoryBeside_TheCrystalInUseAndWornItemsGreyed()
        {
            var h = new Harness();
            var grid = h.OpenGrid(WindCrystal);
            Assert.Equal(new StockUiSynthesisCrystal(WindCrystal, 2), grid.SynthesisCrystal);
            Assert.Equal("Carve", StockUiSynthesis.ElementWord(StockUiSynthesis.ElementOf(grid.SynthesisCrystal.ItemId)));
            Assert.Equal(1, grid.SelectedButtonId);
            Assert.Equal(new StockUiItemHelpBar("Synthesis", null, null, 492), grid.ItemHelpBar);
            var wind = grid.CompanionRows.Single(r => r.ItemId == WindCrystal);
            Assert.True(wind.Greyed);
            Assert.False(grid.CompanionRows.Single(r => r.ItemId == FireCrystal).Greyed);
            Assert.True(grid.CompanionRows.Single(r => r.ItemId == BronzeCap).Greyed);
        }

        [Fact]
        public void PlacingAStack_AsksTheCount_ThenMovesToTheNextSlot()
        {
            var h = new Harness();
            var grid = h.OpenGrid(WindCrystal);
            grid.SelectedButtonId = 1;
            h.Menus.Activate();
            var list = h.Menus.Top!;
            // "Items Select materials for synthesis." (the video).
            Assert.Equal(new StockUiItemHelpBar("Items", null, null, StockUiSynthesis.HelpSelectMaterials), list.ItemHelpBar);
            Harness.Select(list, Seashell);
            h.Menus.Activate();
            var prompt = h.Menus.Top!;
            Assert.True(prompt.IsQuantity);
            Assert.Equal(1u, prompt.Quantity);
            Assert.Equal(8u, prompt.QuantityMax); // 14 held, eight items at most
            Assert.Equal(12u, prompt.QuantityTotal); // "1 /12" in the video
            Assert.Equal("No. of Items", prompt.Parent!.ItemHelpBar!.Value.Title);
            prompt.Quantity = 3;
            h.Menus.Activate();

            Assert.Same(grid, h.Menus.Top);
            Assert.Equal(new StockUiSynthesisSlot(Seashell, 3, 5), grid.SynthesisSlots[0]);
            Assert.Equal(2, grid.SelectedButtonId);
            Assert.True(grid.CompanionRows.Single(r => r.ItemId == Seashell && r.Slot == 5).Picked);
            grid.SelectedButtonId = 1;
            Assert.Equal(Seashell, grid.DescribedItem);
        }

        [Fact]
        public void PlacedItems_LimitTheNextCount_ToEightItemsAndWhatIsHeld()
        {
            var h = new Harness();
            var grid = h.OpenGrid(FireCrystal);
            h.Place(grid, 1, ZincOre, 6);
            h.Place(grid, 2, ZincOre, 2);
            Assert.Equal(new StockUiSynthesisSlot(ZincOre, 2, 4), grid.SynthesisSlots[1]);
            // Eight placed: nothing more fits.
            grid.SelectedButtonId = 3;
            h.Menus.Activate();
            Harness.Select(h.Menus.Top!, Seashell);
            h.Menus.Activate();
            Assert.Contains("No more materials can be added.", h.Notices);
        }

        [Fact]
        public void ConfirmOnAFilledSlot_EmptiesIt()
        {
            var h = new Harness();
            var grid = h.OpenGrid(FireCrystal);
            h.Place(grid, 1, ZincOre, 3);
            grid.SelectedButtonId = 1;
            h.Menus.Activate();
            Assert.Null(grid.SynthesisSlots[0]);
            Assert.Same(grid, h.Menus.Top);
        }

        [Fact]
        public async Task Ok_SendsTheSynthesis_FromSeveralStacks_AndClosesBackToTheMainMenu()
        {
            var h = new Harness();
            var grid = h.OpenGrid(WindCrystal);
            h.Place(grid, 1, Seashell, 3);
            grid.SelectedButtonId = StockUiSynthesis.OkButton;
            h.Menus.Activate();
            await Task.Delay(50, TestContext.Current.CancellationToken);
            var sent = Assert.Single(h.Synths);
            Assert.Equal((WindCrystal, (byte)2), (sent.Crystal, sent.Slot));
            Assert.Equal(new[] { (Seashell, (byte)5), (Seashell, (byte)5), (Seashell, (byte)5) }, sent.Items);
            // Back to the main menu (the video: "Items / View current inventory." after OK).
            Assert.Single(h.Menus.OpenMenus);
            Assert.Equal(StockUiMenuEntries.MainMenu, h.Menus.Top!.Name);
        }

        [Fact]
        public void Expand_TakesFromTheSlotsOwnStackFirst_ThenTheOthers_AndLeavesTheCrystal()
        {
            var inventory = new InventoryState();
            inventory.SetItem(ContainerId.Inventory, 1, FireCrystal, 2, ItemLockFlag.Normal);
            inventory.SetItem(ContainerId.Inventory, 2, Seashell, 2, ItemLockFlag.Normal);
            inventory.SetItem(ContainerId.Inventory, 3, Seashell, 5, ItemLockFlag.Normal);
            var slots = new StockUiSynthesisSlot?[8];
            slots[0] = new StockUiSynthesisSlot(Seashell, 4, 2);
            slots[1] = new StockUiSynthesisSlot(FireCrystal, 1, 1); // a crystal as an ingredient: the crystal's own stack gives one less
            var items = StockUiSynthesis.Expand(inventory, new StockUiSynthesisCrystal(FireCrystal, 1), slots, out _)!;
            Assert.Equal(new[] { (Seashell, (byte)2), (Seashell, (byte)2), (Seashell, (byte)3), (Seashell, (byte)3), (FireCrystal, (byte)1) }, items);

            slots[1] = new StockUiSynthesisSlot(FireCrystal, 2, 1);
            Assert.Null(StockUiSynthesis.Expand(inventory, new StockUiSynthesisCrystal(FireCrystal, 1), slots, out ushort shortItem));
            Assert.Equal(FireCrystal, shortItem);
        }

        [Fact]
        public void WhileSynthesizing_TheGridIsRefused_WithRetailsLine()
        {
            var h = new Harness();
            var grid = h.OpenGrid(FireCrystal);
            h.Place(grid, 1, ZincOre, 3);
            h.Synthesizing = true;
            grid.SelectedButtonId = StockUiSynthesis.OkButton;
            h.Menus.Activate();
            Assert.Empty(h.Synths);
            Assert.Equal(StockUiSynthesis.LockedMessage, Assert.Single(h.Errors));
        }

        [Fact]
        public void ItemsWindowUse_OnACrystal_OpensTheGridOverTheList()
        {
            var h = new Harness();
            Assert.True(h.Menus.OpenMainMenu());
            var items = h.Menus.OpenItems(h.Menus.Top);
            Harness.Select(items!, LightningCrystal);
            h.Menus.Activate(); // Use / Drop
            h.Menus.Activate(); // Use
            var grid = h.Menus.Top!;
            Assert.True(grid.IsSynthesisGrid);
            Assert.Same(items, grid.Parent);
            Assert.Equal("Split", StockUiSynthesis.ElementWord(StockUiSynthesis.ElementOf(grid.SynthesisCrystal.ItemId)));
            Assert.DoesNotContain(h.Menus.OpenMenus, m => m.ItemAction != null);
        }

        [Fact]
        public void History_ShowsTheRecipeUnderTheCursor_FavoritesFirst_AndSynthesizesFromIt()
        {
            var h = new Harness();
            h.History.Record(new SynthesisRequest(FireCrystal, new[] { ZincOre, ZincOre, ZincOre }), ZincOxide);
            h.History.Record(new SynthesisRequest(WindCrystal, new[] { Seashell, Seashell, Seashell }), ShellPowder);
            var menu = h.OpenSynthesisMenu();
            menu.SelectedButtonId = StockUiSynthesis.HistoryButton;
            h.Menus.Activate();
            var list = h.Menus.Top!;
            Assert.Equal(StockUiItemListKind.SynthesisHistory, list.ItemListKind);
            Assert.Equal(new ushort[] { ShellPowder, ZincOxide }, list.ItemRows.Select(r => r.ItemId));
            // "Synthesis / Synthesize this combination of items." (Synthesis_History.png).
            Assert.Equal(new StockUiItemHelpBar("Synthesis", null, null, StockUiSynthesis.HelpSynthesize), list.ItemHelpBar);
            var grid = list.Parent!;
            Assert.True(grid.IsSynthesisHistory);
            Assert.Equal(new StockUiSynthesisSlot(Seashell, 3, 5), grid.SynthesisSlots[0]);

            h.Menus.Move(Gordian.Core.Input.InputAction.MenuDown);
            Assert.Equal(ZincOxide, grid.SynthesisRecipe!.ResultItemId);
            Assert.Equal(new StockUiSynthesisCrystal(FireCrystal, 1), grid.SynthesisCrystal);
            Assert.Equal(new StockUiSynthesisSlot(ZincOre, 3, 4), grid.SynthesisSlots[0]);

            // Favorite: the zinc oxide recipe goes first and the cursor stays on it.
            Assert.True(h.Menus.ToggleSynthesisOptions());
            h.Menus.Top!.SelectedButtonId = StockUiSynthesis.FavoriteButton;
            h.Menus.Activate();
            Assert.Equal(new ushort[] { ZincOxide, ShellPowder }, list.ItemRows.Select(r => r.ItemId));
            Assert.Equal(ZincOxide, list.SelectedItemRow!.Value.ItemId);

            // Confirm puts the cursor on OK; Cancel there brings the list back; OK synthesizes.
            h.Menus.Activate();
            Assert.Same(grid, h.Menus.Top);
            Assert.Equal(StockUiSynthesis.OkButton, grid.SelectedButtonId);
            Assert.Equal(ZincOxide, grid.DescribedItem);
            h.Menus.CloseTop();
            Assert.Equal(StockUiItemListKind.SynthesisHistory, h.Menus.Top!.ItemListKind);
            h.Menus.Activate();
            h.Menus.Activate();
            Assert.Equal((FireCrystal, (byte)1), (h.Synths.Single().Crystal, h.Synths.Single().Slot));
            Assert.Equal(3, h.Synths.Single().Items.Length);
        }

        [Fact]
        public void HistoryList_Cancel_LeavesHistory()
        {
            var h = new Harness();
            var menu = h.OpenSynthesisMenu();
            menu.SelectedButtonId = StockUiSynthesis.HistoryButton;
            h.Menus.Activate();
            h.Menus.CloseTop();
            Assert.Same(menu, h.Menus.Top);
        }

        [Fact]
        public void History_RecordsSuccessesOnce_KeepsFavorites_AndRoundTripsThroughJson()
        {
            var history = new SynthesisHistory();
            history.Record(new SynthesisRequest(FireCrystal, new[] { ZincOre, ZincOre, ZincOre }), ZincOxide);
            history.Record(new SynthesisRequest(WindCrystal, new[] { Seashell, Seashell, Seashell }), ShellPowder);
            Assert.True(history.SetFavorite(history.Entries[1], true));
            history.Record(new SynthesisRequest(FireCrystal, new[] { ZincOre, ZincOre, ZincOre }), ZincOxide);
            Assert.Equal(2, history.Entries.Count);
            Assert.True(history.Entries[0].Favorite);
            Assert.Equal(new[] { new SynthesisIngredient(ZincOre, 3) }, history.Entries[0].Ingredients);
            var copy = SynthesisHistory.FromJson(history.ToJson());
            Assert.Equal(history.Entries.Select(e => (e.CrystalItemId, e.ResultItemId, e.Favorite)), copy.Entries.Select(e => (e.CrystalItemId, e.ResultItemId, e.Favorite)));
            history.Record(new SynthesisRequest(FireCrystal, Array.Empty<ushort>()), ZincOxide); // nothing to record
            Assert.Equal(2, history.Entries.Count);

            // A break (round 1, 2026-10-18) is recorded too: its result unknown (0) until a success names it, and a break of a
            // recipe that once succeeded keeps that result. The list shows a broken-only recipe by its first ingredient.
            history.Record(new SynthesisRequest(LightningCrystal, new[] { Seashell }), 0);
            Assert.Equal((LightningCrystal, (ushort)0), (history.Entries[1].CrystalItemId, history.Entries[1].ResultItemId));
            Assert.Equal(Seashell, StockUiSynthesis.HistoryRows(history.Entries, null)[1].ItemId);
            history.Record(new SynthesisRequest(FireCrystal, new[] { ZincOre, ZincOre, ZincOre }), 0);
            Assert.Equal(ZincOxide, history.Entries.Single(e => e.CrystalItemId == FireCrystal).ResultItemId);
            Assert.Equal(3, SynthesisHistory.FromJson(history.ToJson()).Entries.Count);
        }

        [Fact]
        public async Task CraftingModule_RemembersTheLastRequest_ForTheHistory()
        {
            var state = new CraftingState();
            var module = new CraftingPacketModule(state, (_, _) => Task.CompletedTask);
            Assert.True(await module.SynthesizeAsync(FireCrystal, 1, new[] { (ZincOre, (byte)4), (ZincOre, (byte)4) }));
            Assert.Equal(FireCrystal, state.LastRequest!.CrystalItemId);
            Assert.Equal(new[] { ZincOre, ZincOre }, state.LastRequest.IngredientItemIds);
            Assert.True(state.IsSynthesizing);
        }

        [Fact]
        public void ElementOfCrystals_CoversTheHqCrystals()
        {
            Assert.Equal(0, StockUiSynthesis.ElementOf(4096));
            Assert.Equal(4, StockUiSynthesis.ElementOf(4242));
            Assert.Equal(7, StockUiSynthesis.ElementOf(6513));
            Assert.Equal(-1, StockUiSynthesis.ElementOf(4104)); // a cluster
            Assert.True(StockUiItems.IsCrystal(6506));
            Assert.Equal("Heat", StockUiSynthesis.ElementWord(0));
        }

        [Fact]
        public void GuildShop_OpensOnTheOpenStatus_ListsTheStock_AndBuys()
        {
            var h = new Harness();
            h.Menus.OnGuildStatus(ShopOpenStatus.Open);
            Assert.True(h.Menus.IsGuildShopOpen);
            Assert.Equal(StockUiGuildShop.MenuName, h.Menus.Top!.Name);
            h.Menus.Top.SelectedButtonId = StockUiGuildShop.BuyButton;
            h.Menus.Activate();
            Assert.Equal("buylist", Assert.Single(h.GuildSent));
            var list = h.Menus.Top!;
            Assert.True(list.IsGuild && list.IsShopList);
            Assert.Empty(list.ShopRows);

            h.Inventory.AddGuildBuyItems(0, new[] { new GuildItemEntry(ZincOre, 20, 40, 120), new GuildItemEntry(Seashell, 0, 40, 30) });
            h.Inventory.CompleteGuildList(sells: true);
            h.Menus.OnGuildListChanged();
            Assert.Equal(new ushort[] { ZincOre, Seashell }, list.ShopRows.Select(r => r.ItemId));
            Assert.True(list.ShopRows[1].Greyed); // out of stock

            list.SelectedButtonId = 1;
            h.Menus.Activate();
            var prompt = h.Menus.Top!;
            Assert.True(prompt.IsQuantity && prompt.IsGuild);
            Assert.Equal(12u, prompt.QuantityMax); // the stack (5000 gil buys 41, the guild has 20)
            prompt.Quantity = 5;
            h.Menus.Activate();
            SpinUntil(() => h.GuildSent.Contains($"buy {ZincOre} 5"));
            h.Menus.OnGuildTransaction();
            SpinUntil(() => h.GuildSent.Count(s => s == "buylist") == 2);

            list.SelectedButtonId = 2;
            h.Menus.Activate();
            Assert.Contains("The guild is out of Seashell.", h.Notices);
        }

        [Fact]
        public void GuildShop_SellList_ShowsEverythingTheGuildBuys_AndSellsFromTheInventory()
        {
            var h = new Harness();
            h.Menus.OnGuildStatus(ShopOpenStatus.Open);
            h.Menus.Top!.SelectedButtonId = StockUiGuildShop.SellButton;
            h.Menus.Activate();
            var list = h.Menus.Top!;
            h.Inventory.AddGuildSellItems(0, new[] { new GuildItemEntry(Seashell, 3, 40, 12), new GuildItemEntry(Chestnut, 0, 40, 5), new GuildItemEntry(ZincOre, 40, 40, 90) });
            h.Menus.OnGuildListChanged();
            // image-5 (retail): "Select from your inventory." first, then every item the guild buys with its price, held or not.
            Assert.Equal(StockUiGuildShop.SelectFromInventoryText, list.ShopRows[0].Name);
            Assert.Equal(new ushort[] { 0, Seashell, Chestnut, ZincOre }, list.ShopRows.Select(r => r.ItemId));
            Assert.Equal((12u, (byte)5, 12u, false), (list.ShopRows[1].Count, list.ShopRows[1].Slot, list.ShopRows[1].Price, list.ShopRows[1].Greyed));
            Assert.Equal((5u, true), (list.ShopRows[2].Price, list.ShopRows[2].Greyed)); // no chestnuts held
            Assert.True(list.ShopRows[3].Greyed); // the guild's stock of zinc ore is full
            Assert.Equal(1, list.SelectedButtonId);

            list.SelectedButtonId = 3;
            h.Menus.Activate();
            Assert.Contains("You do not have any Ronfaure chestnuts.", h.Notices);

            // A held row sells from its largest stack.
            list.SelectedButtonId = 2;
            h.Menus.Activate();
            h.Menus.Top!.Quantity = 4;
            h.Menus.Activate();
            SpinUntil(() => h.GuildSent.Contains($"sell {Seashell} 5 4"));

            // "Select from your inventory." lists the inventory's items the guild buys; Confirm asks the count.
            list.SelectedButtonId = 1;
            h.Menus.Activate();
            var inventory = h.Menus.Top!;
            Assert.Equal(StockUiItemListKind.GuildSell, inventory.ItemListKind);
            Assert.Equal(new ushort[] { ZincOre, Seashell, Seashell }, inventory.ItemRows.Select(r => r.ItemId));
            Assert.True(inventory.ItemRows[0].Greyed);
            inventory.SelectedButtonId = 3; // the stack of two in slot 7
            h.Menus.Activate();
            var prompt = h.Menus.Top!;
            Assert.Equal(2u, prompt.QuantityMax);
            prompt.Quantity = 2;
            h.Menus.Activate();
            SpinUntil(() => h.GuildSent.Contains($"sell {Seashell} 7 2"));

            h.Menus.CloseGuildShop();
            Assert.False(h.Menus.IsGuildShopOpen);
            Assert.False(h.Menus.IsOpen);
        }

        [Fact]
        public void GuildShop_CancelOnTheWindowEndsIt()
        {
            var h = new Harness();
            h.Menus.OnGuildStatus(ShopOpenStatus.Open);
            h.Menus.CloseTop();
            Assert.False(h.Menus.IsGuildShopOpen);
            h.Menus.OnGuildStatus(ShopOpenStatus.Close);
            Assert.False(h.Menus.IsOpen);
        }

        /// <summary>
        /// S2C 0x030's Type is LandSandBoat's RESULT_* (0 success, 1 fail, 2 high quality), so 0 plays the success ending ednn
        /// and 1 the break edmm (round 3, 2026-10-18: read as SYNTHESIS_RESULT, fail 0 / success 1, the endings were swapped).
        /// </summary>
        [Fact]
        public void EffectType_ZeroIsSuccess_OneIsBreak_TwoUpIsHighQuality()
        {
            Assert.Equal("ednn", ActionMotionLibrary.SynthesisEnding(0));
            Assert.Equal("edmm", ActionMotionLibrary.SynthesisEnding(1));
            Assert.Equal("edxx", ActionMotionLibrary.SynthesisEnding(2));
            Assert.Equal("edxx", ActionMotionLibrary.SynthesisEnding(4));
            Assert.Equal(SynthesisResult.Success, (SynthesisResult)0);
            Assert.Equal(SynthesisResult.Fail, (SynthesisResult)1);
            Assert.Equal(SynthesisResult.HighQuality1, (SynthesisResult)2);
        }

        [Fact]
        public void SynthesisStatus_TakesTheHealingPosture_AndTheEffectSitsTheCrafterDown()
        {
            Assert.Equal(AnimationCategory.Healing, RestPosture.CategoryFor(RestPosture.StatusSynthesis));
            var combat = new CombatState();
            var crafting = new CraftingState();
            var world = new WorldState();
            var player = new LocalPlayerState { ServerId = 0x0100 };
            new SynthesisAnimationController().Attach(combat, crafting, world, player);
            var other = new WorldEntity(0x0200, 20, EntityType.Player);
            world.UpsertEntity(other);
            combat.SetCraftEffect(new CraftEffectInfo(0x0200, 20, SynthesisEffect.Wind, 1, RestPosture.StatusSynthesis, 0));
            Assert.Equal(RestPosture.StatusSynthesis, other.AnimationState);
            Assert.False(crafting.IsSynthesizing);
            combat.SetCraftEffect(new CraftEffectInfo(0x0100, 10, SynthesisEffect.Fire, 1, RestPosture.StatusSynthesis, 0));
            Assert.True(crafting.IsSynthesizing);
        }

        private static void SpinUntil(Func<bool> condition)
        {
            Assert.True(System.Threading.SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(2)));
        }

        /// <summary>The retail menu DAT's synthesis and guild windows and their help texts (skipped without the install).</summary>
        [Fact]
        public void RetailMenus_SynthesisAndGuildWindowsResolve()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            if (library == null) return;

            Assert.True(library.TryGetMenu(StockUiSynthesis.Menu, out var menu));
            Assert.Equal((384, 48, UiAnchor.TopRight), (menu.Frame.X, menu.Frame.Y, menu.Frame.Anchor));
            Assert.Equal(new[] { 819, 820 }, new[] { 1, 2 }.Select(id => menu.FindButton(id)!.HelpTextId));
            Assert.True(library.TryGetMenu(StockUiMenuEntries.MainMenu, out var main));
            Assert.Equal(816, main.FindButton(10)!.HelpTextId);

            Assert.True(library.TryGetMenu(StockUiSynthesis.GridMenu, out var grid));
            Assert.Equal((16, 48, 182, 94, 492), (grid.Frame.X, grid.Frame.Y, grid.Frame.Width, grid.Frame.Height, grid.Frame.HelpTextId));
            Assert.Equal(10, grid.Buttons.Count(StockUiMenuController.IsSelectable));
            Assert.Equal(493, grid.FindButton(StockUiSynthesis.OkButton)!.HelpTextId);
            Assert.Contains(grid.Frame.Shapes, s => s.Kind == 6 && s.GroupName.Equals("anc_equi", StringComparison.OrdinalIgnoreCase));

            Assert.True(library.TryGetMenu(StockUiSynthesis.HistoryListMenu, out var history));
            Assert.Equal((182, 190, 10), (history.Frame.Width, history.Frame.Height, history.Buttons.Count(StockUiMenuController.IsSelectable)));
            Assert.True(library.TryGetMenu(StockUiSynthesis.OptionsMenu, out var options));
            Assert.Equal(new[] { 817, 818 }, new[] { 1, 2 }.Select(id => options.FindButton(id)!.HelpTextId));
            Assert.True(library.TryGetMenu(StockUiGuildShop.MenuName, out var guild));
            Assert.Equal((16, 256, UiAnchor.BottomLeft), (guild.Frame.X, guild.Frame.Y, guild.Frame.Anchor));

            var help = Gordian.Core.Resources.Tables.DMsgStringTable.Parse(File.ReadAllBytes(Path.Combine(GameDirectory, "ROM", "165", "75.DAT")))!;
            Assert.Equal("Select materials for synthesis.", help.Records[StockUiSynthesis.HelpSelectMaterials].PrimaryText);
            Assert.Equal("Select the items you wish to synthesize.", help.Records[492].PrimaryText);
            Assert.Equal("Synthesize this combination of items.", help.Records[StockUiSynthesis.HelpSynthesize].PrimaryText);
            Assert.Equal("Expend crystals and synthesize items.", help.Records[819].PrimaryText);
            Assert.Equal("Select an item from the guild's buy list.", help.Records[StockUiGuildShop.HelpBuyList].PrimaryText);
        }
    }
}
