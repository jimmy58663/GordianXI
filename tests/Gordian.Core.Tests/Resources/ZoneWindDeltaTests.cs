// tests/Gordian.Core.Tests/Resources/ZoneWindDeltaTests.cs
using System;
using System.Numerics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>
    /// A placed swaying mesh (#606) keeps its wind offsets as directions: rotated and scaled with the placement, never
    /// moved, then taken to display space (-x, -y, z) like its normals.
    /// </summary>
    public class ZoneWindDeltaTests
    {
        [Fact]
        public void InstantiateSubmesh_TurnsAndScalesTheWindOffsetsWithoutMovingThem()
        {
            var template = new MeshGroup
            {
                Name = "_kusa_001_m",
                Vertices = [new MeshVertex(Vector3.Zero, Vector3.UnitY, Vector2.Zero, 0x80808080)],
                Indices = [0, 0, 0],
                WindDeltas = [new Vector3(0.1f, 0f, 0f)]
            };
            // Turned 90 degrees about Y, doubled, moved far away.
            var transform = ZoneDefDecoder.CreateTrsMatrix(new Vector3(100f, 5f, -40f), new Vector3(0f, MathF.PI / 2, 0f), new Vector3(2f));
            var placed = ZoneDefDecoder.InstantiateSubmesh(template, transform, "p");

            Assert.NotNull(placed.WindDeltas);
            var raw = Vector3.TransformNormal(new Vector3(0.1f, 0f, 0f), transform);
            var expected = new Vector3(-raw.X, -raw.Y, raw.Z);
            Assert.Equal(expected.X, placed.WindDeltas![0].X, 4);
            Assert.Equal(expected.Y, placed.WindDeltas[0].Y, 4);
            Assert.Equal(expected.Z, placed.WindDeltas[0].Z, 4);
            Assert.Equal(0.2f, placed.WindDeltas[0].Length(), 4);
        }

        [Fact]
        public void InstantiateSubmesh_LeavesAStillMeshWithoutOffsets()
        {
            var template = new MeshGroup { Name = "rock", Vertices = [new MeshVertex(Vector3.Zero, Vector3.UnitY, Vector2.Zero, 0)], Indices = [0, 0, 0] };
            Assert.Null(ZoneDefDecoder.InstantiateSubmesh(template, Matrix4x4.Identity, "p").WindDeltas);
        }
    }
}
