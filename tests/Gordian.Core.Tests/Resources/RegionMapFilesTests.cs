// tests/Gordian.Core.Tests/Resources/RegionMapFilesTests.cs
using System.IO;
using System.Text;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>The region maps behind Region Info (#488): which DAT holds each, and that each decodes to its named 512 x 512 texture.</summary>
    public class RegionMapFilesTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        [Fact]
        public void PathOf_NamesEachMapsDat()
        {
            Assert.Equal(Path.Combine("ROM", "17", "23.DAT"), RegionMapFiles.PathOf(RegionMap.Conquest));
            Assert.Equal(Path.Combine("ROM", "161", "34.DAT"), RegionMapFiles.PathOf(RegionMap.Besieged));
            Assert.Equal(Path.Combine("ROM", "195", "94.DAT"), RegionMapFiles.PathOf(RegionMap.Campaign));
            Assert.Equal(Path.Combine("ROM", "292", "107.DAT"), RegionMapFiles.PathOf(RegionMap.Colonization));
        }

        [Fact]
        public void RetailDats_HoldTheNamedMaps()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            foreach (var (map, name) in new[] { (RegionMap.Conquest, "conquest"), (RegionMap.Besieged, "m_ex3_rg"), (RegionMap.Campaign, "m_ex4_rg"), (RegionMap.Colonization, "m_ex5_rg") })
            {
                var dat = rm.LoadDatBytes(RegionMapFiles.PathOf(map));
                Assert.NotNull(dat);
                Assert.Contains("menumap " + name, Encoding.ASCII.GetString(dat!));
                var texture = RegionMapFiles.Load(rm, map);
                Assert.Equal((512, 512), (texture!.Width, texture.Height));
            }
        }
    }
}
