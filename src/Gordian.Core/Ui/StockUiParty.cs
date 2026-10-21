// src/Gordian.Core/Ui/StockUiParty.cs
using System;
using System.Collections.Generic;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// What the Party menu needs to know about the character's party: which of the three menus to open and which entries
    /// are greyed.
    /// </summary>
    /// <param name="InParty">The server says the character is in a party, even a party of one.</param>
    /// <param name="IsLeader">The character leads the party.</param>
    /// <param name="OtherMembers">How many members besides the character (Trusts included).</param>
    /// <param name="InAlliance">The party belongs to an alliance.</param>
    public readonly record struct StockUiPartyContext(bool InParty, bool IsLeader, int OtherMembers, bool InAlliance);

    /// <summary>
    /// The main menu's Party entry (#397, #24). The English menu DAT carries three Party lists in the top right corner,
    /// found by dumping every menu and matching the label sprites against the maintainer's retail captures
    /// (<c>Party_Menu_Leader.png</c>, <c>Party_Menu_Member.png</c>, <c>Party_Menu_Ungrouped.png</c>, 2026-10-16): <c>prty1</c>
    /// (not in a party: Seek Party, Find Member, Request, View Request, Autogroup, Languages, Alarm Type), <c>prty2</c> (a
    /// member: Member List, Request, View Request, Rearrange, Focus, Distribution, Disband) and <c>prty3</c> (the leader:
    /// Find Member, Member List, Request, View Request, Distribution, Chg. Leader, Rearrange, Focus, Rmv. Member, Disband,
    /// Alliance, Level Sync). <c>prty6</c> and <c>prty7</c> are other variants (the leader of an alliance, a party flagged as
    /// seeking) whose state is not captured; they are not opened.
    /// </summary>
    public static class StockUiParty
    {
        public const string NotInPartyMenu = "prty1", MemberMenu = "prty2", LeaderMenu = "prty3";

        // Button ids of the DAT's buttons (their order down the list is not their id order).
        public const int SeekPartyButton = 1, UngroupedFindMemberButton = 2, UngroupedRequestButton = 5, UngroupedViewRequestButton = 6,
            AutogroupButton = 3, LanguagesButton = 4, AlarmTypeButton = 7;
        public const int MemberListButton = 1, MemberRequestButton = 6, MemberViewRequestButton = 7, MemberRearrangeButton = 4,
            MemberFocusButton = 5, MemberDistributionButton = 2, MemberDisbandButton = 3;
        public const int LeaderFindMemberButton = 1, LeaderMemberListButton = 2, LeaderRequestButton = 11, LeaderViewRequestButton = 12,
            LeaderDistributionButton = 3, ChangeLeaderButton = 4, LeaderRearrangeButton = 9, LeaderFocusButton = 10,
            RemoveMemberButton = 5, LeaderDisbandButton = 6, AllianceButton = 7, LevelSyncButton = 8;

        /// <summary>The Party menu that fits the character's state.</summary>
        public static string MenuFor(StockUiPartyContext party) =>
            !party.InParty ? NotInPartyMenu : party.IsLeader ? LeaderMenu : MemberMenu;

        /// <summary>
        /// The help bar's title for a Party menu (null for any other menu): "Party" for the ungrouped and member lists and
        /// "Party Leader" for the leader's, as the captures' help bar reads (frame title ids 19 / 94 / 95, ROM/165/76).
        /// </summary>
        public static string? TitleOf(string menuName) =>
            menuName.Equals(LeaderMenu, StringComparison.OrdinalIgnoreCase) ? "Party Leader"
            : menuName.Equals(NotInPartyMenu, StringComparison.OrdinalIgnoreCase) || menuName.Equals(MemberMenu, StringComparison.OrdinalIgnoreCase) ? "Party"
            : null;

        /// <summary>
        /// The entries drawn greyed (their kind-4 alternate images). Retail's capture greys only Alliance of a leader's menu
        /// and does not say why. <b>Provisional:</b> Alliance is greyed while the party has no other member or already
        /// belongs to an alliance, and Chg. Leader / Rmv. Member while there is nobody else to pass leadership to or remove.
        /// </summary>
        public static HashSet<int>? GreyedButtons(string menuName, StockUiPartyContext party)
        {
            if (!menuName.Equals(LeaderMenu, StringComparison.OrdinalIgnoreCase)) return null;
            var greyed = new HashSet<int>();
            if (party.OtherMembers == 0)
            {
                greyed.Add(ChangeLeaderButton);
                greyed.Add(RemoveMemberButton);
            }
            if (party.OtherMembers == 0 || party.InAlliance) greyed.Add(AllianceButton);
            return greyed.Count > 0 ? greyed : null;
        }
    }
}
