// tests/Gordian.Engine.Tests/Ui/StockUiMogHouseTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Ui;
using Gordian.Core.World;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Ui
{
    /// <summary>The Mog House menus (#93): the moogle's menu, Layout (C2S 0x0FA / 0x0FB), Open Mog House / Remodel (0x0CB), the mannequin (0x03B).</summary>
    public class StockUiMogHouseTests
    {
        private const ushort SimpleBed = 2, OakBed = 3, BrassFlowerpot = 217, HumeMannequin = 256, BronzeCap = 12448, LeatherVest = 12568;

        private static readonly Dictionary<ushort, ItemRecord> Records = new()
        {
            [SimpleBed] = new ItemRecord { ItemId = SimpleBed, Name = "Simple Bed", ItemType = MogHouseFurniture.FurnishingItemType },
            [OakBed] = new ItemRecord { ItemId = OakBed, Name = "Oak Bed", ItemType = MogHouseFurniture.FurnishingItemType },
            [BrassFlowerpot] = new ItemRecord { ItemId = BrassFlowerpot, Name = "Brass Flowerpot", ItemType = MogHouseFurniture.FlowerpotItemType },
            [HumeMannequin] = new ItemRecord { ItemId = HumeMannequin, Name = "H Mannequin", ItemType = MogHouseFurniture.MannequinItemType },
            [BronzeCap] = new ItemRecord { ItemId = BronzeCap, Name = "Bronze Cap", EquipSlotsMask = 1 << (int)EquipSlotId.Head },
            [LeatherVest] = new ItemRecord { ItemId = LeatherVest, Name = "Leather Vest", EquipSlotsMask = 1 << (int)EquipSlotId.Body },
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

        private static UiResourceLibrary Library() => UiResourceLibrary.FromDefinitions(new[]
        {
            List(StockUiMogHouse.MoogleMenu, 16, 208, 7),
            List(StockUiMogHouse.AwayMoogleMenu, 16, 272, 3),
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
                // A placed bed at (2, 20), a mannequin at (16, 4), an oak bed and a flowerpot in the safes.
                Inventory.SetItemAttributes(ContainerId.MogSafe, 1, SimpleBed, 1, 0, ItemLockFlag.Normal, Ext(true, 2, 20, 1));
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

            public void CursorTo(int x, int z)
            {
                var layout = Menus.MogHouseLayout;
                Menus.MoveLayoutCursor(x - layout.CursorX, 0);
                Menus.MoveLayoutCursor(0, z - layout.CursorZ);
                Assert.Equal((x, z), (layout.CursorX, layout.CursorZ));
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
        public void Layout_PlacesAPieceFromTheSafe_AndQuitSendsTheEmptyLayout()
        {
            var h = new Harness();
            h.Moogle(5);
            var layout = h.Menus.MogHouseLayout;
            Assert.True(layout.Active);
            Assert.Empty(h.Menus.OpenMenus);
            Assert.Equal(4, layout.Pieces.Count);

            // Confirm on empty floor: rmlo1; Place lists what is not placed (the oak bed and the flowerpot).
            h.CursorTo(4, 10);
            h.Menus.ConfirmLayoutCell();
            Assert.Equal(StockUiMogHouse.LayoutFloorMenu, h.Menus.Top!.Name);
            h.Choose(1);
            Assert.Equal(new[] { "Oak Bed", "Brass Flowerpot" }, h.Menus.Top!.Rows.Select(r => r.Text));
            h.Answer(1);
            Assert.Empty(h.Menus.OpenMenus);
            Assert.Equal(OakBed, layout.Held?.ItemId);

            // Turn it twice, try the block in front of the door, then place it.
            h.Menus.TurnHeldPiece();
            h.Menus.TurnHeldPiece();
            h.CursorTo(8, 4);
            h.Menus.ConfirmLayoutCell();
            Assert.Equal(StockUiMogHouse.CellNotUsableNotice, h.Notices.Last());
            Assert.Empty(h.House.Sent);
            h.CursorTo(4, 10);
            h.Menus.ConfirmLayoutCell();
            Assert.Null(layout.Held);
            Assert.Equal(new[] { $"0x0FA {OakBed} {(byte)ContainerId.MogSafe}:3 f0 4,0,10 r2" }, h.House.Sent);

            // Cancel on the grid ends the layout: 0x0FA with every field 0.
            h.Menus.CancelLayout();
            Assert.False(layout.Active);
            Assert.Equal("0x0FA 0", h.House.Sent.Last());
        }

        [Fact]
        public void Layout_OnAPiece_MovesOrClearsIt()
        {
            var h = new Harness();
            h.Moogle(5);
            var layout = h.Menus.MogHouseLayout;
            h.CursorTo(2, 20);
            h.Menus.ConfirmLayoutCell();
            Assert.Equal(StockUiMogHouse.LayoutPieceMenu, h.Menus.Top!.Name);

            // Move: the bed follows the cursor with its rotation; Cancel puts it back without a packet.
            h.Choose(1);
            Assert.Equal(SimpleBed, layout.Held?.ItemId);
            Assert.Equal(1, layout.Rotation);
            Assert.Null(layout.PieceAt(2, 20)); // the held piece is not drawn at its old cell
            h.Menus.CancelLayout();
            Assert.Null(layout.Held);
            Assert.True(layout.Active);
            Assert.Empty(h.House.Sent);

            // Clear Away: 0x0FB.
            h.Menus.ConfirmLayoutCell();
            h.Choose(2);
            Assert.Equal(new[] { $"0x0FB {SimpleBed} {(byte)ContainerId.MogSafe}:1" }, h.House.Sent);
        }

        [Fact]
        public void Layout_ClearAll_ClearsEveryPieceOnTheFloor()
        {
            var h = new Harness();
            h.Moogle(5);
            h.Menus.ConfirmLayoutCell();
            h.Choose(2); // rmlo1 Clear All
            Assert.Equal(StockUiMogHouse.ClearAllMenu, h.Menus.Top!.Name);
            h.Choose(2); // Return
            Assert.Equal(StockUiMogHouse.LayoutFloorMenu, h.Menus.Top!.Name);
            h.Choose(2);
            h.Choose(1);
            Assert.Equal(2, h.House.Sent.Count(s => s.StartsWith("0x0FB")));
        }

        [Fact]
        public void Layout_OnTheSecondFloor_LeavesFlowerpotsOutAndShowsNoFirstFloorPieces()
        {
            var h = new Harness();
            h.House.OnSecondFloor = true;
            h.Moogle(5);
            var layout = h.Menus.MogHouseLayout;
            Assert.True(layout.SecondFloor);
            Assert.Null(layout.PieceAt(2, 20));
            h.Menus.ConfirmLayoutCell();
            h.Choose(1);
            Assert.Equal(new[] { "Oak Bed" }, h.Menus.Top!.Rows.Select(r => r.Text));
        }

        [Fact]
        public void Mannequin_EquipsFromTheStorage_AndRemoves()
        {
            var h = new Harness();
            h.Moogle(5);
            h.CursorTo(16, 4);
            h.Menus.ConfirmLayoutCell();
            Assert.Equal(StockUiMogHouse.LayoutMannequinMenu, h.Menus.Top!.Name);
            h.Choose(1);
            var slots = h.Menus.Top!;
            Assert.Equal("Head: Bronze Cap", slots.Rows[3].Text);
            Assert.Equal("Body: -", slots.Rows[4].Text);
            Assert.Equal(StockUiMogHouse.RemoveAllRow, slots.Rows[8].Text);

            // Body: the Storage's body armour.
            h.Answer(5);
            Assert.Equal(new[] { "Leather Vest" }, h.Menus.Top!.Rows.Select(r => r.Text));
            h.Answer(1);
            Assert.Equal($"0x03B Equip {(byte)ContainerId.MogSafe}:2 Body 4", h.House.Sent.Last());

            // Head: Remove first.
            h.Menus.ConfirmLayoutCell();
            h.Choose(1);
            h.Answer(4);
            Assert.Equal(StockUiMogHouse.RemoveRow, h.Menus.Top!.Rows[0].Text);
            h.Answer(1);
            Assert.Equal($"0x03B Unequip {(byte)ContainerId.MogSafe}:2 Head 3", h.House.Sent.Last());

            // Remove all.
            h.Menus.ConfirmLayoutCell();
            h.Choose(1);
            h.Answer(9);
            Assert.Equal($"0x03B UnequipAll {(byte)ContainerId.MogSafe}:2 MainWeapon 0", h.House.Sent.Last());
        }

        [Fact]
        public async Task OpenMogHouse_AsksAndToggles_AndLayoutIsRefusedWhileOpen()
        {
            var h = new Harness();
            h.Moogle(6);
            await h.AnswerYesNoAsync(true);
            Assert.Equal(new[] { "0x0CB Open" }, h.House.Sent);
            Assert.True(h.Menus.MogHouseOpenToVisitors);

            h.Moogle(5);
            Assert.Equal(StockUiMogHouse.LayoutWhileOpenNotice, h.Notices.Last());
            Assert.False(h.Menus.MogHouseLayout.Active);

            h.Moogle(6);
            h.House.Sent.Clear();
            await h.AnswerYesNoAsync(true);
            Assert.Equal(new[] { "0x0CB Close" }, h.House.Sent);
            Assert.False(h.Menus.MogHouseOpenToVisitors);
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
