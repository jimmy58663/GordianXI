// src/Gordian.Engine/Ui/StockUiMenuController.Map.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Resources.Tables;

namespace Gordian.Engine.Ui
{
    /// <summary>
    /// The stock Map window (#23) from the English menu DAT (39542, ROM/119/51; read 2026-10-24): main menu Map opens
    /// <c>map0</c> (112 x 58 at 384,48, top right, titled "Map") with Markers (button 1), Wide Scan (2) and Change Map (3),
    /// and the client draws the zone's map (<see cref="ZoneMapFiles"/>) behind it over the whole window with the help bar
    /// "Map" / the zone's name, as the maintainer's retail captures show (<c>Main_Menu_Page1.mp4</c>, 5:20-6:00; round 1
    /// image-4 and image-5, 2026-10-25, 2560 x 1440). The movement input pans the map and the camera controls zoom it, from
    /// the map covering the window (zoomed out) to <see cref="MaxZoom"/> times that (zoomed in: the round 1 shots' icons are
    /// 4.06 times apart). Change Map opens <c>arealist</c> (366 x 88 at 16,208, bottom left, over the chat; 5 rows): "Current
    /// Area" "(Area)", then every region (ROM/165/78) with the number of its zones that have a map ("San d'Oria [4]", round 1
    /// image-6); a region lists its zones in <c>maplist</c>, each with its compact name (ROM/165/85) at the right ("Southern
    /// San d'Oria (SSandOria)", round 2 image-26), and a zone with more than one map its maps. Both lists sit on the chat log
    /// and write their rows in the log font (round 2 image-25). Retail opens the map on
    /// the player with a position marker and the grid square under the help bar ("(F-10)"); GordianXI opens it centred,
    /// without the marker: the world-to-map scale is not known (see <see cref="ZoneMapFiles"/>).
    /// </summary>
    public static class StockUiMap
    {
        public const string MenuName = "map0", AreaListMenu = "arealist", MapListMenu = "maplist";
        public const string Title = "Map";
        public const int MarkersButton = 1, WideScanButton = 2, ChangeMapButton = 3;

        /// <summary>The Change Map lists' rows (<c>arealist</c> / <c>maplist</c> buttons 1-5).</summary>
        public const int ListRows = 5;

        /// <summary>How far the map zooms in from covering the window.</summary>
        public const float MaxZoom = 4f;

        /// <summary>Held zoom input multiplies the zoom by this much a second; a mouse wheel notch by <see cref="WheelZoomStep"/>.</summary>
        public const float ZoomPerSecond = 2.5f, WheelZoomStep = 1.25f;

        /// <summary>Held pan input moves the view by this share of the map a second at the zoomed-out size (less when zoomed in).</summary>
        public const float PanPerSecond = 0.5f;

        public const string CurrentAreaLabel = "Current Area", CurrentAreaValue = "(Area)";
    }

    /// <summary>What a Change Map list row opens.</summary>
    public enum StockUiMapListLevel
    {
        /// <summary>An <c>arealist</c> row: "Current Area" (<see cref="StockUiMapListEntry.Region"/> -1) or a region.</summary>
        Area,

        /// <summary>A zone of a region.</summary>
        Zone,

        /// <summary>One of a zone's maps.</summary>
        Map,
    }

    /// <summary>A Change Map list row: what it opens.</summary>
    public readonly record struct StockUiMapListEntry(StockUiMapListLevel Level, int Region, int Zone, int MapIndex);

    public sealed partial class StockUiOpenMenu
    {
        /// <summary>The Map window's list (<c>map0</c>).</summary>
        public bool IsMapWindow => Name.Equals(StockUiMap.MenuName, StringComparison.OrdinalIgnoreCase) && MapZone > 0;

        /// <summary>The zone whose map the Map window shows.</summary>
        public int MapZone { get; internal set; }

        /// <summary>The zone's map numbers (<see cref="ZoneMapFiles.MapsOf"/>).</summary>
        public IReadOnlyList<byte> MapNumbers { get; internal set; } = Array.Empty<byte>();

        /// <summary>The index into <see cref="MapNumbers"/> of the map shown.</summary>
        public int MapIndex { get; internal set; }

        /// <summary>The map number shown.</summary>
        public int MapNumber => MapNumbers.Count > 0 ? MapNumbers[Math.Clamp(MapIndex, 0, MapNumbers.Count - 1)] : 0;

        /// <summary>The zoom: 1 is the map covering the window, up to <see cref="StockUiMap.MaxZoom"/>.</summary>
        public float MapZoom { get; internal set; } = 1f;

        /// <summary>The map point (0-1 across and down the map texture) at the window's centre.</summary>
        public float MapCenterX { get; private set; } = 0.5f;

        /// <inheritdoc cref="MapCenterX"/>
        public float MapCenterY { get; private set; } = 0.5f;

        /// <summary>What each Change Map list row opens.</summary>
        public IReadOnlyList<StockUiMapListEntry> MapEntries { get; internal set; } = Array.Empty<StockUiMapListEntry>();

        internal void PanMap(float dx, float dy)
        {
            MapCenterX += dx;
            MapCenterY += dy;
        }

        internal void ResetMapView()
        {
            MapZoom = 1f;
            MapCenterX = MapCenterY = 0.5f;
        }

        /// <summary>
        /// Keeps the map covering the window: the centre stays at least half the visible span from each edge (the drawer
        /// passes the spans it drew this frame, as shares of the map).
        /// </summary>
        public void ClampMapCenter(float halfSpanX, float halfSpanY)
        {
            MapCenterX = halfSpanX >= 0.5f ? 0.5f : Math.Clamp(MapCenterX, halfSpanX, 1 - halfSpanX);
            MapCenterY = halfSpanY >= 0.5f ? 0.5f : Math.Clamp(MapCenterY, halfSpanY, 1 - halfSpanY);
        }
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>The zone the character is in; set by the session.</summary>
        public Func<int>? CurrentZone { get; set; }

        /// <summary>The Map window's Wide Scan (C2S 0x0F4, as <c>/widescan</c>); set by the session.</summary>
        public Func<Task>? WideScanRequested { get; set; }

        /// <summary>A zone's name by zone id (ROM/165/84); set by the HUD.</summary>
        public Func<int, string?>? ZoneName { get; set; }

        /// <summary>A region's name by its client number (ROM/165/78); set by the HUD.</summary>
        public Func<int, string?>? RegionName { get; set; }

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

        /// <summary>
        /// Pans and zooms the open Map window: <paramref name="dx"/> / <paramref name="dy"/> are the pan input times the
        /// tick's seconds (right and down positive), <paramref name="zoom"/> the zoom input times the seconds (in positive),
        /// <paramref name="wheel"/> the mouse wheel's notches (up zooms in).
        /// </summary>
        public void MoveMapView(float dx, float dy, float zoom, float wheel)
        {
            if (dx == 0 && dy == 0 && zoom == 0 && wheel == 0) return;
            lock (_sync)
            {
                if (MapWindow is not { } map) return;
                float factor = MathF.Pow(StockUiMap.ZoomPerSecond, zoom) * MathF.Pow(StockUiMap.WheelZoomStep, wheel);
                map.MapZoom = Math.Clamp(map.MapZoom * factor, 1f, StockUiMap.MaxZoom);
                float speed = StockUiMap.PanPerSecond / map.MapZoom;
                map.PanMap(dx * speed, dy * speed);
            }
            Changed?.Invoke();
        }

        /// <summary>Change Map: the area list over the chat (<c>arealist</c>): the current area, then every region with maps.</summary>
        private void ChangeMap(StockUiOpenMenu map)
        {
            lock (_sync)
            {
                var entries = new List<StockUiMapListEntry> { new(StockUiMapListLevel.Area, -1, map.MapZone, 0) };
                var rows = new List<StockUiListRow> { new(1, StockUiMap.CurrentAreaLabel, false, Value: StockUiMap.CurrentAreaValue) };
                foreach (int region in ZoneRegions.Regions)
                {
                    int zones = ZoneRegions.ZonesIn(region).Count(z => ZoneMapFiles.MapsOf(z).Count > 0);
                    if (zones == 0) continue;
                    entries.Add(new StockUiMapListEntry(StockUiMapListLevel.Area, region, 0, 0));
                    rows.Add(new StockUiListRow(rows.Count + 1, RegionName?.Invoke(region) ?? string.Empty, false,
                        Value: "[" + zones.ToString(CultureInfo.InvariantCulture) + "]"));
                }
                OpenMapList(StockUiMap.AreaListMenu, map, rows, entries);
            }
            Changed?.Invoke();
        }

        /// <summary>Pushes a Change Map list (<c>arealist</c> or <c>maplist</c>) with its rows. Call under the lock.</summary>
        private StockUiOpenMenu? OpenMapList(string menuName, StockUiOpenMenu parent, List<StockUiListRow> rows, List<StockUiMapListEntry> entries)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(menuName, out var definition))
            {
                NoticePosted?.Invoke("Map: the area list is not available in this session.");
                return null;
            }
            var menu = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null, listKind: StockUiListKind.MapAreas)
            {
                Rows = rows,
                MapEntries = entries,
            };
            menu.SelectedButtonId = 1;
            PushOpen(menu);
            return menu;
        }

        /// <summary>Confirm on a Change Map row: a region lists its zones, a zone with several maps its maps, a map shows.</summary>
        private void ActivateMapList(StockUiOpenMenu list, int buttonId)
        {
            lock (_sync)
            {
                int index = list.EntryIndex(buttonId);
                if (index < 0 || index >= list.MapEntries.Count) return;
                var entry = list.MapEntries[index];
                switch (entry.Level)
                {
                    case StockUiMapListLevel.Area when entry.Region < 0:
                    case StockUiMapListLevel.Zone:
                        OpenZone(list, entry.Zone);
                        break;
                    case StockUiMapListLevel.Area:
                        var rows = new List<StockUiListRow>();
                        var entries = new List<StockUiMapListEntry>();
                        foreach (int zone in ZoneRegions.ZonesIn(entry.Region))
                        {
                            int count = ZoneMapFiles.MapsOf(zone).Count;
                            if (count == 0) continue;
                            entries.Add(new StockUiMapListEntry(StockUiMapListLevel.Zone, entry.Region, zone, 0));
                            string? compact = ZoneCompactName?.Invoke(zone);
                            rows.Add(new StockUiListRow(rows.Count + 1, ZoneName?.Invoke(zone) ?? string.Empty, false,
                                Value: compact is { Length: > 0 } ? "(" + compact + ")" : null));
                        }
                        OpenMapList(StockUiMap.MapListMenu, list, rows, entries);
                        break;
                    case StockUiMapListLevel.Map:
                        ShowMap(entry.Zone, entry.MapIndex);
                        break;
                }
            }
            Changed?.Invoke();
        }

        /// <summary>A zone's row: its only map shows, or its maps are listed. Call under the lock.</summary>
        private void OpenZone(StockUiOpenMenu list, int zone)
        {
            var maps = ZoneMapFiles.MapsOf(zone);
            if (maps.Count <= 1)
            {
                if (maps.Count == 1) ShowMap(zone, 0);
                return;
            }
            string name = ZoneName?.Invoke(zone) ?? string.Empty;
            var rows = new List<StockUiListRow>(maps.Count);
            var entries = new List<StockUiMapListEntry>(maps.Count);
            for (int i = 0; i < maps.Count; i++)
            {
                entries.Add(new StockUiMapListEntry(StockUiMapListLevel.Map, ZoneRegions.RegionOf(zone), zone, i));
                rows.Add(new StockUiListRow(i + 1, name, false, Value: "(" + (i + 1).ToString(CultureInfo.InvariantCulture) + ")"));
            }
            OpenMapList(StockUiMap.MapListMenu, list, rows, entries);
        }

        /// <summary>Shows one of a zone's maps in the Map window and closes the lists over it. Call under the lock.</summary>
        private void ShowMap(int zone, int mapIndex)
        {
            if (MapWindow is not { } map) return;
            map.MapZone = zone;
            map.MapNumbers = ZoneMapFiles.MapsOf(zone);
            map.MapIndex = Math.Clamp(mapIndex, 0, Math.Max(0, map.MapNumbers.Count - 1));
            map.ResetMapView();
            int keep = Array.IndexOf(_open, map) + 1;
            if (keep > 0 && keep < _open.Length) _open = _open[..keep];
        }
    }
}
