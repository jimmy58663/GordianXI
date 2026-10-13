// src/Gordian.Core/Ui/StockUiMenuController.KeyItems.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Diagnostics;
using Gordian.Core.Resources.Ui;

namespace Gordian.Core.Ui
{
    public sealed partial class StockUiOpenMenu
    {
        /// <summary>The Key Items window's category list (<see cref="StockUiKeyItems"/>).</summary>
        public bool IsKeyItemCategories { get; init; }

        /// <summary>The category a Key Items list shows; null for other menus.</summary>
        public StockUiKeyItemCategory? KeyItemCategory { get; init; }

        /// <summary>The key item table the Key Items windows read names and descriptions from.</summary>
        public StockUiKeyItemCatalog? KeyItemCatalog { get; init; }

        public bool IsKeyItemList => KeyItemCategory != null;

        public bool IsKeyItemWindow => IsKeyItemCategories || IsKeyItemList;

        /// <summary>The categories the category list shows, parallel to <see cref="Rows"/>.</summary>
        public IReadOnlyList<StockUiKeyItemCategory> KeyItemCategories { get; internal set; } = Array.Empty<StockUiKeyItemCategory>();

        /// <summary>Entry indexes of the categories with nothing held (drawn greyed; Confirm posts a notice).</summary>
        public IReadOnlySet<int> KeyItemEmptyCategories { get; internal set; } = new HashSet<int>();

        /// <summary>Whether the category list's entry <paramref name="index"/> has no key item held.</summary>
        public bool IsKeyItemCategoryEmpty(int index) => KeyItemEmptyCategories.Contains(index);

        /// <summary>A Key Items list's rows, parallel to <see cref="Rows"/>.</summary>
        public IReadOnlyList<StockUiKeyItemRow> KeyItemRows { get; internal set; } = Array.Empty<StockUiKeyItemRow>();

        /// <summary>The key item under a Key Items list's cursor, if any.</summary>
        public StockUiKeyItemRow? SelectedKeyItem
        {
            get
            {
                int index = EntryIndex(SelectedButtonId);
                var rows = KeyItemRows;
                return index >= 0 && index < rows.Count ? rows[index] : null;
            }
        }

        /// <summary>The selected key item's name and description lines for the description window (empty without one).</summary>
        public IReadOnlyList<string> KeyItemDescriptionLines
        {
            get
            {
                if (SelectedKeyItem is not { } row || KeyItemCatalog is not { } catalog) return Array.Empty<string>();
                var lines = new List<string> { row.Name };
                lines.AddRange(StockUiKeyItems.Wrap(catalog.Description(row.Id), maxLines: StockUiItemDescription.MaxLines - 1));
                return lines;
            }
        }
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>The key item table (names, descriptions, categories); null without the game's DATs. Set by the HUD.</summary>
        public Func<StockUiKeyItemCatalog?>? KeyItemCatalog { get; set; }

        /// <summary>Whether the character holds a key item (S2C 0x055's tables).</summary>
        public Func<ushort, bool>? KeyItemHeld { get; set; }

        /// <summary>Whether a held key item has been read (its "seen" bit in S2C 0x055).</summary>
        public Func<ushort, bool>? KeyItemSeen { get; set; }

        /// <summary>A new key item was viewed in the Key Items list: mark it read (C2S 0x064).</summary>
        public Action<ushort>? KeyItemViewed { get; set; }

        private readonly HashSet<ushort> _keyItemsReported = new();
        private bool _keyItemWatch;

        /// <summary>
        /// Opens the Key Items window (#91) over <paramref name="parent"/>: the categories with the number held in each. Returns
        /// it, or null (with a notice) when the window or the key item table is not available.
        /// </summary>
        public StockUiOpenMenu? OpenKeyItems(StockUiOpenMenu? parent)
        {
            var catalog = KeyItemCatalog?.Invoke();
            var definition = KeyItemWindowDefinition();
            if (catalog == null || definition == null)
            {
                NoticePosted?.Invoke("Key Items: the key item table is not loaded in this session.");
                return null;
            }
            var menu = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null, StockUiKeyItems.VisibleRows)
            {
                IsKeyItemCategories = true,
                KeyItemCatalog = catalog,
                ItemOwner = this,
            };
            lock (_sync)
            {
                FillKeyItemCategories(menu);
                menu.SelectedButtonId = menu.Rows.Count > 0 ? 1 : 0;
                PushOpen(menu);
            }
            WatchKeyItems();
            Changed?.Invoke();
            return menu;
        }

        /// <summary>
        /// The Key Items window's frame: <c>evitem</c>'s (16,48, 366 x 248, title "Key Items") with list rows of the quest
        /// log's pitch (<c>quset</c>: 16 px), placed 40 px in as <c>evitem</c>'s own rows so the cursor arrow sits inside the
        /// window. The end rows link to themselves, so up / down past them scroll and left / right page.
        /// </summary>
        private UiMenuDefinition? KeyItemWindowDefinition()
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiKeyItems.WindowMenu, out var window)) return null;
            const int RowX = 40, RowY = 6, RowPitch = 16, RowWidth = 312, RowHeight = 16;
            var buttons = new List<UiMenuButton>(StockUiKeyItems.VisibleRows);
            for (int i = 1; i <= StockUiKeyItems.VisibleRows; i++)
            {
                buttons.Add(new UiMenuButton
                {
                    ButtonId = (short)i,
                    X = RowX,
                    Y = (short)(RowY + (i - 1) * RowPitch),
                    Width = RowWidth,
                    Height = RowHeight,
                    NavUp = (sbyte)(i == 1 ? 1 : i - 1),
                    NavDown = (sbyte)(i == StockUiKeyItems.VisibleRows ? i : i + 1),
                    NavLeft = (sbyte)i,
                    NavRight = (sbyte)i,
                });
            }
            return new UiMenuDefinition
            {
                DatId = window.DatId,
                Category = window.Category,
                Name = window.Name,
                MenuType = window.MenuType,
                Frame = window.Frame,
                Buttons = buttons,
            };
        }

        private void FillKeyItemCategories(StockUiOpenMenu menu)
        {
            var catalog = menu.KeyItemCatalog;
            if (catalog == null) return;
            Func<ushort, bool> held = KeyItemHeld ?? (_ => false), seen = KeyItemSeen ?? (_ => true);
            var rows = new List<StockUiListRow>(catalog.Categories.Count);
            var greyed = new HashSet<int>();
            foreach (var category in catalog.Categories)
            {
                var (count, fresh) = StockUiKeyItems.Count(category, held, seen);
                int button = rows.Count + 1;
                rows.Add(new StockUiListRow(button, category.Name, fresh > 0, null,
                    count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                if (count == 0) greyed.Add(rows.Count - 1);
            }
            menu.KeyItemCategories = catalog.Categories;
            menu.Rows = rows;
            menu.EmptyText = null;
            menu.KeyItemEmptyCategories = greyed;
        }

        private StockUiOpenMenu? OpenKeyItemList(StockUiOpenMenu categories, StockUiKeyItemCategory category)
        {
            var definition = KeyItemWindowDefinition();
            if (definition == null) return null;
            var list = new StockUiOpenMenu(definition, categories, Array.Empty<string>(), null, StockUiKeyItems.VisibleRows)
            {
                KeyItemCategory = category,
                KeyItemCatalog = categories.KeyItemCatalog,
                ItemOwner = this,
            };
            lock (_sync)
            {
                FillKeyItemRows(list);
                list.SelectedButtonId = list.Rows.Count > 0 ? 1 : 0;
                PushOpen(list);
            }
            Changed?.Invoke();
            return list;
        }

        private void FillKeyItemRows(StockUiOpenMenu list)
        {
            if (list.KeyItemCategory is not { } category || list.KeyItemCatalog is not { } catalog) return;
            ushort? before = list.SelectedKeyItem?.Id;
            var rows = StockUiKeyItems.Rows(catalog, category, KeyItemHeld ?? (_ => false), KeyItemSeen ?? (_ => true));
            var listRows = new List<StockUiListRow>(rows.Count);
            foreach (var row in rows) listRows.Add(new StockUiListRow(listRows.Count + 1, row.Name, row.IsNew));
            list.KeyItemRows = rows;
            list.Rows = listRows;
            list.EmptyText = rows.Count == 0 ? "You have no key items in this category." : null;
            int maxFirst = Math.Max(0, rows.Count - list.VisibleRows);
            int index = before is { } id ? rows.FindIndex(r => r.Id == id) : -1;
            if (index >= 0 && (index < list.FirstRow || index >= list.FirstRow + list.VisibleRows)) list.FirstRow = Math.Clamp(index, 0, maxFirst);
            if (list.FirstRow > maxFirst) list.FirstRow = maxFirst;
            list.ScrollFrom = list.FirstRow;
            if (rows.Count == 0) list.SelectedButtonId = 0;
            else if (index >= 0) list.SelectedButtonId = index - list.FirstRow + 1;
            else list.SelectedButtonId = Math.Clamp(list.SelectedButtonId, 1, Math.Max(1, Math.Min(list.VisibleRows, rows.Count - list.FirstRow)));
        }

        /// <summary>The key item tables changed (S2C 0x055, a key item read): the open Key Items windows take fresh rows.</summary>
        public void RefreshKeyItems()
        {
            bool changed = false;
            lock (_sync)
            {
                foreach (var menu in _open)
                {
                    if (menu.IsKeyItemCategories) FillKeyItemCategories(menu);
                    else if (menu.IsKeyItemList) FillKeyItemRows(menu);
                    else continue;
                    changed = true;
                }
            }
            if (changed) Changed?.Invoke();
        }

        /// <summary>Confirm in a Key Items window: a category opens its list; a key item does nothing (retail shows its description).</summary>
        private bool TryActivateKeyItems(StockUiOpenMenu top)
        {
            if (top.IsKeyItemList) return true;
            if (!top.IsKeyItemCategories) return false;
            int index = top.EntryIndex(top.SelectedButtonId);
            if (index < 0 || index >= top.KeyItemCategories.Count) return true;
            var category = top.KeyItemCategories[index];
            if (top.IsKeyItemCategoryEmpty(index))
            {
                NoticePosted?.Invoke($"You have no {category.Name.ToLowerInvariant()}.");
                return true;
            }
            OpenKeyItemList(top, category);
            return true;
        }

        /// <summary>Watches the menus for a new key item coming under the cursor (once per controller).</summary>
        private void WatchKeyItems()
        {
            if (_keyItemWatch) return;
            _keyItemWatch = true;
            Changed += NoteKeyItemViewed;
        }

        /// <summary>
        /// A new key item under the Key Items list's cursor is marked read (C2S 0x064), once: retail sends it the first time an
        /// unread (yellow-bubble) key item is viewed.
        /// </summary>
        private void NoteKeyItemViewed()
        {
            if (Top is not { IsKeyItemList: true } list || list.SelectedKeyItem is not { IsNew: true } row) return;
            lock (_sync)
            {
                if (!_keyItemsReported.Add(row.Id)) return;
            }
            GordianLog.Info("KEYITEM", $"Key item {row.Id} ({row.Name}) viewed: marking it read.");
            KeyItemViewed?.Invoke(row.Id);
        }
    }
}
