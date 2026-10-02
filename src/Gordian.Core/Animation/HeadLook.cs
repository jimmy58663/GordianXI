// src/Gordian.Core/Animation/HeadLook.cs
using System;
using System.Numerics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Animation
{
    /// <summary>
    /// The head look of an event (<see cref="World.EventLook"/>, opcodes 0x1E / 0x4A / 0x79): the entity's head turns
    /// toward the entity it looks at while its body keeps its heading, as the maintainer's retail recording of the Southern
    /// San d'Oria intro shows (Ceraule's head turns to the player as they walk up, 2026-10-01). The head is the joint of
    /// skeleton reference 5: on every race skeleton and the fixed NPC models checked (Curilla 69, Trion 64) it is the joint
    /// the face mesh is bound to (Hume male 52, Hume female 30, Elvaan male 30, Elvaan female 57, Tarutaru 7, Mithra and
    /// Galka 40), with reference 3 the neck below it. The head also tilts toward the target's head: the maintainer's retail
    /// recording of the Windurst Waters intro as a Galka shows Ajido-Marujido and Apururu looking up at the player
    /// (2026-10-02). How far and how fast retail turns and tilts the head is not measured.
    /// </summary>
    public static class HeadLook
    {
        /// <summary>The skeleton reference whose joint is the head.</summary>
        public const int HeadReference = 5;

        /// <summary>The largest turn of the head from the body's heading (60 degrees; retail's limit is not measured).</summary>
        public const float MaxYaw = MathF.PI / 3f;

        /// <summary>
        /// The largest tilt of the head up or down toward the target's head (45 degrees; not measured). The maintainer's
        /// retail recording of the Windurst Waters intro as a Galka (2026-10-02, 2:04) shows Ajido-Marujido tilting his head
        /// well back to look up at the player's face from about a yalm and a half away.
        /// </summary>
        public const float MaxPitch = MathF.PI / 4f;

        /// <summary>How fast the head eases toward its angle (per second, exponential; the viewport's turn ease uses 8).</summary>
        public const float EaseRate = 8f;

        /// <summary>The head joint of a skeleton, or -1 when it has no head reference.</summary>
        public static int HeadJoint(Skeleton skeleton) =>
            skeleton.References.Count > HeadReference && skeleton.References[HeadReference].Index < skeleton.Count
                ? skeleton.References[HeadReference].Index
                : -1;

        /// <summary>
        /// The head's turn (radians, in the heading convention of <see cref="World.WorldEntity.HeadingOf"/>) for an entity
        /// at <paramref name="from"/> with body heading <paramref name="heading"/> looking at <paramref name="to"/>,
        /// clamped to <see cref="MaxYaw"/>. Positions use the internal axes (X, Y = height, Z).
        /// </summary>
        public static float TargetYaw(float heading, Vector3 from, Vector3 to)
        {
            float dx = to.X - from.X, dz = to.Z - from.Z;
            if (dx * dx + dz * dz < 1e-6f) return 0f;
            float delta = World.WorldEntity.HeadingOf(dx, dz) - heading;
            delta -= MathF.Tau * MathF.Round(delta / MathF.Tau);
            return Math.Clamp(delta, -MaxYaw, MaxYaw);
        }

        /// <summary>
        /// The head's tilt (radians, positive = up) for a head at height <paramref name="fromHeight"/> looking at a head at
        /// <paramref name="toHeight"/> <paramref name="horizontalDistance"/> away, clamped to <see cref="MaxPitch"/>.
        /// </summary>
        public static float TargetPitch(float fromHeight, float toHeight, float horizontalDistance)
        {
            float rise = toHeight - fromHeight;
            if (MathF.Abs(rise) < 1e-4f) return 0f;
            return Math.Clamp(MathF.Atan2(rise, Math.Max(horizontalDistance, 0.05f)), -MaxPitch, MaxPitch);
        }

        /// <summary>Eases the current turn toward <paramref name="target"/> over <paramref name="deltaSeconds"/>.</summary>
        public static float Ease(float current, float target, float deltaSeconds) =>
            current + (target - current) * (1f - MathF.Exp(-EaseRate * Math.Max(0f, deltaSeconds)));

        /// <summary>
        /// The model-space rotation of a turn by <paramref name="yaw"/>. Models are authored Y-down and drawn through a
        /// half turn about X (the renderer's <c>EntityRotMatrix</c>, diag(1, -1, -1)) and then the heading rotation
        /// RotY(-heading - π), so turning the body by yaw is RotY(-yaw) in display space, which is RotY(+yaw) in model space.
        /// </summary>
        public static Quaternion ModelRotation(float yaw) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);

        /// <summary>
        /// The model-space rotation of a turn by <paramref name="yaw"/> and a tilt by <paramref name="pitch"/> (positive =
        /// up). The model faces +X with Y down, so tilting up turns +X toward -Y: RotZ(-pitch), taken in the turned frame.
        /// </summary>
        public static Quaternion ModelRotation(float yaw, float pitch) =>
            ModelRotation(yaw) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -pitch);

        /// <summary>
        /// Turns the head joint and every joint below it (face, hair, helmet) by <paramref name="yaw"/> and tilts them by
        /// <paramref name="pitch"/> about the head joint, in place, on a pose already evaluated in model space.
        /// </summary>
        public static void Apply(Skeleton skeleton, in SkeletonPoseEvaluator.EvaluatedPose pose, int head, float yaw, float pitch = 0f)
        {
            int n = Math.Min(skeleton.Count, pose.Rotations.Length);
            if (head < 0 || head >= n || (MathF.Abs(yaw) < 1e-4f && MathF.Abs(pitch) < 1e-4f)) return;
            var turn = ModelRotation(yaw, pitch);
            Vector3 pivot = pose.Translations[head];
            for (int j = 0; j < n; j++)
            {
                if (!IsBelow(skeleton, j, head)) continue;
                pose.Translations[j] = pivot + Vector3.Transform(pose.Translations[j] - pivot, turn);
                pose.Rotations[j] = Quaternion.Normalize(turn * pose.Rotations[j]);
            }
        }

        /// <summary>Whether <paramref name="joint"/> is <paramref name="ancestor"/> or below it.</summary>
        private static bool IsBelow(Skeleton skeleton, int joint, int ancestor)
        {
            for (int guard = 0; joint >= 0 && joint < skeleton.Count && guard < skeleton.Count; guard++)
            {
                if (joint == ancestor) return true;
                joint = skeleton.Joints[joint].Parent;
            }
            return false;
        }
    }
}
