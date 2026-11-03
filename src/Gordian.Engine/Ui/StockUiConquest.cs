// src/Gordian.Engine/Ui/StockUiConquest.cs
using System.Collections.Generic;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Tables;
using Gordian.Core.World;

namespace Gordian.Engine.Ui
{
    /// <summary>One side's bar of the Regional Influence graph: who, and its share in percent.</summary>
    public readonly record struct StockUiInfluenceBar(string Side, int Percent);

    /// <summary>Which side of its point a conquest map label sits on.</summary>
    public enum StockUiMapLabelSide { Above, Right, Left, Below }

    /// <summary>What a conquest map marker stands for, which sets its look.</summary>
    public enum StockUiConquestMarkerKind
    {
        /// <summary>A conquest region: a grey ball, its name in white.</summary>
        Region,

        /// <summary>A city: its crest under its name in lilac (Jeuno, retail image-51).</summary>
        City,

        /// <summary>A zone of the shown region: a grey ball, its short name in white.</summary>
        Zone,

        /// <summary>The zone the character is in: as <see cref="Zone"/>, its name in cream (Bibiki Bay, retail image-49).</summary>
        CurrentZone,
    }

    /// <summary>
    /// A labelled point on the conquest map, at (<paramref name="X"/>, <paramref name="Y"/>) in pixels of its 512 x 512 image;
    /// <paramref name="Region"/> is the client region it stands for or lies in.
    /// </summary>
    public readonly record struct StockUiConquestMarker(StockUiConquestMarkerKind Kind, string Label, float X, float Y, StockUiMapLabelSide Side, int Region);

    /// <summary>
    /// What the Region Info window's Conquest page shows (#488), read from S2C 0x05E (<see cref="ProgressionState"/>) and the
    /// zone the character is in:
    /// <list type="bullet">
    /// <item>In a conquest region (<see cref="ZoneRegions.ConquestRecordOf"/>): <c>conquer1</c>'s Regional Control (the
    /// region's owner from its 0x05E record: the nation, or the menu table's "This region is under the control of the
    /// beastmen." / "This region is not under any country's control.", ROM/165/72 #3, #2), Next tally in (days) and
    /// Regional Influence (San d'Oria, Bastok, Windurst and the beastmen's shares of the region).</item>
    /// <item>Elsewhere: <c>conq6sta</c>'s Balance of Power, the three nations by their rank in regions held (the 0x05E
    /// balance byte: two bits per nation, San d'Oria first, 1 = first place), and Regional Control saying "This area is
    /// not under any country's control." (#4).</item>
    /// <item>Under either, <c>conq3pts</c>'s Conquest Points (the 0x05E value: the home nation's conquest points).</item>
    /// </list>
    /// Balance and owner encodings referenced from LandSandBoat (https://github.com/LandSandBoat/server,
    /// <c>src/map/conquest_system.cpp</c> <c>GetBalance</c>, <c>src/map/packets/s2c/0x05e_conquest.cpp</c>).
    /// The maintainer's retail captures (round 2 of #488, image-49 to image-51) show the windows where they are drawn and
    /// the Balance of Power as "1. San d'Oria"; the Regional Control and Influence texts are still our reading of the
    /// frames' baked-in headings (retail writes the influence as words such as "Minimal" for a region the cursor is on).
    /// </summary>
    public sealed record StockUiConquestView(
        int ConquestRecord,
        string RegionName,
        string Control,
        int NextTally,
        IReadOnlyList<StockUiInfluenceBar> Influence,
        IReadOnlyList<string> Balance,
        uint ConquestPoints)
    {
        public bool InConquestRegion => ConquestRecord >= 0;

        /// <summary>
        /// The owner as the window's Regional Control writes it: a nation, "Beastmen" or "Neutral" (retail image-55: the
        /// window says "Neutral" while the help bar carries <see cref="Control"/>'s sentence).
        /// </summary>
        public string Owner { get; init; } = StockUiConquest.NeutralOwner;

        /// <summary>The client region the page shows (the one under the map's cursor; -1 between regions).</summary>
        public int Region { get; init; } = -1;

        /// <summary>
        /// Whether <see cref="Influence"/> holds the region's shares: S2C 0x05E carries them for the character's own region
        /// only (the others' influence is a level per nation, which retail writes as words such as "Minimal").
        /// </summary>
        public bool HasInfluence { get; init; } = true;

        /// <summary>The labels on the map (<see cref="StockUiConquest.Markers"/>).</summary>
        public IReadOnlyList<StockUiConquestMarker> Markers { get; init; } = System.Array.Empty<StockUiConquestMarker>();
    }

    /// <summary>The Region Info list (<c>region</c>) and the Conquest page's windows (see <see cref="StockUiConquestView"/>).</summary>
    public static class StockUiConquest
    {
        /// <summary>
        /// <c>region</c> (384,48, 112 x 74, title 1 "Region Info"): Conquest, Besieged, Campaign, Colonization (label sprites;
        /// all four buttons carry help 331 "View the balance of power for each region.").
        /// </summary>
        public const string RegionMenu = "region";
        public const int ConquestButton = 1, BesiegedButton = 2, CampaignButton = 3, ColonizationButton = 4;

        /// <summary>The Conquest page's frames: Regional Control / Next tally in / Regional Influence, Balance of Power, Conquest Points.</summary>
        public const string ControlWindow = "conquer1", BalanceWindow = "conq6sta", PointsWindow = "conq3pts";

        /// <summary>The help bar's title over the conquest map (retail image-49).</summary>
        public const string Title = "Conquest";

        /// <summary>The help the <c>region</c> buttons carry (ROM/165/75 #331).</summary>
        public const int HelpRegionInfo = 331;

        /// <summary>The menu message table's (ROM/165/72) control texts.</summary>
        public const int NeutralRegionText = 2, BeastmenRegionText = 3, NeutralAreaText = 4;

        public const string NeutralRegionFallback = "This region is not under any country's control.";
        public const string BeastmenRegionFallback = "This region is under the control of the beastmen.";
        public const string NeutralAreaFallback = "This area is not under any country's control.";

        /// <summary>The window's short owners (retail image-55 "Neutral"; "Beastmen" as the influence rows name them).</summary>
        public const string NeutralOwner = "Neutral", BeastmenOwner = "Beastmen";

        /// <summary>The nations as the conquest packet numbers them (owner 1-3 = nation + 1).</summary>
        public static readonly IReadOnlyList<string> Nations = new[] { "San d'Oria", "Bastok", "Windurst" };

        /// <summary>Text rows inside the frames (layout px from each frame's top-left), under the baked-in headings.</summary>
        public const float ValueX = 10, ControlY = 22, TallyY = 53, InfluenceY = 84, InfluencePitch = 28, BalanceY = 22, BalancePitch = 15, PointsY = 20;

        /// <summary>
        /// The conquest map's size: this many times the size at which it covers the screen. Measured on the maintainer's
        /// retail captures of the conquest map (round 2 of #488, image-49 to image-51, 2550-2559 px wide): matching the map
        /// image against each capture gives 11.5 screen px per map px in all three, where covering is 4.99.
        /// </summary>
        public const float MapZoom = 2.3f;

        /// <summary>
        /// Where the regions and the cities sit on the conquest map (map px of ROM/17/23; client region numbers: 0-3 the
        /// cities, 4-24 the regions), and which side their name is on. Measured on the maintainer's retail captures (round 2
        /// of #488: image-49 to image-51; round 3: image-55 to image-73, one with the cursor on each region): each capture
        /// was registered against the map image (scale 11.5 in every one, correlation 0.75-0.99), the grey balls found by
        /// their colour and shape and converted to map px; a ball seen in several captures lands within half a map pixel of
        /// itself. The values are rounded to the half pixel (most balls measure x.3-x.6). The cities' points are their crests'
        /// centres, read off the captures (Windurst's, under its name, is the least sure).
        /// </summary>
        private static readonly Dictionary<int, (float X, float Y, StockUiMapLabelSide Side)> RegionPoints = new()
        {
            [0] = (176.5f, 162f, StockUiMapLabelSide.Above),     // San d'Oria
            [1] = (220.5f, 313.5f, StockUiMapLabelSide.Above),   // Bastok
            [2] = (347.5f, 329.5f, StockUiMapLabelSide.Below),   // Windurst
            [3] = (286.5f, 202.5f, StockUiMapLabelSide.Above),   // Jeuno
            [4] = (176.5f, 182.5f, StockUiMapLabelSide.Right),   // Ronfaure
            [5] = (182.5f, 238.5f, StockUiMapLabelSide.Above),   // Zulkheim
            [6] = (240.5f, 154.5f, StockUiMapLabelSide.Above),   // Norvallen
            [7] = (168.5f, 318.5f, StockUiMapLabelSide.Above),   // Gustaberg
            [8] = (252.5f, 233.5f, StockUiMapLabelSide.Above),   // Derfland
            [9] = (353.5f, 296.5f, StockUiMapLabelSide.Above),   // Sarutabaruta
            [10] = (376.5f, 262.5f, StockUiMapLabelSide.Above),  // Kolshushu
            [11] = (359.5f, 220.5f, StockUiMapLabelSide.Above),  // Aragoneu
            [12] = (253.5f, 131.5f, StockUiMapLabelSide.Above),  // Fauregandi
            [13] = (162.5f, 115.5f, StockUiMapLabelSide.Above),  // Valdeaunia
            [14] = (312.5f, 162.5f, StockUiMapLabelSide.Above),  // Qufim
            [15] = (417.5f, 212.5f, StockUiMapLabelSide.Above),  // Li'Telor
            [16] = (85.5f, 355.5f, StockUiMapLabelSide.Above),   // Kuzotz
            [17] = (94.5f, 261.5f, StockUiMapLabelSide.Above),   // Vollbow
            [18] = (399.5f, 393.5f, StockUiMapLabelSide.Above),  // Elshimo Lowlands
            [19] = (442.5f, 393.5f, StockUiMapLabelSide.Above),  // Elshimo Uplands
            [20] = (334.5f, 131.5f, StockUiMapLabelSide.Above),  // Tu'Lia
            [22] = (244.5f, 294.5f, StockUiMapLabelSide.Right),  // Movalpolos
            [24] = (67.5f, 233.5f, StockUiMapLabelSide.Right),   // Tavnazian Archipelago
        };

        /// <summary>
        /// Where the zones sit on the conquest map (map px; zone ids), measured as <see cref="RegionPoints"/>, with the region
        /// whose cursor shows them. Retail labels the zones of the region the cursor is on with their short names
        /// (ROM/165/83), and its lists are LandSandBoat's region of each zone (<see cref="ZoneRegions"/>) plus the battlefield
        /// and Cloister zones LandSandBoat leaves without a region (Bearclaw Pinnacle, Boneyard Gully, The Shrouded Maw, Mine
        /// Shaft #2716, Monarch Linn, the Cloisters of Gales, Flames, Tremors and Tides), so the region is written here.
        /// </summary>
        private static readonly (int Zone, int Region, float X, float Y, StockUiMapLabelSide Side)[] ZonePoints =
        {
            (140, 4, 144.5f, 138f, StockUiMapLabelSide.Left), (139, 4, 154.5f, 138f, StockUiMapLabelSide.Right),
            (142, 4, 144.5f, 146f, StockUiMapLabelSide.Left), (141, 4, 154.5f, 146f, StockUiMapLabelSide.Right),
            (100, 4, 161.5f, 162f, StockUiMapLabelSide.Left), (101, 4, 190.5f, 162f, StockUiMapLabelSide.Right),
            (167, 4, 171.5f, 172f, StockUiMapLabelSide.Right), (190, 4, 181.5f, 193f, StockUiMapLabelSide.Below),
            (102, 5, 182.5f, 216f, StockUiMapLabelSide.Above), (103, 5, 149.5f, 225f, StockUiMapLabelSide.Left),
            (193, 5, 192.5f, 245f, StockUiMapLabelSide.Right), (248, 5, 147.5f, 246f, StockUiMapLabelSide.Above),
            (108, 5, 182.5f, 255f, StockUiMapLabelSide.Below), (196, 5, 195.5f, 272f, StockUiMapLabelSide.Right),
            (1, 6, 219.5f, 142.5f, StockUiMapLabelSide.Above), (2, 6, 217.5f, 154.5f, StockUiMapLabelSide.Below),
            (104, 6, 232.5f, 176.5f, StockUiMapLabelSide.Above), (105, 6, 261.5f, 174.5f, StockUiMapLabelSide.Right),
            (195, 6, 261.5f, 184.5f, StockUiMapLabelSide.Right), (149, 6, 215.5f, 192.5f, StockUiMapLabelSide.Left),
            (150, 6, 215.5f, 202.5f, StockUiMapLabelSide.Left),
            (144, 7, 242.5f, 276.5f, StockUiMapLabelSide.Left), (143, 7, 237.5f, 284.5f, StockUiMapLabelSide.Left),
            (106, 7, 161.5f, 292.5f, StockUiMapLabelSide.Left), (172, 7, 213.5f, 324.5f, StockUiMapLabelSide.Below),
            (191, 7, 178.5f, 331.5f, StockUiMapLabelSide.Left), (107, 7, 194.5f, 344.5f, StockUiMapLabelSide.Below),
            (173, 7, 156.5f, 362.5f, StockUiMapLabelSide.Right),
            (110, 8, 264.5f, 216.5f, StockUiMapLabelSide.Left), (197, 8, 273.5f, 216.5f, StockUiMapLabelSide.Right),
            (147, 8, 235.5f, 241.5f, StockUiMapLabelSide.Right), (109, 8, 227.5f, 243.5f, StockUiMapLabelSide.Left),
            (148, 8, 235.5f, 250.5f, StockUiMapLabelSide.Right),
            (145, 9, 323.5f, 293.5f, StockUiMapLabelSide.Left), (194, 9, 366.5f, 300.5f, StockUiMapLabelSide.Right),
            (146, 9, 323.5f, 301.5f, StockUiMapLabelSide.Left), (115, 9, 322.5f, 309.5f, StockUiMapLabelSide.Left),
            (116, 9, 374.5f, 309.5f, StockUiMapLabelSide.Right), (170, 9, 348.5f, 313.5f, StockUiMapLabelSide.Below),
            (192, 9, 378.5f, 323.5f, StockUiMapLabelSide.Right), (169, 9, 359.5f, 332.5f, StockUiMapLabelSide.Right),
            (198, 10, 379.5f, 247.5f, StockUiMapLabelSide.Above), (118, 10, 412.5f, 269.5f, StockUiMapLabelSide.Above),
            (117, 10, 373.5f, 273.5f, StockUiMapLabelSide.Left), (213, 10, 414.5f, 279.5f, StockUiMapLabelSide.Left),
            (249, 10, 408.5f, 291.5f, StockUiMapLabelSide.Right), (4, 10, 387.5f, 300.5f, StockUiMapLabelSide.Right),
            (3, 10, 395.5f, 311.5f, StockUiMapLabelSide.Right),
            (120, 11, 315.5f, 202.5f, StockUiMapLabelSide.Right), (200, 11, 315.5f, 219.5f, StockUiMapLabelSide.Below),
            (151, 11, 382.5f, 225.5f, StockUiMapLabelSide.Right), (152, 11, 382.5f, 232.5f, StockUiMapLabelSide.Right),
            (119, 11, 354.5f, 234.5f, StockUiMapLabelSide.Below), (8, 11, 384.5f, 239.5f, StockUiMapLabelSide.Right),
            (7, 11, 381.5f, 246.5f, StockUiMapLabelSide.Right),
            (203, 12, 270.5f, 99.5f, StockUiMapLabelSide.Right), (111, 12, 238.5f, 106.5f, StockUiMapLabelSide.Above),
            (204, 12, 254.5f, 106.5f, StockUiMapLabelSide.Right), (9, 12, 222.5f, 109.5f, StockUiMapLabelSide.Left),
            (206, 12, 254.5f, 114.5f, StockUiMapLabelSide.Right), (10, 12, 216.5f, 116.5f, StockUiMapLabelSide.Below),
            (166, 12, 214.5f, 131.5f, StockUiMapLabelSide.Below),
            (6, 13, 175.5f, 79f, StockUiMapLabelSide.Above), (165, 13, 145.5f, 86.5f, StockUiMapLabelSide.Left),
            (5, 13, 175.5f, 86.5f, StockUiMapLabelSide.Right), (162, 13, 144.5f, 94.5f, StockUiMapLabelSide.Left),
            (161, 13, 153.5f, 94.5f, StockUiMapLabelSide.Right), (112, 13, 183.5f, 102.5f, StockUiMapLabelSide.Right),
            (127, 14, 311.5f, 145.5f, StockUiMapLabelSide.Right), (126, 14, 301.5f, 170.5f, StockUiMapLabelSide.Left),
            (179, 14, 315.5f, 170.5f, StockUiMapLabelSide.Right), (158, 14, 315.5f, 178.5f, StockUiMapLabelSide.Right),
            (157, 14, 315.5f, 186.5f, StockUiMapLabelSide.Right), (184, 14, 315.5f, 194.5f, StockUiMapLabelSide.Right),
            (251, 15, 383.5f, 188.5f, StockUiMapLabelSide.Above), (122, 15, 391.5f, 204.5f, StockUiMapLabelSide.Above),
            (121, 15, 395.5f, 215.5f, StockUiMapLabelSide.Below), (154, 15, 427.5f, 227.5f, StockUiMapLabelSide.Right),
            (153, 15, 441.5f, 236.5f, StockUiMapLabelSide.Left), (202, 15, 450.5f, 244.5f, StockUiMapLabelSide.Left),
            (247, 16, 109.5f, 334.5f, StockUiMapLabelSide.Above), (125, 16, 61.5f, 344.5f, StockUiMapLabelSide.Above),
            (114, 16, 104.5f, 372.5f, StockUiMapLabelSide.Right), (168, 16, 69.5f, 379.5f, StockUiMapLabelSide.Below),
            (209, 16, 107.5f, 383.5f, StockUiMapLabelSide.Right), (208, 16, 100.5f, 391.5f, StockUiMapLabelSide.Right),
            (212, 17, 110.5f, 239.5f, StockUiMapLabelSide.Right), (201, 17, 90.5f, 245.5f, StockUiMapLabelSide.Left),
            (113, 17, 91.5f, 270.5f, StockUiMapLabelSide.Left), (128, 17, 101.5f, 278.5f, StockUiMapLabelSide.Right),
            (174, 17, 83.5f, 301.5f, StockUiMapLabelSide.Right),
            (250, 18, 418.5f, 371.5f, StockUiMapLabelSide.Above), (123, 18, 413.5f, 398.5f, StockUiMapLabelSide.Below),
            (252, 18, 376.5f, 407.5f, StockUiMapLabelSide.Left), (176, 18, 389.5f, 413.5f, StockUiMapLabelSide.Below),
            (205, 19, 441.5f, 368.5f, StockUiMapLabelSide.Above), (207, 19, 453f, 371.5f, StockUiMapLabelSide.Right),
            (124, 19, 454f, 393.5f, StockUiMapLabelSide.Right), (163, 19, 442f, 403.5f, StockUiMapLabelSide.Left),
            (159, 19, 451f, 404.5f, StockUiMapLabelSide.Right), (160, 19, 451f, 411.5f, StockUiMapLabelSide.Left),
            (211, 19, 457f, 421.5f, StockUiMapLabelSide.Below),
            (181, 20, 334.5f, 101.5f, StockUiMapLabelSide.Right), (180, 20, 334.5f, 108.5f, StockUiMapLabelSide.Right),
            (178, 20, 334.5f, 115.5f, StockUiMapLabelSide.Right), (177, 20, 334.5f, 140.5f, StockUiMapLabelSide.Right),
            (130, 20, 334.5f, 147.5f, StockUiMapLabelSide.Right),
            (13, 22, 262.5f, 270.5f, StockUiMapLabelSide.Right), (12, 22, 256.5f, 278.5f, StockUiMapLabelSide.Right),
            (11, 22, 250.5f, 286.5f, StockUiMapLabelSide.Right),
            (29, 24, 39.5f, 188.5f, StockUiMapLabelSide.Right), (31, 24, 32.5f, 196.5f, StockUiMapLabelSide.Right),
            (30, 24, 39.5f, 204.5f, StockUiMapLabelSide.Right), (28, 24, 47.5f, 218.5f, StockUiMapLabelSide.Left),
            (27, 24, 47.5f, 225.5f, StockUiMapLabelSide.Right), (25, 24, 67.5f, 241.5f, StockUiMapLabelSide.Left),
            (24, 24, 74.5f, 241.5f, StockUiMapLabelSide.Right), (32, 24, 62.5f, 258.5f, StockUiMapLabelSide.Below),
        };

        /// <summary>
        /// The cursor's speed (map px per second, either axis): the movement video (round 3 of #488) moves it about 800
        /// screen px a second at 11.5 px per map px. PROVISIONAL: read off a keyboard recording; the stick may differ.
        /// </summary>
        public const float CursorSpeed = 70;

        /// <summary>
        /// How close (map px) the cursor must come to a region's ball to select it. PROVISIONAL: the captures show the cursor's
        /// box up to about 1.6 map px off the ball it selects (image-58, image-60).
        /// </summary>
        public const float SelectRadius = 3;

        /// <summary>
        /// The share of the screen the cursor keeps inside while the map can still scroll (left, top, right, bottom, of the
        /// screen's width and height): past it the map scrolls instead (the video: the box stops at x 198 and 2258 and y 208
        /// and 860 of 2552 x 1434). Once the map is at its edge the cursor goes on toward the screen's edge (round 3).
        /// </summary>
        public const float CursorLeft = 0.078f, CursorTop = 0.145f, CursorRight = 0.885f, CursorBottom = 0.6f;

        /// <summary>
        /// The conquest region whose ball is nearest the map point (<paramref name="x"/>, <paramref name="y"/>) within
        /// <see cref="SelectRadius"/>, or -1 (between regions, or on a city: the Balance of Power, retail image-51).
        /// </summary>
        public static int RegionAt(float x, float y)
        {
            int best = -1;
            float bestDistance = SelectRadius * SelectRadius;
            foreach (var (id, p) in RegionPoints)
            {
                if (ZoneRegions.ConquestRecordOf(id) < 0) continue;
                float dx = p.X - x, dy = p.Y - y, d = dx * dx + dy * dy;
                if (d <= bestDistance) { best = id; bestDistance = d; }
            }
            return best;
        }

        /// <summary>The conquest map's image size (px).</summary>
        public const float MapSize = 512;

        /// <summary>The map point (0-1 across and down) of a region's marker, or null for one not placed yet.</summary>
        public static (float U, float V)? CenterOf(int region) =>
            RegionPoints.TryGetValue(region, out var p) ? (p.X / MapSize, p.Y / MapSize) : null;

        /// <summary>
        /// The labels on the conquest map: every region and city, and the zones of <paramref name="region"/> (the one the
        /// cursor is on; the character's <paramref name="zone"/> marked). Names from <paramref name="regionNames"/>
        /// (ROM/165/78) and <paramref name="zoneNames"/> (the short names, ROM/165/83); a point without a name is left out.
        /// </summary>
        public static IReadOnlyList<StockUiConquestMarker> Markers(int region, int zone, System.Func<int, string?>? regionNames, System.Func<int, string?>? zoneNames)
        {
            var markers = new List<StockUiConquestMarker>();
            foreach (var (id, p) in RegionPoints)
            {
                if (regionNames?.Invoke(id) is not { Length: > 0 } name) continue;
                markers.Add(new StockUiConquestMarker(id <= 3 ? StockUiConquestMarkerKind.City : StockUiConquestMarkerKind.Region, name, p.X, p.Y, p.Side, id));
            }
            if (region < 0) return markers;
            foreach (var p in ZonePoints)
            {
                if (p.Region != region || zoneNames?.Invoke(p.Zone) is not { Length: > 0 } name) continue;
                markers.Add(new StockUiConquestMarker(p.Zone == zone ? StockUiConquestMarkerKind.CurrentZone : StockUiConquestMarkerKind.Zone, name, p.X, p.Y, p.Side, region));
            }
            return markers;
        }

        /// <summary>The values' size against the log font's: the frames are 112 px wide (PROVISIONAL).</summary>
        public const float ValueScale = 0.75f;

        /// <summary>
        /// The page for <paramref name="region"/> (client region numbering, -1 between regions), with the menu texts from
        /// <paramref name="menuText"/> (null for the fallbacks). <paramref name="characterRegion"/> is the region the
        /// character is in (by default <paramref name="region"/>): only its influence shares are known.
        /// </summary>
        public static StockUiConquestView Build(ProgressionState progression, int region, string regionName, System.Func<int, string?>? menuText = null,
            int zone = -1, System.Func<int, string?>? regionNames = null, System.Func<int, string?>? zoneNames = null, int? characterRegion = null)
        {
            string Text(int id, string fallback) => menuText?.Invoke(id) is { Length: > 0 } t ? t : fallback;
            int record = ZoneRegions.ConquestRecordOf(region);
            var influence = progression.ConquestCurrentRegion;
            string control, ownerName = NeutralOwner;
            if (record < 0) control = Text(NeutralAreaText, NeutralAreaFallback);
            else
            {
                byte owner = progression.GetRegionConquest(record).Owner;
                control = owner switch
                {
                    >= 1 and <= 3 => Nations[owner - 1],
                    4 => Text(BeastmenRegionText, BeastmenRegionFallback),
                    _ => Text(NeutralRegionText, NeutralRegionFallback),
                };
                ownerName = owner switch { >= 1 and <= 3 => Nations[owner - 1], 4 => BeastmenOwner, _ => NeutralOwner };
            }
            var bars = new[]
            {
                new StockUiInfluenceBar(Nations[0], influence.Sandoria),
                new StockUiInfluenceBar(Nations[1], influence.Bastok),
                new StockUiInfluenceBar(Nations[2], influence.Windurst),
                new StockUiInfluenceBar("Beastmen", influence.Beastmen),
            };
            return new StockUiConquestView(record, regionName, control, progression.ConquestNextTally, bars,
                BalanceLines(progression.ConquestBalance), progression.ConquestPoints)
            {
                Region = region,
                Owner = ownerName,
                HasInfluence = region == (characterRegion ?? region),
                Markers = Markers(region, zone, regionNames, zoneNames),
            };
        }

        /// <summary>
        /// The nations by rank from the 0x05E balance byte ("1. San d'Oria"); ties share a rank, in nation order (retail
        /// image-51, round 2 of #488: "1. San d'Oria", "1. Bastok", "1. Windurst" for a three-way tie).
        /// </summary>
        public static IReadOnlyList<string> BalanceLines(byte balance)
        {
            var lines = new List<string>(3);
            for (int rank = 1; rank <= 3; rank++)
            {
                for (int nation = 0; nation < 3; nation++)
                {
                    if (((balance >> (nation * 2)) & 3) == rank) lines.Add($"{rank}. {Nations[nation]}");
                }
            }
            return lines;
        }

        /// <summary>"137 days." (retail image-55), "1 day." (PROVISIONAL singular).</summary>
        public static string TallyText(int days) => days == 1 ? "1 day." : $"{days} days.";
    }
}
