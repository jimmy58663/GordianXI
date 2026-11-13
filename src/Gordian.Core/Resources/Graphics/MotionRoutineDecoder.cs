// src/Gordian.Core/Resources/Graphics/MotionRoutineDecoder.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// Clean-room decoder for the motion side of FFXI DAT Section 0x07 (EffectRoutine) chunks: the routines an actor's
    /// model and motion packs carry for its actions (the swings <c>ati0</c>-<c>ati2</c>, the moving swings
    /// <c>atf0</c>/<c>atb0</c>/<c>atl0</c>/<c>atr0</c>, the cast chants <c>ca??</c> and releases <c>sh??</c>/<c>ss??</c>, the
    /// reactions <c>damg</c>/<c>gurd</c>/<c>pary</c>, the weapon draw <c>out0</c> and sheathe <c>in 0</c>).
    /// Command layout as <see cref="EffectRoutineDecoder"/>: { u8 op, u16 size in dwords (low 5 bits), u8, u16 delay,
    /// u16 duration, 4-char reference, ... }, a command running at the sum of the delays before it. The ops read here:
    /// <list type="bullet">
    /// <item>0x05 play a Section 0x2B clip: +0x10 f32 speed, +0x18 u16 blend-in ticks, +0x1C u16 blend-out ticks, +0x1E u16
    /// loop count (0 = loop until replaced: xi-tools docs/anim/schedule.md <c>maxLoops</c>; seen on the event gestures' last
    /// clips and the PC <c>corp</c>, #193); the reference ends in <c>?</c> where the body-region digit goes.</item>
    /// <item>0x03 / 0x3B (this DAT or the shared <c>ROM/0/0</c>), 0x57 / 0x3C (the actor's own routines) link another
    /// routine; 0x3B and 0x3C wait for it to end (<see cref="WaitTicks"/>). A link to <c>dada</c>, <c>mdam</c> or <c>ldad</c> is
    /// the moment the result shows.</item>
    /// <item>0x21 / 0x25 procedural flinch: +0x18 f32 duration in ticks.</item>
    /// <item>0x5A procedural pose flash: +0x0E u16 pose index, +0x14 f32 duration in ticks.</item>
    /// <item>0x75 show / hide a weapon slot: +0x08 u32 hide, +0x0C u16 slot (0 main, 1 sub, 2 ranged).</item>
    /// <item>0x2E control lock (+0x06 u16 duration in ticks, no other operand): while it runs the caster's player cannot
    /// move. Weapon skill and job ability <c>main</c>s carry it from tick 0 to about the hit (Fast Blade 90, job abilities
    /// 100 / 110), the weapon draw / sheathe <c>in 0</c> / <c>out0</c> for 72, an item use's start <c>cait</c> for 39, and the
    /// shared releases <c>wash</c> / <c>waso</c> of <c>ROM/0/0</c> (every spell, item and ranged release waits on one) for 60
    /// (<see cref="SharedControlLocks"/>). The swings and the cast chants carry none, so the player moves while attacking or
    /// casting. Named <c>IdleLockCasterControl</c> in xi-tools docs/reference/ps2_beta_2001.md and read as "the player
    /// cannot act" in docs/ability/mixer.md; the routines above were read from the retail DATs (2026-11-12, #511).</item>
    /// <item>0x76 / 0x77 ranged start / finish (no parameters): the PC <c>calg</c> and <c>shlg</c> run the actor's
    /// <c>lc&lt;NN&gt;</c> / <c>ls&lt;NN&gt;</c>, NN the ranged weapon's RangeType (xi-tools docs/fx/effect_system.md op list,
    /// docs/ability/mixer.md "lc&lt;NN&gt; (RangedStart) then ls&lt;NN&gt; (RangedFinish)").</item>
    /// </list>
    /// Format referenced from xi-tools (docs/fx/effect_system.md op table, docs/ability/mixer.md link and lock rules) and
    /// xi-model-viewer (https://github.com/vekien/xi-model-viewer, ui/js/dat.js parseRoutine); the op 0x05 blend and loop
    /// fields and the 0x21 / 0x5A parameters were read from the retail PC battle packs (e.g. <c>ROM/32/13</c>) and base
    /// motion DATs (<c>ROM/27/82</c>).
    /// </summary>
    public static class MotionRoutineDecoder
    {
        /// <summary>A clip that repeats at least this often is treated as looping until replaced (the cast chants).</summary>
        public const int SustainedLoopCount = 8;

        private const int MaxLinkDepth = 4;

        private const byte OpEnd = 0x00;
        private const byte OpPlayClip = 0x05;
        private const byte OpLink = 0x03;
        private const byte OpLinkWait = 0x3B;
        private const byte OpLinkActor = 0x57;
        private const byte OpLinkActorWait = 0x3C;
        private const byte OpFlinchSource = 0x21;
        private const byte OpFlinchTarget = 0x25;
        private const byte OpPoseFlash = 0x5A;
        private const byte OpShowHideWeapon = 0x75;
        private const byte OpRangedStart = 0x76;
        private const byte OpRangedFinish = 0x77;
        private const byte OpSpellEffect = 0x19;
        private const byte OpControlLock = 0x2E;

        /// <summary>
        /// The shared hit routines a link to which marks the moment the result shows: <c>dada</c> (melee), <c>mdam</c>
        /// (magic) and <c>ldad</c> (ranged: the <c>ls??</c> finishes link it, e.g. <c>ls06</c> at tick 30).
        /// </summary>
        private static bool IsHitLink(string reference) => reference is "dada" or "mdam" or "ldad";

        /// <summary>
        /// Reads a routine's commands. Returns null when the payload is too short or its command offset is out of range.
        /// </summary>
        public static RawMotionRoutine? Decode(ReadOnlySpan<byte> payload, string name)
        {
            if (payload.Length < 0x20) return null;

            int commandsOffset = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0x14)) - 16;
            int totalTicks = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0x1C));
            if (commandsOffset < 0 || commandsOffset >= payload.Length) return null;

            var commands = new List<MotionRoutineCommand>();
            var sounds = new List<RoutineSoundCommand>();
            List<RoutineSpellEffect>? spellEffects = null;
            int choiceGroup = -1, choiceGroups = 0;
            int clock = 0;
            int p = commandsOffset;
            while (p + 8 <= payload.Length)
            {
                byte op = payload[p];
                int sizeBytes = Math.Max(1, BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(p + 1)) & 0x1F) * 4;
                if (op == OpEnd) break;

                int start = clock;
                clock += BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(p + 4));
                int duration = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(p + 6));
                var command = payload.Slice(p, Math.Min(sizeBytes, payload.Length - p));
                RoutineSoundCollector.ReadCommand(op, command, start, sounds, ref choiceGroup, ref choiceGroups);
                if (op == OpSpellEffect && command.Length >= 12)
                {
                    (spellEffects ??= new List<RoutineSpellEffect>()).Add(new RoutineSpellEffect(start, (int)BinaryPrimitives.ReadUInt32LittleEndian(command.Slice(8))));
                }

                switch (op)
                {
                    case OpPlayClip when command.Length >= 0x20:
                        commands.Add(new MotionRoutineCommand(
                            op, start, duration, ReadId(command.Slice(8, 4)),
                            BlendInTicks: BinaryPrimitives.ReadUInt16LittleEndian(command.Slice(0x18)),
                            BlendOutTicks: BinaryPrimitives.ReadUInt16LittleEndian(command.Slice(0x1C)),
                            Loops: (int)BinaryPrimitives.ReadUInt16LittleEndian(command.Slice(0x1E)),
                            Speed: ReadPositiveFloat(command.Slice(0x10), 1.0f),
                            PoseIndex: -1,
                            ReactionTicks: 0));
                        break;

                    case OpLink or OpLinkWait or OpLinkActor or OpLinkActorWait when command.Length >= 12:
                        commands.Add(new MotionRoutineCommand(op, start, duration, ReadId(command.Slice(8, 4)), 0, 0, 1, 1.0f, -1, 0));
                        break;

                    case OpFlinchSource or OpFlinchTarget when command.Length >= 0x1C:
                        commands.Add(new MotionRoutineCommand(op, start, duration, string.Empty, 0, 0, 1, 1.0f, -1,
                            ReactionTicks: (int)ReadPositiveFloat(command.Slice(0x18), 0f)));
                        break;

                    case OpShowHideWeapon when command.Length >= 0x10:
                        // u32 hide (1) or show (0), u16 weapon slot: ROM/0/0.DAT's hwmg stows slots 0 and 1 for a cast;
                        // a fixed NPC model's init runs wof4 to hide its wep4 weapon (Prince Trion, model 64). Op meaning
                        // from xi-tools docs/reference/ps2_decomp_crosscheck.md (HideWepControl).
                        commands.Add(new MotionRoutineCommand(op, start, duration, string.Empty, 0, 0, 1, 1.0f, -1, 0,
                            WeaponSlot: BinaryPrimitives.ReadUInt16LittleEndian(command.Slice(0x0C)),
                            HideWeapon: BinaryPrimitives.ReadUInt32LittleEndian(command.Slice(0x08)) != 0));
                        break;

                    case OpControlLock:
                        if (duration > 0) commands.Add(new MotionRoutineCommand(op, start, duration, string.Empty, 0, 0, 1, 1.0f, -1, 0));
                        break;

                    case OpRangedStart or OpRangedFinish:
                        commands.Add(new MotionRoutineCommand(op, start, duration, string.Empty, 0, 0, 1, 1.0f, -1, 0));
                        break;

                    case OpPoseFlash when command.Length >= 0x18:
                        commands.Add(new MotionRoutineCommand(op, start, duration, string.Empty, 0, 0, 1, 1.0f,
                            PoseIndex: BinaryPrimitives.ReadUInt16LittleEndian(command.Slice(0x0E)),
                            ReactionTicks: (int)ReadPositiveFloat(command.Slice(0x14), 0f)));
                        break;
                }

                p += sizeBytes;
            }

            return new RawMotionRoutine
            {
                Name = name ?? string.Empty,
                TotalTicks = totalTicks > 0 ? totalTicks : clock,
                Commands = commands,
                SoundCommands = sounds,
                SpellEffects = spellEffects ?? (IReadOnlyList<RoutineSpellEffect>)Array.Empty<RoutineSpellEffect>()
            };
        }

        /// <summary>
        /// Flattens every routine in <paramref name="routines"/> that plays a clip, shows a hit or carries a reaction, following
        /// links into the same set (links to routines outside it, such as the shared <c>ROM/0/0</c> ones, are skipped). Clip
        /// references are kept only when <paramref name="clipExists"/> knows them. The weapon show / hide routines of
        /// <c>ROM/0/0</c> (<see cref="SharedWeaponRoutines"/>) are the exception: their changes are taken in. The ranged
        /// start / finish ops run <c>lc</c> / <c>ls</c> + <paramref name="rangeType"/> (two digits), or nothing when it is negative.
        /// </summary>
        public static Dictionary<string, MotionRoutine> BuildAll(IReadOnlyDictionary<string, RawMotionRoutine> routines, Func<string, bool> clipExists,
            int rangeType = -1)
        {
            ArgumentNullException.ThrowIfNull(routines);
            ArgumentNullException.ThrowIfNull(clipExists);

            var result = new Dictionary<string, MotionRoutine>(StringComparer.Ordinal);
            foreach (var (name, _) in routines)
            {
                var built = Build(routines, name, clipExists, rangeType);
                if (built != null) result[name] = built;
            }
            return result;
        }

        /// <summary>
        /// How long a blocking link (0x3B / 0x3C) to <paramref name="child"/> waits: its own length, or, for a routine whose
        /// length is 0 and that only chains others, the sum of what its own blocking links wait for. The work schedules of
        /// the fixed NPC models (<c>@scd</c>, #455) are built that way: <c>@scd</c> chains <c>m000</c>, <c>m001</c>... with
        /// 0x3B, each of length 0 and itself chaining the work loop <c>l000</c> one, three or five times with 0x3B. Reading
        /// such a routine's own length (0) had every link start at once. *Inference* from the data.
        /// </summary>
        public static int WaitTicks(RawMotionRoutine child, IReadOnlyDictionary<string, RawMotionRoutine> routines, int depth = 0)
        {
            ArgumentNullException.ThrowIfNull(child);
            ArgumentNullException.ThrowIfNull(routines);
            if (child.TotalTicks > 0 || depth >= MaxLinkDepth) return child.TotalTicks;
            int ticks = 0;
            foreach (var command in child.Commands)
            {
                if (command.Op is OpLinkWait or OpLinkActorWait && command.Reference != child.Name
                    && routines.TryGetValue(command.Reference, out var grandchild))
                {
                    ticks += WaitTicks(grandchild, routines, depth + 1);
                }
            }
            return ticks;
        }

        /// <summary>The routine a ranged start (0x76) or finish (0x77) runs for a RangeType: <c>lc06</c> / <c>ls06</c> for a bow.</summary>
        public static string RangedRoutineName(bool finish, int rangeType) =>
            rangeType is >= 0 and < 100 ? (finish ? "ls" : "lc") + rangeType.ToString("D2", System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

        /// <summary>
        /// Flattens one routine (see <see cref="BuildAll"/>); null when it has no clip, hit or reaction.
        /// </summary>
        public static MotionRoutine? Build(IReadOnlyDictionary<string, RawMotionRoutine> routines, string name, Func<string, bool> clipExists,
            int rangeType = -1)
        {
            if (!routines.TryGetValue(name, out var root)) return null;

            var segments = new List<MotionSegment>();
            var hits = new List<int>();
            var weaponChanges = new List<WeaponVisibilityChange>();
            var controlLocks = new List<ControlLock>();
            int flinchTicks = 0, poseIndex = -1, poseTicks = 0;
            int end = Flatten(root, 0, 0);

            int Flatten(RawMotionRoutine routine, int baseTick, int depth)
            {
                int shift = 0; // blocking links push the rest of the routine back by the child's length
                int routineEnd = baseTick;
                foreach (var command in routine.Commands)
                {
                    int at = baseTick + command.StartTick + shift;
                    switch (command.Op)
                    {
                        case OpPlayClip:
                            string clip = command.Reference.TrimEnd('?').TrimEnd();
                            if (clip.Length > 0 && clipExists(clip))
                            {
                                segments.Add(new MotionSegment(clip, at, command.Duration, command.BlendInTicks, command.BlendOutTicks, command.Loops, command.Speed));
                            }
                            break;

                        case OpFlinchSource or OpFlinchTarget:
                            flinchTicks = Math.Max(flinchTicks, command.ReactionTicks);
                            break;

                        case OpPoseFlash:
                            poseIndex = command.PoseIndex;
                            poseTicks = command.ReactionTicks;
                            break;

                        case OpControlLock:
                            controlLocks.Add(new ControlLock(at, command.Duration));
                            break;

                        case OpShowHideWeapon:
                            if (command.WeaponSlot >= 0) weaponChanges.Add(new WeaponVisibilityChange(at, command.WeaponSlot, command.HideWeapon));
                            break;

                        case OpRangedStart or OpRangedFinish:
                            string ranged = RangedRoutineName(command.Op == OpRangedFinish, rangeType);
                            if (ranged.Length > 0 && depth < MaxLinkDepth && routines.TryGetValue(ranged, out var rangedChild))
                            {
                                routineEnd = Math.Max(routineEnd, Flatten(rangedChild, at, depth + 1));
                            }
                            break;

                        default:
                            if (IsHitLink(command.Reference))
                            {
                                hits.Add(at);
                            }
                            else if (depth < MaxLinkDepth && command.Reference != routine.Name
                                     && routines.TryGetValue(command.Reference, out var child))
                            {
                                int childEnd = Flatten(child, at, depth + 1);
                                routineEnd = Math.Max(routineEnd, childEnd);
                                if (command.Op is OpLinkWait or OpLinkActorWait) shift += WaitTicks(child, routines);
                            }
                            else
                            {
                                if (SharedWeaponRoutines.ByName.TryGetValue(command.Reference, out var shared))
                                {
                                    foreach (var (slot, hide) in shared) weaponChanges.Add(new WeaponVisibilityChange(at, slot, hide));
                                }
                                if (SharedControlLocks.ByName.TryGetValue(command.Reference, out int lockTicks))
                                {
                                    controlLocks.Add(new ControlLock(at, lockTicks));
                                }
                            }
                            break;
                    }
                }
                return Math.Max(routineEnd, baseTick + routine.TotalTicks + shift);
            }

            if (segments.Count == 0 && hits.Count == 0 && flinchTicks == 0 && poseIndex < 0 && controlLocks.Count == 0) return null;

            segments.Sort((a, b) => a.StartTick.CompareTo(b.StartTick));
            hits.Sort();
            return new MotionRoutine
            {
                Name = name,
                TotalTicks = end,
                Segments = segments,
                HitTicks = hits,
                FlinchTicks = flinchTicks,
                PoseFlashIndex = poseIndex,
                PoseFlashTicks = poseTicks,
                // Stable by tick: changes at one tick apply in file order.
                WeaponChanges = weaponChanges.Count == 0 ? Array.Empty<WeaponVisibilityChange>() : weaponChanges.OrderBy(c => c.Tick).ToArray(),
                ControlLocks = controlLocks.Count == 0 ? Array.Empty<ControlLock>() : controlLocks.OrderBy(c => c.StartTick).ToArray()
            };
        }

        private static float ReadPositiveFloat(ReadOnlySpan<byte> span, float fallback)
        {
            float value = BinaryPrimitives.ReadSingleLittleEndian(span);
            return float.IsFinite(value) && value > 0f && value < 100000f ? value : fallback;
        }

        private static string ReadId(ReadOnlySpan<byte> span)
        {
            int end = span.IndexOf((byte)0);
            if (end < 0) end = span.Length;
            foreach (byte b in span.Slice(0, end))
            {
                if (b < 0x20 || b > 0x7E) return string.Empty;
            }
            return Encoding.ASCII.GetString(span.Slice(0, end));
        }
    }
}
