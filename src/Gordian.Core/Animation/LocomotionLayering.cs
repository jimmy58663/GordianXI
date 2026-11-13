// src/Gordian.Core/Animation/LocomotionLayering.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Animation
{
    /// <summary>
    /// Plays a moving swing on the upper body over the walk or run already playing on the lower body (#511 rounds 2-4).
    /// The PC moving swings (<c>atf0</c> / <c>atb0</c> / <c>atl0</c> / <c>atr0</c>, clips <c>amf</c> / <c>amb</c> / <c>aml</c> /
    /// <c>amr</c>) animate the torso and arms with the pelvis facing forward and the legs held still, while the strafe clips
    /// turn the pelvis 45 degrees (Hume male <c>cmvl</c> / <c>cmvr</c>: joint 2, with the spine root, joint 48, turned back the
    /// other way). So the body is split by the skeleton's hierarchy, not by which joints a clip moves: the root, the pelvis
    /// and everything under it except the spine (the hips and both legs, and the small joints hung on the pelvis) come from
    /// the locomotion; the spine and what hangs from it (the torso, arms and head), and the weapon joints re-parented into
    /// the hands, come from the swing, facing the way the swing has them and carried on the locomotion's pelvis.
    /// *Inference* from the retail clips and skeletons (2026-11-13); no reference describes the layering.
    /// </summary>
    public static class LocomotionLayering
    {
        private static readonly ConditionalWeakTable<Skeleton, SplitInfo> Splits = new();

        private sealed record SplitInfo(int UpperRoot, bool[] UpperSubtree);

        /// <summary>
        /// The joint the upper body hangs from: on the first joint down from the root with two or more children (the pelvis),
        /// the child with the largest subtree (the spine, carrying the arms and head; the hips carry only the legs). -1 when the
        /// skeleton has no such joint.
        /// </summary>
        public static int UpperBodyRoot(Skeleton skeleton) => SplitOf(skeleton).UpperRoot;

        private static SplitInfo SplitOf(Skeleton skeleton)
        {
            ArgumentNullException.ThrowIfNull(skeleton);
            return Splits.GetValue(skeleton, Compute);

            static SplitInfo Compute(Skeleton skeleton)
            {
                int n = skeleton.Count;
                var children = new List<int>[n];
                for (int i = 0; i < n; i++) children[i] = new List<int>();
                int root = -1;
                for (int i = 0; i < n; i++)
                {
                    int parent = skeleton.Joints[i].Parent;
                    if (parent >= 0 && parent < n && parent != i) children[parent].Add(i);
                    else if (root < 0) root = i;
                }
                var subtree = new int[n];
                int Size(int j)
                {
                    if (subtree[j] > 0) return subtree[j];
                    int size = 1;
                    foreach (int c in children[j]) size += Size(c);
                    return subtree[j] = size;
                }

                int hub = root;
                for (int guard = 0; hub >= 0 && guard < n && children[hub].Count == 1; guard++) hub = children[hub][0];
                if (hub < 0 || children[hub].Count < 2) return new SplitInfo(-1, new bool[n]);

                int upper = -1;
                foreach (int c in children[hub])
                {
                    if (upper < 0 || Size(c) > Size(upper)) upper = c;
                }
                var mask = new bool[n];
                var stack = new Stack<int>();
                stack.Push(upper);
                while (stack.Count > 0)
                {
                    int j = stack.Pop();
                    mask[j] = true;
                    foreach (int c in children[j]) stack.Push(c);
                }
                return new SplitInfo(upper, mask);
            }
        }

        /// <summary>
        /// Whether joint <paramref name="joint"/> belongs to the upper body: under the spine, or a joint
        /// <paramref name="parentOverrides"/> re-parents into it (the weapon grips held in the hands), or under such a joint.
        /// </summary>
        public static bool IsUpperBody(Skeleton skeleton, int joint, IReadOnlyDictionary<int, int>? parentOverrides = null)
        {
            var split = SplitOf(skeleton);
            if (split.UpperRoot < 0) return false;
            for (int guard = 0, j = joint; j >= 0 && j < skeleton.Count && guard <= skeleton.Count; guard++)
            {
                if (split.UpperSubtree[j]) return true;
                if (parentOverrides != null && parentOverrides.TryGetValue(j, out int target))
                {
                    j = target;
                    continue;
                }
                int parent = skeleton.Joints[j].Parent;
                if (parent == j) break;
                j = parent;
            }
            return false;
        }

        /// <summary>
        /// The pose with the upper body of <paramref name="upper"/> (the swing) on the lower body of <paramref name="lower"/>
        /// (the locomotion): lower-body joints are the locomotion's; upper-body joints keep the swing's orientation and are
        /// moved so the spine starts where the locomotion's spine starts. Both poses are model space, from
        /// <see cref="SkeletonPoseEvaluator"/> with the same <paramref name="parentOverrides"/>.
        /// </summary>
        public static SkeletonPoseEvaluator.EvaluatedPose Combine(Skeleton skeleton, SkeletonPoseEvaluator.EvaluatedPose upper,
            SkeletonPoseEvaluator.EvaluatedPose lower, IReadOnlyDictionary<int, int>? parentOverrides = null)
        {
            ArgumentNullException.ThrowIfNull(skeleton);
            int root = UpperBodyRoot(skeleton);
            int n = Math.Min(upper.Rotations.Length, lower.Rotations.Length);
            if (root < 0 || root >= n) return upper;

            var rotations = (Quaternion[])lower.Rotations.Clone();
            var translations = (Vector3[])lower.Translations.Clone();
            var scales = lower.Scales.Length == n ? (Vector3[])lower.Scales.Clone() : new Vector3[n];
            if (lower.Scales.Length != n) Array.Fill(scales, Vector3.One);
            Vector3 offset = lower.Translations[root] - upper.Translations[root];
            for (int j = 0; j < n; j++)
            {
                if (!IsUpperBody(skeleton, j, parentOverrides)) continue;
                rotations[j] = upper.Rotations[j];
                translations[j] = upper.Translations[j] + offset;
                if (upper.Scales.Length == n) scales[j] = upper.Scales[j];
            }
            return new SkeletonPoseEvaluator.EvaluatedPose(rotations, translations, scales);
        }
    }
}
