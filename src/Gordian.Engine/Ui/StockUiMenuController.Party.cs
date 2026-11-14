// src/Gordian.Engine/Ui/StockUiMenuController.Party.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.Search;
using Gordian.Core.Resources.Ui;

namespace Gordian.Engine.Ui
{
    public sealed partial class StockUiOpenMenu
    {
        internal static readonly IReadOnlyDictionary<int, UiShapeReference> NoLabelImages = new Dictionary<int, UiShapeReference>();

        /// <summary>
        /// Labels drawn instead of a button's own, keyed by ButtonId: the Party lists' "Play Solo", "Stop Request" and
        /// "Lvl. Sync Off" while those flags are on (<see cref="StockUiParty.LabelImages"/>).
        /// </summary>
        public IReadOnlyDictionary<int, UiShapeReference> LabelImages { get; internal set; } = NoLabelImages;
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>The character's party state for the main menu's Party entry (<see cref="StockUiParty"/>); unset = not in a party.</summary>
        public Func<StockUiPartyContext>? PartyContext { get; set; }

        /// <summary>
        /// Runs the Party menu's Disband once confirmed: <c>true</c> when the character leads the party (breaks it up,
        /// C2S 0x070), <c>false</c> for a member (leaves it, C2S 0x06F).
        /// </summary>
        public Func<bool, Task>? PartyDisbandRequested { get; set; }

        /// <summary>The session's party, search server and social commands for the Party, Search, Linkshell and Communication windows (#489, #490).</summary>
        public IStockUiSocial? Social { get; set; }

        /// <summary>The prompts before Disband. <b>Provisional:</b> whether retail asks, and its wording, is not captured.</summary>
        public const string DisbandPartyPrompt = "Disband party?", LeavePartyPrompt = "Leave party?";

        /// <summary>The prompt before Alliance &gt; Dissolve. <b>Provisional:</b> retail's wording is not captured.</summary>
        public const string DissolveAlliancePrompt = "Dissolve alliance?";

        private StockUiPartyContext CurrentParty => PartyContext?.Invoke() ?? default;

        // The flags just toggled, until the server's S2C 0x0B4 copy of the config agrees: the label flips at once.
        private bool? _seekOverride, _autogroupOverride, _recruitOverride;

        private StockUiSocialState SocialState
        {
            get
            {
                var state = Social?.State ?? default;
                return state with
                {
                    SeekingParty = Settle(ref _seekOverride, state.SeekingParty),
                    Autogroup = Settle(ref _autogroupOverride, state.Autogroup),
                    Recruiting = Settle(ref _recruitOverride, state.Recruiting),
                };
            }
        }

        /// <summary>The flag as last toggled, or the server's once it agrees (which drops the override).</summary>
        private static bool Settle(ref bool? pending, bool server)
        {
            if (pending is not { } wanted) return server;
            if (wanted == server) pending = null;
            return wanted;
        }

        /// <summary>Opens the Party list for the character's state beside the main menu. Call under the lock.</summary>
        private void OpenPartyMenu(StockUiOpenMenu from)
        {
            Push(StockUiParty.MenuFor(CurrentParty), from, Array.Empty<string>(), null, null);
        }

        /// <summary>
        /// Greys the entries a leader cannot use yet (<see cref="StockUiParty.GreyedButtons"/>) and marks the languages, alarm
        /// and distribution in effect (<see cref="StockUiParty.MarkedButtons"/>).
        /// </summary>
        private void RefreshPartyMenu(StockUiOpenMenu menu)
        {
            menu.SetGreyed(StockUiParty.GreyedButtons(menu.Name, CurrentParty));
            var state = SocialState;
            menu.SetMarks(StockUiParty.MarkedButtons(menu.Name, state, _settings.GetValue(StockUiSettingKey.PartyInviteAlarm)));
            menu.LabelImages = (IReadOnlyDictionary<int, UiShapeReference>?)StockUiParty.LabelImages(menu.Name, state) ?? StockUiOpenMenu.NoLabelImages;
        }

        private async Task ConfirmPartyDisbandAsync(bool leader)
        {
            if (!await PromptYesNoAsync(leader ? DisbandPartyPrompt : LeavePartyPrompt, defaultYes: false).ConfigureAwait(false)) return;
            CloseAll();
            try
            {
                if (PartyDisbandRequested != null) await PartyDisbandRequested(leader).ConfigureAwait(false);
                else NoticePosted?.Invoke("Disband is not available in this session.");
            }
            catch (Exception ex)
            {
                GordianLog.Warn(LogCategories.Ui, $"Party disband failed: {ex.Message}");
                NoticePosted?.Invoke($"Disband failed: {ex.Message}");
            }
        }

        /// <summary>A Party menu entry (#489): the lists it opens, the flags it toggles, the commands it runs.</summary>
        private void RunPartyAction(StockUiMenuEntry entry, StockUiOpenMenu from)
        {
            var (action, value) = StockUiParty.ActionOf(entry.Argument);
            var social = Social;
            if (social == null)
            {
                NoticePosted?.Invoke($"{entry.Label} is not available in this session.");
                return;
            }
            var state = SocialState;
            switch (action)
            {
                case StockUiPartyAction.MemberList:
                    _ = OpenPeopleAsync(StockUiPeoplePurpose.MemberList, social.MemberListAsync(), from);
                    break;
                case StockUiPartyAction.FindMember:
                    // Players seeking a party in this area, as "/sea party" searches.
                    var query = new SearchQuery { Flags1 = (ushort)SearchFlags.SeekingParty };
                    if (social.CurrentZone != 0) query.Areas.Add(social.CurrentZone);
                    else query.AllAreas = true;
                    _ = OpenPeopleAsync(StockUiPeoplePurpose.SearchResults, social.SearchAsync(query), from);
                    break;
                // The flags toggle in place: the list stays open and the entry's label turns to "Play Solo" / "Stop Request"
                // and back (round 1, 2026-11-01).
                case StockUiPartyAction.SeekParty:
                    _seekOverride = !state.SeekingParty;
                    ToggleFlag(StockUiSocialCommand.SetSeekParty, !state.SeekingParty, from);
                    break;
                case StockUiPartyAction.Autogroup:
                    _autogroupOverride = !state.Autogroup;
                    ToggleFlag(StockUiSocialCommand.SetAutogroup, !state.Autogroup, from);
                    break;
                case StockUiPartyAction.Request:
                    _recruitOverride = !state.Recruiting;
                    ToggleFlag(StockUiSocialCommand.SetRecruit, !state.Recruiting, from);
                    break;
                case StockUiPartyAction.ViewRequest:
                    lock (_sync) OpenPeopleList(StockUiPeoplePurpose.JoinRequests, social.JoinRequests(), from);
                    break;
                // Chg. Leader, Rmv. Member, Focus and the representative pick the member with the sub-target cursor on the
                // party window, and act without asking (round 1, 2026-11-01).
                case StockUiPartyAction.ChangeLeader:
                    PickPartyMember(entry.Label, StockUiSocialCommand.PassLeader, includeSelf: true);
                    break;
                case StockUiPartyAction.RemoveMember:
                    PickPartyMember(entry.Label, StockUiSocialCommand.Kick, includeSelf: true);
                    break;
                case StockUiPartyAction.Focus:
                    PickPartyMember(entry.Label, StockUiSocialCommand.Focus, includeSelf: true);
                    break;
                case StockUiPartyAction.LevelSync:
                    // While a sync is on the entry reads "Lvl. Sync Off" and ends it (round 1 image-27).
                    if (state.LevelSynced) RunSocialAndClose(StockUiSocialCommand.DisableLevelSync, default);
                    else _ = OpenPeopleAsync(StockUiPeoplePurpose.LevelSync, social.MemberListAsync(), from, StockUiPeople.LevelSyncMenu);
                    break;
                case StockUiPartyAction.ShowDistribution:
                    NoticePosted?.Invoke(state.QuartermasterName is { Length: > 0 } name
                        ? $"Treasure distribution: {name} collects all treasure."
                        : "Treasure distribution: lottery.");
                    break;
                case StockUiPartyAction.Lottery:
                    RunSocialAndClose(StockUiSocialCommand.SetLottery, default);
                    break;
                case StockUiPartyAction.Quartermaster:
                    PickPartyMember(entry.Label, StockUiSocialCommand.SetQuartermaster, includeSelf: true);
                    break;
                case StockUiPartyAction.AllianceForm:
                    RunSocialAndClose(StockUiSocialCommand.AllianceInviteTarget, default);
                    break;
                case StockUiPartyAction.AllianceDissolve:
                    _ = ConfirmSocialAsync(DissolveAlliancePrompt, StockUiSocialCommand.AllianceDissolve, default);
                    break;
                case StockUiPartyAction.Language:
                    int languages = state.PartyLanguages ^ value;
                    _ = RunSocialAsync(StockUiSocialCommand.SetPartyLanguages, default, languages);
                    // The server's 0x0B4 echo follows; mark the rows at once.
                    lock (_sync) from.SetMarks(StockUiParty.MarkedButtons(from.Name, state with { PartyLanguages = languages }, 0));
                    Changed?.Invoke();
                    break;
                case StockUiPartyAction.Alarm:
                    _settings.SetValue(StockUiSettingKey.PartyInviteAlarm, value);
                    lock (_sync) Refresh(from);
                    Changed?.Invoke();
                    break;
                default:
                    NoticePosted?.Invoke($"{entry.Label}: this window is not available yet.");
                    break;
            }
        }

        /// <summary>Sends a Party flag and redraws the open list with its label flipped.</summary>
        private void ToggleFlag(StockUiSocialCommand command, bool on, StockUiOpenMenu list)
        {
            _ = RunSocialAsync(command, default, on ? 1 : 0);
            lock (_sync) Refresh(list);
            Changed?.Invoke();
        }

        /// <summary>
        /// The sub-target cursor on the party window's members (<see cref="IStockUiSocial.PickPartyMember"/>); the menus step
        /// aside while it is up and come back after.
        /// </summary>
        private void PickPartyMember(string title, StockUiSocialCommand command, bool includeSelf)
        {
            if (Social?.PickPartyMember(title, command, includeSelf) is { Length: > 0 } notice) NoticePosted?.Invoke(notice);
        }

        /// <summary>Runs a social command, closing the menus first.</summary>
        private void RunSocialAndClose(StockUiSocialCommand command, StockUiPerson person, int argument = 0)
        {
            CloseAll();
            _ = RunSocialAsync(command, person, argument);
        }

        /// <summary>Asks (No under the cursor), then closes the menus and runs the command.</summary>
        private async Task ConfirmSocialAsync(string question, StockUiSocialCommand command, StockUiPerson person, int argument = 0)
        {
            if (!await PromptYesNoAsync(question, defaultYes: false).ConfigureAwait(false)) return;
            CloseAll();
            await RunSocialAsync(command, person, argument).ConfigureAwait(false);
        }

        private async Task RunSocialAsync(StockUiSocialCommand command, StockUiPerson person, int argument = 0)
        {
            var social = Social;
            if (social == null) return;
            try
            {
                if (await social.RunAsync(command, person, argument).ConfigureAwait(false) is { Length: > 0 } notice) NoticePosted?.Invoke(notice);
            }
            catch (Exception ex)
            {
                GordianLog.Warn(LogCategories.Ui, $"{command} failed: {ex.Message}");
                NoticePosted?.Invoke($"That could not be done: {ex.Message}");
            }
        }
    }
}
