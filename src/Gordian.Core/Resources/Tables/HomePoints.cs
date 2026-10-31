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
    /// x at 0x1C2E, z at 0x1D2E (y, the height, at 0x1E2E; the heading at 0x1F2E; the gil cost at 0x202E). The warp
    /// subroutine at 0x121E reads x / z / y from them for the same-zone teleport (0x05C mode 1). **Beyond XiEvents and
    /// LandSandBoat:** the table's place in the event DAT. Positions are yalms (the tables hold thousandths). Checked by
    /// <c>HomePointTableTests</c> against the install. Retail's own table differs from LandSandBoat's NPC comments in places
    /// (Windurst Woods #1 is at (9.1, -0.4) here, the comment's (-98.6, -183.4) is Lower Jeuno #1).
    /// </summary>
    public static class HomePoints
    {
        /// <summary>(zone id, number within the zone, x * 1000, z * 1000) by global number.</summary>
        private static readonly (short Zone, byte Number, int X, int Z)[] Points =
        {
            (230, 1, -85468, -66454), (230, 2, 45000, -35000), (230, 3, 140000, 123000), (231, 1, -178101, 71279), (231, 2, 10000, 95000), (231, 3, 70000, 10000),
            (232, 1, -38000, -63000), (232, 2, 48000, -105000), (232, 3, -6000, -150000), (234, 1, 39189, -42618), (234, 2, 118000, -58000), (235, 1, -344000, -155000),
            (235, 2, -328000, -33000), (235, 3, -189000, 26000), (236, 1, 126000, 8000), (236, 2, 40000, -238000), (237, 1, 45000, -19000), (238, 1, -32022, 131741),
            (238, 2, 138000, -14000), (239, 1, -72070, 124784), (239, 2, -212000, -99000), (239, 3, 31000, -40000), (240, 1, -188000, 101000), (240, 2, -207000, 210000),
            (240, 3, 180000, 226000), (241, 1, 9088, -383), (241, 2, 107000, -56000), (241, 3, -92000, 62000), (241, 4, 74000, -139000), (243, 1, -6000, 0),
            (243, 2, 53000, -57000), (243, 3, -67000, -25000), (244, 1, -98981, 167569), (244, 2, 32000, -44000), (244, 3, -52000, 16000), (245, 1, -98588, -183416),
            (245, 2, 18000, 54000), (246, 1, 37076, 8831), (246, 2, -155000, -4000), (250, 1, 77654, -94457), (249, 1, -12750, 87286), (252, 1, -26910, -47164),
            (247, 1, -29276, -76585), (248, 1, 36117, 34635), (256, 1, -84435, -32303), (257, 1, -51857, 59877), (261, 1, -107000, 295000), (262, 1, -193000, -252000),
            (265, 1, -415000, 409000), (263, 1, -420000, -62000), (266, 1, -23000, 174000), (267, 1, 210000, 315000), (142, 1, 434000, 171000), (143, 1, 109000, -147000),
            (145, 1, -132000, -303000), (204, 1, 243000, 62000), (208, 1, -984000, -289000), (160, 1, -80000, 62000), (162, 1, -554000, 66000), (130, 1, 5000, 526000),
            (130, 2, -312000, -422000), (130, 3, -499000, 167000), (130, 4, 500000, 158000), (130, 5, 305000, -427000), (26, 1, -1250, 107425), (50, 1, -21130, -20944),
            (53, 1, -19860, -25441), (48, 1, 105457, 49951), (80, 1, -85468, -66454), (87, 1, -293048, -102558), (94, 1, -32022, 131741), (158, 1, -365000, -36000),
            (178, 1, -13000, 61000), (29, 1, -520000, 505000), (52, 1, -98000, -493000), (79, 1, -449000, -497000), (5, 1, 65000, 181000), (5, 2, 379000, -62600),
            (5, 3, 424000, 220000), (5, 4, 64000, 460000), (5, 5, -219000, -62000), (7, 1, -200000, 342000), (9, 1, -58000, 14000), (12, 1, 440000, -18000),
            (30, 1, 187000, 361000), (33, 1, 7000, 709000), (33, 2, -532000, 447000), (33, 3, 569000, 410000), (34, 1, -12000, -288000), (35, 1, -426000, 368000),
            (61, 1, -533000, 56500), (113, 1, -303000, 526000), (153, 1, 88000, -217000), (160, 2, 183000, -61000), (204, 2, 102344, 270362), (205, 1, -63000, 81000),
            (208, 2, 573000, -499000), (230, 4, -165000, 11000), (231, 4, -133000, 195000), (234, 3, 87000, 1000), (235, 4, -191000, -69000), (236, 3, -127000, 10000),
            (237, 2, -78000, 3000), (238, 3, 5000, -175000), (252, 2, -66000, 54000), (247, 2, -21000, 110000), (50, 2, 130000, -16000), (50, 3, -108000, 108000),
            (50, 4, -99000, -68000), (256, 2, 31950, -164000), (257, 2, -51500, -96500), (137, 1, 223333, -254167), (281, 1, 4000, 8500), (155, 1, -554000, 66000),
            (126, 1, -212000, 93000), (169, 1, -257500, 82000), (276, 1, 757000, 17500), (25, 1, -65000, 563000), (238, 4, -92000, 54000), (241, 5, -43500, -145000),
            (26, 2, 14000, -5000), (26, 3, 73590, 38870),
        };

        /// <summary>How many home points the table holds.</summary>
        public static int Count => Points.Length;

        /// <summary>Home point <paramref name="number"/>'s zone, its number in that zone ("Home Point #n") and its x / z in yalms.</summary>
        public static bool TryGet(int number, out int zoneId, out int numberInZone, out float x, out float z)
        {
            if (number < 0 || number >= Points.Length || Points[number].Zone == 0)
            {
                zoneId = numberInZone = 0;
                x = z = 0;
                return false;
            }
            var p = Points[number];
            (zoneId, numberInZone, x, z) = (p.Zone, p.Number, p.X * 0.001f, p.Z * 0.001f);
            return true;
        }

        /// <summary>The home points of zone <paramref name="zoneId"/>: their x / z in yalms, in global number order (the Map window's icons).</summary>
        public static IReadOnlyList<(float X, float Z)> InZone(int zoneId)
        {
            var points = new List<(float X, float Z)>();
            foreach (var p in Points)
            {
                if (p.Zone == zoneId) points.Add((p.X * 0.001f, p.Z * 0.001f));
            }
            return points;
        }
    }
}
