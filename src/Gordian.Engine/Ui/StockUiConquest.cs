// src/Gordian.Engine/Ui/StockUiConquest.cs
using System.Collections.Generic;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Tables;
using Gordian.Core.World;

namespace Gordian.Engine.Ui
{
    /// <summary>One side's bar of the Regional Influence graph: who, and its share in percent.</summary>
    public readonly record struct StockUiInfluenceBar(string Side, int Percent);

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
    /// PROVISIONAL: no retail capture yet; which windows show where, and the text inside them, are our reading of the
    /// frames' baked-in headings.
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

        /// <summary>The values' size against the log font's: the frames are 112 px wide (PROVISIONAL).</summary>
        public const float ValueScale = 0.75f;

        /// <summary>
        /// The page for the character's region (client region numbering, -1 outside one), with the menu texts from
        /// <paramref name="menuText"/> (null for the fallbacks).
        /// </summary>
        public static StockUiConquestView Build(ProgressionState progression, int region, string regionName, System.Func<int, string?>? menuText = null)
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
                BalanceLines(progression.ConquestBalance), progression.ConquestPoints);
        }

        /// <summary>The nations by rank from the 0x05E balance byte ("1st: San d'Oria"); ties share a rank.</summary>
        public static IReadOnlyList<string> BalanceLines(byte balance)
        {
            var lines = new List<string>(3);
            for (int rank = 1; rank <= 3; rank++)
            {
                for (int nation = 0; nation < 3; nation++)
                {
                    if (((balance >> (nation * 2)) & 3) == rank) lines.Add($"{Ordinal(rank)}: {Nations[nation]}");
                }
            }
            return lines;
        }

        private static string Ordinal(int rank) => rank switch { 1 => "1st", 2 => "2nd", _ => "3rd" };

        /// <summary>"3 days", "1 day" (PROVISIONAL wording).</summary>
        public static string TallyText(int days) => days == 1 ? "1 day" : $"{days} days";
    }
}
