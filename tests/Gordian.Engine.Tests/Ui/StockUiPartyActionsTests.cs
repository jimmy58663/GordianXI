using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using Gordian.Core.Network.Search;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Input;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Ui
{
    /// <summary>The Party menu's entries past Disband (#489): lists, flags, languages, alarm, distribution, alliance and level sync.</summary>
    public class StockUiPartyActionsTests
    {
        internal sealed class FakeSocial : IStockUiSocial
        {
            public StockUiSocialState State { get; set; }
            public List<StockUiPerson> Members { get; } = new();
            public List<StockUiPerson> Requests { get; } = new();
            public StockUiPeopleResult SearchResult { get; set; } = new(Array.Empty<StockUiPerson>());
            public List<(StockUiSocialCommand Command, StockUiPerson Person, int Argument)> Runs { get; } = new();
            public SearchQuery? LastQuery { get; private set; }
            public (string Text, uint Type)? Comment { get; private set; }
            public ushort CurrentZone { get; set; } = 243;

            public IReadOnlyList<StockUiPerson> PartyMembers(bool includeSelf) =>
                includeSelf ? new[] { new StockUiPerson("Cybin", 0x100) }.Concat(Members).ToList() : Members;

            public IReadOnlyList<StockUiPerson> JoinRequests() => Requests;

            public Task<StockUiPeopleResult> MemberListAsync() => Task.FromResult(new StockUiPeopleResult(PartyMembers(true)));

            public Task<StockUiPeopleResult> SearchAsync(SearchQuery query)
            {
                LastQuery = query;
                return Task.FromResult(SearchResult);
            }

            public Task<StockUiPeopleResult> LinkshellMembersAsync(int slot) => Task.FromResult(SearchResult);

            public Task<StockUiPeopleResult> BlacklistAsync() => Task.FromResult(SearchResult);

            public Task<string?> RunAsync(StockUiSocialCommand command, StockUiPerson person = default, int argument = 0)
            {
                lock (Runs) Runs.Add((command, person, argument));
                return Task.FromResult<string?>(null);
            }

            public Task<string?> SetSearchCommentAsync(string text, uint commentType)
            {
                Comment = (text, commentType);
                return Task.FromResult<string?>(null);
            }
        }

        internal static UiMenuDefinition List(string name, params int[] buttonIds) => List(name, 384, 48, UiAnchor.TopRight, buttonIds);

        internal static UiMenuDefinition List(string name, short x, short y, UiAnchor anchor, params int[] buttonIds)
        {
            var buttons = new List<UiMenuButton>();
            for (int i = 0; i < buttonIds.Length; i++)
            {
                int id = buttonIds[i];
                buttons.Add(new UiMenuButton
                {
                    ButtonId = (short)id, X = 16, Y = (short)(6 + 16 * i), Width = 88, Height = 16,
                    NavUp = (sbyte)buttonIds[(i - 1 + buttonIds.Length) % buttonIds.Length], NavDown = (sbyte)buttonIds[(i + 1) % buttonIds.Length],
                    NavLeft = (sbyte)id, NavRight = (sbyte)id,
                });
            }
            return new UiMenuDefinition
            {
                Name = name,
                Frame = new UiMenuFrame { X = x, Y = y, Width = 112, Height = (short)(10 + 16 * buttonIds.Length), Anchor = anchor },
                Buttons = buttons,
            };
        }

        /// <summary>A 366 x 248 people list of fifteen rows (<c>scresult</c>, <c>levelsyn</c>, <c>friend</c>, <c>blklist</c>).</summary>
        internal static UiMenuDefinition PeopleList(string name) => List(name, 16, 48, UiAnchor.TopLeft, Enumerable.Range(1, 15).ToArray());

        internal static IEnumerable<UiMenuDefinition> SharedMenus() => new[]
        {
            List(StockUiMenuEntries.MainMenu, 6, 7, 8, 9),
            List(StockUiMenuEntries.MainMenuPage2, 9),
            List(StockUiMenuEntries.YesNoMenu, 16, 256, UiAnchor.BottomLeft, 1, 2),
            List(StockUiMenuEntries.MessageYesNoMenu, 130, 208, UiAnchor.BottomLeft, 1, 2),
            PeopleList(StockUiPeople.ResultsMenu),
            PeopleList(StockUiPeople.LevelSyncMenu),
            List(StockUiPeople.OptionsMenu, 1, 4, 5, 2, 3),
        };

        private static StockUiMenuController Controller(StockUiPartyContext party, FakeSocial social, List<string>? notices = null)
        {
            var library = UiResourceLibrary.FromDefinitions(SharedMenus().Concat(new[]
            {
                List(StockUiParty.NotInPartyMenu, 1, 2, 5, 6, 3, 4, 7),
                List(StockUiParty.SeekingMenu, 1, 2, 5, 6, 3, 4),
                List(StockUiParty.MemberMenu, 1, 6, 7, 4, 5, 2, 3),
                List(StockUiParty.LeaderMenu, 1, 2, 11, 12, 3, 4, 9, 10, 5, 6, 7, 8),
                List(StockUiParty.AllianceMenu, 1, 2),
                List(StockUiParty.LanguagesMenu, 1, 2, 3, 4, 5),
                List(StockUiParty.DistributionMenu, 1, 2),
                List(StockUiParty.AlarmMenu, 1, 2, 3, 4, 5, 6, 7, 8),
            }));
            var menus = new StockUiMenuController { Library = library, PartyContext = () => party, Social = social };
            if (notices != null) menus.NoticePosted += notices.Add;
            return menus;
        }

        private static StockUiOpenMenu OpenParty(StockUiMenuController menus)
        {
            Assert.True(menus.OpenMainMenu());
            menus.Activate(); // Party
            return menus.Top!;
        }

        private static void Choose(StockUiMenuController menus, int button)
        {
            for (int i = 0; i < 40 && menus.Top!.SelectedButtonId != button; i++) menus.Move(InputAction.MenuDown);
            Assert.Equal(button, menus.Top!.SelectedButtonId);
            menus.Activate();
        }

        /// <summary>Waits for the async work a Confirm started (the search server, a prompt's answer).</summary>
        internal static async Task Until(Func<bool> condition)
        {
            for (int i = 0; i < 100 && !condition(); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.True(condition());
        }

        private static readonly StockUiPartyContext Leader = new(true, true, 2, false);

        [Fact]
        public async Task SeekParty_TogglesThePartyFlag_AndTheFlaggedListIsPrty6()
        {
            var social = new FakeSocial();
            var menus = Controller(new StockUiPartyContext(false, false, 0, false), social);
            OpenParty(menus);
            Choose(menus, StockUiParty.SeekPartyButton);
            await Until(() => social.Runs.Count == 1);
            Assert.Equal((StockUiSocialCommand.SetSeekParty, 1), (social.Runs[0].Command, social.Runs[0].Argument));
            Assert.False(menus.IsOpen);

            social.State = social.State with { SeekingParty = true };
            menus = Controller(new StockUiPartyContext(false, false, 0, false, SeekingParty: true), social);
            Assert.Equal(StockUiParty.SeekingMenu, OpenParty(menus).Name);
            Choose(menus, StockUiParty.SeekingOffButton);
            await Until(() => social.Runs.Count == 2);
            Assert.Equal((StockUiSocialCommand.SetSeekParty, 0), (social.Runs[1].Command, social.Runs[1].Argument));
        }

        [Theory]
        [InlineData(StockUiParty.AutogroupButton, StockUiSocialCommand.SetAutogroup)]
        [InlineData(StockUiParty.UngroupedRequestButton, StockUiSocialCommand.SetRecruit)]
        public async Task AutogroupAndRequest_ToggleTheirFlags(int button, StockUiSocialCommand command)
        {
            var social = new FakeSocial { State = new StockUiSocialState(false, true, true, 0) };
            var menus = Controller(new StockUiPartyContext(false, false, 0, false), social);
            OpenParty(menus);
            Choose(menus, button);
            await Until(() => social.Runs.Count == 1);
            Assert.Equal((command, 0), (social.Runs[0].Command, social.Runs[0].Argument)); // on -> off
        }

        [Fact]
        public async Task MemberList_ListsThePartyWithJobs_AndTellPicksThePartner()
        {
            var social = new FakeSocial();
            social.Members.Add(new StockUiPerson("Zeid", 0x200, Detail: "DRK75/SAM37 Port Jeuno"));
            var menus = Controller(Leader, social);
            string? tell = null;
            ChatInputMode? mode = null;
            menus.TellTargetSelected = name => tell = name;
            menus.ChatModeSelected = m => mode = m;
            OpenParty(menus);
            Choose(menus, StockUiParty.LeaderMemberListButton);
            await Until(() => menus.Top?.ListKind == StockUiListKind.People);

            var list = menus.Top!;
            Assert.Equal(StockUiPeople.ResultsMenu, list.Name);
            Assert.Equal("Member List", list.HelpTitle);
            Assert.Equal(new[] { "Cybin", "Zeid" }, list.Rows.Select(r => r.Text));
            Assert.Equal("DRK75/SAM37 Port Jeuno", list.Rows[1].Value);
            menus.Move(InputAction.MenuDown);
            menus.Move(InputAction.MenuDown); // only populated rows take the cursor (the list wraps)
            Assert.Equal(1, list.SelectedButtonId);
            menus.Move(InputAction.MenuDown);

            menus.Activate();
            Assert.Equal(StockUiPeople.OptionsMenu, menus.Top!.Name);
            menus.Activate(); // Tell
            Assert.Equal("Zeid", tell);
            Assert.Equal(ChatInputMode.Tell, mode);
            Assert.False(menus.IsOpen);
        }

        [Fact]
        public async Task ScOption_InviteAndRequest_RunForThePerson()
        {
            var social = new FakeSocial { SearchResult = new StockUiPeopleResult(new[] { new StockUiPerson("Ayame", CharacterId: 0x1234) }) };
            var menus = Controller(Leader, social);
            OpenParty(menus);
            Choose(menus, StockUiParty.LeaderFindMemberButton);
            await Until(() => menus.Top?.ListKind == StockUiListKind.People);
            menus.Activate();
            Choose(menus, StockUiPeople.InviteButton);
            await Until(() => social.Runs.Count == 1);
            Assert.Equal(StockUiSocialCommand.Invite, social.Runs[0].Command);
            Assert.Equal(0x1234u, social.Runs[0].Person.CharacterId);
        }

        [Fact]
        public async Task FindMember_SearchesThisAreaForPlayersSeekingAParty()
        {
            var notices = new List<string>();
            var social = new FakeSocial();
            var menus = Controller(new StockUiPartyContext(false, false, 0, false), social, notices);
            OpenParty(menus);
            Choose(menus, StockUiParty.UngroupedFindMemberButton);
            await Until(() => notices.Count == 1);
            Assert.Equal("No players found.", notices[0]);
            Assert.Equal((ushort)SearchFlags.SeekingParty, social.LastQuery!.Flags1);
            Assert.Equal(new ushort[] { 243 }, social.LastQuery.Areas);
        }

        [Theory]
        [InlineData(StockUiParty.ChangeLeaderButton, "Pass leadership to Zeid?", StockUiSocialCommand.PassLeader)]
        [InlineData(StockUiParty.RemoveMemberButton, "Remove Zeid from the party?", StockUiSocialCommand.Kick)]
        [InlineData(StockUiParty.LevelSyncButton, "Synchronize levels to Zeid?", StockUiSocialCommand.LevelSync)]
        public async Task LeaderLists_AskThenRunForTheMember(int button, string question, StockUiSocialCommand command)
        {
            var social = new FakeSocial();
            social.Members.Add(new StockUiPerson("Zeid", 0x200));
            var menus = Controller(Leader, social);
            OpenParty(menus);
            Choose(menus, button);
            var list = menus.Top!;
            Assert.Equal(StockUiListKind.People, list.ListKind);
            Assert.Equal(button == StockUiParty.LevelSyncButton ? StockUiPeople.LevelSyncMenu : StockUiPeople.ResultsMenu, list.Name);
            Assert.Equal(new[] { "Zeid" }, list.Rows.Select(r => r.Text)); // not the character

            menus.Activate();
            Assert.True(menus.Top!.IsPrompt);
            Assert.Equal(question, menus.Top.Message);
            menus.Move(InputAction.MenuUp); // Yes
            menus.Activate();
            await Until(() => social.Runs.Count == 1);
            Assert.Equal(command, social.Runs[0].Command);
            Assert.Equal("Zeid", social.Runs[0].Person.Name);
        }

        [Fact]
        public async Task LevelSync_UnderSync_AsksToRemoveIt()
        {
            var social = new FakeSocial { State = new StockUiSocialState(false, false, false, 0, LevelSynced: true) };
            social.Members.Add(new StockUiPerson("Zeid", 0x200));
            var menus = Controller(Leader, social);
            OpenParty(menus);
            Choose(menus, StockUiParty.LevelSyncButton);
            await Until(() => menus.Top?.IsPrompt == true);
            Assert.Equal(StockUiMenuController.RemoveLevelSyncPrompt, menus.Top!.Message);
            menus.Move(InputAction.MenuUp);
            menus.Activate();
            await Until(() => social.Runs.Count == 1);
            Assert.Equal(StockUiSocialCommand.DisableLevelSync, social.Runs[0].Command);
        }

        [Fact]
        public void EmptyLists_PostANotice()
        {
            var notices = new List<string>();
            var menus = Controller(Leader, new FakeSocial(), notices);
            var party = OpenParty(menus);
            Choose(menus, StockUiParty.LeaderViewRequestButton);
            Assert.Same(party, menus.Top);
            Assert.Equal("No players are currently requesting to join.", notices.Single());
        }

        [Fact]
        public async Task ViewRequest_InvitesTheRequester()
        {
            var social = new FakeSocial();
            social.Requests.Add(new StockUiPerson("Prishe", 0x300, 12));
            var menus = Controller(new StockUiPartyContext(false, false, 0, false), social);
            OpenParty(menus);
            Choose(menus, StockUiParty.UngroupedViewRequestButton);
            Assert.Equal("View Request", menus.Top!.HelpTitle);
            menus.Activate();
            Assert.Equal("Invite Prishe?", menus.Top!.Message);
            menus.Move(InputAction.MenuUp);
            menus.Activate();
            await Until(() => social.Runs.Count == 1);
            Assert.Equal((StockUiSocialCommand.Invite, 0x300u), (social.Runs[0].Command, social.Runs[0].Person.ServerId));
        }

        [Fact]
        public async Task Languages_MarkTheSetOnes_AndToggleABit()
        {
            var social = new FakeSocial { State = new StockUiSocialState(false, false, false, 0x02 | 0x10) }; // English, Other
            var menus = Controller(new StockUiPartyContext(false, false, 0, false), social);
            OpenParty(menus);
            Choose(menus, StockUiParty.LanguagesButton);
            var languages = menus.Top!;
            Assert.Equal(StockUiParty.LanguagesMenu, languages.Name);
            Assert.True(languages.IsMarked(1));
            Assert.True(languages.IsMarked(5));
            Assert.False(languages.IsMarked(4));

            Choose(menus, 4); // Japanese
            await Until(() => social.Runs.Count == 1);
            Assert.Equal((StockUiSocialCommand.SetPartyLanguages, 0x02 | 0x10 | 0x01), (social.Runs[0].Command, social.Runs[0].Argument));
            Assert.True(languages.IsMarked(4));
            Assert.Same(languages, menus.Top); // the list stays for more choices
        }

        [Fact]
        public void AlarmType_IsStoredAndMarked()
        {
            var menus = Controller(new StockUiPartyContext(false, false, 0, false), new FakeSocial());
            OpenParty(menus);
            Choose(menus, StockUiParty.AlarmTypeButton);
            var alarm = menus.Top!;
            Assert.True(alarm.IsMarked(StockUiParty.AlarmOffButton));
            Choose(menus, 4); // sound 3
            Assert.Equal(3, menus.Settings.GetValue(StockUiSettingKey.PartyInviteAlarm));
            Assert.True(alarm.IsMarked(4));
            Assert.False(alarm.IsMarked(StockUiParty.AlarmOffButton));
        }

        [Fact]
        public async Task Distribution_LeaderSetsLotteryOrARepresentative_MemberSeesTheSetting()
        {
            var social = new FakeSocial();
            social.Members.Add(new StockUiPerson("Zeid", 0x200));
            var menus = Controller(Leader, social);
            OpenParty(menus);
            Choose(menus, StockUiParty.LeaderDistributionButton);
            Assert.Equal(StockUiParty.DistributionMenu, menus.Top!.Name);
            Assert.True(menus.Top.IsMarked(StockUiParty.LotteryButton));
            Choose(menus, StockUiParty.QuartermasterButton);
            Assert.Equal(new[] { "Cybin", "Zeid" }, menus.Top!.Rows.Select(r => r.Text)); // the leader may collect too
            menus.Move(InputAction.MenuDown);
            menus.Activate();
            await Until(() => social.Runs.Count == 1);
            Assert.Equal((StockUiSocialCommand.SetQuartermaster, "Zeid"), (social.Runs[0].Command, social.Runs[0].Person.Name));

            var notices = new List<string>();
            social.State = social.State with { QuartermasterName = "Zeid" };
            menus = Controller(new StockUiPartyContext(true, false, 1, false), social, notices);
            OpenParty(menus);
            Choose(menus, StockUiParty.MemberDistributionButton);
            Assert.Equal("Treasure distribution: Zeid collects all treasure.", notices.Single());
        }

        [Fact]
        public async Task Alliance_FormInvitesTheTarget_DissolveAsksFirst()
        {
            var social = new FakeSocial();
            var menus = Controller(new StockUiPartyContext(true, true, 2, false), social);
            OpenParty(menus);
            Choose(menus, StockUiParty.AllianceButton);
            Assert.Equal(StockUiParty.AllianceMenu, menus.Top!.Name);
            Assert.True(menus.Top.IsGreyed(StockUiParty.AllianceDissolveButton));
            menus.Activate(); // Form
            await Until(() => social.Runs.Count == 1);
            Assert.Equal(StockUiSocialCommand.AllianceInviteTarget, social.Runs[0].Command);

            menus = Controller(new StockUiPartyContext(true, true, 2, true), social);
            OpenParty(menus);
            Choose(menus, StockUiParty.AllianceButton);
            Assert.False(menus.Top!.IsGreyed(StockUiParty.AllianceDissolveButton));
            Choose(menus, StockUiParty.AllianceDissolveButton);
            Assert.Equal(StockUiMenuController.DissolveAlliancePrompt, menus.Top!.Message);
            menus.Move(InputAction.MenuUp);
            menus.Activate();
            await Until(() => social.Runs.Count == 2);
            Assert.Equal(StockUiSocialCommand.AllianceDissolve, social.Runs[1].Command);
        }

        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        /// <summary>The retail Party lists, their submenus and the people windows exist, and every selectable button has an entry.</summary>
        [Fact]
        public void RetailMenus_PartyWindowsResolve()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            if (library == null) return;

            foreach (string name in new[]
            {
                StockUiParty.NotInPartyMenu, StockUiParty.SeekingMenu, StockUiParty.MemberMenu, StockUiParty.LeaderMenu,
                StockUiParty.AllianceMenu, StockUiParty.LanguagesMenu, StockUiParty.DistributionMenu, StockUiParty.AlarmMenu,
                StockUiPeople.OptionsMenu,
            })
            {
                Assert.True(library.TryGetMenu(name, out var menu), name);
                foreach (var button in menu.Buttons)
                {
                    if (button.X < 0 || button.X >= menu.Frame.Width || !StockUiMenuController.IsSelectable(button)) continue;
                    Assert.True(StockUiMenuEntries.TryGet(name, button.ButtonId, out _), $"{name} button {button.ButtonId} has no entry");
                }
            }
            foreach (string name in new[] { StockUiPeople.ResultsMenu, StockUiPeople.LevelSyncMenu })
            {
                Assert.True(library.TryGetMenu(name, out var list), name);
                Assert.Equal(15, list.Buttons.Count(StockUiMenuController.IsSelectable));
                Assert.Equal((16, 48, 366, 248), (list.Frame.X, list.Frame.Y, list.Frame.Width, list.Frame.Height));
            }
        }

        [Fact]
        public void Detail_JoinsJobsAndArea()
        {
            Assert.Equal("WAR75/NIN37 Port Jeuno", StockUiPeople.Detail(1, 75, 13, 37, "Port Jeuno", null));
            Assert.Equal("WAR75", StockUiPeople.Detail(1, 75, 13, 0, null, null));
            Assert.Equal("Port Jeuno", StockUiPeople.Detail(0, 0, 0, 0, "Port Jeuno", null)); // anonymous
            Assert.Equal(string.Empty, StockUiPeople.Detail(0, 0, 0, 0, null, null));
        }

        [Fact]
        public void PartyActionArgument_RoundTrips()
        {
            Assert.Equal((StockUiPartyAction.Language, 0x10), StockUiParty.ActionOf(StockUiParty.Action(StockUiPartyAction.Language, 0x10)));
            Assert.Equal((StockUiPartyAction.MemberList, 0), StockUiParty.ActionOf(StockUiParty.Action(StockUiPartyAction.MemberList)));
        }
    }
}
