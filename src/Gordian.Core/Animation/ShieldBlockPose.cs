// src/Gordian.Core/Animation/ShieldBlockPose.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Animation
{
    /// <summary>
    /// The shield block (#546 rounds 1-4): the guard pose <c>gdm</c> played on the shield arm alone.
    /// <para>
    /// A block plays the PC routine <c>gur1</c>, whose op 0x5A pose flash is pose 0, the guard pose <c>gdm</c> of the guard
    /// routine <c>gurd</c> (xi-tools names the op IdleCasterGuard); the game data has no block pose of its own. <c>gdm</c> is a
    /// single pose (2 keys) over most of the body that brings both hands up to the middle of the chest (as XI Model Viewer
    /// shows it). Played on the whole body as an offset from its neutral twin <c>gdi6</c>, as a plain guard plays it, it
    /// moves mostly the weapon arm, so the block came from the wrong hand (in-game test round 1). Round 2 mirrored it onto
    /// the shield arm and round 3 turned the shield arm procedurally to face ahead; neither looked like retail (rounds 3-4).
    /// The maintainer's suggestion (round 4): <c>gdm</c> on the shield side only. So a block blends the shield arm (the joint
    /// where the arm leaves the chest, <see cref="ShieldMount.Pivot"/>, and every joint below it, <see cref="ArmMask"/>) toward
    /// <c>gdm</c> as it is (not its offset from <c>gdi6</c>, which is small on that arm: Hume male's shield moves under 0.2
    /// yalm): the joints below the pivot take <c>gdm</c>'s local rotations, and the pivot turns the arm to point where
    /// <c>gdm</c> points it relative to the body (<see cref="Apply"/>), while the other arm and the body stay on whatever is
    /// playing. The maintainer's retail recording
    /// <c>Shield_Block_Retail.mp4</c> (a paladin, 2026-11-13) shows the shield pulled across the chest toward the attacker at
    /// about 51.4 s, held about 8 frames at 30 fps (about 0.27 s, the routine's 16-tick flash), then back.
    /// </para>
    /// <para>
    /// The shield is found from its mesh (<see cref="Find"/>: the off-hand slot's vertices, all bound to one arm joint on every
    /// race checked: Hume male 87, Galka 74, Tarutaru male 28, Elvaan female 94).
    /// </para>
    /// </summary>
    public static class ShieldBlockPose
    {
        private const int LeftHandReference = 126, RightHandReference = 127;

        /// <summary>The off-hand weapon slot (the shield's meshes).</summary>
        private const int SubSlot = 1;

        /// <summary>
        /// Where the shield sits on the arm: the joint its meshes are bound to, and the shield's centre and face normal in
        /// that joint's frame; <see cref="Pivot"/> is the joint where the arm leaves the chest, <see cref="Chest"/> its parent.
        /// </summary>
        public sealed record ShieldMount(int Joint, Vector3 LocalCentre, Vector3 LocalNormal, int Pivot, int Chest);

        /// <summary>
        /// The model's shield, or null: the off-hand slot's meshes bound wholly to one joint of the left arm, flat (a plate:
        /// its thickness under a third of its width), as the shields (sub looks 19-63) are.
        /// </summary>
        public static ShieldMount? Find(EntityModel model)
        {
            ArgumentNullException.ThrowIfNull(model);
            if (model.Skeleton is not { } skeleton || skeleton.References.Count <= RightHandReference) return null;
            int joint = -1;
            var points = new List<Vector3>();
            foreach (var group in model.AnimatedMeshGroups)
            {
                if (group.WeaponSlot != SubSlot) continue;
                foreach (var v in group.Vertices)
                {
                    if (v.Joint1 >= 0 || (joint >= 0 && v.Joint0 != joint)) return null;
                    joint = v.Joint0;
                    points.Add(v.Position0);
                }
            }
            if (joint < 0 || points.Count < 8 || joint >= skeleton.Count) return null;

            int left = skeleton.References[LeftHandReference].Index, right = skeleton.References[RightHandReference].Index;
            if (left >= skeleton.Count || right >= skeleton.Count) return null;
            var aboveRight = new HashSet<int>();
            for (int j = right, guard = 0; j >= 0 && guard <= skeleton.Count; j = Parent(skeleton, j), guard++) aboveRight.Add(j);
            int pivot = -1;
            for (int j = left, guard = 0; j >= 0 && guard <= skeleton.Count; j = Parent(skeleton, j), guard++)
            {
                if (aboveRight.Contains(j)) break;
                pivot = j;
            }
            if (pivot < 0 || Parent(skeleton, pivot) < 0 || !IsBelow(skeleton, joint, pivot)) return null;

            var (centre, normal, flat) = Plane(points);
            return flat ? new ShieldMount(joint, centre, normal, pivot, Parent(skeleton, pivot)) : null;
        }

        /// <summary>
        /// The joints the block's guard pose may move by their local rotations: those below the shield arm's pivot (a
        /// re-parented joint by its new parent) that <paramref name="poseMask"/> allows (null: all). The pivot itself is
        /// turned by <see cref="Apply"/>.
        /// </summary>
        public static bool[] ArmMask(Skeleton skeleton, ShieldMount mount, bool[]? poseMask, IReadOnlyDictionary<int, int>? parentOverrides = null)
        {
            ArgumentNullException.ThrowIfNull(skeleton);
            ArgumentNullException.ThrowIfNull(mount);
            var mask = new bool[skeleton.Count];
            for (int j = 0; j < mask.Length; j++)
            {
                mask[j] = j != mount.Pivot && (poseMask == null || (j < poseMask.Length && poseMask[j])) && IsBelow(skeleton, j, mount.Pivot, parentOverrides);
            }
            return mask;
        }

        /// <summary>
        /// Turns the shield arm of <paramref name="pose"/> (model space, in place) rigidly about its pivot so the pivot's
        /// rotation moves toward <paramref name="guardPivot"/> (the pivot's model-space rotation in <c>gdm</c> played alone) by
        /// <paramref name="weight"/>: the arm points where the guard points it relative to the body, whatever the chest is
        /// doing (Galka's and Elvaan male's <c>gdm</c> bend the chest forward, so the guard's arm on their upright stance chest
        /// would hold the shield over the head).
        /// </summary>
        public static void Apply(Skeleton skeleton, in SkeletonPoseEvaluator.EvaluatedPose pose, ShieldMount mount, Quaternion guardPivot,
            float weight, IReadOnlyDictionary<int, int>? parentOverrides = null)
        {
            ArgumentNullException.ThrowIfNull(skeleton);
            ArgumentNullException.ThrowIfNull(mount);
            int n = Math.Min(skeleton.Count, pose.Rotations.Length);
            if (weight <= 0f || mount.Pivot >= n) return;
            var turn = Quaternion.Normalize(guardPivot * Quaternion.Conjugate(pose.Rotations[mount.Pivot]));
            if (turn.W < 0f) turn = -turn;
            turn = Quaternion.Normalize(Quaternion.Slerp(Quaternion.Identity, turn, Math.Min(weight, 1f)));
            Vector3 pivot = pose.Translations[mount.Pivot];
            for (int j = 0; j < n; j++)
            {
                if (!IsBelow(skeleton, j, mount.Pivot, parentOverrides)) continue;
                pose.Translations[j] = pivot + Vector3.Transform(pose.Translations[j] - pivot, turn);
                pose.Rotations[j] = Quaternion.Normalize(turn * pose.Rotations[j]);
            }
        }

        /// <summary>The shield's centre and outward face normal (away from the chest) in a model-space pose.</summary>
        public static (Vector3 Centre, Vector3 Outward) ShieldIn(in SkeletonPoseEvaluator.EvaluatedPose pose, ShieldMount mount)
        {
            ArgumentNullException.ThrowIfNull(mount);
            var rotation = pose.Rotations[mount.Joint];
            var centre = pose.Translations[mount.Joint] + Vector3.Transform(mount.LocalCentre, rotation);
            var normal = Vector3.Normalize(Vector3.Transform(mount.LocalNormal, rotation));
            return (centre, Vector3.Dot(normal, centre - pose.Translations[mount.Chest]) >= 0f ? normal : -normal);
        }

        /// <summary>The points' centre and the normal of their best plane, and whether they lie flat.</summary>
        private static (Vector3 Centre, Vector3 Normal, bool Flat) Plane(List<Vector3> points)
        {
            var c = Vector3.Zero;
            foreach (var p in points) c += p;
            c /= points.Count;
            float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (var p in points)
            {
                var d = p - c;
                xx += d.X * d.X; xy += d.X * d.Y; xz += d.X * d.Z; yy += d.Y * d.Y; yz += d.Y * d.Z; zz += d.Z * d.Z;
            }
            const float e = 1e-6f;
            var m = new Matrix4x4(xx + e, xy, xz, 0, xy, yy + e, yz, 0, xz, yz, zz + e, 0, 0, 0, 0, 1);
            if (!Matrix4x4.Invert(m, out var inverse)) return (c, Vector3.UnitX, false);
            // Inverse power iteration: the covariance's smallest axis, the plate's normal.
            var normal = Vector3.Normalize(new Vector3(1f, 0.3f, 0.2f));
            for (int i = 0; i < 60; i++) normal = Vector3.Normalize(Vector3.TransformNormal(normal, inverse));
            float thin = 0f, wide = 0f;
            foreach (var p in points)
            {
                var d = p - c;
                float across = Vector3.Dot(d, normal);
                thin = MathF.Max(thin, MathF.Abs(across));
                wide = MathF.Max(wide, (d - across * normal).Length());
            }
            return (c, normal, thin < wide / 3f);
        }

        private static int Parent(Skeleton skeleton, int joint)
        {
            int parent = skeleton.Joints[joint].Parent;
            return parent >= 0 && parent < skeleton.Count && parent != joint ? parent : -1;
        }

        /// <summary>Whether <paramref name="joint"/> is <paramref name="ancestor"/> or hangs below it (a re-parented joint by its new parent).</summary>
        private static bool IsBelow(Skeleton skeleton, int joint, int ancestor, IReadOnlyDictionary<int, int>? parentOverrides = null)
        {
            for (int guard = 0; joint >= 0 && joint < skeleton.Count && guard <= skeleton.Count; guard++)
            {
                if (joint == ancestor) return true;
                joint = parentOverrides != null && parentOverrides.TryGetValue(joint, out int over) ? over : Parent(skeleton, joint);
            }
            return false;
        }
    }
}
