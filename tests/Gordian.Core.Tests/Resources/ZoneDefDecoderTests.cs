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
            // Build synthetic ZoneDef unencrypted payload (mode = 0x01 <= 0x1A, 2 nodes)
            int stride = ZoneDefDecoder.StrideModern; // 100 bytes
            byte[] payload = new byte[0x20 + (2 * stride)];

            // meta0: mode = 1
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x01000000);
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
    }
}
