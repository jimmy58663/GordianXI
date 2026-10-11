// tests/Gordian.Core.Tests/Ui/StockUiItemWindowTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Input;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Ui
{
    /// <summary>The stock Items window (#351) and Equipment window (#352): the shared item list, Use / Drop, sorting, storages, equipping.</summary>
    public class StockUiItemWindowTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        private const ushort Potion = 4112, EchoDrops = 4151, Chestnut = 639, BronzeCap = 12448, LeatherBandana = 12440,
            HomeRing = 28540, BronzeSword = 16535, Brass = 12449, FireCrystal = 4096;

        private static readonly Dictionary<ushort, ItemRecord> Records = new()
        {
            [Potion] = new ItemRecord { ItemId = Potion, Name = "Potion", LogName = "potion", StackSize = 12, ValidTargets = StockUiItems.TargetSelf | StockUiItems.TargetParty, Description = "Restores 50 HP." },
            [EchoDrops] = new ItemRecord { ItemId = EchoDrops, Name = "Echo Drops", LogName = "flask of echo drops", StackSize = 12, ValidTargets = StockUiItems.TargetSelf },
            [Chestnut] = new ItemRecord { ItemId = Chestnut, Name = "Ronfaure Chestnut", LogName = "Ronfaure chestnut", LogPlural = "Ronfaure chestnuts", StackSize = 12 },
            [FireCrystal] = new ItemRecord { ItemId = FireCrystal, Name = "Fire Crystal", LogName = "fire crystal", StackSize = 12 },
            [BronzeCap] = new ItemRecord { ItemId = BronzeCap, Name = "Bronze Cap", LogName = "bronze cap", Level = 7, JobsMask = 0x7FFFFE, EquipSlotsMask = 1 << (int)EquipSlotId.Head, RacesMask = 0x1FE },
            [LeatherBandana] = new ItemRecord { ItemId = LeatherBandana, Name = "Leather Bandana", Level = 30, JobsMask = 1 << 1, EquipSlotsMask = 1 << (int)EquipSlotId.Head },
            [Brass] = new ItemRecord { ItemId = Brass, Name = "Brass Cap", Level = 11, JobsMask = 1 << 3, EquipSlotsMask = 1 << (int)EquipSlotId.Head },
            [HomeRing] = new ItemRecord { ItemId = HomeRing, Name = "Home Ring", Level = 1, JobsMask = 0x7FFFFE, ValidTargets = StockUiItems.TargetSelf,
                EquipSlotsMask = (1 << (int)EquipSlotId.Ring1) | (1 << (int)EquipSlotId.Ring2) },
            [BronzeSword] = new ItemRecord { ItemId = BronzeSword, Name = "Bronze Sword", Level = 1, JobsMask = 0x7FFFFE, Skill = 3, EquipSlotsMask = 1 << (int)EquipSlotId.Main },
        };

        /// <summary>A vertical list whose buttons ring up/down and link left/right to themselves.</summary>
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

        /// <summary>"inventor" as the DAT authors it: ten 132 x 16 rows at an 18 px pitch from (3,6), the end rows linked to themselves.</summary>
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
            return new UiMenuDefinition { Name = StockUiItems.ListMenu, Frame = new UiMenuFrame { X = 16, Y = 48, Width = 182, Height = 190, Anchor = UiAnchor.TopLeft }, Buttons = buttons };
        }

        /// <summary>"equip": the 4 x 4 grid, buttons numbered down each column (1-4 left ... 13-16 right), links stopping at the edges.</summary>
        private static UiMenuDefinition EquipGrid()
        {
            var buttons = new List<UiMenuButton>();
            for (int c = 0; c < 4; c++)
            {
                for (int r = 0; r < 4; r++)
                {
                    int id = c * 4 + r + 1;
                    buttons.Add(new UiMenuButton
                    {
                        ButtonId = (short)id, X = (short)(24 + 34 * c), Y = (short)(11 + 34 * r), Width = 32, Height = 32,
                        NavUp = (sbyte)(r == 0 ? id : id - 1), NavDown = (sbyte)(r == 3 ? id : id + 1),
                        NavLeft = (sbyte)(c == 0 ? id : id - 4), NavRight = (sbyte)(c == 3 ? id : id + 4),
                    });
                }
            }
            return new UiMenuDefinition { Name = StockUiItems.EquipmentMenu, Frame = new UiMenuFrame { X = 130, Y = 48, Width = 182, Height = 190, Anchor = UiAnchor.TopLeft }, Buttons = buttons };
        }

        private static UiMenuDefinition QuantityFrame() => new()
        {
            Name = StockUiShop.QuantityMenu,
            Frame = new UiMenuFrame { X = 16, Y = 240, Width = 112, Height = 56, Anchor = UiAnchor.TopLeft },
            Buttons = new List<UiMenuButton>
            {
                new() { ButtonId = StockUiShop.QuantityField, X = 34, Y = 22, Width = 24, Height = 16, NavUp = 1, NavDown = 1, NavLeft = 1, NavRight = 1 },
            },
        };

        private static UiResourceLibrary Library() => UiResourceLibrary.FromDefinitions(new[]
        {
            List(StockUiMenuEntries.MainMenu, 384, 48, UiAnchor.TopRight, 12),
            ItemList(),
            List(StockUiItems.ActionMenu, 384, 48, UiAnchor.TopRight, 2),
            List(StockUiItems.OptionsMenu, 384, 48, UiAnchor.TopRight, 3),
            List(StockUiItems.StorageMenu, 384, 48, UiAnchor.TopRight, 16),
            EquipGrid(),
            QuantityFrame(),
            List(StockUiMenuEntries.MessageYesNoMenu, 130, 208, UiAnchor.BottomLeft, 2),
        });

        private sealed class Harness
        {
            public readonly InventoryState Inventory = new();
            public readonly StockUiMenuController Menus = new() { Library = Library() };
            public readonly List<string> Notices = new();
            public readonly List<(uint Id, ushort Index, ContainerId Container, byte Slot)> Uses = new();
            public readonly List<(byte Slot, EquipSlotId EquipSlot, ContainerId Container)> Equips = new();
            public readonly List<ContainerId> Sorts = new();
            public readonly TaskCompletionSource<(ContainerId, byte, uint)> Dropped = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public ushort LastValidTargets;
            public StockUiTargetContext? LastContext;
            public StockUiEquipCharacter Character = new(1, 30); // Warrior 30

            public Harness()
            {
                Menus.Inventory = Inventory;
                Menus.ItemLookup = id => Records.TryGetValue(id, out var r) ? r : null;
                Menus.NoticePosted += Notices.Add;
                Menus.ItemUse = (id, index, container, slot) => { Uses.Add((id, index, container, slot)); return Task.CompletedTask; };
                Menus.ItemDrop = (container, slot, count) => { Dropped.TrySetResult((container, slot, count)); return Task.CompletedTask; };
                Menus.ItemEquip = (slot, equipSlot, container) => { Equips.Add((slot, equipSlot, container)); return Task.CompletedTask; };
                Menus.ItemSort = container => { Sorts.Add(container); return Task.CompletedTask; };
                Menus.ItemUseTarget = (targets, context) =>
                {
                    LastValidTargets = targets;
                    LastContext = context;
                    return (new StockUiItemTarget(0x01020304, 10, "Me"), null);
                };
                Menus.EquipCharacter = () => Character;
                Inventory.ItemChanged += (_, _, _) => Menus.OnInventoryChanged();
                Inventory.EquipChanged += (_, _, _) => Menus.OnItemsChanged();
                Inventory.SetContainerSizes(ContainerId.Inventory, 81, 81);
                Inventory.SetContainerSizes(ContainerId.Wardrobe, 81, 81);
                Inventory.SetItem(ContainerId.Inventory, 0, InventoryState.GilItemId, 5000, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 1, Chestnut, 12, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 2, Potion, 3, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 4, BronzeCap, 1, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 5, EchoDrops, 1, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 6, LeatherBandana, 1, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 7, Brass, 1, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Wardrobe, 1, BronzeSword, 1, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Wardrobe, 2, FireCrystal, 5, ItemLockFlag.Normal);
            }

            /// <summary>Opens the main menu and confirms one of its entries (2 Equipment, 4 Items).</summary>
            public StockUiOpenMenu OpenFromMainMenu(int button)
            {
                Assert.True(Menus.OpenMainMenu());
                Menus.Top!.SelectedButtonId = button;
                Menus.Activate();
                return Menus.Top!;
            }

            public void SelectItem(StockUiOpenMenu list, ushort itemId)
            {
                int index = list.ItemRows.ToList().FindIndex(r => r.ItemId == itemId);
                Assert.True(index >= 0 && index < list.VisibleRows, $"item {itemId} not on the first page");
                list.FirstRow = 0;
                list.SelectedButtonId = index + 1;
            }
        }

        [Fact]
        public void MainMenuItems_ListsTheInventoryInSlotOrderWithoutGil()
        {
            var h = new Harness();
            var list = h.OpenFromMainMenu(4);
            Assert.Equal(StockUiItemListKind.Items, list.ItemListKind);
            Assert.Equal(ContainerId.Inventory, list.ItemContainer);
            Assert.Equal(new ushort[] { Chestnut, Potion, BronzeCap, EchoDrops, LeatherBandana, Brass }, list.ItemRows.Select(r => r.ItemId));
            Assert.Equal(12u, list.ItemRows[0].Count);
            Assert.True(list.ItemRows[0].ShowsCount);
            Assert.False(list.ItemRows[2].ShowsCount); // a single, unstackable cap
            Assert.Equal((6, 80), list.ItemFill);
            Assert.Equal(StockUiItems.ListRows, list.VisibleRows);
            Assert.True(list.IsItemWindow);
            // The list's bounds reach the options window at the top right, so the main menu there is hidden.
            Assert.True(list.OverlapsAuthored(h.Menus.OpenMenus[0]));
        }

        [Fact]
        public void ItemsList_ScrollsPastItsTenRows()
        {
            var h = new Harness();
            for (byte slot = 10; slot < 20; slot++) h.Inventory.SetItem(ContainerId.Inventory, slot, FireCrystal, 1, ItemLockFlag.Normal);
            var list = h.OpenFromMainMenu(4);
            Assert.Equal(16, list.ItemRows.Count);
            for (int i = 0; i < 10; i++) h.Menus.Move(InputAction.MenuDown);
            Assert.Equal(1, list.FirstRow);
            Assert.Equal(10, list.SelectedButtonId);
            h.Menus.Move(InputAction.MenuRight); // pages forward
            Assert.Equal(6, list.FirstRow);
        }

        [Fact]
        public void Use_SendsItemUseOnTheResolvedTarget_AndClosesTheMenus()
        {
            var h = new Harness();
            var list = h.OpenFromMainMenu(4);
            h.SelectItem(list, Potion);
            h.Menus.Activate();
            var action = h.Menus.Top!;
            Assert.Equal(Potion, action.ItemAction!.Value.ItemId);
            Assert.Equal(StockUiItems.UseButton, action.SelectedButtonId);
            Assert.False(action.IsGreyed(StockUiItems.UseButton));
            h.Menus.Activate();
            Assert.Equal((0x01020304u, (ushort)10, ContainerId.Inventory, (byte)2), Assert.Single(h.Uses));
            Assert.Equal(StockUiItems.TargetSelf | StockUiItems.TargetParty, h.LastValidTargets);
            Assert.Null(h.LastContext);
            Assert.False(h.Menus.IsOpen);
        }

        [Fact]
        public void Use_IsGreyedForAnItemWithoutTargets()
        {
            var h = new Harness();
            var list = h.OpenFromMainMenu(4);
            h.SelectItem(list, Chestnut);
            h.Menus.Activate();
            var action = h.Menus.Top!;
            Assert.True(action.IsGreyed(StockUiItems.UseButton));
            Assert.Equal(StockUiItems.DropButton, action.SelectedButtonId);
            action.SelectedButtonId = StockUiItems.UseButton;
            h.Menus.Activate();
            Assert.Empty(h.Uses);
            Assert.Contains("Ronfaure Chestnut cannot be used.", h.Notices);
        }

        [Fact]
        public async Task Drop_AStackAsksTheCountThenConfirms()
        {
            var h = new Harness();
            var list = h.OpenFromMainMenu(4);
            h.SelectItem(list, Chestnut);
            h.Menus.Activate();
            h.Menus.Activate(); // Drop (Use is greyed)
            var quantity = h.Menus.Top!;
            Assert.True(quantity.IsQuantity);
            Assert.Equal(12u, quantity.QuantityMax);
            Assert.True(list.ShowsQuantity);
            h.Menus.Move(InputAction.MenuUp);
            h.Menus.Move(InputAction.MenuUp);
            Assert.Equal(3u, quantity.Quantity);
            h.Menus.Activate();
            var prompt = h.Menus.Top!;
            Assert.Equal("Drop 3 Ronfaure chestnuts?", prompt.Message);
            Assert.Equal(2, prompt.SelectedButtonId); // No under the cursor
            Assert.False(list.ShowsQuantity);
            prompt.SelectedButtonId = 1;
            h.Menus.Activate();
            var dropped = await h.Dropped.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal((ContainerId.Inventory, (byte)1, 3u), dropped);
        }

        [Fact]
        public void Drop_IsGreyedForAWornItem()
        {
            var h = new Harness();
            h.Inventory.SetEquip(EquipSlotId.Head, ContainerId.Inventory, 4);
            var list = h.OpenFromMainMenu(4);
            Assert.True(list.ItemRows.Single(r => r.ItemId == BronzeCap).Equipped);
            h.SelectItem(list, BronzeCap);
            h.Menus.Activate();
            var action = h.Menus.Top!;
            Assert.True(action.IsGreyed(StockUiItems.DropButton));
            Assert.True(action.IsGreyed(StockUiItems.UseButton));
            action.SelectedButtonId = StockUiItems.DropButton;
            h.Menus.Activate();
            Assert.Contains("Bronze Cap is equipped and cannot be dropped.", h.Notices);
        }

        [Fact]
        public void Options_AutoSortSendsItemStackAndOrdersTheListByItem()
        {
            var h = new Harness();
            var list = h.OpenFromMainMenu(4);
            Assert.True(h.Menus.ToggleItemOptions());
            var options = h.Menus.Top!;
            Assert.True(options.IsItemOptions);
            Assert.True(options.IsGreyed(StockUiItems.ManualSortButton));
            Assert.Equal(StockUiItems.AutoSortButton, options.SelectedButtonId);
            h.Menus.Activate();
            Assert.Equal(ContainerId.Inventory, Assert.Single(h.Sorts));
            Assert.Same(list, h.Menus.Top);
            Assert.Equal(list.ItemRows.Select(r => r.ItemId).OrderBy(id => id), list.ItemRows.Select(r => r.ItemId));
            // + again from the list opens the options; pressed on them it returns to the list.
            Assert.True(h.Menus.ToggleItemOptions());
            Assert.True(h.Menus.ToggleItemOptions());
            Assert.Same(list, h.Menus.Top);
        }

        [Fact]
        public void Options_SwitchShowsAnotherStorage_UnavailableOnesGreyed()
        {
            var h = new Harness();
            var list = h.OpenFromMainMenu(4);
            h.Menus.ToggleItemOptions();
            h.Menus.Top!.SelectedButtonId = StockUiItems.SwitchButton;
            h.Menus.Activate();
            var storage = h.Menus.Top!;
            Assert.True(storage.IsItemStorageMenu);
            Assert.Equal(1, storage.SelectedButtonId); // Inventory, the one shown
            Assert.True(storage.IsGreyed(StockUiItems.StorageButtonOf(ContainerId.MogSafe)));
            Assert.False(storage.IsGreyed(StockUiItems.StorageButtonOf(ContainerId.Wardrobe)));
            storage.SelectedButtonId = StockUiItems.StorageButtonOf(ContainerId.MogSafe);
            h.Menus.Activate();
            Assert.Contains("Mog Safe is not available.", h.Notices);
            storage.SelectedButtonId = StockUiItems.StorageButtonOf(ContainerId.Wardrobe);
            h.Menus.Activate();
            Assert.Same(list, h.Menus.Top);
            Assert.Equal(ContainerId.Wardrobe, list.ItemContainer);
            Assert.Equal(new ushort[] { BronzeSword, FireCrystal }, list.ItemRows.Select(r => r.ItemId));
            // The window opens on the storage last shown.
            h.Menus.CloseAll();
            Assert.Equal(ContainerId.Wardrobe, h.OpenFromMainMenu(4).ItemContainer);
        }

        [Fact]
        public void ItemsChanged_RefreshTheList_AndCloseUseDropWhenItsItemIsGone()
        {
            var h = new Harness();
            var list = h.OpenFromMainMenu(4);
            h.SelectItem(list, EchoDrops);
            h.Menus.Activate();
            Assert.NotNull(h.Menus.Top!.ItemAction);
            h.Inventory.RemoveItem(ContainerId.Inventory, 5);
            Assert.Same(list, h.Menus.Top);
            Assert.DoesNotContain(list.ItemRows, r => r.ItemId == EchoDrops);
        }

        [Fact]
        public void Equipment_ListsTheItemsThatFitTheSlot_GreyedByJobAndLevel()
        {
            var h = new Harness();
            h.Inventory.SetEquip(EquipSlotId.Head, ContainerId.Inventory, 4);
            var grid = h.OpenFromMainMenu(2);
            Assert.True(grid.IsEquipment);
            Assert.Equal(BronzeCap, grid.EquippedItemIn(StockUiCheck.ButtonOf(EquipSlotId.Head)));
            Assert.Equal(0, grid.EquippedItemIn(StockUiCheck.ButtonOf(EquipSlotId.Body)));
            grid.SelectedButtonId = StockUiCheck.ButtonOf(EquipSlotId.Head);
            Assert.Equal(BronzeCap, grid.DescribedItem);
            h.Menus.Activate();
            var list = h.Menus.Top!;
            Assert.Equal(StockUiItemListKind.Equipment, list.ItemListKind);
            Assert.Equal(EquipSlotId.Head, list.ItemEquipSlot);
            Assert.Equal(StockUiItems.EquipListX, list.Menu.Frame.X);
            Assert.True(grid.IsListOpen);
            var rows = list.ItemRows;
            Assert.True(rows[0].IsRemove);
            Assert.Equal(new ushort[] { BronzeCap, LeatherBandana, Brass }, rows.Skip(1).Select(r => r.ItemId));
            Assert.True(rows[1].Equipped);
            Assert.False(rows[2].Greyed); // WAR 30 wears the Leather Bandana (Lv.30 WAR)
            Assert.True(rows[3].Greyed);  // Brass Cap: WHM only
            Assert.Equal("Your current job cannot equip that item.", rows[3].GreyedReason);

            list.SelectedButtonId = 4;
            h.Menus.Activate();
            Assert.Contains("Your current job cannot equip that item.", h.Notices);
            Assert.Empty(h.Equips);

            list.SelectedButtonId = 3;
            h.Menus.Activate();
            Assert.Equal(((byte)6, EquipSlotId.Head, ContainerId.Inventory), Assert.Single(h.Equips));
            Assert.Same(grid, h.Menus.Top);
            Assert.False(grid.IsListOpen);
        }

        [Fact]
        public void Equipment_RemoveUnequipsWithItemIndexZero()
        {
            var h = new Harness();
            h.Inventory.SetEquip(EquipSlotId.Head, ContainerId.Inventory, 4);
            var grid = h.OpenFromMainMenu(2);
            grid.SelectedButtonId = StockUiCheck.ButtonOf(EquipSlotId.Head);
            h.Menus.Activate();
            h.Menus.Top!.SelectedButtonId = 1;
            h.Menus.Activate();
            Assert.Equal(((byte)0, EquipSlotId.Head, ContainerId.Inventory), Assert.Single(h.Equips));
        }

        [Fact]
        public void Equipment_ListsWardrobeItems_AndAnEmptySlotWithNothingToWearPostsANotice()
        {
            var h = new Harness();
            h.Character = new StockUiEquipCharacter(1, 1);
            var grid = h.OpenFromMainMenu(2);
            grid.SelectedButtonId = StockUiCheck.ButtonOf(EquipSlotId.Main);
            h.Menus.Activate();
            var list = h.Menus.Top!;
            var row = Assert.Single(list.ItemRows);
            Assert.Equal((BronzeSword, ContainerId.Wardrobe), (row.ItemId, row.Container));
            h.Menus.CloseTop();
            grid.SelectedButtonId = StockUiCheck.ButtonOf(EquipSlotId.Feet);
            h.Menus.Activate();
            Assert.Same(grid, h.Menus.Top);
            Assert.Contains("You have nothing to equip there.", h.Notices);
        }

        [Fact]
        public void Equipment_RingInTheOtherSlotIsGreyed()
        {
            var h = new Harness();
            h.Inventory.SetItem(ContainerId.Inventory, 9, HomeRing, 1, ItemLockFlag.Normal);
            h.Inventory.SetEquip(EquipSlotId.Ring1, ContainerId.Inventory, 9);
            var rows = StockUiItems.EquipRows(h.Inventory, EquipSlotId.Ring2, h.Menus.ItemLookup, h.Character);
            var ring = Assert.Single(rows);
            Assert.True(ring.Greyed);
            Assert.Equal("That item is equipped in another slot.", ring.GreyedReason);
        }

        [Fact]
        public void CommandMenuItems_UseTheItemsOnTheMenusTarget()
        {
            var rows = StockUiCommandMenu.Compose(new StockUiTargetContext(StockUiTargetKind.Player, 0x0A0B0C0D, "Friend"));
            Assert.Contains(rows, r => r.Label == StockUiCommandMenu.Items && r.Entry.Command == StockUiMenuCommand.Items && !r.Greyed);

            var h = new Harness();
            var target = new StockUiTargetContext(StockUiTargetKind.Player, 0x0A0B0C0D, "Friend");
            var list = h.Menus.OpenItems(null, target)!;
            h.SelectItem(list, Potion);
            h.Menus.Activate();
            h.Menus.Activate();
            Assert.Equal(target, h.LastContext);
            Assert.Single(h.Uses);
        }

        [Fact]
        public void Description_PagesTheItemUnderTheCursor()
        {
            var h = new Harness();
            var list = h.OpenFromMainMenu(4);
            h.SelectItem(list, Potion);
            var page = Assert.Single(list.DescriptionPages);
            Assert.Equal(new[] { "Potion", "Restores 50 HP." }, page.Lines);
            Assert.False(h.Menus.NextItemPage());
        }

        [Fact]
        public void Description_OfANonEquipmentItemWrapsLongLines()
        {
            var record = new ItemRecord
            {
                ItemId = 1, Name = "Scroll", Description = "A very long single line of item description text that runs well past the window's width.",
            };
            var page = Assert.Single(StockUiItemDescription.Pages(record));
            Assert.Equal("Scroll", page.Lines[0]);
            Assert.True(page.Lines.Count >= 3);
            Assert.All(page.Lines, l => Assert.True(l.Length <= 42));
        }

        [Theory]
        [InlineData("head", EquipSlotId.Head)]
        [InlineData("Range", EquipSlotId.Ranged)]
        [InlineData("lear", EquipSlotId.Ear1)]
        [InlineData("rring", EquipSlotId.Ring2)]
        [InlineData("ring1", EquipSlotId.Ring1)]
        public void TryParseSlot_ReadsRetailSlotWords(string word, EquipSlotId slot)
        {
            Assert.True(StockUiItems.TryParseSlot(word, out var parsed));
            Assert.Equal(slot, parsed);
        }

        [Theory]
        [InlineData("\"Echo Drops\" <me>", "Echo Drops", "<me>")]
        [InlineData("Echo Drops <t>", "Echo Drops", "<t>")]
        [InlineData("Potion", "Potion", "")]
        [InlineData("\"Potion\" Friend", "Potion", "Friend")]
        public void SplitName_SeparatesTheItemFromTheTarget(string args, string name, string target)
        {
            Assert.Equal((name, target), StockUiItems.SplitName(args));
        }

        [Fact]
        public void FindItem_MatchesShortOrLogNamesInContainerOrder()
        {
            var h = new Harness();
            var found = StockUiItems.FindItem(h.Inventory, h.Menus.ItemLookup, "flask of echo drops", StockUiItems.UsableContainers);
            Assert.Equal((ContainerId.Inventory, (byte)5), (found!.Container, found.Slot));
            Assert.Equal(ContainerId.Wardrobe, StockUiItems.FindItem(h.Inventory, h.Menus.ItemLookup, "bronze sword", StockUiItems.EquippableContainers)!.Container);
            Assert.Null(StockUiItems.FindItem(h.Inventory, h.Menus.ItemLookup, "Elixir", StockUiItems.UsableContainers));
        }

        [Fact]
        public void Containers_AvailabilityAndFillCountSlotZero()
        {
            var inventory = new InventoryState();
            inventory.SetContainerSizes(ContainerId.Inventory, 81, 81);
            inventory.SetContainerSizes(ContainerId.Wardrobe3, 1, 1);
            Assert.True(StockUiItems.IsAvailable(inventory, ContainerId.Inventory));
            Assert.False(StockUiItems.IsAvailable(inventory, ContainerId.Wardrobe3));
            Assert.Equal((0, 80), StockUiItems.Fill(inventory, ContainerId.Inventory));
        }

        [Theory]
        [InlineData("/item \"Potion\" <me>", ChatCommandResultKind.ItemUse)]
        [InlineData("/equip head \"Bronze Cap\"", ChatCommandResultKind.Equip)]
        [InlineData("/eq head", ChatCommandResultKind.Equip)]
        public void Router_ParsesItemAndEquipCommands(string input, ChatCommandResultKind kind)
        {
            Assert.Equal(kind, ChatCommandRouter.Parse(input).Kind);
        }

        [Fact]
        public void RetailMenus_ItemAndEquipmentWindowsResolve()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            if (library == null) return;

            Assert.True(library.TryGetMenu(StockUiItems.ListMenu, out var list));
            Assert.Equal((16, 48, 182, 190), (list.Frame.X, list.Frame.Y, list.Frame.Width, list.Frame.Height));
            Assert.Equal(StockUiItems.ListRows, list.Buttons.Count(StockUiMenuController.IsSelectable));
            Assert.Equal(18, list.FindButton(2)!.Y - list.FindButton(1)!.Y);
            Assert.Equal(1, list.FindButton(1)!.NavUp);
            Assert.Equal(StockUiItems.ListRows, list.FindButton(StockUiItems.ListRows)!.NavDown);
            Assert.Contains(list.Frame.Shapes, s => s.Kind == 6 && s.GroupName.Equals("anc_item", StringComparison.OrdinalIgnoreCase));

            Assert.True(library.TryGetMenu(StockUiItems.ActionMenu, out var action));
            foreach (int id in new[] { StockUiItems.UseButton, StockUiItems.DropButton })
            {
                Assert.Contains(action.FindButton(id)!.Shapes, s => s.Kind == 0 && library.TryGetImage(s, out _));
                Assert.Contains(action.FindButton(id)!.Shapes, s => s.Kind == 4 && library.TryGetImage(s, out _));
            }
            Assert.True(library.TryGetMenu(StockUiItems.OptionsMenu, out var options));
            Assert.Equal(3, options.Buttons.Count(StockUiMenuController.IsSelectable));
            Assert.Contains(options.FindButton(StockUiItems.ManualSortButton)!.Shapes, s => s.Kind == 4);
            Assert.True(library.TryGetMenu(StockUiItems.StorageMenu, out var storage));
            Assert.Equal(16, storage.Buttons.Count(StockUiMenuController.IsSelectable));
            Assert.Equal(StockUiItems.ListRows, list.Buttons.Count);

            Assert.True(library.TryGetMenu(StockUiItems.EquipmentMenu, out var equip));
            Assert.Equal((130, 48), (equip.Frame.X, equip.Frame.Y));
            Assert.Equal(16, equip.Buttons.Count(StockUiMenuController.IsSelectable));
            Assert.True(library.TryGetMenu(StockUiItems.StatsMenu, out var persona));
            Assert.Equal((16, 48, 112, 190), (persona.Frame.X, persona.Frame.Y, persona.Frame.Width, persona.Frame.Height));

            // The flows run on the retail frames.
            var h = new Harness();
            h.Menus.Library = library;
            var items = h.OpenFromMainMenu(4);
            Assert.True(items.IsItemList);
            h.SelectItem(items, Potion);
            h.Menus.Activate();
            Assert.NotNull(h.Menus.Top!.ItemAction);
            h.Menus.CloseAll();
            var grid = h.OpenFromMainMenu(2);
            Assert.True(grid.IsEquipment);
            grid.SelectedButtonId = StockUiCheck.ButtonOf(EquipSlotId.Head);
            h.Menus.Activate();
            Assert.True(h.Menus.Top!.IsItemList);
        }
    }
}
