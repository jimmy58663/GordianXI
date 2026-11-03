// src/Gordian.Engine/Ui/StockUiSocial.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Network.Search;

namespace Gordian.Engine.Ui
{
    /// <summary>
    /// One player in a client-drawn people list (the party's member list, search results, a linkshell's members, the friend
    /// list, the blacklist): the name, the ids a command needs, and the text drawn at the row's right.
    /// </summary>
    /// <param name="Name">The character name.</param>
    /// <param name="ServerId">The character's server id when known (party members, nearby players); 0 otherwise.</param>
    /// <param name="TargetIndex">The target index in this zone; 0 when the player is elsewhere or unknown.</param>
    /// <param name="CharacterId">The search server's character id (the low 20 bits); 0 when the list did not come from it.</param>
    /// <param name="Detail">Text drawn at the row's right when the row has no <see cref="Jobs"/> (a linkshell item's slot); null for none.</param>
    /// <param name="LinkshellRank">A linkshell member's rank (1 holder, 2 pearlsack, 3 pearl); 0 elsewhere.</param>
    public readonly record struct StockUiPerson(string Name, uint ServerId = 0, ushort TargetIndex = 0, uint CharacterId = 0,
        string? Detail = null, byte LinkshellRank = 0)
    {
        /// <summary>The left column of a retail people row: the jobs and the race, "[MNK99/RDM49]Elv"; null for none (anonymous).</summary>
        public string? Jobs { get; init; }

        /// <summary>The mission rank drawn on the nation's colour; 0 for none (anonymous, or not from the search server).</summary>
        public byte MissionRank { get; init; }

        /// <summary>The home nation (0 San d'Oria, 1 Bastok, 2 Windurst) behind <see cref="MissionRank"/>.</summary>
        public byte Nation { get; init; }

        /// <summary>The area at the row's right; null when the player is in yours (retail leaves it blank, round 1 image-26).</summary>
        public string? Area { get; init; }

        /// <summary>The languages of a player seeking a party, as letters ("E F"); null for none.</summary>
        public string? Languages { get; init; }

        /// <summary>Whether the player is in your party, or leads it: the name's colour.</summary>
        public StockUiPersonStanding Standing { get; init; }

        /// <summary>The player has a search comment (<c>SearchFlags.HasComment</c>).</summary>
        public bool HasComment { get; init; }

        /// <summary>
        /// The search comment's type (C2S 0x0E0's, 0x11 ... 0x73; its high digit is the type's genre, which picks the dot
        /// beside the name and the comment's colour, round 2 image-54); 0 for none.
        /// </summary>
        public uint CommentType { get; init; }

        /// <summary>The line retail prints above the comment: "Gordian (Lv99 Monk / Lv49 Red Mage):" (round 2 image-54); null for none.</summary>
        public string? CommentHeader { get; init; }

        /// <summary>The linkshell's colour (the 4-bit R, G, B of the item's extra data); 0 when not known.</summary>
        public ushort LinkshellColor { get; init; }
    }

    /// <summary>
    /// A people row's name colour: yellow for your party's leader, cyan for its members, white for everyone else (round 1
    /// image-26, image-28, image-33 and image-44).
    /// </summary>
    public enum StockUiPersonStanding
    {
        Other,
        PartyMember,
        PartyLeader,
    }

    /// <summary>A people list's answer: the players, or why there are none.</summary>
    /// <param name="People">The players, in the order the server gave them.</param>
    /// <param name="Error">A notice to post instead of opening an empty list (the search server did not answer); null when fine.</param>
    public sealed record StockUiPeopleResult(IReadOnlyList<StockUiPerson> People, string? Error = null)
    {
        public static StockUiPeopleResult Failed(string error) => new(Array.Empty<StockUiPerson>(), error);
    }

    /// <summary>What a people list is for: what Confirm on a row does.</summary>
    public enum StockUiPeoplePurpose
    {
        None,

        /// <summary>The Party menu's Member List: Confirm opens the person's options (<c>scoption</c>).</summary>
        MemberList,

        /// <summary>Level Sync (<c>levelsyn</c>): Confirm synchronizes the party's levels to the member (C2S 0x077).</summary>
        LevelSync,

        /// <summary>View Request: the players asking to join; Confirm asks, then invites the player.</summary>
        JoinRequests,

        /// <summary>A search's results (Search, Find Member): Confirm opens the person's options (<c>scoption</c>).</summary>
        SearchResults,

        /// <summary>A linkshell's members (<c>link12</c>): Confirm opens the member options by rank (<c>link7</c> ... <c>link10</c>).</summary>
        LinkshellMembers,

        /// <summary>The friend list (<c>friend</c>): Confirm opens the person's options (<c>scoption</c>).</summary>
        Friends,

        /// <summary>The Linkshell window's items (<c>link5</c>): Confirm opens the item's options.</summary>
        LinkshellItems,

        /// <summary>PlayOnline's messages (<c>msglist</c>): always empty, GordianXI has no PlayOnline service.</summary>
        Messages,

        /// <summary>The Emote List (<c>emote</c>): Confirm performs the emote.</summary>
        Emotes,
    }

    /// <summary>The social commands a menu entry asks the session to run (<see cref="IStockUiSocial.RunAsync"/>).</summary>
    public enum StockUiSocialCommand
    {
        /// <summary>The party flag (seeking a party, <c>/seekparty</c>): C2S 0x0DC InviteFlg; the argument is 1 on, 0 off.</summary>
        SetSeekParty,

        /// <summary>The auto-group flag: C2S 0x0DC AutoPartyFlg; the argument is 1 on, 0 off.</summary>
        SetAutogroup,

        /// <summary>Request acceptance mode (<c>/rec</c>): C2S 0x0DC RecruitFlg; the argument is 1 on, 0 off.</summary>
        SetRecruit,

        /// <summary>The party languages (C2S 0x0DB kind 1); the argument is the <c>PartyLanguages</c> mask.</summary>
        SetPartyLanguages,

        /// <summary>Passes party leadership to the person (as <c>/pcmd leader</c>).</summary>
        PassLeader,

        /// <summary>Removes the person from the party (as <c>/pcmd kick</c>).</summary>
        Kick,

        /// <summary>Synchronizes the party's levels to the person (C2S 0x077 SetLevelSync).</summary>
        LevelSync,

        /// <summary>Ends the party's level sync (C2S 0x077 DisableLevelSync).</summary>
        DisableLevelSync,

        /// <summary>Treasure by lot (C2S 0x077 SetLottery).</summary>
        SetLottery,

        /// <summary>The person collects all treasure (C2S 0x077 SetQuartermaster).</summary>
        SetQuartermaster,

        /// <summary>Invites the targeted player's party into the alliance (as <c>/acmd add &lt;t&gt;</c>).</summary>
        AllianceInviteTarget,

        /// <summary>Breaks the alliance up (as <c>/acmd breakup</c>).</summary>
        AllianceDissolve,

        /// <summary>Invites the person to the party (as <c>/pcmd add</c>).</summary>
        Invite,

        /// <summary>Asks to join the person's party (C2S 0x11C Add).</summary>
        RequestJoin,

        /// <summary>Withdraws a request to join the person's party (C2S 0x11C Remove).</summary>
        CancelRequest,

        /// <summary>Targets the person (a party member in this zone).</summary>
        Target,

        /// <summary>
        /// Focus-targets the party member, or turns focus targeting off when the member already has it ("Focus targeting is
        /// enabled. (Name)" / "Focus targeting is disabled.", round 1 image-29).
        /// </summary>
        Focus,

        /// <summary>
        /// A linkshell member's rank: To Sack (C2S 0x077 change kind 2 PearlToSack) and To Pearl (change kind 3 SackToPearl),
        /// with the worn slot (1 or 2) as the 0x077 kind; LandSandBoat <c>0x077_group_change2.h</c>.
        /// </summary>
        LinkshellToSack,
        LinkshellToPearl,

        /// <summary>Removes a member from the linkshell worn in the slot (the argument): C2S 0x071 with the slot as its kind.</summary>
        LinkshellKick,

        /// <summary>Performs the emote (the person's name is the command, "/bow").</summary>
        Emote,
    }

    /// <summary>The flags the Party menu shows (from the server's S2C 0x0B4 copy of the character's config).</summary>
    /// <param name="SeekingParty">The party flag is on (InviteFlg).</param>
    /// <param name="Autogroup">The auto-group flag is on (AutoPartyFlg).</param>
    /// <param name="Recruiting">Request acceptance mode is on (RecruitFlg).</param>
    /// <param name="PartyLanguages">The party languages (a <c>PartyLanguages</c> mask).</param>
    /// <param name="QuartermasterName">The member collecting all treasure; null when treasure goes by lot.</param>
    /// <param name="LevelSynced">The party is under level sync.</param>
    public readonly record struct StockUiSocialState(bool SeekingParty, bool Autogroup, bool Recruiting, int PartyLanguages,
        string? QuartermasterName = null, bool LevelSynced = false);

    /// <summary>
    /// What the Party, Search, Linkshell and Communication windows ask of the session (#489, #490): its party, the search
    /// server, and the commands the windows run. The session's implementation is <see cref="StockUiSocialActions"/>; tests
    /// give the menus their own.
    /// </summary>
    public interface IStockUiSocial
    {
        /// <summary>The flags and party settings the menus show.</summary>
        StockUiSocialState State { get; }

        /// <summary>The party's members from the party state (the character too when <paramref name="includeSelf"/>), in slot order.</summary>
        IReadOnlyList<StockUiPerson> PartyMembers(bool includeSelf);

        /// <summary>The players asking to join the party (S2C 0x11D).</summary>
        IReadOnlyList<StockUiPerson> JoinRequests();

        /// <summary>The Member List: the search server's party list, else the party state's members.</summary>
        Task<StockUiPeopleResult> MemberListAsync();

        /// <summary>Runs a search on the search server (the same query <c>/sea</c> sends).</summary>
        Task<StockUiPeopleResult> SearchAsync(SearchQuery query);

        /// <summary>The members of the linkshell worn in slot 1 or 2.</summary>
        Task<StockUiPeopleResult> LinkshellMembersAsync(int slot);

        /// <summary>The members of a linkshell by its id (a linkshell item's extra data).</summary>
        Task<StockUiPeopleResult> LinkshellMembersByIdAsync(uint groupId);

        /// <summary>The linkshell items the character holds (inventory and wardrobes), worn ones first.</summary>
        IReadOnlyList<StockUiLinkshellItem> LinkshellItems();

        /// <summary>
        /// Unequips a worn linkshell item or wears one in <paramref name="slot"/> (C2S 0x0C4), or makes a linkpearl from it
        /// (C2S 0x0C3).
        /// </summary>
        Task<string?> LinkshellAsync(StockUiLinkshellItem item, StockUiSearchAction action, int slot = 0);

        /// <summary>The zone the character is in, for the searches of this area.</summary>
        ushort CurrentZone { get; }

        /// <summary>The character's main job level (Search by Level starts there).</summary>
        byte MainLevel { get; }

        /// <summary>The character's name (a linkshell member list's own row has only Tell).</summary>
        string CharacterName { get; }

        /// <summary>Runs a command; returns the notice to post (null for none).</summary>
        Task<string?> RunAsync(StockUiSocialCommand command, StockUiPerson person = default, int argument = 0);

        /// <summary>Sets the search comment (C2S 0x0E0) with its comment type.</summary>
        Task<string?> SetSearchCommentAsync(string text, uint commentType);

        /// <summary>
        /// Opens the sub-target cursor on the party window's members (yourself too when <paramref name="includeSelf"/>) and runs
        /// <paramref name="command"/> on the one picked; returns a notice when it cannot start (nobody to pick).
        /// </summary>
        string? PickPartyMember(string title, StockUiSocialCommand command, bool includeSelf);

        /// <summary>A player's search comment from the search server; null when they have none or it does not answer.</summary>
        Task<string?> SearchCommentAsync(StockUiPerson person);
    }

    /// <summary>
    /// The people-list windows of the English menu DAT (39542, ROM/119/51; the 2026-10-31 dump of every menu with its title
    /// ROM/165/76 and help ROM/165/75): <c>scresult</c> (16,48, 366 x 248, fifteen 356 x 16 rows from y 4, no title: the
    /// search results), <c>levelsyn</c> (the same frame, title 149 "Level Sync", help 674), <c>friend</c> and <c>msglist</c>
    /// (the same frame), <c>link12</c> (16,106, ten rows: a linkshell's members), and <c>scoption</c> (384,48, 112 x 90: Tell 1, Request 4,
    /// Cancel Request 5, Send Message 2, Invite 3), the options for a person in a list. The rows are client text, drawn as
    /// retail's (round 1 image-26, image-28, image-44; the App's <c>StockUiPeopleWindow</c>).
    /// </summary>
    public static class StockUiPeople
    {
        public const string ResultsMenu = "scresult", LevelSyncMenu = "levelsyn", OptionsMenu = "scoption";

        /// <summary>The first row of a search's list (round 1 image-26 and image-44).</summary>
        public const string ResultsHeader = "Search Results";
        public const int TellButton = 1, MessageButton = 2, InviteButton = 3, RequestButton = 4, CancelRequestButton = 5;

        /// <summary>The help bar's title over a people list.</summary>
        public static string TitleOf(StockUiPeoplePurpose purpose) => purpose switch
        {
            StockUiPeoplePurpose.MemberList => "Party",
            StockUiPeoplePurpose.LevelSync => "Level Sync",
            StockUiPeoplePurpose.JoinRequests => "View Request",
            StockUiPeoplePurpose.SearchResults => "Search",
            StockUiPeoplePurpose.LinkshellMembers => "Linkshell",
            StockUiPeoplePurpose.Friends => "Friend List",
            StockUiPeoplePurpose.LinkshellItems => "Linkshell",
            StockUiPeoplePurpose.Messages => "Messages",
            StockUiPeoplePurpose.Emotes => "Emote List",
            _ => string.Empty,
        };

        /// <summary>The notice when a list has nobody in it.</summary>
        public static string EmptyNotice(StockUiPeoplePurpose purpose) => purpose switch
        {
            StockUiPeoplePurpose.LevelSync => "There are no party members.",
            StockUiPeoplePurpose.JoinRequests => "No players are currently requesting to join.",
            StockUiPeoplePurpose.Friends => StockUiSearch.NoFriendsText,
            StockUiPeoplePurpose.Messages => "There are no messages.",
            StockUiPeoplePurpose.LinkshellMembers => "No linkshell members are online.",
            _ => "No players found.",
        };

        /// <summary>
        /// A search entry's row as retail draws it (round 1 image-26, image-28, image-44): the jobs and race at the left
        /// ("[MNK99/RDM49]Elv"), the mission rank on the nation's colour, the name, and the area at the right unless it is
        /// <paramref name="currentZone"/>; a player seeking a party also shows their languages.
        /// </summary>
        public static StockUiPerson FromSearch(SearchEntity entity, Func<byte, string?>? jobName, Func<int, string?>? zoneName, ushort currentZone = 0,
            uint linkshellId = 0)
        {
            byte rank = LinkshellRankOf(entity, linkshellId);
            bool anonymous = entity.IsAnonymous || entity.MainJob == 0;
            return new StockUiPerson(entity.Name, CharacterId: entity.CharacterId, LinkshellRank: rank)
            {
                Jobs = anonymous ? null : JobsColumn(entity.MainJob, entity.MainLevel, entity.SubJob, entity.SubLevel, entity.Race, jobName),
                MissionRank = anonymous ? (byte)0 : entity.Rank,
                Nation = entity.Nation,
                Area = entity.ZoneId == currentZone && currentZone != 0 ? null : zoneName?.Invoke(entity.ZoneId),
                Languages = (entity.Flags & SearchFlags.SeekingParty) != 0 ? LanguageLetters(entity.Languages) : null,
                HasComment = (entity.Flags & SearchFlags.HasComment) != 0,
                CommentType = (entity.Flags & SearchFlags.HasComment) != 0 ? entity.CommentType : 0,
                CommentHeader = CommentHeaderOf(entity.Name, anonymous, entity.MainJob, entity.MainLevel, entity.SubJob, entity.SubLevel),
            };
        }

        /// <summary>
        /// "Gordian (Lv99 Monk / Lv49 Red Mage):", the line above a search comment in the log (round 2 image-54); the name
        /// alone for an anonymous player (PROVISIONAL: not captured).
        /// </summary>
        public static string CommentHeaderOf(string name, bool anonymous, byte mainJob, byte mainLevel, byte subJob, byte subLevel)
        {
            if (anonymous) return name + ":";
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string jobs = "Lv" + mainLevel.ToString(inv) + " " + StockUiCheck.JobName(mainJob);
            if (subJob != 0 && subLevel != 0) jobs += " / Lv" + subLevel.ToString(inv) + " " + StockUiCheck.JobName(subJob);
            return name + " (" + jobs + "):";
        }

        /// <summary>
        /// A linkshell list member's rank in the shell <paramref name="linkshellId"/>: the rank beside whichever of their two
        /// worn shells has that id (round 2: a holder wearing their shell in slot 2 and another's pearl in slot 1 showed as a
        /// pearl). Without an id, the first worn shell's. A rank of 0 beside the listed shell is the holder's: LandSandBoat
        /// stores the worn item's type as the rank (<c>CLinkshell::AddMember</c> with <c>GetLSType</c>), and type 0 is its
        /// <c>LSTYPE_NEW_LINKSHELL</c>, a linkshell. The holder's own byte is 1 (LandSandBoat's search linkshell list: 1
        /// holder, 2 sack, 3 pearl; round 4's log: "Gemini: ls1 0x1 rank 1"); <c>StockUiSocialActions</c> logs the bytes.
        /// </summary>
        public static byte LinkshellRankOf(SearchEntity entity, uint linkshellId)
        {
            if (linkshellId != 0 && entity.LinkshellId1 == linkshellId) return entity.LinkshellRank1 == 0 ? (byte)1 : entity.LinkshellRank1;
            if (linkshellId != 0 && entity.LinkshellId2 == linkshellId) return entity.LinkshellRank2 == 0 ? (byte)1 : entity.LinkshellRank2;
            return entity.LinkshellRank1 != 0 ? entity.LinkshellRank1 : entity.LinkshellRank2;
        }

        /// <summary>The shell a linkshell list is of: the id every member wears (the one most of them share); 0 for none.</summary>
        public static uint SharedLinkshellId(IReadOnlyList<SearchEntity> players)
        {
            var counts = new Dictionary<uint, int>();
            foreach (var p in players)
            {
                if (p.LinkshellId1 != 0) counts[p.LinkshellId1] = counts.GetValueOrDefault(p.LinkshellId1) + 1;
                if (p.LinkshellId2 != 0 && p.LinkshellId2 != p.LinkshellId1) counts[p.LinkshellId2] = counts.GetValueOrDefault(p.LinkshellId2) + 1;
            }
            uint best = 0;
            int most = 0;
            foreach (var (id, n) in counts)
            {
                if (n > most) (best, most) = (id, n);
            }
            return best;
        }

        /// <summary>"[MNK99/RDM49]Elv": the jobs in brackets (no sub job when it has no level), then the race's first three letters.</summary>
        public static string JobsColumn(byte mainJob, byte mainLevel, byte subJob, byte subLevel, byte race, Func<byte, string?>? jobName)
        {
            string main = (jobName?.Invoke(mainJob) ?? StockUiCheck.JobAbbreviation(mainJob)) + mainLevel.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string jobs = subJob != 0 && subLevel != 0
                ? main + "/" + (jobName?.Invoke(subJob) ?? StockUiCheck.JobAbbreviation(subJob)) + subLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : main;
            return "[" + jobs + "]" + RaceAbbreviation(race);
        }

        /// <summary>
        /// The race's three letters (round 1 image-28: "Tar", "Elv"); the search server's race is the character's model race
        /// (LandSandBoat 1-2 Hume, 3-4 Elvaan, 5-6 Tarutaru, 7 Mithra, 8 Galka).
        /// </summary>
        public static string RaceAbbreviation(byte race) => race switch
        {
            1 or 2 => "Hum",
            3 or 4 => "Elv",
            5 or 6 => "Tar",
            7 => "Mit",
            8 => "Gal",
            _ => string.Empty,
        };

        /// <summary>
        /// A seeker's languages as letters, "E F" (round 1 image-26). The bits are C2S 0x0DB's (<see cref="StockUiParty.LanguageBits"/>).
        /// <b>Provisional:</b> the letters and their order beyond "E F" are not captured.
        /// </summary>
        public static string? LanguageLetters(ushort languages)
        {
            var letters = new List<string>(5);
            if ((languages & 0x01) != 0) letters.Add("J");
            if ((languages & 0x02) != 0) letters.Add("E");
            if ((languages & 0x08) != 0) letters.Add("F");
            if ((languages & 0x04) != 0) letters.Add("G");
            if ((languages & 0x10) != 0) letters.Add("O");
            return letters.Count > 0 ? string.Join(' ', letters) : null;
        }
    }
}
