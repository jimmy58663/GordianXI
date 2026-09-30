// src/Gordian.Core/Animation/EventMotionBank.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Animation
{
    /// <summary>
    /// An event motion DAT (<c>mot_</c>): the gesture routines (Section 0x07: <c>tlk0</c>, <c>itl0</c>, <c>pas0</c>, ...)
    /// and the clips they play (Section 0x2B) that an event loads onto an entity with opcode 0x5B before playing one of
    /// them (XiEvents OpCodes/0x005B, ReadEventMotionRes; file ids in <see cref="Events.EventVm.MotionBankFileId"/>). The
    /// clips are authored for the skeleton of the models the event gives them to.
    /// </summary>
    public sealed class EventMotionBank
    {
        private readonly Dictionary<string, AnimationClip> _clips = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RawMotionRoutine> _raw = new(StringComparer.Ordinal);

        public EventMotionBank(int fileId, IEnumerable<AnimationClip> clips, IEnumerable<RawMotionRoutine> routines)
        {
            FileId = fileId;
            foreach (var clip in clips)
            {
                _clips[clip.Name] = clip;
                // Routines name a clip by its first three letters and a wildcard (tl1?), as on the models.
                if (clip.Name.Length >= 2 && clip.Name[^1] is >= '0' and <= '2') _clips.TryAdd(clip.Name[..^1], clip);
            }
            foreach (var routine in routines)
            {
                if (routine.Name.Length > 0) _raw[routine.Name] = routine;
            }
            Routines = MotionRoutineDecoder.BuildAll(_raw, _clips.ContainsKey);
        }

        public int FileId { get; }

        /// <summary>The bank's routines, flattened against its own clips.</summary>
        public IReadOnlyDictionary<string, MotionRoutine> Routines { get; }

        public IReadOnlyDictionary<string, AnimationClip> Clips => _clips;

        /// <summary>Reads a bank from its DAT; null when it has no routines.</summary>
        public static EventMotionBank? Parse(ReadOnlySpan<byte> file, int fileId)
        {
            var container = EntityModelLoader.ParseDatContainer(file, $"EventMotion_{fileId}");
            if (container.Routines.Count == 0) return null;
            return new EventMotionBank(fileId, container.Animations, container.Routines);
        }

        /// <summary>How many 60 Hz frames a routine of the bank plays (one pass of a looping one), 0 when it has none.</summary>
        public int GetRoutineFrames(string name) =>
            Routines.TryGetValue(name, out var routine) ? EntityAnimationState.OnePassTicks(routine) : 0;
    }
}
