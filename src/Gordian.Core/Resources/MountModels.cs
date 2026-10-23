// src/Gordian.Core/Resources/MountModels.cs
using System;
using System.Collections.Generic;
using System.IO;
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
    /// it (<see cref="MountScale"/>). On another mount the rider moves with that mount's seat (skeleton reference 1) in place
    /// of the chocobo's (<see cref="ChocoboSeat"/>; **Beyond xi-tools**, which does not say how the rider is seated). Every
    /// mount uses the same riding pack.</item>
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

        /// <summary>The chocobo's seat height (reference 1 in its idle, yalms), for the name plate over another mount.</summary>
        public const float ChocoboRestSeatHeight = 1.44f;

        /// <summary>The skeleton reference the rider sits on: reference 1, the body joint of the chocobo and of every mount.</summary>
        public const int SeatReference = 1;

        /// <summary>
        /// How much the mount is scaled under this rider (#87, round 2: retail scales the mount by the rider's race,
        /// image-9.png and the maintainer's video of 2026-10-23). Every race's riding pack is authored for a chocobo scaled to
        /// it: its seated hips sit at s x (the chocobo's reference 1 + 0.34 yalm) through <c>chi</c>, <c>run</c> and
        /// <c>wlk</c>, with s = the pack's hip height over the Hume male's: Hume female 0.995, Elvaan male 1.233, Elvaan female
        /// 1.216, Tarutaru 0.955, Mithra 0.982, Galka 1.298 (read from the retail packs, 2026-10-22). The video's raptors
        /// measure the same ratios against the Mithra's (Galka and Elvaan about 1.25, Tarutaru 0.96). **Beyond xi-tools.**
        /// *Inference*: that the client scales by this ratio rather than a table of its own.
        /// </summary>
        public static float MountScale(EntityModel rider)
        {
            if (rider.Skeleton is not { Count: > 2 } skeleton || !rider.Animations.TryGetValue("chi", out var seated)) return 1f;
            float hips = -SkeletonPoseEvaluator.EvaluatePose(skeleton, seated, 0f, true).Translations[2].Y;
            return hips > 0.1f ? hips / ReferenceRiderHips : 1f;
        }

        /// <summary>
        /// The mount's own size against the chocobo the riding packs are authored on, applied on top of the rider's
        /// <see cref="MountScale"/> (#87, round 3: "mount sizing is too large", while the chocobo under an Elvaan looked
        /// right). Measured from the maintainer's retail shots of every race riding side by side (2026-10-23):
        /// <list type="bullet">
        /// <item>The raptor (ids 1 and 30) is 0.85. On image-12.png the rider's head stands 1.27 (Galka), 1.22 (Elvaan,
        /// Hume), 1.18 (Hume female) times its raptor's top over the ground; these fit 0.84-0.86 of the race scale for every
        /// race (the Tarutaru 0.96, under a hat).</item>
        /// <item>Fenrir (id 9) is 0.65. On image-13.png, against the Galka's seated hips-to-head (1.06 yalms) as the ruler, the
        /// Fenrir under it stands 2.23 yalms to its spikes and its back meets the Galka's hips at about 1.9: 0.61 of the race
        /// scale; the Elvaan's gives 0.70.</item>
        /// <item>The chocobo and every mount not measured are 1 (PROVISIONAL for the others).</item>
        /// </list>
        /// </summary>
        public static float MountBaseScale(in MountAppearance mount)
        {
            if (mount.RidesChocobo) return 1f;
            return mount.MountId switch
            {
                1 or 30 => RaptorBaseScale,
                9 => FenrirBaseScale,
                _ => 1f,
            };
        }

        /// <summary>The raptor's and Fenrir's size against the riding chocobo (<see cref="MountBaseScale"/>).</summary>
        public const float RaptorBaseScale = 0.85f, FenrirBaseScale = 0.65f;

        /// <summary>
        /// How far the rider's seat is moved along the mount from its seat reference (model units, + forward), for a mount
        /// whose reference 1 is not under the riding spot (#87, round 3 H15). Fenrir's back is flat at 1.80-1.86 from 1.0 to
        /// 0.0 behind its reference and climbs to 2.45-2.5 over the shoulders from 0.4 ahead, where the pack's 0.27 put the
        /// riders (too high and too far forward against image-13.png, where the Galka sits mid-back): 0.6 back. Others: 0.
        /// </summary>
        public static float SeatShift(in MountAppearance mount) => !mount.RidesChocobo && mount.MountId == 9 ? FenrirSeatShift : 0f;

        /// <summary>Fenrir's seat shift (<see cref="SeatShift"/>).</summary>
        public const float FenrirSeatShift = -0.6f;

        /// <summary>
        /// The seat point (<see cref="SeatReference"/>) of <paramref name="model"/> in its model space (Y down, unscaled) for
        /// <paramref name="pose"/>.
        /// </summary>
        public static System.Numerics.Vector3 SeatPoint(EntityModel model, in SkeletonPoseEvaluator.EvaluatedPose pose) =>
            Gordian.Core.Graphics.ActorEffectInstance.ResolveJointReference(model.Skeleton, pose.Translations, pose.Rotations, pose.Scales, SeatReference);

        /// <summary>
        /// Where the riding pack expects the chocobo's seat while it plays <paramref name="clip"/> at <paramref name="time"/>:
        /// the chocobo race's own clip of that name (the packs are as long as the chocobo's and move with it) at the same
        /// time; its idle when it has no such clip.
        /// </summary>
        public static System.Numerics.Vector3 ChocoboSeat(EntityModel chocobo, AnimationClip? clip, float time)
        {
            if (chocobo.Skeleton is not { Count: > 0 } skeleton) return System.Numerics.Vector3.Zero;
            AnimationClip? own = null;
            if (clip != null && !chocobo.Animations.TryGetValue(clip.Name, out own)) chocobo.Animations.TryGetValue(clip.Name + "0", out own);
            own ??= chocobo.Animations.TryGetValue("idl", out var idle) ? idle : null;
            var pose = own != null ? SkeletonPoseEvaluator.EvaluatePose(skeleton, own, time, true) : SkeletonPoseEvaluator.ComputeBindPose(skeleton);
            return SeatPoint(chocobo, pose);
        }

        /// <summary>
        /// How far ahead of the seat reference the riding packs put the hips (yalms): the Hume male's <c>chi</c> has them 0.27
        /// ahead of the chocobo's reference 1, and every race's is within 0.1 of that scaled.
        /// </summary>
        public const float SeatForward = 0.27f;

        // The patch searched for the top of a mount's back: forward / back and sideways (yalms).
        private const float BackHalfLength = 0.18f, BackHalfWidth = 0.12f;

        /// <summary>
        /// How far the top of the mount's back stands over its seat reference where the rider sits (<see cref="SeatForward"/>
        /// ahead of it, in its idle's first frame): the chocobo's 0.21, the raptor's 0.28, Fenrir's 1.02 (a thick body over
        /// its hip joint). The rider is raised by the difference from the chocobo's (round 2 H11: on Fenrir the riders sank
        /// into the body). 0 when the patch has no vertex.
        /// </summary>
        public static float BackHeight(EntityModel mount, float seatShift = 0f)
        {
            if (mount.Skeleton is not { Count: > 0 } skeleton) return 0f;
            var pose = mount.Animations.TryGetValue("idl", out var idle)
                ? SkeletonPoseEvaluator.EvaluatePose(skeleton, idle, 0f, true)
                : SkeletonPoseEvaluator.ComputeBindPose(skeleton);
            var seat = SeatPoint(mount, pose);
            float x = seat.X + SeatForward + seatShift;
            float top = float.MinValue;
            foreach (var p in SkinnedPoints(mount, pose))
            {
                if (MathF.Abs(p.X - x) < BackHalfLength && MathF.Abs(p.Z - seat.Z) < BackHalfWidth) top = MathF.Max(top, -p.Y);
            }
            return top == float.MinValue ? 0f : top + seat.Y;
        }

        private static IEnumerable<System.Numerics.Vector3> SkinnedPoints(EntityModel model, SkeletonPoseEvaluator.EvaluatedPose pose)
        {
            foreach (var group in model.AnimatedMeshGroups)
            {
                foreach (var v in group.Vertices)
                {
                    var skinned = new SkinnedVertex
                    {
                        Position0 = v.Position0, Position1 = v.Position1, Normal0 = v.Normal0, Normal1 = v.Normal1,
                        Weight0 = v.Weight0, Weight1 = v.Weight1, Joint0 = v.Joint0, Joint1 = v.Joint1,
                    };
                    yield return SkeletonPoseEvaluator.SkinVertex(skinned, pose).Position;
                }
            }
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
