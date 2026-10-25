// tests/Gordian.Engine.Tests/Ui/StockUiMapTests.cs
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Input;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Ui
{
    /// <summary>
    /// The Map window (#23): main menu Map opens <c>map0</c> on the zone's first map; Change Map lists the current area and the
    /// regions, a region its zones, a zone with several maps its maps; the movement and camera input pan and zoom the map.
    /// </summary>
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
                Library = UiResourceLibrary.FromDefinitions(new[]
                {
                    List(StockUiMenuEntries.MainMenu, 12), List(StockUiMap.MenuName, 1, 2, 3),
                    List(StockUiMap.AreaListMenu, 1, 2, 3, 4, 5), List(StockUiMap.MapListMenu, 1, 2, 3, 4, 5),
                }),
                CurrentZone = () => zone,
                ZoneName = z => z switch { 230 => "Southern San d'Oria", 231 => "Northern San d'Oria", 50 => "Aht Urhgan Whitegate", _ => $"zone {z}" },
                RegionName = r => r switch { 0 => "San d'Oria", 1 => "Bastok", _ => $"region {r}" },
            };
            menus.NoticePosted += notices.Add;
            return menus;
        }

        [Fact]
        public void Map_OpensOnTheZonesFirstMap_ChangeMapListsAreasZonesAndMaps()
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
            var areas = menus.Top!;
            Assert.Equal(StockUiMap.AreaListMenu, areas.Name);
            Assert.Equal(StockUiListKind.MapAreas, areas.ListKind);
            Assert.Equal(new StockUiListRow(1, "Current Area", false, Value: "(Area)"), areas.Rows[0]);
            Assert.Equal(new StockUiListRow(2, "San d'Oria", false, Value: "[4]"), areas.Rows[1]); // round 1 image-6
            Assert.Equal("Bastok", areas.Rows[2].Text);

            // Current Area: Whitegate's two maps; the second shows and the lists close.
            menus.Activate();
            var maps = menus.Top!;
            Assert.Equal(StockUiMap.MapListMenu, maps.Name);
            Assert.Equal(2, maps.Rows.Count);
            menus.Move(InputAction.MenuDown);
            menus.Activate();
            Assert.Same(map, menus.Top);
            Assert.Equal(1, map.MapNumber);

            // San d'Oria: its four zones; Northern San d'Oria's one map shows.
            menus.Activate(); // Change Map (still selected)
            menus.Move(InputAction.MenuDown); // San d'Oria
            menus.Activate();
            var zones = menus.Top!;
            Assert.Equal(4, zones.Rows.Count);
            Assert.Equal("Southern San d'Oria", zones.Rows[0].Text);
            menus.Move(InputAction.MenuDown); // Northern San d'Oria
            menus.Activate();
            Assert.Same(map, menus.Top);
            Assert.Equal(231, map.MapZone);
            Assert.Empty(notices);
        }

        /// <summary>The movement input pans the map and the camera input zooms it, between covering the window and 4 times that (round 1).</summary>
        [Fact]
        public void MapView_PansAndZooms_WithinItsLimits()
        {
            var menus = Controller(50, new List<string>());
            Assert.True(menus.OpenMainMenu());
            menus.Activate();
            var map = menus.MapWindow!;
            Assert.Equal(1f, map.MapZoom);

            menus.MoveMapView(0, 0, 10, 0);
            Assert.Equal(StockUiMap.MaxZoom, map.MapZoom);
            menus.MoveMapView(0, 0, 0, -100);
            Assert.Equal(1f, map.MapZoom);
            menus.MoveMapView(0, 0, 0, 1);
            Assert.Equal(StockUiMap.WheelZoomStep, map.MapZoom, 3);

            float x = map.MapCenterX;
            menus.MoveMapView(0.2f, 0, 0, 0);
            Assert.True(map.MapCenterX > x);
            // The drawer keeps the map covering the window: a quarter of the map visible either side of the centre.
            menus.MoveMapView(10f, 10f, 0, 0);
            map.ClampMapCenter(0.25f, 0.25f);
            Assert.Equal(0.75f, map.MapCenterX);
            Assert.Equal(0.75f, map.MapCenterY);
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
