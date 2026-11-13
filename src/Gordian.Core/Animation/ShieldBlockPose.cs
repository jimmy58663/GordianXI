// src/Gordian.Core/Animation/ShieldBlockPose.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Animation
{
    /// <summary>
    /// The shield block (#546 rounds 1-3): the shield arm swings the shield up in front of the chest, upright and facing
    /// the attacker.
    /// <para>
    /// A block plays the PC routine <c>gur1</c>, whose op 0x5A pose flash is pose 0, the guard pose <c>gdm</c> of the guard
    /// routine <c>gurd</c> (xi-tools names the op IdleCasterGuard). That guard is the weapon arm's (Hume male: the right hand
    /// rises about 1.0 yalm, the left about 0.1), so the block came from the wrong hand (in-game test round 1), and the game
    /// data has no block pose of its own. The maintainer's retail recording <c>Shield_Block_Retail.mp4</c> (a paladin with a
    /// shield, 2026-11-13) shows the block: at about 51.4 s the shield is held out in front of the chest toward the attacker,
    /// upright, its face toward the attacker, for about 8 frames at 30 fps (about 0.27 s), then back to the stance; at about
    /// 46.2 s it goes up toward the attacker the same way. Round 2 played the guard mirrored onto the shield arm; the arm
    /// chains' local frames mirror each other, but the guard's own motion does not make a block: in game the arm went out
    /// away from the attacker, and over the idle stance (a block while not engaged) the shield lay flat (round 3).
    /// </para>
    /// <para>
    /// So the block is procedural, laid on whatever pose is playing (<see cref="Apply"/>): the whole shield arm, from the
    /// joint where it leaves the chest (the first joint above the left hand reference 126 that is not above the right hand
    /// 127), turns rigidly so the shield's outward face points straight ahead (model +X, toward the attacker) and the shield
    /// comes in front of the chest. The turn keeps the angle between the arm and the shield's face, so the arm's direction
    /// is chosen to make the face land exactly ahead: forward by that angle, the rest down and out to the shield side. The
    /// shield is found from its mesh (<see cref="Find"/>: the off-hand slot's vertices, all bound to one arm joint on every
    /// race checked: Hume male 87, Galka 74, Tarutaru male 28, Elvaan female 94). *Inference*: the arm's path is ours; the
    /// video shows the end pose (and the body turning toward the attacker, which is not modelled).
    /// </para>
    /// </summary>
    public static class ShieldBlockPose
    {
        private const int LeftHandReference = 126, RightHandReference = 127;

        /// <summary>
        /// The least the arm points forward at the block (the cosine to straight ahead): a shield bound almost along the arm
        /// would otherwise be held out to the side to face ahead exactly; with this it comes in front, its face a little off.
        /// </summary>
        private const float MinForward = 0.65f;

        /// <summary>The off-hand weapon slot (the shield's meshes).</summary>
        private const int SubSlot = 1;

        /// <summary>
        /// Where the shield sits on the arm: the joint its meshes are bound to, and the shield's centre and face normal in
        /// that joint's frame; <see cref="Pivot"/> is the joint the arm turns about, <see cref="Chest"/> its parent.
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
        /// Turns the shield arm of <paramref name="pose"/> (model space, in place) toward the block by <paramref name="weight"/>
        /// (0 none, 1 the full block).
        /// </summary>
        public static void Apply(Skeleton skeleton, in SkeletonPoseEvaluator.EvaluatedPose pose, ShieldMount mount, float weight,
            IReadOnlyDictionary<int, int>? parentOverrides = null)
        {
            ArgumentNullException.ThrowIfNull(skeleton);
            ArgumentNullException.ThrowIfNull(mount);
            int n = Math.Min(skeleton.Count, pose.Rotations.Length);
            if (weight <= 0f || mount.Joint >= n || mount.Pivot >= n || mount.Chest >= n) return;
            var turn = Turn(pose, mount);
            if (weight < 1f) turn = Quaternion.Normalize(Quaternion.Slerp(Quaternion.Identity, turn, weight));
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

        /// <summary>
        /// The rigid turn about the pivot that brings the shield's outward face to straight ahead (+X) with the shield in
        /// front: the arm (pivot to shield centre) goes to a direction whose forward part equals the cosine between the arm and
        /// the face (which any turn keeps), the rest down and out to the shield side; then a twist about the arm sets the face.
        /// </summary>
        private static Quaternion Turn(in SkeletonPoseEvaluator.EvaluatedPose pose, ShieldMount mount)
        {
            var (centre, outward) = ShieldIn(pose, mount);
            var arm = centre - pose.Translations[mount.Pivot];
            if (arm.LengthSquared() < 1e-8f) return Quaternion.Identity;
            var along = Vector3.Normalize(arm);
            float forward = Math.Clamp(Vector3.Dot(outward, along), MinForward, 0.95f);
            // Down (+Y in model space) and out to the shield's side (+Z, the left), as the shield hangs in the stance.
            var aside = Vector3.Normalize(new Vector3(0f, 0.75f, 0.66f));
            var aim = Vector3.Normalize(forward * Vector3.UnitX + MathF.Sqrt(1f - forward * forward) * aside);

            var swing = FromTo(along, aim);
            var face = Vector3.Transform(outward, swing);
            Vector3 Across(Vector3 v) => v - Vector3.Dot(v, aim) * aim;
            var from = Across(face);
            var to = Across(Vector3.UnitX);
            if (from.LengthSquared() < 1e-8f || to.LengthSquared() < 1e-8f) return swing;
            from = Vector3.Normalize(from);
            to = Vector3.Normalize(to);
            float twist = MathF.Atan2(Vector3.Dot(Vector3.Cross(from, to), aim), Vector3.Dot(from, to));
            return Quaternion.Normalize(Quaternion.CreateFromAxisAngle(aim, twist) * swing);
        }

        private static Quaternion FromTo(Vector3 from, Vector3 to)
        {
            float dot = Math.Clamp(Vector3.Dot(from, to), -1f, 1f);
            var axis = Vector3.Cross(from, to);
            if (axis.LengthSquared() < 1e-10f)
            {
                if (dot > 0f) return Quaternion.Identity;
                axis = Vector3.Cross(from, MathF.Abs(from.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY);
            }
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(dot));
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
