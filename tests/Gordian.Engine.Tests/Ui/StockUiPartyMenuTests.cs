using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Engine.Input;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Engine.Tests.Ui
{
    /// <summary>The main menu's Party entry (#397): which of the three DAT lists opens, what is greyed and Disband.</summary>
    public class StockUiPartyMenuTests
    {
        private static UiMenuDefinition List(string name, short x, short y, UiAnchor anchor, params int[] buttonIds)
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

        private static StockUiMenuController Controller(StockUiPartyContext party)
        {
            var library = UiResourceLibrary.FromDefinitions(new[]
            {
                List(StockUiMenuEntries.MainMenu, 384, 48, UiAnchor.TopRight, 6),
                List(StockUiMenuEntries.YesNoMenu, 16, 256, UiAnchor.BottomLeft, 1, 2),
                List(StockUiMenuEntries.MessageYesNoMenu, 130, 208, UiAnchor.BottomLeft, 1, 2),
                List(StockUiParty.NotInPartyMenu, 384, 48, UiAnchor.TopRight, 1, 2, 5, 6, 3, 4, 7),
                List(StockUiParty.MemberMenu, 384, 48, UiAnchor.TopRight, 1, 6, 7, 4, 5, 2, 3),
                List(StockUiParty.LeaderMenu, 384, 48, UiAnchor.TopRight, 1, 2, 11, 12, 3, 4, 9, 10, 5, 6, 7, 8),
            });
            return new StockUiMenuController { Library = library, PartyContext = () => party };
        }

        private static StockUiOpenMenu OpenParty(StockUiMenuController menus)
        {
            Assert.True(menus.OpenMainMenu());
            menus.Activate(); // Party
            return menus.Top!;
        }

        [Theory]
        [InlineData(false, false, StockUiParty.NotInPartyMenu)]
        [InlineData(true, false, StockUiParty.MemberMenu)]
        [InlineData(true, true, StockUiParty.LeaderMenu)]
        public void Party_OpensTheListForTheCharactersState(bool inParty, bool leader, string expected)
        {
            var menus = Controller(new StockUiPartyContext(inParty, leader, inParty ? 2 : 0, false));

            Assert.Equal(expected, OpenParty(menus).Name);
        }

        [Fact]
        public void PartyOfOne_IsStillAPartyWithTheLeadersList()
        {
            // The leader left a party of two: the remaining member leads a party of one.
            var state = new PartyState();
            state.UpsertMember(new PartyMember { ServerId = 0x100, Name = "Cybin", IsLeader = true, MemberNumber = 0 });
            Assert.True(state.IsInParty);
            Assert.True(state.IsLeaderOf(0x100));

            var menus = Controller(new StockUiPartyContext(state.IsInParty, state.IsLeaderOf(0x100), 0, false));
            Assert.Equal(StockUiParty.LeaderMenu, OpenParty(menus).Name);

            state.ClearParty();
            Assert.False(state.IsInParty);
        }

        [Fact]
        public void LeaderList_GreysAllianceChgLeaderAndRmvMemberWhileAlone()
        {
            var alone = OpenParty(Controller(new StockUiPartyContext(true, true, 0, false)));
            Assert.True(alone.IsGreyed(StockUiParty.AllianceButton));
            Assert.True(alone.IsGreyed(StockUiParty.ChangeLeaderButton));
            Assert.True(alone.IsGreyed(StockUiParty.RemoveMemberButton));
            Assert.False(alone.IsGreyed(StockUiParty.LeaderDisbandButton));

            var grouped = OpenParty(Controller(new StockUiPartyContext(true, true, 2, false)));
            Assert.False(grouped.IsGreyed(StockUiParty.AllianceButton));
            Assert.False(grouped.IsGreyed(StockUiParty.ChangeLeaderButton));

            var allied = OpenParty(Controller(new StockUiPartyContext(true, true, 2, true)));
            Assert.True(allied.IsGreyed(StockUiParty.AllianceButton));
        }

        [Fact]
        public void EveryPartyButtonHasAnEntry()
        {
            foreach (var (menu, count) in new[] { (StockUiParty.NotInPartyMenu, 7), (StockUiParty.MemberMenu, 7), (StockUiParty.LeaderMenu, 12) })
            {
                int found = Enumerable.Range(1, 12).Count(id => StockUiMenuEntries.TryGet(menu, id, out _));
                Assert.Equal(count, found);
            }
        }

        [Theory]
        [InlineData(true, StockUiParty.LeaderDisbandButton, StockUiMenuController.DisbandPartyPrompt)]
        [InlineData(false, StockUiParty.MemberDisbandButton, StockUiMenuController.LeavePartyPrompt)]
        public async Task Disband_AsksFirst_ThenBreaksUpOrLeaves(bool leader, int button, string prompt)
        {
            var menus = Controller(new StockUiPartyContext(true, leader, 1, false));
            bool? sent = null;
            menus.PartyDisbandRequested = isLeader =>
            {
                sent = isLeader;
                return Task.CompletedTask;
            };
            var list = OpenParty(menus);
            while (list.SelectedButtonId != button) menus.Move(InputAction.MenuDown);
            menus.Activate();
            Assert.True(menus.Top!.IsPrompt);
            Assert.Equal(prompt, menus.Top.Message);
            Assert.Null(sent);

            // No (the default) sends nothing; Yes sends.
            menus.CloseTop();
            await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Null(sent);

            menus.Activate();
            menus.Move(InputAction.MenuUp); // Yes
            menus.Activate();
            await Task.Delay(50, TestContext.Current.CancellationToken);
            Assert.Equal(leader, sent);
            Assert.False(menus.IsOpen);
        }

        [Fact]
        public void PartyTitles_NameTheHelpBar()
        {
            Assert.Equal("Party Leader", StockUiParty.TitleOf(StockUiParty.LeaderMenu));
            Assert.Equal("Party", StockUiParty.TitleOf(StockUiParty.MemberMenu));
            Assert.Equal("Party", StockUiParty.TitleOf(StockUiParty.NotInPartyMenu));
            Assert.Null(StockUiParty.TitleOf(StockUiMenuEntries.MainMenu));
        }

        [Fact]
        public void InvitePendingFlag_FollowsTheInviteAndItsClearing()
        {
            var state = new PartyState();
            Assert.False(state.HasPendingInvite);
            state.SetPendingInvite(new PartyInvite(1, 2, "Zeid", PartyKind.Party, System.DateTime.UtcNow));
            Assert.True(state.HasPendingInvite);
            state.ClearPendingInvite();
            Assert.False(state.HasPendingInvite);
        }

        [Fact]
        public void Leadership_FollowsTheLeaderFlag_NotTheSlot()
        {
            var state = new PartyState();
            state.UpsertMember(new PartyMember { ServerId = 0x100, Name = "Cybin", IsLeader = true, MemberNumber = 0 });
            state.UpsertMember(new PartyMember { ServerId = 0x200, Name = "Zeid", MemberNumber = 1 });
            Assert.True(state.IsLeaderOf(0x100));
            Assert.False(state.IsLeaderOf(0x200));

            // Leadership passes to the member in slot 1 (the old leader's packet need not follow).
            state.UpsertMember(new PartyMember { ServerId = 0x200, Name = "Zeid", IsLeader = true, MemberNumber = 1 });
            Assert.True(state.IsLeaderOf(0x200));
            Assert.False(state.IsLeaderOf(0x100));
            Assert.False(state.IsLeaderOf(0x300)); // not a member
        }

        [Fact]
        public void ReleasingEveryTrust_ReturnsToSolo_ButAPlayerPartyOfOneStays()
        {
            const uint self = 0x100, trustA = 0x01004802, trustB = 0x01004803;
            var state = new PartyState();
            state.UpsertMember(new PartyMember { ServerId = self, Name = "Cybin", IsLeader = true });
            state.UpsertMember(new PartyMember { ServerId = trustA, Name = "Shantotto", MemberNumber = 1 });
            state.UpsertMember(new PartyMember { ServerId = trustB, Name = "Ajido", MemberNumber = 2 });
            int disbanded = 0;
            state.PartyDisbanded += () => disbanded++;

            state.RetainMembers(new uint[] { self, trustB }); // one Trust released
            Assert.True(state.IsInParty);
            state.RetainMembers(new uint[] { self }); // the last one
            Assert.False(state.IsInParty);
            Assert.Equal(1, disbanded);

            // A party of players left with one member is still a party.
            state.UpsertMember(new PartyMember { ServerId = self, Name = "Cybin", IsLeader = true });
            state.UpsertMember(new PartyMember { ServerId = 0x200, Name = "Zeid", MemberNumber = 1 });
            state.RemoveMember(0x200);
            Assert.True(state.IsInParty);
        }
    }
}
