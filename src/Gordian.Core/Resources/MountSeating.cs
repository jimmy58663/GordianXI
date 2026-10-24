// src/Gordian.Core/Resources/MountSeating.cs
using System;
using System.Collections.Generic;
using System.IO;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;

namespace Gordian.Core.Resources
{
    /// <summary>
    /// How big a mount is drawn under each race, how the rider sits on it and where (#87, round 5). All of it is in the
    /// mount's own model DAT, read from every retail mount (2026-10-23). **Beyond xi-tools / xi-model-viewer / XiPackets**
    /// except where noted.
    /// <list type="bullet">
    /// <item><b>Size per race</b>: Info section (0x45 <c>info</c>) bytes 10-13, four percentages by the rider's build:
    /// 10 Hume, Mithra and Tarutaru, 11 Elvaan, 12 Galka, 13 not used by a playable race. xi-model-viewer
    /// (https://github.com/vekien/xi-model-viewer) <c>ui/js/dat/inspect.js</c> names byte 10 "Scale" (after xim). The raptor
    /// carries 85 / 100 / 115 / 140, matching image-12.png's per-race fits (0.84-0.86 for Hume, Mithra and Tarutaru, 1.04
    /// Elvaan, 1.11 Galka); Fenrir 60 / 60 / 60 / 100, the same size under every race, as image-13.png's five Fenrirs.</item>
    /// <item><b>Pose per race</b>: the second Info section (<c>moun</c>) bytes 8-15, one per race in the retail race order
    /// (Hume male, Hume female, Elvaan male, Elvaan female, Tarutaru male, Tarutaru female, Mithra, Galka): which of the mount
    /// rider pack's poses the rider holds. 6 straddles (the chocobo seat; the raptor, Fenrir and most others), 5 stands
    /// (the Magic Pot and Levitus, image-26.png), 3 sits as on a chair (the Spectral Chair, Moogle), 2 sits on the floor, 1
    /// kneels, 4 lies prone, 7 crouches. xi-model-viewer reads byte 10 of this section as a "pose type"; that it is one
    /// pose per race is Beyond xi-model-viewer. Bytes 0-7 are small signed per-race values (the tiger 0x40 for both female
    /// Elvaan, Hume female and Mithra; the morbol -20 for most) that are not decoded.</item>
    /// <item><b>Seat per race</b>: skeleton reference 48 + the race's index (r48 Hume male ... r55 Galka): where the rider
    /// sits, on the mount's body (the raptor's back, Fenrir's mid-back 0.5 behind its hip joint, the Magic Pot's lid).</item>
    /// <item><b>Rider poses</b>: the mount rider packs <c>ROM/351/61</c> + 3 per race (Hume male, Hume female, Elvaan male,
    /// Elvaan female, Tarutaru, Mithra, Galka; the next two files are waist parts), clips <c>{pose}hi</c> (idle),
    /// <c>{pose}lk</c>, <c>{pose}un</c>, <c>{pose}vb</c> for poses 1-7 plus the chocobo's <c>chi</c> set; each pose's four clips
    /// are as long as each other (the rider holds the pose; the mount's seat carries it). Pose 6 is the chocobo pack's
    /// straddle, authored with its origin on the ground under a chocobo scaled to the race (<see cref="MountModels.MountScale"/>);
    /// the other poses have it at the rider's feet or seat, on the support.</item>
    /// </list>
    /// </summary>
    public static class MountSeating
    {
        /// <summary>The first per-race seat reference (r48 Hume male ... r55 Galka).</summary>
        public const int FirstSeatReference = 48;

        /// <summary>The straddle pose (the chocobo pack's): its origin is on the ground under the chocobo.</summary>
        public const byte StraddlePose = 6;

        /// <summary>A mount's ride data: its Info bytes 10-13 as fractions (byte 10 its size) and its rider pose per race (<c>moun</c> bytes 8-15).</summary>
        public sealed record RideData(float[] Scales, byte[] Poses)
        {
            /// <summary>
            /// The mount's size before the rider's race (Info byte 10, xi-model-viewer's "Scale"; #87 round 7). The renderer
            /// multiplies it by the race's chocobo factor (<see cref="MountModels.MountScale"/>). Round 5 read bytes 11-12 as
            /// the Elvaan and Galka sizes: that left Fenrir (60 / 60 / 60) one size under every race, where retail's grows from
            /// the Tarutaru to the Galka (image-13.png), and the raptor's 0.85 x factor fits image-12.png's per-race sizes
            /// (Elvaan 1.04, Galka 1.11) better than 100 / 115 did. *Inference*; bytes 11-13 are not decoded (raptor
            /// 100 / 115 / 140, Fenrir 60 / 60 / 100).
            /// </summary>
            public float BaseScale => Scales.Length > 0 ? Scales[0] : 1f;

            /// <summary>The pose <paramref name="race"/> holds; the straddle when the mount does not say.</summary>
            public byte PoseFor(CharacterRace race)
            {
                int index = CharacterEquipmentResolver.GetRetailRaceIndex(race);
                return index >= 0 && index < Poses.Length && Poses[index] != 0 ? Poses[index] : StraddlePose;
            }
        }

        /// <summary>The seat reference of <paramref name="race"/> (48 + its retail race index); -1 for a race without one.</summary>
        public static int SeatReference(CharacterRace race)
        {
            int index = CharacterEquipmentResolver.GetRetailRaceIndex(race);
            return index < 0 ? -1 : FirstSeatReference + index;
        }

        /// <summary>Reads a mount model's ride data; null when it has no <c>info</c> section (the chocobo race has no <c>moun</c>).</summary>
        public static RideData? Read(ReadOnlySpan<byte> dat)
        {
            float[]? scales = null;
            var poses = new byte[8];
            foreach (var header in DatSectionWalker.ReadHeaders(dat))
            {
                if (header.TypeCode != DatSectionType.Info || header.DataSizeBytes < 16 || header.DataOffset + 16 > dat.Length) continue;
                var body = dat.Slice(header.DataOffset, 16);
                if (header.DatId.StartsWith("moun", StringComparison.Ordinal))
                {
                    body.Slice(8, 8).CopyTo(poses);
                }
                else
                {
                    scales = new float[4];
                    for (int i = 0; i < 4; i++) scales[i] = body[10 + i] is 0 or 0xFF ? 1f : body[10 + i] / 100f;
                }
            }
            return scales == null ? null : new RideData(scales, poses);
        }

        /// <summary>The race's mount rider pack: its motions and first waist part (<c>ROM/351/61</c> and <c>62</c> for Hume male).</summary>
        public static (string Motions, string Waist) PackPaths(CharacterRace race)
        {
            int pack = race switch
            {
                CharacterRace.HumeMale => 0,
                CharacterRace.HumeFemale => 1,
                CharacterRace.ElvaanMale => 2,
                CharacterRace.ElvaanFemale => 3,
                CharacterRace.TaruMale or CharacterRace.TaruFemale => 4,
                CharacterRace.Mithra => 5,
                CharacterRace.Galka => 6,
                _ => -1,
            };
            if (pack < 0) return (string.Empty, string.Empty);
            int file = 61 + pack * 3;
            return (Path.Combine("ROM", "351", $"{file}.DAT"), Path.Combine("ROM", "351", $"{file + 1}.DAT"));
        }

        /// <summary>The race's mount rider pack (body-region parts joined) and routines; null without it.</summary>
        public static (List<AnimationClip> Clips, List<RawMotionRoutine> Routines)? LoadPack(CharacterRace race, Func<string, byte[]?> datByPath)
        {
            var (motionsPath, waistPath) = PackPaths(race);
            if (motionsPath.Length == 0) return null;
            byte[]? motions = datByPath(motionsPath);
            if (motions == null || motions.Length == 0) return null;
            var parsed = EntityModelLoader.ParseDatContainer(motions, motionsPath);
            var clips = new List<AnimationClip>(parsed.Animations);
            byte[]? waist = datByPath(waistPath);
            if (waist != null && waist.Length > 0) clips.AddRange(EntityModelLoader.ParseDatContainer(waist, waistPath).Animations);
            return (EntityModelLoader.JoinBodyRegionParts(clips), parsed.Routines);
        }

        /// <summary>
        /// A pose's clips under the names the rider plays: <c>{pose}hi</c> as <c>chi</c> (its idle), <c>{pose}lk</c> as
        /// <c>wlk</c>, <c>{pose}un</c> as <c>run</c>, <c>{pose}vb</c> as <c>mvb</c>. Empty when the pack lacks the pose.
        /// </summary>
        public static List<AnimationClip> PoseClips(IReadOnlyList<AnimationClip> pack, byte pose)
        {
            var byName = new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);
            foreach (var clip in pack) byName[clip.Name] = clip;
            var result = new List<AnimationClip>(4);
            foreach (var (suffix, name) in new[] { ("hi", "chi"), ("lk", "wlk"), ("un", "run"), ("vb", "mvb") })
            {
                if (!byName.TryGetValue($"{pose}{suffix}", out var clip)) continue;
                result.Add(new AnimationClip { Name = name, NumFrames = clip.NumFrames, KeyFrameDuration = clip.KeyFrameDuration, Tracks = clip.Tracks });
            }
            return result;
        }
    }
}
