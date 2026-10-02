// src/Gordian.Core/Animation/FaceMotion.cs
using System;
using System.Numerics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Animation
{
    /// <summary>
    /// The face of a humanoid actor: the talking mouth and the eye blink. Both are small skeletal clips every humanoid
    /// skeleton carries beside its body motions (checked 2026-10-01 on the eight race skeletons and the fixed NPC models
    /// 32, 163, 164; monster skeletons have neither):
    /// <list type="bullet">
    /// <item><c>mou4</c>: the mouth joint (Elvaan male 34, Hume male 54, Tarutaru 11) opens and closes over 31 frames at
    /// 15 fps (2 s). The face mesh's lip vertices blend between the head joint and it, so the mouth opens over the dark
    /// mouth patch in the face texture's corner.</item>
    /// <item><c>eye3</c>: the eyelid joints (Elvaan male 33 and 42) close over the eyeball and open again in 4-11
    /// frames (about 0.17 s).</item>
    /// </list>
    /// Retail flaps the mouth while an event has the actor talk (<c>NpcSpeechFrame</c> set by opcodes 0x1E / 0x4A / 0x79,
    /// cleared by 0x7B; <see cref="World.EventLook.SpeechFrame"/>). How often retail blinks is not measured.
    /// The clips are layered on the evaluated pose (each joint turns by the clip's local rotation on top of the body
    /// motion), as <see cref="HeadLook"/> is.
    /// </summary>
    public sealed class FaceMotion
    {
        /// <summary>The talking mouth clip.</summary>
        public const string MouthClip = "mou4";

        /// <summary>The blink clip.</summary>
        public const string BlinkClip = "eye3";

        /// <summary>The shortest wait between blinks, in seconds (not measured against retail).</summary>
        public const float MinBlinkInterval = 2f;

        /// <summary>The longest wait between blinks, in seconds (not measured against retail).</summary>
        public const float MaxBlinkInterval = 6f;

        private readonly Random _random;
        private float _mouthTime = -1f;
        private float _blinkTime = -1f;
        private float _untilBlink;

        public FaceMotion(Random? random = null)
        {
            _random = random ?? Random.Shared;
            _untilBlink = NextBlinkInterval();
        }

        /// <summary>The time into <see cref="MouthClip"/>, or -1 when the mouth is still.</summary>
        public float MouthTime => _mouthTime;

        /// <summary>The time into <see cref="BlinkClip"/>, or -1 between blinks.</summary>
        public float BlinkTime => _blinkTime;

        /// <summary>
        /// Advances the face by <paramref name="deltaSeconds"/>: the mouth runs while <paramref name="talking"/>, and a blink
        /// starts when its wait runs out unless <paramref name="canBlink"/> is false (a dead actor's death motion closes
        /// its eyes itself).
        /// </summary>
        public void Advance(float deltaSeconds, bool talking, bool canBlink, EntityModel model)
        {
            float dt = Math.Max(0f, deltaSeconds);

            _mouthTime = talking && model.Animations.ContainsKey(MouthClip) ? Math.Max(0f, _mouthTime) + dt : -1f;

            if (_blinkTime >= 0f)
            {
                _blinkTime += dt;
                if (!model.Animations.TryGetValue(BlinkClip, out var blink) || _blinkTime >= blink.DurationSeconds) _blinkTime = -1f;
            }
            else if (canBlink && model.Animations.ContainsKey(BlinkClip))
            {
                _untilBlink -= dt;
                if (_untilBlink <= 0f)
                {
                    _blinkTime = 0f;
                    _untilBlink = NextBlinkInterval();
                }
            }
        }

        /// <summary>Layers the mouth and the blink on <paramref name="pose"/> (already evaluated in model space), in place.</summary>
        public void Apply(EntityModel model, in SkeletonPoseEvaluator.EvaluatedPose pose)
        {
            if (model.Skeleton is not { } skeleton) return;
            if (_mouthTime >= 0f && model.Animations.TryGetValue(MouthClip, out var mouth)) Layer(skeleton, pose, mouth, _mouthTime, loop: true);
            if (_blinkTime >= 0f && model.Animations.TryGetValue(BlinkClip, out var blink)) Layer(skeleton, pose, blink, _blinkTime, loop: false);
        }

        private float NextBlinkInterval() => MinBlinkInterval + (float)_random.NextDouble() * (MaxBlinkInterval - MinBlinkInterval);

        /// <summary>
        /// Turns each joint <paramref name="clip"/> has a track for by the clip's local rotation at <paramref name="time"/>,
        /// on top of the pose: the joint's model-space rotation R = Rparent * L becomes Rparent * D * L, which turns the joint
        /// and everything below it by Rparent * D * Rparent⁻¹ about the joint. Parents are turned before their children.
        /// </summary>
        public static void Layer(Skeleton skeleton, in SkeletonPoseEvaluator.EvaluatedPose pose, AnimationClip clip, float time, bool loop)
        {
            int n = Math.Min(skeleton.Count, pose.Rotations.Length);
            Span<int> joints = stackalloc int[Math.Min(clip.Tracks.Count, 64)];
            int count = 0;
            foreach (int joint in clip.Tracks.Keys)
            {
                if (joint > 0 && joint < n && count < joints.Length) joints[count++] = joint;
            }
            joints = joints[..count];
            SortByDepth(skeleton, joints);

            foreach (int joint in joints)
            {
                if (!clip.TrySample(joint, time, loop, out var local, out _)) continue;
                if (MathF.Abs(local.W) > 0.99999f) continue;
                int parent = skeleton.Joints[joint].Parent;
                var parentRot = parent >= 0 && parent < n ? pose.Rotations[parent] : Quaternion.Identity;
                var turn = Quaternion.Normalize(parentRot * local * Quaternion.Conjugate(parentRot));
                Vector3 pivot = pose.Translations[joint];
                for (int j = 0; j < n; j++)
                {
                    if (!IsBelow(skeleton, j, joint)) continue;
                    pose.Translations[j] = pivot + Vector3.Transform(pose.Translations[j] - pivot, turn);
                    pose.Rotations[j] = Quaternion.Normalize(turn * pose.Rotations[j]);
                }
            }
        }

        private static void SortByDepth(Skeleton skeleton, Span<int> joints)
        {
            for (int i = 1; i < joints.Length; i++)
            {
                int key = joints[i], depth = Depth(skeleton, key), k = i - 1;
                while (k >= 0 && Depth(skeleton, joints[k]) > depth)
                {
                    joints[k + 1] = joints[k];
                    k--;
                }
                joints[k + 1] = key;
            }
        }

        private static int Depth(Skeleton skeleton, int joint)
        {
            int depth = 0;
            for (int guard = 0; joint >= 0 && joint < skeleton.Count && guard < skeleton.Count; guard++, depth++)
            {
                joint = skeleton.Joints[joint].Parent;
            }
            return depth;
        }

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
