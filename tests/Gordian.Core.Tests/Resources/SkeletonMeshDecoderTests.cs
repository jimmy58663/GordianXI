// tests/Gordian.Core.Tests/Resources/SkeletonMeshDecoderTests.cs
using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class SkeletonMeshDecoderTests
    {
        [Fact]
        public void SkeletonMeshDecoder_DecodeMesh_ParsesVerticesAndTriangleList()
        {
            // Build synthetic Section 0x2A payload
            // Header: 42 bytes minimum
            // Flags: flags3 = 0 (cloth=false -> hasNormals=true, useJointArray=false)
            // numJoints = 0
            // vertexCountsOffset = 42
            // vertexCounts: single=1, double=1 -> total=2 (4 bytes)
            // vertexJointMappingOffset = 46
            // joint mappings: 2 verts * 4 bytes = 8 bytes (offset 46..54)
            // vertexDataOffset = 54
            // vertex data:
            //   single: 12 bytes pos + 12 bytes norm = 24 bytes
            //   double: 32 bytes pos/weights + 24 bytes norm = 56 bytes
            //   total vertex data = 80 bytes (offset 54..134)
            // instructionOffset = 134
            // instructions:
            //   0x8000 texture name "body_tex" (2 + 16 = 18 bytes)
            //   0x0054 tri list with 1 tri (2 + 2 + 6 + 24 = 34 bytes)
            //   0xFFFF end (2 bytes)
            //   total instructions = 54 bytes (offset 134..188)

            int totalSize = 188;
            byte[] payload = new byte[totalSize];

            // Header offsets
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(6, 4), 134 / 2); // instructionOffset (word)
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12, 4), 0);       // jointArrayOffset
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(16, 2), 0);     // numJoints
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(18, 4), 42 / 2);  // vertexCountsOffset (word)
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(22, 2), 2);     // numVertexCounts = 2
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(24, 4), 46 / 2);  // vertexJointMappingOffset (word)
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(30, 4), 54 / 2);  // vertexDataOffset (word)

            // Vertex counts at 42
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(42, 2), 1); // 1 single
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(44, 2), 1); // 1 double

            // Joint mapping at 46
            // Vert 0 (single): joint0 = 3
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(46, 2), 3);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(48, 2), 0);
            // Vert 1 (double): joint0 = 3, joint1 = 5
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(50, 2), 3);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(52, 2), 5);

            // Vertex data at 54
            // Vert 0: Position (1, 2, 3), Normal (0, 1, 0)
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(54, 4), 1f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(58, 4), 2f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(62, 4), 3f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(66, 4), 0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(70, 4), 1f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(74, 4), 0f);

            // Vert 1 (double at 78):
            // p0=(2, 0, 0), p1=(0, 4, 0), w0=0.6, w1=0.4
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(78, 4), 2f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(82, 4), 0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(86, 4), 0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(90, 4), 4f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(94, 4), 0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(98, 4), 0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(102, 4), 0.6f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(106, 4), 0.4f);

            // Normals at 110:
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(110, 4), 0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(114, 4), 1f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(118, 4), 0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(122, 4), 0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(126, 4), 1f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(130, 4), 0f);

            // Instructions at 134
            // 0x8000: Texture name "body_tex"
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(134, 2), 0x8000);
            Encoding.ASCII.GetBytes("body_tex").CopyTo(payload.AsSpan(136, 8));

            // 0x0054: Triangle List with 1 tri at 152
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(152, 2), 0x0054);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(154, 2), 1); // 1 tri
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(156, 2), 0); // i0
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(158, 2), 1); // i1
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(160, 2), 0); // i2
            // UVs (6 floats = 24 bytes) at 162
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(162, 4), 0.1f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(166, 4), 0.2f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(170, 4), 0.3f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(174, 4), 0.4f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(178, 4), 0.5f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(182, 4), 0.6f);

            // 0xFFFF terminator at 186
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(186, 2), 0xFFFF);

            var group = SkeletonMeshDecoder.DecodeMesh(payload, "test/path.dat");
            Assert.NotNull(group);
            Assert.Equal(2, group.Vertices.Length);

            // Vert 0
            Assert.Equal(new Vector3(1f, 2f, 3f), group.Vertices[0].Position0);
            Assert.Equal(3, group.Vertices[0].Joint0);
            Assert.Equal(-1, group.Vertices[0].Joint1);

            // Vert 1
            Assert.Equal(new Vector3(2f, 0f, 0f), group.Vertices[1].Position0);
            Assert.Equal(new Vector3(0f, 4f, 0f), group.Vertices[1].Position1);
            Assert.Equal(0.6f, group.Vertices[1].Weight0);
            Assert.Equal(0.4f, group.Vertices[1].Weight1);
            Assert.Equal(3, group.Vertices[1].Joint0);
            Assert.Equal(5, group.Vertices[1].Joint1);

            // Piece
            Assert.Single(group.Pieces);
            var piece = group.Pieces[0];
            Assert.Equal(MeshTopology.TriangleList, piece.Topology);
            Assert.Equal("body_tex", piece.TextureName);
            Assert.Equal(3, piece.Corners.Length);
            Assert.Equal(0, piece.Corners[0].VertexIndex);
            Assert.Equal(1, piece.Corners[1].VertexIndex);
            Assert.Equal(new Vector2(0.1f, 0.2f), piece.Corners[0].TexCoord);
        }

        [Fact]
        public void SkeletonMeshDecoder_DecodeMesh_RejectsTruncatedPayload()
        {
            byte[] truncated = new byte[20];
            Assert.Null(SkeletonMeshDecoder.DecodeMesh(truncated));
        }
    }
}
