// src/Gordian.Engine/Ui/StockUiMenuController.Search.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Network.Search;
using Gordian.Engine.Input;

namespace Gordian.Engine.Ui
{
    public sealed partial class StockUiOpenMenu
    {
        /// <summary>The Linkshell window's items, parallel to <see cref="Rows"/> (<c>link5</c>).</summary>
        public IReadOnlyList<StockUiLinkshellItem> LinkshellItems { get; internal set; } = Array.Empty<StockUiLinkshellItem>();

        /// <summary>The linkshell item a <c>link2</c> / <c>link3</c> / <c>link4</c> list is for.</summary>
        public StockUiLinkshellItem? LinkshellItem { get; internal set; }

        /// <summary>The text field drawn in this window (Search by Name's name, the search comment); null for none.</summary>
        public StockUiTextField? SocialField { get; internal set; }

        /// <summary>The button the <see cref="SocialField"/> is drawn over; 0 to draw it inside the frame's top left.</summary>
        public int SocialFieldButton { get; internal set; }
    }

    public sealed partial class StockUiMenuController
    {
        // Search by comment / Edit comment: which one the comment type list was opened for, and the type picked.
        private bool _commentEdit;
        private int _commentGenre;

        /// <summary>The comment prompt's notice once the server has the comment. <b>Provisional</b> wording.</summary>
        public const string CommentSetNotice = "Search comment set.";

        /// <summary>A Search, Linkshell or Communication entry (#490).</summary>
        private void RunSearchAction(StockUiMenuEntry entry, StockUiOpenMenu from)
        {
            var (action, value) = StockUiSearch.ActionOf(entry.Argument);
            var social = Social;
            if (social == null)
            {
                NoticePosted?.Invoke($"{entry.Label} is not available in this session.");
                return;
            }
            switch (action)
            {
                case StockUiSearchAction.Area:
                    Search(from, AreaQuery(social));
                    break;
                case StockUiSearchAction.Job:
                    Search(from, AreaQuery(social, q => q.Job = (byte)value));
                    break;
                case StockUiSearchAction.Nation:
                    Search(from, AreaQuery(social, q => q.Nation = (byte)value));
                    break;
                case StockUiSearchAction.Race:
                    Search(from, AreaQuery(social, q => q.Race = (byte)value));
                    break;
                case StockUiSearchAction.Level:
                    Search(from, AreaQuery(social, q => q.Level = StockUiSearch.LevelBracket(value)));
                    break;
                case StockUiSearchAction.Rank:
                    Search(from, AreaQuery(social, q => q.Rank = ((byte)value, (byte)value)));
                    break;
                case StockUiSearchAction.Friends:
                    _ = OpenPeopleAsync(StockUiPeoplePurpose.SearchResults, social.SearchAsync(new SearchQuery { AllAreas = true, FriendsOnly = true }), from);
                    break;
                case StockUiSearchAction.LinkshellMembers:
                    _ = OpenPeopleAsync(StockUiPeoplePurpose.LinkshellMembers, social.LinkshellMembersAsync(value), from);
                    break;
                case StockUiSearchAction.NameField:
                    BeginNameField(from);
                    break;
                case StockUiSearchAction.NameBegin:
                    if (MacroField.IsOpen && from.SocialField == MacroField) MacroField.Commit();
                    else SearchByName(from, _searchName);
                    break;
                case StockUiSearchAction.NameCancel:
                    if (MacroField.IsOpen && from.SocialField == MacroField) MacroField.Cancel();
                    CloseMenu(from);
                    break;
                case StockUiSearchAction.CommentSearch or StockUiSearchAction.CommentEdit:
                    _commentEdit = action == StockUiSearchAction.CommentEdit;
                    lock (_sync) Push(StockUiSearch.CommentTypeMenu, from, Array.Empty<string>(), null, null);
                    break;
                case StockUiSearchAction.CommentGenre:
                    _commentGenre = value;
                    if (StockUiSearch.RefineMenuOf(value) is { } refine) lock (_sync) Push(refine, from, Array.Empty<string>(), null, null);
                    else FinishComment(from, StockUiSearch.CommentType(value, 0));
                    break;
                case StockUiSearchAction.CommentRefine:
                    FinishComment(from, StockUiSearch.CommentType(_commentGenre, value));
                    break;
                case StockUiSearchAction.LinkshellItems:
                    lock (_sync) OpenLinkshellItems(social.LinkshellItems(), from);
                    break;
                case StockUiSearchAction.ItemMembers when from.LinkshellItem is { } item:
                    _ = OpenPeopleAsync(StockUiPeoplePurpose.LinkshellMembers, social.LinkshellMembersByIdAsync(item.GroupId), from);
                    break;
                case StockUiSearchAction.ItemEquip or StockUiSearchAction.ItemMakePearl when from.LinkshellItem is { } item:
                    CloseAll();
                    _ = RunLinkshellAsync(item, action);
                    break;
                case StockUiSearchAction.FriendList:
                    _ = OpenPeopleAsync(StockUiPeoplePurpose.Friends, social.SearchAsync(new SearchQuery { AllAreas = true, FriendsOnly = true }), from,
                        StockUiSearch.FriendListMenu);
                    break;
                case StockUiSearchAction.BlacklistDelete when from.Parent?.SelectedPerson is { } person:
                    _ = ConfirmSocialAsync($"Remove {person.Name} from the blacklist?", StockUiSocialCommand.BlacklistRemove, person);
                    break;
            }
        }

        /// <summary>A search of this area (every area when the zone is not known yet), with one more filter.</summary>
        private static SearchQuery AreaQuery(IStockUiSocial social, Action<SearchQuery>? filter = null)
        {
            var query = new SearchQuery();
            if (social.CurrentZone != 0) query.Areas.Add(social.CurrentZone);
            else query.AllAreas = true;
            filter?.Invoke(query);
            return query;
        }

        private void Search(StockUiOpenMenu from, SearchQuery query)
        {
            if (Social is { } social) _ = OpenPeopleAsync(StockUiPeoplePurpose.SearchResults, social.SearchAsync(query), from);
        }

        private string _searchName = string.Empty;

        /// <summary>
        /// Search by Name's field (<c>scname</c> button 1): the shared text field, at most <see cref="StockUiSearch.MaxNameLength"/>
        /// characters; Enter searches every area for the name (or a name prefix), Escape keeps the window.
        /// </summary>
        private void BeginNameField(StockUiOpenMenu menu)
        {
            if (MacroField.IsOpen) MacroField.Commit();
            lock (_sync)
            {
                menu.SocialField = MacroField;
                menu.SocialFieldButton = StockUiSearch.NameFieldButton;
            }
            MacroField.Open(_searchName, StockUiSearch.MaxNameLength, (typed, _) =>
            {
                _searchName = typed.Trim();
                SearchByName(menu, _searchName);
            }, escapeKeepsText: false, verticalMoves: false);
            Changed?.Invoke();
        }

        private void SearchByName(StockUiOpenMenu from, string name)
        {
            if (name.Length == 0)
            {
                NoticePosted?.Invoke("Enter a name to search for.");
                return;
            }
            Search(from, new SearchQuery { AllAreas = true, Name = name });
        }

        /// <summary>The comment type is chosen: Search by comment searches this area for it; Edit opens the comment box.</summary>
        private void FinishComment(StockUiOpenMenu from, uint commentType)
        {
            if (!_commentEdit)
            {
                if (Social is { } social) Search(from, AreaQuery(social, q => q.CommentType = commentType));
                return;
            }
            StockUiOpenMenu? box;
            lock (_sync) box = Push(StockUiSearch.CommentWindow, from, Array.Empty<string>(), null, null);
            if (box == null) return;
            if (MacroField.IsOpen) MacroField.Commit();
            lock (_sync) box.SocialField = MacroField;
            bool sent = false;
            MacroField.Open(string.Empty, StockUiSearch.MaxCommentLength, (typed, step) =>
            {
                sent = true;
                CloseAll();
                _ = SetCommentAsync(typed.Trim(), commentType);
            }, escapeKeepsText: false, verticalMoves: false);
            MacroField.OpenChanged += OnClosed;
            void OnClosed(bool open)
            {
                if (open) return;
                MacroField.OpenChanged -= OnClosed;
                if (!sent) CloseMenu(box); // Escape: back to the comment type
            }
            Changed?.Invoke();
        }

        private async Task SetCommentAsync(string text, uint commentType)
        {
            if (Social is not { } social) return;
            try
            {
                NoticePosted?.Invoke(await social.SetSearchCommentAsync(text, commentType).ConfigureAwait(false) ?? CommentSetNotice);
            }
            catch (Exception ex)
            {
                NoticePosted?.Invoke($"The search comment could not be set: {ex.Message}");
            }
        }

        /// <summary>The Linkshell window (<c>link5</c>): one row per linkshell item, its worn slot at the right. Call under the lock.</summary>
        private void OpenLinkshellItems(IReadOnlyList<StockUiLinkshellItem> items, StockUiOpenMenu from)
        {
            if (items.Count == 0)
            {
                NoticePosted?.Invoke("You do not have a linkshell.");
                return;
            }
            var people = new List<StockUiPerson>(items.Count);
            foreach (var item in items) people.Add(new StockUiPerson(item.Name, Detail: item.WornSlot != 0 ? (item.WornSlot == 1 ? "Linkshell" : "Linkshell 2") : null));
            if (OpenPeopleList(StockUiPeoplePurpose.LinkshellItems, people, from, StockUiSearch.LinkshellItemMenu) is { } list) list.LinkshellItems = items;
        }

        /// <summary>Confirm on a Linkshell row: the item's options (<c>link2</c>, <c>link3</c> or <c>link4</c>). Call under the lock.</summary>
        private void OpenLinkshellOptions(StockUiOpenMenu list)
        {
            int index = list.EntryIndex(list.SelectedButtonId);
            if (index < 0 || index >= list.LinkshellItems.Count) return;
            var item = list.LinkshellItems[index];
            if (Push(StockUiSearch.MenuOf(item.Kind), list, Array.Empty<string>(), null, null) is not { } options) return;
            options.LinkshellItem = item;
            // Create Linkpearl needs the shell worn (its greyed alternate otherwise).
            if (item.WornSlot == 0 && item.Kind != StockUiLinkshellKind.Linkpearl) options.SetGreyed(new HashSet<int> { 3 });
        }

        private async Task RunLinkshellAsync(StockUiLinkshellItem item, StockUiSearchAction action)
        {
            if (Social is not { } social) return;
            try
            {
                if (await social.LinkshellAsync(item, action).ConfigureAwait(false) is { Length: > 0 } notice) NoticePosted?.Invoke(notice);
            }
            catch (Exception ex)
            {
                NoticePosted?.Invoke($"Linkshell: {ex.Message}");
            }
        }

        /// <summary>
        /// Left / right on the friend list or the blacklist switch to the other (GordianXI's way to the blacklist; PROVISIONAL).
        /// Call under the lock.
        /// </summary>
        private bool TryFlipSocialList(StockUiOpenMenu top)
        {
            if (Social is not { } social || top.PeoplePurpose is not (StockUiPeoplePurpose.Friends or StockUiPeoplePurpose.Blacklist)) return false;
            var parent = top.Parent;
            if (parent == null) return false;
            bool toBlacklist = top.PeoplePurpose == StockUiPeoplePurpose.Friends;
            CloseMenu(top);
            _ = toBlacklist
                ? OpenPeopleAsync(StockUiPeoplePurpose.Blacklist, social.BlacklistAsync(), parent, StockUiSearch.BlacklistMenu)
                : OpenPeopleAsync(StockUiPeoplePurpose.Friends, social.SearchAsync(new SearchQuery { AllAreas = true, FriendsOnly = true }), parent,
                    StockUiSearch.FriendListMenu);
            return true;
        }
    }
}
