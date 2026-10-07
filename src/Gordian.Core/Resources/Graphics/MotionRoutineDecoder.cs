// src/Gordian.Core/Resources/Graphics/MotionRoutineDecoder.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
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
    /// routine; 0x3B and 0x3C wait for it to end. A link to <c>dada</c> or <c>mdam</c> is the moment the result shows.</item>
    /// <item>0x21 / 0x25 procedural flinch: +0x18 f32 duration in ticks.</item>
    /// <item>0x5A procedural pose flash: +0x0E u16 pose index, +0x14 f32 duration in ticks.</item>
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
                SoundCommands = sounds
            };
        }

        /// <summary>
        /// Flattens every routine in <paramref name="routines"/> that plays a clip, shows a hit or carries a reaction, following
        /// links into the same set (links to routines outside it, such as the shared <c>ROM/0/0</c> ones, are skipped). Clip
        /// references are kept only when <paramref name="clipExists"/> knows them.
        /// </summary>
        public static Dictionary<string, MotionRoutine> BuildAll(IReadOnlyDictionary<string, RawMotionRoutine> routines, Func<string, bool> clipExists)
        {
            ArgumentNullException.ThrowIfNull(routines);
            ArgumentNullException.ThrowIfNull(clipExists);

            var result = new Dictionary<string, MotionRoutine>(StringComparer.Ordinal);
            foreach (var (name, _) in routines)
            {
                var built = Build(routines, name, clipExists);
                if (built != null) result[name] = built;
            }
            return result;
        }

        /// <summary>
        /// Flattens one routine (see <see cref="BuildAll"/>); null when it has no clip, hit or reaction.
        /// </summary>
        public static MotionRoutine? Build(IReadOnlyDictionary<string, RawMotionRoutine> routines, string name, Func<string, bool> clipExists)
        {
            if (!routines.TryGetValue(name, out var root)) return null;

            var segments = new List<MotionSegment>();
            var hits = new List<int>();
            int flinchTicks = 0, poseIndex = -1, poseTicks = 0;
            int end = Flatten(root, 0, 0);

            int Flatten(RawMotionRoutine routine, int baseTick, int depth)
            {
                int shift = 0; // blocking links push the rest of the routine back by the child's length
                int routineEnd = baseTick + routine.TotalTicks;
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

                        default:
                            if (command.Reference is "dada" or "mdam")
                            {
                                hits.Add(at);
                            }
                            else if (depth < MaxLinkDepth && command.Reference != routine.Name
                                     && routines.TryGetValue(command.Reference, out var child))
                            {
                                int childEnd = Flatten(child, at, depth + 1);
                                routineEnd = Math.Max(routineEnd, childEnd);
                                if (command.Op is OpLinkWait or OpLinkActorWait) shift += child.TotalTicks;
                            }
                            break;
                    }
                }
                return routineEnd + shift;
            }

            if (segments.Count == 0 && hits.Count == 0 && flinchTicks == 0 && poseIndex < 0) return null;

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
                PoseFlashTicks = poseTicks
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
