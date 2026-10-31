// tests/Gordian.Engine.Tests/Ui/QuestLogTests.cs
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Ui;
using Gordian.Core.World;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Ui
{
    /// <summary>The Missions, Quests and Region Info windows (#488).</summary>
    public class QuestLogTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        #region Test data

        private static StockUiQuestLogEntry E(int id, string name, string description = "") => StockUiQuestLogCatalog.Entry(id, name, description);

        private static StockUiQuestLogCatalog Catalog() => StockUiQuestLogCatalog.FromEntries(new Dictionary<StockUiQuestLogBook, IReadOnlyList<StockUiQuestLogEntry>>
        {
            [StockUiQuestLogBook.QuestsSandoria] = new[] { E(0, "A Sentry's Peril", "Client: Glenne"), E(1, "Waters of the Cheval"), E(3, "+Eco-Warrior") },
            [StockUiQuestLogBook.QuestsAhtUrhgan] = new[] { E(0, "Keeping Notes"), E(127, "Last Quest"), E(130, "Not a Quest") },
            [StockUiQuestLogBook.MissionsSandoria] = new[] { E(0, "Smash the Orcish Scouts"), E(1, "Bat Hunt"), E(2, "Save the Children", "Mission Orders: Rescue them.") },
            [StockUiQuestLogBook.MissionsBastok] = new[] { E(0, "The Zeruhn Report") },
            [StockUiQuestLogBook.MissionsZilart] = new[] { E(0, "The New Frontier"), E(2, "The Outlands"), E(4, "Welcome t'Norg"), E(1000, "To Commence RotZ") },
            [StockUiQuestLogBook.MissionsCop] = new[]
            {
                E(101, "Ancient Flames Beckon"), E(110, "__The Rites of Life"), E(118, "__Below the Arks"),
                E(137, "The Isle of Forgotten Saints"), E(138, "__An Invitation West"), E(1000, "To Begin CoP..."),
            },
            [StockUiQuestLogBook.MissionsAcp] = new[] { E(0, "A Crystalline Prophecy"), E(1, "The Echo Awakens"), E(2, "Gatherer of Light (I)") },
            [StockUiQuestLogBook.MissionsSoa] = new[] { E(110, "The Sacred City of Adoulin"), E(110, "__Rumors from the West"), E(112, "__The Geomagnetron"), E(999, "__fin") },
            [StockUiQuestLogBook.MissionsTvr] = new[] { E(0, "The Voracious Resurgence"), E(110, "__The Gloom Phantom's Approach"), E(118, "__The Brygid Cup") },
            [StockUiQuestLogBook.MissionsToau] = new[] { E(0, "Land of Sacred Serpents"), E(1, "Immortal Sentries") },
            [StockUiQuestLogBook.MissionsAssault] = new[] { E(1, "Leujaoam Cleansing"), E(2, "Orichalcum Survey") },
        });

        private static StockUiQuestLogState State(int nation = 0)
        {
            var state = new StockUiQuestLogState { Nation = nation, NationMission = 2, Zilart = 4, Promathia = 118 };
            // San d'Oria quests: 0 offered, 1 complete, 3 offered and complete (counts as completed).
            state.QuestOffer[0] = 0b1001;
            state.QuestComplete[0] = 0b1010;
            // San d'Oria missions 0 and 1 done; Zilart 0 and 2 done.
            state.MissionComplete[0] = 0b11;
            state.MissionComplete[6] = 0b101;
            return state;
        }

        private static UiMenuDefinition Window(string name, short x, short y, int buttons = 0, short width = 366, short height = 248)
        {
            var list = new List<UiMenuButton>();
            for (int i = 1; i <= buttons; i++)
            {
                list.Add(new UiMenuButton
                {
                    ButtonId = (short)i, X = 16, Y = (short)(6 + 16 * (i - 1)), Width = 88, Height = 16,
                    NavUp = (sbyte)(i == 1 ? buttons : i - 1), NavDown = (sbyte)(i == buttons ? 1 : i + 1), NavLeft = (sbyte)i, NavRight = (sbyte)i,
                });
            }
            return new UiMenuDefinition { Name = name, Frame = new UiMenuFrame { X = x, Y = y, Width = width, Height = height }, Buttons = list };
        }

        private static UiResourceLibrary Library() => UiResourceLibrary.FromDefinitions(new[]
        {
            Window(StockUiMenuEntries.MainMenu, 384, 48, 12, 112, 202),
            Window(StockUiMenuEntries.MainMenuPage2, 384, 48, 12, 112, 202),
            Window(StockUiQuestLog.GroupMenu, 384, 48, 7, 112, 122),
            Window(StockUiQuestLog.StorylineMenu, 16, 48),
            Window(StockUiQuestLog.MissionListMenu, 16, 48),
            Window(StockUiQuestLog.AreaMenu, 16, 48),
            Window(StockUiQuestLog.QuestKindMenu, 16, 48),
            Window(StockUiQuestLog.QuestListMenu, 16, 48),
            Window(StockUiConquest.RegionMenu, 384, 48, 4, 112, 74),
            Window(StockUiConquest.ControlWindow, 384, 48, 0, 112, 206),
            Window(StockUiConquest.BalanceWindow, 384, 48, 0, 112, 72),
        });

        private sealed class Harness
        {
            public readonly StockUiMenuController Menus = new() { Library = Library() };
            public StockUiQuestLogState State = QuestLogTests.State();
            public StockUiConquestView? Conquest;
            public readonly List<string> Notices = new();

            public Harness()
            {
                var catalog = Catalog();
                Menus.QuestLogCatalog = () => catalog;
                Menus.QuestLogState = () => State;
                Menus.ConquestView = () => Conquest;
                Menus.NoticePosted += Notices.Add;
            }

            public StockUiOpenMenu Choose(int button)
            {
                Menus.Top!.SelectedButtonId = button;
                Menus.Activate();
                return Menus.Top!;
            }
        }

        #endregion

        #region Rows

        [Fact]
        public void Entry_StripsTheTableMarks()
        {
            var entry = E(3, "+Eco-Warrior");
            Assert.Equal(("Eco-Warrior", true, 0), (entry.Name, entry.Repeatable, entry.Depth));
            var nested = E(331, "______Vicissitudes");
            Assert.Equal(("Vicissitudes", false, 3), (nested.Name, nested.Repeatable, nested.Depth));
        }

        [Fact]
        public void QuestRows_CurrentAreOfferedNotDone_CompletedAreDone()
        {
            var catalog = Catalog();
            var state = State();
            Assert.Equal(new[] { "A Sentry's Peril" }, StockUiQuestLog.QuestRows(catalog, state, StockUiQuestLogBook.QuestsSandoria, false).Select(r => r.Entry.Name));
            Assert.Equal(new[] { "Waters of the Cheval", "Eco-Warrior" },
                StockUiQuestLog.QuestRows(catalog, state, StockUiQuestLogBook.QuestsSandoria, true).Select(r => r.Entry.Name));

            // The Aht Urhgan block holds quests in its first 128 bits; words 4-7 are the missions LandSandBoat puts there.
            int word = (int)QuestLogArea.AhtUrhgan * 8;
            state.QuestOffer[word + 3] = 1u << 31; // quest 127
            state.QuestOffer[word + 4] = 1u << 2;  // the current Assault mission number, not quest 130
            Assert.Equal(new[] { "Last Quest" }, StockUiQuestLog.QuestRows(catalog, state, StockUiQuestLogBook.QuestsAhtUrhgan, false).Select(r => r.Entry.Name));
        }

        [Fact]
        public void MissionRows_Nation_DoneByBit_CurrentById_OtherNationsNotAllowed()
        {
            var catalog = Catalog();
            var state = State();
            var rows = StockUiQuestLog.MissionRows(catalog, state, StockUiQuestLogBook.MissionsSandoria);
            Assert.Equal(new[] { ("Smash the Orcish Scouts", StockUiQuestLogStatus.Completed), ("Bat Hunt", StockUiQuestLogStatus.Completed), ("Save the Children", StockUiQuestLogStatus.Current) },
                rows.Select(r => (r.Entry.Name, r.Status)));
            Assert.Empty(StockUiQuestLog.MissionRows(catalog, state, StockUiQuestLogBook.MissionsBastok));
            Assert.Equal(StockUiQuestLog.NotAllowedText, StockUiQuestLog.EmptyMissionText(state, StockUiQuestLogBook.MissionsBastok));
            Assert.Equal(StockUiQuestLog.NoMissionsText, StockUiQuestLog.EmptyMissionText(state, StockUiQuestLogBook.MissionsSandoria));
        }

        [Fact]
        public void MissionRows_Zilart_FromItsBits_AndTalesBeginningShowsItsRowAlone()
        {
            var catalog = Catalog();
            var state = State();
            Assert.Equal(new[] { ("The New Frontier", StockUiQuestLogStatus.Completed), ("The Outlands", StockUiQuestLogStatus.Completed), ("Welcome t'Norg", StockUiQuestLogStatus.Current) },
                StockUiQuestLog.MissionRows(catalog, state, StockUiQuestLogBook.MissionsZilart).Select(r => (r.Entry.Name, r.Status)));
            var declined = new StockUiQuestLogState { TalesBeginning = 0x01, Zilart = 0xFFFF };
            Assert.Equal(new[] { "To Commence RotZ" }, StockUiQuestLog.MissionRows(catalog, declined, StockUiQuestLogBook.MissionsZilart).Select(r => r.Entry.Name));
            // No mission (0xFFFF) and none done: nothing.
            Assert.Empty(StockUiQuestLog.MissionRows(catalog, new StockUiQuestLogState(), StockUiQuestLogBook.MissionsZilart));
        }

        [Fact]
        public void MissionRows_Promathia_CountsBelowTheCurrentNumber_WithChapterHeadings()
        {
            var rows = StockUiQuestLog.MissionRows(Catalog(), State(), StockUiQuestLogBook.MissionsCop);
            Assert.Equal(new[]
            {
                ("Ancient Flames Beckon", StockUiQuestLogStatus.Heading, 0),
                ("The Rites of Life", StockUiQuestLogStatus.Completed, 1),
                ("Below the Arks", StockUiQuestLogStatus.Current, 1),
            }, rows.Select(r => (r.Entry.Name, r.Status, r.Entry.Depth)));
            Assert.Empty(StockUiQuestLog.MissionRows(Catalog(), new StockUiQuestLogState(), StockUiQuestLogBook.MissionsCop));
        }

        [Fact]
        public void MissionRows_AddOns_SoA_AndTvr()
        {
            var catalog = Catalog();
            // A Crystalline Prophecy is the low nibble: 2 = the third mission in progress.
            var acp = StockUiQuestLog.MissionRows(catalog, new StockUiQuestLogState { Addons = 0x102 }, StockUiQuestLogBook.MissionsAcp);
            Assert.Equal(new[] { StockUiQuestLogStatus.Completed, StockUiQuestLogStatus.Completed, StockUiQuestLogStatus.Current }, acp.Select(r => r.Status));
            // Seekers of Adoulin at 999: the story is done, every mission completed and "fin" left out.
            var soa = StockUiQuestLog.MissionRows(catalog, new StockUiQuestLogState { Adoulin = 999 }, StockUiQuestLogBook.MissionsSoa);
            Assert.Equal(new[] { ("The Sacred City of Adoulin", StockUiQuestLogStatus.Heading), ("Rumors from the West", StockUiQuestLogStatus.Completed), ("The Geomagnetron", StockUiQuestLogStatus.Completed) },
                soa.Select(r => (r.Entry.Name, r.Status)));
            // The Voracious Resurgence's bit 31: done with none in progress.
            var tvr = StockUiQuestLog.MissionRows(catalog, new StockUiQuestLogState { Voracious = 0x80000000u | 118 }, StockUiQuestLogBook.MissionsTvr);
            Assert.DoesNotContain(tvr, r => r.Status == StockUiQuestLogStatus.Current);
            Assert.Equal(3, tvr.Count);
        }

        [Fact]
        public void MissionRows_ToauAndAssault_FromTheAhtUrhganWords()
        {
            var catalog = Catalog();
            var state = new StockUiQuestLogState();
            int word = (int)QuestLogArea.AhtUrhgan * 8;
            state.MissionComplete[8] = 1;      // Treasures of Aht Urhgan 0 done
            state.QuestOffer[word + 5] = 1;    // ... 1 in progress
            state.QuestComplete[word + 4] = 0b10; // Assault 1 done
            state.QuestOffer[word + 4] = 2;    // Assault 2 in progress
            Assert.Equal(new[] { StockUiQuestLogStatus.Completed, StockUiQuestLogStatus.Current },
                StockUiQuestLog.MissionRows(catalog, state, StockUiQuestLogBook.MissionsToau).Select(r => r.Status));
            Assert.Equal(new[] { ("Leujaoam Cleansing", StockUiQuestLogStatus.Completed), ("Orichalcum Survey", StockUiQuestLogStatus.Current) },
                StockUiQuestLog.MissionRows(catalog, state, StockUiQuestLogBook.MissionsAssault).Select(r => (r.Entry.Name, r.Status)));
        }

        [Fact]
        public void State_FromProgression_CopiesTheMissionPackets()
        {
            var progression = new ProgressionState();
            var main = new byte[36];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(main.AsSpan(0), 1);     // Bastok
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(main.AsSpan(4), 5);     // its mission 5
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(main.AsSpan(12), 118);  // CoP
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(main.AsSpan(20), 0x321); // add-ons
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(main.AsSpan(32), MissionPorts.Main);
            progression.UpdateMissions(new S2C_0x056_Mission(main));
            var quests = new byte[36];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(quests.AsSpan(0), 0b101);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(quests.AsSpan(32), 0x0098); // Bastok completed quests
            progression.UpdateMissions(new S2C_0x056_Mission(quests));

            var state = StockUiQuestLogState.From(progression);
            Assert.Equal((1, 5u, 118u, 0x321u), (state.Nation, state.NationMission, state.Promathia, state.Addons));
            Assert.Equal(0b101u, state.QuestComplete[(int)QuestLogArea.Bastok * 8]);
        }

        #endregion

        #region Windows

        [Fact]
        public void Quests_AreasThenCurrentOrCompletedThenTheSummary()
        {
            var h = new Harness();
            Assert.True(h.Menus.Open(StockUiMenuEntries.MainMenuPage2));
            var areas = h.Choose(2);
            Assert.Equal(StockUiQuestLogView.Areas, areas.QuestLogView);
            Assert.Equal(11, areas.Rows.Count);
            Assert.Equal("San d'Oria", areas.Rows[0].Text);
            Assert.Equal("Coalition", areas.Rows[^1].Text);
            Assert.Equal(StockUiQuestLog.PickerRows, areas.VisibleRows);
            Assert.True(areas.CanScroll);
            Assert.Equal((39, 40 + 25), (areas.Menu.FindButton(2)!.X, areas.Menu.FindButton(2)!.Y));

            var kind = h.Choose(1);
            Assert.Equal(StockUiQuestLogView.QuestKind, kind.QuestLogView);
            Assert.Equal(new[] { "Current Quests", "Completed Quests" }, kind.Rows.Select(r => r.Text));

            var completed = h.Choose(2);
            Assert.Equal(StockUiQuestLogView.List, completed.QuestLogView);
            Assert.Equal("San d'Oria: Completed Quests", completed.QuestLogHeading);
            Assert.Equal(new[] { "Waters of the Cheval", "Eco-Warrior" }, completed.Rows.Select(r => r.Text));
            Assert.Equal("Quests", completed.QuestLogTitle);

            var page = h.Choose(1);
            Assert.Equal(StockUiQuestLogView.Description, page.QuestLogView);
            Assert.Equal("Waters of the Cheval", page.QuestLogShown!.Value.Entry.Name);
            Assert.Empty(page.Menu.Buttons);

            // Bastok has no quests: "No current quests." (ROM/165/72 #99).
            h.Menus.CloseTop();
            h.Menus.CloseTop();
            h.Menus.CloseTop();
            areas.SelectedButtonId = 2;
            h.Menus.Activate();
            var empty = h.Choose(1);
            Assert.Empty(empty.Rows);
            Assert.Equal(StockUiQuestLog.NoCurrentQuestsFallback, empty.EmptyText);
        }

        [Fact]
        public void Missions_NationsPickTheStoryline_TheListOpensOnTheMissionInProgress()
        {
            var h = new Harness();
            Assert.True(h.Menus.Open(StockUiMenuEntries.MainMenuPage2));
            var groups = h.Choose(1);
            Assert.Equal(StockUiQuestLog.GroupMenu, groups.Name);

            var nations = h.Choose(1);
            Assert.Equal(StockUiQuestLogView.Storylines, nations.QuestLogView);
            Assert.Equal(new[] { "San d'Oria", "Bastok", "Windurst" }, nations.Rows.Select(r => r.Text));
            Assert.Equal(1, nations.SelectedButtonId); // the home nation, San d'Oria
            Assert.Equal(3, nations.Menu.Buttons.Count);

            var list = h.Choose(1);
            Assert.Equal(StockUiQuestLog.MissionListMenu, list.Name);
            Assert.Equal(3, list.Rows.Count);
            Assert.True(list.Rows[2].Marked);   // the mission in progress
            Assert.Equal(3, list.SelectedButtonId);
            Assert.Equal("San d'Oria", list.QuestLogHeading);
            Assert.Equal((12, 4 + 16), (list.Menu.FindButton(2)!.X, list.Menu.FindButton(2)!.Y));

            var orders = h.Choose(3);
            Assert.Equal("Mission Orders: Rescue them.", orders.QuestLogShown!.Value.Entry.Description);

            // Another nation: "Not allowed under current allegiance."
            h.Menus.CloseTop();
            h.Menus.CloseTop();
            nations.SelectedButtonId = 2;
            h.Menus.Activate();
            Assert.Equal(StockUiQuestLog.NotAllowedText, h.Menus.Top!.EmptyText);

            // Zilart opens its one storyline at once; the list follows a new 0x056.
            h.Menus.CloseTop();
            h.Menus.CloseTop();
            var zilart = h.Choose(2);
            Assert.Equal(StockUiQuestLogView.List, zilart.QuestLogView);
            Assert.Equal(3, zilart.Rows.Count);
            h.State = new StockUiQuestLogState { Zilart = 0xFFFF, TalesBeginning = 1 };
            h.Menus.RefreshQuestLog();
            Assert.Equal(new[] { "To Commence RotZ" }, zilart.Rows.Select(r => r.Text));
        }

        [Fact]
        public void Missions_WithoutTheTables_PostANotice()
        {
            var h = new Harness();
            h.Menus.QuestLogCatalog = () => null;
            Assert.True(h.Menus.Open(StockUiMenuEntries.MainMenuPage2));
            h.Choose(2);
            Assert.Contains(h.Notices, n => n.Contains("quest and mission tables", System.StringComparison.Ordinal));
        }

        [Fact]
        public void RegionInfo_ConquestShowsTheRegionsPage_OrTheBalanceOutsideOne()
        {
            var h = new Harness();
            var progression = new ProgressionState();
            byte[] payload = new byte[176];
            payload[0] = 1 | (3 << 2) | (2 << 4); // San d'Oria 1st, Windurst 2nd, Bastok 3rd
            payload[22 + 3] = 4;                  // Ronfaure held by the beastmen
            payload[130] = 30; payload[131] = 20; payload[132] = 10; payload[144] = 40;
            payload[136] = 3;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(140), 1500);
            progression.UpdateConquest(new S2C_0x05E_Conquest(payload));
            h.Conquest = StockUiConquest.Build(progression, 4, "Ronfaure");

            Assert.True(h.Menus.Open(StockUiMenuEntries.MainMenu));
            var region = h.Choose(11);
            Assert.Equal(StockUiConquest.RegionMenu, region.Name);
            var page = h.Choose(StockUiConquest.ConquestButton);
            Assert.Equal(StockUiQuestLogView.Conquest, page.QuestLogView);
            Assert.Equal(StockUiConquest.ControlWindow, page.Name);
            var view = page.ConquestView!;
            Assert.Equal(StockUiConquest.BeastmenRegionFallback, view.Control);
            Assert.Equal(3, view.NextTally);
            Assert.Equal(new[] { 30, 20, 10, 40 }, view.Influence.Select(b => b.Percent));
            Assert.Equal(1500u, view.ConquestPoints);
            Assert.Equal("Region Info", page.QuestLogTitle);

            // In a city: the Balance of Power.
            h.Menus.CloseTop();
            h.Conquest = StockUiConquest.Build(progression, 0, "San d'Oria");
            var balance = h.Choose(StockUiConquest.ConquestButton);
            Assert.Equal(StockUiConquest.BalanceWindow, balance.Name);
            Assert.Equal(new[] { "1st: San d'Oria", "2nd: Windurst", "3rd: Bastok" }, balance.ConquestView!.Balance);
            Assert.Equal(StockUiConquest.NeutralAreaFallback, balance.ConquestView.Control);

            // Besieged and the rest are not done yet.
            h.Menus.CloseTop();
            h.Choose(StockUiConquest.BesiegedButton);
            Assert.Contains(h.Notices, n => n.StartsWith("Besieged", System.StringComparison.Ordinal));
        }

        [Fact]
        public void Conquest_OwnersAndTally()
        {
            var progression = new ProgressionState();
            byte[] payload = new byte[176];
            payload[22 + 1 * 4 + 3] = 2; // Zulkheim: Bastok
            progression.UpdateConquest(new S2C_0x05E_Conquest(payload));
            Assert.Equal("Bastok", StockUiConquest.Build(progression, 5, "Zulkheim").Control);
            Assert.Equal(StockUiConquest.NeutralRegionFallback, StockUiConquest.Build(progression, 6, "Norvallen").Control);
            Assert.Equal("Region text", StockUiConquest.Build(progression, 6, "Norvallen", id => id == StockUiConquest.NeutralRegionText ? "Region text" : null).Control);
            Assert.Equal(("1 day", "4 days"), (StockUiConquest.TallyText(1), StockUiConquest.TallyText(4)));
        }

        #endregion

        #region Retail data

        [Fact]
        public void RetailTables_NameEveryBook()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var catalog = StockUiQuestLogCatalog.FromTables(rm.GetDMsgTable);
            foreach (var book in System.Enum.GetValues<StockUiQuestLogBook>()) Assert.NotEmpty(catalog.Entries(book));
            Assert.Equal("A Sentry's Peril", catalog.Entries(StockUiQuestLogBook.QuestsSandoria).Single(e => e.Id == 0).Name);
            Assert.StartsWith("Client:", catalog.Entries(StockUiQuestLogBook.QuestsSandoria)[0].Description);
            Assert.Equal("Rycharde the Chef", catalog.Entries(StockUiQuestLogBook.QuestsOtherAreas).Single(e => e.Id == 0).Name);
            Assert.Equal("The Firebloom Tree", catalog.Entries(StockUiQuestLogBook.QuestsOutlands).Single(e => e.Id == 1).Name);
            Assert.Equal("Keeping Notes", catalog.Entries(StockUiQuestLogBook.QuestsAhtUrhgan).Single(e => e.Id == 0).Name);
            Assert.Equal("Lost in Translocation", catalog.Entries(StockUiQuestLogBook.QuestsCrystalWar).Single(e => e.Id == 0).Name);
            Assert.Equal("Catering Capers", catalog.Entries(StockUiQuestLogBook.QuestsAbyssea).Single(e => e.Id == 0).Name);
            Assert.All(catalog.Entries(StockUiQuestLogBook.QuestsCoalition), e => Assert.True(e.Repeatable));
            Assert.Equal("Smash the Orcish Scouts", catalog.Entries(StockUiQuestLogBook.MissionsSandoria)[0].Name);
            var rites = catalog.Entries(StockUiQuestLogBook.MissionsCop).Single(e => e.Id == 110);
            Assert.Equal(("The Rites of Life", 1), (rites.Name, rites.Depth));
            // The unused Japanese slots are left out ("ZLシナリオ1", "ASミッション00").
            Assert.DoesNotContain(catalog.Entries(StockUiQuestLogBook.MissionsZilart), e => e.Id == 1);
            Assert.DoesNotContain(catalog.Entries(StockUiQuestLogBook.MissionsAssault), e => e.Id == 0);
            Assert.Equal("Leujaoam Cleansing", catalog.Entries(StockUiQuestLogBook.MissionsAssault).Single(e => e.Id == 1).Name);
            Assert.Equal("Cavernous Maws", catalog.Entries(StockUiQuestLogBook.MissionsWotg)[0].Name);
            Assert.Equal("The Echo Awakens", catalog.Entries(StockUiQuestLogBook.MissionsAcp).Single(e => e.Id == 1).Name);
            Assert.Equal("The Voracious Resurgence", catalog.Entries(StockUiQuestLogBook.MissionsTvr)[0].Name);
        }

        [Fact]
        public void RetailMenus_QuestLogWindowsResolve()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            if (library == null) return;

            // missionm: seven groups, Add-ons (6) authored last, under Adoulin (7).
            Assert.True(library.TryGetMenu(StockUiQuestLog.GroupMenu, out var groups));
            Assert.Equal((384, 48, 85, StockUiQuestLog.HelpMissions), (groups.Frame.X, groups.Frame.Y, groups.Frame.TitleTextId, groups.Frame.HelpTextId));
            Assert.Equal(StockUiQuestLog.GroupButtons, groups.Buttons.OrderBy(b => b.Y).Select(b => (int)b.ButtonId));
            foreach (var button in groups.Buttons) Assert.True(StockUiMenuEntries.TryGet(StockUiQuestLog.GroupMenu, button.ButtonId, out _));
            foreach (int group in StockUiQuestLog.GroupButtons) Assert.True(StockUiQuestLog.Groups.ContainsKey(group));

            // The pickers' and lists' rows as the controller rebuilds them.
            Assert.True(library.TryGetMenu(StockUiQuestLog.StorylineMenu, out var storylines));
            Assert.Equal((39, 65, 320, 18), (storylines.FindButton(2)!.X, storylines.FindButton(2)!.Y, storylines.FindButton(2)!.Width, storylines.FindButton(2)!.Height));
            Assert.True(library.TryGetMenu(StockUiQuestLog.AreaMenu, out var areas));
            Assert.Equal((39, 40, 100), (areas.FindButton(1)!.X, areas.FindButton(1)!.Y, areas.Frame.TitleTextId));
            Assert.True(library.TryGetMenu(StockUiQuestLog.QuestKindMenu, out var kind));
            Assert.Equal((39, 31 + 37), (kind.FindButton(2)!.X, kind.FindButton(2)!.Y));
            foreach (string name in new[] { StockUiQuestLog.MissionListMenu, StockUiQuestLog.QuestListMenu })
            {
                Assert.True(library.TryGetMenu(name, out var list));
                Assert.Equal(StockUiQuestLog.ListRows, list.Buttons.Count);
                Assert.Equal((0, 4 + 16 * 14, 356, 16), (list.FindButton(15)!.X, list.FindButton(15)!.Y, list.FindButton(15)!.Width, list.FindButton(15)!.Height));
            }
            Assert.True(library.TryGetGroup("quest", out var band));
            Assert.NotEmpty(band.Images);

            // Region Info and the Conquest page's frames.
            Assert.True(library.TryGetMenu(StockUiConquest.RegionMenu, out var region));
            Assert.Equal(4, region.Buttons.Count);
            Assert.All(region.Buttons, b => Assert.Equal(StockUiConquest.HelpRegionInfo, b.HelpTextId));
            foreach (var button in region.Buttons) Assert.True(StockUiMenuEntries.TryGet(StockUiConquest.RegionMenu, button.ButtonId, out _));
            Assert.True(library.TryGetMenu(StockUiConquest.ControlWindow, out var control));
            Assert.Equal((384, 48, 112, 206), (control.Frame.X, control.Frame.Y, control.Frame.Width, control.Frame.Height));
            Assert.True(library.TryGetMenu(StockUiConquest.BalanceWindow, out var balance));
            Assert.Equal((384, 48, 72), (balance.Frame.X, balance.Frame.Y, balance.Frame.Height));
            Assert.True(library.TryGetMenu(StockUiConquest.PointsWindow, out var points));
            Assert.Equal((384, 256), (points.Frame.X, points.Frame.Y));

            // The texts the windows read (ROM/165/72; help ROM/165/75).
            var messages = rm.GetDMsgTable(DMsgCategory.MenuMessages)!;
            Assert.Equal(StockUiQuestLog.NoCurrentQuestsFallback, messages.Records[StockUiQuestLog.NoCurrentQuestsText].PrimaryText);
            Assert.Equal(StockUiQuestLog.NoCompletedQuestsFallback, messages.Records[StockUiQuestLog.NoCompletedQuestsText].PrimaryText);
            Assert.Equal(StockUiConquest.NeutralRegionFallback, messages.Records[StockUiConquest.NeutralRegionText].PrimaryText);
            Assert.Equal(StockUiConquest.BeastmenRegionFallback, messages.Records[StockUiConquest.BeastmenRegionText].PrimaryText);
            Assert.Equal(StockUiConquest.NeutralAreaFallback, messages.Records[StockUiConquest.NeutralAreaText].PrimaryText);
            var help = rm.GetDMsgTable(DMsgCategory.MenuHelp)!;
            Assert.Equal("View list of current missions.", help.Records[StockUiQuestLog.HelpMissions].PrimaryText);
            Assert.Equal("View the balance of power for each region.", help.Records[StockUiConquest.HelpRegionInfo].PrimaryText);

            // The windows open on the retail frames.
            var menus = new StockUiMenuController { Library = library };
            var catalog = StockUiQuestLogCatalog.FromTables(rm.GetDMsgTable);
            menus.QuestLogCatalog = () => catalog;
            menus.QuestLogState = () => State();
            Assert.True(menus.Open(StockUiMenuEntries.MainMenuPage2));
            menus.Top!.SelectedButtonId = 2;
            menus.Activate();
            Assert.Equal(StockUiQuestLog.AreaMenu, menus.Top!.Name);
            menus.Activate();
            menus.Activate();
            Assert.Equal(StockUiQuestLog.QuestListMenu, menus.Top!.Name);
            Assert.Equal("A Sentry's Peril", menus.Top!.Rows[0].Text);
        }

        #endregion
    }
}
