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
            // Another nation: its missions done, none in progress (round 2 of #488).
            Assert.Empty(StockUiQuestLog.MissionRows(catalog, state, StockUiQuestLogBook.MissionsBastok));
            state.MissionComplete[2] = 0b1;
            Assert.Equal(new[] { ("The Zeruhn Report", StockUiQuestLogStatus.Completed) },
                StockUiQuestLog.MissionRows(catalog, state, StockUiQuestLogBook.MissionsBastok).Select(r => (r.Entry.Name, r.Status)));
        }

        [Fact]
        public void PageLines_CurrentThenCompleted_OtherNationsNotAllowed_EmptyQuestsSaySo()
        {
            var catalog = Catalog();
            var state = State();
            var sandoria = StockUiQuestLog.PageLines(catalog, state, StockUiQuestLogBook.MissionsSandoria, false);
            Assert.Equal(new[]
            {
                (StockUiQuestLogLineKind.Heading, StockUiQuestLog.CurrentHeading), (StockUiQuestLogLineKind.Entry, "Save the Children"),
                (StockUiQuestLogLineKind.Heading, StockUiQuestLog.CompletedHeading), (StockUiQuestLogLineKind.Entry, "Smash the Orcish Scouts"),
                (StockUiQuestLogLineKind.Entry, "Bat Hunt"),
            }, sandoria.Select(l => (l.Kind, l.Text)));
            // Another nation: the note under Current (retail, round 1), and its Completed list or the empty text (round 2).
            Assert.Equal(new[] { StockUiQuestLog.CurrentHeading, StockUiQuestLog.NotAllowedText, StockUiQuestLog.CompletedHeading, StockUiQuestLog.NoCompletedMissionsText },
                StockUiQuestLog.PageLines(catalog, state, StockUiQuestLogBook.MissionsBastok, false).Select(l => l.Text));
            state.MissionComplete[2] = 0b1;
            Assert.Equal(new[] { StockUiQuestLog.CurrentHeading, StockUiQuestLog.NotAllowedText, StockUiQuestLog.CompletedHeading, "The Zeruhn Report" },
                StockUiQuestLog.PageLines(catalog, state, StockUiQuestLogBook.MissionsBastok, false).Select(l => l.Text));
            // A storyline not started: both sections say so (retail image-48).
            var none = StockUiQuestLog.PageLines(catalog, new StockUiQuestLogState(), StockUiQuestLogBook.MissionsCop, false);
            Assert.Equal(new[]
            {
                (StockUiQuestLogLineKind.Heading, StockUiQuestLog.CurrentHeading), (StockUiQuestLogLineKind.Note, StockUiQuestLog.NoCurrentMissionsText),
                (StockUiQuestLogLineKind.Heading, StockUiQuestLog.CompletedHeading), (StockUiQuestLogLineKind.Note, StockUiQuestLog.NoCompletedMissionsText),
            }, none.Select(l => (l.Kind, l.Text)));
            Assert.Equal(StockUiQuestLogLineKind.Note, StockUiQuestLog.PageLines(catalog, state, StockUiQuestLogBook.MissionsBastok, false)[1].Kind);
            // A quest page: the quests alone, or the empty text.
            Assert.Equal(new[] { "A Sentry's Peril" }, StockUiQuestLog.PageLines(catalog, state, StockUiQuestLogBook.QuestsSandoria, false).Select(l => l.Text));
            var empty = StockUiQuestLog.PageLines(catalog, state, StockUiQuestLogBook.QuestsBastok, false, "No current quests.");
            Assert.Equal((StockUiQuestLogLineKind.Note, "No current quests."), (empty.Single().Kind, empty.Single().Text));
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
            // At the chapter's own number its first mission is in progress: under Current and, as the chapter, Completed (image-11).
            var lines = StockUiQuestLog.PageLines(Catalog(), new StockUiQuestLogState { Promathia = 101 }, StockUiQuestLogBook.MissionsCop, false);
            Assert.Equal(new[] { "Current:", "Ancient Flames Beckon", "Completed:", "Ancient Flames Beckon" }, lines.Select(l => l.Text));
            Assert.Equal(StockUiQuestLogStatus.Current, lines[1].Row!.Value.Status);
            Assert.Equal(StockUiQuestLogStatus.Heading, lines[3].Row!.Value.Status);
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
        public void Quests_CurrentOrCompleted_ThenPagesThroughTheAreas_ThenTheSummary()
        {
            var h = new Harness();
            Assert.True(h.Menus.Open(StockUiMenuEntries.MainMenuPage2));
            var kind = h.Choose(2);
            Assert.Equal(StockUiQuestLogView.QuestKind, kind.QuestLogView);
            Assert.Equal(StockUiQuestLog.QuestKindMenu, kind.Name);
            Assert.Equal(new[] { "Current", "Completed", "Current", "Objective List" }, kind.Rows.Select(r => r.Text)); // rows 3-4: #493

            var completed = h.Choose(2);
            Assert.Equal(StockUiQuestLogView.Pages, completed.QuestLogView);
            Assert.Equal(StockUiQuestLog.AreaMenu, completed.Name);
            Assert.Equal("San d'Oria: Completed Quests", completed.QuestLogHeading);
            Assert.Equal(new[] { "Waters of the Cheval", "Eco-Warrior" }, completed.Rows.Select(r => r.Text));
            Assert.Equal(1, completed.SelectedButtonId);
            Assert.Equal("Quests", completed.QuestLogTitle);
            Assert.Equal((39, 40 + 25), (completed.Menu.FindButton(2)!.X, completed.Menu.FindButton(2)!.Y));

            var page = h.Choose(1);
            Assert.Equal(StockUiQuestLogView.Description, page.QuestLogView);
            Assert.Equal(StockUiQuestLog.QuestListMenu, page.Name);
            Assert.Equal("Waters of the Cheval", page.QuestLogShown!.Value.Entry.Name);
            Assert.Empty(page.Menu.Buttons);

            // Right: Bastok has none ("No completed quests.", ROM/165/72 #100), so nothing takes the cursor; left wraps to Coalition.
            h.Menus.CloseTop();
            h.Menus.Move(Gordian.Engine.Input.InputAction.MenuRight);
            Assert.Equal("Bastok: Completed Quests", completed.QuestLogHeading);
            Assert.Equal(StockUiQuestLog.NoCompletedQuestsFallback, completed.Rows.Single().Text);
            Assert.Equal(0, completed.SelectedButtonId);
            h.Menus.Move(Gordian.Engine.Input.InputAction.MenuLeft);
            h.Menus.Move(Gordian.Engine.Input.InputAction.MenuLeft);
            Assert.Equal("Coalition: Completed Assignments", completed.QuestLogHeading);
        }

        [Fact]
        public void Missions_EveryGroupOpensThePagingWindow_OnItsStoryline()
        {
            var h = new Harness();
            Assert.True(h.Menus.Open(StockUiMenuEntries.MainMenuPage2));
            var groups = h.Choose(1);
            Assert.Equal(StockUiQuestLog.GroupMenu, groups.Name);

            // Nations: the home nation's page, the cursor on the mission in progress under "Current:".
            var pages = h.Choose(1);
            Assert.Equal(StockUiQuestLogView.Pages, pages.QuestLogView);
            Assert.Equal(StockUiQuestLog.StorylineMenu, pages.Name);
            Assert.Equal("San d'Oria", pages.QuestLogHeading);
            Assert.Equal(new[] { "Current:", "Save the Children", "Completed:", "Smash the Orcish Scouts", "Bat Hunt" }, pages.Rows.Select(r => r.Text));
            Assert.True(pages.Rows[1].Marked);
            Assert.Equal(2, pages.SelectedButtonId);

            // Down steps over "Completed:"; up from the mission in progress wraps to the last mission.
            h.Menus.Move(Gordian.Engine.Input.InputAction.MenuDown);
            Assert.Equal(4, pages.SelectedButtonId);
            h.Menus.Move(Gordian.Engine.Input.InputAction.MenuUp);
            h.Menus.Move(Gordian.Engine.Input.InputAction.MenuUp);
            Assert.Equal(5, pages.SelectedButtonId);

            // Confirm: the orders.
            h.Menus.Move(Gordian.Engine.Input.InputAction.MenuDown);
            var orders = h.Choose(pages.SelectedButtonId);
            Assert.Equal("Mission Orders: Rescue them.", orders.QuestLogShown!.Value.Entry.Description);
            h.Menus.CloseTop();

            // Right: Bastok, "Not allowed under current allegiance." under Current and no cursor.
            h.Menus.Move(Gordian.Engine.Input.InputAction.MenuRight);
            Assert.Equal("Bastok", pages.QuestLogHeading);
            Assert.Equal(StockUiQuestLog.NotAllowedText, pages.Rows[1].Text);
            Assert.Equal(0, pages.SelectedButtonId);
            // Left past San d'Oria wraps to the last storyline.
            h.Menus.Move(Gordian.Engine.Input.InputAction.MenuLeft);
            h.Menus.Move(Gordian.Engine.Input.InputAction.MenuLeft);
            Assert.Equal("The Voracious Resurgence", pages.QuestLogHeading);

            // Zilart starts on Rise of the Zilart; the page follows a new 0x056.
            h.Menus.CloseTop();
            var zilart = h.Choose(2);
            Assert.Equal("Rise of the Zilart", zilart.QuestLogHeading);
            Assert.Equal(new[] { "Current:", "Welcome t'Norg", "Completed:", "The New Frontier", "The Outlands" }, zilart.Rows.Select(r => r.Text));
            h.State = new StockUiQuestLogState { Zilart = 0xFFFF, TalesBeginning = 1 };
            h.Menus.RefreshQuestLog();
            Assert.Equal(new[] { "Current:", "To Commence RotZ", "Completed:", "No completed missions." }, zilart.Rows.Select(r => r.Text));

            // Add-ons starts on A Crystalline Prophecy.
            h.Menus.CloseTop();
            Assert.Equal("A Crystalline Prophecy", h.Choose(6).QuestLogHeading);
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
        public void ConquestMarkers_RegionsEverywhere_ZonesOfTheCharactersRegion()
        {
            string? Region(int id) => id switch { 3 => "Jeuno", 10 => "Kolshushu", 14 => "Qufim", _ => $"R{id}" };
            string? Zone(int id) => id switch { 4 => "Bibiki Bay", 213 => "Onzozo Lbrth.", 126 => "Qufim Isl.", _ => $"Z{id}" };
            // In Bibiki Bay (Kolshushu): every placed region, Jeuno as a city, and Kolshushu's seven zones (retail image-49).
            var markers = StockUiConquest.Markers(10, 4, Region, Zone);
            Assert.Contains(markers, m => m is { Kind: StockUiConquestMarkerKind.Region, Label: "Kolshushu", X: 376.5f, Y: 262.5f, Side: StockUiMapLabelSide.Above });
            Assert.Contains(markers, m => m is { Kind: StockUiConquestMarkerKind.City, Label: "Jeuno", Region: 3 });
            Assert.Contains(markers, m => m is { Kind: StockUiConquestMarkerKind.CurrentZone, Label: "Bibiki Bay" });
            Assert.Contains(markers, m => m is { Kind: StockUiConquestMarkerKind.Zone, Label: "Onzozo Lbrth.", Side: StockUiMapLabelSide.Left });
            Assert.Equal(7, markers.Count(m => m.Kind is StockUiConquestMarkerKind.Zone or StockUiConquestMarkerKind.CurrentZone));
            Assert.DoesNotContain(markers, m => m.Label == "Qufim Isl.");
            // In a city: the regions alone; a region without a name is left out.
            Assert.DoesNotContain(StockUiConquest.Markers(3, 243, Region, Zone), m => m.Kind is StockUiConquestMarkerKind.Zone or StockUiConquestMarkerKind.CurrentZone);
            Assert.Empty(StockUiConquest.Markers(10, 4, null, null));
            // Every region and city is placed (round 3 captures); a zone LandSandBoat leaves without a region still shows with
            // its region's (the Cloister of Flames with Elshimo Uplands, image-57).
            foreach (int region in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 22, 24 })
                Assert.NotNull(StockUiConquest.CenterOf(region));
            Assert.Null(StockUiConquest.CenterOf(21));
            Assert.Equal((312.5f / 512f, 162.5f / 512f), StockUiConquest.CenterOf(14));
            var uplands = StockUiConquest.Markers(19, -1, Region, id => id == 207 ? "Clstr.of Flames" : $"Z{id}");
            Assert.Contains(uplands, m => m is { Label: "Clstr.of Flames", Side: StockUiMapLabelSide.Right });
            Assert.Equal(7, uplands.Count(m => m.Kind == StockUiConquestMarkerKind.Zone));
            Assert.Contains(StockUiConquest.Markers(9, -1, Region, Zone), m => m is { Label: "Z170", Side: StockUiMapLabelSide.Below });
        }

        [Fact]
        public void ConquestCursor_SelectsTheRegionItComesTo_AndScrollsTheMapAtTheScreensEdge()
        {
            // The region under the cursor: within 3 map px of a region's ball; a city or open map is no region.
            Assert.Equal(10, StockUiConquest.RegionAt(376.5f, 262.5f));
            Assert.Equal(10, StockUiConquest.RegionAt(378f, 264f));
            Assert.Equal(-1, StockUiConquest.RegionAt(370f, 262.5f));
            Assert.Equal(-1, StockUiConquest.RegionAt(286.5f, 202.5f));

            var h = new Harness();
            var progression = new ProgressionState();
            h.Conquest = StockUiConquest.Build(progression, 10, "Kolshushu", zone: 4);
            h.Menus.ConquestRegionView = r => StockUiConquest.Build(progression, r, $"R{r}", zone: 4, characterRegion: 10);
            Assert.True(h.Menus.Open(StockUiMenuEntries.MainMenu));
            h.Choose(11);
            var page = h.Choose(StockUiConquest.ConquestButton);
            Assert.Equal((376.5f / 512f, 262.5f / 512f), (page.MapCursorX, page.MapCursorY));
            Assert.Equal(10, page.ConquestView!.Region);
            Assert.True(page.ConquestView.HasInfluence);

            // Movement moves the cursor (70 map px a second), not the map, and leaving the ball shows the Balance of Power.
            float centerX = page.MapCenterX;
            h.Menus.MoveMapView(0.1f, 0, 0, 0);
            Assert.Equal((376.5f + 7) / 512f, page.MapCursorX, 4);
            Assert.Equal(centerX, page.MapCenterX);
            Assert.Equal(-1, page.ConquestView!.Region);
            Assert.False(page.ConquestView.InConquestRegion);

            // Up to Aragoneu (359.5, 220.5): its page, without shares (another region's influence is not in 0x05E).
            h.Menus.MoveMapView(-(7 + 17) / 70f, -42 / 70f, 0, 0);
            Assert.Equal(11, page.ConquestView!.Region);
            Assert.False(page.ConquestView.HasInfluence);

            // The drawer keeps the cursor in view: past the inner part of the screen the map scrolls under it...
            const float mapSize = 512 * 11.5f, width = 2552, height = 1434;
            page.KeepMapCursorInView(mapSize, mapSize, width, height, 12, 40);
            float Sx() => width / 2 + (page.MapCursorX - page.MapCenterX) * mapSize;
            for (int i = 0; i < 16; i++)
            {
                h.Menus.MoveMapView(0.1f, 0, 0, 0);
                page.KeepMapCursorInView(mapSize, mapSize, width, height, 12, 40);
            }
            Assert.True(page.MapCenterX > 376.5f / 512f);
            Assert.Equal(width * StockUiConquest.CursorRight, Sx(), 1);
            // ...and at the map's right edge the cursor goes on to the screen's edge.
            for (int i = 0; i < 20; i++)
            {
                h.Menus.MoveMapView(1f, 0, 0, 0);
                page.KeepMapCursorInView(mapSize, mapSize, width, height, 12, 40);
            }
            Assert.Equal(1 - width / 2 / mapSize, page.MapCenterX, 4);
            Assert.Equal(width - 12, Sx(), 1);
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
            int requests = 0;
            h.Menus.ConquestRequested = () => { requests++; return System.Threading.Tasks.Task.CompletedTask; };

            Assert.True(h.Menus.Open(StockUiMenuEntries.MainMenu));
            var region = h.Choose(11);
            Assert.Equal(StockUiConquest.RegionMenu, region.Name);
            var page = h.Choose(StockUiConquest.ConquestButton);
            Assert.Equal(StockUiQuestLogView.Conquest, page.QuestLogView);
            Assert.Equal(StockUiConquest.ControlWindow, page.Name);
            // Opening asks for the figures (C2S 0x05A; round 2: a session without a 0x05E drew an empty Balance of Power).
            Assert.Equal(1, requests);
            // The conquest map behind, panned by the movement input but never zoomed.
            Assert.Equal(Gordian.Core.Resources.Tables.RegionMap.Conquest, page.RegionMap);
            Assert.Same(page, h.Menus.PannedMap);
            Assert.Equal(StockUiConquest.MapZoom, page.MapZoom);
            // It opens on the character's region (retail image-49): the map and its cursor on Ronfaure's marker.
            Assert.Equal((176.5f / 512f, 182.5f / 512f), (page.MapCenterX, page.MapCenterY));
            Assert.Equal((176.5f / 512f, 182.5f / 512f), (page.MapCursorX, page.MapCursorY));
            h.Menus.MoveMapView(0.01f, 0, 1f, 2f);
            Assert.Equal(StockUiConquest.MapZoom, page.MapZoom);
            Assert.True(page.MapCursorX > 176.5f / 512f);
            var view = page.ConquestView!;
            Assert.Equal(StockUiConquest.BeastmenRegionFallback, view.Control);
            Assert.Equal(3, view.NextTally);
            Assert.Equal(new[] { 30, 20, 10, 40 }, view.Influence.Select(b => b.Percent));
            Assert.Equal(1500u, view.ConquestPoints);
            Assert.Equal("Conquest", page.QuestLogTitle);

            // In a city: the Balance of Power.
            h.Menus.CloseTop();
            h.Conquest = StockUiConquest.Build(progression, 0, "San d'Oria");
            var balance = h.Choose(StockUiConquest.ConquestButton);
            Assert.Equal(StockUiConquest.BalanceWindow, balance.Name);
            Assert.Equal(new[] { "1. San d'Oria", "2. Windurst", "3. Bastok" }, balance.ConquestView!.Balance);
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
            Assert.Equal(("1 day.", "137 days."), (StockUiConquest.TallyText(1), StockUiConquest.TallyText(137)));
            // The window's owner in a word (retail image-55 "Neutral").
            Assert.Equal(("Bastok", "Neutral"), (StockUiConquest.Build(progression, 5, "Zulkheim").Owner, StockUiConquest.Build(progression, 6, "Norvallen").Owner));
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
            foreach (string name in new[] { StockUiQuestLog.StorylineMenu, StockUiQuestLog.AreaMenu })
            {
                // The paging windows' red arrows (frames#25 / #26).
                Assert.True(library.TryGetMenu(name, out var paging));
                Assert.Equal((9, 343), (paging.FindButton(StockUiQuestLog.PreviousPageButton)!.X, paging.FindButton(StockUiQuestLog.NextPageButton)!.X));
            }
            foreach (string name in new[] { StockUiQuestLog.MissionListMenu, StockUiQuestLog.QuestListMenu })
            {
                Assert.True(library.TryGetMenu(name, out var list));
                Assert.Equal((16, 48, 366, 248), (list.Frame.X, list.Frame.Y, list.Frame.Width, list.Frame.Height));
            }

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
            Assert.Equal(StockUiQuestLog.QuestKindMenu, menus.Top!.Name);
            menus.Activate();
            Assert.Equal(StockUiQuestLog.AreaMenu, menus.Top!.Name);
            Assert.Equal("A Sentry's Peril", menus.Top!.Rows[0].Text);
        }

        #endregion
    }
}
