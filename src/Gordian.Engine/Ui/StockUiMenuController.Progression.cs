// src/Gordian.Engine/Ui/StockUiMenuController.Progression.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Resources.Tables;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Input;

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

        /// <summary>A Records of Eminence list's title (#493): "Current  2/30", "Objective List" or the category's name.</summary>
        public string RoeListTitle { get; internal set; } = string.Empty;

        /// <summary>What a Records of Eminence detail page shows.</summary>
        public StockUiRoeDetail? RoeDetail { get; internal set; }

        /// <summary>A title over a prompt's top edge ("Commands" on the merit question).</summary>
        public string? PromptTitle { get; internal set; }

        /// <summary>Whether a prompt's message is drawn upright from the top left (the Records of Eminence questions).</summary>
        public bool PromptTextLeft { get; internal set; }

        /// <summary>The controller behind this window, for the Quests window's figures.</summary>
        public StockUiMenuController? Controller => ItemOwner;

        /// <summary>The job points spent on the job the Gifts page shows, for its "Combo Total:".</summary>
        public int JobGiftsSpent { get; internal set; }

        /// <summary>A Records of Eminence list (#493).</summary>
        public bool IsRoeList => ProgressionList is StockUiProgressionList.RoeCategories or StockUiProgressionList.RoeObjectives or StockUiProgressionList.RoeCurrent;
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>The merit table's strings by id (ROM/169/75): categories at 2 n, merits at their id. Set by the HUD.</summary>
        public Func<int, string?>? MeritText { get; set; }

        /// <summary>The job point gifts' strings by id (ROM/324/59). Set by the HUD.</summary>
        public Func<int, string?>? JobGiftText { get; set; }

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

        /// <summary>The Quests window's figures (S2C 0x110, 0x112, 0x113); set by the session.</summary>
        public Func<StockUiRoeTotals>? RoeTotals { get; set; }

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
            StockUiProgressionList.JobPointCategories or StockUiProgressionList.JobGifts =>
                (StockUiStatusPages.JobPointMenu, StockUiProgression.JobPointCategoryHelp, StockUiStatusPage.JobPoints),
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
                    return (StockUiProgression.MeritCategoryRows(categories, MeritText, WeaponSkillsOpen), categories);
                }
                case StockUiProgressionList.Merits:
                    return StockUiProgression.MeritListRows(key, CurrentMerits(), MeritText, ShortText(StockUiProgression.NextLabel, "NEXT"), StatusPoints?.Invoke()?.MainJob ?? 0);
                case StockUiProgressionList.JobPointCategories:
                {
                    var categories = StockUiProgression.JobPointCategories(key, JobPointText);
                    return (StockUiProgression.JobPointCategoryRows(key, categories, JobPointLevel, JobPointText), categories);
                }
                case StockUiProgressionList.JobGifts:
                    return StockUiProgression.JobGiftRows(key, StockUiStatusPages.JobPointsOf(StatusPoints?.Invoke(), key).Spent, JobGiftText);
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
            // The Records of Eminence lists: quest00's frame with rows of their own.
            bool roe = list is StockUiProgressionList.RoeCategories or StockUiProgressionList.RoeObjectives or StockUiProgressionList.RoeCurrent;
            var definition = roe ? QuestLogDefinition(StockUiRecordsOfEminence.ListMenu, StockUiRecordsOfEminence.ListRows, StockUiQuestLog.KindRowX - StockUiStatusPages.ListRowInset,
                i => StockUiRecordsOfEminence.ListRowY + i * StockUiRecordsOfEminence.ListRowPitch, StockUiQuestLog.PickerRowWidth, StockUiQuestLog.PickerRowHeight, Array.Empty<int>()) : null;
            var open = OpenStatusList(page, menu, from, rows, help >= 0 ? new[] { help } : Array.Empty<int>(), cursor, progression: list, key: key, keys: keys, listWindow: definition);
            if (open != null && roe) open.RoeListTitle = RoeTitle(list, key);
            if (open != null && list == StockUiProgressionList.JobPointCategories) open.StatusValueRight = StockUiProgression.JobPointValueRight;
            if (open != null && list == StockUiProgressionList.Merits) open.StatusValueRight = StockUiProgression.MeritValueRight;
            return open;
        }

        /// <summary>A Records of Eminence list's title.</summary>
        private string RoeTitle(StockUiProgressionList list, int key)
        {
            if (list == StockUiProgressionList.RoeCurrent)
                return StockUiRecordsOfEminence.ListTitle(ShortText(StockUiRecordsOfEminence.CurrentText, "Current"), StockUiRecordsOfEminence.ActiveCount(RoeState?.Invoke()));
            if (list == StockUiProgressionList.RoeCategories) return ShortText(StockUiRecordsOfEminence.ObjectiveListText, "Objective List");
            return RoeTables?.Invoke()?.Category(key)?.Name ?? string.Empty;
        }

        /// <summary>Left / Right on a category's list: the category beside it in the list it was opened from (retail client). Call under the lock.</summary>
        private bool TryMoveRoeCategory(InputAction direction)
        {
            if (direction is not (InputAction.MenuLeft or InputAction.MenuRight) || Top is not { IsRoeList: true } top) return false;
            if (top.ProgressionList != StockUiProgressionList.RoeObjectives || top.Parent is not { IsRoeList: true } parent) return true;
            var siblings = new List<int>();
            foreach (int key in parent.RowKeys)
            {
                if (key >= RecordsOfEminenceTables.FirstCategoryRecord) siblings.Add(key);
            }
            int at = siblings.IndexOf(top.ProgressionKey);
            if (at < 0 || siblings.Count < 2) return true;
            top.ProgressionKey = siblings[(at + (direction == InputAction.MenuRight ? 1 : siblings.Count - 1)) % siblings.Count];
            var (rows, keys) = ProgressionRows(top.ProgressionList, top.ProgressionKey);
            top.Rows = rows;
            top.RowKeys = keys;
            top.RoeListTitle = RoeTitle(top.ProgressionList, top.ProgressionKey);
            top.FirstRow = 0;
            top.ScrollFrom = 0;
            top.SelectedButtonId = rows.Count > 0 ? 1 : 0;
            return true;
        }

        /// <summary>Whether Weapon Skills can be opened: the character holds Heart of the Bushin.</summary>
        private bool WeaponSkillsOpen => KeyItemHeld?.Invoke(StockUiProgression.WeaponSkillsKeyItem) == true;

        /// <summary>Left / Right on the Job Points window's pages: the job's categories and its gifts, the cursor back on the first row. Call under the lock.</summary>
        private bool TryMoveJobPointPage(InputAction direction)
        {
            if (direction is not (InputAction.MenuLeft or InputAction.MenuRight) || Top is not { } top) return false;
            if (top.ProgressionList is not (StockUiProgressionList.JobPointCategories or StockUiProgressionList.JobGifts)) return false;
            bool gifts = top.ProgressionList == StockUiProgressionList.JobPointCategories;
            top.ProgressionList = gifts ? StockUiProgressionList.JobGifts : StockUiProgressionList.JobPointCategories;
            top.StatusValueRight = gifts ? StockUiProgression.JobGiftValueRight : StockUiProgression.JobPointValueRight;
            top.JobGiftsSpent = StockUiStatusPages.JobPointsOf(StatusPoints?.Invoke(), top.ProgressionKey).Spent;
            var (rows, keys) = ProgressionRows(top.ProgressionList, top.ProgressionKey);
            top.Rows = rows;
            top.RowKeys = keys;
            top.FirstRow = 0;
            top.ScrollFrom = 0;
            top.SelectedButtonId = rows.Count > 0 ? 1 : 0;
            return true;
        }

        /// <summary>Confirm on a list of #491 / #493.</summary>
        private void ActivateProgressionList(StockUiOpenMenu list)
        {
            int rowKey = list.SelectedRowKey;
            switch (list.ProgressionList)
            {
                case StockUiProgressionList.MeritCategories when rowKey == StockUiProgression.WeaponSkillsCategory && !WeaponSkillsOpen:
                    break;
                case StockUiProgressionList.MeritCategories when rowKey > 0:
                    lock (_sync) OpenProgressionList(StockUiProgressionList.Merits, rowKey, list, 0);
                    Changed?.Invoke();
                    break;
                case StockUiProgressionList.Merits when rowKey > 0:
                    // The retail client: outside a Mog House it says so and opens nothing. Another job's merits are greyed:
                    // that Confirm does nothing there is PROVISIONAL (not tried on the retail client).
                    if (InMogHouse?.Invoke() != true)
                    {
                        NoticePosted?.Invoke(StockUiProgression.OutsideMogHouse);
                        break;
                    }
                    if (StockUiProgression.HasMeritGroups(list.ProgressionKey) && StockUiProgression.JobOfCategory(list.ProgressionKey) != StatusPoints?.Invoke()?.MainJob) break;
                    lock (_sync)
                    {
                        var edit = Push(StockUiProgression.MeritEditMenu, list, Array.Empty<string>(), null, null);
                        if (edit != null)
                        {
                            edit.StatusWindow = StockUiStatusPage.MeritPoints;
                            edit.ProgressionKey = rowKey;
                            // Lower is greyed at 0 upgrades, and Confirm on it does nothing (retail client).
                            int upgrades = 0;
                            foreach (var m in CurrentMerits())
                            {
                                if (m.Id == rowKey) upgrades = m.Count;
                            }
                            if (upgrades == 0) edit.SetGreyed(new HashSet<int> { StockUiProgression.MeritLowerButton });
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
            if (menu.ProgressionList == StockUiProgressionList.JobGifts) menu.JobGiftsSpent = StockUiStatusPages.JobPointsOf(StatusPoints?.Invoke(), menu.ProgressionKey).Spent;
            var (rows, keys) = ProgressionRows(menu.ProgressionList, menu.ProgressionKey);
            if (menu.IsRoeList) menu.RoeListTitle = RoeTitle(menu.ProgressionList, menu.ProgressionKey);
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
        /// <c>merit3</c>'s Raise or Lower: asks in the bare <c>yesno</c> box beneath it, the cursor on No (retail client, 2026-11-06;
        /// <c>merit3</c> stays open), then sends C2S 0x0BE kind 3. Raising needs the merit's next cost in merit points held.
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
            if (raise && merit is { } r && (StockUiProgression.NextCost(r) == 0 || (StatusPoints?.Invoke()?.MeritPoints ?? 0) < r.Next))
            {
                NoticePosted?.Invoke(StockUiProgression.NextCost(r) == 0 ? $"{name} cannot be raised any further." : $"You do not have enough merit points to raise {name}.");
                return;
            }
            if (MeritChanged is not { } send)
            {
                NoticePosted?.Invoke("Merit Points: this is not available in this session.");
                return;
            }
            var prompt = OpenPlacedPrompt(StockUiMenuEntries.YesNoMenu, UiAnchor.TopRight, StockUiProgression.MeritPromptX, StockUiProgression.MeritPromptY, null, false, answer =>
            {
                if (answer == true) _ = send(meritId, raise);
            });
            if (prompt != null) prompt.PromptTitle = StockUiProgression.MeritPromptTitle;
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
                NoticePosted?.Invoke(StockUiProgression.OutsideMogHouse);
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

        /// <summary>The Quests window's rows: Current, Completed, and Records of Eminence's Current ("11/30") and Objective List.</summary>
        private List<StockUiListRow> QuestKindRows() => new()
        {
            new(1, StockUiQuestLog.CurrentKind, false),
            new(2, StockUiQuestLog.CompletedKind, false),
            new(StockUiRecordsOfEminence.CurrentButton, ShortText(StockUiRecordsOfEminence.CurrentText, "Current"), false,
                Value: string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"{StockUiRecordsOfEminence.ActiveCount(RoeState?.Invoke())}/{StockUiRecordsOfEminence.ActiveLimit}")),
            new(StockUiRecordsOfEminence.ObjectiveListButton, ShortText(StockUiRecordsOfEminence.ObjectiveListText, "Objective List"), false),
        };

        /// <summary>Records of Eminence's Current or Objective List (C2S 0x117 first, as retail does).</summary>
        private void OpenRoeList(int button, StockUiOpenMenu parent)
        {
            if (RoeTables?.Invoke() == null)
            {
                NoticePosted?.Invoke("Records of Eminence: the objective tables are not loaded in this session.");
                return;
            }
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
        /// Confirm on a Records of Eminence list: a category opens its children; an objective opens its detail page, with the
        /// question to remove it (C2S 0x10D) when taken or to take it (C2S 0x10C) otherwise; none when it is completed and cannot
        /// be repeated, or is the time-limited record (retail client, 2026-11-06).
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
            if (tables.Objective(id) is not { } objective) return;
            var state = RoeState?.Invoke();
            var slot = state?.SlotOf(id);
            bool done = slot == null && !objective.Repeatable && state?.Completed(id) == true;
            bool limited = slot is { Slot: >= StockUiRecordsOfEminence.ActiveLimit };
            var detail = new StockUiRoeDetail(objective, done ? objective.Required : slot?.Progress ?? 0, slot != null, done, limited);
            var page = OpenRoeDetail(detail);
            if (page == null || done || limited) return;
            if (slot is { } taken)
            {
                if (RoeRemoved is not { } remove) return;
                string question = ShortText(StockUiRecordsOfEminence.RemovePromptText, "Remove objective?");
                if (taken.Progress > 0) question += " " + ShortText(StockUiRecordsOfEminence.RemoveWarningText, "Your progress will be reset if you cancel this objective. Are you sure?");
                OpenRoePrompt(question, defaultYes: false, () =>
                {
                    CloseRoeDetail(page);
                    _ = remove(id);
                });
                return;
            }
            if (RoeStarted is not { } start) return;
            OpenRoePrompt(ShortText(StockUiRecordsOfEminence.SetPromptText, "Set objective?"), defaultYes: true, () =>
            {
                CloseRoeDetail(page);
                _ = start(id);
            });
        }

        /// <summary>The question under an objective's detail page: <c>comyn</c> at the page's foot, its text at the top left (retail client, 2026-11-06).</summary>
        private void OpenRoePrompt(string question, bool defaultYes, Action yes)
        {
            var prompt = OpenPlacedPrompt(StockUiMenuEntries.MessageYesNoMenu, UiAnchor.TopLeft, StockUiRecordsOfEminence.PromptX, StockUiRecordsOfEminence.PromptY, question, defaultYes, answer =>
            {
                if (answer == true) yes();
            });
            if (prompt != null) prompt.PromptTextLeft = true;
        }

        /// <summary>
        /// A yes / no window at a place of its own, not its DAT's: the retail client puts these two beside the window that asks.
        /// <paramref name="answered"/> as <see cref="OpenYesNo"/>'s.
        /// </summary>
        private StockUiOpenMenu? OpenPlacedPrompt(string menuName, UiAnchor anchor, int x, int y, string? message, bool defaultYes, Action<bool?> answered)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(menuName, out var window)) return null;
            var f = window.Frame;
            var definition = new UiMenuDefinition
            {
                DatId = window.DatId, Category = window.Category, Name = window.Name, MenuType = window.MenuType, Buttons = window.Buttons,
                Frame = new UiMenuFrame
                {
                    X = (short)x, Y = (short)y, Width = f.Width, Height = f.Height, AuthoredHeight = f.AuthoredHeight, CursorOffsetX = f.CursorOffsetX,
                    CursorOffsetY = f.CursorOffsetY, DrawOffsetX = f.DrawOffsetX, DrawOffsetY = f.DrawOffsetY, Anchor = anchor, Shapes = f.Shapes,
                    HelpTextId = f.HelpTextId, TitleTextId = f.TitleTextId,
                },
            };
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            StockUiOpenMenu prompt;
            lock (_sync)
            {
                prompt = new StockUiOpenMenu(definition, Top, Array.Empty<string>(), message) { Prompt = tcs };
                prompt.SelectedButtonId = defaultYes ? 1 : 2;
                PushOpen(prompt);
            }
            _ = tcs.Task.ContinueWith(t => answered(prompt.Cancelled ? null : t.Result), TaskScheduler.Default);
            Changed?.Invoke();
            return prompt;
        }

        /// <summary>An objective's detail page in <c>quest00</c>'s frame.</summary>
        private StockUiOpenMenu? OpenRoeDetail(StockUiRoeDetail detail)
        {
            var definition = QuestLogDefinition(StockUiRecordsOfEminence.ListMenu, 0, 0, 0, 0, 0, 0);
            if (definition == null) return null;
            StockUiOpenMenu page;
            lock (_sync)
            {
                page = new StockUiOpenMenu(definition, Top, Array.Empty<string>(), null)
                {
                    QuestLogView = StockUiQuestLogView.RoeDetail,
                    IsQuestsWindow = true,
                    RoeDetail = detail,
                    ItemOwner = this,
                };
                PushOpen(page);
            }
            Changed?.Invoke();
            return page;
        }

        /// <summary>Closes a detail page once its question is answered Yes (PROVISIONAL: Yes was not pressed on the retail client).</summary>
        private void CloseRoeDetail(StockUiOpenMenu page)
        {
            lock (_sync)
            {
                if (_open.Length > 0 && ReferenceEquals(_open[^1], page)) _open = _open[..^1];
            }
            Changed?.Invoke();
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
