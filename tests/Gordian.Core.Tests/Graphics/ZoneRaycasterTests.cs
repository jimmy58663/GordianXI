// tests/Gordian.Core.Tests/Graphics/ZoneRaycasterTests.cs
using System.Numerics;
using Gordian.Core.Graphics;
using Gordian.Core.Resources.Models;
using Xunit;

namespace Gordian.Core.Tests.Graphics
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
