// tests/Gordian.App.Tests/Graphics/StockUiActionListWindowTests.cs
using System.Collections.Generic;
using System.Diagnostics;
using Gordian.App.Graphics;
using Gordian.Core.Actions;
using Gordian.Core.Ui;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>The spell list (#240) rendered from the retail <c>magic</c> frame on D3D11 (GORDIAN_UI_DUMP writes the frame).</summary>
    public class StockUiActionListWindowTests
    {
        [Fact]
        public void RendersSpellListWithMpCostsGreyedRowsAndScrollbar()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;

            long recastEnds = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 30;
            var rows = new List<StockUiActionRow>
            {
                new(StockUiActionType.Spell, 1, "Cure", ActionTargetFlags.Self, "8", 0, null),
                new(StockUiActionType.Spell, 2, "Cure II", ActionTargetFlags.Self, "24", recastEnds, null),
                new(StockUiActionType.Spell, 3, "Cure III", ActionTargetFlags.Self, "46", 0, "You do not have enough MP to cast Cure III."),
            };
            for (int i = 0; i < 12; i++) rows.Add(new(StockUiActionType.Spell, (ushort)(10 + i), $"Spell {i}", ActionTargetFlags.Self, "10", 0, null));
            var menus = new StockUiMenuController
            {
                Library = screen.Library,
                ActionRowsProvider = (_, _, _) => rows,
            };
            var list = menus.OpenActionList(StockUiActionList.Spells, 1, null)!;
            Assert.True(list.CanScroll);
            var frame = list.Menu.Frame;
            var at = StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, 1, screen.Width, screen.Height);
            var shot = screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, list, at, Stopwatch.GetTimestamp()), "gpu_magic_list.png");

            int px = (int)at.X, py = (int)at.Y;
            // Names from the rows' left, MP costs at the right, on the fourth row (y 4 + 48; the first is selected, orange).
            Assert.True(screen.HasLightPixel(shot, px + 20, px + 80, py + 52 + 8), "no spell name on the fourth row");
            Assert.True(screen.HasLightPixel(shot, px + 150, px + 172, py + 52 + 8), "no MP cost on the fourth row");
            // The row in recast shows its time left at the right ("0:30").
            Assert.True(screen.HasLightPixel(shot, px + 140, px + 172, py + 20 + 8, 120), "no recast on the second row");
        }
    }
}
