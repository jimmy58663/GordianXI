// tests/Gordian.Engine.Tests/Ui/StockUiMogHouseTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Gordian.Core.Resources.Ui;
using Gordian.Core.World;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Ui
{
    /// <summary>The Mog House menus (#93): the moogle's menu, Layout (C2S 0x0FA / 0x0FB), Open Mog House / Remodel (0x0CB), the mannequin (0x03B).</summary>
    public class StockUiMogHouseTests
    {
        private const ushort SimpleBed = 2, OakBed = 3, OakTable = 24, Vase = 150, Flag = 181, BrassFlowerpot = 217, HumeMannequin = 256, BronzeCap = 12448, LeatherVest = 12568;

        private static readonly Dictionary<ushort, ItemRecord> Records = new()
        {
            [SimpleBed] = new ItemRecord { ItemId = SimpleBed, Name = "Simple Bed", ItemType = MogHouseFurniture.FurnishingItemType, EquipSlotsMask = 2 },
            [OakBed] = new ItemRecord { ItemId = OakBed, Name = "Oak Bed", ItemType = MogHouseFurniture.FurnishingItemType },
            [OakTable] = new ItemRecord { ItemId = OakTable, Name = "Oak Table", ItemType = MogHouseFurniture.FurnishingItemType },
            [Vase] = new ItemRecord { ItemId = Vase, Name = "Vase", ItemType = MogHouseFurniture.FurnishingItemType },
            [Flag] = new ItemRecord { ItemId = Flag, Name = "San d'Orian Flag", ItemType = MogHouseFurniture.FurnishingItemType },
            [BrassFlowerpot] = new ItemRecord { ItemId = BrassFlowerpot, Name = "Brass Flowerpot", ItemType = MogHouseFurniture.FlowerpotItemType },
            [HumeMannequin] = new ItemRecord { ItemId = HumeMannequin, Name = "H Mannequin", ItemType = MogHouseFurniture.MannequinItemType, EquipSlotsMask = 28 },
            [BronzeCap] = new ItemRecord { ItemId = BronzeCap, Name = "Bronze Cap", ItemType = 5, EquipSlotsMask = 1 << (int)EquipSlotId.Head },
            [LeatherVest] = new ItemRecord { ItemId = LeatherVest, Name = "Leather Vest", ItemType = 5, EquipSlotsMask = 1 << (int)EquipSlotId.Body },
        };

        private static readonly Dictionary<ushort, FurnitureInfo> Furniture = new()
        {
            [SimpleBed] = new FurnitureInfo(FurnitureFlags.Floor, 4, 6, 70),
            [OakBed] = new FurnitureInfo(FurnitureFlags.Floor, 4, 6, 110),
            [OakTable] = new FurnitureInfo(FurnitureFlags.Surface, 3, 4, 100),
            [Vase] = new FurnitureInfo(FurnitureFlags.OnTable, 1, 1, 10),
            [Flag] = new FurnitureInfo(FurnitureFlags.Wall, 3, 1, 250),
            [BrassFlowerpot] = new FurnitureInfo(FurnitureFlags.OnTable | FurnitureFlags.Flowerpot, 1, 1, 100),
            [HumeMannequin] = new FurnitureInfo(FurnitureFlags.Floor, 3, 2, 1),
        };

        private static UiMenuDefinition List(string name, short x, short y, int count)
        {
            var buttons = new List<UiMenuButton>();
            for (int i = 1; i <= count; i++)
            {
                buttons.Add(new UiMenuButton
                {
                    ButtonId = (short)i, X = 21, Y = (short)(5 + 16 * (i - 1)), Width = 88, Height = 16,
                    NavUp = (sbyte)(i == 1 ? count : i - 1), NavDown = (sbyte)(i == count ? 1 : i + 1), NavLeft = (sbyte)i, NavRight = (sbyte)i,
                });
            }
            return new UiMenuDefinition { Name = name, Frame = new UiMenuFrame { X = x, Y = y, Width = 112, Height = (short)(8 + 16 * count) }, Buttons = buttons };
        }

        /// <summary><c>inspect</c> as the DAT authors it: the 4 x 4 grid by columns, button 17 under it.</summary>
        private static UiMenuDefinition MannequinFrame()
        {
            var buttons = new List<UiMenuButton>();
            for (int i = 1; i <= 16; i++)
            {
                int c = (i - 1) / 4, r = (i - 1) % 4;
                buttons.Add(new UiMenuButton
                {
                    ButtonId = (short)i, X = (short)(24 + 34 * c), Y = (short)(11 + 34 * r), Width = 32, Height = 32,
                    NavUp = (sbyte)(r == 0 ? 17 : i - 1), NavDown = (sbyte)(r == 3 ? 17 : i + 1), NavLeft = (sbyte)(c == 0 ? i + 12 : i - 4), NavRight = (sbyte)(c == 3 ? i - 12 : i + 4),
                });
            }
            buttons.Add(new UiMenuButton { ButtonId = 17, X = 48, Y = 148, Width = 88, Height = 16, NavUp = 4, NavDown = 1, NavLeft = 17, NavRight = 17 });
            return new UiMenuDefinition { Name = StockUiCheck.MenuName, Frame = new UiMenuFrame { X = 16, Y = 48, Width = 182, Height = 190 }, Buttons = buttons };
        }

        private static UiMenuDefinition ItemList() => new()
        {
            Name = StockUiItems.ListMenu,
            Frame = new UiMenuFrame { X = 16, Y = 48, Width = 182, Height = 190, HelpTextId = 3 },
            Buttons = Enumerable.Range(1, StockUiItems.ListRows).Select(i => new UiMenuButton
            {
                ButtonId = (short)i, X = 3, Y = (short)(6 + 18 * (i - 1)), Width = 132, Height = 16,
                NavUp = (sbyte)(i == 1 ? 1 : i - 1), NavDown = (sbyte)(i == StockUiItems.ListRows ? i : i + 1), NavLeft = (sbyte)i, NavRight = (sbyte)i,
            }).ToList(),
        };

        private static UiResourceLibrary Library() => UiResourceLibrary.FromDefinitions(new[]
        {
            List(StockUiMogHouse.MoogleMenu, 16, 208, 7),
            List(StockUiMogHouse.AwayMoogleMenu, 16, 272, 3),
            List(StockUiMenuEntries.MainMenu, 384, 48, 12),
            List(StockUiMogHouse.SafeMenu, 384, 48, 2),
            MannequinFrame(),
            ItemList(),
            List(StockUiMogHouse.LayoutFloorMenu, 16, 240, 3),
            List(StockUiMogHouse.LayoutPieceMenu, 16, 224, 4),
            List(StockUiMogHouse.ClearAllMenu, 16, 256, 2),
            List(StockUiMogHouse.LayoutMannequinMenu, 16, 208, 5),
            List(StockUiViewHouse.MenuName, 384, 48, 15),
            List(StockUiMenuEntries.MessageYesNoMenu, 130, 200, 2),
            new UiMenuDefinition
            {
                Name = StockUiMenuController.QueryMenu,
                Frame = new UiMenuFrame { X = 16, Y = 208, Width = 366, Height = 88 },
                Buttons = Enumerable.Range(1, 3).Select(i => new UiMenuButton { ButtonId = (short)i, X = 28, Y = (short)(28 + 20 * (i - 1)), Width = 132, Height = 16 }).ToList(),
            },
        });

        private sealed class House : IStockUiMogHouse
        {
            public readonly List<string> Sent = new();
            public bool OnSecondFloor { get; set; }

            private Task Record(string packet)
            {
                lock (Sent) Sent.Add(packet);
                return Task.CompletedTask;
            }

            public Task PlaceAsync(FurniturePlacement piece, bool secondFloor, byte x, byte level, byte z, byte rotation) =>
                Record($"0x0FA {piece.ItemId} {(byte)piece.Container}:{piece.Slot} f{(secondFloor ? 1 : 0)} {x},{level},{z} r{rotation}");
            public Task FinishLayoutAsync() => Record("0x0FA 0");
            public Task ClearAwayAsync(FurniturePlacement piece) => Record($"0x0FB {piece.ItemId} {(byte)piece.Container}:{piece.Slot}");
            public Task SetHouseOpenAsync(bool open) => Record(open ? "0x0CB Open" : "0x0CB Close");
            public Task RemodelAsync(ushort style) => Record($"0x0CB Remodel {style}");
            public Task MannequinAsync(SubcontainerKind kind, FurniturePlacement mannequin, SubcontainerSlotIndex slot, byte storageSlot) =>
                Record($"0x03B {kind} {(byte)mannequin.Container}:{mannequin.Slot} {slot} {storageSlot}");
        }

        private sealed class Harness
        {
            public readonly InventoryState Inventory = new();
            public readonly StockUiMenuController Menus = new() { Library = Library() };
            public readonly House House = new();
            public readonly List<string> Notices = new();
            public bool InMogHouse = true;

            public Harness()
            {
                Menus.Inventory = Inventory;
                Menus.ItemLookup = id => Records.TryGetValue(id, out var r) ? r : null;
                Menus.MogHouse = House;
                Menus.InMogHouse = () => InMogHouse;
                Menus.NoticePosted += Notices.Add;
                Menus.MogHouseLayout.InfoOf = id => Furniture.TryGetValue(id, out var info) ? info : null;
                Inventory.SetContainerSizes(ContainerId.MogSafe, 80, 80);
                Inventory.SetContainerSizes(ContainerId.MogSafe2, 80, 80);
                // A placed bed at (2, 16), a mannequin at (16, 4), an oak bed and a flowerpot in the safes.
                Inventory.SetItemAttributes(ContainerId.MogSafe, 1, SimpleBed, 1, 0, ItemLockFlag.Normal, Ext(true, 2, 16, 0));
                Inventory.SetItemAttributes(ContainerId.MogSafe, 2, HumeMannequin, 1, 0, ItemLockFlag.Normal, MannequinExt(16, 4, head: 3));
                Inventory.SetItemAttributes(ContainerId.MogSafe, 3, OakBed, 1, 0, ItemLockFlag.Normal, Ext(false, 0, 0, 0));
                Inventory.SetItemAttributes(ContainerId.MogSafe2, 1, BrassFlowerpot, 1, 0, ItemLockFlag.Normal, Ext(false, 0, 0, 0));
                Inventory.SetItemAttributes(ContainerId.Storage, 3, BronzeCap, 1, 0, ItemLockFlag.Mannequin, ReadOnlySpan<byte>.Empty);
                Inventory.SetItemAttributes(ContainerId.Storage, 4, LeatherVest, 1, 0, ItemLockFlag.Normal, ReadOnlySpan<byte>.Empty);
            }

            public void Moogle(int button)
            {
                Assert.True(Menus.OpenMoogleMenu());
                Assert.Equal(StockUiMogHouse.MoogleMenu, Menus.Top!.Name);
                Menus.Top!.SelectedButtonId = button;
                Menus.Activate();
            }

            public void Choose(int button)
            {
                Menus.Top!.SelectedButtonId = button;
                Menus.Activate();
            }

            /// <summary>Confirm on a query's option (1-based, the list's first rows).</summary>
            public void Answer(int option)
            {
                var query = Menus.Top!;
                Assert.True(query.IsQuery);
                query.FirstRow = Math.Max(0, option - query.VisibleRows);
                query.SelectedButtonId = option - query.FirstRow;
                Menus.Activate();
            }

            /// <summary>The piece in hand to a cell, a step at a time.</summary>
            public void HoldTo(int x, int z)
            {
                var layout = Menus.MogHouseLayout;
                while (layout.HeldX != x) Menus.MoveLayoutCursor(Math.Sign(x - layout.HeldX), 0);
                while (layout.HeldZ != z) Menus.MoveLayoutCursor(0, Math.Sign(z - layout.HeldZ));
            }

            /// <summary>Confirm on an item of the open Mog Safe list.</summary>
            public void Pick(ushort itemId)
            {
                var list = Menus.Top!;
                Assert.Equal(StockUiItemListKind.Picker, list.ItemListKind);
                list.FirstRow = 0;
                list.SelectedButtonId = list.ItemRows.ToList().FindIndex(r => r.ItemId == itemId) + 1;
                Menus.Activate();
            }

            public async Task AnswerYesNoAsync(bool yes)
            {
                for (int i = 0; i < 200 && Menus.Top?.Name != StockUiMenuEntries.MessageYesNoMenu; i++) await Task.Delay(5);
                Choose(yes ? 1 : 2);
                for (int i = 0; i < 200 && House.Sent.Count == 0 && yes; i++) await Task.Delay(5);
            }
        }

        private static byte[] Ext(bool installed, byte x, byte z, byte rotation)
        {
            var ext = new byte[24];
            ext[1] = installed ? (byte)0x40 : (byte)0;
            ext[6] = x;
            ext[8] = z;
            ext[9] = rotation;
            return ext;
        }

        private static byte[] MannequinExt(byte x, byte z, byte head)
        {
            var ext = Ext(true, x, z, 0);
            ext[13] = head;
            return ext;
        }

        [Fact]
        public void MoogleMenu_OpensMyroomAtHome_AndMogextAway()
        {
            var h = new Harness();
            Assert.True(h.Menus.OpenMoogleMenu());
            Assert.Equal(StockUiMogHouse.MoogleMenu, h.Menus.Top!.Name);
            h.InMogHouse = false;
            Assert.True(h.Menus.OpenMoogleMenu());
            Assert.Equal(StockUiMogHouse.AwayMoogleMenu, h.Menus.Top!.Name);
            Assert.Single(h.Menus.OpenMenus);
        }

        [Fact]
        public void MoogleMenu_MogSafe_OpensTheStorageList_AndUnbuiltEntriesSaySo()
        {
            var h = new Harness();
            h.Moogle(1);
            Assert.Equal(StockUiViewHouse.MenuName, h.Menus.Top!.Name);
            h.Moogle(4);
            Assert.Equal("Gardening is not available yet.", h.Notices.Last());
        }

        [Fact]
        public void Layout_StartsOnAPiece_AndTheDirectionsJumpBetweenPieces()
        {
            var h = new Harness();
            h.Moogle(5);
            var layout = h.Menus.MogHouseLayout;
            Assert.True(layout.Active);
            Assert.Empty(h.Menus.OpenMenus);
            Assert.Equal(SimpleBed, layout.Selected?.ItemId);

            // The mannequin lies to the south-east of the bed; nothing lies west of it.
            h.Menus.MoveLayoutCursor(-1, 0);
            Assert.Equal(SimpleBed, layout.Selected?.ItemId);
            h.Menus.MoveLayoutCursor(1, 0);
            Assert.Equal(HumeMannequin, layout.Selected?.ItemId);
            h.Menus.MoveLayoutCursor(0, 1);
            Assert.Equal(SimpleBed, layout.Selected?.ItemId);
        }

        [Fact]
        public void Layout_PlacesAPieceFromASafe_AndCancelBringsBackTheMoogleMenu()
        {
            var h = new Harness();
            h.Moogle(5);
            var layout = h.Menus.MogHouseLayout;
            h.Menus.ConfirmLayoutCell();
            Assert.Equal(StockUiMogHouse.LayoutPieceMenu, h.Menus.Top!.Name);
            h.Choose(3); // Layout
            Assert.Equal(StockUiMogHouse.SafeMenu, h.Menus.Top!.Name);
            h.Choose(1); // Mog Safe
            h.Pick(OakBed);
            Assert.Empty(h.Menus.OpenMenus);
            Assert.Equal(OakBed, layout.Held?.ItemId);

            // Turned twice; the block in front of the door and the bed refuse it.
            h.Menus.TurnHeldPiece();
            h.Menus.TurnHeldPiece();
            Assert.Equal(2, layout.Rotation);
            h.HoldTo(8, 4);
            h.Menus.ConfirmLayoutCell();
            Assert.Equal(StockUiMogHouse.CellNotUsableNotice, h.Notices.Last());
            h.HoldTo(3, 15);
            Assert.False(layout.CanPlaceHeld());
            Assert.Empty(h.House.Sent);
            h.HoldTo(1, 4);
            h.Menus.ConfirmLayoutCell();
            Assert.Null(layout.Held);
            Assert.Equal(new[] { $"0x0FA {OakBed} {(byte)ContainerId.MogSafe}:3 f0 1,0,4 r2" }, h.House.Sent);
            Assert.Equal(OakBed, layout.Selected?.ItemId); // the cursor stays on it, before S2C 0x020 comes back

            // Cancel ends the Layout: 0x0FA with every field 0, and the moogle's menu is back on Layout.
            h.Menus.CancelLayout();
            Assert.False(layout.Active);
            Assert.Equal("0x0FA 0", h.House.Sent.Last());
            Assert.Equal(StockUiMogHouse.MoogleMenu, h.Menus.Top!.Name);
            Assert.Equal(StockUiMogHouse.LayoutButton, h.Menus.Top!.SelectedButtonId);
        }

        [Fact]
        public void Layout_OnAPiece_RearrangesOrRemovesIt()
        {
            var h = new Harness();
            h.Moogle(5);
            var layout = h.Menus.MogHouseLayout;
            h.Menus.ConfirmLayoutCell();
            h.Choose(1); // Rearrange
            Assert.Equal(SimpleBed, layout.Held?.ItemId);
            Assert.Equal((2, 16), (layout.HeldX, layout.HeldZ));

            // Cancel puts it back without a packet, and the Layout stays.
            h.Menus.MoveLayoutCursor(1, 0);
            h.Menus.CancelLayout();
            Assert.Null(layout.Held);
            Assert.True(layout.Active);
            Assert.Empty(h.House.Sent);

            // Remove: 0x0FB, and the cursor goes on to the mannequin.
            h.Menus.ConfirmLayoutCell();
            Assert.False(h.Menus.Top!.IsGreyed(StockUiMogHouse.PieceRemoveButton));
            Assert.True(h.Menus.Top!.IsGreyed(StockUiMogHouse.PieceRemoveAllButton)); // the Storage still holds 2
            h.Choose(2);
            Assert.Equal(new[] { $"0x0FB {SimpleBed} {(byte)ContainerId.MogSafe}:1" }, h.House.Sent);
            Assert.Equal(HumeMannequin, layout.Selected?.ItemId);
        }

        [Fact]
        public void Layout_MovesTheFootprintWithItsRotation_OnTheGrid()
        {
            var h = new Harness();
            h.Moogle(5);
            var layout = h.Menus.MogHouseLayout;
            h.Menus.ConfirmLayoutCell();
            h.Choose(1);
            // 4 x 6 turned is 6 x 4; it stops at the grid's east edge.
            h.Menus.TurnHeldPiece();
            for (int i = 0; i < 30; i++) h.Menus.MoveLayoutCursor(1, 0);
            Assert.Equal(MogHouseFurniture.GridWidth - 6, layout.HeldX);
        }

        [Fact]
        public void Layout_ATableTakesItsPieces_AndAWallPieceWalksTheSlots()
        {
            var h = new Harness();
            h.Inventory.SetItemAttributes(ContainerId.MogSafe, 4, OakTable, 1, 0, ItemLockFlag.Normal, Ext(true, 2, 8, 0));
            h.Inventory.SetItemAttributes(ContainerId.MogSafe, 5, Vase, 1, 0, ItemLockFlag.Normal, Ext(false, 0, 0, 0));
            h.Inventory.SetItemAttributes(ContainerId.MogSafe, 6, Flag, 1, 0, ItemLockFlag.Normal, Ext(false, 0, 0, 0));
            h.Moogle(5);
            var layout = h.Menus.MogHouseLayout;

            // A vase set on the table stands at the table's height / 10.
            h.Menus.ConfirmLayoutCell();
            h.Choose(3);
            h.Choose(1);
            h.Pick(Vase);
            h.HoldTo(3, 9);
            Assert.Equal(10, layout.HeldLevel());
            h.Menus.ConfirmLayoutCell();
            Assert.Equal($"0x0FA {Vase} {(byte)ContainerId.MogSafe}:5 f0 3,10,9 r0", h.House.Sent.Last());

            // Moving the table sends the vase first, the same offset away.
            h.Menus.MoveLayoutCursor(0, 1); // onto the table
            Assert.Equal(OakTable, layout.Selected?.ItemId);
            h.Menus.ConfirmLayoutCell();
            h.Choose(1);
            h.HoldTo(3, 8);
            h.House.Sent.Clear();
            h.Menus.ConfirmLayoutCell();
            for (int i = 0; i < 200 && h.House.Sent.Count < 2; i++) Task.Delay(5).Wait();
            Assert.Equal(new[] { $"0x0FA {Vase} {(byte)ContainerId.MogSafe}:5 f0 4,10,9 r0", $"0x0FA {OakTable} {(byte)ContainerId.MogSafe}:4 f0 3,0,8 r0" }, h.House.Sent);

            // A wall piece: the slots, not the cells, moved as retail moves them (Up / Down along a wall, Left / Right across).
            h.Menus.ConfirmLayoutCell();
            h.Choose(3);
            h.Choose(1);
            h.Pick(Flag);
            Assert.Equal(0, layout.HeldX); // east wall, north
            h.Menus.MoveLayoutCursor(1, 0);
            Assert.Equal(0, layout.HeldX); // no wall further east
            h.Menus.MoveLayoutCursor(0, -1);
            Assert.Equal(1, layout.HeldX); // east wall, south
            h.Menus.MoveLayoutCursor(-1, 0);
            Assert.Equal(2, layout.HeldX); // west wall, south
            h.Menus.MoveLayoutCursor(0, 1);
            h.Menus.MoveLayoutCursor(0, 1);
            h.Menus.MoveLayoutCursor(0, 1);
            Assert.Equal(4, layout.HeldX); // west wall, north, and no further
            h.Menus.TurnHeldPiece();
            h.Menus.ConfirmLayoutCell();
            Assert.Equal($"0x0FA {Flag} {(byte)ContainerId.MogSafe}:6 f0 4,0,0 r0", h.House.Sent.Last());
        }

        [Fact]
        public void Layout_RemoveAll_ClearsEveryPieceOnTheFloor()
        {
            var h = new Harness();
            h.Inventory.RemoveItem(ContainerId.Storage, 3); // nothing in the Storage: the room may lose all its slots
            h.Inventory.RemoveItem(ContainerId.Storage, 4);
            h.Moogle(5);
            h.Menus.ConfirmLayoutCell();
            h.Choose(4); // rmlo2 Remove All
            Assert.Equal(StockUiMogHouse.ClearAllMenu, h.Menus.Top!.Name);
            h.Choose(2); // Exit
            Assert.Equal(StockUiMogHouse.LayoutPieceMenu, h.Menus.Top!.Name);
            h.Choose(4);
            h.Choose(1);
            Assert.Equal(2, h.House.Sent.Count(s => s.StartsWith("0x0FB")));
            Assert.Null(h.Menus.MogHouseLayout.Selected);

            // Nothing placed: Confirm opens the empty room's menu.
            h.Menus.ConfirmLayoutCell();
            Assert.Equal(StockUiMogHouse.LayoutFloorMenu, h.Menus.Top!.Name);
        }

        [Fact]
        public void Layout_OnTheSecondFloor_LeavesFlowerpotsOutAndShowsNoFirstFloorPieces()
        {
            var h = new Harness();
            h.House.OnSecondFloor = true;
            h.Moogle(5);
            var layout = h.Menus.MogHouseLayout;
            Assert.True(layout.SecondFloor);
            Assert.Null(layout.Selected);
            h.Menus.ConfirmLayoutCell();
            h.Choose(1);
            h.Choose(2); // Mog Safe 2: only the flowerpot, which the second floor refuses
            Assert.True(h.Menus.Top!.ItemRows.Single().Greyed);
        }

        [Fact]
        public void MainMenu_InTheMogHouse_ReadsMogHouseAndOpensTheMooglesMenu()
        {
            var h = new Harness();
            Assert.True(h.Menus.OpenMainMenu());
            var main = h.Menus.Top!;
            Assert.Equal(StockUiMogHouse.MogHouseLabelImage, main.LabelImages[StockUiMogHouse.MainMenuButton].ImageIndex);
            Assert.Equal(StockUiMogHouse.MogHouseHelp, main.HelpTextIdOf(main.Menu.FindButton(StockUiMogHouse.MainMenuButton)!));
            main.SelectedButtonId = StockUiMogHouse.MainMenuButton;
            h.Menus.Activate();
            Assert.Equal(StockUiMogHouse.MoogleMenu, h.Menus.Top!.Name);

            h.Menus.CloseAll();
            h.InMogHouse = false;
            Assert.True(h.Menus.OpenMainMenu());
            Assert.Empty(h.Menus.Top!.LabelImages);
        }

        [Fact]
        public void Mannequin_WindowDressesItFromTheStorage()
        {
            var h = new Harness();
            h.Moogle(5);
            h.Menus.MoveLayoutCursor(1, 0);
            h.Menus.ConfirmLayoutCell();
            Assert.Equal(StockUiMogHouse.LayoutMannequinMenu, h.Menus.Top!.Name);
            h.Choose(1); // Equip
            var window = h.Menus.Top!;
            Assert.True(window.IsMannequinWindow);
            Assert.Single(h.Menus.OpenMenus);
            Assert.Equal(HumeMannequin, h.Menus.OpenMannequin?.ItemId);
            Assert.Equal(StockUiMogHouse.UnequipAllImage, window.LabelImages[StockUiMogHouse.UnequipAllButton].ImageIndex);
            Assert.Equal(new ushort[] { BronzeCap, LeatherVest }, window.CompanionRows.Select(r => r.ItemId));
            Assert.True(window.IsGreyed(13)); // Ammo: dim
            Assert.False(window.IsGreyed(1));
            Assert.Equal(BronzeCap, window.MannequinItemIn(2)); // Head, worn from Storage slot 3

            // The cursor passes the dim slots: Main to Sub, Ranged, then past Ammo round to Main.
            h.Menus.Move(Gordian.Engine.Input.InputAction.MenuRight);
            Assert.Equal(5, window.SelectedButtonId);
            h.Menus.Move(Gordian.Engine.Input.InputAction.MenuRight);
            Assert.Equal(9, window.SelectedButtonId);
            h.Menus.Move(Gordian.Engine.Input.InputAction.MenuRight);
            Assert.Equal(1, window.SelectedButtonId);

            // Body: what fits, from the Storage, beside the grid; the pick goes out and the window stays.
            h.Choose(3);
            Assert.Equal(new ushort[] { LeatherVest }, h.Menus.Top!.ItemRows.Select(r => r.ItemId));
            h.Pick(LeatherVest);
            Assert.Same(window, h.Menus.Top);
            Assert.Equal($"0x03B Equip {(byte)ContainerId.MogSafe}:2 Body 4", h.House.Sent.Last());

            // Confirm on what a slot wears takes it off.
            h.Choose(2);
            Assert.True(h.Menus.Top!.ItemRows[0].Equipped);
            h.Pick(BronzeCap);
            Assert.Equal($"0x03B Unequip {(byte)ContainerId.MogSafe}:2 Head 3", h.House.Sent.Last());

            h.Choose(StockUiMogHouse.UnequipAllButton);
            for (int i = 0; i < 200 && !h.House.Sent.Last().Contains("UnequipAll"); i++) Task.Delay(5).Wait();
            Assert.Equal($"0x03B UnequipAll {(byte)ContainerId.MogSafe}:2 MainWeapon 0", h.House.Sent.Last());

            h.Menus.CloseTop();
            Assert.Null(h.Menus.OpenMannequin);
            Assert.True(h.Menus.MogHouseLayout.Active);
        }

        [Fact]
        public void Mannequins_AreEntitiesInTheRoom()
        {
            var h = new Harness();
            h.Moogle(5);
            var world = new WorldState();
            var mannequins = new MogHouseMannequins(world, h.Inventory, h.Menus.MogHouseLayout);
            mannequins.Sync(inRoom: true);
            var shown = world.Entities.Single();
            Assert.Equal(string.Empty, shown.Name); // nothing targets it
            Assert.Equal((1 << 8) | MogHouseMannequins.FaceModel, shown.Appearance.FaceModel);
            Assert.Equal(0x1000, shown.Appearance.Head);

            // The server's look replaces the bare one; a turn of the spin comes round.
            h.Inventory.SetMannequinLook(ContainerId.MogSafe, 2, new MannequinLook(0x1012, 0x2000, 0x3000, 0x4000, 0x5000, 0x6000, 0x7000, 0x8000, 3, 0));
            mannequins.Sync(inRoom: true);
            Assert.Equal(0x1012, shown.Appearance.Head);
            Assert.Equal((3 << 8) | MogHouseMannequins.FaceModel, shown.Appearance.FaceModel);
            Assert.Equal(MogHouseMannequins.FacingOf(h.Menus.MogHouseLayout.Pieces.Single(p => p.ItemId == HumeMannequin).Rotation), shown.Direction);
            Assert.Equal(new byte[] { 0, 64, 128, 192 }, new[] { 0, 1, 2, 3 }.Select(MogHouseMannequins.FacingOf)); // E, S, W, N
            byte facing = shown.Direction;
            mannequins.Spin(h.Menus.MogHouseLayout.Pieces.Single(p => p.ItemId == HumeMannequin), MogHouseMannequins.SpinSecondsPerTurn / 4);
            Assert.Equal((byte)(facing + 64), shown.Direction);

            mannequins.Sync(inRoom: false);
            Assert.Empty(world.Entities);
        }

        [Fact]
        public void Safe2_IsGreyedUntilUnlocked_AndReadsSafe2()
        {
            var h = new Harness();
            h.Menus.MogSafe2Unlocked = () => false;
            h.Moogle(5);
            h.Menus.ConfirmLayoutCell();
            h.Choose(3);
            Assert.True(h.Menus.Top!.IsGreyed(StockUiMogHouse.SafeMenuSafe2Button));
            h.Menus.CloseAll();
            h.Menus.MogSafe2Unlocked = () => true;
            h.Menus.ConfirmLayoutCell();
            h.Choose(3);
            Assert.False(h.Menus.Top!.IsGreyed(StockUiMogHouse.SafeMenuSafe2Button));

            h.Menus.CloseAll();
            h.Menus.CancelLayout();
            h.Menus.CloseAll();
            h.Menus.MogSafe2Unlocked = () => false;
            h.Moogle(1); // Storage: mogcont
            Assert.True(h.Menus.Top!.IsGreyed(StockUiMogHouse.ViewHouseSafe2Button));
            Assert.Equal("Safe 2", StockUiViewHouse.Title(ContainerId.MogSafe2));
        }

        [Fact]
        public void SafeList_ShowsPlacedPiecesGreen_AndPickingOneTakesItUp()
        {
            var h = new Harness();
            h.Moogle(5);
            var layout = h.Menus.MogHouseLayout;
            h.Menus.ConfirmLayoutCell();
            h.Choose(3);
            h.Choose(1);
            var rows = h.Menus.Top!.ItemRows;
            Assert.Equal(new ushort[] { SimpleBed, HumeMannequin, OakBed }, rows.Select(r => r.ItemId).OrderBy(i => i == SimpleBed ? 0 : i == HumeMannequin ? 1 : 2));
            Assert.True(rows.Single(r => r.ItemId == SimpleBed).Equipped);
            Assert.False(rows.Single(r => r.ItemId == OakBed).Equipped);
            h.Pick(SimpleBed);
            Assert.Equal(SimpleBed, layout.Held?.ItemId);
            Assert.Equal((2, 16), (layout.HeldX, layout.HeldZ));
        }

        [Fact]
        public void ViewHouse_GreysPlacedFurniture()
        {
            var h = new Harness();
            h.Moogle(1);
            h.Choose(1); // Mog Safe
            var rows = h.Menus.Top!.ItemRows;
            Assert.True(rows.Single(r => r.ItemId == SimpleBed).Greyed);
            Assert.False(rows.Single(r => r.ItemId == OakBed).Greyed);
        }

        [Fact]
        public void Remove_IsRefusedWhenTheStorageCouldNotShrink()
        {
            // The bed gives 2 slots, the mannequin 28: the Storage's 2 items need the mannequin, not the bed.
            var h = new Harness();
            h.Moogle(5);
            var layout = h.Menus.MogHouseLayout;
            for (byte slot = 10; slot < 38; slot++) h.Inventory.SetItemAttributes(ContainerId.Storage, slot, LeatherVest, 1, 0, ItemLockFlag.Normal, ReadOnlySpan<byte>.Empty);
            h.Menus.ConfirmLayoutCell();
            Assert.True(h.Menus.Top!.IsGreyed(StockUiMogHouse.PieceRemoveButton)); // the bed: 30 items, 28 slots left
            Assert.True(h.Menus.Top!.IsGreyed(StockUiMogHouse.PieceRemoveAllButton));
            h.Choose(2);
            Assert.Empty(h.House.Sent);
            Assert.Equal(SimpleBed, layout.Selected?.ItemId);
            h.Menus.CloseAll();

            for (byte slot = 12; slot < 38; slot++) h.Inventory.RemoveItem(ContainerId.Storage, slot);
            h.Menus.ConfirmLayoutCell();
            h.Choose(2);
            Assert.Equal(new[] { $"0x0FB {SimpleBed} {(byte)ContainerId.MogSafe}:1" }, h.House.Sent);
        }

        [Fact]
        public async Task OpenMog_ReadsCloseMog_AndGreysLayoutAndRemodel()
        {
            var h = new Harness();
            h.Moogle(6);
            await h.AnswerYesNoAsync(true);
            Assert.Equal(new[] { "0x0CB Open" }, h.House.Sent);
            Assert.True(h.Menus.MogHouseOpenToVisitors);

            // The menu stays up with the new look.
            var menu = h.Menus.Top!;
            Assert.Equal(StockUiMogHouse.MoogleMenu, menu.Name);
            Assert.Equal(StockUiMogHouse.CloseMogLabelImage, menu.LabelImages[StockUiMogHouse.OpenHouseButton].ImageIndex);
            Assert.True(menu.IsGreyed(StockUiMogHouse.LayoutButton));
            Assert.True(menu.IsGreyed(StockUiMogHouse.RemodelButton));
            h.Choose(StockUiMogHouse.LayoutButton);
            Assert.False(h.Menus.MogHouseLayout.Active);

            h.House.Sent.Clear();
            h.Choose(StockUiMogHouse.OpenHouseButton);
            await h.AnswerYesNoAsync(true);
            Assert.Equal(new[] { "0x0CB Close" }, h.House.Sent);
            Assert.False(h.Menus.MogHouseOpenToVisitors);
            Assert.Empty(h.Menus.Top!.LabelImages);
            Assert.False(h.Menus.Top!.IsGreyed(StockUiMogHouse.LayoutButton));
        }

        [Fact]
        public void Remodel_SendsTheChosenStyle()
        {
            var h = new Harness();
            h.Moogle(7);
            Assert.Equal(4, h.Menus.Top!.Rows.Count);
            h.Answer(2);
            Assert.Equal(new[] { "0x0CB Remodel 616" }, h.House.Sent);
        }

        [Fact]
        public void ZoneChange_EndsTheLayout()
        {
            var h = new Harness();
            h.Moogle(5);
            h.Menus.OnMogHouseZoneChanged();
            Assert.False(h.Menus.MogHouseLayout.Active);
        }
    }
}
