// tests/Gordian.Core.Tests/Resources/ZoneWaterGeneratorTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class ZoneWaterGeneratorTests
    {
        [Theory]
        [InlineData("shi1", true)]
        [InlineData("shi2", true)]
        [InlineData("shi5", true)]
        [InlineData("hum1", true)]
        [InlineData("humt", true)]
        [InlineData("mizu", true)]
        [InlineData("hna0", true)]
        [InlineData("tree", false)]
        [InlineData("rock", false)]
        public void IsWaterGenerator_IdentifiesWaterParticleGenerators(string name, bool expected)
        {
            bool actual = ZoneDataLoader.IsWaterGenerator(name);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void ParseZoneContainer_Phase2bInstantiatesWaterGeneratorsWithUVScroll()
        {
            // Build synthetic container with:
            // 1. Template mesh (Section 0x2E) named "rip1" with texture "umi1"
            // 2. Section 0x05 Particle Generator named "shi1" linking to "rip1" with UV scroll <-0.01, -0.03>
            byte[] meshPayload = BuildSyntheticZoneMeshPayload("rip1", "umi1");
            byte[] meshSection = BuildChunk(DatSectionType.ZoneMesh, meshPayload, "rip1");

            byte[] genPayload = BuildSyntheticWaterGeneratorPayload("shi1", "rip1", new Vector3(10f, 0f, 20f), new Vector2(-0.01f, -0.03f));
            byte[] genSection = BuildChunk(DatSectionType.ParticleGenerator, genPayload, "shi1");

            byte[] container = new byte[meshSection.Length + genSection.Length];
            meshSection.CopyTo(container, 0);
            genSection.CopyTo(container, meshSection.Length);

            var textures = new Dictionary<string, DecodedTexture>(StringComparer.OrdinalIgnoreCase);
            var zone = ZoneDataLoader.ParseZoneContainer(container, zoneId: 4, outTextures: textures);

            Assert.NotNull(zone);
            Assert.NotEmpty(zone.MeshGroups);

            var waterGroup = zone.MeshGroups.Find(g => g.IsWater);
            Assert.NotNull(waterGroup);
            Assert.True(waterGroup.IsBlend);
            Assert.True(waterGroup.NoCull);
            Assert.Equal("umi1", waterGroup.TextureName);
            Assert.Equal(new Vector2(-0.01f, -0.03f), waterGroup.UVScroll);
        }

        private static byte[] BuildSyntheticZoneMeshPayload(string meshName, string texName)
        {
            int sm = 0x20;
            int numVerts = 3;
            int vertStride = 36;
            int numIndices = 3;

            int totalSize = sm + 20 + (numVerts * vertStride) + 4 + (numIndices * 2) + 16;
            byte[] payload = new byte[totalSize];

            Encoding.ASCII.GetBytes(meshName).CopyTo(payload.AsSpan(0x10, Math.Min(16, meshName.Length)));
            Encoding.ASCII.GetBytes(texName).CopyTo(payload.AsSpan(sm, Math.Min(16, texName.Length)));

            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(sm + 16, 2), (ushort)numVerts);

            int vStart = sm + 20;
            for (int v = 0; v < numVerts; v++)
            {
                int vo = vStart + (v * vertStride);
                BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(vo, 4), v * 5.0f);
                BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(vo + 4, 4), 0.0f);
                BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(vo + 8, 4), v * 5.0f);
            }

            int idxHeader = vStart + (numVerts * vertStride);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(idxHeader, 2), (ushort)numIndices);

            int idxStart = idxHeader + 4;
            for (int i = 0; i < numIndices; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(idxStart + (i * 2), 2), (ushort)i);
            }

            return payload;
        }

        private static byte[] BuildSyntheticWaterGeneratorPayload(string datId, string linkedId, Vector3 basePos, Vector2 uvScroll)
        {
            // Generator payload:
            // Header (128 bytes): +0x48 = DatId (4 chars), +0x70 = Section 1 stream offset, +0x78 = Section 3 stream offset
            // Opcode 0x01 (StandardParticleSetup): config dword (op 0x01, len 10 dwords = 40B), +0x0C=linkedId(4), +0x14=pos(12), +0x21=linkedType(1)
            int op1Offset = 128;
            int op27Offset = op1Offset + 40;
            int op28Offset = op27Offset + 8;

            byte[] payload = new byte[128 + 40 + 8 + 8];
            Encoding.ASCII.GetBytes(datId.PadRight(4).Substring(0, 4)).CopyTo(payload.AsSpan(0x48));

            // Stream offsets relative to section start (including 16B chunk header):
            // streamOffsets[1] = Section 2 (Initializers) at 16 + op1Offset
            // streamOffsets[2] = Section 3 (Particle Updaters) at 16 + op27Offset
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x74, 4), (uint)(16 + op1Offset));
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x78, 4), (uint)(16 + op27Offset));

            // Opcode 0x01: 40 bytes = 10 dwords
            uint op1Config = 0x01 | ((uint)10 << 8);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(op1Offset, 4), op1Config);
            Encoding.ASCII.GetBytes(linkedId.PadRight(4).Substring(0, 4)).CopyTo(payload.AsSpan(op1Offset + 12));
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(op1Offset + 20, 4), basePos.X);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(op1Offset + 24, 4), basePos.Y);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(op1Offset + 28, 4), basePos.Z);
            payload[op1Offset + 33] = 1; // Mesh

            // Opcode 0x27: Axis U scroll, 8 bytes = 2 dwords
            uint op27Config = 0x27 | ((uint)2 << 8);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(op27Offset, 4), op27Config);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(op27Offset + 4, 4), uvScroll.X);

            // Opcode 0x28: Axis V scroll, 8 bytes = 2 dwords
            uint op28Config = 0x28 | ((uint)2 << 8);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(op28Offset, 4), op28Config);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(op28Offset + 4, 4), uvScroll.Y);

            return payload;
        }

        private static byte[] BuildChunk(DatSectionType type, byte[] payload, string datId = "test")
        {
            int total = (16 + payload.Length + 15) & ~15;
            byte[] chunk = new byte[total];

            Encoding.ASCII.GetBytes(datId.PadRight(4).Substring(0, 4)).CopyTo(chunk.AsSpan(0, 4));
            uint units = (uint)(total / 16);
            uint meta = ((uint)type & 0x7F) | ((units & 0x7FFFF) << 7);
            BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4, 4), meta);

            payload.CopyTo(chunk.AsSpan(16));
            return chunk;
        }
    }
}
