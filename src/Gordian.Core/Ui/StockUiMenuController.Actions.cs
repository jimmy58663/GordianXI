// src/Gordian.Core/Ui/StockUiMenuController.Actions.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Gordian.Core.Actions;
using Gordian.Core.Diagnostics;

namespace Gordian.Core.Ui
{
    public sealed partial class StockUiOpenMenu
    {
        /// <summary>Which action list this window shows (<see cref="StockUiActionMenu"/>), if any.</summary>
        public StockUiActionList ActionList { get; init; }

        public bool IsActionList => ActionList != StockUiActionList.None;

        /// <summary>The spell kind a spell list shows.</summary>
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
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>
        /// Composes an action list's rows for the character (the action service's catalog and state): the list, the spell
        /// kind for a spell list, and the command menu's target. Null when the session has no catalog (no game DATs).
        /// </summary>
        public Func<StockUiActionList, int, StockUiTargetContext?, IReadOnlyList<StockUiActionRow>?>? ActionRowsProvider { get; set; }

        /// <summary>Runs an action chosen in the Magic or Abilities windows (the action service picks or asks for the target).</summary>
        public Func<StockUiActionRequest, Task<PlayerActionResult>>? ActionRequested { get; set; }

        /// <summary>
        /// Opens a spell, job ability or weapon skill list over <paramref name="parent"/> (the category or Abilities window).
        /// An empty list posts a notice instead. Returns the list, or null.
        /// </summary>
        public StockUiOpenMenu? OpenActionList(StockUiActionList list, int category, StockUiOpenMenu? parent)
        {
            var library = _library;
            string menuName = list == StockUiActionList.Spells ? StockUiActionMenu.SpellListMenu : StockUiActionMenu.AbilityListMenu;
            if (library == null || !library.TryGetMenu(menuName, out var definition))
            {
                GordianLog.Warning("UI", $"Stock menu '{menuName}' is not available.");
                return null;
            }
            var target = CommandMenuTarget;
            var provider = ActionRowsProvider;
            var rows = provider?.Invoke(list, category, target);
            if (rows == null)
            {
                NoticePosted?.Invoke("Magic and abilities are not available in this session (the game's spell tables are not loaded).");
                return null;
            }
            if (rows.Count == 0)
            {
                NoticePosted?.Invoke(list switch
                {
                    StockUiActionList.Spells => $"You have no {StockUiActionMenu.CategoryName(category)} spells you can use.",
                    StockUiActionList.JobAbilities => "You have no job abilities you can use.",
                    _ => "You have no weapon skills you can use.",
                });
                return null;
            }
            var menu = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null, StockUiActionMenu.VisibleRows)
            {
                ActionList = list,
                ActionCategory = category,
                ActionTarget = target,
            };
            SetActionRows(menu, rows);
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

        private static void SetActionRows(StockUiOpenMenu menu, IReadOnlyList<StockUiActionRow> rows)
        {
            var listRows = new List<StockUiListRow>(rows.Count);
            for (int i = 0; i < rows.Count; i++) listRows.Add(new StockUiListRow(i + 1, rows[i].Name, false, Value: rows[i].Value));
            menu.ActionRows = rows;
            menu.Rows = listRows;
        }

        /// <summary>
        /// The character's state changed (MP, TP, recasts, learned spells): open action lists take fresh rows, keeping the
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
                    if (index >= 0)
                    {
                        if (index < menu.FirstRow || index >= menu.FirstRow + menu.VisibleRows) menu.FirstRow = Math.Clamp(index, 0, maxFirst);
                        menu.ScrollFrom = menu.FirstRow;
                        menu.SelectedButtonId = index - menu.FirstRow + 1;
                    }
                    else
                    {
                        menu.FirstRow = Math.Clamp(menu.FirstRow, 0, maxFirst);
                        menu.ScrollFrom = menu.FirstRow;
                        menu.SelectedButtonId = Math.Clamp(menu.SelectedButtonId, 1, Math.Max(1, Math.Min(menu.VisibleRows, rows.Count - menu.FirstRow)));
                    }
                }
                changed = true;
            }
            if (changed) Changed?.Invoke();
        }

        /// <summary>Confirm on an action list's row: a greyed one posts why; otherwise the menus close and the action runs.</summary>
        private void ActivateActionRow(StockUiOpenMenu list)
        {
            if (list.SelectedActionRow is not { } row) return;
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
            var request = new StockUiActionRequest(row.Type, row.Id, row.Name, row.TargetFlags, list.ActionTarget);
            CloseAll();
            _ = RequestActionAsync(request);
        }

        /// <summary>Abilities' Ranged Atk.: a ranged attack on the command menu's target, or the current one.</summary>
        private void RunRangedAttack()
        {
            var request = new StockUiActionRequest(StockUiActionType.RangedAttack, 0, "Ranged Attack", ActionTargetFlags.Enemy, CommandMenuTarget);
            CloseAll();
            _ = RequestActionAsync(request);
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
    }
}
