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
    /// The scale is not in the DAT, and the maps come in three: 0.8 px per yalm for towns, 0.4 for dungeons and 0.2 for
    /// fields (with some towns and dungeons drawn at 0.2 too). The town scale was first registered on the maintainer's
    /// retail screenshots of the home point map (round 1 of #168, 2026-10-30): Northern San d'Oria's home points #1
    /// (-178.1, 71.3) and #4 (-133, 195) and Southern San d'Oria's #2 (45, -35) land at s = 0.79-0.81 with the corner as
    /// the origin (within 2 px), and the guide Teilsa (-83.6, -100.8) where the maintainer marked her on Port San d'Oria's
    /// map. Each map's scale below was then fitted (2026-11-01, round 2 of #485) by plotting LandSandBoat's mob spawn
    /// points and NPC positions for the zone (its <c>mob_spawn_points</c> and <c>npc_list</c> tables) at 0.2, 0.4 and 0.8
    /// with the corner as the origin, and keeping the scale whose points land on the map's drawn areas (the pixels that
    /// differ from the blank parchment every map shares) and cover them best: hits times coverage at least 1.3 times the
    /// next scale's, with at least half the points on drawn pixels (60% for a zone with one map). The corner rule held on
    /// every map kept. That fit overturned the earlier guess that dungeons are 0.2: Newton Movalpolos, Fei'Yin, Giddeus,
    /// Yughott Grotto and zone 266's maps line up only at 0.4. <see cref="ScaleOf"/> answers only for the
    /// 203 maps kept (of 646); the rest, mostly the floors of multi-floor dungeons with too few spawn points on them, stay
    /// uncalibrated.
    /// </para>
    /// </summary>
    public static class ZoneMapProjection
    {
        /// <summary>Maps (zone, map number) drawn at 0.8 px per yalm: towns, Dynamis towns, Ro'Maeve, Dragon's Aery.</summary>
        private static readonly HashSet<(int Zone, int Map)> TownMaps = new()
        {
            (26, 1), (26, 2), (27, 2), (28, 2), (42, 1), (42, 2), (48, 0), (50, 0), (50, 1), (53, 0), (71, 2), (80, 0), (87, 0),
            (94, 1), (96, 2), (122, 0), (154, 0), (158, 1), (158, 2), (158, 3), (174, 2), (184, 1), (184, 2), (184, 3), (185, 0),
            (186, 0), (187, 0), (188, 0), (230, 0), (231, 0), (232, 0), (233, 0), (234, 0), (235, 0), (236, 0), (237, 0), (238, 0),
            (238, 1), (239, 0), (240, 0), (241, 0), (243, 0), (244, 0), (245, 0), (246, 0), (247, 0), (248, 0), (249, 0), (250, 0),
            (252, 0), (256, 0), (257, 0),
        };

        /// <summary>Maps drawn at 0.4 px per yalm: most dungeons.</summary>
        private static readonly HashSet<(int Zone, int Map)> DungeonMaps = new()
        {
            (11, 0), (12, 0), (16, 0), (18, 0), (20, 0), (22, 0), (62, 1), (65, 1), (85, 1), (92, 1), (127, 0), (128, 0), (136, 0),
            (138, 3), (140, 1), (141, 0), (142, 0), (142, 1), (143, 1), (143, 2), (143, 3), (145, 1), (147, 1), (148, 0), (149, 0),
            (150, 0), (151, 1), (151, 2), (151, 15), (155, 1), (161, 2), (161, 15), (162, 1), (162, 2), (165, 1), (165, 2),
            (165, 3), (165, 4), (166, 0), (167, 16), (171, 3), (172, 0), (173, 0), (174, 0), (175, 1), (175, 2), (177, 1),
            (178, 1), (190, 1), (191, 0), (191, 1), (193, 15), (195, 16), (198, 15), (198, 16), (200, 16), (204, 1), (204, 15),
            (212, 2), (213, 0), (258, 0), (266, 0), (268, 1), (269, 1), (270, 1), (271, 6), (272, 1), (273, 1), (276, 1),
        };

        /// <summary>Maps drawn at 0.2 px per yalm: the fields and the larger dungeons.</summary>
        private static readonly HashSet<(int Zone, int Map)> FieldMaps = new()
        {
            (2, 0), (2, 1), (4, 1), (5, 0), (7, 0), (15, 0), (24, 0), (25, 0), (26, 3), (29, 2), (30, 2), (33, 0), (39, 0),
            (40, 0), (41, 0), (42, 3), (45, 0), (51, 0), (52, 1), (52, 2), (61, 0), (71, 1), (79, 1), (79, 2), (81, 0), (82, 0),
            (83, 0), (84, 0), (88, 0), (89, 0), (90, 0), (91, 0), (95, 0), (97, 0), (98, 0), (100, 0), (101, 0), (102, 0),
            (103, 0), (104, 0), (105, 0), (106, 0), (107, 0), (108, 0), (109, 0), (110, 0), (111, 0), (112, 0), (113, 0), (114, 0),
            (115, 0), (116, 0), (117, 0), (118, 0), (119, 0), (120, 0), (121, 0), (123, 0), (124, 0), (125, 0), (126, 0),
            (129, 10), (130, 0), (132, 0), (135, 0), (137, 0), (153, 2), (215, 0), (216, 0), (217, 0), (218, 0), (253, 0),
            (254, 0), (260, 0), (261, 0), (262, 0), (263, 0), (265, 0), (267, 0), (288, 0), (289, 0), (291, 1),
        };

        /// <summary>A town map's pixels per yalm.</summary>
        public const float TownScale = 0.8f;

        /// <summary>A dungeon map's pixels per yalm.</summary>
        public const float DungeonScale = 0.4f;

        /// <summary>A field map's pixels per yalm.</summary>
        public const float FieldScale = 0.2f;

        /// <summary>The map's pixels per yalm, or null when the map is not calibrated.</summary>
        public static float? ScaleOf(int zoneId, int map) =>
            TownMaps.Contains((zoneId, map)) ? TownScale
            : DungeonMaps.Contains((zoneId, map)) ? DungeonScale
            : FieldMaps.Contains((zoneId, map)) ? FieldScale
            : null;

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
