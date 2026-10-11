// src/Gordian.Core/Ui/StockUiMenuController.Items.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Ui;
using Gordian.Core.World;

namespace Gordian.Core.Ui
{
    /// <summary>Whom an item is used on: the entity's server id, target index and name.</summary>
    public readonly record struct StockUiItemTarget(uint ServerId, ushort TargetIndex, string Name);

    public sealed partial class StockUiOpenMenu
    {
        /// <summary>Which window an item list (<see cref="StockUiItems.ListMenu"/>) belongs to; null for other menus.</summary>
        public StockUiItemListKind? ItemListKind { get; init; }

        public bool IsItemList => ItemListKind != null;

        /// <summary>The container the Items window lists (the Switch storage list changes it).</summary>
        public ContainerId ItemContainer { get; internal set; }

        /// <summary>The equipment slot the Equipment window's list is for.</summary>
        public EquipSlotId ItemEquipSlot { get; init; }

        /// <summary>An item list's rows, parallel to <see cref="Rows"/>.</summary>
        public IReadOnlyList<StockUiItemRow> ItemRows { get; internal set; } = Array.Empty<StockUiItemRow>();

        /// <summary>The item list row under the cursor, if any.</summary>
        public StockUiItemRow? SelectedItemRow
        {
            get
            {
                int index = EntryIndex(SelectedButtonId);
                var rows = ItemRows;
                return index >= 0 && index < rows.Count ? rows[index] : null;
            }
        }

        /// <summary>The used and total slots of the listed container (the list's header).</summary>
        public (int Used, int Total) ItemFill { get; internal set; }

        /// <summary>The target an Items window opened from the target command menu uses its items on; null from the main menu.</summary>
        public StockUiTargetContext? ItemTarget { get; init; }

        /// <summary>The row the Use / Drop window (<see cref="StockUiItems.ActionMenu"/>) acts on; null for other menus.</summary>
        public StockUiItemRow? ItemAction { get; init; }

        /// <summary>The row a Drop quantity prompt is for; null for other menus.</summary>
        public StockUiItemRow? ItemDropRow { get; init; }

        /// <summary>The Items window's options (<see cref="StockUiItems.OptionsMenu"/>: Auto / Manual / Switch).</summary>
        public bool IsItemOptions { get; init; }

        /// <summary>The storage list the options' Switch opens (<see cref="StockUiItems.StorageMenu"/>).</summary>
        public bool IsItemStorageMenu { get; init; }

        /// <summary>The Equipment window's slot grid (<see cref="StockUiItems.EquipmentMenu"/>).</summary>
        public bool IsEquipment { get; init; }

        /// <summary>The inventory the Equipment grid shows the worn items of.</summary>
        public InventoryState? Inventory { get; init; }

        /// <summary>The stats side the Equipment window draws (<see cref="StockUiItems.StatsMenu"/>).</summary>
        public Func<StockUiCharacterStats?>? CharacterStats { get; init; }

        internal Func<bool>? ListOpenCheck { get; init; }

        /// <summary>Whether the Equipment grid has its item list open over it (the list then shows the description).</summary>
        public bool IsListOpen => ListOpenCheck?.Invoke() == true;

        /// <summary>An Items or Equipment window: drawn at its own place rather than following the root.</summary>
        public bool IsItemWindow => IsItemList || ItemAction != null || ItemDropRow != null || IsItemOptions || IsItemStorageMenu || IsEquipment;

        /// <summary>The item worn in the slot a grid button shows (0 when empty or not the Equipment grid).</summary>
        public ushort EquippedItemIn(int buttonId) =>
            IsEquipment && StockUiCheck.TryGetSlot(buttonId, out var slot) ? StockUiItems.EquippedItem(Inventory, slot)?.ItemId ?? 0 : (ushort)0;

        /// <summary>The item whose description the window shows: the list's row under the cursor, or the grid's worn item.</summary>
        public ushort DescribedItem => IsItemList
            ? SelectedItemRow is { IsRemove: false } row ? row.ItemId : (ushort)0
            : EquippedItemIn(SelectedButtonId);

        /// <summary>The description pages of <see cref="DescribedItem"/> (empty without an item or its record).</summary>
        public IReadOnlyList<StockUiItemDescriptionPage> DescriptionPages =>
            DescribedItem is var id && id != 0 && ItemLookup?.Invoke(id) is { } record
                ? StockUiItemDescription.Pages(record)
                : Array.Empty<StockUiItemDescriptionPage>();

        private ushort _pageItem;
        private int _page;

        /// <summary>The description page shown (0 = the first); it goes back to the first whenever the described item changes.</summary>
        public int DescriptionPage => _pageItem == DescribedItem ? _page : 0;

        /// <summary>Turns the description to its next page, wrapping; false when it has one page.</summary>
        internal bool NextDescriptionPage()
        {
            int count = DescriptionPages.Count;
            if (count <= 1) return false;
            _page = (DescriptionPage + 1) % count;
            _pageItem = DescribedItem;
            return true;
        }
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>Sends C2S 0x037 (use an item: target id and index, container, slot).</summary>
        public Func<uint, ushort, ContainerId, byte, Task>? ItemUse { get; set; }

        /// <summary>Sends C2S 0x028 (drop: container, slot, count).</summary>
        public Func<ContainerId, byte, uint, Task>? ItemDrop { get; set; }

        /// <summary>Sends C2S 0x050 (equip: item index, 0 to unequip; slot; container).</summary>
        public Func<byte, EquipSlotId, ContainerId, Task>? ItemEquip { get; set; }

        /// <summary>Sends C2S 0x03A (merge a container's stacks; Auto-sort).</summary>
        public Func<ContainerId, Task>? ItemSort { get; set; }

        /// <summary>
        /// Whom an item with these valid targets (<see cref="StockUiItems.TargetSelf"/> ...) is used on: the command menu's
        /// target when the Items window was opened from it, else the session's choice; an error message when none fits.
        /// </summary>
        public Func<ushort, StockUiTargetContext?, (StockUiItemTarget? Target, string? Error)>? ItemUseTarget { get; set; }

        /// <summary>The main job and level the Equipment list checks items against.</summary>
        public Func<StockUiEquipCharacter>? EquipCharacter { get; set; }

        /// <summary>The Equipment window's stats side.</summary>
        public Func<StockUiCharacterStats?>? CharacterStats { get; set; }

        /// <summary>The container the Items window last showed (it opens on it again, as retail keeps it).</summary>
        private ContainerId _itemsContainer = ContainerId.Inventory;

        /// <summary>Containers auto-sorted this session: their lists are ordered by item id.</summary>
        private readonly HashSet<ContainerId> _sortedContainers = new();

        /// <summary>The Equipment grid's last slot and the Items list's last place, remembered as retail keeps the cursor.</summary>
        private int _lastEquipButton = 1;
        private (int First, int Selected) _lastItemsPlace = (0, 1);

        /// <summary>
        /// Opens the Items window (#351) over <paramref name="parent"/> (the main menu, or the target command menu, whose
        /// target the window's items are then used on), on the container last shown. Returns the list, or null when the UI is
        /// not loaded.
        /// </summary>
        public StockUiOpenMenu? OpenItems(StockUiOpenMenu? parent, StockUiTargetContext? target = null)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiItems.ListMenu, out var definition)) return null;
            if (!StockUiItems.IsAvailable(Inventory, _itemsContainer)) _itemsContainer = ContainerId.Inventory;
            var menu = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null, VisibleRowsOf(definition))
            {
                ItemListKind = StockUiItemListKind.Items,
                ItemContainer = _itemsContainer,
                ItemTarget = target,
                ItemLookup = ItemLookup,
            };
            lock (_sync)
            {
                FillItemRows(menu);
                int maxFirst = Math.Max(0, menu.Rows.Count - menu.VisibleRows);
                menu.FirstRow = Math.Clamp(_lastItemsPlace.First, 0, maxFirst);
                menu.ScrollFrom = menu.FirstRow;
                menu.SelectedButtonId = Math.Clamp(_lastItemsPlace.Selected, 1, Math.Max(1, Math.Min(menu.VisibleRows, menu.Rows.Count - menu.FirstRow)));
                PushOpen(menu);
            }
            Changed?.Invoke();
            return menu;
        }

        /// <summary>
        /// Opens the Equipment window (#352) over <paramref name="parent"/>: the slot grid with the worn items' icons and the
        /// stats side, the cursor on the slot last chosen. Returns it, or null when the UI is not loaded.
        /// </summary>
        public StockUiOpenMenu? OpenEquipment(StockUiOpenMenu? parent)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiItems.EquipmentMenu, out var definition)) return null;
            StockUiOpenMenu? grid = null;
            grid = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null)
            {
                IsEquipment = true,
                Inventory = Inventory,
                ItemLookup = ItemLookup,
                CharacterStats = CharacterStats,
                ListOpenCheck = () => Array.Exists(_open, m => m.IsItemList && ReferenceEquals(m.Parent, grid)),
            };
            grid.SelectedButtonId = definition.FindButton(_lastEquipButton) != null ? _lastEquipButton : FirstSelectable(definition);
            lock (_sync) PushOpen(grid);
            Changed?.Invoke();
            return grid;
        }

        private void PushOpen(StockUiOpenMenu menu)
        {
            var open = new StockUiOpenMenu[_open.Length + 1];
            Array.Copy(_open, open, _open.Length);
            open[^1] = menu;
            _open = open;
        }

        private static int VisibleRowsOf(UiMenuDefinition definition)
        {
            int visible = 0;
            foreach (var button in definition.Buttons)
            {
                if (IsSelectable(button)) visible++;
            }
            return visible;
        }

        private StockUiEquipCharacter Character() => EquipCharacter?.Invoke() ?? default;

        /// <summary>Rebuilds an item list's rows (under the lock), keeping the cursor on the same item when it is still listed.</summary>
        private void FillItemRows(StockUiOpenMenu menu)
        {
            var before = menu.SelectedItemRow;
            var rows = menu.ItemListKind == StockUiItemListKind.Equipment
                ? StockUiItems.EquipRows(Inventory, menu.ItemEquipSlot, ItemLookup, Character())
                : StockUiItems.Rows(Inventory, menu.ItemContainer, ItemLookup, _sortedContainers.Contains(menu.ItemContainer));
            var listRows = new List<StockUiListRow>(rows.Count);
            foreach (var row in rows) listRows.Add(new StockUiListRow(listRows.Count + 1, row.Name, false));
            menu.ItemRows = rows;
            menu.Rows = listRows;
            menu.ItemFill = StockUiItems.Fill(Inventory, menu.ItemContainer);

            int maxFirst = Math.Max(0, rows.Count - menu.VisibleRows);
            if (before is { } previous)
            {
                int index = rows.FindIndex(r => r.IsRemove == previous.IsRemove && r.Container == previous.Container && r.Slot == previous.Slot);
                if (index >= 0 && (index < menu.FirstRow || index >= menu.FirstRow + menu.VisibleRows))
                {
                    menu.FirstRow = Math.Clamp(index - menu.SelectedButtonId + 1, 0, maxFirst);
                    menu.ScrollFrom = menu.FirstRow;
                }
                if (index >= 0) menu.SelectedButtonId = index - menu.FirstRow + 1;
            }
            if (menu.FirstRow > maxFirst)
            {
                menu.FirstRow = maxFirst;
                menu.ScrollFrom = maxFirst;
            }
            int lastRow = Math.Max(1, Math.Min(menu.VisibleRows, rows.Count - menu.FirstRow));
            if (menu.SelectedButtonId < 1 || menu.SelectedButtonId > lastRow) menu.SelectedButtonId = lastRow;
        }

        /// <summary>
        /// The character's items or equipment changed (S2C 0x01F / 0x020 / 0x01E / 0x050): open item lists take the new rows,
        /// and a Use / Drop window whose item is gone closes. Cheap when no item window is open.
        /// </summary>
        public void OnItemsChanged()
        {
            StockUiOpenMenu? closeFrom = null;
            bool changed = false;
            lock (_sync)
            {
                foreach (var menu in _open)
                {
                    if (menu.IsItemList)
                    {
                        FillItemRows(menu);
                        changed = true;
                    }
                    else if (menu.IsEquipment)
                    {
                        changed = true;
                    }
                    else if (menu.ItemAction is { } action && closeFrom == null)
                    {
                        if (Inventory?.GetContainer(action.Container).TryGetItem(action.Slot, out var item) != true || item.ItemId != action.ItemId)
                        {
                            closeFrom = menu;
                        }
                    }
                }
            }
            if (closeFrom != null) CloseMenu(closeFrom);
            else if (changed) Changed?.Invoke();
        }

        /// <summary>Remembers where an item window's cursor was (the Items list's place, the Equipment grid's slot).</summary>
        private void RememberItemWindow(StockUiOpenMenu menu)
        {
            if (menu.IsEquipment) _lastEquipButton = menu.SelectedButtonId;
            else if (menu.ItemListKind == StockUiItemListKind.Items) _lastItemsPlace = (menu.FirstRow, menu.SelectedButtonId);
        }

        /// <summary>The Items list nearest the top of the stack.</summary>
        private StockUiOpenMenu? ItemsList()
        {
            var open = _open;
            for (int i = open.Length - 1; i >= 0; i--)
            {
                if (open[i].ItemListKind == StockUiItemListKind.Items) return open[i];
            }
            return null;
        }

        /// <summary>
        /// + on the keyboard or Y on the gamepad in the Items window: from the list the cursor goes to the options window
        /// beside it ("+ : Sort": Auto, Manual, Switch); pressed again it goes back to the list. Returns whether it applied.
        /// </summary>
        public bool ToggleItemOptions()
        {
            var top = Top;
            if (top is { IsItemOptions: true })
            {
                CloseMenu(top);
                return true;
            }
            var library = _library;
            if (top is not { ItemListKind: StockUiItemListKind.Items } || library == null || !library.TryGetMenu(StockUiItems.OptionsMenu, out var definition)) return false;
            var options = new StockUiOpenMenu(definition, top, Array.Empty<string>(), null) { IsItemOptions = true };
            options.SetGreyed(new HashSet<int> { StockUiItems.ManualSortButton });
            options.SelectedButtonId = StockUiItems.AutoSortButton;
            lock (_sync) PushOpen(options);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Confirm in one of the Items or Equipment windows; false when the top menu is none of them.</summary>
        private bool TryActivateItemWindow(StockUiOpenMenu top, UiMenuButton button)
        {
            if (top.IsItemList) ActivateItemRow(top);
            else if (top.ItemAction is { } action) ActivateItemAction(top, button, action);
            else if (top.IsItemOptions) ActivateItemOptions(top, button);
            else if (top.IsItemStorageMenu) ActivateItemStorage(top, button);
            else if (top.IsEquipment) ActivateEquipmentSlot(top, button);
            else return false;
            return true;
        }

        /// <summary>Confirm on a list row: the Items window opens Use / Drop; the Equipment list equips the row or removes the slot's item.</summary>
        private void ActivateItemRow(StockUiOpenMenu list)
        {
            if (list.SelectedItemRow is not { } row) return;
            if (list.ItemListKind == StockUiItemListKind.Equipment)
            {
                ActivateEquipRow(list, row);
                return;
            }
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiItems.ActionMenu, out var definition)) return;
            var record = ItemLookup?.Invoke(row.ItemId);
            var greyed = new HashSet<int>();
            bool usable = StockUiItems.IsUsable(record) && Contains(StockUiItems.UsableContainers, row.Container);
            if (!usable) greyed.Add(StockUiItems.UseButton);
            if (row.Equipped || row.Container == ContainerId.RecycleBin) greyed.Add(StockUiItems.DropButton);
            var menu = new StockUiOpenMenu(definition, list, Array.Empty<string>(), null) { ItemAction = row };
            menu.SetGreyed(greyed.Count > 0 ? greyed : null);
            menu.SelectedButtonId = usable || greyed.Contains(StockUiItems.DropButton) ? StockUiItems.UseButton : StockUiItems.DropButton;
            lock (_sync) PushOpen(menu);
            Changed?.Invoke();
        }

        private static bool Contains(IReadOnlyList<ContainerId> containers, ContainerId container)
        {
            foreach (var c in containers)
            {
                if (c == container) return true;
            }
            return false;
        }

        /// <summary>Use: sends 0x037 on the resolved target and closes the menus. Drop: the count (for a stack), then the yes/no prompt.</summary>
        private void ActivateItemAction(StockUiOpenMenu menu, UiMenuButton button, StockUiItemRow row)
        {
            var record = StockUiShop.Lookup(ItemLookup, row.ItemId);
            if (menu.IsGreyed(button.ButtonId))
            {
                NoticePosted?.Invoke(button.ButtonId == StockUiItems.UseButton
                    ? $"{record.Name} cannot be used."
                    : row.Equipped ? $"{record.Name} is equipped and cannot be dropped." : $"{record.Name} cannot be dropped.");
                return;
            }
            if (button.ButtonId == StockUiItems.UseButton)
            {
                var (target, error) = ItemUseTarget?.Invoke(record.ValidTargets, (menu.Parent ?? menu).ItemTarget) ?? (null, "Items cannot be used in this session.");
                var use = ItemUse;
                if (target is not { } t || use == null)
                {
                    NoticePosted?.Invoke(error ?? "Items cannot be used in this session.");
                    return;
                }
                CloseAll();
                GordianLog.Info("ITEM", $"Using {record.Name} (item {row.ItemId}, {row.Container} slot {row.Slot}) on {t.Name} ({t.ServerId}, index {t.TargetIndex}).");
                _ = SendItemAsync(() => use(t.ServerId, t.TargetIndex, row.Container, row.Slot), "Use");
                return;
            }
            if (button.ButtonId != StockUiItems.DropButton) return;
            if (row.Count > 1)
            {
                OpenDropQuantity(menu, row);
                return;
            }
            ConfirmDrop(menu, row, 1);
        }

        /// <summary>The quantity prompt (<c>itemctrl</c>) for dropping part of a stack, starting at 1 (as the shop's).</summary>
        private void OpenDropQuantity(StockUiOpenMenu action, StockUiItemRow row)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiShop.QuantityMenu, out var definition)) return;
            var list = action.Parent ?? action;
            var prompt = new StockUiOpenMenu(definition, list, Array.Empty<string>(), null)
            {
                IsQuantity = true,
                ItemDropRow = row,
                QuantityMax = Math.Max(1, row.Count),
                QuantityTotal = Math.Max(1, row.Count),
                ItemLookup = ItemLookup,
            };
            prompt.Quantity = 1;
            prompt.SelectedButtonId = StockUiShop.QuantityField;
            lock (_sync)
            {
                list.ShowsQuantity = true;
                PushOpen(prompt);
            }
            Changed?.Invoke();
        }

        /// <summary>Confirm on a Drop quantity prompt: closes it and asks to confirm the drop.</summary>
        private void RunDropQuantity(StockUiOpenMenu prompt, StockUiItemRow row)
        {
            uint count = prompt.Quantity;
            lock (_sync)
            {
                if (ReferenceEquals(Top, prompt)) _open = _open[..^1];
                if (prompt.Parent is { } list) list.ShowsQuantity = false;
            }
            Changed?.Invoke();
            StockUiOpenMenu? action = null;
            foreach (var m in _open)
            {
                if (m.ItemAction is { } a && a.Container == row.Container && a.Slot == row.Slot) action = m;
            }
            ConfirmDrop(action, row, count);
        }

        /// <summary>Asks "Drop ...?" (No under the cursor); Yes sends 0x028 and closes the Use / Drop window.</summary>
        private void ConfirmDrop(StockUiOpenMenu? action, StockUiItemRow row, uint count)
        {
            var record = StockUiShop.Lookup(ItemLookup, row.ItemId);
            var prompt = OpenYesNo(StockUiItems.DropPrompt(record, count), defaultYes: false, yes =>
            {
                if (yes != true) return;
                if (action != null) CloseMenu(action);
                var drop = ItemDrop;
                if (drop == null)
                {
                    NoticePosted?.Invoke("Items cannot be dropped in this session.");
                    return;
                }
                GordianLog.Info("ITEM", $"Dropping {count} x {record.Name} (item {row.ItemId}, {row.Container} slot {row.Slot}).");
                _ = SendItemAsync(() => drop(row.Container, row.Slot, count), "Drop");
            });
            if (prompt == null) NoticePosted?.Invoke("Drop: the prompt is not available.");
        }

        /// <summary>Auto sends 0x03A and orders the list by item; Manual is not built yet; Switch opens the storage list.</summary>
        private void ActivateItemOptions(StockUiOpenMenu options, UiMenuButton button)
        {
            var list = ItemsList();
            if (options.IsGreyed(button.ButtonId))
            {
                NoticePosted?.Invoke("Manual Sort is not available yet.");
                return;
            }
            if (list == null)
            {
                CloseMenu(options);
                return;
            }
            switch (button.ButtonId)
            {
                case StockUiItems.AutoSortButton:
                {
                    var container = list.ItemContainer;
                    CloseMenu(options);
                    lock (_sync)
                    {
                        _sortedContainers.Add(container);
                        FillItemRows(list);
                    }
                    Changed?.Invoke();
                    if (ItemSort is { } sort)
                    {
                        GordianLog.Info("ITEM", $"Auto-sort of {container}.");
                        _ = SendItemAsync(() => sort(container), "Sort");
                    }
                    break;
                }
                case StockUiItems.SwitchButton:
                {
                    var library = _library;
                    if (library == null || !library.TryGetMenu(StockUiItems.StorageMenu, out var definition)) return;
                    var storage = new StockUiOpenMenu(definition, options, Array.Empty<string>(), null) { IsItemStorageMenu = true };
                    var greyed = new HashSet<int>();
                    foreach (var b in definition.Buttons)
                    {
                        if (StockUiItems.TryGetStorage(b.ButtonId, out var c) && !StockUiItems.IsAvailable(Inventory, c)) greyed.Add(b.ButtonId);
                    }
                    storage.SetGreyed(greyed.Count > 0 ? greyed : null);
                    int current = StockUiItems.StorageButtonOf(list.ItemContainer);
                    storage.SelectedButtonId = current > 0 ? current : FirstSelectable(definition);
                    lock (_sync) PushOpen(storage);
                    Changed?.Invoke();
                    break;
                }
            }
        }

        /// <summary>A storage of the Switch list: the Items list shows it from its first row, and the cursor returns to the list.</summary>
        private void ActivateItemStorage(StockUiOpenMenu storage, UiMenuButton button)
        {
            if (!StockUiItems.TryGetStorage(button.ButtonId, out var container)) return;
            if (storage.IsGreyed(button.ButtonId))
            {
                NoticePosted?.Invoke($"{StockUiItems.ContainerName(container)} is not available.");
                return;
            }
            var list = ItemsList();
            CloseMenu(storage.Parent ?? storage);
            if (list == null) return;
            lock (_sync)
            {
                list.ItemContainer = container;
                _itemsContainer = container;
                list.FirstRow = 0;
                list.ScrollFrom = 0;
                list.SelectedButtonId = 1;
                FillItemRows(list);
            }
            Changed?.Invoke();
        }

        /// <summary>Confirm on a grid slot: opens the list of the items that fit it, beside the grid.</summary>
        private void ActivateEquipmentSlot(StockUiOpenMenu grid, UiMenuButton button)
        {
            var library = _library;
            if (!StockUiCheck.TryGetSlot(button.ButtonId, out var slot) || library == null || !library.TryGetMenu(StockUiItems.ListMenu, out var template)) return;
            var frame = new UiMenuFrame
            {
                X = StockUiItems.EquipListX,
                Y = template.Frame.Y,
                Width = template.Frame.Width,
                Height = template.Frame.Height,
                Anchor = template.Frame.Anchor,
                Shapes = template.Frame.Shapes,
                CursorOffsetX = template.Frame.CursorOffsetX,
                CursorOffsetY = template.Frame.CursorOffsetY,
                HelpTextId = template.Frame.HelpTextId,
                TitleTextId = template.Frame.TitleTextId,
            };
            var definition = new UiMenuDefinition
            {
                DatId = template.DatId,
                Category = template.Category,
                Name = template.Name,
                MenuType = template.MenuType,
                Frame = frame,
                Buttons = template.Buttons,
            };
            var list = new StockUiOpenMenu(definition, grid, Array.Empty<string>(), null, VisibleRowsOf(definition))
            {
                ItemListKind = StockUiItemListKind.Equipment,
                ItemEquipSlot = slot,
                ItemContainer = ContainerId.Inventory,
                ItemLookup = ItemLookup,
            };
            lock (_sync)
            {
                FillItemRows(list);
                if (list.ItemRows.Count == 0)
                {
                    list = null;
                }
                else
                {
                    list.SelectedButtonId = 1;
                    PushOpen(list);
                }
            }
            if (list == null)
            {
                NoticePosted?.Invoke("You have nothing to equip there.");
                return;
            }
            Changed?.Invoke();
        }

        /// <summary>Equips the row's item in the list's slot (0x050), or with Remove unequips it (item index 0); the list closes.</summary>
        private void ActivateEquipRow(StockUiOpenMenu list, StockUiItemRow row)
        {
            if (row.Greyed)
            {
                NoticePosted?.Invoke(row.GreyedReason ?? $"{row.Name} cannot be equipped.");
                return;
            }
            var slot = list.ItemEquipSlot;
            CloseMenu(list);
            if (!row.IsRemove && row.Equipped) return; // already worn in this slot
            var equip = ItemEquip;
            if (equip == null)
            {
                NoticePosted?.Invoke("Equipment cannot be changed in this session.");
                return;
            }
            byte index = row.IsRemove ? (byte)0 : row.Slot;
            var container = row.IsRemove ? ContainerId.Inventory : row.Container;
            GordianLog.Info("ITEM", row.IsRemove ? $"Removing the {slot} slot's item." : $"Equipping {row.Name} (item {row.ItemId}, {container} slot {row.Slot}) in {slot}.");
            _ = SendItemAsync(() => equip(index, slot, container), row.IsRemove ? "Remove" : "Equip");
        }

        /// <summary>
        /// Turns the description of the item under an Items or Equipment window's cursor to its next page (the gamepad's X,
        /// as in the check window). Returns whether a page turned.
        /// </summary>
        public bool NextItemPage()
        {
            bool turned = false;
            lock (_sync)
            {
                if (Top is { } top && (top.IsItemList || top.IsEquipment)) turned = top.NextDescriptionPage();
            }
            if (turned) Changed?.Invoke();
            return turned;
        }

        private async Task SendItemAsync(Func<Task> send, string what)
        {
            try
            {
                await send().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                GordianLog.Warning("ITEM", $"{what} failed: {ex.Message}");
                NoticePosted?.Invoke($"{what} failed: {ex.Message}");
            }
        }
    }
}
