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

    /// <summary>A bar a Region Info panel fills over its frame's baked track: from (X, Y), <see cref="Width"/> long, <see cref="Fill"/> 0-1 of it.</summary>
    public readonly record struct StockUiRegionInfoBar(float X, float Y, float Width, float Fill);

    /// <summary>One of a Region Info page's DAT windows, drawn at its DAT place, with the values written on it.</summary>
    public sealed record StockUiRegionInfoPanel(string Window, IReadOnlyList<StockUiRegionInfoText> Texts, IReadOnlyList<StockUiRegionInfoBar> Bars);

    /// <summary>
    /// What a Besieged, Campaign or Colonization page (#492) shows over its region map: the help bar's title and line, and
    /// its panels. <see cref="PanelsSwapSides"/>: the panels move to the screen's left half while the map is panned right of
    /// its centre (Besieged's Orders panel, the maintainer's retail check of 2026-11-01).
    /// </summary>
    public sealed record StockUiRegionInfoView(RegionMap Map, string Title, string? HelpLine, IReadOnlyList<StockUiRegionInfoPanel> Panels,
        bool PanelsSwapSides);

    /// <summary>
    /// Region Info's Besieged, Campaign and Colonization pages (#492), built on the Conquest map of #488: each opens its region
    /// map (<see cref="RegionMapFiles"/>), panned by the movement input at the conquest map's fixed size, never zoomed, with the
    /// English menu DAT's panels for it (read 2026-11-02 with <c>GORDIAN_UI_DUMP_MENUS</c>; all 112 px wide, top right):
    /// <list type="bullet">
    /// <item><b>Besieged</b>: <c>beseige1</c> (112 x 206; "Orders:" and "Battle Status:" baked in), filled from S2C 0x05E's
    /// Besieged block: Al Zahbi's orders and each beastman stronghold's orders and level. <c>beseige2</c> (Enemy Base, Enemy
    /// Forces, Archaic Mirrors, Prisoners: a stronghold's details) is not shown: when retail shows it is not captured.</item>
    /// <item><b>Campaign</b>: <c>campresu</c> (112 x 88; "Controlled Areas" with San d'Oria, Bastok, Windurst, Beastmen),
    /// filled from S2C 0x071 mode 2. LandSandBoat never sends it, so the help bar reads the menu message "Now obtaining
    /// information from server." (ROM/165/72 #6), as the maintainer saw retail do on LandSandBoat. <c>campsan</c> (a nation's
    /// Reconnaissance, Morale, Prosperity) and <c>camparea</c> (a zone's Fortifications, Resources, Influence) are not
    /// shown: they need a cursor on the map, not captured.</item>
    /// <item><b>Colonization</b>: in an Ulbuka zone, <c>coloresu</c> (112 x 56; "Col. Rate" with a bar, "Bivouacs") for that
    /// zone; elsewhere (the Adoulin cities) <c>colorank</c> (112 x 120; "Coalition Rank" and the six coalitions); and
    /// <c>colopoin</c> (112 x 40 at 384,256; "Bayld") under either. Filled from S2C 0x071 mode 3, which LandSandBoat sends
    /// in answer to C2S 0x05A.</item>
    /// </list>
    /// PROVISIONAL: the values' places (right-aligned 8 px in from the frame's right edge on the baked label rows), their
    /// wording (Besieged orders after LandSandBoat's <c>besieged_system.h</c> names; no table of the install holds them) and
    /// the ranks / rates shown outside Ulbuka.
    /// </summary>
    public static class StockUiRegionInfo
    {
        public const string BesiegedWindow = "beseige1", CampaignWindow = "campresu";
        public const string RanksWindow = "colorank", RateWindow = "coloresu", BayldWindow = "colopoin";

        public const string BesiegedTitle = "Besieged", CampaignTitle = "Campaign", ColonizationTitle = "Colonization";

        /// <summary>The menu message (ROM/165/72) "Now obtaining information from server.", and its fallback.</summary>
        public const int ObtainingText = 6;

        public const string ObtainingFallback = "Now obtaining information from server.";

        /// <summary>Where right-aligned values end, from the frames' left edge (layout px; the frames are 112 wide).</summary>
        public const float ValueRight = 104;

        /// <summary>Besieged: the orders line under "Orders:" and the battle status rows under "Battle Status:" (layout px).</summary>
        public const float OrdersX = 10, OrdersY = 22, StatusY = 66, StatusPitch = 12;

        /// <summary>Campaign's and the coalitions' value rows: the baked labels' rows (layout px).</summary>
        public static readonly float[] ControlledRows = { 22, 38, 54, 70 };

        public static readonly float[] RankRows = { 21, 37, 53, 69, 85, 101 };

        /// <summary>Colonization's rate row, bar (over the baked track, x 32-96) and bivouacs row; Bayld's value row (layout px).</summary>
        public const float RateY = 5, RateBarX = 32, RateBarY = 20, RateBarWidth = 64, BivouacsY = 36, BayldY = 22;

        /// <summary>Al Zahbi's orders by S2C 0x05E's value (LandSandBoat <c>ORDERS_ALZAHBI_*</c>).</summary>
        public static readonly IReadOnlyList<string> AlZahbiOrders = new[] { "Defend Al Zahbi", "Intercept enemy forces", "Infiltrate enemy base" };

        /// <summary>How many characters of the orders fit on one line of the 112-px panel at the values' size; longer orders wrap.</summary>
        public const int OrdersLineLength = 16;

        /// <summary>A beastman stronghold's orders by value (LandSandBoat <c>ORDERS_*</c>).</summary>
        public static readonly IReadOnlyList<string> StrongholdOrders = new[] { "Training", "Advancing", "Attacking", "Retreating", "Defending", "Preparing" };

        /// <summary>The beastman forces of the three strongholds, in S2C 0x05E's order (Mamook, Halvung, Arrapago).</summary>
        public static readonly IReadOnlyList<string> Strongholds = new[] { "Mamool Ja", "Trolls", "Undead Swarm" };

        /// <summary>The Colonization zones in S2C 0x071 mode 3's order: Yahse, Ceizak, Foret de Hennetiel, Yorcia, Morimar, Marjami, Kamihr, Outer Ra'Kaznar.</summary>
        public static readonly IReadOnlyList<int> ColonizationZones = new[] { 260, 261, 262, 263, 265, 266, 267, 274 };

        /// <summary>Builds a page's view from the progression state; <paramref name="zone"/> is the character's.</summary>
        public static StockUiRegionInfoView Build(RegionMap map, ProgressionState progression, int zone, Func<int, string?>? menuMessage)
        {
            string obtaining = menuMessage?.Invoke(ObtainingText) is { Length: > 0 } text ? text : ObtainingFallback;
            return map switch
            {
                RegionMap.Besieged => Besieged(progression.AlZahbiOrders,
                    new[] { progression.MamookStronghold, progression.HalvungStronghold, progression.ArrapagoStronghold }),
                RegionMap.Campaign => Campaign(progression.Campaign, obtaining),
                _ => Colonization(progression.Colonization, zone, obtaining),
            };
        }

        public static StockUiRegionInfoView Besieged(byte alZahbiOrders, IReadOnlyList<BesiegedStronghold> strongholds)
        {
            var texts = new List<StockUiRegionInfoText>();
            string orders = Word(AlZahbiOrders, alZahbiOrders);
            int cut = orders.Length > OrdersLineLength ? orders.LastIndexOf(' ', OrdersLineLength) : -1;
            texts.Add(new StockUiRegionInfoText(OrdersX, OrdersY, cut > 0 ? orders[..cut] : orders));
            if (cut > 0) texts.Add(new StockUiRegionInfoText(OrdersX, OrdersY + StatusPitch, orders[(cut + 1)..]));
            for (int i = 0; i < strongholds.Count && i < Strongholds.Count; i++)
            {
                float y = StatusY + i * 2 * StatusPitch;
                texts.Add(new StockUiRegionInfoText(OrdersX, y, Strongholds[i]));
                texts.Add(new StockUiRegionInfoText(ValueRight, y + StatusPitch,
                    $"{Word(StrongholdOrders, strongholds[i].Orders)} Lv.{strongholds[i].Level.ToString(CultureInfo.InvariantCulture)}", RightAligned: true));
            }
            return new StockUiRegionInfoView(RegionMap.Besieged, BesiegedTitle, null,
                new[] { new StockUiRegionInfoPanel(BesiegedWindow, texts, Array.Empty<StockUiRegionInfoBar>()) }, PanelsSwapSides: true);
        }

        public static StockUiRegionInfoView Campaign(CampaignInfo? campaign, string obtaining)
        {
            var texts = new List<StockUiRegionInfoText>();
            for (int i = 0; campaign != null && i < ControlledRows.Length && i < campaign.ControlledZones.Count; i++)
            {
                texts.Add(new StockUiRegionInfoText(ValueRight, ControlledRows[i], Number(campaign.ControlledZones[i]), RightAligned: true));
            }
            return new StockUiRegionInfoView(RegionMap.Campaign, CampaignTitle, campaign == null ? obtaining : null,
                new[] { new StockUiRegionInfoPanel(CampaignWindow, texts, Array.Empty<StockUiRegionInfoBar>()) }, PanelsSwapSides: false);
        }

        public static StockUiRegionInfoView Colonization(ColonizationInfo? colonization, int zone, string obtaining)
        {
            int zoneIndex = -1;
            for (int i = 0; i < ColonizationZones.Count; i++)
            {
                if (ColonizationZones[i] == zone) zoneIndex = i;
            }
            var panels = new List<StockUiRegionInfoPanel>();
            var texts = new List<StockUiRegionInfoText>();
            var bars = new List<StockUiRegionInfoBar>();
            if (zoneIndex >= 0)
            {
                if (colonization != null && zoneIndex < colonization.Zones.Count)
                {
                    var here = colonization.Zones[zoneIndex];
                    texts.Add(new StockUiRegionInfoText(ValueRight, RateY, Number(here.Rate) + "%", RightAligned: true));
                    bars.Add(new StockUiRegionInfoBar(RateBarX, RateBarY, RateBarWidth, Math.Clamp(here.Rate / 100f, 0, 1)));
                    texts.Add(new StockUiRegionInfoText(ValueRight, BivouacsY, $"{Number(here.Bivouacs)}/{Number(here.MaxBivouacs)}", RightAligned: true));
                }
                panels.Add(new StockUiRegionInfoPanel(RateWindow, texts, bars));
            }
            else
            {
                for (int i = 0; colonization != null && i < RankRows.Length && i < colonization.CoalitionRanks.Count; i++)
                {
                    texts.Add(new StockUiRegionInfoText(ValueRight, RankRows[i], Number(colonization.CoalitionRanks[i]), RightAligned: true));
                }
                panels.Add(new StockUiRegionInfoPanel(RanksWindow, texts, bars));
            }
            var bayld = colonization != null
                ? new[] { new StockUiRegionInfoText(ValueRight, BayldY, colonization.Bayld.ToString("N0", CultureInfo.InvariantCulture), RightAligned: true) }
                : Array.Empty<StockUiRegionInfoText>();
            panels.Add(new StockUiRegionInfoPanel(BayldWindow, bayld, Array.Empty<StockUiRegionInfoBar>()));
            return new StockUiRegionInfoView(RegionMap.Colonization, ColonizationTitle, colonization == null ? obtaining : null, panels, PanelsSwapSides: false);
        }

        private static string Word(IReadOnlyList<string> words, int value) =>
            value >= 0 && value < words.Count ? words[value] : value.ToString(CultureInfo.InvariantCulture);

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
