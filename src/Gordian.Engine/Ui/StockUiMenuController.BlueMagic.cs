// src/Gordian.Engine/Ui/StockUiMenuController.BlueMagic.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.Packets;
using Gordian.Engine.Actions;

namespace Gordian.Engine.Ui
{
    /// <summary>Which Set Spells window a menu is (#363).</summary>
    public enum StockUiBlueMagicView
    {
        None,

        /// <summary>The twenty set slots (<c>bluequip</c>), with the learned list, the points and the details drawn beside it.</summary>
        Slots,

        /// <summary>The learned spells (<c>bluinven</c>) while they take the cursor for a slot.</summary>
        Learned,
    }

    public sealed partial class StockUiOpenMenu
    {
        /// <summary>Which Set Spells window this is; <see cref="StockUiBlueMagicView.None"/> for other menus.</summary>
        public StockUiBlueMagicView BlueMagicView { get; init; }

        /// <summary>The set slots (the Slots window), parallel to <see cref="Rows"/> there.</summary>
        public IReadOnlyList<StockUiBlueSlotRow> BlueSlots { get; internal set; } = Array.Empty<StockUiBlueSlotRow>();

        /// <summary>The learned spells (both windows: drawn beside the slots, and the Learned window's rows).</summary>
        public IReadOnlyList<StockUiBlueSpellRow> BlueSpells { get; internal set; } = Array.Empty<StockUiBlueSpellRow>();

        /// <summary>The points in use and the most allowed.</summary>
        public (int Used, int Max) BluePoints { get; internal set; }

        /// <summary>The slot (0-19) the Learned window sets a spell into.</summary>
        public int BlueTargetSlot { get; init; } = -1;

        /// <summary>Whether the Learned window is open over the Slots window (which then leaves the learned list to it).</summary>
        public bool BlueLearnedOpen { get; internal set; }

        public bool IsBlueMagicWindow => BlueMagicView != StockUiBlueMagicView.None;

        /// <summary>The spell the details window describes: the selected slot's or learned row's; 0 for none.</summary>
        public ushort BlueDescribedSpell
        {
            get
            {
                int index = EntryIndex(SelectedButtonId);
                return BlueMagicView switch
                {
                    StockUiBlueMagicView.Slots => index >= 0 && index < BlueSlots.Count ? BlueSlots[index].SpellId : (ushort)0,
                    StockUiBlueMagicView.Learned => index >= 0 && index < BlueSpells.Count ? BlueSpells[index].SpellId : (ushort)0,
                    _ => 0,
                };
            }
        }
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>The character's Blue Mage state; null when the session has none.</summary>
        public Func<StockUiBlueMagicState?>? BlueMagicState { get; set; }

        /// <summary>The spell table and names (the action service's catalog).</summary>
        public Func<ActionCatalog?>? BlueMagicCatalog { get; set; }

        /// <summary>C2S 0x102: sets a spell (id less 512) into a slot (0-19); the flag is the support job's.</summary>
        public Func<int, byte, bool, Task>? BlueSpellSet { get; set; }

        /// <summary>C2S 0x102: removes the spell (id less 512) now in a slot (0-19).</summary>
        public Func<int, byte, bool, Task>? BlueSpellRemove { get; set; }

        /// <summary>Whether the Blue Magic list's Options window is <c>blusortw</c>, whose Set Spells works: Blue Mage is a job of yours.</summary>
        public bool CanSetBlueSpells => BlueMagicState?.Invoke() is { Level: > 0 };

        /// <summary>
        /// Set Spells (the Options window's button 3): <c>bluequip</c> takes the cursor on its first slot, the learned list,
        /// the points and the details beside it.
        /// </summary>
        public StockUiOpenMenu? OpenSetSpells(StockUiOpenMenu? parent)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiBlueMagic.SlotsMenu, out var definition)) return null;
            if (BlueMagicState?.Invoke() is not { Level: > 0 })
            {
                NoticePosted?.Invoke("You cannot set blue magic spells on your current job.");
                return null;
            }
            var menu = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null, VisibleRowsOf(definition))
            {
                BlueMagicView = StockUiBlueMagicView.Slots,
            };
            menu.SelectedButtonId = 1;
            lock (_sync)
            {
                FillBlueMagic(menu);
                PushOpen(menu);
            }
            Changed?.Invoke();
            return menu;
        }

        /// <summary>The Blue Mage data changed (S2C 0x044 after a set, a level change): the open windows take the new rows.</summary>
        public void RefreshBlueMagic()
        {
            bool changed = false;
            lock (_sync)
            {
                foreach (var menu in _open)
                {
                    if (!menu.IsBlueMagicWindow) continue;
                    FillBlueMagic(menu);
                    changed = true;
                }
            }
            if (changed) Changed?.Invoke();
        }

        /// <summary>Fills a Set Spells window's rows from the state (under the lock).</summary>
        private void FillBlueMagic(StockUiOpenMenu menu)
        {
            var state = BlueMagicState?.Invoke();
            var catalog = BlueMagicCatalog?.Invoke();
            if (state == null) return;
            menu.BluePoints = StockUiBlueMagic.PointsIn(state);
            menu.BlueSlots = StockUiBlueMagic.SlotRows(state, catalog);
            menu.BlueSpells = catalog != null ? StockUiBlueMagic.LearnedRows(state, catalog, menu.BlueTargetSlot, MenuMessageText) : Array.Empty<StockUiBlueSpellRow>();
            int count = menu.BlueMagicView == StockUiBlueMagicView.Slots ? menu.BlueSlots.Count : menu.BlueSpells.Count;
            var rows = new List<StockUiListRow>(count);
            for (int i = 0; i < count; i++)
            {
                string text = menu.BlueMagicView == StockUiBlueMagicView.Slots ? menu.BlueSlots[i].Name : menu.BlueSpells[i].Name;
                rows.Add(new StockUiListRow(i + 1, text, false));
            }
            menu.Rows = rows;
            ClampRows(menu);
        }

        /// <summary>Confirm on a Set Spells window; false when the top window is not one.</summary>
        private bool TryActivateBlueMagic(StockUiOpenMenu top)
        {
            if (top.BlueMagicView == StockUiBlueMagicView.Slots)
            {
                ActivateBlueSlot(top);
                return true;
            }
            if (top.BlueMagicView == StockUiBlueMagicView.Learned)
            {
                ActivateLearnedSpell(top);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Confirm on a slot: an empty one opens the learned list for it, a set one is emptied (C2S 0x102 with the spell in its
        /// slot and no spell to add; PROVISIONAL: what retail's Confirm on a set slot does is not captured), a locked one says so.
        /// </summary>
        private void ActivateBlueSlot(StockUiOpenMenu slots)
        {
            int index = slots.EntryIndex(slots.SelectedButtonId);
            if (index < 0 || index >= slots.BlueSlots.Count || BlueMagicState?.Invoke() is not { } state) return;
            var slot = slots.BlueSlots[index];
            if (slot.Locked)
            {
                NoticePosted?.Invoke($"Set {slot.Slot:00} opens at Blue Mage level {slot.UnlockLevel}.");
                return;
            }
            if (slot.SpellId != 0)
            {
                Remove(state, index, slot.SpellId, slot.Name);
                return;
            }
            OpenLearned(slots, index);
        }

        private void OpenLearned(StockUiOpenMenu slots, int targetSlot)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiBlueMagic.LearnedMenu, out var definition)) return;
            var learned = new StockUiOpenMenu(definition, slots, Array.Empty<string>(), null, VisibleRowsOf(definition))
            {
                BlueMagicView = StockUiBlueMagicView.Learned,
                BlueTargetSlot = targetSlot,
            };
            learned.SelectedButtonId = 1;
            lock (_sync)
            {
                FillBlueMagic(learned);
                // The cursor starts on the first spell that can be set, past "(Remove All)".
                int first = -1;
                for (int i = 1; i < learned.BlueSpells.Count && first < 0; i++)
                {
                    if (learned.BlueSpells[i] is { IsSet: false, GreyedReason: null }) first = i;
                }
                if (first > 0)
                {
                    learned.FirstRow = Math.Max(0, Math.Min(first, learned.Rows.Count - learned.VisibleRows));
                    learned.ScrollFrom = learned.FirstRow;
                    learned.SelectedButtonId = first - learned.FirstRow + 1;
                }
                slots.BlueLearnedOpen = true;
                PushOpen(learned);
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// Confirm on a learned spell: "(Remove All)" asks "Remove all set blue magic spells?" (ROM/165/72 #228); a set spell is
        /// removed from its slot (PROVISIONAL); a greyed one says why; any other is set into the slot and the cursor goes back to
        /// the slots, on the next empty one.
        /// </summary>
        private void ActivateLearnedSpell(StockUiOpenMenu learned)
        {
            int index = learned.EntryIndex(learned.SelectedButtonId);
            if (index < 0 || index >= learned.BlueSpells.Count || BlueMagicState?.Invoke() is not { } state) return;
            var row = learned.BlueSpells[index];
            if (row.RemoveAll)
            {
                OpenYesNo(MenuText(StockUiBlueMagic.MessageRemoveAllPrompt, "Remove all set blue magic spells?"), defaultYes: false, answer =>
                {
                    if (answer == true) _ = RemoveAllAsync(state);
                });
                return;
            }
            if (row.IsSet)
            {
                int slot = StockUiBlueMagic.SlotOf(state, row.SpellId);
                if (slot >= 0) Remove(state, slot, row.SpellId, row.Name);
                return;
            }
            if (row.GreyedReason != null)
            {
                NoticePosted?.Invoke(row.GreyedReason);
                return;
            }
            int target = learned.BlueTargetSlot;
            if (target < 0 || BlueSpellSet is not { } set) return;
            byte value = (byte)(row.SpellId - S2C_0x044_ExtendedJob.BlueSpellIdBase);
            GordianLog.Info("BLUE", $"Setting {row.Name} (spell {row.SpellId}) into set {target + 1}.");
            _ = SendAsync(() => set(target, value, state.SubJob), "Set blue magic spell");
            CloseLearned(learned, StockUiBlueMagic.NextEmptySlot(state, target + 1) is var next && next >= 0 && next != target ? next : target);
        }

        private void CloseLearned(StockUiOpenMenu learned, int cursorSlot)
        {
            var slots = learned.Parent;
            CloseMenu(learned);
            if (slots is not { BlueMagicView: StockUiBlueMagicView.Slots }) return;
            lock (_sync)
            {
                slots.BlueLearnedOpen = false;
                if (cursorSlot >= 0 && cursorSlot < slots.Rows.Count)
                {
                    if (cursorSlot < slots.FirstRow || cursorSlot >= slots.FirstRow + slots.VisibleRows)
                    {
                        slots.FirstRow = Math.Clamp(cursorSlot - slots.VisibleRows + 1, 0, Math.Max(0, slots.Rows.Count - slots.VisibleRows));
                        slots.ScrollFrom = slots.FirstRow;
                    }
                    slots.SelectedButtonId = cursorSlot - slots.FirstRow + 1;
                }
            }
            Changed?.Invoke();
        }

        private void Remove(StockUiBlueMagicState state, int slot, ushort spellId, string name)
        {
            if (BlueSpellRemove is not { } remove) return;
            byte value = (byte)(spellId - S2C_0x044_ExtendedJob.BlueSpellIdBase);
            GordianLog.Info("BLUE", $"Removing {name} (spell {spellId}) from set {slot + 1}.");
            _ = SendAsync(() => remove(slot, value, state.SubJob), "Remove blue magic spell");
        }

        /// <summary>"(Remove All)": one C2S 0x102 per set slot, in slot order (the packet removes one spell at a time).</summary>
        private async Task RemoveAllAsync(StockUiBlueMagicState state)
        {
            if (BlueSpellRemove is not { } remove) return;
            for (int i = 0; i < state.Slots.Count; i++)
            {
                byte value = state.Slots[i];
                if (value == 0) continue;
                try
                {
                    await remove(i, value, state.SubJob).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    GordianLog.Warning("BLUE", $"Removing set {i + 1} failed: {ex.Message}");
                    return;
                }
            }
        }
    }
}
