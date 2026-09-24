// src/Gordian.Core/Resources/Graphics/ParticleMeshDecoder.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// Clean-room binary decoder for FFXI DAT Section 0x1F (ParticleMesh) chunks: a few small triangle lists drawn by
    /// Section 0x05 particle generators (e.g. Bibiki Bay's sea surface 'umi1' and shoreline surf 'shi1'..'shi4').
    /// Layout: u32 version (3, 5 or 6), u8 texturedMeshCount, u8 untexturedMeshCount, u16 totalTriangles,
    /// u16 per-mesh triangle counts padded to 3/4/7/8/11/12/15 entries (+ u16 pad on version 3),
    /// 16-byte texture names (4 slots on version 3, otherwise one per textured mesh), then per mesh
    /// 3 * triangleCount vertices of { f32 x, y, z; f32 nx, ny, nz; BGRA diffuse; f32 u, v }.
    /// Textured meshes come first. Vertices are returned in raw DAT space.
    /// Format referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer,
    /// ui/js/dat/sections.js parseParticleMesh, after xim ParticleMeshSection).
    /// </summary>
    public static class ParticleMeshDecoder
    {
        private const int VertexStride = 36;
        private const int TextureNameLength = 16;

        /// <summary>
        /// Decodes a Section 0x1F payload (excluding the 16-byte section header) into one triangle-list
        /// <see cref="MeshGroup"/> per mesh, or null if the payload is malformed.
        /// </summary>
        /// <param name="payload">Section payload.</param>
        /// <param name="datId">Section DatId, used to name the meshes.</param>
        /// <param name="zoneResource">
        /// True for meshes loaded from a zone DAT, whose diffuse colors are authored at half range and are doubled
        /// (clamped to 0xFF), matching the client's zone-resource color scaling.
        /// </param>
        public static List<MeshGroup>? Decode(ReadOnlySpan<byte> payload, string datId, bool zoneResource = true)
        {
            if (payload.Length < 8) return null;

            uint version = BinaryPrimitives.ReadUInt32LittleEndian(payload);
            if (version is not (3 or 5 or 6)) return null;

            int texturedCount = payload[4];
            int totalCount = texturedCount + payload[5];
            int countSlots = totalCount switch
            {
                <= 3 => 3,
                <= 4 => 4,
                <= 7 => 7,
                <= 8 => 8,
                <= 11 => 11,
                <= 12 => 12,
                <= 15 => 15,
                _ => -1
            };
            if (countSlots < 0) return null;

            int offset = 8;
            if (offset + countSlots * 2 > payload.Length) return null;
            var triangleCounts = new int[countSlots];
            for (int i = 0; i < countSlots; i++)
            {
                triangleCounts[i] = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(offset + i * 2));
            }
            offset += countSlots * 2;
            if (version == 3) offset += 2;

            int nameSlots = version == 3 ? 4 : texturedCount;
            if (offset + nameSlots * TextureNameLength > payload.Length) return null;
            var textureNames = new string[nameSlots];
            for (int i = 0; i < nameSlots; i++)
            {
                textureNames[i] = ReadName(payload.Slice(offset + i * TextureNameLength, TextureNameLength));
            }
            offset += nameSlots * TextureNameLength;

            int colorScale = zoneResource ? 2 : 1;
            var meshes = new List<MeshGroup>(totalCount);
            for (int m = 0; m < totalCount; m++)
            {
                int vertexCount = 3 * triangleCounts[m];
                if (offset + vertexCount * VertexStride > payload.Length) return null;

                var vertices = new MeshVertex[vertexCount];
                var indices = new int[vertexCount];
                Vector3 minBounds = new(float.MaxValue);
                Vector3 maxBounds = new(float.MinValue);
                for (int v = 0; v < vertexCount; v++)
                {
                    var vs = payload.Slice(offset + v * VertexStride, VertexStride);
                    var position = new Vector3(
                        BinaryPrimitives.ReadSingleLittleEndian(vs),
                        BinaryPrimitives.ReadSingleLittleEndian(vs.Slice(4)),
                        BinaryPrimitives.ReadSingleLittleEndian(vs.Slice(8)));
                    var normal = new Vector3(
                        BinaryPrimitives.ReadSingleLittleEndian(vs.Slice(12)),
                        BinaryPrimitives.ReadSingleLittleEndian(vs.Slice(16)),
                        BinaryPrimitives.ReadSingleLittleEndian(vs.Slice(20)));

                    // Diffuse is stored BGRA; repack as RGBA (R in the low byte) like the Section 0x2E decoder.
                    uint r = (uint)Math.Min(0xFF, vs[26] * colorScale);
                    uint g = (uint)Math.Min(0xFF, vs[25] * colorScale);
                    uint b = (uint)Math.Min(0xFF, vs[24] * colorScale);
                    uint a = (uint)Math.Min(0xFF, vs[27] * colorScale);
                    var uv = new Vector2(
                        BinaryPrimitives.ReadSingleLittleEndian(vs.Slice(28)),
                        BinaryPrimitives.ReadSingleLittleEndian(vs.Slice(32)));

                    vertices[v] = new MeshVertex(position, normal, uv, r | (g << 8) | (b << 16) | (a << 24));
                    indices[v] = v;
                    minBounds = Vector3.Min(minBounds, position);
                    maxBounds = Vector3.Max(maxBounds, position);
                }
                offset += vertexCount * VertexStride;

                meshes.Add(new MeshGroup
                {
                    Name = $"{datId}#{m}",
                    TextureName = m < texturedCount && m < textureNames.Length ? textureNames[m] : string.Empty,
                    Vertices = vertices,
                    Indices = indices,
                    MinBounds = vertexCount > 0 ? minBounds : Vector3.Zero,
                    MaxBounds = vertexCount > 0 ? maxBounds : Vector3.Zero,
                    IsBlend = true,
                    NoCull = true
                });
            }

            return meshes;
        }

        private static string ReadName(ReadOnlySpan<byte> span)
        {
            int end = span.IndexOf((byte)0);
            if (end < 0) end = span.Length;
            return Encoding.ASCII.GetString(span.Slice(0, end)).TrimEnd();
        }
    }
}
