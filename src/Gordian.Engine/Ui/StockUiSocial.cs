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
    /// <param name="Detail">Text drawn at the row's right: jobs and levels, then the area ("WAR75/NIN37 Jeuno"); null for none.</param>
    /// <param name="Rank">A linkshell member's rank (1 holder, 2 pearlsack, 3 pearl); 0 elsewhere.</param>
    public readonly record struct StockUiPerson(string Name, uint ServerId = 0, ushort TargetIndex = 0, uint CharacterId = 0,
        string? Detail = null, byte Rank = 0);

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

        /// <summary>Chg. Leader: Confirm asks, then passes leadership (as <c>/pcmd leader</c>).</summary>
        ChangeLeader,

        /// <summary>Rmv. Member: Confirm asks, then removes the member (as <c>/pcmd kick</c>).</summary>
        RemoveMember,

        /// <summary>Level Sync (<c>levelsyn</c>): Confirm asks, then synchronizes the party's levels to the member (C2S 0x077).</summary>
        LevelSync,

        /// <summary>Distribution &gt; the representative: Confirm makes the member collect all treasure (C2S 0x077).</summary>
        Quartermaster,

        /// <summary>Focus: Confirm targets the member.</summary>
        Focus,

        /// <summary>View Request: the players asking to join; Confirm asks, then invites the player.</summary>
        JoinRequests,

        /// <summary>A search's results (Search, Find Member): Confirm opens the person's options (<c>scoption</c>).</summary>
        SearchResults,

        /// <summary>A linkshell's members: Confirm opens the member options (<c>link7</c> ... <c>link10</c>).</summary>
        LinkshellMembers,

        /// <summary>The friend list: Confirm opens the person's options (<c>scoption</c>).</summary>
        Friends,

        /// <summary>The blacklist: Confirm opens <c>blkmain</c> (Delete from blacklist).</summary>
        Blacklist,

        /// <summary>The Linkshell window's items (<c>link5</c>): Confirm opens the item's options.</summary>
        LinkshellItems,
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

        /// <summary>Removes the person from the blacklist (C2S 0x03D delete, as <c>/blacklist delete</c>).</summary>
        BlacklistRemove,
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

        /// <summary>Equips or unequips a linkshell item (C2S 0x0C4), or makes a linkpearl from it (C2S 0x0C3).</summary>
        Task<string?> LinkshellAsync(StockUiLinkshellItem item, StockUiSearchAction action);

        /// <summary>The blacklist (asked for first when the client has not got it whole).</summary>
        Task<StockUiPeopleResult> BlacklistAsync();

        /// <summary>The zone the character is in, for the searches of this area.</summary>
        ushort CurrentZone { get; }

        /// <summary>Runs a command; returns the notice to post (null for none).</summary>
        Task<string?> RunAsync(StockUiSocialCommand command, StockUiPerson person = default, int argument = 0);

        /// <summary>Sets the search comment (C2S 0x0E0) with its comment type.</summary>
        Task<string?> SetSearchCommentAsync(string text, uint commentType);
    }

    /// <summary>
    /// The people-list windows of the English menu DAT (39542, ROM/119/51; the 2026-10-31 dump of every menu with its title
    /// ROM/165/76 and help ROM/165/75): <c>scresult</c> (16,48, 366 x 248, fifteen 356 x 16 rows from y 4, no title: the
    /// search results), <c>levelsyn</c> (the same frame, title 149 "Level Sync", help 674), <c>friend</c> and <c>blklist</c>
    /// (the same frame; <c>blklist</c> title 126 "Blacklist"), and <c>scoption</c> (384,48, 112 x 90: Tell 1, Request 4,
    /// Cancel Request 5, Send Message 2, Invite 3), the options for a person in a list. The rows are client text: the name
    /// at the left in the log font, the jobs and area at the right, as the Map window's lists draw theirs.
    /// </summary>
    public static class StockUiPeople
    {
        public const string ResultsMenu = "scresult", LevelSyncMenu = "levelsyn", OptionsMenu = "scoption";
        public const int TellButton = 1, MessageButton = 2, InviteButton = 3, RequestButton = 4, CancelRequestButton = 5;

        /// <summary>The help bar's title over a people list.</summary>
        public static string TitleOf(StockUiPeoplePurpose purpose) => purpose switch
        {
            StockUiPeoplePurpose.MemberList => "Member List",
            StockUiPeoplePurpose.ChangeLeader => "Chg. Leader",
            StockUiPeoplePurpose.RemoveMember => "Rmv. Member",
            StockUiPeoplePurpose.LevelSync => "Level Sync",
            StockUiPeoplePurpose.Quartermaster => "Distribution",
            StockUiPeoplePurpose.Focus => "Focus",
            StockUiPeoplePurpose.JoinRequests => "View Request",
            StockUiPeoplePurpose.SearchResults => "Search",
            StockUiPeoplePurpose.LinkshellMembers => "Linkshell",
            StockUiPeoplePurpose.Friends => "Friend List",
            StockUiPeoplePurpose.Blacklist => "Blacklist",
            StockUiPeoplePurpose.LinkshellItems => "Linkshell",
            _ => string.Empty,
        };

        /// <summary>The notice when a list has nobody in it.</summary>
        public static string EmptyNotice(StockUiPeoplePurpose purpose) => purpose switch
        {
            StockUiPeoplePurpose.ChangeLeader or StockUiPeoplePurpose.RemoveMember or StockUiPeoplePurpose.LevelSync
                or StockUiPeoplePurpose.Focus => "There are no other party members.",
            StockUiPeoplePurpose.JoinRequests => "No players are currently requesting to join.",
            StockUiPeoplePurpose.Friends => "No one on your friend list is online.",
            StockUiPeoplePurpose.Blacklist => "Your blacklist is empty.",
            StockUiPeoplePurpose.LinkshellMembers => "No linkshell members are online.",
            _ => "No players found.",
        };

        /// <summary>A search entry's row: its name, and its jobs and area at the right ("WAR75/NIN37 Port Jeuno").</summary>
        public static StockUiPerson FromSearch(SearchEntity entity, Func<byte, string?>? jobName, Func<int, string?>? zoneName)
        {
            string detail = Detail(entity.IsAnonymous ? (byte)0 : entity.MainJob, entity.MainLevel, entity.SubJob, entity.SubLevel,
                zoneName?.Invoke(entity.ZoneId), jobName);
            byte rank = entity.LinkshellRank1 != 0 ? entity.LinkshellRank1 : entity.LinkshellRank2;
            return new StockUiPerson(entity.Name, CharacterId: entity.CharacterId, Detail: detail.Length > 0 ? detail : null, Rank: rank);
        }

        /// <summary>"WAR75/NIN37 Area": the jobs (no sub job when it has no level), then the area; empty for none.</summary>
        public static string Detail(byte mainJob, byte mainLevel, byte subJob, byte subLevel, string? area, Func<byte, string?>? jobName)
        {
            string jobs = string.Empty;
            if (mainJob != 0)
            {
                string main = (jobName?.Invoke(mainJob) ?? StockUiCheck.JobAbbreviation(mainJob)) + mainLevel;
                jobs = subJob != 0 && subLevel != 0 ? main + "/" + (jobName?.Invoke(subJob) ?? StockUiCheck.JobAbbreviation(subJob)) + subLevel : main;
            }
            if (string.IsNullOrEmpty(area)) return jobs;
            return jobs.Length > 0 ? jobs + " " + area : area;
        }
    }
}
