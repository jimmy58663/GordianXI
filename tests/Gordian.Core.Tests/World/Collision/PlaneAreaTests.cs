using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.World.Collision;
using Xunit;

namespace Gordian.Core.Tests.World.Collision
{
    /// <summary>
    /// #610 round 5: <see cref="ZoneCollisionMesh.GetPlaneArea"/>, the flat plane a sloped floor triangle is part of.
    /// </summary>
    public class PlaneAreaTests
    {
        private static CollisionTriangle Face(Vector3 a, Vector3 b, Vector3 c, CollisionTerrain terrain = CollisionTerrain.Stone)
        {
            var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            if (normal.Y > 0) normal = -normal; // internal space: -Y is up
            return new CollisionTriangle(a, b, c, normal, false, terrain, false);
        }

        // A 30-degree quad 4 yalms deep (along +X) and 2 wide, rising toward +X: two triangles of 4.62 square yalms each.
        private static readonly float Top = -4.0f * System.MathF.Tan(30.0f * System.MathF.PI / 180.0f);
        private static readonly Vector3 A = new(0, 0, 0), B = new(4, Top, 0), C = new(4, Top, 2), D = new(0, 0, 2);
        private static readonly float QuadArea = 4.0f / System.MathF.Cos(30.0f * System.MathF.PI / 180.0f) * 2.0f;

        [Fact]
        public void TwoCoplanarTriangles_ShareTheirPlanesArea()
        {
            var mesh = new ZoneCollisionMesh(new List<CollisionTriangle> { Face(A, B, C), Face(A, C, D) });
            Assert.Equal(QuadArea, mesh.GetPlaneArea(0), 3);
            Assert.Equal(QuadArea, mesh.GetPlaneArea(1), 3);
        }

        [Fact]
        public void ALoneTriangle_OrItsExactDuplicate_IsNoPlane()
        {
            var mesh = new ZoneCollisionMesh(new List<CollisionTriangle> { Face(A, B, C), Face(A, B, C), Face(C, A, B) });
            Assert.Equal(0.0f, mesh.GetPlaneArea(0));
            Assert.Equal(0.0f, mesh.GetPlaneArea(1));
            Assert.Equal(0.0f, mesh.GetPlaneArea(2));
        }

        [Fact]
        public void ADuplicate_IsCountedOnce_AndJoinsItsPlane()
        {
            var mesh = new ZoneCollisionMesh(new List<CollisionTriangle> { Face(A, B, C), Face(A, C, D), Face(A, B, C) });
            Assert.Equal(QuadArea, mesh.GetPlaneArea(0), 3);
            Assert.Equal(QuadArea, mesh.GetPlaneArea(2), 3);
        }

        [Fact]
        public void TrianglesAFewDegreesApart_AreSeparate()
        {
            var raised = D with { Y = -0.3f }; // the second triangle tilts several degrees away from the first
            var mesh = new ZoneCollisionMesh(new List<CollisionTriangle> { Face(A, B, C), Face(A, C, raised) });
            Assert.Equal(0.0f, mesh.GetPlaneArea(0));
            Assert.Equal(0.0f, mesh.GetPlaneArea(1));
        }

        [Fact]
        public void GentleFloors_AndWalls_HaveNoPlaneArea()
        {
            var flat = new ZoneCollisionMesh(new List<CollisionTriangle>
            {
                Face(new(0, 0, 0), new(4, 0, 0), new(4, 0, 2)),
                Face(new(0, 0, 0), new(4, 0, 2), new(0, 0, 2)),
                Face(new(0, 0, 0), new(0, -4, 0), new(0, -4, 2)),
                Face(new(0, 0, 0), new(0, -4, 2), new(0, 0, 2)),
            });
            for (int i = 0; i < 4; i++) Assert.Equal(0.0f, flat.GetPlaneArea(i));
            Assert.Equal(0.0f, flat.GetPlaneArea(-1));
            Assert.Equal(0.0f, flat.GetPlaneArea(99));
        }
    }
}
