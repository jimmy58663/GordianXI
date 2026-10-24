// src/Gordian.Core/Animation/FirstPersonEye.cs
using System.Runtime.CompilerServices;
using Gordian.Core.Animation;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;

namespace Gordian.Engine.Animation
{
    /// <summary>
    /// The height of a character's eyes above its feet, where the first-person camera sits (#305). It is read from the
    /// model's own skeleton in the first frame of its idle: the eyelid joints the blink clip (<see cref="FaceMotion.BlinkClip"/>)
    /// moves, averaged, else the head joint (<see cref="HeadLook.HeadJoint"/>). Measured on the retail race skeletons
    /// (2026-10-12, <c>FirstPersonEyeRealDataTests</c>), in yalms before the size scale: Hume male 1.66, Hume female 1.54,
    /// Elvaan male 1.94, Elvaan female 1.80, Tarutaru 0.71, Mithra 1.53, Galka 2.14. The fixed 1.3 used before suited no
    /// race: it sat at an Elvaan's chest and above a Tarutaru's head.
    /// </summary>
    public static class FirstPersonEye
    {
        private static readonly ConditionalWeakTable<EntityModel, StrongBox<float>> Cache = new();

        /// <summary>The eye height (yalms above the feet, before the size scale), or null for a model without a skeleton.</summary>
        public static float? HeightOf(EntityModel model)
        {
            if (Cache.TryGetValue(model, out var cached)) return cached.Value;
            if (model.Skeleton is not { Count: > 0 } skeleton) return null;

            var idle = NpcStanceResolver.ResolveTargetClip(model, AnimationCategory.Idle);
            var pose = idle != null
                ? SkeletonPoseEvaluator.EvaluatePose(skeleton, idle, 0f, loop: true)
                : SkeletonPoseEvaluator.ComputeBindPose(skeleton);

            float? height = null;
            if (model.Animations.TryGetValue(FaceMotion.BlinkClip, out var blink) && blink.Tracks.Count > 0)
            {
                float sum = 0f;
                int count = 0;
                foreach (int joint in blink.Tracks.Keys)
                {
                    if (joint < 0 || joint >= pose.Translations.Length) continue;
                    sum += -pose.Translations[joint].Y; // model space is Y-down
                    count++;
                }
                if (count > 0) height = sum / count;
            }
            if (height == null)
            {
                int head = HeadLook.HeadJoint(skeleton);
                if (head >= 0 && head < pose.Translations.Length) height = -pose.Translations[head].Y;
            }
            if (height is not > 0f) return null;

            Cache.AddOrUpdate(model, new StrongBox<float>(height.Value));
            return height;
        }
    }
}
