// src/Gordian.Engine/Ui/StockUiParty.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Resources.Ui;

namespace Gordian.Engine.Ui
{
    /// <summary>
    /// What the Party menu needs to know about the character's party: which of the three menus to open and which entries
    /// are greyed.
    /// </summary>
    /// <param name="InParty">The server says the character is in a party, even a party of one.</param>
    /// <param name="IsLeader">The character leads the party.</param>
    /// <param name="OtherMembers">How many members besides the character (Trusts included).</param>
    /// <param name="InAlliance">The party belongs to an alliance.</param>
    /// <param name="SeekingParty">The party flag is on (seeking a party).</param>
    /// <param name="IsAllianceLeader">The character leads the alliance.</param>
    public readonly record struct StockUiPartyContext(bool InParty, bool IsLeader, int OtherMembers, bool InAlliance, bool SeekingParty = false,
        bool IsAllianceLeader = false);

    /// <summary>
    /// The main menu's Party entry (#397, #24). The English menu DAT carries three Party lists in the top right corner,
    /// found by dumping every menu and matching the label sprites against the maintainer's retail captures
    /// (<c>Party_Menu_Leader.png</c>, <c>Party_Menu_Member.png</c>, <c>Party_Menu_Ungrouped.png</c>, 2026-10-16): <c>prty1</c>
    /// (not in a party: Seek Party, Find Member, Request, View Request, Autogroup, Languages, Alarm Type), <c>prty2</c> (a
    /// member: Member List, Request, View Request, Rearrange, Focus, Distribution, Disband) and <c>prty3</c> (the leader:
    /// Find Member, Member List, Request, View Request, Distribution, Chg. Leader, Rearrange, Focus, Rmv. Member, Disband,
    /// Alliance, Level Sync). <c>prty6</c> is the ungrouped list with the party flag on (its first entry's help is "Turn off
    /// party flag." where <c>prty1</c>'s is "Toggle party flag.", and it has no Alarm Type); <c>prty7</c> is a leader's list
    /// whose Distribution only shows the setting ("Check treasure distribution setting."), a state not captured, so it is
    /// not opened. The entries' submenus (#489, the 2026-10-31 dump with titles ROM/165/76 and help ROM/165/75): Alliance
    /// opens <c>prty4</c> (title 96: Form 1, Dissolve 2), Languages <c>prty5us</c> (title 97: English 1, French 2, German 3,
    /// Japanese 4, Other 5; <c>prty5</c> is the Japanese order), the leader's Distribution <c>prty8</c> (title 98: Lottery 1,
    /// the representative 2) and Alarm Type <c>alarm</c> (440,48, 56 x 140, title 19: Off 1 and sounds 1-7 as 2-8).
    /// </summary>
    /// <summary>What a Party menu entry (or a person's option) does (#489); the menu entry's Argument with <see cref="StockUiParty.Action"/>.</summary>
    public enum StockUiPartyAction
    {
        None,
        MemberList,
        FindMember,
        SeekParty,
        Autogroup,
        Request,
        ViewRequest,
        ChangeLeader,
        RemoveMember,
        LevelSync,
        Focus,
        ShowDistribution,
        Lottery,
        Quartermaster,
        AllianceForm,
        AllianceDissolve,

        /// <summary>A Languages row: toggles its bit (the value).</summary>
        Language,

        /// <summary>An Alarm Type row: 0 off, 1-7 a sound (the value).</summary>
        Alarm,

        // A person's options (scoption).
        Tell,
        RequestJoin,
        CancelRequest,
        Invite,
    }

    public static class StockUiParty
    {
        /// <summary>Packs a Party entry's action and the value it sets (a language bit, an alarm sound) into a menu entry's Argument.</summary>
        public static int Action(StockUiPartyAction action, int value = 0) => (int)action | (value << 8);

        /// <summary>Unpacks <see cref="Action"/>.</summary>
        public static (StockUiPartyAction Action, int Value) ActionOf(int argument) => ((StockUiPartyAction)(argument & 0xFF), argument >> 8);

        public const string NotInPartyMenu = "prty1", MemberMenu = "prty2", LeaderMenu = "prty3", SeekingMenu = "prty6";
        public const string AllianceMenu = "prty4", LanguagesMenu = "prty5us", DistributionMenu = "prty8", AlarmMenu = "alarm";

        // Button ids of the DAT's buttons (their order down the list is not their id order).
        public const int SeekPartyButton = 1, UngroupedFindMemberButton = 2, UngroupedRequestButton = 5, UngroupedViewRequestButton = 6,
            AutogroupButton = 3, LanguagesButton = 4, AlarmTypeButton = 7;
        public const int MemberListButton = 1, MemberRequestButton = 6, MemberViewRequestButton = 7, MemberRearrangeButton = 4,
            MemberFocusButton = 5, MemberDistributionButton = 2, MemberDisbandButton = 3;
        public const int LeaderFindMemberButton = 1, LeaderMemberListButton = 2, LeaderRequestButton = 11, LeaderViewRequestButton = 12,
            LeaderDistributionButton = 3, ChangeLeaderButton = 4, LeaderRearrangeButton = 9, LeaderFocusButton = 10,
            RemoveMemberButton = 5, LeaderDisbandButton = 6, AllianceButton = 7, LevelSyncButton = 8;
        public const int SeekingOffButton = 1, SeekingFindMemberButton = 2, SeekingRequestButton = 5, SeekingViewRequestButton = 6,
            SeekingAutogroupButton = 3, SeekingLanguagesButton = 4;
        public const int AllianceFormButton = 1, AllianceDissolveButton = 2;
        public const int LotteryButton = 1, QuartermasterButton = 2;

        /// <summary>The Alarm Type list's first button (Off); sounds 1-7 are the buttons after it.</summary>
        public const int AlarmOffButton = 1, AlarmSounds = 7;

        /// <summary>
        /// The Languages list's buttons in order and the <c>PartyLanguages</c> bit each sets (C2S 0x0DB kind 1; bits from
        /// LandSandBoat <c>GP_CLI_COMMAND_CONFIG_LANGUAGE_FLAGS</c>, https://github.com/LandSandBoat/server: Japanese 1,
        /// English 2, German 4, French 8, Other 0x10).
        /// </summary>
        public static readonly IReadOnlyList<(int Button, int Bit)> LanguageBits = new[] { (1, 0x02), (2, 0x08), (3, 0x04), (4, 0x01), (5, 0x10) };

        /// <summary>
        /// The Party menu that fits the character's state. Not in a party it is always <c>prty1</c>: retail keeps the list open
        /// when Seek Party is chosen and only that entry's label turns to "Play Solo", with Languages and Alarm Type still there
        /// (the maintainer's round 1 check, 2026-11-01), which <c>prty6</c> (no Alarm Type) does not fit.
        /// </summary>
        public static string MenuFor(StockUiPartyContext party) =>
            !party.InParty ? NotInPartyMenu : party.IsLeader ? LeaderMenu : MemberMenu;

        /// <summary>
        /// The label images ("menu windowps") the Party lists swap in for a flag that is on: "Play Solo" over Seek Party
        /// (#257, <c>prty6</c>'s first button) and over Autogroup (#484, <c>prty6</c>'s Autogroup), "Stop Request" over Request
        /// (#780, beside Request #778 in the DAT) and "Lvl. Sync Off" over Level Sync (#613, beside #611). Found by drawing the
        /// group's images (the 2026-11-01 dump); round 1 image-27 shows "Lvl. Sync Off" in the leader's list during a sync.
        /// <b>Provisional:</b> "Stop Request" is not captured.
        /// </summary>
        public const int PlaySoloImage = 257, AutogroupSoloImage = 484, StopRequestImage = 780, LevelSyncOffImage = 613;

        /// <summary>The label group of the Party lists' buttons.</summary>
        public const string LabelGroup = "menu    windowps";

        /// <summary>The labels a Party list swaps for the flags that are on (<see cref="PlaySoloImage"/> ...); null for none.</summary>
        public static Dictionary<int, UiShapeReference>? LabelImages(string menuName, StockUiSocialState state)
        {
            var labels = new Dictionary<int, UiShapeReference>();
            void Swap(bool on, int button, int image)
            {
                if (on) labels[button] = new UiShapeReference(0, (ushort)image, LabelGroup);
            }
            if (Is(menuName, NotInPartyMenu))
            {
                Swap(state.SeekingParty, SeekPartyButton, PlaySoloImage);
                Swap(state.Autogroup, AutogroupButton, AutogroupSoloImage);
                Swap(state.Recruiting, UngroupedRequestButton, StopRequestImage);
            }
            else if (Is(menuName, MemberMenu))
            {
                Swap(state.Recruiting, MemberRequestButton, StopRequestImage);
            }
            else if (Is(menuName, LeaderMenu))
            {
                Swap(state.Recruiting, LeaderRequestButton, StopRequestImage);
                Swap(state.LevelSynced, LevelSyncButton, LevelSyncOffImage);
            }
            return labels.Count > 0 ? labels : null;
        }

        /// <summary>
        /// The help bar's title for a Party menu (null for any other menu): "Party" for the ungrouped and member lists and
        /// "Party Leader" for the leader's, as the captures' help bar reads (frame title ids 19 / 94 / 95, ROM/165/76).
        /// </summary>
        public static string? TitleOf(string menuName) =>
            Is(menuName, LeaderMenu) ? "Party Leader"
            : Is(menuName, NotInPartyMenu) || Is(menuName, MemberMenu) || Is(menuName, SeekingMenu) || Is(menuName, AlarmMenu) ? "Party"
            : Is(menuName, AllianceMenu) ? "Alliance"
            : Is(menuName, LanguagesMenu) ? "Languages"
            : Is(menuName, DistributionMenu) ? "Distribution"
            : null;

        private static bool Is(string menuName, string name) => menuName.Equals(name, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The entries drawn with the "in effect" marker: the party languages that are set, the Alarm Type in use, and the
        /// distribution in effect (Lottery, or the representative while a member collects all treasure).
        /// </summary>
        public static HashSet<int>? MarkedButtons(string menuName, StockUiSocialState state, int alarmType)
        {
            if (Is(menuName, LanguagesMenu))
            {
                var marked = new HashSet<int>();
                foreach (var (button, bit) in LanguageBits)
                {
                    if ((state.PartyLanguages & bit) != 0) marked.Add(button);
                }
                return marked;
            }
            if (Is(menuName, AlarmMenu)) return new HashSet<int> { AlarmOffButton + Math.Clamp(alarmType, 0, AlarmSounds) };
            if (Is(menuName, DistributionMenu)) return new HashSet<int> { state.QuartermasterName != null ? QuartermasterButton : LotteryButton };
            return null;
        }

        /// <summary>
        /// The entries drawn greyed (their kind-4 alternate images). Retail's capture greys only Alliance of a leader's menu
        /// and does not say why. <b>Provisional:</b> Alliance is greyed while the party has no other member and no alliance
        /// (it opens Form / Dissolve, so an alliance keeps it), and Chg. Leader, Rmv. Member and Level Sync while there is
        /// nobody else in the party.
        /// </summary>
        public static HashSet<int>? GreyedButtons(string menuName, StockUiPartyContext party)
        {
            if (Is(menuName, AllianceMenu))
            {
                // Only the alliance leader dissolves the alliance or brings in another party (LandSandBoat's C2S 0x070 and
                // 0x06E kind 5 check isAllianceLeader; round 1: a party leader's Dissolve took them out of the alliance and
                // their party). PROVISIONAL: when retail uses the greyed alternates is not captured.
                if (!party.InAlliance) return new HashSet<int> { AllianceDissolveButton };
                return party.IsAllianceLeader ? null : new HashSet<int> { AllianceFormButton, AllianceDissolveButton };
            }
            if (!Is(menuName, LeaderMenu)) return null;
            var greyed = new HashSet<int>();
            if (party.OtherMembers == 0)
            {
                greyed.Add(ChangeLeaderButton);
                greyed.Add(RemoveMemberButton);
                greyed.Add(LevelSyncButton);
            }
            // Alliance opens the Form / Dissolve list, so an alliance keeps it open to dissolve.
            if (party.OtherMembers == 0 && !party.InAlliance) greyed.Add(AllianceButton);
            return greyed.Count > 0 ? greyed : null;
        }
    }
}
