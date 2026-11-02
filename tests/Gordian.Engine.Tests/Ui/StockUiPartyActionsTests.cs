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
            public byte MainLevel { get; set; } = 99;
            public string CharacterName { get; set; } = "Cybin";

            public IReadOnlyList<StockUiPerson> PartyMembers(bool includeSelf) =>
                includeSelf ? new[] { new StockUiPerson("Cybin", 0x100) }.Concat(Members).ToList() : Members;

            public IReadOnlyList<StockUiPerson> JoinRequests() => Requests;

            public Task<StockUiPeopleResult> MemberListAsync() => Task.FromResult(new StockUiPeopleResult(PartyMembers(true)));

            public Task<StockUiPeopleResult> SearchAsync(SearchQuery query)
            {
                LastQuery = query;
                return Task.FromResult(SearchResult);
            }

            public Task<StockUiPeopleResult> LinkshellMembersAsync(int slot)
            {
                LinkshellSlotAsked = slot;
                return Task.FromResult(SearchResult);
            }

            public List<StockUiLinkshellItem> Linkshells { get; } = new();
            public List<(StockUiLinkshellItem Item, StockUiSearchAction Action, int Slot)> LinkshellRuns { get; } = new();
            public uint? LinkshellIdAsked { get; private set; }
            public int? LinkshellSlotAsked { get; private set; }

            public Task<StockUiPeopleResult> LinkshellMembersByIdAsync(uint groupId)
            {
                LinkshellIdAsked = groupId;
                return Task.FromResult(SearchResult);
            }

            public IReadOnlyList<StockUiLinkshellItem> LinkshellItems() => Linkshells;

            public Task<string?> LinkshellAsync(StockUiLinkshellItem item, StockUiSearchAction action, int slot = 0)
            {
                lock (LinkshellRuns) LinkshellRuns.Add((item, action, slot));
                return Task.FromResult<string?>(null);
            }

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

            public List<(string Title, StockUiSocialCommand Command, bool IncludeSelf)> Picks { get; } = new();

            public string? PickPartyMember(string title, StockUiSocialCommand command, bool includeSelf)
            {
                Picks.Add((title, command, includeSelf));
                return null;
            }

            public Dictionary<uint, string> Comments { get; } = new();

            public Task<string?> SearchCommentAsync(StockUiPerson person) =>
                Task.FromResult(Comments.TryGetValue(person.CharacterId, out var comment) ? comment : null);
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
        public async Task SeekParty_TogglesInPlace_AndTheLabelReadsPlaySolo()
        {
            var social = new FakeSocial();
            var menus = Controller(new StockUiPartyContext(false, false, 0, false), social);
            var party = OpenParty(menus);
            Assert.Equal(StockUiParty.NotInPartyMenu, party.Name);
            Assert.Empty(party.LabelImages);
            Choose(menus, StockUiParty.SeekPartyButton);
            await Until(() => social.Runs.Count == 1);
            Assert.Equal((StockUiSocialCommand.SetSeekParty, 1), (social.Runs[0].Command, social.Runs[0].Argument));
            Assert.Same(party, menus.Top); // the list stays open
            Assert.Equal(StockUiParty.PlaySoloImage, party.LabelImages[StockUiParty.SeekPartyButton].ImageIndex);

            // Before the server's copy of the flags arrives, the next toggle turns it back off.
            menus.Activate();
            await Until(() => social.Runs.Count == 2);
            Assert.Equal((StockUiSocialCommand.SetSeekParty, 0), (social.Runs[1].Command, social.Runs[1].Argument));
            Assert.False(party.LabelImages.ContainsKey(StockUiParty.SeekPartyButton));

            // Seeking, the list is still prty1, with Languages and Alarm Type.
            social.State = social.State with { SeekingParty = true };
            menus = Controller(new StockUiPartyContext(false, false, 0, false, SeekingParty: true), social);
            party = OpenParty(menus);
            Assert.Equal(StockUiParty.NotInPartyMenu, party.Name);
            Assert.Equal(StockUiParty.PlaySoloImage, party.LabelImages[StockUiParty.SeekPartyButton].ImageIndex);
        }

        [Theory]
        [InlineData(StockUiParty.AutogroupButton, StockUiSocialCommand.SetAutogroup, StockUiParty.AutogroupSoloImage)]
        [InlineData(StockUiParty.UngroupedRequestButton, StockUiSocialCommand.SetRecruit, StockUiParty.StopRequestImage)]
        public async Task AutogroupAndRequest_ToggleTheirOwnFlagAndLabel(int button, StockUiSocialCommand command, int onImage)
        {
            var social = new FakeSocial();
            var menus = Controller(new StockUiPartyContext(false, false, 0, false), social);
            var party = OpenParty(menus);
            Choose(menus, button);
            await Until(() => social.Runs.Count == 1);
            Assert.Equal((command, 1), (social.Runs[0].Command, social.Runs[0].Argument));
            Assert.Same(party, menus.Top);
            Assert.Equal(new[] { button }, party.LabelImages.Keys); // only that entry changes
            Assert.Equal(onImage, party.LabelImages[button].ImageIndex);

            social.State = new StockUiSocialState(false, true, true, 0); // the server agrees; on -> off next
            menus.Activate();
            await Until(() => social.Runs.Count == 2);
            Assert.Equal((command, 0), (social.Runs[1].Command, social.Runs[1].Argument));
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
            Assert.Equal("Party", list.HelpTitle);
            Assert.Equal(0, list.PeopleOffset); // no "Search Results" bar
            Assert.Equal(new[] { "Cybin", "Zeid" }, list.Rows.Select(r => r.Text));
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
            Assert.Equal(new[] { StockUiPeople.ResultsHeader, "Ayame" }, menus.Top!.Rows.Select(r => r.Text));
            Assert.Equal("[1]", menus.Top.Rows[0].Value);
            menus.Move(InputAction.MenuDown);
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
        [InlineData(StockUiParty.ChangeLeaderButton, StockUiSocialCommand.PassLeader, true)]
        [InlineData(StockUiParty.RemoveMemberButton, StockUiSocialCommand.Kick, true)]
        [InlineData(StockUiParty.LeaderFocusButton, StockUiSocialCommand.Focus, true)]
        public void LeaderPicks_UseTheSubTargetCursor_WithoutAsking(int button, StockUiSocialCommand command, bool includeSelf)
        {
            var social = new FakeSocial();
            var menus = Controller(Leader, social);
            var party = OpenParty(menus);
            Choose(menus, button);
            Assert.Equal(command, social.Picks.Single().Command);
            Assert.Equal(includeSelf, social.Picks.Single().IncludeSelf);
            Assert.Same(party, menus.Top); // no list, no prompt
        }

        [Fact]
        public async Task LevelSync_ListsTheParty_AndSyncsWithoutAsking()
        {
            var social = new FakeSocial();
            social.Members.Add(new StockUiPerson("Zeid", 0x200));
            var menus = Controller(Leader, social);
            OpenParty(menus);
            Choose(menus, StockUiParty.LevelSyncButton);
            await Until(() => menus.Top?.ListKind == StockUiListKind.People);
            var list = menus.Top!;
            Assert.Equal(StockUiPeople.LevelSyncMenu, list.Name);
            Assert.Equal(new[] { "Cybin", "Zeid" }, list.Rows.Select(r => r.Text)); // the leader may sync to themself
            menus.Move(InputAction.MenuDown);
            menus.Activate();
            await Until(() => social.Runs.Count == 1);
            Assert.Equal((StockUiSocialCommand.LevelSync, "Zeid"), (social.Runs[0].Command, social.Runs[0].Person.Name));
        }

        [Fact]
        public async Task LevelSync_UnderSync_ReadsLvlSyncOff_AndEndsIt()
        {
            var social = new FakeSocial { State = new StockUiSocialState(false, false, false, 0, LevelSynced: true) };
            var menus = Controller(Leader, social);
            var party = OpenParty(menus);
            Assert.Equal(StockUiParty.LevelSyncOffImage, party.LabelImages[StockUiParty.LevelSyncButton].ImageIndex);
            Choose(menus, StockUiParty.LevelSyncButton);
            await Until(() => social.Runs.Count == 1);
            Assert.Equal(StockUiSocialCommand.DisableLevelSync, social.Runs[0].Command);
        }

        [Fact]
        public async Task FindMember_PrintsTheSeekersComment()
        {
            var notices = new List<string>();
            var social = new FakeSocial { SearchResult = new StockUiPeopleResult(new[] { new StockUiPerson("Ayame", CharacterId: 0x1234) { HasComment = true } }) };
            social.Comments[0x1234] = "LFP WAR75";
            var menus = Controller(new StockUiPartyContext(false, false, 0, false), social, notices);
            OpenParty(menus);
            Choose(menus, StockUiParty.UngroupedFindMemberButton);
            await Until(() => menus.Top?.ListKind == StockUiListKind.People);
            var list = menus.Top!;
            menus.Move(InputAction.MenuDown);
            menus.Activate();
            Assert.Equal(StockUiPeople.OptionsMenu, menus.Top!.Name);
            await Until(() => notices.Contains("Ayame: LFP WAR75"));

            // The "Search Results" bar goes back to the Party list.
            menus.CloseTop();
            menus.Move(InputAction.MenuUp);
            Assert.Same(list, menus.Top);
            menus.Activate();
            Assert.Equal(StockUiParty.NotInPartyMenu, menus.Top!.Name);
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
            Assert.Equal((StockUiSocialCommand.SetQuartermaster, true), (social.Picks.Single().Command, social.Picks.Single().IncludeSelf)); // the leader may collect too
            await Task.CompletedTask;

            var notices = new List<string>();
            social.State = social.State with { QuartermasterName = "Zeid" };
            menus = Controller(new StockUiPartyContext(true, false, 1, false), social, notices);
            OpenParty(menus);
            Choose(menus, StockUiParty.MemberDistributionButton);
            Assert.Equal("Treasure distribution: Zeid collects all treasure.", notices.Single());
        }

        [Fact]
        public void Alliance_OnlyTheAllianceLeaderFormsOrDissolves()
        {
            var menus = Controller(new StockUiPartyContext(true, true, 2, true, IsAllianceLeader: false), new FakeSocial());
            OpenParty(menus);
            Choose(menus, StockUiParty.AllianceButton);
            Assert.True(menus.Top!.IsGreyed(StockUiParty.AllianceFormButton));
            Assert.True(menus.Top.IsGreyed(StockUiParty.AllianceDissolveButton));
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

            menus = Controller(new StockUiPartyContext(true, true, 2, true, IsAllianceLeader: true), social);
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
        public void SearchRows_TakeRetailsColumns()
        {
            Assert.Equal("[MNK99/RDM49]Elv", StockUiPeople.JobsColumn(2, 99, 5, 49, 3, null));
            Assert.Equal("[WAR75]Gal", StockUiPeople.JobsColumn(1, 75, 13, 0, 8, null));
            Assert.Equal("E F", StockUiPeople.LanguageLetters(0x02 | 0x08));
            Assert.Null(StockUiPeople.LanguageLetters(0));

            var entity = new SearchEntity("Gordian", 243, 0, 2, 5, 99, 49, 3, 3, (uint)(SearchFlags.SeekingParty | SearchFlags.HasComment), 0, 7, 0x11, 0x0A, 0, 0, 0, 0);
            var row = StockUiPeople.FromSearch(entity, null, z => z == 243 ? "Ru'Lude Gardens" : "Port Jeuno", currentZone: 243);
            Assert.Equal(("[MNK99/RDM49]Elv", (byte)3, (byte)0, "E F", true), (row.Jobs, row.MissionRank, row.Nation, row.Languages, row.HasComment));
            Assert.Null(row.Area); // your own area is left blank
            Assert.Equal("Port Jeuno", StockUiPeople.FromSearch(entity with { ZoneId = 246 }, null, z => "Port Jeuno", currentZone: 243).Area);

            var anonymous = StockUiPeople.FromSearch(entity with { Flags1 = (uint)SearchFlags.Anonymous }, null, null, 243);
            Assert.Equal((null, (byte)0), (anonymous.Jobs, anonymous.MissionRank));
        }

        [Fact]
        public void PartyActionArgument_RoundTrips()
        {
            Assert.Equal((StockUiPartyAction.Language, 0x10), StockUiParty.ActionOf(StockUiParty.Action(StockUiPartyAction.Language, 0x10)));
            Assert.Equal((StockUiPartyAction.MemberList, 0), StockUiParty.ActionOf(StockUiParty.Action(StockUiPartyAction.MemberList)));
        }
    }
}
