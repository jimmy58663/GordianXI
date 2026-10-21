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
        /// 3 marksmanship, 4 / 5 throwing, 6 archery, 10 / 11 handbell), which picks the <c>lc&lt;NN&gt;</c> / <c>ls&lt;NN&gt;</c> routines a
        /// ranged attack plays; -1 without one.
        /// </summary>
        public int RangedType { get; set; } = -1;

        /// <summary>
        /// Bit mask of the weapon slots (bit n = the <c>wepN</c> meshes) hidden while no routine says otherwise: what the
        /// model's <c>init</c> routine hides. A PC's <c>init</c> links <c>hwpc</c>, so its ranged weapon (slot 2) is hidden
        /// until a ranged attack shows it.
        /// </summary>
        public int DefaultHiddenWeaponSlots { get; set; }

        /// <summary>
        /// Whether the character fights with the dual-wield motion packs (#308): a sub weapon held in the left hand, both
        /// weapons of types with a dual-wield pack. Its off-hand hits (S2C 0x028 <c>sub_kind</c> 1) play the off-hand
        /// pack's <c>bti*</c> and moving <c>btf0</c> / <c>btb0</c> / <c>btl0</c> / <c>btr0</c>.
        /// </summary>
        public bool IsDualWielding { get; set; }

        /// <summary>
        /// Whether the model is a player character assembled from a race and gear (<c>AssembleCharacter</c>), whose melee
        /// swings play at the retail swing pace (#307, <c>EntityAnimationState.SwingSpeed</c>); false for fixed models.
        /// </summary>
        public bool IsCharacter { get; set; }

        /// <summary>The model's blur aura (Section 0x5E: avatars, Prime avatars), or null when it has none (#82).</summary>
        public BlurAura? BlurAura { get; set; }

        /// <summary>Rebuilds <see cref="MotionRoutines"/> from <see cref="RawMotionRoutines"/> against the current clips.</summary>
        internal void RebuildMotionRoutines()
        {
            MotionRoutines = MotionRoutineDecoder.BuildAll(RawMotionRoutines, clip => Animations.ContainsKey(clip), RangedType);
            _routineSounds.Clear();
            _offHandSounds.Clear();
            _routineEffects.Clear();
        }

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<RoutineEffectCue>> _routineEffects = new(StringComparer.Ordinal);

        /// <summary>
        /// The shared effect routines and spell effects routine <paramref name="name"/> starts on the actor, through its own
        /// links (#132, <see cref="RoutineEffectCollector"/>: a chant's circle, a release's flash); cached per name.
        /// </summary>
        public IReadOnlyList<RoutineEffectCue> GetRoutineEffects(string name) =>
            _routineEffects.GetOrAdd(name, n => RoutineEffectCollector.Collect(RawMotionRoutines, n));

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<RoutineSoundCue>> _routineSounds = new(StringComparer.Ordinal);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<RoutineSoundCue>> _offHandSounds = new(StringComparer.Ordinal);

        /// <summary>
        /// The sounds routine <paramref name="name"/> plays, through the actor's own links (#41,
        /// <see cref="RoutineSoundCollector"/>); empty when it has none. Cached per name. With <paramref name="offHand"/>
        /// (an off-hand swing or hit) links to the main weapon's sound routines go to the sub weapon's left-hand ones
        /// (<see cref="RoutineSoundCollector.OffHandRoutine"/>).
        /// </summary>
        public IReadOnlyList<RoutineSoundCue> GetRoutineSounds(string name, bool offHand = false) => offHand
            ? _offHandSounds.GetOrAdd(name, n => RoutineSoundCollector.Collect(RawMotionRoutines, n, offHand: true))
            : _routineSounds.GetOrAdd(name, n => RoutineSoundCollector.Collect(RawMotionRoutines, n));

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
