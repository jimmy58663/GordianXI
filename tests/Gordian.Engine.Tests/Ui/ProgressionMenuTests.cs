// tests/Gordian.Engine.Tests/Ui/ProgressionMenuTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Input;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Ui
{
    /// <summary>
    /// Merit Points' Categories, spending job points, the Unity windows (#491, #94) and Records of Eminence (#493): the rules
    /// (<see cref="StockUiProgression"/>, <see cref="StockUiRecordsOfEminence"/>), the flows on stand-in windows, and the
    /// retail tables when the game is installed.
    /// </summary>
    public class ProgressionMenuTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        #region Rules

        [Fact]
        public void MeritIds_MapToTheirCategories()
        {
            Assert.Equal(64, StockUiProgression.MeritCategoryId(1));
            Assert.Equal(1, StockUiProgression.CategoryOfMerit(0x40));  // Max HP
            Assert.Equal(1, StockUiProgression.CategoryOfMerit(0x44));  // Maximum Merit Points
            Assert.Equal(6, StockUiProgression.CategoryOfMerit(384));   // Berserk Recast
            Assert.Equal(6, StockUiProgression.CategoryOfMerit(2048));  // Warrior group 2
            Assert.Equal(27, StockUiProgression.CategoryOfMerit(3392)); // Geomancer group 2
            Assert.Equal(28, StockUiProgression.CategoryOfMerit(3456 + 2));
            Assert.Equal(0, StockUiProgression.CategoryOfMerit(0));
            // LandSandBoat xi.meritCategory: WAR_GROUP_2 2048, SCH_GROUP_2 3264, GEO_GROUP_2 3392, RUN_GROUP_2 3456; Weapon Skills has none.
            Assert.Equal(2048, StockUiProgression.SecondGroupId(6));
            Assert.Equal(3264, StockUiProgression.SecondGroupId(25));
            Assert.Equal(3392, StockUiProgression.SecondGroupId(27));
            Assert.Equal(3456, StockUiProgression.SecondGroupId(28));
            Assert.Equal(-1, StockUiProgression.SecondGroupId(26));
            Assert.Equal(-1, StockUiProgression.SecondGroupId(1));
        }

        [Fact]
        public void CategoryMax_ReadsTheTableLine()
        {
            Assert.Equal(75, StockUiProgression.CategoryMax("Adjust your maximum HP and MP.\nMax. total ability increases: 75"));
            Assert.Equal(20, StockUiProgression.CategoryMax("Adjust various abilities for WAR lv.75 or above.\nGroup 1 max. total ability increases: 10\nGroup 2 max. total ability increases: 10"));
            Assert.Equal(0, StockUiProgression.CategoryMax("Adjust weapon skills for levels 96 and above. Total ability increases: 15."));
            Assert.Equal(0, StockUiProgression.CategoryMax(null));
        }

        [Fact]
        public void MeritRows_TotalPerCategory_GreyedWithoutANextCost()
        {
            var merits = new[] { new StockUiMerit(0x40, 3, 4), new StockUiMerit(0x42, 0, 15), new StockUiMerit(384, 1, 2), new StockUiMerit(2048, 3, 1) };
            var text = MeritText();
            var categories = StockUiProgression.MeritCategories(text);
            Assert.Equal(new[] { 1, 6 }, categories);
            var rows = StockUiProgression.MeritCategoryRows(categories, merits, text);
            Assert.Equal(("HP/MP", "19/75"), (rows[0].Text, rows[0].Value));
            Assert.Equal(("Warrior", "3/20"), (rows[1].Text, rows[1].Value));
            var hp = StockUiProgression.MeritsOf(1, merits, text);
            Assert.Equal(new[] { 0x40, 0x42 }, hp.Select(m => m.Id));
            var meritRows = StockUiProgression.MeritRows(hp, text);
            Assert.Equal(("Max HP", "4", (UiColor?)null), (meritRows[0].Text, meritRows[0].Value, meritRows[0].Color));
            Assert.Equal(StockUiProgression.Greyed, meritRows[1].Color);
            Assert.Equal(new[] { 384, 2048 }, StockUiProgression.MeritsOf(6, merits, text).Select(m => m.Id));
        }

        [Fact]
        public void JobPoints_IndexAndCost()
        {
            Assert.Equal(64, StockUiProgression.JobPointCategoryNameId(1, 0));   // "Mighty Strikes Effect"
            Assert.Equal(128 + 2, StockUiProgression.JobPointCategoryNameId(2, 1)); // "Dodge Effect"
            Assert.Equal((ushort)0x20, StockUiProgression.JobPointIndex(1, 0));  // LandSandBoat JP_MIGHTY_STRIKES_EFFECT
            Assert.Equal((ushort)0x41, StockUiProgression.JobPointIndex(2, 1));
            Assert.Equal(1, StockUiProgression.JobPointCost(0));
            Assert.Equal(20, StockUiProgression.JobPointCost(19));
            Assert.Equal(0, StockUiProgression.JobPointCost(20));
        }

        [Fact]
        public void UnityRank_ByPoints_TiesShare()
        {
            var points = new uint[] { 10, 50, 50, 0, 0, 0, 0, 0, 0, 0, 30 };
            Assert.Equal(1, StockUiProgression.UnityRank(points, 2));
            Assert.Equal(1, StockUiProgression.UnityRank(points, 3));
            Assert.Equal(3, StockUiProgression.UnityRank(points, 11));
            Assert.Equal(4, StockUiProgression.UnityRank(points, 1));
            Assert.Equal(0, StockUiProgression.UnityRank(new uint[11], 1));
            Assert.Equal(0, StockUiProgression.UnityRank(points, 0));
            var info = new StockUiUnityInfo(2, 7, 9, new uint[11], points);
            var rows = StockUiProgression.UnityInfoRows(info, chatActive: false, null);
            Assert.Equal("Unity 2", rows[0].Value);
            Assert.Equal("--", rows[1].Value); // last week: nothing sent
            Assert.Equal("1", rows[2].Value);
            Assert.Equal(StockUiProgression.Greyed, rows[StockUiProgression.UnityChatActiveRow - 1].Color);
            Assert.Null(rows[StockUiProgression.UnityChatInactiveRow - 1].Color);
            var ranking = StockUiProgression.UnityRankingRows(info, null);
            Assert.Equal(new[] { "Unity 2", "Unity 3", "Unity 11", "Unity 1" }, ranking.Take(4).Select(r => r.Text));
            Assert.True(ranking[0].Marked);
        }

        [Fact]
        public void RoeRows_HideLockedChildren_ShowProgress_GreyDone()
        {
            var tables = RoeTables();
            var state = new StockUiRoeState(new[] { new StockUiRoeSlot(0, 12, 37) }, id => id == 1);
            var (top, topKeys) = StockUiRecordsOfEminence.CategoryRows(tables, RecordsOfEminenceTables.RootCategory, state);
            Assert.Equal(new[] { "Tutorial", "Combat (Wide Area)" }, top.Select(r => r.Text));
            Assert.Equal(new[] { 0xF210, 0xF211 }, topKeys);

            var (objectives, keys) = StockUiRecordsOfEminence.CategoryRows(tables, 0xF210, state);
            // First Step Forward (done, not repeatable: greyed), Vanquish Multiple Enemies I (taken: 37/200); the third needs 4 done.
            Assert.Equal(new[] { "First Step Forward", "Vanquish Multiple Enemies I" }, objectives.Select(r => r.Text));
            Assert.Equal(StockUiProgression.Greyed, objectives[0].Color);
            Assert.Equal("37/200", objectives[1].Value);
            Assert.Equal(new[] { 0xE001, 0xE00C }, keys);

            var (current, currentKeys) = StockUiRecordsOfEminence.CurrentRows(tables, state);
            Assert.Equal(("Vanquish Multiple Enemies I", "37/200"), (current.Single().Text, current.Single().Value));
            Assert.Equal(0xE00C, currentKeys.Single());

            string? line = StockUiRecordsOfEminence.HelpLine(tables.Objective(12), state, null);
            Assert.Equal("Number Completed 37/200  Rewards: 1,000 Spa. 5,000 EXP 100 Unity Accolades", line);
        }

        #endregion

        #region Flows

        [Fact]
        public void MeritCategories_RaiseAsksThenSends_InTheMogHouseOnly()
        {
            var h = new Harness();
            Assert.True(h.Menus.Open(StockUiStatusPages.MeritMenu));
            h.Menus.Activate(); // Categories
            var categories = h.Menus.Top!;
            Assert.Equal(StockUiProgressionList.MeritCategories, categories.ProgressionList);
            Assert.Equal(StockUiProgression.MeritCategoryMenu, categories.Name);
            Assert.Equal(StockUiProgression.MeritCategoryHelp, categories.SelectedStatusRowHelp);
            h.Menus.Activate(); // HP/MP
            var list = h.Menus.Top!;
            Assert.Equal((StockUiProgressionList.Merits, 1), (list.ProgressionList, list.ProgressionKey));
            Assert.Equal(new[] { "Max HP", "Max MP" }, list.Rows.Select(r => r.Text));

            // Outside the Mog House: a notice, nothing sent.
            h.Menus.Activate();
            Assert.Equal(StockUiProgression.MeritEditMenu, h.Menus.Top!.Name);
            h.Menus.Activate(); // Raise
            Assert.Contains(h.Notices, n => n.Contains("Mog House", StringComparison.Ordinal));
            Assert.Same(list, h.Menus.Top);

            h.InMogHouse = true;
            h.Menus.Activate();
            h.Menus.Activate(); // Raise
            Assert.True(h.Menus.Top!.IsPrompt);
            Assert.Equal("Raise Max HP?", h.Menus.Top!.Message);
            h.Menus.Activate(); // Yes
            Assert.Equal((0x40, true), h.MeritSent.Task.Wait(2000) ? h.MeritSent.Task.Result : default);

            // Max MP has no next cost (at its most): Raise posts a notice.
            h.Menus.Move(InputAction.MenuDown);
            h.Menus.Activate();
            h.Menus.Activate();
            Assert.Contains(h.Notices, n => n == "Max MP cannot be raised any further.");

            // S2C 0x08C: the open lists follow, the cursor kept.
            h.Merits = new[] { new StockUiMerit(0x40, 4, 5), new StockUiMerit(0x42, 0, 15) };
            h.Menus.OnStatusPointsChanged();
            Assert.Equal("5", list.Rows[0].Value);
            Assert.Equal(1, list.SelectedStatusRow);
        }

        [Fact]
        public void JobPoints_AJobListsItsCategories_SpendingAsksThenSends()
        {
            var h = new Harness();
            Assert.True(h.Menus.Open(StockUiStatus.CommandMenu));
            h.Menus.Top!.SelectedButtonId = StockUiStatus.JobPointsButton;
            h.Menus.Activate();
            Assert.Equal(StockUiStatusPage.JobPoints, h.Menus.Top!.StatusWindow);
            h.Menus.Top!.SelectedButtonId = 1; // Warrior
            h.Menus.Activate();
            var categories = h.Menus.Top!;
            Assert.Equal((StockUiProgressionList.JobPointCategories, 1), (categories.ProgressionList, categories.ProgressionKey));
            Assert.Equal(("Mighty Strikes Effect", "3/20"), (categories.Rows[0].Text, categories.Rows[0].Value));
            h.InMogHouse = true;
            h.Menus.Activate();
            Assert.Equal("Raise Mighty Strikes Effect? (4 job points)", h.Menus.Top!.Message);
            h.Menus.Activate();
            Assert.True(h.JobPointSent.Task.Wait(2000));
            Assert.Equal((ushort)0x20, h.JobPointSent.Task.Result);
        }

        [Fact]
        public void Unity_InfoTogglesChat_RankingAsksForTheFigures()
        {
            var h = new Harness();
            Assert.True(h.Menus.Open(StockUiStatusPages.UnityMenu));
            h.Menus.Activate(); // Unity Info
            var info = h.Menus.Top!;
            Assert.Equal(StockUiProgressionList.UnityInfo, info.ProgressionList);
            Assert.Equal("Naja Salaheem", info.Rows[0].Value);
            Assert.Equal(StockUiProgression.UnityChatActiveRow - 1, info.SelectedStatusRow);
            h.Menus.Move(InputAction.MenuDown); // Inactive
            h.Menus.Activate();
            Assert.Equal(new[] { false }, h.UnityChat);
            Assert.False(h.Menus.UnityChatActive);
            Assert.Null(info.Rows[StockUiProgression.UnityChatInactiveRow - 1].Color);

            h.Menus.CloseTop();
            h.Menus.Top!.SelectedButtonId = StockUiStatusPages.UnityRankingButton;
            h.Menus.Activate();
            Assert.Equal(1, h.UnityQuests);
            Assert.Equal(StockUiProgressionList.UnityRanking, h.Menus.Top!.ProgressionList);
            Assert.Equal("Naja Salaheem", h.Menus.Top!.Rows[0].Text);
        }

        [Fact]
        public void RecordsOfEminence_TakeAndRemoveObjectives()
        {
            var h = new Harness();
            var kind = h.Menus.OpenQuests(null)!;
            Assert.Equal(4, kind.Rows.Count);
            Assert.Equal("Records of Eminence", kind.Rows[2].Text);
            kind.SelectedButtonId = StockUiRecordsOfEminence.RoeKindButton;
            h.Menus.Activate();
            var roe = h.Menus.Top!;
            Assert.Equal(StockUiQuestLogView.RoeMenu, roe.QuestLogView);
            Assert.Equal(StockUiRecordsOfEminence.HelpTitle, roe.QuestLogTitle);

            // Current with nothing taken: "No current objectives."
            h.Menus.Activate();
            Assert.Contains("No current objectives.", h.Notices);
            Assert.Equal(1, h.UnityQuests);

            // Objective List: Tutorial > First Step Forward is done for good; Vanquish Multiple Enemies I is taken.
            roe.SelectedButtonId = StockUiRecordsOfEminence.ObjectiveListButton;
            h.Menus.Activate();
            Assert.Equal(StockUiProgressionList.RoeCategories, h.Menus.Top!.ProgressionList);
            h.Menus.Activate(); // Tutorial
            var objectives = h.Menus.Top!;
            Assert.Equal(StockUiProgressionList.RoeObjectives, objectives.ProgressionList);
            h.Menus.Activate(); // First Step Forward
            Assert.Contains("This objective cannot be undertaken again.", h.Notices);

            h.Completed.Clear();
            h.Menus.OnStatusPointsChanged();
            h.Menus.Activate();
            Assert.Equal("Set objective?", h.Menus.Top!.Message);
            h.Menus.Activate(); // Yes
            Assert.True(h.RoeStarted.Task.Wait(2000));
            Assert.Equal(1, h.RoeStarted.Task.Result);

            // S2C 0x111 lists it: the row shows its progress; Confirm now asks to remove it.
            h.Slots = new[] { new StockUiRoeSlot(0, 1, 0) };
            h.Menus.OnStatusPointsChanged();
            Assert.Equal("0/1", objectives.Rows[0].Value);
            h.Menus.Activate();
            Assert.StartsWith("Remove objective?", h.Menus.Top!.Message, StringComparison.Ordinal);
            h.Menus.Activate(); // default No
            Assert.False(h.RoeRemoved.Task.IsCompleted);
            h.Menus.Activate();
            h.Menus.Top!.SelectedButtonId = 1;
            h.Menus.Activate(); // Yes
            Assert.True(h.RoeRemoved.Task.Wait(2000));
            Assert.Equal(1, h.RoeRemoved.Task.Result);
        }

        #endregion

        #region Retail data

        [Fact]
        public void RetailTables_MeritsJobPointsAndRecordsOfEminence()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            string M(int id) => rm.TryGetString(DMsgCategory.MeritStrings, id, out var t) ? t : string.Empty;
            Assert.Equal("HP/MP", M(2));
            Assert.Equal("Max HP", M(0x40));
            Assert.Equal("Berserk Recast", M(384));
            Assert.Equal(75, StockUiProgression.CategoryMax(M(3)));
            Assert.Equal(20, StockUiProgression.CategoryMax(M(13)));
            var categories = StockUiProgression.MeritCategories(M);
            Assert.Equal(StockUiProgression.MeritCategoryCount, categories.Count);
            Assert.Equal("Rune Fencer", M(categories[^1] * 2));
            string J(int id) => rm.TryGetString(DMsgCategory.JobPointStrings, id, out var t) ? t : string.Empty;
            Assert.Equal("Mighty Strikes Effect", J(StockUiProgression.JobPointCategoryNameId(1, 0)));
            Assert.Equal(10, StockUiProgression.JobPointCategories(1, J).Count);

            var roe = RecordsOfEminenceTables.Load(rm.LoadDatBytes);
            Assert.NotNull(roe);
            var vanquish = roe!.Objective(12)!;
            // LandSandBoat roe_records.lua [12]: goal 200, repeat, sparks 1000, exp 5000.
            Assert.Equal(("Vanquish Multiple Enemies I", 200, true, 1000, 5000), (vanquish.Name, vanquish.Required, vanquish.Repeatable, vanquish.Sparks, vanquish.Experience));
            Assert.False(roe.Objective(1)!.Repeatable);
            Assert.Equal(1000, roe.Objective(5)!.Accolades); // All for One
            var top = roe.TopCategories();
            Assert.Equal(11, top.Count);
            Assert.Equal("Tutorial", top[0].Name);
            Assert.Contains(top, c => c.Name == "Unity");
            Assert.Equal("Basics", roe.Category(0xF230)!.Name);
            Assert.Equal(0xE001, roe.Category(0xF230)!.Children[0].RecordId);
        }

        [Fact]
        public void RetailMenus_ProgressionWindowsResolve()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            if (library == null) return;
            foreach (string name in new[] { StockUiProgression.MeritCategoryMenu, StockUiProgression.MeritListMenu })
            {
                Assert.True(library.TryGetMenu(name, out var list));
                Assert.Equal((16, 48, 366, 190), (list.Frame.X, list.Frame.Y, list.Frame.Width, list.Frame.Height));
                Assert.Equal(11, list.Buttons.Count(b => b.Width > 100));
            }
            Assert.True(library.TryGetMenu(StockUiProgression.MeritEditMenu, out var edit));
            Assert.Equal((612, 613), (edit.FindButton(1)!.HelpTextId, edit.FindButton(2)!.HelpTextId));
            foreach (var button in edit.Buttons) Assert.True(StockUiMenuEntries.TryGet(StockUiProgression.MeritEditMenu, button.ButtonId, out _));
            foreach (string box in new[] { StockUiProgression.MeritInfoBox, StockUiProgression.JobPointInfoBox })
            {
                Assert.True(library.TryGetMenu(box, out var info));
                Assert.Equal((16, 240, 112, 56), (info.Frame.X, info.Frame.Y, info.Frame.Width, info.Frame.Height));
            }
        }

        #endregion

        #region Helpers

        private static Func<int, string?> MeritText() => id => id switch
        {
            2 => "HP/MP",
            3 => "Adjust your maximum HP and MP.\nMax. total ability increases: 75",
            12 => "Warrior",
            13 => "Adjust various abilities for WAR lv.75 or above.\nGroup 1 max. total ability increases: 10\nGroup 2 max. total ability increases: 10",
            0x40 => "Max HP",
            0x42 => "Max MP",
            384 => "Berserk Recast",
            2048 => "Warrior Group 2",
            _ => id < 64 && id % 2 == 0 ? "MeripoName" + id / 2 : null,
        };

        /// <summary>A stand-in RoE table: the root with Tutorial and Combat, Tutorial with three objectives (the third locked).</summary>
        private static RecordsOfEminenceTables RoeTables()
        {
            static byte[] Rotate(byte[] plain)
            {
                // The tables are stored rotated right by 3 (ItemTableDecoder.DecryptBlock rotates left by 3).
                var stored = new byte[plain.Length];
                for (int i = 0; i < plain.Length; i++) stored[i] = (byte)((plain[i] >> 3) | (plain[i] << 5));
                return stored;
            }
            static void Str(byte[] block, int table, string text)
            {
                BitConverter.GetBytes(1).CopyTo(block, table);
                BitConverter.GetBytes(0x0C).CopyTo(block, table + 4);
                System.Text.Encoding.ASCII.GetBytes(text).CopyTo(block, table + 0x0C + 0x1C);
            }
            byte[] Objective(int id, string name, int required, bool repeat, int sparks = 0, int exp = 0, int accolades = 0)
            {
                var b = new byte[0x1400];
                BitConverter.GetBytes(0xE000 + id).CopyTo(b, 0);
                BitConverter.GetBytes(repeat ? 1 : 0).CopyTo(b, 8);
                BitConverter.GetBytes(required).CopyTo(b, 0x0C);
                BitConverter.GetBytes(sparks).CopyTo(b, 0x10);
                BitConverter.GetBytes(exp).CopyTo(b, 0x14);
                BitConverter.GetBytes(accolades).CopyTo(b, 0x1C);
                Str(b, 0x20, name);
                return b;
            }
            byte[] Category(int record, string name, params (int Id, uint Condition)[] children)
            {
                var b = new byte[0x1400];
                BitConverter.GetBytes(record).CopyTo(b, 0);
                BitConverter.GetBytes(children.Length).CopyTo(b, 4);
                for (int i = 0; i < children.Length; i++)
                {
                    BitConverter.GetBytes(children[i].Id).CopyTo(b, 8 + i * 20);
                    BitConverter.GetBytes(children[i].Condition).CopyTo(b, 12 + i * 20);
                }
                if (name.Length > 0) Str(b, 0x238, name);
                return b;
            }
            var objectives = Rotate(Objective(1, "First Step Forward", 1, false)
                .Concat(Objective(12, "Vanquish Multiple Enemies I", 200, true, 1000, 5000, 100))
                .Concat(Objective(13, "Vanquish Multiple Enemies II", 500, false)).ToArray());
            var categories = Rotate(Category(0xF200, "", (0xF210, 0), (0xF211, 0))
                .Concat(Category(0xF210, "Tutorial", (0xE001, 0), (0xE00C, 0x01000001), (0xE00D, 0x01000004)))
                .Concat(Category(0xF211, "Combat (Wide Area)", (0xE00C, 0))).ToArray());
            return RecordsOfEminenceTables.Parse(objectives, categories)!;
        }

        private static UiMenuDefinition Window(string name, int x, int y, int buttons, int width = 366, int height = 190, int rowWidth = 88)
        {
            var list = new List<UiMenuButton>();
            for (int i = 1; i <= buttons; i++)
            {
                list.Add(new UiMenuButton
                {
                    ButtonId = (short)i, X = 0, Y = (short)(7 + 16 * (i - 1)), Width = (short)rowWidth, Height = 16,
                    NavUp = (sbyte)(i == 1 ? buttons : i - 1), NavDown = (sbyte)(i == buttons ? 1 : i + 1), NavLeft = (sbyte)i, NavRight = (sbyte)i,
                });
            }
            return new UiMenuDefinition { Name = name, Frame = new UiMenuFrame { X = (short)x, Y = (short)y, Width = (short)width, Height = (short)height }, Buttons = list };
        }

        private static UiResourceLibrary Library() => UiResourceLibrary.FromDefinitions(new[]
        {
            Window(StockUiStatus.CommandMenu, 384, 48, 13, 112, 218),
            Window(StockUiStatusPages.MeritMenu, 384, 48, 2, 112, 42),
            Window(StockUiProgression.MeritEditMenu, 384, 48, 2, 112, 42),
            Window(StockUiStatusPages.UnityMenu, 384, 48, 3, 112, 58),
            Window(StockUiProgression.MeritCategoryMenu, 16, 48, 11, rowWidth: 356),
            Window(StockUiProgression.MeritListMenu, 16, 48, 11, rowWidth: 356),
            Window(StockUiStatusPages.JobPointMenu, 16, 48, 10, rowWidth: 356),
            Window(StockUiQuestLog.QuestKindMenu, 16, 48, 5, height: 248, rowWidth: 320),
            Window(StockUiMenuEntries.MessageYesNoMenu, 16, 208, 2, 366, 88),
        });

        private sealed class Harness
        {
            public readonly StockUiMenuController Menus = new() { Library = Library() };
            public readonly List<string> Notices = new();
            public bool InMogHouse;
            public IReadOnlyList<StockUiMerit> Merits = new[] { new StockUiMerit(0x40, 3, 4), new StockUiMerit(0x42, 0, 15) };
            public IReadOnlyList<StockUiRoeSlot> Slots = Array.Empty<StockUiRoeSlot>();
            public readonly HashSet<int> Completed = new() { 1 };
            public readonly TaskCompletionSource<(int, bool)> MeritSent = new();
            public readonly TaskCompletionSource<ushort> JobPointSent = new();
            public readonly TaskCompletionSource<int> RoeStarted = new();
            public readonly TaskCompletionSource<int> RoeRemoved = new();
            public readonly List<bool> UnityChat = new();
            public int UnityQuests;

            public Harness()
            {
                var jobs = new StockUiJobPointTotals[24];
                jobs[1] = new StockUiJobPointTotals(0, 10, 0);
                var points = new StockUiStatusPoints(0, 30, 30, true, false, jobs, 0, new int[11], _ => 0, _ => 0, null, 1);
                var roe = RoeTables();
                Menus.StatusPoints = () => points;
                Menus.KeyItemHeld = _ => true;
                Menus.InMogHouse = () => InMogHouse;
                Menus.MeritText = MeritText();
                Menus.Merits = () => Merits;
                Menus.MeritChanged = (id, raise) => { MeritSent.TrySetResult((id, raise)); return Task.CompletedTask; };
                Menus.JobPointText = id => id switch { 64 => "Mighty Strikes Effect", 66 => "Berserk Effect", _ => null };
                Menus.JobPointLevel = (job, k) => job == 1 && k == 0 ? new StockUiJobPointCategory(3, 4) : default;
                Menus.JobPointSpent = index => { JobPointSent.TrySetResult(index); return Task.CompletedTask; };
                Menus.MenuStringText = id => id switch
                {
                    108 => "Records of Eminence", 109 => "Current", 110 => "Objective List", 111 => "Set objective?",
                    112 => "This objective cannot be undertaken again.", 114 => "No current objectives.", 115 => "Remove objective?",
                    404 => "Ayame", 410 => "Naja Salaheem", _ => null,
                };
                Menus.UnityInfo = () => new StockUiUnityInfo(8, 0, 0, new uint[11], new uint[] { 0, 0, 0, 0, 0, 0, 0, 90, 0, 0, 0 });
                Menus.UnityChatChanged = active => { UnityChat.Add(active); return Task.CompletedTask; };
                Menus.UnityQuestRequested = () => UnityQuests++;
                var catalog = StockUiQuestLogCatalog.FromEntries(new Dictionary<StockUiQuestLogBook, IReadOnlyList<StockUiQuestLogEntry>>());
                Menus.QuestLogCatalog = () => catalog;
                Menus.QuestLogState = () => new StockUiQuestLogState();
                Menus.RoeTables = () => roe;
                Menus.RoeState = () => new StockUiRoeState(Slots, id => Completed.Contains(id));
                Menus.RoeStarted = id => { RoeStarted.TrySetResult(id); return Task.CompletedTask; };
                Menus.RoeRemoved = id => { RoeRemoved.TrySetResult(id); return Task.CompletedTask; };
                Menus.NoticePosted += Notices.Add;
            }
        }

        #endregion
    }
}
