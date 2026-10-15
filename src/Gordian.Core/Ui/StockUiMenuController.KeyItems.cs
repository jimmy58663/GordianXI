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

        /// <summary>The category a Key Items list, its Examine question or a description is for; null for other menus.</summary>
        public StockUiKeyItemCategory? KeyItemCategory { get; init; }

        /// <summary>The key item table the Key Items windows read names and descriptions from.</summary>
        public StockUiKeyItemCatalog? KeyItemCatalog { get; init; }

        /// <summary>The key item an Examine question (<see cref="StockUiKeyItems.PromptMenu"/>) or a description is for.</summary>
        public StockUiKeyItemRow? KeyItemShown { get; init; }

        /// <summary>The Examine question's text ("Examine job gesture: warrior?").</summary>
        public string? KeyItemQuestion { get; init; }

        /// <summary>Whether this is the "Examine ...?" question (Yes / No) for <see cref="KeyItemShown"/>.</summary>
        public bool IsKeyItemExamine { get; init; }

        public bool IsKeyItemList => KeyItemCategory != null && KeyItemShown == null;

        /// <summary>A key item's description (image-10): <see cref="KeyItemShown"/> with its text, no cursor.</summary>
        public bool IsKeyItemDescription => KeyItemShown != null && !IsKeyItemExamine;

        public bool IsKeyItemWindow => IsKeyItemCategories || KeyItemCategory != null;

        /// <summary>The categories the category list shows, parallel to <see cref="Rows"/>.</summary>
        public IReadOnlyList<StockUiKeyItemCategory> KeyItemCategories { get; internal set; } = Array.Empty<StockUiKeyItemCategory>();

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

        /// <summary>The description of <see cref="KeyItemShown"/> as the table writes it (empty without one).</summary>
        public string KeyItemDescription => KeyItemShown is { } row && KeyItemCatalog is { } catalog ? catalog.Description(row.Id) : string.Empty;
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>The key item table (names, descriptions, categories); null without the game's DATs. Set by the HUD.</summary>
        public Func<StockUiKeyItemCatalog?>? KeyItemCatalog { get; set; }

        /// <summary>Whether the character holds a key item (S2C 0x055's tables).</summary>
        public Func<ushort, bool>? KeyItemHeld { get; set; }

        /// <summary>Whether a held key item has been read (its "seen" bit in S2C 0x055).</summary>
        public Func<ushort, bool>? KeyItemSeen { get; set; }

        /// <summary>A key item was examined (Yes to "Examine ...?"): mark it read (C2S 0x064).</summary>
        public Action<ushort>? KeyItemViewed { get; set; }

        /// <summary>
        /// Opens the Key Items window (#91) over <paramref name="parent"/>: the categories. Returns it, or null (with a notice)
        /// when the window or the key item table is not available.
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
            var rows = new List<StockUiListRow>(catalog.Categories.Count);
            foreach (var category in catalog.Categories) rows.Add(new StockUiListRow(rows.Count + 1, category.Name, false));
            lock (_sync)
            {
                menu.KeyItemCategories = catalog.Categories;
                menu.Rows = rows;
                menu.SelectedButtonId = rows.Count > 0 ? 1 : 0;
                PushOpen(menu);
            }
            Changed?.Invoke();
            return menu;
        }

        /// <summary>
        /// The Key Items window: <c>evitem</c>'s frame and row places (x 40, y 13 + 29 n, 256 x 18; image-6), the end rows
        /// linked to themselves so up / down past them scroll and left / right page. With <paramref name="rows"/> false, the
        /// frame alone (the description).
        /// </summary>
        private UiMenuDefinition? KeyItemWindowDefinition(bool rows = true)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiKeyItems.WindowMenu, out var window)) return null;
            var buttons = new List<UiMenuButton>(StockUiKeyItems.VisibleRows);
            for (int i = 1; rows && i <= StockUiKeyItems.VisibleRows; i++)
            {
                buttons.Add(new UiMenuButton
                {
                    ButtonId = (short)i,
                    X = StockUiKeyItems.RowX,
                    Y = (short)(StockUiKeyItems.RowY + (i - 1) * StockUiKeyItems.RowPitch),
                    Width = StockUiKeyItems.RowWidth,
                    Height = StockUiKeyItems.RowHeight,
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
            list.EmptyText = rows.Count == 0 ? MenuText(StockUiKeyItems.NoKeyItemsText, StockUiKeyItems.NoKeyItemsFallback) : null;
            int maxFirst = Math.Max(0, rows.Count - list.VisibleRows);
            int index = before is { } id ? rows.FindIndex(r => r.Id == id) : -1;
            if (index >= 0 && (index < list.FirstRow || index >= list.FirstRow + list.VisibleRows)) list.FirstRow = Math.Clamp(index, 0, maxFirst);
            if (list.FirstRow > maxFirst) list.FirstRow = maxFirst;
            list.ScrollFrom = list.FirstRow;
            if (rows.Count == 0) list.SelectedButtonId = 0;
            else if (index >= 0) list.SelectedButtonId = index - list.FirstRow + 1;
            else list.SelectedButtonId = Math.Clamp(list.SelectedButtonId, 1, Math.Max(1, Math.Min(list.VisibleRows, rows.Count - list.FirstRow)));
        }

        /// <summary>The key item tables changed (S2C 0x055, a key item read): the open Key Items lists take fresh rows.</summary>
        public void RefreshKeyItems()
        {
            bool changed = false;
            lock (_sync)
            {
                foreach (var menu in _open)
                {
                    if (!menu.IsKeyItemList) continue;
                    FillKeyItemRows(menu);
                    changed = true;
                }
            }
            if (changed) Changed?.Invoke();
        }

        /// <summary>
        /// Confirm in a Key Items window: a category opens its list; a key item not read yet asks "Examine ...?", a read one
        /// shows its description; Yes to the question marks it read (C2S 0x064) and shows the description, No returns.
        /// </summary>
        private bool TryActivateKeyItems(StockUiOpenMenu top, UiMenuButton button)
        {
            if (!top.IsKeyItemWindow) return false;
            if (top.IsKeyItemCategories)
            {
                int index = top.EntryIndex(top.SelectedButtonId);
                if (index >= 0 && index < top.KeyItemCategories.Count) OpenKeyItemList(top, top.KeyItemCategories[index]);
            }
            else if (top.IsKeyItemList)
            {
                if (top.SelectedKeyItem is not { } row) return true;
                if (row.IsNew) OpenKeyItemExamine(top, row);
                else OpenKeyItemDescription(top, row);
            }
            else if (top.IsKeyItemExamine && top.KeyItemShown is { } shown && top.Parent is { } list)
            {
                CloseMenu(top);
                if (button.ButtonId != StockUiKeyItems.YesButton) return true;
                GordianLog.Info("KEYITEM", $"Key item {shown.Id} ({shown.Name}) examined: marking it read.");
                KeyItemViewed?.Invoke(shown.Id);
                OpenKeyItemDescription(list, shown with { IsNew = false });
            }
            return true;
        }

        /// <summary>"Examine ...?" (image-9) in <c>evitem01</c>, Yes under the cursor.</summary>
        private void OpenKeyItemExamine(StockUiOpenMenu list, StockUiKeyItemRow row)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiKeyItems.PromptMenu, out var definition)) return;
            string question = StockUiKeyItems.ExaminePrompt(MenuText(StockUiKeyItems.ExamineText, StockUiKeyItems.ExamineFallback), row.TableName.Length > 0 ? row.TableName : row.Name);
            var prompt = new StockUiOpenMenu(definition, list, Array.Empty<string>(), null)
            {
                IsKeyItemExamine = true,
                KeyItemQuestion = question,
                KeyItemShown = row,
                KeyItemCategory = list.KeyItemCategory,
                KeyItemCatalog = list.KeyItemCatalog,
                ItemOwner = this,
            };
            prompt.SelectedButtonId = StockUiKeyItems.YesButton;
            lock (_sync) PushOpen(prompt);
            Changed?.Invoke();
        }

        /// <summary>The key item's description in the same window (image-10); Cancel returns to the list.</summary>
        private void OpenKeyItemDescription(StockUiOpenMenu list, StockUiKeyItemRow row)
        {
            var definition = KeyItemWindowDefinition(rows: false);
            if (definition == null) return;
            var page = new StockUiOpenMenu(definition, list, Array.Empty<string>(), null)
            {
                KeyItemShown = row,
                KeyItemCategory = list.KeyItemCategory,
                KeyItemCatalog = list.KeyItemCatalog,
                ItemOwner = this,
            };
            lock (_sync) PushOpen(page);
            Changed?.Invoke();
        }
    }
}
