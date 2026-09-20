// src/Gordian.Core/Resources/Models/AnimationClip.cs
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gordian.Core.Resources.Models
{
    /// <summary>
    /// Per-joint rotation and translation keyframe arrays for one bone within an animation clip.
    /// Joints with no track present fall back to the skeleton's static bind-pose local transform.
    /// Format referenced from xi-tools (https://github.com/vekien/xi-tools) docs/anim/format.md.
    /// </summary>
    public sealed class BoneAnimationTrack
    {
        public int JointIndex { get; init; }
        public Quaternion[] Rotations { get; init; } = Array.Empty<Quaternion>();
        public Vector3[] Translations { get; init; } = Array.Empty<Vector3>();
    }

    /// <summary>
    /// Decoded FFXI Section 0x2B skeletal animation clip: a shared frame count/duration
    /// and a sparse set of per-joint keyframe tracks.
    /// Format referenced from xi-tools (https://github.com/vekien/xi-tools) docs/anim/format.md.
    /// </summary>
    public sealed class AnimationClip
    {
        public string Name { get; init; } = string.Empty;
        public int NumFrames { get; init; }
        public float KeyFrameDuration { get; init; }
        public IReadOnlyDictionary<int, BoneAnimationTrack> Tracks { get; init; } = new Dictionary<int, BoneAnimationTrack>();

        /// <summary>
        /// Total clip playback duration in seconds, derived from the documented
        /// "keyFrameDuration * 30 = animation-frames-per-second" relationship.
        /// </summary>
        public float DurationSeconds
        {
            get
            {
                float framesPerSecond = KeyFrameDuration * 30.0f;
                if (framesPerSecond <= 0.0001f || NumFrames <= 0) return 0f;
                return NumFrames / framesPerSecond;
            }
        }

        /// <summary>
        /// Samples a joint's local rotation/translation at the given playback time using NLERP
        /// (shortest-path normalized-lerp, matching documented retail interpolation - not true SLERP).
        /// Returns false if this clip has no track for the requested joint.
        /// </summary>
        public bool TrySample(int jointIndex, float timeSeconds, bool loop, out Quaternion rotation, out Vector3 translation)
        {
            rotation = Quaternion.Identity;
            translation = Vector3.Zero;

            if (!Tracks.TryGetValue(jointIndex, out var track) || NumFrames <= 0)
            {
                return false;
            }

            if (NumFrames == 1 || track.Rotations.Length == 0)
            {
                rotation = track.Rotations.Length > 0 ? track.Rotations[0] : Quaternion.Identity;
                translation = track.Translations.Length > 0 ? track.Translations[0] : Vector3.Zero;
                return true;
            }

            float duration = DurationSeconds;
            float frameF = duration > 0.0001f ? (timeSeconds / duration) * NumFrames : 0f;

            int f0, f1;
            float frac;

            if (loop)
            {
                frameF %= NumFrames;
                if (frameF < 0) frameF += NumFrames;
                f0 = (int)MathF.Floor(frameF) % NumFrames;
                f1 = (f0 + 1) % NumFrames;
                frac = frameF - MathF.Floor(frameF);
            }
            else
            {
                frameF = Math.Clamp(frameF, 0f, NumFrames - 1);
                f0 = (int)MathF.Floor(frameF);
                f1 = Math.Min(f0 + 1, NumFrames - 1);
                frac = frameF - f0;
            }

            rotation = Nlerp(track.Rotations[f0], track.Rotations[f1], frac);
            translation = Vector3.Lerp(track.Translations[f0], track.Translations[f1], frac);
            return true;
        }

        /// <summary>
        /// Normalized-lerp with shortest-path negation, matching the documented FFXI client
        /// interpolation behavior (xi-tools docs/anim/format.md): NOT true SLERP.
        /// </summary>
        private static Quaternion Nlerp(Quaternion q1, Quaternion q2, float t)
        {
            if (Quaternion.Dot(q1, q2) < 0f)
            {
                q2 = new Quaternion(-q2.X, -q2.Y, -q2.Z, -q2.W);
            }

            var lerped = new Quaternion(
                q1.X + (q2.X - q1.X) * t,
                q1.Y + (q2.Y - q1.Y) * t,
                q1.Z + (q2.Z - q1.Z) * t,
                q1.W + (q2.W - q1.W) * t);

            return lerped.LengthSquared() > 0.0001f ? Quaternion.Normalize(lerped) : Quaternion.Identity;
        }
    }
}
