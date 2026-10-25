// src/Gordian.Engine/Ui/StockUiStatus.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Models;
using Gordian.Core.World;

namespace Gordian.Engine.Ui
{
    /// <summary>The pages the Status window's list opens (<see cref="StockUiStatus"/>).</summary>
    public enum StockUiStatusPage
    {
        None = 0,
        Profile,
        JobLevels,
        CombatSkills,
        MagicSkills,
        CraftSkills,
    }

    /// <summary>One skill line of a Status skills page: the skill's name, its level, whether it is capped, and a craft's rank.</summary>
    public readonly record struct StockUiSkillLine(string Name, int Level, bool Capped, string? Rank = null);

    /// <summary>
    /// What the Status window shows (#23), read from the local player's state when it is drawn: the stats side (the same as the
    /// Equipment window's), the level box, the profile, every job's level and the skills.
    /// </summary>
    /// <param name="JobLevels">Levels by job id (index 1 Warrior ... 22 Rune Fencer; 0 unused), from S2C 0x01B.</param>
    /// <param name="MainJob">The main job's id (its row in Job Levels is drawn yellow).</param>
    /// <param name="SubJob">The support job's id (its row in Job Levels is drawn light blue).</param>
    /// <param name="NationId">The nation (0 San d'Oria, 1 Bastok, 2 Windurst; -1 none): its emblem shows before the name.</param>
    /// <param name="MentorRank">The mentor flag (S2C 0x01B: 0 none, 1 bronze, 2 silver, 3 gold) after "Mentor :".</param>
    /// <param name="GladiatorRank">The Monstrosity Gladiator rank as the Profile writes it ("Gla", "CG", "HCG").</param>
    /// <param name="Prestige">The Monstrosity prestige.</param>
    public sealed record StockUiStatusInfo(StockUiCharacterStats Stats, int Level, int ExpNow, int ExpNext,
        string Title, string Nation, string Race, string HomePoint, int Rank, int RankPoints, int MasteryRank,
        IReadOnlyList<byte> JobLevels, byte MainJob,
        IReadOnlyList<StockUiSkillLine> CombatSkills, IReadOnlyList<StockUiSkillLine> MagicSkills, IReadOnlyList<StockUiSkillLine> CraftSkills,
        byte SubJob = 0, int NationId = -1, int MentorRank = 0, string GladiatorRank = "Gla", int Prestige = 0);

    /// <summary>
    /// The stock Status window (#23) from the English menu DAT (39542, ROM/119/51; read 2026-10-24 with the menu dump), laid
    /// out after the maintainer's retail capture <c>Main_Menu_Page1.mp4</c> (2026-10-16/17, 0:00-0:20 and 1:55, 2550 x 1434,
    /// UI 1:1):
    /// <list type="bullet">
    /// <item>Main menu Status opens <c>statcom2</c> (112 x 218 at 384,48, top right; 13 entries: Profile, Job Levels, Master
    /// Levels, Combat Skl., Magic Skl., Craft Skl., Currencies, Currencies 2, Unity, Play Time, Merit Points, Job Points, Alter
    /// Ego Points, in that order down the list although the DAT numbers them 1, 2, 12, 3, 4, 5, 6, 10, 11, 9, 7, 8, 13).</item>
    /// <item>While it or one of its pages is open, the stats side <c>statuswi</c> (16,16; the same <c>windowps</c> #8 image as
    /// the Equipment window's <c>persona</c>) and the level box <c>level</c> (16,208) show at the top left; the help bar shows
    /// the selected entry's help line beside the stats side, without a title box.</item>
    /// <item>The pages open at 130,48 beside the stats side: <c>profile</c> (Nation, Race, Home Point, Rank, Rank Points,
    /// Mastery Rank, Mentor, Monstrosity, baked in), <c>joblevel</c> (22 rows: odd job ids down the left column, even ones down
    /// the right), <c>btlskill</c> (Combat Skills), <c>mgcskill</c> (Magic Skills) and Craft Skills (<c>mgcskill</c>'s frame
    /// with the <c>frames</c> #101 "Craft Skills" image, which no DAT menu references).</item>
    /// </list>
    /// </summary>
    public static class StockUiStatus
    {
        public const string CommandMenu = "statcom2", WindowMenu = "statuswi", LevelMenu = "level";
        public const string ProfileMenu = "profile", JobLevelMenu = "joblevel", CombatSkillMenu = "btlskill", MagicSkillMenu = "mgcskill";

        /// <summary>The "Craft Skills" frame image (<c>frames</c> #101, 252 x 152), drawn in <see cref="MagicSkillMenu"/>'s place, stretched to the 10 crafts.</summary>
        public const string CraftFrameGroup = "frames";
        public const int CraftFrameImage = 101;
        public const short CraftFrameHeight = 152;

        /// <summary>The height of <c>joblevel</c>'s frame image (<c>frames</c> #102): 166, short of the 22 rows the DAT window (182) holds.</summary>
        public const short JobLevelImageHeight = 166;

        /// <summary>The page rows' pitch and the room around them: a page is <c>PageChrome + rows * RowPitch</c> tall.</summary>
        public const int RowPitch = 14, PageChrome = 22;

        /// <summary>
        /// A page's height for its rows: Job Levels' 11, the skills' rows (two skills a row for combat and magic), the crafts' 10.
        /// Retail's border-to-border heights are 19 + 14 per row (173 for Job Levels, 47 for a 2-row Magic Skills; round 1
        /// captures image-1 and image-3, 2026-10-25); our frame's edges sit 1 and 2 px inside it. Null for Profile, which keeps
        /// its DAT height.
        /// </summary>
        public static short? PageHeight(StockUiStatusPage page, StockUiStatusInfo? info)
        {
            int rows = page switch
            {
                StockUiStatusPage.JobLevels => JobRowsPerColumn,
                StockUiStatusPage.CombatSkills => ((info?.CombatSkills.Count ?? CombatSkills.Count) + 1) / 2,
                StockUiStatusPage.MagicSkills => ((info?.MagicSkills.Count ?? MagicSkills.Count) + 1) / 2,
                StockUiStatusPage.CraftSkills => CraftSkills.Count,
                _ => -1,
            };
            if (rows < 0) return null;
            return (short)(PageChrome + Math.Max(1, rows) * RowPitch);
        }

        /// <summary>The Rank Points gauge's empty strip (<c>frames</c> #98) and its fill (#103), under "Rank Points".</summary>
        public const int RankGaugeImage = 98, RankGaugeFillImage = 103;

        /// <summary><c>statcom2</c>'s buttons by entry.</summary>
        public const int ProfileButton = 1, JobLevelsButton = 2, MasterLevelsButton = 12, CombatSkillsButton = 3, MagicSkillsButton = 4,
            CraftSkillsButton = 5, CurrenciesButton = 6, Currencies2Button = 10, UnityButton = 11, PlayTimeButton = 9,
            MeritPointsButton = 7, JobPointsButton = 8, AlterEgoButton = 13;

        /// <summary><c>joblevel</c>'s rows: 11 down the left column (buttons 1-11), 11 down the right (12-22).</summary>
        public const int JobRowsPerColumn = 11;

        /// <summary>The job a <c>joblevel</c> row shows: the left column holds Warrior, White Mage, Red Mage ... (odd ids), the right Monk, Black Mage, Thief ... (even ids), as retail lists them.</summary>
        public static byte JobOfRow(int buttonId) => buttonId switch
        {
            >= 1 and <= JobRowsPerColumn => (byte)(buttonId * 2 - 1),
            > JobRowsPerColumn and <= JobRowsPerColumn * 2 => (byte)((buttonId - JobRowsPerColumn) * 2),
            _ => 0,
        };

        /// <summary>The menu a page opens in.</summary>
        public static string MenuOf(StockUiStatusPage page) => page switch
        {
            StockUiStatusPage.Profile => ProfileMenu,
            StockUiStatusPage.JobLevels => JobLevelMenu,
            StockUiStatusPage.CombatSkills => CombatSkillMenu,
            StockUiStatusPage.MagicSkills or StockUiStatusPage.CraftSkills => MagicSkillMenu,
            _ => string.Empty,
        };

        /// <summary>Combat skills in retail's order (LandSandBoat <c>xi.skill</c> ids: 1-12 the weapons, 25-31 ranged and defence; the automaton's 22-24 are not listed).</summary>
        public static readonly IReadOnlyList<(int Id, string Name)> CombatSkills = new[]
        {
            (1, "Hand-to-Hand"), (2, "Dagger"), (3, "Sword"), (4, "Great Sword"), (5, "Axe"), (6, "Great Axe"), (7, "Scythe"),
            (8, "Polearm"), (9, "Katana"), (10, "Great Katana"), (11, "Club"), (12, "Staff"), (25, "Archery"), (26, "Marksmanship"),
            (27, "Throwing"), (28, "Guarding"), (29, "Evasion"), (30, "Shield"), (31, "Parrying"),
        };

        /// <summary>Magic skills (<c>xi.skill</c> 32-45), named without "Magic" as retail's page writes them ("Divine", "Enfeebling"; round 1 capture image-3, 2026-10-25).</summary>
        public static readonly IReadOnlyList<(int Id, string Name)> MagicSkills = new[]
        {
            (32, "Divine"), (33, "Healing"), (34, "Enhancing"), (35, "Enfeebling"), (36, "Elemental"),
            (37, "Dark"), (38, "Summoning"), (39, "Ninjutsu"), (40, "Singing"), (41, "String Instrument"),
            (42, "Wind Instrument"), (43, "Blue"), (44, "Geomancy"), (45, "Handbell"),
        };

        /// <summary>Crafts (<c>xi.skill</c> 48-57).</summary>
        public static readonly IReadOnlyList<(int Id, string Name)> CraftSkills = new[]
        {
            (48, "Fishing"), (49, "Woodworking"), (50, "Smithing"), (51, "Goldsmithing"), (52, "Clothcraft"), (53, "Leathercraft"),
            (54, "Bonecraft"), (55, "Alchemy"), (56, "Cooking"), (57, "Synergy"),
        };

        /// <summary>Craft rank names by rank (LandSandBoat <c>xi.craftRank</c>: 0 Amateur ... 15 Legend).</summary>
        private static readonly string[] CraftRanks =
        {
            "Amateur", "Recruit", "Initiate", "Novice", "Apprentice", "Journeyman", "Craftsman", "Artisan", "Adept", "Veteran",
            "Expert", "Authority", "Luminary", "Master", "Grandmaster", "Legend",
        };

        public static string CraftRankName(int rank) => rank >= 0 && rank < CraftRanks.Length ? CraftRanks[rank] : string.Empty;

        /// <summary>
        /// The combat or magic skill lines from S2C 0x062's raw skill words (<c>level | 0x8000</c> when capped). Skills at 0 are
        /// left out, as retail's Combat Skills page leaves out a Puppetmaster's Katana and Great Katana (capture 0:13).
        /// </summary>
        public static IReadOnlyList<StockUiSkillLine> SkillLines(IReadOnlyList<(int Id, string Name)> skills, IReadOnlyList<ushort> raw)
        {
            var lines = new List<StockUiSkillLine>(skills.Count);
            foreach (var (id, name) in skills)
            {
                ushort word = id < raw.Count ? raw[id] : (ushort)0;
                int level = word & 0x7FFF;
                if (level == 0) continue;
                lines.Add(new StockUiSkillLine(name, level, (word & 0x8000) != 0));
            }
            return lines;
        }

        /// <summary>
        /// The craft lines from S2C 0x062's craft words (<c>level * 0x20 + rank</c>, plus the cap bit; 0xFFFF unused). Every
        /// craft is listed, at Amateur 0 when untrained.
        /// </summary>
        public static IReadOnlyList<StockUiSkillLine> CraftLines(IReadOnlyList<ushort> raw)
        {
            var lines = new List<StockUiSkillLine>(CraftSkills.Count);
            foreach (var (id, name) in CraftSkills)
            {
                ushort word = id < raw.Count ? raw[id] : (ushort)0;
                if (word == 0xFFFF) word = 0;
                lines.Add(new StockUiSkillLine(name, (word & 0x7FFF) >> 5, (word & 0x8000) != 0, CraftRankName(word & 0x1F)));
            }
            return lines;
        }

        /// <summary>A number as the level box writes it: thousands separated by commas ("55,999").</summary>
        public static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>The nation's name (S2C 0x061 <c>Nation</c>: 0 San d'Oria, 1 Bastok, 2 Windurst).</summary>
        public static string NationName(int nation) => nation switch
        {
            0 => "San d'Oria",
            1 => "Bastok",
            2 => "Windurst",
            _ => string.Empty,
        };

        /// <summary>The Gladiator rank as the Profile writes it (S2C 0x063 type 3: 0 Gla, 1 CG, 2 HCG; XiPackets world/server/0x0063).</summary>
        public static string GladiatorRankName(int rank) => rank switch
        {
            1 => "CG",
            2 => "HCG",
            _ => "Gla",
        };

        /// <summary>
        /// The mastery rank the Profile writes: <c>mastery_rank</c> wraps every 10 onto the next mentor flag (11 is silver
        /// rank 1, 21 gold rank 1; XiPackets world/server/0x001B).
        /// </summary>
        public static int MasteryRankShown(int masteryRank) => masteryRank <= 0 ? 0 : (masteryRank - 1) % 10 + 1;

        /// <summary>The slots the item level averages: the main weapon and the five armour slots (the sub slot joins when it holds a weapon).</summary>
        private static readonly EquipSlotId[] ItemLevelSlots =
        {
            EquipSlotId.Main, EquipSlotId.Head, EquipSlotId.Body, EquipSlotId.Hands, EquipSlotId.Legs, EquipSlotId.Feet,
        };

        /// <summary>
        /// The item level the stats side writes ("ILv 119"): the average (rounded down) of the worn main weapon, head, body,
        /// hands, legs and feet, and the sub weapon when there is one (a shield, a grip or an empty slot is left out: a level-99
        /// Aegis does not lower it). Each item counts its item level, or its equip level when it has none. Null ("---", as the
        /// retail capture of a character in level-99 gear shows) when nothing worn has an item level. PROVISIONAL: the
        /// maintainer's account of retail (round 1 of the 2026-10-24 test); whether an empty armour slot counts as 0 is not
        /// checked. The server's own field (S2C 0x061 <c>ilvl</c>) is not this: LandSandBoat sends the bonus over level 99
        /// there (<c>charutils::getItemLevelDifference</c>, 20 in full item-level-119 gear).
        /// </summary>
        public static int? ItemLevel(InventoryState? inventory, Func<ushort, ItemRecord?>? lookup)
        {
            if (inventory == null || lookup == null) return null;
            int sum = 0, count = 0;
            bool any = false;
            void Add(ItemRecord record)
            {
                any |= record.ItemLevel > 0;
                sum += record.ItemLevel > 0 ? record.ItemLevel : record.Level;
                count++;
            }
            foreach (var slot in ItemLevelSlots)
            {
                if (StockUiItems.EquippedItem(inventory, slot) is { } item && lookup(item.ItemId) is { } record) Add(record);
            }
            if (StockUiItems.EquippedItem(inventory, EquipSlotId.Sub) is { } sub && lookup(sub.ItemId) is { Damage: > 0, ShieldSize: 0 } weapon) Add(weapon);
            return any && count > 0 ? sum / count : null;
        }

        /// <summary>The race's name (the entity's race: 1/2 Hume, 3/4 Elvaan, 5/6 Tarutaru, 7 Mithra, 8 Galka).</summary>
        public static string RaceName(int race) => race switch
        {
            1 or 2 => "Hume",
            3 or 4 => "Elvaan",
            5 or 6 => "Tarutaru",
            7 => "Mithra",
            8 => "Galka",
            _ => string.Empty,
        };
    }
}
