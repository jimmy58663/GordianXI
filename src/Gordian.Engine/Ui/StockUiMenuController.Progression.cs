// src/Gordian.Engine/Ui/StockUiMenuController.Progression.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Resources.Tables;

namespace Gordian.Engine.Ui
{
    public sealed partial class StockUiOpenMenu
    {
        /// <summary>Which list of #491 / #493 this <see cref="StockUiListKind.StatusValues"/> window is; None for the others.</summary>
        public StockUiProgressionList ProgressionList { get; internal set; }

        /// <summary>
        /// What the list is of: the merit category (1-28), the job (1-22), the RoE category's record id; for <c>merit3</c> the
        /// merit id it edits. -1 when none.
        /// </summary>
        public int ProgressionKey { get; internal set; } = -1;

        /// <summary>What each row stands for, parallel to <see cref="Rows"/>: a merit id, a job point category, an RoE record id.</summary>
        public IReadOnlyList<int> RowKeys { get; internal set; } = Array.Empty<int>();

        /// <summary>The key of the row under the cursor, or -1.</summary>
        public int SelectedRowKey => SelectedStatusRow is >= 0 and var row && row < RowKeys.Count ? RowKeys[row] : -1;

        /// <summary>A Records of Eminence list (#493).</summary>
        public bool IsRoeList => ProgressionList is StockUiProgressionList.RoeCategories or StockUiProgressionList.RoeObjectives or StockUiProgressionList.RoeCurrent;
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>The merit table's strings by id (ROM/169/75): categories at 2 n, merits at their id. Set by the HUD.</summary>
        public Func<int, string?>? MeritText { get; set; }

        /// <summary>The merits S2C 0x08C listed (<c>ProgressionState.GetMerits</c>); set by the session.</summary>
        public Func<IReadOnlyList<StockUiMerit>>? Merits { get; set; }

        /// <summary>A job's job point category (job, k): its level and next cost (S2C 0x08D); set by the session.</summary>
        public Func<int, int, StockUiJobPointCategory>? JobPointLevel { get; set; }

        /// <summary>Raises (true) or lowers a merit by id: C2S 0x0BE kind 3. Set by the session.</summary>
        public Func<int, bool, Task>? MeritChanged { get; set; }

        /// <summary>Raises a job point category by its index (job x 32 + k): C2S 0x0BF. Set by the session.</summary>
        public Func<ushort, Task>? JobPointSpent { get; set; }

        /// <summary>Asks for the Unity figures as <c>ut_menu</c> opens: C2S 0x116 kinds 0 and 1. Set by the session.</summary>
        public Action? UnityMenuOpened { get; set; }

        /// <summary>Asks for the Unity / RoE quest information: C2S 0x117 (Ranking Info, RoE Current and Objective List). Set by the session.</summary>
        public Action? UnityQuestRequested { get; set; }

        /// <summary>Turns Unity chat on or off: C2S 0x118. Set by the session.</summary>
        public Func<bool, Task>? UnityChatChanged { get; set; }

        /// <summary>The Unity windows' figures; set by the session.</summary>
        public Func<StockUiUnityInfo?>? UnityInfo { get; set; }

        /// <summary>
        /// Whether Unity chat is on. The server does not report it (LandSandBoat's C2S 0x118 handler answers with the character
        /// packets only), so the client keeps what was last chosen; on until changed (PROVISIONAL default).
        /// </summary>
        public bool UnityChatActive { get; private set; } = true;

        /// <summary>The Records of Eminence tables (ROM/307/16, ROM/307/24); null without the game's DATs. Set by the HUD.</summary>
        public Func<RecordsOfEminenceTables?>? RoeTables { get; set; }

        /// <summary>The Records of Eminence log (S2C 0x111 / 0x112); set by the session.</summary>
        public Func<StockUiRoeState?>? RoeState { get; set; }

        /// <summary>Takes an objective (C2S 0x10C) by objective id. Set by the session.</summary>
        public Func<int, Task>? RoeStarted { get; set; }

        /// <summary>Removes an objective (C2S 0x10D) by objective id. Set by the session.</summary>
        public Func<int, Task>? RoeRemoved { get; set; }

        private string ShortText(int id, string fallback) => MenuStringText?.Invoke(id) is { Length: > 0 } text ? text : fallback;

        private IReadOnlyList<StockUiMerit> CurrentMerits() => Merits?.Invoke() ?? Array.Empty<StockUiMerit>();

        /// <summary>A Status entry added by #491 (<see cref="StockUiMenuCommand.Progression"/>).</summary>
        private void RunProgression(StockUiProgressionAction action, StockUiOpenMenu from)
        {
            switch (action)
            {
                case StockUiProgressionAction.MeritCategories:
                    lock (_sync) OpenProgressionList(StockUiProgressionList.MeritCategories, -1, from, 0);
                    break;
                case StockUiProgressionAction.MeritRaise:
                case StockUiProgressionAction.MeritLower:
                    EditMerit(from.ProgressionKey, action == StockUiProgressionAction.MeritRaise, from);
                    return;
                case StockUiProgressionAction.UnityInfo:
                    lock (_sync) OpenProgressionList(StockUiProgressionList.UnityInfo, -1, from, StockUiProgression.UnityChatActiveRow - 1);
                    break;
                case StockUiProgressionAction.UnityRanking:
                    UnityQuestRequested?.Invoke();
                    lock (_sync) OpenProgressionList(StockUiProgressionList.UnityRanking, -1, from, 0);
                    break;
                case StockUiProgressionAction.UnityLists:
                    lock (_sync) OpenProgressionList(StockUiProgressionList.UnityLeaders, -1, from, Math.Max(0, (UnityInfo?.Invoke()?.Leader ?? 1) - 1));
                    break;
            }
            Changed?.Invoke();
        }

        /// <summary>The DAT window a list of #491 / #493 opens in, its help line(s) and the Status page it belongs to.</summary>
        private static (string Menu, int Help, StockUiStatusPage Page) ProgressionWindow(StockUiProgressionList list) => list switch
        {
            StockUiProgressionList.MeritCategories => (StockUiProgression.MeritCategoryMenu, StockUiProgression.MeritCategoryHelp, StockUiStatusPage.MeritPoints),
            StockUiProgressionList.Merits => (StockUiProgression.MeritListMenu, StockUiProgression.MeritListHelp, StockUiStatusPage.MeritPoints),
            StockUiProgressionList.JobPointCategories => (StockUiProgression.MeritListMenu, StockUiProgression.JobPointCategoryHelp, StockUiStatusPage.JobPoints),
            StockUiProgressionList.UnityInfo => (StockUiProgression.MeritCategoryMenu, -1, StockUiStatusPage.Unity),
            StockUiProgressionList.UnityRanking => (StockUiProgression.MeritCategoryMenu, -1, StockUiStatusPage.Unity),
            StockUiProgressionList.UnityLeaders => (StockUiProgression.MeritCategoryMenu, StockUiProgression.UnityListHelp, StockUiStatusPage.Unity),
            _ => (StockUiRecordsOfEminence.ListMenu, -1, StockUiStatusPage.None),
        };

        /// <summary>The rows of a list of #491 / #493 and what each stands for, from the state now.</summary>
        private (IReadOnlyList<StockUiListRow> Rows, IReadOnlyList<int> Keys) ProgressionRows(StockUiProgressionList list, int key)
        {
            switch (list)
            {
                case StockUiProgressionList.MeritCategories:
                {
                    var categories = StockUiProgression.MeritCategories(MeritText);
                    return (StockUiProgression.MeritCategoryRows(categories, CurrentMerits(), MeritText), categories);
                }
                case StockUiProgressionList.Merits:
                {
                    var merits = StockUiProgression.MeritsOf(key, CurrentMerits(), MeritText);
                    var keys = new int[merits.Count];
                    for (int i = 0; i < keys.Length; i++) keys[i] = merits[i].Id;
                    return (StockUiProgression.MeritRows(merits, MeritText), keys);
                }
                case StockUiProgressionList.JobPointCategories:
                {
                    var categories = StockUiProgression.JobPointCategories(key, JobPointText);
                    return (StockUiProgression.JobPointCategoryRows(key, categories, JobPointLevel, JobPointText), categories);
                }
                case StockUiProgressionList.UnityInfo:
                    return (StockUiProgression.UnityInfoRows(UnityInfo?.Invoke(), UnityChatActive, MenuStringText), Array.Empty<int>());
                case StockUiProgressionList.UnityRanking:
                    return (StockUiProgression.UnityRankingRows(UnityInfo?.Invoke(), MenuStringText), Array.Empty<int>());
                case StockUiProgressionList.UnityLeaders:
                    return (StockUiProgression.UnityLeaderRows(UnityInfo?.Invoke()?.Leader ?? 0, MenuStringText), Array.Empty<int>());
                case StockUiProgressionList.RoeCategories:
                case StockUiProgressionList.RoeObjectives:
                    return RoeTables?.Invoke() is { } tables ? StockUiRecordsOfEminence.CategoryRows(tables, key, RoeState?.Invoke())
                        : (Array.Empty<StockUiListRow>(), Array.Empty<int>());
                case StockUiProgressionList.RoeCurrent:
                    return RoeTables?.Invoke() is { } roe ? StockUiRecordsOfEminence.CurrentRows(roe, RoeState?.Invoke())
                        : (Array.Empty<StockUiListRow>(), Array.Empty<int>());
            }
            return (Array.Empty<StockUiListRow>(), Array.Empty<int>());
        }

        /// <summary>Opens a list of #491 / #493 over <paramref name="from"/>, the cursor on row <paramref name="cursor"/>. Call under the lock.</summary>
        private StockUiOpenMenu? OpenProgressionList(StockUiProgressionList list, int key, StockUiOpenMenu from, int cursor)
        {
            var (menu, help, page) = ProgressionWindow(list);
            var (rows, keys) = ProgressionRows(list, key);
            return OpenStatusList(page, menu, from, rows, help >= 0 ? new[] { help } : Array.Empty<int>(), cursor, progression: list, key: key, keys: keys);
        }

        /// <summary>Confirm on a list of #491 / #493.</summary>
        private void ActivateProgressionList(StockUiOpenMenu list)
        {
            int rowKey = list.SelectedRowKey;
            switch (list.ProgressionList)
            {
                case StockUiProgressionList.MeritCategories when rowKey > 0:
                    lock (_sync) OpenProgressionList(StockUiProgressionList.Merits, rowKey, list, 0);
                    Changed?.Invoke();
                    break;
                case StockUiProgressionList.Merits when rowKey > 0:
                    lock (_sync)
                    {
                        var edit = Push(StockUiProgression.MeritEditMenu, list, Array.Empty<string>(), null, null);
                        if (edit != null)
                        {
                            edit.StatusWindow = StockUiStatusPage.MeritPoints;
                            edit.ProgressionKey = rowKey;
                        }
                    }
                    Changed?.Invoke();
                    break;
                case StockUiProgressionList.JobPointCategories when rowKey >= 0:
                    SpendJobPoint(list.ProgressionKey, rowKey, list);
                    break;
                case StockUiProgressionList.UnityInfo:
                    if (list.SelectedStatusRow + 1 is StockUiProgression.UnityChatActiveRow or StockUiProgression.UnityChatInactiveRow)
                        SetUnityChat(list.SelectedStatusRow + 1 == StockUiProgression.UnityChatActiveRow);
                    break;
                case StockUiProgressionList.UnityLeaders:
                    // "Display the members logged in to the designated Unity." (help 888): a search by Unity, not decoded yet.
                    NoticePosted?.Invoke("Unity Lists: searching a Unity's members is not available yet.");
                    break;
                case StockUiProgressionList.RoeCategories:
                case StockUiProgressionList.RoeObjectives:
                case StockUiProgressionList.RoeCurrent:
                    ActivateRoe(list, rowKey);
                    break;
            }
        }

        /// <summary>The lists of #491 / #493 follow the state: S2C 0x08C / 0x08D / 0x063 / 0x111 / 0x112 changed. Call under the lock.</summary>
        private bool RefreshProgressionList(StockUiOpenMenu menu)
        {
            if (menu.ProgressionList == StockUiProgressionList.None) return false;
            var (rows, keys) = ProgressionRows(menu.ProgressionList, menu.ProgressionKey);
            int keep = menu.SelectedRowKey;
            menu.Rows = rows;
            menu.RowKeys = keys;
            int index = keep >= 0 ? IndexOfKey(keys, keep) : -1;
            if (index < 0) index = Math.Min(menu.SelectedStatusRow < 0 ? 0 : menu.SelectedStatusRow, Math.Max(0, rows.Count - 1));
            int visible = menu.VisibleRows;
            if (index < menu.FirstRow || index >= menu.FirstRow + visible) menu.FirstRow = Math.Clamp(index - visible + 1, 0, Math.Max(0, rows.Count - visible));
            menu.ScrollFrom = menu.FirstRow;
            menu.SelectedButtonId = rows.Count > 0 ? index - menu.FirstRow + 1 : 0;
            return true;
        }

        private static int IndexOfKey(IReadOnlyList<int> keys, int key)
        {
            for (int i = 0; i < keys.Count; i++)
            {
                if (keys[i] == key) return i;
            }
            return -1;
        }

        /// <summary>
        /// <c>merit3</c>'s Raise or Lower: asks first, then sends C2S 0x0BE kind 3 and closes <c>merit3</c>. LandSandBoat takes it
        /// in a Mog House only; raising needs the merit's next cost (0 when it is at its most or outside the Mog House) in
        /// merit points held.
        /// </summary>
        private void EditMerit(int meritId, bool raise, StockUiOpenMenu edit)
        {
            if (meritId <= 0) return;
            string name = MeritText?.Invoke(meritId) ?? "the merit";
            StockUiMerit? merit = null;
            foreach (var m in CurrentMerits())
            {
                if (m.Id == meritId) merit = m;
            }
            lock (_sync)
            {
                if (_open.Length > 0 && ReferenceEquals(_open[^1], edit)) _open = _open[..^1];
            }
            Changed?.Invoke();
            if (InMogHouse?.Invoke() != true)
            {
                NoticePosted?.Invoke("Merit points can only be allocated in your Mog House.");
                return;
            }
            if (raise && merit is { } r && (r.Next == 0 || (StatusPoints?.Invoke()?.MeritPoints ?? 0) < r.Next))
            {
                NoticePosted?.Invoke(r.Next == 0 ? $"{name} cannot be raised any further." : $"You do not have enough merit points to raise {name}.");
                return;
            }
            if (!raise && merit is { Count: 0 })
            {
                NoticePosted?.Invoke($"{name} has not been raised.");
                return;
            }
            if (MeritChanged is not { } send)
            {
                NoticePosted?.Invoke("Merit Points: this is not available in this session.");
                return;
            }
            // PROVISIONAL wording: retail's merityn question is not captured.
            string question = raise ? $"Raise {name}?" : $"Lower {name}? Merit points will not be reimbursed.";
            OpenYesNo(question, defaultYes: raise, answer =>
            {
                if (answer == true) _ = send(meritId, raise);
            });
        }

        /// <summary>
        /// Confirm on a job point category: asks, then sends C2S 0x0BF. LandSandBoat takes it in a Mog House only; a level costs
        /// its number plus one in job points, to level 20.
        /// </summary>
        private void SpendJobPoint(int job, int category, StockUiOpenMenu list)
        {
            string name = JobPointText?.Invoke(StockUiProgression.JobPointCategoryNameId(job, category)) ?? "the category";
            var level = JobPointLevel?.Invoke(job, category) ?? default;
            int cost = level.Next > 0 ? level.Next : StockUiProgression.JobPointCost(level.Level);
            int held = StockUiStatusPages.JobPointsOf(StatusPoints?.Invoke(), job).JobPoints;
            if (InMogHouse?.Invoke() != true)
            {
                NoticePosted?.Invoke("Job points can only be allocated in your Mog House.");
                return;
            }
            if (level.Level >= StockUiProgression.MaxJobPointLevel || cost == 0)
            {
                NoticePosted?.Invoke($"{name} cannot be raised any further.");
                return;
            }
            if (held < cost)
            {
                NoticePosted?.Invoke($"You do not have enough job points to raise {name}.");
                return;
            }
            if (JobPointSpent is not { } send)
            {
                NoticePosted?.Invoke("Job Points: this is not available in this session.");
                return;
            }
            OpenYesNo($"Raise {name}? ({cost} job points)", defaultYes: true, answer =>
            {
                if (answer == true) _ = send(StockUiProgression.JobPointIndex(job, category));
            });
        }

        /// <summary>Unity Info's Active / Inactive: sends C2S 0x118 and shows the new setting.</summary>
        private void SetUnityChat(bool active)
        {
            if (UnityChatChanged is not { } send)
            {
                NoticePosted?.Invoke("Unity chat settings are not available in this session.");
                return;
            }
            _ = send(active);
            UnityChatActive = active;
            OnStatusPointsChanged();
        }

        /// <summary>Main menu Quests > Records of Eminence (<c>quest00</c> row 3): Current and Objective List.</summary>
        private void OpenRoeMenu(StockUiOpenMenu parent)
        {
            if (RoeTables?.Invoke() == null)
            {
                NoticePosted?.Invoke("Records of Eminence: the objective tables are not loaded in this session.");
                return;
            }
            var definition = QuestLogDefinition(StockUiQuestLog.QuestKindMenu, 2, StockUiQuestLog.KindRowX, StockUiQuestLog.KindRowY,
                StockUiQuestLog.KindRowPitch, StockUiQuestLog.PickerRowWidth, StockUiQuestLog.PickerRowHeight);
            if (definition == null) return;
            var menu = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null, 2)
            {
                QuestLogView = StockUiQuestLogView.RoeMenu,
                IsQuestsWindow = true,
                ItemOwner = this,
            };
            lock (_sync)
            {
                menu.Rows = new List<StockUiListRow>
                {
                    new(StockUiRecordsOfEminence.CurrentButton, ShortText(StockUiRecordsOfEminence.CurrentText, "Current"), false),
                    new(StockUiRecordsOfEminence.ObjectiveListButton, ShortText(StockUiRecordsOfEminence.ObjectiveListText, "Objective List"), false),
                };
                menu.SelectedButtonId = 1;
                PushOpen(menu);
            }
            Changed?.Invoke();
        }

        /// <summary>The Records of Eminence choice's Current or Objective List (C2S 0x117 first, as retail does).</summary>
        private void OpenRoeList(int button, StockUiOpenMenu parent)
        {
            UnityQuestRequested?.Invoke();
            bool current = button == StockUiRecordsOfEminence.CurrentButton;
            if (current && (RoeState?.Invoke()?.Slots.Count ?? 0) == 0)
            {
                NoticePosted?.Invoke(ShortText(StockUiRecordsOfEminence.NoCurrentText, "No current objectives."));
                return;
            }
            lock (_sync)
            {
                OpenProgressionList(current ? StockUiProgressionList.RoeCurrent : StockUiProgressionList.RoeCategories,
                    current ? -1 : RecordsOfEminenceTables.RootCategory, parent, 0);
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// Confirm on a Records of Eminence list: a category opens its children; an objective taken asks to remove it (C2S
        /// 0x10D), another asks to take it (C2S 0x10C) unless it is completed and cannot be repeated.
        /// </summary>
        private void ActivateRoe(StockUiOpenMenu list, int record)
        {
            if (record < 0 || RoeTables?.Invoke() is not { } tables) return;
            if (record >= RecordsOfEminenceTables.FirstCategoryRecord)
            {
                lock (_sync) OpenProgressionList(StockUiProgressionList.RoeObjectives, record, list, 0);
                Changed?.Invoke();
                return;
            }
            int id = record - RecordsOfEminenceTables.FirstObjectiveRecord;
            var objective = tables.Objective(id);
            var state = RoeState?.Invoke();
            if (state?.SlotOf(id) != null)
            {
                if (RoeRemoved is not { } remove) return;
                string question = ShortText(StockUiRecordsOfEminence.RemovePromptText, "Remove objective?") + " "
                    + ShortText(StockUiRecordsOfEminence.RemoveWarningText, "Your progress will be reset if you cancel this objective. Are you sure?");
                OpenYesNo(question, defaultYes: false, answer =>
                {
                    if (answer == true) _ = remove(id);
                });
                return;
            }
            if (objective != null && !objective.Repeatable && state?.Completed(id) == true)
            {
                NoticePosted?.Invoke(ShortText(StockUiRecordsOfEminence.NotAgainText, "This objective cannot be undertaken again."));
                return;
            }
            if (RoeStarted is not { } start) return;
            OpenYesNo(ShortText(StockUiRecordsOfEminence.SetPromptText, "Set objective?"), defaultYes: true, answer =>
            {
                if (answer == true) _ = start(id);
            });
        }

        /// <summary>The objective under a Records of Eminence list's cursor, or null.</summary>
        public RoeObjective? SelectedRoeObjective(StockUiOpenMenu list)
        {
            int record = list.SelectedRowKey;
            if (!list.IsRoeList || record < RecordsOfEminenceTables.FirstObjectiveRecord || record >= RecordsOfEminenceTables.FirstCategoryRecord) return null;
            return RoeTables?.Invoke()?.Objective(record - RecordsOfEminenceTables.FirstObjectiveRecord);
        }
    }
}
