// tests/Gordian.Engine.Tests/Graphics/ZoneRaycasterTests.cs
using System.Numerics;
using Gordian.Engine.Graphics;
using Gordian.Core.Resources.Models;
using Xunit;

namespace Gordian.Engine.Tests.Graphics
{
    public class ZoneRaycasterTests
    {
        private static MeshGroup Wall(float x, bool isWater = false) => new()
        {
            Vertices =
            [
                new MeshVertex(new Vector3(x, -10f, -10f), Vector3.UnitX, Vector2.Zero, 0),
                new MeshVertex(new Vector3(x, 10f, -10f), Vector3.UnitX, Vector2.Zero, 0),
                new MeshVertex(new Vector3(x, 0f, 10f), Vector3.UnitX, Vector2.Zero, 0),
            ],
            Indices = [0, 1, 2],
            MinBounds = new Vector3(x, -10f, -10f),
            MaxBounds = new Vector3(x, 10f, 10f),
            IsWater = isWater
        };

        private static MeshGroup Floor(float y, string environmentId) => new()
        {
            Vertices =
            [
                new MeshVertex(new Vector3(-10f, y, -10f), Vector3.UnitY, Vector2.Zero, 0),
                new MeshVertex(new Vector3(10f, y, -10f), Vector3.UnitY, Vector2.Zero, 0),
                new MeshVertex(new Vector3(0f, y, 10f), Vector3.UnitY, Vector2.Zero, 0),
            ],
            Indices = [0, 1, 2],
            MinBounds = new Vector3(-10f, y, -10f),
            MaxBounds = new Vector3(10f, y, 10f),
            EnvironmentId = environmentId
        };

        [Fact]
        public void ReleasedMeshes_AreRayTestedThroughTheKeptPositions()
        {
            // #576: once a zone is on the GPU its vertex arrays go; sun occlusion and floor tests read the kept positions.
            var zone = new ZoneGeometry();
            var wall = Wall(5f);
            var floor = Floor(-2f, string.Empty);
            zone.MeshGroups.Add(wall);
            zone.MeshGroups.Add(floor);
            wall.ReleaseRenderData(keepRayMesh: true);
            floor.ReleaseRenderData(keepRayMesh: true);
            Assert.Empty(wall.Vertices);

            Assert.True(ZoneRaycaster.IsOccluded(zone, Vector3.Zero, Vector3.UnitX, 100f));
            Assert.False(ZoneRaycaster.IsOccluded(zone, Vector3.Zero, -Vector3.UnitX, 100f));
            Assert.Same(floor, ZoneRaycaster.FindFloor(zone, Vector3.Zero, 10f));
        }

        [Fact]
        public void FindFloor_ReturnsTheNearestSurfaceBelow()
        {
            var zone = new ZoneGeometry();
            zone.MeshGroups.Add(Floor(-20f, "deep"));
            zone.MeshGroups.Add(Floor(-2f, "ev01"));
            zone.MeshGroups.Add(Floor(5f, "ceiling"));

            Assert.Equal("ev01", ZoneRaycaster.FindFloor(zone, Vector3.Zero, 500f)?.EnvironmentId);
            Assert.Null(ZoneRaycaster.FindFloor(zone, new Vector3(50f, 0f, 0f), 500f));
            Assert.Null(ZoneRaycaster.FindFloor(zone, Vector3.Zero, 1f));
        }

        [Fact]
        public void IsOccluded_HitsSolidGeometryInFrontWithinRange()
        {
            var zone = new ZoneGeometry();
            zone.MeshGroups.Add(Wall(10f));

            Assert.True(ZoneRaycaster.IsOccluded(zone, Vector3.Zero, Vector3.UnitX, 900f));
            Assert.False(ZoneRaycaster.IsOccluded(zone, Vector3.Zero, -Vector3.UnitX, 900f));
            Assert.False(ZoneRaycaster.IsOccluded(zone, Vector3.Zero, Vector3.UnitX, 5f));
            Assert.False(ZoneRaycaster.IsOccluded(zone, Vector3.Zero, Vector3.Normalize(new Vector3(1f, 5f, 0f)), 900f));
        }

        [Fact]
        public void IsOccluded_IgnoresWaterSurfaces()
        {
            var zone = new ZoneGeometry();
            zone.MeshGroups.Add(Wall(10f, isWater: true));

            Assert.False(ZoneRaycaster.IsOccluded(zone, Vector3.Zero, Vector3.UnitX, 900f));
        }
    }
}
