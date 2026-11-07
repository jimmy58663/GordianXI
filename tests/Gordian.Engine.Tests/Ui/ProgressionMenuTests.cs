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
            Assert.Equal(15, StockUiProgression.CategoryMax("Adjust weapon skills for levels 96 and above. Total ability increases: 15."));
            Assert.Equal(0, StockUiProgression.CategoryMax(null));
        }

        [Fact]
        public void MeritRows_TotalPerCategory_GreyedWithoutANextCost()
        {
            var merits = new[] { new StockUiMerit(0x40, 3, 4), new StockUiMerit(0x42, 0, 15), new StockUiMerit(384, 1, 2), new StockUiMerit(2048, 3, 1) };
            var text = MeritText();
            var categories = StockUiProgression.MeritCategories(text);
            Assert.Equal(new[] { 1, 6 }, categories);
            var rows = StockUiProgression.MeritCategoryRows(categories, text);
            Assert.Equal(("HP/MP", (string?)null), (rows[0].Text, rows[0].Value));
            Assert.Equal(19, StockUiProgression.CategoryTotal(1, merits));
            var hp = StockUiProgression.MeritsOf(1, merits, text);
            Assert.Equal(new[] { 0x40, 0x42 }, hp.Select(m => m.Id));
            var meritRows = StockUiProgression.MeritRows(hp, text);
            Assert.Equal(("Max HP", "4/15", (UiColor?)null), (meritRows[0].Text, meritRows[0].Value, meritRows[0].Color));
            Assert.Equal("NEXT 3 P", meritRows[0].Note);
            Assert.Equal(StockUiProgression.Greyed, meritRows[1].Color);
            Assert.Null(meritRows[1].Note);
            Assert.Equal(new[] { 384, 2048 }, StockUiProgression.MeritsOf(6, merits, text).Select(m => m.Id));
        }

        [Fact]
        public void JobCategories_SplitIntoTwoGroups()
        {
            var merits = new[] { new StockUiMerit(384, 1, 2), new StockUiMerit(386, 1, 1), new StockUiMerit(2048, 3, 1) };
            var text = MeritText();
            Assert.True(StockUiProgression.HasMeritGroups(6));
            Assert.False(StockUiProgression.HasMeritGroups(1));
            Assert.False(StockUiProgression.HasMeritGroups(StockUiProgression.WeaponSkillsCategory));
            Assert.Equal((1, 2), (StockUiProgression.MeritGroupOf(384), StockUiProgression.MeritGroupOf(2048)));
            Assert.Equal((1, 20, 21, 22), (StockUiProgression.JobOfCategory(6), StockUiProgression.JobOfCategory(25), StockUiProgression.JobOfCategory(27), StockUiProgression.JobOfCategory(28)));
            Assert.Equal(new[] { 2048 }, StockUiProgression.MeritsOf(6, merits, _ => "Merit", 2).Select(m => m.Id));
            Assert.Equal(10, StockUiProgression.GroupMax("Adjust.\nGroup 1 max. total ability increases: 10\nGroup 2 max. total ability increases: 10", 2));
            Func<int, string?> names = id => id switch
            {
                13 => "Group 1 max. total ability increases: 10\nGroup 2 max. total ability increases: 10",
                384 => "Berserk Recast", 386 => "Defender Recast", 2048 => "Warrior's Charge", _ => null,
            };
            var (rows, keys) = StockUiProgression.MeritListRows(6, merits, names, "NEXT", mainJob: 1);
            Assert.Equal(new[] { 0, 384, 386, 0, 2048 }, keys);
            Assert.Equal(new[] { "Group1", "Berserk Recast", "Defender Recast", "Group2", "Warrior's Charge" }, rows.Select(r => r.Text));
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, rows.Select(r => r.ButtonId));
            Assert.True(rows[0].Header);
            Assert.Equal("(Max.total ability increases:  10 )", rows[3].Note);
            Assert.Null(rows[1].Color);
            Assert.Equal(StockUiProgression.GroupYellow, rows[4].Color);
            // Another job's merits are grey, NEXT kept.
            var (other, _) = StockUiProgression.MeritListRows(6, merits, names, "NEXT", mainJob: 10);
            Assert.Equal((StockUiProgression.Greyed, "NEXT 1 P"), (other[1].Color!.Value, other[1].Note));
        }

        [Fact]
        public void MeritAtItsMost_UpgradesBlue_NoNext_NameKeepsItsColour()
        {
            // The retail client, 2026-11-07: Lullaby Recast at 5 shows "5" in blue, no NEXT, its name white.
            var atMost = new StockUiMerit(0x40, 9, 15);
            Assert.Equal(0, StockUiProgression.NextCost(atMost));
            Assert.True(StockUiProgression.AtMeritMax(atMost));
            var row = StockUiProgression.MeritRows(new[] { atMost }, _ => "Max HP").Single();
            Assert.Equal(("15/15", (string?)null, (UiColor?)null, (UiColor?)StockUiProgression.AtMost), (row.Value, row.Note, row.Color, row.ValueColor));
            Assert.Equal(15, StockUiProgression.CategoryMax("Adjust weapon skills. Total ability increases: 15."));
            Assert.Equal("Use 1 merit point to raise your \"Lullaby Recast\" by one level?", StockUiProgression.MeritRaiseQuestion(1, "Lullaby Recast"));
            Assert.Equal("Use 5 merit points to raise your \"Lullaby Recast\" by one level?", StockUiProgression.MeritRaiseQuestion(5, "Lullaby Recast"));
            Assert.Equal("Lower your \"Lullaby Recast\" by one level?\nThe point(s) used to raise it will not be reimbursed.", StockUiProgression.MeritLowerQuestion("Lullaby Recast"));
            Assert.Equal("Use 0 alter ego points to raise your \"Max HP\" by one level?", StockUiProgression.AlterEgoRaiseQuestion(0, "Max HP"));
        }

        [Fact]
        public void JobCategory_ListsItsGroupsUnderHeaders()
        {
            var h = new Harness { Merits = new[] { new StockUiMerit(0x40, 3, 4), new StockUiMerit(384, 1, 2), new StockUiMerit(2048, 3, 1) } };
            h.Menus.MeritText = id => id switch { 2 => "HP/MP", 12 => "Warrior", 13 => "Group 1 max. total ability increases: 10", 64 => "Max HP", 384 => "Berserk Recast", 2048 => "Warrior's Charge", _ => null };
            Assert.True(h.Menus.Open(StockUiStatusPages.MeritMenu));
            h.Menus.Activate();
            h.Menus.Move(InputAction.MenuDown); // Warrior
            h.Menus.Activate();
            var list = h.Menus.Top!;
            Assert.Equal((StockUiProgressionList.Merits, 6), (list.ProgressionList, list.ProgressionKey));
            Assert.Equal(new[] { "Group1", "Berserk Recast", "Group2", "Warrior's Charge" }, list.Rows.Select(r => r.Text));
            h.InMogHouse = true;
            h.Menus.Activate(); // the header: nothing
            Assert.Same(list, h.Menus.Top);
            Assert.Equal("NEXT 3 P", list.Rows[3].Note);
            h.Merits = new[] { new StockUiMerit(2048, 4, 2) };
            h.Menus.OnStatusPointsChanged();
            Assert.Equal(("2/5", "NEXT 4 P"), (list.Rows[^1].Value, list.Rows[^1].Note));
        }

        [Fact]
        public void WeaponSkills_ListsAfterOthers_GreyedWithoutTheKeyItem()
        {
            Func<int, string?> text = id => id switch
            {
                2 => "HP/MP", 4 => "Attributes", 6 => "Combat Skills", 8 => "Magic Skills", 10 => "Others", 12 => "Warrior", 52 => "Weapon Skills", _ => null,
            };
            var categories = StockUiProgression.MeritCategories(text);
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 26, 6 }, categories);
            var locked = StockUiProgression.MeritCategoryRows(categories, text, weaponSkillsOpen: false);
            Assert.Equal(("Weapon Skills", (UiColor?)StockUiProgression.Greyed), (locked[5].Text, locked[5].Color));
            Assert.Null(locked[4].Color);
            Assert.Null(StockUiProgression.MeritCategoryRows(categories, text)[5].Color);
        }

        [Theory]
        [InlineData(0x40, 15)] // Max HP
        [InlineData(0x44, 45)] // Maximum Merit Points
        [InlineData(0x80, 15)] // STR
        [InlineData(0xC0, 8)]  // Hand-to-Hand
        [InlineData(0x100, 8)] // Divine Magic
        [InlineData(0x140, 5)] // Enmity
        [InlineData(384, 5)]   // Berserk Recast
        [InlineData(2048, 5)]  // Warrior's second group
        public void MeritMax_ByMerit(int merit, int most) => Assert.Equal(most, StockUiProgression.MeritMax(merit));

        [Fact]
        public void JobGifts_ListTheNamedSlots_GreyUntilEarned()
        {
            Func<int, string?> text = id => id switch { 512 => "Magic Defense Bonus", 514 => "Capacity Point Bonus", 516 => ".", 558 => "Elemental Bonus", _ => null };
            Assert.Equal(512, StockUiProgression.JobGiftNameId(2, 0));
            var (rows, keys) = StockUiProgression.JobGiftRows(2, 5, text);
            Assert.Equal(new[] { 0, 1, 23 }, keys);
            Assert.Equal(("Magic Defense Bonus", "5", (UiColor?)null), (rows[0].Text, rows[0].Value, rows[0].Color));
            var (locked, _) = StockUiProgression.JobGiftRows(2, 4, text);
            Assert.Equal(StockUiProgression.Greyed, locked[0].Color);
            // The retail lists of image-31 and image-32.
            Assert.Equal(new[] { 5, 5, 10, 20, 25, 30, 45, 50, 55, 60 }, Enumerable.Range(0, 10).Select(StockUiProgression.JobGiftThreshold));
            Assert.Equal(new[] { 280, 320, 355, 360, 405, 445, 450, 500, 500, 545 },
                new[] { 22, 24, 25, 26, 27, 28, 29, 30, 31, 32 }.Select(StockUiProgression.JobGiftThreshold));
        }

        [Fact]
        public void BasicMessages_NameMeritsJobPointsAndObjectives()
        {
            var old = Gordian.Engine.Events.EventDialogController.NameResolver;
            Gordian.Engine.Events.EventDialogController.NameResolver = (kind, id) => (kind, id) switch
            {
                (Gordian.Engine.Events.EventMessageFormatter.MeritKind, 32) => "Max HP",
                (Gordian.Engine.Events.EventMessageFormatter.JobPointKind, 32) => "Mighty Strikes Effect",
                (Gordian.Engine.Events.EventMessageFormatter.RoeObjectiveKind, 1) => "First Step Forward",
                _ => null,
            };
            try
            {
                string Line(byte[] raw, uint param, uint value) => CombatLogFormatter.FormatBattleMessage(
                    new CombatMessageRecord { MessageId = 1, Param = param, Value = value }, _ => "Me", battleMessages: _ => EventMessageDecoder.Decode(raw));
                // Messages 380, 720 and 704 of file 7027.
                var merit = Convert.FromHexString("596F7572207FB000206D6F64696669636174696F6E2068617320726973656E20746F206C6576656C2012012E7F310007");
                var jobPoint = Convert.FromHexString("596F7572207FB100206D6F64696669636174696F6E2068617320726973656E20746F206C6576656C2012012E7F310007");
                var taken = Convert.FromHexString("596F75206861766520756E64657274616B656E2087B2010541828080802E87B37F310007");
                Assert.Equal("Your Max HP modification has risen to level 3.", Line(merit, 32, 3));
                Assert.Equal("Your Mighty Strikes Effect modification has risen to level 3.", Line(jobPoint, 32, 3));
                Assert.Equal("You have undertaken \"First Step Forward.\"", Line(taken, 1, 0));
            }
            finally
            {
                Gordian.Engine.Events.EventDialogController.NameResolver = old;
            }
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
            Assert.Equal(8, rows.Count);
            Assert.Equal("Unity 2", rows[0].Value);
            Assert.Equal("--", rows[1].Value); // last week: nothing sent
            Assert.Equal("1", rows[2].Value);
            Assert.Equal(("Active", false), (rows[StockUiProgression.UnityChatActiveRow - 1].Text, rows[StockUiProgression.UnityChatActiveRow - 1].Marked));
            Assert.Equal(("Inactive", true), (rows[StockUiProgression.UnityChatInactiveRow - 1].Text, rows[StockUiProgression.UnityChatInactiveRow - 1].Marked));
            // Outside a Unity: "None", "--" and no evaluations (2026-11-07).
            var none = StockUiProgression.UnityInfoRows(new StockUiUnityInfo(0, 0, 0, new uint[11], new uint[11]), true, null);
            Assert.Equal(("None", "--", ""), (none[0].Value, none[1].Value, none[3].Value));
            // The rankings: most points first, ties in leader order sharing a rank, "0pt".
            var ranking = StockUiProgression.UnityRankingRows(info, partial: true, null);
            Assert.Equal(new[] { "Unity 2", "Unity 3", "Unity 11", "Unity 1", "Unity 4" }, ranking.Take(5).Select(r => r.Text));
            Assert.Equal(new[] { "1", "1", "3", "4", "5" }, ranking.Take(5).Select(r => r.Note));
            Assert.Equal(("50pt", "0pt"), (ranking[0].Value, ranking[4].Value));
            Assert.True(ranking[0].Marked);
            var last = StockUiProgression.UnityRankingRows(info, partial: false, null);
            Assert.Equal(("Unity 1", "1", "0pt"), (last[0].Text, last[0].Note, last[0].Value));
            Assert.Equal(new[] { "Ranking Results", "Partial Ranking Results" }, StockUiProgression.UnityRankingChoiceRows(null).Select(r => r.Text));
            // Unity Lists: "Unity" first, then the leaders with their short names.
            var (lists, keys) = StockUiProgression.UnityListsRows(id => id == 405 ? "Invincible Shield" : null);
            Assert.Equal(12, lists.Count);
            Assert.Equal(("Unity", "(Unity)", 0), (lists[0].Text, lists[0].Note, keys[0]));
            Assert.Equal(("Invincible Shield", "(IShield)", 3), (lists[3].Text, lists[3].Note, keys[3]));
            Assert.Equal(3u << 22, StockUiProgression.UnitySearchFlags(3));
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

            Assert.Equal("1000 Spa.  5000 EXP  100 Acc.", StockUiRecordsOfEminence.RewardLine(tables.Objective(12)!, null));
            Assert.Equal(1, StockUiRecordsOfEminence.ActiveCount(state));
        }

        [Fact]
        public void RoeConditions_ZoneUnityAndRotation_EmptySectionsHide()
        {
            bool Show(byte kind, int value, StockUiRoeState state) =>
                StockUiRecordsOfEminence.IsVisible(new RoeChild(0xE001, new[] { new RoeCondition(kind, value) }), state);
            var state = new StockUiRoeState(Array.Empty<StockUiRoeSlot>(), id => id == 1)
            {
                ZoneVisited = zone => zone == 100, UnityLeader = 3, UnitySharedRotation = 2, UnityLeaderRotation = 1,
            };
            Assert.True(Show(RoeCondition.ZoneVisited, 100, state));
            Assert.False(Show(RoeCondition.ZoneVisited, 101, state));
            Assert.True(Show(RoeCondition.UnityLeader, 3, state));
            Assert.False(Show(RoeCondition.UnityLeader, 4, state));
            Assert.True(Show(RoeCondition.UnityRotation, 2, state));
            Assert.False(Show(RoeCondition.UnityRotation, 3, state));
            Assert.True(Show(RoeCondition.UnityRotation, 0x101, state));
            Assert.False(Show(RoeCondition.UnityRotation, 0x100, state));
            // Before S2C 0x110 every rotation counts; without S2C 0x008 every zone does.
            var unknown = new StockUiRoeState(Array.Empty<StockUiRoeSlot>(), _ => false);
            Assert.True(Show(RoeCondition.UnityRotation, 4, unknown));
            Assert.True(Show(RoeCondition.ZoneVisited, 200, unknown));

            // The Locked section's only objective needs objective 4: until it is done the section does not show.
            var tables = RoeTables();
            var none = new StockUiRoeState(Array.Empty<StockUiRoeSlot>(), _ => false);
            var (top, _) = StockUiRecordsOfEminence.CategoryRows(tables, RecordsOfEminenceTables.RootCategory, none);
            Assert.Equal(new[] { "Tutorial", "Combat (Wide Area)" }, top.Select(r => r.Text));
            var done = new StockUiRoeState(Array.Empty<StockUiRoeSlot>(), id => id == 4);
            Assert.Contains("Locked", StockUiRecordsOfEminence.CategoryRows(tables, RecordsOfEminenceTables.RootCategory, done).Rows.Select(r => r.Text));
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

            // Outside the Mog House: the client's line, nothing opens.
            h.Menus.Activate();
            Assert.Contains(StockUiProgression.OutsideMogHouse, h.Notices);
            Assert.Same(list, h.Menus.Top);

            h.InMogHouse = true;
            h.Menus.Activate();
            var edit = h.Menus.Top!;
            Assert.Equal(StockUiProgression.MeritEditMenu, edit.Name);
            h.Menus.Activate(); // Raise: the bare Yes / No, the cursor on No; merit3 stays
            Assert.True(h.Menus.Top!.IsPrompt);
            Assert.Null(h.Menus.Top!.Message);
            Assert.Equal(2, h.Menus.Top!.SelectedButtonId);
            h.Menus.Activate(); // No
            Assert.Same(edit, h.Menus.Top);
            Assert.False(h.MeritSent.Task.IsCompleted);
            h.Menus.Activate();
            h.Menus.Top!.SelectedButtonId = 1;
            h.Menus.Activate(); // Commands Yes: the question in words, the cursor on No (retail client, 2026-11-07)
            var question = WaitForMessage(h, "Use 3 merit points to raise your \"Max HP\" by one level?");
            Assert.Equal(2, question.SelectedButtonId);
            Assert.True(question.PromptTextLeft);
            question.SelectedButtonId = 1;
            h.Menus.Activate(); // Yes: sent, merit3 closes
            Assert.Equal((0x40, true), h.MeritSent.Task.Wait(10000) ? h.MeritSent.Task.Result : default);
            for (int i = 0; i < 1000 && !ReferenceEquals(h.Menus.Top, list); i++) System.Threading.Thread.Sleep(10);
            Assert.Same(list, h.Menus.Top);

            // Max MP at its most (the server still sends a cost): asked the same with that cost; Lower asks its own question.
            h.Merits = new[] { new StockUiMerit(0x40, 3, 4), new StockUiMerit(0x42, 9, 15) };
            h.Menus.OnStatusPointsChanged();
            h.Menus.Move(InputAction.MenuDown);
            h.Menus.Activate();
            h.Menus.Activate(); // Raise
            h.Menus.Top!.SelectedButtonId = 1;
            h.Menus.Activate();
            WaitForMessage(h, "Use 9 merit points to raise your \"Max MP\" by one level?");
            h.Menus.CloseTop();
            h.Menus.Top!.SelectedButtonId = StockUiProgression.MeritLowerButton;
            h.Menus.Activate(); // Lower
            h.Menus.Top!.SelectedButtonId = 1;
            h.Menus.Activate();
            var lower = WaitForMessage(h, "Lower your \"Max MP\" by one level?\nThe point(s) used to raise it will not be reimbursed.");
            lower.SelectedButtonId = 1;
            h.Menus.Activate();
            Assert.Equal((0x42, false), h.MeritLowered.Task.Wait(10000) ? h.MeritLowered.Task.Result : default);
            for (int i = 0; i < 1000 && !ReferenceEquals(h.Menus.Top, list); i++) System.Threading.Thread.Sleep(10);

            // S2C 0x08C: the open lists follow, the cursor kept.
            h.Merits = new[] { new StockUiMerit(0x40, 4, 5), new StockUiMerit(0x42, 0, 15) };
            h.Menus.OnStatusPointsChanged();
            Assert.Equal("5/15", list.Rows[0].Value);
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
            Assert.True(h.JobPointSent.Task.Wait(10000));
            Assert.Equal((ushort)0x20, h.JobPointSent.Task.Result);

            // Left / Right turns to the job's gifts and back; Confirm on a gift does nothing.
            Assert.Equal(StockUiStatusPages.JobPointMenu, categories.Name);
            h.Menus.Move(InputAction.MenuRight);
            Assert.Equal(StockUiProgressionList.JobGifts, categories.ProgressionList);
            Assert.Equal(("Physical Defense Bonus", "5"), (categories.Rows[0].Text, categories.Rows[0].Value));
            Assert.Equal(StockUiProgression.Greyed, categories.Rows[0].Color);
            h.Menus.Activate();
            Assert.Same(categories, h.Menus.Top);
            h.Menus.Move(InputAction.MenuLeft);
            Assert.Equal(StockUiProgressionList.JobPointCategories, categories.ProgressionList);
            Assert.Equal(StockUiProgression.JobPointValueRight, categories.StatusValueRight);
        }

        [Fact]
        public void Unity_InfoTogglesChat_RankingsTurn_ListsSearch()
        {
            var h = new Harness();
            Assert.True(h.Menus.Open(StockUiStatusPages.UnityMenu));
            h.Menus.Activate(); // Unity Info
            var info = h.Menus.Top!;
            Assert.Equal(StockUiProgressionList.UnityInfo, info.ProgressionList);
            Assert.Equal(("Unity Information", false), (info.RoeListTitle, info.ListTitleArrows));
            Assert.Equal("Naja Salaheem", info.Rows[0].Value);
            Assert.Equal(StockUiProgression.UnityChatActiveRow - 1, info.SelectedStatusRow);
            h.Menus.Move(InputAction.MenuUp); // the cursor stays on Active / Inactive
            Assert.Equal(StockUiProgression.UnityChatActiveRow - 1, info.SelectedStatusRow);
            h.Menus.Move(InputAction.MenuDown); // Inactive
            h.Menus.Move(InputAction.MenuDown);
            Assert.Equal(StockUiProgression.UnityChatInactiveRow - 1, info.SelectedStatusRow);
            h.Menus.Activate();
            Assert.Equal(new[] { false }, h.UnityChat);
            Assert.False(h.Menus.UnityChatActive);
            Assert.True(info.Rows[StockUiProgression.UnityChatInactiveRow - 1].Marked);

            // Ranking Info: a choice; C2S 0x117 goes as a ranking opens. Left / Right turns between the two.
            h.Menus.CloseTop();
            h.Menus.Top!.SelectedButtonId = StockUiStatusPages.UnityRankingButton;
            h.Menus.Activate();
            var choice = h.Menus.Top!;
            Assert.Equal(0, h.UnityQuests);
            Assert.Equal((StockUiProgressionList.UnityRanking, "Ranking Information"), (choice.ProgressionList, choice.RoeListTitle));
            Assert.Equal(new[] { "Ranking Results", "Partial Ranking Results" }, choice.Rows.Select(r => r.Text));
            h.Menus.Activate(); // Ranking Results: last week's (nothing)
            var ranking = h.Menus.Top!;
            Assert.Equal(1, h.UnityQuests);
            Assert.Equal((StockUiProgressionList.UnityRankingList, "Ranking Results", true), (ranking.ProgressionList, ranking.RoeListTitle, ranking.ListTitleArrows));
            Assert.Equal(("1", "0pt"), (ranking.Rows[0].Note, ranking.Rows[0].Value));
            h.Menus.Move(InputAction.MenuRight); // Partial Ranking Results: this week's
            Assert.Equal("Partial Ranking Results", ranking.RoeListTitle);
            Assert.Equal(("Naja Salaheem", "1", "90pt"), (ranking.Rows[0].Text, ranking.Rows[0].Note, ranking.Rows[0].Value));
            Assert.Equal("2", ranking.Rows[1].Note);
            h.Menus.Activate(); // nothing
            Assert.Same(ranking, h.Menus.Top);
            h.Menus.CloseTop();
            h.Menus.CloseTop();

            // Unity Lists: "Unity" first under the cursor; Confirm closes it and searches (no search server here).
            h.Menus.Top!.SelectedButtonId = 3;
            h.Menus.Activate();
            var lists = h.Menus.Top!;
            Assert.Equal((StockUiProgressionList.UnityLeaders, StockUiProgression.UnityListsMenu), (lists.ProgressionList, lists.Name));
            Assert.Equal(("Unity", 0), (lists.Rows[0].Text, lists.SelectedStatusRow));
            h.Menus.Activate();
            Assert.NotSame(lists, h.Menus.Top);
            Assert.Contains("Unity Lists: searching is not available in this session.", h.Notices);
        }

        [Fact]
        public void AlterEgo_AsksInTheMogHouse_ThenSends()
        {
            var h = new Harness();
            h.Menus.AlterEgoText = id => id switch { 2 => "HP/MP", 16 => "Max HP", _ => null };
            Assert.True(h.Menus.Open(StockUiStatus.CommandMenu));
            h.Menus.Top!.SelectedButtonId = StockUiStatus.AlterEgoButton;
            h.Menus.Activate();
            h.Menus.Activate(); // HP/MP
            var items = h.Menus.Top!;
            Assert.Equal((StockUiProgression.Greyed, (string?)null), (items.Rows[0].Color!.Value, items.Rows[0].Note));
            h.Menus.Activate(); // outside: the merits' line, nothing else
            Assert.Contains(StockUiProgression.OutsideMogHouse, h.Notices);
            Assert.Same(items, h.Menus.Top);

            h.InMogHouse = true;
            h.Menus.OnStatusPointsChanged();
            Assert.Equal(((UiColor?)null, "NEXT 0 P"), (items.Rows[0].Color, items.Rows[0].Note));
            h.Menus.Activate();
            var question = WaitForMessage(h, "Use 0 alter ego points to raise your \"Max HP\" by one level?");
            Assert.Equal(2, question.SelectedButtonId);
            question.SelectedButtonId = 1;
            h.Menus.Activate();
            Assert.True(h.AlterEgoSent.Task.Wait(10000));
            Assert.Equal(Gordian.Core.Network.Packets.AlterEgoCategory.MaxHp, h.AlterEgoSent.Task.Result);
        }

        [Fact]
        public void RecordsOfEminence_TakeAndRemoveObjectives()
        {
            var h = new Harness();
            var kind = h.Menus.OpenQuests(null)!;
            Assert.Equal(new[] { "Current", "Completed", "Current", "Objective List" }, kind.Rows.Select(r => r.Text));
            Assert.Equal("0/30", kind.Rows[2].Value);
            kind.SelectedButtonId = StockUiRecordsOfEminence.CurrentButton;

            // Current with nothing taken: "No current objectives."
            h.Menus.Activate();
            Assert.Contains("No current objectives.", h.Notices);
            Assert.Equal(1, h.UnityQuests);

            // Objective List: Tutorial > First Step Forward is done for good; Vanquish Multiple Enemies I is taken.
            kind.SelectedButtonId = StockUiRecordsOfEminence.ObjectiveListButton;
            h.Menus.Activate();
            Assert.Equal(StockUiProgressionList.RoeCategories, h.Menus.Top!.ProgressionList);
            h.Menus.Activate(); // Tutorial
            var objectives = h.Menus.Top!;
            Assert.Equal(StockUiProgressionList.RoeObjectives, objectives.ProgressionList);
            Assert.Equal("Tutorial", objectives.RoeListTitle);
            h.Menus.Activate(); // First Step Forward: its page, no question
            Assert.Equal((StockUiQuestLogView.RoeDetail, true), (h.Menus.Top!.QuestLogView, h.Menus.Top!.RoeDetail!.Done));
            h.Menus.CloseTop();
            Assert.Same(objectives, h.Menus.Top);

            // Right: the category beside it; Left comes back.
            h.Menus.Move(InputAction.MenuRight);
            Assert.Equal("Combat (Wide Area)", objectives.RoeListTitle);
            h.Menus.Move(InputAction.MenuLeft);
            Assert.Equal("Tutorial", objectives.RoeListTitle);

            h.Completed.Clear();
            h.Menus.OnStatusPointsChanged();
            h.Menus.Activate();
            Assert.Equal("Set objective?", h.Menus.Top!.Message);
            Assert.Equal(1, h.Menus.Top!.SelectedButtonId); // the cursor on Yes
            h.Menus.Activate(); // Yes: the page stays up, the objective now taken (retail client, 2026-11-07)
            Assert.True(h.RoeStarted.Task.Wait(10000));
            Assert.Equal(1, h.RoeStarted.Task.Result);
            for (int i = 0; i < 1000 && h.Menus.Top?.RoeDetail?.Taken != true; i++) System.Threading.Thread.Sleep(10);
            Assert.Equal((StockUiQuestLogView.RoeDetail, true), (h.Menus.Top!.QuestLogView, h.Menus.Top!.RoeDetail!.Taken));
            h.Menus.CloseTop();
            Assert.Same(objectives, h.Menus.Top);

            // S2C 0x111 lists it: the row shows its progress; Confirm now asks to remove it.
            h.Slots = new[] { new StockUiRoeSlot(0, 1, 0) };
            h.Menus.OnStatusPointsChanged();
            Assert.Equal("0/1", objectives.Rows[0].Value);
            Assert.Equal("1/30", kind.Rows[2].Value);
            h.Menus.Activate();
            Assert.Equal("Remove objective?", h.Menus.Top!.Message);
            h.Menus.Activate(); // default No: the page stays
            Assert.False(h.RoeRemoved.Task.IsCompleted);
            Assert.Equal(StockUiQuestLogView.RoeDetail, h.Menus.Top!.QuestLogView);
            h.Menus.CloseTop();

            // With progress, Yes asks again: the reset warning, the cursor on No; Yes removes it and closes the page.
            h.Slots = new[] { new StockUiRoeSlot(0, 1, 1) };
            h.Menus.OnStatusPointsChanged();
            h.Menus.Activate();
            h.Menus.Top!.SelectedButtonId = 1;
            h.Menus.Activate(); // Yes
            var warning = WaitForMessage(h, "Your progress will be reset if you cancel this objective. \nAre you sure?");
            Assert.Equal(2, warning.SelectedButtonId);
            Assert.False(h.RoeRemoved.Task.IsCompleted);
            warning.SelectedButtonId = 1;
            h.Menus.Activate();
            Assert.True(h.RoeRemoved.Task.Wait(10000));
            Assert.Equal(1, h.RoeRemoved.Task.Result);
            for (int i = 0; i < 1000 && !ReferenceEquals(h.Menus.Top, objectives); i++) System.Threading.Thread.Sleep(10);
            Assert.Same(objectives, h.Menus.Top);
        }

        /// <summary>Waits for a prompt with <paramref name="message"/> on top: the second questions open from the thread pool.</summary>
        private static StockUiOpenMenu WaitForMessage(Harness h, string message)
        {
            for (int i = 0; i < 1000 && h.Menus.Top?.Message != message; i++) System.Threading.Thread.Sleep(10);
            Assert.Equal(message, h.Menus.Top?.Message);
            Assert.True(h.Menus.Top!.IsPrompt);
            return h.Menus.Top!;
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
            Assert.True(library.TryGetMenu(StockUiProgression.MeritInfoBox, out var info));
            Assert.Equal((16, 240, 112, 56), (info.Frame.X, info.Frame.Y, info.Frame.Width, info.Frame.Height));
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
            var categories = Rotate(Category(0xF200, "", (0xF210, 0), (0xF211, 0), (0xF212, 0))
                .Concat(Category(0xF210, "Tutorial", (0xE001, 0), (0xE00C, 0x01000001), (0xE00D, 0x01000004)))
                .Concat(Category(0xF211, "Combat (Wide Area)", (0xE00C, 0)))
                .Concat(Category(0xF212, "Locked", (0xE00D, 0x01000004))).ToArray());
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
            Window(StockUiMenuEntries.MessageYesNoMenu, 130, 208, 2, 252, 88),
            Window("query", 16, 208, 3, 366, 88),
            Window(StockUiProgression.UnityListsMenu, 16, 208, 5, 366, 88, rowWidth: 356),
            Window(StockUiStatusPages.AlterEgoMenu, 16, 48, 3, rowWidth: 356),
            Window(StockUiStatusPages.AlterEgoItemMenu, 16, 48, 7, rowWidth: 356),
            Window(StockUiMenuEntries.YesNoMenu, 384, 96, 2, 112, 42),
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
            public readonly TaskCompletionSource<(int, bool)> MeritLowered = new();
            public readonly TaskCompletionSource<Gordian.Core.Network.Packets.AlterEgoCategory> AlterEgoSent = new();
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
                Menus.MeritChanged = (id, raise) => { (raise ? MeritSent : MeritLowered).TrySetResult((id, raise)); return Task.CompletedTask; };
                Menus.AlterEgoNextCost = _ => 0;
                Menus.AlterEgoSpent = category => { AlterEgoSent.TrySetResult(category); return Task.CompletedTask; };
                Menus.JobPointText = id => id switch { 64 => "Mighty Strikes Effect", 66 => "Berserk Effect", _ => null };
                Menus.JobPointLevel = (job, k) => job == 1 && k == 0 ? new StockUiJobPointCategory(3, 4) : default;
                Menus.JobPointSpent = index => { JobPointSent.TrySetResult(index); return Task.CompletedTask; };
                Menus.JobGiftText = id => id == 0 ? "Physical Defense Bonus" : null;
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
