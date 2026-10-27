// src/Gordian.Core/Resources/Tables/ZoneRegions.cs
using System.Collections.Generic;
using System.Linq;

namespace Gordian.Core.Resources.Tables
{
    /// <summary>
    /// The region each zone with a map belongs to, numbered as the client's region names list them (ROM/165/78.DAT,
    /// <c>DMsgCategory.RegionNames</c>: 0 San d'Oria, 1 Bastok, 2 Windurst, 3 Jeuno, 4 Ronfaure ... 28 Empire of Aht
    /// Urhgan ... 49 The Sacred City of Adoulin, 50 The Adoulin Islands, 51 East Ulbuka Territory, 52 Ra'Kaznar, 53 Escha):
    /// the Map window's Change Map list groups the maps by it (#23).
    /// Zone-to-region assignments referenced from LandSandBoat (https://github.com/LandSandBoat/server,
    /// <c>src/map/utils/zoneutils.cpp</c> <c>GetCurrentRegion</c>; generated 2026-10-25 and renumbered from its
    /// <c>REGION_TYPE</c> to the client's order). **Beyond LandSandBoat:** Al Zahbi and Aht Urhgan Whitegate are put in
    /// "Empire of Aht Urhgan" (28) and the two Adoulin cities in "The Sacred City of Adoulin" (49), which LandSandBoat
    /// folds into West Aht Urhgan and the Adoulin Islands; Yorcia Weald, Marjami Ravine and Kamihr Drifts (Adoulin Islands),
    /// Dho and Woh Gates (East Ulbuka), Outer Ra'Kaznar and its Inner Court (Ra'Kaznar) and Escha / Reisenjima (Escha), which
    /// LandSandBoat leaves without a region, are placed by name. PROVISIONAL until checked against retail's list. The
    /// ships, airships, the Chocobo Circuit and Leafallia have no region here: their maps open from "Current Area" only, as do
    /// Heaven's Tower's (retail counts Windurst's zones as 4 in the list, round 1 image-6: Windurst Waters, Walls, Port and
    /// Woods).
    /// </summary>
    public static class ZoneRegions
    {
        private static readonly Dictionary<int, int> RegionByZone = new()
        {
            [1] = 6, [2] = 6, [3] = 10, [4] = 10, [5] = 13, [7] = 11, [9] = 12, [11] = 22, [12] = 22, [14] = 25, [15] = 46,
            [16] = 25, [18] = 25, [20] = 25, [22] = 25, [24] = 24, [25] = 24, [26] = 23, [27] = 24, [28] = 24, [29] = 24,
            [30] = 24, [32] = 24, [33] = 26, [34] = 26, [35] = 26, [39] = 21, [40] = 21, [41] = 21, [42] = 21, [43] = 48,
            [44] = 48, [45] = 46, [48] = 28, [50] = 28, [51] = 30, [52] = 29, [53] = 32, [54] = 32, [55] = 32, [56] = 32,
            [61] = 31, [62] = 31, [63] = 31, [65] = 30, [66] = 30, [68] = 30, [69] = 32, [71] = 29, [72] = 33, [73] = 33,
            [74] = 33, [75] = 33, [76] = 33, [77] = 33, [79] = 32, [80] = 34, [81] = 34, [82] = 35, [83] = 37, [84] = 35,
            [85] = 35, [86] = 35, [87] = 36, [88] = 36, [89] = 36, [90] = 37, [91] = 37, [92] = 37, [93] = 36, [94] = 38,
            [95] = 38, [96] = 38, [97] = 39, [98] = 39, [99] = 39, [100] = 4, [101] = 4, [102] = 5, [103] = 5, [104] = 6,
            [105] = 6, [106] = 7, [107] = 7, [108] = 5, [109] = 8, [110] = 8, [111] = 12, [112] = 13, [113] = 17, [114] = 16,
            [115] = 9, [116] = 9, [117] = 10, [118] = 10, [119] = 11, [120] = 11, [121] = 15, [122] = 15, [123] = 18, [124] = 19,
            [125] = 16, [126] = 14, [127] = 14, [128] = 17, [129] = 38, [130] = 20, [132] = 46, [134] = 21, [135] = 21,
            [136] = 40, [137] = 41, [138] = 41, [139] = 4, [140] = 4, [141] = 4, [142] = 4, [143] = 7, [144] = 7, [145] = 9,
            [146] = 9, [147] = 8, [148] = 8, [149] = 6, [150] = 6, [151] = 11, [152] = 11, [153] = 15, [154] = 15, [155] = 41,
            [156] = 41, [157] = 14, [158] = 14, [159] = 19, [160] = 19, [161] = 13, [162] = 13, [163] = 19, [164] = 39,
            [165] = 13, [166] = 12, [167] = 4, [168] = 16, [169] = 9, [170] = 9, [171] = 37, [172] = 7, [173] = 7, [174] = 17,
            [175] = 35, [176] = 18, [177] = 20, [178] = 20, [179] = 14, [180] = 20, [181] = 20, [182] = 47, [184] = 14,
            [185] = 21, [186] = 21, [187] = 21, [188] = 21, [190] = 4, [191] = 7, [192] = 9, [193] = 5, [194] = 9, [195] = 6,
            [196] = 5, [197] = 8, [198] = 10, [200] = 11, [202] = 15, [203] = 12, [204] = 12, [205] = 19, [206] = 12, [208] = 16,
            [212] = 17, [213] = 10, [215] = 46, [216] = 46, [217] = 46, [218] = 46, [230] = 0, [231] = 0, [232] = 0, [233] = 0,
            [234] = 1, [235] = 1, [236] = 1, [237] = 1, [238] = 2, [239] = 2, [240] = 2, [241] = 2, [243] = 3,
            [244] = 3, [245] = 3, [246] = 3, [247] = 16, [248] = 5, [249] = 10, [250] = 18, [251] = 15, [252] = 18, [253] = 46,
            [254] = 46, [256] = 49, [257] = 49, [258] = 50, [259] = 50, [260] = 51, [261] = 51, [262] = 51, [263] = 50,
            [264] = 50, [265] = 51, [266] = 50, [267] = 50, [268] = 51, [269] = 51, [270] = 51, [271] = 51, [272] = 51,
            [273] = 51, [274] = 52, [275] = 52, [276] = 52, [288] = 53, [289] = 53, [291] = 53,
        };

        /// <summary>The region a zone belongs to (client order), or -1.</summary>
        public static int RegionOf(int zone) => RegionByZone.TryGetValue(zone, out int region) ? region : -1;

        /// <summary>The zones of a region, by zone id.</summary>
        public static IReadOnlyList<int> ZonesIn(int region) => RegionByZone.Where(p => p.Value == region).Select(p => p.Key).OrderBy(z => z).ToList();

        /// <summary>The regions that have zones, in the client's order.</summary>
        public static IReadOnlyList<int> Regions => RegionByZone.Values.Distinct().OrderBy(r => r).ToList();
    }
}
