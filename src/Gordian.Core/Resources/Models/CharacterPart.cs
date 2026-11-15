// src/Gordian.Core/Resources/Models/CharacterPart.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Tables;

namespace Gordian.Core.Resources.Models
{
    /// <summary>
    /// Names one gear piece of a player model: the race whose skeleton it binds to, the slot it is worn in and the file it
    /// comes from (<see cref="CharacterEquipmentResolver.TryResolveGearFileId"/>). Every character of that race wearing the
    /// piece in that slot shares the one <see cref="CharacterPart"/> (#573).
    /// </summary>
    public readonly record struct CharacterPartKey(CharacterRace Race, CharacterSlot Slot, int FileId);

    /// <summary>
    /// One parsed gear piece (face, head, body, hands, legs, feet, main, sub or ranged), shared by every character wearing
    /// it (#573): its mesh pieces baked for GPU skinning against the race's bind pose, its textures, any clips and
    /// motion routines its DAT carries, and the few Info fields that pick the wearer's motions. Every piece is kept; each
    /// character leaves out the pieces its own gear hides (<see cref="AnimatedMeshGroup.DisplayType"/>). Immutable once
    /// built, so builds on worker threads can share it.
    /// </summary>
    public sealed class CharacterPart
    {
        private long _lastUsed = Stopwatch.GetTimestamp();

        public CharacterPart(CharacterPartKey key, IReadOnlyList<AnimatedMeshGroup> groups, IReadOnlyDictionary<string, DecodedTexture> textures)
        {
            Key = key;
            Groups = groups;
            Textures = textures;
        }

        public CharacterPartKey Key { get; }

        /// <summary>The part's mesh pieces, one group per piece, with their weapon slot set (<see cref="AnimatedMeshGroup.WeaponSlot"/>).</summary>
        public IReadOnlyList<AnimatedMeshGroup> Groups { get; }

        /// <summary>The part's textures by name (full and short names, as <see cref="EntityModelLoader.ParseDatContainer"/> files them).</summary>
        public IReadOnlyDictionary<string, DecodedTexture> Textures { get; }

        /// <summary>The occludeType of each of the part's meshes (<see cref="SkeletonMeshGroup.OccludeType"/>).</summary>
        public IReadOnlyList<byte> OccludeTypes { get; init; } = Array.Empty<byte>();

        /// <summary>Clips the part's DAT carries itself (no retail gear DAT carries any); they join the wearer's motion set.</summary>
        public IReadOnlyList<AnimationClip> Animations { get; init; } = Array.Empty<AnimationClip>();

        /// <summary>
        /// Motion routines the part's DAT carries itself, laid over the race's routines of each wearer's model (#573): a
        /// face's voice routines (<c>vded</c>, <c>vdam</c>, <c>vatk</c>...), a body's damage sound (<c>sdam</c>), a weapon's
        /// swing, draw and sheathe sounds (<c>skaz</c>, <c>sinr</c>, <c>sotr</c>; off hand <c>skal</c>, <c>sinl</c>...), a
        /// ranged weapon's (<c>kalg</c>, <c>lgin</c>, <c>lgot</c>...).
        /// </summary>
        public IReadOnlyList<RawMotionRoutine> Routines { get; init; } = Array.Empty<RawMotionRoutine>();

        /// <summary>A ranged weapon's RangeType (<c>EntityModelLoader.ReadRangedType</c>), else -1.</summary>
        public int RangedType { get; init; } = -1;

        /// <summary>Whether a body piece takes the second waist set (long skirts and robes, Info byte 9 = 2).</summary>
        public bool SecondWaistSet { get; init; }

        /// <summary>A main or sub weapon's Info fields (type, sub-type, mount), which pick the battle packs.</summary>
        internal EntityModelLoader.WeaponInfo? WeaponInfo { get; init; }

        /// <summary>The joint re-parenting a main or sub weapon asks for (grip joint to hand joint), in order.</summary>
        public IReadOnlyList<KeyValuePair<int, int>> ParentOverrides { get; init; } = Array.Empty<KeyValuePair<int, int>>();

        /// <summary>
        /// About how many bytes the part holds: its vertices and indices (the size of its GPU copy, with 32-bit indices) and
        /// its textures' pixels. What the part memory budget counts.
        /// </summary>
        public long ApproximateBytes { get; init; }

        /// <summary>When a character wearing the part was last used (<see cref="Stopwatch"/> timestamp).</summary>
        public long LastUsed => Volatile.Read(ref _lastUsed);

        internal void Touch(long now)
        {
            if (now > Volatile.Read(ref _lastUsed)) Volatile.Write(ref _lastUsed, now);
        }

        /// <summary>The <see cref="ApproximateBytes"/> of a part made of these groups and textures.</summary>
        public static long MeasureBytes(IReadOnlyList<AnimatedMeshGroup> groups, IReadOnlyDictionary<string, DecodedTexture> textures)
        {
            long bytes = 0;
            foreach (var group in groups) bytes += (long)group.Vertices.Length * Unsafe.SizeOf<AnimatedMeshVertex>() + (long)group.Indices.Length * sizeof(int);
            // Textures are filed under a full and a short name; count each once.
            var seen = new HashSet<DecodedTexture>(ReferenceEqualityComparer.Instance);
            foreach (var texture in textures.Values)
            {
                if (seen.Add(texture)) bytes += texture.RgbaPixels.Length;
            }
            return bytes;
        }

        public override string ToString() => $"CharacterPart [{Key.Race} {Key.Slot} file {Key.FileId}] ({Groups.Count} pieces, {ApproximateBytes / 1024} KB)";
    }

    /// <summary>
    /// Supplies the shared gear parts (#573). The cache asks for "the part for gear file id X" and never parses a file
    /// itself, so a source other than a retail DAT (a glTF asset pack's importer) can supply a part later without touching
    /// the cache. A part binds to the race skeleton it is given and plays the wearer's shared motion set.
    /// </summary>
    public interface ICharacterPartSource
    {
        /// <summary>
        /// The part named by <paramref name="key"/>, its meshes baked against <paramref name="skeleton"/>'s bind pose; null
        /// when the file is missing.
        /// </summary>
        CharacterPart? LoadPart(CharacterPartKey key, Skeleton? skeleton);
    }
}
