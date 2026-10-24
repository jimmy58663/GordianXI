// src/Gordian.Core/Ui/StockUiMenuController.Actions.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        /// <summary>Whether this is a spell list opened from the main menu, which shows the Options window at the top right.</summary>
        public bool ShowsActionOptions => ActionList == StockUiActionList.Spells && ActionTarget == null;
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
            if (top is not { ShowsActionOptions: true } || library == null) return false;
            string name = top.ActionCategory == StockUiActionMenu.BlueMagicKind ? StockUiActionMenu.BlueOptionsMenu : StockUiActionMenu.OptionsMenu;
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
        /// The Options window: Auto sorts the list back into the spell table's order and returns to it; Manual (retail: pick
        /// a spell, then where to put it, as the item list's Manual) is not built yet.
        /// </summary>
        private void ActivateActionOptions(StockUiOpenMenu options, int buttonId)
        {
            if (buttonId == StockUiActionMenu.OptionsManualButton)
            {
                NoticePosted?.Invoke("Manual sorting is not available yet.");
                return;
            }
            CloseMenu(options);
            if (options.Parent is { } list)
            {
                lock (_sync)
                {
                    list.FirstRow = 0;
                    list.ScrollFrom = 0;
                    list.SelectedButtonId = list.ActionRows.Count > 0 ? 1 : 0;
                }
            }
            RefreshActionLists();
        }
    }
}
