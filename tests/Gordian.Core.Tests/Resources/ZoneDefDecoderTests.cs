// tests/Gordian.Core.Tests/Resources/ZoneDefDecoderTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class ZoneDefDecoderTests
    {
        [Fact]
        public void CreateTrsMatrix_TransformsPositionAndScaleAccurately()
        {
            Vector3 pos = new(100f, 25f, -50f);
            Vector3 rot = Vector3.Zero;
            Vector3 scale = new(2f, 3f, 4f);

            var matrix = ZoneDefDecoder.CreateTrsMatrix(pos, rot, scale);

            // Origin should map directly to translation
            var transformedOrigin = Vector3.Transform(Vector3.Zero, matrix);
            Assert.Equal(pos.X, transformedOrigin.X, 0.001f);
            Assert.Equal(pos.Y, transformedOrigin.Y, 0.001f);
            Assert.Equal(pos.Z, transformedOrigin.Z, 0.001f);

            // Unit X should scale by 2 and shift by pos
            var transformedX = Vector3.Transform(Vector3.UnitX, matrix);
            Assert.Equal(pos.X + 2f, transformedX.X, 0.001f);
            Assert.Equal(pos.Y, transformedX.Y, 0.001f);
            Assert.Equal(pos.Z, transformedX.Z, 0.001f);
        }

        [Fact]
        public void InstantiateSubmesh_TransformsVerticesAndUpdatesBounds()
        {
            var template = new MeshGroup
            {
                Name = "tree_palm",
                TextureName = "tree_tex.png",
                Vertices = new MeshVertex[]
                {
                    new(new Vector3(0, 0, 0), Vector3.UnitY, Vector2.Zero, 0xFFFFFFFF),
                    new(new Vector3(5, 10, 2), Vector3.UnitY, Vector2.One, 0xFFFFFFFF),
                },
                Indices = new int[] { 0, 1, 0 },
                MinBounds = new Vector3(0, 0, 0),
                MaxBounds = new Vector3(5, 10, 2)
            };

            Vector3 pos = new(200f, 10f, 300f);
            var matrix = ZoneDefDecoder.CreateTrsMatrix(pos, Vector3.Zero, Vector3.One);

            var instantiated = ZoneDefDecoder.InstantiateSubmesh(template, matrix, "inst_01");

            Assert.Equal("tree_palm_inst_01", instantiated.Name);
            Assert.Equal("tree_tex.png", instantiated.TextureName);
            Assert.Equal(2, instantiated.Vertices.Length);
            Assert.Equal(new Vector3(-200f, -10f, 300f), instantiated.Vertices[0].Position);
            Assert.Equal(new Vector3(-205f, -20f, 302f), instantiated.Vertices[1].Position);
            Assert.Equal(new Vector3(-205f, -20f, 300f), instantiated.MinBounds);
            Assert.Equal(new Vector3(-200f, -10f, 302f), instantiated.MaxBounds);
        }

        [Fact]
        public void DecryptZoneObjects_UnmasksObjectNamesWithXor55()
        {
            // Synthetic encrypted ZoneDef (mode 0x1B > 0x1A) with an XOR stream length of 0: only the names are masked.
            int stride = ZoneDefDecoder.StrideModern; // 100 bytes
            byte[] payload = new byte[0x20 + (2 * stride)];

            // meta0: mode = 0x1B, stream length 0
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x1B000000);
            // meta4: nodeCount = 2
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 2);

            string origName = "rz_yasi_m";
            byte[] nameBytes = Encoding.ASCII.GetBytes(origName);
            for (int i = 0; i < nameBytes.Length; i++)
            {
                payload[0x20 + i] = (byte)(nameBytes[i] ^ 0x55);
            }

            byte[] dummyTable = new byte[256];
            int count = ZoneDefDecoder.DecryptZoneObjects(payload, dummyTable);

            Assert.Equal(2, count);
            string unmasked = Encoding.ASCII.GetString(payload.AsSpan(0x20, nameBytes.Length));
            Assert.Equal(origName, unmasked);
        }

        [Fact]
        public void DecryptZoneObjects_LeavesUnencryptedNamesAlone()
        {
            // Mode 0x1A or below is not encrypted, names included (xi-tools xi_decrypt.py); Port Jeuno's shop interior.
            int stride = ZoneDefDecoder.StrideModern;
            byte[] payload = new byte[0x20 + (2 * stride)];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x1A000000);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 2);
            Encoding.ASCII.GetBytes("ju402_box02_2").CopyTo(payload, 0x20);
            Assert.Equal(2, ZoneDefDecoder.DecryptZoneObjects(payload, new byte[256]));
            Assert.Equal("ju402_box02_2", Encoding.ASCII.GetString(payload.AsSpan(0x20, 13)));
        }

        [Fact]
        public void DecryptZoneObjects_DetectsModernStrideFromMaskedUnderscoreNames()
        {
            // City zones (e.g. Southern San d'Oria) name objects like "rig_10": '_' masks to 0x0A, a control byte,
            // so stride detection must unmask names before scoring them. Record bodies are filled with printable
            // bytes so a misaligned 0x54 probe would look like a name if the mask were ignored.
            int stride = ZoneDefDecoder.StrideModern;
            const int nodes = 8;
            byte[] payload = new byte[0x20 + (nodes * stride)];
            payload.AsSpan(0x20).Fill((byte)'A');
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x1B000000);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), nodes);

            for (int n = 0; n < nodes; n++)
            {
                byte[] name = Encoding.ASCII.GetBytes($"rig_{n:D2}".PadRight(16));
                for (int i = 0; i < 16; i++) payload[0x20 + (n * stride) + i] = (byte)(name[i] ^ 0x55);
            }

            int count = ZoneDefDecoder.DecryptZoneObjects(payload, new byte[256]);

            Assert.Equal(nodes, count);
            Assert.Equal(ZoneDefDecoder.StrideModern, ZoneDefDecoder.DetectObjectStride(payload, nodes));
            Assert.Equal("rig_05", Encoding.ASCII.GetString(payload.AsSpan(0x20 + (5 * stride), 6)));
        }

        [Fact]
        public void ParseZonePlacements_FiltersDeletedAndCollisionProxies()
        {
            int stride = ZoneDefDecoder.StrideModern;
            byte[] payload = new byte[0x20 + (3 * stride)];

            // Placement 0: Valid visual object
            int b0 = 0x20;
            Encoding.ASCII.GetBytes("palm_tree").CopyTo(payload.AsSpan(b0, 9));
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(b0 + 0x10, 4), 10.0f); // px
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(b0 + 0x14, 4), 5.0f);  // py
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(b0 + 0x18, 4), 20.0f); // pz
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(b0 + 0x28, 4), 1.0f);  // sx
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(b0 + 0x2C, 4), 1.0f);  // sy
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(b0 + 0x30, 4), 1.0f);  // sz
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(b0 + 0x40, 4), 50.0f); // drawDist

            // Placement 1: Deleted object (py <= -90,000)
            int b1 = b0 + stride;
            Encoding.ASCII.GetBytes("deleted_box").CopyTo(payload.AsSpan(b1, 11));
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(b1 + 0x14, 4), -100000.0f); // py

            // Placement 2: Collision-only proxy (drawDist == 1.0)
            int b2 = b1 + stride;
            Encoding.ASCII.GetBytes("hit_wall").CopyTo(payload.AsSpan(b2, 8));
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(b2 + 0x14, 4), 0.0f);  // py
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(b2 + 0x40, 4), 1.0f);  // drawDist = 1.0

            var placements = ZoneDefDecoder.ParseZonePlacements(payload, 3);

            Assert.Single(placements);
            Assert.Equal("palm_tree", placements[0].MeshId);
            Assert.Equal(new Vector3(10f, 5f, 20f), placements[0].Position);
        }

        [Fact]
        public void ParseZonePlacements_ReadsEnvironmentLinkAndPointLightReferences()
        {
            int stride = ZoneDefDecoder.StrideModern;
            byte[] payload = new byte[0x20 + stride];
            int b = 0x20;
            Encoding.ASCII.GetBytes("cave_floor").CopyTo(payload.AsSpan(b, 10));
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(b + 0x28, 4), 1.0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(b + 0x2C, 4), 1.0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(b + 0x30, 4), 1.0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(b + 0x40, 4), 50.0f);
            Encoding.ASCII.GetBytes("ev01").CopyTo(payload.AsSpan(b + 0x4C, 4));
            // 1-based light-table references; 0 means none.
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(b + 0x54, 4), 14);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(b + 0x58, 4), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(b + 0x5C, 4), 37);

            var placement = Assert.Single(ZoneDefDecoder.ParseZonePlacements(payload, 1));

            Assert.Equal("ev01", placement.EnvironmentId);
            Assert.Equal(new[] { 13, 36 }, placement.PointLightSlots);
            Assert.False(placement.NoPointLights);
            Assert.False(placement.InteriorLightsOnly);
        }

        [Theory]
        [InlineData(0x00, false, false)] // Castle Zvahl's torch-lit floor b_07b
        [InlineData(0x02, false, true)]  // the Meadhouse's furniture, Lower Jeuno's step01
        [InlineData(0x01, true, false)]  // Lower Jeuno's shop goods
        [InlineData(0x03, true, true)]   // Metalworks' hall, Lower Jeuno's arcade tunnel
        public void ParseZonePlacements_ReadsTheNoPointLightsBits(byte flags, bool noPointLights, bool interiorLightsOnly)
        {
            int stride = ZoneDefDecoder.StrideModern;
            byte[] payload = new byte[0x20 + stride];
            int b = 0x20;
            Encoding.ASCII.GetBytes("tozoner").CopyTo(payload.AsSpan(b, 7));
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(b + 0x40, 4), 120.0f);
            payload[b + 0x46] = flags;
            payload[b + 0x47] = 0x01;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(b + 0x54, 4), 63);

            var placement = Assert.Single(ZoneDefDecoder.ParseZonePlacements(payload, 1));

            Assert.Equal(noPointLights, placement.NoPointLights);
            Assert.Equal(interiorLightsOnly, placement.InteriorLightsOnly);
            Assert.Equal(new[] { 62 }, placement.PointLightSlots);
        }

        [Fact]
        public void ParsePointLightTable_ReadsGeneratorIdsUpToTheCollisionBlock()
        {
            const int tableOffset = 0x40;
            byte[] payload = new byte[tableOffset + 3 * 0x4C + 0x10];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x18, 4), tableOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x08, 4), tableOffset + 3 * 0x4C);
            Encoding.ASCII.GetBytes("pl01").CopyTo(payload.AsSpan(tableOffset, 4));
            Encoding.ASCII.GetBytes("pl03").CopyTo(payload.AsSpan(tableOffset + 2 * 0x4C, 4));

            var ids = ZoneDefDecoder.ParsePointLightTable(payload);

            Assert.Equal(new[] { "pl01", "", "pl03" }, ids);
        }

        [Fact]
        public void ResolveTemplate_PrefersHighLodAndMatchesVariants()
        {
            var templates = new Dictionary<string, List<MeshGroup>>(StringComparer.OrdinalIgnoreCase)
            {
                ["house_l"] = new() { new MeshGroup { Name = "house_l" } },
                ["house_m"] = new() { new MeshGroup { Name = "house_m" } },
                ["house_h"] = new() { new MeshGroup { Name = "house_h" } },
                ["bridge"] = new() { new MeshGroup { Name = "bridge" } },
                ["category stone_pillar"] = new() { new MeshGroup { Name = "category stone_pillar" } },
            };

            var realMeshNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "house_l", "house_m", "house_h", "bridge", "stone_pillar"
            };

            // When requesting lower variant "house_l", should resolve to highest variant "house_h"
            var resolvedHouse = ZoneDefDecoder.ResolveTemplate("house_l", templates, realMeshNames);
            Assert.NotNull(resolvedHouse);
            Assert.Equal("house_h", resolvedHouse[0].Name);

            // Exact match for "bridge"
            var resolvedBridge = ZoneDefDecoder.ResolveTemplate("bridge", templates, realMeshNames);
            Assert.NotNull(resolvedBridge);
            Assert.Equal("bridge", resolvedBridge[0].Name);

            // Normalized match
            var resolvedHouseNorm = ZoneDefDecoder.ResolveTemplate("HOUSE_M", templates, realMeshNames);
            Assert.NotNull(resolvedHouseNorm);
            Assert.Equal("house_h", resolvedHouseNorm[0].Name);
        }

        [Theory]
        [InlineData("lowsea", "sea.png", true)]
        [InlineData("suimen", "tex.png", true)]
        [InlineData("water_fall", "tex.png", true)]
        [InlineData("ocean_waves", "tex.png", true)]
        [InlineData("rz_sima_dou_m", "model per_sna", false)]
        [InlineData("rz_sima_st1_m", "model per_kabe", false)]
        [InlineData("_par_w7", "model par_w7", false)]
        public void IsWaterMesh_AccuratelyIdentifiesWaterAndTerrainSurfaces(string meshName, string textureName, bool expected)
        {
            bool actual = ZoneDefDecoder.IsWaterMesh(meshName, textureName);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void InstantiateSubmesh_CorrectlyPropagatesFoliageAndWaterFlags()
        {
            var terrainTemplate = new MeshGroup
            {
                Name = "rz_sima_dou_m",
                TextureName = "model per_sna",
                Vertices = new MeshVertex[] { new(Vector3.Zero, Vector3.UnitY, Vector2.Zero, 0xFFFFFFFF) },
                Indices = new int[] { 0, 0, 0 },
                IsBlend = true
            };
            var foliageTemplate = new MeshGroup
            {
                Name = "_par_w7",
                TextureName = "model par_w7",
                Vertices = new MeshVertex[] { new(Vector3.Zero, Vector3.UnitY, Vector2.Zero, 0xFFFFFFFF) },
                Indices = new int[] { 0, 0, 0 },
                IsFoliage = true
            };
            var waterTemplate = new MeshGroup
            {
                Name = "lowsea",
                TextureName = "model sea",
                Vertices = new MeshVertex[] { new(Vector3.Zero, Vector3.UnitY, Vector2.Zero, 0xFFFFFFFF) },
                Indices = new int[] { 0, 0, 0 },
                IsBlend = true
            };

            var trs = Matrix4x4.Identity;
            var terrainInst = ZoneDefDecoder.InstantiateSubmesh(terrainTemplate, trs, "p_01");
            var foliageInst = ZoneDefDecoder.InstantiateSubmesh(foliageTemplate, trs, "p_02");
            var waterInst = ZoneDefDecoder.InstantiateSubmesh(waterTemplate, trs, "p_03");

            // Terrain decal: IsBlend true, IsFoliage false, IsWater false -> Drawn in Pass 2 with depth writing
            Assert.True(terrainInst.IsBlend);
            Assert.False(terrainInst.IsFoliage);
            Assert.False(terrainInst.IsWater);

            // Foliage tree: IsBlend false, IsFoliage true, IsWater false -> Drawn in Pass 3 with cutout discard
            Assert.False(foliageInst.IsBlend);
            Assert.True(foliageInst.IsFoliage);
            Assert.False(foliageInst.IsWater);

            // Water: IsWater true -> Drawn in Pass 5 with depth write disabled
            Assert.True(waterInst.IsWater);
        }

        [Theory]
        // #251: Fort Ghelsba's palisade logs (no 0x8000 flag) use the bark textures kawa / kawa_hos, which the river
        // hint "kawa" matched; the client draws them solid.
        [InlineData(false, "uge_he02", "model   kawa_hos", false)]
        [InlineData(false, "_uge_bo56", "model   kawa", false)]
        // Opaque sea and castle surfaces are solid too (ship routes' em_umi01, Whitegate's shiro).
        [InlineData(false, "sea1_h", "model   em_umi01", false)]
        [InlineData(false, "lowsea", "sea.png", false)]
        // Blended water keeps the water pass.
        [InlineData(true, "lowsea", "sea.png", true)]
        [InlineData(true, "sea1_h", "model   sea_00", true)]
        // A blended decal without a water name is not water.
        [InlineData(true, "rz_sima_dou_m", "model per_sna", false)]
        public void IsWaterSurface_RequiresTheBlendFlag(bool isBlend, string meshName, string textureName, bool expected)
        {
            Assert.Equal(expected, ZoneDefDecoder.IsWaterSurface(isBlend, meshName, textureName));
        }

        [Fact]
        public void InstantiateSubmesh_OpaqueSubmeshWithAWaterName_IsNotWater()
        {
            var palisade = new MeshGroup
            {
                Name = "uge_he02",
                TextureName = "model   kawa_hos",
                Vertices = new MeshVertex[] { new(Vector3.Zero, Vector3.UnitY, Vector2.Zero, 0xFFFFFFFF) },
                Indices = new int[] { 0, 0, 0 },
                IsWater = true // even a template flagged by an older decode
            };
            var inst = ZoneDefDecoder.InstantiateSubmesh(palisade, Matrix4x4.Identity, "uge_he02");
            Assert.False(inst.IsBlend);
            Assert.False(inst.IsWater);
        }

        [Fact]
        public void QufimIsland_BlendedDecals_AreNotWater()
        {
            string gameDir = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!System.IO.Directory.Exists(gameDir)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(gameDir);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(126, out var zone, out _)) return;

            // quf is Qufim's general texture prefix: its blended decals draw in authored order, not in the water pass (#255).
            var decals = zone.MeshGroups.FindAll(g => g.IsBlend && g.TextureName.Contains("quf_", StringComparison.Ordinal));
            Assert.NotEmpty(decals);
            Assert.All(decals, g => Assert.False(g.IsWater, g.ToString()));
        }
    }
}
