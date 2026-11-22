using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.World.Collision;
using Xunit;

namespace Gordian.Core.Tests.World.Collision
{
    /// <summary>
    /// #632: faces at 60 degrees or steeper are walls at any height and never ground (<see cref="ZoneCollisionMesh.MinFloorNormalY"/>,
    /// <see cref="ZoneCollisionMesh.CrossesLowWall"/>). Internal space: -Y is up.
    /// </summary>
    public class LowWallTests
    {
        private const float StepUp = 0.5f, FootRadius = 0.9f, MaxDrop = 60.0f;

        private static CollisionTriangle Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 normal) =>
            new(a, b, c, Vector3.Normalize(normal), false, CollisionTerrain.Stone, false);

        private static IEnumerable<CollisionTriangle> Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            yield return Tri(a, b, c, normal);
            yield return Tri(a, c, d, normal);
        }

        /// <summary>A floor at height 0 for x 0-1, a 0.15 riser at x = 1 facing -X, and an upper floor from x 1 to 3.</summary>
        private static ZoneCollisionMesh Curb(float upperFarHeight)
        {
            const float top = -0.15f;
            var triangles = new List<CollisionTriangle>();
            triangles.AddRange(Quad(new(0, 0, 0), new(1, 0, 0), new(1, 0, 2), new(0, 0, 2), -Vector3.UnitY));
            triangles.AddRange(Quad(new(1, 0, 0), new(1, top, 0), new(1, top, 2), new(1, 0, 2), -Vector3.UnitX));
            var upperNormal = Vector3.Normalize(Vector3.Cross(new Vector3(0, 0, 2), new Vector3(2, upperFarHeight - top, 0)));
            if (upperNormal.Y > 0) upperNormal = -upperNormal;
            triangles.AddRange(Quad(new(1, top, 0), new(3, upperFarHeight, 0), new(3, upperFarHeight, 2), new(1, top, 2), upperNormal));
            return new ZoneCollisionMesh(triangles);
        }

        [Fact]
        public void ALowRiser_BelowASlope_StopsTheFeet()
        {
            var mesh = Curb(upperFarHeight: -0.35f); // the upper floor rises on: not a level tread
            Assert.True(mesh.CrossesLowWall(new Vector3(0.95f, 0, 1), new Vector3(1.05f, 0, 1), StepUp, FootRadius, MaxDrop, out var normal, out float distance));
            Assert.Equal(-1.0f, normal.X, 3);
            Assert.Equal(0.05f, distance, 3);
        }

        [Fact]
        public void ALowRiser_BelowALevelTread_IsSteppedOver()
        {
            var mesh = Curb(upperFarHeight: -0.15f); // a level tread: the foot rounding lifts the feet onto its edge
            Assert.False(mesh.CrossesLowWall(new Vector3(0.95f, 0, 1), new Vector3(1.05f, 0, 1), StepUp, FootRadius, MaxDrop, out _, out _));
        }

        [Fact]
        public void TheRiser_IsTestedWhateverTheMoveLength()
        {
            var mesh = Curb(upperFarHeight: -0.35f);
            foreach (float length in new[] { 0.02f, 0.083f, 0.17f, 0.5f, 1.5f })
            {
                var start = new Vector3(1.0f - (length / 2), 0, 1);
                Assert.True(mesh.CrossesLowWall(start, start + new Vector3(length, 0, 0), StepUp, FootRadius, MaxDrop, out _, out _), $"move of {length}");
            }
        }

        [Fact]
        public void MovingAwayFromTheFace_OrAlongIt_IsFree()
        {
            var mesh = Curb(upperFarHeight: -0.35f);
            Assert.False(mesh.CrossesLowWall(new Vector3(0.95f, 0, 1), new Vector3(0.5f, 0, 1), StepUp, FootRadius, MaxDrop, out _, out _));
            Assert.False(mesh.CrossesLowWall(new Vector3(0.95f, 0, 0.5f), new Vector3(0.95f, 0, 1.5f), StepUp, FootRadius, MaxDrop, out _, out _));
        }

        [Fact]
        public void FacesAtSixtyDegreesOrSteeper_AreNeverGround()
        {
            // Southern San d'Oria's strip bevels: 59.9 degrees (normal Y -0.5017) is a floor, 60.0 (-0.49997) a wall.
            static ZoneCollisionMesh Slope(float normalY)
            {
                float run = 1.0f, rise = run * MathF.Sqrt(1 - (normalY * normalY)) / -normalY; // rise over run = tan(angle)
                var normal = Vector3.Normalize(new Vector3(-rise, -run, 0)); // facing -X and up
                return new ZoneCollisionMesh(new List<CollisionTriangle>(Quad(new(0, 0, 0), new(run, -rise, 0), new(run, -rise, 1), new(0, 0, 1), normal)));
            }

            Assert.True(Slope(-0.5017f).TryGetGround(new Vector3(0.2f, 0, 0.5f), StepUp, MaxDrop, out _));
            Assert.False(Slope(-0.49997f).TryGetGround(new Vector3(0.2f, 0, 0.5f), StepUp, MaxDrop, out _));
            Assert.Equal(ZoneCollisionMesh.MaxWallNormalY, ZoneCollisionMesh.MinFloorNormalY);
        }
    }
}
