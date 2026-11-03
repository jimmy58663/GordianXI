// src/Gordian.Engine/Ui/StockUiProgression.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Gordian.Engine.Ui
{
    /// <summary>What a Status menu entry added by #491 does (<see cref="StockUiMenuCommand.Progression"/>'s Argument).</summary>
    public enum StockUiProgressionAction
    {
        None = 0,

        /// <summary>Merit Points' Categories (<c>merit1</c> button 1): the categories list.</summary>
        MeritCategories,

        /// <summary><c>merit3</c>'s Raise.</summary>
        MeritRaise,

        /// <summary><c>merit3</c>'s Lower.</summary>
        MeritLower,

        /// <summary><c>ut_menu</c>'s Unity Info.</summary>
        UnityInfo,

        /// <summary><c>ut_menu</c>'s Ranking Info.</summary>
        UnityRanking,

        /// <summary><c>ut_menu</c>'s Unity Lists.</summary>
        UnityLists,
    }

    /// <summary>Which list of #491 / #493 a <see cref="StockUiListKind.StatusValues"/> window is.</summary>
    public enum StockUiProgressionList
    {
        None = 0,
        MeritCategories,
        Merits,
        JobPointCategories,
        UnityInfo,
        UnityRanking,
        UnityLeaders,
        RoeCategories,
        RoeObjectives,
        RoeCurrent,
    }

    /// <summary>One merit S2C 0x08C listed: its id (category id + 2 k), the upgrades and the next upgrade's cost (0: none).</summary>
    public readonly record struct StockUiMerit(int Id, int Next, int Count);

    /// <summary>One job point category of a job (S2C 0x08D): its level and the job points the next level costs.</summary>
    public readonly record struct StockUiJobPointCategory(int Level, int Next);

    /// <summary>
    /// The Unity windows' figures (#491, #94): the character's Unity (S2C 0x061 <c>unity_info</c>, leader 1-11, 0 none), its
    /// personal evaluation (0x061 <c>unity_points1</c> partial / <c>unity_points2</c>) and each Unity's points for last week
    /// and this week (S2C 0x063 type 0x07, answered to C2S 0x116).
    /// </summary>
    public sealed record StockUiUnityInfo(int Leader, int PartialEvaluation, int Evaluation, IReadOnlyList<uint> LastWeekPoints, IReadOnlyList<uint> ThisWeekPoints);

    /// <summary>
    /// The Merit Points Categories, Job Points spending and Unity windows (#491, #94), from the English menu DAT (39542,
    /// ROM/119/51; menu dump 2026-11-03), the merit table (ROM/169/75, <c>DMsgCategory.MeritStrings</c>), the job point table
    /// (ROM/314/62) and the short strings (ROM/165/77 395-453 for Unity):
    /// <list type="bullet">
    /// <item>Merit Points > Categories: <c>meritcat</c> (366 x 190 at 16,48, title 130 "Categories", 11 rows, help 609) lists
    /// the categories with their upgrades and most ("12/75"); Confirm lists the category's merits in <c>merit2ca</c> (the same
    /// frame) with each merit's upgrades; <c>meritinf</c> (16,240: "Merit Points", "Combo Total/Max" baked in) shows the merit
    /// points held and the category's total. Confirm on a merit opens <c>merit3</c> (Raise, help 612 / Lower, help 613), each
    /// asking first, then sends C2S 0x0BE kind 3 (Param1 1 raise / 0 lower, Param2 the merit id / 2: LandSandBoat shifts it
    /// back). LandSandBoat allows it in a Mog House only and sends each merit's next cost as 0 elsewhere.</item>
    /// <item>Job Points: Confirm on a job lists its ten categories (ROM/314/62 at job x 64 + 2 k) with "level/20" in
    /// <c>merit2ca</c> and <c>merit2in</c> (16,240: "Job Points", "Combo Total/Max"); Confirm asks, then sends C2S 0x0BF with
    /// the category's index (job x 32 + k; LandSandBoat's <c>JOBPOINT_TYPE</c>), allowed in a Mog House only. A level costs
    /// its number plus one in job points (LandSandBoat <c>JobPointCost</c>; S2C 0x08D <c>next</c>), to 20.</item>
    /// <item>Unity: opening <c>ut_menu</c> sends C2S 0x116 kinds 0 and 1 (XiPackets: retail sends both, each answered by 32 S2C
    /// 0x063). Unity Info lists "Current Unity:", the rankings, the personal evaluations and "Unity chat settings:" with
    /// Active / Inactive (C2S 0x118; the maintainer's capture <c>Main_Menu_Page1.mp4</c> 1:00-1:10); Ranking Info sends C2S
    /// 0x117 and ranks the eleven Unities by this week's points; Unity Lists lists the leaders (help 892 "Select a Unity.").
    /// PROVISIONAL: every window but <c>ut_menu</c> borrows <c>meritcat</c>'s frame (no DAT window names them).</item>
    /// </list>
    /// </summary>
    public static class StockUiProgression
    {
        public const string MeritCategoryMenu = "meritcat", MeritListMenu = "merit2ca", MeritEditMenu = "merit3";
        public const string MeritInfoBox = "meritinf", JobPointInfoBox = "merit2in";

        /// <summary><c>merit3</c>'s buttons.</summary>
        public const int MeritRaiseButton = 1, MeritLowerButton = 2;

        /// <summary>Help lines (ROM/165/75): 609 the categories, 612 Raise (the merits list), 800 the job point categories, 892 the leaders.</summary>
        public const int MeritCategoryHelp = 609, MeritListHelp = 612, JobPointCategoryHelp = 800, UnityListHelp = 892;

        /// <summary>The merit categories' count (ROM/169/75 names them at 2 .. 56: HP/MP 1 ... Rune Fencer 28).</summary>
        public const int MeritCategoryCount = 28;

        /// <summary>The first job category (Warrior, 384 = 6 x 64) and the job categories' second group's base (0x800).</summary>
        private const int FirstJobCategory = 6, WeaponSkillsCategory = 26, SecondGroupBase = 0x800;

        /// <summary>A category's S2C 0x08C id (64 n): its merits' ids are this plus 2 k.</summary>
        public static int MeritCategoryId(int category) => category * 64;

        /// <summary>
        /// A job category's second group's id (LandSandBoat <c>xi.meritCategory</c> <c>WAR_GROUP_2</c> = 2048 ... <c>RUN_GROUP_2</c>
        /// = 3456: 0x800 + (n - 6) x 64); -1 for the other categories.
        /// </summary>
        public static int SecondGroupId(int category) =>
            category >= FirstJobCategory && category != WeaponSkillsCategory && category <= MeritCategoryCount
                ? SecondGroupBase + (category - FirstJobCategory) * 64 : -1;

        /// <summary>Which category a merit id belongs to (its first or second group), or 0.</summary>
        public static int CategoryOfMerit(int meritId)
        {
            int group = meritId & ~63;
            if (group >= SecondGroupBase) return (group - SecondGroupBase) / 64 + FirstJobCategory;
            int category = group / 64;
            return category is >= 1 and <= MeritCategoryCount ? category : 0;
        }

        /// <summary>Whether a merit table name is an unused slot ("MeripoName29", Japanese placeholders).</summary>
        public static bool IsPlaceholder(string? name) =>
            string.IsNullOrWhiteSpace(name) || name.StartsWith("MeripoName", StringComparison.Ordinal) || name[0] > 0x2FFF;

        private static readonly Regex MaxIncreases = new(@"max\. total ability increases: (\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// A category's most upgrades, read from its line in ROM/169/75 ("Max. total ability increases: 75"; a job's two
        /// groups add up: "Group 1 ... 10", "Group 2 ... 10"); 0 when the line names none (Weapon Skills).
        /// </summary>
        public static int CategoryMax(string? help)
        {
            if (string.IsNullOrEmpty(help)) return 0;
            int total = 0;
            foreach (Match m in MaxIncreases.Matches(help)) total += int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            return total;
        }

        /// <summary>The categories that have a name in the table, in table order (HP/MP ... Rune Fencer).</summary>
        public static IReadOnlyList<int> MeritCategories(Func<int, string?>? text)
        {
            var list = new List<int>(MeritCategoryCount);
            for (int c = 1; c <= MeritCategoryCount; c++)
            {
                if (!IsPlaceholder(text?.Invoke(c * 2))) list.Add(c);
            }
            return list;
        }

        /// <summary>The upgrades of every merit of a category (both groups of a job).</summary>
        public static int CategoryTotal(int category, IReadOnlyList<StockUiMerit> merits)
        {
            int total = 0;
            foreach (var m in merits)
            {
                if (CategoryOfMerit(m.Id) == category) total += m.Count;
            }
            return total;
        }

        /// <summary>The categories list's rows: the name and "total/most" ("12/75"; the total alone when no most is known).</summary>
        public static IReadOnlyList<StockUiListRow> MeritCategoryRows(IReadOnlyList<int> categories, IReadOnlyList<StockUiMerit> merits, Func<int, string?>? text)
        {
            var rows = new List<StockUiListRow>(categories.Count);
            foreach (int c in categories)
            {
                int max = CategoryMax(text?.Invoke(c * 2 + 1));
                int total = CategoryTotal(c, merits);
                string value = max > 0 ? string.Create(CultureInfo.InvariantCulture, $"{total}/{max}") : total.ToString(CultureInfo.InvariantCulture);
                rows.Add(new StockUiListRow(rows.Count + 1, text?.Invoke(c * 2) ?? string.Empty, false, Value: value));
            }
            return rows;
        }

        /// <summary>A category's merits that S2C 0x08C listed and the table names, in id order (the first group, then the second).</summary>
        public static IReadOnlyList<StockUiMerit> MeritsOf(int category, IReadOnlyList<StockUiMerit> merits, Func<int, string?>? text)
        {
            var list = new List<StockUiMerit>();
            foreach (var m in merits)
            {
                if (CategoryOfMerit(m.Id) == category && !IsPlaceholder(text?.Invoke(m.Id))) list.Add(m);
            }
            list.Sort((a, b) => a.Id.CompareTo(b.Id));
            return list;
        }

        /// <summary>The merits list's rows: the name and the upgrades; a merit that cannot be raised now (no next cost) is greyed.</summary>
        public static IReadOnlyList<StockUiListRow> MeritRows(IReadOnlyList<StockUiMerit> merits, Func<int, string?>? text)
        {
            var rows = new List<StockUiListRow>(merits.Count);
            foreach (var m in merits)
            {
                rows.Add(new StockUiListRow(rows.Count + 1, text?.Invoke(m.Id) ?? string.Empty, false, m.Next == 0 ? Greyed : null,
                    m.Count.ToString(CultureInfo.InvariantCulture)));
            }
            return rows;
        }

        /// <summary>The lists' greyed rows' colour: the lavender grey of a greyed magic or ability row (half scale).</summary>
        public static readonly Gordian.Core.Resources.Ui.UiColor Greyed = new(0x4F, 0x4F, 0x65, 0x80);

        /// <summary>The job point categories per job (LandSandBoat <c>JOBPOINTS_JPTYPE_PER_CATEGORY</c>) and their highest level.</summary>
        public const int JobPointCategoriesPerJob = 10, MaxJobPointLevel = 20;

        /// <summary>A job point category's name in ROM/314/62 (job x 64 + 2 k: 64 "Mighty Strikes Effect"); its line follows.</summary>
        public static int JobPointCategoryNameId(int job, int category) => job * 64 + category * 2;

        /// <summary>The index C2S 0x0BF and S2C 0x08D carry for a job's category: job x 32 + k (LandSandBoat <c>JOBPOINT_TYPE</c>).</summary>
        public static ushort JobPointIndex(int job, int category) => (ushort)(job * 32 + category);

        /// <summary>The categories of a job the table names (k = 0 ... 9), in order.</summary>
        public static IReadOnlyList<int> JobPointCategories(int job, Func<int, string?>? text)
        {
            var list = new List<int>(JobPointCategoriesPerJob);
            for (int k = 0; k < JobPointCategoriesPerJob; k++)
            {
                if (!IsPlaceholder(text?.Invoke(JobPointCategoryNameId(job, k)))) list.Add(k);
            }
            return list;
        }

        /// <summary>The job point categories' rows: the name and "level/20".</summary>
        public static IReadOnlyList<StockUiListRow> JobPointCategoryRows(int job, IReadOnlyList<int> categories, Func<int, int, StockUiJobPointCategory>? levels,
            Func<int, string?>? text)
        {
            var rows = new List<StockUiListRow>(categories.Count);
            foreach (int k in categories)
            {
                int level = levels?.Invoke(job, k).Level ?? 0;
                rows.Add(new StockUiListRow(rows.Count + 1, text?.Invoke(JobPointCategoryNameId(job, k)) ?? string.Empty, false,
                    level >= MaxJobPointLevel ? Greyed : null, string.Create(CultureInfo.InvariantCulture, $"{level}/{MaxJobPointLevel}")));
            }
            return rows;
        }

        /// <summary>A job point level's cost: its number plus one (LandSandBoat <c>JobPointCost</c>); 0 at the top.</summary>
        public static int JobPointCost(int level) => level >= MaxJobPointLevel ? 0 : level + 1;

        /// <summary>The Unity leaders (1-11), ROM/165/77 403-413 ("Pieuje" ... "Sylvie"); 402 "None" and 418 "No Unity".</summary>
        public const int UnityLeaderCount = 11, FirstUnityName = 403, NoUnityName = 402;

        /// <summary>Unity Info's labels (ROM/165/77): 397 "Current Unity:", 398 / 399 the rankings, 452 / 453 the evaluations, 439 "Unity chat settings:", 440 / 441 Active / Inactive, 434 "--".</summary>
        public const int CurrentUnityLabel = 397, RankingLabel = 398, PartialRankingLabel = 399, EvaluationLabel = 452, PartialEvaluationLabel = 453,
            ChatSettingsLabel = 439, ChatActiveLabel = 440, ChatInactiveLabel = 441, NoValueText = 434, UnityInfoTitle = 395, RankingTitle = 396;

        /// <summary>Unity Info's rows (<see cref="UnityInfoRows"/>): the Active and Inactive rows are the last two.</summary>
        public const int UnityChatActiveRow = 6, UnityChatInactiveRow = 7;

        /// <summary>A Unity's rank (1-11) by <paramref name="points"/> (more points rank higher; ties share a rank), or 0 when nothing is known.</summary>
        public static int UnityRank(IReadOnlyList<uint> points, int leader)
        {
            if (leader < 1 || leader > points.Count || points.Count == 0) return 0;
            bool any = false;
            foreach (uint p in points) any |= p != 0;
            if (!any) return 0;
            uint mine = points[leader - 1];
            int rank = 1;
            foreach (uint p in points) if (p > mine) rank++;
            return rank;
        }

        /// <summary>
        /// Unity Info's rows: "Current Unity:" the leader, the rankings (last week's, then this week's partial; "--" outside a
        /// Unity or before the server sent them), the evaluations, and "Unity chat settings:" Active / Inactive with the one in
        /// effect white and the other greyed (the capture shows a pink ball on the chosen one).
        /// </summary>
        public static IReadOnlyList<StockUiListRow> UnityInfoRows(StockUiUnityInfo? info, bool chatActive, Func<int, string?>? text)
        {
            string T(int id, string fallback) => text?.Invoke(id) is { Length: > 0 } s ? s : fallback;
            var inv = CultureInfo.InvariantCulture;
            string none = T(NoValueText, "--");
            int leader = info?.Leader ?? 0;
            string unity = leader is >= 1 and <= UnityLeaderCount ? T(FirstUnityName + leader - 1, "Unity " + leader) : T(NoUnityName, "None");
            string Rank(IReadOnlyList<uint>? points) => info != null && points != null && UnityRank(points, leader) is > 0 and var r ? r.ToString(inv) : none;
            string Points(int value) => leader > 0 ? value.ToString(inv) : none;
            return new[]
            {
                new StockUiListRow(1, T(CurrentUnityLabel, "Current Unity:"), false, Value: unity),
                new StockUiListRow(2, T(RankingLabel, "Ranking Results:"), false, Value: Rank(info?.LastWeekPoints)),
                new StockUiListRow(3, T(PartialRankingLabel, "Partial Ranking Results:"), false, Value: Rank(info?.ThisWeekPoints)),
                new StockUiListRow(4, T(EvaluationLabel, "Personal Evaluation:"), false, Value: Points(info?.Evaluation ?? 0)),
                new StockUiListRow(5, T(PartialEvaluationLabel, "Partial Personal Evaluation"), false, Value: Points(info?.PartialEvaluation ?? 0)),
                // The setting in effect is drawn white, the other greyed (the capture's pink ball, PROVISIONAL look).
                new StockUiListRow(UnityChatActiveRow, T(ChatSettingsLabel, "Unity chat settings:"), chatActive, chatActive ? null : Greyed,
                    T(ChatActiveLabel, "Active")),
                new StockUiListRow(UnityChatInactiveRow, string.Empty, !chatActive, chatActive ? Greyed : null, T(ChatInactiveLabel, "Inactive")),
            };
        }

        /// <summary>Ranking Info's rows: the eleven Unities by this week's points, each with its rank and points ("1.  12,345").</summary>
        public static IReadOnlyList<StockUiListRow> UnityRankingRows(StockUiUnityInfo? info, Func<int, string?>? text)
        {
            var order = new List<int>(UnityLeaderCount);
            for (int l = 1; l <= UnityLeaderCount; l++) order.Add(l);
            var points = info?.ThisWeekPoints ?? Array.Empty<uint>();
            uint P(int l) => l - 1 < points.Count ? points[l - 1] : 0;
            order.Sort((a, b) => P(b) != P(a) ? P(b).CompareTo(P(a)) : a.CompareTo(b));
            var rows = new List<StockUiListRow>(UnityLeaderCount);
            foreach (int l in order)
            {
                int rank = UnityRank(points, l);
                string value = rank > 0 ? rank.ToString(CultureInfo.InvariantCulture) + ".  " + StockUiStatus.Number((int)Math.Min(P(l), int.MaxValue)) : "--";
                rows.Add(new StockUiListRow(rows.Count + 1, text?.Invoke(FirstUnityName + l - 1) ?? "Unity " + l, l == info?.Leader, Value: value));
            }
            return rows;
        }

        /// <summary>Unity Lists' rows: the eleven leaders, the character's own marked.</summary>
        public static IReadOnlyList<StockUiListRow> UnityLeaderRows(int leader, Func<int, string?>? text)
        {
            var rows = new List<StockUiListRow>(UnityLeaderCount);
            for (int l = 1; l <= UnityLeaderCount; l++)
                rows.Add(new StockUiListRow(l, text?.Invoke(FirstUnityName + l - 1) ?? "Unity " + l, l == leader));
            return rows;
        }
    }
}
