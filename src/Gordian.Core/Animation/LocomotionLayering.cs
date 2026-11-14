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
    /// Plays a moving swing on the upper body over the walk or run already playing on the lower body (#511 rounds 2-5).
    /// The PC moving swings (<c>atf0</c> / <c>atb0</c> / <c>atl0</c> / <c>atr0</c>, clips <c>amf</c> / <c>amb</c> / <c>aml</c> /
    /// <c>amr</c>) animate the torso and arms with the pelvis facing forward and the legs held still, while the strafe clips
    /// turn the pelvis 45 degrees (Hume male <c>cmvl</c> / <c>cmvr</c>: joint 2, with the spine root, joint 48, turned back the
    /// other way). So the body is split by the skeleton's hierarchy: the chain from the root to the pelvis, the legs and the
    /// spine up to the chest come from the locomotion, so the torso keeps the walk or run's twist (round 6); everything else
    /// (the arms, the head, and the gear and weapon mount joints) comes from the swing, placed rigidly on the locomotion's
    /// chest.
    /// <para>
    /// The pelvis is found from the hands (joint references 126 / 127 on every PC skeleton): up the spine from the joint the
    /// two arms meet at, the first joint that also carries a long chain (four or more joints deep: a leg). Hume male: the arms
    /// meet at 50, the spine starts at 48, the pelvis is 2 and the legs hang from 25. Elvaan male: 28, 26, 2 and 3. Galka: 38,
    /// 36, 2 and 3. The first rule tried, the first joint with two or more children, stops at Elvaan male joint 1, which carries
    /// 25 mount joints, and took the legs for the upper body (round 5). The mount joints matter too: a hand-to-hand or
    /// dual-wield off-hand mount hangs from the pelvis and the battle clips carry it into the hand, so it must follow the
    /// swing, not the run. *Inference* from the retail clips and skeletons (2026-11-13); no reference describes the layering.
    /// </para>
    /// </summary>
    public static class LocomotionLayering
    {
        private static readonly ConditionalWeakTable<Skeleton, SplitInfo> Splits = new();

        /// <summary>Joint references of the left and right hands on the PC skeletons (xi-tools skeleton reference table).</summary>
        private const int LeftHandReference = 126, RightHandReference = 127;

        /// <summary>A branch at least this many joints deep hanging from the pelvis is a leg.</summary>
        private const int LegDepth = 4;

        private sealed record SplitInfo(int UpperRoot, int Pelvis, int Chest, bool[] Lower, bool[] Spine);

        /// <summary>The joint the spine starts at (the upper body's root), or -1 when the skeleton cannot be split.</summary>
        public static int UpperBodyRoot(Skeleton skeleton) => SplitOf(skeleton).UpperRoot;

        /// <summary>The chest: the joint the two arms meet at, the top of the spine the locomotion keeps, or -1.</summary>
        public static int Chest(Skeleton skeleton) => SplitOf(skeleton).Chest;

        /// <summary>Whether <paramref name="joint"/> is on the spine from its root up to the chest (kept from the locomotion).</summary>
        public static bool IsSpine(Skeleton skeleton, int joint) =>
            joint >= 0 && joint < skeleton.Count && SplitOf(skeleton).Spine[joint];

        /// <summary>The pelvis: the joint both the spine and the legs hang from, or -1.</summary>
        public static int Pelvis(Skeleton skeleton) => SplitOf(skeleton).Pelvis;

        private static SplitInfo SplitOf(Skeleton skeleton)
        {
            ArgumentNullException.ThrowIfNull(skeleton);
            return Splits.GetValue(skeleton, Compute);

            static SplitInfo Compute(Skeleton skeleton)
            {
                int n = skeleton.Count;
                var none = new SplitInfo(-1, -1, -1, new bool[n], new bool[n]);
                if (skeleton.References.Count <= RightHandReference) return none;
                int left = skeleton.References[LeftHandReference].Index, right = skeleton.References[RightHandReference].Index;
                if (left >= n || right >= n) return none;

                var children = new List<int>[n];
                for (int i = 0; i < n; i++) children[i] = new List<int>();
                for (int i = 0; i < n; i++)
                {
                    int parent = ParentOf(skeleton, i);
                    if (parent >= 0) children[parent].Add(i);
                }
                var depth = new int[n];
                int Depth(int j)
                {
                    if (depth[j] > 0) return depth[j];
                    int deepest = 0;
                    foreach (int c in children[j]) deepest = Math.Max(deepest, Depth(c));
                    return depth[j] = deepest + 1;
                }

                // Where the two arms meet: the deepest joint above both hands.
                var aboveLeft = new HashSet<int>();
                for (int j = left, guard = 0; j >= 0 && guard <= n; j = ParentOf(skeleton, j), guard++) aboveLeft.Add(j);
                int meet = right;
                for (int guard = 0; meet >= 0 && !aboveLeft.Contains(meet) && guard <= n; guard++) meet = ParentOf(skeleton, meet);
                if (meet < 0) return none;

                // Up the spine to the joint that also carries a leg.
                int upper = meet;
                for (int guard = 0; guard <= n; guard++)
                {
                    int pelvis = ParentOf(skeleton, upper);
                    if (pelvis < 0) return none;
                    bool carriesLeg = false;
                    foreach (int c in children[pelvis]) carriesLeg |= c != upper && Depth(c) >= LegDepth;
                    if (carriesLeg)
                    {
                        var lower = new bool[n];
                        for (int j = pelvis, g = 0; j >= 0 && g <= n; j = ParentOf(skeleton, j), g++) lower[j] = true;
                        var stack = new Stack<int>();
                        foreach (int c in children[pelvis])
                        {
                            if (c != upper && Depth(c) >= LegDepth) stack.Push(c);
                        }
                        while (stack.Count > 0)
                        {
                            int j = stack.Pop();
                            lower[j] = true;
                            foreach (int c in children[j]) stack.Push(c);
                        }
                        var spine = new bool[n];
                        for (int j = meet, g = 0; j >= 0 && g <= n; j = ParentOf(skeleton, j), g++)
                        {
                            spine[j] = true;
                            if (j == upper) break;
                        }
                        return new SplitInfo(upper, pelvis, meet, lower, spine);
                    }
                    upper = pelvis;
                }
                return none;
            }
        }

        private static int ParentOf(Skeleton skeleton, int joint)
        {
            int parent = skeleton.Joints[joint].Parent;
            return parent >= 0 && parent < skeleton.Count && parent != joint ? parent : -1;
        }

        /// <summary>
        /// Whether joint <paramref name="joint"/> follows the swing: anything not on the root-to-pelvis chain or a leg, and any
        /// joint <paramref name="parentOverrides"/> re-parents into it (the weapon grips held in the hands).
        /// </summary>
        public static bool IsUpperBody(Skeleton skeleton, int joint, IReadOnlyDictionary<int, int>? parentOverrides = null)
        {
            var split = SplitOf(skeleton);
            if (split.UpperRoot < 0 || joint < 0 || joint >= skeleton.Count) return false;
            if (parentOverrides != null && parentOverrides.TryGetValue(joint, out int target) && target >= 0 && target < skeleton.Count)
            {
                return !split.Lower[target];
            }
            return !split.Lower[joint];
        }

        /// <summary>
        /// The pose with the swing of <paramref name="upper"/> on the body of <paramref name="lower"/> (the locomotion). The legs,
        /// the pelvis and the spine up to the chest are the locomotion's, so the torso keeps the twist the walk or run gives it
        /// (a strafe turns the shoulders about 70 degrees from the battle stance; a swing's own torso faces the target square,
        /// round 6). Everything else that follows the swing (the arms, the head, the weapon grips and mounts) keeps its place
        /// relative to the swing's chest, turned and moved rigidly so that chest lands on the locomotion's. Both poses are model
        /// space, from <see cref="SkeletonPoseEvaluator"/> with the same <paramref name="parentOverrides"/>.
        /// </summary>
        public static SkeletonPoseEvaluator.EvaluatedPose Combine(Skeleton skeleton, SkeletonPoseEvaluator.EvaluatedPose upper,
            SkeletonPoseEvaluator.EvaluatedPose lower, IReadOnlyDictionary<int, int>? parentOverrides = null)
        {
            ArgumentNullException.ThrowIfNull(skeleton);
            var split = SplitOf(skeleton);
            int chest = split.Chest;
            int n = Math.Min(upper.Rotations.Length, lower.Rotations.Length);
            if (split.UpperRoot < 0 || chest < 0 || chest >= n) return upper;

            var rotations = (Quaternion[])lower.Rotations.Clone();
            var translations = (Vector3[])lower.Translations.Clone();
            var scales = lower.Scales.Length == n ? (Vector3[])lower.Scales.Clone() : new Vector3[n];
            if (lower.Scales.Length != n) Array.Fill(scales, Vector3.One);
            var turn = Quaternion.Normalize(lower.Rotations[chest] * Quaternion.Inverse(upper.Rotations[chest]));
            for (int j = 0; j < n; j++)
            {
                if (split.Spine[j] || !IsUpperBody(skeleton, j, parentOverrides)) continue;
                rotations[j] = Quaternion.Normalize(turn * upper.Rotations[j]);
                translations[j] = lower.Translations[chest] + Vector3.Transform(upper.Translations[j] - upper.Translations[chest], turn);
                if (upper.Scales.Length == n) scales[j] = upper.Scales[j];
            }
            TurnHeadWithTheBody(skeleton, split, upper, lower, turn, rotations, translations, n);
            return new SkeletonPoseEvaluator.EvaluatedPose(rotations, translations, scales);
        }

        /// <summary>Skeleton references of the neck and the head (the joint the face is bound to) on the PC skeletons.</summary>
        private const int NeckReference = 3, HeadReference = 5;

        /// <summary>
        /// The head under a moving swing (#548 round 1): a moving swing twists the chest a long way (Elvaan male <c>amr</c> about
        /// 140 degrees at the strike) while the head turns much less (under 45 degrees from the pelvis), so the head keeps looking
        /// ahead. Placed on the locomotion's chest with the chest's turn, as the arms are, the head swung about 60 degrees off to
        /// one side and back within a few frames: the maintainer saw the head look "parallel with my shoulder" for a couple of
        /// frames while strafing and attacking. So the head, and everything below it, turns with the body instead (the turn
        /// that puts the swing's pelvis on the locomotion's), and the neck halfway between the two. At the start and end of
        /// the swing the two turns agree, so the head meets the locomotion's own. Measured on the real path
        /// (<c>MovingSwingRealPathTests</c>, 2026-11-13).
        /// </summary>
        private static void TurnHeadWithTheBody(Skeleton skeleton, SplitInfo split, SkeletonPoseEvaluator.EvaluatedPose upper,
            SkeletonPoseEvaluator.EvaluatedPose lower, Quaternion chestTurn, Quaternion[] rotations, Vector3[] translations, int n)
        {
            if (skeleton.References.Count <= HeadReference || split.Pelvis < 0 || split.Pelvis >= n) return;
            int neck = skeleton.References[NeckReference].Index, head = skeleton.References[HeadReference].Index;
            if (neck >= n || head >= n || split.Spine[neck] || split.Lower[neck] || !IsBelow(skeleton, head, neck)
                || !IsBelow(skeleton, neck, split.Chest)) return;

            var bodyTurn = Quaternion.Normalize(lower.Rotations[split.Pelvis] * Quaternion.Inverse(upper.Rotations[split.Pelvis]));
            if (Quaternion.Dot(bodyTurn, chestTurn) < 0f) bodyTurn = -bodyTurn;
            var neckTurn = Quaternion.Normalize(Quaternion.Slerp(chestTurn, bodyTurn, 0.5f));

            // Parents before children: the neck keeps its place on the chest, each joint below it hangs from its parent as in
            // the swing, turned by the parent's turn.
            var turns = new Quaternion[n];
            var done = new bool[n];
            for (int pass = 0; pass < n; pass++)
            {
                bool progressed = false;
                for (int j = 0; j < n; j++)
                {
                    if (done[j] || !IsBelow(skeleton, j, neck)) continue;
                    int parent = ParentOf(skeleton, j);
                    if (j != neck && (parent < 0 || !done[parent])) continue;
                    turns[j] = j == neck ? neckTurn : IsBelow(skeleton, j, head) ? bodyTurn : neckTurn;
                    rotations[j] = Quaternion.Normalize(turns[j] * upper.Rotations[j]);
                    if (j != neck) translations[j] = translations[parent] + Vector3.Transform(upper.Translations[j] - upper.Translations[parent], turns[parent]);
                    done[j] = progressed = true;
                }
                if (!progressed) break;
            }
        }

        /// <summary>Whether <paramref name="joint"/> is <paramref name="ancestor"/> or below it.</summary>
        private static bool IsBelow(Skeleton skeleton, int joint, int ancestor)
        {
            for (int guard = 0; joint >= 0 && guard <= skeleton.Count; guard++)
            {
                if (joint == ancestor) return true;
                joint = ParentOf(skeleton, joint);
            }
            return false;
        }
    }
}
