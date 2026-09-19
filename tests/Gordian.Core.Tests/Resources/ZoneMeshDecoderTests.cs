// tests/Gordian.Core.Tests/Resources/ZoneMeshDecoderTests.cs
using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Gordian.Core.Resources.Graphics;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class ZoneMeshDecoderTests
    {
        [Fact]
        public void ZoneMeshDecoder_ParseZoneMesh_ParsesSubmeshAndBounds()
        {
            // Build a synthetic unencrypted Section 0x2E payload
            // Header at 0..31 (name "zone_ground")
            // Submesh at offset 0x20:
            // Texture name: "ground.png" (16 bytes)
            // numVerts = 3 (ushort at +16) + pad 2 bytes = 20 bytes
            // 3 vertices * 36 bytes = 108 bytes
            // Index header: numIndices = 3 (ushort) + pad 2 bytes = 4 bytes
            // 3 indices * 2 bytes = 6 bytes
            // Total submesh size = 20 + 108 + 4 + 6 = 138 bytes

            int totalSize = 0x20 + 138 + 16;
            byte[] payload = new byte[totalSize];

            Encoding.ASCII.GetBytes("zone_ground").CopyTo(payload.AsSpan(0x10, 11));

            int sm = 0x20;
            Encoding.ASCII.GetBytes("ground.png").CopyTo(payload.AsSpan(sm, 10));
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(sm + 16, 2), 3); // 3 vertices

            int vStart = sm + 20;
            // Vertex 0: Position (0, 0, 0)
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(vStart, 4), 0.0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(vStart + 4, 4), 0.0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(vStart + 8, 4), 0.0f);

            // Vertex 1: Position (10, 0, 0)
            int v1 = vStart + 36;
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(v1, 4), 10.0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(v1 + 4, 4), 0.0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(v1 + 8, 4), 0.0f);

            // Vertex 2: Position (0, 5, 20)
            int v2 = vStart + 72;
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(v2, 4), 0.0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(v2 + 4, 4), 5.0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(v2 + 8, 4), 20.0f);

            // Index header at vStart + 108
            int idxHeader = vStart + 108;
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(idxHeader, 2), 3); // 3 indices

            int idxStart = idxHeader + 4;
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(idxStart, 2), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(idxStart + 2, 2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(idxStart + 4, 2), 2);

            var groups = ZoneMeshDecoder.ParseZoneMesh(payload);

            Assert.Single(groups);
            var group = groups[0];
            Assert.Equal("zone_ground", group.Name);
            Assert.Equal("ground.png", group.TextureName);
            Assert.Equal(3, group.Vertices.Length);
            Assert.Equal(3, group.Indices.Length);
            Assert.Equal(1, group.TriangleCount);

            // Verify bounding box calculation in native zone coordinates (x, y, z)
            Assert.Equal(new Vector3(0.0f, 0.0f, 0.0f), group.MinBounds);
            Assert.Equal(new Vector3(10.0f, 5.0f, 20.0f), group.MaxBounds);
        }

        [Fact]
        public void ZoneMeshDecoder_DecryptZoneMesh_ExecutesStreamCipher()
        {
            byte[] table1 = new byte[256];
            byte[] table2 = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                table1[i] = (byte)i;
                table2[i] = (byte)(255 - i);
            }

            // Construct minimal encrypted payload: mode = 5, totalSize = 32
            byte[] payload = new byte[32];
            uint metadata = 32u | (5u << 24); // mode 5
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), metadata);
            payload[5] = 0xA5; // seed

            byte[] original = (byte[])payload.Clone();

            // Run decryption
            ZoneMeshDecoder.DecryptZoneMesh(payload, table1, table2);

            // Decrypted payload bytes 8..31 should be modified by the stream cipher
            bool changed = false;
            for (int i = 8; i < 32; i++)
            {
                if (payload[i] != original[i]) changed = true;
            }

            Assert.True(changed);
        }
    }
}
