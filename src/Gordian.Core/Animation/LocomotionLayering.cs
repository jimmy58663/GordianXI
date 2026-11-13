// src/Gordian.Core/Animation/LocomotionLayering.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Animation
{
    /// <summary>
    /// Lays an action clip over the walk or run (#511 round 2). The PC moving swings (<c>atf0</c> / <c>atb0</c> / <c>atl0</c> /
    /// <c>atr0</c>, clips <c>amf</c> / <c>amb</c> / <c>aml</c> / <c>amr</c>) key every joint but animate only the upper body: on the
    /// Hume male battle pack <c>ROM/32/13</c>, <c>amf</c> moves joints 3-17, 24 and 50-88 and holds the legs (25-47, which
    /// <c>crun</c> animates) still. Played alone it freezes the legs mid-run, so retail must take the legs from the locomotion
    /// clip. *Inference* from the clip data (2026-11-12): no reference describes the layering.
    /// </summary>
    public static class LocomotionLayering
    {
        /// <summary>A joint whose keys turn less than this (1 - |dot|, about 1.6 degrees) is held still by the clip.</summary>
        private const float StillRotation = 0.0001f;

        /// <summary>A joint whose keys move less than this (yalms) is held still by the clip.</summary>
        private const float StillTranslation = 0.001f;

        /// <summary>
        /// Whether <paramref name="clip"/> holds <paramref name="joint"/> still: no track, a reset entry, or keys that neither turn
        /// nor move it.
        /// </summary>
        public static bool HoldsStill(AnimationClip clip, int joint)
        {
            ArgumentNullException.ThrowIfNull(clip);
            if (!clip.Tracks.TryGetValue(joint, out var track) || track.IsReset) return true;
            var rotations = track.Rotations;
            for (int i = 1; i < rotations.Length; i++)
            {
                if (1f - MathF.Abs(Quaternion.Dot(rotations[i], rotations[0])) > StillRotation) return false;
            }
            var translations = track.Translations;
            for (int i = 1; i < translations.Length; i++)
            {
                if (Vector3.DistanceSquared(translations[i], translations[0]) > StillTranslation * StillTranslation) return false;
            }
            return true;
        }

        /// <summary>
        /// <paramref name="action"/> with each joint it holds still and <paramref name="locomotion"/> animates taken from
        /// <paramref name="locomotion"/>, looping at the locomotion clip's own length. <paramref name="clipRate"/> is how many
        /// seconds of the action clip play per real second (the routine clock's speed times the step's), so the legs keep
        /// the locomotion clip's real pace. Returns <paramref name="action"/> itself when nothing is taken.
        /// </summary>
        public static AnimationClip Over(AnimationClip action, AnimationClip? locomotion, float clipRate)
        {
            ArgumentNullException.ThrowIfNull(action);
            if (locomotion == null || ReferenceEquals(action, locomotion) || locomotion.DurationSeconds <= 0.0001f || clipRate <= 0f) return action;

            Dictionary<int, BoneAnimationTrack>? tracks = null;
            foreach (var (joint, track) in locomotion.Tracks)
            {
                if (track.IsReset || HoldsStill(locomotion, joint) || !HoldsStill(action, joint)) continue;
                tracks ??= new Dictionary<int, BoneAnimationTrack>(action.Tracks);
                tracks[joint] = new BoneAnimationTrack
                {
                    JointIndex = joint,
                    Rotations = track.Rotations,
                    Translations = track.Translations,
                    Scales = track.Scales,
                    PartDurationSeconds = (track.PartDurationSeconds > 0f ? track.PartDurationSeconds : locomotion.DurationSeconds) * clipRate,
                    LoopsAlways = true,
                };
            }
            if (tracks == null) return action;
            return new AnimationClip { Name = action.Name, NumFrames = action.NumFrames, KeyFrameDuration = action.KeyFrameDuration, Tracks = tracks };
        }
    }
}
