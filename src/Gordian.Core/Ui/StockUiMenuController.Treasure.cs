// src/Gordian.Core/Ui/StockUiMenuController.Treasure.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.Resources.Ui;
using Gordian.Core.World;

namespace Gordian.Core.Ui
{
    public sealed partial class StockUiOpenMenu
    {
        /// <summary>Whether this is the Treasure Pool list (<see cref="StockUiTreasurePool.ListMenu"/>).</summary>
        public bool IsTreasureList { get; init; }

        /// <summary>The Treasure Pool list's rows (the occupied pool slots), refreshed as the pool changes.</summary>
        public IReadOnlyList<StockUiTreasureRow> TreasureRows { get; internal set; } = Array.Empty<StockUiTreasureRow>();

        /// <summary>The row under the Treasure Pool list's cursor, if it shows one.</summary>
        public StockUiTreasureRow? SelectedTreasureRow
        {
            get
            {
                int index = SelectedButtonId - 1;
                var rows = TreasureRows;
                return index >= 0 && index < rows.Count ? rows[index] : null;
            }
        }

        /// <summary>The pool slot a Cast Lot / Pass window (<see cref="StockUiTreasurePool.ActionMenu"/>) is for, else null.</summary>
        public byte? TreasureActionSlot { get; init; }

        /// <summary>The "Spoils Options" window (<see cref="StockUiTreasurePool.DoneMenu"/>) with the cursor on Done.</summary>
        public bool IsTreasureDone { get; init; }

        /// <summary>A Treasure Pool window, drawn at its own place rather than following the root.</summary>
        public bool IsTreasureWindow => IsTreasureList || TreasureActionSlot != null || IsTreasureDone;

        /// <summary>The pool the Treasure Pool list shows (for the countdown).</summary>
        public TreasurePoolState? TreasurePool { get; init; }
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>The session's treasure pool: the Treasure Pool window's items, and whether the command menus offer Treasure.</summary>
        public TreasurePoolState? TreasurePool { get; set; }

        /// <summary>Sends C2S 0x041 (cast lots on a pool slot).</summary>
        public Func<byte, Task>? TreasureLot { get; set; }

        /// <summary>Sends C2S 0x042 (pass on a pool slot).</summary>
        public Func<byte, Task>? TreasurePass { get; set; }

        /// <summary>
        /// Opens the Treasure Pool list over <paramref name="parent"/> (the command menu whose Treasure entry was chosen),
        /// the cursor on the first item. Returns the list, or null when the UI or the pool is missing or the pool is empty.
        /// </summary>
        public StockUiOpenMenu? OpenTreasurePool(StockUiOpenMenu? parent)
        {
            var library = _library;
            var pool = TreasurePool;
            if (library == null || pool == null || !library.TryGetMenu(StockUiTreasurePool.ListMenu, out var definition)) return null;
            var rows = StockUiTreasurePool.Rows(pool, ItemLookup);
            if (rows.Count == 0)
            {
                NoticePosted?.Invoke("There is nothing in the treasure pool.");
                return null;
            }
            var menu = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null)
            {
                IsTreasureList = true,
                TreasurePool = pool,
                ItemLookup = ItemLookup,
            };
            menu.TreasureRows = rows;
            menu.SelectedButtonId = 1;
            lock (_sync)
            {
                var open = new StockUiOpenMenu[_open.Length + 1];
                Array.Copy(_open, open, _open.Length);
                open[^1] = menu;
                _open = open;
            }
            Changed?.Invoke();
            return menu;
        }

        /// <summary>
        /// The pool changed (S2C 0x0D2 / 0x0D3, a zone change): the open list takes the new rows, keeping the cursor on its
        /// slot; a Cast Lot / Pass window whose item is gone closes, and so does the list when the pool empties.
        /// </summary>
        public void OnTreasureChanged()
        {
            StockUiOpenMenu? closeFrom = null;
            bool changed = false;
            lock (_sync)
            {
                foreach (var menu in _open)
                {
                    if (menu.IsTreasureList)
                    {
                        byte? slot = menu.SelectedTreasureRow?.Slot;
                        var rows = StockUiTreasurePool.Rows(TreasurePool, ItemLookup);
                        if (rows.Count == 0)
                        {
                            closeFrom = menu;
                            break;
                        }
                        menu.TreasureRows = rows;
                        int index = slot is { } s ? rows.FindIndex(r => r.Slot == s) : -1;
                        menu.SelectedButtonId = index >= 0 ? index + 1 : Math.Clamp(menu.SelectedButtonId, 1, rows.Count);
                        changed = true;
                    }
                    else if (menu.TreasureActionSlot is { } actionSlot)
                    {
                        var row = TreasurePool?.GetSlot(actionSlot);
                        if (row == null)
                        {
                            closeFrom = menu;
                            break;
                        }
                        menu.SetGreyed(ActionGreyed(row));
                        changed = true;
                    }
                }
            }
            if (closeFrom != null) CloseMenu(closeFrom);
            else if (changed) Changed?.Invoke();
        }

        private static HashSet<int>? ActionGreyed(TreasureSlot slot)
        {
            var greyed = new HashSet<int>();
            if (slot.Entry != Network.Packets.TreasureEntryKind.None) greyed.Add(StockUiTreasurePool.LotButton);
            if (slot.Entry == Network.Packets.TreasureEntryKind.Pass) greyed.Add(StockUiTreasurePool.PassButton);
            return greyed.Count > 0 ? greyed : null;
        }

        /// <summary>Confirm on a list row: opens Cast Lot / Pass beside it (the row's place in layout space).</summary>
        private void ActivateTreasureRow(StockUiOpenMenu list)
        {
            var library = _library;
            if (list.SelectedTreasureRow is not { } row || library == null || !library.TryGetMenu(StockUiTreasurePool.ActionMenu, out var template)) return;
            var slot = TreasurePool?.GetSlot(row.Slot);
            if (slot == null) return;
            var button = list.SelectedButton;
            var listFrame = list.Menu.Frame;
            var frame = new UiMenuFrame
            {
                X = (short)(listFrame.X + listFrame.Width + StockUiTreasurePool.ActionWindowGap),
                Y = (short)(listFrame.Y + (button?.Y ?? 0) - 5),
                Width = template.Frame.Width,
                Height = template.Frame.Height,
                Anchor = listFrame.Anchor,
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
            var menu = new StockUiOpenMenu(definition, list, Array.Empty<string>(), null) { TreasureActionSlot = row.Slot };
            var greyed = ActionGreyed(slot);
            menu.SetGreyed(greyed);
            menu.SelectedButtonId = greyed?.Contains(StockUiTreasurePool.LotButton) == true ? StockUiTreasurePool.PassButton : StockUiTreasurePool.LotButton;
            lock (_sync)
            {
                var open = new StockUiOpenMenu[_open.Length + 1];
                Array.Copy(_open, open, _open.Length);
                open[^1] = menu;
                _open = open;
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// + on the keyboard or Y on the gamepad in the Treasure Pool (retail): from the list the cursor goes to the Spoils
        /// Options window's Done; pressed again (or Cancel) it goes back to the list. Returns whether it applied.
        /// </summary>
        public bool ToggleTreasureDone()
        {
            var library = _library;
            StockUiOpenMenu? top = Top;
            if (top is { IsTreasureDone: true })
            {
                CloseMenu(top);
                return true;
            }
            if (top is not { IsTreasureList: true } || library == null || !library.TryGetMenu(StockUiTreasurePool.DoneMenu, out var definition)) return false;
            var done = new StockUiOpenMenu(definition, top, Array.Empty<string>(), null) { IsTreasureDone = true };
            done.SelectedButtonId = StockUiTreasurePool.DoneButton;
            lock (_sync)
            {
                var open = new StockUiOpenMenu[_open.Length + 1];
                Array.Copy(_open, open, _open.Length);
                open[^1] = done;
                _open = open;
            }
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Done: passes on every item you have neither lotted nor passed on (help 922; the maintainer's retail check,
        /// 2026-10-07) and returns the cursor to the list.
        /// </summary>
        private void ActivateTreasureDone(StockUiOpenMenu done)
        {
            CloseMenu(done);
            var pass = TreasurePass;
            if (pass == null)
            {
                NoticePosted?.Invoke("The treasure pool is not available in this session.");
                return;
            }
            foreach (var slot in TreasurePool?.Snapshot() ?? Array.Empty<TreasureSlot>())
            {
                if (slot.Entry == Network.Packets.TreasureEntryKind.None) _ = SendTreasureAsync(pass, slot.Slot, lot: false);
            }
        }

        /// <summary>Confirm on Cast Lot or Pass: sends 0x041 / 0x042 and returns to the list (the 0x0D3 answer updates it).</summary>
        private void ActivateTreasureAction(StockUiOpenMenu menu, UiMenuButton button)
        {
            byte slot = menu.TreasureActionSlot!.Value;
            bool lot = button.ButtonId == StockUiTreasurePool.LotButton;
            var poolSlot = TreasurePool?.GetSlot(slot);
            string what = lot ? "Cast Lot" : "Pass";
            ushort itemId = poolSlot?.ItemId ?? 0;
            if (menu.IsGreyed(button.ButtonId))
            {
                GordianLog.Info("TREASURE", $"{what} refused in the window: slot={slot} item={itemId} reason=already {(poolSlot?.Entry == Network.Packets.TreasureEntryKind.Pass ? "passed" : "lotted")}");
                NoticePosted?.Invoke(lot ? "You have already cast lots on or passed this item." : "You have already passed on this item.");
                return;
            }
            if (poolSlot == null)
            {
                GordianLog.Info("TREASURE", $"{what} refused in the window: slot={slot} reason=not in the pool");
                CloseMenu(menu);
                return;
            }
            if (lot && TreasureLotRefusal(poolSlot) is { } refusal)
            {
                // Retail checks before sending, and LandSandBoat would ignore the lot without a word (treasure_pool.cpp).
                GordianLog.Info("TREASURE", $"Cast Lot refused in the window: slot={slot} item={itemId} reason={refusal.Reason}");
                NoticePosted?.Invoke(refusal.Message);
                return;
            }
            CloseMenu(menu);
            var send = lot ? TreasureLot : TreasurePass;
            if (send == null)
            {
                GordianLog.Info("TREASURE", $"{what} not sent: slot={slot} item={itemId} reason=no session");
                NoticePosted?.Invoke("The treasure pool is not available in this session.");
                return;
            }
            GordianLog.Info("TREASURE", $"{what} confirmed in the window: slot={slot} item={itemId}, sending");
            _ = SendTreasureAsync(send, slot, lot);
        }

        /// <summary>
        /// Why a lot on a pool item would be refused, as retail checks it before sending (null when it may go): a full
        /// inventory ("You cannot cast lots. Your inventory is full.", ROM/165/70 #125) or a Rare item the character already
        /// holds in any container but the recycle bin ("You can only hold one item of this type.", ROM/165/70 #126).
        /// LandSandBoat refuses both silently (<c>CTreasurePool::lotItem</c>, <c>charutils::HasItem</c>). The inventory
        /// counts as full only once its size is known (S2C 0x01C).
        /// </summary>
        public (string Reason, string Message)? TreasureLotRefusal(TreasureSlot slot)
        {
            ArgumentNullException.ThrowIfNull(slot);
            var inventory = Inventory;
            if (inventory == null) return null;
            var bag = inventory.GetContainer(Network.Packets.ContainerId.Inventory);
            if (bag.MaxSize > 0)
            {
                int used = 0;
                foreach (var item in inventory.SnapshotItems(Network.Packets.ContainerId.Inventory))
                {
                    if (item.Slot >= 1 && item.ItemId != 0) used++;
                }
                if (used >= bag.MaxSize) return ("inventory full", StockUiTreasurePool.InventoryFullMessage);
            }
            var record = ItemLookup?.Invoke(slot.ItemId);
            if (record != null && (record.Flags & StockUiTreasurePool.RareFlag) != 0)
            {
                for (var container = Network.Packets.ContainerId.Inventory; container < Network.Packets.ContainerId.Count; container++)
                {
                    if (container == Network.Packets.ContainerId.RecycleBin) continue;
                    foreach (var item in inventory.SnapshotItems(container))
                    {
                        if (item.ItemId == slot.ItemId) return ("rare item already held", StockUiTreasurePool.RareHeldMessage);
                    }
                }
            }
            return null;
        }

        private async Task SendTreasureAsync(Func<byte, Task> send, byte slot, bool lot)
        {
            try
            {
                await send(slot).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                GordianLog.Warning("UI", $"Treasure {(lot ? "lot" : "pass")} failed: {ex.Message}");
                NoticePosted?.Invoke($"{(lot ? "Cast Lot" : "Pass")} failed: {ex.Message}");
            }
        }
    }
}
