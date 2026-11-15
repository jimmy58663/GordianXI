// tests/Gordian.Core.Tests/Resources/ZoneCachePurgeRealDataTests.cs
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

            // The character logs out: Jeuno and its interior go too.
            Assert.Empty(rm.PurgeUnusedZones([], idleSeconds: 0));
            Assert.Equal(new ZoneCacheStats(0, 0, 0, 0), rm.GetZoneCacheStats());

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
    }
}
