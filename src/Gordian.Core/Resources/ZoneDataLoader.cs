// src/Gordian.Core/Resources/ZoneDataLoader.cs
using System;
using System.Collections.Generic;
using System.IO;
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
                        for (int m = 0; m < submeshes.Count; m++)
                        {
                            zone.MeshGroups.Add(submeshes[m]);
                        }
                        break;
                    }
                }
            }

            GordianLog.Debug("RES", $"ParseZoneContainer(zone={zoneId}): {headers.Count} sections, {texCount} textures, {meshSectionCount} ZoneMesh(0x2E) sections → {zone.MeshGroups.Count} submeshes. keysProvided={!table1.IsEmpty && !table2.IsEmpty}");

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
