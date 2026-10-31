// src/Gordian.Engine/Events/EventMap.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Resources.Tables;

namespace Gordian.Engine.Events
{
    /// <summary>
    /// The map data an event gives a query option (opcode 0xD4 sub-cases 1 / 3 / 4 / 5, XiEvents OpCodes/0x00D4).
    /// Sub 1: <see cref="First"/> a travel point kind and <see cref="Second"/> its number (the home point lists send kind 0
    /// and the home point's global number, <see cref="HomePoints"/>; kinds 2 and 3 are other travel networks, *inference*
    /// from their use sites). Sub 3: <see cref="First"/> alone. Subs 4 / 5: <see cref="First"/> and three values.
    /// </summary>
    public readonly record struct EventQueryMapEntry(int Sub, int First, int Second, int Value1, int Value2, int Value3)
    {
        /// <summary>Sub 1's travel point kind of the home points.</summary>
        public const int HomePointKind = 0;
    }

    /// <summary>A marker an event put on its map (opcode 0x8B): world x / z in yalms and the name beside it.</summary>
    public readonly record struct EventMapMarker(int Index, float X, float Z, string Name);

    /// <summary>The art a map marker uses.</summary>
    public enum EventMapPin
    {
        /// <summary>A marker an event placed (0x8B), labelled with its name.</summary>
        Event,

        /// <summary>A home point (<c>maphp</c>), labelled "Home Point".</summary>
        HomePoint,
    }

    /// <summary>
    /// What the event map shows this frame: a zone's map, the point it is centred on (null: the map's centre) and the
    /// markers on it.
    /// </summary>
    public sealed record EventMapFocus(int Zone, int MapNumber, float? X, float? Z, IReadOnlyList<EventMapMarker> Markers, EventMapPin Pin);

    /// <summary>
    /// The event map's state (#168), replaced whole on each change so the HUD can read it on another thread. Two kinds:
    /// the map an event opens with 0x89 / 0xC8 and marks with 0x8B (the intro map tutorial), up until 0x8A; and the map
    /// behind a query (0xD4 sub 0, the home point lists), shown only while the cursor is on an option with map data.
    /// </summary>
    public sealed class EventMapState
    {
        public static readonly EventMapState Closed = new(false, 0, -1, Array.Empty<EventMapMarker>(), false, new Dictionary<int, EventQueryMapEntry>());

        private EventMapState(bool open, int zone, int mapNumber, IReadOnlyList<EventMapMarker> markers, bool queryMap,
            IReadOnlyDictionary<int, EventQueryMapEntry> queryEntries)
        {
            IsOpen = open;
            Zone = zone;
            MapNumber = mapNumber;
            Markers = markers;
            IsQueryMap = queryMap;
            QueryEntries = queryEntries;
        }

        /// <summary>Whether an event opened a map (0x89 / 0xC8) that is not closed yet.</summary>
        public bool IsOpen { get; }

        /// <summary>The open map's zone id.</summary>
        public int Zone { get; }

        /// <summary>The open map's number (<see cref="ZoneMapFiles.MapsOf"/>), or -1 for the zone's first map.</summary>
        public int MapNumber { get; }

        /// <summary>The markers placed on the open map, in order.</summary>
        public IReadOnlyList<EventMapMarker> Markers { get; }

        /// <summary>Whether the open query has the map behind it (0xD4 sub 0).</summary>
        public bool IsQueryMap { get; }

        /// <summary>The map data of the next or open query's options by 0-based option index.</summary>
        public IReadOnlyDictionary<int, EventQueryMapEntry> QueryEntries { get; }

        public EventMapState Open(int zone, int mapNumber) => new(true, zone, mapNumber, Array.Empty<EventMapMarker>(), IsQueryMap, QueryEntries);

        public EventMapState Close() => new(false, 0, -1, Array.Empty<EventMapMarker>(), IsQueryMap, QueryEntries);

        /// <summary>Places marker <paramref name="marker"/>, replacing one with its index.</summary>
        public EventMapState Mark(EventMapMarker marker)
        {
            var markers = new List<EventMapMarker>(Markers.Count + 1);
            foreach (var m in Markers)
            {
                if (m.Index != marker.Index) markers.Add(m);
            }
            markers.Add(marker);
            return new EventMapState(IsOpen, Zone, MapNumber, markers, IsQueryMap, QueryEntries);
        }

        public EventMapState WithQueryEntry(int option, EventQueryMapEntry entry)
        {
            var entries = new Dictionary<int, EventQueryMapEntry>(QueryEntries) { [option] = entry };
            return new EventMapState(IsOpen, Zone, MapNumber, Markers, IsQueryMap, entries);
        }

        public EventMapState WithQueryMap() => new(IsOpen, Zone, MapNumber, Markers, true, QueryEntries);

        /// <summary>The query closed: its map and its options' data go with it.</summary>
        public EventMapState WithoutQuery() => new(IsOpen, Zone, MapNumber, Markers, false, new Dictionary<int, EventQueryMapEntry>());

        /// <summary>
        /// What to draw with the query cursor on 0-based option <paramref name="selectedOption"/> (null when no query is
        /// open): the query's map when it has one and the option is a place it knows, else the event's open map, else nothing.
        /// The retail capture (#168, Home_Point_Map.mp4) shows the map only on a home point entry, not on "Nowhere." or the
        /// zone list.
        /// </summary>
        public EventMapFocus? Focus(int? selectedOption)
        {
            if (IsQueryMap && selectedOption is int option)
            {
                if (QueryEntries.TryGetValue(option, out var entry) && entry is { Sub: 1, First: EventQueryMapEntry.HomePointKind }
                    && HomePoints.TryGet(entry.Second, out int zone, out _, out float x, out float z))
                {
                    return new EventMapFocus(zone, -1, x, z, new[] { new EventMapMarker(entry.Second, x, z, HomePointLabel) }, EventMapPin.HomePoint);
                }
                if (!IsOpen) return null;
            }
            if (!IsOpen) return null;
            float? fx = null, fz = null;
            if (Markers.Count > 0) (fx, fz) = (Markers[^1].X, Markers[^1].Z);
            return new EventMapFocus(Zone, MapNumber, fx, fz, Markers, EventMapPin.Event);
        }

        /// <summary>The home point marker's label (retail capture, #168).</summary>
        public const string HomePointLabel = "Home Point";
    }
}
