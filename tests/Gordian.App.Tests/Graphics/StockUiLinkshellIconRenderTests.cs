// tests/Gordian.App.Tests/Graphics/StockUiLinkshellIconRenderTests.cs
using System;
using System.Threading.Tasks;
using Gordian.App.Graphics;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// A linkshell's Link List (#490) rendered from the retail DATs on D3D11: each member's rank icon (shell, sack, pearl) and
    /// <c>link6</c>'s large icon draw over their slots (round 5, image-78: the list had no item lookup, so every cell showed
    /// only the empty slot frame). GORDIAN_UI_DUMP writes the frame.
    /// </summary>
    public class StockUiLinkshellIconRenderTests
    {
        [Fact]
        public async Task LinkList_DrawsEachRanksIconAndTheHeaderIcon()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;

            var menus = new StockUiMenuController
            {
                Library = screen.Library,
                ItemLookup = id => screen.Resources.TryGetItem(id, out var record) ? record : null,
            };
            Assert.True(menus.Open(StockUiMenuEntries.MainMenu));
            var shell = new StockUiLinkshellItem("GordianXI", StockUiLinkshellKind.Linkshell, 0, 5, 0x1, 1, 0x0F3A);
            var members = new StockUiPeopleResult(new[]
            {
                new StockUiPerson("Gemini", LinkshellRank: 1), new StockUiPerson("Claude", LinkshellRank: 2),
                new StockUiPerson("Gordian", LinkshellRank: 3),
            });
            await menus.OpenLinkshellMembersAsync(shell, Task.FromResult(members), menus.Top!);
            var menu = menus.Top!;
            Assert.Equal(StockUiPeoplePurpose.LinkshellMembers, menu.PeoplePurpose);
            Assert.NotNull(menu.ItemLookup);

            var frame = menu.Menu.Frame;
            var placement = StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, 1, screen.Width, screen.Height);
            var firstRow = menu.Menu.FindButton(1)!;
            float pitch = StockUiMenuWindow.RowPitch(firstRow, menu.Menu.FindButton(2));
            byte[] rgba = screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, menu, placement, 0, logFont: screen.LogFont),
                "gpu_linkshell_members.png");

            // The icon cell's centre (the icon is 16 px at x 3 of the row) on each member's row and on the empty fourth row.
            (byte R, byte G, byte B) Cell(int row) => screen.Pixel(rgba, (int)(placement.X + firstRow.X + 11), (int)(placement.Y + firstRow.Y + row * pitch + 8));
            static int Distance((byte R, byte G, byte B) a, (byte R, byte G, byte B) b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
            var empty = Cell(3);
            var icons = new[] { Cell(0), Cell(1), Cell(2) };
            for (int i = 0; i < icons.Length; i++)
                Assert.True(Distance(icons[i], empty) > 30, $"rank {i + 1}: no icon over the slot ({icons[i]} vs empty {empty})");
            Assert.True(Distance(icons[0], icons[1]) > 30 && Distance(icons[1], icons[2]) > 30 && Distance(icons[0], icons[2]) > 30,
                $"the shell, sack and pearl should differ: {icons[0]} {icons[1]} {icons[2]}");

            // link6's large icon (32 px at 10, 12) against its own empty frame left of it.
            Assert.True(screen.Library.TryGetMenu(StockUiSearch.LinkshellHeader, out var header));
            var at = StockUiWindowPlacement.Beside(placement, frame, header.Frame);
            var large = screen.Pixel(rgba, (int)(at.X + 10 + 16), (int)(at.Y + 12 + 16));
            var beside = screen.Pixel(rgba, (int)(at.X + 4), (int)(at.Y + 12 + 16));
            Assert.True(Distance(large, beside) > 30, $"no large icon in link6 ({large} vs {beside})");
        }
    }
}
