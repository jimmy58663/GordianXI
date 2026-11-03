// src/Gordian.Core/Resources/Tables/HomePoints.cs
using System;
using System.Collections.Generic;

namespace Gordian.Core.Resources.Tables
{
    /// <summary>
    /// The home points by their global number (#168): the number the home point event gives each list entry with opcode
    /// 0xD4 sub-case 1 (kind 0) and LandSandBoat's <c>hpIndex</c>. Read from the retail home point event itself: Southern San
    /// d'Oria's home point NPCs (zone 230, actor 0x010E6087, event 8700) keep per-number tables in their byte code, read
    /// with opcode 0x9D sub 0 through work references: the zone at code offset 0x152E, the number within the zone at 0x172E,
    /// x at 0x1C2E, z at 0x1D2E, y (the height, internal space: smaller is higher) at 0x1E2E (the heading at 0x1F2E; the
    /// gil cost at 0x202E). The warp
    /// subroutine at 0x121E reads x / z / y from them for the same-zone teleport (0x05C mode 1). **Beyond XiEvents and
    /// LandSandBoat:** the table's place in the event DAT. Positions are yalms (the tables hold thousandths). Checked by
    /// <c>HomePointTableTests</c> against the install. Retail's own table differs from LandSandBoat's NPC comments in places
    /// (Windurst Woods #1 is at (9.1, -0.4) here, the comment's (-98.6, -183.4) is Lower Jeuno #1).
    /// <para>
    /// The floor (#481): which of a multi-floor zone's maps a point is on is the map its collision floor names
    /// (<see cref="World.Collision.ZoneCollisionMesh.MapFloorUnder"/>) at the point's x, y and z. <see cref="Floors"/> holds
    /// the result for every point in a zone with several maps (Castle Zvahl Keep #1 is on map 4, the floor round 3 of #168
    /// found it on), so a list of another zone's home points needs no collision loaded; <c>ZoneMapProjectionTests</c>
    /// checks it against the install.
    /// </para>
    /// </summary>
    public static class HomePoints
    {
        /// <summary>(zone id, number within the zone, x * 1000, z * 1000, y * 1000) by global number.</summary>
        private static readonly (short Zone, byte Number, int X, int Z, int Y)[] Points =
        {
            (230, 1, -85468, -66454, 1000), (230, 2, 45000, -35000, 2000), (230, 3, 140000, 123000, -2000),
            (231, 1, -178101, 71279, 4000), (231, 2, 10000, 95000, -200), (231, 3, 70000, 10000, -200),
            (232, 1, -38000, -63000, -4000), (232, 2, 48000, -105000, -12000), (232, 3, -6000, -150000, -13000),
            (234, 1, 39189, -42618, 0), (234, 2, 118000, -58000, 1000), (235, 1, -344000, -155000, -10000),
            (235, 2, -328000, -33000, -12000), (235, 3, -189000, 26000, -8000), (236, 1, 126000, 8000, 8500),
            (236, 2, 40000, -238000, 8500), (237, 1, 45000, -19000, -14000), (238, 1, -32022, 131741, -5000),
            (238, 2, 138000, -14000, 0), (239, 1, -72070, 124784, -5013), (239, 2, -212000, -99000, 0),
            (239, 3, 31000, -40000, -6500), (240, 1, -188000, 101000, -4000), (240, 2, -207000, 210000, -8160),
            (240, 3, 180000, 226000, -12000), (241, 1, 9088, -383, -2500), (241, 2, 107000, -56000, -5000),
            (241, 3, -92000, 62000, -5000), (241, 4, 74000, -139000, -7500), (243, 1, -6000, 0, 3000),
            (243, 2, 53000, -57000, 9000), (243, 3, -67000, -25000, 6000), (244, 1, -98981, 167569, 0),
            (244, 2, 32000, -44000, -1000), (244, 3, -52000, 16000, 1000), (245, 1, -98588, -183416, 0),
            (245, 2, 18000, 54000, -1000), (246, 1, 37076, 8831, 0), (246, 2, -155000, -4000, -1000),
            (250, 1, 77654, -94457, -13000), (249, 1, -12750, 87286, -15791), (252, 1, -26910, -47164, 296),
            (247, 1, -29276, -76585, 0), (248, 1, 36117, 34635, -10729), (256, 1, -84435, -32303, 3999),
            (257, 1, -51857, 59877, -150), (261, 1, -107000, 295000, 3200), (262, 1, -193000, -252000, -500),
            (265, 1, -415000, 409000, -63200), (263, 1, -420000, -62000, 0), (266, 1, -23000, 174000, 0),
            (267, 1, 210000, 315000, 20300), (142, 1, 434000, 171000, -40300), (143, 1, 109000, -147000, -38500),
            (145, 1, -132000, -303000, -3000), (204, 1, 243000, 62000, -24500), (208, 1, -984000, -289000, 17000),
            (160, 1, -80000, 62000, 46000), (162, 1, -554000, 66000, -70000), (130, 1, 5000, 526000, -42000),
            (130, 2, -312000, -422000, -42000), (130, 3, -499000, 167000, -42000), (130, 4, 500000, 158000, -42000),
            (130, 5, 305000, -427000, -42000), (26, 1, -1250, 107425, -27907), (50, 1, -21130, -20944, 0),
            (53, 1, -19860, -25441, 0), (48, 1, 105457, 49951, -2000), (80, 1, -85468, -66454, 1000),
            (87, 1, -293048, -102558, -10000), (94, 1, -32022, 131741, -5000), (158, 1, -365000, -36000, -176500),
            (178, 1, -13000, 61000, 48000), (29, 1, -520000, 505000, -18856), (52, 1, -98000, -493000, -10000),
            (79, 1, -449000, -497000, 13400), (5, 1, 65000, 181000, -196500), (5, 2, 379000, -62600, 23000),
            (5, 3, 424000, 220000, -32500), (5, 4, 64000, 460000, -96500), (5, 5, -219000, -62000, -1000),
            (7, 1, -200000, 342000, -10000), (9, 1, -58000, 14000, 40000), (12, 1, 440000, -18000, 28000),
            (30, 1, 187000, 361000, 1000), (33, 1, 7000, 709000, 0), (33, 2, -532000, 447000, 0), (33, 3, 569000, 410000, 0),
            (34, 1, -12000, -288000, 500), (35, 1, -426000, 368000, 0), (61, 1, -533000, 56500, -4100),
            (113, 1, -303000, 526000, -8000), (153, 1, 88000, -217000, -15420), (160, 2, 183000, -61000, 34470),
            (204, 2, 102344, 270362, -113), (205, 1, -63000, 81000, 50000), (208, 2, 573000, -499000, 8950),
            (230, 4, -165000, 11000, -1000), (231, 4, -133000, 195000, 12000), (234, 3, 87000, 1000, 7000),
            (235, 4, -191000, -69000, -6000), (236, 3, -127000, 10000, -6000), (237, 2, -78000, 3000, 2000),
            (238, 3, 5000, -175000, -4000), (252, 2, -66000, 54000, -5200), (247, 2, -21000, 110000, 8130),
            (50, 2, 130000, -16000, 0), (50, 3, -108000, 108000, -6000), (50, 4, -99000, -68000, 0), (256, 2, 31950, -164000, 0),
            (257, 2, -51500, -96500, -150), (137, 1, 223333, -254167, -13427), (281, 1, 4000, 8500, -190),
            (155, 1, -554000, 66000, -70000), (126, 1, -212000, 93000, -21000), (169, 1, -257500, 82000, 24000),
            (276, 1, 757000, 17500, 120000), (25, 1, -65000, 563000, -17500), (238, 4, -92000, 54000, -2000),
            (241, 5, -43500, -145000, 0), (26, 2, 14000, -5000, -10000), (26, 3, 73590, 38870, -36150),
        };

        /// <summary>
        /// The map of the points in multi-floor zones whose floor names one (global number to map number); the others are on
        /// their zone's first map.
        /// </summary>
        private static readonly Dictionary<int, int> Floors = new()
        {
            [17] = 1, [18] = 1, [52] = 2, [53] = 3, [54] = 1, [55] = 1, [56] = 4, [57] = 2, [58] = 4, [64] = 2, [70] = 1,
            [71] = 3, [72] = 15, [73] = 2, [74] = 2, [75] = 1, [84] = 1, [88] = 1, [89] = 1, [92] = 2, [93] = 6, [94] = 15,
            [95] = 18, [96] = 1, [103] = 2, [113] = 4, [115] = 1, [116] = 2, [118] = 1, [120] = 3, [121] = 1,
        };

        /// <summary>How many home points the table holds.</summary>
        public static int Count => Points.Length;

        /// <summary>Home point <paramref name="number"/>'s zone, its number in that zone ("Home Point #n") and its x / z in yalms.</summary>
        public static bool TryGet(int number, out int zoneId, out int numberInZone, out float x, out float z) =>
            TryGet(number, out zoneId, out numberInZone, out x, out _, out z);

        /// <summary>Home point <paramref name="number"/>'s zone, number in that zone and x / y / z in yalms (y internal: smaller is higher).</summary>
        public static bool TryGet(int number, out int zoneId, out int numberInZone, out float x, out float y, out float z)
        {
            if (number < 0 || number >= Points.Length || Points[number].Zone == 0)
            {
                zoneId = numberInZone = 0;
                x = y = z = 0;
                return false;
            }
            var p = Points[number];
            (zoneId, numberInZone, x, y, z) = (p.Zone, p.Number, p.X * 0.001f, p.Y * 0.001f, p.Z * 0.001f);
            return true;
        }

        /// <summary>The map home point <paramref name="number"/> is on (<see cref="ZoneMapFiles.MapsOf"/>), or -1 for none.</summary>
        public static int MapOf(int number) =>
            number < 0 || number >= Points.Length ? -1 : ZoneMapFiles.FloorOf(Points[number].Zone, Floors.GetValueOrDefault(number));

        /// <summary>The home points of zone <paramref name="zoneId"/> with the map each is on, in global number order (the Map window's icons).</summary>
        public static IReadOnlyList<ZoneMapPoint> InZone(int zoneId)
        {
            var points = new List<ZoneMapPoint>();
            for (int i = 0; i < Points.Length; i++)
            {
                if (Points[i].Zone == zoneId) points.Add(new ZoneMapPoint(Points[i].X * 0.001f, Points[i].Z * 0.001f, MapOf(i)));
            }
            return points;
        }
    }
}
