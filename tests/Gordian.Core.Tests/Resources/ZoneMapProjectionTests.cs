// tests/Gordian.Core.Tests/Resources/ZoneMapProjectionTests.cs
using System.IO;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Events;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>World to map coordinates (#168) and the retail home point table the home point map reads.</summary>
    public class ZoneMapProjectionTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        private static ResourceManager? OpenGame()
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var resources = new ResourceManager(GameDirectory);
            resources.InitializeFileTable();
            return resources;
        }

        [Fact]
        public void ToMap_PutsTheWorldOriginMinusTheCornerAndNorthUp()
        {
            var origin = new ZoneMapOrigin(-304, -304);
            Assert.Equal((304f, 304f), ZoneMapProjection.ToMap(origin, 1f, 0, 0));
            var (u, v) = ZoneMapProjection.ToMap(origin, 1f, 10, 20);
            Assert.Equal(314f, u);
            Assert.Equal(284f, v); // north is up the map
        }

        [Fact]
        public void ScaleOf_AnswersOnlyForCalibratedMaps()
        {
            Assert.Equal(0.8f, ZoneMapProjection.ScaleOf(230, 0));
            Assert.Equal(0.8f, ZoneMapProjection.ScaleOf(241, 0));
            Assert.Equal(0.2f, ZoneMapProjection.ScaleOf(204, 1)); // Fei'Yin
            Assert.Equal(0.2f, ZoneMapProjection.ScaleOf(130, 0)); // Ru'Aun Gardens
            Assert.Null(ZoneMapProjection.ScaleOf(100, 0)); // West Ronfaure: no home point to check it by
            Assert.Null(ZoneMapProjection.ScaleOf(9, 1)); // Pso'Xja: which floor its home point is on is not known
            Assert.Null(ZoneMapProjection.ScaleOf(230, 1));
        }

        [Fact]
        public void ReadOrigin_ReadsTheImageQuadsCorner_AndTheCaptureLandsInItsGridSquare()
        {
            var resources = OpenGame();
            if (resources == null) return;
            Assert.Equal(new ZoneMapOrigin(-288, -272), ReadOrigin(resources, 230));
            var north = ReadOrigin(resources, 231);
            Assert.Equal(new ZoneMapOrigin(-304, -304), north);

            // The retail capture (Main_Menu_Page1.mp4): Northern San d'Oria (-133.184, 201.095) is grid square F-4. The grid
            // is 32 px squares, A-O across and 1-15 down, inside the image's 16 px border.
            var (u, v) = ZoneMapProjection.ToMap(north!.Value, ZoneMapProjection.ScaleOf(231, 0)!.Value, -133.184f, 201.095f);
            Assert.Equal('F', (char)('A' + (int)((u - 16) / 32)));
            Assert.Equal(4, (int)((v - 16) / 32) + 1);
        }

        /// <summary>
        /// The maintainer's retail screenshots of the home point map (round 1 of #168, 2556 x 1438), registered against the
        /// map images (5.0 screen px per map px): the pale-blue crystal stands on these map pixels.
        /// </summary>
        [Theory]
        [InlineData(3, 231, 162.3f, 247.7f)] // Northern San d'Oria #1 (image-5)
        [InlineData(98, 231, 199.6f, 149.2f)] // Northern San d'Oria #4 (image-7)
        [InlineData(1, 230, 323.8f, 300.2f)] // Southern San d'Oria #2 (image-9)
        public void HomePoints_LandWhereRetailPinsThem(int number, int zone, float retailU, float retailV)
        {
            var resources = OpenGame();
            if (resources == null) return;
            Assert.True(HomePoints.TryGet(number, out int zoneId, out _, out float x, out float z));
            Assert.Equal(zone, zoneId);
            var (u, v) = ZoneMapProjection.ToMap(ReadOrigin(resources, zone)!.Value, ZoneMapProjection.ScaleOf(zone, 0)!.Value, x, z);
            Assert.InRange(u, retailU - 3, retailU + 3);
            Assert.InRange(v, retailV - 3, retailV + 3);
        }

        private static ZoneMapOrigin? ReadOrigin(ResourceManager resources, int zone) =>
            ZoneMapProjection.ReadOrigin(resources.LoadDatBytes(ZoneMapFiles.PathOf(zone, 0)!)!);

        [Fact]
        public void HomePoints_KnowEachPointsZoneAndPlace()
        {
            Assert.True(HomePoints.TryGet(0, out int zone, out int number, out float x, out float z));
            Assert.Equal((230, 1), (zone, number)); // Southern San d'Oria #1
            Assert.Equal(-85.468f, x, 3);
            Assert.Equal(-66.454f, z, 3);
            // The four Northern San d'Oria entries the retail list sends (D4 01 with 3, 4, 5 and 98).
            Assert.True(HomePoints.TryGet(98, out zone, out number, out x, out z));
            Assert.Equal((231, 4), (zone, number));
            Assert.Equal(-133f, x, 3);
            Assert.Equal(195f, z, 3);
            Assert.False(HomePoints.TryGet(HomePoints.Count, out _, out _, out _, out _));
            Assert.Equal(4, HomePoints.InZone(231).Count);
            Assert.Equal(-133f, HomePoints.InZone(231)[3].X, 3);
        }

        /// <summary>
        /// The table is the retail home point event's: Southern San d'Oria's home point NPCs (zone 230, actor 0x010E6087) keep
        /// per-number tables of work references at code offsets 0x152E (zone), 0x172E (number in zone), 0x1C2E (x) and
        /// 0x1D2E (z), in thousandths.
        /// </summary>
        [Fact]
        public void HomePoints_MatchTheRetailHomePointEventsTables()
        {
            var resources = OpenGame();
            if (resources == null) return;
            var script = ZoneEventScript.Parse(resources.LoadDatBytesByFileId(ZoneEventScript.GetFileId(230))!)!;
            EventBlock? block = null;
            foreach (var b in script.Blocks) if (b.ActorId == 0x010E6087) block = b;
            Assert.NotNull(block);

            int Read(int table, int i)
            {
                int key = block!.Code[table + 2 * i] | block.Code[table + 2 * i + 1] << 8;
                Assert.True((key & 0x8000) != 0);
                return (int)block.References[key & 0x7FFF];
            }

            for (int i = 0; i < HomePoints.Count; i++)
            {
                Assert.True(HomePoints.TryGet(i, out int zone, out int number, out float x, out float z));
                Assert.Equal(Read(0x152E, i), zone);
                Assert.Equal(Read(0x172E, i), number);
                Assert.Equal(Read(0x1C2E, i), (int)System.MathF.Round(x * 1000));
                Assert.Equal(Read(0x1D2E, i), (int)System.MathF.Round(z * 1000));
            }
            Assert.Equal(0, Read(0x152E, HomePoints.Count)); // the table ends there
        }
    }
}
