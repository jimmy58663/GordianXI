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
            var all = new List<AnimationClip>(clips);
            foreach (var clip in all)
            {
                _clips[clip.Name] = clip;
                // Routines name a clip by its first three letters and a wildcard (tl1?), as on the models.
                if (clip.Name.Length >= 2 && clip.Name[^1] is >= '0' and <= '2') _clips.TryAdd(clip.Name[..^1], clip);
            }
            // A gesture is stored as body-region parts (tlk0 legs, tlk1 upper body with the weapon joints, tlk2 waist),
            // as on the fixed NPC models: the stem plays them together. With the legs part alone, Curilla's arms and
            // sheathed sword fell to the bind pose while she talked (#163, bank 140 = file 32244).
            foreach (var joined in EntityModelLoader.JoinBodyRegionParts(all)) _clips[joined.Name] = joined;
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

        /// <summary>
        /// The two DATs of a 0x66 motion package (XiEvents OpCodes/0x005B, ReadTpcEventMotionRes; called a package, as in
        /// xi-tools, which gives Cornelia's <c>kka0</c> as package 12). Located in the retail DATs (2026-10-01): file
        /// 32360 + 2n holds package n with the waist part (the gestures' parts 0 and 1 and their routines in one folder,
        /// part 2 in another; file 32361 + 2n is a twin, presumably for robe bodies as with the emotes), and file 32712 + n
        /// holds it without the waist part; the first table ends where the second begins (176 packages). Some packages are
        /// only in the second (29, the Royal Knights' talk: 32418 is empty, 32741 has it). Every package whose few
        /// gestures pin it down (17, 22, 24, 25, 43, 51, 54, 57, 62, 63 over all zones' 0x66 uses) has them at both places.
        /// The San d'Oria packages (20, 21, 29) are authored for the Elvaan skeleton.
        /// </summary>
        public static (int WithWaist, int WithoutWaist) PackageFileIds(int package) => (32360 + 2 * package, 32712 + package);

        /// <summary>How many 60 Hz frames a routine of the bank plays (one pass of a looping one), 0 when it has none.</summary>
        public int GetRoutineFrames(string name) =>
            Routines.TryGetValue(name, out var routine) ? EntityAnimationState.OnePassTicks(routine) : 0;
    }
}
