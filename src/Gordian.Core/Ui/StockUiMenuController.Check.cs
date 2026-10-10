// src/Gordian.Core/Ui/StockUiMenuController.Check.cs
using System;
using System.Collections.Generic;
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

        private int _checkPage;

        /// <summary>
        /// The description page shown for the item under a check window's cursor (0 = the first); it goes back to the first
        /// page whenever the cursor moves.
        /// </summary>
        public int CheckPage
        {
            get => _checkPage;
            internal set => _checkPage = value;
        }

        /// <summary>The description pages of the item under a check window's cursor (empty off an item or without its record).</summary>
        public IReadOnlyList<StockUiItemDescriptionPage> SelectedCheckPages =>
            SelectedCheckItem is var id && id != 0 && ItemLookup?.Invoke(id) is { } record
                ? StockUiItemDescription.Pages(record)
                : Array.Empty<StockUiItemDescriptionPage>();
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>The client's job name by job id (ROM/165/86), for the check window's help bar; set by the HUD.</summary>
        public Func<byte, string?>? JobName { get; set; }

        /// <summary>Whether a check window is open (the target cannot change meanwhile, as in retail).</summary>
        public bool IsCheckOpen
        {
            get
            {
                foreach (var menu in _open)
                {
                    if (menu.IsCheck) return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Opens the check window for a checked player (S2C 0x0C9's general block, #64), closing the menus that were open
        /// (the command menu that sent the check has closed already; a pinned window stays under it). The cursor starts on
        /// View Wares, where the bazaar message shows (retail screenshot, 2026-10-07); View Wares is greyed, though the
        /// cursor can still rest on it, when the character has nothing for sale. Returns the window, or null when the UI is
        /// not loaded.
        /// </summary>
        public StockUiOpenMenu? OpenCheck(StockUiCheckData data)
        {
            ArgumentNullException.ThrowIfNull(data);
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiCheck.MenuName, out var definition)) return null;
            CloseAll();
            data = data with { JobText = StockUiCheck.FormatJobs(data.Info, JobName) };
            StockUiOpenMenu menu;
            lock (_sync)
            {
                var parent = _open.Length > 0 ? _open[^1] : null;
                menu = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null) { Check = data, ItemLookup = ItemLookup };
                menu.SelectedButtonId = definition.FindButton(StockUiCheck.ViewWaresButton) != null ? StockUiCheck.ViewWaresButton : FirstSelectable(definition);
                menu.SetGreyed(data.HasBazaar ? null : new HashSet<int> { StockUiCheck.ViewWaresButton });
                var open = new StockUiOpenMenu[_open.Length + 1];
                Array.Copy(_open, open, _open.Length);
                open[^1] = menu;
                _open = open;
            }
            Changed?.Invoke();
            return menu;
        }

        /// <summary>
        /// Turns the description of the item under the check window's cursor to its next page, wrapping to the first
        /// (retail: the gamepad's X, here the main menu action, which the check window does not otherwise use). Returns
        /// whether a page turned.
        /// </summary>
        public bool NextCheckPage()
        {
            bool turned = false;
            lock (_sync)
            {
                if (Top is { IsCheck: true } top)
                {
                    int count = top.SelectedCheckPages.Count;
                    if (count > 1)
                    {
                        top.CheckPage = (top.CheckPage + 1) % count;
                        turned = true;
                    }
                }
            }
            if (turned) Changed?.Invoke();
            return turned;
        }

        /// <summary>
        /// Confirm in the check window: View Wares would open the character's bazaar, which GordianXI cannot show yet (a
        /// greyed View Wares, nothing for sale, does nothing); a slot does nothing.
        /// </summary>
        private void ActivateCheck(StockUiOpenMenu menu, UiMenuButton button)
        {
            if (button.ButtonId != StockUiCheck.ViewWaresButton || menu.IsGreyed(button.ButtonId)) return;
            NoticePosted?.Invoke("View Wares is not available yet.");
        }
    }
}
