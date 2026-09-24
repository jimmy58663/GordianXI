// src/Gordian.Core/Resources/Graphics/ZoneDefDecoder.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// Clean-room binary decoder for FFXI Section 0x1C ZoneDef (Object Placement) tables.
    /// Decrypts keyed XOR stream, unmasks object names, extracts world transforms (TRS),
    /// and instantiates 0x2E mesh templates into the 3D game world.
    /// Protocol and binary chunk layout referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer)
    /// and xi-tools (xi/zone/xi_export.py).
    /// </summary>
    public static class ZoneDefDecoder
    {
        public const int StrideModern = 0x64; // 100 bytes (retail standard)
        public const int StrideProto = 0x54;  // 84 bytes (pre-production / prototype)

        /// <summary>
        /// Decrypts a Section 0x1C ZoneDef data payload in place using Table 1.
        /// </summary>
        /// <param name="payload">Section 0x1C payload data (excluding the 16-byte chunk header).</param>
        /// <param name="table1">256-byte primary substitution table extracted from FFXiMain.dll.</param>
        /// <returns>Number of placement nodes contained in the table.</returns>
        public static int DecryptZoneObjects(Span<byte> payload, ReadOnlySpan<byte> table1)
        {
            if (payload.Length < 8 || table1.Length < 256) return 0;

            uint meta0 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            uint meta4 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));

            byte mode = (byte)((meta0 >> 24) & 0xFF);
            int nodeCount = (int)(meta4 & 0x00FFFFFF);

            if (mode > 0x1A)
            {
                int decodeLength = (int)(meta0 & 0x00FFFFFF);
                byte keySeed = (byte)((meta4 >> 24) & 0xFF);
                long key = table1[keySeed ^ 0xFF];
                long counter = 0;
                int consumed = 0;
                int p = 8;
                int sectionEnd = payload.Length;

                if (p + decodeLength > sectionEnd)
                {
                    decodeLength = Math.Max(0, sectionEnd - p);
                }

                while (consumed < decodeLength)
                {
                    int xorLength = (int)(((key >> 4) & 7) + 16);
                    bool applyMask = (key & 1) != 0;
                    bool hasRemaining = consumed + xorLength < decodeLength;

                    if (applyMask && hasRemaining)
                    {
                        for (int k = 0; k < xorLength && (p + k) < sectionEnd; k++)
                        {
                            payload[p + k] ^= 0xFF;
                        }
                    }

                    p += xorLength;
                    counter++;
                    key += counter;
                    consumed += xorLength;
                }
            }

            // Names are still XOR-masked here; detection must unmask them or '_' (0x0A masked) reads as a control byte.
            int stride = DetectObjectStride(payload, nodeCount, namesMasked: true);

            // Name unmasking: XOR 0x55 across each object's 16-byte name
            int namePos = 0x20;
            for (int i = 0; i < nodeCount; i++)
            {
                int baseOffset = namePos + (i * stride);
                if (baseOffset + 16 > payload.Length) break;

                for (int j = 0; j < 16; j++)
                {
                    payload[baseOffset + j] ^= 0x55;
                }
            }

            return nodeCount;
        }

        /// <summary>
        /// Detects whether the placement table uses modern 100-byte (0x64) or prototype 84-byte (0x54) records.
        /// </summary>
        /// <param name="namesMasked">True when object names still carry their 0x55 XOR mask.</param>
        public static int DetectObjectStride(ReadOnlySpan<byte> payload, int nodeCount, bool namesMasked = false)
        {
            byte mask = namesMasked ? (byte)0x55 : (byte)0;
            if (nodeCount < 2) return StrideModern;

            int limit = Math.Min(nodeCount, 32);
            int score54 = 0;
            int score64 = 0;

            for (int i = 0; i < limit; i++)
            {
                if (IsPrintableName(payload, 0x20 + (i * StrideProto), mask)) score54++;
                if (IsPrintableName(payload, 0x20 + (i * StrideModern), mask)) score64++;
            }

            if (score64 > score54 + 2) return StrideModern;
            if (score54 > score64 + 2) return StrideProto;

            return StrideModern;
        }

        private static bool IsPrintableName(ReadOnlySpan<byte> payload, int offset, byte mask)
        {
            if (offset + 16 > payload.Length) return false;
            int printable = 0;
            for (int i = 0; i < 16; i++)
            {
                byte c = (byte)(payload[offset + i] ^ mask);
                if (c == 0) break;
                if (c < 0x20 || c > 0x7E) return false;
                printable++;
            }
            return printable >= 2;
        }

        /// <summary>
        /// Parses decrypted Section 0x1C payload into structured ZonePlacement records.
        /// </summary>
        public static List<ZonePlacement> ParseZonePlacements(ReadOnlySpan<byte> payload, int nodeCount)
        {
            var placements = new List<ZonePlacement>(nodeCount);
            if (payload.Length < 0x20 || nodeCount <= 0) return placements;

            int stride = DetectObjectStride(payload, nodeCount);
            int namePos = 0x20;

            for (int i = 0; i < nodeCount; i++)
            {
                int b = namePos + (i * stride);
                if (b + 0x34 > payload.Length) break;

                string meshId = ReadCString(payload.Slice(b, 16));
                if (string.IsNullOrEmpty(meshId)) continue;

                float px = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(b + 0x10, 4));
                float py = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(b + 0x14, 4));
                float pz = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(b + 0x18, 4));

                // Filter deleted/hidden instances (FFXI hides deleted objects at y ≈ -100,000)
                if (py <= -90000.0f || float.IsNaN(px) || float.IsNaN(py) || float.IsNaN(pz) ||
                    float.IsInfinity(px) || float.IsInfinity(py) || float.IsInfinity(pz))
                {
                    continue;
                }

                float rx = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(b + 0x1C, 4));
                float ry = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(b + 0x20, 4));
                float rz = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(b + 0x24, 4));

                float sx = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(b + 0x28, 4));
                float sy = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(b + 0x2C, 4));
                float sz = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(b + 0x30, 4));

                float drawDist = 0.0f;
                if (stride >= StrideModern && b + 0x44 <= payload.Length)
                {
                    drawDist = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(b + 0x40, 4));
                }

                // 1.0 is the authoring sentinel for "collision only proxy, never rendered"
                if (MathF.Abs(drawDist - 1.0f) < 0.001f)
                {
                    continue;
                }

                placements.Add(new ZonePlacement(
                    meshId,
                    new Vector3(px, py, pz),
                    new Vector3(rx, ry, rz),
                    new Vector3(sx, sy, sz),
                    drawDist
                ));
            }

            return placements;
        }

        /// <summary>
        /// Creates a TRS transformation matrix matching FFXI's column-major T * Rz * Ry * Rx * S convention.
        /// </summary>
        public static Matrix4x4 CreateTrsMatrix(Vector3 pos, Vector3 rot, Vector3 scale)
        {
            float rx = rot.X, ry = rot.Y, rz = rot.Z;
            float sx = scale.X, sy = scale.Y, sz = scale.Z;
            float sinx = MathF.Sin(rx), siny = MathF.Sin(ry), sinz = MathF.Sin(rz);
            float cosx = MathF.Cos(rx), cosy = MathF.Cos(ry), cosz = MathF.Cos(rz);

            return new Matrix4x4(
                (cosy * cosz) * sx,
                (cosy * sinz) * sx,
                -siny * sx,
                0f,

                (sinx * siny * cosz - cosx * sinz) * sy,
                (sinx * siny * sinz + cosx * cosz) * sy,
                (sinx * cosy) * sy,
                0f,

                (cosx * siny * cosz + sinx * sinz) * sz,
                (cosx * siny * sinz - sinx * cosz) * sz,
                (cosx * cosy) * sz,
                0f,

                pos.X,
                pos.Y,
                pos.Z,
                1f
            );
        }

        /// <summary>
        /// Instantiates a template submesh into world space using the placement's TRS transform.
        /// </summary>
        public static MeshGroup InstantiateSubmesh(MeshGroup template, in Matrix4x4 transform, string placementName)
        {
            var srcVerts = template.Vertices;
            var dstVerts = new MeshVertex[srcVerts.Length];

            Vector3 minBounds = new(float.MaxValue);
            Vector3 maxBounds = new(float.MinValue);

            for (int v = 0; v < srcVerts.Length; v++)
            {
                var sv = srcVerts[v];
                var worldPos = Vector3.Transform(sv.Position, transform);
                var worldNormal = Vector3.Normalize(Vector3.TransformNormal(sv.Normal, transform));

                if (float.IsNaN(worldNormal.X) || float.IsNaN(worldNormal.Y) || float.IsNaN(worldNormal.Z))
                {
                    worldNormal = sv.Normal;
                }

                // Retail FFXI level editor toDisplay transform: (-x, -y, z).
                // Mapped into canonical +Y-up display space (180° rotation about Z).
                var displayPos = new Vector3(-worldPos.X, -worldPos.Y, worldPos.Z);
                var displayNormal = new Vector3(-worldNormal.X, -worldNormal.Y, worldNormal.Z);

                minBounds = Vector3.Min(minBounds, displayPos);
                maxBounds = Vector3.Max(maxBounds, displayPos);

                dstVerts[v] = new MeshVertex(displayPos, displayNormal, sv.TexCoord, sv.ColorRgba);
            }

            int[] indices = new int[template.Indices.Length];
            Array.Copy(template.Indices, indices, template.Indices.Length);

            return new MeshGroup
            {
                Name = $"{template.Name}_{placementName}",
                TextureName = template.TextureName,
                Vertices = dstVerts,
                Indices = indices,
                MinBounds = minBounds,
                MaxBounds = maxBounds,
                IsBlend = template.IsBlend,
                NoCull = template.NoCull,
                IsFoliage = template.IsFoliage || template.Name.StartsWith("_") || placementName.StartsWith("_"),
                IsWater = template.IsWater || IsWaterMesh(template.Name, template.TextureName) || IsWaterMesh(placementName, template.TextureName)
            };
        }

        private static readonly string[] SkyPrefixes =
        {
            "sun", "moon", "star", "clod", "cld", "cloud", "kamo", "suny", "sora", "dust", "fogd", "fog", "haze", "mist", "ykum"
        };

        /// <summary>
        /// Checks whether a mesh or texture name represents an ocean, sea, river, or water surface.
        /// Protocol specification and naming conventions referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer).
        /// </summary>
        public static bool IsWaterMesh(string meshName, string textureName)
        {
            string n = (meshName ?? string.Empty).ToLowerInvariant();
            string t = (textureName ?? string.Empty).ToLowerInvariant();

            if (n == "lowsea" || n == "2lowsea" || n == "lowcol" || n == "suimen" || n == "tamadai")
                return true;
            if (n.StartsWith("sea") || n.StartsWith("water") || n.StartsWith("ocean") || n.Contains("suimen") || n.EndsWith("sea"))
                return true;
            if (n.StartsWith("umw") || n.StartsWith("uma") || n.StartsWith("umb") || n.StartsWith("umn") || n.StartsWith("ucks"))
                return true;
            if (t.Contains("water") || t.Contains("sea") || t.Contains("suimen") || t.Contains("river") ||
                t.Contains("umi") || t.Contains("umw") || t.Contains("nami") || t.Contains("kiwa") ||
                t.Contains("shir") || t.Contains("quf") || t.Contains("kawa"))
                return true;

            return false;
        }

        /// <summary>
        /// Checks whether a mesh name represents dynamic weather/sky geometry that should not be baked into static world terrain.
        /// Derived from xi-model-viewer (https://github.com/vekien/xi-model-viewer) and xi-tools (https://github.com/vekien/xi-tools).
        /// </summary>
        public static bool IsSkyMesh(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            if (n.StartsWith("suna")) return false; // Wall texture (sunakabe), not sky

            for (int i = 0; i < SkyPrefixes.Length; i++)
            {
                if (n.StartsWith(SkyPrefixes[i])) return true;
            }
            return false;
        }

        /// <summary>
        /// Checks whether a mesh name represents a celestial body (Sun, Moon, Stars, celestial sphere, halo).
        /// Note that 'suny_*' represents sunshine cloud layers, not celestial bodies.
        /// Protocol specification referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer).
        /// </summary>
        public static bool IsCelestialMesh(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            if (n.StartsWith("suny") || n.StartsWith("suna")) return false;
            return n.StartsWith("sun") || n.StartsWith("moon") || n.StartsWith("star") || n.Contains("sphere") || n.StartsWith("kasa");
        }


        private static string Norm(string s) => s.Replace(" ", "").Replace("_", "").ToLowerInvariant();

        /// <summary>
        /// Resolves a placement meshId to the best matching template mesh group list.
        /// Handles LOD detail suffixes (_h, _m, _l), exact names, aliases, and normalized fuzzy matches.
        /// Clean-room implementation referencing xi-model-viewer (https://github.com/vekien/xi-model-viewer).
        /// </summary>
        public static List<MeshGroup>? ResolveTemplate(
            string meshId,
            IReadOnlyDictionary<string, List<MeshGroup>> templates,
            HashSet<string>? realMeshNames = null)
        {
            if (string.IsNullOrEmpty(meshId)) return null;

            // 1. LOD suffix resolution: prefer highest detail variant (_h, _m, _l)
            if (meshId.Length >= 2 && (meshId.EndsWith("_l", StringComparison.OrdinalIgnoreCase) ||
                                       meshId.EndsWith("_m", StringComparison.OrdinalIgnoreCase) ||
                                       meshId.EndsWith("_h", StringComparison.OrdinalIgnoreCase)))
            {
                string baseName = meshId.Substring(0, meshId.Length - 2);
                if (templates.TryGetValue(baseName + "_h", out var hList)) return hList;
                if (templates.TryGetValue(baseName + "_m", out var mList)) return mList;
                if (templates.TryGetValue(baseName + "_l", out var lList)) return lList;
            }

            // 2. Direct exact match
            if (templates.TryGetValue(meshId, out var directList)) return directList;

            // 3. Try appending detail suffix _h, _m, _l
            if (templates.TryGetValue(meshId + "_h", out var hList2)) return hList2;
            if (templates.TryGetValue(meshId + "_m", out var mList2)) return mList2;
            if (templates.TryGetValue(meshId + "_l", out var lList2)) return lList2;

            // 4. Normalized exact match (ignoring spaces and underscores)
            string want = Norm(meshId);
            if (want.Length < 2) return null;

            foreach (var (key, list) in templates)
            {
                if (Norm(key) == want) return list;
            }

            // 5. Fuzzy match on tail / endsWith (for MIN_FUZZ >= 4)
            if (want.Length >= 4)
            {
                string lowId = meshId.ToLowerInvariant();
                foreach (var (key, list) in templates)
                {
                    if (realMeshNames != null && !realMeshNames.Contains(key)) continue;

                    string k = Norm(key);
                    if (k.Length < 4) continue;

                    if (k.EndsWith(want, StringComparison.OrdinalIgnoreCase)) return list;
                    if (want.EndsWith(k, StringComparison.OrdinalIgnoreCase))
                    {
                        string rawTail = key.ToLowerInvariant().TrimStart('_', ' ');
                        if (lowId.EndsWith(rawTail, StringComparison.OrdinalIgnoreCase)) return list;
                    }
                }
            }

            return null;
        }

        private static string ReadCString(ReadOnlySpan<byte> span)
        {
            int end = 0;
            while (end < span.Length && span[end] != 0) end++;
            return Encoding.ASCII.GetString(span.Slice(0, end)).Trim();
        }
    }
}
