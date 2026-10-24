// src/Gordian.Engine/Ui/StockUiMenuController.Items.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Ui;
using Gordian.Core.World;

namespace Gordian.Engine.Ui
{
    /// <summary>Whom an item is used on: the entity's server id, target index and name.</summary>
    public readonly record struct StockUiItemTarget(uint ServerId, ushort TargetIndex, string Name);

    public sealed partial class StockUiOpenMenu
    {
        /// <summary>Which window an item list (<see cref="StockUiItems.ListMenu"/>) belongs to; null for other menus.</summary>
        public StockUiItemListKind? ItemListKind { get; init; }

        public bool IsItemList => ItemListKind != null;

        /// <summary>The container an item list shows (the Equipment list's storage changes with Storage Options).</summary>
        public ContainerId ItemContainer { get; internal set; }

        /// <summary>The equipment slot the Equipment list is for.</summary>
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

        /// <summary>
        /// Rows drawn beside a window without taking the cursor: the inventory right of the Recycling Bin, the storage's
        /// equipment right of the Equipment grid (retail captures, 2026-10-11).
        /// </summary>
        public IReadOnlyList<StockUiItemRow> CompanionRows { get; internal set; } = Array.Empty<StockUiItemRow>();

        /// <summary>The target an Items window opened from the target command menu uses its items on; null from the main menu.</summary>
        public StockUiTargetContext? ItemTarget { get; init; }

        /// <summary>The row the Use / Drop window (<see cref="StockUiItems.ActionMenu"/>) acts on; null for other menus.</summary>
        public StockUiItemRow? ItemAction { get; init; }

        /// <summary>The row a Drop (or, with <see cref="ItemRestore"/>, a Recycling Bin restore) quantity prompt is for.</summary>
        public StockUiItemRow? ItemDropRow { get; init; }

        /// <summary>Whether the quantity prompt restores a Recycling Bin item rather than dropping one.</summary>
        public bool ItemRestore { get; init; }

        /// <summary>The main menu Items window's options (<see cref="StockUiItems.OptionsMenu"/>: Auto / Manual / Recycle Bin).</summary>
        public bool IsItemOptions { get; init; }

        /// <summary>Auto-sort's Yes / No (<see cref="StockUiItems.SortConfirmMenu"/>).</summary>
        public bool IsSortConfirm { get; init; }

        /// <summary>The Equipment list's storage choice (<see cref="StockUiItems.EquipStorageMenu"/>).</summary>
        public bool IsEquipStorageMenu { get; init; }

        /// <summary>The Equipment window's slot grid (<see cref="StockUiItems.EquipmentMenu"/>).</summary>
        public bool IsEquipment { get; init; }

        /// <summary>The storage the Equipment grid's companion list shows.</summary>
        public ContainerId EquipContainer { get; internal set; }

        /// <summary>The inventory the Equipment grid shows the worn items of.</summary>
        public InventoryState? Inventory { get; init; }

        /// <summary>The stats side the Equipment window draws (<see cref="StockUiItems.StatsMenu"/>).</summary>
        public Func<StockUiCharacterStats?>? CharacterStats { get; init; }

        /// <summary>The Items list is in Manual sort: the first Confirm picks the item to move, the second its place.</summary>
        public bool IsManualSort { get; internal set; }

        /// <summary>The entry index of the item picked to move in Manual sort; null before the first pick.</summary>
        public int? ManualSortFrom { get; internal set; }

        internal StockUiMenuController? ItemOwner { get; init; }

        /// <summary>Whether the Equipment grid has its item list open over it (the list then takes the cursor and the description).</summary>
        public bool IsListOpen => ItemOwner?.HasChild(this, m => m.IsItemList) == true;

        /// <summary>Whether a Use / Drop window is open over this item list (retail then shows the description right under the list).</summary>
        public bool HasItemActionOver => ItemOwner?.HasChild(this, m => m.ItemAction != null) == true;

        /// <summary>The help bar this window draws: only the item list or grid nearest the top of the stack draws one.</summary>
        public StockUiItemHelpBar? ItemHelpBar => ItemOwner?.ItemHelpBarFor(this);

        /// <summary>A menu help text (ROM/165/75) by id, null when the table is not loaded.</summary>
        public string? ItemHelpText(int id) => ItemOwner?.MenuHelpText?.Invoke(id);

        /// <summary>An Items or Equipment window: drawn at its own place rather than following the root.</summary>
        public bool IsItemWindow => IsItemList || ItemAction != null || ItemDropRow != null || IsItemOptions || IsSortConfirm || IsEquipStorageMenu || IsEquipment
            || IsSynthesisGrid || IsSynthesisOptions;

        /// <summary>The item worn in the slot a grid button shows (0 when empty or not the Equipment grid).</summary>
        public ushort EquippedItemIn(int buttonId) =>
            IsEquipment && StockUiCheck.TryGetSlot(buttonId, out var slot) ? StockUiItems.EquippedItem(Inventory, slot)?.ItemId ?? 0 : (ushort)0;

        /// <summary>
        /// The item whose description the window shows in the item info window (#418: one source for every window that has
        /// one): an item list's row under the cursor, the Equipment, Equipment Sets or synthesis grid's slot, the check
        /// window's slot, the Treasure Pool's row or the shop's row; 0 for none.
        /// </summary>
        public ushort DescribedItem =>
            IsItemList ? SelectedItemRow is { IsStorageOptions: false } row ? row.ItemId : (ushort)0
            : IsSynthesisGrid ? SynthesisGridItem
            : IsEquipSetGrid ? EquipSetItemIn(SelectedButtonId)
            : IsEquipment ? EquippedItemIn(SelectedButtonId)
            : IsCheck ? SelectedCheckItem
            : IsTreasureList ? SelectedTreasureRow?.ItemId ?? 0
            : IsShopList ? SelectedShopRow?.ItemId ?? 0
            : (ushort)0;

        private ushort _pagesItem;
        private Func<string, float>? _pagesMeasure;
        private IReadOnlyList<StockUiItemDescriptionPage> _pages = Array.Empty<StockUiItemDescriptionPage>();

        /// <summary>
        /// The description pages of <see cref="DescribedItem"/> (empty without an item or its record), built once per item
        /// (and again when the HUD's font measure arrives) rather than on every read.
        /// </summary>
        public IReadOnlyList<StockUiItemDescriptionPage> DescriptionPages
        {
            get
            {
                ushort id = DescribedItem;
                var measure = StockUiItemDescription.FontMeasure;
                if (id != _pagesItem || !ReferenceEquals(measure, _pagesMeasure) || (id != 0 && _pages.Count == 0))
                {
                    _pages = id != 0 && ItemLookup?.Invoke(id) is { } record
                        ? StockUiItemDescription.Pages(record)
                        : Array.Empty<StockUiItemDescriptionPage>();
                    _pagesItem = id;
                    _pagesMeasure = measure;
                }
                return _pages;
            }
        }

        private ushort _pageItem;
        private int _page;

        /// <summary>
        /// The description page shown (0 = the first); it goes back to the first whenever the cursor moves or the described
        /// item changes.
        /// </summary>
        public int DescriptionPage => _pageItem == DescribedItem ? _page : 0;

        /// <summary>The page of <see cref="DescriptionPages"/> shown, null when there is no description.</summary>
        public StockUiItemDescriptionPage? ShownDescriptionPage
        {
            get
            {
                var pages = DescriptionPages;
                return pages.Count == 0 ? null : pages[Math.Clamp(DescriptionPage, 0, pages.Count - 1)];
            }
        }

        /// <summary>Turns the description to its next page, wrapping; false when it has one page.</summary>
        internal bool NextDescriptionPage()
        {
            int count = DescriptionPages.Count;
            if (count <= 1) return false;
            _page = (DescriptionPage + 1) % count;
            _pageItem = DescribedItem;
            return true;
        }

        /// <summary>Back to the description's first page (the cursor moved).</summary>
        private void ResetDescriptionPage() => _page = 0;
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>Sends C2S 0x037 (use an item: target id and index, container, slot).</summary>
        public Func<uint, ushort, ContainerId, byte, Task>? ItemUse { get; set; }

        /// <summary>Sends C2S 0x028 (drop: container, slot, count).</summary>
        public Func<ContainerId, byte, uint, Task>? ItemDrop { get; set; }

        /// <summary>Sends C2S 0x029 (move: count, from container and slot, to container and slot).</summary>
        public Func<uint, ContainerId, byte, ContainerId, byte, Task>? ItemMove { get; set; }

        /// <summary>Sends C2S 0x050 (equip: item index, 0 to unequip; slot; container).</summary>
        public Func<byte, EquipSlotId, ContainerId, Task>? ItemEquip { get; set; }

        /// <summary>Sends C2S 0x03A (merge a container's stacks; Auto-sort).</summary>
        public Func<ContainerId, Task>? ItemSort { get; set; }

        /// <summary>
        /// Whom an item with these valid targets (<see cref="StockUiItems.TargetSelf"/> ...) is used on without the sub-target
        /// cursor: the command menu's target when the Items window was opened from it, else the session's choice; an error
        /// message when none fits.
        /// </summary>
        public Func<ushort, StockUiTargetContext?, (StockUiItemTarget? Target, string? Error)>? ItemUseTarget { get; set; }

        /// <summary>
        /// Starts the sub-target cursor (retail's &lt;st&gt;) for an item that can be used on others (the maintainer's retail
        /// check, 2026-10-11: a Potion goes to the sub-target cursor). Arguments: the row (container, slot) and the item's
        /// valid targets; true when the cursor started (the menus then close). Without it, <see cref="ItemUseTarget"/> picks
        /// the target.
        /// </summary>
        public Func<StockUiItemRow, ushort, bool>? ItemSubTarget { get; set; }

        /// <summary>The main job and level the Equipment list checks items against.</summary>
        public Func<StockUiEquipCharacter>? EquipCharacter { get; set; }

        /// <summary>The Equipment window's stats side.</summary>
        public Func<StockUiCharacterStats?>? CharacterStats { get; set; }

        /// <summary>The menu help text table (ROM/165/75) by id, for the item windows' help bar; set by the HUD.</summary>
        public Func<int, string?>? MenuHelpText { get; set; }

        /// <summary>A client message for the log in the error colour (a crystal used with a full inventory).</summary>
        public event Action<string>? ErrorPosted;

        /// <summary>The client's own order of a container's items (auto / manual sort), as slots; kept for the session.</summary>
        private readonly Dictionary<ContainerId, List<byte>> _itemOrder = new();

        /// <summary>The Equipment grid's last slot and the Items list's last place, remembered as retail keeps the cursor.</summary>
        private int _lastEquipButton = 1;
        private (int First, int Selected) _lastItemsPlace = (0, 1);

        /// <summary>Whether a menu opened over <paramref name="parent"/> (directly) matches.</summary>
        internal bool HasChild(StockUiOpenMenu parent, Func<StockUiOpenMenu, bool> match)
        {
            foreach (var menu in _open)
            {
                if (ReferenceEquals(menu.Parent, parent) && match(menu)) return true;
            }
            return false;
        }

        /// <summary>
        /// Opens the Items window (#351) over <paramref name="parent"/>. From the main menu it lists the inventory; from the
        /// target command menu (<paramref name="target"/> set) only the usable items, which Confirm uses at once (the
        /// maintainer's retail captures, 2026-10-11). Returns the list, or null when the UI is not loaded.
        /// </summary>
        public StockUiOpenMenu? OpenItems(StockUiOpenMenu? parent, StockUiTargetContext? target = null)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiItems.ListMenu, out var definition)) return null;
            bool usable = target != null;
            var menu = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null, VisibleRowsOf(definition))
            {
                ItemListKind = usable ? StockUiItemListKind.Usable : StockUiItemListKind.Items,
                ItemContainer = ContainerId.Inventory,
                ItemTarget = target,
                ItemLookup = ItemLookup,
                ItemOwner = this,
            };
            lock (_sync)
            {
                FillItemRows(menu);
                if (!usable)
                {
                    int maxFirst = Math.Max(0, menu.Rows.Count - menu.VisibleRows);
                    menu.FirstRow = Math.Clamp(_lastItemsPlace.First, 0, maxFirst);
                    menu.ScrollFrom = menu.FirstRow;
                    menu.SelectedButtonId = Math.Clamp(_lastItemsPlace.Selected, 1, Math.Max(1, Math.Min(menu.VisibleRows, menu.Rows.Count - menu.FirstRow)));
                }
                else
                {
                    menu.SelectedButtonId = 1;
                }
                PushOpen(menu);
            }
            Changed?.Invoke();
            return menu;
        }

        /// <summary>
        /// Opens the Equipment window (#352) over <paramref name="parent"/>: the slot grid with the worn items' icons, the stats
        /// side and the storage's equipment beside it, the cursor on the slot last chosen. Returns it, or null when the UI is
        /// not loaded.
        /// </summary>
        public StockUiOpenMenu? OpenEquipment(StockUiOpenMenu? parent)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiItems.EquipmentMenu, out var definition)) return null;
            var grid = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null)
            {
                IsEquipment = true,
                Inventory = Inventory,
                ItemLookup = ItemLookup,
                CharacterStats = CharacterStats,
                EquipContainer = ContainerId.Inventory,
                ItemOwner = this,
            };
            grid.SelectedButtonId = definition.FindButton(_lastEquipButton) != null ? _lastEquipButton : FirstSelectable(definition);
            lock (_sync)
            {
                grid.CompanionRows = StockUiItems.EquipRows(Inventory, grid.EquipContainer, null, ItemLookup, Character());
                PushOpen(grid);
            }
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

        /// <summary>A container's rows in the client's order (auto / manual sort).</summary>
        private List<StockUiItemRow> OrderedRows(ContainerId container, bool usableOnly = false)
        {
            var rows = StockUiItems.Rows(Inventory, container, ItemLookup, usableOnly);
            StockUiItems.ApplyOrder(rows, _itemOrder.TryGetValue(container, out var order) ? order : null);
            return rows;
        }

        /// <summary>Config > Gameplay > Inventory Type: 1 by category, 2 by name.</summary>
        private int SortType => _settings.GetValue(StockUiSettingKey.InventoryType) == 2 ? 2 : 1;

        /// <summary>Filled inventory slots last seen (-1 before the first count), to notice an item obtained.</summary>
        private int _inventoryFilled = -1;
        private long _autoStackPending;

        /// <summary>The minimum time between two automatic 0x03A (LandSandBoat logs a player out who sorts too fast, LIGHTLUGGAGE_BLOCK).</summary>
        public static readonly TimeSpan AutoStackDelay = TimeSpan.FromSeconds(2);

        /// <summary>
        /// With Inventory Sort On, an item obtained (more filled inventory slots than before) sorts the inventory by the
        /// Inventory Type and sends one C2S 0x03A to merge the stacks, at most once per <see cref="AutoStackDelay"/>. Between
        /// items the order is left alone, so a manual order stays until the next item arrives.
        /// </summary>
        private void NoteInventoryCount()
        {
            int filled = StockUiItems.Fill(Inventory, ContainerId.Inventory).Used;
            int before = _inventoryFilled;
            _inventoryFilled = filled;
            if (before < 0 || filled <= before || !_settings.IsOn(StockUiSettingKey.InventorySort)) return;
            // The sort runs only when an item arrives: a manual order stays until then (the maintainer's round 2, 2026-10-12).
            lock (_sync) _itemOrder[ContainerId.Inventory] = StockUiItems.AutoOrder(StockUiItems.Rows(Inventory, ContainerId.Inventory, ItemLookup), ItemLookup, SortType);
            if (ItemSort is not { } sort) return;
            if (System.Threading.Interlocked.Exchange(ref _autoStackPending, 1) == 1) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(AutoStackDelay).ConfigureAwait(false);
                    GordianLog.Info("ITEM", "Inventory Sort: merging the inventory's stacks after an item was obtained.");
                    await sort(ContainerId.Inventory).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    GordianLog.Warning("ITEM", $"Inventory Sort failed: {ex.Message}");
                }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref _autoStackPending, 0);
                }
            });
        }

        /// <summary>Rebuilds an item list's rows (under the lock), keeping the cursor on the same item when it is still listed.</summary>
        private void FillItemRows(StockUiOpenMenu menu)
        {
            var before = menu.SelectedItemRow;
            var rows = menu.ItemListKind switch
            {
                StockUiItemListKind.Equipment when menu.EquipSetNumber > 0 => StockUiEquipSets.SetRows(Inventory, menu.ItemContainer, menu.ItemEquipSlot, ItemLookup, MenuMessageText),
                StockUiItemListKind.Equipment => StockUiItems.EquipRows(Inventory, menu.ItemContainer, menu.ItemEquipSlot, ItemLookup, Character()),
                StockUiItemListKind.Usable => OrderedRows(ContainerId.Inventory, usableOnly: true),
                StockUiItemListKind.Crystals => StockUiSynthesis.CrystalRows(OrderedRows(ContainerId.Inventory)),
                StockUiItemListKind.SynthesisMaterials when menu.Parent is { IsSynthesisGrid: true } grid =>
                    StockUiSynthesis.MaterialRows(OrderedRows(ContainerId.Inventory), grid.SynthesisCrystal, grid.SynthesisSlots),
                StockUiItemListKind.SynthesisHistory => StockUiSynthesis.HistoryRows(HistoryEntries(), ItemLookup),
                StockUiItemListKind.GuildSell => StockUiGuildShop.InventoryRows(Inventory?.SnapshotGuildSellList() ?? Array.Empty<GuildItemEntry>(), OrderedRows(ContainerId.Inventory)),
                _ => OrderedRows(menu.ItemContainer),
            };
            if (menu.ItemListKind == StockUiItemListKind.RecycleBin) menu.CompanionRows = OrderedRows(ContainerId.Inventory);
            var listRows = new List<StockUiListRow>(rows.Count);
            foreach (var row in rows) listRows.Add(new StockUiListRow(listRows.Count + 1, row.Name, false));
            menu.ItemRows = rows;
            menu.Rows = listRows;

            int maxFirst = Math.Max(0, rows.Count - menu.VisibleRows);
            if (before is { } previous)
            {
                int index = rows.FindIndex(r => r.IsStorageOptions == previous.IsStorageOptions && r.Container == previous.Container && r.Slot == previous.Slot);
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
            NoteInventoryCount();
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
                        menu.CompanionRows = StockUiItems.EquipRows(Inventory, menu.EquipContainer, null, ItemLookup, Character());
                        changed = true;
                    }
                    else if (menu.IsSynthesisGrid)
                    {
                        RefreshSynthesisRows(menu);
                        changed = true;
                    }
                    else if (menu.IsEquipSetGrid)
                    {
                        menu.CompanionRows = StockUiEquipSets.SetRows(Inventory, menu.EquipContainer, null, ItemLookup, MenuMessageText);
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

        /// <summary>Cancel in Manual sort leaves the mode (the list stays open); true when it did.</summary>
        /// <summary>
        /// Cancel in Manual sort (called under the lock): leaves the mode and returns the cursor to the options with Manual
        /// under it; a second Cancel then closes the options and returns to the list (the maintainer's round 1, 2026-10-12).
        /// </summary>
        private bool CancelItemMode(StockUiOpenMenu top)
        {
            if (!top.IsManualSort) return false;
            top.IsManualSort = false;
            top.ManualSortFrom = null;
            var library = _library;
            if (library != null && library.TryGetMenu(StockUiItems.OptionsMenu, out var definition))
            {
                var options = new StockUiOpenMenu(definition, top, Array.Empty<string>(), null) { IsItemOptions = true };
                options.SelectedButtonId = StockUiItems.ManualSortButton;
                PushOpen(options);
            }
            return true;
        }

        /// <summary>The main menu's Items list nearest the top of the stack.</summary>
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
        /// The help bar of the item windows (the maintainer's retail captures, 2026-10-11): the title ("Items", "Equipment",
        /// "Recycling Bin", "No. of Items" while a count is chosen), the fraction after it ("29/30" used / total for the
        /// inventory, "2/29" listed / held for the command menu's usable items, the bin's "0/10"; none for Equipment), and the
        /// help of the window taking input (its selected button's, else its own; the list's "Select an item.", Manual sort's
        /// "Select item to move.", Auto's "Sort list?"). Only the item list or grid nearest the top draws it.
        /// </summary>
        internal StockUiItemHelpBar? ItemHelpBarFor(StockUiOpenMenu window)
        {
            var open = _open;
            StockUiOpenMenu? baseWindow = null;
            for (int i = open.Length - 1; i >= 0 && baseWindow == null; i--)
            {
                if (open[i].IsItemList || open[i].IsEquipment || open[i].IsEquipSetGrid || open[i].IsSynthesisGrid) baseWindow = open[i];
            }
            if (!ReferenceEquals(baseWindow, window) || open.Length == 0) return null;
            var top = open[^1];
            string title = top.IsQuantity ? StockUiItems.TitleQuantity
                : window.ItemListKind == StockUiItemListKind.RecycleBin ? StockUiItems.TitleRecycleBin
                : window.IsSynthesisGrid || window.ItemListKind == StockUiItemListKind.SynthesisHistory ? StockUiSynthesis.Title
                : window.EquipSetNumber > 0 ? StockUiEquipSets.Title
                : window.IsEquipment || window.ItemListKind == StockUiItemListKind.Equipment ? StockUiItems.TitleEquipment
                : StockUiItems.TitleItems;
            int? numerator = null, denominator = null;
            if (!top.IsQuantity)
            {
                switch (window.ItemListKind)
                {
                    case StockUiItemListKind.Items:
                    case StockUiItemListKind.RecycleBin:
                        (int used, int total) = StockUiItems.Fill(Inventory, window.ItemContainer);
                        numerator = used;
                        denominator = total;
                        break;
                    case StockUiItemListKind.Usable:
                        numerator = window.ItemRows.Count;
                        denominator = StockUiItems.Fill(Inventory, ContainerId.Inventory).Used;
                        break;
                    case StockUiItemListKind.Crystals:
                        // "Items 3/30" (capture Crystal_Synthesis.png): the crystals listed over the inventory's size.
                        numerator = window.ItemRows.Count;
                        denominator = StockUiItems.Fill(Inventory, ContainerId.Inventory).Total;
                        break;
                }
            }
            int help = -1;
            for (int i = open.Length - 1; i >= 0 && help < 0; i--)
            {
                var menu = open[i];
                if (menu.IsSortConfirm) help = StockUiItems.HelpSortList;
                else if (menu.ItemListKind == StockUiItemListKind.SynthesisMaterials) help = StockUiSynthesis.HelpSelectMaterials;
                // The History capture shows "Synthesize this combination of items." while the list has the cursor.
                else if (menu.ItemListKind == StockUiItemListKind.SynthesisHistory) help = StockUiSynthesis.HelpSynthesize;
                else if (menu.IsManualSort) help = StockUiItems.HelpSelectItemToMove;
                else if (menu.SelectedButton is { HelpTextId: >= 0 } button) help = button.HelpTextId;
                else if (menu.Menu.Frame.HelpTextId >= 0) help = menu.Menu.Frame.HelpTextId;
                else if (menu.IsItemList) help = StockUiItems.HelpSelectItem;
                if (ReferenceEquals(menu, window)) break;
            }
            return new StockUiItemHelpBar(title, numerator, denominator, help);
        }

        /// <summary>
        /// + on the keyboard or Y on the gamepad in the main menu's Items window: from the list the cursor goes to the options
        /// at the top right ("+ : Sort": Auto, Manual, Recycle Bin; the maintainer's capture 2026-10-11); pressed again it goes
        /// back to the list. Returns whether it applied.
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
            if (top is not { ItemListKind: StockUiItemListKind.Items, IsManualSort: false } || library == null
                || !library.TryGetMenu(StockUiItems.OptionsMenu, out var definition)) return false;
            var options = new StockUiOpenMenu(definition, top, Array.Empty<string>(), null) { IsItemOptions = true };
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
            else if (top.IsSortConfirm) ActivateSortConfirm(top, button);
            else if (top.IsEquipStorageMenu) ActivateEquipStorage(top, button);
            else if (top.IsEquipment) ActivateEquipmentSlot(top, button);
            else return false;
            return true;
        }

        /// <summary>Confirm on a list row, by list: Use / Drop, Manual sort, use at once, restore, equip.</summary>
        private void ActivateItemRow(StockUiOpenMenu list)
        {
            if (list.SelectedItemRow is not { } row) return;
            switch (list.ItemListKind)
            {
                case StockUiItemListKind.Equipment:
                    ActivateEquipRow(list, row);
                    return;
                case StockUiItemListKind.Usable:
                    UseItem(row, list.ItemTarget, list);
                    return;
                case StockUiItemListKind.RecycleBin:
                    if (row.Count > 1) OpenItemQuantity(list, list, row, restore: true);
                    else RestoreItem(row, 1);
                    return;
                case StockUiItemListKind.Crystals:
                    OpenSynthesisGrid(list, row);
                    return;
                case StockUiItemListKind.SynthesisMaterials:
                    PickMaterial(list, row);
                    return;
                case StockUiItemListKind.SynthesisHistory:
                    ChooseHistoryRecipe(list);
                    return;
                case StockUiItemListKind.GuildSell:
                    PickGuildSale(list, row);
                    return;
            }
            if (list.IsManualSort)
            {
                ManualSortStep(list);
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

        /// <summary>Use: as <see cref="UseItem"/>. Drop: the count (for a stack), then the yes/no prompt.</summary>
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
                UseItem(row, (menu.Parent ?? menu).ItemTarget, menu.Parent ?? menu);
                return;
            }
            if (button.ButtonId != StockUiItems.DropButton) return;
            if (row.Count > 1)
            {
                OpenItemQuantity(menu, menu.Parent ?? menu, row, restore: false);
                return;
            }
            ConfirmDrop(menu, row, 1);
        }

        /// <summary>
        /// Uses an item (the maintainer's retail checks, 2026-10-11 / 12): a crystal opens the synthesis window, or with a full
        /// inventory prints "Unable to execute that command. Your inventory is full." in red (ROM/165/70 #140); every other
        /// usable item goes to the sub-target cursor (<see cref="ItemSubTarget"/>; retail opens it for a Potion too, the cursor
        /// limited to the item's targets, so a self-only item gets it on yourself). Without the hook, C2S 0x037 goes out on
        /// the resolved target (count 0, as LandSandBoat requires). Either way the Items list stays (or comes back after the
        /// cursor, <see cref="ResumeItemWindow"/>): only the Use / Drop window closes.
        /// </summary>
        private void UseItem(StockUiItemRow row, StockUiTargetContext? context, StockUiOpenMenu list)
        {
            var known = ItemLookup?.Invoke(row.ItemId);
            var record = known ?? StockUiShop.Lookup(ItemLookup, row.ItemId);
            if (StockUiItems.IsCrystal(row.ItemId))
            {
                // The synthesis grid opens over the Items list (the video, 2026-10-16: Use on a Wind Crystal); Use / Drop closes.
                CloseItemAction(list);
                OpenSynthesisGrid(list, row);
                return;
            }
            if (!StockUiItems.IsUsable(known))
            {
                NoticePosted?.Invoke($"{record.Name} cannot be used.");
                return;
            }
            CloseItemAction(list);
            if (ItemSubTarget is { } subTarget)
            {
                // The cursor's owner closes the menus; remember the list so it can come back afterwards.
                _itemsResume = (list.ItemListKind ?? StockUiItemListKind.Items, context, list.FirstRow, list.SelectedButtonId, list);
                if (subTarget(row, record.ValidTargets)) return;
                _itemsResume = null;
            }
            var (target, error) = ItemUseTarget?.Invoke(record.ValidTargets, context) ?? (null, "Items cannot be used in this session.");
            var use = ItemUse;
            if (target is not { } t || use == null)
            {
                NoticePosted?.Invoke(error ?? "Items cannot be used in this session.");
                return;
            }
            GordianLog.Info("ITEM", $"Using {record.Name} (item {row.ItemId}, {row.Container} slot {row.Slot}) on {t.Name} ({t.ServerId}, index {t.TargetIndex}).");
            _ = SendItemAsync(() => use(t.ServerId, t.TargetIndex, row.Container, row.Slot), "Use");
        }

        /// <summary>Closes a Use / Drop window open over <paramref name="list"/>, if any (the list keeps the cursor).</summary>
        private void CloseItemAction(StockUiOpenMenu list)
        {
            foreach (var menu in _open)
            {
                if (menu.ItemAction != null && ReferenceEquals(menu.Parent, list))
                {
                    CloseMenu(menu);
                    return;
                }
            }
        }

        /// <summary>The Items list to bring back after the sub-target cursor: its kind, the command menu's target, its place.</summary>
        private (StockUiItemListKind Kind, StockUiTargetContext? Target, int First, int Selected, StockUiOpenMenu List)? _itemsResume;

        /// <summary>
        /// Brings the Items window back after the sub-target cursor used or dropped an item (the maintainer's round 1, 2026-10-12:
        /// using or cancelling returns to the Items list, the command menu's one included): the main menu's Items, or the
        /// command menu's on its target, at the place it was. The cursor's owner calls it when the cursor ends; nothing happens
        /// when no item started the cursor. Returns the reopened list.
        /// </summary>
        public StockUiOpenMenu? ResumeItemWindow()
        {
            if (_itemsResume is not { } resume) return null;
            _itemsResume = null;
            if (Array.IndexOf(_open, resume.List) >= 0)
            {
                // The list is still open (the cursor did not close the menus): only the windows over it go, so no second list
                // stacks on it. A stacked copy needed two Cancels to leave (the maintainer's round 2, 2026-10-12).
                for (var top = Top; top != null && !ReferenceEquals(top, resume.List); top = Top) CloseMenu(top);
                Changed?.Invoke();
                return resume.List;
            }
            // Anything else still open (not the list) goes first, so the reopened menus are the only ones.
            if (IsOpen) CloseAll();
            StockUiOpenMenu? parent = null;
            if (resume.Kind == StockUiItemListKind.Usable && resume.Target is { } target)
            {
                if (!OpenCommandMenu(target)) return null;
                parent = Top;
            }
            else
            {
                if (!OpenMainMenu()) return null;
                parent = Top;
                if (parent != null && parent.Menu.FindButton(StockUiItems.MainMenuItemsButton) != null && parent.PageRing.Count > 0
                    && parent.Name.Equals(StockUiMenuEntries.MainMenu, StringComparison.OrdinalIgnoreCase))
                {
                    parent.SelectedButtonId = StockUiItems.MainMenuItemsButton;
                }
            }
            var list = OpenItems(parent, resume.Kind == StockUiItemListKind.Usable ? resume.Target : null);
            if (list != null)
            {
                lock (_sync)
                {
                    list.FirstRow = Math.Clamp(resume.First, 0, Math.Max(0, list.Rows.Count - list.VisibleRows));
                    list.ScrollFrom = list.FirstRow;
                    list.SelectedButtonId = Math.Clamp(resume.Selected, 1, Math.Max(1, Math.Min(list.VisibleRows, list.Rows.Count - list.FirstRow)));
                }
                Changed?.Invoke();
            }
            return list;
        }

        /// <summary>
        /// The quantity prompt (<c>itemctrl</c>) for dropping part of a stack or restoring part of a bin stack, starting at 1;
        /// the HUD draws it at the bottom left with the description beside it (retail capture 2026-10-11, the bin's restore).
        /// </summary>
        private void OpenItemQuantity(StockUiOpenMenu over, StockUiOpenMenu list, StockUiItemRow row, bool restore)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiShop.QuantityMenu, out var definition)) return;
            var prompt = new StockUiOpenMenu(definition, list, Array.Empty<string>(), null)
            {
                IsQuantity = true,
                ItemDropRow = row,
                ItemRestore = restore,
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

        /// <summary>Confirm on a Drop / restore quantity prompt: closes it, then asks to confirm the drop or restores at once.</summary>
        private void RunDropQuantity(StockUiOpenMenu prompt, StockUiItemRow row)
        {
            uint count = prompt.Quantity;
            lock (_sync)
            {
                if (ReferenceEquals(Top, prompt)) _open = _open[..^1];
                if (prompt.Parent is { } list) list.ShowsQuantity = false;
            }
            Changed?.Invoke();
            if (prompt.ItemRestore)
            {
                RestoreItem(row, count);
                return;
            }
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

        /// <summary>
        /// Restores a Recycling Bin item to the inventory: C2S 0x029 from container 17 to the inventory's first free slot
        /// (<see cref="StockUiItems.AnyFreeSlot"/>); LandSandBoat allows the bin's items to go back to the inventory or the
        /// Mog Locker only. A full inventory prints the error message instead.
        /// </summary>
        private void RestoreItem(StockUiItemRow row, uint count)
        {
            if (StockUiItems.IsInventoryFull(Inventory))
            {
                ErrorPosted?.Invoke(StockUiItems.InventoryFullMessage);
                return;
            }
            var move = ItemMove;
            if (move == null)
            {
                NoticePosted?.Invoke("Items cannot be restored in this session.");
                return;
            }
            GordianLog.Info("ITEM", $"Restoring {count} x item {row.ItemId} from the recycle bin slot {row.Slot}.");
            _ = SendItemAsync(() => move(count, ContainerId.RecycleBin, row.Slot, ContainerId.Inventory, StockUiItems.AnyFreeSlot), "Restore");
        }

        /// <summary>Auto asks to sort (Yes / No under the options); Manual returns to the list to move an item; Recycle Bin opens the bin.</summary>
        private void ActivateItemOptions(StockUiOpenMenu options, UiMenuButton button)
        {
            var list = ItemsList();
            var library = _library;
            if (list == null || library == null)
            {
                CloseMenu(options);
                return;
            }
            switch (button.ButtonId)
            {
                case StockUiItems.AutoSortButton:
                {
                    if (!library.TryGetMenu(StockUiItems.SortConfirmMenu, out var definition)) return;
                    var confirm = new StockUiOpenMenu(definition, options, Array.Empty<string>(), null) { IsSortConfirm = true };
                    confirm.SelectedButtonId = StockUiItems.SortNoButton; // No under the cursor (retail capture 2026-10-11)
                    lock (_sync) PushOpen(confirm);
                    Changed?.Invoke();
                    break;
                }
                case StockUiItems.ManualSortButton:
                    CloseMenu(options);
                    lock (_sync)
                    {
                        list.IsManualSort = true;
                        list.ManualSortFrom = null;
                    }
                    Changed?.Invoke();
                    break;
                case StockUiItems.RecycleBinButton:
                {
                    if (!library.TryGetMenu(StockUiItems.ListMenu, out var definition)) return;
                    var bin = new StockUiOpenMenu(definition, options, Array.Empty<string>(), null, VisibleRowsOf(definition))
                    {
                        ItemListKind = StockUiItemListKind.RecycleBin,
                        ItemContainer = ContainerId.RecycleBin,
                        ItemLookup = ItemLookup,
                        ItemOwner = this,
                    };
                    lock (_sync)
                    {
                        FillItemRows(bin);
                        bin.SelectedButtonId = 1;
                        PushOpen(bin);
                    }
                    Changed?.Invoke();
                    break;
                }
            }
        }

        /// <summary>Auto-sort's Yes: sends 0x03A and orders the list by item; No returns to the options.</summary>
        private void ActivateSortConfirm(StockUiOpenMenu confirm, UiMenuButton button)
        {
            if (button.ButtonId != StockUiItems.SortYesButton)
            {
                CloseMenu(confirm);
                return;
            }
            var list = ItemsList();
            CloseMenu(confirm.Parent ?? confirm);
            if (list == null) return;
            var container = list.ItemContainer;
            lock (_sync)
            {
                _itemOrder[container] = StockUiItems.AutoOrder(StockUiItems.Rows(Inventory, container, ItemLookup), ItemLookup, SortType);
                FillItemRows(list);
            }
            Changed?.Invoke();
            if (ItemSort is { } sort)
            {
                GordianLog.Info("ITEM", $"Auto-sort of {container}.");
                _ = SendItemAsync(() => sort(container), "Sort");
            }
        }

        /// <summary>
        /// Manual sort: the first Confirm picks the item, the second the place; the first item takes the second's place (below
        /// it when it was above, above it when it was below; the maintainer's retail check, 2026-10-11). The order is the
        /// client's (LandSandBoat has no packet to reorder a container). The mode stays for the next pair until Cancel (the
        /// maintainer's round 1, 2026-10-12).
        /// </summary>
        private void ManualSortStep(StockUiOpenMenu list)
        {
            int index = list.EntryIndex(list.SelectedButtonId);
            if (index < 0 || index >= list.ItemRows.Count) return;
            lock (_sync)
            {
                if (list.ManualSortFrom is not { } from)
                {
                    list.ManualSortFrom = index;
                }
                else
                {
                    _itemOrder[list.ItemContainer] = StockUiItems.MoveInOrder(list.ItemRows, from, index);
                    list.ManualSortFrom = null;
                    FillItemRows(list);
                }
            }
            Changed?.Invoke();
        }

        /// <summary>A storage of the Equipment list's choice: the list shows its items for the slot, and the cursor returns to the list.</summary>
        private void ActivateEquipStorage(StockUiOpenMenu storage, UiMenuButton button)
        {
            if (!StockUiItems.TryGetEquipStorage(button.ButtonId, out var container)) return;
            if (storage.IsGreyed(button.ButtonId))
            {
                NoticePosted?.Invoke($"{StockUiItems.ContainerName(container)} is not available.");
                return;
            }
            var list = storage.Parent;
            CloseMenu(storage);
            if (list is not { ItemListKind: StockUiItemListKind.Equipment }) return;
            lock (_sync)
            {
                list.ItemContainer = container;
                if (list.Parent is { IsEquipment: true } grid)
                {
                    grid.EquipContainer = container;
                    grid.CompanionRows = StockUiItems.EquipRows(Inventory, container, null, ItemLookup, Character());
                }
                list.FirstRow = 0;
                list.ScrollFrom = 0;
                list.SelectedButtonId = 1;
                FillItemRows(list);
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// Confirm on a grid slot: the list beside the grid shows that slot's items, from the storage the worn item is in (else
        /// the storage shown), the cursor on the first item (the maintainer's retail captures, 2026-10-11).
        /// </summary>
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
            var worn = StockUiItems.EquippedItem(Inventory, slot);
            var list = new StockUiOpenMenu(definition, grid, Array.Empty<string>(), null, VisibleRowsOf(definition))
            {
                ItemListKind = StockUiItemListKind.Equipment,
                ItemEquipSlot = slot,
                ItemContainer = worn?.Container ?? grid.EquipContainer,
                ItemLookup = ItemLookup,
                ItemOwner = this,
            };
            lock (_sync)
            {
                FillItemRows(list);
                list.SelectedButtonId = list.ItemRows.Count > 1 ? 2 : 1;
                if (worn != null)
                {
                    var rows = list.ItemRows;
                    for (int i = 1; i < rows.Count && i < list.VisibleRows; i++)
                    {
                        if (rows[i].Container == worn.Container && rows[i].Slot == worn.Slot) list.SelectedButtonId = i + 1;
                    }
                }
                PushOpen(list);
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// Confirm on an Equipment list row: "Storage Options" opens the storage choice to the right; the worn item is removed
        /// (C2S 0x050 index 0; PROVISIONAL: how retail removes is not captured); another item is equipped (0x050). The list
        /// closes after a change.
        /// </summary>
        private void ActivateEquipRow(StockUiOpenMenu list, StockUiItemRow row)
        {
            var library = _library;
            if (row.IsStorageOptions)
            {
                if (library == null || !library.TryGetMenu(StockUiItems.EquipStorageMenu, out var definition)) return;
                var storage = new StockUiOpenMenu(definition, list, Array.Empty<string>(), null) { IsEquipStorageMenu = true };
                var greyed = new HashSet<int>();
                foreach (var b in definition.Buttons)
                {
                    if (StockUiItems.TryGetEquipStorage(b.ButtonId, out var c) && !StockUiItems.IsAvailable(Inventory, c)) greyed.Add(b.ButtonId);
                }
                storage.SetGreyed(greyed.Count > 0 ? greyed : null);
                int current = StockUiItems.EquipStorageButtonOf(list.ItemContainer);
                storage.SelectedButtonId = current > 0 ? current : FirstSelectable(definition);
                lock (_sync) PushOpen(storage);
                Changed?.Invoke();
                return;
            }
            if (row.Greyed)
            {
                NoticePosted?.Invoke(row.GreyedReason ?? $"{row.Name} cannot be equipped.");
                return;
            }
            var slot = list.ItemEquipSlot;
            var worn = StockUiItems.EquippedItem(Inventory, slot);
            bool remove = worn != null && worn.Container == row.Container && worn.Slot == row.Slot;
            CloseMenu(list);
            var equip = ItemEquip;
            if (equip == null)
            {
                NoticePosted?.Invoke("Equipment cannot be changed in this session.");
                return;
            }
            byte index = remove ? (byte)0 : row.Slot;
            var container = remove ? ContainerId.Inventory : row.Container;
            GordianLog.Info("ITEM", remove ? $"Removing {row.Name} from {slot}." : $"Equipping {row.Name} (item {row.ItemId}, {container} slot {row.Slot}) in {slot}.");
            _ = SendItemAsync(() => equip(index, slot, container), remove ? "Remove" : "Equip");
        }

        /// <summary>
        /// Turns the item description of the top window (Items, Equipment, Equipment Sets, synthesis, check, Treasure Pool,
        /// shop) to its next page, wrapping to the first (retail: the gamepad's X, here the main menu action, which these
        /// windows do not otherwise use). Returns whether a page turned.
        /// </summary>
        public bool NextItemPage()
        {
            bool turned = false;
            lock (_sync)
            {
                if (Top is { } top) turned = top.NextDescriptionPage();
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
