// src/Gordian.Engine/Ui/StockUiMenuController.MogHouse.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Gordian.Engine.Input;

namespace Gordian.Engine.Ui
{
    public sealed partial class StockUiMenuController
    {
        /// <summary>The Mog House's packets (#93); null in a session without them.</summary>
        public IStockUiMogHouse? MogHouse { get; set; }

        /// <summary>The layout grid while Layout is open (#93); drawn by the App.</summary>
        public MogHouseLayoutState MogHouseLayout { get; } = new();

        /// <summary>
        /// Whether the Mog House is open to party and alliance members. The server does not say (LandSandBoat keeps it and
        /// answers nothing), so the client remembers what it sent; a zone change closes it (<see cref="OnMogHouseZoneChanged"/>).
        /// </summary>
        public bool MogHouseOpenToVisitors { get; private set; }

        /// <summary>
        /// S2C 0x02E: the moogle's menu. In the character's own Mog House <c>myroom</c>, elsewhere (a Nomad Moogle)
        /// <c>mogext</c>; it replaces whatever menu was open.
        /// </summary>
        public bool OpenMoogleMenu()
        {
            string name = InMogHouse?.Invoke() == true ? StockUiMogHouse.MoogleMenu : StockUiMogHouse.AwayMoogleMenu;
            CloseAll();
            lock (_sync) return Push(name, null, Array.Empty<string>(), null, null) != null;
        }

        /// <summary>A zone change ends the layout and closes the house to visitors.</summary>
        public void OnMogHouseZoneChanged()
        {
            MogHouseLayout.Active = false;
            MogHouseLayout.Held = null;
            MogHouseOpenToVisitors = false;
            Changed?.Invoke();
        }

        /// <summary>The Mog Safes changed (S2C 0x020, 0x0FA): the layout grid reads the furniture again.</summary>
        public void OnMogHouseFurnitureChanged()
        {
            if (!MogHouseLayout.Active) return;
            RefreshFurniture();
            Changed?.Invoke();
        }

        private void RefreshFurniture()
        {
            if (Inventory is { } inventory) MogHouseLayout.SetPieces(MogHouseFurniture.Snapshot(inventory, ItemLookup));
        }

        private bool IsFlowerpot(ushort itemId) => ItemLookup?.Invoke(itemId)?.ItemType == MogHouseFurniture.FlowerpotItemType;

        private string ItemName(ushort itemId) => ItemLookup?.Invoke(itemId)?.Name is { Length: > 0 } name ? name : $"Item {itemId}";

        /// <summary>A Mog House menu entry (#93).</summary>
        private void RunMogHouseAction(StockUiMenuEntry entry, StockUiOpenMenu from)
        {
            var action = (StockUiMogHouseAction)entry.Argument;
            if (action == StockUiMogHouseAction.Storages)
            {
                lock (_sync) OpenViewHouse(from);
                return;
            }
            if (action is StockUiMogHouseAction.Delivery or StockUiMogHouseAction.ChangeJobs or StockUiMogHouseAction.Gardening)
            {
                NoticePosted?.Invoke($"{entry.Label} is not available yet.");
                return;
            }
            var house = MogHouse;
            if (house == null)
            {
                NoticePosted?.Invoke($"{entry.Label} is not available in this session.");
                return;
            }
            var layout = MogHouseLayout;
            var piece = layout.PieceAt(layout.CursorX, layout.CursorZ);
            switch (action)
            {
                case StockUiMogHouseAction.Layout:
                    BeginLayout(house);
                    break;
                case StockUiMogHouseAction.OpenHouse:
                    ToggleHouseOpen(house);
                    break;
                case StockUiMogHouseAction.Remodel:
                    OpenStyleList(house);
                    break;
                case StockUiMogHouseAction.Place:
                    OpenFurnitureList();
                    break;
                case StockUiMogHouseAction.ClearAll:
                    lock (_sync) Push(StockUiMogHouse.ClearAllMenu, from, Array.Empty<string>(), null, null);
                    break;
                case StockUiMogHouseAction.QuitLayout:
                    EndLayout(house);
                    break;
                case StockUiMogHouseAction.Move when piece is { } moving:
                    layout.Held = moving;
                    layout.Rotation = moving.Rotation;
                    CloseAll();
                    break;
                case StockUiMogHouseAction.ClearAway when piece is { } cleared:
                    CloseAll();
                    _ = SendMogHouseAsync(() => house.ClearAwayAsync(cleared), "Clear away");
                    break;
                case StockUiMogHouseAction.ClearAllConfirm:
                    CloseAll();
                    foreach (var shown in layout.Pieces)
                    {
                        if (layout.IsShown(shown)) _ = SendMogHouseAsync(() => house.ClearAwayAsync(shown), "Clear away");
                    }
                    break;
                case StockUiMogHouseAction.Return:
                    CloseMenu(from);
                    break;
                case StockUiMogHouseAction.Mannequin when piece is { } mannequin && MogHouseFurniture.IsMannequin(mannequin.ItemId):
                    OpenMannequin(house, mannequin);
                    break;
            }
        }

        /// <summary>
        /// Layout: the menus close and the grid takes the input, the cursor on the first usable cell nearest the room's
        /// middle. LandSandBoat refuses layout packets while the house is open to visitors, so the client says so first.
        /// </summary>
        private void BeginLayout(IStockUiMogHouse house)
        {
            if (MogHouseOpenToVisitors)
            {
                NoticePosted?.Invoke(StockUiMogHouse.LayoutWhileOpenNotice);
                return;
            }
            var layout = MogHouseLayout;
            layout.SecondFloor = house.OnSecondFloor;
            layout.Held = null;
            layout.Rotation = 0;
            layout.CursorX = layout.SecondFloor ? MogHouseFurniture.GridWidth / 2 : MogHouseFurniture.DoorBlockLeft - 1;
            layout.CursorZ = MogHouseFurniture.Rows(layout.SecondFloor) / 2;
            RefreshFurniture();
            layout.Active = true;
            CloseAll();
        }

        /// <summary>Leaves the layout: C2S 0x0FA with every field 0 tells the server the rearranging is done.</summary>
        private void EndLayout(IStockUiMogHouse house)
        {
            MogHouseLayout.Active = false;
            MogHouseLayout.Held = null;
            CloseAll();
            _ = SendMogHouseAsync(house.FinishLayoutAsync, "Layout");
        }

        /// <summary>
        /// The layout grid's own input while no menu is over it: the directions move the cursor (with the held piece),
        /// Confirm places the held piece or opens the cell's menu, the options key (+ / Y) turns the held piece a quarter,
        /// Cancel puts a moved piece back or leaves the layout. False when the grid does not have the input.
        /// </summary>
        private bool ProcessMogHouseLayout(InputState input)
        {
            var layout = MogHouseLayout;
            if (!layout.Active || _open.Length > 0) return false;
            var house = MogHouse;
            if (house == null)
            {
                layout.Active = false;
                return false;
            }
            if (input.WasActionTriggered(InputAction.MenuUp)) MoveLayoutCursor(0, 1);
            if (input.WasActionTriggered(InputAction.MenuDown)) MoveLayoutCursor(0, -1);
            if (input.WasActionTriggered(InputAction.MenuLeft)) MoveLayoutCursor(-1, 0);
            if (input.WasActionTriggered(InputAction.MenuRight)) MoveLayoutCursor(1, 0);
            if (input.WasActionTriggered(InputAction.CycleLogWindow)) TurnHeldPiece();
            if (input.WasActionTriggered(InputAction.Confirm)) ConfirmLayoutCell();
            else if (input.WasActionTriggered(InputAction.Cancel)) CancelLayout();
            return true;
        }

        /// <summary>The layout cursor one cell over (Up is away from the door), the held piece with it.</summary>
        public void MoveLayoutCursor(int dx, int dz)
        {
            if (!MogHouseLayout.Active) return;
            MogHouseLayout.MoveCursor(dx, dz);
            SoundCue?.Invoke(StockUiSoundCue.CursorMove);
            Changed?.Invoke();
        }

        /// <summary>The options key on the grid: the held piece turns a quarter clockwise.</summary>
        public void TurnHeldPiece()
        {
            var layout = MogHouseLayout;
            if (!layout.Active || layout.Held == null) return;
            layout.Rotation = (byte)((layout.Rotation + 1) & 3);
            SoundCue?.Invoke(StockUiSoundCue.CursorMove);
            Changed?.Invoke();
        }

        /// <summary>Confirm on the grid: places the held piece, else opens the cell's menu (<c>rmlo1</c>, <c>rmlo2</c> or <c>rmlo4</c>).</summary>
        public void ConfirmLayoutCell()
        {
            var layout = MogHouseLayout;
            if (!layout.Active || MogHouse is not { } house) return;
            SoundCue?.Invoke(StockUiSoundCue.Select);
            if (layout.Held is { } held) PlaceHeld(house, held);
            else lock (_sync) Push(StockUiMogHouse.LayoutMenuFor(layout.PieceAt(layout.CursorX, layout.CursorZ)), null, Array.Empty<string>(), null, null);
        }

        /// <summary>Cancel on the grid: a held piece goes back where it was (or back to the Mog Safe), else the layout ends.</summary>
        public void CancelLayout()
        {
            var layout = MogHouseLayout;
            if (!layout.Active || MogHouse is not { } house) return;
            SoundCue?.Invoke(StockUiSoundCue.Close);
            if (layout.Held != null)
            {
                layout.Held = null;
                Changed?.Invoke();
            }
            else EndLayout(house);
        }

        /// <summary>
        /// Confirm with a piece held: C2S 0x0FA puts it at the cursor cell with its rotation, on the floor (level 0).
        /// The server checks the footprint, overlaps, wall slots and stacking and answers with S2C 0x0FA and 0x020
        /// (nothing when it refuses); the client only refuses cells off the floor's usable area.
        /// </summary>
        private void PlaceHeld(IStockUiMogHouse house, FurniturePlacement held)
        {
            var layout = MogHouseLayout;
            int x = layout.CursorX, z = layout.CursorZ;
            if (!MogHouseFurniture.IsFloorCell(x, z, layout.SecondFloor))
            {
                NoticePosted?.Invoke(StockUiMogHouse.CellNotUsableNotice);
                return;
            }
            byte rotation = layout.Rotation;
            bool secondFloor = layout.SecondFloor;
            layout.Held = null;
            Changed?.Invoke();
            _ = SendMogHouseAsync(() => house.PlaceAsync(held, secondFloor, (byte)x, 0, (byte)z, rotation), "Place");
        }

        /// <summary>Place: the furnishings not placed yet (<c>furnitur</c>'s list, provisionally a query); the one picked follows the cursor.</summary>
        private void OpenFurnitureList()
        {
            var layout = MogHouseLayout;
            RefreshFurniture();
            var pieces = layout.Unplaced(IsFlowerpot);
            if (pieces.Count == 0)
            {
                NoticePosted?.Invoke(StockUiMogHouse.NoFurnitureNotice);
                return;
            }
            var options = new List<StockUiQueryOption>(pieces.Count);
            foreach (var piece in pieces) options.Add(new StockUiQueryOption(options.Count + 1, ItemName(piece.ItemId)));
            OpenQuery(new[] { StockUiMogHouse.FurnitureQuestion }, options, 0, answer =>
            {
                if (answer < 1 || answer > pieces.Count) return;
                layout.Held = pieces[answer - 1];
                layout.Rotation = 0;
                CloseAll();
            }, 8);
        }

        /// <summary>Open Mog House: asks, then C2S 0x0CB Open (or Close when it is open).</summary>
        private void ToggleHouseOpen(IStockUiMogHouse house)
        {
            bool open = !MogHouseOpenToVisitors;
            OpenYesNo(open ? StockUiMogHouse.OpenHouseQuestion : StockUiMogHouse.CloseHouseQuestion, defaultYes: true, answer =>
            {
                if (answer != true) return;
                MogHouseOpenToVisitors = open;
                NoticePosted?.Invoke(open ? StockUiMogHouse.HouseOpenedNotice : StockUiMogHouse.HouseClosedNotice);
                CloseAll();
                _ = SendMogHouseAsync(() => house.SetHouseOpenAsync(open), "Open Mog House");
            });
        }

        /// <summary>
        /// Remodel: the house styles (<c>roomlist</c>, the query's own frame). LandSandBoat changes the second floor's
        /// look and, on the second floor, rezones the character into it; without the Mog Patio's key item it uses the
        /// nation's style.
        /// </summary>
        private void OpenStyleList(IStockUiMogHouse house)
        {
            var styles = StockUiMogHouse.HouseStyles;
            var options = new List<StockUiQueryOption>(styles.Count);
            foreach (var (_, name) in styles) options.Add(new StockUiQueryOption(options.Count + 1, name));
            OpenQuery(new[] { StockUiMogHouse.StyleQuestion }, options, 0, answer =>
            {
                if (answer < 1 || answer > styles.Count) return;
                ushort style = styles[answer - 1].Style;
                CloseAll();
                _ = SendMogHouseAsync(() => house.RemodelAsync(style), "Remodel");
            }, styles.Count);
        }

        /// <summary>
        /// The mannequin's equipment (<c>rmlo4</c> Equipment): its eight slots with what each wears (from the Storage), then
        /// Remove all. A slot lists the Storage's equipment for it (Remove first when it wears something); the pick is
        /// C2S 0x03B. The retail window is not captured yet: the lists are queries, <b>provisional</b>.
        /// </summary>
        private void OpenMannequin(IStockUiMogHouse house, FurniturePlacement mannequin)
        {
            var gear = MannequinGearOf(mannequin);
            var slots = StockUiMogHouse.MannequinSlots;
            var options = new List<StockUiQueryOption>(slots.Count + 1);
            for (int i = 0; i < slots.Count; i++)
            {
                string worn = gear[i] != 0 && StorageItem(gear[i]) is { } item ? ItemName(item.ItemId) : StockUiMogHouse.EmptySlotText;
                options.Add(new StockUiQueryOption(i + 1, $"{slots[i].Name}: {worn}"));
            }
            options.Add(new StockUiQueryOption(slots.Count + 1, StockUiMogHouse.RemoveAllRow));
            OpenQuery(new[] { StockUiMogHouse.MannequinQuestion }, options, 0, answer =>
            {
                if (answer == slots.Count + 1)
                {
                    CloseAll();
                    _ = SendMogHouseAsync(() => house.MannequinAsync(SubcontainerKind.UnequipAll, mannequin, SubcontainerSlotIndex.MainWeapon, 0), "Mannequin");
                }
                else if (answer >= 1 && answer <= slots.Count) OpenMannequinSlot(house, mannequin, answer - 1, gear[answer - 1]);
            }, 8);
        }

        private void OpenMannequinSlot(IStockUiMogHouse house, FurniturePlacement mannequin, int index, byte worn)
        {
            var (slot, name, bit) = StockUiMogHouse.MannequinSlots[index];
            var choices = new List<byte>();
            var options = new List<StockUiQueryOption>();
            if (worn != 0)
            {
                choices.Add(0);
                options.Add(new StockUiQueryOption(1, StockUiMogHouse.RemoveRow));
            }
            if (Inventory is { } inventory)
            {
                foreach (var item in inventory.SnapshotItems(ContainerId.Storage))
                {
                    if (item.ItemId == 0 || ItemLookup?.Invoke(item.ItemId) is not { } record || !record.CanEquipSlot(bit)) continue;
                    choices.Add(item.Slot);
                    // An item another mannequin wears is locked (S2C 0x01F/0x020 lock flag Mannequin).
                    options.Add(new StockUiQueryOption(options.Count + 1, ItemName(item.ItemId), Greyed: item.LockFlag != ItemLockFlag.Normal && item.Slot != worn));
                }
            }
            if (options.Count == 0)
            {
                NoticePosted?.Invoke($"You have no equipment for the {name.ToLowerInvariant()} slot in your Storage.");
                return;
            }
            OpenQuery(new[] { $"{StockUiMogHouse.MannequinQuestion} ({name})" }, options, 0, answer =>
            {
                if (answer < 1 || answer > choices.Count) return;
                byte storageSlot = choices[answer - 1];
                CloseAll();
                _ = storageSlot == 0
                    ? SendMogHouseAsync(() => house.MannequinAsync(SubcontainerKind.Unequip, mannequin, slot, worn), "Mannequin")
                    : SendMogHouseAsync(() => house.MannequinAsync(SubcontainerKind.Equip, mannequin, slot, storageSlot), "Mannequin");
            }, 8);
        }

        private MannequinGear MannequinGearOf(FurniturePlacement mannequin) =>
            Inventory is { } inventory && inventory.GetContainer(mannequin.Container).TryGetItem(mannequin.Slot, out var item) ? MannequinGear.Read(item) : default;

        private InventoryItem? StorageItem(byte slot) =>
            Inventory is { } inventory && inventory.GetContainer(ContainerId.Storage).TryGetItem(slot, out var item) ? item : null;

        private async Task SendMogHouseAsync(Func<Task> send, string what)
        {
            try
            {
                await send().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Gordian.Core.Diagnostics.GordianLog.Warning("MOGHOUSE", $"{what} failed: {ex.Message}");
                NoticePosted?.Invoke($"{what} failed: {ex.Message}");
            }
        }
    }
}
