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

        /// <summary>Rebuilds <see cref="MotionRoutines"/> from <see cref="RawMotionRoutines"/> against the current clips.</summary>
        internal void RebuildMotionRoutines()
        {
            MotionRoutines = MotionRoutineDecoder.BuildAll(RawMotionRoutines, clip => Animations.ContainsKey(clip));
            _routineSounds.Clear();
        }

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<RoutineSoundCue>> _routineSounds = new(StringComparer.Ordinal);

        /// <summary>
        /// The sounds routine <paramref name="name"/> plays, through the actor's own links (#41,
        /// <see cref="RoutineSoundCollector"/>); empty when it has none. Cached per name.
        /// </summary>
        public IReadOnlyList<RoutineSoundCue> GetRoutineSounds(string name) =>
            _routineSounds.GetOrAdd(name, n => RoutineSoundCollector.Collect(RawMotionRoutines, n));

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

                min = Vector3.Min(min, g.MinBounds);
                max = Vector3.Max(max, g.MaxBounds);
            }

            MinBounds = min;
            MaxBounds = max;
        }

        public override string ToString() => $"EntityModel [{Name}] ({AnimatedMeshGroups.Count} meshes, {Textures.Count} textures, {TotalTriangles} tris)";
    }
}
