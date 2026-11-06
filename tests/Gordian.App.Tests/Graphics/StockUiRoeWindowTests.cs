// tests/Gordian.App.Tests/Graphics/StockUiRoeWindowTests.cs
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Gordian.App.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// The Records of Eminence windows (#493) on the retail menu DAT and RoE tables on D3D11: Quests' four rows, the RoE
    /// choice, the category tree and an objective with its help bar and box. GORDIAN_UI_DUMP writes the frames.
    /// </summary>
    public class StockUiRoeWindowTests
    {
        private static byte[] RenderOpen(StockUiOffscreen screen, StockUiMenuController menus, string dump) => screen.Render(r =>
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
            StockUiStatusPagesWindow.DrawRoe(r, screen.Library, screen.Font, screen.LogFont, menus, 1, screen.Width, screen.Height);
        }, dump);

        [Fact]
        public void RendersTheRecordsOfEminenceWindows()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;
            var roe = RecordsOfEminenceTables.Load(screen.Resources.LoadDatBytes);
            if (roe == null) return;
            var catalog = StockUiQuestLogCatalog.FromTables(screen.Resources.GetDMsgTable);
            var slots = new[] { new StockUiRoeSlot(0, 12, 37) };
            // OpenYesNo answers on the thread pool, which a full parallel test run can keep busy for seconds.
            var started = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var menus = new StockUiMenuController
            {
                Library = screen.Library,
                MenuStringText = id => screen.Resources.TryGetString(DMsgCategory.MenuStrings, id, out var t) ? t : null,
                MenuHelpText = id => screen.Resources.TryGetString(DMsgCategory.MenuHelp, id, out var t) ? t : null,
                QuestLogCatalog = () => catalog,
                QuestLogState = () => new StockUiQuestLogState(),
                RoeTables = () => roe,
                RoeState = () => new StockUiRoeState(slots, id => id == 1),
                RoeTotals = () => new StockUiRoeTotals(1, 91669, 55355, 1170, 725, 273),
                RoeStarted = id => { started.TrySetResult(id); return Task.CompletedTask; },
            };

            var kind = menus.OpenQuests(null)!;
            Assert.Equal(new[] { "Current", "Completed", "Current", "Objective List" }, kind.Rows.Select(r => r.Text));
            Assert.Equal("1/30", kind.Rows[2].Value);
            var quests = RenderOpen(screen, menus, "gpu_roe_quests.png");
            // The figures at the window's foot (image-34): "Sparks :" at the left.
            Assert.True(screen.HasLightPixel(quests, 16 + 14, 16 + 60, 48 + 198 + 9, threshold: 150), "no figures");
            kind.SelectedButtonId = StockUiRecordsOfEminence.ObjectiveListButton;
            menus.Activate();
            Assert.Equal("Tutorial", menus.Top!.Rows[0].Text);
            Assert.Equal("Objective List", menus.Top!.RoeListTitle);
            RenderOpen(screen, menus, "gpu_roe_categories.png");
            menus.Activate(); // Tutorial
            Assert.Equal("Basics", menus.Top!.Rows[0].Text);
            menus.Activate(); // Basics
            var objectives = menus.Top!;
            Assert.Equal("First Step Forward", objectives.Rows[0].Text);
            Assert.Equal(StockUiProgression.Greyed, objectives.Rows[0].Color);
            objectives.SelectedButtonId = 3; // Vanquish One Enemy (after First Step Forward and All for One)
            Assert.NotNull(menus.SelectedRoeObjective(objectives));
            var shot = RenderOpen(screen, menus, "gpu_roe_objectives.png");
            // The list's title in quest00's frame.
            Assert.True(screen.HasLightPixel(shot, 16 + 60, 16 + 120, 48 + 11 + 6, threshold: 150), "no list title");
            menus.Activate();
            Assert.True(menus.Top!.IsPrompt);
            Assert.Equal("Set objective?", menus.Top!.Message);
            RenderOpen(screen, menus, "gpu_roe_set.png");
            menus.Activate();
            Assert.True(started.Task.Wait(TimeSpan.FromSeconds(10)), "RoeStarted not called");
            Assert.Equal(menus.SelectedRoeObjective(objectives)!.Id, started.Task.Result);
        }
    }
}
