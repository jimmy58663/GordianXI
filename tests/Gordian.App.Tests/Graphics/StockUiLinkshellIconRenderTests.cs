// tests/Gordian.App.Tests/Graphics/StockUiLinkshellIconRenderTests.cs
using System;
using System.Threading.Tasks;
using Gordian.App.Graphics;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// A linkshell's Link List (#490) rendered from the retail DATs on D3D11: each member's rank icon (shell, sack, pearl, in the
    /// shell's colour since round 6) and <c>link6</c>'s large icon draw over their slots (round 5, image-78: the list had no item lookup, so every cell showed
    /// only the empty slot frame). GORDIAN_UI_DUMP writes the frame.
    /// </summary>
    public class StockUiLinkshellIconRenderTests
    {
        [Fact]
        public async Task LinkList_DrawsEachRanksIconInTheShellsColourAndTheHeaderIcon()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;

            // Grey (colour 0) leaves the icons as they are; the shell's magenta (0x0F3A) tints them (round 6).
            var plain = await RenderLinkList(screen, 0, null);
            var tinted = await RenderLinkList(screen, 0x0F3A, "gpu_linkshell_members.png");

            static int Distance((byte R, byte G, byte B) a, (byte R, byte G, byte B) b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
            var empty = tinted.Cell(3);
            for (int i = 0; i < 3; i++)
            {
                Assert.True(Distance(plain.Cell(i), plain.Cell(3)) > 30, $"rank {i + 1}: no icon over the slot ({plain.Cell(i)} vs empty {plain.Cell(3)})");
                Assert.True(Distance(tinted.Cell(i), empty) > 30, $"rank {i + 1}: no tinted icon over the slot ({tinted.Cell(i)} vs empty {empty})");
                Assert.True(Distance(tinted.Cell(i), plain.Cell(i)) > 20, $"rank {i + 1}: not tinted ({tinted.Cell(i)} vs untinted {plain.Cell(i)})");
            }
            Assert.True(Distance(plain.Cell(0), plain.Cell(1)) > 30 && Distance(plain.Cell(1), plain.Cell(2)) > 30 && Distance(plain.Cell(0), plain.Cell(2)) > 30,
                $"the shell, sack and pearl should differ: {plain.Cell(0)} {plain.Cell(1)} {plain.Cell(2)}");
            Assert.True(Distance(tinted.Large, tinted.BesideLarge) > 30, $"no large icon in link6 ({tinted.Large} vs {tinted.BesideLarge})");
        }

        private sealed record LinkListFrame(Func<int, (byte R, byte G, byte B)> Cell, (byte R, byte G, byte B) Large, (byte R, byte G, byte B) BesideLarge);

        /// <summary>Opens Gemini (shell), Claude (sack) and Gordian (pearl) in a Link List of a shell of <paramref name="color"/> and renders it.</summary>
        private static async Task<LinkListFrame> RenderLinkList(StockUiOffscreen screen, ushort color, string? dumpName)
        {
            var menus = new StockUiMenuController
            {
                Library = screen.Library,
                ItemLookup = id => screen.Resources.TryGetItem(id, out var record) ? record : null,
            };
            Assert.True(menus.Open(StockUiMenuEntries.MainMenu));
            var shell = new StockUiLinkshellItem("GordianXI", StockUiLinkshellKind.Linkshell, 0, 5, 0x1, 1, color);
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
            byte[] rgba = screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, menu, placement, 0, logFont: screen.LogFont), dumpName);

            // link6's large icon (32 px at 10, 12) and its own empty frame left of it.
            Assert.True(screen.Library.TryGetMenu(StockUiSearch.LinkshellHeader, out var header));
            var at = StockUiWindowPlacement.Beside(placement, frame, header.Frame);
            // The icon cell's centre (the icon is 16 px at x 3 of the row); row 3 is an empty slot.
            return new LinkListFrame(
                row => screen.Pixel(rgba, (int)(placement.X + firstRow.X + 11), (int)(placement.Y + firstRow.Y + row * pitch + 8)),
                screen.Pixel(rgba, (int)(at.X + 10 + 16), (int)(at.Y + 12 + 16)),
                screen.Pixel(rgba, (int)(at.X + 4), (int)(at.Y + 12 + 16)));
        }
    }
}
