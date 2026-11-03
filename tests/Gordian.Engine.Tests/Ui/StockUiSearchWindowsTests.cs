using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Tables;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Input;
using Gordian.Engine.Ui;
using Xunit;
using static Gordian.Engine.Tests.Ui.StockUiPartyActionsTests;

namespace Gordian.Engine.Tests.Ui
{
    /// <summary>The main menu's Search, Linkshell and Communication windows (#490).</summary>
    public class StockUiSearchWindowsTests
    {
        private static StockUiMenuController Controller(FakeSocial social, List<string>? notices = null)
        {
            var library = UiResourceLibrary.FromDefinitions(SharedMenus().Concat(new[]
            {
                List(StockUiSearch.SearchMenu, 1, 2, 3, 4, 5, 6, 17, 7, 16, 14, 8, 9, 15, 10, 13, 11, 12),
                List(StockUiSearch.JobMenu, Enumerable.Range(1, 22).ToArray()),
                List(StockUiSearch.NationMenu, 1, 2, 3),
                List(StockUiSearch.RaceMenu, 1, 2, 3, 4, 5),
                List(StockUiSearch.LevelMenu, 1, 2, 3, 4),
                List(StockUiSearch.RankMenu, Enumerable.Range(1, 10).ToArray()),
                List(StockUiSearch.NameMenu, 1, 2, 3),
                List(StockUiSearch.CommentTypeMenu, 1, 2, 3, 4, 5, 6, 7),
                List(StockUiSearch.PartyCommentMenu, 1, 2, 3),
                List(StockUiSearch.ItemCommentMenu, 1, 2, 3),
                List(StockUiSearch.LinkshellCommentMenu, 1, 2, 3),
                new UiMenuDefinition { Name = StockUiSearch.CommentWindow, Frame = new UiMenuFrame { X = 16, Y = 240, Width = 366, Height = 56 } },
                List(StockUiSearch.AreaListMenu, 16, 208, UiAnchor.BottomLeft, 1, 2, 3, 4, 5),
                List(StockUiSearch.ZoneListMenu, 16, 208, UiAnchor.BottomLeft, 1, 2, 3, 4, 5),
                List(StockUiSearch.LinkshellItemMenu, 16, 106, UiAnchor.TopLeft, Enumerable.Range(1, 10).ToArray()),
                List(StockUiSearch.MemberListMenu, 16, 106, UiAnchor.TopLeft, Enumerable.Range(1, 10).ToArray()),
                List(StockUiSearch.LinkshellMenu, 1, 2, 3),
                List(StockUiSearch.PearlsackMenu, 1, 2, 3),
                List(StockUiSearch.LinkpearlMenu, 1, 2),
                List(StockUiSearch.UnwornMenu, 1, 3, 2),
                List(StockUiSearch.HolderOverPearlMenu, 1, 2, 3),
                List(StockUiSearch.HolderOverSackMenu, 1, 2, 3),
                List(StockUiSearch.SackMenu, 1, 2),
                List(StockUiSearch.LinkshellMemberMenu, 1),
                List(StockUiSearch.CommunicationMenu, 1, 2),
                List(StockUiSearch.FriendMenu, 1, 2, 3),
                PeopleList(StockUiSearch.FriendListMenu),
                PeopleList(StockUiSearch.MessagesMenu),
                List(StockUiSearch.OnlineStatusMenu, 1, 2, 3),
                List(StockUiSearch.EmoteMenu, 16, 96, UiAnchor.BottomLeft, Enumerable.Range(1, 12).ToArray()),
            }));
            var menus = new StockUiMenuController { Library = library, Social = social };
            if (notices != null) menus.NoticePosted += notices.Add;
            return menus;
        }

        private static void Choose(StockUiMenuController menus, int button)
        {
            for (int i = 0; i < 40 && menus.Top!.SelectedButtonId != button; i++) menus.Move(InputAction.MenuDown);
            Assert.Equal(button, menus.Top!.SelectedButtonId);
            menus.Activate();
        }

        private static readonly StockUiPeopleResult OnePlayer = new(new[] { new StockUiPerson("Ayame", CharacterId: 7, Detail: "SAM75/WAR37 Port Bastok") });

        [Fact]
        public void MainMenuEntries_OpenTheWindows()
        {
            Assert.True(StockUiMenuEntries.TryGet(StockUiMenuEntries.MainMenu, 8, out var search));
            Assert.Equal(StockUiSearch.SearchMenu, search.Opens);
            Assert.True(StockUiMenuEntries.TryGet(StockUiMenuEntries.MainMenu, 9, out var linkshell));
            Assert.Equal((StockUiSearchAction.LinkshellItems, 0), StockUiSearch.ActionOf(linkshell.Argument));
            Assert.True(StockUiMenuEntries.TryGet(StockUiMenuEntries.MainMenuPage2, 9, out var communication));
            Assert.Equal(StockUiSearch.CommunicationMenu, communication.Opens);
        }

        [Theory]
        [InlineData(StockUiSearch.JobMenu, 13)]
        [InlineData(StockUiSearch.NationMenu, 2)]
        [InlineData(StockUiSearch.RaceMenu, 5)]
        [InlineData(StockUiSearch.RankMenu, 6)]
        public async Task Lists_SearchThisAreaWithTheirFilter(string listMenu, int button)
        {
            var social = new FakeSocial { SearchResult = OnePlayer };
            var menus = Controller(social);
            Assert.True(menus.Open(StockUiSearch.SearchMenu));
            Choose(menus, listMenu switch
            {
                StockUiSearch.JobMenu => 3, StockUiSearch.NationMenu => 4, StockUiSearch.RaceMenu => 5, _ => 7,
            });
            Assert.Equal(listMenu, menus.Top!.Name);
            Choose(menus, button);
            await Until(() => menus.Top?.ListKind == StockUiListKind.People);

            var query = social.LastQuery!;
            Assert.Equal(new ushort[] { 243 }, query.Areas);
            switch (listMenu)
            {
                case StockUiSearch.JobMenu: Assert.Equal((byte)13, query.Job); break; // ninja
                case StockUiSearch.NationMenu: Assert.Equal((byte)1, query.Nation); break; // Bastok
                case StockUiSearch.RaceMenu: Assert.Equal((byte)4, query.Race); break; // Galka
                default: Assert.Equal(((byte)6, (byte)6), query.Rank); break;
            }
            Assert.Equal(StockUiPeople.ResultsMenu, menus.Top!.Name);
            Assert.Equal(new[] { StockUiPeople.ResultsHeader, "Ayame" }, menus.Top.Rows.Select(r => r.Text));
            Assert.Equal("Ayame", menus.Top.People.Single().Name);
        }

        [Fact]
        public async Task Level_TypesTheRangeInItsFields_AndSearches()
        {
            var social = new FakeSocial { SearchResult = OnePlayer, MainLevel = 75 };
            var menus = Controller(social);
            menus.Open(StockUiSearch.SearchMenu);
            Choose(menus, 6);
            var window = menus.Top!;
            Assert.Equal(StockUiSearch.LevelMenu, window.Name);
            Assert.Equal((75, 75), (window.LevelMin, window.LevelMax)); // your own level
            Assert.Equal("75", window.SideTexts[StockUiSearch.LevelMinButton].Text);

            menus.Activate(); // the minimum
            Assert.True(menus.MacroField.IsOpen);
            menus.MacroField.HandleKey(GordianKey.Backspace, InputModifiers.None);
            menus.MacroField.HandleKey(GordianKey.Backspace, InputModifiers.None);
            menus.MacroField.InsertText("60");
            menus.MacroField.Commit();
            Assert.Equal(60, window.LevelMin);
            Assert.Equal("60", window.SideTexts[StockUiSearch.LevelMinButton].Text);

            Choose(menus, StockUiSearch.LevelBeginButton);
            await Until(() => menus.Top?.ListKind == StockUiListKind.People);
            Assert.Equal(((byte)60, (byte)75), social.LastQuery!.Level);
        }

        [Fact]
        public async Task Name_TypesInTheField_AndSearchesEveryArea()
        {
            var social = new FakeSocial { SearchResult = OnePlayer };
            var menus = Controller(social);
            menus.Open(StockUiSearch.SearchMenu);
            Choose(menus, 2);
            Assert.Equal(StockUiSearch.NameMenu, menus.Top!.Name);
            menus.Activate(); // the field
            Assert.True(menus.MacroField.IsOpen);
            Assert.Same(menus.MacroField, menus.Top!.SocialField);
            // Round 1: the next tick closed the field (it was only kept open for the macro windows), so nothing could be typed.
            Assert.True(menus.ProcessInput(new InputState(), TimeSpan.FromMilliseconds(16)));
            Assert.True(menus.MacroField.IsOpen);
            menus.MacroField.InsertText("Aya");
            menus.MacroField.Commit();
            await Until(() => menus.Top?.ListKind == StockUiListKind.People);
            Assert.True(social.LastQuery!.AllAreas);
            Assert.Equal("Aya", social.LastQuery.Name);
        }

        [Fact]
        public async Task Area_StartsWithTheAreaListOverTheChat()
        {
            var social = new FakeSocial { SearchResult = OnePlayer };
            var menus = Controller(social);
            menus.Open(StockUiSearch.SearchMenu);
            Choose(menus, 1);
            var areas = menus.Top!;
            Assert.Equal((StockUiSearch.AreaListMenu, StockUiSearchList.Areas, StockUiSearch.AreaTitle), (areas.Name, areas.SearchList, areas.HelpTitle));
            Assert.Equal(new[] { "Current Area", "Current Region", "All Areas" }, areas.Rows.Take(3).Select(r => r.Text));
            Assert.Equal(new[] { "(Area)", "(Region)", "(World)" }, areas.Rows.Take(3).Select(r => r.Value));

            menus.Activate(); // Current Area
            await Until(() => menus.Top?.ListKind == StockUiListKind.People);
            Assert.Equal(new ushort[] { 243 }, social.LastQuery!.Areas);

            menus.CloseTop();
            menus.Move(InputAction.MenuDown);
            menus.Activate(); // Current Region: the zones of 243's region
            await Until(() => menus.Top?.ListKind == StockUiListKind.People);
            Assert.Equal(ZoneRegions.ZonesIn(ZoneRegions.RegionOf(243)).Select(z => (ushort)z).Take(15), social.LastQuery!.Areas);

            menus.CloseTop();
            menus.Move(InputAction.MenuDown);
            menus.Activate(); // All Areas
            await Until(() => menus.Top?.ListKind == StockUiListKind.People);
            Assert.True(social.LastQuery!.AllAreas);

            menus.CloseTop();
            menus.Move(InputAction.MenuDown);
            menus.Activate(); // the first region: its zones
            var zones = menus.Top!;
            Assert.Equal(StockUiSearchList.Zones, zones.SearchList);
            int firstZone = ZoneRegions.ZonesIn(ZoneRegions.Regions[0])[0];
            menus.Activate();
            await Until(() => menus.Top?.ListKind == StockUiListKind.People);
            Assert.Equal(new[] { (ushort)firstZone }, social.LastQuery!.Areas);
        }

        [Fact]
        public void Unity_ListsTheUnities_AndSaysTheSearchIsNotAvailable()
        {
            var notices = new List<string>();
            var menus = Controller(new FakeSocial(), notices);
            menus.Open(StockUiSearch.SearchMenu);
            Choose(menus, 14);
            var unity = menus.Top!;
            Assert.Equal(StockUiSearchList.Unity, unity.SearchList);
            Assert.Equal(new[] { "Unity", "Pieuje", "Ayame", "Invincible Shield", "Apururu" }, unity.Rows.Take(5).Select(r => r.Text));
            Assert.Equal("(IShield)", unity.Rows[3].Value);
            menus.Activate();
            Assert.Equal("Search by Unity is not available yet.", notices.Single());
        }

        [Fact]
        public void Ballista_IsGreyed()
        {
            var menus = Controller(new FakeSocial());
            menus.Open(StockUiSearch.SearchMenu);
            Assert.True(menus.Top!.IsGreyed(StockUiSearch.BallistaButton));
        }

        [Fact]
        public async Task Friend_And_Linkshell_SearchTheirLists()
        {
            var social = new FakeSocial { SearchResult = OnePlayer };
            var menus = Controller(social);
            menus.Open(StockUiSearch.SearchMenu);
            Choose(menus, 8);
            await Until(() => menus.Top?.ListKind == StockUiListKind.People);
            Assert.True(social.LastQuery!.FriendsOnly);
            Assert.True(social.LastQuery.AllAreas);

            menus.CloseAll();
            menus.Open(StockUiSearch.SearchMenu);
            Choose(menus, 15);
            await Until(() => menus.Top?.ListKind == StockUiListKind.People);
            Assert.Equal(2, social.LinkshellSlotAsked);
            Assert.Equal(StockUiPeoplePurpose.SearchResults, menus.Top!.PeoplePurpose);
        }

        [Theory]
        [InlineData(1, 2, 0x12u)] // job levels, looking for members
        [InlineData(4, 1, 0x41u)] // want to sell
        [InlineData(5, 3, 0x53u)]
        [InlineData(6, 0, 0x61u)] // friends: no refine list
        [InlineData(7, 0, 0x73u)]
        public async Task CommentSearch_PicksTheTypeThenSearches(int genre, int refine, uint type)
        {
            var social = new FakeSocial { SearchResult = OnePlayer };
            var menus = Controller(social);
            menus.Open(StockUiSearch.SearchMenu);
            Choose(menus, 11);
            Assert.Equal(StockUiSearch.CommentTypeMenu, menus.Top!.Name);
            Choose(menus, genre);
            if (refine != 0) Choose(menus, refine);
            await Until(() => social.LastQuery != null);
            Assert.Equal(type, social.LastQuery!.CommentType);
            Assert.Equal(type, StockUiSearch.CommentType(genre, refine));
        }

        [Fact]
        public async Task EditComment_OpensTheBoxWithTheTypesDot_AndSendsTheText()
        {
            var notices = new List<string>();
            var social = new FakeSocial();
            var menus = Controller(social, notices);
            menus.Open(StockUiSearch.SearchMenu);
            Choose(menus, 12);
            Choose(menus, 1);
            Choose(menus, 1); // looking for a party
            var box = menus.Top!;
            Assert.Equal((StockUiSearch.CommentWindow, 1), (box.Name, box.CommentGenre));
            Assert.True(menus.MacroField.IsOpen);
            Assert.True(menus.ProcessInput(new InputState(), TimeSpan.FromMilliseconds(16)));
            Assert.True(menus.MacroField.IsOpen); // round 1: the box closed before anything could be typed
            menus.MacroField.InsertText("LFP WAR75");
            menus.MacroField.Commit();
            await Until(() => social.Comment != null);
            Assert.Equal(("LFP WAR75", 0x11u), social.Comment!.Value);
            Assert.False(menus.IsOpen);
            await Until(() => notices.Contains(StockUiMenuController.CommentSetNotice));
        }

        [Fact]
        public void EditComment_EscapeReturnsToTheType()
        {
            var social = new FakeSocial();
            var menus = Controller(social);
            menus.Open(StockUiSearch.SearchMenu);
            Choose(menus, 12);
            Choose(menus, 7);
            Assert.Equal(StockUiSearch.CommentWindow, menus.Top!.Name);
            menus.MacroField.Cancel();
            Assert.Equal(StockUiSearch.CommentTypeMenu, menus.Top!.Name);
            Assert.Null(social.Comment);
        }

        [Fact]
        public async Task Linkshell_WornItem_LinkListAndRankedMemberOptions()
        {
            var social = new FakeSocial
            {
                SearchResult = new StockUiPeopleResult(new[]
                {
                    new StockUiPerson("Cybin", LinkshellRank: 1), new StockUiPerson("Zeid", LinkshellRank: 2), new StockUiPerson("Prishe", LinkshellRank: 3),
                }),
            };
            social.Linkshells.Add(new StockUiLinkshellItem("Rangers", StockUiLinkshellKind.Linkshell, 0, 5, 0x1234, 1, 0x0F0));
            var menus = Controller(social);
            menus.Open(StockUiMenuEntries.MainMenu);
            Choose(menus, 9);
            var list = menus.Top!;
            Assert.Equal(StockUiSearch.LinkshellItemMenu, list.Name);
            Assert.Equal("Rangers", list.Rows.Single().Text);

            menus.Activate();
            var options = menus.Top!;
            Assert.Equal(StockUiSearch.LinkshellMenu, options.Name);
            Assert.Equal(StockUiSearch.UnequipImage, options.LabelImages[StockUiSearch.UnequipButton].ImageIndex);
            menus.Activate(); // Link List
            await Until(() => menus.Top?.PeoplePurpose == StockUiPeoplePurpose.LinkshellMembers);
            var members = menus.Top!;
            Assert.Equal(StockUiSearch.MemberListMenu, members.Name);
            Assert.Equal(0x1234u, social.LinkshellIdAsked);
            Assert.All(members.People, p => Assert.Equal(0x0F0, p.LinkshellColor));

            menus.Activate(); // yourself: Tell only
            Assert.Equal(StockUiSearch.LinkshellMemberMenu, menus.Top!.Name);
            menus.CloseTop();
            menus.Move(InputAction.MenuDown);
            menus.Activate(); // a sack holder
            Assert.Equal(StockUiSearch.HolderOverSackMenu, menus.Top!.Name);
            menus.CloseTop();
            menus.Move(InputAction.MenuDown);
            menus.Activate(); // a pearl
            Assert.Equal(StockUiSearch.HolderOverPearlMenu, menus.Top!.Name);
            Choose(menus, 2); // To Sack
            await Until(() => social.Runs.Count == 1);
            Assert.Equal((StockUiSocialCommand.LinkshellToSack, "Prishe", 1), (social.Runs[0].Command, social.Runs[0].Person.Name, social.Runs[0].Argument));
        }

        [Fact]
        public async Task Linkshell_SackHolder_KickGreyedOverSackOrShell()
        {
            var social = new FakeSocial
            {
                SearchResult = new StockUiPeopleResult(new[]
                {
                    new StockUiPerson("Cybin", LinkshellRank: 2), new StockUiPerson("Gemini", LinkshellRank: 1),
                    new StockUiPerson("Zeid", LinkshellRank: 2), new StockUiPerson("Prishe", LinkshellRank: 3),
                }),
            };
            social.Linkshells.Add(new StockUiLinkshellItem("Rangers", StockUiLinkshellKind.Pearlsack, 0, 5, 0x1234, 1, 0x0F0));
            var menus = Controller(social);
            menus.Open(StockUiMenuEntries.MainMenu);
            Choose(menus, 9);
            menus.Activate();
            menus.Activate(); // Link List
            await Until(() => menus.Top?.PeoplePurpose == StockUiPeoplePurpose.LinkshellMembers);
            foreach (var (name, greyed) in new[] { ("Gemini", true), ("Zeid", true), ("Prishe", false) })
            {
                menus.Move(InputAction.MenuDown);
                menus.Activate();
                Assert.Equal(StockUiSearch.SackMenu, menus.Top!.Name);
                Assert.Equal(greyed, menus.Top.IsGreyed(StockUiSearch.SackKickButton));
                menus.CloseTop();
            }
        }

        [Fact]
        public async Task Linkshell_UnwornItem_SetsASlotOrDrops()
        {
            var social = new FakeSocial();
            social.Linkshells.Add(new StockUiLinkshellItem("Sentinels", StockUiLinkshellKind.Linkpearl, 0, 9, 0x99, 0));
            var menus = Controller(social);
            (ContainerId, byte, uint)? dropped = null;
            menus.ItemDrop = (container, slot, count) =>
            {
                dropped = (container, slot, count);
                return Task.CompletedTask;
            };
            menus.Open(StockUiMenuEntries.MainMenu);
            Choose(menus, 9);
            menus.Activate();
            Assert.Equal(StockUiSearch.UnwornMenu, menus.Top!.Name);
            Choose(menus, 3); // Set as No. 2
            await Until(() => social.LinkshellRuns.Count == 1);
            Assert.Equal((StockUiSearchAction.ItemSetSlot, 2), (social.LinkshellRuns[0].Action, social.LinkshellRuns[0].Slot));

            menus.Open(StockUiMenuEntries.MainMenu);
            Choose(menus, 9);
            menus.Activate();
            Choose(menus, 2); // Drop
            Assert.Equal("Drop Sentinels?", menus.Top!.Message);
            menus.Move(InputAction.MenuUp);
            menus.Activate();
            await Until(() => dropped != null);
            Assert.Equal((ContainerId.Inventory, (byte)9, 1u), dropped!.Value);
        }

        [Fact]
        public void Linkshell_NoneHeld_PostsANotice()
        {
            var notices = new List<string>();
            var menus = Controller(new FakeSocial(), notices);
            menus.Open(StockUiMenuEntries.MainMenu);
            Choose(menus, 9);
            Assert.Equal(StockUiMenuEntries.MainMenu, menus.Top!.Name);
            Assert.Equal("You do not have a linkshell.", notices.Single());
        }

        [Fact]
        public async Task Communication_FriendList_ToListMessagesAndOnlineStatus()
        {
            var social = new FakeSocial();
            var menus = Controller(social);
            menus.Open(StockUiSearch.CommunicationMenu);
            menus.Activate(); // Friend List
            Assert.Equal(StockUiSearch.FriendMenu, menus.Top!.Name);

            menus.Activate(); // To List: nobody, and it says so in the window
            await Until(() => menus.Top?.PeoplePurpose == StockUiPeoplePurpose.Friends);
            Assert.Equal((StockUiSearch.FriendListMenu, StockUiSearch.NoFriendsText), (menus.Top!.Name, menus.Top.EmptyText));
            Assert.True(social.LastQuery!.FriendsOnly);
            menus.CloseTop();

            Choose(menus, 2); // Messages
            Assert.Equal((StockUiSearch.MessagesMenu, StockUiPeoplePurpose.Messages), (menus.Top!.Name, menus.Top.PeoplePurpose));
            menus.CloseTop();

            Choose(menus, 3); // Online Stat.
            var status = menus.Top!;
            Assert.Equal(StockUiSearch.OnlineStatusMenu, status.Name);
            Assert.True(status.IsMarked(1));
            Choose(menus, 2); // Away
            Assert.True(status.IsMarked(2));
            Assert.False(status.IsMarked(1));
        }

        [Fact]
        public async Task EmoteList_PerformsTheEmote()
        {
            var social = new FakeSocial();
            var menus = Controller(social);
            menus.Open(StockUiSearch.CommunicationMenu);
            Choose(menus, 2);
            var emotes = menus.Top!;
            Assert.Equal((StockUiSearch.EmoteMenu, "Emote List"), (emotes.Name, emotes.HelpTitle));
            Assert.Equal("/amazed", emotes.Rows[0].Text);
            Assert.Equal(emotes.Rows.Select(r => r.Text).OrderBy(t => t, StringComparer.Ordinal), emotes.Rows.Select(r => r.Text));
            menus.Move(InputAction.MenuDown);
            menus.Activate();
            await Until(() => social.Runs.Count == 1);
            Assert.Equal((StockUiSocialCommand.Emote, "/angry"), (social.Runs[0].Command, social.Runs[0].Person.Name));
        }

        [Fact]
        public void MemberOptions_FollowBothRanks()
        {
            Assert.Equal(StockUiSearch.HolderOverPearlMenu, StockUiSearch.MemberMenuOf(1, 3, false));
            Assert.Equal(StockUiSearch.HolderOverSackMenu, StockUiSearch.MemberMenuOf(1, 2, false));
            Assert.Equal(StockUiSearch.SackMenu, StockUiSearch.MemberMenuOf(2, 3, false));
            Assert.Equal(StockUiSearch.SackMenu, StockUiSearch.MemberMenuOf(2, 1, false)); // image-35
            Assert.Equal(StockUiSearch.LinkshellMemberMenu, StockUiSearch.MemberMenuOf(3, 1, false));
            Assert.Equal(StockUiSearch.LinkshellMemberMenu, StockUiSearch.MemberMenuOf(1, 1, true));
            Assert.Equal(StockUiSearch.PartyCommentMenu, StockUiSearch.RefineMenuOf(3));
            Assert.Null(StockUiSearch.RefineMenuOf(6));
        }

        [Fact]
        public void LinkshellColor_IsTheExtraDatasSixthWord()
        {
            var ext = new byte[24];
            ext[6] = 0x3F; ext[7] = 0xF1; // R F, G 3, B 1; the alpha nibble F dropped
            Assert.Equal(0x13F, StockUiItems.LinkshellColorOf(515, ext));
            Assert.Equal(0, StockUiItems.LinkshellColorOf(4096, ext));
            Assert.Equal(0, StockUiItems.LinkshellColorOf(515, null));
        }

        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        /// <summary>The retail Search, Linkshell and Communication windows exist and every selectable button has an entry.</summary>
        [Fact]
        public void RetailMenus_SearchLinkshellCommunicationResolve()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            if (library == null) return;
            foreach (string name in new[]
            {
                StockUiSearch.SearchMenu, StockUiSearch.JobMenu, StockUiSearch.NationMenu, StockUiSearch.RaceMenu, StockUiSearch.LevelMenu,
                StockUiSearch.RankMenu, StockUiSearch.NameMenu, StockUiSearch.CommentTypeMenu, StockUiSearch.PartyCommentMenu,
                StockUiSearch.ItemCommentMenu, StockUiSearch.LinkshellCommentMenu, StockUiSearch.LinkshellMenu, StockUiSearch.PearlsackMenu,
                StockUiSearch.LinkpearlMenu, StockUiSearch.UnwornMenu, StockUiSearch.HolderOverPearlMenu, StockUiSearch.HolderOverSackMenu,
                StockUiSearch.SackMenu, StockUiSearch.LinkshellMemberMenu, StockUiSearch.CommunicationMenu, StockUiSearch.FriendMenu,
                StockUiSearch.OnlineStatusMenu,
            })
            {
                Assert.True(library.TryGetMenu(name, out var menu), name);
                foreach (var button in menu.Buttons)
                {
                    if (button.X < 0 || button.X >= menu.Frame.Width || !StockUiMenuController.IsSelectable(button)) continue;
                    Assert.True(StockUiMenuEntries.TryGet(name, button.ButtonId, out _), $"{name} button {button.ButtonId} has no entry");
                }
            }
            foreach (var (name, rows) in new[]
            {
                (StockUiSearch.FriendListMenu, 15), (StockUiSearch.MessagesMenu, 15), (StockUiSearch.LinkshellItemMenu, 10), (StockUiSearch.MemberListMenu, 10),
                (StockUiSearch.EmoteMenu, 12), (StockUiSearch.AreaListMenu, 5), (StockUiSearch.ZoneListMenu, 5),
            })
            {
                Assert.True(library.TryGetMenu(name, out var list), name);
                Assert.Equal(rows, list.Buttons.Count(StockUiMenuController.IsSelectable));
            }
            Assert.True(library.TryGetMenu(StockUiSearch.CommentWindow, out _));
            Assert.True(library.TryGetMenu(StockUiSearch.LinkshellHeader, out var header));
            Assert.Equal((16, 48, 366, 56), (header.Frame.X, header.Frame.Y, header.Frame.Width, header.Frame.Height));
        }
    }
}
