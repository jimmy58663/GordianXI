// tests/Gordian.Core.Tests/Resources/WeightedMeshDecoderTests.cs
using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>
    /// Section 0x25 weighted meshes (#204): the header, the morph targets with their packed normals, the per-vertex
    /// streams and the blend.
    /// </summary>
    public class WeightedMeshDecoderTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        /// <summary>
        /// One triangle, two morph targets of three positions and one normal each: target 1 lifts every position by 2 on Y.
        /// </summary>
        private static byte[] BuildPayload(byte flags = 0x02)
        {
            const int positions = 3, normals = 1, vertices = 3, targets = 2;
            int stride = positions * 12 + normals * 4;
            int colorOffset = 0x20 + targets * stride;
            int uvOffset = colorOffset + vertices * 4;
            int indexOffset = uvOffset + vertices * 8;
            var payload = new byte[indexOffset + vertices * 4];
            BinaryPrimitives.WriteUInt16LittleEndian(payload, 1);
            payload[2] = flags;
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4), positions);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6), normals);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8), (ushort)indexOffset);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(10), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(12), (ushort)colorOffset);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(14), (ushort)uvOffset);
            Encoding.ASCII.GetBytes("mabuta  nb").CopyTo(payload, 16);

            Vector3[] basePositions = { new(1, 0, 0), new(0, 1, 0), new(0, 0, 1) };
            for (int t = 0; t < targets; t++)
            {
                int at = 0x20 + t * stride;
                for (int i = 0; i < positions; i++)
                {
                    var p = basePositions[i] + new Vector3(0, 2 * t, 0);
                    BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(at + i * 12), p.X);
                    BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(at + i * 12 + 4), p.Y);
                    BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(at + i * 12 + 8), p.Z);
                }
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(at + positions * 12), 0x20000000); // -Z
            }
            for (int v = 0; v < vertices; v++)
            {
                payload[colorOffset + v * 4] = 0x10; // B
                payload[colorOffset + v * 4 + 1] = 0x20; // G
                payload[colorOffset + v * 4 + 2] = 0x40; // R
                payload[colorOffset + v * 4 + 3] = 0x80; // A
                BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(uvOffset + v * 8), v * 0.25f);
                BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(uvOffset + v * 8 + 4), 0.5f);
                BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(indexOffset + v * 2), (ushort)(2 - v));
                BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(indexOffset + (vertices + v) * 2), 0);
            }
            return payload;
        }

        [Fact]
        public void Decode_ReadsTargetsNormalsAndVertexStreams()
        {
            var mesh = WeightedMeshDecoder.Decode(BuildPayload(), "mb");

            Assert.NotNull(mesh);
            Assert.Equal("mabuta  nb", mesh.TextureName);
            Assert.False(mesh.AlphaDiscard);
            Assert.Equal(2, mesh.TargetCount);
            Assert.Equal(3, mesh.VertexCount);
            Assert.Equal(new Vector3(0, 3, 0), mesh.Positions[1][1]);
            Assert.Equal(new Vector3(0, 0, -1), mesh.Normals[0][0]);
            Assert.Equal(new ushort[] { 2, 1, 0 }, mesh.PositionIndices);
            Assert.Equal(new Vector2(0.5f, 0.5f), mesh.TexCoords[2]);
            // BGRA repacked as RGBA, doubled for a zone resource.
            Assert.Equal(0xFF204080u, mesh.Colors[0]);
        }

        [Fact]
        public void Decode_RejectsBadHeaders()
        {
            var payload = BuildPayload();
            payload[0] = 2;
            Assert.Null(WeightedMeshDecoder.Decode(payload, "mb"));
            Assert.Null(WeightedMeshDecoder.Decode(BuildPayload(flags: 0x00), "mb")); // no targets
            Assert.Null(WeightedMeshDecoder.Decode(BuildPayload().AsSpan(0, 0x40), "mb")); // truncated
            Assert.True(WeightedMeshDecoder.Decode(BuildPayload(flags: 0x82), "mb")!.AlphaDiscard);
        }

        [Fact]
        public void Blend_IsANormalizedWeightedSumInDisplaySpace()
        {
            var mesh = WeightedMeshDecoder.Decode(BuildPayload(), "mb")!;
            var output = new MeshVertex[mesh.VertexCount];

            // 1.5 / -0.5 reaches past target 0, as 51328's uw starts.
            mesh.Blend(new[] { 1.5f, -0.5f }, output);
            // Vertex 0 reads position 2, (0, 0, 1) on target 0 and (0, 2, 1) on target 1: (0, -1, 1) raw, display (-x, -y, z).
            Assert.Equal(new Vector3(0, 1, 1), output[0].Position);
            Assert.Equal(new Vector3(0, 0, -1), output[0].Normal);

            mesh.Blend(new[] { 0f, 1f }, output);
            Assert.Equal(new Vector3(0, -2, 1), output[0].Position);

            // Weights that sum past 1 keep the mesh's size (Alzadaal's fish reach 1.74 mid-stroke).
            mesh.Blend(new[] { 0.87f, 0.87f }, output);
            Assert.Equal(new Vector3(0, -1, 1), output[0].Position);
            // Weights summing to nothing draw the first target.
            mesh.Blend(new[] { 0f, 0f }, output);
            Assert.Equal(new Vector3(0, 0, 1), output[0].Position);
            Assert.Equal(mesh.Colors[0], output[0].ColorRgba);
            Assert.Equal(mesh.TexCoords[0], output[0].TexCoord);
        }

        [Theory]
        [InlineData(0x20000000u, 0f, 0f, -1f)]
        [InlineData(0x1FF00000u, 0f, 0f, 1f)]
        [InlineData(0x000001FFu, 1f, 0f, 0f)]
        [InlineData(0x00080000u, 0f, -1f, 0f)]
        public void UnpackNormal_ReadsSignedTenBitComponents(uint packed, float x, float y, float z)
        {
            Assert.Equal(new Vector3(x, y, z), WeightedMeshDecoder.UnpackNormal(packed));
        }

        /// <summary>
        /// Port Jeuno 324's blink mask (file 51402 <c>mb</c>): a 1 x 1 card in a 3 x 5 grid whose lid rows (0.19 and -0.25)
        /// close onto the middle row in the second target. Skipped without the game install.
        /// </summary>
        [Fact]
        public void RetailBlinkMask_ClosesItsLidRowsInTheSecondTarget()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var bytes = rm.LoadDatBytesByFileId(51402)!;
            var header = DatSectionWalker.ReadHeaders(bytes).Single(h => h.TypeCode == DatSectionType.WeightedMesh);
            var mesh = WeightedMeshDecoder.Decode(DatSectionWalker.GetSectionPayload(bytes, header).Span, header.DatId);

            Assert.NotNull(mesh);
            Assert.Equal("mabuta  nb", mesh.TextureName);
            Assert.Equal(2, mesh.TargetCount);
            Assert.Equal(48, mesh.VertexCount);
            Assert.Equal(0.19f, mesh.Positions[0][1].Y, 3);
            Assert.Equal(0f, mesh.Positions[1][1].Y, 3);
            Assert.Equal(-0.25f, mesh.Positions[0][3].Y, 3);
            Assert.Equal(0f, mesh.Positions[1][3].Y, 3);
            // The triangles tile the card exactly: area 1, none degenerate.
            float area = 0f;
            for (int v = 0; v < mesh.VertexCount; v += 3)
            {
                var a = mesh.Positions[0][mesh.PositionIndices[v]];
                var b = mesh.Positions[0][mesh.PositionIndices[v + 1]];
                var c = mesh.Positions[0][mesh.PositionIndices[v + 2]];
                float triangle = Vector3.Cross(b - a, c - a).Length() / 2f;
                Assert.True(triangle > 1e-4f);
                area += triangle;
            }
            Assert.Equal(1f, area, 3);
        }
    }
}
