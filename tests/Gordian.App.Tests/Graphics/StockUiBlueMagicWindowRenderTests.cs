// tests/Gordian.App.Tests/Graphics/StockUiBlueMagicWindowRenderTests.cs
using System.Collections.Generic;
using Gordian.App.Graphics;
using Gordian.Engine.Actions;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// Offscreen D3D11 render of Blue Magic's Set Spells windows (#363) on the retail DAT frames and the retail spell table: the
    /// slots, the learned list beside them, the points and the details (GORDIAN_UI_DUMP writes gpu_blue_magic.png).
    /// </summary>
    public class StockUiBlueMagicWindowRenderTests
    {
        [Fact]
        public void RendersTheSlots_TheLearnedList_ThePointsAndTheDetails()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;
            var catalog = ActionCatalog.Load(screen.Resources);
            if (catalog == null) return;

            var slots = new byte[20];
            slots[0] = 549 - 512; // Pollen
            slots[1] = 517 - 512; // Metallic Body
            var learned = new HashSet<ushort> { 549, 517, 524, 513, 515, 519, 529 };
            var menus = new StockUiMenuController
            {
                Library = screen.Library,
                BlueMagicCatalog = () => catalog,
                BlueMagicState = () => new StockUiBlueMagicState(40, false, slots, 0, learned.Contains),
                CharacterStats = () => new StockUiCharacterStats("Gemini", "Lv40 Blue Mage", "Lv20 White Mage", "ILv ---", 900, 900, 400, 400, 1000,
                    new[] { 75, 79, 78, 83, 88, 83, 83 }, new int[7]),
            };
            var window = menus.OpenSetSpells(null)!;
            var frame = window.Menu.Frame;
            var placement = StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, 1, screen.Width, screen.Height);
            int x = (int)placement.X, y = (int)placement.Y;
            byte[] pixels = screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, window, placement, 0, logFont: screen.LogFont,
                screen: new StockUiScreen(screen.Width, screen.Height, null)), "gpu_blue_magic.png");

            // Slot 2's name (slot 1 is selected), the learned list beside the slots at x 314 ("Type" on its first row), the status
            // column at (16, 48), the points under "Total Points/Max", and the details under the slots at (130, 240).
            Assert.True(screen.HasLightPixel(pixels, x + 3 + 41, x + 140, y + 24 + 8), "no spell in slot 2");
            Assert.True(screen.HasLightPixel(pixels, x + 184 + 18, x + 184 + 140, y + 42 + 8), "no learned list beside the slots");
            Assert.True(screen.HasLightPixel(pixels, x + 184 + 100, x + 184 + 165, y + 6 + 8), "no Type header");
            Assert.True(screen.HasLightPixel(pixels, x - 114 + 4, x - 114 + 100, y + 11), "no status column");
            Assert.True(screen.HasLightPixel(pixels, x - 114 + 60, x - 114 + 104, y + 192 + 34), "no points");
            Assert.True(screen.HasLightPixel(pixels, x + 8, x + 200, y + 192 + 10), "no details");
            Assert.Equal((2, 25), window.BluePoints);
        }
    }
}
