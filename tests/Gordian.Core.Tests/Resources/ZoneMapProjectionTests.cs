// tests/Gordian.Core.Tests/Resources/ZoneMapProjectionTests.cs
using System.IO;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Events;
using Gordian.Core.Resources.Graphics;
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
            Assert.Equal(0.4f, ZoneMapProjection.ScaleOf(204, 1)); // Fei'Yin: a dungeon
            Assert.Equal(0.4f, ZoneMapProjection.ScaleOf(12, 0)); // Newton Movalpolos
            Assert.Equal(0.2f, ZoneMapProjection.ScaleOf(130, 0)); // Ru'Aun Gardens
            Assert.Equal(0.2f, ZoneMapProjection.ScaleOf(100, 0)); // West Ronfaure: a field (round 2 of #485)
            Assert.Equal(0.8f, ZoneMapProjection.ScaleOf(26, 1)); // Tavnazian Safehold's town map...
            Assert.Equal(0.2f, ZoneMapProjection.ScaleOf(26, 3)); // ...and its 0.2 map
            Assert.Equal(0.8f, ZoneMapProjection.ScaleOf(9, 1)); // Pso'Xja: fitted on its floors' collision (#481)
            Assert.Equal(0.4f, ZoneMapProjection.ScaleOf(162, 4)); // Castle Zvahl Keep's floor 4 (#481)
            Assert.Null(ZoneMapProjection.ScaleOf(9, 16)); // the floor fit was not decisive
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
                Assert.True(HomePoints.TryGet(i, out _, out _, out _, out float y, out _));
                Assert.Equal(Read(0x1E2E, i), (int)System.MathF.Round(y * 1000));
            }
            Assert.Equal(0, Read(0x152E, HomePoints.Count)); // the table ends there
        }

        /// <summary>
        /// #479: every survival guide NPC runs one script (zone 2's actor 0x0100217D) with per-number tables 0xD0 bytes apart:
        /// zone at 0x13BF, x at 0x17CF, z at 0x189F, y at 0x196F; its query map entries are 0xD4 sub 1 with kind 2.
        /// </summary>
        [Fact]
        public void SurvivalGuides_MatchTheRetailGuideEventsTables()
        {
            var resources = OpenGame();
            if (resources == null) return;
            var script = ZoneEventScript.Parse(resources.LoadDatBytesByFileId(ZoneEventScript.GetFileId(2))!)!;
            EventBlock? block = null;
            foreach (var b in script.Blocks) if (b.ActorId == 0x0100217D) block = b;
            Assert.NotNull(block);
            Assert.True(block!.TryGetEvent(8500, out _, out _));

            int Read(int table, int i)
            {
                int key = block!.Code[table + 2 * i] | block.Code[table + 2 * i + 1] << 8;
                Assert.True((key & 0x8000) != 0);
                return (int)block.References[key & 0x7FFF];
            }

            for (int i = 0; i < SurvivalGuides.Count; i++)
            {
                Assert.True(SurvivalGuides.TryGet(i, out int zone, out float x, out float y, out float z));
                Assert.Equal(Read(0x13BF, i), zone);
                Assert.Equal(Read(0x17CF, i), (int)System.MathF.Round(x * 1000));
                Assert.Equal(Read(0x189F, i), (int)System.MathF.Round(z * 1000));
                Assert.Equal(Read(0x196F, i), (int)System.MathF.Round(y * 1000));
                Assert.Equal(0x80 | Read(0x16FF, i), SurvivalGuides.ListNumberOf(i));
                Assert.Equal(i, SurvivalGuides.FromListNumber(SurvivalGuides.ListNumberOf(i)));
            }
            Assert.Equal(-1, SurvivalGuides.FromListNumber(6)); // a guide's own number is not a list number
            Assert.Equal(0, Read(0x13BF, SurvivalGuides.Count)); // the table ends there
            Assert.Equal(98, SurvivalGuides.Count);
        }

        [Fact]
        public void SurvivalGuide_StandsWhereRetailDrawsTheBook()
        {
            // Retail round 1 image-5 of #168: the book just above Northern San d'Oria's home point #1.
            var resources = OpenGame();
            if (resources == null) return;
            var origin = ReadOrigin(resources, 231)!.Value;
            float scale = ZoneMapProjection.ScaleOf(231, 0)!.Value;
            var guide = Assert.Single(SurvivalGuides.InZone(231));
            HomePoints.TryGet(3, out _, out _, out float hx, out float hz);
            var (gu, gv) = ZoneMapProjection.ToMap(origin, scale, guide.X, guide.Z);
            var (hu, hv) = ZoneMapProjection.ToMap(origin, scale, hx, hz);
            Assert.InRange(gu - hu, -6, 6); // straight above
            Assert.InRange(hv - gv, 4, 12); // 7 map px up
        }

        /// <summary>
        /// #481: a collision transform's +0xA4 word names the zone map of its floor (bits 3-5, then bits 26-27 as 8 and 16).
        /// </summary>
        [Fact]
        public void MapFloorOf_ReadsTheFloorBits()
        {
            Assert.Equal(4, ZoneCollisionDecoder.MapFloorOf(0x20));
            Assert.Equal(4, ZoneCollisionDecoder.MapFloorOf(0x21));
            Assert.Equal(1, ZoneCollisionDecoder.MapFloorOf(0x0A));
            Assert.Equal(0, ZoneCollisionDecoder.MapFloorOf(0x06));
            Assert.Equal(14, ZoneCollisionDecoder.MapFloorOf(0x4000036));
            Assert.Equal(16, ZoneCollisionDecoder.MapFloorOf(0x8000006));
            Assert.Equal(17, ZoneCollisionDecoder.MapFloorOf(0x800000E));
        }

        /// <summary>
        /// #481 against the install: every floor a multi-floor zone's collision names is one of its map files, and the
        /// home point and survival guide floors the tables keep are what the collision says at their x, y and z.
        /// </summary>
        [Fact]
        public void Floors_AreTheZonesMaps_AndTheTravelPointsFloorsMatchTheCollision()
        {
            var resources = OpenGame();
            if (resources == null) return;
            foreach (int zone in new[] { 9, 34, 162, 167, 205 })
            {
                var collision = resources.TryLoadZoneCollision(zone)!;
                var floors = new System.Collections.Generic.HashSet<int>();
                foreach (var t in collision.Triangles) if (t.MapFloor != 0) floors.Add(t.MapFloor);
                Assert.NotEmpty(floors);
                foreach (int floor in floors) Assert.Contains((byte)floor, ZoneMapFiles.MapsOf(zone));
            }
            // Castle Zvahl Keep's home point stands on floor 4.
            Assert.Equal(4, resources.TryLoadZoneCollision(162)!.MapFloorUnder(new System.Numerics.Vector3(-554, -70, 66)));

            for (int i = 0; i < HomePoints.Count; i++)
            {
                HomePoints.TryGet(i, out int zone, out _, out float x, out float y, out float z);
                if (ZoneMapFiles.MapsOf(zone).Count < 2) continue;
                var collision = resources.TryLoadZoneCollision(zone);
                Assert.Equal(ZoneMapFiles.FloorOf(zone, collision, new System.Numerics.Vector3(x, y, z)), HomePoints.MapOf(i));
            }
            for (int i = 0; i < SurvivalGuides.Count; i++)
            {
                SurvivalGuides.TryGet(i, out int zone, out float x, out float y, out float z);
                if (ZoneMapFiles.MapsOf(zone).Count < 2) continue;
                var collision = resources.TryLoadZoneCollision(zone);
                Assert.Equal(ZoneMapFiles.FloorOf(zone, collision, new System.Numerics.Vector3(x, y, z)), SurvivalGuides.MapOf(i));
            }
            Assert.Equal(4, HomePoints.MapOf(58));
            Assert.Equal(-1, HomePoints.MapOf(82)); // Pso'Xja: no floor under its home point, and no map 0
        }
    }
}
