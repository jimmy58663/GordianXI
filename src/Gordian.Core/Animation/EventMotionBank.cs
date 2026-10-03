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
        /// holds it without the waist part. Some packages are only in the second (29, the Royal Knights' talk: 32418 is
        /// empty, 32741 has it). Every package whose few gestures pin it down (17, 22, 24, 25, 43, 51, 54, 57, 62, 63 over
        /// all zones' 0x66 uses) has them at both places. The San d'Oria packages (20, 21, 29) are authored for the Elvaan
        /// skeleton. These two tables hold packages 0-69 only: from 70 on see <see cref="PackageFiles"/>.
        /// </summary>
        public static (int WithWaist, int WithoutWaist) PackageFileIds(int package) => (32360 + 2 * package, 32712 + package);

        /// <summary>The first package of the race sets table (<see cref="RaceSetFileBase"/>).</summary>
        public const int FirstRaceSetPackage = 70;

        /// <summary>One past the last package located in the race sets table.</summary>
        public const int RaceSetPackageEnd = 140;

        /// <summary>
        /// Packages 70-139 are file 61171 + n, ten per player race: 70-79 Hume male, 80-89 Hume female, 90-99 Elvaan male,
        /// 100-109 Elvaan female, 110-119 Tarutaru, 120-129 Mithra, 130-139 Galka, the same kinds of gesture at the same
        /// place in each ten (n0 <c>atp0</c> / <c>atp1</c> / <c>ec00</c>..., n1 <c>pas0</c> / <c>llz0</c>, n3 <c>dak0</c>-<c>dak3</c>, n4
        /// <c>pia0</c>-<c>pia2</c> ...). Located in the retail DATs (2026-10-03, #209): of the 1,264 uses of a literal
        /// package 70-139 in the event scripts of zones 0-299, 1,220 name a routine of that file and none one of 32360 + 2n
        /// or 32712 + n, which hold other gestures there (32500, package 70 by the first table, has <c>afr0</c> / <c>ure0</c>);
        /// the other 44 name routines found in none of the three.
        /// Port Jeuno 324 gives the player <c>atp0</c> from package skeleton slot · 10 + 70 at the cut to the front shot
        /// under the flash in the sky: a stand with the face tilted 28 degrees up, held, as the maintainer's retail recording
        /// shows (about 1:17-1:21).
        /// </summary>
        public const int RaceSetFileBase = 61171;

        /// <summary>
        /// The DATs that may hold 0x66 motion package <paramref name="package"/> (XiEvents OpCodes/0x005B,
        /// ReadTpcEventMotionRes), in the order to try them: for 0-69 the first and second tables
        /// (<see cref="PackageFileIds"/>), for 70-139 the race sets table (<see cref="RaceSetFileBase"/>); none for the
        /// others, which are not located (140 and up: 2,369 uses in the same scan).
        /// </summary>
        public static int[] PackageFiles(int package)
        {
            if (package < 0) return [];
            if (package < FirstRaceSetPackage)
            {
                var (withWaist, withoutWaist) = PackageFileIds(package);
                return [withWaist, withoutWaist];
            }
            return package < RaceSetPackageEnd ? [RaceSetFileBase + package] : [];
        }

        /// <summary>How many 60 Hz frames a routine of the bank plays (one pass of a looping one), 0 when it has none.</summary>
        public int GetRoutineFrames(string name) =>
            Routines.TryGetValue(name, out var routine) ? EntityAnimationState.OnePassTicks(routine) : 0;
    }
}
