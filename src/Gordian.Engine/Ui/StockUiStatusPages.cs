// src/Gordian.Engine/Ui/StockUiStatusPages.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using Gordian.Core.Network.Packets;

namespace Gordian.Engine.Ui
{
    /// <summary>One job's job points (S2C 0x063 type 0x05): capacity points, unspent job points and job points spent.</summary>
    public readonly record struct StockUiJobPointTotals(int CapacityPoints, int JobPoints, int Spent);

    /// <summary>
    /// What the Status window's Merit Points, Job Points, Alter Ego Points, Unity, Currencies and Play Time read (#487), taken
    /// from the session's state when drawn or opened.
    /// </summary>
    /// <param name="LimitPoints">Limit points (S2C 0x063 type 0x02).</param>
    /// <param name="MeritPoints">Merit points held (S2C 0x063 type 0x02).</param>
    /// <param name="MaxMeritPoints">The most merit points that can be held (S2C 0x063 type 0x02).</param>
    /// <param name="CanUseMeritMode">Level 75 or more with the Limit Breaker key item: Limit Points can be chosen.</param>
    /// <param name="MeritModeEnabled">Limit points are being earned instead of experience.</param>
    /// <param name="JobPoints">Job points by job id (index 0 unused; S2C 0x063 type 0x05).</param>
    /// <param name="AlterEgoPoints">Alter ego points (S2C 0x08E).</param>
    /// <param name="AlterEgoUpgrades">Each category's upgrade level, in <see cref="StockUiStatusPages.AlterEgoCategories"/> order (S2C 0x08E).</param>
    /// <param name="Currency1">A currency of S2C 0x113.</param>
    /// <param name="Currency2">A currency of S2C 0x118.</param>
    /// <param name="PlayTimeSeconds">The seconds played (S2C 0x00A <c>PlayTime</c> plus the time since); null before the first zone-in.</param>
    /// <param name="MainJob">The main job (its Job Points row is first under the cursor).</param>
    public sealed record StockUiStatusPoints(int LimitPoints, int MeritPoints, int MaxMeritPoints, bool CanUseMeritMode, bool MeritModeEnabled,
        IReadOnlyList<StockUiJobPointTotals> JobPoints, int AlterEgoPoints, IReadOnlyList<int> AlterEgoUpgrades,
        Func<Currency1Kind, int> Currency1, Func<Currency2Kind, int> Currency2, long? PlayTimeSeconds, byte MainJob);

    /// <summary>One row of a Currencies page: the currency, its name in ROM/165/77 and its help line in ROM/165/75 (-1 for none).</summary>
    public readonly record struct StockUiCurrencyRow(int Kind, int NameId, int HelpId);

    /// <summary>
    /// The Status window's remaining entries (#487), from the English menu DAT (39542, ROM/119/51; the menu dump of
    /// 2026-10-31) and the menu string tables (help ROM/165/75, titles ROM/165/76, short strings ROM/165/77):
    /// <list type="bullet">
    /// <item>Master Levels: <c>masterle</c> (252 x 182 at 130,48, "Master Levels" baked into <c>frames</c> #203; 22 rows of
    /// 112 x 14 at x 8 and 134 from y 19, as <c>joblevel</c>), drawn as Job Levels.</item>
    /// <item>Merit Points: <c>merit1</c> (Categories, Mode Switch; at 384,48 over the Status list) with <c>levmerit</c> (112 x 88
    /// at 16,208: "Limit Points" and "Merit Points" baked in) where the level box stands. Mode Switch opens <c>merit2</c> (EXP,
    /// Limit Points), which sends C2S 0x0BE kind 2.</item>
    /// <item>Job Points: <c>jbpcat</c> (366 x 190 at 16,48, help 800 "Select the job for which you wish to expend job points.";
    /// 10 rows of 356 x 16 from y 18, pitch 17) with <c>levmeri2</c> (112 x 88 at 16,240: "Capacity Points", "Job Points").</item>
    /// <item>Alter Ego Points: <c>fp_cat</c> (366 x 190 at 16,48, 11 rows of 356 x 16 from y 7: one per S2C 0x08E category)
    /// with <c>fp_info</c> (112 x 40 at 16,240: "Alter Ego Points").</item>
    /// <item>Unity: <c>ut_menu</c> (Unity Info, Ranking Info, Unity Lists; title 173 "Unity") with <c>ut_point</c> (112 x 40 at
    /// 384,256: "Unity Points").</item>
    /// <item>Currencies and Currencies 2: no DAT window. GordianXI lists them in <c>meritcat</c>'s window (<c>frames</c> #120,
    /// 366 x 190 at 16,48, 11 rows of 356 x 16), the name from ROM/165/77 and the amount at the right, in packet order
    /// (S2C 0x113 for Currencies, 0x118 for Currencies 2), the row's help line (ROM/165/75 722-798) in the help bar.
    /// PROVISIONAL: the window, the order and the rows shown are not checked against retail.</item>
    /// <item>Play Time: no window; the total time played is written to the log (help 476 "Display total time spent in
    /// Vana'diel."). PROVISIONAL: the line's wording.</item>
    /// </list>
    /// </summary>
    public static class StockUiStatusPages
    {
        public const string MasterLevelMenu = "masterle";
        public const string MeritMenu = "merit1", MeritModeMenu = "merit2", MeritBox = "levmerit";
        public const string JobPointMenu = "jbpcat", JobPointBox = "levmeri2";
        public const string AlterEgoMenu = "fp_cat", AlterEgoBox = "fp_info";
        public const string UnityMenu = "ut_menu", UnityBox = "ut_point";

        /// <summary>The window the Currencies pages borrow: <c>meritcat</c>'s frame and 11 rows.</summary>
        public const string CurrencyWindowMenu = "meritcat";

        /// <summary>
        /// How far the lists' rows are moved in from the DAT's (which start at the window's left edge, x 0), so the text clears
        /// the list clip and the cursor stands beside the window (GordianXI's choice; retail not captured).
        /// </summary>
        public const int ListRowInset = 8;

        /// <summary><c>merit1</c>'s buttons.</summary>
        public const int MeritCategoriesButton = 1, MeritModeSwitchButton = 2;

        /// <summary><c>merit2</c>'s buttons: the mode C2S 0x0BE kind 2 sends (0 experience points, 1 limit points).</summary>
        public const int MeritModeExpButton = 1, MeritModeLimitButton = 2;

        /// <summary><c>ut_menu</c>'s buttons.</summary>
        public const int UnityInfoButton = 1, UnityRankingButton = 2, UnityListsButton = 3;

        /// <summary>The help bar's titles over the lists (ROM/165/76 #167 "Currencies", #130 "Categories").</summary>
        public const string CurrenciesTitle = "Currencies", CategoriesTitle = "Categories";

        /// <summary>
        /// The Alter Ego Points categories in S2C 0x08E order (<see cref="AlterEgoCategory"/> 8-18; LandSandBoat adds Combat
        /// Skills and Magic Skills, which XiPackets does not list). The names are GordianXI's (no table names them).
        /// </summary>
        public static readonly IReadOnlyList<(AlterEgoCategory Category, string Name)> AlterEgoCategories = new[]
        {
            (AlterEgoCategory.MaxHp, "Max HP"), (AlterEgoCategory.MaxMp, "Max MP"), (AlterEgoCategory.Str, "STR"),
            (AlterEgoCategory.Dex, "DEX"), (AlterEgoCategory.Vit, "VIT"), (AlterEgoCategory.Agi, "AGI"), (AlterEgoCategory.Int, "INT"),
            (AlterEgoCategory.Mnd, "MND"), (AlterEgoCategory.Chr, "CHR"), (AlterEgoCategory.CombatSkills, "Combat Skills"),
            (AlterEgoCategory.MagicSkills, "Magic Skills"),
        };

        /// <summary>
        /// Currencies (S2C 0x113) in packet order with their names (ROM/165/77) and help lines (ROM/165/75). The help lines
        /// 722-776 follow the packet order except Daily Tally (none found); 783-796 are matched by what they describe
        /// (Voidwatch, Dominion, the Echelon trophies, the replenishing participation items).
        /// </summary>
        public static readonly IReadOnlyList<StockUiCurrencyRow> Currencies1 = BuildCurrencies1();

        /// <summary>Currencies 2 (S2C 0x118) in packet order with their names (ROM/165/77) and the help lines found (ROM/165/75).</summary>
        public static readonly IReadOnlyList<StockUiCurrencyRow> Currencies2 = BuildCurrencies2();

        private static StockUiCurrencyRow[] BuildCurrencies1()
        {
            var rows = new List<StockUiCurrencyRow>(99);
            void Add(Currency1Kind kind, int name, int help = -1) => rows.Add(new StockUiCurrencyRow((int)kind, name, help));
            // Conquest Points ... Chocobucks (Windurst Team): names 136-169, help 722-755.
            for (int k = 0; k <= (int)Currency1Kind.ChocobucksWindurstTeam; k++) Add((Currency1Kind)k, 136 + k, 722 + k);
            Add(Currency1Kind.DailyTally, 213);
            // Research Marks ... Allied Notes: names 170-190, help 756-776.
            for (int k = (int)Currency1Kind.ResearchMarks; k <= (int)Currency1Kind.AlliedNotes; k++) Add((Currency1Kind)k, 135 + k, 721 + k);
            Add(Currency1Kind.CopperAmanVouchers, 214);
            Add(Currency1Kind.LoginPoints, 472);
            Add(Currency1Kind.Cruor, 197);
            Add(Currency1Kind.ResistanceCredits, 198, 783);
            Add(Currency1Kind.DominionNotes, 199, 785);
            for (int i = 0; i < 5; i++) Add((Currency1Kind)((int)Currency1Kind.EchelonBattleTrophies5th + i), 200 + i, 786 + i);
            Add(Currency1Kind.CaveConservationPoints, 205);
            Add(Currency1Kind.ImperialArmyIdTags, 207, 793);
            Add(Currency1Kind.OpCredits, 208, 794);
            Add(Currency1Kind.TraverserStones, 210, 796);
            Add(Currency1Kind.Voidstones, 211);
            Add(Currency1Kind.KupofriedsCorundums, 212);
            Add(Currency1Kind.MoblinPheromoneSacks, 206, 792);
            for (int i = 0; i < 10; i++) Add((Currency1Kind)((int)Currency1Kind.RemsTaleChapters1 + i), 457 + i);
            for (int i = 0; i < 5; i++) Add((Currency1Kind)((int)Currency1Kind.BloodshedPlans + i), 215 + i);
            Add(Currency1Kind.ReclamationMarks, 467);
            Add(Currency1Kind.UnityAccolades, 443, 893);
            for (int i = 0; i < 8; i++) Add((Currency1Kind)((int)Currency1Kind.FireCrystals + i), 473 + i);
            Add(Currency1Kind.Deeds, 484);
            return rows.ToArray();
        }

        private static StockUiCurrencyRow[] BuildCurrencies2()
        {
            var rows = new List<StockUiCurrencyRow>(94);
            void Add(Currency2Kind kind, int name, int help = -1) => rows.Add(new StockUiCurrencyRow((int)kind, name, help));
            Add(Currency2Kind.Bayld, 191, 777);
            Add(Currency2Kind.KineticUnits, 192, 778);
            Add(Currency2Kind.CoalitionImprimaturs, 209, 795);
            Add(Currency2Kind.MysticalCanteens, 512);
            Add(Currency2Kind.ObsidianFragments, 193, 779);
            Add(Currency2Kind.LebondoptWings, 194, 780);
            Add(Currency2Kind.PulchridoptWings, 195, 781);
            Add(Currency2Kind.MweyaPlasmCorpuscles, 196, 782);
            // Ghastly Stones ... Duskorb Stones +2: names 350-394.
            for (int k = (int)Currency2Kind.GhastlyStones; k <= (int)Currency2Kind.DuskorbStonesPlus2; k++) Add((Currency2Kind)k, 342 + k);
            Add(Currency2Kind.PellucidStones, 468);
            Add(Currency2Kind.FernStones, 469);
            Add(Currency2Kind.TaupeStones, 470);
            Add(Currency2Kind.MellidoptWings, 445);
            Add(Currency2Kind.EschaBeads, 455);
            Add(Currency2Kind.EschaSilt, 454);
            Add(Currency2Kind.Potpourri, 471);
            Add(Currency2Kind.Hallmarks, 481);
            Add(Currency2Kind.TotalHallmarks, 482);
            Add(Currency2Kind.BadgesOfGallantry, 483);
            Add(Currency2Kind.CrafterPoints, 485);
            // The crystals and spheres set, the MC-I-SR0x: names 486-510.
            for (int k = (int)Currency2Kind.FireCrystalsSet; k <= (int)Currency2Kind.DarknessSpheresSet; k++) Add((Currency2Kind)k, 422 + k);
            Add(Currency2Kind.SilverAmanVouchers, 511);
            Add(Currency2Kind.DomainPoints, 513);
            Add(Currency2Kind.DomainPointsEarnedToday, 514);
            Add(Currency2Kind.MogSegments, 519, 1087);
            Add(Currency2Kind.Gallimaufry, 518, 1086);
            return rows.ToArray();
        }

        /// <summary>The rows a Currencies page lists, with the amount at the right ("1,234").</summary>
        public static IReadOnlyList<StockUiListRow> CurrencyListRows(bool second, StockUiStatusPoints? points, Func<int, string?>? names)
        {
            var table = second ? Currencies2 : Currencies1;
            var rows = new List<StockUiListRow>(table.Count);
            foreach (var row in table)
            {
                int amount = points == null ? 0 : second ? points.Currency2((Currency2Kind)row.Kind) : points.Currency1((Currency1Kind)row.Kind);
                rows.Add(new StockUiListRow(rows.Count + 1, names?.Invoke(row.NameId) ?? string.Empty, false, Value: StockUiStatus.Number(amount)));
            }
            return rows;
        }

        /// <summary>The help line ids of a Currencies page's rows, parallel to <see cref="CurrencyListRows"/>.</summary>
        public static IReadOnlyList<int> CurrencyHelpIds(bool second)
        {
            var table = second ? Currencies2 : Currencies1;
            var ids = new int[table.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = table[i].HelpId;
            return ids;
        }

        /// <summary>The jobs the Job Points list shows, Warrior ... Rune Fencer (job ids 1-22).</summary>
        public const int JobCount = 22;

        /// <summary>The Job Points list: each job and its unspent job points ("12 JP"; PROVISIONAL, retail's row is not captured).</summary>
        public static IReadOnlyList<StockUiListRow> JobPointRows(StockUiStatusPoints? points)
        {
            var rows = new List<StockUiListRow>(JobCount);
            for (int job = 1; job <= JobCount; job++)
            {
                var totals = JobPointsOf(points, job);
                rows.Add(new StockUiListRow(job, StockUiCheck.JobName((byte)job), false,
                    Value: totals.JobPoints.ToString(CultureInfo.InvariantCulture) + " JP"));
            }
            return rows;
        }

        /// <summary>A job's job points; zero when not known.</summary>
        public static StockUiJobPointTotals JobPointsOf(StockUiStatusPoints? points, int job) =>
            points != null && job >= 0 && job < points.JobPoints.Count ? points.JobPoints[job] : default;

        /// <summary>The Alter Ego Points list: each category and its upgrade level ("Lv. 0"; PROVISIONAL).</summary>
        public static IReadOnlyList<StockUiListRow> AlterEgoRows(StockUiStatusPoints? points)
        {
            var rows = new List<StockUiListRow>(AlterEgoCategories.Count);
            for (int i = 0; i < AlterEgoCategories.Count; i++)
            {
                int level = points != null && i < points.AlterEgoUpgrades.Count ? points.AlterEgoUpgrades[i] : 0;
                rows.Add(new StockUiListRow(i + 1, AlterEgoCategories[i].Name, false, Value: "Lv. " + level.ToString(CultureInfo.InvariantCulture)));
            }
            return rows;
        }

        /// <summary>
        /// The Play Time line: "Total play time: 12 days, 3 hours, 4 minutes and 5 seconds." (PROVISIONAL wording), or a notice
        /// when the server has not sent it yet.
        /// </summary>
        public static string PlayTimeText(long? seconds)
        {
            if (seconds is not { } total || total < 0) return "Play time is not known yet.";
            var inv = CultureInfo.InvariantCulture;
            long days = total / 86400, hours = total / 3600 % 24, minutes = total / 60 % 60, secs = total % 60;
            static string Unit(long n, string unit, IFormatProvider f) => n.ToString(f) + " " + unit + (n == 1 ? string.Empty : "s");
            return $"Total play time: {Unit(days, "day", inv)}, {Unit(hours, "hour", inv)}, {Unit(minutes, "minute", inv)} and {Unit(secs, "second", inv)}.";
        }

        /// <summary>Whether a job has mastery unlocked (S2C 0x01B <c>job_mastery_flags</c>).</summary>
        public static bool HasMastery(uint flags, int job) => job is > 0 and < 32 && (flags & (1u << job)) != 0;

        /// <summary>A Master Levels row's level: the master level, or "-" for a job without mastery (PROVISIONAL: retail not captured).</summary>
        public static string MasterLevelText(StockUiStatusInfo info, int job)
        {
            if (!HasMastery(info.JobMasteryFlags, job)) return "-";
            int level = info.MasterLevels is { } levels && job < levels.Count ? levels[job] : 0;
            return level.ToString(CultureInfo.InvariantCulture);
        }
    }
}
