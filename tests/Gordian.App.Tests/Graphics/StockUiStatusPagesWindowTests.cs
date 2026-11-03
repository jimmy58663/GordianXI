// tests/Gordian.App.Tests/Graphics/StockUiStatusPagesWindowTests.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Gordian.App.Graphics;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Models;
using Gordian.Engine.Input;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// The Status window's own windows (#487) rendered from the retail <c>masterle</c>, <c>meritcat</c>, <c>merit1</c> /
    /// <c>merit2</c> / <c>levmerit</c>, <c>jbpcat</c> / <c>levmeri2</c> / <c>merithel</c>, <c>fp_cat</c> / <c>fp_cat2</c> /
    /// <c>fp_info</c> / <c>fp_help</c> and <c>ut_menu</c> / <c>ut_point</c> resources on D3D11. GORDIAN_UI_DUMP writes the frames.
    /// </summary>
    public class StockUiStatusPagesWindowTests
    {
        private static StockUiStatusInfo Info()
        {
            var stats = new StockUiCharacterStats("Gemini", "Lv99 Puppetmaster", "Lv49 Warrior", "ILv ---/Su 0", 9999, 9999, 0, 0, 3000,
                new[] { 80, 91, 81, 92, 86, 73, 86 }, new int[7]);
            var jobs = new byte[24];
            for (int i = 1; i <= 22; i++) jobs[i] = 99;
            var masters = new byte[24];
            masters[18] = 12;
            return new StockUiStatusInfo(stats, 99, 55999, 56000, "New Adventurer", "San d'Oria", "Tarutaru", "SSandOria", 1, 0, 1, jobs, 18,
                Array.Empty<StockUiSkillLine>(), Array.Empty<StockUiSkillLine>(), Array.Empty<StockUiSkillLine>(), SubJob: 1, NationId: 0,
                MasterLevels: masters, JobMasteryFlags: 1u << 18);
        }

        private static StockUiStatusPoints Points()
        {
            var jobs = new StockUiJobPointTotals[24];
            jobs[18] = new StockUiJobPointTotals(12345, 321, 2100);
            var upgrades = new int[StockUiStatusPages.AlterEgoCategories.Count];
            upgrades[0] = 3;
            return new StockUiStatusPoints(9876, 12, 75, true, false, jobs, 555, upgrades,
                kind => kind switch { Currency1Kind.ConquestPointsSandoria => 54321, Currency1Kind.UnityAccolades => 777, _ => 0 },
                kind => kind == Currency2Kind.Bayld ? 1000 : 0, 3 * 86400 + 3661, 18);
        }

        private static byte[] RenderOpen(StockUiOffscreen screen, StockUiMenuController menus, string dump, StockUiPlacement? party = null)
        {
            return screen.Render(r =>
            {
                var open = menus.OpenMenus;
                for (int i = 0; i < open.Count; i++)
                {
                    bool covered = false;
                    for (int j = i + 1; j < open.Count && !covered; j++) covered = open[j].OverlapsAuthored(open[i]);
                    if (covered) continue;
                    var frame = open[i].Menu.Frame;
                    var at = StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, 1, screen.Width, screen.Height);
                    StockUiMenuWindow.Draw(r, screen.Library, screen.Font, open[i], at, Stopwatch.GetTimestamp(), logFont: screen.LogFont);
                }
                StockUiStatusWindow.DrawPanels(r, screen.Library, screen.Font, screen.LogFont, menus, 1, screen.Width, screen.Height, party);
            }, dump);
        }

        /// <summary>Opens the Status list and moves its cursor to <paramref name="button"/>.</summary>
        private static void OpenStatusEntry(StockUiMenuController menus, int button)
        {
            menus.CloseAll();
            Assert.True(menus.OpenMainMenu());
            menus.Activate(); // Status
            Assert.True(menus.Top!.IsStatusList);
            for (int i = 0; i < 13 && menus.Top!.SelectedButtonId != button; i++) menus.Move(InputAction.MenuDown);
            Assert.Equal(button, menus.Top!.SelectedButtonId);
            menus.Activate();
        }

        private static StockUiMenuController Controller(StockUiOffscreen screen, StockUiStatusInfo info, StockUiStatusPoints points, Func<ushort, bool> keyItems) => new()
        {
            Library = screen.Library,
            StatusInfo = () => info,
            StatusPoints = () => points,
            KeyItemHeld = keyItems,
            MenuHelpText = id => screen.Resources.TryGetString(DMsgCategory.MenuHelp, id, out var t) ? t : null,
            MenuStringText = id => screen.Resources.TryGetString(DMsgCategory.MenuStrings, id, out var t) ? t : null,
            JobPointText = id => screen.Resources.TryGetString(DMsgCategory.JobPointStrings, id, out var t) ? t : null,
            AlterEgoText = id => screen.Resources.TryGetString(DMsgCategory.AlterEgoStrings, id, out var t) ? t : null,
            MeritText = id => screen.Resources.TryGetString(DMsgCategory.MeritStrings, id, out var t) ? t : null,
            Merits = () => new[] { new StockUiMerit(0x40, 3, 4), new StockUiMerit(0x42, 2, 1), new StockUiMerit(0x80, 9, 3) },
            JobPointLevel = (job, k) => new StockUiJobPointCategory(k == 0 ? 7 : 0, k == 0 ? 8 : 1),
            UnityInfo = () => new StockUiUnityInfo(8, 120, 340, new uint[11], new uint[] { 5, 0, 0, 0, 0, 0, 0, 90, 0, 0, 40 }),
        };

        [Fact]
        public void RendersTheStatusWindowsOwnWindows()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;
            var info = Info();
            var requested = new List<string>();
            var notices = new List<string>();
            var echoes = new List<string>();
            byte? mode = null;
            var menus = Controller(screen, info, Points(),
                id => id is StockUiStatusPages.JobBreakerKeyItem or StockUiStatusPages.CipherBraceletKeyItem);
            menus.CurrenciesRequested = second => requested.Add(second ? "0x115" : "0x10F");
            menus.JobPointsRequested = () => requested.Add("0x0C0");
            menus.MeritModeChanged = m => { mode = m; return Task.CompletedTask; };
            menus.NoticePosted += notices.Add;
            menus.EchoPosted += echoes.Add;

            // Master Levels: masterle's 22 rows, the mastered Puppetmaster's 12, "-" for the others.
            OpenStatusEntry(menus, StockUiStatus.MasterLevelsButton);
            Assert.Equal(StockUiStatusPage.MasterLevels, menus.Top!.StatusPage);
            Assert.Equal(StockUiStatus.PageHeight(StockUiStatusPage.MasterLevels, info), menus.Top!.Menu.Frame.Height);
            var shot = RenderOpen(screen, menus, "gpu_status_master.png");
            Assert.True(screen.HasLightPixel(shot, 130 + 134 + 4, 130 + 240, 48 + 19 + 14 * 8 + 8, threshold: 150), "no Puppetmaster row");

            // Currencies: asks for 0x113; the list stands where the stats side was, the row's help line in the help bar.
            OpenStatusEntry(menus, StockUiStatus.CurrenciesButton);
            Assert.Contains("0x10F", requested);
            var list = menus.Top!;
            Assert.True(list.IsStatusValueList);
            Assert.Equal(StockUiStatusPage.Currencies, list.StatusWindow);
            Assert.Equal("Conquest Points (San d'Oria)", list.Rows[0].Text);
            Assert.Equal("54,321", list.Rows[0].Value);
            Assert.Equal(722, list.SelectedStatusRowHelp);
            shot = RenderOpen(screen, menus, "gpu_status_currencies.png");
            // The second row (the first is the selected one, in orange): its name and, at the right, its amount.
            Assert.True(screen.HasLightPixel(shot, 16 + 12, 16 + 150, 48 + 23 + 8, threshold: 150), "no currency name");
            Assert.True(screen.HasLightPixel(shot, 16 + 330, 16 + 350, 48 + 23 + 8, threshold: 150), "no currency amount");
            // Down past the eleventh row scrolls the list.
            for (int i = 0; i < 11; i++) menus.Move(InputAction.MenuDown);
            Assert.Equal(1, list.FirstRow);
            Assert.Equal(11, list.EntryIndex(list.SelectedButtonId));

            OpenStatusEntry(menus, StockUiStatus.Currencies2Button);
            Assert.Contains("0x115", requested);
            Assert.Equal("Bayld", menus.Top!.Rows[0].Text);
            Assert.Equal("1,000", menus.Top!.Rows[0].Value);
            Assert.Equal("Apollyon Units", menus.Top!.Rows[^1].Text);

            // Merit Points: merit1 with the Limit Points / Merit Points box in the level box's place; Mode Switch sends the mode.
            OpenStatusEntry(menus, StockUiStatus.MeritPointsButton);
            Assert.Equal(StockUiStatusPages.MeritMenu, menus.Top!.Name);
            Assert.Equal(StockUiStatusPages.MeritBox, StockUiStatusPagesWindow.BoxOf(menus.OpenMenus));
            shot = RenderOpen(screen, menus, "gpu_status_merits.png");
            Assert.True(screen.HasLightPixel(shot, 16 + 50, 16 + 106, 208 + 22 + 6, threshold: 150), "no limit points");
            menus.Move(InputAction.MenuDown); // Mode Switch
            menus.Activate();
            Assert.Equal(StockUiStatusPages.MeritModeMenu, menus.Top!.Name);
            Assert.True(menus.Top!.IsMarked(StockUiStatusPages.MeritModeExpButton)); // the red bar under the mode in effect
            Assert.False(menus.Top!.IsMarked(StockUiStatusPages.MeritModeLimitButton));
            RenderOpen(screen, menus, "gpu_status_merit_mode.png");
            menus.Move(InputAction.MenuDown); // Limit Points
            menus.Activate();
            Assert.Equal((byte)1, mode);
            Assert.Equal(StockUiStatusPages.MeritMenu, menus.Top!.Name);

            // Categories (#491): meritcat with each category's upgrades, meritinf in the level box's place; HP/MP's merits.
            menus.Move(InputAction.MenuUp); // Categories
            menus.Activate();
            Assert.Equal(StockUiProgressionList.MeritCategories, menus.Top!.ProgressionList);
            Assert.Equal(("HP/MP", "5/75"), (menus.Top!.Rows[0].Text, menus.Top!.Rows[0].Value));
            Assert.Equal(StockUiProgression.MeritInfoBox, StockUiStatusPagesWindow.BoxOf(menus.OpenMenus));
            shot = RenderOpen(screen, menus, "gpu_status_merit_categories.png");
            Assert.True(screen.HasLightPixel(shot, 16 + 50, 16 + 106, 240 + 16 + 6, threshold: 150), "no merit points in meritinf");
            Assert.True(screen.HasLightPixel(shot, 130 + 4, 130 + 100, 240 + 4 + 6, threshold: 150), "no category info");
            menus.Activate(); // HP/MP
            Assert.Equal(new[] { "Max HP", "Max MP" }, menus.Top!.Rows.Select(r => r.Text));
            RenderOpen(screen, menus, "gpu_status_merit_list.png");
            menus.Activate(); // Max HP: merit3
            Assert.Equal(StockUiProgression.MeritEditMenu, menus.Top!.Name);
            RenderOpen(screen, menus, "gpu_status_merit_edit.png");

            // Job Points: the job names, the cursor on the main job, its capacity and job points in the box, its line in the info box.
            OpenStatusEntry(menus, StockUiStatus.JobPointsButton);
            Assert.Contains("0x0C0", requested);
            Assert.Equal(StockUiStatusPage.JobPoints, menus.Top!.StatusWindow);
            Assert.Equal(17, menus.Top!.SelectedStatusRow);
            Assert.Equal(("Puppetmaster", (string?)null), (menus.Top!.Rows[17].Text, menus.Top!.Rows[17].Value));
            Assert.Equal(StockUiStatusPages.JobPointListHelp, menus.Top!.SelectedStatusRowHelp);
            shot = RenderOpen(screen, menus, "gpu_status_jobpoints.png");
            Assert.True(screen.HasLightPixel(shot, 16 + 50, 16 + 106, 240 + 22 + 6, threshold: 150), "no capacity points");
            Assert.True(screen.HasLightPixel(shot, 130 + 4, 130 + 100, 240 + 4 + 6, threshold: 150), "no info box name");
            // Confirm on a job (#491): its ten categories with "level/20", merit2in in the level box's place.
            menus.Activate();
            Assert.Equal((StockUiProgressionList.JobPointCategories, 18), (menus.Top!.ProgressionList, menus.Top!.ProgressionKey));
            Assert.Equal(10, menus.Top!.Rows.Count);
            Assert.Equal("7/20", menus.Top!.Rows[0].Value);
            Assert.Equal(StockUiProgression.JobPointInfoBox, StockUiStatusPagesWindow.BoxOf(menus.OpenMenus));
            shot = RenderOpen(screen, menus, "gpu_status_jobpoint_categories.png");
            Assert.True(screen.HasLightPixel(shot, 16 + 50, 16 + 106, 240 + 16 + 6, threshold: 150), "no job points in merit2in");

            // Alter Ego Points: HP/MP, Attributes, Skills; Attributes lists STR ... CHR with "upgrades/most".
            OpenStatusEntry(menus, StockUiStatus.AlterEgoButton);
            Assert.Equal("HP/MP", menus.Top!.Rows[0].Text);
            Assert.Equal(3, menus.Top!.Rows.Count);
            shot = RenderOpen(screen, menus, "gpu_status_alterego.png");
            Assert.True(screen.HasLightPixel(shot, 16 + 50, 16 + 106, 240 + 20 + 6, threshold: 150), "no alter ego points");
            menus.Move(InputAction.MenuDown); // Attributes
            menus.Activate();
            Assert.Equal(StockUiStatusPages.AlterEgoItemMenu, menus.Top!.Name);
            Assert.Equal(("STR", "0/60"), (menus.Top!.Rows[0].Text, menus.Top!.Rows[0].Value));
            Assert.Equal(StockUiStatusPages.AlterEgoItemHelp, menus.Top!.SelectedStatusRowHelp);
            shot = RenderOpen(screen, menus, "gpu_status_alterego_items.png");
            Assert.True(screen.HasLightPixel(shot, 130 + 4, 130 + 60, 240 + 4 + 6, threshold: 150), "no item info");
            menus.CloseTop();
            Assert.Equal(StockUiStatusPages.AlterEgoMenu, menus.Top!.Name);

            // Unity: the Unity Points box on top of the party window, in the target window's place.
            var party = new StockUiPlacement(screen.Width - 128, 600, 1, false);
            OpenStatusEntry(menus, StockUiStatus.UnityButton);
            Assert.Equal(StockUiStatusPages.UnityMenu, menus.Top!.Name);
            Assert.True(StockUiStatusPagesWindow.DocksOverParty(menus));
            shot = RenderOpen(screen, menus, "gpu_status_unity.png", party);
            Assert.True(screen.HasLightPixel(shot, (int)screen.Width - 128 + 40, (int)screen.Width - 128 + 106, 600 - 42 + 20 + 6, threshold: 150),
                "no unity points");
            menus.Activate(); // Unity Info (#491)
            Assert.Equal(StockUiProgressionList.UnityInfo, menus.Top!.ProgressionList);
            Assert.Equal("Naja Salaheem", menus.Top!.Rows[0].Value);
            Assert.Equal("1", menus.Top!.Rows[2].Value);
            RenderOpen(screen, menus, "gpu_status_unity_info.png", party);

            // Play Time: one line in the echo colour.
            OpenStatusEntry(menus, StockUiStatus.PlayTimeButton);
            Assert.Contains("Total play time: 3 days, 1 hour, 1 minute and 1 second.", echoes);
            Assert.True(menus.Top!.IsStatusList);
        }

        /// <summary>Round 2: Job Points stays listed, greyed without the job breaker; Master Levels and Alter Ego Points are greyed.</summary>
        [Fact]
        public void GatesTheEntriesByKeyItems()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;
            var menus = Controller(screen, Info() with { JobMasteryFlags = 0 }, Points() with { JobPoints = new StockUiJobPointTotals[24] },
                id => id == StockUiStatusPages.MasterBreakerKeyItem);
            var notices = new List<string>();
            menus.NoticePosted += notices.Add;
            Assert.True(menus.OpenMainMenu());
            menus.Activate(); // Status
            var list = menus.Top!;
            Assert.True(list.IsStatusList);
            Assert.NotNull(list.Menu.FindButton(StockUiStatus.JobPointsButton));
            Assert.Equal(218, list.Menu.Frame.Height);
            Assert.True(list.IsGreyed(StockUiStatus.JobPointsButton));
            Assert.True(list.IsGreyed(StockUiStatus.MasterLevelsButton));
            Assert.True(list.IsGreyed(StockUiStatus.AlterEgoButton));
            RenderOpen(screen, menus, "gpu_status_gated.png");
            menus.Move(InputAction.MenuDown); // Job Levels
            menus.Move(InputAction.MenuDown); // Master Levels
            menus.Activate();
            Assert.True(menus.Top!.IsStatusList);
            Assert.NotEmpty(notices);
        }
    }
}
