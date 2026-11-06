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

        /// <summary>A job category's two merit groups (Key: the category).</summary>
        MeritGroups,

        /// <summary>A category's merits (Key: <see cref="StockUiProgression.MeritListKey"/>).</summary>
        Merits,
        JobPointCategories,

        /// <summary>The Job Points window's second page: the job's gifts (<c>jbpcat</c>'s frame).</summary>
        JobGifts,
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
    /// frame) with each merit's upgrades and NEXT, the merit points the next upgrade costs; a job category first lists its
    /// Group 1 and Group 2 (each with its own limit: its line in the table, XiPackets 0x008C's tables). <c>meritinf</c> (16,240:
    /// "Merit Points", "Combo Total/Max" baked in) shows the merit points held and the category's or group's total. No retail
    /// capture of these lists: the group list and NEXT's place are PROVISIONAL. Confirm on a merit opens <c>merit3</c> (Raise, help 612 / Lower, help 613), each
    /// asking first, then sends C2S 0x0BE kind 3 (Param1 1 raise / 0 lower, Param2 the merit id / 2: LandSandBoat shifts it
    /// back). LandSandBoat allows it in a Mog House only and sends each merit's next cost as 0 elsewhere.</item>
    /// <item>Job Points: Confirm on a job lists its ten categories (ROM/314/62 at job x 64 + 2 k) with "level/20" in
    /// <c>jbpcat</c> under a "Job Points" title bar with page arrows, <c>levmeri2</c> below (retail image-30); Confirm asks,
    /// then sends C2S 0x0BF with the category's index (job x 32 + k; LandSandBoat's <c>JOBPOINT_TYPE</c>), allowed in a Mog
    /// House only. A level costs its number plus one in job points (LandSandBoat <c>JobPointCost</c>; S2C 0x08D <c>next</c>),
    /// to 20. Left / Right turns to the second page, "Gifts" with "Combo Total:" the job points spent: the job's gifts
    /// (ROM/324/59) with the job points spent each needs, grey until earned (image-31 to image-33).</item>
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
        public const string MeritInfoBox = "meritinf";

        /// <summary><c>merit3</c>'s buttons.</summary>
        public const int MeritRaiseButton = 1, MeritLowerButton = 2;

        /// <summary>Help lines (ROM/165/75): 609 the categories, 612 Raise (the merits list), 800 the job point categories, 892 the leaders.</summary>
        public const int MeritCategoryHelp = 609, MeritListHelp = 612, JobPointCategoryHelp = 800, UnityListHelp = 892;

        /// <summary>The merit categories' count (ROM/169/75 names them at 2 .. 56: HP/MP 1 ... Rune Fencer 28).</summary>
        public const int MeritCategoryCount = 28;

        /// <summary>The first job category (Warrior, 384 = 6 x 64) and the job categories' second group's base (0x800).</summary>
        private const int FirstJobCategory = 6, SecondGroupBase = 0x800;

        /// <summary>The Others category, and Weapon Skills, listed right after it (the maintainer's retail check, round 1).</summary>
        public const int OthersCategory = 5, WeaponSkillsCategory = 26;

        /// <summary>The key item that opens Weapon Skills ("Heart of the Bushin", LandSandBoat <c>HEART_OF_THE_BUSHIN</c>); greyed without it.</summary>
        public const ushort WeaponSkillsKeyItem = 2059;

        /// <summary>Weapon Skills' most upgrades, which its table line does not name (LandSandBoat <c>data/merits.yaml</c>).</summary>
        private const int WeaponSkillsMax = 15;

        /// <summary>The Maximum Merit Points merit, the one merit of HP/MP with its own limit.</summary>
        private const int MaxMeritPointsMerit = 0x44;

        /// <summary>
        /// A merit's most upgrades (S2C 0x08C does not carry it): the length of its upgrade cost list in LandSandBoat's
        /// <c>data/merits.yaml</c>: 15 for HP/MP and the attributes, 45 for Maximum Merit Points, 8 for the combat and magic
        /// skills, 5 for the others, the jobs and the weapon skills. GordianXI's own addition, retail shows the upgrades only.
        /// </summary>
        public static int MeritMax(int meritId) => meritId == MaxMeritPointsMerit ? 45 : CategoryOfMerit(meritId) switch
        {
            1 or 2 => 15,
            3 or 4 => 8,
            _ => 5,
        };

        /// <summary>A category's S2C 0x08C id (64 n): its merits' ids are this plus 2 k.</summary>
        public static int MeritCategoryId(int category) => category * 64;

        /// <summary>
        /// A job category's second group's id (LandSandBoat <c>xi.meritCategory</c> <c>WAR_GROUP_2</c> = 2048 ... <c>RUN_GROUP_2</c>
        /// = 3456: 0x800 + (n - 6) x 64); -1 for the other categories.
        /// </summary>
        public static int SecondGroupId(int category) =>
            category >= FirstJobCategory && category != WeaponSkillsCategory && category <= MeritCategoryCount
                ? SecondGroupBase + (category - FirstJobCategory) * 64 : -1;

        /// <summary>Whether a category's merits are split into Group 1 and Group 2 (the jobs).</summary>
        public static bool HasMeritGroups(int category) => SecondGroupId(category) >= 0;

        /// <summary>A job merit's group: 2 from the second group's base on, else 1.</summary>
        public static int MeritGroupOf(int meritId) => (meritId & ~63) >= SecondGroupBase ? 2 : 1;

        /// <summary>The merits list's key: the category, and its group (0: all its merits) above it.</summary>
        public static int MeritListKey(int category, int group) => category | group << 8;

        /// <summary>A merits list key's category and group.</summary>
        public static (int Category, int Group) MeritListParts(int key) => (key & 0xFF, key >> 8);

        /// <summary>The group rows' names. The client's strings have none but the category's line ("Group 1 max. total ability increases: 10").</summary>
        public const string MeritGroupName = "Group";

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

        /// <summary>A job group's most upgrades: its own figure in the category's line; 0 when the line names none.</summary>
        public static int GroupMax(string? help, int group)
        {
            if (string.IsNullOrEmpty(help)) return 0;
            var matches = MaxIncreases.Matches(help);
            return group >= 1 && group <= matches.Count ? int.Parse(matches[group - 1].Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        }

        /// <summary>The upgrades of a job group's merits.</summary>
        public static int GroupTotal(int category, int group, IReadOnlyList<StockUiMerit> merits)
        {
            int total = 0;
            foreach (var m in merits)
            {
                if (CategoryOfMerit(m.Id) == category && MeritGroupOf(m.Id) == group) total += m.Count;
            }
            return total;
        }

        /// <summary>A job category's group rows ("Group 1", "3/10") and their keys (1, 2).</summary>
        public static (IReadOnlyList<StockUiListRow> Rows, IReadOnlyList<int> Keys) MeritGroupRows(int category, IReadOnlyList<StockUiMerit> merits, Func<int, string?>? text)
        {
            var rows = new List<StockUiListRow>(2);
            for (int group = 1; group <= 2; group++)
            {
                string value = string.Create(CultureInfo.InvariantCulture, $"{GroupTotal(category, group, merits)}/{GroupMax(text?.Invoke(category * 2 + 1), group)}");
                rows.Add(new StockUiListRow(group, string.Create(CultureInfo.InvariantCulture, $"{MeritGroupName} {group}"), false, Value: value));
            }
            return (rows, new[] { 1, 2 });
        }

        /// <summary>The categories that have a name in the table, in table order but Weapon Skills after Others (HP/MP ... Rune Fencer).</summary>
        public static IReadOnlyList<int> MeritCategories(Func<int, string?>? text)
        {
            var list = new List<int>(MeritCategoryCount);
            for (int c = 1; c <= MeritCategoryCount; c++)
            {
                if (c != WeaponSkillsCategory && !IsPlaceholder(text?.Invoke(c * 2))) list.Add(c);
            }
            if (!IsPlaceholder(text?.Invoke(WeaponSkillsCategory * 2))) list.Insert(Math.Min(OthersCategory, list.Count), WeaponSkillsCategory);
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

        /// <summary>A category's most upgrades: its table line's, or Weapon Skills' own.</summary>
        public static int CategoryMostOf(int category, Func<int, string?>? text) =>
            category == WeaponSkillsCategory ? WeaponSkillsMax : CategoryMax(text?.Invoke(category * 2 + 1));

        /// <summary>
        /// The categories list's rows: the name and "total/most" ("12/75"; the total alone when no most is known); Weapon Skills
        /// is greyed without <paramref name="weaponSkillsOpen"/>.
        /// </summary>
        public static IReadOnlyList<StockUiListRow> MeritCategoryRows(IReadOnlyList<int> categories, IReadOnlyList<StockUiMerit> merits, Func<int, string?>? text,
            bool weaponSkillsOpen = true)
        {
            var rows = new List<StockUiListRow>(categories.Count);
            foreach (int c in categories)
            {
                int max = CategoryMostOf(c, text);
                int total = CategoryTotal(c, merits);
                string value = max > 0 ? string.Create(CultureInfo.InvariantCulture, $"{total}/{max}") : total.ToString(CultureInfo.InvariantCulture);
                rows.Add(new StockUiListRow(rows.Count + 1, text?.Invoke(c * 2) ?? string.Empty, false,
                    c == WeaponSkillsCategory && !weaponSkillsOpen ? Greyed : null, value));
            }
            return rows;
        }

        /// <summary>A category's merits that S2C 0x08C listed and the table names, in id order (the first group, then the second), or one <paramref name="group"/>'s.</summary>
        public static IReadOnlyList<StockUiMerit> MeritsOf(int category, IReadOnlyList<StockUiMerit> merits, Func<int, string?>? text, int group = 0)
        {
            var list = new List<StockUiMerit>();
            foreach (var m in merits)
            {
                if (CategoryOfMerit(m.Id) == category && (group == 0 || MeritGroupOf(m.Id) == group) && !IsPlaceholder(text?.Invoke(m.Id))) list.Add(m);
            }
            list.Sort((a, b) => a.Id.CompareTo(b.Id));
            return list;
        }

        /// <summary>ROM/165/77's "NEXT", the level box's; "--" (434) when there is no next upgrade or S2C 0x08C sends no cost (LandSandBoat outside a Mog House).</summary>
        public const int NextLabel = 90;

        /// <summary>The right edge of a merit's "upgrades/most", left of its NEXT (PROVISIONAL).</summary>
        public const int MeritValueRight = 250;

        /// <summary>
        /// The merits list's rows: the name, "upgrades/most" ("2/15") and "NEXT 5", the merit points the next upgrade costs (S2C
        /// 0x08C <c>next</c>); a merit that cannot be raised now (no next cost) is greyed.
        /// </summary>
        public static IReadOnlyList<StockUiListRow> MeritRows(IReadOnlyList<StockUiMerit> merits, Func<int, string?>? text, string nextLabel = "NEXT")
        {
            var rows = new List<StockUiListRow>(merits.Count);
            foreach (var m in merits)
            {
                rows.Add(new StockUiListRow(rows.Count + 1, text?.Invoke(m.Id) ?? string.Empty, false, m.Next == 0 ? Greyed : null,
                    string.Create(CultureInfo.InvariantCulture, $"{m.Count}/{MeritMax(m.Id)}"),
                    Note: m.Next > 0 ? string.Create(CultureInfo.InvariantCulture, $"{nextLabel} {m.Next}") : nextLabel + " --"));
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

        /// <summary>The blue of a figure at its most ("20/20", "500/500 P"; retail image-30, white below it in image-33), half scale.</summary>
        public static readonly Gordian.Core.Resources.Ui.UiColor AtMost = new(0x25, 0x48, 0x68, 0x80);

        /// <summary>The pink of the row under the cursor on the Job Points pages (image-30 to image-33), the quest log's, half scale.</summary>
        public static readonly Gordian.Core.Resources.Ui.UiColor SelectedPink = new(0x80, 0x41, 0x64, 0x80);

        /// <summary>The right edges of the values in <c>jbpcat</c>: a category's "level/20" and a gift's job points (image-30, image-31).</summary>
        public const int JobPointValueRight = 269, JobGiftValueRight = 342;

        /// <summary>The title bars of the Job Points window's pages (image-30, image-31).</summary>
        public const string JobPointsPageTitle = "Job Points", JobGiftsPageTitle = "Gifts", ComboTotalLabel = "Combo Total:";

        /// <summary>The strings ROM/324/59 keeps a job, and the gift slots that hold one.</summary>
        private const int JobGiftStringsPerJob = 512, JobGiftSlots = 62;

        /// <summary>
        /// The job points spent each gift slot needs. The client's table holds only the names, so the figures are LandSandBoat's
        /// (<c>sql/job_point_gifts.sql</c>, the same for every job), checked against the retail lists of image-31 to image-33;
        /// its two rows for one gift (attack and ranged attack) are one slot here. Slot 23 is unused.
        /// </summary>
        private static readonly int[] JobGiftThresholds =
        {
            5, 5, 10, 20, 25, 30, 45, 50, 55, 60, 80, 95, 100, 100, 125, 145, 150, 180, 205, 210, 245, 275, 280, 0, 320, 355, 360, 405, 445, 450, 500,
            500, 545, 550, 605, 655, 660, 720, 775, 780, 845, 905, 910, 980, 1045, 1050, 1125, 1195, 1200, 1200, 1280, 1355, 1360, 1445, 1530, 1620,
            1710, 1805, 1900, 2000, 2100, 2100,
        };

        /// <summary>A gift's name in ROM/324/59; its line follows.</summary>
        public static int JobGiftNameId(int job, int slot) => (job - 1) * JobGiftStringsPerJob + slot * 2;

        /// <summary>The job points spent a gift slot needs.</summary>
        public static int JobGiftThreshold(int slot) => slot >= 0 && slot < JobGiftThresholds.Length ? JobGiftThresholds[slot] : 0;

        /// <summary>The gifts' rows and their slots: the name and the job points spent it needs, grey until <paramref name="spent"/> reaches them.</summary>
        public static (IReadOnlyList<StockUiListRow> Rows, IReadOnlyList<int> Keys) JobGiftRows(int job, int spent, Func<int, string?>? text)
        {
            var rows = new List<StockUiListRow>(JobGiftSlots);
            var keys = new List<int>(JobGiftSlots);
            for (int slot = 0; slot < JobGiftSlots; slot++)
            {
                string? name = text?.Invoke(JobGiftNameId(job, slot));
                if (IsPlaceholder(name) || name == ".") continue;
                int needed = JobGiftThreshold(slot);
                rows.Add(new StockUiListRow(rows.Count + 1, name!, false, spent >= needed ? null : Greyed, needed.ToString(CultureInfo.InvariantCulture)));
                keys.Add(slot);
            }
            return (rows, keys);
        }

        /// <summary>The job point categories' rows: the name and "level/20", blue at 20.</summary>
        public static IReadOnlyList<StockUiListRow> JobPointCategoryRows(int job, IReadOnlyList<int> categories, Func<int, int, StockUiJobPointCategory>? levels,
            Func<int, string?>? text)
        {
            var rows = new List<StockUiListRow>(categories.Count);
            foreach (int k in categories)
            {
                int level = levels?.Invoke(job, k).Level ?? 0;
                rows.Add(new StockUiListRow(rows.Count + 1, text?.Invoke(JobPointCategoryNameId(job, k)) ?? string.Empty, false,
                    Value: string.Create(CultureInfo.InvariantCulture, $"{level}/{MaxJobPointLevel}"), ValueColor: level >= MaxJobPointLevel ? AtMost : null));
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
