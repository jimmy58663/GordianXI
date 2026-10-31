// src/Gordian.Engine/Ui/StockUiMenuController.ItemPicker.cs
using System;
using Gordian.Core.Network.Packets;

namespace Gordian.Engine.Ui
{
    public sealed partial class StockUiOpenMenu
    {
        /// <summary>What an item picker list (<see cref="StockUiItemListKind.Picker"/>) offers and does.</summary>
        public StockUiItemPicker? Picker { get; init; }

        /// <summary>The picker row a count prompt ("No. of Items") is for; null for other prompts.</summary>
        public StockUiItemRow? PickerRow { get; init; }
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>
        /// Opens an item picker over <paramref name="parent"/>: the item list (<c>inventor</c>) at layout x
        /// <paramref name="x"/>, its rows from <see cref="StockUiItemPicker.Rows"/>, the cursor on the first row. Used by the
        /// trade windows (#486); kept free of trade so the Bazaar can list items through it. Returns the list.
        /// </summary>
        public StockUiOpenMenu? OpenItemPicker(StockUiOpenMenu parent, int x, StockUiItemPicker picker)
        {
            ArgumentNullException.ThrowIfNull(picker);
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiItems.ListMenu, out var template)) return null;
            var definition = Placed(template, x, template.Frame.Y);
            var list = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null, VisibleRowsOf(definition))
            {
                ItemListKind = StockUiItemListKind.Picker,
                ItemContainer = ContainerId.Inventory,
                ItemLookup = ItemLookup,
                ItemOwner = this,
                Picker = picker,
            };
            lock (_sync)
            {
                FillItemRows(list);
                list.SelectedButtonId = 1;
                PushOpen(list);
            }
            Changed?.Invoke();
            return list;
        }

        /// <summary>
        /// Confirm on a picker row: a greyed row says why; a row of which more than one can be picked asks the count ("No. of
        /// Items", from 1 to the picker's room); otherwise one is picked at once.
        /// </summary>
        private void PickItem(StockUiOpenMenu list, StockUiItemRow row)
        {
            if (list.Picker is not { } picker) return;
            if (row.Greyed)
            {
                NoticePosted?.Invoke(row.GreyedReason ?? $"{row.Name} cannot be selected.");
                return;
            }
            uint room = picker.Room(row);
            if (room == 0)
            {
                NoticePosted?.Invoke(picker.FullText ?? $"{row.Name} cannot be selected.");
                return;
            }
            if (room == 1)
            {
                picker.Picked(row, 1);
                return;
            }
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiShop.QuantityMenu, out var definition)) return;
            var prompt = new StockUiOpenMenu(definition, list, Array.Empty<string>(), null)
            {
                IsQuantity = true,
                ItemDropRow = row,
                PickerRow = row,
                QuantityMax = room,
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

        /// <summary>Confirm on a picker's count: the count prompt closes and the picker gets the pick.</summary>
        private void RunPickerQuantity(StockUiOpenMenu prompt, StockUiItemRow row)
        {
            if (prompt.Parent is not { Picker: { } picker } list) return;
            uint count = prompt.Quantity;
            CloseMenu(prompt);
            lock (_sync) list.ShowsQuantity = false;
            if (count > 0) picker.Picked(row, count);
        }
    }
}
