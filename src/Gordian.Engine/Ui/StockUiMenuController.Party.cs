// src/Gordian.Engine/Ui/StockUiMenuController.Party.cs
using System;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.Search;

namespace Gordian.Engine.Ui
{
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
        public const string DissolveAlliancePrompt = "Dissolve alliance?", RemoveLevelSyncPrompt = "Remove level sync?";

        private StockUiPartyContext CurrentParty => PartyContext?.Invoke() ?? default;

        private StockUiSocialState SocialState => Social?.State ?? default;

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
            menu.SetMarks(StockUiParty.MarkedButtons(menu.Name, SocialState, _settings.GetValue(StockUiSettingKey.PartyInviteAlarm)));
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
                GordianLog.Warning("UI", $"Party disband failed: {ex.Message}");
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
            var state = social.State;
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
                case StockUiPartyAction.SeekParty:
                    RunSocialAndClose(StockUiSocialCommand.SetSeekParty, default, state.SeekingParty ? 0 : 1);
                    break;
                case StockUiPartyAction.Autogroup:
                    RunSocialAndClose(StockUiSocialCommand.SetAutogroup, default, state.Autogroup ? 0 : 1);
                    break;
                case StockUiPartyAction.Request:
                    RunSocialAndClose(StockUiSocialCommand.SetRecruit, default, state.Recruiting ? 0 : 1);
                    break;
                case StockUiPartyAction.ViewRequest:
                    lock (_sync) OpenPeopleList(StockUiPeoplePurpose.JoinRequests, social.JoinRequests(), from);
                    break;
                case StockUiPartyAction.ChangeLeader:
                    lock (_sync) OpenPeopleList(StockUiPeoplePurpose.ChangeLeader, social.PartyMembers(false), from);
                    break;
                case StockUiPartyAction.RemoveMember:
                    lock (_sync) OpenPeopleList(StockUiPeoplePurpose.RemoveMember, social.PartyMembers(false), from);
                    break;
                case StockUiPartyAction.Focus:
                    lock (_sync) OpenPeopleList(StockUiPeoplePurpose.Focus, social.PartyMembers(false), from);
                    break;
                case StockUiPartyAction.LevelSync:
                    if (state.LevelSynced) _ = ConfirmSocialAsync(RemoveLevelSyncPrompt, StockUiSocialCommand.DisableLevelSync, default);
                    else lock (_sync) OpenPeopleList(StockUiPeoplePurpose.LevelSync, social.PartyMembers(false), from, StockUiPeople.LevelSyncMenu);
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
                    lock (_sync) OpenPeopleList(StockUiPeoplePurpose.Quartermaster, social.PartyMembers(true), from);
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

        /// <summary>Runs a social command, closing the menus first (the flags change the Party list's variant).</summary>
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
                GordianLog.Warning("UI", $"{command} failed: {ex.Message}");
                NoticePosted?.Invoke($"That could not be done: {ex.Message}");
            }
        }
    }
}
