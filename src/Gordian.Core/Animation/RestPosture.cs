// src/Gordian.Core/Animation/RestPosture.cs
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Animation
{
    /// <summary>
    /// The seated postures an entity's server status puts it in (#343): resting with <c>/heal</c> and sitting with
    /// <c>/sit</c>. Each is three motion routines of the actor's own model: one that sits down and then holds the seated
    /// loop, one that is the loop alone, and one that stands up.
    /// <list type="bullet">
    /// <item>Healing (status 33): <c>res0</c> plays <c>rx0</c> (52 ticks, 0.87 s), then loops <c>rx1</c> (loop count 0:
    /// until replaced); <c>res1</c> is <c>rx1</c> alone; <c>res2</c> plays <c>rx2</c> (56 ticks, 0.93 s) and blends out over
    /// 20 ticks.</item>
    /// <item>Sitting (status 47): <c>sit0</c> plays <c>si0</c> (120 ticks), then loops <c>si1</c>; <c>sit1</c> is <c>si1</c>
    /// alone; <c>sit2</c> plays <c>si2</c> (120 ticks).</item>
    /// </list>
    /// Read from the retail race skeletons' Section 0x07 routines (2026-10-12, <c>RestPostureRealDataTests</c>): the same
    /// routines and clip names on all eight races, and on the fixed models that carry them (e.g. 24, 530, 1443-1445,
    /// 1596-1598). The seated clips lower the head joint by 0.2-0.7 yalms against the idle (Hume male 1.62 to 0.93 in
    /// <c>rx1</c>). Routine format referenced from xi-tools (docs/anim/schedule.md: the sit routine that "plays the sit-down
    /// clip once, then the sitting idle"). The status values are LandSandBoat's <c>xi.animation.HEALING</c> (33) and
    /// <c>SIT</c> (47) (https://github.com/LandSandBoat/server, scripts/enum/animation.codegen.lua), sent in the entity
    /// status of S2C 0x00D / 0x00E and, for the player, S2C 0x037. <b>Beyond xi-tools:</b> which routines the statuses play.
    /// <para>
    /// Synthesis (status 44, LandSandBoat <c>xi.animation.SYNTH</c>, also in S2C 0x030's <c>Status</c>) takes the Healing
    /// posture (#315): in the maintainer's retail video (<c>Synth_Wind_Fire_Lightning_Breaks.mp4</c>, a Tarutaru, 2026-10-16)
    /// the crafter sits in one short motion, holds a seated pose with the hands together at the chest and, after the result,
    /// rocks back onto its back and stands up; Tarutaru's <c>rx2</c> does exactly that (the head joint drops from 0.43 to
    /// 0.12 yalms and 0.59 behind the root at 0.4-0.5 s, then rises to the standing 0.62), and <c>rx0</c> lowers the head
    /// from 0.63 to 0.44 between 0.3 and 0.5 s. The raised right hand holding the crystal is the race's <c>ri0?</c>, which the synthesis
    /// effect file's <c>frst</c> plays (<c>SynthesisAnimationController</c>).
    /// </para>
    /// </summary>
    public static class RestPosture
    {
        /// <summary>LandSandBoat <c>xi.animation.HEALING</c>: resting with /heal.</summary>
        public const byte StatusHealing = 33;

        /// <summary>LandSandBoat <c>xi.animation.SIT</c>: sitting with /sit.</summary>
        public const byte StatusSitting = 47;

        /// <summary>LandSandBoat <c>xi.animation.SYNTH</c>: synthesizing (#315); drawn with the Healing posture.</summary>
        public const byte StatusSynthesis = 44;

        /// <summary>The seated category a server status puts the entity in, or null for any other status.</summary>
        public static AnimationCategory? CategoryFor(byte status) => status switch
        {
            StatusHealing or StatusSynthesis => AnimationCategory.Healing,
            StatusSitting => AnimationCategory.Sitting,
            _ => null,
        };

        /// <summary>Whether the category is one of the seated postures.</summary>
        public static bool IsSeated(AnimationCategory category) => category is AnimationCategory.Healing or AnimationCategory.Sitting;

        /// <summary>The routine that sits down (then holds the loop) for a seated category, or null.</summary>
        public static string? EnterRoutine(AnimationCategory category) => category switch
        {
            AnimationCategory.Healing => "res0",
            AnimationCategory.Sitting => "sit0",
            _ => null,
        };

        /// <summary>The routine that is the seated loop alone, or null.</summary>
        public static string? LoopRoutine(AnimationCategory category) => category switch
        {
            AnimationCategory.Healing => "res1",
            AnimationCategory.Sitting => "sit1",
            _ => null,
        };

        /// <summary>The routine that stands up, or null.</summary>
        public static string? ExitRoutine(AnimationCategory category) => category switch
        {
            AnimationCategory.Healing => "res2",
            AnimationCategory.Sitting => "sit2",
            _ => null,
        };

        /// <summary>The sit-down clip: the first step of the enter routine, or null when the model lacks it.</summary>
        public static AnimationClip? EnterClip(EntityModel model, AnimationCategory category) => StepClip(model, EnterRoutine(category), last: false);

        /// <summary>
        /// The seated loop: the loop routine's clip, else the enter routine's last step (when it has more than one), or null
        /// when the model has neither.
        /// </summary>
        public static AnimationClip? LoopClip(EntityModel model, AnimationCategory category)
        {
            var clip = StepClip(model, LoopRoutine(category), last: true);
            if (clip != null) return clip;
            string? enter = EnterRoutine(category);
            return enter != null && model.MotionRoutines.TryGetValue(enter, out var routine) && routine.Segments.Count > 1
                ? StepClip(model, enter, last: true)
                : null;
        }

        /// <summary>The stand-up clip: the first step of the exit routine, or null when the model lacks it.</summary>
        public static AnimationClip? ExitClip(EntityModel model, AnimationCategory category) => StepClip(model, ExitRoutine(category), last: false);

        /// <summary>
        /// The one-shot clip to play when the category changes from <paramref name="from"/> to <paramref name="to"/>: sitting
        /// down into a seated posture, or standing up out of one; null when neither applies or the model lacks the clip.
        /// </summary>
        public static AnimationClip? ResolveTransition(EntityModel model, AnimationCategory from, AnimationCategory to)
        {
            if (from == to) return null;
            if (IsSeated(to)) return EnterClip(model, to);
            if (IsSeated(from)) return ExitClip(model, from);
            return null;
        }

        private static AnimationClip? StepClip(EntityModel model, string? routineName, bool last)
        {
            if (routineName == null || !model.MotionRoutines.TryGetValue(routineName, out var routine) || routine.Segments.Count == 0) return null;
            var step = last ? routine.Segments[^1] : routine.Segments[0];
            return model.Animations.TryGetValue(step.ClipName, out var clip) ? clip : null;
        }
    }
}
