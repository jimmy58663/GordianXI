// src/Gordian.Engine/Ui/StockUiMenuController.Actions.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Engine.Actions;
using Gordian.Core.Diagnostics;

namespace Gordian.Engine.Ui
{
    public sealed partial class StockUiOpenMenu
    {
        /// <summary>Which action list this window shows (<see cref="StockUiActionMenu"/>), if any.</summary>
        public StockUiActionList ActionList { get; init; }

        public bool IsActionList => ActionList != StockUiActionList.None;

        /// <summary>The spell kind a spell list shows (0: every kind but Trust).</summary>
        public int ActionCategory { get; init; }

        /// <summary>The action list's rows, parallel to <see cref="Rows"/>.</summary>
        public IReadOnlyList<StockUiActionRow> ActionRows { get; internal set; } = Array.Empty<StockUiActionRow>();

        /// <summary>The row under the action list's cursor, if it shows one.</summary>
        public StockUiActionRow? SelectedActionRow
        {
            get
            {
                int index = EntryIndex(SelectedButtonId);
                var rows = ActionRows;
                return index >= 0 && index < rows.Count ? rows[index] : null;
            }
        }

        /// <summary>The command menu target the list was opened for (null from the main menu).</summary>
        public StockUiTargetContext? ActionTarget { get; init; }

        /// <summary>What an empty action list writes in its window ("No mounts available."); null when it has rows.</summary>
        public string? EmptyText { get; internal set; }

        /// <summary>The spell lists' Options window (<see cref="StockUiActionMenu.OptionsMenu"/>) opened over a list with + / Y.</summary>
        public bool IsActionOptions { get; init; }

        /// <summary>
        /// Whether this spell list shows the Options window at the top right: every list, from the main menu or the command
        /// menu. Retail's command menu magic lists have none (image-14); the maintainer wants them anyway (#476 round 2).
        /// </summary>
        public bool ShowsActionOptions => ActionList == StockUiActionList.Spells;
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>
        /// Composes an action list's rows for the character (the action service's catalog and state): the list, the spell
        /// kind for a spell list, and the command menu's target (whose lists hold only what you can use). Null when the
        /// session has no catalog (no game DATs).
        /// </summary>
        public Func<StockUiActionList, int, StockUiTargetContext?, IReadOnlyList<StockUiActionRow>?>? ActionRowsProvider { get; set; }

        /// <summary>Runs an action chosen in the Magic or Abilities windows (the action service opens the sub-target cursor).</summary>
        public Func<StockUiActionRequest, Task<PlayerActionResult>>? ActionRequested { get; set; }

        private volatile bool _suspended;

        /// <summary>
        /// The open menus are set aside (not drawn, no input) while the sub-target cursor picks the target of an action
        /// chosen in them, and come back as they were when it ends (retail video 2026-10-11).
        /// </summary>
        public bool Suspended
        {
            get => _suspended;
            set
            {
                if (_suspended == value) return;
                _suspended = value;
                Changed?.Invoke();
            }
        }

        /// <summary>
        /// Opens a spell, job ability, weapon skill, trait, pet command or mount list over <paramref name="parent"/>. An
        /// empty list opens with its "No ... available." text. Returns the list, or null without the window or the tables.
        /// </summary>
        public StockUiOpenMenu? OpenActionList(StockUiActionList list, int category, StockUiOpenMenu? parent)
        {
            var library = _library;
            string menuName = list switch
            {
                StockUiActionList.Spells => StockUiActionMenu.SpellListMenu,
                StockUiActionList.Mounts => StockUiActionMenu.MountListMenu,
                _ => StockUiActionMenu.AbilityListMenu,
            };
            if (library == null || !library.TryGetMenu(menuName, out var definition))
            {
                GordianLog.Warning("UI", $"Stock menu '{menuName}' is not available.");
                return null;
            }
            var target = CommandTargetOf(parent);
            IReadOnlyList<StockUiActionRow>? rows = ActionRowsProvider?.Invoke(list, category, target)
                ?? (list == StockUiActionList.Mounts ? Array.Empty<StockUiActionRow>() : null);
            if (rows != null) rows = OrderedActionRows(list, category, rows);
            if (rows == null)
            {
                NoticePosted?.Invoke("Magic and abilities are not available in this session (the game's spell tables are not loaded).");
                return null;
            }
            var menu = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null, StockUiActionMenu.VisibleRows)
            {
                ActionList = list,
                ActionCategory = category,
                ActionTarget = target,
            };
            SetActionRows(menu, rows);
            menu.SelectedButtonId = rows.Count > 0 ? 1 : 0;
            if (_lastAction.TryGetValue((list, category), out ushort lastId))
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].Id != lastId) continue;
                    menu.FirstRow = Math.Clamp(i - menu.VisibleRows + 1, 0, Math.Max(0, rows.Count - menu.VisibleRows));
                    menu.ScrollFrom = menu.FirstRow;
                    menu.SelectedButtonId = i - menu.FirstRow + 1;
                    break;
                }
            }
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

        private static void SetActionRows(StockUiOpenMenu menu, IReadOnlyList<StockUiActionRow> rows)
        {
            var listRows = new List<StockUiListRow>(rows.Count);
            for (int i = 0; i < rows.Count; i++) listRows.Add(new StockUiListRow(i + 1, rows[i].Name, false));
            menu.ActionRows = rows;
            menu.Rows = listRows;
            menu.EmptyText = rows.Count == 0 ? StockUiActionMenu.EmptyText(menu.ActionList) : null;
        }

        /// <summary>
        /// The character's state changed (MP, TP, recasts, learned lists): open action lists take fresh rows, keeping the
        /// cursor on its action.
        /// </summary>
        public void RefreshActionLists()
        {
            var provider = ActionRowsProvider;
            if (provider == null) return;
            bool changed = false;
            foreach (var menu in _open)
            {
                if (!menu.IsActionList) continue;
                var rows = provider(menu.ActionList, menu.ActionCategory, menu.ActionTarget);
                if (rows == null) continue;
                lock (_sync)
                {
                    rows = OrderedActionRows(menu.ActionList, menu.ActionCategory, rows);
                    ushort? id = menu.SelectedActionRow?.Id;
                    SetActionRows(menu, rows);
                    int index = -1;
                    for (int i = 0; i < rows.Count && id != null; i++)
                    {
                        if (rows[i].Id == id) index = i;
                    }
                    int maxFirst = Math.Max(0, rows.Count - menu.VisibleRows);
                    if (rows.Count == 0)
                    {
                        menu.FirstRow = 0;
                        menu.SelectedButtonId = 0;
                    }
                    else if (index >= 0)
                    {
                        if (index < menu.FirstRow || index >= menu.FirstRow + menu.VisibleRows) menu.FirstRow = Math.Clamp(index, 0, maxFirst);
                        menu.SelectedButtonId = index - menu.FirstRow + 1;
                    }
                    else
                    {
                        menu.FirstRow = Math.Clamp(menu.FirstRow, 0, maxFirst);
                        menu.SelectedButtonId = Math.Clamp(menu.SelectedButtonId, 1, Math.Max(1, Math.Min(menu.VisibleRows, rows.Count - menu.FirstRow)));
                    }
                    menu.ScrollFrom = menu.FirstRow;
                }
                changed = true;
            }
            if (changed) Changed?.Invoke();
        }

        /// <summary>
        /// Confirm on an action list's row: a greyed one posts why, a trait does nothing, a running recast posts the time
        /// left; otherwise the action service opens the sub-target cursor and the menus step aside until it ends.
        /// </summary>
        private void ActivateActionRow(StockUiOpenMenu list)
        {
            if (list.IsManualSort)
            {
                ActionManualSortStep(list);
                return;
            }
            if (list.SelectedActionRow is not { } row || row.Type == StockUiActionType.Trait) return;
            if (row.Type == StockUiActionType.PetList)
            {
                // Blood Pact: Rage / Ward (and Ready) open their own lists (retail image-41, -42).
                OpenActionList(StockUiActionList.PetAbilities, row.OpensPetList, list);
                return;
            }
            if (row.GreyedReason is { } reason)
            {
                NoticePosted?.Invoke(reason);
                return;
            }
            var left = row.RecastLeft(Stopwatch.GetTimestamp());
            if (left > TimeSpan.Zero)
            {
                NoticePosted?.Invoke($"{row.Name} can be used again in {StockUiActionMenu.FormatRecast(left)}.");
                return;
            }
            _ = RequestActionAsync(new StockUiActionRequest(row.Type, row.Id, row.Name, row.TargetFlags, list.ActionTarget));
        }

        /// <summary>Abilities' Ranged Atk.: a ranged attack, through the sub-target cursor on enemies.</summary>
        private void RunRangedAttack(StockUiOpenMenu from) =>
            _ = RequestActionAsync(new StockUiActionRequest(StockUiActionType.RangedAttack, 0, "Ranged Attack", ActionTargetFlags.Enemy, CommandTargetOf(from)));

        /// <summary>
        /// The command menu target a window was opened under (walking up its parents), or null for one opened from the main
        /// menu, which can now sit over the command menu.
        /// </summary>
        private static StockUiTargetContext? CommandTargetOf(StockUiOpenMenu? menu)
        {
            for (var m = menu; m != null; m = m.Parent)
            {
                if (m.IsCommandMenu) return m.Target;
            }
            return null;
        }

        private async Task RequestActionAsync(StockUiActionRequest request)
        {
            var run = ActionRequested;
            if (run == null)
            {
                NoticePosted?.Invoke($"{request.Name} is not available in this session.");
                return;
            }
            try
            {
                var result = await run(request).ConfigureAwait(false);
                if (result.Kind is PlayerActionResultKind.Warning or PlayerActionResultKind.Error && !string.IsNullOrEmpty(result.Message))
                {
                    NoticePosted?.Invoke(result.Message);
                }
            }
            catch (Exception ex)
            {
                GordianLog.Warning("UI", $"{request.Name} failed: {ex.Message}");
                NoticePosted?.Invoke($"{request.Name} failed: {ex.Message}");
            }
        }

        /// <summary>
        /// + on the keyboard or Y on the gamepad over a main-menu spell list: the cursor goes to the Options window at the
        /// top right ("+ : Sort", Auto, Manual; retail capture 2026-10-11), and back again (or Cancel). Returns whether it applied.
        /// </summary>
        public bool ToggleActionOptions()
        {
            var library = _library;
            var top = Top;
            if (top is { IsActionOptions: true })
            {
                CloseMenu(top);
                return true;
            }
            if (top is not { ShowsActionOptions: true, IsManualSort: false } || library == null) return false;
            string name = OptionsMenuFor(top.ActionCategory);
            if (!library.TryGetMenu(name, out var definition)) return false;
            var options = new StockUiOpenMenu(definition, top, Array.Empty<string>(), null) { IsActionOptions = true };
            options.SelectedButtonId = StockUiActionMenu.OptionsAutoButton;
            lock (_sync)
            {
                var open = new StockUiOpenMenu[_open.Length + 1];
                Array.Copy(_open, open, _open.Length);
                open[^1] = options;
                _open = open;
            }
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// The Options window of a spell list: Blue Magic's is <c>blusortw</c> with a working Set Spells when Blue Mage is a job of
        /// yours (#363), else <c>mgcsortw</c>, whose Set Spells is baked in greyed (image-17); the other kinds' is <c>mgcsort</c>.
        /// </summary>
        public string OptionsMenuFor(int category) => category != StockUiActionMenu.BlueMagicKind ? StockUiActionMenu.OptionsMenu
            : CanSetBlueSpells ? StockUiBlueMagic.OptionsMenu : StockUiActionMenu.BlueOptionsMenu;

        /// <summary>
        /// The Options window: Auto sorts the list back into the spell table's order and returns to it; Manual returns to the
        /// list to move spells, as the item list's Manual (#476; the maintainer: every Manual sort works the same).
        /// </summary>
        private void ActivateActionOptions(StockUiOpenMenu options, int buttonId)
        {
            if (buttonId == StockUiBlueMagic.OptionsSetSpellsButton && options.Name.Equals(StockUiBlueMagic.OptionsMenu, StringComparison.OrdinalIgnoreCase))
            {
                // Set Spells (#363) opens over the Blue Magic list.
                var spells = options.Parent;
                CloseMenu(options);
                OpenSetSpells(spells);
                return;
            }
            if (buttonId == StockUiActionMenu.OptionsManualButton)
            {
                var sorted = options.Parent;
                CloseMenu(options);
                if (sorted != null)
                {
                    lock (_sync)
                    {
                        sorted.IsManualSort = true;
                        sorted.ManualSortFrom = null;
                    }
                    Changed?.Invoke();
                }
                return;
            }
            CloseMenu(options);
            if (options.Parent is { } list)
            {
                lock (_sync)
                {
                    _actionOrder.Remove(list.ActionCategory);
                    list.FirstRow = 0;
                    list.ScrollFrom = 0;
                    list.SelectedButtonId = list.ActionRows.Count > 0 ? 1 : 0;
                }
            }
            RefreshActionLists();
        }

        /// <summary>
        /// The spell kinds' Manual orders (spell ids, by <see cref="StockUiOpenMenu.ActionCategory"/>). The client's, as the
        /// item lists' are: nothing to send, kept for the session. A kind's order also applies where the command menu lists it.
        /// </summary>
        private readonly Dictionary<int, List<ushort>> _actionOrder = new();

        /// <summary>A spell list's rows in its Manual order: the ordered spells first, any others after them in table order.</summary>
        private IReadOnlyList<StockUiActionRow> OrderedActionRows(StockUiActionList list, int category, IReadOnlyList<StockUiActionRow> rows)
        {
            if (list != StockUiActionList.Spells || !_actionOrder.TryGetValue(category, out var order)) return rows;
            var rank = new Dictionary<ushort, int>(order.Count);
            for (int i = 0; i < order.Count; i++) rank[order[i]] = i;
            return rows.OrderBy(r => rank.TryGetValue(r.Id, out int place) ? place : int.MaxValue).ToList(); // stable
        }

        /// <summary>
        /// Manual sort: the first Confirm picks the spell, the second the place; the first takes the second's place and the
        /// mode stays for the next pair until Cancel, as <see cref="ManualSortStep"/> for the item lists.
        /// </summary>
        private void ActionManualSortStep(StockUiOpenMenu list)
        {
            int index = list.EntryIndex(list.SelectedButtonId);
            if (index < 0 || index >= list.ActionRows.Count) return;
            lock (_sync)
            {
                if (list.ManualSortFrom is not { } from)
                {
                    list.ManualSortFrom = index;
                }
                else
                {
                    var rows = list.ActionRows.ToList();
                    var moved = rows[from];
                    rows.RemoveAt(from);
                    rows.Insert(index, moved);
                    _actionOrder[list.ActionCategory] = rows.ConvertAll(r => r.Id);
                    list.ManualSortFrom = null;
                    SetActionRows(list, rows);
                }
            }
            Changed?.Invoke();
        }
    }
}
