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
        /// <summary>The mannequin the Mannequin window (<c>inspect</c>, image-49) dresses; null for other windows.</summary>
        public FurniturePlacement? Mannequin { get; init; }

        public bool IsMannequinWindow => Mannequin != null;

        /// <summary>The item a mannequin wears in the slot a grid button shows (0 when empty).</summary>
        public ushort MannequinItemIn(int buttonId) =>
            Mannequin is { } mannequin && ItemOwner != null && StockUiCheck.TryGetSlot(buttonId, out var slot) ? ItemOwner.MannequinItem(mannequin, slot) : (ushort)0;

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
            else if (menu.Name.Equals(StockUiMogHouse.SafeMenu, StringComparison.OrdinalIgnoreCase))
            {
                menu.SetGreyed(Safe2Locked ? new HashSet<int> { StockUiMogHouse.SafeMenuSafe2Button } : null);
            }
        }

        /// <summary>Whether Mog Safe 2 is open to the character; set by the session.</summary>
        public Func<bool>? MogSafe2Unlocked { get; set; }

        private bool Safe2Locked => MogSafe2Unlocked?.Invoke() == false;

        /// <summary>The mannequin the open Mannequin window dresses, or null: the camera turns to it and it spins.</summary>
        public FurniturePlacement? OpenMannequin
        {
            get
            {
                foreach (var menu in _open)
                {
                    if (menu.Mannequin is { } mannequin) return mannequin;
                }
                return null;
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
                    if (!ClearAway(house, cleared)) NoticePosted?.Invoke(StockUiMogHouse.StorageFullNotice);
                    break;
                case StockUiMogHouseAction.ClearAllConfirm:
                    CloseAll();
                    bool refused = false;
                    foreach (var shown in layout.Pieces.Where(layout.IsShown).ToList()) refused |= !ClearAway(house, shown);
                    if (refused) NoticePosted?.Invoke(StockUiMogHouse.StorageFullNotice);
                    break;
                case StockUiMogHouseAction.Return:
                    CloseMenu(from);
                    break;
                case StockUiMogHouseAction.Mannequin when piece is { } mannequin && MogHouseFurniture.IsMannequin(mannequin.ItemId):
                    OpenMannequinWindow(mannequin);
                    break;
            }
        }

        /// <summary>
        /// C2S 0x0FB, and the piece leaves the room at once; the cursor goes to another piece. False, with nothing sent, when the
        /// Storage could not shrink (LandSandBoat's 0x0FB refuses that without a word, so the client checks first).
        /// </summary>
        private bool ClearAway(IStockUiMogHouse house, FurniturePlacement piece)
        {
            var layout = MogHouseLayout;
            int used = Inventory?.SnapshotItems(ContainerId.Storage).Count(i => i.ItemId != 0) ?? 0;
            if (!layout.StorageAllowsRemoving(piece, used, id => ItemLookup?.Invoke(id)?.FurnitureStorage ?? 0)) return false;
            layout.UpdatePiece(piece with { Installed = false });
            if (layout.Selected == null) layout.SelectFirst();
            Changed?.Invoke();
            _ = SendMogHouseAsync(() => house.ClearAwayAsync(piece), "Remove");
            return true;
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
            var picker = new StockUiItemPicker(_ => SafeRows(safe), _ => 1, (row, _) => HoldFromSafe(row), title, Count: safe);
            OpenItemPicker(from, SafeListX, picker);
        }

        /// <summary>
        /// A Mog Safe's furnishings for Layout: placed ones in green, as equipped items are (round 2), and picking one takes it
        /// up as Rearrange does; a piece on the other floor is greyed, and flowerpots on a second floor (LandSandBoat refuses them).
        /// </summary>
        private List<StockUiItemRow> SafeRows(ContainerId safe)
        {
            var layout = MogHouseLayout;
            var rows = new List<StockUiItemRow>();
            foreach (var row in OrderedRows(safe))
            {
                var piece = layout.Pieces.FirstOrDefault(p => p.Container == safe && p.Slot == row.Slot);
                if (piece.ItemId == 0) continue;
                bool otherFloor = piece.Installed && piece.SecondFloor != layout.SecondFloor;
                bool pot = !piece.Installed && layout.SecondFloor && (layout.InfoFor(piece.ItemId).Flags & Gordian.Core.Resources.Tables.FurnitureFlags.Flowerpot) != 0;
                rows.Add(row with { Equipped = piece.Installed, Greyed = otherFloor || pot, GreyedReason = otherFloor ? StockUiMogHouse.OtherFloorNotice : pot ? StockUiMogHouse.CellNotUsableNotice : null });
            }
            return rows;
        }

        private void HoldFromSafe(StockUiItemRow row)
        {
            var layout = MogHouseLayout;
            var piece = layout.Pieces.FirstOrDefault(p => p.Container == row.Container && p.Slot == row.Slot);
            CloseAll();
            if (piece.ItemId == 0) return;
            if (piece.Installed)
            {
                layout.SelectedKey = (piece.Container, piece.Slot);
                layout.Hold(piece, piece.X, piece.Z, piece.Rotation);
                Changed?.Invoke();
                return;
            }
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

        /// <summary>
        /// The Mannequin window (<c>rmlo4</c> Equip; the maintainer's image-49): the DAT's <c>inspect</c> frame at the top left, its
        /// grid's eight mannequin slots lit and the rest dim, Unequip All under it, the Storage's equipment beside it, the item
        /// under the cursor below; the view turns to the mannequin, which spins. Confirm on a slot lists what fits it.
        /// </summary>
        private void OpenMannequinWindow(FurniturePlacement mannequin)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiMogHouse.MannequinMenu, out var definition)) return;
            CloseAll();
            var window = new StockUiOpenMenu(definition, null, Array.Empty<string>(), null)
            {
                Mannequin = mannequin,
                Inventory = Inventory,
                ItemLookup = ItemLookup,
                ItemOwner = this,
            };
            var greyed = new HashSet<int>();
            for (int button = 1; button < StockUiMogHouse.UnequipAllButton; button++)
            {
                if (StockUiCheck.TryGetSlot(button, out var slot) && StockUiMogHouse.MannequinIndex(slot) < 0) greyed.Add(button);
            }
            window.SetGreyed(greyed);
            window.LabelImages = new Dictionary<int, UiShapeReference>
            {
                [StockUiMogHouse.UnequipAllButton] = new(0, StockUiMogHouse.UnequipAllImage, StockUiParty.LabelGroup),
            };
            window.SelectedButtonId = 1;
            lock (_sync)
            {
                window.CompanionRows = MannequinRows(mannequin, null);
                PushOpen(window);
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// The Storage's equipment for the window's list, for one slot or (null) all of it. What this mannequin wears there is green
        /// and Confirm takes it off; what another mannequin wears is locked (the item's lock flag) and greyed.
        /// </summary>
        private List<StockUiItemRow> MannequinRows(FurniturePlacement mannequin, EquipSlotId? slot)
        {
            var rows = new List<StockUiItemRow>();
            if (Inventory is not { } inventory) return rows;
            var gear = MannequinGearOf(mannequin);
            int index = slot is { } s ? StockUiMogHouse.MannequinIndex(s) : -1;
            byte worn = index >= 0 ? gear[index] : (byte)0;
            var items = inventory.SnapshotItems(ContainerId.Storage);
            Array.Sort(items, (a, b) => a.Slot.CompareTo(b.Slot));
            foreach (var item in items)
            {
                if (item.ItemId == 0 || ItemLookup?.Invoke(item.ItemId) is not { } record || !StockUiItems.IsEquipment(record) || record.EquipSlotsMask == 0) continue;
                if (slot is { } fit ? !StockUiItems.FitsSlot(record, fit) : !StockUiMogHouse.MannequinSlots.Any(m => StockUiItems.FitsSlot(record, (EquipSlotId)m.EquipBit))) continue;
                bool here = worn != 0 && item.Slot == worn;
                bool locked = item.LockFlag != ItemLockFlag.Normal && !here;
                rows.Add(new StockUiItemRow(item.ItemId, record.Name, item.Count, ContainerId.Storage, item.Slot, Math.Max(1, (int)record.StackSize),
                    Equipped: here || item.LockFlag == ItemLockFlag.Mannequin, Greyed: slot != null && locked, GreyedReason: locked ? StockUiMogHouse.LockedGearNotice : null));
            }
            return rows;
        }

        /// <summary>Confirm in the Mannequin window: Unequip All, or a lit slot's list beside the grid.</summary>
        private void ActivateMannequinWindow(StockUiOpenMenu window, UiMenuButton button)
        {
            if (window.Mannequin is not { } mannequin || MogHouse is not { } house) return;
            if (button.ButtonId == StockUiMogHouse.UnequipAllButton)
            {
                _ = SendMogHouseAsync(() => house.MannequinAsync(SubcontainerKind.UnequipAll, mannequin, SubcontainerSlotIndex.MainWeapon, 0), "Mannequin");
                return;
            }
            if (!StockUiCheck.TryGetSlot(button.ButtonId, out var slot) || window.IsGreyed(button.ButtonId)) return;
            int index = StockUiMogHouse.MannequinIndex(slot);
            if (index < 0) return;
            var subSlot = StockUiMogHouse.MannequinSlots[index].Slot;
            var picker = new StockUiItemPicker(_ => MannequinRows(mannequin, slot), _ => 1, (row, count) =>
            {
                byte worn = MannequinGearOf(mannequin)[index];
                var close = Top;
                if (close != null && close.Picker != null) CloseMenu(close);
                _ = SendMogHouseAsync(() => house.MannequinAsync(row.Slot == worn ? SubcontainerKind.Unequip : SubcontainerKind.Equip, mannequin, subSlot, row.Slot), "Mannequin");
            }, StockUiMogHouse.LayoutTitle, HelpTextId: StockUiMogHouse.MannequinHelp);
            OpenItemPicker(window, StockUiMogHouse.MannequinListX, picker);
        }

        /// <summary>The cursor in the Mannequin window passes over the dim slots to the next lit one. Call under the lock.</summary>
        private bool TryMoveMannequin(InputAction direction)
        {
            if (_open.Length == 0 || _open[^1] is not { IsMannequinWindow: true } window || window.SelectedButton is not { } from) return false;
            var at = from;
            for (int step = 0; step < 4; step++)
            {
                int next = direction switch
                {
                    InputAction.MenuUp => at.NavUp, InputAction.MenuDown => at.NavDown, InputAction.MenuLeft => at.NavLeft, _ => at.NavRight,
                };
                if (next <= 0 || next == at.ButtonId || window.Menu.FindButton(next) is not { } button) break;
                at = button;
                if (window.IsGreyed(next)) continue;
                window.SelectedButtonId = next;
                SoundCue?.Invoke(StockUiSoundCue.CursorMove);
                break;
            }
            return true;
        }

        /// <summary>What a mannequin wears in an equipment slot, by item id.</summary>
        internal ushort MannequinItem(FurniturePlacement mannequin, EquipSlotId slot)
        {
            int index = StockUiMogHouse.MannequinIndex(slot);
            byte storageSlot = index >= 0 ? MannequinGearOf(mannequin)[index] : (byte)0;
            return storageSlot != 0 && StorageItem(storageSlot) is { } item ? item.ItemId : (ushort)0;
        }

        /// <summary>Greys the Mog Safes' placed furniture in a View House list: it cannot leave the safe while it stands in the room (round 2).</summary>
        private void GreyPlacedFurniture(List<StockUiItemRow> rows)
        {
            if (Inventory is not { } inventory) return;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.Container is not (ContainerId.MogSafe or ContainerId.MogSafe2) || !MogHouseFurniture.IsFurniture(row.ItemId, ItemLookup)) continue;
                if (inventory.GetContainer(row.Container).TryGetItem(row.Slot, out var item) && FurniturePlacement.Read(item).Installed)
                {
                    rows[i] = row with { Greyed = true, GreyedReason = StockUiMogHouse.PlacedNotice };
                }
            }
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
