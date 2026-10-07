// src/Gordian.Core/Resources/Graphics/RoutineSoundCollector.cs
using System;
using System.Collections.Generic;
using System.Text;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// The sounds of an actor's motion routines (#41: combat and action sounds). Retail keeps them in the routines
    /// themselves, so they play with the motion: a monster's swing <c>ati0</c> plays its <c>skaz</c> whoosh, its
    /// <c>atk0</c> links the cry <c>vatk</c> (a random pick between four cries and six silent entries), and a hit's
    /// reaction <c>damg</c> runs <c>chit</c> on the attacker (op 0x09: a monster's own <c>shit</c>; a character's
    /// <c>chit</c> in its base motion DAT links its weapon's <c>se h</c>, the weapon DAT's hit sound) plus the target's
    /// own <c>sdam</c> and cry <c>vdam</c>; <c>dead</c> links the death cry <c>vded</c>; a character's swing links its
    /// weapon's <c>skaz</c>, and the draw / sheathe its <c>sotr</c> / <c>sinr</c>.
    /// <para>Ops from xi-tools (docs/fx/effect_system.md: 0x0A / 0x0B / 0x4A / 0x53 / 0x60 sound effect, 0x03 / 0x3B /
    /// 0x57 / 0x3C linked routine, 0x09 linked routine on the target, 0x3D / 0x3E random child); the routine chains above
    /// were read from the retail data (monster model 10, Hume male base motion <c>ROM/27/82</c> and battle pack
    /// <c>ROM/32/13</c>, main weapon DATs 8393-8404). <b>Beyond xi-tools:</b> 0x50 as the silent member of a random
    /// choice, and a sound pointer resolving by name within the routine's own DAT.</para>
    /// </summary>
    public static class RoutineSoundCollector
    {
        /// <summary>Links followed at most this deep.</summary>
        public const int MaxLinkDepth = 4;

        private const byte OpLink = 0x03;
        private const byte OpLinkTarget = 0x09;
        private const byte OpLinkWait = 0x3B;
        private const byte OpLinkActorWait = 0x3C;
        private const byte OpRandomOpen = 0x3D;
        private const byte OpRandomClose = 0x3E;
        private const byte OpEmptyChoice = 0x50;
        private const byte OpLinkActor = 0x57;

        /// <summary>Whether an op is a sound command.</summary>
        public static bool IsSoundOp(byte op) => op is 0x0A or 0x0B or 0x4A or 0x53 or 0x60;

        private static bool IsLinkOp(byte op) => op is OpLink or OpLinkWait or OpLinkActor or OpLinkActorWait;

        /// <summary>
        /// Records one command if it matters for sound (called by <see cref="MotionRoutineDecoder.Decode"/> for each
        /// command; <paramref name="group"/> / <paramref name="groups"/> track the random choice the command is in).
        /// </summary>
        internal static void ReadCommand(byte op, ReadOnlySpan<byte> command, int start, List<RoutineSoundCommand> into, ref int group, ref int groups)
        {
            switch (op)
            {
                case OpRandomOpen:
                    group = groups++;
                    return;
                case OpRandomClose:
                    group = -1;
                    return;
                case OpEmptyChoice when group >= 0:
                    into.Add(new RoutineSoundCommand(op, start, string.Empty, 0, group));
                    return;
            }

            if ((IsSoundOp(op) || IsLinkOp(op) || op == OpLinkTarget) && command.Length >= 12)
            {
                string name = ReadName(command.Slice(8, 4));
                if (name.Length > 0)
                {
                    into.Add(new RoutineSoundCommand(op, start, name, 0, group));
                }
            }
        }

        /// <summary>
        /// Resolves the sound pointer names of a DAT's routines against that DAT's Section 0x3D pointers
        /// (<paramref name="pointers"/>: name to sound id). Returns the routine itself when nothing changes.
        /// </summary>
        public static RawMotionRoutine ResolveSoundIds(RawMotionRoutine routine, IReadOnlyDictionary<string, int> pointers)
        {
            ArgumentNullException.ThrowIfNull(routine);
            ArgumentNullException.ThrowIfNull(pointers);
            if (routine.SoundCommands.Count == 0 || pointers.Count == 0)
            {
                return routine;
            }

            var resolved = new RoutineSoundCommand[routine.SoundCommands.Count];
            bool changed = false;
            for (int i = 0; i < resolved.Length; i++)
            {
                RoutineSoundCommand command = routine.SoundCommands[i];
                if (IsSoundOp(command.Op) && command.SoundId == 0 && pointers.TryGetValue(command.Name, out int id))
                {
                    command = command with { SoundId = id };
                    changed = true;
                }

                resolved[i] = command;
            }

            return changed
                ? new RawMotionRoutine { Name = routine.Name, TotalTicks = routine.TotalTicks, Commands = routine.Commands, SoundCommands = resolved }
                : routine;
        }

        /// <summary>
        /// Flattens the sounds of routine <paramref name="name"/>, following its links into <paramref name="routines"/>
        /// (a blocking link, 0x3B / 0x3C, pushes the rest back by the child's length, as for the clips). Sorted by tick.
        /// Empty when the routine is unknown or plays no sound.
        /// </summary>
        public static IReadOnlyList<RoutineSoundCue> Collect(IReadOnlyDictionary<string, RawMotionRoutine> routines, string name)
        {
            ArgumentNullException.ThrowIfNull(routines);
            if (string.IsNullOrEmpty(name) || !routines.TryGetValue(name, out RawMotionRoutine? root))
            {
                return Array.Empty<RoutineSoundCue>();
            }

            var cues = new List<RoutineSoundCue>();
            Walk(root, 0, 0);
            if (cues.Count == 0)
            {
                return Array.Empty<RoutineSoundCue>();
            }

            cues.Sort((a, b) => a.Tick.CompareTo(b.Tick));
            return cues;

            void Walk(RawMotionRoutine routine, int baseTick, int depth)
            {
                int shift = 0;
                var groups = new SortedDictionary<int, (int Tick, byte Op, List<int> Choices)>();
                foreach (RoutineSoundCommand command in routine.SoundCommands)
                {
                    int at = baseTick + command.StartTick + shift;
                    if (command.ChoiceGroup >= 0)
                    {
                        if (!groups.TryGetValue(command.ChoiceGroup, out var group))
                        {
                            group = (at, (byte)0, new List<int>());
                        }

                        // A member that is not a resolved sound (0x50, a link, an unknown pointer) is a silent pick.
                        group.Choices.Add(IsSoundOp(command.Op) ? command.SoundId : 0);
                        if (group.Op == 0 && IsSoundOp(command.Op))
                        {
                            group.Op = command.Op;
                        }

                        groups[command.ChoiceGroup] = group;
                        continue;
                    }

                    if (IsSoundOp(command.Op))
                    {
                        if (command.SoundId > 0)
                        {
                            cues.Add(new RoutineSoundCue(at, new[] { command.SoundId }, command.Op));
                        }
                    }
                    else if (command.Op == OpLinkTarget)
                    {
                        cues.Add(new RoutineSoundCue(at, Array.Empty<int>(), command.Op, command.Name));
                    }
                    else if (IsLinkOp(command.Op) && depth < MaxLinkDepth && command.Name != routine.Name
                             && routines.TryGetValue(command.Name, out RawMotionRoutine? child))
                    {
                        Walk(child, at, depth + 1);
                        if (command.Op is OpLinkWait or OpLinkActorWait)
                        {
                            shift += child.TotalTicks;
                        }
                    }
                }

                foreach (var group in groups.Values)
                {
                    if (group.Choices.Exists(id => id > 0))
                    {
                        cues.Add(new RoutineSoundCue(group.Tick, group.Choices.ToArray(), group.Op));
                    }
                }
            }
        }

        private static string ReadName(ReadOnlySpan<byte> span)
        {
            int end = span.IndexOf((byte)0);
            if (end < 0)
            {
                end = span.Length;
            }

            foreach (byte b in span.Slice(0, end))
            {
                if (b < 0x20 || b > 0x7E)
                {
                    return string.Empty;
                }
            }

            return Encoding.ASCII.GetString(span.Slice(0, end));
        }
    }
}
