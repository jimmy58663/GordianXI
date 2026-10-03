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
    /// Each event line the actor speaks (<see cref="World.WorldEntity.SpokenLines"/>) flaps the mouth a number of times
    /// set by the line's length (<see cref="FlapsFor"/>, <c>mou4</c> looped one flap at a time), and the player's Confirm
    /// stops it at once (<see cref="World.WorldEntity.SpeechStops"/>; retail 0x23 calls <c>SpeakStop</c>, XiEvents
    /// OpCodes/0x0023). Counted against retail (2026-10-03, #198): Joachim's lines in Port Jeuno 324 flap 3 / 3 / 6 / 6 /
    /// 8 / 5 times for 45 / 34 / 98 / 107 / ~141 / 97 characters, Deraquien's "Intruders!" once, and his 220-character
    /// line flaps 12 times and stops by itself. Looping for as long as retail's <c>NpcSpeechFrame</c> stays set was far too
    /// much (in-game test, 2026-10-02). How often retail blinks is not measured.
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

        /// <summary>The flaps in one play of <see cref="MouthClip"/>.</summary>
        public const int FlapsPerClip = 3;

        /// <summary>
        /// The characters of a line per mouth flap: the least-squares fit, through the origin, of the flaps counted by eye
        /// in retail (2026-10-03, #198) on Joachim's six lines in Port Jeuno 324 and Deraquien's two (12 for his 220
        /// characters). With it every count but one (34 characters: 2, counted 3) comes out as seen.
        /// </summary>
        public const float CharactersPerFlap = 17.8f;

        /// <summary>How many times the mouth flaps for a line of <paramref name="characters"/> shown characters (at least one).</summary>
        public static int FlapsFor(int characters) => Math.Max(1, (int)MathF.Round(characters / CharactersPerFlap));

        private readonly Random _random;
        private float _mouthTime = -1f;
        private int _spokenLines = -1;
        private int _speechStops = -1;
        private float _mouthEnd;
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
        /// Advances the face by <paramref name="deltaSeconds"/>: the mouth plays its clip once from the start whenever
        /// <paramref name="spokenLines"/> (the entity's count of spoken lines) changes, and a blink starts when its wait runs
        /// out unless <paramref name="canBlink"/> is false (a dead actor's death motion closes its eyes itself). A new line
        /// flaps the mouth <paramref name="lineFlaps"/> times; a change of <paramref name="speechStops"/> (the player closed
        /// the line) stops it. The first call only takes the counts.
        /// </summary>
        public void Advance(float deltaSeconds, int spokenLines, bool canBlink, EntityModel model, int lineFlaps = FlapsPerClip, int speechStops = 0)
        {
            float dt = Math.Max(0f, deltaSeconds);
            bool hasMouth = model.Animations.TryGetValue(MouthClip, out var mouth);

            if (_mouthTime >= 0f)
            {
                _mouthTime += dt;
                if (!hasMouth || _mouthTime >= _mouthEnd) _mouthTime = -1f;
            }
            if (spokenLines != _spokenLines)
            {
                if (_spokenLines >= 0 && hasMouth)
                {
                    _mouthTime = 0f;
                    _mouthEnd = Math.Max(0, lineFlaps) * mouth!.DurationSeconds / FlapsPerClip;
                }
                _spokenLines = spokenLines;
            }
            if (speechStops != _speechStops)
            {
                if (_speechStops >= 0) _mouthTime = -1f;
                _speechStops = speechStops;
            }

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
