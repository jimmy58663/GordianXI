// src/Gordian.Core/Animation/ShieldBlockPose.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Animation
{
    /// <summary>
    /// The shield block pose (#546 round 1): the guard pose mirrored onto the shield arm.
    /// <para>
    /// A block plays the PC routine <c>gur1</c>, whose op 0x5A pose flash is pose 0, the same as the guard routine
    /// <c>gurd</c>. Pose 0 is the guard pose <c>gdm</c> (over its neutral twin <c>gdi6</c>), and that pose bends the weapon
    /// arm: the right hand rises about 0.7 yalm and comes 0.5 forward across the body, while the left arm barely moves. The
    /// maintainer saw the block come from the wrong hand (in-game test round 1). The game data has no other block pose, so
    /// retail picks the arm itself. Its block raises and thrusts the shield arm toward the attacker for about 0.2 s: in
    /// <c>Shield_Block_Retail.mp4</c> (the maintainer's retail recording of a paladin with a shield, 2026-11-13)
    /// the shield goes up toward the attacker at about 46.2 s and out at it at about 51.4 s, each for about 3 frames at 15
    /// fps, then back to the stance.
    /// </para>
    /// <para>
    /// So the block plays <c>gdm</c> / <c>gdi6</c> mirrored left for right: <see cref="PoseName"/> / <see cref="NeutralName"/>.
    /// The two arm chains (from the chest to the hand references 126 / 127) mirror each other in the skeleton: each left
    /// joint's bind rotation is its right twin's reflected through the body's midplane (model Z: (x, y, z, w) to
    /// (-x, -y, z, w)) and its offset has Z negated (checked on the Hume, Elvaan and Galka skeletons, 2026-11-13). So each
    /// arm joint takes its twin's track reflected the same way, and the spine joints below the chest take their own track
    /// reflected where their bind is symmetric. Other joints (fingers, mounts, legs) keep the guard's tracks. *Inference*:
    /// that retail mirrors the guard rather than playing a pose of its own is read from the video.
    /// </para>
    /// </summary>
    public static class ShieldBlockPose
    {
        /// <summary>The mirrored guard pose, played over <see cref="NeutralName"/> by a block.</summary>
        public const string PoseName = "bkm";

        /// <summary>The mirrored neutral twin of the guard pose (<c>gdi6</c>).</summary>
        public const string NeutralName = "bki6";

        private const string GuardPose = "gdm", GuardNeutral = "gdi6";
        private const int LeftHandReference = 126, RightHandReference = 127;

        /// <summary>
        /// Adds <see cref="PoseName"/> and <see cref="NeutralName"/> to a PC model carrying the guard pose and its twin. Does
        /// nothing when the model lacks them or its arm chains do not pair up.
        /// </summary>
        public static void AddTo(EntityModel model)
        {
            ArgumentNullException.ThrowIfNull(model);
            if (model.Skeleton is not { } skeleton
                || !model.Animations.TryGetValue(GuardPose, out var guard)
                || !model.Animations.TryGetValue(GuardNeutral, out var neutral)) return;
            var mirror = MirrorMap(skeleton);
            if (mirror == null) return;
            if (HandTravel(skeleton, guard, neutral, RightHandReference) <= HandTravel(skeleton, guard, neutral, LeftHandReference))
            {
                // The guard already moves the shield hand more (Tarutaru and Elvaan female: a small guard, 0.1-0.2 yalm,
                // slightly to the left): the block plays it as it is.
                model.Animations[PoseName] = guard;
                model.Animations[NeutralName] = neutral;
                return;
            }
            model.Animations[PoseName] = Mirror(guard, PoseName, mirror);
            model.Animations[NeutralName] = Mirror(neutral, NeutralName, mirror);
        }

        /// <summary>How far the pose moves a hand (reference) from where its neutral twin holds it, in yalms (model space).</summary>
        private static float HandTravel(Skeleton skeleton, AnimationClip pose, AnimationClip neutral, int handReference)
        {
            var a = Gordian.Core.Resources.Graphics.SkeletonPoseEvaluator.EvaluatePose(skeleton, pose, 0f, false);
            var b = Gordian.Core.Resources.Graphics.SkeletonPoseEvaluator.EvaluatePose(skeleton, neutral, 0f, false);
            return Vector3.Distance(
                Skeleton.ResolveJointReference(skeleton, a.Translations, a.Rotations, a.Scales, handReference),
                Skeleton.ResolveJointReference(skeleton, b.Translations, b.Rotations, b.Scales, handReference));
        }

        /// <summary>
        /// Each joint's mirror twin for the arm chains, itself for a symmetric spine joint, or -1 for a joint left as it is;
        /// null when the skeleton's arms do not pair up.
        /// </summary>
        internal static int[]? MirrorMap(Skeleton skeleton)
        {
            int n = skeleton.Count;
            if (skeleton.References.Count <= RightHandReference) return null;
            int left = skeleton.References[LeftHandReference].Index, right = skeleton.References[RightHandReference].Index;
            if (left >= n || right >= n || left == right) return null;
            var leftChain = Chain(skeleton, left);
            var rightChain = Chain(skeleton, right);
            if (leftChain.Count != rightChain.Count) return null;

            var map = new int[n];
            Array.Fill(map, -1);
            int shared = 0;
            while (shared < leftChain.Count && leftChain[shared] == rightChain[shared]) shared++;
            if (shared == 0) return null;
            for (int k = 0; k < shared; k++)
            {
                int joint = leftChain[k];
                if (Differs(skeleton.Joints[joint].Rotation, Reflect(skeleton.Joints[joint].Rotation)) < 1e-3f) map[joint] = joint;
            }
            for (int k = shared; k < leftChain.Count; k++)
            {
                int a = leftChain[k], b = rightChain[k];
                // The hand joints themselves differ by more than a reflection (their own frames); the chains above them pair up.
                if (k < leftChain.Count - 1 && Differs(skeleton.Joints[a].Rotation, Reflect(skeleton.Joints[b].Rotation)) > 1e-2f) return null;
                map[a] = b;
                map[b] = a;
            }
            return map;
        }

        private static List<int> Chain(Skeleton skeleton, int joint)
        {
            var chain = new List<int>();
            for (int guard = 0; joint >= 0 && joint < skeleton.Count && guard <= skeleton.Count; guard++)
            {
                chain.Add(joint);
                int parent = skeleton.Joints[joint].Parent;
                joint = parent == joint ? -1 : parent;
            }
            chain.Reverse();
            return chain;
        }

        private static AnimationClip Mirror(AnimationClip source, string name, int[] map)
        {
            var tracks = new Dictionary<int, BoneAnimationTrack>(source.Tracks.Count);
            foreach (var (joint, track) in source.Tracks)
            {
                if (joint >= map.Length || map[joint] < 0) tracks[joint] = track;
            }
            for (int joint = 0; joint < map.Length; joint++)
            {
                int twin = map[joint];
                if (twin < 0) continue;
                if (source.Tracks.TryGetValue(twin, out var track)) tracks[joint] = Reflect(track, joint);
            }
            return new AnimationClip
            {
                Name = name,
                NumFrames = source.NumFrames,
                KeyFrameDuration = source.KeyFrameDuration,
                Tracks = tracks
            };
        }

        private static BoneAnimationTrack Reflect(BoneAnimationTrack track, int joint)
        {
            var rotations = new Quaternion[track.Rotations.Length];
            for (int i = 0; i < rotations.Length; i++) rotations[i] = Reflect(track.Rotations[i]);
            var translations = new Vector3[track.Translations.Length];
            for (int i = 0; i < translations.Length; i++) translations[i] = new Vector3(track.Translations[i].X, track.Translations[i].Y, -track.Translations[i].Z);
            return new BoneAnimationTrack
            {
                JointIndex = joint,
                Rotations = rotations,
                Translations = translations,
                Scales = track.Scales,
                PartDurationSeconds = track.PartDurationSeconds,
                IsReset = track.IsReset,
                LoopsAlways = track.LoopsAlways
            };
        }

        /// <summary>A rotation reflected through the model's midplane (normal Z).</summary>
        internal static Quaternion Reflect(Quaternion q) => new(-q.X, -q.Y, q.Z, q.W);

        private static float Differs(Quaternion a, Quaternion b) =>
            1f - MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b)));
    }
}
