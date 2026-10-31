using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.World.Collision;
using Xunit;

namespace Gordian.Core.Tests.World.Collision
{
    public class CullingTableTests
    {
        private static readonly Vector3 Up = new(0, -1, 0); // internal -Y is up

        private static IEnumerable<CollisionTriangle> Floor(float x0, float z0, float x1, float z1, float y, int table)
        {
            yield return new CollisionTriangle(new(x0, y, z0), new(x1, y, z0), new(x1, y, z1), Up, false, CollisionTerrain.Stone, false, table);
            yield return new CollisionTriangle(new(x0, y, z0), new(x1, y, z1), new(x0, y, z1), Up, false, CollisionTerrain.Stone, false, table);
        }

        [Fact]
        public void TheHighestFloorUnderTheEyePicksTheTable()
        {
            var triangles = new List<CollisionTriangle>();
            triangles.AddRange(Floor(0, 0, 20, 20, 0, table: 1));    // ground
            triangles.AddRange(Floor(0, 0, 20, 20, -10, table: 4));  // a balcony 10 yalms up
            triangles.AddRange(Floor(30, 0, 50, 20, 0, table: -1));  // a floor with no table
            var mesh = new ZoneCollisionMesh(triangles);

            Assert.Equal(1, mesh.CullingTableUnder(new Vector3(5, -3, 5)));   // between the floors: the ground
            Assert.Equal(4, mesh.CullingTableUnder(new Vector3(5, -12, 5)));  // above the balcony
            Assert.Equal(-1, mesh.CullingTableUnder(new Vector3(40, -3, 5))); // no table: everything draws
            Assert.Equal(-1, mesh.CullingTableUnder(new Vector3(25, -3, 5))); // no floor at all
        }

        [Fact]
        public void AnInteriorsFloorsPickNoTableOfTheZone()
        {
            var zone = new ZoneCollisionMesh(new List<CollisionTriangle>(Floor(0, 0, 20, 20, 0, table: 2)));
            var interior = new ZoneCollisionMesh(new List<CollisionTriangle>(Floor(100, 100, 120, 120, 0, table: 0)));

            var merged = ZoneCollisionMesh.Merge(zone, new[] { interior });

            Assert.Equal(2, merged.CullingTableUnder(new Vector3(5, -2, 5)));
            Assert.Equal(-1, merged.CullingTableUnder(new Vector3(110, -2, 110)));
        }
    }
}
