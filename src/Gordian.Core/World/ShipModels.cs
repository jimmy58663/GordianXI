// src/Gordian.Core/World/ShipModels.cs
using System.Collections.Generic;

namespace Gordian.Core.World
{
    /// <summary>
    /// Where a ship entity's model is (#403). A ship (S2C 0x00E SubKind 4) carries a ship number, not a model id, and no file
    /// of the install holds a ship model on its own: the only vessel meshes are the second ZoneDefs of the ship zones (their
    /// <c>mode/ship</c> directory, <see cref="Resources.Models.ZoneVessel"/>; every other zone DAT with a ship stop has an
    /// empty 0x2E section named <c>ship</c> instead, checked over the whole retail install on 2026-10-20). So the ship a dock
    /// zone shows is the vessel of the zone it sails to: the barge (16, Carpenters' Landing) is Phanauet Channel's, the
    /// Manaclipper (17, Bibiki Bay) is the Manaclipper zone's. Ship numbers from LandSandBoat
    /// (https://github.com/LandSandBoat/server, data/zones/carpenters_landing/npcs.yaml and bibiki_bay/npcs.yaml).
    /// **Beyond XiPackets / LandSandBoat**, which do not say what the number selects. Other ship numbers (the airships 1, 2,
    /// 5, 6, the ferries 3, 4, 13, 14, 20, 21...) are not mapped yet: their vessels differ between the zones of one route.
    /// </summary>
    public static class ShipModels
    {
        private static readonly Dictionary<uint, int> VesselZones = new()
        {
            [16] = 1, // the barge: Phanauet Channel
            [17] = 3, // the Manaclipper
        };

        /// <summary>The zone whose vessel ZoneDef is ship <paramref name="shipNumber"/>'s model; false when unknown.</summary>
        public static bool TryGetVesselZone(uint shipNumber, out int zoneId) => VesselZones.TryGetValue(shipNumber, out zoneId);
    }
}
