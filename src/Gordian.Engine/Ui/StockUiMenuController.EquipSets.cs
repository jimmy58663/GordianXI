// src/Gordian.Engine/Ui/StockUiMenuController.EquipSets.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Engine.Actions;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Ui;
using Gordian.Core.World;

namespace Gordian.Engine.Ui
{
    /// <summary>A set name being typed with Change Name: the set and the shared text field holding the text and the caret.</summary>
    public sealed record StockUiNameEdit(int Number, StockUiTextField Field)
    {
        public string Text => Field.Text;
    }

    public sealed partial class StockUiOpenMenu
    {
        /// <summary>The set name being typed (Change Name), drawn in the set's capsule of the list; null when none.</summary>
        public StockUiNameEdit? EquipSetNameEdit => ItemOwner?.NameEdit;

        /// <summary>The Equipment Sets list (<see cref="StockUiEquipSets.ListMenu"/>).</summary>
        public bool IsEquipSetList { get; init; }

        /// <summary>A set's options (<see cref="StockUiEquipSets.OptionsMenu"/>).</summary>
        public bool IsEquipSetOptions { get; init; }

        /// <summary>A set's slot grid while it is edited (<see cref="StockUiEquipSets.GridMenu"/>).</summary>
        public bool IsEquipSetGrid { get; init; }

        public bool IsEquipSetWindow => IsEquipSetList || IsEquipSetOptions || IsEquipSetGrid;

        /// <summary>The sets the Equipment Sets windows show and edit.</summary>
        public EquipmentSetBook? EquipSetBook { get; init; }

        /// <summary>The set (1-based) the options, the grid or a slot's item list are for; 0 for the list.</summary>
        public int EquipSetNumber { get; init; }

        /// <summary>The set under the list's cursor, or the one the window is for.</summary>
        public int SelectedEquipSet => IsEquipSetList ? EntryIndex(SelectedButtonId) + 1 : EquipSetNumber;

        /// <summary>The set <see cref="SelectedEquipSet"/> as it is now; null without a book or a valid number.</summary>
        public EquipmentSet? EquipSet => EquipSetBook != null && EquipmentSetBook.IsValidNumber(SelectedEquipSet) ? EquipSetBook.Get(SelectedEquipSet) : null;

        /// <summary>Whether the set's grid is open over these options (the options then do not draw it beside them).</summary>
        public bool IsEquipSetGridOpen => ItemOwner?.HasChild(this, m => m.IsEquipSetGrid) == true;

        /// <summary>The item a set's grid button holds (0 when the slot is empty or marked "Remove piece").</summary>
        public ushort EquipSetItemIn(int buttonId) =>
            StockUiCheck.TryGetSlot(buttonId, out var slot) && EquipSet is { } set && set[slot].HasItem ? set[slot].ItemId : (ushort)0;

        /// <summary>Whether a set's grid button is marked "Remove piece".</summary>
        public bool IsEquipSetRemoved(int buttonId) =>
            StockUiCheck.TryGetSlot(buttonId, out var slot) && EquipSet is { } set && set[slot].Kind == EquipmentSetPieceKind.Remove;

        /// <summary>A list or grid that scrolls and pages as the action lists do (the end rows link to themselves).</summary>
        internal bool PagesAsList => IsKeyItemWindow || IsEquipSetList || IsAuctionList || AuctionView == StockUiAuctionView.History
            || IsBlueMagicWindow;

        /// <summary>A set slot list's storage choice (<see cref="StockUiEquipSets.StorageMenu"/>).</summary>
        public bool IsEquipSetStorage { get; init; }

        /// <summary>Whether a set's slot list is open over this grid (it then takes the cursor, and the grid draws no list or help bar).</summary>
        public bool IsEquipSetListOpen => ItemOwner?.HasChild(this, m => m.IsItemList) == true;
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>The character's equipment sets; null when the session has none.</summary>
        public Func<EquipmentSetBook?>? EquipSets { get; set; }

        /// <summary>Equips a set (<c>/equipset</c>, C2S 0x051).</summary>
        public Func<int, Task<PlayerActionResult>>? EquipSetEquip { get; set; }

        /// <summary>Locks the appearance to a set (<c>/lockstyleset</c>, C2S 0x053 Set).</summary>
        public Func<int, Task<PlayerActionResult>>? EquipSetStyleLock { get; set; }

        /// <summary>Asks the server to check a set slot's new item (C2S 0x052: the slot, the change, the rest of the set).</summary>
        public Func<EquipSlotId, EquipsetRequestItem, EquipsetRequestItem[], Task>? EquipSetCheck { get; set; }

        /// <summary>The last S2C 0x116 the client holds (to tell a new answer from the old one).</summary>
        public Func<EquipsetSlotEntry[]>? EquipSetValidation { get; set; }

        /// <summary>The set copied with Copy, for Paste (the session's; retail: "Temporarily copy set.").</summary>
        private EquipmentSet? _copiedSet;

        /// <summary>The set a C2S 0x052 was sent for and the S2C 0x116 snapshot at that time.</summary>
        private (int Number, EquipsetSlotEntry[] Before)? _pendingCheck;

        /// <summary>
        /// Opens the Equipment Sets list (<c>mcres20</c>) over <paramref name="parent"/> on the page of the set last chosen.
        /// Returns it, or null (with a notice) without the window or the sets.
        /// </summary>
        public StockUiOpenMenu? OpenEquipmentSets(StockUiOpenMenu? parent)
        {
            var library = _library;
            var book = EquipSets?.Invoke();
            if (book == null || library == null || !library.TryGetMenu(StockUiEquipSets.ListMenu, out var definition))
            {
                NoticePosted?.Invoke("Equipment sets are not available in this session.");
                return null;
            }
            var list = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null, StockUiEquipSets.VisibleRows)
            {
                IsEquipSetList = true,
                EquipSetBook = book,
                ItemLookup = ItemLookup,
                ItemOwner = this,
            };
            var rows = new List<StockUiListRow>(EquipmentSetBook.SetCount);
            for (int i = 1; i <= EquipmentSetBook.SetCount; i++) rows.Add(new StockUiListRow(i, EquipmentSet.DefaultName(i), false));
            lock (_sync)
            {
                list.Rows = rows;
                int last = Math.Clamp(_lastEquipSet, 1, EquipmentSetBook.SetCount) - 1;
                list.FirstRow = last / StockUiEquipSets.VisibleRows * StockUiEquipSets.VisibleRows;
                list.ScrollFrom = list.FirstRow;
                list.SelectedButtonId = last - list.FirstRow + 1;
                PushOpen(list);
            }
            Changed?.Invoke();
            return list;
        }

        private int _lastEquipSet = 1;

        /// <summary>The menu message table (ROM/165/72) by id ("Remove piece.", "Examine %s?", ...); set by the HUD.</summary>
        public Func<int, string?>? MenuMessageText { get; set; }

        private string MenuText(int id, string fallback) => MenuMessageText?.Invoke(id) is { Length: > 0 } text ? text : fallback;

        /// <summary>Confirm in an Equipment Sets window; false when the top menu is none of them.</summary>
        private bool TryActivateEquipSets(StockUiOpenMenu top, UiMenuButton button)
        {
            if (top.IsEquipSetList)
            {
                OpenEquipSetOptions(top);
                return true;
            }
            if (top.IsEquipSetOptions)
            {
                ActivateEquipSetOption(top, button);
                return true;
            }
            if (top.IsEquipSetGrid)
            {
                OpenEquipSetSlotList(top, button);
                return true;
            }
            if (top.IsEquipSetStorage)
            {
                ActivateEquipSetStorage(top, button);
                return true;
            }
            if (top.IsItemList && top.EquipSetNumber > 0 && top.SelectedItemRow is { } row)
            {
                if (row.IsStorageOptions) OpenEquipSetStorage(top);
                else PlaceInSet(top, row);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Left / right on the set list turn the page, wrapping from the last page to the first and back (the maintainer's
        /// round 1 note, 2026-10-13); the cursor keeps its row.
        /// </summary>
        internal static bool TurnEquipSetPage(StockUiOpenMenu list, bool forward)
        {
            int page = list.FirstRow / StockUiEquipSets.VisibleRows;
            page = (page + (forward ? 1 : -1) + EquipmentSetBook.PageCount) % EquipmentSetBook.PageCount;
            list.FirstRow = page * StockUiEquipSets.VisibleRows;
            list.ScrollFrom = list.FirstRow;
            return true;
        }

        /// <summary>
        /// The options (<c>mcresmn</c>) for the set under the list's cursor: its top level with the chosen set's capsule top, its left edge
        /// 223 px from the screen's right (#385, retail capture on local LSB 2026-11-08, <c>385-equipset-options-set010.png</c>: Set010's
        /// capsule and the window's top both at y 198, the window from x 2337 at 2560 wide, UI 1:1).
        /// </summary>
        private void OpenEquipSetOptions(StockUiOpenMenu list)
        {
            var library = _library;
            int number = list.SelectedEquipSet;
            if (library == null || !EquipmentSetBook.IsValidNumber(number) || !library.TryGetMenu(StockUiEquipSets.OptionsMenu, out var template)) return;
            _lastEquipSet = number;
            var row = list.SelectedButton;
            int y = list.Menu.Frame.Y + (row?.Y ?? 0);
            y = Math.Clamp(y, 0, UiResourceLibrary.LayoutHeight - template.Frame.Height);
            var options = new StockUiOpenMenu(Placed(template, StockUiEquipSets.OptionsX, y, UiAnchor.TopRight), list, Array.Empty<string>(), null)
            {
                IsEquipSetOptions = true,
                EquipSetBook = list.EquipSetBook,
                EquipSetNumber = number,
                ItemLookup = ItemLookup,
                ItemOwner = this,
            };
            RefreshEquipSetOptions(options);
            options.SelectedButtonId = StockUiEquipSets.EditButton;
            lock (_sync) PushOpen(options);
            Changed?.Invoke();
        }

        /// <summary>A DAT window moved to (<paramref name="x"/>, <paramref name="y"/>) in the 512 x 448 layout, with its anchor.</summary>
        private static UiMenuDefinition Placed(UiMenuDefinition template, int x, int y, UiAnchor anchor = UiAnchor.TopLeft) => new()
        {
            DatId = template.DatId,
            Category = template.Category,
            Name = template.Name,
            MenuType = template.MenuType,
            Frame = new UiMenuFrame
            {
                X = (short)x,
                Y = (short)y,
                Width = template.Frame.Width,
                Height = template.Frame.Height,
                Anchor = anchor,
                Shapes = template.Frame.Shapes,
                CursorOffsetX = template.Frame.CursorOffsetX,
                CursorOffsetY = template.Frame.CursorOffsetY,
                HelpTextId = template.Frame.HelpTextId,
                TitleTextId = template.Frame.TitleTextId,
            },
            Buttons = template.Buttons,
        };

        /// <summary>
        /// Paste is greyed until a set is copied, and nothing else is: retail leaves Copy, Delete, Use Current, Equip and Style Lock
        /// white on an empty set, and Equip on one works (#385, retail capture on local LSB 2026-11-08, <c>385-equipset-options-empty-set.png</c>).
        /// </summary>
        private void RefreshEquipSetOptions(StockUiOpenMenu options) =>
            options.SetGreyed(_copiedSet == null ? new HashSet<int> { StockUiEquipSets.PasteButton } : null);

        private void ActivateEquipSetOption(StockUiOpenMenu options, UiMenuButton button)
        {
            var book = options.EquipSetBook;
            int number = options.EquipSetNumber;
            if (book == null || !EquipmentSetBook.IsValidNumber(number)) return;
            var set = book.Get(number);
            if (options.IsGreyed(button.ButtonId))
            {
                NoticePosted?.Invoke("No set has been copied.");
                return;
            }
            // The prompts start on No, are written upright from the window's top left and wrap there; Esc goes back to the set list,
            // closing the options too (#385, retail capture on local LSB 2026-11-08, 385-equipset-*-prompt.png).
            void Confirm(int textId, Action yes)
            {
                var prompt = OpenYesNo(MenuText(textId, StockUiEquipSets.FallbackText(textId)), defaultYes: false, answer =>
                {
                    if (answer == null)
                    {
                        CloseMenu(options);
                        return;
                    }
                    if (answer != true) return;
                    yes();
                    RefreshEquipSetOptions(options);
                    Changed?.Invoke();
                });
                if (prompt != null) prompt.PromptTextLeft = true;
            }
            switch (button.ButtonId)
            {
                case StockUiEquipSets.EditButton:
                    OpenEquipSetGrid(options);
                    break;
                case StockUiEquipSets.ChangeNameButton:
                    BeginNameEdit(options, number, set);
                    break;
                case StockUiEquipSets.CopyButton:
                    // Retail goes back to the set list with no message (#385, 2026-11-08 capture).
                    _copiedSet = set;
                    CloseMenu(options);
                    break;
                case StockUiEquipSets.PasteButton:
                    if (_copiedSet is { } copied) Confirm(StockUiEquipSets.PastePromptText, () => book.Set(number, copied));
                    break;
                case StockUiEquipSets.DeleteButton:
                    Confirm(StockUiEquipSets.DeletePromptText, () => book.Clear(number));
                    break;
                case StockUiEquipSets.UseCurrentButton:
                    Confirm(StockUiEquipSets.UseCurrentPromptText, () => book.Set(number, StockUiEquipSets.FromWorn(book.Get(number), Inventory)));
                    break;
                case StockUiEquipSets.EquipButton:
                    // Retail logs "Set #2 equipped." for Equip in this menu, even on an empty set (#385, capture on local LSB
                    // 2026-11-08); LandSandBoat sends nothing for C2S 0x051, so the line is the client's. Its colour was not
                    // captured: system white here (PROVISIONAL). An empty set changes nothing, so nothing is sent for it.
                    if (set.IsEmpty) PostTinted(EquippedText(number), StockUiFontColors.FixedColor(ChatLogChannel.System));
                    else _ = RunEquipSetAsync(EquipSetEquip, number, "Equip", EquippedText(number));
                    break;
                case StockUiEquipSets.StyleLockButton:
                    _ = RunEquipSetAsync(EquipSetStyleLock, number, "Style Lock");
                    break;
            }
        }

        private volatile StockUiNameEdit? _nameEdit;
        private volatile bool _namingSet;

        /// <summary>The set name being typed (Change Name), or null.</summary>
        public StockUiNameEdit? NameEdit => _nameEdit;

        /// <summary>Whether Change Name is taking typed text: the viewport feeds the shared <see cref="MacroField"/> the keyboard.</summary>
        public bool IsEditingName => _nameEdit != null && MacroField.IsOpen && Array.Exists(_open, m => m.IsEquipSetList);

        /// <summary>
        /// Change Name (maintainer retail capture 2026-10-13, image-16): the options close and the set's own capsule in the list
        /// becomes the text field, starting with the set's name (at most <see cref="EquipmentSet.MaxNameLength"/> printable
        /// ASCII characters, what the retail file can hold). It is the shared <see cref="StockUiTextField"/> (#400): caret,
        /// Left / Right / Home / End / Delete and paste; Enter keeps the name (its default when left empty), Escape and the
        /// gamepad's cancel leave it as it was (in-game B33), and Up / Down do nothing.
        /// </summary>
        private void BeginNameEdit(StockUiOpenMenu options, int number, EquipmentSet set)
        {
            CloseMenu(options);
            if (MacroField.IsOpen) MacroField.Commit();
            _nameEdit = new StockUiNameEdit(number, MacroField);
            _namingSet = true;
            MacroField.Open(set.Name, EquipmentSet.MaxNameLength, (typed, _) =>
            {
                if (EquipSets?.Invoke() is { } book && EquipmentSetBook.IsValidNumber(number))
                {
                    string name = typed.Trim();
                    book.Set(number, book.Get(number).WithName(name.Length > 0 ? name : EquipmentSet.DefaultName(number)));
                }
            }, escapeKeepsText: false, verticalMoves: false);
            MacroField.OpenChanged += ClearOnClose;
            void ClearOnClose(bool open)
            {
                if (open) return;
                MacroField.OpenChanged -= ClearOnClose;
                _nameEdit = null;
                _namingSet = false;
                Changed?.Invoke();
            }
            Changed?.Invoke();
        }

        /// <summary>The line retail logs for the options' Equip (#385).</summary>
        internal static string EquippedText(int number) => $"Set #{number} equipped.";

        private async Task RunEquipSetAsync(Func<int, Task<PlayerActionResult>>? run, int number, string what, string? done = null)
        {
            if (run == null)
            {
                NoticePosted?.Invoke($"{what}: not available in this session.");
                return;
            }
            try
            {
                var result = await run(number).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(result.Message)) NoticePosted?.Invoke(result.Message);
                if (done != null && result.Success) PostTinted(done, StockUiFontColors.FixedColor(ChatLogChannel.System));
            }
            catch (Exception ex)
            {
                GordianLog.Warn(LogCategories.Equipset, $"{what} of set {number} failed: {ex.Message}");
                NoticePosted?.Invoke($"{what} failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Edit (image-12): the set's slot grid (<c>mcresed</c>) takes the cursor, on Main, with the storage's equipment right of
        /// it (the storage the set's first item is in, else the inventory) and the slot's item under it.
        /// </summary>
        private void OpenEquipSetGrid(StockUiOpenMenu options)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiEquipSets.GridMenu, out var definition)) return;
            var container = ContainerId.Inventory;
            if (options.EquipSet is { } set)
            {
                foreach (var piece in set.Pieces)
                {
                    if (!piece.HasItem) continue;
                    container = piece.Container;
                    break;
                }
            }
            var grid = new StockUiOpenMenu(definition, options, Array.Empty<string>(), null)
            {
                IsEquipSetGrid = true,
                EquipSetBook = options.EquipSetBook,
                EquipSetNumber = options.EquipSetNumber,
                ItemLookup = ItemLookup,
                ItemOwner = this,
            };
            grid.EquipContainer = container;
            grid.SelectedButtonId = FirstSelectable(definition);
            lock (_sync)
            {
                grid.CompanionRows = StockUiEquipSets.SetRows(Inventory, container, null, ItemLookup, MenuMessageText);
                PushOpen(grid);
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// Confirm on a grid slot: the list right of the grid takes the cursor with that slot's equipment (Storage Options,
        /// Remove piece., Keep same piece., then the items), from the storage the set's item is in, else the storage shown;
        /// the cursor on the set's item, else on the first item, else on "Remove piece.".
        /// </summary>
        private void OpenEquipSetSlotList(StockUiOpenMenu grid, UiMenuButton button)
        {
            var library = _library;
            if (!StockUiCheck.TryGetSlot(button.ButtonId, out var slot) || library == null || !library.TryGetMenu(StockUiItems.ListMenu, out var template)) return;
            var current = grid.EquipSet?[slot] ?? default;
            var definition = Placed(template, StockUiEquipSets.SlotListX, template.Frame.Y);
            var list = new StockUiOpenMenu(definition, grid, Array.Empty<string>(), null, VisibleRowsOf(definition))
            {
                ItemListKind = StockUiItemListKind.Equipment,
                ItemEquipSlot = slot,
                ItemLookup = ItemLookup,
                ItemOwner = this,
                EquipSetNumber = grid.EquipSetNumber,
                EquipSetBook = grid.EquipSetBook,
            };
            list.ItemContainer = current.HasItem ? current.Container : grid.EquipContainer;
            lock (_sync)
            {
                FillItemRows(list);
                var rows = list.ItemRows;
                int select = rows.Count > 3 ? 3 : 1;
                for (int i = 3; i < rows.Count && current.HasItem; i++)
                {
                    if (rows[i].Container == current.Container && rows[i].Slot == current.Index && rows[i].ItemId == current.ItemId) select = i;
                }
                list.FirstRow = Math.Clamp(select - list.VisibleRows + 1, 0, Math.Max(0, rows.Count - list.VisibleRows));
                list.ScrollFrom = list.FirstRow;
                list.SelectedButtonId = select - list.FirstRow + 1;
                PushOpen(list);
            }
            Changed?.Invoke();
        }

        /// <summary>Storage Options in a set's list (image-14): <c>mcrmogc</c> at the top right; storages without slots greyed.</summary>
        private void OpenEquipSetStorage(StockUiOpenMenu list)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiEquipSets.StorageMenu, out var definition)) return;
            var storage = new StockUiOpenMenu(definition, list, Array.Empty<string>(), null) { IsEquipSetStorage = true };
            var greyed = new HashSet<int>();
            foreach (var b in definition.Buttons)
            {
                if (StockUiEquipSets.TryGetStorage(b.ButtonId, out var c) && !StockUiItems.IsAvailable(Inventory, c)) greyed.Add(b.ButtonId);
            }
            storage.SetGreyed(greyed.Count > 0 ? greyed : null);
            int at = StockUiEquipSets.StorageButtonOf(list.ItemContainer);
            storage.SelectedButtonId = at > 0 ? at : FirstSelectable(definition);
            lock (_sync) PushOpen(storage);
            Changed?.Invoke();
        }

        /// <summary>A storage chosen: the list (and the grid's list) show its equipment, the cursor back on the list.</summary>
        private void ActivateEquipSetStorage(StockUiOpenMenu storage, UiMenuButton button)
        {
            if (!StockUiEquipSets.TryGetStorage(button.ButtonId, out var container)) return;
            if (storage.IsGreyed(button.ButtonId))
            {
                NoticePosted?.Invoke($"{StockUiItems.ContainerName(container)} is not available.");
                return;
            }
            var list = storage.Parent;
            CloseMenu(storage);
            if (list is not { IsItemList: true }) return;
            lock (_sync)
            {
                list.ItemContainer = container;
                if (list.Parent is { IsEquipSetGrid: true } grid)
                {
                    grid.EquipContainer = container;
                    grid.CompanionRows = StockUiEquipSets.SetRows(Inventory, container, null, ItemLookup, MenuMessageText);
                }
                list.FirstRow = 0;
                list.ScrollFrom = 0;
                list.SelectedButtonId = 1;
                FillItemRows(list);
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// Confirm in a set slot's list: "Remove piece." marks the slot (red X), "Keep same piece." empties it, an item goes in
        /// (the maintainer's notes and image-13, 2026-10-13). The list closes. An item or a Remove mark is checked with C2S
        /// 0x052 (the change and the rest of the set); the server's S2C 0x116 can take out pieces that do not go together (a
        /// two-handed weapon and a shield, ...). "Keep same piece." sends nothing: LandSandBoat answers a change without an item
        /// as "Remove piece". Items the main job cannot wear are allowed (LandSandBoat checks the set only when it is used).
        /// </summary>
        private void PlaceInSet(StockUiOpenMenu list, StockUiItemRow row)
        {
            var book = list.EquipSetBook;
            int number = list.EquipSetNumber;
            if (book == null || !EquipmentSetBook.IsValidNumber(number)) return;
            var slot = list.ItemEquipSlot;
            var set = book.Get(number);
            var piece = row.SetAction switch
            {
                StockUiSetRowAction.RemovePiece => EquipmentSetPiece.Remove,
                StockUiSetRowAction.KeepSamePiece => EquipmentSetPiece.Empty,
                _ => EquipmentSetPiece.Item(row.Container, row.Slot, row.ItemId),
            };
            CloseMenu(list);
            book.Set(number, set.WithPiece(slot, piece));
            GordianLog.Info(LogCategories.Equipset, $"Set {number}: {slot} = {piece.Kind}{(piece.HasItem ? $" {row.Name} (item {row.ItemId}, {row.Container} slot {row.Slot})" : string.Empty)}.");
            Changed?.Invoke();
            if (piece.Kind == EquipmentSetPieceKind.Empty || EquipSetCheck is not { } check) return;
            lock (_sync) _pendingCheck = (number, EquipSetValidation?.Invoke() ?? Array.Empty<EquipsetSlotEntry>());
            _ = SendItemAsync(() => check(slot, piece.ToRequestItem(), StockUiEquipSets.CheckEntries(set, slot)), "Equipment set check");
        }

        /// <summary>
        /// The server answered an equipment set check (S2C 0x116): the set the check was sent for takes the server's slots.
        /// Ignored when no check is pending or the answer is the one held before the check.
        /// </summary>
        public void OnEquipSetValidated(EquipsetSlotEntry[] entries)
        {
            (int Number, EquipsetSlotEntry[] Before) pending;
            lock (_sync)
            {
                if (_pendingCheck is not { } p || entries.Length == 0 || entries.AsSpan().SequenceEqual(p.Before)) return;
                pending = p;
                _pendingCheck = null;
            }
            var book = EquipSets?.Invoke();
            if (book == null) return;
            var before = book.Get(pending.Number);
            var after = StockUiEquipSets.ApplyValidation(before, entries);
            for (int i = 0; i < EquipmentSet.SlotCount; i++)
            {
                if (before.Pieces[i] != after.Pieces[i]) GordianLog.Info(LogCategories.Equipset, $"Set {pending.Number}: the server's check changed {(EquipSlotId)i} to {after.Pieces[i].Kind} {after.Pieces[i].ItemId}.");
            }
            book.Set(pending.Number, after);
            Changed?.Invoke();
        }
    }
}

