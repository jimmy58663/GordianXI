// src/Gordian.Core/Resources/Graphics/ZoneMeshDecoder.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// Clean-room binary decoder for FFXI Section 0x2E Zone Mesh resources.
    /// Handles 2-pass key table decryption and parses 3D terrain submeshes, vertices, normals, UVs, and indices.
    /// Derived from community research in xi-model-viewer (https://github.com/vekien/xi-model-viewer)
    /// and xi-tools (xi/zone/xi_decrypt.py, xi_export.py).
    /// </summary>
    public static class ZoneMeshDecoder
    {
        public static readonly byte[] Table1Sig = { 0xE2, 0xE5, 0x06, 0xA9 };
        public static readonly byte[] Table2Sig = { 0xB8, 0xC5, 0xF7, 0x84 };

        /// <summary>
        /// Decrypts a Section 0x2E zone mesh data stream using the two 256-byte substitution key tables.
        /// </summary>
        public static void DecryptZoneMesh(Span<byte> payload, ReadOnlySpan<byte> table1, ReadOnlySpan<byte> table2)
        {
            if (payload.Length < 16 || table1.Length < 256 || table2.Length < 256) return;

            uint metadata = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            int totalSize = (int)(metadata & 0x00FFFFFF);
            int decodeLength = Math.Min(totalSize - 8, payload.Length - 8);
            byte mode = (byte)((metadata >> 24) & 0xFF);

            if (decodeLength <= 0) return;

            // Pass 1: Keyed XOR stream (mode >= 5)
            if (mode >= 5)
            {
                byte seed = (byte)(payload[5] ^ 0xF0);
                int key = table1[seed];
                int counter = 0;
                int p = 8;

                for (int i = 0; i < decodeLength; i++)
                {
                    int kb = key % 256;
                    int keyMod = (kb << 8) | kb;
                    counter++;
                    key += counter;
                    int shift = key % 8;
                    payload[p + i] ^= (byte)((keyMod >> shift) & 0xFF);
                    counter++;
                    key += counter;
                }
            }

            // Pass 2: Conditional swap of 8-byte blocks between the two body halves
            ushort flag = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2));
            if (flag == 0xFFFF)
            {
                int key1 = payload[5] ^ 0xF0;
                int key2 = table2[key1];
                int decodeCount = (decodeLength & ~0x0F) >> 1;
                int p = 8;
                int i = 0;

                while (i < decodeCount)
                {
                    if ((key2 % 2) != 0)
                    {
                        for (int j = 0; j < 8; j++)
                        {
                            byte a = payload[p + j];
                            byte b = payload[p + decodeCount + j];
                            payload[p + j] = b;
                            payload[p + decodeCount + j] = a;
                        }
                    }
                    p += 8;
                    i += 8;
                    key1 += 9;
                    key2 += key1;
                }
            }
        }

        /// <summary>
        /// Decodes a decrypted Section 0x2E zone mesh payload into structured 3D MeshGroups.
        /// </summary>
        public static List<MeshGroup> ParseZoneMesh(ReadOnlySpan<byte> payload)
        {
            var results = new List<MeshGroup>();
            if (payload.Length < 0x60) return results;

            string meshName = ReadCString(payload.Slice(0x10, 16));

            // Scan for submeshes
            // A submesh header is: 16-byte texture name + uint16 numVerts + uint16 pad
            // followed by numVerts * 36 bytes (pos 12B, normal 12B, UV 8B, color 4B)
            // followed by uint16 numIndices + uint16 pad + numIndices * 2 bytes
            int pos = 0x20;
            int len = payload.Length;

            while (pos + 20 <= len)
            {
                if (LooksLikeSubmesh(payload, pos, len, out ushort numVerts, out ushort numIndices))
                {
                    string texName = ReadCString(payload.Slice(pos, 16));
                    int vertStart = pos + 20;
                    int vertStride = 36; // Pos (12) + Normal (12) + UV (8) + RGBA (4)

                    var vertices = new MeshVertex[numVerts];
                    Vector3 minBounds = new(float.MaxValue);
                    Vector3 maxBounds = new(float.MinValue);

                    for (int v = 0; v < numVerts; v++)
                    {
                        int vo = vertStart + (v * vertStride);
                        float px = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(vo, 4));
                        float py = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(vo + 4, 4));
                        float pz = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(vo + 8, 4));

                        float nx = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(vo + 12, 4));
                        float ny = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(vo + 16, 4));
                        float nz = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(vo + 20, 4));

                        float u = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(vo + 24, 4));
                        float uv_v = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(vo + 28, 4));
                        uint color = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(vo + 32, 4));

                        var position = new Vector3(px, py, pz);
                        minBounds = Vector3.Min(minBounds, position);
                        maxBounds = Vector3.Max(maxBounds, position);

                        vertices[v] = new MeshVertex(
                            position,
                            new Vector3(nx, ny, nz),
                            new Vector2(u, uv_v),
                            color
                        );
                    }

                    int idxHeader = vertStart + (numVerts * vertStride);
                    int idxStart = idxHeader + 4;
                    var indices = new int[numIndices];

                    for (int idx = 0; idx < numIndices; idx++)
                    {
                        indices[idx] = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(idxStart + (idx * 2), 2));
                    }

                    results.Add(new MeshGroup
                    {
                        Name = meshName,
                        TextureName = texName,
                        Vertices = vertices,
                        Indices = indices,
                        MinBounds = minBounds,
                        MaxBounds = maxBounds
                    });

                    pos = idxStart + (numIndices * 2);
                    // 4-byte align
                    pos = (pos + 3) & ~3;
                }
                else
                {
                    pos += 4;
                }
            }

            return results;
        }

        private static bool LooksLikeSubmesh(ReadOnlySpan<byte> data, int pos, int end, out ushort numVerts, out ushort numIndices)
        {
            numVerts = 0;
            numIndices = 0;

            if (pos + 20 > end) return false;

            numVerts = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pos + 16, 2));
            if (numVerts is 0 or > 20000) return false;

            int vertStride = 36;
            int idxHeaderPos = pos + 20 + (numVerts * vertStride);
            if (idxHeaderPos + 4 > end) return false;

            numIndices = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(idxHeaderPos, 2));
            if (numIndices is 0 or > 60000) return false;

            int submeshEnd = idxHeaderPos + 4 + (numIndices * 2);
            return submeshEnd <= end;
        }

        private static string ReadCString(ReadOnlySpan<byte> span)
        {
            int end = 0;
            while (end < span.Length && span[end] != 0) end++;
            return Encoding.ASCII.GetString(span.Slice(0, end)).Trim();
        }
    }
}
