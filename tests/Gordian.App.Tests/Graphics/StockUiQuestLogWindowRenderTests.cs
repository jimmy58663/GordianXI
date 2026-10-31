// tests/Gordian.App.Tests/Graphics/StockUiQuestLogWindowRenderTests.cs
using Gordian.App.Graphics;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Models;
using Gordian.Core.World;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// Offscreen D3D11 renders of the Missions, Quests and Region Info windows (#488) on the retail DAT frames with the
    /// retail quest and mission tables. GORDIAN_UI_DUMP writes the frames.
    /// </summary>
    public class StockUiQuestLogWindowRenderTests
    {
        [Fact]
        public void RendersMissionsQuestsAndConquest()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;

            var catalog = StockUiQuestLogCatalog.FromTables(screen.Resources.GetDMsgTable);
            var state = new StockUiQuestLogState { Nation = 0, NationMission = 5, Promathia = 228 };
            state.MissionComplete[0] = 0b11111;           // San d'Oria 0-4 done
            state.QuestOffer[0] = 0b1000_0001;            // two current San d'Oria quests
            state.QuestComplete[0] = 0b0110;
            var progression = new ProgressionState();
            byte[] conquest = new byte[176];
            conquest[0] = 1 | (2 << 2) | (3 << 4);
            conquest[22 + 3] = 1;                          // Ronfaure: San d'Oria
            conquest[130] = 55; conquest[131] = 20; conquest[132] = 15; conquest[144] = 10;
            conquest[136] = 4;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(conquest.AsSpan(140), 23456);
            progression.UpdateConquest(new S2C_0x05E_Conquest(conquest));
            int region = 4;
            var menus = new StockUiMenuController
            {
                Library = screen.Library,
                QuestLogCatalog = () => catalog,
                QuestLogState = () => state,
                ConquestView = () => StockUiConquest.Build(progression, region, "Ronfaure"),
                MenuHelpText = id => screen.Resources.TryGetString(DMsgCategory.MenuHelp, id, out var t) ? t : null,
                MenuMessageText = id => screen.Resources.TryGetString(DMsgCategory.MenuMessages, id, out var t) ? t : null,
            };
            var layout = new StockUiScreen(screen.Width, screen.Height, null);
            byte[] Shot(string name)
            {
                var frame = menus.Top!.Menu.Frame;
                var placement = StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, 1, screen.Width, screen.Height);
                return screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, menus.Top!, placement, 0, logFont: screen.LogFont, screen: layout), name);
            }
            (int X, int Y) Origin()
            {
                var frame = menus.Top!.Menu.Frame;
                var placement = StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, 1, screen.Width, screen.Height);
                return ((int)placement.X, (int)placement.Y);
            }

            // Missions > Nations: the storylines, then San d'Oria's missions, then one's orders.
            Assert.NotNull(menus.OpenMissionGroup(1, null));
            var (px, py) = Origin();
            byte[] storylines = Shot("gpu_missions_storylines.png");
            Assert.True(screen.HasLightPixel(storylines, px + 39 + 12, px + 200, py + 65 + 9), "no storyline name");
            menus.Activate();
            byte[] list = Shot("gpu_missions_list.png");
            Assert.True(screen.HasLightPixel(list, px + 24, px + 300, py + 4 + 8), "no mission name");
            menus.Activate();
            byte[] orders = Shot("gpu_missions_orders.png");
            Assert.True(screen.HasLightPixel(orders, px + 20, px + 300, py + 36 + 8), "no mission orders");
            menus.CloseAll();

            // Promathia: chapters and their missions one step in.
            Assert.NotNull(menus.OpenMissionGroup(3, null));
            Shot("gpu_missions_promathia.png");
            menus.CloseAll();

            // Quests: the areas, then San d'Oria's current quests.
            Assert.NotNull(menus.OpenQuestAreas(null));
            Shot("gpu_quests_areas.png");
            menus.Activate();
            Shot("gpu_quests_kind.png");
            menus.Activate();
            (px, py) = Origin();
            byte[] quests = Shot("gpu_quests_current.png");
            Assert.True(screen.HasLightPixel(quests, px + 24, px + 300, py + 20 + 8), "no quest name on the second row");
            menus.CloseAll();

            // Region Info > Conquest in Ronfaure, then in a city.
            Assert.NotNull(menus.OpenConquest(null));
            (px, py) = Origin();
            byte[] control = Shot("gpu_conquest_region.png");
            Assert.True(screen.HasLightPixel(control, px + 12, px + 100, py + 20 + 8), "no regional control");
            menus.CloseAll();
            region = 0;
            Assert.NotNull(menus.OpenConquest(null));
            Shot("gpu_conquest_balance.png");
        }
    }
}
