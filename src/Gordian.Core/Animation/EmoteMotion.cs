// src/Gordian.Core/Animation/EmoteMotion.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;

namespace Gordian.Core.Animation
{
    /// <summary>
    /// The emote motions of the player races (<c>/clap</c>, <c>/bow</c>...), as an event plays them on an entity with opcode
    /// 0x6E (XiEvents OpCodes/0x006E, SetEntityEmoteAnimation). Each race keeps them in six motion DATs from its emote file
    /// number on (Hume male 32040 = ROM/32/40), eight routines <c>em00</c>-<c>em07</c> each playing one clip as its body
    /// parts (<c>clp?</c>): parts 0 and 1 in that file, the waist part 2 in the file six numbers on. Emote slot n is file
    /// n / 8, routine n % 8. File numbers referenced from xi-tools (src/xi/entity/anim/xi_export.py, _EMOTE_MOTION_FILE,
    /// from the FFXI model viewer's LoadMotion tables); the routines and clips were read from the retail DATs (2026-10-01).
    /// </summary>
    public static class EmoteMotion
    {
        /// <summary>The routine name an emote bank gives its one routine (the DATs' own <c>em0n</c> repeat in every file).</summary>
        public static string RoutineName(int emote) => $"emote{emote}";

        /// <summary>
        /// The emote slot an emote id plays (the ids of C2S 0x05D and LandSandBoat's <c>Emote</c> enum: 0 point, 1 bow,
        /// 2 salute, 3 kneel ... 13 clap). Read from the clips each slot plays (slot 0 <c>bow</c>, 1 <c>poi</c>, 2-4 the
        /// three salutes <c>sl1</c>-<c>sl3</c>, 5 <c>kne</c>, 6 <c>lau</c>, ... 15 <c>clp</c>): from kneel on, slot = id + 2.
        /// The salute variant is taken from <paramref name="variant"/> (presumably the nation; not verified). -1 when none.
        /// </summary>
        public static int Slot(int emote, int variant) => emote switch
        {
            0 => 1,
            1 => 0,
            2 => 2 + Math.Clamp(variant, 0, 2),
            >= 3 and <= 45 => emote + 2,
            _ => -1,
        };

        /// <summary>The emote motion file number (folder * 1000 + file) of a race, 0 for none.</summary>
        public static int FileNumber(CharacterRace race) => CharacterEquipmentResolver.GetRetailRaceIndex(race) switch
        {
            0 => 32040,
            1 => 37013,
            2 => 41114,
            3 => 46075,
            4 => 51037,
            5 => 51071,
            6 => 56041,
            7 => 61008,
            _ => 0,
        };

        /// <summary>
        /// Loads one emote of a race as an event motion bank with the single routine <see cref="RoutineName"/>, its clips
        /// joined from the part files. Null when the race has no emote files or the slot plays nothing (face-only emotes
        /// such as <c>/smile</c>).
        /// </summary>
        public static EventMotionBank? LoadBank(CharacterRace race, int emote, int variant, Func<string, byte[]?> loadPath)
        {
            ArgumentNullException.ThrowIfNull(loadPath);
            int fileNumber = FileNumber(race);
            int slot = Slot(emote, variant);
            if (fileNumber == 0 || slot < 0) return null;
            int file = fileNumber + slot / 8;
            var main = loadPath(CharacterEquipmentResolver.MotFileNoToPath(file));
            if (main == null) return null;
            var container = EntityModelLoader.ParseDatContainer(main, $"Emote_{file}");
            string own = $"em{slot % 8:D2}";
            RawMotionRoutine? routine = null;
            foreach (var r in container.Routines)
            {
                if (r.Name == own) routine = r;
            }
            if (routine == null || routine.Commands.Count == 0) return null;
            var clips = new List<AnimationClip>(container.Animations);
            if (loadPath(CharacterEquipmentResolver.MotFileNoToPath(file + 6)) is { } waist)
            {
                clips.AddRange(EntityModelLoader.ParseDatContainer(waist, $"Emote_{file + 6}").Animations);
            }
            var renamed = new RawMotionRoutine { Name = RoutineName(emote), TotalTicks = routine.TotalTicks, Commands = routine.Commands };
            return new EventMotionBank(file, clips, new[] { renamed });
        }
    }
}
