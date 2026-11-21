// tests/Gordian.Core.Tests/Resources/InteriorPointLightRealDataTests.cs
using System.IO;
using System.Linq;
using Gordian.Core.Resources;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>
    /// Placements flagged at +0x46 bit 1 take their point lights only in an interior (#428, #612): a building interior's DAT,
    /// or a DAT that authors no outdoor keyframe (a dungeon, a Mog House room). Skipped without the game install.
    /// </summary>
    public class InteriorPointLightRealDataTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        [Fact]
        public void IndoorDatsKeepTheirInteriorOnlyLights_OutdoorZonesDoNot()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();

            // Southern San d'Oria's Mog House room: every lit placement has the bit; its room lamp l_01 lights it.
            Assert.True(rm.TryLoadZone(MogHouseRooms.ZoneKey(0x101), out var room, out _));
            Assert.Contains(room.MeshGroups, g => g.PointLightSlots.Length > 0);

            // Crawlers' Nest: 391 lit mesh groups (15 before #612; most of the rest also carry bit 0, no point lights).
            Assert.True(rm.TryLoadZone(197, out var nest, out _));
            Assert.True(nest.MeshGroups.Count(g => g.PointLightSlots.Length > 0) > 300);

            // Lower Jeuno (outdoor keyframes): step01 by the Guide Stone keeps no lights, as retail shows it almost dark.
            Assert.True(rm.TryLoadZone(245, out var jeuno, out _));
            var step01 = jeuno.MeshGroups.Where(g => g.Name.StartsWith("step01")).ToList();
            Assert.NotEmpty(step01);
            Assert.All(step01, g => Assert.Empty(g.PointLightSlots));
        }
    }
}
