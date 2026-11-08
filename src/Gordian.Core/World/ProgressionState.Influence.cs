// src/Gordian.Core/World/ProgressionState.Influence.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Network.Packets;

namespace Gordian.Core.World
{
    /// <summary>The Campaign map's figures (S2C 0x071 mode 2, #492): zones controlled by side, the nations and the 26 zones.</summary>
    public sealed record CampaignInfo(int AlliedNotes, IReadOnlyList<int> ControlledZones, IReadOnlyList<CampaignNation> Nations,
        IReadOnlyList<CampaignZone> Zones);

    /// <summary>The Colonization map's figures (S2C 0x071 mode 3, #492): the six coalition ranks, the eight zones and Bayld.</summary>
    public sealed record ColonizationInfo(IReadOnlyList<byte> CoalitionRanks, IReadOnlyList<ColonizationZone> Zones, int Bayld);

    public sealed partial class ProgressionState
    {
        private CampaignInfo? _campaign;
        private ColonizationInfo? _colonization;

        /// <summary>The Campaign map's figures, or null before the server sent any (LandSandBoat does not, #492).</summary>
        public CampaignInfo? Campaign
        {
            get { lock (_lock) return _campaign; }
        }

        /// <summary>The Colonization map's figures, or null before the server sent any.</summary>
        public ColonizationInfo? Colonization
        {
            get { lock (_lock) return _colonization; }
        }

        /// <summary>S2C 0x071 brought new Campaign or Colonization figures.</summary>
        public event Action? InfluenceUpdated;

        /// <summary>Takes S2C 0x071: mode 2 fills its half of the Campaign zones, mode 3 replaces the Colonization figures.</summary>
        public void UpdateInfluence(in S2C_0x071_Influence packet)
        {
            if (!packet.IsValid) return;
            lock (_lock)
            {
                if (packet.Mode == S2C_0x071_Influence.CampaignMode)
                {
                    var zones = new CampaignZone[S2C_0x071_Influence.CampaignZoneCount];
                    if (_campaign != null)
                    {
                        for (int i = 0; i < zones.Length; i++) zones[i] = _campaign.Zones[i];
                    }
                    int offset = packet.ZoneOffset;
                    for (int i = 0; i < S2C_0x071_Influence.CampaignZonesPerPacket && offset + i < zones.Length; i++) zones[offset + i] = packet.Zone(i);
                    var controlled = new int[4];
                    for (int i = 0; i < controlled.Length; i++) controlled[i] = packet.ControlledZones(i);
                    var nations = new CampaignNation[S2C_0x071_Influence.CampaignNationCount];
                    for (int i = 0; i < nations.Length; i++) nations[i] = packet.Nation(i);
                    _campaign = new CampaignInfo(packet.AlliedNotes, controlled, nations, zones);
                }
                else
                {
                    var ranks = new byte[S2C_0x071_Influence.CoalitionCount];
                    for (int i = 0; i < ranks.Length; i++) ranks[i] = packet.CoalitionRank(i);
                    var zones = new ColonizationZone[S2C_0x071_Influence.ColonizationZoneCount];
                    for (int i = 0; i < zones.Length; i++) zones[i] = packet.ColonizationZoneAt(i);
                    _colonization = new ColonizationInfo(ranks, zones, packet.Bayld);
                }
            }
            InfluenceUpdated?.Invoke();
        }
    }
}
