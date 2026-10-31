// src/Gordian.Core/Resources/Tables/ZoneMapProjection.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Gordian.Core.Resources.Containers;

namespace Gordian.Core.Resources.Tables
{
    /// <summary>Where a zone map's 512 x 512 image sits: the top-left corner of the quad its element group draws.</summary>
    public readonly record struct ZoneMapOrigin(int X, int Y);

    /// <summary>
    /// World to map coordinates for the zone maps of <see cref="ZoneMapFiles"/> (#168). Each map DAT's one-part element group
    /// (Section 0x31, <c>menumap m_ZZZ_FF</c>) draws the image as a 512 x 512 quad whose corners are int16 pairs after the
    /// two 16-byte names: (-288, -272) .. (224, 240) for Southern San d'Oria, (-304, -304) .. (208, 208) for Northern San
    /// d'Oria. **Beyond xi-tools** (which only lists the files): the quad's top-left corner is minus the map pixel of the
    /// world origin, so a world point (x, z) (z north) is at map pixel (x * s - corner.X, -z * s - corner.Y), s being the
    /// map's pixels per yalm.
    /// <para>
    /// The scale is not in the DAT. Town maps are 0.8 px per yalm: the maintainer's retail screenshots of the home point
    /// map (round 1 of #168, 2026-10-30), registered against the map images, put Northern San d'Oria's home points #1
    /// (-178.1, 71.3) and #4 (-133, 195) and Southern San d'Oria's #2 (45, -35) at s = 0.79-0.81 with the corner as the
    /// origin (within 2 px), and the guide Teilsa (-83.6, -100.8) where the maintainer marked her on Port San d'Oria's map
    /// at s = 0.8. Field and dungeon maps are 0.2: the retail home point table (<see cref="HomePoints"/>) lands on the
    /// drawn rooms, paths and islands of the maps listed below at 0.2 and off them at 0.25 or 0.8 (plotted 2026-10-30;
    /// Ru'Aun Gardens' five island home points and Fei'Yin's two rooms were the clearest). <see cref="ScaleOf"/> answers
    /// only for the maps checked; a zone with several maps (floors) is listed only for the map its home points fit.
    /// </para>
    /// </summary>
    public static class ZoneMapProjection
    {
        /// <summary>Town maps (zone, map number), 0.8 px per yalm: their home points land on the drawn streets.</summary>
        private static readonly HashSet<(int Zone, int Map)> TownMaps = new()
        {
            (26, 1), (48, 0), (50, 0), (53, 0), (80, 0), (87, 0), (230, 0), (231, 0), (232, 0), (234, 0), (235, 0), (236, 0),
            (237, 0), (238, 0), (239, 0), (240, 0), (241, 0), (243, 0), (244, 0), (245, 0), (246, 0), (247, 0), (248, 0),
            (249, 0), (250, 0), (252, 0), (256, 0), (257, 0),
        };

        /// <summary>Field and dungeon maps (zone, map number), 0.2 px per yalm.</summary>
        private static readonly HashSet<(int Zone, int Map)> FieldMaps = new()
        {
            (5, 0), (7, 0), (12, 0), (25, 0), (33, 0), (61, 0), (126, 0), (130, 0), (137, 0), (142, 0), (145, 1), (204, 1),
            (261, 0), (262, 0), (263, 0), (265, 0), (266, 0), (267, 0),
        };

        /// <summary>A town map's pixels per yalm.</summary>
        public const float TownScale = 0.8f;

        /// <summary>A field or dungeon map's pixels per yalm.</summary>
        public const float FieldScale = 0.2f;

        /// <summary>The map's pixels per yalm, or null when the map is not calibrated.</summary>
        public static float? ScaleOf(int zoneId, int map) =>
            TownMaps.Contains((zoneId, map)) ? TownScale : FieldMaps.Contains((zoneId, map)) ? FieldScale : null;

        /// <summary>The map pixel (0-512 across and down the image) of world point (<paramref name="x"/>, <paramref name="z"/>).</summary>
        public static (float U, float V) ToMap(ZoneMapOrigin origin, float scale, float x, float z) =>
            (x * scale - origin.X, -z * scale - origin.Y);

        /// <summary>The image quad's top-left corner from a map DAT's element group, or null when it has none.</summary>
        public static ZoneMapOrigin? ReadOrigin(ReadOnlySpan<byte> dat)
        {
            // Group layout: name (16), a byte, name (16), three bytes, then the quad's four corners as int16 (x, y).
            const int CornerOffset = 36;
            foreach (var header in DatSectionWalker.ReadHeaders(dat))
            {
                if (header.TypeCode != DatSectionType.UiElementGroup || header.DataOffset + CornerOffset + 4 > dat.Length) continue;
                var corner = dat.Slice(header.DataOffset + CornerOffset, 4);
                return new ZoneMapOrigin(BinaryPrimitives.ReadInt16LittleEndian(corner), BinaryPrimitives.ReadInt16LittleEndian(corner[2..]));
            }
            return null;
        }
    }
}
