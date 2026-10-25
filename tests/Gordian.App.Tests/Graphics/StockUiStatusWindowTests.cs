// tests/Gordian.App.Tests/Graphics/StockUiStatusWindowTests.cs
using System.Diagnostics;
using Gordian.App.Graphics;
using Gordian.Engine.Input;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// The Status window (#23) rendered from the retail <c>statuswi</c>, <c>level</c>, <c>statcom2</c> and page resources on D3D11,
    /// with the numbers of the maintainer's retail capture (<c>Main_Menu_Page1.mp4</c>, 0:00-0:20: Gemini, Puppetmaster 99 /
    /// Warrior 49). GORDIAN_UI_DUMP writes the frames.
    /// </summary>
    public class StockUiStatusWindowTests
    {
        private static StockUiStatusInfo Info()
        {
            var stats = new StockUiCharacterStats("Gemini", "Lv99 Puppetmaster", "Lv49 Warrior", "ILv ---/Su 0", 9999, 9999, 0, 0, 3000,
                new[] { 80, 91, 81, 92, 86, 73, 86 }, new int[7]);
            var jobs = new byte[24];
            for (int i = 1; i <= 22; i++) jobs[i] = 99;
            var raw = new ushort[64];
            raw[1] = 424 | 0x8000; raw[2] = 368 | 0x8000; raw[3] = 144 | 0x8000; raw[29] = 398 | 0x8000; raw[31] = 334;
            raw[49] = (ushort)(60 * 0x20 + 6); raw[48] = 0xFFFF;
            return new StockUiStatusInfo(stats, 99, 55999, 56000, "New Adventurer", "San d'Oria", "Tarutaru", "SSandOria", 1, 0, 1, jobs, 18,
                StockUiStatus.SkillLines(StockUiStatus.CombatSkills, raw), StockUiStatus.SkillLines(StockUiStatus.MagicSkills, raw),
                StockUiStatus.CraftLines(raw), SubJob: 1, NationId: 0, MentorRank: 1);
        }

        private static byte[] RenderOpen(StockUiOffscreen screen, StockUiMenuController menus, string dump)
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
                StockUiStatusWindow.DrawPanels(r, screen.Library, screen.Font, screen.LogFont, menus, 1, screen.Width, screen.Height);
            }, dump);
        }

        [Fact]
        public void RendersStatsSideLevelBoxAndPages()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;
            var info = Info();
            var menus = new StockUiMenuController { Library = screen.Library, StatusInfo = () => info, MenuHelpText = id => $"help {id}" };
            Assert.True(menus.OpenMainMenu());
            menus.Activate(); // Status
            Assert.True(menus.IsStatusOpen);
            Assert.True(menus.Top!.IsStatusList);

            var shot = RenderOpen(screen, menus, "gpu_status_list.png");
            // The level box (16, 208): "EXP"'s grey and the numbers right-aligned to x 89; the stats side's name at the top.
            Assert.True(screen.HasLightPixel(shot, 16 + 4, 16 + 60, 16 + 12, threshold: 150), "no name on the stats side");
            Assert.True(screen.HasLightPixel(shot, 16 + 40, 16 + 89, 208 + 41, threshold: 80), "no EXP number");

            menus.Activate(); // Profile
            Assert.Equal(StockUiStatusPage.Profile, menus.Top!.StatusPage);
            shot = RenderOpen(screen, menus, "gpu_status_profile.png");
            Assert.True(screen.HasLightPixel(shot, 130 + 74, 130 + 140, 48 + 26), "no nation");

            // The D-pad on a page moves the list and shows the next entry's page (round 1); Job Levels has no cursor.
            menus.Move(InputAction.MenuDown); // Job Levels
            Assert.Equal(StockUiStatusPage.JobLevels, menus.Top!.StatusPage);
            Assert.Empty(menus.Top!.Menu.Buttons);
            Assert.Equal(StockUiStatus.PageHeight(StockUiStatusPage.JobLevels, info), menus.Top!.Menu.Frame.Height);
            Assert.Equal(StockUiStatus.JobLevelsButton, menus.Top!.Parent!.SelectedButtonId);
            shot = RenderOpen(screen, menus, "gpu_status_jobs.png");
            Assert.True(screen.HasLightPixel(shot, 130 + 134 + 4, 130 + 200, 48 + 19 + 14 * 8 + 8, threshold: 150), "no Puppetmaster row");

            menus.CloseTop();
            menus.Move(InputAction.MenuDown); // Master Levels
            menus.Move(InputAction.MenuDown); // Combat Skl.
            menus.Activate();
            Assert.Equal(StockUiStatusPage.CombatSkills, menus.Top!.StatusPage);
            shot = RenderOpen(screen, menus, "gpu_status_combat.png");
            Assert.True(screen.HasLightPixel(shot, 130 + 12, 130 + 80, 48 + 19 + 8, threshold: 150), "no Hand-to-Hand");

            menus.CloseTop();
            menus.Move(InputAction.MenuDown);
            menus.Move(InputAction.MenuDown); // Craft Skl.
            menus.Activate();
            Assert.Equal(StockUiStatusPage.CraftSkills, menus.Top!.StatusPage);
            Assert.Equal(StockUiStatus.PageHeight(StockUiStatusPage.CraftSkills, info), menus.Top!.Menu.Frame.Height);
            shot = RenderOpen(screen, menus, "gpu_status_craft.png");
            Assert.True(screen.HasLightPixel(shot, 130 + 12, 130 + 80, 48 + 19 + 8, threshold: 150), "no Fishing");
        }
    }
}
