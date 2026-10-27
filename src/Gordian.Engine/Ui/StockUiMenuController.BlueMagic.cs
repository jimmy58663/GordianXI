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

        /// <summary>Every learned spell whatever the family filter (the details window describes from it).</summary>
        public IReadOnlyList<StockUiBlueSpellRow> BlueAllSpells { get; internal set; } = Array.Empty<StockUiBlueSpellRow>();

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

        private int _blueFamily;

        /// <summary>The monster family the learned list is narrowed to ("Refine by Type", 1-18); 0 for every spell.</summary>
        public int BlueFamily => _blueFamily;

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
            // The status column left of the slots is the Equipment window's (image-20).
            var menu = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null, VisibleRowsOf(definition))
            {
                BlueMagicView = StockUiBlueMagicView.Slots,
                CharacterStats = CharacterStats,
            };
            menu.SelectedButtonId = 1;
            lock (_sync)
            {
                _blueFamily = 0;
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
            menu.BlueSpells = catalog != null ? StockUiBlueMagic.LearnedRows(state, catalog, menu.BlueTargetSlot, MenuMessageText, _blueFamily)
                : Array.Empty<StockUiBlueSpellRow>();
            menu.BlueAllSpells = _blueFamily == 0 || catalog == null ? menu.BlueSpells
                : StockUiBlueMagic.LearnedRows(state, catalog, menu.BlueTargetSlot, MenuMessageText);
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
            if (top.Name.Equals(StockUiBlueMagic.TypeMenu, StringComparison.OrdinalIgnoreCase))
            {
                if (top.SelectedButton is { } family && IsSelectable(family)) PickBlueFamily(top, family.ButtonId);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Confirm on a slot: the learned list takes the cursor for it, an empty one's or a set one's (round 1, N13: retail
        /// offers spells to swap in, the set one green, and picking it again empties the slot); a locked one says so.
        /// </summary>
        private void ActivateBlueSlot(StockUiOpenMenu slots)
        {
            int index = slots.EntryIndex(slots.SelectedButtonId);
            if (index < 0 || index >= slots.BlueSlots.Count || BlueMagicState?.Invoke() is null) return;
            var slot = slots.BlueSlots[index];
            if (slot.Locked)
            {
                NoticePosted?.Invoke($"Set {slot.Slot:00} opens at Blue Mage level {slot.UnlockLevel}.");
                return;
            }
            OpenLearned(slots, index);
        }

        /// <summary>"Type ▶": <c>bluesibo</c> over the learned list, or, with a family picked, the whole list again (image-21, image-22).</summary>
        private void ToggleBlueFamily(StockUiOpenMenu learned)
        {
            if (_blueFamily != 0)
            {
                PickBlueFamily(null, 0);
                return;
            }
            StockUiOpenMenu? menu;
            lock (_sync)
            {
                menu = Push(StockUiBlueMagic.TypeMenu, learned, Array.Empty<string>(), null, null);
                if (menu != null) menu.SelectedButtonId = 1;
            }
            Changed?.Invoke();
        }

        /// <summary>Narrows the learned list to a family (0 = every spell) and closes the family window.</summary>
        private void PickBlueFamily(StockUiOpenMenu? typeMenu, int family)
        {
            if (typeMenu != null) CloseMenu(typeMenu);
            lock (_sync)
            {
                _blueFamily = family;
                foreach (var menu in _open)
                {
                    if (!menu.IsBlueMagicWindow) continue;
                    FillBlueMagic(menu);
                }
            }
            Changed?.Invoke();
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
                // The cursor starts on the slot's spell when it holds one, else on the first spell that can be set.
                ushort current = targetSlot < slots.BlueSlots.Count ? slots.BlueSlots[targetSlot].SpellId : (ushort)0;
                int first = -1;
                for (int i = StockUiBlueMagic.FirstSpellRow; i < learned.BlueSpells.Count && first < 0; i++)
                {
                    var row = learned.BlueSpells[i];
                    if (current != 0 ? row.SpellId == current : row is { IsSet: false, GreyedReason: null }) first = i;
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
        /// Confirm on a learned spell: "Type ▶" narrows the list by monster family (or widens it again); "(Remove All)" asks
        /// "Remove all set blue magic spells?" (ROM/165/72 #228); the spell set in the slot being changed (green) is removed
        /// from it, one set in another slot says so (PROVISIONAL: the server would empty this slot and set nothing); a greyed one
        /// says why; any other is set into the slot, replacing what was there (LandSandBoat <c>blueutils::SetBlueSpell</c>), and
        /// the cursor goes back to the slots, on the next empty one.
        /// </summary>
        private void ActivateLearnedSpell(StockUiOpenMenu learned)
        {
            int index = learned.EntryIndex(learned.SelectedButtonId);
            if (index < 0 || index >= learned.BlueSpells.Count || BlueMagicState?.Invoke() is not { } state) return;
            var row = learned.BlueSpells[index];
            if (row.TypeHeader)
            {
                ToggleBlueFamily(learned);
                return;
            }
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
                if (slot >= 0 && slot == learned.BlueTargetSlot)
                {
                    Remove(state, slot, row.SpellId, row.Name);
                    CloseLearned(learned, slot);
                }
                else
                {
                    NoticePosted?.Invoke($"{row.Name} is already set.");
                }
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
