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
        public Vector3[] Scales { get; init; } = Array.Empty<Vector3>();
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
        /// Total clip duration in seconds.
        /// Matches xi-model-viewer (Math.max(numFrames - 1, 1) / keyFrameDuration / 30).
        /// </summary>
        public float DurationSeconds
        {
            get
            {
                float fps = KeyFrameDuration * 30.0f;
                if (fps <= 0.0001f || NumFrames <= 0) return 0f;
                int intervals = Math.Max(NumFrames - 1, 1);
                return intervals / fps;
            }
        }

        /// <summary>
        /// Samples a joint's local rotation/translation at the given playback time using NLERP
        /// (shortest-path normalized-lerp, matching documented retail interpolation - not true SLERP).
        /// Returns false if this clip has no track for the requested joint.
        /// </summary>
        public bool TrySample(int jointIndex, float timeSeconds, bool loop, out Quaternion rotation, out Vector3 translation)
        {
            return TrySample(jointIndex, timeSeconds, loop, out rotation, out translation, out _);
        }

        /// <summary>
        /// Samples a joint's local rotation, translation, and scale at the given playback time using NLERP.
        /// Returns false if this clip has no track for the requested joint.
        /// </summary>
        public bool TrySample(int jointIndex, float timeSeconds, bool loop, out Quaternion rotation, out Vector3 translation, out Vector3 scale)
        {
            rotation = Quaternion.Identity;
            translation = Vector3.Zero;
            scale = Vector3.One;

            if (!Tracks.TryGetValue(jointIndex, out var track) || NumFrames <= 0)
            {
                return false;
            }

            int trackFrames = track.Rotations.Length;
            if (trackFrames == 0)
            {
                return true;
            }

            int last = trackFrames - 1;
            if (last <= 0)
            {
                rotation = track.Rotations[0];
                translation = track.Translations.Length > 0 ? track.Translations[0] : Vector3.Zero;
                scale = track.Scales.Length > 0 ? track.Scales[0] : Vector3.One;
                return true;
            }

            float duration = DurationSeconds;
            float phase = duration > 0.0001f ? timeSeconds / duration : 0f;

            if (loop)
            {
                phase %= 1.0f;
                if (phase < 0f) phase += 1.0f;
            }
            else
            {
                phase = Math.Clamp(phase, 0f, 1f);
            }

            float pos = phase * last;
            int lower = Math.Min((int)MathF.Floor(pos), last - 1);
            int upper = lower + 1;
            float t = pos - lower;

            rotation = Nlerp(track.Rotations[lower], track.Rotations[upper], t);
            Vector3 t0 = track.Translations.Length > lower ? track.Translations[lower] : Vector3.Zero;
            Vector3 t1 = track.Translations.Length > upper ? track.Translations[upper] : Vector3.Zero;
            translation = Vector3.Lerp(t0, t1, t);

            Vector3 s0 = track.Scales.Length > lower ? track.Scales[lower] : Vector3.One;
            Vector3 s1 = track.Scales.Length > upper ? track.Scales[upper] : Vector3.One;
            scale = Vector3.Lerp(s0, s1, t);
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
