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
    public enum StockUiMapLabelSide { Above, Right, Left }

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

        /// <summary>The client region the character is in (-1 outside one).</summary>
        public int Region { get; init; } = -1;

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

        public const string Title = "Region Info";

        /// <summary>The help the <c>region</c> buttons carry (ROM/165/75 #331).</summary>
        public const int HelpRegionInfo = 331;

        /// <summary>The menu message table's (ROM/165/72) control texts.</summary>
        public const int NeutralRegionText = 2, BeastmenRegionText = 3, NeutralAreaText = 4;

        public const string NeutralRegionFallback = "This region is not under any country's control.";
        public const string BeastmenRegionFallback = "This region is under the control of the beastmen.";
        public const string NeutralAreaFallback = "This area is not under any country's control.";

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
        /// Where the regions sit on the conquest map (map px of ROM/17/23; client region numbers), and which side their name
        /// is on. Measured on the maintainer's retail captures (round 2 of #488: image-49 in Bibiki Bay, image-50 panned to
        /// Qufim, image-51 panned between regions): each capture was matched against the map image (scale 11.5, correlation
        /// 0.85-0.92), and the markers seen in two captures agree within half a map pixel. Jeuno (3) is the one city seen.
        /// Not seen yet, so not drawn: San d'Oria, Bastok, Windurst, Norvallen, Gustaberg, Fauregandi, Valdeaunia, Kuzotz,
        /// Vollbow, the Elshimo regions, Tu'Lia and the Tavnazian Archipelago. Movalpolos sat at the bottom edge
        /// (PROVISIONAL height).
        /// </summary>
        private static readonly Dictionary<int, (float X, float Y, StockUiMapLabelSide Side)> RegionPoints = new()
        {
            [3] = (286, 202, StockUiMapLabelSide.Above),
            [4] = (176, 182, StockUiMapLabelSide.Right),
            [5] = (182, 238, StockUiMapLabelSide.Above),
            [8] = (252, 233, StockUiMapLabelSide.Above),
            [9] = (353, 296, StockUiMapLabelSide.Above),
            [10] = (376, 262, StockUiMapLabelSide.Above),
            [11] = (359, 220, StockUiMapLabelSide.Above),
            [14] = (313, 163, StockUiMapLabelSide.Above),
            [15] = (417, 212, StockUiMapLabelSide.Above),
            [22] = (244, 294, StockUiMapLabelSide.Right),
        };

        /// <summary>
        /// Where the zones sit on the conquest map (map px; zone ids), measured as <see cref="RegionPoints"/>. Retail labels
        /// the zones of the region the map is on with their short names (ROM/165/83): Kolshushu's seven in image-49, Qufim's
        /// six in image-50 (LandSandBoat's region of each zone, <see cref="ZoneRegions"/>).
        /// </summary>
        private static readonly Dictionary<int, (float X, float Y, StockUiMapLabelSide Side)> ZonePoints = new()
        {
            [3] = (395, 311, StockUiMapLabelSide.Right),    // Manaclipper
            [4] = (387, 300, StockUiMapLabelSide.Right),    // Bibiki Bay
            [117] = (373, 273, StockUiMapLabelSide.Left),   // Tahrongi Canyon
            [118] = (412, 269, StockUiMapLabelSide.Above),  // Buburimu Peninsula
            [198] = (379, 247, StockUiMapLabelSide.Above),  // Maze of Shakhrami
            [213] = (414, 279, StockUiMapLabelSide.Left),   // Labyrinth of Onzozo
            [249] = (408, 291, StockUiMapLabelSide.Right),  // Mhaura
            [126] = (302, 171, StockUiMapLabelSide.Left),   // Qufim Island
            [127] = (312, 146, StockUiMapLabelSide.Right),  // Behemoth's Dominion
            [157] = (316, 187, StockUiMapLabelSide.Right),  // Middle Delkfutt's Tower
            [158] = (316, 179, StockUiMapLabelSide.Right),  // Upper Delkfutt's Tower
            [179] = (316, 171, StockUiMapLabelSide.Right),  // The Stellar Fulcrum
            [184] = (316, 195, StockUiMapLabelSide.Right),  // Lower Delkfutt's Tower
        };

        /// <summary>The conquest map's image size (px).</summary>
        public const float MapSize = 512;

        /// <summary>The map point (0-1 across and down) of a region's marker, or null for one not placed yet.</summary>
        public static (float U, float V)? CenterOf(int region) =>
            RegionPoints.TryGetValue(region, out var p) ? (p.X / MapSize, p.Y / MapSize) : null;

        /// <summary>
        /// The labels on the conquest map: every placed region and city, and the zones of the character's region (the one in
        /// <paramref name="zone"/> marked as the character's). Names from <paramref name="regionNames"/> (ROM/165/78) and
        /// <paramref name="zoneNames"/> (the short names, ROM/165/83); a point without a name is left out.
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
            foreach (var (id, p) in ZonePoints)
            {
                if (ZoneRegions.RegionOf(id) != region || zoneNames?.Invoke(id) is not { Length: > 0 } name) continue;
                markers.Add(new StockUiConquestMarker(id == zone ? StockUiConquestMarkerKind.CurrentZone : StockUiConquestMarkerKind.Zone, name, p.X, p.Y, p.Side, region));
            }
            return markers;
        }

        /// <summary>The values' size against the log font's: the frames are 112 px wide (PROVISIONAL).</summary>
        public const float ValueScale = 0.75f;

        /// <summary>
        /// The page for the character's region (client region numbering, -1 outside one), with the menu texts from
        /// <paramref name="menuText"/> (null for the fallbacks).
        /// </summary>
        public static StockUiConquestView Build(ProgressionState progression, int region, string regionName, System.Func<int, string?>? menuText = null,
            int zone = -1, System.Func<int, string?>? regionNames = null, System.Func<int, string?>? zoneNames = null)
        {
            string Text(int id, string fallback) => menuText?.Invoke(id) is { Length: > 0 } t ? t : fallback;
            int record = ZoneRegions.ConquestRecordOf(region);
            var influence = progression.ConquestCurrentRegion;
            string control;
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

        /// <summary>"3 days", "1 day" (PROVISIONAL wording).</summary>
        public static string TallyText(int days) => days == 1 ? "1 day" : $"{days} days";
    }
}
