// src/Gordian.Core/Resources/Models/EntityModel.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Graphics;

namespace Gordian.Core.Resources.Models
{
    /// <summary>
    /// Complete assembled 3D entity model containing skeletal hierarchy,
    /// baked bind-pose submeshes, and texture palettes.
    /// Clean-room implementation referencing FFXI entity model specifications.
    /// </summary>
    public sealed class EntityModel
    {
        public string Name { get; set; } = string.Empty;
        public Skeleton? Skeleton { get; set; }
        public List<AnimatedMeshGroup> AnimatedMeshGroups { get; } = new();
        public Dictionary<string, DecodedTexture> Textures { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, AnimationClip> Animations { get; } = new(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyDictionary<int, int>? ParentOverrides { get; set; }

        /// <summary>
        /// The actor's motion routines by name (Section 0x07 of its model and motion packs, flattened for playback), e.g.
        /// the swings <c>ati0</c>-<c>ati2</c>, the chant <c>cabk</c>, the reactions <c>damg</c> and <c>gurd</c>.
        /// </summary>
        public Dictionary<string, MotionRoutine> MotionRoutines { get; private set; } = new(StringComparer.Ordinal);

        /// <summary>The raw routines <see cref="MotionRoutines"/> is built from; a later source replaces a same-named one.</summary>
        internal Dictionary<string, RawMotionRoutine> RawMotionRoutines { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// The equipped ranged weapon's RangeType (its DAT's Info byte 14: 1 wind instrument, 2 string instrument,
        /// 3 marksmanship, 4 / 5 throwing, 6 archery, 11 handbell), which picks the <c>lc&lt;NN&gt;</c> / <c>ls&lt;NN&gt;</c> routines a
        /// ranged attack plays; -1 without one.
        /// </summary>
        public int RangedType { get; set; } = -1;

        /// <summary>
        /// Bit mask of the weapon slots (bit n = the <c>wepN</c> meshes) hidden while no routine says otherwise: what the
        /// model's <c>init</c> routine hides. A PC's <c>init</c> links <c>hwpc</c>, so its ranged weapon (slot 2) is hidden
        /// until a ranged attack shows it.
        /// </summary>
        public int DefaultHiddenWeaponSlots { get; set; }

        /// <summary>Rebuilds <see cref="MotionRoutines"/> from <see cref="RawMotionRoutines"/> against the current clips.</summary>
        internal void RebuildMotionRoutines()
        {
            MotionRoutines = MotionRoutineDecoder.BuildAll(RawMotionRoutines, clip => Animations.ContainsKey(clip), RangedType);
        }

        public Vector3 MinBounds { get; private set; } = new(float.MaxValue);
        public Vector3 MaxBounds { get; private set; } = new(float.MinValue);

        public int TotalVertices
        {
            get
            {
                int count = 0;
                for (int i = 0; i < AnimatedMeshGroups.Count; i++) count += AnimatedMeshGroups[i].Vertices.Length;
                return count;
            }
        }

        public int TotalTriangles
        {
            get
            {
                int count = 0;
                for (int i = 0; i < AnimatedMeshGroups.Count; i++) count += AnimatedMeshGroups[i].TriangleCount;
                return count;
            }
        }

        /// <summary>
        /// Recalculates the composite bounding box across all constituent mesh groups.
        /// </summary>
        public void UpdateBounds()
        {
            if (AnimatedMeshGroups.Count == 0)
            {
                MinBounds = -Vector3.One;
                MaxBounds = Vector3.One;
                return;
            }

            Vector3 min = new(float.MaxValue);
            Vector3 max = new(float.MinValue);

            for (int i = 0; i < AnimatedMeshGroups.Count; i++)
            {
                var g = AnimatedMeshGroups[i];
                if (g.Vertices.Length == 0) continue;
                // A weapon hidden until an action shows it (a stowed bow) lies at its bind pose, by the feet: not part of the body.
                if (IsWeaponSlotHidden(DefaultHiddenWeaponSlots, g.WeaponSlot)) continue;

                min = Vector3.Min(min, g.MinBounds);
                max = Vector3.Max(max, g.MaxBounds);
            }

            if (min.X > max.X)
            {
                min = -Vector3.One;
                max = Vector3.One;
            }
            MinBounds = min;
            MaxBounds = max;
        }

        /// <summary>Whether a mesh group of weapon slot <paramref name="slot"/> (-1 for none) is hidden under a slot mask.</summary>
        public static bool IsWeaponSlotHidden(int hiddenSlotMask, int slot) => slot is >= 0 and < 32 && (hiddenSlotMask & (1 << slot)) != 0;

        public override string ToString() => $"EntityModel [{Name}] ({AnimatedMeshGroups.Count} meshes, {Textures.Count} textures, {TotalTriangles} tris)";
    }
}
