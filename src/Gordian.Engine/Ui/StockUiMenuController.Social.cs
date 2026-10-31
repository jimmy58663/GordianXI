// src/Gordian.Engine/Ui/StockUiMenuController.Social.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.Resources.Ui;

namespace Gordian.Engine.Ui
{
    public sealed partial class StockUiOpenMenu
    {
        /// <summary>A people list's players, parallel to <see cref="Rows"/> (<see cref="StockUiPeople"/>).</summary>
        public IReadOnlyList<StockUiPerson> People { get; internal set; } = Array.Empty<StockUiPerson>();

        /// <summary>What a people list is for; <see cref="StockUiPeoplePurpose.None"/> for other menus.</summary>
        public StockUiPeoplePurpose PeoplePurpose { get; init; }

        /// <summary>
        /// The help bar's title over this window when the DAT frame gives none (a people list, the options for a person);
        /// null for the rest.
        /// </summary>
        public string? HelpTitle { get; init; }

        /// <summary>The person under the cursor of a people list; null elsewhere or past the end.</summary>
        public StockUiPerson? SelectedPerson
        {
            get
            {
                int index = EntryIndex(SelectedButtonId);
                return ListKind == StockUiListKind.People && index >= 0 && index < People.Count ? People[index] : null;
            }
        }
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>Opens a people list once its players arrive (from the search server), or posts why there are none.</summary>
        private async Task OpenPeopleAsync(StockUiPeoplePurpose purpose, Task<StockUiPeopleResult> fetch, StockUiOpenMenu parent,
            string menuName = StockUiPeople.ResultsMenu)
        {
            StockUiPeopleResult result;
            try
            {
                result = await fetch.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                GordianLog.Warning("UI", $"{StockUiPeople.TitleOf(purpose)} failed: {ex.Message}");
                NoticePosted?.Invoke($"{StockUiPeople.TitleOf(purpose)}: {ex.Message}");
                return;
            }
            if (result.Error is { Length: > 0 } error)
            {
                NoticePosted?.Invoke(error);
                return;
            }
            lock (_sync)
            {
                // The menus may have closed while the search server answered.
                if (Array.IndexOf(_open, parent) < 0) return;
                OpenPeopleList(purpose, result.People, parent, menuName);
            }
        }

        /// <summary>
        /// Pushes a people list (<c>scresult</c>, or another window of its frame) with a row per player: the name at the left,
        /// the jobs and area at the right. A list with nobody in it posts a notice instead. Call under the lock.
        /// </summary>
        private StockUiOpenMenu? OpenPeopleList(StockUiPeoplePurpose purpose, IReadOnlyList<StockUiPerson> people, StockUiOpenMenu parent,
            string menuName = StockUiPeople.ResultsMenu)
        {
            if (people.Count == 0)
            {
                NoticePosted?.Invoke(StockUiPeople.EmptyNotice(purpose));
                return null;
            }
            var library = _library;
            if (library == null || !library.TryGetMenu(menuName, out var definition))
            {
                NoticePosted?.Invoke($"{StockUiPeople.TitleOf(purpose)} is not available in this session.");
                return null;
            }
            var rows = new List<StockUiListRow>(people.Count);
            foreach (var person in people) rows.Add(new StockUiListRow(rows.Count + 1, person.Name, false, Value: person.Detail));
            var menu = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null, VisibleRowsOf(definition), StockUiListKind.People)
            {
                People = people,
                Rows = rows,
                PeoplePurpose = purpose,
                HelpTitle = StockUiPeople.TitleOf(purpose),
            };
            menu.SelectedButtonId = 1;
            PushOpen(menu);
            Changed?.Invoke();
            return menu;
        }

        /// <summary>Confirm on a people list's row or on a person's option (<c>scoption</c>); false for other menus.</summary>
        private bool TryActivateSocial(StockUiOpenMenu top, UiMenuButton button)
        {
            if (top.ListKind != StockUiListKind.People) return false;
            if (top.SelectedPerson is not { } person) return true;
            switch (top.PeoplePurpose)
            {
                case StockUiPeoplePurpose.MemberList or StockUiPeoplePurpose.SearchResults or StockUiPeoplePurpose.Friends:
                    // The options read the person from the list beneath them.
                    lock (_sync) Push(StockUiPeople.OptionsMenu, top, Array.Empty<string>(), null, null);
                    break;
                case StockUiPeoplePurpose.ChangeLeader:
                    _ = ConfirmSocialAsync($"Pass leadership to {person.Name}?", StockUiSocialCommand.PassLeader, person);
                    break;
                case StockUiPeoplePurpose.RemoveMember:
                    _ = ConfirmSocialAsync($"Remove {person.Name} from the party?", StockUiSocialCommand.Kick, person);
                    break;
                case StockUiPeoplePurpose.LevelSync:
                    _ = ConfirmSocialAsync($"Synchronize levels to {person.Name}?", StockUiSocialCommand.LevelSync, person);
                    break;
                case StockUiPeoplePurpose.JoinRequests:
                    _ = ConfirmSocialAsync($"Invite {person.Name}?", StockUiSocialCommand.Invite, person);
                    break;
                case StockUiPeoplePurpose.Quartermaster:
                    RunSocialAndClose(StockUiSocialCommand.SetQuartermaster, person);
                    break;
                case StockUiPeoplePurpose.Focus:
                    RunSocialAndClose(StockUiSocialCommand.Target, person);
                    break;
            }
            return true;
        }

        /// <summary>An option for the person under a people list's cursor (<c>scoption</c>): Tell, Request, Cancel Request, Invite.</summary>
        private void RunPersonAction(StockUiMenuEntry entry, StockUiOpenMenu from)
        {
            if (from.Parent?.SelectedPerson is not { } person) return;
            switch ((StockUiPartyAction)entry.Argument)
            {
                case StockUiPartyAction.Tell:
                    // Picks the person as the tell partner and opens the input line in tell mode, as the chat-mode list's Tell does.
                    CloseAll();
                    TellTargetSelected?.Invoke(person.Name);
                    ChatModeSelected?.Invoke(ChatInputMode.Tell);
                    break;
                case StockUiPartyAction.Invite:
                    RunSocialAndClose(StockUiSocialCommand.Invite, person);
                    break;
                case StockUiPartyAction.RequestJoin:
                    RunSocialAndClose(StockUiSocialCommand.RequestJoin, person);
                    break;
                case StockUiPartyAction.CancelRequest:
                    RunSocialAndClose(StockUiSocialCommand.CancelRequest, person);
                    break;
            }
        }
    }
}
