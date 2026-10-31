using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Resources;
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
                List(StockUiSearch.LevelMenu, Enumerable.Range(1, 10).ToArray()),
                List(StockUiSearch.RankMenu, Enumerable.Range(1, 10).ToArray()),
                List(StockUiSearch.NameMenu, 1, 2, 3),
                List(StockUiSearch.CommentTypeMenu, 1, 2, 3, 4, 5, 6, 7),
                List(StockUiSearch.PartyCommentMenu, 1, 2, 3),
                List(StockUiSearch.ItemCommentMenu, 1, 2, 3),
                List(StockUiSearch.LinkshellCommentMenu, 1, 2, 3),
                new UiMenuDefinition { Name = StockUiSearch.CommentWindow, Frame = new UiMenuFrame { X = 16, Y = 240, Width = 366, Height = 56 } },
                List(StockUiSearch.LinkshellItemMenu, 16, 106, UiAnchor.TopLeft, Enumerable.Range(1, 10).ToArray()),
                List(StockUiSearch.LinkshellMenu, 1, 2, 3),
                List(StockUiSearch.PearlsackMenu, 1, 2, 3),
                List(StockUiSearch.LinkpearlMenu, 1, 2),
                List(StockUiSearch.LinkshellMemberMenu, 1),
                List(StockUiSearch.CommunicationMenu, 1, 2),
                PeopleList(StockUiSearch.FriendListMenu),
                PeopleList(StockUiSearch.BlacklistMenu),
                List(StockUiSearch.BlacklistOptionsMenu, 1),
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
        [InlineData(StockUiSearch.LevelMenu, 10)]
        [InlineData(StockUiSearch.RankMenu, 6)]
        public async Task Lists_SearchThisAreaWithTheirFilter(string listMenu, int button)
        {
            var social = new FakeSocial { SearchResult = OnePlayer };
            var menus = Controller(social);
            Assert.True(menus.Open(StockUiSearch.SearchMenu));
            Choose(menus, listMenu switch
            {
                StockUiSearch.JobMenu => 3, StockUiSearch.NationMenu => 4, StockUiSearch.RaceMenu => 5, StockUiSearch.LevelMenu => 6, _ => 7,
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
                case StockUiSearch.LevelMenu: Assert.Equal(((byte)91, (byte)99), query.Level); break;
                default: Assert.Equal(((byte)6, (byte)6), query.Rank); break;
            }
            Assert.Equal(StockUiPeople.ResultsMenu, menus.Top!.Name);
            Assert.Equal("Ayame", menus.Top.Rows.Single().Text);
            Assert.Equal("SAM75/WAR37 Port Bastok", menus.Top.Rows.Single().Value);
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
            menus.MacroField.InsertText("Aya");
            menus.MacroField.Commit();
            await Until(() => menus.Top?.ListKind == StockUiListKind.People);
            Assert.True(social.LastQuery!.AllAreas);
            Assert.Equal("Aya", social.LastQuery.Name);
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
            Assert.Equal(StockUiPeoplePurpose.LinkshellMembers, menus.Top!.PeoplePurpose);
            menus.Activate();
            Assert.Equal(StockUiSearch.LinkshellMemberMenu, menus.Top!.Name); // a member's Tell
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
        public async Task EditComment_SendsTheTextWithItsType()
        {
            var notices = new List<string>();
            var social = new FakeSocial();
            var menus = Controller(social, notices);
            menus.Open(StockUiSearch.SearchMenu);
            Choose(menus, 12);
            Choose(menus, 1);
            Choose(menus, 1); // looking for a party
            Assert.Equal(StockUiSearch.CommentWindow, menus.Top!.Name);
            Assert.True(menus.MacroField.IsOpen);
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
        public async Task Linkshell_ListsItems_OptionsFitTheKind()
        {
            var social = new FakeSocial { SearchResult = OnePlayer };
            social.Linkshells.Add(new StockUiLinkshellItem("Rangers", StockUiLinkshellKind.Linkshell, 0, 5, 0x1234, 1));
            social.Linkshells.Add(new StockUiLinkshellItem("Sentinels", StockUiLinkshellKind.Linkpearl, 0, 9, 0x99, 0));
            var menus = Controller(social);
            menus.Open(StockUiMenuEntries.MainMenu);
            Choose(menus, 9);
            var list = menus.Top!;
            Assert.Equal(StockUiSearch.LinkshellItemMenu, list.Name);
            Assert.Equal(new[] { "Rangers", "Sentinels" }, list.Rows.Select(r => r.Text));
            Assert.Equal("Linkshell", list.Rows[0].Value);

            menus.Activate();
            Assert.Equal(StockUiSearch.LinkshellMenu, menus.Top!.Name);
            Assert.False(menus.Top.IsGreyed(3)); // worn: a linkpearl can be made
            menus.Activate(); // Members
            await Until(() => menus.Top?.PeoplePurpose == StockUiPeoplePurpose.LinkshellMembers);
            Assert.Equal(0x1234u, social.LinkshellIdAsked);

            menus.CloseTop();
            menus.CloseTop();
            menus.Move(InputAction.MenuDown);
            menus.Activate();
            Assert.Equal(StockUiSearch.LinkpearlMenu, menus.Top!.Name);
            Choose(menus, 2); // equip
            await Until(() => social.LinkshellRuns.Count == 1);
            Assert.Equal(("Sentinels", StockUiSearchAction.ItemEquip), (social.LinkshellRuns[0].Item.Name, social.LinkshellRuns[0].Action));
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
        public async Task Communication_FriendList_FlipsToTheBlacklist_AndDeletes()
        {
            var social = new FakeSocial { SearchResult = OnePlayer, Blacklist = new StockUiPeopleResult(new[] { new StockUiPerson("Troll", CharacterId: 3) }) };
            var menus = Controller(social);
            menus.Open(StockUiSearch.CommunicationMenu);
            menus.Activate(); // Friend List
            await Until(() => menus.Top?.PeoplePurpose == StockUiPeoplePurpose.Friends);
            Assert.Equal(StockUiSearch.FriendListMenu, menus.Top!.Name);
            Assert.True(social.LastQuery!.FriendsOnly);

            menus.Move(InputAction.MenuRight);
            await Until(() => menus.Top?.PeoplePurpose == StockUiPeoplePurpose.Blacklist);
            Assert.Equal(StockUiSearch.BlacklistMenu, menus.Top!.Name);
            Assert.Equal(StockUiSearch.CommunicationMenu, menus.Top.Parent!.Name);
            menus.Activate();
            Assert.Equal(StockUiSearch.BlacklistOptionsMenu, menus.Top!.Name);
            menus.Activate(); // Delete
            Assert.Equal("Remove Troll from the blacklist?", menus.Top!.Message);
            menus.Move(InputAction.MenuUp);
            menus.Activate();
            await Until(() => social.Runs.Count == 1);
            Assert.Equal((StockUiSocialCommand.BlacklistRemove, "Troll"), (social.Runs[0].Command, social.Runs[0].Person.Name));
        }

        [Fact]
        public async Task EmptyFriendList_StillOpens_ToReachTheBlacklist()
        {
            var notices = new List<string>();
            var menus = Controller(new FakeSocial(), notices);
            menus.Open(StockUiSearch.CommunicationMenu);
            menus.Activate();
            await Until(() => menus.Top?.PeoplePurpose == StockUiPeoplePurpose.Friends);
            Assert.Contains("No one on your friend list is online.", notices);
        }

        [Fact]
        public void LevelBrackets_AndRefineMenus()
        {
            Assert.Equal(((byte)1, (byte)10), StockUiSearch.LevelBracket(1));
            Assert.Equal(((byte)81, (byte)90), StockUiSearch.LevelBracket(9));
            Assert.Equal(((byte)91, (byte)99), StockUiSearch.LevelBracket(10));
            Assert.Equal(StockUiSearch.PartyCommentMenu, StockUiSearch.RefineMenuOf(3));
            Assert.Null(StockUiSearch.RefineMenuOf(6));
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
                StockUiSearch.LinkpearlMenu, StockUiSearch.LinkshellMemberMenu, StockUiSearch.CommunicationMenu, StockUiSearch.BlacklistOptionsMenu,
            })
            {
                Assert.True(library.TryGetMenu(name, out var menu), name);
                foreach (var button in menu.Buttons)
                {
                    if (button.X < 0 || button.X >= menu.Frame.Width || !StockUiMenuController.IsSelectable(button)) continue;
                    Assert.True(StockUiMenuEntries.TryGet(name, button.ButtonId, out _), $"{name} button {button.ButtonId} has no entry");
                }
            }
            foreach (var (name, rows) in new[] { (StockUiSearch.FriendListMenu, 15), (StockUiSearch.BlacklistMenu, 15), (StockUiSearch.LinkshellItemMenu, 10) })
            {
                Assert.True(library.TryGetMenu(name, out var list), name);
                Assert.Equal(rows, list.Buttons.Count(StockUiMenuController.IsSelectable));
            }
            Assert.True(library.TryGetMenu(StockUiSearch.CommentWindow, out _));
        }
    }
}
