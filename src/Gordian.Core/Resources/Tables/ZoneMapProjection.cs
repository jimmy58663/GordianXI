// src/Gordian.Core/Resources/Tables/ZoneMapProjection.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Gordian.Core.Resources.Containers;

namespace Gordian.Core.Resources.Tables
{
    /// <summary>Where a zone map's 512 x 512 image sits: the top-left corner of the quad its element group draws.</summary>
    public readonly record struct ZoneMapOrigin(int X, int Y);

    /// <summary>A travel point on a zone map: its world x / z in yalms and the map (<see cref="ZoneMapFiles.MapsOf"/>) it is on.</summary>
    public readonly record struct ZoneMapPoint(float X, float Z, int Map);

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
    /// Yughott Grotto and zone 266's maps line up only at 0.4.
    /// </para>
    /// <para>
    /// The floors of multi-floor zones were fitted a second time (2026-11-02, #481) with the zone's own collision: each
    /// floor triangle names its map (<see cref="World.Collision.CollisionTriangle.MapFloor"/>), so every map's floors were
    /// sampled and plotted at 0.2, 0.25, 0.3, 0.4, 0.5, 0.6, 0.8 and 1.0 with the corner as the origin. A map is kept when
    /// one of 0.2 / 0.4 / 0.8 puts the most samples on drawn pixels of all eight, at least 55% of them, and 1.25 times the
    /// other two. That fit agreed with 113 of the 116 maps the first fit had placed and it could judge (zone 129 map 10,
    /// zone 136 and Port Jeuno, where it disagreed, keep the first fit's scale), and added 338 maps:
    /// Castle Zvahl Keep's floors 3 and 4 at 0.4 (its home point is on floor 4), Pso'Xja's at 0.8. No map needed an offset
    /// past 2 px from the corner rule. <see cref="ScaleOf"/> answers for the 541 maps kept (of 646); the rest stay
    /// uncalibrated and show centred without markers.
    /// </para>
    /// </summary>
    public static class ZoneMapProjection
    {
        /// <summary>Maps (zone, map number) drawn at 0.8 px per yalm: towns, Dynamis towns, Ro'Maeve, Dragon's Aery.</summary>
        private static readonly HashSet<(int Zone, int Map)> TownMaps = new()
        {
            (9, 1), (9, 2), (9, 3), (9, 4), (9, 5), (9, 6), (9, 7), (9, 8), (9, 9), (9, 11), (14, 2), (14, 3), (26, 1), (26, 2),
            (27, 1), (27, 2), (27, 3), (28, 2), (42, 1), (42, 2), (48, 0), (50, 0), (50, 1), (53, 0), (70, 1), (70, 2), (70, 3),
            (70, 4), (70, 5), (70, 6), (71, 2), (80, 0), (87, 0), (94, 1), (96, 2), (122, 0), (142, 2), (153, 1), (154, 0),
            (157, 4), (157, 5), (157, 7), (157, 8), (157, 9), (158, 1), (158, 2), (158, 3), (158, 6), (160, 2), (160, 5),
            (160, 6), (160, 8), (167, 15), (173, 3), (173, 4), (174, 2), (176, 1), (176, 2), (176, 4), (177, 3), (177, 4),
            (177, 5), (177, 7), (178, 2), (178, 3), (178, 4), (178, 5), (178, 15), (184, 1), (184, 2), (184, 3), (185, 0),
            (186, 0), (187, 0), (188, 0), (205, 2), (205, 3), (205, 5), (205, 15), (205, 16), (205, 17), (205, 18), (208, 5),
            (230, 0), (231, 0), (232, 0), (233, 0), (234, 0), (235, 0), (236, 0), (237, 0), (238, 0), (238, 1), (238, 2),
            (239, 0), (240, 0), (241, 0), (243, 0), (244, 0), (245, 0), (246, 0), (247, 0), (248, 0), (249, 0), (250, 0),
            (252, 0), (256, 0), (257, 0), (259, 2), (259, 4), (259, 5), (259, 6), (259, 8), (259, 11), (259, 12), (259, 13),
            (259, 14), (259, 15),
        };

        /// <summary>Maps drawn at 0.4 px per yalm: most dungeons.</summary>
        private static readonly HashSet<(int Zone, int Map)> DungeonMaps = new()
        {
            (11, 0), (12, 0), (16, 0), (18, 0), (20, 0), (22, 0), (34, 15), (34, 16), (35, 2), (35, 3), (35, 4), (43, 1), (54, 1),
            (54, 2), (54, 4), (54, 5), (54, 6), (54, 7), (54, 9), (54, 10), (54, 11), (55, 1), (55, 2), (55, 3), (55, 4), (55, 5),
            (55, 6), (55, 8), (55, 9), (55, 10), (56, 2), (56, 3), (56, 5), (56, 6), (56, 7), (56, 8), (56, 9), (56, 10), (62, 1),
            (62, 2), (62, 3), (62, 4), (63, 1), (63, 2), (63, 3), (63, 4), (63, 5), (63, 6), (63, 7), (63, 8), (63, 9), (63, 10),
            (65, 1), (65, 2), (65, 3), (66, 1), (66, 2), (66, 3), (66, 4), (66, 5), (66, 6), (66, 7), (66, 8), (66, 9), (66, 10),
            (68, 1), (68, 2), (68, 3), (68, 4), (68, 5), (68, 6), (68, 7), (69, 1), (69, 2), (69, 3), (69, 4), (69, 5), (69, 6),
            (69, 7), (69, 8), (69, 9), (69, 10), (72, 1), (72, 2), (72, 3), (72, 4), (72, 5), (72, 6), (72, 7), (72, 9), (72, 10),
            (73, 1), (73, 2), (73, 3), (73, 4), (73, 5), (73, 6), (74, 1), (74, 2), (74, 3), (74, 4), (74, 5), (74, 6), (75, 1),
            (75, 2), (75, 3), (75, 4), (76, 1), (76, 2), (76, 3), (76, 4), (77, 1), (77, 2), (77, 3), (77, 4), (77, 5), (77, 6),
            (77, 7), (85, 1), (86, 2), (86, 3), (86, 4), (86, 5), (86, 6), (86, 7), (86, 8), (86, 9), (86, 10), (86, 11),
            (86, 12), (86, 13), (86, 14), (86, 15), (86, 16), (86, 17), (86, 18), (92, 1), (92, 15), (93, 1), (93, 2), (93, 3),
            (93, 4), (93, 5), (93, 6), (93, 7), (93, 8), (93, 9), (93, 10), (93, 11), (93, 12), (93, 13), (93, 14), (93, 15),
            (93, 17), (93, 18), (99, 1), (99, 2), (99, 3), (99, 4), (99, 5), (99, 6), (127, 0), (128, 0), (129, 1), (129, 2),
            (129, 3), (129, 4), (129, 5), (129, 6), (129, 7), (129, 8), (129, 9), (129, 11), (129, 12), (129, 13), (129, 14),
            (129, 15), (129, 16), (129, 17), (129, 18), (136, 0), (138, 1), (138, 2), (138, 3), (140, 1), (141, 0), (142, 0),
            (142, 1), (143, 1), (143, 2), (143, 3), (145, 1), (145, 15), (147, 1), (147, 15), (148, 0), (149, 0), (150, 0),
            (151, 1), (151, 2), (151, 3), (151, 5), (151, 6), (151, 15), (152, 0), (155, 1), (155, 2), (155, 4), (160, 3),
            (161, 1), (161, 2), (161, 15), (162, 1), (162, 2), (162, 3), (162, 4), (164, 2), (164, 3), (164, 4), (165, 1),
            (165, 2), (165, 3), (165, 4), (166, 0), (167, 16), (167, 17), (169, 1), (169, 2), (171, 1), (171, 2), (171, 3),
            (172, 0), (173, 0), (174, 0), (175, 1), (175, 2), (175, 3), (177, 1), (178, 1), (190, 1), (190, 2), (190, 14),
            (191, 0), (191, 1), (191, 14), (192, 1), (192, 2), (192, 3), (192, 4), (193, 1), (193, 2), (193, 14), (193, 15),
            (194, 2), (194, 14), (195, 15), (195, 16), (196, 1), (196, 14), (196, 15), (196, 16), (196, 17), (197, 14), (197, 15),
            (197, 16), (197, 17), (198, 14), (198, 15), (198, 16), (200, 1), (200, 2), (200, 3), (200, 14), (200, 16), (204, 1),
            (204, 15), (212, 1), (212, 2), (213, 0), (258, 0), (266, 0), (268, 1), (268, 2), (269, 1), (270, 1), (270, 2),
            (271, 1), (271, 2), (271, 3), (271, 4), (271, 5), (271, 6), (271, 7), (271, 8), (271, 9), (271, 10), (271, 11),
            (271, 12), (271, 13), (271, 14), (271, 15), (271, 16), (271, 17), (271, 18), (272, 1), (272, 2), (273, 1), (273, 2),
            (274, 1), (274, 2), (275, 1), (275, 2), (275, 3), (275, 4), (275, 5), (275, 6), (275, 7), (275, 8), (275, 9),
            (275, 10), (275, 11), (275, 12), (275, 13), (275, 14), (275, 15), (275, 16), (276, 1), (276, 2),
        };

        /// <summary>Maps drawn at 0.2 px per yalm: the fields and the larger dungeons.</summary>
        private static readonly HashSet<(int Zone, int Map)> FieldMaps = new()
        {
            (2, 0), (2, 1), (4, 1), (4, 2), (5, 0), (7, 0), (15, 0), (24, 0), (25, 0), (26, 3), (29, 1), (29, 2), (30, 1),
            (30, 2), (33, 0), (39, 0), (40, 0), (41, 0), (42, 3), (44, 1), (44, 2), (45, 0), (51, 0), (52, 1), (52, 2), (61, 0),
            (71, 1), (79, 1), (79, 2), (79, 3), (79, 4), (81, 0), (82, 0), (83, 0), (84, 0), (88, 0), (89, 0), (90, 0), (91, 0),
            (95, 0), (96, 1), (97, 0), (98, 0), (100, 0), (101, 0), (102, 0), (103, 0), (104, 0), (105, 0), (106, 0), (107, 0),
            (108, 0), (109, 0), (110, 0), (111, 0), (112, 0), (113, 0), (114, 0), (115, 0), (116, 0), (117, 0), (118, 0),
            (119, 0), (120, 0), (121, 0), (123, 0), (124, 0), (125, 0), (126, 0), (129, 10), (130, 0), (132, 0), (134, 0),
            (135, 0), (137, 0), (153, 2), (174, 1), (194, 3), (215, 0), (216, 0), (217, 0), (218, 0), (251, 0), (253, 0),
            (254, 0), (260, 0), (261, 0), (262, 0), (263, 0), (264, 1), (265, 0), (267, 0), (288, 0), (289, 0), (291, 1),
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
