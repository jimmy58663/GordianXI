// tests/Gordian.Core.Tests/Resources/ZoneDataLoaderTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Graphics;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class ZoneDataLoaderTests
    {
        [Theory]
        [InlineData(0, 100)]
        [InlineData(44, 144)]
        [InlineData(255, 355)]
        [InlineData(256, 0x147B3)]
        [InlineData(257, 0x147B4)]
        public void GetZoneModelFileId_ReturnsAccurateFileId(int zoneId, int expectedFileId)
        {
            int actual = ZoneDataLoader.GetZoneModelFileId(zoneId);
            Assert.Equal(expectedFileId, actual);
        }

        [Fact]
        public void ParseZoneContainer_ParsesTextureAndMeshSections()
        {
            // Build synthetic container with two sections:
            // 1. Texture (Section 0x20)
            // 2. ZoneMesh (Section 0x2E)

            // Section 1: Texture (0x20)
            // Header: 16 bytes. SectionType = 0x20.
            // Payload: Texture header (0x01, name "sand_dune.png", 2x2 32-bit RGBA)
            byte[] texPayload = BuildSyntheticTexturePayload("sand_dune.png", 2, 2);
            byte[] texSection = BuildChunk(DatSectionType.Texture, texPayload);

            // Section 2: ZoneMesh (0x2E)
            byte[] meshPayload = BuildSyntheticZoneMeshPayload("zone_dune", "sand_dune.png");
            byte[] meshSection = BuildChunk(DatSectionType.ZoneMesh, meshPayload);

            byte[] container = new byte[texSection.Length + meshSection.Length];
            texSection.CopyTo(container, 0);
            meshSection.CopyTo(container, texSection.Length);

            var textures = new Dictionary<string, DecodedTexture>(StringComparer.OrdinalIgnoreCase);
            var zone = ZoneDataLoader.ParseZoneContainer(container, zoneId: 10, outTextures: textures);

            Assert.NotNull(zone);
            Assert.Equal(10, zone.ZoneId);
            Assert.Single(zone.MeshGroups);
            Assert.Equal("zone_dune", zone.MeshGroups[0].Name);
            Assert.Equal("sand_dune.png", zone.MeshGroups[0].TextureName);
            Assert.True(textures.ContainsKey("sand_dune.png"));
            Assert.Equal(2, textures["sand_dune.png"].Width);
            Assert.Equal(2, textures["sand_dune.png"].Height);
        }

        [Fact]
        public void TryExtractFromDllBytes_FindsSignaturesAndExtractsTables()
        {
            // Create simulated DLL buffer with Table1Sig and Table2Sig after 0x30000
            byte[] dllBytes = new byte[0x32000];

            int offset1 = 0x30100;
            ZoneMeshDecoder.Table1Sig.CopyTo(dllBytes.AsSpan(offset1, 4));
            for (int i = 4; i < 256; i++) dllBytes[offset1 + i] = (byte)i;

            int offset2 = 0x30500;
            ZoneMeshDecoder.Table2Sig.CopyTo(dllBytes.AsSpan(offset2, 4));
            for (int i = 4; i < 256; i++) dllBytes[offset2 + i] = (byte)(255 - i);

            bool success = ZoneDataLoader.TryExtractFromDllBytes(dllBytes, out var table1, out var table2);

            Assert.True(success);
            Assert.Equal(256, table1.Length);
            Assert.Equal(256, table2.Length);
            Assert.Equal(ZoneMeshDecoder.Table1Sig, table1[0..4]);
            Assert.Equal(ZoneMeshDecoder.Table2Sig, table2[0..4]);
        }

        [Fact]
        public void TryExtractFromDllBytes_FindsSignaturesBeforeScanStartViaFallback()
        {
            // Simulate signatures located before 0x30000 (e.g. at 0x1000 and 0x2000)
            byte[] dllBytes = new byte[0x10000];

            int offset1 = 0x1000;
            ZoneMeshDecoder.Table1Sig.CopyTo(dllBytes.AsSpan(offset1, 4));
            for (int i = 4; i < 256; i++) dllBytes[offset1 + i] = (byte)i;

            int offset2 = 0x2000;
            ZoneMeshDecoder.Table2Sig.CopyTo(dllBytes.AsSpan(offset2, 4));
            for (int i = 4; i < 256; i++) dllBytes[offset2 + i] = (byte)(255 - i);

            bool success = ZoneDataLoader.TryExtractFromDllBytes(dllBytes, out var table1, out var table2);

            Assert.True(success);
            Assert.Equal(256, table1.Length);
            Assert.Equal(256, table2.Length);
            Assert.Equal(ZoneMeshDecoder.Table1Sig, table1[0..4]);
            Assert.Equal(ZoneMeshDecoder.Table2Sig, table2[0..4]);
        }

        [Fact]
        public void ParseZoneContainer_RegistersTrimmedAndShortTextureNames()
        {
            // A texture named "tim     sn_01_a" (16 bytes)
            string compoundName = "tim     sn_01_a";
            byte[] texPayload = BuildSyntheticTexturePayload(compoundName, 4, 4);
            byte[] texSection = BuildChunk(DatSectionType.Texture, texPayload);

            var textures = new Dictionary<string, DecodedTexture>(StringComparer.OrdinalIgnoreCase);
            ZoneDataLoader.ParseZoneContainer(texSection, zoneId: 1, outTextures: textures);

            // Verifying full name, trimmed name, and short name (suffix)
            Assert.True(textures.ContainsKey(compoundName));
            Assert.True(textures.ContainsKey("sn_01_a"));
        }

        private static byte[] BuildChunk(DatSectionType type, byte[] payload)
        {
            int total = (16 + payload.Length + 15) & ~15;
            byte[] chunk = new byte[total];

            Encoding.ASCII.GetBytes("test").CopyTo(chunk.AsSpan(0, 4));
            uint units = (uint)(total / 16);
            uint meta = ((uint)type & 0x7F) | ((units & 0x7FFFF) << 7);
            BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4, 4), meta);

            payload.CopyTo(chunk.AsSpan(16));
            return chunk;
        }

        private static byte[] BuildSyntheticTexturePayload(string name, int w, int h)
        {
            int payloadSize = 64 + (w * h * 4);
            byte[] payload = new byte[payloadSize];

            payload[0] = 0x01; // uncompressed / 32bpp
            byte[] nameBytes = Encoding.ASCII.GetBytes(name);
            int copyLen = Math.Min(16, nameBytes.Length);
            nameBytes.AsSpan(0, copyLen).CopyTo(payload.AsSpan(1, copyLen));

            int p = 1 + 16 + 4;
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(p, 4), w); p += 4;
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(p, 4), h); p += 4;
            p += 2;
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(p, 2), 32); // 32 bpp
            p += 20;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(p, 4), 0); p += 4;

            // Fill pixels with solid green
            for (int i = 0; i < w * h; i++)
            {
                int o = p + (i * 4);
                payload[o] = 0;      // B
                payload[o + 1] = 255; // G
                payload[o + 2] = 0;  // R
                payload[o + 3] = 255; // A
            }

            return payload;
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

        [Fact]
        public void RealInstallation_DiagnosticCheck()
        {
            string gameDir = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!System.IO.Directory.Exists(gameDir)) return;

            var rm = new ResourceManager(gameDir);
            bool ftOk = rm.InitializeFileTable();
            Assert.True(ftOk);

            bool zOk = rm.TryLoadZone(4, out var zone, out var textures);
            Assert.True(zOk, $"TryLoadZone(4) failed after expansion tables. rm.FileTable count: {rm.FileTable.Count}");
            Assert.NotNull(zone);
            Assert.NotEmpty(zone.MeshGroups);
            Assert.NotEmpty(textures);

            var camera = new Gordian.Core.Graphics.ViewportCamera();
            Vector3 playerPos = new(-398.85f, -1.68f, -466.56f);
            camera.Update(playerPos, pitch: 15.0f, yaw: 102f * 360f / 256f, distance: 6.0f, aspectRatio: 16f / 9f);
            var frustum = camera.Frustum;

            int culled = 0;
            int visible = 0;
            foreach (var mg in zone.MeshGroups)
            {
                Assert.False(float.IsNaN(mg.MinBounds.X), $"MinBounds.X was NaN in mesh '{mg.Name}'");
                Assert.False(float.IsNaN(mg.MinBounds.Y), $"MinBounds.Y was NaN in mesh '{mg.Name}'");
                Assert.False(float.IsNaN(mg.MinBounds.Z), $"MinBounds.Z was NaN in mesh '{mg.Name}'");
                Assert.False(float.IsNaN(mg.MaxBounds.X), $"MaxBounds.X was NaN in mesh '{mg.Name}'");
                Assert.False(float.IsNaN(mg.MaxBounds.Y), $"MaxBounds.Y was NaN in mesh '{mg.Name}'");
                Assert.False(float.IsNaN(mg.MaxBounds.Z), $"MaxBounds.Z was NaN in mesh '{mg.Name}'");

                if (frustum.IntersectsBox(mg.MinBounds, mg.MaxBounds))
                {
                    visible++;
                }
                else
                {
                    culled++;
                }
            }
            Assert.True(zone.MeshGroups.Count > 300, $"Expected > 300 valid meshes in Zone 4, got {zone.MeshGroups.Count}");
            // The player must be able to see at least some nearby terrain from their own spawn position.
            // (A single corrupted vertex used to poison and drop entire submeshes near the spawn area,
            // leaving the player standing in a void with 0 visible meshes -- see ZoneMeshDecoder.)
            Assert.True(visible > 0, $"Expected at least some visible meshes near player spawn, got 0 (culled={culled})");

            // Also check Bastok Mines (Zone 234)
            if (rm.TryLoadZone(234, out var zone234, out var tex234) && zone234 != null)
            {
                for (int i = 0; i < Math.Min(5, zone234.MeshGroups.Count); i++)
                {
                    var mg = zone234.MeshGroups[i];
                    Gordian.Core.Diagnostics.GordianLog.Info("ZONE", $"Zone 234 Mesh[{i}]: Name='{mg.Name}', Tex='{mg.TextureName}', Min={mg.MinBounds}, Max={mg.MaxBounds}, Verts={mg.Vertices.Length}");
                }
            }

            // Now test entity model loading
            var player = new Gordian.Core.World.PlayerEntity(1, 1024);
            // HumeMale = 1. Face 1 = 0. ModelId = (1 << 8) | 0 = 0x0100
            player.Appearance.GrapIdTable[0] = 0x0100; // HumeMale face 0
            // Head: Bronze Cap (model 1), Body: Bronze Harness (model 1), etc.
            player.Appearance.GrapIdTable[1] = 0x1001; // Head model 1
            player.Appearance.GrapIdTable[2] = 0x2001; // Body model 1
            player.Appearance.GrapIdTable[3] = 0x3001; // Hands model 1
            player.Appearance.GrapIdTable[4] = 0x4001; // Legs model 1
            player.Appearance.GrapIdTable[5] = 0x5001; // Feet model 1

            bool pOk = rm.TryLoadEntityModel(player, out var playerModel);
            Assert.True(pOk, "TryLoadEntityModel failed for player");
            Assert.NotNull(playerModel);
            Assert.NotEmpty(playerModel.MeshGroups);
            Gordian.Core.Diagnostics.GordianLog.Info("PLAYER", $"Player MinBounds={playerModel.MinBounds}, MaxBounds={playerModel.MaxBounds}, MeshGroups={playerModel.MeshGroups.Count}");
            for (int i = 0; i < playerModel.MeshGroups.Count; i++)
            {
                var mg = playerModel.MeshGroups[i];
                Gordian.Core.Diagnostics.GordianLog.Info("PLAYER", $"Player Mesh[{i}]: Name='{mg.Name}', Tex='{mg.TextureName}', Min={mg.MinBounds}, Max={mg.MaxBounds}, Verts={mg.Vertices.Length}");
            }
            
            // Check face dat pieces directly
            Gordian.Core.Resources.Tables.CharacterEquipmentResolver.TryResolveGearFileId(Gordian.Core.Resources.Tables.CharacterRace.HumeMale, Gordian.Core.Resources.Tables.CharacterSlot.Face, 0, out int faceFid);
            byte[]? faceDat = rm.LoadDatBytesByFileId(faceFid);
            Assert.NotNull(faceDat);
            var faceContainer = EntityModelLoader.ParseDatContainer(faceDat, "Face");
            Assert.NotEmpty(faceContainer.Meshes);
            var meshGroup = faceContainer.Meshes[0];
            Assert.NotEmpty(meshGroup.Pieces);
            Assert.Equal(144, meshGroup.Vertices.Length);
        }
    }
}
