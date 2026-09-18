// src/Gordian.Core/Resources/EntityModelLoader.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Diagnostics;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;

namespace Gordian.Core.Resources
{
    /// <summary>
    /// Loads and stitches 3D entity models from FFXI DAT containers.
    /// Supports modular player character assembly (Race Skeleton + Face + Armor Slots + Weapons)
    /// and monolithic NPC/Monster/Trust model loading.
    /// Clean-room implementation referencing FFXI entity model specifications.
    /// </summary>
    public static class EntityModelLoader
    {
        public readonly record struct RawDatContainer(
            Skeleton? Skeleton,
            List<SkeletonMeshGroup> Meshes,
            Dictionary<string, DecodedTexture> Textures
        );

        /// <summary>
        /// Reads and extracts Skeleton (0x29), SkeletonMesh (0x2A), and Texture (0x20) sections from a raw DAT payload.
        /// </summary>
        public static RawDatContainer ParseDatContainer(ReadOnlySpan<byte> datBytes, string sourceName = "")
        {
            var meshes = new List<SkeletonMeshGroup>();
            var textures = new Dictionary<string, DecodedTexture>(StringComparer.OrdinalIgnoreCase);
            Skeleton? skeleton = null;

            var headers = DatSectionWalker.ReadHeaders(datBytes);
            for (int i = 0; i < headers.Count; i++)
            {
                var h = headers[i];
                if (h.DataOffset + h.DataSizeBytes > datBytes.Length) continue;

                var payload = datBytes.Slice(h.DataOffset, h.DataSizeBytes);

                switch (h.TypeCode)
                {
                    case DatSectionType.Skeleton:
                        if (skeleton == null)
                        {
                            skeleton = SkeletonDecoder.DecodeSkeleton(payload);
                        }
                        break;

                    case DatSectionType.SkeletonMesh:
                        var mesh = SkeletonMeshDecoder.DecodeMesh(payload, sourceName);
                        if (mesh != null)
                        {
                            meshes.Add(mesh);
                        }
                        break;

                    case DatSectionType.Texture:
                        var tex = TextureDecoder.DecodeTexture(payload);
                        if (tex != null && !textures.ContainsKey(tex.Name))
                        {
                            textures[tex.Name] = tex;
                        }
                        break;
                }
            }

            return new RawDatContainer(skeleton, meshes, textures);
        }

        /// <summary>
        /// Assembles an EntityModel from a primary skeleton container and any number of modular part containers.
        /// </summary>
        public static EntityModel AssembleModel(
            ReadOnlySpan<byte> primaryDat,
            IReadOnlyList<ReadOnlyMemory<byte>>? extraDats = null,
            string name = "")
        {
            var model = new EntityModel { Name = name };

            var primary = ParseDatContainer(primaryDat, name);
            model.Skeleton = primary.Skeleton;

            foreach (var kvp in primary.Textures)
            {
                model.Textures[kvp.Key] = kvp.Value;
            }

            var allMeshes = new List<SkeletonMeshGroup>(primary.Meshes);

            if (extraDats != null)
            {
                for (int i = 0; i < extraDats.Count; i++)
                {
                    var extra = ParseDatContainer(extraDats[i].Span, $"Part_{i}");
                    if (model.Skeleton == null && extra.Skeleton != null)
                    {
                        model.Skeleton = extra.Skeleton;
                    }

                    foreach (var kvp in extra.Textures)
                    {
                        model.Textures[kvp.Key] = kvp.Value;
                    }

                    allMeshes.AddRange(extra.Meshes);
                }
            }

            if (model.Skeleton != null && allMeshes.Count > 0)
            {
                var bindPose = SkeletonPoseEvaluator.ComputeBindPose(model.Skeleton);
                for (int m = 0; m < allMeshes.Count; m++)
                {
                    var evaluated = SkeletonPoseEvaluator.EvaluateMeshGroup(allMeshes[m], bindPose);
                    model.MeshGroups.AddRange(evaluated);
                }
            }

            model.UpdateBounds();
            return model;
        }

        /// <summary>
        /// Modular character assembler stitching Race base skeleton, Face, and Armor/Weapon slots into a unified model.
        /// </summary>
        public static EntityModel? AssembleCharacter(
            CharacterRace race,
            ushort faceModel,
            ReadOnlySpan<ushort> grapTable,
            Func<string, byte[]?> datByPath,
            Func<int, byte[]?> datByFileId)
        {
            if (race == CharacterRace.Unknown)
            {
                return null;
            }

            string baseSkelPath = CharacterEquipmentResolver.GetBaseSkeletonPath(race);
            if (string.IsNullOrEmpty(baseSkelPath)) return null;

            byte[]? baseDat = datByPath(baseSkelPath);
            if (baseDat == null || baseDat.Length == 0)
            {
                GordianLog.Warning("RES", $"Could not load base skeleton DAT for {race} at '{baseSkelPath}'");
                return null;
            }

            var extraDats = new List<ReadOnlyMemory<byte>>();

            // 1. Face slot (from GrapIdTable[0] & 0xFF)
            ushort faceId = (ushort)(faceModel & 0xFF);
            if (CharacterEquipmentResolver.TryResolveGearFileId(race, CharacterSlot.Face, faceId, out int faceFid))
            {
                byte[]? faceDat = datByFileId(faceFid);
                if (faceDat != null && faceDat.Length > 0)
                {
                    extraDats.Add(faceDat);
                }
            }

            // 2. Armor and Weapon slots (Head through Ranged)
            // Slot indices: 1:Head, 2:Body, 3:Hands, 4:Legs, 5:Feet, 6:Main, 7:Sub, 8:Ranged
            for (int slotIdx = 1; slotIdx < 9 && slotIdx < grapTable.Length; slotIdx++)
            {
                ushort rawVal = grapTable[slotIdx];
                ushort modelId = (ushort)(rawVal & 0x0FFF);
                if (modelId == 0) continue;

                var slot = (CharacterSlot)slotIdx;
                if (CharacterEquipmentResolver.TryResolveGearFileId(race, slot, modelId, out int gearFid))
                {
                    byte[]? gearDat = datByFileId(gearFid);
                    if (gearDat != null && gearDat.Length > 0)
                    {
                        extraDats.Add(gearDat);
                    }
                }
            }

            return AssembleModel(baseDat, extraDats, $"{race}_Face{faceId}");
        }

        /// <summary>
        /// Loads an NPC, Monster, or Trust entity model from its numeric ModelId.
        /// </summary>
        public static EntityModel? LoadMonsterModel(uint modelId, Func<int, byte[]?> datByFileId)
        {
            if (modelId == 0) return null;

            int fileId = CharacterEquipmentResolver.GetMonsterFileId(modelId);
            byte[]? dat = datByFileId(fileId);
            if (dat == null || dat.Length == 0)
            {
                return null;
            }

            return AssembleModel(dat, null, $"Monster_{modelId}");
        }
    }
}
