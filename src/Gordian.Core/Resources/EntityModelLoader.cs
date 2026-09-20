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
        /// <summary>
        /// Controls automatic loading of the base+1 (upper body) / base+3 (waist) locomotion packs and
        /// the battle pack in <see cref="AssembleCharacter"/>. Defaults to true.
        /// </summary>
        public static bool EnableSpeculativeMotionPacks { get; set; } = true;

        public readonly record struct RawDatContainer(
            Skeleton? Skeleton,
            List<SkeletonMeshGroup> Meshes,
            Dictionary<string, DecodedTexture> Textures,
            List<AnimationClip> Animations
        );

        /// <summary>
        /// Reads and extracts Skeleton (0x29), SkeletonMesh (0x2A), SkeletonAnimation (0x2B), and
        /// Texture (0x20) sections from a raw DAT payload.
        /// </summary>
        public static RawDatContainer ParseDatContainer(ReadOnlySpan<byte> datBytes, string sourceName = "")
        {
            var meshes = new List<SkeletonMeshGroup>();
            var textures = new Dictionary<string, DecodedTexture>(StringComparer.OrdinalIgnoreCase);
            var animations = new List<AnimationClip>();
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

                    case DatSectionType.SkeletonAnimation:
                        var clip = SkeletonAnimationDecoder.DecodeClip(payload, h.DatId);
                        if (clip != null)
                        {
                            animations.Add(clip);
                        }
                        break;

                    case DatSectionType.Texture:
                        var tex = TextureDecoder.DecodeTexture(payload);
                        if (tex != null)
                        {
                            if (!textures.ContainsKey(tex.Name))
                            {
                                textures[tex.Name] = tex;
                            }
                            if (tex.Name.Length > 8)
                            {
                                string shortName = tex.Name.Substring(8).Trim();
                                if (!string.IsNullOrEmpty(shortName) && !textures.ContainsKey(shortName))
                                {
                                    textures[shortName] = tex;
                                }
                            }
                        }
                        break;
                }
            }

            return new RawDatContainer(skeleton, meshes, textures, animations);
        }

        /// <summary>
        /// Assembles an EntityModel from a primary skeleton container and any number of modular part containers.
        /// Supports optional skeletal joint parent overrides for weapon attachments.
        /// </summary>
        public static EntityModel AssembleModel(
            ReadOnlySpan<byte> primaryDat,
            IReadOnlyList<ReadOnlyMemory<byte>>? extraDats = null,
            string name = "",
            IReadOnlyDictionary<int, int>? parentOverrides = null)
        {
            var model = new EntityModel { Name = name };

            var primary = ParseDatContainer(primaryDat, name);
            model.Skeleton = primary.Skeleton;
            model.ParentOverrides = parentOverrides;

            foreach (var kvp in primary.Textures)
            {
                model.Textures[kvp.Key] = kvp.Value;
            }

            foreach (var clip in primary.Animations)
            {
                model.Animations[clip.Name] = clip;
                string stripped = StripBodyRegionSuffix(clip.Name);
                if (!model.Animations.ContainsKey(stripped))
                {
                    model.Animations[stripped] = clip;
                }
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

                    foreach (var clip in extra.Animations)
                    {
                        model.Animations[clip.Name] = clip;
                    }

                    allMeshes.AddRange(extra.Meshes);
                }
            }

            if (model.Skeleton != null && allMeshes.Count > 0)
            {
                var bindPose = SkeletonPoseEvaluator.ComputeBindPose(model.Skeleton, parentOverrides);
                for (int m = 0; m < allMeshes.Count; m++)
                {
                    var evaluated = SkeletonPoseEvaluator.BuildAnimatedMeshGroups(allMeshes[m], bindPose);
                    model.AnimatedMeshGroups.AddRange(evaluated);
                }
            }

            model.UpdateBounds();
            return model;
        }

        /// <summary>
        /// Modular character assembler stitching Race base skeleton, Face, and Armor/Weapon slots into a unified model.
        /// Automatically re-parents drawn weapon grip joints onto hand attach sockets.
        /// Clean-room implementation referencing FFXI weapon grip attach specifications in xi-model-viewer (https://github.com/vekien/xi-model-viewer).
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
            var weaponDats = new List<(CharacterSlot Slot, ReadOnlyMemory<byte> Dat)>();

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
            for (int slotIdx = 1; slotIdx < 9; slotIdx++)
            {
                var slot = (CharacterSlot)slotIdx;
                ushort rawVal = slotIdx < grapTable.Length ? grapTable[slotIdx] : (ushort)0;
                ushort modelId = (ushort)(rawVal & 0x0FFF);

                // Head, Main, Sub, and Ranged slots are optional/empty when modelId == 0 (no helmet / unarmed).
                // Body, Hands, Legs, and Feet slots always require a model (modelId 0 is the default/naked race armor).
                bool isRequiredArmorSlot = slot is CharacterSlot.Body or CharacterSlot.Hands or CharacterSlot.Legs or CharacterSlot.Feet;
                if (modelId == 0 && !isRequiredArmorSlot)
                {
                    continue;
                }

                if (CharacterEquipmentResolver.TryResolveGearFileId(race, slot, modelId, out int gearFid))
                {
                    byte[]? gearDat = datByFileId(gearFid);
                    if (gearDat != null && gearDat.Length > 0)
                    {
                        extraDats.Add(gearDat);
                        if (slot is CharacterSlot.Main or CharacterSlot.Sub or CharacterSlot.Ranged)
                        {
                            weaponDats.Add((slot, gearDat));
                        }
                    }
                }
            }

            Dictionary<int, int>? parentOverrides = null;
            if (weaponDats.Count > 0)
            {
                var baseContainer = ParseDatContainer(baseDat, "BaseSkeleton");
                if (baseContainer.Skeleton != null && baseContainer.Skeleton.References.Count > 127)
                {
                    parentOverrides = ResolveWeaponParentOverrides(baseContainer.Skeleton, weaponDats);
                }
            }

            var model = AssembleModel(baseDat, extraDats, $"{race}_Face{faceId}", parentOverrides);

            // Layer upper-body (+1) and waist/skirt (+3) locomotion packs, plus the H2H battle pack,
            // on top of the base skeleton's own (lower-body) clips already captured by AssembleModel.
            // Format referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer) ui/js/pclists.js.
            // Gated off by default - see EnableSpeculativeMotionPacks.
            if (EnableSpeculativeMotionPacks)
            {
                var (_, upperPath, waistPath) = CharacterEquipmentResolver.GetLocomotionPackPaths(race);
                var overlaySources = new List<List<AnimationClip>>();

                byte[]? upperDat = string.IsNullOrEmpty(upperPath) ? null : datByPath(upperPath);
                if (upperDat != null && upperDat.Length > 0)
                {
                    overlaySources.Add(ParseDatContainer(upperDat, "LocomotionUpper").Animations);
                }

                byte[]? waistDat = string.IsNullOrEmpty(waistPath) ? null : datByPath(waistPath);
                if (waistDat != null && waistDat.Length > 0)
                {
                    overlaySources.Add(ParseDatContainer(waistDat, "LocomotionWaist").Animations);
                }

                string battlePath = CharacterEquipmentResolver.GetBattlePackPath(race);
                byte[]? battleDat = string.IsNullOrEmpty(battlePath) ? null : datByPath(battlePath);
                if ((battleDat == null || battleDat.Length == 0) && CharacterEquipmentResolver.GetBattlePackFileId(race) is int battleFid && battleFid > 0)
                {
                    battleDat = datByFileId(battleFid);
                }

                List<AnimationClip>? battleAnims = null;
                if (battleDat != null && battleDat.Length > 0)
                {
                    battleAnims = ParseDatContainer(battleDat, "BattlePack").Animations;
                }

                if (overlaySources.Count > 0 || (battleAnims != null && battleAnims.Count > 0))
                {
                    MergeLocomotionCategories(model, overlaySources, battleAnims);
                }
            }

            return model;
        }

        /// <summary>
        /// Groups a body's own clips plus any overlay-pack clips (upper body, waist/skirt)
        /// by their body-region-stripped category name (e.g. "idl0"/"idl1"/"idl2" -&gt; "idl"), and replaces
        /// EntityModel.Animations with composite AnimationClips per category.
        /// When a battle pack is provided, battle clips (e.g. "btl0"/"btl1" -&gt; "btl", attacks) are also merged.
        /// Locomotion clips from the battle pack (e.g. "wlk1", "run1") are mapped to combat variants ("cwlk", "crun")
        /// so they do not clobber normal resting locomotion clips.
        /// Format referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer).
        /// </summary>
        private static void MergeLocomotionCategories(
            EntityModel model,
            List<List<AnimationClip>> overlaySources,
            List<AnimationClip>? battleAnims = null)
        {
            var byCategory = new Dictionary<string, List<AnimationClip>>(StringComparer.OrdinalIgnoreCase);

            void AddClip(AnimationClip clip)
            {
                string category = StripBodyRegionSuffix(clip.Name);
                if (!byCategory.TryGetValue(category, out var list))
                {
                    list = new List<AnimationClip>();
                    byCategory[category] = list;
                }
                list.Add(clip);
            }

            foreach (var baseClip in model.Animations.Values)
            {
                AddClip(baseClip);
            }

            foreach (var source in overlaySources)
            {
                foreach (var clip in source)
                {
                    AddClip(clip);
                }
            }

            model.Animations.Clear();

            foreach (var kvp in byCategory)
            {
                var sources = kvp.Value;
                if (sources.Count == 0) continue;

                var tracks = new Dictionary<int, BoneAnimationTrack>();
                foreach (var clip in sources)
                {
                    foreach (var trackKvp in clip.Tracks)
                    {
                        tracks[trackKvp.Key] = trackKvp.Value;
                    }
                }

                var timingSource = sources[0];
                model.Animations[kvp.Key] = new AnimationClip
                {
                    Name = kvp.Key,
                    NumFrames = timingSource.NumFrames,
                    KeyFrameDuration = timingSource.KeyFrameDuration,
                    Tracks = tracks
                };
            }

            if (battleAnims != null && battleAnims.Count > 0)
            {
                var battleByCategory = new Dictionary<string, List<AnimationClip>>(StringComparer.OrdinalIgnoreCase);
                foreach (var clip in battleAnims)
                {
                    string category = StripBodyRegionSuffix(clip.Name);
                    if (!battleByCategory.TryGetValue(category, out var list))
                    {
                        list = new List<AnimationClip>();
                        battleByCategory[category] = list;
                    }
                    list.Add(clip);
                }

                foreach (var kvp in battleByCategory)
                {
                    string cat = kvp.Key;
                    var sources = kvp.Value;
                    if (sources.Count == 0) continue;

                    var tracks = new Dictionary<int, BoneAnimationTrack>();
                    foreach (var clip in sources)
                    {
                        foreach (var trackKvp in clip.Tracks)
                        {
                            tracks[trackKvp.Key] = trackKvp.Value;
                        }
                    }

                    var timingSource = sources[0];
                    var compositeClip = new AnimationClip
                    {
                        Name = cat,
                        NumFrames = timingSource.NumFrames,
                        KeyFrameDuration = timingSource.KeyFrameDuration,
                        Tracks = tracks
                    };

                    if (!model.Animations.ContainsKey(cat))
                    {
                        model.Animations[cat] = compositeClip;
                    }
                    else
                    {
                        // Existing resting locomotion clips (e.g. wlk, run, ded):
                        // Do NOT overwrite the resting animation!
                        // Instead, save with a combat prefix (e.g. "cwlk", "crun")
                        model.Animations[$"c{cat}"] = compositeClip;
                    }
                }
            }
        }

        /// <summary>
        /// Strips a trailing body-region digit (0=lower, 1=upper, 2=waist) from a clip name,
        /// e.g. "idl0" -&gt; "idl". Names without a recognized trailing digit are left unchanged.
        /// </summary>
        private static string StripBodyRegionSuffix(string clipName)
        {
            if (clipName.Length >= 2 && clipName[^1] is >= '0' and <= '2')
            {
                return clipName[..^1];
            }
            return clipName;
        }

        /// <summary>
        /// Builds parent joint overrides for drawn weapons by resolving grip references to hand sockets.
        /// Main hand maps to reference 127 (Right hand), Sub hand maps to reference 126 (Left hand).
        /// Format referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer).
        /// </summary>
        public static Dictionary<int, int> ResolveWeaponParentOverrides(
            Skeleton skeleton,
            IReadOnlyList<(CharacterSlot Slot, ReadOnlyMemory<byte> Dat)> weaponDats)
        {
            var overrides = new Dictionary<int, int>();
            var refs = skeleton.References;
            if (refs.Count <= 127) return overrides;

            for (int w = 0; w < weaponDats.Count; w++)
            {
                var (slot, dat) = weaponDats[w];
                int handRefIdx = slot == CharacterSlot.Sub ? 126 : 127;
                int handJoint = refs[handRefIdx].Index;

                int? gripJoint = null;

                // 1. Check Info section (0x45) byte 6 (standardJointIndex)
                var headers = DatSectionWalker.ReadHeaders(dat.Span);
                for (int h = 0; h < headers.Count; h++)
                {
                    var head = headers[h];
                    if (head.TypeCode == DatSectionType.Info && head.DataOffset + 7 <= dat.Length)
                    {
                        byte stdJointByte = dat.Span[head.DataOffset + 6];
                        if (stdJointByte != 0xFF && stdJointByte < refs.Count)
                        {
                            gripJoint = refs[stdJointByte].Index;
                            break;
                        }
                    }
                }

                // 2. If no valid Info standardJointIndex, extract lowest positive vertex joint index
                if (!gripJoint.HasValue)
                {
                    var weaponContainer = ParseDatContainer(dat.Span, "WeaponInspect");
                    int minJoint = -1;
                    for (int m = 0; m < weaponContainer.Meshes.Count; m++)
                    {
                        var mesh = weaponContainer.Meshes[m];
                        for (int v = 0; v < mesh.Vertices.Length; v++)
                        {
                            int j0 = mesh.Vertices[v].Joint0;
                            int j1 = mesh.Vertices[v].Joint1;
                            if (j0 > 0 && (minJoint == -1 || j0 < minJoint)) minJoint = j0;
                            if (j1 > 0 && (minJoint == -1 || j1 < minJoint)) minJoint = j1;
                        }
                    }

                    if (minJoint > 0)
                    {
                        gripJoint = minJoint;
                    }
                }

                if (gripJoint.HasValue && gripJoint.Value != handJoint)
                {
                    overrides[gripJoint.Value] = handJoint;
                }
            }

            return overrides;
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
