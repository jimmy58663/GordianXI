// tests/Gordian.Core.Tests/Resources/ZoneRenderDataReleaseTests.cs
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>
    /// #576: once a zone is on the GPU, its CPU copy of what only the upload needed (mesh vertices and indices, its own
    /// texture pixels) is freed; what the runtime and the other renderers read stays.
    /// </summary>
    public class ZoneRenderDataReleaseTests
    {
        private static MeshGroup Quad(string name, string texture, bool water = false) => new()
        {
            Name = name,
            TextureName = texture,
            IsWater = water,
            Vertices =
            [
                new MeshVertex(new Vector3(0, 0, 0), Vector3.UnitY, Vector2.Zero, 0),
                new MeshVertex(new Vector3(1, 0, 0), Vector3.UnitY, Vector2.Zero, 0),
                new MeshVertex(new Vector3(1, 0, 1), Vector3.UnitY, Vector2.Zero, 0),
                new MeshVertex(new Vector3(0, 0, 1), Vector3.UnitY, Vector2.Zero, 0),
            ],
            Indices = [0, 1, 2, 0, 2, 3],
        };

        [Fact]
        public void MeshGroup_Release_KeepsCountsAndTheRayMesh()
        {
            var group = Quad("floor", "tex");
            group.ReleaseRenderData(keepRayMesh: true);

            Assert.True(group.IsRenderDataReleased);
            Assert.Empty(group.Vertices);
            Assert.Empty(group.Indices);
            Assert.Equal(4, group.VertexCount);
            Assert.Equal(2, group.TriangleCount);
            Assert.Equal(new Vector3(1, 0, 1), group.RayPositions![2]);
            Assert.Equal(new ushort[] { 0, 1, 2, 0, 2, 3 }, group.RayIndices);

            var water = Quad("sea", "umi", water: true);
            water.ReleaseRenderData(keepRayMesh: false);
            Assert.Null(water.RayPositions);
            Assert.Equal(2, water.TriangleCount);
        }

        [Fact]
        public void Zone_Release_FreesItsOwnMeshesTexturesAndVessel_KeepsSharedTextures()
        {
            var zone = new ZoneGeometry { ZoneId = 1, DatSource = "file100" };
            var floor = Quad("floor", "tex");
            var sea = Quad("sea", "umi", water: true);
            var platform = Quad("@lift", "tex");
            var door = Quad("_door", "tex");
            var hull = Quad("hull", "ship");
            zone.MeshGroups.Add(floor);
            zone.MeshGroups.Add(sea);
            zone.MovingPlatformGroups["@lift"] = [platform];
            zone.DoorLeaves["_door"] = [new DoorLeaf(0, default, [door])];
            zone.Vessel = new ZoneVessel();
            zone.Vessel.MeshGroups.Add(hull);

            var own = new DecodedTexture("tex", 2, 2, new byte[16]) { Source = "file100@64" };
            var shipTexture = new DecodedTexture("ship", 2, 2, new byte[16]) { Source = "file100@128" };
            var shared = new DecodedTexture("spark", 2, 2, new byte[16]) { Source = "file0@32" };
            var textures = new Dictionary<string, DecodedTexture> { ["tex"] = own, ["ship"] = shipTexture, ["spark"] = shared };

            zone.ReleaseRenderData(textures);

            Assert.True(zone.IsRenderDataReleased);
            Assert.True(floor.IsRenderDataReleased);
            Assert.NotNull(floor.RayPositions);
            Assert.True(sea.IsRenderDataReleased);
            Assert.Null(sea.RayPositions);      // water never occludes or floors
            Assert.True(platform.IsRenderDataReleased);
            Assert.True(door.IsRenderDataReleased);
            Assert.True(hull.IsRenderDataReleased);    // #585: a ship model at a dock reads its own copy of the ship zone
            Assert.Equal(8, zone.TotalVertices);
            Assert.Equal(4, zone.TotalTriangles);

            Assert.True(own.PixelsReleased);
            Assert.Empty(own.RgbaPixels);
            Assert.Equal(2, own.Width);
            Assert.True(shipTexture.PixelsReleased);
            Assert.False(shared.PixelsReleased);   // shared effect textures come from another DAT
        }

        [Fact]
        public void Interior_Release_WithoutTheRayMesh()
        {
            // #585: a building interior is released through its shared GPU copy; nothing ray-tests it.
            var interior = new ZoneGeometry { DatSource = "file200" };
            var wall = Quad("wall", "tex");
            interior.MeshGroups.Add(wall);
            var texture = new DecodedTexture("tex", 2, 2, new byte[16]) { Source = "file200@0" };
            interior.ReleaseRenderData(new Dictionary<string, DecodedTexture> { ["tex"] = texture }, keepRayMesh: false);
            Assert.True(wall.IsRenderDataReleased);
            Assert.Null(wall.RayPositions);
            Assert.True(texture.PixelsReleased);
        }

        [Fact]
        public void Furniture_Release_FreesItsMeshesAndOwnTextures()
        {
            // Item 1 (any id): its own textures come from FurnitureTable.ModelFileId(item).
            ushort item = 1;
            int fileId = Gordian.Core.Resources.Tables.FurnitureTable.ModelFileId(item);
            var mesh = Quad("bed", "tex");
            var own = new DecodedTexture("tex", 2, 2, new byte[16]) { Source = DecodedTexture.FileLabel(fileId) + "@0" };
            var other = new DecodedTexture("fx", 2, 2, new byte[16]) { Source = "file0@32" };
            var model = new Gordian.Core.Resources.Tables.FurnitureModel(item, [mesh], new Dictionary<string, DecodedTexture> { ["tex"] = own, ["fx"] = other });
            model.ReleaseRenderData();
            Assert.True(model.IsRenderDataReleased);
            Assert.True(mesh.IsRenderDataReleased);
            Assert.True(own.PixelsReleased);
            Assert.False(other.PixelsReleased);
        }

        [Fact]
        public void Zone_WithoutADatSource_KeepsItsTextures()
        {
            var zone = new ZoneGeometry { ZoneId = 2 };
            zone.MeshGroups.Add(Quad("floor", "tex"));
            var texture = new DecodedTexture("tex", 2, 2, new byte[16]) { Source = "file5@0" };
            zone.ReleaseRenderData(new Dictionary<string, DecodedTexture> { ["tex"] = texture });
            Assert.False(texture.PixelsReleased);
        }
    }
}
