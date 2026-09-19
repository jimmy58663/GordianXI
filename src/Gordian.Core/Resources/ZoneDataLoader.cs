// src/Gordian.Core/Resources/ZoneDataLoader.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Gordian.Core.Diagnostics;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Resources
{
    /// <summary>
    /// Clean-room loader and section parser for FFXI Zone DAT files.
    /// Extracts 3D zone terrain geometry (Section 0x2E) and texture palettes (Section 0x20).
    /// Derived from community specifications in xi-model-viewer (https://github.com/vekien/xi-model-viewer)
    /// and LandSandBoat (https://github.com/LandSandBoat/server).
    /// </summary>
    public static class ZoneDataLoader
    {
        private const int ModelBaseHi = 0x147B3;

        /// <summary>
        /// Calculates the canonical FFXI ROM File ID for a zone's 3D model container DAT.
        /// </summary>
        public static int GetZoneModelFileId(int zoneId)
        {
            if (zoneId < 256)
            {
                return 100 + zoneId;
            }

            return ModelBaseHi + (zoneId - 256);
        }

        /// <summary>
        /// Parses an entire Zone DAT container buffer into structured ZoneGeometry and DecodedTextures.
        /// </summary>
        public static ZoneGeometry ParseZoneContainer(
            ReadOnlySpan<byte> datBytes,
            int zoneId,
            ReadOnlySpan<byte> table1 = default,
            ReadOnlySpan<byte> table2 = default,
            Dictionary<string, DecodedTexture>? outTextures = null)
        {
            var zone = new ZoneGeometry { ZoneId = zoneId };
            var headers = DatSectionWalker.ReadHeaders(datBytes);

            int texCount = 0, meshSectionCount = 0;
            var templates = new Dictionary<string, List<MeshGroup>>(StringComparer.OrdinalIgnoreCase);
            var realMeshNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var raw0x2ESubmeshes = new List<MeshGroup>();
            DatSectionHeader? zoneDefHeader = null;

            void RegisterTemplate(string name, List<MeshGroup> meshList, bool isRealName)
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                if (isRealName) realMeshNames.Add(name);

                if (templates.TryGetValue(name, out var existing))
                {
                    int existingVerts = 0;
                    for (int v = 0; v < existing.Count; v++) existingVerts += existing[v].Vertices.Length;
                    int newVerts = 0;
                    for (int v = 0; v < meshList.Count; v++) newVerts += meshList[v].Vertices.Length;

                    if (newVerts > existingVerts)
                    {
                        templates[name] = meshList;
                    }
                }
                else
                {
                    templates[name] = meshList;
                }
            }

            for (int i = 0; i < headers.Count; i++)
            {
                var header = headers[i];
                if (header.DataOffset + header.DataSizeBytes > datBytes.Length)
                {
                    continue;
                }

                var payload = datBytes.Slice(header.DataOffset, header.DataSizeBytes);

                switch (header.TypeCode)
                {
                    case DatSectionType.Texture:
                    {
                        texCount++;
                        var texture = TextureDecoder.DecodeTexture(payload);
                        if (texture != null && outTextures != null)
                        {
                            outTextures[texture.Name] = texture;
                            string trimmed = texture.Name.Trim();
                            if (!string.IsNullOrEmpty(trimmed) && !outTextures.ContainsKey(trimmed))
                            {
                                outTextures[trimmed] = texture;
                            }
                            if (texture.Name.Length > 8)
                            {
                                string shortName = texture.Name.Substring(8).Trim();
                                if (!string.IsNullOrEmpty(shortName) && !outTextures.ContainsKey(shortName))
                                {
                                    outTextures[shortName] = texture;
                                }
                            }
                        }
                        break;
                    }

                    case DatSectionType.ZoneMesh:
                    {
                        meshSectionCount++;
                        byte[] workingCopy = payload.ToArray();

                        // If key tables provided, attempt decryption
                        if (!table1.IsEmpty && !table2.IsEmpty && workingCopy.Length >= 16)
                        {
                            ZoneMeshDecoder.DecryptZoneMesh(workingCopy, table1, table2);
                        }

                        var submeshes = ZoneMeshDecoder.ParseZoneMesh(workingCopy);
                        if (submeshes.Count > 0)
                        {
                            raw0x2ESubmeshes.AddRange(submeshes);
                            string primaryName = submeshes[0].Name;
                            RegisterTemplate(primaryName, submeshes, isRealName: true);

                            int spaceIdx = primaryName.LastIndexOf(' ');
                            if (spaceIdx >= 0 && spaceIdx < primaryName.Length - 1)
                            {
                                string tail = primaryName.Substring(spaceIdx + 1).Trim();
                                if (!string.IsNullOrEmpty(tail))
                                {
                                    RegisterTemplate(tail, submeshes, isRealName: true);
                                }
                            }

                            if (!string.IsNullOrEmpty(header.DatId) && !header.DatId.Equals(primaryName, StringComparison.OrdinalIgnoreCase))
                            {
                                RegisterTemplate(header.DatId, submeshes, isRealName: false);
                            }
                        }
                        break;
                    }

                    case DatSectionType.ZoneDef:
                    {
                        zoneDefHeader ??= header;
                        break;
                    }
                }
            }

            // Phase 2: World Placement Instancing via Section 0x1C (ZoneDef)
            int placedCount = 0;
            if (zoneDefHeader.HasValue && !table1.IsEmpty)
            {
                var zdHeader = zoneDefHeader.Value;
                byte[] zdPayload = datBytes.Slice(zdHeader.DataOffset, zdHeader.DataSizeBytes).ToArray();
                int nodeCount = ZoneDefDecoder.DecryptZoneObjects(zdPayload, table1);
                var placements = ZoneDefDecoder.ParseZonePlacements(zdPayload, nodeCount);
                zone.Placements.AddRange(placements);

                for (int p = 0; p < placements.Count; p++)
                {
                    var placement = placements[p];
                    if (ZoneDefDecoder.IsSkyMesh(placement.MeshId)) continue;

                    var templateSubmeshes = ZoneDefDecoder.ResolveTemplate(placement.MeshId, templates, realMeshNames);
                    if (templateSubmeshes == null || templateSubmeshes.Count == 0) continue;

                    var trsMatrix = ZoneDefDecoder.CreateTrsMatrix(placement.Position, placement.Rotation, placement.Scale);

                    for (int s = 0; s < templateSubmeshes.Count; s++)
                    {
                        var instantiated = ZoneDefDecoder.InstantiateSubmesh(templateSubmeshes[s], trsMatrix, placement.MeshId);
                        zone.MeshGroups.Add(instantiated);
                        placedCount++;
                    }
                }
            }

            // Fallback: If no placements were instantiated (e.g. non-world DAT or unit test without 0x1C)
            if (placedCount == 0 && raw0x2ESubmeshes.Count > 0)
            {
                for (int i = 0; i < raw0x2ESubmeshes.Count; i++)
                {
                    var raw = raw0x2ESubmeshes[i];
                    var convVerts = new MeshVertex[raw.Vertices.Length];
                    Vector3 minB = new(float.MaxValue);
                    Vector3 maxB = new(float.MinValue);
                    for (int v = 0; v < raw.Vertices.Length; v++)
                    {
                        var sv = raw.Vertices[v];
                        var dp = new Vector3(-sv.Position.X, -sv.Position.Y, sv.Position.Z);
                        var dn = new Vector3(-sv.Normal.X, -sv.Normal.Y, sv.Normal.Z);
                        minB = Vector3.Min(minB, dp);
                        maxB = Vector3.Max(maxB, dp);
                        convVerts[v] = new MeshVertex(dp, dn, sv.TexCoord, sv.ColorRgba);
                    }
                    zone.MeshGroups.Add(new MeshGroup
                    {
                        Name = raw.Name,
                        TextureName = raw.TextureName,
                        Vertices = convVerts,
                        Indices = raw.Indices,
                        MinBounds = minB,
                        MaxBounds = maxB,
                        IsBlend = raw.IsBlend,
                        NoCull = raw.NoCull,
                        IsFoliage = raw.IsFoliage
                    });
                }
            }

            GordianLog.Debug("RES", $"ParseZoneContainer(zone={zoneId}): {headers.Count} sections, {texCount} textures, {meshSectionCount} ZoneMesh(0x2E) sections, {zone.Placements.Count} placements → {zone.MeshGroups.Count} submeshes (placed={placedCount}). keysProvided={!table1.IsEmpty && !table2.IsEmpty}");

            return zone;
        }

        /// <summary>
        /// Attempts to scan FFXiMain.dll or FFXiMain.dll.orig within the game directory
        /// to locate the two 256-byte substitution decryption key tables.
        /// </summary>
        public static bool TryExtractKeyTables(
            string gameDirectory,
            out byte[] table1,
            out byte[] table2)
        {
            table1 = Array.Empty<byte>();
            table2 = Array.Empty<byte>();

            if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory))
            {
                return false;
            }

            string[] candidateDlls =
            {
                Path.Combine(gameDirectory, "FFXiMain.dll"),
                Path.Combine(gameDirectory, "FFXiMain.dll.orig"),
            };

            foreach (var dllPath in candidateDlls)
            {
                if (!File.Exists(dllPath)) continue;

                try
                {
                    byte[] dllBytes = File.ReadAllBytes(dllPath);
                    if (TryExtractFromDllBytes(dllBytes, out table1, out table2))
                    {
                        GordianLog.Info("RES", $"Extracted zone decryption key tables from {Path.GetFileName(dllPath)}");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    GordianLog.Warning("RES", $"Could not read {dllPath} for key table extraction: {ex.Message}");
                }
            }

            return false;
        }

        /// <summary>
        /// Scans binary DLL memory bytes for Table1Sig and Table2Sig signatures.
        /// </summary>
        public static bool TryExtractFromDllBytes(
            ReadOnlySpan<byte> dllBytes,
            out byte[] table1,
            out byte[] table2)
        {
            table1 = Array.Empty<byte>();
            table2 = Array.Empty<byte>();

            const int scanStart = 0x30000;
            const int tableSize = 256;

            int idx1 = FindSignature(dllBytes, ZoneMeshDecoder.Table1Sig, scanStart);
            if (idx1 < 0) idx1 = FindSignature(dllBytes, ZoneMeshDecoder.Table1Sig, 0);

            int idx2 = FindSignature(dllBytes, ZoneMeshDecoder.Table2Sig, scanStart);
            if (idx2 < 0) idx2 = FindSignature(dllBytes, ZoneMeshDecoder.Table2Sig, 0);

            if (idx1 < 0 || idx2 < 0 || idx1 + tableSize > dllBytes.Length || idx2 + tableSize > dllBytes.Length)
            {
                return false;
            }

            table1 = dllBytes.Slice(idx1, tableSize).ToArray();
            table2 = dllBytes.Slice(idx2, tableSize).ToArray();
            return true;
        }

        private static int FindSignature(ReadOnlySpan<byte> data, ReadOnlySpan<byte> sig, int startOffset)
        {
            if (data.Length < sig.Length) return -1;
            int start = Math.Clamp(startOffset, 0, data.Length - sig.Length);

            for (int i = start; i <= data.Length - sig.Length; i++)
            {
                if (data.Slice(i, sig.Length).SequenceEqual(sig))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
