// tests/Gordian.Core.Tests/Resources/ParticleMeshDecoderTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Gordian.Core.Resources.Graphics;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class ParticleMeshDecoderTests
    {
        [Fact]
        public void Decode_Version5_ReadsTexturedAndUntexturedTriangleLists()
        {
            byte[] payload = BuildParticleMeshPayload("effect  umi1", 0x40);

            var meshes = ParticleMeshDecoder.Decode(payload, "umi1");

            Assert.NotNull(meshes);
            Assert.Equal(2, meshes.Count);

            var textured = meshes[0];
            Assert.Equal("effect  umi1", textured.TextureName);
            Assert.Equal(3, textured.Vertices.Length);
            Assert.Equal(new[] { 0, 1, 2 }, textured.Indices);
            Assert.Equal(new Vector3(1f, 2f, 3f), textured.Vertices[0].Position);
            Assert.Equal(new Vector3(0f, -1f, 0f), textured.Vertices[0].Normal);
            Assert.Equal(new Vector2(0.25f, 0.75f), textured.Vertices[0].TexCoord);
            Assert.Equal(new Vector3(1f, 2f, 3f), textured.MinBounds);
            Assert.Equal(new Vector3(3f, 2f, 5f), textured.MaxBounds);

            // Untextured meshes follow the textured ones.
            Assert.Equal(string.Empty, meshes[1].TextureName);
            Assert.Equal(3, meshes[1].Vertices.Length);
        }

        [Fact]
        public void Decode_SwizzlesBgraAndDoublesZoneResourceColors()
        {
            // Stored BGRA = (0x10, 0x20, 0x30, alpha); zone resources double every channel, clamped to 0xFF.
            var meshes = ParticleMeshDecoder.Decode(BuildParticleMeshPayload("effect  umi1", 0x90), "umi1")!;
            uint color = meshes[0].Vertices[0].ColorRgba;

            Assert.Equal(0x60u, color & 0xFF);          // R = 0x30 * 2
            Assert.Equal(0x40u, (color >> 8) & 0xFF);   // G = 0x20 * 2
            Assert.Equal(0x20u, (color >> 16) & 0xFF);  // B = 0x10 * 2
            Assert.Equal(0xFFu, color >> 24);           // A = 0x90 * 2, clamped
        }

        [Fact]
        public void Decode_NonZoneResourceKeepsAuthoredColors()
        {
            var meshes = ParticleMeshDecoder.Decode(BuildParticleMeshPayload("effect  umi1", 0x40), "umi1", zoneResource: false)!;
            uint color = meshes[0].Vertices[0].ColorRgba;

            Assert.Equal(0x30u, color & 0xFF);
            Assert.Equal(0x40u, color >> 24);
        }

        [Fact]
        public void Decode_Version3_UsesFourTextureNameSlotsAndPad()
        {
            var meshes = ParticleMeshDecoder.Decode(BuildParticleMeshPayload("effect  shn1", 0x40, version: 3), "humt");

            Assert.NotNull(meshes);
            Assert.Equal(2, meshes.Count);
            Assert.Equal("effect  shn1", meshes[0].TextureName);
            Assert.Equal(new Vector3(1f, 2f, 3f), meshes[0].Vertices[0].Position);
        }

        [Fact]
        public void Decode_RejectsUnknownVersionAndTruncatedPayloads()
        {
            byte[] payload = BuildParticleMeshPayload("effect  umi1", 0x40);

            byte[] badVersion = (byte[])payload.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(badVersion, 4);
            Assert.Null(ParticleMeshDecoder.Decode(badVersion, "umi1"));

            Assert.Null(ParticleMeshDecoder.Decode(payload.AsSpan(0, payload.Length - 10), "umi1"));
        }

        /// <summary>
        /// Builds a Section 0x1F payload with one textured and one untextured single-triangle mesh.
        /// </summary>
        internal static byte[] BuildParticleMeshPayload(string textureName, byte alpha, uint version = 5)
        {
            var bytes = new List<byte>();
            void U32(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); bytes.AddRange(b); }
            void U16(ushort v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, v); bytes.AddRange(b); }
            void F32(float v) { var b = new byte[4]; BinaryPrimitives.WriteSingleLittleEndian(b, v); bytes.AddRange(b); }
            void Name(string name) { var b = new byte[16]; Encoding.ASCII.GetBytes(name).CopyTo(b, 0); bytes.AddRange(b); }

            U32(version);
            bytes.Add(1); // textured meshes
            bytes.Add(1); // untextured meshes
            U16(2);       // total triangles
            U16(1); U16(1); U16(0); // triangle counts, padded to 3 slots
            if (version == 3) U16(0);

            Name(textureName);
            if (version == 3) { Name(string.Empty); Name(string.Empty); Name(string.Empty); }

            for (int mesh = 0; mesh < 2; mesh++)
            {
                var positions = new[] { new Vector3(1f, 2f, 3f), new Vector3(3f, 2f, 3f), new Vector3(1f, 2f, 5f) };
                foreach (var p in positions)
                {
                    F32(p.X); F32(p.Y); F32(p.Z);
                    F32(0f); F32(-1f); F32(0f);
                    bytes.Add(0x10); bytes.Add(0x20); bytes.Add(0x30); bytes.Add(alpha); // BGRA
                    F32(0.25f); F32(0.75f);
                }
            }

            return bytes.ToArray();
        }
    }
}
