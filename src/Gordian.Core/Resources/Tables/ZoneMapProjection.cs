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
    /// The scale is not in the DAT. It is 1 on the city maps checked: the maintainer's retail capture puts Northern San
    /// d'Oria (-133.2, 201.1) in grid square F-4, which s = 1 gives (map pixel 171, 103), and the retail home point table
    /// (<see cref="HomePoints"/>) and the guide markers of the intro events (opcode 0x8B) land on the drawn streets of the
    /// San d'Oria, Bastok and Windurst maps at s = 1 (checked by plotting them, 2026-10-29). Field maps are smaller: Cape
    /// Teriggan's outpost (-185, -63) fits the outpost drawn on its map at about s = 1/4, so a field map is not given a
    /// scale until it is calibrated. <see cref="ScaleOf"/> answers only for the calibrated maps.
    /// </para>
    /// </summary>
    public static class ZoneMapProjection
    {
        /// <summary>The maps whose scale was checked (zone, map number): the cities of the three starting nations.</summary>
        private static readonly HashSet<(int Zone, int Map)> CityScale = new()
        {
            (230, 0), (231, 0), (232, 0), (234, 0), (235, 0), (236, 0), (238, 0), (239, 0), (240, 0), (241, 0),
        };

        /// <summary>The map's pixels per yalm, or null when the map is not calibrated.</summary>
        public static float? ScaleOf(int zoneId, int map) => CityScale.Contains((zoneId, map)) ? 1f : null;

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
