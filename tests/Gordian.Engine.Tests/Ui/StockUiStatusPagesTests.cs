// tests/Gordian.Engine.Tests/Ui/StockUiStatusPagesTests.cs
using System;
using System.IO;
using System.Linq;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Ui
{
    /// <summary>The Status window's own windows (#487): the currency tables, the lists' rows, Play Time and Master Levels.</summary>
    public class StockUiStatusPagesTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        [Fact]
        public void CurrencyTables_ListEveryCurrencyOnce()
        {
            Assert.Equal(Enum.GetValues<Currency1Kind>().Select(k => (int)k).Order(), StockUiStatusPages.Currencies1.Select(r => r.Kind).Order());
            Assert.Equal(Enum.GetValues<Currency2Kind>().Select(k => (int)k).Order(), StockUiStatusPages.Currencies2.Select(r => r.Kind).Order());
            var names = StockUiStatusPages.Currencies1.Concat(StockUiStatusPages.Currencies2).Select(r => r.NameId).ToList();
            Assert.Equal(names.Count, names.Distinct().Count());
            var help = StockUiStatusPages.Currencies1.Concat(StockUiStatusPages.Currencies2).Select(r => r.HelpId).Where(h => h >= 0).ToList();
            Assert.Equal(help.Count, help.Distinct().Count());
        }

        [Fact]
        public void CurrencyRows_NameTheCurrencyAndItsAmount()
        {
            var points = Points(c1: kind => kind == Currency1Kind.SparksOfEminence ? 99999 : 0);
            var rows = StockUiStatusPages.CurrencyListRows(false, points, id => "name " + id);
            int sparks = StockUiStatusPages.Currencies1.ToList().FindIndex(r => r.Kind == (int)Currency1Kind.SparksOfEminence);
            Assert.Equal("name 178", rows[sparks].Text);
            Assert.Equal("99,999", rows[sparks].Value);
            Assert.Equal(sparks + 1, rows[sparks].ButtonId);
            Assert.Equal(722, StockUiStatusPages.CurrencyHelpIds(false)[0]);
            Assert.Equal(777, StockUiStatusPages.CurrencyHelpIds(true)[0]);
        }

        /// <summary>The names as the English client writes them (ROM/165/77).</summary>
        [Fact]
        public void RetailCurrencyNames_ComeFromTheMenuStrings()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            string Name(int id) => rm.TryGetString(DMsgCategory.MenuStrings, id, out var text) ? text : string.Empty;
            foreach (var row in StockUiStatusPages.Currencies1.Concat(StockUiStatusPages.Currencies2))
            {
                Assert.False(string.IsNullOrWhiteSpace(Name(row.NameId)), $"currency {row.Kind}: name {row.NameId} is empty");
            }
            string NameOf1(Currency1Kind kind) => Name(StockUiStatusPages.Currencies1.First(r => r.Kind == (int)kind).NameId);
            string NameOf2(Currency2Kind kind) => Name(StockUiStatusPages.Currencies2.First(r => r.Kind == (int)kind).NameId);
            Assert.Equal("Conquest Points (San d'Oria)", NameOf1(Currency1Kind.ConquestPointsSandoria));
            Assert.Equal("Daily Tally", NameOf1(Currency1Kind.DailyTally));
            Assert.Equal("Research Marks", NameOf1(Currency1Kind.ResearchMarks));
            Assert.Equal("Allied Notes", NameOf1(Currency1Kind.AlliedNotes));
            Assert.Equal("Moblin Pheromone Sacks", NameOf1(Currency1Kind.MoblinPheromoneSacks));
            Assert.Equal("Primacy Plans Stored", NameOf1(Currency1Kind.PrimacyPlans));
            Assert.Equal("Deeds", NameOf1(Currency1Kind.Deeds));
            Assert.Equal("Bayld", NameOf2(Currency2Kind.Bayld));
            Assert.Equal("Ghastly Stones Stored", NameOf2(Currency2Kind.GhastlyStones));
            Assert.Equal("Duskorb Stones +2 Stored", NameOf2(Currency2Kind.DuskorbStonesPlus2));
            Assert.Equal("Fire Crystals Set", NameOf2(Currency2Kind.FireCrystalsSet));
            Assert.Equal("Darkness Spheres Set", NameOf2(Currency2Kind.DarknessSpheresSet));
            Assert.Equal("Gallimaufry", NameOf2(Currency2Kind.Gallimaufry));
            Assert.True(rm.TryGetString(DMsgCategory.MenuHelp, 722, out var help));
            Assert.Contains("San d'Oria", help);
        }

        [Fact]
        public void JobPointAndAlterEgoRows()
        {
            var jobs = new StockUiJobPointTotals[24];
            jobs[22] = new StockUiJobPointTotals(100, 7, 500);
            var upgrades = new int[StockUiStatusPages.AlterEgoCategories.Count];
            upgrades[^1] = 2;
            var points = Points(jobs: jobs, upgrades: upgrades);
            // Round 1 image-7: the job names only.
            var jp = StockUiStatusPages.JobPointRows(null);
            Assert.Equal(StockUiStatusPages.JobCount, jp.Count);
            Assert.Equal(("Warrior", null), (jp[0].Text, jp[0].Value));
            Assert.Equal("Rune Fencer", jp[21].Text);
            Assert.Equal("name 44", StockUiStatusPages.JobPointRows(id => "name " + id)[21].Text);
            Assert.Equal(100, StockUiStatusPages.JobPointsOf(points, 22).CapacityPoints);
            Assert.Equal(default, StockUiStatusPages.JobPointsOf(null, 22));

            // Round 1 images 8 and 9: three categories, each item "upgrades/most".
            var groups = StockUiStatusPages.AlterEgoGroupRows(null);
            Assert.Equal(new[] { "HP/MP", "Attributes", "Skills" }, groups.Select(r => r.Text));
            Assert.Equal(11, StockUiStatusPages.AlterEgoGroups.Sum(g => g.Count));
            var skills = StockUiStatusPages.AlterEgoItemRows(2, points, null);
            Assert.Equal(("Magic Skills", "2/60"), (skills[1].Text, skills[1].Value));
            var attributes = StockUiStatusPages.AlterEgoItemRows(1, points, null);
            Assert.Equal(("STR", "0/60"), (attributes[0].Text, attributes[0].Value));
            Assert.Equal(7, attributes.Count);
            // The retail client, 2026-11-07: "NEXT 0 P" in a Mog House; grey with none outside.
            Assert.Equal("NEXT 0 P", attributes[0].Note);
            var outside = StockUiStatusPages.AlterEgoItemRows(1, points, null, inMogHouse: false);
            Assert.Equal((StockUiProgression.Greyed, (string?)null), (outside[0].Color!.Value, outside[0].Note));
        }

        [Fact]
        public void MasterLevels_OpenOnceAJobIsMastered()
        {
            var jobs = new StockUiJobPointTotals[24];
            Assert.False(StockUiStatusPages.MasterLevelsUnlocked(0, Points(jobs: jobs), masterBreaker: true));
            Assert.True(StockUiStatusPages.MasterLevelsUnlocked(1u << 18, Points(jobs: jobs), masterBreaker: false));
            jobs[5] = new StockUiJobPointTotals(0, 0, 2100);
            Assert.True(StockUiStatusPages.MasterLevelsUnlocked(0, Points(jobs: jobs), masterBreaker: true));
            Assert.False(StockUiStatusPages.MasterLevelsUnlocked(0, Points(jobs: jobs), masterBreaker: false));
        }

        /// <summary>Round 1's corrections to the Currencies order (2026-11-01).</summary>
        [Fact]
        public void CurrencyOrder_FollowsRetail()
        {
            int At1(Currency1Kind k) => StockUiStatusPages.Currencies1.ToList().FindIndex(r => r.Kind == (int)k);
            int At2(Currency2Kind k) => StockUiStatusPages.Currencies2.ToList().FindIndex(r => r.Kind == (int)k);
            Assert.True(At1(Currency1Kind.DarkCrystals) < At1(Currency1Kind.GuildPointsFishing));
            Assert.Equal(At1(Currency1Kind.BallistaPoints) - 1, At1(Currency1Kind.DailyTally));
            Assert.Equal(At1(Currency1Kind.MoblinMarbles) - 1, At1(Currency1Kind.MoblinPheromoneSacks));
            Assert.Equal(At1(Currency1Kind.MoblinMarbles) + 1, At1(Currency1Kind.CaveConservationPoints));
            Assert.Equal(new[] { 1, 2, 3 }, new[] { Currency1Kind.Deeds, Currency1Kind.CopperAmanVouchers, Currency1Kind.ReclamationMarks }
                .Select(k => At1(k) - At1(Currency1Kind.SparksOfEminence)));
            Assert.Equal(At1(Currency1Kind.Tokens) + 1, At1(Currency1Kind.ImperialArmyIdTags));
            Assert.Equal(At1(Currency1Kind.TherionIchor) + 1, At1(Currency1Kind.BloodshedPlans));
            Assert.Equal(At1(Currency1Kind.TherionIchor) + 5, At1(Currency1Kind.PrimacyPlans));
            Assert.Equal(At1(Currency1Kind.AlliedNotes) + 1, At1(Currency1Kind.OpCredits));
            Assert.Equal(At1(Currency1Kind.RemsTaleChapters1) - 1, At1(Currency1Kind.UnityAccolades));
            Assert.Equal(At1(Currency1Kind.RemsTaleChapters10) + 1, At1(Currency1Kind.LoginPoints));

            Assert.Equal(At2(Currency2Kind.CoalitionImprimaturs) + 1, At2(Currency2Kind.KineticUnits));
            Assert.Equal(At2(Currency2Kind.PulchridoptWings) + 1, At2(Currency2Kind.MellidoptWings));
            Assert.Equal(At2(Currency2Kind.MellidoptWings) + 1, At2(Currency2Kind.GhastlyStones));
            Assert.Equal(At2(Currency2Kind.DuskorbStonesPlus2) + 1, At2(Currency2Kind.MweyaPlasmCorpuscles));
            Assert.Equal(At2(Currency2Kind.MweyaPlasmCorpuscles) + 1, At2(Currency2Kind.EschaSilt));
            Assert.Equal(At2(Currency2Kind.EschaSilt) + 1, At2(Currency2Kind.EschaBeads));
            Assert.Equal(At2(Currency2Kind.DarknessSpheresSet) + 1, At2(Currency2Kind.Hallmarks));
            Assert.Equal(At2(Currency2Kind.Hallmarks) + 2, At2(Currency2Kind.BadgesOfGallantry));
            Assert.Equal(At2(Currency2Kind.SilverAmanVouchers) + 1, At2(Currency2Kind.MysticalCanteens));
            Assert.Equal(At2(Currency2Kind.Gallimaufry) + 3, At2(Currency2Kind.ApollyonUnits));
        }

        /// <summary>Job Points' and Alter Ego Points' names and info lines (ROM/314/62, ROM/384/121).</summary>
        [Fact]
        public void RetailJobPointAndAlterEgoStrings()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            string Jp(int id) => rm.TryGetString(DMsgCategory.JobPointStrings, id, out var t) ? t : string.Empty;
            string Ae(int id) => rm.TryGetString(DMsgCategory.AlterEgoStrings, id, out var t) ? t : string.Empty;
            Assert.Equal("Warrior", Jp(StockUiStatusPages.JobPointNameId(1)));
            Assert.Equal("Adjust various abilities for WAR lv. 99 or above.", Jp(StockUiStatusPages.JobPointHelpId(1)));
            Assert.Equal("Rune Fencer", Jp(StockUiStatusPages.JobPointNameId(22)));
            Assert.Equal("Skills", Ae(StockUiStatusPages.AlterEgoGroupNameId(2)));
            Assert.Equal("Valid from Lv. 99 or higher.", Ae(StockUiStatusPages.AlterEgoGroupNameId(2) + 1));
            for (int i = 0; i < StockUiStatusPages.AlterEgoCategories.Count; i++)
                Assert.Equal(StockUiStatusPages.AlterEgoCategories[i].Name, Ae(StockUiStatusPages.AlterEgoItemNameId(i)));
            Assert.Equal("Uses 1 alter ego point to increase the alter ego's STR by 1.", Ae(StockUiStatusPages.AlterEgoItemNameId(2) + 1));
        }

        [Fact]
        public void PlayTime_DaysHoursMinutesSeconds()
        {
            Assert.Equal("Total play time: 0 days, 0 hours, 0 minutes and 0 seconds.", StockUiStatusPages.PlayTimeText(0));
            Assert.Equal("Total play time: 12 days, 1 hour, 2 minutes and 3 seconds.", StockUiStatusPages.PlayTimeText(12 * 86400 + 3600 + 123));
            Assert.Equal("Play time is not known yet.", StockUiStatusPages.PlayTimeText(null));
        }

        [Fact]
        public void MasterLevels_DashWithoutMastery()
        {
            var masters = new byte[24];
            masters[5] = 30;
            var info = new StockUiStatusInfo(new StockUiCharacterStats("", "", "", "", 0, 0, 0, 0, 0, new int[7], new int[7]), 99, 0, 0, "", "", "",
                "", 0, 0, 0, new byte[24], 5, Array.Empty<StockUiSkillLine>(), Array.Empty<StockUiSkillLine>(), Array.Empty<StockUiSkillLine>(),
                MasterLevels: masters, JobMasteryFlags: (1u << 5) | (1u << 6));
            Assert.Equal("30", StockUiStatusPages.MasterLevelText(info, 5));
            Assert.Equal("0", StockUiStatusPages.MasterLevelText(info, 6));
            Assert.Equal("-", StockUiStatusPages.MasterLevelText(info, 1));
            Assert.Equal(StockUiStatusPages.MasterLevelMenu, StockUiStatus.MenuOf(StockUiStatusPage.MasterLevels));
            Assert.Equal(StockUiStatus.PageHeight(StockUiStatusPage.JobLevels, null), StockUiStatus.PageHeight(StockUiStatusPage.MasterLevels, null));
        }

        private static StockUiStatusPoints Points(Func<Currency1Kind, int>? c1 = null, StockUiJobPointTotals[]? jobs = null, int[]? upgrades = null) =>
            new(0, 0, 0, false, false, jobs ?? new StockUiJobPointTotals[24], 0, upgrades ?? new int[11], c1 ?? (_ => 0), _ => 0, null, 1);
    }
}
