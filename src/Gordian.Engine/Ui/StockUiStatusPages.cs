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
    /// <param name="ExpCappedOrLimitMode">S2C 0x063 type 0x02's flag: experience is capped (one short of the next level) or limit
    /// points are being earned. The level box's EXP section is grey then, white otherwise (round 1 image-5).</param>
    public sealed record StockUiStatusPoints(int LimitPoints, int MeritPoints, int MaxMeritPoints, bool CanUseMeritMode, bool MeritModeEnabled,
        IReadOnlyList<StockUiJobPointTotals> JobPoints, int AlterEgoPoints, IReadOnlyList<int> AlterEgoUpgrades,
        Func<Currency1Kind, int> Currency1, Func<Currency2Kind, int> Currency2, long? PlayTimeSeconds, byte MainJob,
        bool ExpCappedOrLimitMode = false);

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
    /// <item>Job Points (listed only with the job breaker key item): <c>jbpcat</c> (366 x 190 at 16,48, help 800 "Select the job
    /// for which you wish to expend job points."; 10 rows of 356 x 16 from y 18, pitch 17; the job names only) with
    /// <c>levmeri2</c> (112 x 88 at 16,240: "Capacity Points", "Job Points", with their maximums) and <c>bluehelp</c> (130,240:
    /// the job's name and its ROM/314/62 line).</item>
    /// <item>Alter Ego Points (greyed without the cipher bracelet key item): <c>fp_cat</c> (366 x 190 at 16,48) lists HP/MP,
    /// Attributes and Skills; each opens <c>fp_cat2</c> (its "Alter Ego Points" header, rows from y 18) with the items and
    /// "upgrades/most"; <c>fp_info</c> (16,240: "Alter Ego Points", with the maximum) and <c>bluehelp</c> (130,240: the row's
    /// name and ROM/384/121 line).</item>
    /// <item>Unity: <c>ut_menu</c> (Unity Info, Ranking Info, Unity Lists; title 173 "Unity") with <c>ut_point</c> ("Unity
    /// Points", with the maximum) on top of the party window, where the target window stands (round 1 image-10).</item>
    /// <item>Currencies and Currencies 2: no DAT window. GordianXI lists them in <c>meritcat</c>'s window (<c>frames</c> #120,
    /// 366 x 190 at 16,48, 11 rows of 356 x 16), the name from ROM/165/77 and the amount at the right, in retail's order
    /// (round 1), the row's help line (ROM/165/75 722-798) in the help bar. PROVISIONAL: the window.</item>
    /// <item>Play Time: no window; the total time played is written to the log in the echo colour (help 476 "Display total
    /// time spent in Vana'diel."). PROVISIONAL: the line's wording.</item>
    /// <item>Master Levels is greyed until a job is mastered (<see cref="MasterLevelsOpen"/>).</item>
    /// </list>
    /// </summary>
    public static class StockUiStatusPages
    {
        public const string MasterLevelMenu = "masterle";
        public const string MeritMenu = "merit1", MeritModeMenu = "merit2", MeritBox = "levmerit";
        public const string JobPointMenu = "jbpcat", JobPointBox = "levmeri2";
        public const string AlterEgoMenu = "fp_cat", AlterEgoItemMenu = "fp_cat2", AlterEgoBox = "fp_info";
        public const string UnityMenu = "ut_menu", UnityBox = "ut_point";

        /// <summary>
        /// The info box under the Job Points and Alter Ego Points lists: <c>bluehelp</c> (366 x 56 at 130,240, <c>frames</c> #132),
        /// as wide as the list as the round 1 images 7-9 show. The DAT's <c>merithel</c> / <c>fp_help</c> stand at the same place but
        /// are only 254 wide, which cuts the lines short.
        /// </summary>
        public const string InfoBox = "bluehelp";

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
        /// Skills and Magic Skills, which XiPackets does not list). The names fall back to these when ROM/384/121 is not loaded.
        /// </summary>
        public static readonly IReadOnlyList<(AlterEgoCategory Category, string Name)> AlterEgoCategories = new[]
        {
            (AlterEgoCategory.MaxHp, "Max HP"), (AlterEgoCategory.MaxMp, "Max MP"), (AlterEgoCategory.Str, "STR"),
            (AlterEgoCategory.Dex, "DEX"), (AlterEgoCategory.Vit, "VIT"), (AlterEgoCategory.Agi, "AGI"), (AlterEgoCategory.Int, "INT"),
            (AlterEgoCategory.Mnd, "MND"), (AlterEgoCategory.Chr, "CHR"), (AlterEgoCategory.CombatSkills, "Combat Skills"),
            (AlterEgoCategory.MagicSkills, "Magic Skills"),
        };

        /// <summary>
        /// Currencies (S2C 0x113) in retail's order (the maintainer's round 1 check, 2026-11-01: the stored crystals before the
        /// Guild Points, Daily Tally before Ballista Points, Deeds / Copper A.M.A.N. Vouchers / Reclamation Marks after Sparks of
        /// Eminence, the plans after Therion Ichor, Unity Accolades before the Rem's Tale chapters, Login Points last) with their
        /// names (ROM/165/77) and help lines (ROM/165/75). The help lines 722-776 follow 0x113's packet order except Daily Tally
        /// (none found); 783-796 are matched by what they describe (Voidwatch, Dominion, the Echelon trophies, the replenishing
        /// participation items).
        /// </summary>
        public static readonly IReadOnlyList<StockUiCurrencyRow> Currencies1 = BuildCurrencies1();

        /// <summary>
        /// Currencies 2 (S2C 0x118) in retail's order (round 1, 2026-11-01) with their names (ROM/165/77) and the help lines
        /// found (ROM/165/75). PROVISIONAL: where Pellucid, Fern and Taupe Stones go (round 1 did not move them).
        /// </summary>
        public static readonly IReadOnlyList<StockUiCurrencyRow> Currencies2 = BuildCurrencies2();

        private static StockUiCurrencyRow[] BuildCurrencies1()
        {
            var rows = new List<StockUiCurrencyRow>(99);
            void Add(Currency1Kind kind, int name, int help = -1) => rows.Add(new StockUiCurrencyRow((int)kind, name, help));
            void Run(Currency1Kind first, int count, int name, int help = -1)
            {
                for (int i = 0; i < count; i++) Add((Currency1Kind)((int)first + i), name + i, help < 0 ? -1 : help + i);
            }
            // Conquest Points ... Scylds: names 136-146, help 722-732.
            Run(Currency1Kind.ConquestPointsSandoria, 11, 136, 722);
            Run(Currency1Kind.FireCrystals, 8, 473);
            // Guild Points ... Synergy Fewell (Dark): names 147-164, help 733-750.
            Run(Currency1Kind.GuildPointsFishing, 18, 147, 733);
            Add(Currency1Kind.DailyTally, 213);
            // Ballista Points ... Chocobucks (Windurst Team): names 165-169, help 751-755.
            Run(Currency1Kind.BallistaPoints, 5, 165, 751);
            Run(Currency1Kind.ResearchMarks, 4, 170, 756);
            Add(Currency1Kind.MoblinPheromoneSacks, 206, 792);
            Add(Currency1Kind.MoblinMarbles, 174, 760);
            Add(Currency1Kind.CaveConservationPoints, 205);
            // Infamy ... Sparks of Eminence: names 175-178, help 761-764.
            Run(Currency1Kind.Infamy, 4, 175, 761);
            Add(Currency1Kind.Deeds, 484);
            Add(Currency1Kind.CopperAmanVouchers, 214);
            Add(Currency1Kind.ReclamationMarks, 467);
            // Shining Stars ... Tokens: names 179-186, help 765-772.
            Run(Currency1Kind.ShiningStars, 8, 179, 765);
            Add(Currency1Kind.ImperialArmyIdTags, 207, 793);
            Run(Currency1Kind.Zeni, 3, 187, 773);
            Run(Currency1Kind.BloodshedPlans, 5, 215);
            Add(Currency1Kind.AlliedNotes, 190, 776);
            Add(Currency1Kind.OpCredits, 208, 794);
            Add(Currency1Kind.Cruor, 197);
            Add(Currency1Kind.ResistanceCredits, 198, 783);
            Add(Currency1Kind.DominionNotes, 199, 785);
            Run(Currency1Kind.EchelonBattleTrophies5th, 5, 200, 786);
            Add(Currency1Kind.TraverserStones, 210, 796);
            Add(Currency1Kind.Voidstones, 211);
            Add(Currency1Kind.KupofriedsCorundums, 212);
            Add(Currency1Kind.UnityAccolades, 443, 893);
            Run(Currency1Kind.RemsTaleChapters1, 10, 457);
            Add(Currency1Kind.LoginPoints, 472);
            return rows.ToArray();
        }

        private static StockUiCurrencyRow[] BuildCurrencies2()
        {
            var rows = new List<StockUiCurrencyRow>(97);
            void Add(Currency2Kind kind, int name, int help = -1) => rows.Add(new StockUiCurrencyRow((int)kind, name, help));
            Add(Currency2Kind.Bayld, 191, 777);
            Add(Currency2Kind.CoalitionImprimaturs, 209, 795);
            Add(Currency2Kind.KineticUnits, 192, 778);
            Add(Currency2Kind.ObsidianFragments, 193, 779);
            Add(Currency2Kind.LebondoptWings, 194, 780);
            Add(Currency2Kind.PulchridoptWings, 195, 781);
            Add(Currency2Kind.MellidoptWings, 445);
            // Ghastly Stones ... Duskorb Stones +2: names 350-394.
            for (int k = (int)Currency2Kind.GhastlyStones; k <= (int)Currency2Kind.DuskorbStonesPlus2; k++) Add((Currency2Kind)k, 342 + k);
            Add(Currency2Kind.MweyaPlasmCorpuscles, 196, 782);
            Add(Currency2Kind.EschaSilt, 454);
            Add(Currency2Kind.EschaBeads, 455);
            Add(Currency2Kind.PellucidStones, 468);
            Add(Currency2Kind.FernStones, 469);
            Add(Currency2Kind.TaupeStones, 470);
            Add(Currency2Kind.Potpourri, 471);
            Add(Currency2Kind.CrafterPoints, 485);
            // The crystals and spheres set, the MC-I-SR0x: names 486-510.
            for (int k = (int)Currency2Kind.FireCrystalsSet; k <= (int)Currency2Kind.DarknessSpheresSet; k++) Add((Currency2Kind)k, 422 + k);
            Add(Currency2Kind.Hallmarks, 481);
            Add(Currency2Kind.TotalHallmarks, 482);
            Add(Currency2Kind.BadgesOfGallantry, 483);
            Add(Currency2Kind.SilverAmanVouchers, 511);
            Add(Currency2Kind.MysticalCanteens, 512);
            Add(Currency2Kind.DomainPoints, 513);
            Add(Currency2Kind.DomainPointsEarnedToday, 514);
            Add(Currency2Kind.MogSegments, 519, 1087);
            Add(Currency2Kind.Gallimaufry, 518, 1086);
            Add(Currency2Kind.ImperialStandingAccolades, 522);
            Add(Currency2Kind.TemenosUnits, 523, 1094);
            Add(Currency2Kind.ApollyonUnits, 524, 1095);
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

        /// <summary>
        /// The points boxes' maximums (round 1 images 6, 7, 8 and 10, 2026-11-01): 10,000 limit points (one merit point),
        /// 30,000 capacity points (one job point), 500 job points, 1,000 alter ego points, 99,999 Unity accolades.
        /// </summary>
        public const int MaxLimitPoints = 10000, MaxCapacityPoints = 30000, MaxJobPoints = 500, MaxAlterEgoPoints = 1000, MaxUnityAccolades = 99999;

        /// <summary>
        /// The Job Points list: each job's name (ROM/314/62, or the job table's), no value column (round 1 image-7); the help
        /// bar shows help 800 on every row.
        /// </summary>
        public static IReadOnlyList<StockUiListRow> JobPointRows(Func<int, string?>? strings)
        {
            var rows = new List<StockUiListRow>(JobCount);
            for (int job = 1; job <= JobCount; job++)
            {
                string? name = strings?.Invoke(JobPointNameId(job));
                rows.Add(new StockUiListRow(job, name is { Length: > 0 } ? name : StockUiCheck.JobName((byte)job), false));
            }
            return rows;
        }

        /// <summary>Help 800 "Select the job for which you wish to expend job points." on each of the Job Points list's rows.</summary>
        public const int JobPointListHelp = 800;

        /// <summary>A job's name and its info line ("Adjust various abilities for WAR lv. 99 or above.") in ROM/314/62.</summary>
        public static int JobPointNameId(int job) => 2 + (job - 1) * 2;

        public static int JobPointHelpId(int job) => JobPointNameId(job) + 1;

        /// <summary>A job's job points; zero when not known.</summary>
        public static StockUiJobPointTotals JobPointsOf(StockUiStatusPoints? points, int job) =>
            points != null && job >= 0 && job < points.JobPoints.Count ? points.JobPoints[job] : default;

        /// <summary>
        /// Alter Ego Points' three categories (round 1 image-8) and the S2C 0x08E categories under each (image-9 shows
        /// Attributes: STR ... CHR), with their names and info lines in ROM/384/121: category n's name at 2 + 2n and its line
        /// at 3 + 2n ("Valid from Lv. 99 or higher."), item k's (<see cref="AlterEgoCategories"/> order) at 16 + 2k / 17 + 2k
        /// ("Uses 1 alter ego point to increase the alter ego's STR by 1.").
        /// </summary>
        public static readonly IReadOnlyList<(string Name, int First, int Count)> AlterEgoGroups = new[]
        {
            ("HP/MP", 0, 2), ("Attributes", 2, 7), ("Skills", 9, 2),
        };

        public static int AlterEgoGroupNameId(int group) => 2 + group * 2;

        public static int AlterEgoItemNameId(int item) => 16 + item * 2;

        /// <summary>
        /// An alter ego item's most upgrades: 60 (round 1 image-9, the attributes). PROVISIONAL for Max HP / MP and the skills,
        /// which the capture does not show.
        /// </summary>
        public const int MaxAlterEgoUpgrade = 60;

        /// <summary>Help 1097 on the categories' rows, 1098 on an item list's (ROM/165/75).</summary>
        public const int AlterEgoGroupHelp = 1097, AlterEgoItemHelp = 1098;

        /// <summary>The Alter Ego Points categories (HP/MP, Attributes, Skills), named from ROM/384/121.</summary>
        public static IReadOnlyList<StockUiListRow> AlterEgoGroupRows(Func<int, string?>? strings)
        {
            var rows = new List<StockUiListRow>(AlterEgoGroups.Count);
            for (int g = 0; g < AlterEgoGroups.Count; g++)
            {
                string? name = strings?.Invoke(AlterEgoGroupNameId(g));
                rows.Add(new StockUiListRow(g + 1, name is { Length: > 0 } ? name : AlterEgoGroups[g].Name, false));
            }
            return rows;
        }

        /// <summary>A category's items with their upgrade level and its most ("0/60", image-9).</summary>
        public static IReadOnlyList<StockUiListRow> AlterEgoItemRows(int group, StockUiStatusPoints? points, Func<int, string?>? strings)
        {
            var (_, first, count) = AlterEgoGroups[Math.Clamp(group, 0, AlterEgoGroups.Count - 1)];
            var rows = new List<StockUiListRow>(count);
            var inv = CultureInfo.InvariantCulture;
            for (int i = first; i < first + count; i++)
            {
                int level = points != null && i < points.AlterEgoUpgrades.Count ? points.AlterEgoUpgrades[i] : 0;
                string? name = strings?.Invoke(AlterEgoItemNameId(i));
                rows.Add(new StockUiListRow(rows.Count + 1, name is { Length: > 0 } ? name : AlterEgoCategories[i].Name, false,
                    Value: string.Create(inv, $"{level}/{MaxAlterEgoUpgrade}")));
            }
            return rows;
        }

        /// <summary>The key items that open the Status entries (ROM/175/35: 606 limit breaker, 2544 job breaker, 3262 master breaker, 3361 cipher bracelet).</summary>
        public const ushort LimitBreakerKeyItem = 606, JobBreakerKeyItem = 2544, MasterBreakerKeyItem = 3262, CipherBraceletKeyItem = 3361;

        /// <summary>Job points spent that master a job (all 20 gifts): the job can earn exemplar points.</summary>
        public const int MasteredJobPointsSpent = 2100;

        /// <summary>
        /// Whether Master Levels is usable: a job can earn exemplar points, that is a job is mastered. Retail greys the entry
        /// with the three breaker key items and no mastered job (round 1, 2026-11-01). The rule used: a job's bit in S2C 0x01B
        /// <c>job_mastery_flags</c>, or, as LandSandBoat never sets those, a job with 2,100 job points spent (S2C 0x063 type
        /// 0x05) with the master breaker key item. PROVISIONAL: inferred, not confirmed on a character with a mastered job.
        /// </summary>
        public static bool MasterLevelsOpen(uint masteryFlags, StockUiStatusPoints? points, bool masterBreaker)
        {
            if ((masteryFlags & ~1u) != 0) return true;
            if (!masterBreaker || points == null) return false;
            for (int job = 1; job < points.JobPoints.Count; job++)
            {
                if (points.JobPoints[job].Spent >= MasteredJobPointsSpent) return true;
            }
            return false;
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
