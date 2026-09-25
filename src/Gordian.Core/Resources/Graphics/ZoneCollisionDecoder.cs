// src/Gordian.Core/Resources/Graphics/ZoneCollisionDecoder.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.World.Collision;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// Clean-room decoder for the player-collision triangle soup embedded in a decrypted Section 0x1C ZoneDef payload
    /// (the block at payload offset header +0x08; zero when the zone has none). The block holds a chain of local-space
    /// collision meshes, 0xC0-byte placement transforms and groups of (transform, mesh) pairs; every distinct pair is
    /// one placed collision object, expanded here into world-space triangles.
    /// Collision block layout referenced from xi-tools (docs/zone/collision.md, src/xi/zone/xi_collision.py,
    /// https://github.com/vekien/xi-tools).
    /// </summary>
    public static class ZoneCollisionDecoder
    {
        private const int TransformSize = 0xC0;
        private const int MeshHeaderSize = 0x10;

        /// <summary>
        /// Decodes the collision soup of a decrypted ZoneDef payload (the section data after its 16-byte chunk header),
        /// or returns null when the zone has no collision block or it is malformed.
        /// </summary>
        public static ZoneCollisionMesh? Decode(ReadOnlySpan<byte> payload)
        {
            var triangles = DecodeTriangles(payload);
            return triangles == null ? null : new ZoneCollisionMesh(triangles);
        }

        /// <summary>
        /// Decodes the world-space collision triangles of a decrypted ZoneDef payload, or null when there are none.
        /// </summary>
        public static List<CollisionTriangle>? DecodeTriangles(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 0x20) return null;
            int block = ReadOffset(payload, 0x08);
            if (block <= 0 || block + 0x20 > payload.Length) return null;

            int groupCount = ReadOffset(payload, block + 0x08);
            int groupsOffset = ReadOffset(payload, block + 0x0C);
            if (groupCount <= 0 || groupsOffset <= 0 || groupsOffset >= payload.Length) return null;

            var triangles = new List<CollisionTriangle>();
            var seenPairs = new HashSet<long>();
            int p = groupsOffset;
            for (int g = 0; g < groupCount; g++)
            {
                if (p + 4 > payload.Length) break;
                int pairCount = (int)(BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(p, 4)) & 0x7FF);
                p += 4;
                for (int j = 0; j < pairCount; j++, p += 8)
                {
                    if (p + 8 > payload.Length) return triangles.Count > 0 ? triangles : null;
                    int transformOffset = ReadOffset(payload, p);
                    int meshOffset = ReadOffset(payload, p + 4);
                    if (!seenPairs.Add(((long)transformOffset << 32) | (uint)meshOffset)) continue;
                    AppendObject(payload, transformOffset, meshOffset, triangles);
                }
                p += 4; // zero terminator
            }

            return triangles.Count > 0 ? triangles : null;
        }

        /// <summary>
        /// Transforms one local-space collision mesh by its placement's world matrix.
        /// </summary>
        private static void AppendObject(ReadOnlySpan<byte> payload, int transformOffset, int meshOffset,
                                         List<CollisionTriangle> triangles)
        {
            if (transformOffset <= 0 || transformOffset + TransformSize > payload.Length) return;
            if (meshOffset <= 0 || meshOffset + MeshHeaderSize > payload.Length) return;

            // Both matrices are column-major (translation in elements 12-14), which loads straight into a
            // System.Numerics row-vector matrix.
            var toWorld = ReadMatrix(payload.Slice(transformOffset, 0x40));
            var toLocal = ReadMatrix(payload.Slice(transformOffset + 0x40, 0x40));
            var normalMatrix = Matrix4x4.Transpose(toLocal);

            int positions = ReadOffset(payload, meshOffset + 0x00);
            int normals = ReadOffset(payload, meshOffset + 0x04);
            int indices = ReadOffset(payload, meshOffset + 0x08);
            int triangleCount = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(meshOffset + 0x0C, 2));
            bool meshFlagged = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(meshOffset + 0x0E, 2)) != 0;
            if (positions <= 0 || normals <= 0 || indices <= 0) return;
            if (indices + (triangleCount * 8) > payload.Length) return;

            // The position pool runs up to the normal pool and the normal pool up to the index buffer.
            int vertexCount = (normals - positions) / 12;
            int normalCount = (indices - normals) / 12;
            if (vertexCount <= 0 || normalCount <= 0) return;

            for (int t = 0; t < triangleCount; t++)
            {
                var record = payload.Slice(indices + (t * 8), 8);
                ushort w0 = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0, 2));
                ushort w1 = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(2, 2));
                ushort w2 = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(4, 2));
                ushort w3 = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(6, 2));

                int i0 = w0 & 0x7FFF, i1 = w1 & 0x3FFF, i2 = w2 & 0x3FFF, n = w3 & 0x7FFF;
                if (i0 >= vertexCount || i1 >= vertexCount || i2 >= vertexCount || n >= normalCount) continue;

                Vector3 a = Vector3.Transform(ReadVector(payload, positions + (i0 * 12)), toWorld);
                Vector3 b = Vector3.Transform(ReadVector(payload, positions + (i1 * 12)), toWorld);
                Vector3 c = Vector3.Transform(ReadVector(payload, positions + (i2 * 12)), toWorld);
                Vector3 normal = Vector3.TransformNormal(ReadVector(payload, normals + (n * 12)), normalMatrix);
                float length = normal.Length();
                if (!(length > 1e-6f)) continue;
                normal /= length;

                // The stored normal faces away from the winding's cross product; a placement that mirrors the mesh
                // flips the winding, so restore it.
                if (Vector3.Dot(Vector3.Cross(a - b, b - c), normal) > 0.0f) (a, c) = (c, a);

                // The top nibble of each word forms a material word: bit 0x40 marks a wall, and bit 3 of each nibble
                // sums to the terrain type.
                bool isWall = (w2 & 0x4000) != 0;
                int terrain = ((w0 >> 15) & 1) + ((w1 >> 14) & 2) + ((w2 >> 13) & 4) + ((w3 >> 12) & 8);
                bool cameraTransparent = meshFlagged && (w2 & 0x4000) != 0;

                triangles.Add(new CollisionTriangle(a, b, c, normal, isWall, (CollisionTerrain)terrain, cameraTransparent));
            }
        }

        private static int ReadOffset(ReadOnlySpan<byte> payload, int offset)
        {
            if (offset < 0 || offset + 4 > payload.Length) return -1;
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(offset, 4));
            return value > int.MaxValue ? -1 : (int)value;
        }

        private static Vector3 ReadVector(ReadOnlySpan<byte> payload, int offset) => new(
            BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(offset, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(offset + 4, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(offset + 8, 4)));

        private static Matrix4x4 ReadMatrix(ReadOnlySpan<byte> m)
        {
            Span<float> f = stackalloc float[16];
            for (int i = 0; i < 16; i++) f[i] = BinaryPrimitives.ReadSingleLittleEndian(m.Slice(i * 4, 4));
            return new Matrix4x4(
                f[0], f[1], f[2], f[3],
                f[4], f[5], f[6], f[7],
                f[8], f[9], f[10], f[11],
                f[12], f[13], f[14], f[15]);
        }
    }
}
