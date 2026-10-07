// src/Gordian.Core/Ui/StockUiMenuController.Check.cs
using System;
using Gordian.Core.Resources.Ui;

namespace Gordian.Core.Ui
{
    public sealed partial class StockUiOpenMenu
    {
        /// <summary>The checked player a check window (<see cref="StockUiCheck.MenuName"/>) shows, or null for other menus.</summary>
        public StockUiCheckData? Check { get; init; }

        public bool IsCheck => Check != null;

        /// <summary>The item in the slot under the cursor of a check window (0 when the cursor is on an empty slot or View Wares).</summary>
        public ushort SelectedCheckItem =>
            Check is { } check && StockUiCheck.TryGetSlot(SelectedButtonId, out var slot) ? check.Info.ItemIn(slot) : (ushort)0;
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>
        /// Opens the check window for a checked player (S2C 0x0C9's general block, #64), closing the menus that were open
        /// (the command menu that sent the check has closed already; a pinned window stays under it). The cursor starts on
        /// the first slot. Returns the window, or null when the UI is not loaded.
        /// </summary>
        public StockUiOpenMenu? OpenCheck(StockUiCheckData data)
        {
            ArgumentNullException.ThrowIfNull(data);
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiCheck.MenuName, out var definition)) return null;
            CloseAll();
            StockUiOpenMenu menu;
            lock (_sync)
            {
                var parent = _open.Length > 0 ? _open[^1] : null;
                menu = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null) { Check = data, ItemLookup = ItemLookup };
                menu.SelectedButtonId = FirstSelectable(definition);
                menu.SetGreyed(data.HasBazaar ? null : new System.Collections.Generic.HashSet<int> { StockUiCheck.ViewWaresButton });
                var open = new StockUiOpenMenu[_open.Length + 1];
                Array.Copy(_open, open, _open.Length);
                open[^1] = menu;
                _open = open;
            }
            Changed?.Invoke();
            return menu;
        }

        /// <summary>
        /// Confirm in the check window: View Wares would open the character's bazaar, which GordianXI cannot show yet; a
        /// slot does nothing (its item is described under the window while the cursor is on it).
        /// </summary>
        private void ActivateCheck(StockUiOpenMenu menu, UiMenuButton button)
        {
            if (button.ButtonId != StockUiCheck.ViewWaresButton) return;
            NoticePosted?.Invoke(menu.IsGreyed(button.ButtonId)
                ? $"{menu.Check!.Name} has no bazaar."
                : "View Wares is not available yet.");
        }
    }
}
