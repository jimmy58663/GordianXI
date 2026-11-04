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
    /// <item>Objective List walks the category
    /// tree (the eleven top categories, then their subcategories, then the objectives), each level a list in <c>meritcat</c>'s
    /// frame; an objective taken shows its progress ("37/200"), one completed that cannot be repeated is greyed and posts 112.
    /// Confirm asks 111 and sends C2S 0x10C; on an objective taken it asks 115 and sends C2S 0x10D.</item>
    /// <item>Current lists S2C 0x111's objectives in slot order with their progress; Confirm asks 115 / 116 and sends C2S 0x10D.</item>
    /// <item>The server sends no list of what is on offer (S2C 0x112 carries only the completed bits): as retail must, the
    /// client decides from the category table's conditions (<see cref="RoeCondition"/>). A child shows when they all hold, and
    /// a category only when something under it shows.</item>
    /// <item>PROVISIONAL: the lists' frames and the rows' look (no retail capture of them yet).</item>
    /// </list>
    /// </summary>
    public static class StockUiRecordsOfEminence
    {
        public const string ListMenu = "meritcat";

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

        /// <summary>Current's figure: the objectives taken of the most ("11/30"; the time-limited record in slot 63 does not count).</summary>
        public static int ActiveCount(StockUiRoeState? state)
        {
            int count = 0;
            if (state == null) return count;
            foreach (var s in state.Slots)
            {
                if (s.Slot < ActiveLimit) count++;
            }
            return count;
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

        /// <summary>An objective's row: its name, "progress/required" when taken, greyed when completed and not repeatable.</summary>
        public static StockUiListRow ObjectiveRow(int buttonId, RoeObjective objective, StockUiRoeState? state)
        {
            var slot = state?.SlotOf(objective.Id);
            string? value = slot is { } s ? string.Create(CultureInfo.InvariantCulture, $"{s.Progress}/{objective.Required}") : null;
            bool done = state?.Completed(objective.Id) == true && !objective.Repeatable;
            return new StockUiListRow(buttonId, objective.Name, slot != null, done && slot == null ? StockUiProgression.Greyed : null, value);
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

        /// <summary>
        /// The help bar's line for an objective: "Number Completed 37/200  Rewards: 1,000 Spa. 5,000 EXP" (the strings of
        /// ROM/165/77; PROVISIONAL layout), or null for a category.
        /// </summary>
        public static string? HelpLine(RoeObjective? objective, StockUiRoeState? state, Func<int, string?>? text)
        {
            if (objective == null) return null;
            string T(int id, string fallback) => text?.Invoke(id) is { Length: > 0 } s ? s : fallback;
            int progress = state?.SlotOf(objective.Id)?.Progress ?? 0;
            var line = new System.Text.StringBuilder();
            line.Append(T(CompletedText, "Number Completed")).Append(' ').Append(progress.ToString(CultureInfo.InvariantCulture))
                .Append('/').Append(objective.Required.ToString(CultureInfo.InvariantCulture)).Append("  ").Append(T(RewardsText, "Rewards:"));
            if (objective.Sparks > 0) line.Append(' ').Append(StockUiStatus.Number(objective.Sparks)).Append(T(SparksText, " Spa."));
            if (objective.Experience > 0) line.Append(' ').Append(StockUiStatus.Number(objective.Experience)).Append(T(ExpText, " EXP"));
            if (objective.Accolades > 0) line.Append(' ').Append(StockUiStatus.Number(objective.Accolades)).Append(' ').Append(T(AccoladesText, "Unity Accolades"));
            return line.ToString();
        }
    }
}
