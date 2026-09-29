// src/Gordian.Core/Resources/Models/MotionRoutine.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Resources.Graphics;

namespace Gordian.Core.Resources.Models
{
    /// <summary>
    /// One skeleton-animation step of a motion routine (Section 0x07 op 0x05): the clip it plays, when it starts, and how it
    /// blends in and out. Times are 60 Hz routine ticks.
    /// </summary>
    /// <param name="ClipName">Model clip key: the op's reference with its body-region wildcard <c>?</c> removed (<c>at0?</c> is <c>at0</c>).</param>
    /// <param name="StartTick">Tick the clip starts at (the sum of the delays before the op).</param>
    /// <param name="DurationTicks">The op's duration field: how long the step owns the body.</param>
    /// <param name="BlendInTicks">Cross-fade from the previous pose into this clip.</param>
    /// <param name="BlendOutTicks">Cross-fade from this clip back to the stance when the routine ends.</param>
    /// <param name="Loops">How many times the clip repeats (1 = once; the looping cast chants use 17-63).</param>
    /// <param name="Speed">Playback rate (1 = authored speed).</param>
    public readonly record struct MotionSegment(
        string ClipName,
        int StartTick,
        int DurationTicks,
        int BlendInTicks,
        int BlendOutTicks,
        int Loops,
        float Speed);

    /// <summary>
    /// A motion routine flattened for playback: the clip steps in order, the ticks at which the action's result lands
    /// (the link to the shared hit routines <c>dada</c> / <c>mdam</c>), and the procedural reactions it carries.
    /// Built from the actor's own Section 0x07 routines (e.g. the swing <c>ati0</c>, the chant <c>cabk</c>, the guard
    /// <c>gurd</c>), following links into the actor's other routines.
    /// Format referenced from xi-tools (docs/fx/effect_system.md, docs/ability/mixer.md) and xi-model-viewer
    /// (https://github.com/vekien/xi-model-viewer, ui/js/dat.js parseRoutine).
    /// </summary>
    public sealed class MotionRoutine
    {
        public string Name { get; init; } = string.Empty;

        /// <summary>Routine length in 60 Hz ticks (the header total, or the sum of the command delays).</summary>
        public int TotalTicks { get; init; }

        public IReadOnlyList<MotionSegment> Segments { get; init; } = Array.Empty<MotionSegment>();

        /// <summary>Ticks at which the routine shows its result on the target (links to <c>dada</c> or <c>mdam</c>).</summary>
        public IReadOnlyList<int> HitTicks { get; init; } = Array.Empty<int>();

        /// <summary>
        /// Duration in ticks of the procedural flinch the routine plays on its actor (op 0x21, e.g. in <c>damg</c>), or 0.
        /// </summary>
        public int FlinchTicks { get; init; }

        /// <summary>
        /// The pose index of a procedural pose flash (op 0x5A, e.g. the PC <c>gurd</c> = 0 guard, <c>pary</c> = 1 parry), or -1.
        /// </summary>
        public int PoseFlashIndex { get; init; } = -1;

        /// <summary>Duration in ticks of the pose flash, or 0.</summary>
        public int PoseFlashTicks { get; init; }

        /// <summary>The tick the first clip starts at (the routine's hit is timed from the routine start, not from this).</summary>
        public int FirstClipTick => Segments.Count > 0 ? Segments[0].StartTick : 0;

        /// <summary>
        /// Whether the routine's last clip loops for the length of an action rather than playing once (the cast chants),
        /// so the routine lasts until something replaces it.
        /// </summary>
        public bool IsSustained => Segments.Count > 0 && Segments[^1].Loops >= MotionRoutineDecoder.SustainedLoopCount;

        public override string ToString() => $"MotionRoutine [{Name}] {Segments.Count} clip(s), {TotalTicks} ticks";
    }

    /// <summary>
    /// One command of a Section 0x07 routine as the decoder reads it, before links are followed.
    /// </summary>
    public readonly record struct MotionRoutineCommand(
        byte Op,
        int StartTick,
        int Duration,
        string Reference,
        int BlendInTicks,
        int BlendOutTicks,
        int Loops,
        float Speed,
        int PoseIndex,
        int ReactionTicks);

    /// <summary>A Section 0x07 routine's commands, before links are followed.</summary>
    public sealed class RawMotionRoutine
    {
        public string Name { get; init; } = string.Empty;
        public int TotalTicks { get; init; }
        public IReadOnlyList<MotionRoutineCommand> Commands { get; init; } = Array.Empty<MotionRoutineCommand>();
    }
}
