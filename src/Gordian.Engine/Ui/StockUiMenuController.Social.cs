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

        /// <summary>
        /// Rows before the first person: 1 for a search's list, whose first row is the "Search Results [N]" bar (round 1
        /// image-26 and image-44; Confirm on it goes back to the menu beneath), else 0.
        /// </summary>
        public int PeopleOffset { get; init; }

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
                int index = EntryIndex(SelectedButtonId) - PeopleOffset;
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
            // The friend list and the messages open empty, as retail's do ("No friends registered.", image-39; the message
            // list's column bar, image-40); the others post why there is nothing to list.
            bool opensEmpty = purpose is StockUiPeoplePurpose.Friends or StockUiPeoplePurpose.Messages;
            if (people.Count == 0 && !opensEmpty)
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
            var rows = new List<StockUiListRow>(people.Count + 1);
            int offset = purpose == StockUiPeoplePurpose.SearchResults ? 1 : 0;
            if (offset > 0) rows.Add(new StockUiListRow(1, StockUiPeople.ResultsHeader, false, Value: "[" + people.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]"));
            foreach (var person in people) rows.Add(new StockUiListRow(rows.Count + 1, person.Name, false, Value: person.Detail));
            var menu = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null, VisibleRowsOf(definition), StockUiListKind.People)
            {
                People = people,
                Rows = rows,
                PeoplePurpose = purpose,
                PeopleOffset = offset,
                HelpTitle = StockUiPeople.TitleOf(purpose),
            };
            // The friend list's "No friends registered." (image-39), in the window while it has nobody.
            if (purpose == StockUiPeoplePurpose.Friends && people.Count == 0) menu.EmptyText = StockUiSearch.NoFriendsText;
            menu.SelectedButtonId = 1;
            PushOpen(menu);
            Changed?.Invoke();
            return menu;
        }

        /// <summary>Confirm on a people list's row or on a person's option (<c>scoption</c>); false for other menus.</summary>
        private bool TryActivateSocial(StockUiOpenMenu top, UiMenuButton button)
        {
            if (top.ListKind != StockUiListKind.People) return false;
            if (top.PeoplePurpose == StockUiPeoplePurpose.LinkshellItems)
            {
                lock (_sync) OpenLinkshellOptions(top);
                return true;
            }
            if (top.PeopleOffset > 0 && top.EntryIndex(top.SelectedButtonId) < top.PeopleOffset)
            {
                // The "Search Results" bar: back to the menu the search came from.
                CloseMenu(top);
                return true;
            }
            if (top.SelectedPerson is not { } person) return true;
            switch (top.PeoplePurpose)
            {
                case StockUiPeoplePurpose.MemberList or StockUiPeoplePurpose.SearchResults or StockUiPeoplePurpose.Friends:
                    // The options read the person from the list beneath them. A player with a search comment has it printed
                    // in the log (round 1: Find Member shows the seeker's comment).
                    lock (_sync) Push(StockUiPeople.OptionsMenu, top, Array.Empty<string>(), null, null);
                    if (person.HasComment) _ = PrintCommentAsync(person);
                    break;
                case StockUiPeoplePurpose.LinkshellMembers:
                    // The options your rank and theirs allow (image-33 to image-37); without a worn shell, Tell only.
                    byte yours = top.LinkshellItem is { WornSlot: > 0 } shell ? shell.Rank : (byte)3;
                    bool yourself = string.Equals(person.Name, Social?.CharacterName, StringComparison.OrdinalIgnoreCase);
                    lock (_sync) Push(StockUiSearch.MemberMenuOf(yours, person.LinkshellRank, yourself), top, Array.Empty<string>(), null, null);
                    break;
                case StockUiPeoplePurpose.Emotes:
                    RunSocialAndClose(StockUiSocialCommand.Emote, person);
                    break;
                case StockUiPeoplePurpose.LevelSync:
                    RunSocialAndClose(StockUiSocialCommand.LevelSync, person);
                    break;
                case StockUiPeoplePurpose.JoinRequests:
                    _ = ConfirmSocialAsync($"Invite {person.Name}?", StockUiSocialCommand.Invite, person);
                    break;
            }
            return true;
        }

        /// <summary>Prints a player's search comment in the log ("Name: comment"), once the search server answers.</summary>
        private async Task PrintCommentAsync(StockUiPerson person)
        {
            if (Social is not { } social) return;
            try
            {
                if (await social.SearchCommentAsync(person).ConfigureAwait(false) is { Length: > 0 } comment) NoticePosted?.Invoke($"{person.Name}: {comment}");
            }
            catch (Exception ex)
            {
                GordianLog.Warning("UI", $"Search comment of {person.Name} failed: {ex.Message}");
            }
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
