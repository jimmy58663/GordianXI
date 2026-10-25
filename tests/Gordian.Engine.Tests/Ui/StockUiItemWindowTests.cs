// tests/Gordian.Engine.Tests/Ui/StockUiItemWindowTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Engine.Input;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Engine.Tests.Ui
{
    /// <summary>The stock Items window (#351) and Equipment window (#352): the shared item list, Use / Drop, sorting, storages, equipping.</summary>
    public class StockUiItemWindowTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        private const ushort Potion = 4112, EchoDrops = 4151, Chestnut = 639, BronzeCap = 12448, LeatherBandana = 12440,
            HomeRing = 28540, BronzeSword = 16535, Brass = 12449, FireCrystal = 4096;

        private static readonly Dictionary<ushort, ItemRecord> Records = new()
        {
            [Potion] = new ItemRecord { ItemId = Potion, Name = "Potion", LogName = "potion", StackSize = 12, ValidTargets = StockUiItems.TargetSelf | StockUiItems.TargetParty, Flags = StockUiItems.CanUseFlag, Description = "Restores 50 HP." },
            [EchoDrops] = new ItemRecord { ItemId = EchoDrops, Name = "Echo Drops", LogName = "flask of echo drops", StackSize = 12, ValidTargets = StockUiItems.TargetSelf, Flags = StockUiItems.CanUseFlag },
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
            Frame = new UiMenuFrame { X = 16, Y = 240, Width = 112, Height = 56, Anchor = UiAnchor.TopLeft, HelpTextId = 268 },
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
            List(StockUiItems.SortConfirmMenu, 384, 124, UiAnchor.TopRight, 2),
            List(StockUiItems.EquipStorageMenu, 384, 48, UiAnchor.TopRight, 9),
            EquipGrid(),
            QuantityFrame(),
            List(StockUiMenuEntries.MessageYesNoMenu, 130, 208, UiAnchor.BottomLeft, 2),
            List(StockUiMenuEntries.MainMenuPage2, 384, 48, UiAnchor.TopRight, 12),
            List(StockUiViewHouse.MenuName, 384, 48, UiAnchor.TopRight, 15),
            List(StockUiViewHouse.OptionsMenu, 384, 48, UiAnchor.TopRight, 3),
        });

        private sealed class Harness
        {
            public readonly InventoryState Inventory = new();
            public readonly StockUiMenuController Menus = new() { Library = Library() };
            public readonly List<string> Notices = new();
            public readonly List<string> Errors = new();
            public readonly List<(uint Id, ushort Index, ContainerId Container, byte Slot)> Uses = new();
            public readonly List<(byte Slot, EquipSlotId EquipSlot, ContainerId Container)> Equips = new();
            public readonly List<(uint Count, ContainerId From, byte FromSlot, ContainerId To, byte ToSlot)> Moves = new();
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
                Menus.ErrorPosted += Errors.Add;
                Menus.ItemUse = (id, index, container, slot) => { Uses.Add((id, index, container, slot)); return Task.CompletedTask; };
                Menus.ItemDrop = (container, slot, count) => { Dropped.TrySetResult((container, slot, count)); return Task.CompletedTask; };
                Menus.ItemEquip = (slot, equipSlot, container) => { Equips.Add((slot, equipSlot, container)); return Task.CompletedTask; };
                Menus.ItemMove = (count, from, fromSlot, to, toSlot) => { Moves.Add((count, from, fromSlot, to, toSlot)); return Task.CompletedTask; };
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
                Inventory.SetContainerSizes(ContainerId.Inventory, 80, 80);
                Inventory.SetContainerSizes(ContainerId.Wardrobe, 80, 80);
                Inventory.SetContainerSizes(ContainerId.RecycleBin, 10, 10);
                Inventory.SetItem(ContainerId.Inventory, 0, InventoryState.GilItemId, 5000, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 1, Chestnut, 12, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 2, Potion, 3, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 4, BronzeCap, 1, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 5, EchoDrops, 1, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 6, LeatherBandana, 1, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 7, Brass, 1, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Inventory, 8, FireCrystal, 5, ItemLockFlag.Normal);
                Inventory.SetItem(ContainerId.Wardrobe, 1, BronzeSword, 1, ItemLockFlag.Normal);
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
        public void MainMenuItems_ListsTheInventoryInSlotOrder_WithTheHelpBar()
        {
            var h = new Harness();
            var list = h.OpenFromMainMenu(4);
            Assert.Equal(StockUiItemListKind.Items, list.ItemListKind);
            Assert.Equal(new ushort[] { Chestnut, Potion, BronzeCap, EchoDrops, LeatherBandana, Brass, FireCrystal }, list.ItemRows.Select(r => r.ItemId));
            Assert.True(list.ItemRows[0].ShowsCount);
            Assert.False(list.ItemRows[2].ShowsCount); // a single, unstackable cap
            Assert.Equal(StockUiItems.ListRows, list.VisibleRows);
            // "Items 7/80 Select an item." (retail capture: "Items 29/30 Select an item.").
            Assert.Equal(new StockUiItemHelpBar("Items", 7, 80, StockUiItems.HelpSelectItem), list.ItemHelpBar);
            // The list's bounds reach the options window at the top right, so the main menu there is hidden.
            Assert.True(list.OverlapsAuthored(h.Menus.OpenMenus[0]));
        }

        [Fact]
        public void ItemsList_ScrollsPastItsTenRows()
        {
            var h = new Harness();
            for (byte slot = 10; slot < 20; slot++) h.Inventory.SetItem(ContainerId.Inventory, slot, Chestnut, 1, ItemLockFlag.Normal);
            var list = h.OpenFromMainMenu(4);
            Assert.Equal(17, list.ItemRows.Count);
            for (int i = 0; i < 10; i++) h.Menus.Move(InputAction.MenuDown);
            Assert.Equal(1, list.FirstRow);
            Assert.Equal(10, list.SelectedButtonId);
            h.Menus.Move(InputAction.MenuRight); // pages forward
            Assert.Equal(7, list.FirstRow);
        }

        [Fact]
        public void Use_WithoutTheCursorGoesOnTheResolvedTarget_AndReturnsToTheList()
        {
            var h = new Harness();
            var list = h.OpenFromMainMenu(4);
            h.SelectItem(list, EchoDrops);
            h.Menus.Activate();
            var action = h.Menus.Top!;
            Assert.Equal(EchoDrops, action.ItemAction!.Value.ItemId);
            h.Menus.Activate();
            Assert.Equal((0x01020304u, (ushort)10, ContainerId.Inventory, (byte)5), Assert.Single(h.Uses));
            Assert.Same(list, h.Menus.Top); // back on the Items list (round 1, 2026-10-12)
        }

        [Theory]
        [InlineData(Potion)]
        [InlineData(EchoDrops)] // self-only items get the cursor too (on yourself)
        public void Use_StartsTheSubTargetCursorWhenWired_AndTheListComesBackAfterwards(ushort itemId)
        {
            var h = new Harness();
            StockUiItemRow? started = null;
            h.Menus.ItemSubTarget = (row, targets) =>
            {
                started = row;
                h.Menus.CloseAll(); // the cursor's owner closes the menus
                return true;
            };
            var list = h.OpenFromMainMenu(4);
            h.SelectItem(list, itemId);
            int selected = list.SelectedButtonId;
            h.Menus.Activate();
            h.Menus.Activate();
            Assert.Equal(itemId, started!.Value.ItemId);
            Assert.Empty(h.Uses);
            Assert.False(h.Menus.IsOpen);
            var back = h.Menus.ResumeItemWindow()!;
            Assert.Equal(StockUiItemListKind.Items, back.ItemListKind);
            Assert.Equal(selected, back.SelectedButtonId);
            Assert.Equal(StockUiMenuEntries.MainMenu, h.Menus.OpenMenus[0].Name);
            Assert.Null(h.Menus.ResumeItemWindow()); // only once
        }

        [Fact]
        public void Use_IsGreyedForAnItemWithoutTargets()
        {
            var h = new Harness();
            var list = h.OpenFromMainMenu(4);
            h.SelectItem(list, BronzeCap);
            h.Menus.Activate();
            var action = h.Menus.Top!;
            Assert.True(action.IsGreyed(StockUiItems.UseButton));
            Assert.Equal(StockUiItems.DropButton, action.SelectedButtonId);
            action.SelectedButtonId = StockUiItems.UseButton;
            h.Menus.Activate();
            Assert.Empty(h.Uses);
            Assert.Contains("Bronze Cap cannot be used.", h.Notices);
        }

        [Fact]
        public void Use_ACrystalWithAFullInventoryPrintsRetailsError()
        {
            var h = new Harness();
            h.Inventory.SetContainerSizes(ContainerId.Inventory, 7, 7); // 7 slots (as decoded from S2C 0x01C), 7 used
            var list = h.OpenFromMainMenu(4);
            h.SelectItem(list, FireCrystal);
            h.Menus.Activate();
            h.Menus.Activate();
            Assert.Equal(StockUiItems.InventoryFullMessage, Assert.Single(h.Errors));
            Assert.Empty(h.Uses);
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
            Assert.Equal(new StockUiItemHelpBar("No. of Items", null, null, 268), list.ItemHelpBar); // "Set the number of items."
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
            Assert.True(list.HasItemActionOver);
            action.SelectedButtonId = StockUiItems.DropButton;
            h.Menus.Activate();
            Assert.Contains("Bronze Cap is equipped and cannot be dropped.", h.Notices);
        }

        [Fact]
        public void Options_AutoAsksThenSendsItemStackAndOrdersTheListByCategory()
        {
            var h = new Harness();
            var list = h.OpenFromMainMenu(4);
            Assert.True(h.Menus.ToggleItemOptions());
            var options = h.Menus.Top!;
            Assert.True(options.IsItemOptions);
            Assert.Equal(StockUiItems.AutoSortButton, options.SelectedButtonId);
            h.Menus.Activate();
            var confirm = h.Menus.Top!;
            Assert.True(confirm.IsSortConfirm);
            Assert.Equal(StockUiItems.SortNoButton, confirm.SelectedButtonId); // No under the cursor (retail capture)
            Assert.Equal(StockUiItems.HelpSortList, list.ItemHelpBar!.Value.HelpTextId);
            h.Menus.Activate(); // No: back to the options
            Assert.Same(options, h.Menus.Top);
            Assert.Empty(h.Sorts);
            h.Menus.Activate();
            h.Menus.Top!.SelectedButtonId = StockUiItems.SortYesButton;
            h.Menus.Activate();
            Assert.Equal(ContainerId.Inventory, Assert.Single(h.Sorts));
            Assert.Same(list, h.Menus.Top);
            // Type 1 (by category): crystals, usable items, head gear by level, then the rest.
            Assert.Equal(new ushort[] { FireCrystal, Potion, EchoDrops, LeatherBandana, Brass, BronzeCap, Chestnut }, list.ItemRows.Select(r => r.ItemId));
            // + again from the list opens the options; pressed on them it returns to the list.
            Assert.True(h.Menus.ToggleItemOptions());
            Assert.True(h.Menus.ToggleItemOptions());
            Assert.Same(list, h.Menus.Top);
        }

        [Fact]
        public void Options_ManualMovesTheFirstPickToTheSecondsPlace()
        {
            var h = new Harness();
            var list = h.OpenFromMainMenu(4);
            h.Menus.ToggleItemOptions();
            h.Menus.Top!.SelectedButtonId = StockUiItems.ManualSortButton;
            h.Menus.Activate();
            Assert.Same(list, h.Menus.Top);
            Assert.True(list.IsManualSort);
            Assert.Equal(StockUiItems.HelpSelectItemToMove, list.ItemHelpBar!.Value.HelpTextId);
            // Chestnut (first) onto Bronze Cap (third): it goes below it.
            list.SelectedButtonId = 1;
            h.Menus.Activate();
            Assert.Equal(0, list.ManualSortFrom);
            list.SelectedButtonId = 3;
            h.Menus.Activate();
            Assert.True(list.IsManualSort); // the mode stays for the next pair (round 1, 2026-10-12)
            Assert.Null(list.ManualSortFrom);
            Assert.Equal(new ushort[] { Potion, BronzeCap, Chestnut, EchoDrops, LeatherBandana, Brass, FireCrystal }, list.ItemRows.Select(r => r.ItemId));
            // Echo Drops (fourth) onto Potion (first): it goes above it.
            list.SelectedButtonId = 4;
            h.Menus.Activate();
            list.SelectedButtonId = 1;
            h.Menus.Activate();
            Assert.Equal(new ushort[] { EchoDrops, Potion, BronzeCap, Chestnut, LeatherBandana, Brass, FireCrystal }, list.ItemRows.Select(r => r.ItemId));
            // The first Cancel returns to the options with Manual under the cursor; the second to the list.
            h.Menus.CloseTop();
            Assert.False(list.IsManualSort);
            Assert.True(h.Menus.Top!.IsItemOptions);
            Assert.Equal(StockUiItems.ManualSortButton, h.Menus.Top.SelectedButtonId);
            h.Menus.CloseTop();
            Assert.Same(list, h.Menus.Top);
        }

        [Fact]
        public void AutoOrder_Type1_MatchesTheRetailCaptures()
        {
            // The maintainer's Type 1 captures (2026-10-12, image-29 to image-37), with each item's DAT fields
            // (type, flags, valid targets, level, item level, slots, skill, shield) read from the English item DATs.
            ItemRecord R(ushort id, ushort type, uint flags, ushort vt, ushort lv, byte il, ushort slots, byte skill = 0, byte shield = 0) =>
                new() { ItemId = id, Name = id.ToString(System.Globalization.CultureInfo.InvariantCulture), ItemType = type, Flags = flags, ValidTargets = vt,
                    Level = lv, ItemLevel = il, EquipSlotsMask = slots, Skill = skill, ShieldSize = shield };
            var records = new Dictionary<ushort, ItemRecord>
            {
                // image-29 to image-31: Fire / Wind Crystal, Potion, Brz. Bull. Pouch, Carnwenhan, Bronze Bullet, Marsyas,
                // Loughnashade, Bihu / Fili head, body, hands, legs, feet, Linkshell, Linkpearl, Seashell, Dragon Talon, Shell
                // Powder, Kindred's / H. / S. Kindred Crest, Thunder / Shadow Geode.
                [4096] = R(4096, 8, 0x204, 1, 0, 0, 0), [4098] = R(4098, 8, 0x204, 1, 0, 0, 0),
                [4112] = R(4112, 7, 0x604, 1, 4, 0, 0), [5359] = R(5359, 7, 0x604, 1, 4, 0, 0),
                [20561] = R(20561, 4, 0xF840, 0, 99, 119, 0x3, 2), [17343] = R(17343, 4, 0x804, 0, 1, 0, 0x8, 26),
                [21398] = R(21398, 4, 0xF840, 0, 99, 0, 0x4, 42), [22304] = R(22304, 4, 0xF840, 0, 99, 0, 0x4, 42),
                [23407] = R(23407, 5, 0xF840, 0, 99, 119, 0x10), [23429] = R(23429, 5, 0xF840, 0, 99, 119, 0x10),
                [23474] = R(23474, 5, 0xF840, 0, 99, 119, 0x20), [23496] = R(23496, 5, 0xF840, 0, 99, 119, 0x20),
                [23541] = R(23541, 5, 0xF840, 0, 99, 119, 0x40), [23563] = R(23563, 5, 0xF840, 0, 99, 119, 0x40),
                [23608] = R(23608, 5, 0xF840, 0, 99, 119, 0x80), [23630] = R(23630, 5, 0xF840, 0, 99, 119, 0x80),
                [23675] = R(23675, 5, 0xF840, 0, 99, 119, 0x100), [23697] = R(23697, 5, 0xF840, 0, 99, 119, 0x100),
                [513] = R(513, 6, 0x7140, 0, 65535, 0, 0x1), [515] = R(515, 6, 0x3140, 0, 65535, 0, 0x1),
                [888] = R(888, 1, 0x4, 0, 65535, 0, 0), [903] = R(903, 1, 0x4, 0, 65535, 0, 0), [1883] = R(1883, 1, 0x4, 0, 65535, 0, 0),
                [2955] = R(2955, 1, 0x7044, 0, 65535, 0, 0), [2956] = R(2956, 1, 0x7044, 0, 65535, 0, 0), [2957] = R(2957, 1, 0x7044, 0, 65535, 0, 0),
                [3301] = R(3301, 1, 0x4, 0, 65535, 0, 0), [3304] = R(3304, 1, 0x4, 0, 65535, 0, 0),
                // image-32 heads then bodies: Kasuga Kabuto +2, Wakido Kabuto +3, Mpaca's Cap, Nyame Helm, Hiza. Somen +2,
                // Sakonji Kabuto +1; Sakonji Domaru +2, Kasuga Domaru +2, Wakido Domaru +3.
                [23096] = R(23096, 5, 0xF840, 0, 99, 119, 0x10), [23386] = R(23386, 5, 0xF840, 0, 99, 119, 0x10),
                [23758] = R(23758, 5, 0xE840, 0, 99, 119, 0x10), [23761] = R(23761, 5, 0xE840, 0, 99, 119, 0x10),
                [25576] = R(25576, 5, 0xF840, 0, 99, 119, 0x10), [26647] = R(26647, 5, 0xF840, 0, 99, 119, 0x10),
                [23141] = R(23141, 5, 0xF840, 0, 99, 119, 0x20), [23163] = R(23163, 5, 0xF840, 0, 99, 119, 0x20), [23453] = R(23453, 5, 0xF840, 0, 99, 119, 0x20),
                // image-33 weapons: Qutrub Knife, Joyeuse, Ark Sword, Mercurial Sword, Ophidian Sword, Bounty Sickle, Quint
                // Spear, Tzee Xicu's Blade, Yagyu Shortblade, Shinai.
                [20576] = R(20576, 4, 0xF840, 0, 1, 0, 0x3, 2), [17652] = R(17652, 4, 0xF854, 0, 70, 0, 0x3, 3),
                [18913] = R(18913, 4, 0x7C58, 1, 1, 0, 0x3, 3), [18377] = R(18377, 4, 0x804, 0, 62, 0, 0x1, 4),
                [21681] = R(21681, 4, 0xF840, 0, 1, 0, 0x1, 4), [18968] = R(18968, 4, 0x8804, 0, 82, 0, 0x1, 7),
                [19314] = R(19314, 4, 0x8804, 0, 82, 0, 0x1, 8), [21863] = R(21863, 4, 0xF840, 0, 1, 0, 0x1, 8),
                [21933] = R(21933, 4, 0x7840, 0, 1, 0, 0x3, 9), [18441] = R(18441, 4, 0xF850, 0, 1, 0, 0x1, 10),
                // image-34 ammo, shields, heads: Per. Lucky Egg, Oshasha's Treatise, Incantor Stone, Joiner's Scutum, Ark
                // Shield, Aya. Zucchetto +2, Inyanga Tiara +2, Arrogance Crown, Wh. Rarab Cap +1, Inyanga Jubbah +2.
                [22299] = R(22299, 4, 0xF840, 0, 99, 0, 0x8), [22302] = R(22302, 4, 0xF840, 0, 99, 0, 0x8), [19257] = R(19257, 4, 0x8804, 0, 83, 0, 0x8),
                [26425] = R(26425, 5, 0xF840, 0, 1, 0, 0x2, 0, 4), [26490] = R(26490, 5, 0xF840, 0, 1, 0, 0x2, 0, 4),
                [25572] = R(25572, 5, 0xF840, 0, 99, 119, 0x10), [25577] = R(25577, 5, 0xF840, 0, 99, 119, 0x10),
                [23856] = R(23856, 5, 0xF840, 0, 1, 0, 0x10), [25679] = R(25679, 5, 0xFC40, 1, 1, 0, 0x10, 1),
                [25793] = R(25793, 5, 0xF840, 0, 99, 119, 0x20),
                // image-35 and image-36 usable items (types 7 and 1 with CanUse) by id, then a scythe and a bolt: Super Ether,
                // Panacea, Holy Water, Prism Powder, Silent Oil, Pamama au Lait, Gysahl Greens, Tornado, Gnatbane, Pluton Case,
                // Boulder Case, Beitetsu Box, Miso Ramen, Abdhaljs Seal, L. Wing Box, Trust Primer, Foenaria, Blind Bolt.
                [4136] = R(4136, 7, 0x604, 1, 40, 0, 0), [4149] = R(4149, 7, 0x604, 1, 4, 0, 0), [4154] = R(4154, 7, 0x604, 1, 4, 0, 0),
                [4164] = R(4164, 7, 0x604, 1, 4, 0, 0), [4165] = R(4165, 7, 0x604, 1, 4, 0, 0), [4302] = R(4302, 7, 0x60C, 1, 4, 0, 0),
                [4545] = R(4545, 7, 0x60C, 1, 4, 0, 0), [4816] = R(4816, 7, 0x868C, 1, 4, 0, 0), [5984] = R(5984, 7, 0x604, 1, 4, 0, 0),
                [6180] = R(6180, 1, 0x365C, 1, 4, 0, 0), [6182] = R(6182, 1, 0x365C, 1, 4, 0, 0), [6184] = R(6184, 1, 0x3658, 1, 4, 0, 0),
                [6460] = R(6460, 1, 0x60C, 1, 4, 0, 0), [6500] = R(6500, 1, 0x7640, 1, 4, 0, 0), [6592] = R(6592, 7, 0x765C, 1, 4, 0, 0),
                [6716] = R(6716, 7, 0x7648, 1, 4, 0, 0), [21834] = R(21834, 4, 0xF840, 0, 99, 119, 0x1, 7), [18150] = R(18150, 4, 0x804, 0, 10, 0, 0x8, 26),
                // image-37: Dwarf Pugil (a lure in the ammo slot), Tea Set, Goblin Mail, Silver Ingot, Mtl. Beastcoin, Amber,
                // Sheep Wool, Kindred's Seal, Shinobi-Tabi, Earth Ore.
                [17007] = R(17007, 4, 0x7854, 0, 1, 0, 0x8, 48), [133] = R(133, 10, 0x4, 0, 5, 0, 0x1), [507] = R(507, 1, 0x1004, 0, 65535, 0, 0x1),
                [744] = R(744, 1, 0x4, 0, 65535, 0, 0), [749] = R(749, 1, 0x4, 0, 65535, 0, 0), [814] = R(814, 1, 0x4, 0, 65535, 0, 0),
                [832] = R(832, 1, 0x4, 0, 65535, 0, 0), [1127] = R(1127, 1, 0x7044, 0, 65535, 0, 0), [1194] = R(1194, 1, 0x4, 0, 65535, 0, 0),
                [1258] = R(1258, 1, 0x4, 0, 65535, 0, 0),
            };
            ushort[][] captures =
            {
                new ushort[] { 4096, 4098, 4112, 5359, 20561, 17343, 21398, 22304, 23407, 23429, 23474, 23496, 23541, 23563, 23608, 23630,
                    23675, 23697, 513, 515, 888, 903, 1883, 2955, 2956, 2957, 3301, 3304 },
                new ushort[] { 23096, 23386, 23758, 23761, 25576, 26647, 23141, 23163, 23453 },
                new ushort[] { 20576, 17652, 18913, 18377, 21681, 18968, 19314, 21863, 21933, 18441 },
                new ushort[] { 22299, 22302, 19257, 26425, 26490, 25572, 25577, 23856, 25679, 25793 },
                new ushort[] { 4136, 4149, 4154, 4164, 4165, 4302, 4545, 4816 },
                new ushort[] { 5984, 6180, 6182, 6184, 6460, 6500, 6592, 6716, 21834, 18150 },
                new ushort[] { 17007, 133, 507, 744, 749, 814, 832, 1127, 1194, 1258 },
            };
            foreach (var expected in captures)
            {
                // Fed in reverse slot order, so only the comparison can put them right.
                var rows = expected.Reverse().Select((id, i) => new StockUiItemRow(id, id.ToString(System.Globalization.CultureInfo.InvariantCulture), 1, ContainerId.Inventory, (byte)(i + 1))).ToList();
                var order = StockUiItems.AutoOrder(rows, id => records[id]);
                StockUiItems.ApplyOrder(rows, order);
                Assert.Equal(expected, rows.Select(r => r.ItemId));
            }
        }

        [Fact]
        public async Task InventorySortOn_SortsWhenAnItemArrives_AndLeavesAManualOrderAloneUntilThen()
        {
            var h = new Harness();
            var sorted = new TaskCompletionSource<ContainerId>(TaskCreationOptions.RunContinuationsAsynchronously);
            h.Menus.ItemSort = container => { sorted.TrySetResult(container); return Task.CompletedTask; };
            h.Menus.OnItemsChanged(); // the inventory's first count
            h.Menus.Settings.SetValue(StockUiSettingKey.InventorySort, 1);
            var list = h.OpenFromMainMenu(4);
            // Not sorted just because the setting is on (round 2, 2026-10-12): slot order until an item arrives.
            Assert.Equal(new ushort[] { Chestnut, Potion, BronzeCap, EchoDrops, LeatherBandana, Brass, FireCrystal }, list.ItemRows.Select(r => r.ItemId));
            // A manual move stays.
            h.Menus.ToggleItemOptions();
            h.Menus.Top!.SelectedButtonId = StockUiItems.ManualSortButton;
            h.Menus.Activate();
            list.SelectedButtonId = 7;
            h.Menus.Activate();
            list.SelectedButtonId = 1;
            h.Menus.Activate();
            Assert.Equal(FireCrystal, list.ItemRows[0].ItemId);
            h.Menus.OnItemsChanged(); // a count change that is no new item
            Assert.Equal(FireCrystal, list.ItemRows[0].ItemId);
            Assert.Equal(Chestnut, list.ItemRows[1].ItemId);
            // An item arrives: Type 1 (crystals, usable items, equipment by slot and level, then the rest), and one 0x03A.
            h.Inventory.SetItem(ContainerId.Inventory, 20, Chestnut, 2, ItemLockFlag.Normal);
            Assert.Equal(new ushort[] { FireCrystal, Potion, EchoDrops, LeatherBandana, Brass, BronzeCap, Chestnut, Chestnut }, list.ItemRows.Select(r => r.ItemId));
            Assert.Equal(ContainerId.Inventory, await sorted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            // Type 2 sorts by name, at the next item.
            h.Menus.Settings.SetValue(StockUiSettingKey.InventoryType, 2);
            h.Inventory.SetItem(ContainerId.Inventory, 21, Potion, 1, ItemLockFlag.Normal);
            Assert.Equal(list.ItemRows.Select(r => r.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase), list.ItemRows.Select(r => r.Name));
        }

        [Fact]
        public void Use_WhenTheCursorKeepsTheMenusOpen_TheListIsNotStackedTwice()
        {
            // The combined branch's cursor sets the menus aside rather than closing them, then calls ResumeItemWindow: the list
            // must come back alone, so one Cancel leaves it (round 2, 2026-10-12: two Cancels were needed).
            var h = new Harness();
            h.Menus.ItemSubTarget = (row, targets) => true;
            var list = h.OpenFromMainMenu(4);
            int depth = h.Menus.OpenMenus.Count;
            h.SelectItem(list, Potion);
            h.Menus.Activate();
            h.Menus.Activate();
            Assert.Same(list, h.Menus.ResumeItemWindow());
            Assert.Same(list, h.Menus.Top);
            Assert.Equal(depth, h.Menus.OpenMenus.Count);
            h.Menus.CloseTop();
            Assert.Equal(StockUiMenuEntries.MainMenu, h.Menus.Top!.Name);
        }


        [Fact]
        public void Options_RecycleBinListsThrownAwayItems_AndRestoresWithTheCount()
        {
            var h = new Harness();
            h.Inventory.SetItem(ContainerId.RecycleBin, 1, Chestnut, 4, ItemLockFlag.Normal);
            h.Inventory.SetItem(ContainerId.RecycleBin, 2, EchoDrops, 1, ItemLockFlag.Normal);
            var list = h.OpenFromMainMenu(4);
            h.Menus.ToggleItemOptions();
            h.Menus.Top!.SelectedButtonId = StockUiItems.RecycleBinButton;
            h.Menus.Activate();
            var bin = h.Menus.Top!;
            Assert.Equal(StockUiItemListKind.RecycleBin, bin.ItemListKind);
            Assert.Equal(new ushort[] { Chestnut, EchoDrops }, bin.ItemRows.Select(r => r.ItemId));
            Assert.Equal(list.ItemRows.Select(r => r.ItemId), bin.CompanionRows.Select(r => r.ItemId));
            Assert.Equal(new StockUiItemHelpBar("Recycling Bin", 2, 10, StockUiItems.HelpSelectItem), bin.ItemHelpBar);
            Assert.True(bin.OverlapsAuthored(list)); // the bin replaces the inventory list in its corner
            Assert.Null(list.ItemHelpBar);

            // One item: restored at once (0x029 to the inventory's first free slot).
            bin.SelectedButtonId = 2;
            h.Menus.Activate();
            Assert.Equal((1u, ContainerId.RecycleBin, (byte)2, ContainerId.Inventory, StockUiItems.AnyFreeSlot), Assert.Single(h.Moves));
            // A stack: the count first.
            bin.SelectedButtonId = 1;
            h.Menus.Activate();
            var quantity = h.Menus.Top!;
            Assert.True(quantity.IsQuantity && quantity.ItemRestore);
            h.Menus.Move(InputAction.MenuLeft); // All
            h.Menus.Activate();
            Assert.Equal((4u, ContainerId.RecycleBin, (byte)1, ContainerId.Inventory, StockUiItems.AnyFreeSlot), h.Moves[1]);
            Assert.Same(bin, h.Menus.Top);
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
        public void CommandMenuItems_ListOnlyUsableItems_AndUseThemAtOnce()
        {
            var rows = StockUiCommandMenu.Compose(new StockUiTargetContext(StockUiTargetKind.Player, 0x0A0B0C0D, "Friend"));
            Assert.Contains(rows, r => r.Label == StockUiCommandMenu.Items && r.Entry.Command == StockUiMenuCommand.Items && !r.Greyed);

            var h = new Harness();
            var target = new StockUiTargetContext(StockUiTargetKind.Player, 0x0A0B0C0D, "Friend");
            var list = h.Menus.OpenItems(null, target)!;
            Assert.Equal(StockUiItemListKind.Usable, list.ItemListKind);
            Assert.Equal(new ushort[] { Potion, EchoDrops, FireCrystal }, list.ItemRows.Select(r => r.ItemId));
            // "Items 3/7" (retail capture: "Items 2/29"): the usable items listed over the items held.
            Assert.Equal(new StockUiItemHelpBar("Items", 3, 7, StockUiItems.HelpSelectItem), list.ItemHelpBar);
            Assert.False(h.Menus.ToggleItemOptions()); // no options from the command menu
            h.SelectItem(list, Potion);
            h.Menus.Activate(); // no Use / Drop: used at once
            Assert.Equal(target, h.LastContext);
            Assert.Single(h.Uses);
            Assert.Same(list, h.Menus.Top); // back on the command menu's list (round 1, 2026-10-12)
        }

        [Fact]
        public void Equipment_ShowsTheStoragesEquipmentBesideTheGrid_ThenTheSlotsItems()
        {
            var h = new Harness();
            h.Inventory.SetEquip(EquipSlotId.Head, ContainerId.Inventory, 4);
            var grid = h.OpenFromMainMenu(2);
            Assert.True(grid.IsEquipment);
            Assert.Equal(BronzeCap, grid.EquippedItemIn(StockUiCheck.ButtonOf(EquipSlotId.Head)));
            // Beside the grid: "Storage Options", then the inventory's equipment (not the potions or crystals).
            Assert.True(grid.CompanionRows[0].IsStorageOptions);
            Assert.Equal(new ushort[] { BronzeCap, LeatherBandana, Brass }, grid.CompanionRows.Skip(1).Select(r => r.ItemId));
            Assert.True(grid.OverlapsAuthored(h.Menus.OpenMenus[0])); // the list covers the main menu's corner
            grid.SelectedButtonId = StockUiCheck.ButtonOf(EquipSlotId.Head);
            Assert.Equal(new StockUiItemHelpBar("Equipment", null, null, -1), grid.ItemHelpBar); // the synthetic grid has no help ids
            h.Menus.Activate();
            var list = h.Menus.Top!;
            Assert.Equal(StockUiItemListKind.Equipment, list.ItemListKind);
            Assert.Equal(StockUiItems.EquipListX, list.Menu.Frame.X);
            Assert.True(grid.IsListOpen);
            Assert.Equal("Equipment", list.ItemHelpBar!.Value.Title);
            var rows = list.ItemRows;
            Assert.True(rows[0].IsStorageOptions);
            Assert.Equal(new ushort[] { BronzeCap, LeatherBandana, Brass }, rows.Skip(1).Select(r => r.ItemId));
            Assert.Equal(2, list.SelectedButtonId); // the cursor on the worn cap
            Assert.True(rows[1].Equipped);
            Assert.False(rows[2].Greyed); // WAR 30 wears the Leather Bandana (Lv.30 WAR)
            Assert.True(rows[3].Greyed);  // Brass Cap: WHM only
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
        public void Equipment_ConfirmOnTheWornItemRemovesIt()
        {
            var h = new Harness();
            h.Inventory.SetEquip(EquipSlotId.Head, ContainerId.Inventory, 4);
            var grid = h.OpenFromMainMenu(2);
            grid.SelectedButtonId = StockUiCheck.ButtonOf(EquipSlotId.Head);
            h.Menus.Activate();
            h.Menus.Activate(); // the cursor starts on the worn cap
            Assert.Equal(((byte)0, EquipSlotId.Head, ContainerId.Inventory), Assert.Single(h.Equips));
        }

        [Fact]
        public void Equipment_StorageOptionsSwitchesTheListToAWardrobe()
        {
            var h = new Harness();
            h.Character = new StockUiEquipCharacter(1, 1);
            var grid = h.OpenFromMainMenu(2);
            grid.SelectedButtonId = StockUiCheck.ButtonOf(EquipSlotId.Main);
            h.Menus.Activate();
            var list = h.Menus.Top!;
            Assert.Equal(ContainerId.Inventory, list.ItemContainer);
            Assert.Single(list.ItemRows); // Storage Options only: no main weapon in the inventory
            list.SelectedButtonId = 1;
            h.Menus.Activate();
            var storage = h.Menus.Top!;
            Assert.True(storage.IsEquipStorageMenu);
            Assert.Equal(1, storage.SelectedButtonId);
            Assert.True(storage.IsGreyed(StockUiItems.EquipStorageButtonOf(ContainerId.Wardrobe2)));
            storage.SelectedButtonId = StockUiItems.EquipStorageButtonOf(ContainerId.Wardrobe2);
            h.Menus.Activate();
            Assert.Contains("Mog Wardrobe 2 is not available.", h.Notices);
            storage.SelectedButtonId = StockUiItems.EquipStorageButtonOf(ContainerId.Wardrobe);
            h.Menus.Activate();
            Assert.Same(list, h.Menus.Top);
            Assert.Equal(ContainerId.Wardrobe, list.ItemContainer);
            Assert.Equal(ContainerId.Wardrobe, grid.EquipContainer);
            var sword = list.ItemRows[1];
            Assert.Equal((BronzeSword, ContainerId.Wardrobe), (sword.ItemId, sword.Container));
        }

        [Fact]
        public void Equipment_RingInTheOtherSlotIsGreyed()
        {
            var h = new Harness();
            h.Inventory.SetItem(ContainerId.Inventory, 9, HomeRing, 1, ItemLockFlag.Normal);
            h.Inventory.SetEquip(EquipSlotId.Ring1, ContainerId.Inventory, 9);
            var rows = StockUiItems.EquipRows(h.Inventory, ContainerId.Inventory, EquipSlotId.Ring2, h.Menus.ItemLookup, h.Character);
            var ring = rows[1];
            Assert.True(ring.Greyed);
            Assert.Equal("That item is equipped in another slot.", ring.GreyedReason);
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
        public void Containers_AvailabilityAndFill_FromLandSandBoatsItemMax()
        {
            // LandSandBoat sends 1 + the slots (a 30-slot inventory as 31, an unbought wardrobe as 1); the decoder takes the 1
            // off, and the help bar shows "x/30", not "x/29" (round 2, 2026-10-12).
            var payload = new byte[68];
            payload[(int)ContainerId.Inventory] = 31;
            payload[(int)ContainerId.Wardrobe3] = 1;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(32 + 2 * (int)ContainerId.Inventory), 31);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(32 + 2 * (int)ContainerId.Wardrobe3), 1);
            var packet = new S2C_0x01C_ItemMax(payload);
            var inventory = new InventoryState();
            foreach (var c in new[] { ContainerId.Inventory, ContainerId.Wardrobe3 }) inventory.SetContainerSizes(c, packet.GetMaxSize(c), packet.GetUsableSize(c));
            inventory.SetItem(ContainerId.Inventory, 3, Chestnut, 1, ItemLockFlag.Normal);
            Assert.True(StockUiItems.IsAvailable(inventory, ContainerId.Inventory));
            Assert.False(StockUiItems.IsAvailable(inventory, ContainerId.Wardrobe3));
            Assert.Equal((1, 30), StockUiItems.Fill(inventory, ContainerId.Inventory));
            inventory.RemoveItem(ContainerId.Inventory, 3); // a drop does not change the total
            Assert.Equal((0, 30), StockUiItems.Fill(inventory, ContainerId.Inventory));
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
            Assert.Equal(StockUiItems.HelpSelectItem, list.Frame.HelpTextId);
            Assert.Contains(list.Frame.Shapes, s => s.Kind == 6 && s.GroupName.Equals("anc_item", StringComparison.OrdinalIgnoreCase));

            Assert.True(library.TryGetMenu(StockUiItems.ActionMenu, out var action));
            foreach (int id in new[] { StockUiItems.UseButton, StockUiItems.DropButton })
            {
                Assert.Contains(action.FindButton(id)!.Shapes, s => s.Kind == 4 && library.TryGetImage(s, out _));
            }
            Assert.True(library.TryGetMenu(StockUiItems.OptionsMenu, out var options));
            Assert.Equal(new[] { 269, 270, 1068 }, new[] { 1, 2, 3 }.Select(id => options.FindButton(id)!.HelpTextId));
            Assert.True(library.TryGetMenu(StockUiItems.SortConfirmMenu, out var confirm));
            Assert.Equal((384, 124), (confirm.Frame.X, confirm.Frame.Y));
            Assert.True(library.TryGetMenu(StockUiItems.EquipStorageMenu, out var storage));
            Assert.Equal(9, storage.Buttons.Count(StockUiMenuController.IsSelectable));

            Assert.True(library.TryGetMenu(StockUiItems.EquipmentMenu, out var equip));
            Assert.Equal((130, 48), (equip.Frame.X, equip.Frame.Y));
            Assert.Equal(16, equip.Buttons.Count(StockUiMenuController.IsSelectable));
            Assert.True(library.TryGetMenu(StockUiItems.StatsMenu, out var persona));
            Assert.Equal((16, 48, 112, 190), (persona.Frame.X, persona.Frame.Y, persona.Frame.Width, persona.Frame.Height));

            // The help texts the bar shows (ROM/165/75).
            var help = Gordian.Core.Resources.Tables.DMsgStringTable.Parse(File.ReadAllBytes(Path.Combine(GameDirectory, "ROM", "165", "75.DAT")))!;
            Assert.Equal("Select an item.", help.Records[StockUiItems.HelpSelectItem].PrimaryText);
            Assert.Equal("Select item to move.", help.Records[StockUiItems.HelpSelectItemToMove].PrimaryText);
            Assert.Equal("Sort list?", help.Records[StockUiItems.HelpSortList].PrimaryText);

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
            Assert.Equal(244, grid.ItemHelpBar!.Value.HelpTextId); // "Head (Helms, hats, etc.)"
            h.Menus.Activate();
            Assert.True(h.Menus.Top!.IsItemList);
        }

        /// <summary>
        /// View House (#23): the storage list greys storages the server has not given; a Wardrobe's item moves to the inventory
        /// (C2S 0x029 to the first free slot), while the Mog Safe can only be viewed outside the Mog House.
        /// </summary>
        [Fact]
        public void ViewHouse_ListsStorages_MovesFromTheWardrobe_ViewsTheMogSafe()
        {
            var h = new Harness();
            h.Inventory.SetContainerSizes(ContainerId.MogSafe, 50, 50);
            h.Inventory.SetItem(ContainerId.MogSafe, 3, Chestnut, 7, ItemLockFlag.Normal);
            bool inMogHouse = false;
            h.Menus.InMogHouse = () => inMogHouse;
            Assert.True(h.Menus.Open(StockUiMenuEntries.MainMenuPage2));
            h.Menus.Top!.SelectedButtonId = 4; // View House
            h.Menus.Activate();
            var storages = h.Menus.Top!;
            Assert.Equal(StockUiViewHouse.MenuName, storages.Name);
            Assert.False(storages.IsGreyed(1));  // Mog Safe
            Assert.False(storages.IsGreyed(7));  // Mog Wardrobe
            Assert.True(storages.IsGreyed(4));   // Mog Satchel: no size from the server

            storages.SelectedButtonId = 7;
            h.Menus.Activate();
            var wardrobe = h.Menus.Top!;
            Assert.Equal(StockUiItemListKind.Storage, wardrobe.ItemListKind);
            Assert.Equal(ContainerId.Wardrobe, wardrobe.ItemContainer);
            Assert.Equal("Bronze Sword", Assert.Single(wardrobe.ItemRows).Name);
            Assert.Equal(new StockUiItemHelpBar("M.Ward.", 1, 80, StockUiViewHouse.HelpMoveToInventory), wardrobe.ItemHelpBar);
            h.Menus.Activate();
            Assert.Equal((1u, ContainerId.Wardrobe, (byte)1, ContainerId.Inventory, StockUiItems.AnyFreeSlot), Assert.Single(h.Moves));

            // Right: the cursor on the inventory beside it (round 1 image-7); Confirm stores the item in the wardrobe.
            h.Menus.Move(InputAction.MenuRight);
            Assert.True(wardrobe.StorageInventorySide);
            Assert.Equal(ContainerId.Inventory, wardrobe.ItemContainer);
            Assert.Equal(ContainerId.Wardrobe, wardrobe.StorageContainer);
            Assert.Equal("Bronze Sword", Assert.Single(wardrobe.CompanionRows).Name);
            Assert.Equal(new StockUiItemHelpBar("M.Ward.", 1, 80, StockUiViewHouse.HelpMoveTo(ContainerId.Wardrobe)), wardrobe.ItemHelpBar);
            var stored = wardrobe.SelectedItemRow!.Value;
            h.Menus.Activate();
            Assert.Equal((stored.Count, ContainerId.Inventory, stored.Slot, ContainerId.Wardrobe, StockUiItems.AnyFreeSlot), h.Moves[^1]);
            h.Menus.Move(InputAction.MenuLeft);
            Assert.False(wardrobe.StorageInventorySide);
            Assert.Equal(ContainerId.Wardrobe, wardrobe.ItemContainer);

            // + / Y: the storage's options (Auto, Manual, Switch; image-8); Switch goes back to the View House list.
            Assert.True(h.Menus.ToggleItemOptions());
            Assert.Equal(StockUiViewHouse.OptionsMenu, h.Menus.Top!.Name);
            h.Menus.Top!.SelectedButtonId = StockUiViewHouse.SwitchButton;
            h.Menus.Activate();
            Assert.Same(storages, h.Menus.Top);
            h.Moves.Clear();
            h.Moves.Add((1u, ContainerId.Wardrobe, (byte)1, ContainerId.Inventory, StockUiItems.AnyFreeSlot));

            storages.SelectedButtonId = 1;
            h.Menus.Activate();
            var safe = h.Menus.Top!;
            Assert.Equal(ContainerId.MogSafe, safe.ItemContainer);
            h.Menus.Activate();
            Assert.Single(h.Moves);
            Assert.Contains("Mog House", h.Notices[^1]);
            inMogHouse = true;
            h.Menus.Activate();
            Assert.Equal((7u, ContainerId.MogSafe, (byte)3, ContainerId.Inventory, StockUiItems.AnyFreeSlot), h.Moves[1]);
        }
    }
}
