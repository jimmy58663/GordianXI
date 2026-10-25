// tests/Gordian.Core.Tests/Resources/ZoneMapFilesTests.cs
using System.IO;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>The zone map table (#23): which DAT holds each zone's maps, and that one decodes to its 512 x 512 texture.</summary>
    public class ZoneMapFilesTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        [Fact]
        public void MapsOf_ListsAZonesMapNumbers()
        {
            Assert.Equal(new byte[] { 0 }, ZoneMapFiles.MapsOf(231)); // Northern San d'Oria
            Assert.Equal(new byte[] { 0, 1 }, ZoneMapFiles.MapsOf(50)); // Aht Urhgan Whitegate
            Assert.Empty(ZoneMapFiles.MapsOf(0));
            Assert.Equal(Path.Combine("ROM", "18", "60.DAT"), ZoneMapFiles.PathOf(231, 0));
            // A base map re-shipped in an expansion folder: the later copy.
            Assert.Equal(Path.Combine("ROM", "282", "121.DAT"), ZoneMapFiles.PathOf(113, 0));
            Assert.Null(ZoneMapFiles.PathOf(231, 1));
        }

        [Fact]
        public void Load_DecodesTheRetailMap()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var resources = new ResourceManager(GameDirectory);
            resources.InitializeFileTable();
            var map = ZoneMapFiles.Load(resources, 231, 0);
            Assert.NotNull(map);
            Assert.Equal(512, map!.Width);
            Assert.Equal(512, map.Height);
            Assert.Contains("m_231_00", map.Name);
        }
    }
}
