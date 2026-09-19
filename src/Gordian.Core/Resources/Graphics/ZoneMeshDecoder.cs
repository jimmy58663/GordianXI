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
            // Note: key and counter must be 64-bit integers (long). At ~32,768 bytes, a 32-bit signed
            // int overflows into negative numbers, causing negative modulo and bitshifts that corrupt
            // all bytes in the second half of any section > 32KB.
            if (mode >= 5)
            {
                byte seed = (byte)(payload[5] ^ 0xF0);
                long key = table1[seed];
                long counter = 0;
                int p = 8;

                for (int i = 0; i < decodeLength; i++)
                {
                    long kb = key % 256;
                    long keyMod = (kb << 8) | kb;
                    counter++;
                    key += counter;
                    int shift = (int)(key % 8);
                    payload[p + i] ^= (byte)((keyMod >> shift) & 0xFF);
                    counter++;
                    key += counter;
                }
            }

            // Pass 2: Conditional swap of 8-byte blocks between the two body halves
            ushort flag = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2));
            if (flag == 0xFFFF)
            {
                long key1 = payload[5] ^ 0xF0;
                long key2 = table2[(int)key1];
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
        /// Clean-room implementation referencing xi-model-viewer (https://github.com/vekien/xi-model-viewer)
        /// and xi-tools (xi/zone/xi_export.py).
        /// Handles vertexBlend stride (48 vs 36) and converts triangle strips into TriangleList indices.
        /// Vertex coordinates are passed through unmodified to match the network/world coordinate
        /// frame used by WorldEntity.Position (see EntityPacketModule).
        /// </summary>
        public static List<MeshGroup> ParseZoneMesh(ReadOnlySpan<byte> payload)
        {
            var results = new List<MeshGroup>();
            if (payload.Length < 0x40) return results;

            uint config = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
            bool isStrip = (config & 0x1) != 0;
            bool vertexBlend = (config & 0x2) != 0;
            int stride = vertexBlend ? 48 : 36;

            string meshName = ReadCString(payload.Slice(0x10, 16));

            int defStart = 0x20;
            if (payload.Length < defStart + 4) return results;
            uint meshCount0 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(defStart, 4));
            if (meshCount0 == 0) return results; // collision-only "hit" model

            int section1Off = payload.Length >= defStart + 0x20
                ? (int)BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(defStart + 0x1C, 4))
                : 0;

            int payloadLen = payload.Length;
            bool OffOk(int v) => v > 0 && v < payloadLen - defStart;

            if (!OffOk(section1Off) || section1Off > 0x200)
            {
                int[] altOffsets = { 0x4C, 0x5C, 0x50, 0x58 };
                foreach (int at in altOffsets)
                {
                    if (at + 4 <= payload.Length)
                    {
                        int alt = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(at, 4));
                        if (OffOk(alt) && alt <= 0x200)
                        {
                            section1Off = alt;
                            break;
                        }
                    }
                }
            }

            int startPos = OffOk(section1Off) ? defStart + section1Off : 0;
            if (startPos == 0)
            {
                // Fallback: scan for first valid submesh header in payload
                for (int scan = defStart; scan + 20 <= payload.Length; scan += 4)
                {
                    if (LooksLikeSubmesh(payload, scan, payload.Length, stride, out _, out _))
                    {
                        startPos = scan;
                        break;
                    }
                }
            }

            if (startPos == 0 || startPos + 20 > payload.Length) return results;

            int p = startPos;
            int len = payload.Length;

            for (int m = 0; m < 128 && p + 20 <= len; m++)
            {
                if (!LooksLikeSubmesh(payload, p, len, stride, out ushort numVerts, out ushort numIndices))
                {
                    break;
                }

                string texName = ReadCString(payload.Slice(p, 16));
                ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(p + 18, 2));
                bool blend = (flags & 0x8000) != 0;
                bool noCull = (flags & 0x2000) != 0;

                int vertStart = p + 20;

                var vertices = new MeshVertex[numVerts];
                Vector3 minBounds = new(float.MaxValue);
                Vector3 maxBounds = new(float.MinValue);

                // A submesh is one compact architectural piece: all its vertices should cluster
                // tightly. A single decryption artifact blowing one vertex's coordinate out to an
                // extreme value must not discard the whole submesh, but a flat magnitude threshold
                // isn't reliable either (bad values have been seen ranging from the thousands up to
                // near float extremes). Instead, find the submesh's own median position first, then
                // clamp any vertex that's wildly far from its own cluster back to that median.
                Vector3 median = ComputeMedianPosition(payload, vertStart, numVerts, stride);
                const float maxDeviationFromMedian = 500f;

                for (int v = 0; v < numVerts; v++)
                {
                    int vo = vertStart + (v * stride);
                    float px = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(vo, 4));
                    float py = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(vo + 4, 4));
                    float pz = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(vo + 8, 4));

                    if (float.IsNaN(px) || float.IsInfinity(px) || MathF.Abs(px - median.X) > maxDeviationFromMedian) px = median.X;
                    if (float.IsNaN(py) || float.IsInfinity(py) || MathF.Abs(py - median.Y) > maxDeviationFromMedian) py = median.Y;
                    if (float.IsNaN(pz) || float.IsInfinity(pz) || MathF.Abs(pz - median.Z) > maxDeviationFromMedian) pz = median.Z;

                    float nx, ny, nz;
                    uint color;
                    float u, uv_v;

                    int cOff;
                    if (vertexBlend)
                    {
                        // Stride 48: pos(12), blendDelta(12), normal(12), color BGRA(4), uv(8)
                        nx = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(vo + 24, 4));
                        ny = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(vo + 28, 4));
                        nz = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(vo + 32, 4));
                        cOff = vo + 36;
                        u = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(vo + 40, 4));
                        uv_v = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(vo + 44, 4));
                    }
                    else
                    {
                        // Stride 36: pos(12), normal(12), color BGRA(4), uv(8)
                        nx = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(vo + 12, 4));
                        ny = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(vo + 16, 4));
                        nz = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(vo + 20, 4));
                        cOff = vo + 24;
                        u = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(vo + 28, 4));
                        uv_v = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(vo + 32, 4));
                    }

                    // Convert FFXI raw BGRA bytes to standard RGBA little-endian uint
                    byte cb = payload[cOff];
                    byte cg = payload[cOff + 1];
                    byte cr = payload[cOff + 2];
                    byte ca = payload[cOff + 3];
                    color = (uint)cr | ((uint)cg << 8) | ((uint)cb << 16) | ((uint)ca << 24);

                    if (float.IsNaN(nx) || float.IsInfinity(nx)) nx = 0f;
                    if (float.IsNaN(ny) || float.IsInfinity(ny)) ny = 0f;
                    if (float.IsNaN(nz) || float.IsInfinity(nz)) nz = 0f;

                    // Native FFXI coordinate frame (+Y up)
                    var position = new Vector3(px, py, pz);
                    var normal = new Vector3(nx, ny, nz);

                    minBounds = Vector3.Min(minBounds, position);
                    maxBounds = Vector3.Max(maxBounds, position);

                    vertices[v] = new MeshVertex(
                        position,
                        normal,
                        new Vector2(u, uv_v),
                        color
                    );
                }

                int idxHeader = vertStart + (numVerts * stride);
                int idxStart = idxHeader + 4;

                var rawIndices = new ushort[numIndices];
                for (int idx = 0; idx < numIndices; idx++)
                {
                    rawIndices[idx] = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(idxStart + (idx * 2), 2));
                }

                bool useStrip = isStrip || (numIndices > 3 && numIndices % 3 != 0);
                var triangleIndices = new List<int>(numIndices * 3);

                if (useStrip)
                {
                    int parity = 0;
                    for (int t = 0; t < rawIndices.Length - 2; t++)
                    {
                        int i0 = rawIndices[t];
                        int i1 = rawIndices[t + 1];
                        int i2 = rawIndices[t + 2];

                        if (i0 == i1 || i1 == i2 || i0 == i2)
                        {
                            parity = 0;
                            continue;
                        }

                        if (i0 >= numVerts || i1 >= numVerts || i2 >= numVerts)
                        {
                            parity = 0;
                            continue;
                        }

                        if (parity % 2 == 0)
                        {
                            triangleIndices.Add(i0);
                            triangleIndices.Add(i1);
                            triangleIndices.Add(i2);
                        }
                        else
                        {
                            triangleIndices.Add(i1);
                            triangleIndices.Add(i0);
                            triangleIndices.Add(i2);
                        }
                        parity++;
                    }
                }
                else
                {
                    for (int t = 0; t < rawIndices.Length - 2; t += 3)
                    {
                        int i0 = rawIndices[t];
                        int i1 = rawIndices[t + 1];
                        int i2 = rawIndices[t + 2];

                        if (i0 < numVerts && i1 < numVerts && i2 < numVerts)
                        {
                            triangleIndices.Add(i0);
                            triangleIndices.Add(i1);
                            triangleIndices.Add(i2);
                        }
                    }
                }

                if (triangleIndices.Count > 0)
                {
                    results.Add(new MeshGroup
                    {
                        Name = meshName,
                        TextureName = texName,
                        Vertices = vertices,
                        Indices = triangleIndices.ToArray(),
                        MinBounds = minBounds,
                        MaxBounds = maxBounds,
                        IsBlend = blend,
                        NoCull = noCull,
                        IsFoliage = meshName.StartsWith("_")
                    });
                }

                p = idxStart + (numIndices * 2);
                p = (p + 3) & ~3; // 4-byte align
            }

            return results;
        }

        /// <summary>
        /// Computes the per-axis median position across a submesh's raw vertex stream, used as a
        /// robust (outlier-resistant) reference point for clamping individually corrupted vertices.
        /// </summary>
        private static Vector3 ComputeMedianPosition(ReadOnlySpan<byte> payload, int vertStart, int numVerts, int stride)
        {
            if (numVerts <= 0) return Vector3.Zero;

            Span<float> xs = numVerts <= 2048 ? stackalloc float[numVerts] : new float[numVerts];
            Span<float> ys = numVerts <= 2048 ? stackalloc float[numVerts] : new float[numVerts];
            Span<float> zs = numVerts <= 2048 ? stackalloc float[numVerts] : new float[numVerts];

            for (int v = 0; v < numVerts; v++)
            {
                int vo = vertStart + (v * stride);
                float px = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(vo, 4));
                float py = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(vo + 4, 4));
                float pz = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(vo + 8, 4));
                xs[v] = float.IsFinite(px) ? px : 0f;
                ys[v] = float.IsFinite(py) ? py : 0f;
                zs[v] = float.IsFinite(pz) ? pz : 0f;
            }

            xs.Sort();
            ys.Sort();
            zs.Sort();
            int mid = numVerts / 2;
            return new Vector3(xs[mid], ys[mid], zs[mid]);
        }

        private static bool LooksLikeSubmesh(ReadOnlySpan<byte> data, int pos, int end, int stride, out ushort numVerts, out ushort numIndices)
        {
            numVerts = 0;
            numIndices = 0;

            if (pos + 20 > end) return false;

            // Texture name validation: 16 bytes ASCII or blank
            int printable = 0;
            bool blank = true;
            for (int i = 0; i < 16; i++)
            {
                byte c = data[pos + i];
                if (c is 0 or 0x20) continue;
                blank = false;
                if (c < 0x20 || c > 0x7E) return false;
                printable++;
            }
            if (!blank && printable < 2) return false;

            numVerts = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pos + 16, 2));
            if (numVerts is 0 or > 20000) return false;

            int idxHeaderPos = pos + 20 + (numVerts * stride);
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
