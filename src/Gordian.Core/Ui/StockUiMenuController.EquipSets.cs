// src/Gordian.Core/Ui/StockUiMenuController.EquipSets.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Actions;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Ui;
using Gordian.Core.World;

namespace Gordian.Core.Ui
{
    public sealed partial class StockUiOpenMenu
    {
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
        internal bool PagesAsList => IsKeyItemWindow || IsEquipSetList;
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
            if (top.IsItemList && top.EquipSetNumber > 0 && top.SelectedItemRow is { IsStorageOptions: false } row)
            {
                PlaceInSet(top, row);
                return true;
            }
            return false;
        }

        /// <summary>The options (<c>mcresmn</c>) for the set under the list's cursor, left of the list and level with the row.</summary>
        private void OpenEquipSetOptions(StockUiOpenMenu list)
        {
            var library = _library;
            int number = list.SelectedEquipSet;
            if (library == null || !EquipmentSetBook.IsValidNumber(number) || !library.TryGetMenu(StockUiEquipSets.OptionsMenu, out var template)) return;
            _lastEquipSet = number;
            var row = list.SelectedButton;
            int y = list.Menu.Frame.Y + (row?.Y ?? 0) - 6;
            y = Math.Clamp(y, 0, UiResourceLibrary.LayoutHeight - template.Frame.Height);
            var definition = new UiMenuDefinition
            {
                DatId = template.DatId,
                Category = template.Category,
                Name = template.Name,
                MenuType = template.MenuType,
                Frame = new UiMenuFrame
                {
                    X = StockUiEquipSets.OptionsX,
                    Y = (short)y,
                    Width = template.Frame.Width,
                    Height = template.Frame.Height,
                    Anchor = UiAnchor.TopLeft,
                    Shapes = template.Frame.Shapes,
                    CursorOffsetX = template.Frame.CursorOffsetX,
                    CursorOffsetY = template.Frame.CursorOffsetY,
                    HelpTextId = template.Frame.HelpTextId,
                    TitleTextId = template.Frame.TitleTextId,
                },
                Buttons = template.Buttons,
            };
            var options = new StockUiOpenMenu(definition, list, Array.Empty<string>(), null)
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

        /// <summary>Paste is greyed until a set is copied, Style Lock and Equip while the set is empty (PROVISIONAL).</summary>
        private void RefreshEquipSetOptions(StockUiOpenMenu options)
        {
            var greyed = new HashSet<int>();
            if (_copiedSet == null) greyed.Add(StockUiEquipSets.PasteButton);
            if (options.EquipSet is { IsEmpty: true })
            {
                greyed.Add(StockUiEquipSets.StyleLockButton);
                greyed.Add(StockUiEquipSets.EquipButton);
            }
            options.SetGreyed(greyed.Count > 0 ? greyed : null);
        }

        private void ActivateEquipSetOption(StockUiOpenMenu options, UiMenuButton button)
        {
            var book = options.EquipSetBook;
            int number = options.EquipSetNumber;
            if (book == null || !EquipmentSetBook.IsValidNumber(number)) return;
            var set = book.Get(number);
            if (options.IsGreyed(button.ButtonId))
            {
                NoticePosted?.Invoke(button.ButtonId == StockUiEquipSets.PasteButton ? "No set has been copied." : $"Equipment set {number} ({set.Name}) is empty.");
                return;
            }
            switch (button.ButtonId)
            {
                case StockUiEquipSets.EditButton:
                    OpenEquipSetGrid(options);
                    break;
                case StockUiEquipSets.ChangeNameButton:
                    // Retail opens its text input here; GordianXI has none for menus yet (#92 follow-up).
                    NoticePosted?.Invoke("Naming equipment sets is not available yet.");
                    break;
                case StockUiEquipSets.CopyButton:
                    _copiedSet = set;
                    RefreshEquipSetOptions(options);
                    NoticePosted?.Invoke($"Copied equipment set {number} ({set.Name}).");
                    Changed?.Invoke();
                    break;
                case StockUiEquipSets.PasteButton:
                    if (_copiedSet is { } copied)
                    {
                        book.Set(number, copied);
                        RefreshEquipSetOptions(options);
                        Changed?.Invoke();
                    }
                    break;
                case StockUiEquipSets.DeleteButton:
                    OpenYesNo(StockUiEquipSets.DeletePrompt(number, set), defaultYes: false, yes =>
                    {
                        if (yes != true) return;
                        book.Clear(number);
                        RefreshEquipSetOptions(options);
                        Changed?.Invoke();
                    });
                    break;
                case StockUiEquipSets.UseCurrentButton:
                    book.Set(number, StockUiEquipSets.FromWorn(set, Inventory));
                    RefreshEquipSetOptions(options);
                    Changed?.Invoke();
                    break;
                case StockUiEquipSets.EquipButton:
                    _ = RunEquipSetAsync(EquipSetEquip, number, "Equip");
                    break;
                case StockUiEquipSets.StyleLockButton:
                    _ = RunEquipSetAsync(EquipSetStyleLock, number, "Style Lock");
                    break;
            }
        }

        private async Task RunEquipSetAsync(Func<int, Task<PlayerActionResult>>? run, int number, string what)
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
            }
            catch (Exception ex)
            {
                GordianLog.Warning("EQUIPSET", $"{what} of set {number} failed: {ex.Message}");
                NoticePosted?.Invoke($"{what} failed: {ex.Message}");
            }
        }

        /// <summary>Edit: the set's slot grid (<c>mcresed</c>) takes the cursor, on Main.</summary>
        private void OpenEquipSetGrid(StockUiOpenMenu options)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiEquipSets.GridMenu, out var definition)) return;
            var grid = new StockUiOpenMenu(definition, options, Array.Empty<string>(), null)
            {
                IsEquipSetGrid = true,
                EquipSetBook = options.EquipSetBook,
                EquipSetNumber = options.EquipSetNumber,
                ItemLookup = ItemLookup,
                ItemOwner = this,
            };
            grid.SelectedButtonId = FirstSelectable(definition);
            lock (_sync) PushOpen(grid);
            Changed?.Invoke();
        }

        /// <summary>Confirm on a grid slot: the equipment that fits it, right of the grid (the Equipment window's list, with Storage Options).</summary>
        private void OpenEquipSetSlotList(StockUiOpenMenu grid, UiMenuButton button)
        {
            var library = _library;
            if (!StockUiCheck.TryGetSlot(button.ButtonId, out var slot) || library == null || !library.TryGetMenu(StockUiItems.ListMenu, out var template)) return;
            var frame = new UiMenuFrame
            {
                X = StockUiEquipSets.SlotListX,
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
            var current = grid.EquipSet?[slot] ?? default;
            var list = new StockUiOpenMenu(definition, grid, Array.Empty<string>(), null, VisibleRowsOf(definition))
            {
                ItemListKind = StockUiItemListKind.Equipment,
                ItemEquipSlot = slot,
                ItemContainer = current.HasItem ? current.Container : ContainerId.Inventory,
                ItemLookup = ItemLookup,
                ItemOwner = this,
                EquipSetNumber = grid.EquipSetNumber,
                EquipSetBook = grid.EquipSetBook,
            };
            lock (_sync)
            {
                FillItemRows(list);
                list.SelectedButtonId = list.ItemRows.Count > 1 ? 2 : 1;
                if (current.HasItem)
                {
                    var rows = list.ItemRows;
                    for (int i = 1; i < rows.Count && i < list.VisibleRows; i++)
                    {
                        if (rows[i].Container == current.Container && rows[i].Slot == current.Index && rows[i].ItemId == current.ItemId) list.SelectedButtonId = i + 1;
                    }
                }
                PushOpen(list);
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// Confirm on an item in a set slot's list: the item goes into the set (the one already there is taken out instead),
        /// the list closes, and C2S 0x052 asks the server to check the set; its answer (S2C 0x116) can take out pieces that
        /// do not go together (a two-handed weapon and a shield, ...). Items the main job cannot wear are allowed (retail's
        /// sets are not checked against the job; LandSandBoat checks ownership only when the set is used).
        /// </summary>
        private void PlaceInSet(StockUiOpenMenu list, StockUiItemRow row)
        {
            var book = list.EquipSetBook;
            int number = list.EquipSetNumber;
            if (book == null || !EquipmentSetBook.IsValidNumber(number)) return;
            var slot = list.ItemEquipSlot;
            var set = book.Get(number);
            var current = set[slot];
            bool remove = current.HasItem && current.ItemId == row.ItemId && current.Container == row.Container && current.Index == row.Slot;
            var piece = remove ? EquipmentSetPiece.Empty : EquipmentSetPiece.Item(row.Container, row.Slot, row.ItemId);
            CloseMenu(list);
            book.Set(number, set.WithPiece(slot, piece));
            GordianLog.Info("EQUIPSET", remove ? $"Set {number}: {slot} emptied." : $"Set {number}: {row.Name} (item {row.ItemId}, {row.Container} slot {row.Slot}) in {slot}.");
            Changed?.Invoke();
            if (remove || EquipSetCheck is not { } check) return;
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
                if (before.Pieces[i] != after.Pieces[i]) GordianLog.Info("EQUIPSET", $"Set {pending.Number}: the server's check changed {(EquipSlotId)i} to {after.Pieces[i].Kind} {after.Pieces[i].ItemId}.");
            }
            book.Set(pending.Number, after);
            Changed?.Invoke();
        }
    }
}
