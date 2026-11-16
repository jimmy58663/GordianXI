// tests/Gordian.Core.Tests/Resources/ZoneCachePurgeRealDataTests.cs
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gordian.Core.Resources;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>
    /// #576: the zone caches drop a zone (geometry, textures, collision) and its building interiors once nothing has used it
    /// for <see cref="ResourceManager.ZoneIdleSeconds"/>, and a zone whose render data went after its GPU upload is read
    /// again for the next upload. Skipped without the game install.
    /// </summary>
    public class ZoneCachePurgeRealDataTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        private static ResourceManager? Open()
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            return rm;
        }

        [Fact]
        public void UnusedZones_AreDropped_ZonesInUseAndTheirInteriorsStay()
        {
            var rm = Open();
            if (rm == null) return;
            Assert.True(rm.TryLoadZone(230, out var sandoria, out _));
            // A session in Lower Jeuno without a window: collision only, with its shops' interiors.
            var jeuno = rm.TryLoadZoneCollision(245);
            Assert.NotNull(jeuno);
            int shop = jeuno!.SubAreaVolumes.First(v => v.IsSubArea).Param;
            Assert.NotNull(rm.TryLoadSubAreaCollision(shop));
            Assert.Equal(new ZoneCacheStats(1, 2, 0, 1), rm.GetZoneCacheStats());

            // Within the idle time nothing goes, in use or not.
            Assert.Empty(rm.PurgeUnusedZones([]));
            Assert.Equal(new ZoneCacheStats(1, 2, 0, 1), rm.GetZoneCacheStats());

            // Jeuno in use (a character in it): San d'Oria goes, Jeuno and its shop stay.
            var dropped = rm.PurgeUnusedZones([245], idleSeconds: 0);
            Assert.Same(sandoria, Assert.Single(dropped));
            Assert.Null(rm.TryGetLoadedZone(230));
            Assert.Equal(new ZoneCacheStats(0, 1, 0, 1), rm.GetZoneCacheStats());
            Assert.Same(jeuno, rm.TryLoadZoneCollision(245));

            // The character logs out: Jeuno and its interior go too (#585: named, so their GPU copies go with them).
            var droppedInteriors = new List<int>();
            Assert.Empty(rm.PurgeUnusedZones([], idleSeconds: 0, droppedInteriorIds: droppedInteriors));
            Assert.Equal(new ZoneCacheStats(0, 0, 0, 0), rm.GetZoneCacheStats());
            Assert.Equal([shop], droppedInteriors);

            // Read again on the next visit.
            Assert.True(rm.TryLoadZone(230, out var again, out _));
            Assert.NotSame(sandoria, again);
        }

        [Fact]
        public void ReleasedZone_IsReadAgainForTheNextUpload_SharingItsCollision()
        {
            var rm = Open();
            if (rm == null) return;
            Assert.True(rm.TryLoadZone(230, out var zone, out var textures));
            int triangles = zone.TotalTriangles;
            zone.ReleaseRenderData(textures);
            Assert.True(zone.MeshGroups.All(g => g.Vertices.Length == 0));
            Assert.Contains(textures.Values, t => t.PixelsReleased);
            Assert.Equal(triangles, zone.TotalTriangles);

            // Still the cached copy for everything but an upload.
            Assert.True(rm.TryLoadZone(230, out var cached, out _));
            Assert.Same(zone, cached);

            Assert.True(rm.TryReloadZoneRenderData(230, zone, out var reread, out var rereadTextures));
            Assert.NotSame(zone, reread);
            Assert.False(reread.IsRenderDataReleased);
            Assert.Contains(reread.MeshGroups, g => g.Vertices.Length > 0);
            Assert.DoesNotContain(rereadTextures.Values, t => t.PixelsReleased);
            Assert.Same(zone.Collision, reread.Collision);
            Assert.Same(reread, rm.TryGetLoadedZone(230));

            // A second reader holding the old copy gets the one already read.
            Assert.True(rm.TryReloadZoneRenderData(230, zone, out var second, out _));
            Assert.Same(reread, second);
        }

        [Fact]
        public void ReleasedInteriorAndFurniture_AreReadAgain_AndAShipVesselIsReadFromACopyOfItsOwn()
        {
            var rm = Open();
            if (rm == null) return;
            // #585: the Meadhouse (Lower Jeuno), released after its shared upload, is read again for the next one.
            Assert.True(rm.TryLoadSubAreaInterior(462, out var interior, out var textures));
            interior.ReleaseRenderData(textures, keepRayMesh: false);
            Assert.True(rm.TryLoadSubAreaInterior(462, out var cached, out _));
            Assert.Same(interior, cached);
            Assert.True(rm.TryReloadSubAreaInterior(462, interior, out var reread, out var rereadTextures));
            Assert.NotSame(interior, reread);
            Assert.Contains(reread.MeshGroups, g => g.Vertices.Length > 0);
            Assert.DoesNotContain(rereadTextures.Values, t => t.PixelsReleased);
            Assert.True(rm.TryLoadSubAreaInterior(462, out var next, out _));
            Assert.Same(reread, next);

            // A furnishing (the first item below 256 with a model).
            var bed = Enumerable.Range(1, 255).Select(id => rm.TryLoadFurnitureModel((ushort)id)).First(m => m != null)!;
            bed.ReleaseRenderData();
            var bedAgain = rm.TryReloadFurnitureModel(bed.ItemId);
            Assert.NotNull(bedAgain);
            Assert.NotSame(bed, bedAgain);
            Assert.False(bedAgain!.IsRenderDataReleased);
            Assert.Same(bedAgain, rm.TryLoadFurnitureModel(bed.ItemId));
            Assert.Same(bedAgain, rm.TryReloadFurnitureModel(bed.ItemId));

            // The barge (Phanauet Channel's vessel): a copy of the zone of its own, not the cached zone.
            Assert.True(rm.TryLoadZone(1, out var channel, out _));
            Assert.True(rm.TryReadZoneVessel(1, out var vessel, out var vesselTextures));
            Assert.NotSame(channel.Vessel, vessel);
            Assert.NotEmpty(vessel.MeshGroups);
            Assert.NotEmpty(vesselTextures);
            Assert.Same(channel, rm.TryGetLoadedZone(1));
        }
    }
}
