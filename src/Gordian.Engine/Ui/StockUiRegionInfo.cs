// src/Gordian.Engine/Ui/StockUiRegionInfo.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Tables;
using Gordian.Core.World;

namespace Gordian.Engine.Ui
{
    /// <summary>A value a Region Info panel writes: at (X, Y) layout px from its frame's top-left, or ending at X when right-aligned.</summary>
    public readonly record struct StockUiRegionInfoText(float X, float Y, string Text, bool RightAligned = false);

    /// <summary>What colour a Region Info bar is filled with.</summary>
    public enum StockUiRegionInfoBarColor { Red, Blue }

    /// <summary>A bar a Region Info panel fills: from (X, Y), <see cref="Width"/> long, <see cref="Fill"/> 0-1 of it.</summary>
    public readonly record struct StockUiRegionInfoBar(float X, float Y, float Width, float Fill, StockUiRegionInfoBarColor Color = StockUiRegionInfoBarColor.Red);

    /// <summary>
    /// One of a Region Info page's DAT windows, with the values written on it. <see cref="Width"/> is the width retail draws it
    /// when it is not the frame's (0: as authored); <see cref="Icon"/> a crest texture drawn at the frame's top-left.
    /// </summary>
    public sealed record StockUiRegionInfoPanel(string Window, IReadOnlyList<StockUiRegionInfoText> Texts, IReadOnlyList<StockUiRegionInfoBar> Bars,
        float Width = 0, string? Icon = null);

    /// <summary>A marker's art on a Region Info map.</summary>
    public enum StockUiRegionMarkerLook
    {
        /// <summary>A grey ball, white name.</summary>
        Ball,

        /// <summary>A beastman area's hub: <c>aht0</c>, the black-gemmed disc, white name.</summary>
        Hub,

        /// <summary>The empire's hub (West Aht Urhgan): <c>aht1</c>, the teal-gemmed disc, white name.</summary>
        AlliedHub,

        /// <summary>A city's crest (<see cref="StockUiRegionMarker.Crest"/>), lilac name.</summary>
        City,

        /// <summary>A beastman stronghold: <c>mcre</c>, pink name.</summary>
        Stronghold,

        /// <summary>A Campaign front: its name in gold, no point.</summary>
        Front,

        /// <summary>A Campaign zone: a ball in its owner's colour (<see cref="StockUiRegionMarker.Owner"/>).</summary>
        CampaignZone,
    }

    /// <summary>
    /// A point on a Region Info map, in pixels of its 512 x 512 image. <see cref="Owner"/> is a Campaign zone's owner (S2C 0x071:
    /// 0 none, 1-3 the nations, 4 the beastmen), <see cref="Crest"/> a city's texture.
    /// </summary>
    public readonly record struct StockUiRegionMarker(StockUiRegionMarkerLook Look, string Label, float X, float Y, StockUiMapLabelSide Side, int Owner = 0, string? Crest = null);

    /// <summary>The figures in the box in the target window's place: Imperial Standing, Allied Notes and Bayld (S2C 0x113 / 0x118, 0x05E, 0x071).</summary>
    public readonly record struct StockUiRegionInfoFigures(long ImperialStanding, long AlliedNotes, long Bayld);

    /// <summary>
    /// What a Besieged, Campaign or Colonization page (#492) shows over its region map: the help bar's title and line, its
    /// panels (at the top right, the top left while the cursor is in the screen's right half), the markers and the box in the
    /// target window's place.
    /// </summary>
    public sealed record StockUiRegionInfoView(RegionMap Map, string Title, string? HelpLine, IReadOnlyList<StockUiRegionInfoPanel> Panels,
        IReadOnlyList<StockUiRegionMarker> Markers, string BoxWindow, string BoxValue, int Selected);

    /// <summary>
    /// Region Info's Besieged, Campaign and Colonization pages (#492), built on the Conquest map of #488: each opens its region
    /// map (<see cref="RegionMapFiles"/>) at the conquest map's size with the same cursor and the same turn of its panel to the
    /// left while the cursor is in the screen's right half. Laid out from the maintainer's real-retail captures of 2026-11-03
    /// (Besieged image to image-7, Colonization image-8 to image-16, Campaign image-17 to image-28, 2547-2558 px wide),
    /// each registered against its map image (correlation 0.72-0.97, 11.5 screen px per map px in all): every marker is
    /// converted to map px, and one seen in several captures lands within 0.3 map px of itself.
    /// <list type="bullet">
    /// <item><b>Besieged:</b> the hubs are regions 28-33 (ROM/165/78). <c>beseige1</c>, drawn 136 px wide against its 112-px
    /// frame, shows the orders and the three beastman forces; on a stronghold's hub (Mamool Ja Savagelands, Halvung Territory,
    /// Arrapago Islands) <c>beseige2</c> shows that base. Both from S2C 0x05E's Besieged block; box <c>imperial</c>.</item>
    /// <item><b>Campaign:</b> <c>campresu</c> between points; <c>campsan</c> on a city or a beastman stronghold, <c>camparea</c>
    /// on a zone's dot; from S2C 0x071 mode 2. The 26 dots are the packet's 26 zones, each named by the retail video of
    /// 2026-11-04. Box <c>allied</c>. Without that packet (LandSandBoat never sends it) the video shows the help bar's
    /// "Now obtaining information from server." (ROM/165/72 #6), every dot as a dark "?" ball with its name under the
    /// cursor, and no panel or box.</item>
    /// <item><b>Colonization:</b> <c>colorank</c> between points and on the Sacred City, <c>coloresu</c> on an Ulbuka zone's
    /// hub for that zone; from S2C 0x071 mode 3 (which LandSandBoat sends for C2S 0x05A). Box <c>colopoin</c>.</item>
    /// </list>
    /// Not done: the sword through a dot (a battle in the zone: no packet field is known for it), the bar under a selected dot.
    /// </summary>
    public static class StockUiRegionInfo
    {
        public const string OrdersWindow = "beseige1", BaseWindow = "beseige2";
        public const string CampaignWindow = "campresu", NationWindow = "campsan", ZoneWindow = "camparea";
        public const string RanksWindow = "colorank", RateWindow = "coloresu";
        public const string ImperialBox = "imperial", AlliedBox = "allied", BayldBox = "colopoin";

        public const string BesiegedTitle = "Besieged", CampaignTitle = "Campaign", ColonizationTitle = "Colonization";

        /// <summary>The menu message (ROM/165/72) "Now obtaining information from server.", and its fallback.</summary>
        public const int ObtainingText = 6;

        public const string ObtainingFallback = "Now obtaining information from server.";

        /// <summary>How close to a point (map px) the cursor selects it, as <see cref="StockUiConquest.SelectRadius"/>.</summary>
        public const float SelectRadius = StockUiConquest.SelectRadius;

        /// <summary>The Besieged panels' width in retail (image: 136 px against the frame's 112; the labels are not stretched).</summary>
        public const float BesiegedWidth = 136;

        // Layout px from the frames' top-left, measured on the captures (text rows are the text's top).
        private const float OrdersX = 9, OrdersY = 25, ForceY = 69, ForcePitch = 46, LevelX = 17, LevelValueX = 50, LevelDy = 14, OrderX = 16, OrderDy = 26;
        private const float BaseNameX = 20, BaseNameY = 25, BaseBallX = 9, ForcesBarX = 9, ForcesBarY = 69, ForcesBarWidth = 71, BaseRight = 128, ForcesValueY = 67;
        private const float BaseForceY = 85, BaseForceX = 9, MirrorsY = 163, PrisonersY = 179;
        private const float NationTextX = 32, NationLine1Y = 7, NationLine2Y = 21, ReconRight = 92, ReconY = 65, BarX = 32, BarWidth = 64, MoraleBarY = 97, ProsperityBarY = 129;
        private const float ZoneRight = 103, FortY = 19, ResourcesY = 47;
        private static readonly float[] InfluenceBarY = { 91, 115, 139, 163 };
        private const float ControlledRight = 104, RankRight = 98, RateRight = 98, RateY = 6, RateBarY = 21, BivouacsY = 36;

        /// <summary>The box's value: right-aligned 95 px in, on its second row.</summary>
        public const float BoxValueRight = 95, BoxValueY = 22;

        private static readonly float[] ControlledRows = { 22, 38, 54, 70 };
        private static readonly float[] RankRows = { 21, 37, 53, 69, 85, 101 };

        /// <summary>Al Zahbi's orders by S2C 0x05E's value (image: "Invade Enemy Base"; the others after LandSandBoat's <c>ORDERS_ALZAHBI_*</c>).</summary>
        public static readonly IReadOnlyList<string> AlZahbiOrders = new[] { "Defend Al Zahbi", "Intercept Enemy", "Invade Enemy Base" };

        /// <summary>A beastman stronghold's orders by value (image: "Training"; the others after LandSandBoat's <c>ORDERS_*</c>).</summary>
        public static readonly IReadOnlyList<string> StrongholdOrders = new[] { "Training", "Advancing", "Attacking", "Retreating", "Defending", "Preparing" };

        /// <summary>The beastman forces and their bases in S2C 0x05E's order (image-2, image-3, image-7).</summary>
        public static readonly IReadOnlyList<string> Forces = new[] { "Mamool Ja Savages", "Troll Mercenaries", "Undead Swarm" };

        /// <summary>The Campaign nations' panel lines, by S2C 0x071's nation word (image-18, -22, -23, -25, -26).</summary>
        private static readonly (string Side, string Name)[] Nations =
        {
            ("Allied Forces", "San d'Oria"), ("Allied Forces", "Bastok"), ("Allied Forces", "Windurst"),
            ("Beastmen", "Orcs"), ("Beastmen", "Quadav"), ("Beastmen", "Yagudo"), ("Beastmen", "Dark Kindred"),
        };

        /// <summary>The nations' crests by owner (1-3), the beastmen's after them.</summary>
        private static readonly string[] Crests = { "scre", "bcre", "wcre", "mcre" };

        /// <summary>A selectable point of a map: a client region (ROM/165/78), a zone (ROM/165/84) or a Campaign record.</summary>
        private readonly record struct Point(float X, float Y, StockUiMapLabelSide Side, StockUiRegionMarkerLook Look, int Region = -1, int Zone = -1,
            int Stronghold = -1, int Nation = -1, int Record = -1, int ColonyIndex = -1, string? Crest = null);

        /// <summary>A zone shown while its hub (a region or zone) is selected.</summary>
        private readonly record struct SubZone(int Hub, int Zone, float X, float Y, StockUiMapLabelSide Side);

        private const StockUiMapLabelSide Above = StockUiMapLabelSide.Above, Below = StockUiMapLabelSide.Below, Left = StockUiMapLabelSide.Left, Right = StockUiMapLabelSide.Right;

        private static readonly Point[] BesiegedPoints =
        {
            new(427.5f, 299.5f, Above, StockUiRegionMarkerLook.City, Region: 28, Crest: "acre"),
            new(376.5f, 306.5f, Above, StockUiRegionMarkerLook.AlliedHub, Region: 29),
            new(273.5f, 389f, Above, StockUiRegionMarkerLook.Hub, Region: 30, Stronghold: 0),
            new(288.5f, 285.5f, Above, StockUiRegionMarkerLook.Hub, Region: 31, Stronghold: 1),
            new(239.5f, 144.5f, Above, StockUiRegionMarkerLook.Hub, Region: 32, Stronghold: 2),
            new(259.5f, 225.5f, Above, StockUiRegionMarkerLook.Hub, Region: 33),
        };

        private static readonly SubZone[] BesiegedZones =
        {
            new(29, 48, 363.5f, 306.5f, Left), new(29, 50, 390.5f, 308.5f, Below), new(29, 52, 358.5f, 272.5f, Right),
            new(30, 65, 289.5f, 389f, Right), new(30, 67, 265.5f, 373f, Left), new(30, 66, 258.5f, 411f, Right), new(30, 68, 328.5f, 350.5f, Left),
            new(30, 51, 338.5f, 336.5f, Left),
            new(31, 62, 288.5f, 316.5f, Right), new(31, 63, 307.5f, 284.5f, Right), new(31, 61, 282.5f, 296.5f, Right), new(31, 64, 242.5f, 298.5f, Below),
            new(32, 54, 239.5f, 122.5f, Right), new(32, 55, 232.5f, 102.5f, Right), new(32, 79, 276.5f, 144.5f, Right), new(32, 56, 197.5f, 149.5f, Left),
            new(32, 57, 229.5f, 157.5f, Left), new(32, 78, 229.5f, 167.5f, Below), new(32, 69, 285.5f, 168.5f, Above), new(32, 53, 256.5f, 181.5f, Right),
            new(33, 77, 272.5f, 225f, Right), new(33, 72, 272.5f, 235f, Below), new(33, 75, 389.5f, 231f, Left), new(33, 74, 180.5f, 195f, Right),
            new(33, 76, 336.5f, 180f, Left), new(33, 73, 216.5f, 318.5f, Right),
        };

        private static readonly Point[] CampaignPoints =
        {
            new(176.5f, 162.5f, Above, StockUiRegionMarkerLook.City, Region: 0, Nation: 0, Crest: "scre"),
            new(220.5f, 313.5f, Above, StockUiRegionMarkerLook.City, Region: 1, Nation: 1, Crest: "bcre"),
            new(347.5f, 329.5f, Above, StockUiRegionMarkerLook.City, Region: 2, Nation: 2, Crest: "wcre"),
            new(286.5f, 202.5f, Above, StockUiRegionMarkerLook.City, Region: 3, Crest: "jcre"),
            new(203.5f, 208.5f, Above, StockUiRegionMarkerLook.Stronghold, Region: 42, Nation: 3),
            new(247.5f, 257.5f, Above, StockUiRegionMarkerLook.Stronghold, Region: 43, Nation: 4),
            new(399.5f, 230.5f, Above, StockUiRegionMarkerLook.Stronghold, Region: 44, Nation: 5),
            new(150.5f, 86.5f, Above, StockUiRegionMarkerLook.Stronghold, Region: 45, Nation: 6),
            // Each dot's zone and label side from the cursor stopping on it in the maintainer's retail video of 2026-11-04;
            // a zone's record from LandSandBoat's campaign_map.sql; the dots' places from every capture.
            new(161.5f, 166.5f, Left, StockUiRegionMarkerLook.CampaignZone, Zone: 80, Record: 0), new(191.5f, 166.5f, Below, StockUiRegionMarkerLook.CampaignZone, Zone: 81, Record: 1),
            new(225.5f, 184.5f, Below, StockUiRegionMarkerLook.CampaignZone, Zone: 82, Record: 2), new(251.5f, 202.5f, Below, StockUiRegionMarkerLook.CampaignZone, Zone: 83, Record: 3),
            new(249.5f, 162.5f, Above, StockUiRegionMarkerLook.CampaignZone, Zone: 84, Record: 4), new(217.5f, 210.5f, Right, StockUiRegionMarkerLook.CampaignZone, Zone: 85, Record: 5),
            new(255.5f, 184.5f, Right, StockUiRegionMarkerLook.CampaignZone, Zone: 175, Record: 6), new(205.5f, 317.5f, Below, StockUiRegionMarkerLook.CampaignZone, Zone: 87, Record: 7),
            new(163.5f, 292.5f, Above, StockUiRegionMarkerLook.CampaignZone, Zone: 88, Record: 8), new(201.5f, 266.5f, Above, StockUiRegionMarkerLook.CampaignZone, Zone: 89, Record: 9),
            new(211.5f, 240.5f, Above, StockUiRegionMarkerLook.CampaignZone, Zone: 90, Record: 10), new(263.5f, 220.5f, Above, StockUiRegionMarkerLook.CampaignZone, Zone: 91, Record: 11),
            new(233.5f, 256.5f, Left, StockUiRegionMarkerLook.CampaignZone, Zone: 92, Record: 12), new(255.5f, 236.5f, Right, StockUiRegionMarkerLook.CampaignZone, Zone: 171, Record: 13),
            new(333.5f, 334f, Below, StockUiRegionMarkerLook.CampaignZone, Zone: 94, Record: 14), new(323.5f, 306.5f, Above, StockUiRegionMarkerLook.CampaignZone, Zone: 95, Record: 15),
            new(343.5f, 274.5f, Below, StockUiRegionMarkerLook.CampaignZone, Zone: 96, Record: 16), new(355.5f, 233.5f, Below, StockUiRegionMarkerLook.CampaignZone, Zone: 97, Record: 17),
            new(319.5f, 204.5f, Above, StockUiRegionMarkerLook.CampaignZone, Zone: 98, Record: 18), new(385.5f, 233.5f, Below, StockUiRegionMarkerLook.CampaignZone, Zone: 99, Record: 19),
            new(319.5f, 222.5f, Below, StockUiRegionMarkerLook.CampaignZone, Zone: 164, Record: 20), new(233.5f, 108.5f, Right, StockUiRegionMarkerLook.CampaignZone, Zone: 136, Record: 21),
            new(191.5f, 108.5f, Below, StockUiRegionMarkerLook.CampaignZone, Zone: 137, Record: 22), new(152.5f, 100.5f, Right, StockUiRegionMarkerLook.CampaignZone, Zone: 138, Record: 23),
            new(138.5f, 100.5f, Left, StockUiRegionMarkerLook.CampaignZone, Zone: 155, Record: 24), new(138.5f, 90.5f, Left, StockUiRegionMarkerLook.CampaignZone, Zone: 156, Record: 25),
        };

        /// <summary>The Campaign fronts (regions 34-41): gold names, centred on their place.</summary>
        private static readonly (int Region, float X, float Y)[] Fronts =
        {
            (34, 176.5f, 189f), (35, 241.5f, 180f), (36, 214.5f, 292f), (37, 227.5f, 231f), (38, 362.5f, 297f), (39, 361.5f, 219f), (40, 238.5f, 133f), (41, 199.5f, 99f),
        };

        private static readonly Point[] ColonizationPoints =
        {
            new(386.5f, 158.5f, Above, StockUiRegionMarkerLook.City, Region: 49, Crest: "adcre"),
            new(309.5f, 107.5f, Above, StockUiRegionMarkerLook.Ball, Zone: 260, ColonyIndex: 0), new(289.5f, 139.5f, Above, StockUiRegionMarkerLook.Ball, Zone: 261, ColonyIndex: 1),
            new(237.5f, 255.5f, Above, StockUiRegionMarkerLook.Ball, Zone: 262, ColonyIndex: 2), new(204.5f, 139.5f, Above, StockUiRegionMarkerLook.Ball, Zone: 263, ColonyIndex: 3),
            new(199.5f, 63.5f, Above, StockUiRegionMarkerLook.Ball, Zone: 265, ColonyIndex: 4), new(129.5f, 191.5f, Above, StockUiRegionMarkerLook.Ball, Zone: 266, ColonyIndex: 5),
            new(136.5f, 71.5f, Above, StockUiRegionMarkerLook.Ball, Zone: 267, ColonyIndex: 6), new(65f, 79.5f, Above, StockUiRegionMarkerLook.Ball, Region: 52, ColonyIndex: 7),
        };

        /// <summary>Colonization's sub-zones; a hub is its region or zone number. Marjami Ravine's Woh Gates was out of its capture's view.</summary>
        private static readonly SubZone[] ColonizationZones =
        {
            new(49, 256, 376.5f, 166.5f, Left), new(49, 257, 396.5f, 166.5f, Right), new(49, 258, 386.5f, 169.5f, Below),
            new(261, 268, 244.5f, 191.5f, Below), new(263, 270, 229.5f, 158.5f, Below), new(262, 272, 179.5f, 221.5f, Below), new(265, 269, 239f, 97.5f, Below),
            new(267, 281, 106f, 67.5f, Below), new(52, 274, 51f, 97.5f, Right), new(52, 276, 44f, 113.5f, Right), new(52, 277, 37f, 129.5f, Right),
        };

        private static Point[] PointsOf(RegionMap map) => map switch
        {
            RegionMap.Besieged => BesiegedPoints,
            RegionMap.Campaign => CampaignPoints,
            _ => ColonizationPoints,
        };

        /// <summary>The point of <paramref name="map"/> within <see cref="SelectRadius"/> of map pixel (x, y), or -1.</summary>
        public static int PointAt(RegionMap map, float x, float y)
        {
            var points = PointsOf(map);
            int best = -1;
            float bestDistance = SelectRadius * SelectRadius;
            for (int i = 0; i < points.Length; i++)
            {
                float dx = points[i].X - x, dy = points[i].Y - y, d = dx * dx + dy * dy;
                if (d <= bestDistance) (best, bestDistance) = (i, d);
            }
            return best;
        }

        /// <summary>Where a page opens (0-1 across and down the map): on the character's hub when it is in one of the map's zones, else the map's first point.</summary>
        public static (float U, float V, int Point) OpeningPoint(RegionMap map, int zone)
        {
            var points = PointsOf(map);
            int hub = -1;
            foreach (var sub in map == RegionMap.Besieged ? BesiegedZones : map == RegionMap.Colonization ? ColonizationZones : Array.Empty<SubZone>())
            {
                if (sub.Zone == zone) hub = sub.Hub;
            }
            int index = 0;
            for (int i = 0; i < points.Length; i++)
            {
                if (points[i].Zone == zone || (hub >= 0 && (points[i].Region == hub || points[i].Zone == hub))) index = i;
            }
            return (points[index].X / StockUiConquest.MapSize, points[index].Y / StockUiConquest.MapSize, index);
        }

        /// <summary>Builds a page's view for the point under the cursor (<paramref name="selected"/>, -1 for none).</summary>
        public static StockUiRegionInfoView Build(RegionMap map, int selected, ProgressionState progression, StockUiRegionInfoFigures figures,
            Func<int, string?>? menuMessage, Func<int, string?>? regionName, Func<int, string?>? zoneName)
        {
            string obtaining = menuMessage?.Invoke(ObtainingText) is { Length: > 0 } text ? text : ObtainingFallback;
            return map switch
            {
                RegionMap.Besieged => Besieged(selected, progression.AlZahbiOrders,
                    new[] { progression.MamookStronghold, progression.HalvungStronghold, progression.ArrapagoStronghold }, figures.ImperialStanding, regionName, zoneName),
                RegionMap.Campaign => Campaign(selected, progression.Campaign, figures.AlliedNotes, obtaining, regionName, zoneName),
                _ => Colonization(selected, progression.Colonization, figures.Bayld, obtaining, regionName, zoneName),
            };
        }

        public static StockUiRegionInfoView Besieged(int selected, byte alZahbiOrders, IReadOnlyList<BesiegedStronghold> strongholds, long standing,
            Func<int, string?>? regionName = null, Func<int, string?>? zoneName = null)
        {
            var bars = new List<StockUiRegionInfoBar>();
            var texts = new List<StockUiRegionInfoText>();
            int stronghold = selected >= 0 && selected < BesiegedPoints.Length ? BesiegedPoints[selected].Stronghold : -1;
            StockUiRegionInfoPanel panel;
            if (stronghold >= 0 && stronghold < strongholds.Count)
            {
                var s = strongholds[stronghold];
                texts.Add(new StockUiRegionInfoText(BaseNameX, BaseNameY, zoneName?.Invoke(BaseZones[stronghold]) ?? string.Empty));
                bars.Add(new StockUiRegionInfoBar(ForcesBarX, ForcesBarY, ForcesBarWidth, Math.Clamp(s.Forces / 100f, 0, 1), StockUiRegionInfoBarColor.Blue));
                texts.Add(new StockUiRegionInfoText(BaseRight, ForcesValueY, Number(s.Forces), RightAligned: true));
                AddForce(texts, BaseForceX, BaseForceY, stronghold, s);
                texts.Add(new StockUiRegionInfoText(BaseRight, MirrorsY, Number(s.Mirrors), RightAligned: true));
                texts.Add(new StockUiRegionInfoText(BaseRight, PrisonersY, Number(s.Prisoners), RightAligned: true));
                panel = new StockUiRegionInfoPanel(BaseWindow, texts, bars, BesiegedWidth, Icon: null);
            }
            else
            {
                texts.Add(new StockUiRegionInfoText(OrdersX, OrdersY, Word(AlZahbiOrders, alZahbiOrders)));
                for (int i = 0; i < strongholds.Count && i < Forces.Count; i++)
                {
                    AddForce(texts, OrdersX, ForceY + i * ForcePitch, i, strongholds[i]);
                }
                panel = new StockUiRegionInfoPanel(OrdersWindow, texts, bars, BesiegedWidth);
            }
            var markers = PointMarkers(BesiegedPoints, regionName, zoneName, null);
            AddSubZones(markers, BesiegedZones, selected >= 0 && selected < BesiegedPoints.Length ? BesiegedPoints[selected] : default, selected >= 0, zoneName);
            return new StockUiRegionInfoView(RegionMap.Besieged, BesiegedTitle, null, new[] { panel }, markers, ImperialBox, Number(standing), selected);
        }

        /// <summary>The bases' zones, in S2C 0x05E's order (Mamook, Halvung, Arrapago Reef; the panel's "Enemy Base" line).</summary>
        private static readonly int[] BaseZones = { 65, 62, 54 };

        private static void AddForce(List<StockUiRegionInfoText> texts, float x, float y, int stronghold, BesiegedStronghold s)
        {
            texts.Add(new StockUiRegionInfoText(x, y, Forces[stronghold]));
            texts.Add(new StockUiRegionInfoText(x + (LevelX - OrdersX), y + LevelDy, "Lv"));
            texts.Add(new StockUiRegionInfoText(x + (LevelValueX - OrdersX), y + LevelDy, Number(s.Level), RightAligned: true));
            texts.Add(new StockUiRegionInfoText(x + (OrderX - OrdersX), y + OrderDy, Word(StrongholdOrders, s.Orders)));
        }

        public static StockUiRegionInfoView Campaign(int selected, CampaignInfo? campaign, long alliedNotes, string obtaining,
            Func<int, string?>? regionName = null, Func<int, string?>? zoneName = null)
        {
            var point = selected >= 0 && selected < CampaignPoints.Length ? CampaignPoints[selected] : default;
            bool picked = selected >= 0 && selected < CampaignPoints.Length;
            StockUiRegionInfoPanel panel;
            if (campaign != null && picked && point.Nation >= 0 && point.Nation < campaign.Nations.Count)
            {
                var nation = campaign.Nations[point.Nation];
                var (side, name) = Nations[point.Nation];
                panel = new StockUiRegionInfoPanel(NationWindow, new[]
                {
                    new StockUiRegionInfoText(NationTextX, NationLine1Y, side), new StockUiRegionInfoText(NationTextX, NationLine2Y, name),
                    new StockUiRegionInfoText(ReconRight, ReconY, "Lv " + Number(nation.Reconnaissance), RightAligned: true),
                }, new[]
                {
                    new StockUiRegionInfoBar(BarX, MoraleBarY, BarWidth, Math.Clamp(nation.Morale / 100f, 0, 1)),
                    new StockUiRegionInfoBar(BarX, ProsperityBarY, BarWidth, Math.Clamp(nation.Prosperity / 100f, 0, 1)),
                }, Icon: point.Nation < 3 ? point.Crest : "mcre");
            }
            else if (campaign != null && picked && point.Record >= 0 && point.Record < campaign.Zones.Count)
            {
                var zone = campaign.Zones[point.Record];
                // The client shows an influence byte times 0.4 (XiPackets 0x0071).
                float Share(byte value) => Math.Clamp(value * 0.4f / 100f, 0, 1);
                panel = new StockUiRegionInfoPanel(ZoneWindow, new[]
                {
                    new StockUiRegionInfoText(ZoneRight, FortY, $"{Number(zone.Fortifications)}/{Number(zone.MaxFortifications)}", RightAligned: true),
                    new StockUiRegionInfoText(ZoneRight, ResourcesY, $"{Number(zone.Resources)}/{Number(zone.MaxResources)}", RightAligned: true),
                }, new[]
                {
                    new StockUiRegionInfoBar(BarX, InfluenceBarY[0], BarWidth, Share(zone.InfluenceSandoria)),
                    new StockUiRegionInfoBar(BarX, InfluenceBarY[1], BarWidth, Share(zone.InfluenceBastok)),
                    new StockUiRegionInfoBar(BarX, InfluenceBarY[2], BarWidth, Share(zone.InfluenceWindurst)),
                    new StockUiRegionInfoBar(BarX, InfluenceBarY[3], BarWidth, Share(zone.InfluenceBeastmen)),
                }, Icon: zone.Owner is >= 1 and <= 4 ? Crests[zone.Owner - 1] : null);
            }
            else
            {
                var texts = new List<StockUiRegionInfoText>();
                for (int i = 0; campaign != null && i < ControlledRows.Length && i < campaign.ControlledZones.Count; i++)
                {
                    texts.Add(new StockUiRegionInfoText(ControlledRight, ControlledRows[i], Number(campaign.ControlledZones[i]), RightAligned: true));
                }
                panel = new StockUiRegionInfoPanel(CampaignWindow, texts, Array.Empty<StockUiRegionInfoBar>());
            }
            var markers = PointMarkers(CampaignPoints, regionName, zoneName, campaign);
            foreach (var (region, x, y) in Fronts)
            {
                if (regionName?.Invoke(region) is { Length: > 0 } name) markers.Add(new StockUiRegionMarker(StockUiRegionMarkerLook.Front, name, x, y, Above));
            }
            if (picked && point.Record >= 0 && zoneName?.Invoke(point.Zone) is { Length: > 0 } label)
            {
                // Retail's label has no " [S]" (image-19: "Throne Room").
                string plain = label.EndsWith(" [S]", StringComparison.Ordinal) ? label[..^4] : label;
                markers.Add(new StockUiRegionMarker(StockUiRegionMarkerLook.Front, plain, point.X, point.Y, point.Side) with { Look = StockUiRegionMarkerLook.Ball, Crest = string.Empty });
            }
            // Nothing from the server yet: no panel and no box (the video of 2026-11-04).
            if (campaign == null) return new StockUiRegionInfoView(RegionMap.Campaign, CampaignTitle, obtaining, Array.Empty<StockUiRegionInfoPanel>(), markers, string.Empty, string.Empty, selected);
            return new StockUiRegionInfoView(RegionMap.Campaign, CampaignTitle, null, new[] { panel }, markers, AlliedBox,
                Number(campaign.AlliedNotes > 0 ? campaign.AlliedNotes : alliedNotes), selected);
        }

        public static StockUiRegionInfoView Colonization(int selected, ColonizationInfo? colonization, long bayld, string obtaining,
            Func<int, string?>? regionName = null, Func<int, string?>? zoneName = null)
        {
            bool picked = selected >= 0 && selected < ColonizationPoints.Length;
            var point = picked ? ColonizationPoints[selected] : default;
            StockUiRegionInfoPanel panel;
            if (picked && point.ColonyIndex >= 0 && colonization != null && point.ColonyIndex < colonization.Zones.Count)
            {
                var here = colonization.Zones[point.ColonyIndex];
                panel = new StockUiRegionInfoPanel(RateWindow, new[]
                {
                    new StockUiRegionInfoText(RateRight, RateY, Number(here.Rate) + "%", RightAligned: true),
                    new StockUiRegionInfoText(RateRight, BivouacsY, $"{Number(here.Bivouacs)}/{Number(here.MaxBivouacs)}", RightAligned: true),
                }, new[] { new StockUiRegionInfoBar(BarX, RateBarY, BarWidth, Math.Clamp(here.Rate / 100f, 0, 1)) });
            }
            else
            {
                var texts = new List<StockUiRegionInfoText>();
                for (int i = 0; colonization != null && i < RankRows.Length && i < colonization.CoalitionRanks.Count; i++)
                {
                    texts.Add(new StockUiRegionInfoText(RankRight, RankRows[i], Number(colonization.CoalitionRanks[i]), RightAligned: true));
                }
                panel = new StockUiRegionInfoPanel(RanksWindow, texts, Array.Empty<StockUiRegionInfoBar>());
            }
            var markers = PointMarkers(ColonizationPoints, regionName, zoneName, null);
            AddSubZones(markers, ColonizationZones, point, picked, zoneName);
            return new StockUiRegionInfoView(RegionMap.Colonization, ColonizationTitle, colonization == null ? obtaining : null, new[] { panel }, markers, BayldBox,
                Number(colonization?.Bayld is { } value && value > 0 ? value : bayld), selected);
        }

        /// <summary>The map's points: its hubs and cities, and a Campaign zone's dot with S2C 0x071's owner (0 until it comes).</summary>
        private static List<StockUiRegionMarker> PointMarkers(Point[] points, Func<int, string?>? regionName, Func<int, string?>? zoneName, CampaignInfo? campaign)
        {
            var markers = new List<StockUiRegionMarker>();
            foreach (var p in points)
            {
                if (p.Record >= 0)
                {
                    int owner = campaign != null && p.Record < campaign.Zones.Count ? campaign.Zones[p.Record].Owner : 0;
                    markers.Add(new StockUiRegionMarker(StockUiRegionMarkerLook.CampaignZone, string.Empty, p.X, p.Y, p.Side, owner));
                    continue;
                }
                string name = (p.Region >= 0 ? regionName?.Invoke(p.Region) : zoneName?.Invoke(p.Zone)) ?? string.Empty;
                markers.Add(new StockUiRegionMarker(p.Look, name, p.X, p.Y, p.Side, Crest: p.Crest));
            }
            return markers;
        }

        private static void AddSubZones(List<StockUiRegionMarker> markers, SubZone[] zones, Point hub, bool picked, Func<int, string?>? zoneName)
        {
            if (!picked) return;
            foreach (var sub in zones)
            {
                if (sub.Hub != hub.Region && sub.Hub != hub.Zone) continue;
                markers.Add(new StockUiRegionMarker(StockUiRegionMarkerLook.Ball, zoneName?.Invoke(sub.Zone) ?? string.Empty, sub.X, sub.Y, sub.Side));
            }
        }

        private static string Word(IReadOnlyList<string> words, int value) =>
            value >= 0 && value < words.Count ? words[value] : value.ToString(CultureInfo.InvariantCulture);

        private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
