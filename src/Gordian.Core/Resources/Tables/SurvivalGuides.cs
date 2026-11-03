// src/Gordian.Core/Resources/Tables/SurvivalGuides.cs
using System.Collections.Generic;

namespace Gordian.Core.Resources.Tables
{
    /// <summary>
    /// The survival guides by their number (#479): the number the survival guide event (8500) gives each list entry with
    /// opcode 0xD4 sub-case 1 (kind 2, the survival guide network; kind 0 is the home points') and LandSandBoat's
    /// <c>survivalGuides</c> key. Read from the retail event itself: every survival guide NPC runs the same script (zone 2's
    /// actor 0x0100217D, 469 references), which keeps per-number tables of work references 0xD0 bytes (104 entries) apart:
    /// the zone at code offset 0x13BF, the gil cost at 0x148F (1000 each), the list order at 0x16FF, x at 0x17CF, z at
    /// 0x189F, y (the height, internal space) at 0x196F and the heading at 0x1A3F, in thousandths; 98 guides, the rest of
    /// each table zero. **Beyond XiEvents and LandSandBoat:** the table's place in the event DAT and kind 2. The floor of
    /// each guide in a multi-floor zone is found as for <see cref="HomePoints"/>. Checked by <c>ZoneMapProjectionTests</c>
    /// against the install: Northern San d'Oria's guide (-174, 80) stands just north of home point #1 (-178.1, 71.3), where
    /// the maintainer's retail screenshot (round 1 image-5 of #168) shows the book. LandSandBoat's own table agrees within
    /// about a yalm where checked (West Ronfaure (-451.4, -218) against (-452.6, -218.0)).
    /// </summary>
    public static class SurvivalGuides
    {
        /// <summary>The survival guide network's travel point kind in opcode 0xD4 sub 1 (home points are 0).</summary>
        public const int TravelKind = 2;

        /// <summary>(zone id, x * 1000, z * 1000, y * 1000) by number.</summary>
        private static readonly (short Zone, int X, int Z, int Y)[] Points =
        {
            (231, -174000, 80000, 4000), (234, 20000, -115000, 0), (240, -220000, 179000, -8284), (243, 43000, -69000, 10000),
            (26, -5000, 102000, -28000), (50, 134000, 17000, 0), (100, -452571, -217970, -19807), (141, -143000, 7000, -20000),
            (167, -11700, 25377, 20), (190, -119000, 248500, 0), (102, 775000, -18000, 28500), (103, 137000, 98000, -7460),
            (108, -222000, 827000, 71200), (193, -104000, 633000, -220), (196, 60000, -266000, -68000),
            (2, 225000, -528000, -2000), (104, 63000, -14000, 400), (105, -67000, 449000, -2000), (149, 223000, -10000, -700),
            (195, 418000, -99500, -52500), (106, -582687, 52281, 40107), (172, -10000, 7000, 0), (173, -463000, -23000, -10000),
            (191, -24000, 1000, -350), (109, 467000, 410000, 24490), (110, -228000, 386000, 4120), (147, -264000, 107000, 1550),
            (197, 364000, -23000, -32400), (115, -13322, 315696, -12459), (169, -308216, 262000, 15999),
            (192, 453000, 182300, -8000), (4, 495000, 728000, -3000), (117, -160000, 648000, 47000),
            (118, -485700, 46200, -32000), (198, -338000, -179000, -12350), (213, -63000, -280000, -20260),
            (119, -297000, 423000, 16852), (120, 344000, -256000, 5660), (151, -221000, -13000, 250),
            (200, -383000, 363500, -6118), (111, -28000, -125000, -59560), (166, -146000, -298000, 4110),
            (112, 204000, -203000, -24000), (161, 372000, -25000, -12000), (126, -252000, 297000, -20000),
            (127, 336000, -74000, 26130), (184, 464000, -51000, 0), (121, -45000, -151000, 200), (122, 15000, 53000, -28000),
            (154, -61000, -33000, -1400), (114, -260000, -265000, 8500), (125, 420000, 11000, -3290),
            (247, -3500, -95000, -2502), (113, -188000, -67000, 8000), (128, -167000, 24000, -8260),
            (174, -16000, -237000, -20810), (212, 296000, 64000, -40700), (123, -239500, -404000, 0),
            (176, 223000, 335000, -24000), (250, -40000, -93000, -10000), (252, -13000, -34000, 1000), (124, 197000, -81000, 0),
            (159, 60000, 57000, -8000), (205, 89000, -297000, 260), (130, 13000, -593000, -54000), (11, -262000, -54000, 8000),
            (24, -554000, -56000, -6200), (25, -61000, 261000, -30000), (27, 252000, -266000, 0), (28, -220000, 57000, -12000),
            (51, -839000, 98000, -20000), (65, 218000, -104000, -24000), (68, 4000, -91000, 20500), (62, 504000, 241000, -410),
            (53, -20000, -33000, 0), (54, 475000, -20000, -15850), (79, -659000, 340000, -13000), (80, 82000, -68000, 1000),
            (81, 661000, -599000, -19000), (82, -196000, -498000, -7100), (84, -404000, -163000, -16000),
            (175, 418000, -99500, -52500), (87, -247000, 95000, 0), (88, -289000, 533000, 38500), (89, 794000, 772000, 71500),
            (83, -521000, 207000, -32500), (90, 549000, -341000, 24500), (91, -37000, -866000, 610),
            (171, 364000, -23000, -32400), (94, -58000, 216000, -5368), (95, 83000, 567000, -25000),
            (96, 126000, 606000, -21200), (97, 734000, -10000, -33630), (98, 476000, -437000, -8000),
            (164, -303000, 127500, -6118), (136, -144000, 234000, -80000), (138, 372000, -25000, -12000),
            (257, -50000, -120000, 0),
        };

        /// <summary>The map of the guides in multi-floor zones whose floor names one (number to map number).</summary>
        private static readonly Dictionary<int, int> Floors = new()
        {
            [4] = 2, [8] = 16, [9] = 1, [13] = 1, [14] = 1, [15] = 1, [19] = 15, [22] = 2, [23] = 1, [26] = 1, [27] = 15,
            [29] = 1, [30] = 1, [31] = 1, [34] = 15, [38] = 1, [39] = 1, [43] = 1, [46] = 1, [55] = 1, [56] = 1, [58] = 1,
            [62] = 1, [63] = 4, [68] = 1, [69] = 1, [71] = 1, [72] = 2, [73] = 1, [75] = 1, [76] = 2, [81] = 1, [88] = 1,
            [89] = 1, [91] = 1, [94] = 1, [96] = 1,
        };

        /// <summary>How many survival guides the table holds.</summary>
        public static int Count => Points.Length;

        /// <summary>Survival guide <paramref name="number"/>'s zone and x / y / z in yalms (y internal: smaller is higher).</summary>
        public static bool TryGet(int number, out int zoneId, out float x, out float y, out float z)
        {
            if (number < 0 || number >= Points.Length)
            {
                zoneId = 0;
                x = y = z = 0;
                return false;
            }
            var p = Points[number];
            (zoneId, x, y, z) = (p.Zone, p.X * 0.001f, p.Y * 0.001f, p.Z * 0.001f);
            return true;
        }

        /// <summary>The map survival guide <paramref name="number"/> is on (<see cref="ZoneMapFiles.MapsOf"/>), or -1 for none.</summary>
        public static int MapOf(int number) =>
            number < 0 || number >= Points.Length ? -1 : ZoneMapFiles.FloorOf(Points[number].Zone, Floors.GetValueOrDefault(number));

        /// <summary>The survival guides of zone <paramref name="zoneId"/> with the map each is on.</summary>
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
