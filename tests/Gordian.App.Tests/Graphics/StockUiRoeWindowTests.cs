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
            int started = -1;
            var menus = new StockUiMenuController
            {
                Library = screen.Library,
                MenuStringText = id => screen.Resources.TryGetString(DMsgCategory.MenuStrings, id, out var t) ? t : null,
                MenuHelpText = id => screen.Resources.TryGetString(DMsgCategory.MenuHelp, id, out var t) ? t : null,
                QuestLogCatalog = () => catalog,
                QuestLogState = () => new StockUiQuestLogState(),
                RoeTables = () => roe,
                RoeState = () => new StockUiRoeState(slots, id => id == 1),
                RoeStarted = id => { started = id; return Task.CompletedTask; },
            };

            var kind = menus.OpenQuests(null)!;
            Assert.Equal(new[] { "Current", "Completed", "Records of Eminence", "Vana'Bout" }, kind.Rows.Select(r => r.Text));
            RenderOpen(screen, menus, "gpu_roe_quests.png");
            kind.SelectedButtonId = StockUiRecordsOfEminence.RoeKindButton;
            menus.Activate();
            Assert.Equal(new[] { "Current", "Objective List" }, menus.Top!.Rows.Select(r => r.Text));
            menus.Top!.SelectedButtonId = StockUiRecordsOfEminence.ObjectiveListButton;
            menus.Activate();
            Assert.Equal("Tutorial", menus.Top!.Rows[0].Text);
            Assert.Equal(11, menus.Top!.Rows.Count);
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
            // The objective box (bluehelp, 130,240): its name.
            Assert.True(screen.HasLightPixel(shot, 130 + 4, 130 + 120, 240 + 4 + 6, threshold: 150), "no objective box");
            menus.Activate();
            Assert.True(menus.Top!.IsPrompt);
            Assert.Equal("Set objective?", menus.Top!.Message);
            RenderOpen(screen, menus, "gpu_roe_set.png");
            menus.Activate();
            for (int i = 0; i < 100 && started < 0; i++) System.Threading.Thread.Sleep(10);
            Assert.Equal(menus.SelectedRoeObjective(objectives)!.Id, started);
        }
    }
}
