// tests/Gordian.Core.Tests/Resources/WeatherSkyDecoderTests.cs
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
    public class WeatherSkyDecoderTests
    {
        private static byte[] BuildChunk(DatSectionType type, byte[] payload, string datId = "test")
        {
            int total = (16 + payload.Length + 15) & ~15;
            byte[] chunk = new byte[total];

            string padId = (datId + "    ").Substring(0, 4);
            Encoding.ASCII.GetBytes(padId).CopyTo(chunk.AsSpan(0, 4));

            uint units = (uint)(total / 16);
            uint meta = ((uint)type & 0x7F) | ((units & 0x7FFFF) << 7);
            BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4, 4), meta);

            payload.CopyTo(chunk.AsSpan(16));
            return chunk;
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
                BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(vo, 4), v * 10.0f);
                BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(vo + 4, 4), 100.0f);
                BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(vo + 8, 4), v * 10.0f);
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

        private static byte[] BuildSyntheticGeneratorPayload(
            string linkedMeshId,
            Vector2 uvScroll,
            ParticleAttachType attachType = ParticleAttachType.None)
        {
            byte[] payload = new byte[0x100];

            ushort attachFlags = (ushort)((byte)attachType & 0x0F);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0x00, 2), attachFlags);

            uint sec1Offset = 0;
            uint sec2Offset = 0x80 + 16;
            uint sec3Offset = 0xB0 + 16;
            uint sec4Offset = 0;

            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x70, 4), sec1Offset);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x74, 4), sec2Offset);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x78, 4), sec3Offset);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x7C, 4), sec4Offset);

            // Stream 2: Opcode 0x01 StandardParticleSetup (40 bytes at 0x80)
            uint op01Config = 0x01 | (10u << 8);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x80, 4), op01Config);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0x84, 2), 0x0004); // FollowCamera
            string padMesh = (linkedMeshId + "    ").Substring(0, 4);
            Encoding.ASCII.GetBytes(padMesh).CopyTo(payload.AsSpan(0x8C, 4));
            payload[0xA1] = (byte)ParticleLinkedDataType.StaticMesh;

            // Stream 3: Opcode 0x27 & 0x28 (UV scroll at 0xB0)
            uint op27Config = 0x27 | (2u << 8);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0xB0, 4), op27Config);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(0xB4, 4), uvScroll.X);

            uint op28Config = 0x28 | (2u << 8);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0xB8, 4), op28Config);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(0xBC, 4), uvScroll.Y);

            return payload;
        }

        private static byte[] CombineChunks(params byte[][] chunks)
        {
            int total = 0;
            for (int i = 0; i < chunks.Length; i++) total += chunks[i].Length;
            byte[] combined = new byte[total];
            int p = 0;
            for (int i = 0; i < chunks.Length; i++)
            {
                chunks[i].CopyTo(combined.AsSpan(p));
                p += chunks[i].Length;
            }
            return combined;
        }

        [Fact]
        public void ParseZoneContainer_ExtractsWeatherSkyLayersAndPairsWithGenerators()
        {
            // Directory structure:
            // [DIR weat]
            //   [DIR fine]
            //     [0x05 Generator "gen1" -> links to "clod" with UV drift]
            //     [0x2E ZoneMesh "clod_a01" tex "cld_fine.png"]
            //   [END]
            // [END]

            byte[] dirWeat = BuildChunk(DatSectionType.Directory, Array.Empty<byte>(), "weat");
            byte[] dirFine = BuildChunk(DatSectionType.Directory, Array.Empty<byte>(), "fine");
            byte[] genChunk = BuildChunk(DatSectionType.ParticleGenerator,
                BuildSyntheticGeneratorPayload("clod", new Vector2(0.008f, -0.003f)), "gen1");
            byte[] meshChunk = BuildChunk(DatSectionType.ZoneMesh,
                BuildSyntheticZoneMeshPayload("clod_a01", "cld_fine.png"), "clod");
            byte[] endFine = BuildChunk(DatSectionType.End, Array.Empty<byte>(), "");
            byte[] endWeat = BuildChunk(DatSectionType.End, Array.Empty<byte>(), "");

            byte[] container = CombineChunks(dirWeat, dirFine, genChunk, meshChunk, endFine, endWeat);

            var zone = ZoneDataLoader.ParseZoneContainer(container, zoneId: 100);

            Assert.NotNull(zone);
            Assert.NotNull(zone.EnvironmentData);
            Assert.Single(zone.WeatherSkyLayers);

            var layer = zone.WeatherSkyLayers[0];
            Assert.Equal("clod_a01", layer.Name);
            Assert.Equal("fine", layer.WeatherId);
            Assert.False(layer.IsCelestial);
            Assert.Equal(0.008f, layer.UVScroll.X, 0.0001f);
            Assert.Equal(-0.003f, layer.UVScroll.Y, 0.0001f);
            Assert.Equal("cld_fine.png", layer.TextureName);
            Assert.True(layer.FollowCamera);
            Assert.NotEmpty(layer.MeshGroups);
        }

        [Fact]
        public void ParseZoneContainer_SunMeshWithoutSunGeneratorIsNotDrawn()
        {
            // [DIR weat]
            //   [DIR fine]
            //     [0x2E ZoneMesh "sun" tex "sun.png"]
            //   [END]
            //   [DIR rain]
            //     [0x2E ZoneMesh "sun" tex "sun.png"]
            //   [END]
            // [END]

            byte[] dirWeat = BuildChunk(DatSectionType.Directory, Array.Empty<byte>(), "weat");
            byte[] dirFine = BuildChunk(DatSectionType.Directory, Array.Empty<byte>(), "fine");
            byte[] sun1 = BuildChunk(DatSectionType.ZoneMesh,
                BuildSyntheticZoneMeshPayload("sun", "sun.png"), "sun");
            byte[] endFine = BuildChunk(DatSectionType.End, Array.Empty<byte>(), "");

            byte[] dirRain = BuildChunk(DatSectionType.Directory, Array.Empty<byte>(), "rain");
            byte[] sun2 = BuildChunk(DatSectionType.ZoneMesh,
                BuildSyntheticZoneMeshPayload("sun", "sun.png"), "sun");
            byte[] endRain = BuildChunk(DatSectionType.End, Array.Empty<byte>(), "");
            byte[] endWeat = BuildChunk(DatSectionType.End, Array.Empty<byte>(), "");

            byte[] container = CombineChunks(dirWeat, dirFine, sun1, endFine, dirRain, sun2, endRain, endWeat);

            var zone = ZoneDataLoader.ParseZoneContainer(container, zoneId: 101);

            // The sun is generator-driven: a sun mesh that no Sun-attached generator draws produces no sky layer.
            Assert.NotNull(zone);
            Assert.Empty(zone.WeatherSkyLayers);
        }
    }
}
