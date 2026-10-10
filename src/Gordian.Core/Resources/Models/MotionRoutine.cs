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
    /// <param name="Loops">
    /// How many times the clip repeats, then holds its last frame (1 = once; the looping cast chants use 17-63), or 0 to
    /// loop until the next motion replaces it (the held kneel of <c>sha0</c>, the corpse <c>corp</c>, #193).
    /// </param>
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
    /// A routine showing or hiding one of the actor's weapon slots (op 0x75 <c>HideWepControl</c>): slot 0 the main weapon
    /// (<c>wep0</c> meshes), 1 the sub (<c>wep1</c>), 2 the ranged weapon (<c>wep2</c>).
    /// </summary>
    /// <param name="Tick">Routine tick the change applies at.</param>
    /// <param name="Slot">Weapon slot (the <c>N</c> of the <c>wepN</c> mesh sections).</param>
    /// <param name="Hide">True hides the slot's meshes, false shows them.</param>
    public readonly record struct WeaponVisibilityChange(int Tick, int Slot, bool Hide);

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

        /// <summary>
        /// The weapon slots the routine shows or hides, in tick order: a cast or item use hides the main and sub weapons
        /// (<c>hwmg</c>), a ranged attack shows the ranged weapon and hides the others (<c>hwso</c>). They last until the
        /// routine ends, when the actor's default (<see cref="EntityModel.DefaultHiddenWeaponSlots"/>) comes back.
        /// </summary>
        public IReadOnlyList<WeaponVisibilityChange> WeaponChanges { get; init; } = Array.Empty<WeaponVisibilityChange>();

        /// <summary>The tick the first clip starts at (the routine's hit is timed from the routine start, not from this).</summary>
        public int FirstClipTick => Segments.Count > 0 ? Segments[0].StartTick : 0;

        /// <summary>
        /// Whether the routine's last clip loops for the length of an action rather than playing once (the cast chants),
        /// so the routine lasts until something replaces it.
        /// </summary>
        public bool IsSustained => Segments.Count > 0 && Segments[^1].Loops >= MotionRoutineDecoder.SustainedLoopCount;

        /// <summary>
        /// Whether the last clip loops until the next motion replaces it (op 0x05 loop count 0): the event gestures that
        /// end in a pose (<c>sha0</c> kneels, <c>tlk0</c> keeps talking, <c>corp</c> lies down) hold it until the script
        /// plays the next one.
        /// </summary>
        public bool HoldsLastClip => Segments.Count > 0 && Segments[^1].Loops == 0;

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
        int ReactionTicks,
        int WeaponSlot = -1,
        bool HideWeapon = false);

    /// <summary>
    /// The weapon show / hide routines of the shared <c>ROM/0/0.DAT</c> that the actors' own routines link (op 0x03), each a
    /// list of op 0x75 commands (slot, hide). Read from the retail <c>ROM/0/0.DAT</c> (2026-10-07,
    /// <c>SharedWeaponRoutines_MatchTheRetailDat</c>): the PC <c>init</c> links <c>hwpc</c> (the ranged weapon starts hidden),
    /// the casts and item uses link <c>hwmg</c>, the ranged routines <c>calg</c> / <c>shlg</c> / <c>ls06</c>... link
    /// <c>hwso</c>. <c>hwat</c>, <c>stlg</c> and <c>splg</c> put the melee weapons back: every battle pack's basic attack
    /// <c>atk0</c> and counter <c>cnt0</c> link <c>hwat</c>. Op meaning from xi-tools
    /// docs/reference/ps2_decomp_crosscheck.md (HideWepControl).
    /// </summary>
    public static class SharedWeaponRoutines
    {
        private static readonly (int Slot, bool Hide)[] Melee = [(0, false), (1, false), (2, true)];

        /// <summary>Routine name to its (slot, hide) commands, in file order.</summary>
        public static IReadOnlyDictionary<string, (int Slot, bool Hide)[]> ByName { get; } =
            new Dictionary<string, (int Slot, bool Hide)[]>(StringComparer.Ordinal)
            {
                ["hwpc"] = [(2, true)],
                ["hwmg"] = [(0, true), (1, true), (2, true)],
                ["hwso"] = [(2, false), (1, true), (0, true)],
                ["hwat"] = Melee,
                ["stlg"] = Melee,
                ["splg"] = Melee,
            };
    }

    /// <summary>A Section 0x07 routine's commands, before links are followed.</summary>
    public sealed class RawMotionRoutine
    {
        public string Name { get; init; } = string.Empty;
        public int TotalTicks { get; init; }
        public IReadOnlyList<MotionRoutineCommand> Commands { get; init; } = Array.Empty<MotionRoutineCommand>();

        /// <summary>
        /// The commands the routine's sounds depend on (#41), kept apart from <see cref="Commands"/> so playback is
        /// unchanged: sound commands, links, random-choice members. See <see cref="RoutineSoundCommand"/>.
        /// </summary>
        public IReadOnlyList<RoutineSoundCommand> SoundCommands { get; init; } = Array.Empty<RoutineSoundCommand>();
    }

    /// <summary>
    /// One sound-related command of a Section 0x07 routine (#41): a sound command (op 0x0A at the source, 0x0B at the
    /// target, 0x4A / 0x53 / 0x60 variants) naming a Section 0x3D sound pointer; a link to another routine (0x03 / 0x3B
    /// / 0x57 / 0x3C on the actor, 0x09 on the target); or the empty member 0x50 of a random choice. Commands between
    /// the random-choice markers 0x3D and 0x3E share a <see cref="ChoiceGroup"/>: one of them runs.
    /// Ops from xi-tools (docs/fx/effect_system.md command reference); the 0x50 empty member is our reading of the
    /// retail monster voice routines (<c>vatk</c>: four cries and six 0x50 entries).
    /// </summary>
    /// <param name="Op">The command's op.</param>
    /// <param name="StartTick">Tick it runs at (sum of the delays before it).</param>
    /// <param name="Name">The sound pointer's or linked routine's name (empty for 0x50).</param>
    /// <param name="SoundId">The <c>.spw</c> id the sound pointer resolves to in the routine's DAT, or 0.</param>
    /// <param name="ChoiceGroup">The random choice it belongs to, or -1.</param>
    public readonly record struct RoutineSoundCommand(byte Op, int StartTick, string Name, int SoundId, int ChoiceGroup);

    /// <summary>
    /// A sound a motion routine plays (#41), flattened through the actor's own links: at <see cref="Tick"/>, one of
    /// <see cref="Choices"/> (a 0 choice is silence), or, for <see cref="TargetRoutine"/>, the named routine of the
    /// action's other actor (op 0x09, e.g. <c>damg</c> runs the attacker's <c>chit</c>, its weapon's hit sound).
    /// </summary>
    public sealed record RoutineSoundCue(int Tick, IReadOnlyList<int> Choices, byte Op, string TargetRoutine = "")
    {
        /// <summary>Whether this cue runs a routine on the other actor rather than playing a sound.</summary>
        public bool IsTargetLink => TargetRoutine.Length > 0;
    }
}
