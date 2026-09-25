using System.Numerics;
using Gordian.Core.World;
using Gordian.Core.World.Collision;

namespace Gordian.Core.Tests.World.Collision
{
    public class EntityGroundingTests
    {
        private static readonly Vector3 Up = new(0.0f, -1.0f, 0.0f);

        private static IEnumerable<CollisionTriangle> Floor(float x0, float z0, float x1, float z1, float y)
        {
            yield return new CollisionTriangle(new(x0, y, z0), new(x1, y, z0), new(x1, y, z1), Up, false, CollisionTerrain.Stone, false);
            yield return new CollisionTriangle(new(x0, y, z0), new(x1, y, z1), new(x0, y, z1), Up, false, CollisionTerrain.Stone, false);
        }

        /// <summary>
        /// Ground at y = 0 with a bridge deck 6 yalms up (y = -6) across x in [-2, 2].
        /// </summary>
        private static ZoneCollisionMesh GroundAndBridge()
        {
            var triangles = Floor(-20, -20, 20, 20, 0.0f).ToList();
            triangles.AddRange(Floor(-2, -20, 2, 20, -6.0f));
            return new ZoneCollisionMesh(triangles);
        }

        [Fact]
        public void PositionHackedPlayer_IsDrawnOnTheFloorBelow()
        {
            // Windower capture: a player holding itself 10 yalms up is drawn walking on the floor.
            var player = new PlayerEntity(2, 2) { Position = new Vector3(10, -10.0f, 0) };
            Assert.Equal(0.0f, EntityGrounding.GetDisplayHeight(player, GroundAndBridge()), 3);
        }

        [Fact]
        public void CharacterOnABridge_StaysOnTheBridge()
        {
            var mesh = GroundAndBridge();
            Assert.Equal(-6.0f, EntityGrounding.GetDisplayHeight(new PlayerEntity(2, 2) { Position = new Vector3(0, -6.0f, 0) }, mesh), 3);
            Assert.Equal(0.0f, EntityGrounding.GetDisplayHeight(new PlayerEntity(3, 3) { Position = new Vector3(0, 0.0f, 0) }, mesh), 3);
        }

        [Fact]
        public void GroundFlagTransportsAndMissingCollision_KeepTheReportedHeight()
        {
            var mesh = GroundAndBridge();
            var ghost = new WorldEntity(4, 4, EntityType.Npc) { Position = new Vector3(10, -10.0f, 0), IgnoresWorldCollision = true };
            var door = new WorldEntity(5, 5, EntityType.Door) { Position = new Vector3(10, -3.0f, 0) };
            var mob = new WorldEntity(6, 6, EntityType.Monster) { Position = new Vector3(10, -4.0f, 0) };

            Assert.Equal(-10.0f, EntityGrounding.GetDisplayHeight(ghost, mesh), 3);
            Assert.Equal(-3.0f, EntityGrounding.GetDisplayHeight(door, mesh), 3);
            Assert.Equal(0.0f, EntityGrounding.GetDisplayHeight(mob, mesh), 3);
            Assert.Equal(-4.0f, EntityGrounding.GetDisplayHeight(mob, null), 3);

            // Under the map (nothing below): the reported height.
            var under = new PlayerEntity(7, 7) { Position = new Vector3(10, 5.0f, 0) };
            Assert.Equal(5.0f, EntityGrounding.GetDisplayHeight(under, mesh), 3);
        }
    }
}
