// src/Gordian.Engine/Ui/StockUiQuestLog.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Gordian.Core.World;

namespace Gordian.Engine.Ui
{
    /// <summary>
    /// One book of the quest log: a quest area (its current and completed quests) or a mission storyline. The quest areas
    /// come first, in <see cref="QuestLogArea"/> order.
    /// </summary>
    public enum StockUiQuestLogBook
    {
        QuestsSandoria, QuestsBastok, QuestsWindurst, QuestsJeuno, QuestsOtherAreas, QuestsOutlands, QuestsAhtUrhgan,
        QuestsCrystalWar, QuestsAbyssea, QuestsAdoulin, QuestsCoalition,
        MissionsSandoria, MissionsBastok, MissionsWindurst, MissionsZilart, MissionsCop, MissionsToau, MissionsAssault,
        MissionsWotg, MissionsCampaign, MissionsAcp, MissionsAmk, MissionsAsa, MissionsSoa, MissionsRov, MissionsTvr,
    }

    /// <summary>
    /// One quest or mission of a name table: its id (the quest's bit in the area's block, or the mission's number), its name
    /// without the table's marks, the Client / Summary or Mission Orders text, how far it is indented (the table's leading
    /// "__" pairs: the Promathia, Adoulin, Rhapsodies and Voracious Resurgence chapters hold their missions one level in)
    /// and whether the table marks it with a leading "+" (the quests that can be repeated: Eco-Warrior, Escort for Hire,
    /// most of Abyssea's, every Coalition assignment).
    /// </summary>
    public sealed record StockUiQuestLogEntry(int Id, string Name, string Description, int Depth = 0, bool Repeatable = false);

    /// <summary>How a mission row stands: done, the one in progress, or a chapter heading reached.</summary>
    public enum StockUiQuestLogStatus { Completed, Current, Heading }

    /// <summary>One row of a quest or mission list.</summary>
    public readonly record struct StockUiQuestLogRow(StockUiQuestLogEntry Entry, StockUiQuestLogStatus Status);

    /// <summary>What a paging window's line is: a heading ("Current:"), a quest or mission the cursor can rest on, or a note.</summary>
    public enum StockUiQuestLogLineKind { Heading, Entry, Note }

    /// <summary>One line of a Missions or Quests page.</summary>
    public readonly record struct StockUiQuestLogLine(StockUiQuestLogLineKind Kind, string Text, StockUiQuestLogRow? Row)
    {
        public static StockUiQuestLogLine Heading(string text) => new(StockUiQuestLogLineKind.Heading, text, null);

        public static StockUiQuestLogLine Note(string text) => new(StockUiQuestLogLineKind.Note, text, null);

        public static StockUiQuestLogLine Of(StockUiQuestLogRow row) => new(StockUiQuestLogLineKind.Entry, row.Entry.Name, row);

        public bool IsEntry => Kind == StockUiQuestLogLineKind.Entry;
    }

    /// <summary>
    /// The quest and mission name tables (#488), read once per book: the English <c>d_msg</c> tables of file ids 55706-55742
    /// (<see cref="StockUiQuestLog.TableOf"/>), each record an id, a name and a description. They replace the older mission
    /// text file ROM/118/115 (sections <c>sd_qs_e</c>, <c>zl_ms_e</c>...), which carries only the books up to Treasures of Aht
    /// Urhgan; the newer tables carry every book and the same names and ids. Rows whose name is Japanese are the tables'
    /// unused slots ("ZLシナリオ1", "ASミッション00") and are left out.
    /// </summary>
    public sealed class StockUiQuestLogCatalog
    {
        private readonly Func<StockUiQuestLogBook, IReadOnlyList<StockUiQuestLogEntry>?> _load;
        private readonly Dictionary<StockUiQuestLogBook, IReadOnlyList<StockUiQuestLogEntry>> _books = new();
        private readonly object _sync = new();

        private StockUiQuestLogCatalog(Func<StockUiQuestLogBook, IReadOnlyList<StockUiQuestLogEntry>?> load) => _load = load;

        /// <summary>A book's entries in the table's order (empty without its table).</summary>
        public IReadOnlyList<StockUiQuestLogEntry> Entries(StockUiQuestLogBook book)
        {
            lock (_sync)
            {
                if (!_books.TryGetValue(book, out var entries))
                {
                    entries = _load(book) ?? Array.Empty<StockUiQuestLogEntry>();
                    _books[book] = entries;
                }
                return entries;
            }
        }

        /// <summary>A catalog over the game's tables (<paramref name="table"/> loads a <c>d_msg</c> table by category).</summary>
        public static StockUiQuestLogCatalog FromTables(Func<DMsgCategory, DMsgStringTable?> table) =>
            new(book => Parse(table(StockUiQuestLog.TableOf(book))));

        /// <summary>A catalog from entries directly (tests).</summary>
        public static StockUiQuestLogCatalog FromEntries(IReadOnlyDictionary<StockUiQuestLogBook, IReadOnlyList<StockUiQuestLogEntry>> books) =>
            new(book => books.TryGetValue(book, out var entries) ? entries : null);

        /// <summary>A table's records as entries: sub-field 0 the id, then the name and the description.</summary>
        public static IReadOnlyList<StockUiQuestLogEntry>? Parse(DMsgStringTable? table)
        {
            if (table == null) return null;
            var entries = new List<StockUiQuestLogEntry>(table.Count);
            foreach (var record in table.Records)
            {
                if (!record.TryGetNumber(0, out uint id)) continue;
                record.NamedFields.TryGetValue("name", out var name);
                record.NamedFields.TryGetValue("description", out var description);
                if (string.IsNullOrEmpty(name) || IsJapanese(name)) continue;
                entries.Add(Entry((int)id, name, description ?? string.Empty));
            }
            return entries;
        }

        /// <summary>An entry from a table name: a leading "+" marks a repeatable quest, each leading "__" one level in.</summary>
        public static StockUiQuestLogEntry Entry(int id, string name, string description)
        {
            bool repeatable = name.StartsWith('+');
            if (repeatable) name = name[1..];
            int depth = 0;
            while (name.StartsWith("__", StringComparison.Ordinal))
            {
                depth++;
                name = name[2..];
            }
            return new StockUiQuestLogEntry(id, name, description, depth, repeatable);
        }

        private static bool IsJapanese(string text)
        {
            foreach (char c in text)
            {
                if (c is >= '぀' and <= '鿿') return true;
            }
            return false;
        }
    }

    /// <summary>
    /// What S2C 0x056 told the client of its quests and missions, copied out of <see cref="ProgressionState"/> when a list
    /// opens. The tables are the retail <c>MISSION_DATA</c> blocks (XiPackets <c>world/server/0x0056</c>):
    /// <list type="bullet">
    /// <item><c>QuestOffer</c> / <c>QuestComplete</c>: 256 bits per quest area (8 words, <see cref="QuestLogArea"/> order). In
    /// the Aht Urhgan block only words 0-3 are quests: LandSandBoat puts the current Assault, Treasures of Aht Urhgan,
    /// Wings of the Goddess and Campaign missions in its <c>QuestOffer</c> words 4-7, and the completed Assault missions in
    /// its <c>QuestComplete</c> words 4-7.</item>
    /// <item><c>MissionComplete</c>: 64 bits per nation (words 0-1 San d'Oria, 2-3 Bastok, 4-5 Windurst) and Rise of the
    /// Zilart (6-7), Treasures of Aht Urhgan (8-9), Wings of the Goddess (10-11), the Campaign (16-31).</item>
    /// <item>The main port: the home nation and its current mission (0xFFFF none), Rise of the Zilart (0xFFFF none),
    /// Chains of Promathia, the three add-on scenarios (4 bits each), Seekers of Adoulin and Rhapsodies of Vana'diel (999
    /// when the story is done), the Tales' Beginning flags; port 0xFFFE The Voracious Resurgence.</item>
    /// </list>
    /// Table layout referenced from XiPackets (https://github.com/atom0s/XiPackets), <c>world/server/0x0056</c>, and
    /// LandSandBoat (https://github.com/LandSandBoat/server, <c>src/map/packets/s2c/0x056_mission_other.cpp</c>).
    /// </summary>
    public sealed class StockUiQuestLogState
    {
        public uint[] QuestOffer { get; init; } = new uint[MissionPorts.QuestOfferWords];
        public uint[] QuestComplete { get; init; } = new uint[MissionPorts.QuestCompleteWords];
        public uint[] MissionComplete { get; init; } = new uint[MissionPorts.MissionCompleteWords];

        /// <summary>The home nation: 0 San d'Oria, 1 Bastok, 2 Windurst.</summary>
        public int Nation { get; init; }

        public uint NationMission { get; init; } = 0xFFFF;
        public uint Zilart { get; init; } = 0xFFFF;
        public uint Promathia { get; init; }
        public uint Addons { get; init; }
        public uint TalesBeginning { get; init; }
        public uint Adoulin { get; init; }
        public uint Rhapsodies { get; init; }
        public uint Voracious { get; init; }

        /// <summary>The quest log as the client holds it now.</summary>
        public static StockUiQuestLogState From(ProgressionState progression)
        {
            var offer = new uint[MissionPorts.QuestOfferWords];
            var complete = new uint[MissionPorts.QuestCompleteWords];
            var missions = new uint[MissionPorts.MissionCompleteWords];
            progression.CopyMissionTable(MissionTable.QuestOffer, offer);
            progression.CopyMissionTable(MissionTable.QuestComplete, complete);
            progression.CopyMissionTable(MissionTable.MissionComplete, missions);
            return new StockUiQuestLogState
            {
                QuestOffer = offer,
                QuestComplete = complete,
                MissionComplete = missions,
                Nation = (int)progression.NationId,
                NationMission = progression.NationMissionId,
                Zilart = progression.ExpansionRotZ,
                Promathia = progression.ExpansionCoP,
                Addons = progression.ExpansionAddons,
                TalesBeginning = progression.TalesBeginning,
                Adoulin = progression.ExpansionSoA,
                Rhapsodies = progression.ExpansionRoV,
                Voracious = progression.ExpansionTvr,
            };
        }

        /// <summary>Bit <paramref name="bit"/> of a table, counted from word <paramref name="word"/>.</summary>
        public static bool Bit(uint[] table, int word, int bit)
        {
            if (bit < 0) return false;
            int w = word + (bit >> 5);
            return w < table.Length && (table[w] & (1u << (bit & 31))) != 0;
        }
    }

    /// <summary>
    /// The Missions and Quests windows (#488; main menu page 2 > Missions / Quests), laid out on the English menu DAT's
    /// windows (ROM/119/51, read with <c>GORDIAN_UI_DUMP_MENUS=missionm,miss00,missiton,quest00,quest01,quset</c>) as the
    /// maintainer's retail screenshots show them (round 1 of #488, 2026-11-01: image-11, image-12):
    /// <list type="bullet">
    /// <item>Missions: <c>missionm</c> (384,48, 112 x 122, title 85 "Missions", help 468 "View list of current missions.")
    /// lists the groups as label sprites: Nations, Zilart, Promathia, Aht Urhgan, Altana, Adoulin, Add-ons. Every group opens
    /// the one paging window, <c>miss00</c> (16,48, 366 x 248), on its first storyline (Nations on the home nation's);
    /// left and right page through every storyline (<see cref="MissionPages"/>), the storyline's name between the window's
    /// red arrows (buttons 9 and 10, frames#25 / #26), named by the window title table (ROM/165/76): San d'Oria, Bastok,
    /// Windurst (21-23), Treasures of Aht Urhgan (135), Assault (134), Wings of the Goddess (144), Campaign (143), A
    /// Crystalline Prophecy (152), A Moogle Kupo d'Etat (153), A Shantotto Ascension (154), Rhapsodies of Vana'diel (183),
    /// The Voracious Resurgence (188). The eight 320 x 18 rows (x 39, y 40 + 25 n) hold "Current:", the mission in progress,
    /// "Completed:" and the missions done (a chapter reached among them), the cursor only on the missions and opening on the
    /// one in progress (image-11). A nation other than the home nation says "Not allowed under current allegiance."
    /// (XiPackets; retail shows it under Current, round 1). Confirm shows the mission's orders in <c>missiton</c>.</item>
    /// <item>Quests: <c>quest00</c> (16,48, 366 x 248) offers Current and Completed (its rows 1-2, x 39, y 31 + 37 n;
    /// image-12); either opens <c>quest01</c>, the same paging window as Missions, over the eleven areas, each page titled
    /// "San d'Oria: Current Quests" (titles 25-36, 136-137, 145-146, 156-157, 160-161, 163-164) and listing the area's quests.
    /// An empty page says "No current quests." / "No completed quests." (ROM/165/72 #99, #100). Confirm shows the quest's
    /// Client and Summary in <c>quset</c>.</item>
    /// </list>
    /// PROVISIONAL: the page order and its wrap at the ends, the Quests window opening on San d'Oria, the text places, the
    /// colours, the completed missions in the table's order and the empty mission page are our reading. Not done: the
    /// Records of Eminence and Vana'Bout part of <c>quest00</c> (rows 3-5).
    /// </summary>
    public static class StockUiQuestLog
    {
        public const string GroupMenu = "missionm", StorylineMenu = "miss00", MissionListMenu = "missiton";
        public const string AreaMenu = "quest01", QuestKindMenu = "quest00", QuestListMenu = "quset";

        public const string MissionsTitle = "Missions", QuestsTitle = "Quests";

        /// <summary>Main menu page 2's helps (ROM/165/75): 468 "View list of current missions.", 469 "View list of current quests."</summary>
        public const int HelpMissions = 468, HelpQuests = 469;

        /// <summary><c>miss00</c> / <c>quest01</c>: eight rows at x 39, y 40 + 25 n, 320 x 18.</summary>
        public const int PickerRows = 8, PickerRowX = 39, PickerRowY = 40, PickerRowPitch = 25, PickerRowWidth = 320, PickerRowHeight = 18;

        /// <summary>The paging window's red arrows (<c>miss00</c> / <c>quest01</c> buttons 9 and 10).</summary>
        public const int PreviousPageButton = 9, NextPageButton = 10;

        /// <summary>A mission page's headings (retail image-11).</summary>
        public const string CurrentHeading = "Current:", CompletedHeading = "Completed:";

        /// <summary><c>quest00</c>: rows at x 39, y 31 + 37 n, 320 x 18; the first two used (Current, Completed).</summary>
        public const int KindRowX = 39, KindRowY = 31, KindRowPitch = 37;

        /// <summary><c>quest00</c>'s rows (retail image-12).</summary>
        public const string CurrentKind = "Current", CompletedKind = "Completed";

        /// <summary>The menu message table's (ROM/165/72) quest texts.</summary>
        public const int NoCurrentQuestsText = 99, NoCompletedQuestsText = 100;

        public const string NoCurrentQuestsFallback = "No current quests.", NoCompletedQuestsFallback = "No completed quests.";

        /// <summary>The home nation's missions only (XiPackets <c>world/server/0x0056</c>, <c>Nation</c>).</summary>
        public const string NotAllowedText = "Not allowed under current allegiance.";

        /// <summary>
        /// An empty Current or Completed section (retail image-48, round 2 of #488; in no string table of the install, so
        /// written here as "Not allowed" is).
        /// </summary>
        public const string NoCurrentMissionsText = "No current missions.", NoCompletedMissionsText = "No completed missions.";

        /// <summary>The Missions window's pages in order: the groups' storylines, the groups as <c>missionm</c> lists them.</summary>
        public static readonly IReadOnlyList<StockUiQuestLogBook> MissionPages = new[]
        {
            StockUiQuestLogBook.MissionsSandoria, StockUiQuestLogBook.MissionsBastok, StockUiQuestLogBook.MissionsWindurst, StockUiQuestLogBook.MissionsZilart,
            StockUiQuestLogBook.MissionsCop, StockUiQuestLogBook.MissionsToau, StockUiQuestLogBook.MissionsAssault, StockUiQuestLogBook.MissionsWotg,
            StockUiQuestLogBook.MissionsCampaign, StockUiQuestLogBook.MissionsSoa, StockUiQuestLogBook.MissionsAcp, StockUiQuestLogBook.MissionsAmk,
            StockUiQuestLogBook.MissionsAsa, StockUiQuestLogBook.MissionsRov, StockUiQuestLogBook.MissionsTvr,
        };

        /// <summary>The Missions window's groups: the <c>missionm</c> buttons 1-7 and the storylines each holds.</summary>
        public static readonly IReadOnlyDictionary<int, IReadOnlyList<StockUiQuestLogBook>> Groups = new Dictionary<int, IReadOnlyList<StockUiQuestLogBook>>
        {
            [1] = new[] { StockUiQuestLogBook.MissionsSandoria, StockUiQuestLogBook.MissionsBastok, StockUiQuestLogBook.MissionsWindurst },
            [2] = new[] { StockUiQuestLogBook.MissionsZilart },
            [3] = new[] { StockUiQuestLogBook.MissionsCop },
            [4] = new[] { StockUiQuestLogBook.MissionsToau, StockUiQuestLogBook.MissionsAssault },
            [5] = new[] { StockUiQuestLogBook.MissionsWotg, StockUiQuestLogBook.MissionsCampaign },
            [7] = new[] { StockUiQuestLogBook.MissionsSoa },
            [6] = new[] { StockUiQuestLogBook.MissionsAcp, StockUiQuestLogBook.MissionsAmk, StockUiQuestLogBook.MissionsAsa, StockUiQuestLogBook.MissionsRov, StockUiQuestLogBook.MissionsTvr },
        };

        /// <summary>The <c>missionm</c> group buttons in their authored top-to-bottom order (Add-ons, button 6, is last).</summary>
        public static readonly IReadOnlyList<int> GroupButtons = new[] { 1, 2, 3, 4, 5, 7, 6 };

        /// <summary>The quest areas in the Quests window's order (<see cref="QuestLogArea"/> order).</summary>
        public static readonly IReadOnlyList<StockUiQuestLogBook> Areas = new[]
        {
            StockUiQuestLogBook.QuestsSandoria, StockUiQuestLogBook.QuestsBastok, StockUiQuestLogBook.QuestsWindurst, StockUiQuestLogBook.QuestsJeuno,
            StockUiQuestLogBook.QuestsOtherAreas, StockUiQuestLogBook.QuestsOutlands, StockUiQuestLogBook.QuestsAhtUrhgan, StockUiQuestLogBook.QuestsCrystalWar,
            StockUiQuestLogBook.QuestsAbyssea, StockUiQuestLogBook.QuestsAdoulin, StockUiQuestLogBook.QuestsCoalition,
        };

        public static bool IsQuestArea(StockUiQuestLogBook book) => book <= StockUiQuestLogBook.QuestsCoalition;

        /// <summary>The <c>d_msg</c> table that names a book's quests or missions.</summary>
        public static DMsgCategory TableOf(StockUiQuestLogBook book) => book switch
        {
            StockUiQuestLogBook.QuestsSandoria => DMsgCategory.QuestsSandoria,
            StockUiQuestLogBook.QuestsBastok => DMsgCategory.QuestsBastok,
            StockUiQuestLogBook.QuestsWindurst => DMsgCategory.QuestsWindurst,
            StockUiQuestLogBook.QuestsJeuno => DMsgCategory.QuestsJeuno,
            StockUiQuestLogBook.QuestsOtherAreas => DMsgCategory.QuestsOther,
            StockUiQuestLogBook.QuestsOutlands => DMsgCategory.QuestsOutlands,
            StockUiQuestLogBook.QuestsAhtUrhgan => DMsgCategory.QuestsAhtUrhgan,
            StockUiQuestLogBook.QuestsCrystalWar => DMsgCategory.QuestsCrystalWar,
            StockUiQuestLogBook.QuestsAbyssea => DMsgCategory.QuestsAbyssea,
            StockUiQuestLogBook.QuestsAdoulin => DMsgCategory.QuestsAdoulin,
            StockUiQuestLogBook.QuestsCoalition => DMsgCategory.QuestsCoalition,
            StockUiQuestLogBook.MissionsSandoria => DMsgCategory.MissionsSandoria,
            StockUiQuestLogBook.MissionsBastok => DMsgCategory.MissionsBastok,
            StockUiQuestLogBook.MissionsWindurst => DMsgCategory.MissionsWindurst,
            StockUiQuestLogBook.MissionsZilart => DMsgCategory.MissionsZilart,
            StockUiQuestLogBook.MissionsCop => DMsgCategory.MissionsCop,
            StockUiQuestLogBook.MissionsToau => DMsgCategory.MissionsToau,
            StockUiQuestLogBook.MissionsAssault => DMsgCategory.MissionsAssault,
            StockUiQuestLogBook.MissionsWotg => DMsgCategory.MissionsWotg,
            StockUiQuestLogBook.MissionsCampaign => DMsgCategory.MissionsCampaign,
            StockUiQuestLogBook.MissionsAcp => DMsgCategory.MissionsAcp,
            StockUiQuestLogBook.MissionsAmk => DMsgCategory.MissionsAmk,
            StockUiQuestLogBook.MissionsAsa => DMsgCategory.MissionsAsa,
            StockUiQuestLogBook.MissionsSoa => DMsgCategory.MissionsAdoulin,
            StockUiQuestLogBook.MissionsRov => DMsgCategory.MissionsRov,
            _ => DMsgCategory.MissionsTvr,
        };

        /// <summary>A book's name as the window title table (ROM/165/76) writes it; the quest areas by their list titles' first part.</summary>
        public static string NameOf(StockUiQuestLogBook book) => book switch
        {
            StockUiQuestLogBook.QuestsSandoria or StockUiQuestLogBook.MissionsSandoria => "San d'Oria",
            StockUiQuestLogBook.QuestsBastok or StockUiQuestLogBook.MissionsBastok => "Bastok",
            StockUiQuestLogBook.QuestsWindurst or StockUiQuestLogBook.MissionsWindurst => "Windurst",
            StockUiQuestLogBook.QuestsJeuno => "Jeuno",
            StockUiQuestLogBook.QuestsOtherAreas => "Other Areas",
            StockUiQuestLogBook.QuestsOutlands => "Outlands",
            StockUiQuestLogBook.QuestsAhtUrhgan => "Aht Urhgan",
            StockUiQuestLogBook.QuestsCrystalWar => "Crystal War",
            StockUiQuestLogBook.QuestsAbyssea => "Abyssea",
            StockUiQuestLogBook.QuestsAdoulin => "Adoulin",
            StockUiQuestLogBook.QuestsCoalition => "Coalition",
            StockUiQuestLogBook.MissionsZilart => "Rise of the Zilart",
            StockUiQuestLogBook.MissionsCop => "Chains of Promathia",
            StockUiQuestLogBook.MissionsToau => "Treasures of Aht Urhgan",
            StockUiQuestLogBook.MissionsAssault => "Assault",
            StockUiQuestLogBook.MissionsWotg => "Wings of the Goddess",
            StockUiQuestLogBook.MissionsCampaign => "Campaign",
            StockUiQuestLogBook.MissionsAcp => "A Crystalline Prophecy",
            StockUiQuestLogBook.MissionsAmk => "A Moogle Kupo d'Etat",
            StockUiQuestLogBook.MissionsAsa => "A Shantotto Ascension",
            StockUiQuestLogBook.MissionsSoa => "Seekers of Adoulin",
            StockUiQuestLogBook.MissionsRov => "Rhapsodies of Vana'diel",
            _ => "The Voracious Resurgence",
        };

        /// <summary>
        /// A quest list's title (ROM/165/76 25-36, 136, 137, 145, 146, 156, 157, 160, 161, 163, 164): "San d'Oria: Current
        /// Quests"; Coalition's are its assignments.
        /// </summary>
        public static string QuestListTitle(StockUiQuestLogBook area, bool completed) => area == StockUiQuestLogBook.QuestsCoalition
            ? completed ? "Coalition: Completed Assignments" : "Coalition: Current Assignments"
            : NameOf(area) + (completed ? ": Completed Quests" : ": Current Quests");

        /// <summary>
        /// An area's current (offered, not yet complete) or completed quests in the table's order. The Aht Urhgan block holds
        /// quests in its first 128 bits only (<see cref="StockUiQuestLogState"/>).
        /// </summary>
        public static List<StockUiQuestLogRow> QuestRows(StockUiQuestLogCatalog catalog, StockUiQuestLogState state, StockUiQuestLogBook area, bool completed)
        {
            var rows = new List<StockUiQuestLogRow>();
            int word = (int)area * 8;
            int limit = area == StockUiQuestLogBook.QuestsAhtUrhgan ? 128 : 256;
            foreach (var entry in catalog.Entries(area))
            {
                if (entry.Id < 0 || entry.Id >= limit) continue;
                bool done = StockUiQuestLogState.Bit(state.QuestComplete, word, entry.Id);
                bool offered = StockUiQuestLogState.Bit(state.QuestOffer, word, entry.Id);
                if (completed ? done : offered && !done) rows.Add(new StockUiQuestLogRow(entry, completed ? StockUiQuestLogStatus.Completed : StockUiQuestLogStatus.Current));
            }
            return rows;
        }

        /// <summary>The page a group opens on: its first storyline, the home nation's for Nations.</summary>
        public static StockUiQuestLogBook FirstPageOf(int group, StockUiQuestLogState state) =>
            group == 1 ? StockUiQuestLogBook.MissionsSandoria + Math.Clamp(state.Nation, 0, 2)
            : Groups.TryGetValue(group, out var books) ? books[0] : StockUiQuestLogBook.MissionsSandoria;

        /// <summary>A page's title between the arrows: the storyline, or the quest list ("San d'Oria: Current Quests").</summary>
        public static string PageTitle(StockUiQuestLogBook book, bool completed) => IsQuestArea(book) ? QuestListTitle(book, completed) : NameOf(book);

        /// <summary>
        /// A page's lines: a quest area's quests (current or completed), or a storyline's "Current:" and the mission in progress
        /// (another nation's: <see cref="NotAllowedText"/>), then "Completed:" and the missions done with the chapters reached
        /// (another nation's too); an empty section says so (<see cref="NoCurrentMissionsText"/>, <see cref="NoCompletedMissionsText"/>).
        /// <paramref name="emptyQuests"/> is the empty quest page's text.
        /// </summary>
        public static List<StockUiQuestLogLine> PageLines(StockUiQuestLogCatalog catalog, StockUiQuestLogState state, StockUiQuestLogBook book, bool completed,
            string? emptyQuests = null)
        {
            var lines = new List<StockUiQuestLogLine>();
            if (IsQuestArea(book))
            {
                foreach (var row in QuestRows(catalog, state, book, completed)) lines.Add(StockUiQuestLogLine.Of(row));
                if (lines.Count == 0) lines.Add(StockUiQuestLogLine.Note(emptyQuests ?? (completed ? NoCompletedQuestsFallback : NoCurrentQuestsFallback)));
                return lines;
            }
            var rows = MissionRows(catalog, state, book);
            lines.Add(StockUiQuestLogLine.Heading(CurrentHeading));
            int before = lines.Count;
            if (!IsAllowed(state, book)) lines.Add(StockUiQuestLogLine.Note(NotAllowedText));
            foreach (var row in rows)
            {
                if (row.Status == StockUiQuestLogStatus.Current) lines.Add(StockUiQuestLogLine.Of(row));
            }
            if (lines.Count == before) lines.Add(StockUiQuestLogLine.Note(NoCurrentMissionsText));
            lines.Add(StockUiQuestLogLine.Heading(CompletedHeading));
            before = lines.Count;
            foreach (var row in rows)
            {
                if (row.Status != StockUiQuestLogStatus.Current) lines.Add(StockUiQuestLogLine.Of(row));
            }
            if (lines.Count == before) lines.Add(StockUiQuestLogLine.Note(NoCompletedMissionsText));
            return lines;
        }

        /// <summary>Whether a storyline is one the character may view: every one but another nation's, whose mission in progress is not shown.</summary>
        public static bool IsAllowed(StockUiQuestLogState state, StockUiQuestLogBook book) => book switch
        {
            StockUiQuestLogBook.MissionsSandoria => state.Nation == 0,
            StockUiQuestLogBook.MissionsBastok => state.Nation == 1,
            StockUiQuestLogBook.MissionsWindurst => state.Nation == 2,
            _ => true,
        };

        /// <summary>The Tales' Beginning flag of a storyline (0 for those without one).</summary>
        private static uint TalesFlag(StockUiQuestLogBook book) => book switch
        {
            StockUiQuestLogBook.MissionsZilart => 0x01,
            StockUiQuestLogBook.MissionsAcp => 0x02,
            StockUiQuestLogBook.MissionsAsa => 0x04,
            StockUiQuestLogBook.MissionsCop => 0x08,
            StockUiQuestLogBook.MissionsSoa => 0x20,
            StockUiQuestLogBook.MissionsRov => 0x40,
            _ => 0,
        };

        /// <summary>The Tales' Beginning row ("To Begin CoP...", id 1000) the tables end with.</summary>
        public const int TalesBeginningId = 1000;

        /// <summary>The value Seekers of Adoulin, Rhapsodies and the Voracious Resurgence take once their story is done.</summary>
        public const int StoryDone = 999;

        /// <summary>
        /// A storyline's missions reached, in the table's order: those done and the one in progress. Bit-table storylines
        /// (the nations, Zilart, Aht Urhgan, Wings of the Goddess, Assault, Campaign) mark each mission done by its bit and
        /// name the current one by id; the numbered storylines (Promathia, the add-on scenarios, Adoulin, Rhapsodies, the
        /// Voracious Resurgence) count every mission below the current number as done, and a chapter heading up to it as
        /// reached. A storyline set aside with Tales' Beginning shows its "To Begin ..." row alone. Another nation's: its missions done, none in progress.
        /// </summary>
        public static List<StockUiQuestLogRow> MissionRows(StockUiQuestLogCatalog catalog, StockUiQuestLogState state, StockUiQuestLogBook book)
        {
            var rows = new List<StockUiQuestLogRow>();
            var entries = catalog.Entries(book);
            uint tales = TalesFlag(book);
            if (tales != 0 && (state.TalesBeginning & tales) != 0)
            {
                foreach (var entry in entries)
                {
                    if (entry.Id == TalesBeginningId) rows.Add(new StockUiQuestLogRow(entry, StockUiQuestLogStatus.Current));
                }
                return rows;
            }

            if (TryBitTable(state, book, out var table, out int word, out uint current))
            {
                foreach (var entry in entries)
                {
                    if (entry.Id >= TalesBeginningId) continue;
                    if (StockUiQuestLogState.Bit(table, word, entry.Id)) rows.Add(new StockUiQuestLogRow(entry, StockUiQuestLogStatus.Completed));
                    else if (entry.Id == current) rows.Add(new StockUiQuestLogRow(entry, StockUiQuestLogStatus.Current));
                }
                return rows;
            }

            uint value = book switch
            {
                StockUiQuestLogBook.MissionsCop => state.Promathia,
                StockUiQuestLogBook.MissionsAcp => state.Addons & 0xF,
                StockUiQuestLogBook.MissionsAmk => (state.Addons >> 4) & 0xF,
                StockUiQuestLogBook.MissionsAsa => (state.Addons >> 8) & 0xF,
                StockUiQuestLogBook.MissionsSoa => state.Adoulin,
                StockUiQuestLogBook.MissionsRov => state.Rhapsodies,
                _ => state.Voracious & 0xEFFF,
            };
            // Nothing started (0); the Voracious Resurgence's bit 31 says its missions are done with none in progress.
            if (value == 0) return rows;
            bool noneActive = value == StoryDone || (book == StockUiQuestLogBook.MissionsTvr && (state.Voracious & 0x80000000u) != 0);
            foreach (var entry in entries)
            {
                if (entry.Id >= StoryDone) continue;
                if (entry.Depth == 0 && entry.Id <= value && entries.Count > 0 && IsHeading(entries, entry))
                {
                    // A chapter is also its first mission: at its number it is reached and in progress (retail image-11:
                    // Promathia's "Ancient Flames Beckon" under Current and Completed).
                    rows.Add(new StockUiQuestLogRow(entry, StockUiQuestLogStatus.Heading));
                    if (entry.Id == value && !noneActive) rows.Add(new StockUiQuestLogRow(entry, StockUiQuestLogStatus.Current));
                }
                else if (entry.Id < value) rows.Add(new StockUiQuestLogRow(entry, StockUiQuestLogStatus.Completed));
                else if (entry.Id == value) rows.Add(new StockUiQuestLogRow(entry, noneActive ? StockUiQuestLogStatus.Completed : StockUiQuestLogStatus.Current));
            }
            return rows;
        }

        /// <summary>A chapter heading: a top-level row followed by indented ones (Promathia's "Ancient Flames Beckon").</summary>
        private static bool IsHeading(IReadOnlyList<StockUiQuestLogEntry> entries, StockUiQuestLogEntry entry)
        {
            for (int i = 0; i < entries.Count - 1; i++)
            {
                if (ReferenceEquals(entries[i], entry)) return entries[i + 1].Depth > 0;
            }
            return false;
        }

        /// <summary>The bit table, its first word and the current mission id of a bit-table storyline.</summary>
        private static bool TryBitTable(StockUiQuestLogState state, StockUiQuestLogBook book, out uint[] table, out int word, out uint current)
        {
            const int ahtUrhgan = (int)QuestLogArea.AhtUrhgan * 8;
            table = state.MissionComplete;
            current = uint.MaxValue;
            switch (book)
            {
                case StockUiQuestLogBook.MissionsSandoria or StockUiQuestLogBook.MissionsBastok or StockUiQuestLogBook.MissionsWindurst:
                    word = (book - StockUiQuestLogBook.MissionsSandoria) * 2;
                    // Another nation's missions done are listed; the one in progress is the home nation's only.
                    if (IsAllowed(state, book)) current = state.NationMission & 0xFFFF;
                    break;
                case StockUiQuestLogBook.MissionsZilart:
                    word = 6;
                    current = state.Zilart & 0xFFFF;
                    break;
                case StockUiQuestLogBook.MissionsToau:
                    word = 8;
                    current = state.QuestOffer[ahtUrhgan + 5];
                    break;
                case StockUiQuestLogBook.MissionsWotg:
                    word = 10;
                    current = state.QuestOffer[ahtUrhgan + 6];
                    break;
                case StockUiQuestLogBook.MissionsCampaign:
                    word = 16;
                    current = state.QuestOffer[ahtUrhgan + 7];
                    break;
                case StockUiQuestLogBook.MissionsAssault:
                    table = state.QuestComplete;
                    word = ahtUrhgan + 4;
                    current = state.QuestOffer[ahtUrhgan + 4];
                    break;
                default:
                    word = 0;
                    return false;
            }
            if (current == 0xFFFF) current = uint.MaxValue;
            return true;
        }
    }
}
