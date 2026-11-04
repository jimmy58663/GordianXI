// src/Gordian.Engine/Ui/StockUiMenuController.MogHouse.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Ui;
using Gordian.Core.World;
using Gordian.Engine.Input;

namespace Gordian.Engine.Ui
{
    public sealed partial class StockUiOpenMenu
    {
        /// <summary>Help lines that replace a button's own (the main menu's Mog House); null for none.</summary>
        public IReadOnlyDictionary<int, int>? HelpOverrides { get; internal set; }

        /// <summary>The help line of a button.</summary>
        public int HelpTextIdOf(UiMenuButton button) =>
            HelpOverrides != null && HelpOverrides.TryGetValue(button.ButtonId, out int help) ? help : button.HelpTextId;
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>The Mog House's packets (#93); null in a session without them.</summary>
        public IStockUiMogHouse? MogHouse { get; set; }

        /// <summary>The Layout's state (#93), read by the App's camera, furniture and overlay.</summary>
        public MogHouseLayoutState MogHouseLayout { get; } = new();

        /// <summary>The Layout's camera; the App advances it every frame.</summary>
        public MogHouseLayoutCamera MogHouseCamera { get; } = new();

        /// <summary>
        /// Whether the Mog House is open to party and alliance members. The server does not say (LandSandBoat keeps it and
        /// answers nothing), so the client remembers what it sent; a zone change closes it (<see cref="OnMogHouseZoneChanged"/>).
        /// </summary>
        public bool MogHouseOpenToVisitors { get; private set; }

        /// <summary>The Mog Safe's list's place (video at 0:31: the top left, as the Items window).</summary>
        private const int SafeListX = 16;

        // Held directions: the piece keeps moving while one stays down, as the menus' cursor does.
        private readonly TimeSpan?[] _layoutRepeat = new TimeSpan?[4];

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
            RefreshFurniture();
            Changed?.Invoke();
        }

        /// <summary>The Mog Safes changed (S2C 0x020, 0x0FA): the furniture is read again.</summary>
        public void OnMogHouseFurnitureChanged()
        {
            RefreshFurniture();
            var layout = MogHouseLayout;
            if (!layout.Active) return;
            if (layout.Selected == null) layout.SelectFirst();
            Changed?.Invoke();
        }

        private void RefreshFurniture()
        {
            if (MogHouse is { } house) MogHouseLayout.SecondFloor = house.OnSecondFloor;
            if (Inventory is { } inventory) MogHouseLayout.SetPieces(MogHouseFurniture.Snapshot(inventory, ItemLookup));
        }

        /// <summary>
        /// What the Mog House changes in a menu: in it the main menu's Map reads Mog House; while the house is open to visitors
        /// Open Mog reads Close Mog and Layout and Remodel are greyed. Call under the lock.
        /// </summary>
        private void RefreshMogHouseMenu(StockUiOpenMenu menu)
        {
            if (menu.Name.Equals(StockUiMenuEntries.MainMenu, StringComparison.OrdinalIgnoreCase) && InMogHouse?.Invoke() == true)
            {
                menu.LabelImages = new Dictionary<int, UiShapeReference>
                {
                    [StockUiMogHouse.MainMenuButton] = new(0, StockUiMogHouse.MogHouseLabelImage, StockUiParty.LabelGroup),
                };
                menu.HelpOverrides = new Dictionary<int, int> { [StockUiMogHouse.MainMenuButton] = StockUiMogHouse.MogHouseHelp };
            }
            else if (menu.Name.Equals(StockUiMogHouse.MoogleMenu, StringComparison.OrdinalIgnoreCase))
            {
                bool open = MogHouseOpenToVisitors;
                menu.LabelImages = open
                    ? new Dictionary<int, UiShapeReference> { [StockUiMogHouse.OpenHouseButton] = new(0, StockUiMogHouse.CloseMogLabelImage, StockUiParty.LabelGroup) }
                    : StockUiOpenMenu.NoLabelImages;
                menu.SetGreyed(open ? new HashSet<int> { StockUiMogHouse.LayoutButton, StockUiMogHouse.RemodelButton } : null);
            }
        }

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
            var piece = layout.Selected;
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
                    lock (_sync) Push(StockUiMogHouse.SafeMenu, from, Array.Empty<string>(), null, null);
                    break;
                case StockUiMogHouseAction.PlaceFromSafe:
                    OpenSafeList(from, ContainerId.MogSafe, StockUiMogHouse.SafeTitle);
                    break;
                case StockUiMogHouseAction.PlaceFromSafe2:
                    OpenSafeList(from, ContainerId.MogSafe2, StockUiMogHouse.Safe2Title);
                    break;
                case StockUiMogHouseAction.ClearAll:
                    lock (_sync) Push(StockUiMogHouse.ClearAllMenu, from, Array.Empty<string>(), null, null);
                    break;
                case StockUiMogHouseAction.QuitLayout:
                    EndLayout(house);
                    break;
                case StockUiMogHouseAction.Move when piece is { } moving:
                    CloseAll();
                    layout.Hold(moving, moving.X, moving.Z, moving.Rotation);
                    Changed?.Invoke();
                    break;
                case StockUiMogHouseAction.ClearAway when piece is { } cleared:
                    CloseAll();
                    ClearAway(house, cleared);
                    break;
                case StockUiMogHouseAction.ClearAllConfirm:
                    CloseAll();
                    foreach (var shown in layout.Pieces.Where(layout.IsShown).ToList()) ClearAway(house, shown);
                    break;
                case StockUiMogHouseAction.Return:
                    CloseMenu(from);
                    break;
                case StockUiMogHouseAction.Mannequin when piece is { } mannequin && MogHouseFurniture.IsMannequin(mannequin.ItemId):
                    OpenMannequin(house, mannequin);
                    break;
            }
        }

        /// <summary>C2S 0x0FB, and the piece leaves the room at once; the cursor goes to another piece.</summary>
        private void ClearAway(IStockUiMogHouse house, FurniturePlacement piece)
        {
            var layout = MogHouseLayout;
            layout.UpdatePiece(piece with { Installed = false });
            if (layout.Selected == null) layout.SelectFirst();
            Changed?.Invoke();
            _ = SendMogHouseAsync(() => house.ClearAwayAsync(piece), "Remove");
        }

        /// <summary>Layout: the menus close, the camera goes up and the cursor goes on the first placed piece.</summary>
        private void BeginLayout(IStockUiMogHouse house)
        {
            var layout = MogHouseLayout;
            layout.Held = null;
            RefreshFurniture();
            layout.SelectFirst();
            layout.Active = true;
            Array.Clear(_layoutRepeat);
            CloseAll();
        }

        /// <summary>
        /// Leaves the layout: C2S 0x0FA with every field 0 tells the server the rearranging is done, and the moogle's menu comes
        /// back with the cursor on Layout, as in the video at 1:17.
        /// </summary>
        private void EndLayout(IStockUiMogHouse house)
        {
            MogHouseLayout.Active = false;
            MogHouseLayout.Held = null;
            _ = SendMogHouseAsync(house.FinishLayoutAsync, "Layout");
            OpenMoogleMenu();
            if (Top is { } menu && menu.Name.Equals(StockUiMogHouse.MoogleMenu, StringComparison.OrdinalIgnoreCase)) menu.SelectedButtonId = StockUiMogHouse.LayoutButton;
            Changed?.Invoke();
        }

        /// <summary>
        /// The Layout's own input while no menu is over it: the directions jump between the pieces, or move the piece in hand;
        /// the menu button turns it (the maintainer, round 1), Confirm opens the piece's menu or places the piece in hand, Cancel
        /// puts it back or leaves. False when the Layout does not have the input.
        /// </summary>
        private bool ProcessMogHouseLayout(InputState input, TimeSpan elapsed)
        {
            var layout = MogHouseLayout;
            if (!layout.Active || _open.Length > 0) return false;
            if (MogHouse == null)
            {
                layout.Active = false;
                return false;
            }
            ReadOnlySpan<(InputAction Action, int X, int Z)> directions = stackalloc (InputAction, int, int)[]
            {
                (InputAction.MenuUp, 0, 1), (InputAction.MenuDown, 0, -1), (InputAction.MenuLeft, -1, 0), (InputAction.MenuRight, 1, 0),
            };
            for (int i = 0; i < directions.Length; i++)
            {
                var (action, x, z) = directions[i];
                if (!input.IsActionHeld(action))
                {
                    _layoutRepeat[i] = null;
                    continue;
                }
                if (input.WasActionTriggered(action) || _layoutRepeat[i] == null)
                {
                    MoveLayoutCursor(x, z);
                    _layoutRepeat[i] = RepeatDelay;
                    continue;
                }
                var remaining = _layoutRepeat[i]!.Value - elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    MoveLayoutCursor(x, z);
                    remaining = RepeatInterval;
                }
                _layoutRepeat[i] = remaining;
            }
            if (input.WasActionTriggered(InputAction.OpenMenu)) TurnHeldPiece();
            if (input.WasActionTriggered(InputAction.Confirm)) ConfirmLayoutCell();
            else if (input.WasActionTriggered(InputAction.Cancel)) CancelLayout();
            return true;
        }

        /// <summary>A direction on the Layout: the piece in hand a cell over (x east, z north), else the cursor to the next piece that way.</summary>
        public void MoveLayoutCursor(int dx, int dz)
        {
            var layout = MogHouseLayout;
            if (!layout.Active) return;
            if (layout.Held != null) layout.MoveHeld(dx, dz);
            else if (!layout.MoveSelection(dx, dz)) return;
            SoundCue?.Invoke(StockUiSoundCue.CursorMove);
            Changed?.Invoke();
        }

        /// <summary>The menu button on the Layout: the piece in hand turns a quarter.</summary>
        public void TurnHeldPiece()
        {
            var layout = MogHouseLayout;
            if (!layout.Active || layout.Held == null) return;
            layout.TurnHeld();
            SoundCue?.Invoke(StockUiSoundCue.CursorMove);
            Changed?.Invoke();
        }

        /// <summary>Confirm on the Layout: places the piece in hand, else opens the menu of the piece under the cursor (<c>rmlo2</c>, <c>rmlo4</c>), or <c>rmlo1</c> with nothing placed.</summary>
        public void ConfirmLayoutCell()
        {
            var layout = MogHouseLayout;
            if (!layout.Active || MogHouse is not { } house) return;
            if (layout.Held is { } held)
            {
                PlaceHeld(house, held);
                return;
            }
            SoundCue?.Invoke(StockUiSoundCue.Select);
            lock (_sync) Push(StockUiMogHouse.LayoutMenuFor(layout.Selected), null, Array.Empty<string>(), null, null);
        }

        /// <summary>Cancel on the Layout: the piece in hand goes back (nothing was sent), else the Layout ends.</summary>
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
        /// Confirm with a piece in hand: C2S 0x0FA puts it down, and what stands on a table goes first with the same offset
        /// (LandSandBoat: the client sends the child before its parent). The server answers with S2C 0x0FA and 0x020.
        /// </summary>
        private void PlaceHeld(IStockUiMogHouse house, FurniturePlacement held)
        {
            var layout = MogHouseLayout;
            if (!layout.CanPlaceHeld())
            {
                SoundCue?.Invoke(StockUiSoundCue.Unavailable);
                NoticePosted?.Invoke(StockUiMogHouse.CellNotUsableNotice);
                return;
            }
            SoundCue?.Invoke(StockUiSoundCue.Select);
            int x = layout.HeldX, z = layout.HeldZ, level = layout.HeldLevel();
            byte rotation = layout.Rotation;
            bool secondFloor = layout.SecondFloor, wall = layout.HeldInfo is { IsWall: true };
            if (wall) z = level = 0;
            var children = held.Installed ? layout.StackedOn(held) : new List<FurniturePlacement>();
            foreach (var child in children)
            {
                var moved = child with { X = (byte)(child.X + x - held.X), Z = (byte)(child.Z + z - held.Z) };
                layout.UpdatePiece(moved);
                _ = SendMogHouseAsync(() => house.PlaceAsync(child, secondFloor, moved.X, moved.Level, moved.Z, moved.Rotation), "Place");
            }
            var placed = held with { Installed = true, SecondFloor = secondFloor, X = (byte)x, Z = (byte)z, Level = (byte)level, Rotation = wall ? (byte)0 : rotation };
            layout.UpdatePiece(placed);
            layout.Held = null;
            layout.SelectedKey = (placed.Container, placed.Slot);
            Changed?.Invoke();
            _ = SendMogHouseAsync(() => house.PlaceAsync(held, secondFloor, placed.X, placed.Level, placed.Z, placed.Rotation), "Place");
        }

        /// <summary>
        /// Layout on a menu, then a Mog Safe: the item list ("Mog Safe 21/80") of the safe's furnishings not placed yet (video at
        /// 0:31). The pick goes in hand, next to the piece under the cursor.
        /// </summary>
        private void OpenSafeList(StockUiOpenMenu from, ContainerId safe, string title)
        {
            var layout = MogHouseLayout;
            RefreshFurniture();
            var picker = new StockUiItemPicker(
                _ =>
                {
                    var slots = new HashSet<byte>(layout.Unplaced(safe).Select(p => p.Slot));
                    return OrderedRows(safe).Where(r => slots.Contains(r.Slot)).ToList();
                },
                _ => 1,
                (row, _) => HoldFromSafe(row),
                title,
                Count: safe);
            OpenItemPicker(from, SafeListX, picker);
        }

        private void HoldFromSafe(StockUiItemRow row)
        {
            var layout = MogHouseLayout;
            var piece = layout.Pieces.FirstOrDefault(p => p.Container == row.Container && p.Slot == row.Slot);
            CloseAll();
            if (piece.ItemId == 0) return;
            if (layout.InfoFor(piece.ItemId).IsWall)
            {
                int slot = 0;
                while (layout.Pieces.Any(p => layout.IsShown(p) && layout.InfoFor(p.ItemId).IsWall && p.X == slot)) slot++;
                layout.Hold(piece, slot, 0, 0);
            }
            else if (layout.Selected is { } near && !layout.InfoFor(near.ItemId).IsWall) layout.Hold(piece, near.X, near.Z, 0);
            else layout.Hold(piece, 2, MogHouseFurniture.Rows(layout.SecondFloor) / 2, 0);
            Changed?.Invoke();
        }

        /// <summary>Open Mog / Close Mog: asks, then C2S 0x0CB Open or Close.</summary>
        private void ToggleHouseOpen(IStockUiMogHouse house)
        {
            bool open = !MogHouseOpenToVisitors;
            OpenYesNo(open ? StockUiMogHouse.OpenHouseQuestion : StockUiMogHouse.CloseHouseQuestion, defaultYes: true, answer =>
            {
                if (answer != true) return;
                MogHouseOpenToVisitors = open;
                NoticePosted?.Invoke(open ? StockUiMogHouse.HouseOpenedNotice : StockUiMogHouse.HouseClosedNotice);
                lock (_sync)
                {
                    foreach (var menu in _open) RefreshMogHouseMenu(menu);
                }
                Changed?.Invoke();
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

        private string ItemName(ushort itemId) => ItemLookup?.Invoke(itemId)?.Name is { Length: > 0 } name ? name : $"Item {itemId}";

        /// <summary>
        /// The mannequin's equipment (<c>rmlo4</c> Equip): its eight slots with what each wears (from the Storage), then
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
