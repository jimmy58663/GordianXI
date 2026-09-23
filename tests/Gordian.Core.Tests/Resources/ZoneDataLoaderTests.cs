// tests/Gordian.Core.Tests/Resources/ZoneDataLoaderTests.cs
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
            var visibleMeshes = new List<MeshGroup>();
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
                    visibleMeshes.Add(mg);
                }
                else
                {
                    culled++;
                }
            }

            Assert.True(zone.MeshGroups.Count > 500, $"Expected > 500 valid meshes in Zone 4, got {zone.MeshGroups.Count}");
            Assert.True(visible > 0, $"Expected at least some visible meshes near player spawn, got 0 (culled={culled})");


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
            Assert.NotEmpty(playerModel.AnimatedMeshGroups);
            Gordian.Core.Diagnostics.GordianLog.Info("PLAYER", $"Player MinBounds={playerModel.MinBounds}, MaxBounds={playerModel.MaxBounds}, MeshGroups={playerModel.AnimatedMeshGroups.Count}");
            for (int i = 0; i < playerModel.AnimatedMeshGroups.Count; i++)
            {
                var mg = playerModel.AnimatedMeshGroups[i];
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

        [Fact]
        public void RealInstallation_CheckZoneEnvironmentAndWater()
        {
            string gameDir = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!System.IO.Directory.Exists(gameDir)) return;

            var rm = new ResourceManager(gameDir);
            rm.InitializeFileTable();

            foreach (int zoneId in new[] { 4, 100, 102, 103, 248 })
            {
                if (rm.TryLoadZone(zoneId, out var zone, out var textures))
                {
                    var env = zone.EnvironmentData;
                    if (env != null)
                    {
                        var kf = env.Interpolate(12f);
                        if (kf != null)
                        {
                            Gordian.Core.Diagnostics.GordianLog.Info("DIAG_ENV",
                                $"Zone {zoneId}: FogStart={kf.TerrainFogStart}, FogEnd={kf.TerrainFogEnd}, DrawDist={kf.DrawDistance}, Slices={kf.Slices.Count}");
                            foreach (var s in kf.Slices)
                            {
                                Gordian.Core.Diagnostics.GordianLog.Info("DIAG_ENV", $"  Slice Elev={s.Elevation:F2}, Color={s.Color}");
                            }
                        }
                    }
                    else
                    {
                        Gordian.Core.Diagnostics.GordianLog.Info("DIAG_ENV", $"Zone {zoneId}: NO EnvironmentData found!");
                    }

                    // Look for water or cloud meshes
                    int waterMeshes = 0;
                    int skyMeshes = 0;
                    foreach (var m in zone.MeshGroups)
                    {
                        string n = m.Name.ToLowerInvariant();
                        if (n.Contains("sea") || n.Contains("water") || n.Contains("suimen") || n.StartsWith("ka") || n.StartsWith("um"))
                            waterMeshes++;
                        if (n.Contains("cloud") || n.Contains("clod") || n.Contains("sora"))
                            skyMeshes++;
                    }
                    Gordian.Core.Diagnostics.GordianLog.Info("DIAG_ENV",
                        $"Zone {zoneId}: TotalMeshes={zone.MeshGroups.Count}, Placements={zone.Placements.Count}, WaterMeshes={waterMeshes}, SkyMeshes={skyMeshes}");
                }
            }
        }

        [Fact]
        public void RealInstallation_InspectZone4Placements()
        {
            string gameDir = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!System.IO.Directory.Exists(gameDir)) return;

            var rm = new ResourceManager(gameDir);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(4, out var zone, out var textures)) return;

            Vector3 playerDisp = new(398.85f, 1.68f, -466.56f);
            var camera = new Gordian.Core.Graphics.ViewportCamera();
            camera.Update(playerDisp, pitch: 10.0f, yaw: 270.0f, distance: 6.0f, aspectRatio: 16f / 9f);
            var frustum = camera.Frustum;

            foreach (var tex in textures.Keys)
            {
                Gordian.Core.Diagnostics.GordianLog.Info("DIAG_TEX", $"Texture: '{tex}'");
            }
        }

        [Fact]
        public void TestPass0bSkyMeshInspection()
        {
            string gameDir = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!System.IO.Directory.Exists(gameDir)) return;

            var rm = new ResourceManager(gameDir);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(4, out var zone, out var textures)) return;

            foreach (var l in zone.WeatherSkyLayers)
            {
                Gordian.Core.Diagnostics.GordianLog.Info("TEST_ALL_SKY", $"Layer: Name='{l.Name}', Weather='{l.WeatherId}', IsCelestial={l.IsCelestial}, Attach={l.AttachType}, FollowCam={l.FollowCamera}, Pos={l.Position}, Scale={l.Scale}, Tex='{l.TextureName}', Submeshes={l.MeshGroups.Count}");
                foreach (var mg in l.MeshGroups)
                {
                    Gordian.Core.Diagnostics.GordianLog.Info("TEST_ALL_SKY", $"  Submesh: Name='{mg.Name}', Tex='{mg.TextureName}', Verts={mg.Vertices.Length}, MinB={mg.MinBounds}, MaxB={mg.MaxBounds}");
                }
            }

            foreach (var l in zone.WeatherSkyLayers)
            {
                if (l.Name.Contains("fine", StringComparison.OrdinalIgnoreCase))
                {
                    Gordian.Core.Diagnostics.GordianLog.Info("TEST_SKY", $"Layer: Name='{l.Name}', Weather='{l.WeatherId}', Tex='{l.TextureName}', Submeshes={l.MeshGroups.Count}");
                    foreach (var mg in l.MeshGroups)
                    {
                        Gordian.Core.Diagnostics.GordianLog.Info("TEST_SKY", $"  Submesh: Name='{mg.Name}', Tex='{mg.TextureName}', Verts={mg.Vertices.Length}, Tris={mg.TriangleCount}, IndicesLen={mg.Indices.Length}");
                        for (int v = 0; v < Math.Min(10, mg.Vertices.Length); v++)
                        {
                            var vert = mg.Vertices[v];
                            Gordian.Core.Diagnostics.GordianLog.Info("TEST_SKY", $"    Vert[{v}]: Pos={vert.Position}, Norm={vert.Normal}, UV={vert.TexCoord}, Color=0x{vert.ColorRgba:X8}");
                        }
                        if (textures.TryGetValue(mg.TextureName, out var tex))
                        {
                            Gordian.Core.Diagnostics.GordianLog.Info("TEST_SKY", $"    Texture: Name='{tex.Name}', W={tex.Width}, H={tex.Height}, Pixels={tex.RgbaPixels.Length}");
                            // Print min and max R, G, B, A across all pixels
                            byte minR = 255, maxR = 0, minG = 255, maxG = 0, minB = 255, maxB = 0, minA = 255, maxA = 0;
                            for (int p = 0; p < tex.RgbaPixels.Length; p += 4)
                            {
                                byte r = tex.RgbaPixels[p];
                                byte g = tex.RgbaPixels[p + 1];
                                byte b = tex.RgbaPixels[p + 2];
                                byte a = tex.RgbaPixels[p + 3];
                                if (r < minR) minR = r; if (r > maxR) maxR = r;
                                if (g < minG) minG = g; if (g > maxG) maxG = g;
                                if (b < minB) minB = b; if (b > maxB) maxB = b;
                                if (a < minA) minA = a; if (a > maxA) maxA = a;
                            }
                            for (int p = 0; p < Math.Min(20 * 4, tex.RgbaPixels.Length); p += 4)
                            {
                                Gordian.Core.Diagnostics.GordianLog.Info("TEST_SKY_PIX", $"Pixel[{p/4}]: R={tex.RgbaPixels[p]}, G={tex.RgbaPixels[p+1]}, B={tex.RgbaPixels[p+2]}, A={tex.RgbaPixels[p+3]}");
                            }
                            Gordian.Core.Diagnostics.GordianLog.Info("TEST_SKY", $"    R range: [{minR}..{maxR}], G range: [{minG}..{maxG}], B range: [{minB}..{maxB}], A range: [{minA}..{maxA}]");
                        }
                    }
                }
            }

            foreach (var kvp in textures)
            {
                var t = kvp.Value;
                int redPixelCount = 0;
                for (int p = 0; p < t.RgbaPixels.Length; p += 4)
                {
                    byte r = t.RgbaPixels[p];
                    byte g = t.RgbaPixels[p + 1];
                    byte b = t.RgbaPixels[p + 2];
                    if (r > 150 && g < 50 && b < 50) redPixelCount++;
                }
                if (redPixelCount > 10)
                {
                    Gordian.Core.Diagnostics.GordianLog.Info("TEST_RED_TEX", $"Texture '{kvp.Key}' (W={t.Width}, H={t.Height}) has {redPixelCount} red pixels!");
                }
            }
            foreach (var mg in zone.MeshGroups)
            {
                if (mg.TextureName.Contains("yuh", StringComparison.OrdinalIgnoreCase) || mg.Name.Contains("yuh", StringComparison.OrdinalIgnoreCase))
                {
                    Gordian.Core.Diagnostics.GordianLog.Info("TEST_YUH_FIND", $"MeshGroup: Name='{mg.Name}', Tex='{mg.TextureName}', Verts={mg.Vertices.Length}");
                }
            }
            foreach (var l in zone.WeatherSkyLayers)
            {
                if (l.TextureName.Contains("yuh", StringComparison.OrdinalIgnoreCase) || l.Name.Contains("yuh", StringComparison.OrdinalIgnoreCase))
                {
                    Gordian.Core.Diagnostics.GordianLog.Info("TEST_YUH_FIND", $"SkyLayer: Name='{l.Name}', Weather='{l.WeatherId}', Tex='{l.TextureName}'");
                }
            }
            if (zone.EnvironmentData != null)
            {
                foreach (var kvp in zone.EnvironmentData.ParticleGenerators)
                {
                    if (kvp.Key.Contains("yuh", StringComparison.OrdinalIgnoreCase) || (kvp.Value.Setup != null && kvp.Value.Setup.LinkedDataId.Contains("yuh", StringComparison.OrdinalIgnoreCase)))
                    {
                        Gordian.Core.Diagnostics.GordianLog.Info("TEST_YUH_FIND", $"Generator: Key='{kvp.Key}', LinkedId='{kvp.Value.Setup?.LinkedDataId}', Scale={kvp.Value.Scale}");
                    }
                }
            }
        }

        [Fact]

        public void DiagnosticSkyInspection()
        {
            string gameDir = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!System.IO.Directory.Exists(gameDir)) return;

            var rm = new ResourceManager(gameDir);
            rm.InitializeFileTable();

            foreach (int zoneId in new[] { 4, 100, 102, 103, 115 })
            {
                if (!rm.TryLoadZone(zoneId, out var zone, out var textures) || zone == null) continue;

                Gordian.Core.Diagnostics.GordianLog.Info("DIAG_SKY_ZONE", $"=== ZONE {zoneId} ===");
                if (zone.EnvironmentData != null)
                {
                    var weathers = string.Join(", ", zone.EnvironmentData.WeatherKeyframes.Keys);
                    Gordian.Core.Diagnostics.GordianLog.Info("DIAG_SKY_ZONE", $"  Weather Keyframes: [{weathers}]");
                    var gens = string.Join(", ", zone.EnvironmentData.ParticleGenerators.Keys);
                    Gordian.Core.Diagnostics.GordianLog.Info("DIAG_SKY_ZONE", $"  Generators ({zone.EnvironmentData.ParticleGenerators.Count}): [{gens}]");
                }
                foreach (var l in zone.WeatherSkyLayers)
                {
                    Gordian.Core.Diagnostics.GordianLog.Info("DIAG_SKY_ZONE", $"  SkyLayer: Name='{l.Name}', Weather='{l.WeatherId}', Celestial={l.IsCelestial}, Attach={l.AttachType}, FollowCam={l.FollowCamera}, Pos={l.Position}, Scale={l.Scale}, Tex='{l.TextureName}'");
                    foreach (var mg in l.MeshGroups)
                    {
                        var v0 = mg.Vertices.Length > 0 ? mg.Vertices[0] : default;
                        Gordian.Core.Diagnostics.GordianLog.Info("DIAG_SKY_ZONE", $"    Submesh: '{mg.Name}' Tex='{mg.TextureName}' Verts={mg.Vertices.Length} Bounds=[{mg.MinBounds}..{mg.MaxBounds}] V0_Pos={v0.Position} V0_Norm={v0.Normal} V0_UV={v0.TexCoord} V0_Col=0x{v0.ColorRgba:X8}");
                    }
                }
                foreach (var kvp in textures)
                {
                    string k = kvp.Key.ToLowerInvariant();
                    if (k.Contains("moon") || k.Contains("star") || k.Contains("sun") || k.Contains("cld") || k.Contains("fine") || k.Contains("clod") || k.Contains("mist") || k.Contains("rain") || k.Contains("snow") || k.Contains("thdr") || k.Contains("yuh") || k.Contains("kasa") || k.Contains("lf0"))
                    {
                        var t = kvp.Value;
                        byte minA = 255, maxA = 0, minR = 255, maxR = 0, minG = 255, maxG = 0, minB = 255, maxB = 0;
                        int nonZeroAlpha = 0;
                        for (int p = 0; p < t.RgbaPixels.Length; p += 4)
                        {
                            byte r = t.RgbaPixels[p]; byte g = t.RgbaPixels[p + 1]; byte b = t.RgbaPixels[p + 2]; byte a = t.RgbaPixels[p + 3];
                            if (a < minA) minA = a; if (a > maxA) maxA = a;
                            if (r < minR) minR = r; if (r > maxR) maxR = r;
                            if (g < minG) minG = g; if (g > maxG) maxG = g;
                            if (b < minB) minB = b; if (b > maxB) maxB = b;
                            if (a > 10) nonZeroAlpha++;
                        }
                        Gordian.Core.Diagnostics.GordianLog.Info("DIAG_SKY_TEX", $"  Tex '{kvp.Key}': W={t.Width}, H={t.Height}, A=[{minA}..{maxA}], R=[{minR}..{maxR}], G=[{minG}..{maxG}], B=[{minB}..{maxB}], NonZeroA={nonZeroAlpha}/{t.RgbaPixels.Length/4}");
                    }
                }

            }
        }
    }
}

