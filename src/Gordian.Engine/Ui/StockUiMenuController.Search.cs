// src/Gordian.Engine/Ui/StockUiMenuController.Search.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Network.Packets;
using Gordian.Core.Network.Search;
using Gordian.Core.Resources.Tables;
using Gordian.Core.Resources.Ui;

namespace Gordian.Engine.Ui
{
    public sealed partial class StockUiOpenMenu
    {
        /// <summary>The Linkshell window's items, parallel to <see cref="Rows"/> (<c>link5</c>).</summary>
        public IReadOnlyList<StockUiLinkshellItem> LinkshellItems { get; internal set; } = Array.Empty<StockUiLinkshellItem>();

        /// <summary>The linkshell item an item's options or a member list is for (drawn in <c>link6</c> above them).</summary>
        public StockUiLinkshellItem? LinkshellItem { get; internal set; }

        /// <summary>The text field drawn in this window (Search by Name's name, a level, the search comment); null for none.</summary>
        public StockUiTextField? SocialField { get; internal set; }

        /// <summary>The button the <see cref="SocialField"/> is drawn over; 0 to draw it inside the frame's top left.</summary>
        public int SocialFieldButton { get; internal set; }

        /// <summary>A search list over the chat (Search by Area, a region's zones, the Unities); None for other lists.</summary>
        public StockUiSearchList SearchList { get; init; }

        /// <summary>Search by Level's range (<c>scchar</c>), drawn in its two fields.</summary>
        public byte LevelMin { get; internal set; }
        public byte LevelMax { get; internal set; }

        /// <summary>The comment box's comment type (1-7, <c>comgenre</c>): the dot before the text takes its colour (image-46).</summary>
        public int CommentGenre { get; init; }
    }

    public sealed partial class StockUiMenuController
    {
        // Search by comment / Edit comment: which one the comment type list was opened for, and the type picked.
        private bool _commentEdit;
        private int _commentGenre;

        /// <summary>The comment prompt's notice once the server has the comment. <b>Provisional</b> wording.</summary>
        public const string CommentSetNotice = "Search comment set.";

        /// <summary>The online status picked in <c>olstat</c> (1 Online, 2 Away, 3 Invisible); PlayOnline's, so it is only kept.</summary>
        private int _onlineStatus = 1;

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
                    lock (_sync) OpenAreaList(from);
                    Changed?.Invoke();
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
                case StockUiSearchAction.LevelField when value == 0:
                    OpenLevelWindow(from, social);
                    break;
                case StockUiSearchAction.LevelField:
                    BeginLevelField(from, value);
                    break;
                case StockUiSearchAction.LevelBegin:
                    if (MacroField.IsOpen && from.SocialField == MacroField) MacroField.Commit();
                    byte min = Math.Min(from.LevelMin, from.LevelMax), max = Math.Max(from.LevelMin, from.LevelMax);
                    Search(from, AreaQuery(social, q => q.Level = (min, max)));
                    break;
                case StockUiSearchAction.LevelCancel or StockUiSearchAction.NameCancel:
                    if (MacroField.IsOpen && from.SocialField == MacroField) MacroField.Cancel();
                    CloseMenu(from);
                    break;
                case StockUiSearchAction.Rank:
                    Search(from, AreaQuery(social, q => q.Rank = ((byte)value, (byte)value)));
                    break;
                case StockUiSearchAction.Unity:
                    lock (_sync) OpenUnityList(from);
                    Changed?.Invoke();
                    break;
                case StockUiSearchAction.Friends:
                    _ = OpenPeopleAsync(StockUiPeoplePurpose.SearchResults, social.SearchAsync(new SearchQuery { AllAreas = true, FriendsOnly = true }), from);
                    break;
                case StockUiSearchAction.LinkshellMembers:
                    _ = OpenPeopleAsync(StockUiPeoplePurpose.SearchResults, social.LinkshellMembersAsync(value), from);
                    break;
                case StockUiSearchAction.NameField:
                    BeginNameField(from);
                    break;
                case StockUiSearchAction.NameBegin:
                    if (MacroField.IsOpen && from.SocialField == MacroField) MacroField.Commit();
                    else SearchByName(from, _searchName);
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
                    _ = OpenLinkshellMembersAsync(item, social.LinkshellMembersByIdAsync(item.GroupId), from);
                    break;
                case StockUiSearchAction.ItemEquip or StockUiSearchAction.ItemMakePearl when from.LinkshellItem is { } item:
                    CloseAll();
                    _ = RunLinkshellAsync(item, action, 0);
                    break;
                case StockUiSearchAction.ItemSetSlot when from.LinkshellItem is { } item:
                    CloseAll();
                    _ = RunLinkshellAsync(item, action, value);
                    break;
                case StockUiSearchAction.ItemDrop when from.LinkshellItem is { } item:
                    _ = DropLinkshellItemAsync(item);
                    break;
                case StockUiSearchAction.MemberToSack or StockUiSearchAction.MemberToPearl or StockUiSearchAction.MemberKick
                    when from.Parent is { SelectedPerson: { } member, LinkshellItem: { WornSlot: > 0 } shell }:
                    var command = action switch
                    {
                        StockUiSearchAction.MemberToSack => StockUiSocialCommand.LinkshellToSack,
                        StockUiSearchAction.MemberToPearl => StockUiSocialCommand.LinkshellToPearl,
                        _ => StockUiSocialCommand.LinkshellKick,
                    };
                    RunSocialAndClose(command, member, shell.WornSlot);
                    break;
                case StockUiSearchAction.FriendList:
                    lock (_sync) Push(StockUiSearch.FriendMenu, from, Array.Empty<string>(), null, null);
                    break;
                case StockUiSearchAction.FriendTo:
                    _ = OpenPeopleAsync(StockUiPeoplePurpose.Friends, social.SearchAsync(new SearchQuery { AllAreas = true, FriendsOnly = true }), from,
                        StockUiSearch.FriendListMenu);
                    break;
                case StockUiSearchAction.FriendMessages:
                    // PlayOnline's messages: GordianXI has no PlayOnline service, so the list is empty (its column bar shows).
                    lock (_sync) OpenPeopleList(StockUiPeoplePurpose.Messages, Array.Empty<StockUiPerson>(), from, StockUiSearch.MessagesMenu);
                    break;
                case StockUiSearchAction.FriendStatus:
                    lock (_sync) Push(StockUiSearch.OnlineStatusMenu, from, Array.Empty<string>(), null, null);
                    break;
                case StockUiSearchAction.OnlineStatus:
                    _onlineStatus = value;
                    lock (_sync) Refresh(from);
                    Changed?.Invoke();
                    break;
                case StockUiSearchAction.EmoteList:
                    lock (_sync)
                    {
                        var emotes = StockUiSearch.Emotes.Select(e => new StockUiPerson("/" + e)).ToList();
                        OpenPeopleList(StockUiPeoplePurpose.Emotes, emotes, from, StockUiSearch.EmoteMenu);
                    }
                    break;
            }
        }

        /// <summary>
        /// Ballista greyed (no Ballista area is tracked: retail greys it outside one, round 1); a worn item's equip entry
        /// labelled "Unequip"; Search by Level's range in its fields; the online status marked. Call under the lock.
        /// </summary>
        private void RefreshSearchMenu(StockUiOpenMenu menu)
        {
            string name = menu.Name.ToLowerInvariant();
            if (name == StockUiSearch.SearchMenu) menu.SetGreyed(new HashSet<int> { StockUiSearch.BallistaButton });
            else if (name is StockUiSearch.LinkshellMenu or StockUiSearch.PearlsackMenu or StockUiSearch.LinkpearlMenu)
            {
                menu.LabelImages = new Dictionary<int, UiShapeReference>
                {
                    [StockUiSearch.UnequipButton] = new(0, StockUiSearch.UnequipImage, StockUiParty.LabelGroup),
                };
            }
            else if (name == StockUiSearch.LevelMenu)
            {
                var texts = new Dictionary<int, StockUiSideText>();
                foreach (var (button, level) in new[] { (StockUiSearch.LevelMinButton, menu.LevelMin), (StockUiSearch.LevelMaxButton, menu.LevelMax) })
                {
                    // The field being typed in shows the text field instead.
                    if (menu.SocialFieldButton == button && menu.SocialField is { IsOpen: true }) continue;
                    if (menu.Menu.FindButton(button) is { } field && level > 0)
                        texts[button] = new StockUiSideText(level.ToString(CultureInfo.InvariantCulture), field.X + LevelTextInset);
                }
                menu.SideTexts = texts;
            }
            else if (name == StockUiSearch.OnlineStatusMenu) menu.SetMarks(new HashSet<int> { _onlineStatus });
            else if (name == StockUiSearch.SackMenu && menu.Parent?.SelectedPerson is { } member && !StockUiSearch.SackCanKick(member.LinkshellRank))
                menu.SetGreyed(new HashSet<int> { StockUiSearch.SackKickButton });
        }

        /// <summary>The level's digits sit this far into their 24-px field (image-43).</summary>
        private const float LevelTextInset = 4;

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

        /// <summary>
        /// Search by Area (image-45): the list over the chat, "Current Area (Area)", "Current Region (Region)", "All Areas
        /// (World)", then every region with its number of zones. Call under the lock.
        /// </summary>
        private void OpenAreaList(StockUiOpenMenu from)
        {
            var entries = new List<StockUiMapListEntry>
            {
                new(StockUiMapListLevel.Area, CurrentAreaRow, 0, 0), new(StockUiMapListLevel.Area, CurrentRegionRow, 0, 0),
                new(StockUiMapListLevel.Area, AllAreasRow, 0, 0),
            };
            var rows = new List<StockUiListRow>
            {
                new(1, "Current Area", false, Value: "(Area)"), new(2, "Current Region", false, Value: "(Region)"), new(3, "All Areas", false, Value: "(World)"),
            };
            foreach (int region in ZoneRegions.Regions)
            {
                int zones = ZoneRegions.ZonesIn(region).Count;
                if (zones == 0) continue;
                entries.Add(new StockUiMapListEntry(StockUiMapListLevel.Area, region, 0, 0));
                rows.Add(new StockUiListRow(rows.Count + 1, RegionName?.Invoke(region) ?? string.Empty, false, Value: "[" + zones.ToString(CultureInfo.InvariantCulture) + "]"));
            }
            OpenSearchList(StockUiSearch.AreaListMenu, StockUiSearchList.Areas, StockUiSearch.AreaTitle, from, rows, entries);
        }

        private const int CurrentAreaRow = -1, CurrentRegionRow = -2, AllAreasRow = -3;

        /// <summary>Search by Unity (image-47): "Unity (Unity)", then the eleven Unities with their short names. Call under the lock.</summary>
        private void OpenUnityList(StockUiOpenMenu from)
        {
            var rows = new List<StockUiListRow> { new(1, "Unity", false, Value: "(Unity)") };
            var entries = new List<StockUiMapListEntry> { new(StockUiMapListLevel.Area, 0, 0, 0) };
            for (int i = 0; i < StockUiSearch.Unities.Count; i++)
            {
                rows.Add(new StockUiListRow(rows.Count + 1, StockUiSearch.Unities[i].Name, false, Value: "(" + StockUiSearch.Unities[i].Short + ")"));
                entries.Add(new StockUiMapListEntry(StockUiMapListLevel.Area, i + 1, 0, 0));
            }
            OpenSearchList(StockUiSearch.AreaListMenu, StockUiSearchList.Unity, StockUiSearch.UnityTitle, from, rows, entries);
        }

        /// <summary>Pushes a search list over the chat in the Change Map lists' frames and look. Call under the lock.</summary>
        private void OpenSearchList(string menuName, StockUiSearchList kind, string title, StockUiOpenMenu parent, List<StockUiListRow> rows,
            List<StockUiMapListEntry> entries)
        {
            if (_library is not { } library || !library.TryGetMenu(menuName, out var definition))
            {
                NoticePosted?.Invoke($"{title} is not available in this session.");
                return;
            }
            var menu = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null, VisibleRowsOf(definition), StockUiListKind.MapAreas)
            {
                Rows = rows,
                MapEntries = entries,
                SearchList = kind,
                HelpTitle = title,
            };
            menu.SelectedButtonId = 1;
            PushOpen(menu);
        }

        /// <summary>Confirm on a search list: an area or zone is searched, a region lists its zones, a Unity is not searchable yet.</summary>
        private void ActivateSearchList(StockUiOpenMenu list, int buttonId)
        {
            int index = list.EntryIndex(buttonId);
            if (index < 0 || index >= list.MapEntries.Count || Social is not { } social) return;
            var entry = list.MapEntries[index];
            switch (list.SearchList)
            {
                case StockUiSearchList.Unity:
                    // LandSandBoat's search server has no Unity filter and the request's field is not known.
                    NoticePosted?.Invoke("Search by Unity is not available yet.");
                    return;
                case StockUiSearchList.Zones:
                    Search(list, new SearchQuery { Areas = { (ushort)entry.Zone } });
                    return;
            }
            switch (entry.Region)
            {
                case CurrentAreaRow:
                    Search(list, AreaQuery(social));
                    return;
                case AllAreasRow:
                    Search(list, new SearchQuery { AllAreas = true });
                    return;
                case CurrentRegionRow:
                    int region = ZoneRegions.RegionOf(social.CurrentZone);
                    var query = new SearchQuery();
                    foreach (int zone in ZoneRegions.ZonesIn(region).Take(SearchRequestBuilder.MaxAreas)) query.Areas.Add((ushort)zone);
                    if (query.Areas.Count == 0) query.AllAreas = true;
                    Search(list, query);
                    return;
            }
            lock (_sync)
            {
                var rows = new List<StockUiListRow>();
                var entries = new List<StockUiMapListEntry>();
                foreach (int zone in ZoneRegions.ZonesIn(entry.Region))
                {
                    entries.Add(new StockUiMapListEntry(StockUiMapListLevel.Zone, entry.Region, zone, 0));
                    string? compact = ZoneCompactName?.Invoke(zone);
                    rows.Add(new StockUiListRow(rows.Count + 1, ZoneName?.Invoke(zone) ?? string.Empty, false,
                        Value: compact is { Length: > 0 } ? "(" + compact + ")" : null));
                }
                OpenSearchList(StockUiSearch.ZoneListMenu, StockUiSearchList.Zones, StockUiSearch.AreaTitle, list, rows, entries);
            }
            Changed?.Invoke();
        }

        /// <summary>Search by Level (<c>scchar</c>, image-43): both fields start at your main job's level.</summary>
        private void OpenLevelWindow(StockUiOpenMenu from, IStockUiSocial social)
        {
            lock (_sync)
            {
                if (Push(StockUiSearch.LevelMenu, from, Array.Empty<string>(), null, null) is not { } window) return;
                byte level = social.MainLevel is > 0 and <= 99 ? social.MainLevel : (byte)1;
                window.LevelMin = window.LevelMax = level;
                Refresh(window);
            }
            Changed?.Invoke();
        }

        /// <summary>A Search by Level field: the shared text field, two digits; Enter keeps the level (1-99).</summary>
        private void BeginLevelField(StockUiOpenMenu window, int field)
        {
            if (MacroField.IsOpen) MacroField.Commit();
            int button = field == 1 ? StockUiSearch.LevelMinButton : StockUiSearch.LevelMaxButton;
            lock (_sync)
            {
                window.SocialField = MacroField;
                window.SocialFieldButton = button;
                Refresh(window);
            }
            byte current = field == 1 ? window.LevelMin : window.LevelMax;
            MacroField.Open(current.ToString(CultureInfo.InvariantCulture), 2, (typed, _) =>
            {
                if (int.TryParse(typed.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int level))
                {
                    byte clamped = (byte)Math.Clamp(level, 1, 99);
                    lock (_sync)
                    {
                        if (field == 1) window.LevelMin = clamped;
                        else window.LevelMax = clamped;
                        Refresh(window); // the field may have closed first
                    }
                    Changed?.Invoke();
                }
            }, escapeKeepsText: false, verticalMoves: false);
            MacroField.OpenChanged += OnClosed;
            void OnClosed(bool open)
            {
                if (open) return;
                MacroField.OpenChanged -= OnClosed;
                lock (_sync)
                {
                    window.SocialFieldButton = 0;
                    Refresh(window);
                }
                Changed?.Invoke();
            }
            Changed?.Invoke();
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
            StockUiOpenMenu box;
            lock (_sync)
            {
                if (_library is not { } library || !library.TryGetMenu(StockUiSearch.CommentWindow, out var definition)) return;
                box = new StockUiOpenMenu(definition, from, Array.Empty<string>(), null) { CommentGenre = _commentGenre, HelpTitle = "Search Comment" };
                PushOpen(box);
            }
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

        /// <summary>
        /// The Linkshell window (<c>link5</c>, image-30): one row per linkshell item, its kind and its linkshell's name, worn
        /// ones first; the item under the cursor shows in <c>link6</c> above. Call under the lock.
        /// </summary>
        private void OpenLinkshellItems(IReadOnlyList<StockUiLinkshellItem> items, StockUiOpenMenu from)
        {
            if (items.Count == 0)
            {
                NoticePosted?.Invoke("You do not have a linkshell.");
                return;
            }
            var people = new List<StockUiPerson>(items.Count);
            foreach (var item in items) people.Add(new StockUiPerson(item.Name));
            if (OpenPeopleList(StockUiPeoplePurpose.LinkshellItems, people, from, StockUiSearch.LinkshellItemMenu) is { } list) list.LinkshellItems = items;
        }

        /// <summary>
        /// Confirm on a Linkshell row: an unworn item's options (<c>link13</c>: Set as No. 1 / No. 2, Drop; image-31), a worn
        /// one's by kind (<c>link2</c> / <c>link3</c>: Link List, Unequip, Create Pearl; <c>link4</c>: Link List, Unequip;
        /// image-32). Call under the lock.
        /// </summary>
        private void OpenLinkshellOptions(StockUiOpenMenu list)
        {
            int index = list.EntryIndex(list.SelectedButtonId);
            if (index < 0 || index >= list.LinkshellItems.Count) return;
            var item = list.LinkshellItems[index];
            if (Push(StockUiSearch.MenuOf(item), list, Array.Empty<string>(), null, null) is not { } options) return;
            options.LinkshellItem = item;
        }

        /// <summary>A worn item's Link List: its members in <c>link12</c> under <c>link6</c>, each with its rank's icon (image-33).</summary>
        private async Task OpenLinkshellMembersAsync(StockUiLinkshellItem item, Task<StockUiPeopleResult> fetch, StockUiOpenMenu parent)
        {
            StockUiPeopleResult result;
            try
            {
                result = await fetch.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                NoticePosted?.Invoke($"Linkshell: {ex.Message}");
                return;
            }
            if (result.Error is { Length: > 0 } error)
            {
                NoticePosted?.Invoke(error);
                return;
            }
            lock (_sync)
            {
                if (Array.IndexOf(_open, parent) < 0) return;
                var members = result.People.Select(p => p with { LinkshellColor = item.Color }).ToList();
                if (OpenPeopleList(StockUiPeoplePurpose.LinkshellMembers, members, parent, StockUiSearch.MemberListMenu) is { } list) list.LinkshellItem = item;
            }
        }

        private async Task RunLinkshellAsync(StockUiLinkshellItem item, StockUiSearchAction action, int slot)
        {
            if (Social is not { } social) return;
            try
            {
                if (await social.LinkshellAsync(item, action, slot).ConfigureAwait(false) is { Length: > 0 } notice) NoticePosted?.Invoke(notice);
            }
            catch (Exception ex)
            {
                NoticePosted?.Invoke($"Linkshell: {ex.Message}");
            }
        }

        /// <summary>Drop on an unworn linkshell item: asks, then throws it away (C2S 0x028, as the Items window's Drop). <b>Provisional</b> wording.</summary>
        private async Task DropLinkshellItemAsync(StockUiLinkshellItem item)
        {
            if (!await PromptYesNoAsync($"Drop {item.Name}?", defaultYes: false).ConfigureAwait(false)) return;
            CloseAll();
            try
            {
                if (ItemDrop != null) await ItemDrop((ContainerId)item.Container, item.Index, 1).ConfigureAwait(false);
                else NoticePosted?.Invoke("Drop is not available in this session.");
            }
            catch (Exception ex)
            {
                NoticePosted?.Invoke($"Drop failed: {ex.Message}");
            }
        }
    }
}
