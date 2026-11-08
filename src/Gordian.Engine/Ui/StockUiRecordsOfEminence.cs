// src/Gordian.Engine/Ui/StockUiRecordsOfEminence.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using Gordian.Core.Resources.Tables;

namespace Gordian.Engine.Ui
{
    /// <summary>One objective S2C 0x111 lists: its slot (0-29 taken, 63 LandSandBoat's time-limited one), id and progress.</summary>
    public readonly record struct StockUiRoeSlot(int Slot, int ObjectiveId, int Progress);

    /// <summary>The Records of Eminence log as the server left it: the objectives taken (S2C 0x111) and the completed bits (S2C 0x112).</summary>
    public sealed record StockUiRoeState(IReadOnlyList<StockUiRoeSlot> Slots, Func<int, bool> Completed)
    {
        /// <summary>Whether a zone has been visited (S2C 0x008); null when not known, and every zone counts.</summary>
        public Func<int, bool>? ZoneVisited { get; init; }

        /// <summary>The character's Unity leader (S2C 0x061 <c>unity_info</c>, 1-11, 0 none).</summary>
        public int UnityLeader { get; init; }

        /// <summary>The rotation of the Unity (Shared) and Unity (leader) objectives (S2C 0x110); -1 before the first, and any counts.</summary>
        public int UnitySharedRotation { get; init; } = -1;

        /// <inheritdoc cref="UnitySharedRotation"/>
        public int UnityLeaderRotation { get; init; } = -1;

        public StockUiRoeSlot? SlotOf(int objectiveId)
        {
            foreach (var s in Slots)
            {
                if (s.ObjectiveId == objectiveId) return s;
            }
            return null;
        }
    }

    /// <summary>
    /// What an objective's detail page shows: the objective, its progress, and whether it is taken, done for good, or the
    /// time-limited record (LandSandBoat's slot 63, which cannot be removed).
    /// </summary>
    public sealed record StockUiRoeDetail(RoeObjective Objective, int Progress, bool Taken, bool Done, bool LimitedTime);

    /// <summary>The figures of the Quests window's foot (image-34): objectives taken, S2C 0x110's sparks, deeds and plaudits, Unity accolades, S2C 0x112's count.</summary>
    public readonly record struct StockUiRoeTotals(int Active, long Sparks, long Accolades, int Deeds, int Plaudits, int Completed);

    /// <summary>
    /// The Records of Eminence windows (#493), from the client's objective and category tables
    /// (<see cref="RecordsOfEminenceTables"/>), the short strings (ROM/165/77 108-135: 108 "Records of Eminence", 109 "Current",
    /// 110 "Objective List", 111 "Set objective?", 112 "This objective cannot be undertaken again.", 114 "No current objectives.",
    /// 115 "Remove objective?", 116 "Your progress will be reset if you cancel this objective. Are you sure?", 129 / 130 "Number
    /// Required" / "Number Completed", 131 "Rewards:", 123 / 124 / 121 " Spa." / " EXP" / "Unity Accolades", 520 "Vana・Bout")
    /// and XiPackets (C2S 0x10C / 0x10D carry the objective id; C2S 0x117 is sent as Current and Objective List open):
    /// <list type="bullet">
    /// <item>Main menu Quests (<c>quest00</c>; retail image-34) shows, under Current and Completed, the heading "Records of
    /// Eminence" over its Current ("11/30" objectives taken) and Objective List, then Vana'Bout, greyed (it is not active in
    /// retail, no packet for it is known), and the Plaudits, Sparks, Unity Accolades, Deeds and Number Completed figures.</item>
    /// <item>The lists, as the retail client draws them on local LandSandBoat (2026-11-06; Retail Captures, <c>RoE_*</c>; the
    /// progress is LSB's): <c>quest00</c>'s frame, a title between red arrows ("Current 2/30", "Objective List", the category's
    /// name), eight rows of 25 px. Objective List walks the category tree; Left / Right steps to the category beside it. A
    /// category has a small bullet; an objective a green ball, its name and a bar with "progress/needed" under the row's right
    /// end, teal when taken; one completed has a small bullet, a grey name and a full pink bar.</item>
    /// <item>Confirm on an objective opens its detail page in the same frame (the name, the text, "Number Required :", "Rewards:",
    /// "(Not Repeatable)"), with the question under it when there is one: 111 "Set objective?", the cursor on Yes, then C2S
    /// 0x10C; 115 "Remove objective?", the cursor on No, then C2S 0x10D. A completed objective and the time-limited record ask
    /// nothing. Answering No leaves the page up.</item>
    /// <item>Current lists S2C 0x111's objectives in slot order.</item>
    /// <item>The server sends no list of what is on offer (S2C 0x112 carries only the completed bits): as retail must, the
    /// client decides from the category table's conditions (<see cref="RoeCondition"/>). A child shows when they all hold, and
    /// a category only when something under it shows.</item>
    /// <item>PROVISIONAL: 116, the warning on removing, is added when the objective has progress (seen only at 0, where it does
    /// not show), and the page closing after Yes (Yes was not pressed).</item>
    /// </list>
    /// </summary>
    public static class StockUiRecordsOfEminence
    {
        public const string ListMenu = "quest00";

        /// <summary>ROM/165/77 ids.</summary>
        public const int TitleText = 108, CurrentText = 109, ObjectiveListText = 110, SetPromptText = 111, NotAgainText = 112,
            NoCurrentText = 114, RemovePromptText = 115, RemoveWarningText = 116, RequiredText = 129, CompletedText = 130,
            RewardsText = 131, SparksText = 123, ExpText = 124, AccoladesText = 121, HelpTitleText = 133, VanaBoutText = 520;

        public const string Title = "Records of Eminence", VanaBoutTitle = "Vana'Bout";

        /// <summary>The help bar's title over the RoE windows: ROM/165/77 133 "RoE" (the full name does not fit the title box).</summary>
        public const string HelpTitle = "RoE";

        /// <summary><c>quest00</c>'s Records of Eminence rows.</summary>
        public const int CurrentButton = 3, ObjectiveListButton = 4;

        /// <summary>The most objectives taken at once (LandSandBoat; retail image-34's "11/30").</summary>
        public const int ActiveLimit = 30;

        /// <summary>ROM/165/77 ids of the Quests window's foot.</summary>
        public const int SparksLabel = 120, AccoladesLabel = 121, DeedsLabel = 122, PlauditsLabel = 521;

        /// <summary>Current's figure: the objectives taken of the most, the time-limited record among them (retail client: "2/30" with one of each).</summary>
        public static int ActiveCount(StockUiRoeState? state) => state?.Slots.Count ?? 0;

        /// <summary>The window title of a list.</summary>
        public static string ListTitle(string current, int active) => string.Create(CultureInfo.InvariantCulture, $"{current}  {active}/{ActiveLimit}");

        /// <summary>The lists' eight rows in <c>quest00</c>'s frame (retail client, 2026-11-06).</summary>
        public const int ListRows = 8, ListRowY = 39, ListRowPitch = 25;

        /// <summary>Where the question stands: <c>comyn</c>, under the detail page's foot (retail client, 2026-11-06).</summary>
        public const int PromptX = 130, PromptY = 296;

        /// <summary>The detail page's labels.</summary>
        public const string NumberRequiredLabel = "Number Required :", NotRepeatableLabel = "(Not Repeatable)", LimitedTimeLabel = "[Limited-time Challenge]";

        /// <summary>The rewards line ("300 Spa.  1500 EXP  300 Acc."; retail client, 2026-11-06).</summary>
        public static string RewardLine(RoeObjective objective, Func<int, string?>? text)
        {
            string T(int id, string fallback) => text?.Invoke(id) is { Length: > 0 } s ? s : fallback;
            var parts = new List<string>(3);
            string N(int value) => value.ToString(CultureInfo.InvariantCulture);
            if (objective.Sparks > 0) parts.Add(N(objective.Sparks) + T(SparksText, " Spa."));
            if (objective.Experience > 0) parts.Add(N(objective.Experience) + T(ExpText, " EXP"));
            if (objective.Accolades > 0) parts.Add(N(objective.Accolades) + " Acc.");
            return string.Join("  ", parts);
        }

        private static bool Holds(RoeCondition c, StockUiRoeState state) => c.Kind switch
        {
            RoeCondition.ObjectiveCompleted => state.Completed(c.Value),
            RoeCondition.ZoneVisited => state.ZoneVisited?.Invoke(c.Value) != false,
            RoeCondition.UnityLeader => state.UnityLeader == c.Value,
            RoeCondition.UnityRotation when c.Value >= RoeCondition.LeaderRotationBase =>
                state.UnityLeaderRotation < 0 || state.UnityLeaderRotation == c.Value - RoeCondition.LeaderRotationBase,
            RoeCondition.UnityRotation => state.UnitySharedRotation < 0 || state.UnitySharedRotation == c.Value,
            _ => true,
        };

        /// <summary>Whether <paramref name="child"/> of a category shows: its conditions hold.</summary>
        public static bool IsVisible(RoeChild child, StockUiRoeState? state)
        {
            if (state == null) return true;
            foreach (var c in child.Conditions)
            {
                if (!Holds(c, state)) return false;
            }
            return true;
        }

        /// <summary>Whether a category has an objective to show, at any depth.</summary>
        private static bool HasObjectives(RecordsOfEminenceTables tables, RoeCategory category, StockUiRoeState? state, int depth = 0)
        {
            if (depth > 8) return false;
            foreach (var child in category.Children)
            {
                if (!IsVisible(child, state)) continue;
                if (child.IsObjective ? tables.Objective(child.ObjectiveId) != null
                    : tables.Category(child.RecordId) is { } sub && HasObjectives(tables, sub, state, depth + 1)) return true;
            }
            return false;
        }

        /// <summary>
        /// The rows of a category's list and the record id each stands for: subcategories that hold an objective to show by name,
        /// objectives by name with their progress when taken, greyed when completed and not repeatable. Unknown records are left out.
        /// </summary>
        public static (IReadOnlyList<StockUiListRow> Rows, IReadOnlyList<int> Keys) CategoryRows(RecordsOfEminenceTables tables, int categoryRecord,
            StockUiRoeState? state)
        {
            var rows = new List<StockUiListRow>();
            var keys = new List<int>();
            IEnumerable<RoeChild> children;
            if (categoryRecord == RecordsOfEminenceTables.RootCategory)
            {
                var top = new List<RoeChild>();
                foreach (var child in tables.Category(categoryRecord)?.Children ?? (IEnumerable<RoeChild>)Array.Empty<RoeChild>()) top.Add(child);
                children = top;
            }
            else children = tables.Category(categoryRecord)?.Children ?? (IEnumerable<RoeChild>)Array.Empty<RoeChild>();
            foreach (var child in children)
            {
                if (!IsVisible(child, state)) continue;
                if (child.IsObjective)
                {
                    if (tables.Objective(child.ObjectiveId) is not { } o) continue;
                    rows.Add(ObjectiveRow(rows.Count + 1, o, state));
                    keys.Add(child.RecordId);
                }
                else if (tables.Category(child.RecordId) is { Name.Length: > 0 } c && HasObjectives(tables, c, state))
                {
                    rows.Add(new StockUiListRow(rows.Count + 1, c.Name, false));
                    keys.Add(child.RecordId);
                }
            }
            return (rows, keys);
        }

        /// <summary>An objective's row: its name and "progress/required" (full when completed), marked when taken, greyed when completed and not repeatable.</summary>
        public static StockUiListRow ObjectiveRow(int buttonId, RoeObjective objective, StockUiRoeState? state)
        {
            var slot = state?.SlotOf(objective.Id);
            bool done = state?.Completed(objective.Id) == true && !objective.Repeatable && slot == null;
            int progress = done ? objective.Required : slot?.Progress ?? 0;
            return new StockUiListRow(buttonId, objective.Name, slot != null, done ? StockUiProgression.Greyed : null,
                string.Create(CultureInfo.InvariantCulture, $"{progress}/{objective.Required}"));
        }

        /// <summary>Current's rows (the objectives taken, in S2C 0x111's slot order) and each one's record id.</summary>
        public static (IReadOnlyList<StockUiListRow> Rows, IReadOnlyList<int> Keys) CurrentRows(RecordsOfEminenceTables tables, StockUiRoeState? state)
        {
            var rows = new List<StockUiListRow>();
            var keys = new List<int>();
            if (state == null) return (rows, keys);
            foreach (var slot in state.Slots)
            {
                string name = tables.Objective(slot.ObjectiveId)?.Name ?? string.Create(CultureInfo.InvariantCulture, $"Objective {slot.ObjectiveId}");
                int required = tables.Objective(slot.ObjectiveId)?.Required ?? 1;
                rows.Add(new StockUiListRow(rows.Count + 1, name, true, Value: string.Create(CultureInfo.InvariantCulture, $"{slot.Progress}/{required}")));
                keys.Add(RecordsOfEminenceTables.FirstObjectiveRecord + slot.ObjectiveId);
            }
            return (rows, keys);
        }

    }
}
