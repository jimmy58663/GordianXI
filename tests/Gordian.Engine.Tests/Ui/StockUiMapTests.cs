// tests/Gordian.Engine.Tests/Ui/StockUiMapTests.cs
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Input;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Ui
{
    /// <summary>The Map window (#23): main menu Map opens <c>map0</c> on the zone's first map; Change Map steps through the zone's maps.</summary>
    public class StockUiMapTests
    {
        private static UiMenuDefinition List(string name, params int[] ids)
        {
            var buttons = new List<UiMenuButton>();
            for (int i = 0; i < ids.Length; i++)
            {
                buttons.Add(new UiMenuButton
                {
                    ButtonId = (short)ids[i], X = 16, Y = (short)(6 + 16 * i), Width = 88, Height = 16,
                    NavUp = (sbyte)ids[(i - 1 + ids.Length) % ids.Length], NavDown = (sbyte)ids[(i + 1) % ids.Length],
                    NavLeft = (sbyte)ids[i], NavRight = (sbyte)ids[i],
                });
            }
            return new UiMenuDefinition
            {
                Name = name,
                Frame = new UiMenuFrame { X = 384, Y = 48, Width = 112, Height = (short)(10 + 16 * ids.Length), Anchor = UiAnchor.TopRight },
                Buttons = buttons,
            };
        }

        private static StockUiMenuController Controller(int zone, List<string> notices)
        {
            var menus = new StockUiMenuController
            {
                Library = UiResourceLibrary.FromDefinitions(new[] { List(StockUiMenuEntries.MainMenu, 12), List(StockUiMap.MenuName, 1, 2, 3) }),
                CurrentZone = () => zone,
            };
            menus.NoticePosted += notices.Add;
            return menus;
        }

        [Fact]
        public void Map_OpensOnTheZonesFirstMap_ChangeMapStepsRound()
        {
            var notices = new List<string>();
            var menus = Controller(50, notices); // Aht Urhgan Whitegate: maps 0 and 1
            int scans = 0;
            menus.WideScanRequested = () => { scans++; return Task.CompletedTask; };
            Assert.True(menus.OpenMainMenu());
            menus.Activate(); // Map
            var map = menus.MapWindow;
            Assert.NotNull(map);
            Assert.Equal(50, map!.MapZone);
            Assert.Equal(0, map.MapNumber);

            menus.Move(InputAction.MenuDown); // Wide Scan
            menus.Activate();
            Assert.Equal(1, scans);
            menus.Move(InputAction.MenuDown); // Change Map
            menus.Activate();
            Assert.Equal(1, map.MapNumber);
            menus.Activate();
            Assert.Equal(0, map.MapNumber);
            Assert.Empty(notices);
        }

        [Fact]
        public void Map_InAZoneWithoutMaps_PostsANotice()
        {
            var notices = new List<string>();
            var menus = Controller(0, notices);
            Assert.True(menus.OpenMainMenu());
            menus.Activate();
            Assert.Null(menus.MapWindow);
            Assert.Equal("Map: there is no map of this area.", Assert.Single(notices));
        }
    }
}
