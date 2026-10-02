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
            List<AnimationClip> Animations,
            List<RawMotionRoutine> Routines
        );

        /// <summary>
        /// Reads and extracts Skeleton (0x29), SkeletonMesh (0x2A), SkeletonAnimation (0x2B), and
        /// Texture (0x20) sections from a raw DAT payload.
        /// </summary>
        /// <param name="datSource">The DAT the bytes came from (a file label or path): stamps the textures'
        /// <see cref="DecodedTexture.Source"/> so caches can share them safely.</param>
        public static RawDatContainer ParseDatContainer(ReadOnlySpan<byte> datBytes, string sourceName = "", string? datSource = null)
        {
            var meshes = new List<SkeletonMeshGroup>();
            var textures = new Dictionary<string, DecodedTexture>(StringComparer.OrdinalIgnoreCase);
            var animations = new List<AnimationClip>();
            var routines = new List<RawMotionRoutine>();
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
                            mesh.SectionName = h.DatId;
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

                    case DatSectionType.EffectRoutine:
                        if (MotionRoutineDecoder.Decode(payload, h.DatId) is { } routine)
                        {
                            routines.Add(routine);
                        }
                        break;

                    case DatSectionType.Texture:
                        var tex = TextureDecoder.DecodeTexture(payload);
                        if (tex != null)
                        {
                            tex.Source = DecodedTexture.SourceOf(datSource, h.Offset);
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

            return new RawDatContainer(skeleton, meshes, textures, animations, routines);
        }

        /// <summary>
        /// Assembles an EntityModel from a primary skeleton container and any number of modular part containers.
        /// Supports optional skeletal joint parent overrides for weapon attachments.
        /// </summary>
        public static EntityModel AssembleModel(
            ReadOnlySpan<byte> primaryDat,
            IReadOnlyList<ReadOnlyMemory<byte>>? extraDats = null,
            string name = "",
            IReadOnlyDictionary<int, int>? parentOverrides = null,
            GearOcclusion? gearOcclusion = null,
            string? primarySource = null,
            IReadOnlyList<string?>? extraSources = null)
        {
            var model = new EntityModel { Name = name };

            var primary = ParseDatContainer(primaryDat, name, primarySource);
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
            MergeBodyRegionParts(model, primary.Animations);

            AddRoutines(model, primary.Routines);

            var allMeshes = new List<SkeletonMeshGroup>(primary.Meshes);
            var hiddenSlots = InitialHiddenWeaponSlots(model.RawMotionRoutines);
            if (hiddenSlots.Count > 0)
            {
                int removed = allMeshes.RemoveAll(m => WeaponSlotOf(m.SectionName) is int slot && hiddenSlots.Contains(slot));
                if (removed > 0) GordianLog.Debug("RES", $"{name}: init hides weapon slot(s) {string.Join(",", hiddenSlots)} ({removed} mesh(es)).");
            }

            if (extraDats != null)
            {
                for (int i = 0; i < extraDats.Count; i++)
                {
                    var extra = ParseDatContainer(extraDats[i].Span, $"Part_{i}", extraSources != null && i < extraSources.Count ? extraSources[i] : null);
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

                    AddRoutines(model, extra.Routines);
                    allMeshes.AddRange(extra.Meshes);
                }
            }

            if (gearOcclusion != null)
            {
                // The union spans every worn mesh (and whatever the caller seeded, such as a stowed ranged weapon),
                // then each piece is kept or dropped against it.
                foreach (var mesh in allMeshes) gearOcclusion.Add(mesh.OccludeType);
                int hidden = 0;
                foreach (var mesh in allMeshes) hidden += gearOcclusion.RemoveHiddenPieces(mesh);
                if (hidden > 0) GordianLog.Debug("RES", $"{name}: gear occlusion hid {hidden} piece(s).");
            }

            if (model.Skeleton != null && allMeshes.Count > 0)
            {
                // bindPose for bounds is computed in natural resting bind pose without hand overrides,
                // ensuring unposed fallback meshes and initial bounds keep weapons sheathed at rest.
                var bindPose = SkeletonPoseEvaluator.ComputeBindPose(model.Skeleton, null);
                for (int m = 0; m < allMeshes.Count; m++)
                {
                    var evaluated = SkeletonPoseEvaluator.BuildAnimatedMeshGroups(allMeshes[m], bindPose);
                    model.AnimatedMeshGroups.AddRange(evaluated);
                }
            }

            model.UpdateBounds();
            model.RebuildMotionRoutines();
            return model;
        }

        /// <summary>
        /// Adds a DAT's motion routines to the model, replacing any same-named routine from an earlier source (a PC's
        /// weapon battle pack overrides its race base).
        /// </summary>
        private static void AddRoutines(EntityModel model, List<RawMotionRoutine>? routines)
        {
            if (routines == null) return;
            foreach (var routine in routines)
            {
                if (routine.Name.Length > 0) model.RawMotionRoutines[routine.Name] = routine;
            }
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
            var extraSources = new List<string?>();
            var weaponDats = new List<(CharacterSlot Slot, ReadOnlyMemory<byte> Dat)>();
            var occlusion = new GearOcclusion();

            // 1. Face slot (from GrapIdTable[0] & 0xFF)
            ushort faceId = (ushort)(faceModel & 0xFF);
            if (CharacterEquipmentResolver.TryResolveGearFileId(race, CharacterSlot.Face, faceId, out int faceFid))
            {
                byte[]? faceDat = datByFileId(faceFid);
                if (faceDat != null && faceDat.Length > 0)
                {
                    extraDats.Add(faceDat);
                    extraSources.Add(DecodedTexture.FileLabel(faceFid));
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
                        if (slot == CharacterSlot.Ranged)
                        {
                            // A stowed ranged weapon is equipped but not drawn: nothing animates its back-mount bone and
                            // the client scales it to zero until the weapon is drawn. It still counts toward occlusion.
                            // Referenced from xi-tools (docs/gear/pose.md, "The weapon on the floor").
                            foreach (var mesh in ParseDatContainer(gearDat, "Ranged").Meshes) occlusion.Add(mesh.OccludeType);
                            continue;
                        }

                        extraDats.Add(gearDat);
                        extraSources.Add(DecodedTexture.FileLabel(gearFid));
                        if (slot is CharacterSlot.Main or CharacterSlot.Sub)
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

            var model = AssembleModel(baseDat, extraDats, $"{race}_Face{faceId}", parentOverrides, occlusion, baseSkelPath, extraSources);

            // Layer upper-body (+1) and waist/skirt (+3) locomotion packs, plus weapon-specific battle pack,
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

                // Resolve weapon animation type from equipped Main weapon Info section (0x45 byte 3)
                int weaponAnimType = 0; // 0 = H2H / Unarmed default
                for (int w = 0; w < weaponDats.Count; w++)
                {
                    var (slot, dat) = weaponDats[w];
                    if (slot == CharacterSlot.Main)
                    {
                        var headers = DatSectionWalker.ReadHeaders(dat.Span);
                        for (int h = 0; h < headers.Count; h++)
                        {
                            var head = headers[h];
                            if (head.TypeCode == DatSectionType.Info && head.DataOffset + 4 <= dat.Length)
                            {
                                byte animByte = dat.Span[head.DataOffset + 3];
                                if (animByte != 0xFF)
                                {
                                    weaponAnimType = animByte;
                                }
                                break;
                            }
                        }
                        break;
                    }
                }

                string battlePath = CharacterEquipmentResolver.GetBattlePackPath(race, weaponAnimType);
                byte[]? battleDat = string.IsNullOrEmpty(battlePath) ? null : datByPath(battlePath);
                if ((battleDat == null || battleDat.Length == 0) && CharacterEquipmentResolver.GetBattlePackFileId(race, weaponAnimType) is int battleFid && battleFid > 0)
                {
                    battleDat = datByFileId(battleFid);
                }

                List<AnimationClip>? battleAnims = null;
                if (battleDat != null && battleDat.Length > 0)
                {
                    var battle = ParseDatContainer(battleDat, "BattlePack");
                    battleAnims = battle.Animations;
                    // The weapon's swings, draw and sheathe (ati0-ati2, atf0, out0, in 0...) live in its battle pack.
                    AddRoutines(model, battle.Routines);
                }

                if (overlaySources.Count > 0 || (battleAnims != null && battleAnims.Count > 0))
                {
                    MergeLocomotionCategories(model, overlaySources, battleAnims);
                }

                model.RebuildMotionRoutines();
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
        internal static void MergeLocomotionCategories(
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

                    // If an existing resting locomotion clip exists for this category (e.g. wlk, run, mvb, mvl, mvr),
                    // inherit its lower-body and waist tracks so the legs continue walking/running/strafing smoothly.
                    bool hasExisting = model.Animations.TryGetValue(cat, out var existingClip);
                    if (hasExisting && existingClip != null)
                    {
                        foreach (var (jointIdx, baseTrack) in existingClip.Tracks)
                        {
                            tracks[jointIdx] = baseTrack;
                        }
                    }

                    // Layer the combat battle pack tracks on top (overriding upper body arms/torso/hands with combat stance tracks)
                    foreach (var clip in sources)
                    {
                        foreach (var trackKvp in clip.Tracks)
                        {
                            tracks[trackKvp.Key] = trackKvp.Value;
                        }
                    }

                    var timingSource = hasExisting && existingClip != null ? existingClip : sources[0];

                    // Graft missing idle tracks (e.g. waist joints 4, 5, 18-25 and sheathed weapon mounts)
                    // onto the battle clip so skirts and resting mounts are not left in static bind pose.
                    // Clean-room implementation referencing xi-model-viewer (https://github.com/vekien/xi-model-viewer) graftIdleWaist.
                    if (model.Animations.TryGetValue("idl", out var idleClip))
                    {
                        foreach (var (jointIdx, idleTrack) in idleClip.Tracks)
                        {
                            if (!tracks.ContainsKey(jointIdx))
                            {
                                tracks[jointIdx] = idleTrack;
                            }
                        }
                    }

                    var compositeClip = new AnimationClip
                    {
                        Name = hasExisting ? $"c{cat}" : cat,
                        NumFrames = timingSource.NumFrames,
                        KeyFrameDuration = timingSource.KeyFrameDuration,
                        Tracks = tracks
                    };

                    if (!hasExisting)
                    {
                        model.Animations[cat] = compositeClip;
                    }
                    else
                    {
                        // Existing resting locomotion clips (e.g. wlk, run, mvb, mvl, mvr, ded):
                        // Do NOT overwrite the resting animation!
                        // Instead, save with a combat prefix (e.g. "cwlk", "crun", "cmvb", "cmvl", "cmvr")
                        model.Animations[$"c{cat}"] = compositeClip;
                    }
                }
            }
        }

        /// <summary>The weapon slot of a <c>wepN</c> mesh section, or null.</summary>
        internal static int? WeaponSlotOf(string sectionName) =>
            sectionName.Length == 4 && sectionName.StartsWith("wep", StringComparison.Ordinal) && char.IsDigit(sectionName[3])
                ? sectionName[3] - '0'
                : null;

        /// <summary>
        /// The weapon slots a model's <c>init</c> routine leaves hidden (op 0x75, following its links within the model):
        /// Prince Trion's model 64 runs <c>wof4</c> ("weapon off") from <c>init</c>, hiding the sword and scabbard of its
        /// <c>wep4</c> folder, which <c>won4</c> would show again; drawn anyway they hung at his hips pointing outward (#163).
        /// </summary>
        internal static HashSet<int> InitialHiddenWeaponSlots(IReadOnlyDictionary<string, RawMotionRoutine> routines)
        {
            var state = new Dictionary<int, bool>();
            void Walk(RawMotionRoutine routine, int depth)
            {
                if (depth > 4) return;
                foreach (var command in routine.Commands)
                {
                    if (command.Op == 0x75 && command.WeaponSlot >= 0) state[command.WeaponSlot] = command.HideWeapon;
                    else if (command.Op is 0x03 or 0x3B && routines.TryGetValue(command.Reference, out var child)) Walk(child, depth + 1);
                }
            }
            if (routines.TryGetValue("init", out var init)) Walk(init, 0);
            var hidden = new HashSet<int>();
            foreach (var (slot, hide) in state) if (hide) hidden.Add(slot);
            return hidden;
        }

        /// <summary>
        /// Joins a motion stored in body-region parts into one clip under its stem name: a fixed NPC model (Curilla,
        /// model 69) carries <c>wlk0</c> (16 joints: legs), <c>wlk1</c> (73: upper body, arms and the weapon joints) and
        /// <c>wlk2</c> (10: waist), and the stem used to name only the first part in file order, so a walk moved the legs
        /// alone, the upper body stayed in its bind pose with the sword at the floor (#163), and <c>run</c> was the
        /// waist part. Parts are joined only when they animate disjoint joints; same-stem clips that each move the whole
        /// skeleton (a monster's swings <c>at0</c>-<c>at2</c>) are different motions and keep the first as the stem.
        /// </summary>
        internal static void MergeBodyRegionParts(EntityModel model, List<AnimationClip> clips)
        {
            foreach (var joined in JoinBodyRegionParts(clips)) model.Animations[joined.Name] = joined;
        }

        /// <summary>
        /// The joined clips of every stem whose parts animate disjoint joints (see <see cref="MergeBodyRegionParts"/>),
        /// named by the stem. Used for models and for event motion banks, which store gestures the same way.
        /// </summary>
        public static List<AnimationClip> JoinBodyRegionParts(IEnumerable<AnimationClip> clips)
        {
            var byStem = new Dictionary<string, List<AnimationClip>>(StringComparer.OrdinalIgnoreCase);
            foreach (var clip in clips)
            {
                string stem = StripBodyRegionSuffix(clip.Name);
                if (stem == clip.Name) continue;
                if (!byStem.TryGetValue(stem, out var list)) byStem[stem] = list = new List<AnimationClip>();
                list.Add(clip);
            }
            var joinedClips = new List<AnimationClip>();
            foreach (var (stem, parts) in byStem)
            {
                if (parts.Count < 2) continue;
                var tracks = new Dictionary<int, BoneAnimationTrack>();
                bool disjoint = true;
                foreach (var part in parts)
                {
                    foreach (var (joint, track) in part.Tracks)
                    {
                        if (!tracks.TryAdd(joint, track)) disjoint = false;
                    }
                }
                if (!disjoint) continue;
                // Timing from the part that moves the most joints (the parts of one motion share it in the retail models).
                var timing = parts[0];
                foreach (var part in parts) if (part.Tracks.Count > timing.Tracks.Count) timing = part;
                joinedClips.Add(new AnimationClip
                {
                    Name = stem,
                    NumFrames = timing.NumFrames,
                    KeyFrameDuration = timing.KeyFrameDuration,
                    Tracks = tracks
                });
            }
            return joinedClips;
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

                // 1. Check Info section (0x45) for weapon animation type (byte 3) and standardJointIndex (byte 6)
                byte animTypeByte = 0xFF;
                byte stdJointByte = 0xFF;
                var headers = DatSectionWalker.ReadHeaders(dat.Span);
                for (int h = 0; h < headers.Count; h++)
                {
                    var head = headers[h];
                    if (head.TypeCode == DatSectionType.Info && head.DataOffset + 7 <= dat.Length)
                    {
                        animTypeByte = dat.Span[head.DataOffset + 3];
                        stdJointByte = dat.Span[head.DataOffset + 6];
                        break;
                    }
                }

                // Shields (in Sub slot) have animType 0xFF or stdJoint 125 (shield back mount) or 0xFF,
                // and are skinned directly to the Left Forearm joint. They must NOT be re-parented to the hand.
                if (slot == CharacterSlot.Sub && (animTypeByte == 0xFF || stdJointByte == 125 || stdJointByte == 0xFF))
                {
                    continue;
                }

                int handRefIdx = slot == CharacterSlot.Sub ? 126 : 127;
                int handJoint = refs[handRefIdx].Index;

                int? gripJoint = null;
                if (stdJointByte != 0xFF && stdJointByte < refs.Count)
                {
                    gripJoint = refs[stdJointByte].Index;
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
        /// Loads an NPC, Monster, or Trust entity model from its numeric ModelId. A fixed humanoid NPC (Apururu, Curilla)
        /// bundles gear meshes in one DAT, so its own occludeTypes hide its hair and skin under the hat and sleeves, as on
        /// an assembled PC (#181). Monster DATs declare nothing that hides a piece.
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

            return AssembleModel(dat, null, $"Monster_{modelId}", gearOcclusion: new GearOcclusion(), primarySource: DecodedTexture.FileLabel(fileId));
        }
    }
}
