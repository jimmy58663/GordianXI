// src/Gordian.Core/Resources/MountModels.cs
using System;
using System.Collections.Generic;
using System.IO;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Gordian.Core.World;

namespace Gordian.Core.Resources
{
    /// <summary>
    /// The models a mounted player is drawn with (#87): the mount itself and the rider's riding motions.
    /// <list type="bullet">
    /// <item>A mount other than the chocobo is one model DAT at file id <see cref="MountFileBase"/> + its client mount id
    /// (xi-tools <c>docs/mounts/mechanism.md</c>, after xim <c>ActorMountEvent</c>). Checked on the retail files
    /// (2026-10-20): 102706 (id 1) is the blue-and-white raptor of the retail capture (texture <c>wy</c>, 76 joints), 102707
    /// (id 2) <c>tiger</c>, 102708 / 102709 (ids 3 / 4) <c>kani</c> (crabs), 102735 (id 30) <c>rap02</c> (the red raptor).
    /// 102705 (id 0) is byte-identical to 102706: the chocobo (id 0) is drawn from the chocobo race instead, and so is the
    /// Noble Chocobo (id 33, no file).</item>
    /// <item>The chocobo is the chocobo race: skeleton and motions <c>ROM/169/11</c> (58 joints, clips <c>chi</c> idle, <c>wlk</c>,
    /// <c>run</c>, <c>mvb</c>, <c>chh</c>, <c>ch1</c>; routines <c>chor</c>, <c>coff</c>, <c>cdam</c>) with a body, feet, head and tail
    /// DAT per colour (xi-model-viewer / xi-tools <c>mv/lists/characters.json</c>, race "Mount: Chocobo"). Each part is a pair
    /// of files; the body's first one carries the saddle (textures <c>bdy_kura</c>, <c>kura</c>: <i>kura</i> = saddle), the
    /// second is bare. Inference: the second head / feet / tail (more vertices, textures <c>2head</c>, <c>2fot</c>, <c>2o</c>) are
    /// the large beak, large talons and full tail of a raised chocobo (LandSandBoat <c>ChocoboCustomProperties</c> bits 0, 3,
    /// 6); the colour is bits 9-11 (yellow, black, blue, red, green: LandSandBoat <c>xi.chocoboRaising.color</c>).</item>
    /// <item>The rider plays its race's chocobo riding pack in folder 90 (xi-tools <c>characters.json</c> "Chocobo General":
    /// Hume male <c>ROM/90/14</c>, then every 6 files per race, the waist part 2 files on): clips <c>chi</c> (idle), <c>wlk</c>,
    /// <c>run</c>, <c>mvb</c>, <c>chh</c>, <c>ch1</c> / <c>ch2</c> and routines <c>chor</c> (plays <c>chh</c>, 168 ticks, with the
    /// chocobo's own <c>chor</c>), <c>coff</c> (dismount: <c>ch1</c> 104 ticks then <c>ch2</c>), <c>cdam</c>. Its clips are as long
    /// as the chocobo race's (<c>chi</c> 31 frames, <c>run</c> 13, <c>wlk</c> 17, <c>chh</c> 77) and lift the rider from its own
    /// origin (Hume male <c>chi</c>: hips at 1.78 yalms, which fits the chocobo's saddle), each race onto a chocobo scaled to
    /// it (<see cref="MountScale"/>; **Beyond xi-tools**, which does not say how the rider is seated). Other mounts use the
    /// mount rider packs and their own ride data instead (<see cref="MountSeating"/>).</item>
    /// </list>
    /// </summary>
    public static class MountModels
    {
        /// <summary>File id of mount 0's model; mount n is this + n (xi-tools <c>docs/mounts/mechanism.md</c>).</summary>
        public const int MountFileBase = 0x019131;

        /// <summary>The chocobo race's skeleton and motions.</summary>
        public static readonly string ChocoboSkeletonPath = Path.Combine("ROM", "169", "11.DAT");

        /// <summary>The colours of <see cref="ChocoboColorOf"/>, in LandSandBoat's <c>xi.chocoboRaising.color</c> order.</summary>
        public enum ChocoboColor : byte { Yellow = 0, Black, Blue, Red, Green }

        // (folder, file) of the first file of each part pair, per colour: body, feet, head, tail.
        private static readonly (int Folder, int File)[][] ChocoboParts =
        {
            new[] { (169, 12), (169, 14), (169, 16), (169, 18) },
            new[] { (169, 21), (169, 23), (169, 25), (169, 27) },
            new[] { (169, 30), (169, 32), (169, 34), (169, 36) },
            new[] { (170, 41), (170, 43), (170, 45), (170, 47) },
            new[] { (170, 49), (170, 51), (170, 53), (170, 55) },
        };

        private const uint LargeBeakBit = 1u << 0, LargeTalonsBit = 1u << 3, FullTailBit = 1u << 6;

        /// <summary>The model file of a mount other than the chocobo; -1 for the chocobo and the Noble Chocobo (the chocobo race).</summary>
        public static int MountFileId(byte mountId) =>
            mountId is 0 or MountAppearance.NobleChocoboId ? -1 : MountFileBase + mountId;

        /// <summary>A personal chocobo's colour (LandSandBoat <c>ChocoboCustomProperties.color</c>, bits 9-11); yellow otherwise.</summary>
        public static ChocoboColor ChocoboColorOf(in MountAppearance mount)
        {
            if (!mount.IsPersonalChocobo) return ChocoboColor.Yellow;
            uint color = (mount.CustomProperty >> 9) & 0x7;
            return color < ChocoboParts.Length ? (ChocoboColor)color : ChocoboColor.Yellow;
        }

        /// <summary>
        /// The chocobo's part DATs (body, feet, head, tail) for a rental or personal chocobo: the saddled body, and the
        /// large beak, large talons and full tail where a personal chocobo has them (PROVISIONAL, see the type's remarks).
        /// </summary>
        public static IReadOnlyList<string> ChocoboPartPaths(in MountAppearance mount)
        {
            var parts = ChocoboParts[(int)ChocoboColorOf(mount)];
            uint traits = mount.IsPersonalChocobo ? mount.CustomProperty : 0;
            string Part(int index, bool alternate) =>
                Path.Combine("ROM", parts[index].Folder.ToString(), $"{parts[index].File + (alternate ? 1 : 0)}.DAT");
            return new[]
            {
                Part(0, false),
                Part(1, (traits & LargeTalonsBit) != 0),
                Part(2, (traits & LargeBeakBit) != 0),
                Part(3, (traits & FullTailBit) != 0),
            };
        }

        /// <summary>The cache key of a mount's model: one per mount id, one per chocobo look.</summary>
        public static string CacheKey(in MountAppearance mount)
        {
            if (!mount.RidesChocobo) return $"Mount_{mount.MountId}";
            uint traits = mount.IsPersonalChocobo ? mount.CustomProperty & (LargeBeakBit | LargeTalonsBit | FullTailBit) : 0;
            return $"Mount_Chocobo_{ChocoboColorOf(mount)}_{traits}";
        }

        /// <summary>
        /// Assembles the ridden model: the chocobo race with its parts, or the mount's own DAT. Its idle (<c>chi</c>) is also
        /// filed as <c>idl</c>, which the stance resolver plays at rest. Null when not mounted or the files are missing.
        /// </summary>
        public static EntityModel? LoadMount(in MountAppearance mount, Func<string, byte[]?> datByPath, Func<int, byte[]?> datByFileId)
        {
            if (!mount.IsMounted) return null;
            EntityModel? model;
            if (mount.RidesChocobo)
            {
                byte[]? skeleton = datByPath(ChocoboSkeletonPath);
                if (skeleton == null || skeleton.Length == 0) return null;
                var parts = new List<ReadOnlyMemory<byte>>();
                var sources = new List<string?>();
                foreach (var path in ChocoboPartPaths(mount))
                {
                    byte[]? part = datByPath(path);
                    if (part == null || part.Length == 0) continue;
                    parts.Add(part);
                    sources.Add(path);
                }
                model = EntityModelLoader.AssembleModel(skeleton, parts, CacheKey(mount), gearOcclusion: new GearOcclusion(),
                    primarySource: ChocoboSkeletonPath, extraSources: sources);
            }
            else
            {
                int fileId = MountFileId(mount.MountId);
                byte[]? dat = datByFileId(fileId);
                if (dat == null || dat.Length == 0) return null;
                model = EntityModelLoader.AssembleModel(dat, null, CacheKey(mount), gearOcclusion: new GearOcclusion(),
                    primarySource: DecodedTexture.FileLabel(fileId));
            }
            if (model.AnimatedMeshGroups.Count == 0) return null;
            if (model.Animations.TryGetValue("chi", out var idle)) model.Animations["idl"] = idle;
            return model;
        }

        /// <summary>
        /// The race's chocobo riding pack: the motions DAT and its waist part (<c>ROM/90/14</c> and <c>ROM/90/16</c> for Hume
        /// male); empty paths for a race without one (the NPC child races).
        /// </summary>
        public static (string Motions, string Waist) RiderPackPaths(CharacterRace race)
        {
            int file = race switch
            {
                CharacterRace.HumeMale => 14,
                CharacterRace.HumeFemale => 20,
                CharacterRace.ElvaanMale => 26,
                CharacterRace.ElvaanFemale => 32,
                CharacterRace.TaruMale or CharacterRace.TaruFemale => 38,
                CharacterRace.Mithra => 44,
                CharacterRace.Galka => 50,
                _ => -1,
            };
            if (file < 0) return (string.Empty, string.Empty);
            return (Path.Combine("ROM", "90", $"{file}.DAT"), Path.Combine("ROM", "90", $"{file + 2}.DAT"));
        }

        /// <summary>The riding pack's clips (body-region parts joined) and routines; null without the pack.</summary>
        public static (List<AnimationClip> Clips, List<RawMotionRoutine> Routines)? LoadRiderPack(CharacterRace race, Func<string, byte[]?> datByPath)
        {
            var (motionsPath, waistPath) = RiderPackPaths(race);
            if (motionsPath.Length == 0) return null;
            byte[]? motions = datByPath(motionsPath);
            if (motions == null || motions.Length == 0) return null;
            var parsed = EntityModelLoader.ParseDatContainer(motions, motionsPath);
            var clips = new List<AnimationClip>(parsed.Animations);
            byte[]? waist = datByPath(waistPath);
            if (waist != null && waist.Length > 0) clips.AddRange(EntityModelLoader.ParseDatContainer(waist, waistPath).Animations);
            return (EntityModelLoader.JoinBodyRegionParts(clips), parsed.Routines);
        }

        // The on-foot motions a rider must not play: its locomotion and battle stances, which the riding clips replace.
        private static readonly string[] FootMotions =
        {
            "idl", "wlk", "run", "mvb", "mvl", "mvr", "std", "btl", "cwlk", "crun", "cmvb", "cmvl", "cmvr",
        };

        /// <summary>
        /// The rider: <paramref name="character"/>'s skeleton, meshes and textures (shared, so the GPU copy is the same)
        /// with the riding clips in place of its locomotion: <c>chi</c> at rest, <c>wlk</c>, <c>run</c>, <c>mvb</c>, and the
        /// riding routines (<c>chor</c>, <c>coff</c>, <c>cdam</c>) over its own.
        /// </summary>
        public static EntityModel BuildRider(EntityModel character, IReadOnlyList<AnimationClip> clips, IReadOnlyList<RawMotionRoutine> routines)
        {
            var rider = new EntityModel
            {
                Name = character.Name + "_Rider",
                Skeleton = character.Skeleton,
                ParentOverrides = character.ParentOverrides,
                RangedType = character.RangedType,
                DefaultHiddenWeaponSlots = character.DefaultHiddenWeaponSlots,
                IsDualWielding = character.IsDualWielding,
                IsCharacter = character.IsCharacter,
                BlurAura = character.BlurAura,
            };
            rider.AnimatedMeshGroups.AddRange(character.AnimatedMeshGroups);
            foreach (var (name, texture) in character.Textures) rider.Textures[name] = texture;
            foreach (var (name, clip) in character.Animations)
            {
                if (!IsFootMotion(name)) rider.Animations[name] = clip;
            }
            foreach (var clip in clips) rider.Animations[clip.Name] = clip;
            if (rider.Animations.TryGetValue("chi", out var idle)) rider.Animations["idl"] = idle;
            foreach (var (name, routine) in character.RawMotionRoutines) rider.RawMotionRoutines[name] = routine;
            foreach (var routine in routines) rider.RawMotionRoutines[routine.Name] = routine;
            rider.UpdateBounds();
            rider.RebuildMotionRoutines();
            return rider;
        }

        /// <summary>
        /// The Hume male riding pack's seated hip height (<c>ROM/90/14</c> <c>chi</c>, first frame, yalms): the race whose mount
        /// is drawn at scale 1, its pack fitting the chocobo's saddle as authored (hips 0.34 yalm over the chocobo's
        /// reference 1).
        /// </summary>
        public const float ReferenceRiderHips = 1.7802f;

        /// <summary>The chocobo the riding packs are authored on (a rental chocobo).</summary>
        public static readonly MountAppearance RidingChocobo = MountAppearance.FromWire(MountAppearance.StatusChocobo, 0, 1);

        /// <summary>
        /// How much the chocobo is scaled under this rider (#87, round 2: retail scales the mount by the rider's race,
        /// image-9.png and the maintainer's video of 2026-10-23). Every race's chocobo riding pack is authored for a chocobo
        /// scaled to it: its seated hips sit at s x (the chocobo's reference 1 + 0.34 yalm) through <c>chi</c>, <c>run</c> and
        /// <c>wlk</c>, with s = the pack's hip height over the Hume male's: Hume female 0.995, Elvaan male 1.233, Elvaan female
        /// 1.216, Tarutaru 0.955, Mithra 0.982, Galka 1.298 (read from the retail packs, 2026-10-22). The chocobo race has no
        /// per-race sizes of its own (its Info scale bytes read 100 / 125 / 100 / 100). Other mounts carry theirs
        /// (<see cref="MountSeating"/>). **Beyond xi-tools.** *Inference*: that the client scales the chocobo by this ratio.
        /// </summary>
        public static float MountScale(EntityModel rider)
        {
            if (rider.Skeleton is not { Count: > 2 } skeleton || !rider.Animations.TryGetValue("chi", out var seated)) return 1f;
            float hips = -SkeletonPoseEvaluator.EvaluatePose(skeleton, seated, 0f, true).Translations[2].Y;
            return hips > 0.1f ? hips / ReferenceRiderHips : 1f;
        }

        /// <summary>The chocobo's seat height (reference 1 in its idle, yalms), for a straddling rider's name plate.</summary>
        public const float ChocoboRestSeatHeight = 1.44f;

        /// <summary>The chocobo's body joint reference (r1), under the straddling rider's seat.</summary>
        public const int ChocoboSeatReference = 1;

        /// <summary>The position of skeleton reference <paramref name="reference"/> of <paramref name="model"/> in model space (Y down, unscaled) for <paramref name="pose"/>.</summary>
        public static System.Numerics.Vector3 ReferencePoint(EntityModel model, in SkeletonPoseEvaluator.EvaluatedPose pose, int reference) =>
            Gordian.Core.Graphics.ActorEffectInstance.ResolveJointReference(model.Skeleton, pose.Translations, pose.Rotations, pose.Scales, reference);

        /// <summary>Whether <paramref name="model"/> carries skeleton reference <paramref name="reference"/> on a joint (a mount's seat references r48-r55).</summary>
        public static bool HasReference(EntityModel model, int reference) =>
            model.Skeleton is { } skeleton && reference >= 0 && reference < skeleton.References.Count
            && (skeleton.References[reference].Index != 0 || skeleton.References[reference].Offset != System.Numerics.Vector3.Zero);

        /// <summary>
        /// The rider's own seat: skeleton reference 107, on the hip joint, below the hips by each race's own amount (Tarutaru
        /// 0.08, Hume male 0.15, Elvaan male 0.19, Galka 0.29 + its pose; about 0.05-0.1 above the bottom of the seated body
        /// in every pose). It is what goes on the mount's seat reference (#87, round 6: with the hips there, every rider sank
        /// slightly, backsides into the raptor and feet into the Magic Pot's lid). *Inference* from the data: no reference
        /// names r107.
        /// </summary>
        public const int RiderSeatReference = 107;

        /// <summary>
        /// Where the rider's seat (<see cref="RiderSeatReference"/>, else the hips, joint 2) is in its idle's first frame
        /// (model space, Y down): what sits on the mount's seat reference (<see cref="MountSeating.SeatReference"/>). Zero
        /// without a skeleton or an idle.
        /// </summary>
        public static System.Numerics.Vector3 RiderSeat(EntityModel rider)
        {
            if (rider.Skeleton is not { Count: > 2 } skeleton || !rider.Animations.TryGetValue("chi", out var seated)) return System.Numerics.Vector3.Zero;
            var pose = SkeletonPoseEvaluator.EvaluatePose(skeleton, seated, 0f, true);
            return HasReference(rider, RiderSeatReference) ? ReferencePoint(rider, pose, RiderSeatReference) : pose.Translations[2];
        }

        /// <summary>
        /// How far the rider's highest joint (its head) is raised in its seated idle's first frame over where it stands in the
        /// bind pose (#87 round 9). A straddling rider's name plate goes up by this much, so it keeps the gap over the head it has
        /// on foot. From the retail packs: Hume 0.73, Mithra 0.76, Elvaan male 0.96, Galka 1.14, Tarutaru 1.40 (the
        /// Tarutaru's head rises most, its standing head being lowest). Zero without a skeleton or an idle.
        /// </summary>
        public static float SeatedHeadRise(EntityModel rider)
        {
            if (rider.Skeleton is not { Count: > 2 } skeleton || !rider.Animations.TryGetValue("chi", out var seated)) return 0f;
            static float Top(SkeletonPoseEvaluator.EvaluatedPose pose)
            {
                float top = float.MinValue;
                foreach (var t in pose.Translations) top = MathF.Max(top, -t.Y);
                return top;
            }
            return Top(SkeletonPoseEvaluator.EvaluatePose(skeleton, seated, 0f, true)) - Top(SkeletonPoseEvaluator.ComputeBindPose(skeleton));
        }

        private static bool IsFootMotion(string name)
        {
            foreach (var stem in FootMotions)
            {
                if (name.Length >= stem.Length && name.StartsWith(stem, StringComparison.OrdinalIgnoreCase)
                    && (name.Length == stem.Length || char.IsDigit(name[stem.Length]))) return true;
            }
            return false;
        }
    }
}
