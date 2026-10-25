// src/Gordian.Engine/Ui/StockUiMenuController.Map.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Resources.Tables;

namespace Gordian.Engine.Ui
{
    /// <summary>
    /// The stock Map window (#23) from the English menu DAT (39542, ROM/119/51; read 2026-10-24): main menu Map opens
    /// <c>map0</c> (112 x 58 at 384,48, top right, titled "Map") with Markers (button 1), Wide Scan (2) and Change Map (3),
    /// and the client draws the zone's map (<see cref="ZoneMapFiles"/>) behind it with the help bar "Map" / the zone's name,
    /// as the maintainer's retail capture (<c>Main_Menu_Page1.mp4</c>, 5:20-6:00) shows. Retail opens the map zoomed in on
    /// the player with a position marker and the grid square under the help bar ("(F-4)"); GordianXI shows the whole map,
    /// fitted to the screen, without the marker: the world-to-map scale is not known (see <see cref="ZoneMapFiles"/>).
    /// </summary>
    public static class StockUiMap
    {
        public const string MenuName = "map0";
        public const string Title = "Map";
        public const int MarkersButton = 1, WideScanButton = 2, ChangeMapButton = 3;
    }

    public sealed partial class StockUiOpenMenu
    {
        /// <summary>The Map window's list (<c>map0</c>).</summary>
        public bool IsMapWindow => Name.Equals(StockUiMap.MenuName, StringComparison.OrdinalIgnoreCase) && MapZone > 0;

        /// <summary>The zone whose map the Map window shows.</summary>
        public int MapZone { get; init; }

        /// <summary>The zone's map numbers (<see cref="ZoneMapFiles.MapsOf"/>), Change Map steps through them.</summary>
        public IReadOnlyList<byte> MapNumbers { get; init; } = Array.Empty<byte>();

        /// <summary>The index into <see cref="MapNumbers"/> of the map shown.</summary>
        public int MapIndex { get; internal set; }

        /// <summary>The map number shown.</summary>
        public int MapNumber => MapNumbers.Count > 0 ? MapNumbers[Math.Clamp(MapIndex, 0, MapNumbers.Count - 1)] : 0;
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>The zone the character is in; set by the session.</summary>
        public Func<int>? CurrentZone { get; set; }

        /// <summary>The Map window's Wide Scan (C2S 0x0F4, as <c>/widescan</c>); set by the session.</summary>
        public Func<Task>? WideScanRequested { get; set; }

        /// <summary>The open Map window, if any.</summary>
        public StockUiOpenMenu? MapWindow => _open.LastOrDefault(m => m.IsMapWindow);

        /// <summary>Opens the Map window over the main menu on the zone's first map; a notice when the zone has none. Call under the lock.</summary>
        private StockUiOpenMenu? OpenMap(StockUiOpenMenu from)
        {
            var library = _library;
            int zone = CurrentZone?.Invoke() ?? 0;
            var maps = ZoneMapFiles.MapsOf(zone);
            if (library == null || !library.TryGetMenu(StockUiMap.MenuName, out var definition)) return null;
            if (maps.Count == 0)
            {
                NoticePosted?.Invoke("Map: there is no map of this area.");
                return null;
            }
            var menu = new StockUiOpenMenu(definition, from, Array.Empty<string>(), null) { MapZone = zone, MapNumbers = maps };
            menu.SelectedButtonId = FirstSelectable(definition);
            PushOpen(menu);
            return menu;
        }

        /// <summary>Change Map: the zone's next map, round to the first (retail lists the other floors and zones to pick from).</summary>
        private void ChangeMap(StockUiOpenMenu map)
        {
            if (map.MapNumbers.Count <= 1)
            {
                NoticePosted?.Invoke("Map: this area has no other maps.");
                return;
            }
            lock (_sync) map.MapIndex = (map.MapIndex + 1) % map.MapNumbers.Count;
            Changed?.Invoke();
        }
    }
}
