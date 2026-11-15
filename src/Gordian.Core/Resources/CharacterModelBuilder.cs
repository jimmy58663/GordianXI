// src/Gordian.Core/Resources/CharacterModelBuilder.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Gordian.Core.Diagnostics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;

namespace Gordian.Core.Resources
{
    /// <summary>
    /// Builds player models from shared pieces (#573) instead of one full model per race, face and gear set:
    /// <list type="bullet">
    /// <item>a <b>race base</b> per race: the skeleton, its bind pose and the race DAT's own clips and routines, kept for the
    /// session;</item>
    /// <item>the <b>motion packs</b> (locomotion, battle, waist, chair, mannequin) parsed once per file, kept for the session;</item>
    /// <item>a <b>motion set</b> per race, battle pack, waist set, ranged type and mannequin pose: the merged clips and
    /// routines, immutable and shared by every model that matches;</item>
    /// <item>a <b>part</b> per gear piece (<see cref="CharacterPart"/>), shared by everyone wearing it and freed when no
    /// cached model uses it (<see cref="EvictParts"/>).</item>
    /// </list>
    /// A model (<see cref="Build"/>) is then a light object referencing those, plus what depends on the whole worn set: the
    /// pieces the gear hides, the weapon parent overrides and the merged texture names. Thread-safe: concurrent builds share
    /// pieces and build each one once.
    /// </summary>
    public sealed class CharacterModelBuilder
    {
        private readonly Func<string, byte[]?> _datByPath;
        private readonly Func<int, byte[]?> _datByFileId;
        private readonly ICharacterPartSource _partSource;

        private readonly ConcurrentDictionary<CharacterRace, Lazy<RaceBase?>> _raceBases = new();
        private readonly ConcurrentDictionary<string, Lazy<EntityModelLoader.RawDatContainer?>> _packsByPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<int, Lazy<EntityModelLoader.RawDatContainer?>> _packsByFileId = new();
        private readonly ConcurrentDictionary<MotionSetKey, Lazy<EntityModel>> _motionSets = new();
        private readonly ConcurrentDictionary<CharacterPartKey, Lazy<CharacterPart?>> _parts = new();

        public CharacterModelBuilder(Func<string, byte[]?> datByPath, Func<int, byte[]?> datByFileId, ICharacterPartSource? partSource = null)
        {
            _datByPath = datByPath;
            _datByFileId = datByFileId;
            _partSource = partSource ?? new DatCharacterPartSource(datByFileId);
        }

        /// <summary>A race's skeleton, bind pose and race DAT, shared by every model of the race.</summary>
        internal sealed class RaceBase
        {
            public required string Path { get; init; }
            public required EntityModelLoader.RawDatContainer Container { get; init; }
            public Skeleton? Skeleton => Container.Skeleton;
            public BlurAura? BlurAura { get; init; }

            /// <summary>The race DAT's own mesh pieces (PC race DATs carry none), less the weapons its <c>init</c> hides.</summary>
            public required IReadOnlyList<AnimatedMeshGroup> Groups { get; init; }
            public required IReadOnlyList<byte> OccludeTypes { get; init; }
        }

        /// <summary>
        /// What picks a model's motions: the race, the body's waist set, the ranged weapon's type, the weapons' Info fields
        /// (battle pack, dual wield, draw routines), the mannequin pose, whether the motion packs load at all, and the parts
        /// that bring clips or routines of their own (rare; their keys, comma-separated, else null).
        /// </summary>
        internal readonly record struct MotionSetKey(
            CharacterRace Race,
            bool SecondWaistSet,
            int RangedType,
            EntityModelLoader.WeaponInfo? Main,
            EntityModelLoader.WeaponInfo? Sub,
            bool Mannequin,
            bool MotionPacks,
            string? PartMotions);

        /// <summary>Distinct motion sets built so far.</summary>
        public int MotionSetCount => _motionSets.Count;

        /// <summary>Gear parts cached (missing files included).</summary>
        public int PartCount => _parts.Count;

        private long _partBytes;

        /// <summary>The <see cref="CharacterPart.ApproximateBytes"/> of every cached part (kept as a running total).</summary>
        public long PartBytes => Interlocked.Read(ref _partBytes);

        /// <summary>
        /// The model of a character of <paramref name="race"/> with face <paramref name="faceModel"/> (low byte) wearing the
        /// gear of <paramref name="grapTable"/> (slots 1-8: head, body, hands, legs, feet, main, sub, ranged), or null for an
        /// unknown race or a missing race DAT. Built from the shared pieces, building only the ones not cached yet.
        /// </summary>
        public EntityModel? Build(CharacterRace race, ushort faceModel, ReadOnlySpan<ushort> grapTable)
        {
            if (race == CharacterRace.Unknown) return null;
            var raceBase = GetRaceBase(race);
            if (raceBase == null) return null;

            // Face first, then head through ranged: the order the parts' textures and pieces were always merged in.
            var parts = new List<CharacterPart>(9);
            ushort faceId = (ushort)(faceModel & 0xFF);
            if (CharacterEquipmentResolver.TryResolveGearFileId(race, CharacterSlot.Face, faceId, out int faceFid)
                && GetPart(new CharacterPartKey(race, CharacterSlot.Face, faceFid), raceBase) is { } facePart)
            {
                parts.Add(facePart);
            }

            for (int slotIdx = 1; slotIdx < 9; slotIdx++)
            {
                var slot = (CharacterSlot)slotIdx;
                ushort modelId = (ushort)((slotIdx < grapTable.Length ? grapTable[slotIdx] : (ushort)0) & 0x0FFF);
                // Head, Main, Sub and Ranged are empty at 0 (no helmet, unarmed); Body, Hands, Legs and Feet draw the race's
                // default armour at 0.
                bool required = slot is CharacterSlot.Body or CharacterSlot.Hands or CharacterSlot.Legs or CharacterSlot.Feet;
                if (modelId == 0 && !required) continue;
                if (CharacterEquipmentResolver.TryResolveGearFileId(race, slot, modelId, out int gearFid)
                    && GetPart(new CharacterPartKey(race, slot, gearFid), raceBase) is { } part)
                {
                    parts.Add(part);
                }
            }

            int rangedType = -1;
            bool secondWaistSet = false;
            EntityModelLoader.WeaponInfo? main = null, sub = null;
            StringBuilder? partMotions = null;
            bool hasWeapon = false;
            foreach (var part in parts)
            {
                switch (part.Key.Slot)
                {
                    case CharacterSlot.Ranged: rangedType = part.RangedType; break;
                    case CharacterSlot.Body: secondWaistSet = part.SecondWaistSet; break;
                    case CharacterSlot.Main: main = part.WeaponInfo; hasWeapon = true; break;
                    case CharacterSlot.Sub: sub = part.WeaponInfo; hasWeapon = true; break;
                }
                if (part.HasMotions)
                {
                    partMotions ??= new StringBuilder();
                    partMotions.Append(part.Key.Slot).Append(':').Append(part.Key.FileId).Append(',');
                }
            }

            var key = new MotionSetKey(race, secondWaistSet, rangedType, main, sub,
                faceId == CharacterEquipmentResolver.MannequinFace, EntityModelLoader.EnableSpeculativeMotionPacks, partMotions?.ToString());
            var motionSet = _motionSets.GetOrAdd(key, k => new Lazy<EntityModel>(() => BuildMotionSet(k, raceBase, parts), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

            var model = new EntityModel
            {
                Name = $"{race}_Face{faceId}",
                Skeleton = raceBase.Skeleton,
                BlurAura = raceBase.BlurAura,
                IsCharacter = true,
            };
            model.ShareMotionsFrom(motionSet);

            foreach (var (name, texture) in raceBase.Container.Textures) model.Textures[name] = texture;
            foreach (var part in parts)
            {
                foreach (var (name, texture) in part.Textures) model.Textures[name] = texture;
            }

            // The pieces the worn set hides: the union of every worn mesh's occludeType against each piece's displayType.
            var occlusion = new GearOcclusion();
            foreach (byte type in raceBase.OccludeTypes) occlusion.Add(type);
            foreach (var part in parts)
            {
                foreach (byte type in part.OccludeTypes) occlusion.Add(type);
            }
            int hidden = AddVisible(model, raceBase.Groups, occlusion);
            foreach (var part in parts) hidden += AddVisible(model, part.Groups, occlusion);
            if (hidden > 0) GordianLog.Debug(LogCategories.Res, $"{model.Name}: gear occlusion hid {hidden} piece(s).");

            // Drawn weapons re-parent their grip joints onto the hands, main hand then sub hand.
            if (hasWeapon && raceBase.Skeleton is { References.Count: > 127 })
            {
                var overrides = new Dictionary<int, int>();
                foreach (var part in parts)
                {
                    foreach (var (grip, hand) in part.ParentOverrides) overrides[grip] = hand;
                }
                model.ParentOverrides = overrides;
            }

            model.Parts = parts;
            model.UpdateBounds();
            return model;
        }

        private static int AddVisible(EntityModel model, IReadOnlyList<AnimatedMeshGroup> groups, GearOcclusion occlusion)
        {
            int hidden = 0;
            foreach (var group in groups)
            {
                if (occlusion.Hides(group.DisplayType)) hidden++;
                else model.AnimatedMeshGroups.Add(group);
            }
            return hidden;
        }

        internal RaceBase? GetRaceBase(CharacterRace race) =>
            _raceBases.GetOrAdd(race, r => new Lazy<RaceBase?>(() => LoadRaceBase(r), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

        private RaceBase? LoadRaceBase(CharacterRace race)
        {
            string path = CharacterEquipmentResolver.GetBaseSkeletonPath(race);
            if (string.IsNullOrEmpty(path)) return null;
            byte[]? dat = _datByPath(path);
            if (dat == null || dat.Length == 0)
            {
                GordianLog.Warn(LogCategories.Res, $"Could not load base skeleton DAT for {race} at '{path}'");
                return null;
            }

            var container = EntityModelLoader.ParseDatContainer(dat, $"{race}_Base", path);
            var groups = new List<AnimatedMeshGroup>();
            var occludeTypes = new List<byte>();
            var meshes = new List<SkeletonMeshGroup>(container.Meshes);
            // A model's init may hide weapon folders of its own DAT for good (the fixed models' wof4); a PC race DAT has no
            // meshes, but the rule is the same as for any model.
            var routines = new Dictionary<string, RawMotionRoutine>(StringComparer.Ordinal);
            foreach (var routine in container.Routines) if (routine.Name.Length > 0) routines[routine.Name] = routine;
            var hiddenSlots = EntityModelLoader.InitialHiddenWeaponSlots(routines);
            if (hiddenSlots.Count > 0) meshes.RemoveAll(m => EntityModelLoader.WeaponSlotOf(m.SectionName) is int slot && hiddenSlots.Contains(slot));
            if (container.Skeleton != null)
            {
                var bindPose = SkeletonPoseEvaluator.ComputeBindPose(container.Skeleton, null);
                foreach (var mesh in meshes)
                {
                    occludeTypes.Add(mesh.OccludeType);
                    int slot = EntityModelLoader.WeaponSlotOf(mesh.SectionName) ?? -1;
                    foreach (var group in SkeletonPoseEvaluator.BuildAnimatedMeshGroups(mesh, bindPose))
                    {
                        group.WeaponSlot = slot;
                        groups.Add(group);
                    }
                }
            }

            return new RaceBase
            {
                Path = path,
                Container = container,
                BlurAura = BlurAura.Find(dat),
                Groups = groups,
                OccludeTypes = occludeTypes,
            };
        }

        private CharacterPart? GetPart(CharacterPartKey key, RaceBase raceBase)
        {
            var part = _parts.GetOrAdd(key, k => new Lazy<CharacterPart?>(() =>
            {
                // Only the stored Lazy is ever evaluated, so each cached part is counted once.
                var loaded = _partSource.LoadPart(k, raceBase.Skeleton);
                if (loaded != null) Interlocked.Add(ref _partBytes, loaded.ApproximateBytes);
                return loaded;
            }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
            part?.Touch(Stopwatch.GetTimestamp());
            return part;
        }

        private EntityModel BuildMotionSet(MotionSetKey key, RaceBase raceBase, IReadOnlyList<CharacterPart> parts)
        {
            var motionParts = new List<CharacterPart>();
            foreach (var part in parts) if (part.HasMotions) motionParts.Add(part);
            return EntityModelLoader.BuildMotionSet(key, raceBase, motionParts, Pack, PackByFileId);
        }

        /// <summary>A motion pack by path, parsed once (null when missing).</summary>
        internal EntityModelLoader.RawDatContainer? Pack(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return _packsByPath.GetOrAdd(path, p => new Lazy<EntityModelLoader.RawDatContainer?>(() =>
                _datByPath(p) is { Length: > 0 } dat ? EntityModelLoader.ParseDatContainer(dat, p) : null,
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }

        /// <summary>A motion pack by file id, parsed once (null when missing).</summary>
        internal EntityModelLoader.RawDatContainer? PackByFileId(int fileId)
        {
            if (fileId < 0) return null;
            return _packsByFileId.GetOrAdd(fileId, id => new Lazy<EntityModelLoader.RawDatContainer?>(() =>
                _datByFileId(id) is { Length: > 0 } dat ? EntityModelLoader.ParseDatContainer(dat, DecodedTexture.FileLabel(id)) : null,
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }

        /// <summary>
        /// Frees the parts no model in <paramref name="models"/> uses (#573): first those unused for
        /// <paramref name="idleSeconds"/>, then, while the parts hold more than <paramref name="budgetBytes"/>, any unused
        /// part, least recently used first. The motion sets no model plays are dropped too. A part a listed model uses is
        /// never freed, so a budget set too low costs rebuilds, never missing models. Returns how many parts were freed.
        /// </summary>
        public int EvictParts(IEnumerable<EntityModel> models, double idleSeconds, long budgetBytes)
        {
            var used = new HashSet<CharacterPart>(ReferenceEqualityComparer.Instance);
            var usedMotionSets = new HashSet<EntityModel>(ReferenceEqualityComparer.Instance);
            foreach (var model in models)
            {
                foreach (var part in model.Parts) used.Add(part);
                if (model.MotionSet != null) usedMotionSets.Add(model.MotionSet);
            }

            long now = Stopwatch.GetTimestamp();
            long idleTicks = (long)(idleSeconds * Stopwatch.Frequency);
            long bytes = 0;
            var unused = new List<KeyValuePair<CharacterPartKey, Lazy<CharacterPart?>>>();
            int freed = 0;
            foreach (var pair in _parts)
            {
                if (!pair.Value.IsValueCreated) continue; // being built
                var part = pair.Value.Value;
                if (part == null) continue;               // a missing file costs nothing to remember
                if (used.Contains(part)) { bytes += part.ApproximateBytes; continue; }
                if (now - part.LastUsed > idleTicks)
                {
                    if (_parts.TryRemove(pair))
                    {
                        Interlocked.Add(ref _partBytes, -part.ApproximateBytes);
                        freed++;
                    }
                    continue;
                }
                bytes += part.ApproximateBytes;
                unused.Add(pair);
            }

            if (bytes > budgetBytes && unused.Count > 0)
            {
                unused.Sort((a, b) => a.Value.Value!.LastUsed.CompareTo(b.Value.Value!.LastUsed));
                foreach (var pair in unused)
                {
                    if (bytes <= budgetBytes) break;
                    if (_parts.TryRemove(pair))
                    {
                        bytes -= pair.Value.Value!.ApproximateBytes;
                        Interlocked.Add(ref _partBytes, -pair.Value.Value!.ApproximateBytes);
                        freed++;
                    }
                }
            }

            foreach (var pair in _motionSets)
            {
                if (pair.Value.IsValueCreated && !usedMotionSets.Contains(pair.Value.Value)) _motionSets.TryRemove(pair);
            }
            return freed;
        }
    }

    /// <summary>
    /// The retail source of gear parts (#573): parses the part's DAT (through the VFS, so DAT replacement packs work) and
    /// bakes its pieces against the race's bind pose.
    /// </summary>
    public sealed class DatCharacterPartSource : ICharacterPartSource
    {
        private readonly Func<int, byte[]?> _datByFileId;

        public DatCharacterPartSource(Func<int, byte[]?> datByFileId) => _datByFileId = datByFileId;

        public CharacterPart? LoadPart(CharacterPartKey key, Skeleton? skeleton)
        {
            byte[]? dat = _datByFileId(key.FileId);
            if (dat == null || dat.Length == 0) return null;

            var slot = key.Slot;
            var container = EntityModelLoader.ParseDatContainer(dat, $"Part_{slot}", DecodedTexture.FileLabel(key.FileId));
            int weaponSlot = slot switch { CharacterSlot.Main => 0, CharacterSlot.Sub => 1, CharacterSlot.Ranged => 2, _ => -1 };

            var groups = new List<AnimatedMeshGroup>();
            var occludeTypes = new List<byte>(container.Meshes.Count);
            foreach (var mesh in container.Meshes) occludeTypes.Add(mesh.OccludeType);
            if (skeleton != null && container.Meshes.Count > 0)
            {
                // Baked in the resting bind pose without the hand overrides, so the bounds keep weapons sheathed at rest.
                var bindPose = SkeletonPoseEvaluator.ComputeBindPose(skeleton, null);
                foreach (var mesh in container.Meshes)
                {
                    int meshSlot = weaponSlot >= 0 ? weaponSlot : EntityModelLoader.WeaponSlotOf(mesh.SectionName) ?? -1;
                    foreach (var group in SkeletonPoseEvaluator.BuildAnimatedMeshGroups(mesh, bindPose))
                    {
                        group.WeaponSlot = meshSlot;
                        groups.Add(group);
                    }
                }
            }

            IReadOnlyList<KeyValuePair<int, int>> overrides = Array.Empty<KeyValuePair<int, int>>();
            bool resolves = false;
            if (slot is CharacterSlot.Main or CharacterSlot.Sub && skeleton is { References.Count: > 127 })
            {
                overrides = [.. EntityModelLoader.ResolveWeaponParentOverrides(skeleton, [(slot, dat)])];
                resolves = true;
            }

            return new CharacterPart(key, groups, container.Textures)
            {
                OccludeTypes = occludeTypes,
                Animations = container.Animations,
                Routines = container.Routines,
                // The ranged weapon's RangeType picks the lc<NN> / ls<NN> routines of a ranged attack (#158).
                RangedType = slot == CharacterSlot.Ranged ? EntityModelLoader.ReadRangedType(dat) : -1,
                SecondWaistSet = slot == CharacterSlot.Body && CharacterEquipmentResolver.UsesSecondWaistSet(dat),
                WeaponInfo = slot is CharacterSlot.Main or CharacterSlot.Sub ? EntityModelLoader.ReadWeaponInfo(dat) : null,
                ParentOverrides = overrides,
                ResolvesParentOverrides = resolves,
                ApproximateBytes = CharacterPart.MeasureBytes(groups, container.Textures),
            };
        }
    }
}
