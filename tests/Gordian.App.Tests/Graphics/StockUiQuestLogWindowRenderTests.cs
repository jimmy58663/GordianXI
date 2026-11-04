// tests/Gordian.App.Tests/Graphics/StockUiQuestLogWindowRenderTests.cs
using Gordian.App.Graphics;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Gordian.Core.World;
using Gordian.Engine.Input;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// Offscreen D3D11 renders of the Missions, Quests and Region Info windows (#488) on the retail DAT frames with the
    /// retail quest and mission tables and the conquest map. GORDIAN_UI_DUMP writes the frames.
    /// </summary>
    public class StockUiQuestLogWindowRenderTests
    {
        [Fact]
        public void RendersMissionsQuestsAndTheConquestMap()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;

            var catalog = StockUiQuestLogCatalog.FromTables(screen.Resources.GetDMsgTable);
            var state = new StockUiQuestLogState { Nation = 0, NationMission = 5, Promathia = 101 };
            state.MissionComplete[0] = 0b11111;           // San d'Oria 0-4 done
            state.QuestOffer[0] = 0b1000_0001;            // two current San d'Oria quests
            state.QuestComplete[0] = 0b0110;
            var progression = new ProgressionState();
            byte[] conquest = new byte[176];
            conquest[0] = 1 | (2 << 2) | (3 << 4);
            conquest[22 + 6 * 4 + 3] = 1;                  // Kolshushu: San d'Oria
            conquest[130] = 55; conquest[131] = 20; conquest[132] = 15; conquest[144] = 10;
            conquest[136] = 4;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(conquest.AsSpan(140), 23456);
            progression.UpdateConquest(new S2C_0x05E_Conquest(conquest));
            int region = 10, zone = 4;                     // Bibiki Bay, Kolshushu (retail image-49)
            var menus = new StockUiMenuController
            {
                Library = screen.Library,
                QuestLogCatalog = () => catalog,
                QuestLogState = () => state,
                ConquestView = () => StockUiConquest.Build(progression, region, "Kolshushu", null, zone,
                    id => screen.Resources.TryGetString(DMsgCategory.RegionNames, id, out var r) ? r : null,
                    id => screen.Resources.TryGetString(DMsgCategory.ZoneNamesShort, id, out var z) ? z : null),
                MenuHelpText = id => screen.Resources.TryGetString(DMsgCategory.MenuHelp, id, out var t) ? t : null,
                MenuMessageText = id => screen.Resources.TryGetString(DMsgCategory.MenuMessages, id, out var t) ? t : null,
            };
            var layout = new StockUiScreen(screen.Width, screen.Height, null);
            StockUiPlacement Placement()
            {
                var frame = menus.Top!.Menu.Frame;
                return StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, 1, screen.Width, screen.Height);
            }
            byte[] Shot(string name)
            {
                var placement = Placement();
                return screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, menus.Top!, placement, 0, logFont: screen.LogFont, screen: layout), name);
            }

            // Missions > Promathia: the paging window on Chains of Promathia, the chapter's first mission in progress and the
            // chapter reached (retail image-11).
            Assert.NotNull(menus.OpenMissionGroup(3, null));
            var at = Placement();
            int px = (int)at.X, py = (int)at.Y;
            byte[] promathia = Shot("gpu_missions_promathia.png");
            Assert.True(screen.HasLightPixel(promathia, px + 120, px + 250, py + 15), "no page title");
            Assert.True(screen.HasLightPixel(promathia, px + 22, px + 90, py + 40 + 9), "no Current: heading");
            Assert.True(screen.HasLightPixel(promathia, px + 55, px + 250, py + 65 + 9, threshold: 120), "no mission in progress");

            // Right: Treasures of Aht Urhgan; Nations opens on San d'Oria, whose orders Confirm shows.
            menus.CloseAll();
            Assert.NotNull(menus.OpenMissionGroup(1, null));
            Shot("gpu_missions_sandoria.png");
            menus.Move(InputAction.MenuRight);
            byte[] bastok = Shot("gpu_missions_bastok.png");
            Assert.True(screen.HasLightPixel(bastok, px + 55, px + 300, py + 65 + 9), "no Not allowed note");
            menus.Move(InputAction.MenuLeft);
            menus.Activate();
            byte[] orders = Shot("gpu_missions_orders.png");
            Assert.True(screen.HasLightPixel(orders, px + 20, px + 300, py + 36 + 8), "no mission orders");
            menus.CloseAll();

            // Quests: Current / Completed, then San d'Oria's current quests.
            Assert.NotNull(menus.OpenQuests(null));
            byte[] kind = Shot("gpu_quests_kind.png");
            Assert.True(screen.HasLightPixel(kind, px + 55, px + 200, py + 46 + 9), "no Completed row");
            menus.Activate();
            byte[] quests = Shot("gpu_quests_current.png");
            Assert.True(screen.HasLightPixel(quests, px + 55, px + 300, py + 65 + 9), "no quest name on the second row");
            menus.CloseAll();

            // Region Info > Conquest in Bibiki Bay: the conquest map on Kolshushu with its regions and Kolshushu's zones labelled,
            // the region's window and the points in the target's place (retail image-49).
            var page = menus.OpenConquest(null);
            Assert.NotNull(page);
            var map = RegionMapFiles.Load(screen.Resources, RegionMap.Conquest);
            Assert.NotNull(map);
            Assert.True(screen.Library.TryGetMenu("targetwi", out var targetWindow));
            var tf = targetWindow.Frame;
            var target = StockUiLayout.Place(tf.Anchor, tf.X, tf.Y, tf.Width, tf.Height, 1, screen.Width, screen.Height);
            byte[] Conquest(string name)
            {
                var placement = Placement();
                return screen.Render(r =>
                {
                    StockUiRegionMapWindow.Draw(r, page!, map, screen.Width, screen.Height, screen.Library, screen.LogFont);
                    StockUiMenuWindow.Draw(r, screen.Library, screen.Font, menus.Top!, placement, 0, logFont: screen.LogFont, screen: layout);
                    StockUiQuestLogWindow.DrawConquestPoints(r, screen.Library, screen.Font, screen.LogFont, page!.ConquestView!, target, tf.Width, tf.Height);
                }, name);
            }
            at = Placement();
            byte[] control = Conquest("gpu_conquest_region.png");
            Assert.True(screen.HasLightPixel(control, (int)at.X + 12, (int)at.X + 100, (int)at.Y + 20 + 8), "no regional control");
            var (mx, my, mw, mh) = StockUiMapWindow.Placement(page!, map!.Width, map.Height, screen.Width, screen.Height);
            var parchment = screen.Pixel(control, (int)(mx + 330 / 512f * mw), (int)(my + 330 / 512f * mh));
            Assert.True(parchment.R > 40 && parchment.R > parchment.B, $"no conquest map: {parchment}");
            Assert.Contains(page!.ConquestView!.Markers, m => m.Label == "Onzozo Lbrth.");
            int kx = (int)(mx + 376 / 512f * mw), ky = (int)(my + 262 / 512f * mh);
            Assert.True(screen.HasLightPixel(control, kx - 30, kx + 30, ky - 14), "no Kolshushu label");
            Assert.True(screen.HasLightPixel(control, (int)target.X + 10, (int)target.X + 100, (int)(target.Y + (tf.Height - 40) + 20 + 6)), "no conquest points");
            // The cursor to Li'Telor, right of the screen's centre: its page, the window at the top left (round 3, image-61).
            menus.ConquestRegionView = r => StockUiConquest.Build(progression, r, "Li'Telor", null, zone,
                id => screen.Resources.TryGetString(DMsgCategory.RegionNames, id, out var n) ? n : null,
                id => screen.Resources.TryGetString(DMsgCategory.ZoneNamesShort, id, out var z) ? z : null, characterRegion: 10);
            menus.MoveMapView(41 / 70f, -50 / 70f, 0, 0);
            Assert.Equal(15, page.ConquestView!.Region);
            byte[] flipped = Conquest("gpu_conquest_litelor.png");
            Assert.True(screen.HasLightPixel(flipped, 12, 100, (int)at.Y + 20 + 8), "no region window at the left");
            menus.CloseAll();
            menus.ConquestRegionView = null;
            region = 3; zone = 243;                         // Ru'Lude Gardens: the Balance of Power
            page = menus.OpenConquest(null);
            Conquest("gpu_conquest_balance.png");
        }
    }
}
