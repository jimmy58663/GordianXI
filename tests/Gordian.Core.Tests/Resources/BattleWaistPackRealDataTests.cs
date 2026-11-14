// tests/Gordian.Core.Tests/Resources/BattleWaistPackRealDataTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Tests.Resources
{
    /// <summary>
    /// #546 round 6: each battle pack's waist pack (<see cref="CharacterEquipmentResolver.GetBattleWaistPackPath"/>) holds
    /// the whole-body reaction poses <c>gdi6</c> (guard and block) and <c>pyi6</c> (parry), and its draw waist parts match
    /// the pack's draws. Its skirt parts (<c>btl2</c>, <c>at02</c>...) drive the battle clips' skirt joints, from the second
    /// waist set under long skirts and robes. Skipped without the game install.
    /// </summary>
    public class BattleWaistPackRealDataTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        private static readonly CharacterRace[] Races =
        [
            CharacterRace.HumeMale, CharacterRace.HumeFemale, CharacterRace.ElvaanMale, CharacterRace.ElvaanFemale,
            CharacterRace.TaruMale, CharacterRace.Mithra, CharacterRace.Galka,
        ];

        private static HashSet<string> Clips(ResourceManager rm, string path) =>
            rm.LoadDatBytes(path) is { Length: > 0 } bytes
                ? EntityModelLoader.ParseDatContainer(bytes, path).Animations.Select(a => a.Name).ToHashSet(StringComparer.Ordinal)
                : [];

        /// <summary>
        /// Every race's battle packs: the waist pack (or, on Tarutaru, the pack itself) has the guard pose <c>gdi6</c>, and the
        /// waist pack's draw parts (<c>in?2</c>) are the pack's draws (<c>in?1</c>), so it is that pack's. The parry pose
        /// <c>pyi6</c> is there for every pack but a few that have no parry of their own (listed in the output).
        /// </summary>
        [Fact]
        public void EveryBattlePack_HasItsWaistPack_WithTheReactionPoses()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var failures = new List<string>();
            int checkedPacks = 0, withParry = 0;
            foreach (var race in Races)
            {
                foreach (var pack in Enumerable.Range(0, 16).Select(t => (Type: t, Path: CharacterEquipmentResolver.GetBattlePackPath(race, t))).DistinctBy(p => p.Path))
                {
                    var packClips = Clips(rm, pack.Path);
                    string waistPath = CharacterEquipmentResolver.GetBattleWaistPackPath(race, pack.Type);
                    var waistClips = Clips(rm, waistPath);
                    checkedPacks++;
                    bool own = packClips.Contains("gdi6");
                    if (!own && !waistClips.Contains("gdi6")) failures.Add($"{race} {pack.Path} -> {waistPath}: no gdi6");
                    if (packClips.Contains("pyi6") || waistClips.Contains("pyi6")) withParry++;
                    if (own) continue;
                    var draws = packClips.Where(n => n.Length == 4 && n.StartsWith("in", StringComparison.Ordinal) && n[3] == '1').Select(n => n[..3]).ToHashSet();
                    var waistDraws = waistClips.Where(n => n.Length == 4 && n.StartsWith("in", StringComparison.Ordinal) && n[3] == '2').Select(n => n[..3]).ToHashSet();
                    if (!draws.SetEquals(waistDraws)) failures.Add($"{race} {pack.Path} -> {waistPath}: draws {string.Join(",", draws)} vs {string.Join(",", waistDraws)}");
                }
            }
            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
            Assert.True(checkedPacks >= 60, $"{checkedPacks} packs");
            Assert.True(withParry >= checkedPacks - 4, $"{withParry} of {checkedPacks} packs have a parry pose");
        }

        private static ResourceManager? Retail()
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            return rm;
        }

        private static List<AnimationClip> ClipList(ResourceManager rm, string path) =>
            rm.LoadDatBytes(path) is { Length: > 0 } bytes ? EntityModelLoader.ParseDatContainer(bytes, path).Animations : [];

        /// <summary>
        /// Every waist pack, of a single-weapon battle pack or a dual-wield main-hand pack, has a second set
        /// (<c>secondWaistSet</c>) that keys the same joints and differs from the first on skirt tracks (but on Tarutaru, where the two are the same). It holds the first's
        /// clips, the stance's skirt part <c>btl2</c> among them, but for three packs that leave some out (the loader takes
        /// those from the first set): Hume male <c>ROM/32/31</c> (<c>inb2</c>, <c>otb2</c>), the Kunai <c>98/57</c>
        /// (<c>pyi6</c>) and Mithra <c>56/33</c> (<c>gdi6</c>, <c>dfi6</c>, <c>dbi6</c>).
        /// </summary>
        [Fact]
        public void EveryWaistPack_HasASecondSet_WithTheSameSkirtParts()
        {
            var rm = Retail();
            if (rm == null) return;
            var failures = new List<string>();
            var lacking = new List<string>();
            var same = new List<string>();
            int pairs = 0, differing = 0;
            void Check(string label, string first, string second)
            {
                var a = ClipList(rm, first);
                var b = ClipList(rm, second);
                if (a.Count == 0 || b.Count == 0) { failures.Add($"{label}: {first} / {second} missing"); return; }
                pairs++;
                var names = a.Select(c => c.Name).ToHashSet();
                var bNames = b.Select(c => c.Name).ToHashSet();
                if (bNames.Except(names).Any()) failures.Add($"{label}: {second} has clips {first} lacks");
                if (names.Except(bNames).Any()) lacking.Add($"{second} lacks {string.Join(",", names.Except(bNames).Order())}");
                if (!names.Contains("btl2") || !bNames.Contains("btl2")) failures.Add($"{label}: no btl2");
                bool differs = false;
                foreach (var clip in a.Where(c => c.Name.EndsWith('2')))
                {
                    var other = b.FirstOrDefault(c => c.Name == clip.Name);
                    if (other == null) continue;
                    if (!clip.Tracks.Keys.ToHashSet().SetEquals(other.Tracks.Keys)) failures.Add($"{label}: {clip.Name} keys other joints in {second}");
                    differs |= clip.Tracks.Any(t => other.Tracks.TryGetValue(t.Key, out var o) && !t.Value.Rotations.SequenceEqual(o.Rotations));
                }
                if (differs) differing++; else same.Add(label);
            }
            foreach (var race in Races)
            {
                foreach (var pack in Enumerable.Range(0, 16).Select(t => (Type: t, Path: CharacterEquipmentResolver.GetBattlePackPath(race, t))).DistinctBy(p => p.Path))
                {
                    if (Clips(rm, pack.Path).Contains("gdi6")) continue; // Tarutaru: the pack carries its poses itself
                    Check($"{race} {pack.Path}", CharacterEquipmentResolver.GetBattleWaistPackPath(race, pack.Type),
                        CharacterEquipmentResolver.GetBattleWaistPackPath(race, pack.Type, secondWaistSet: true));
                }
                for (int type = 0; type < 16; type++)
                {
                    string first = CharacterEquipmentResolver.GetDualWieldWaistPackPath(race, type);
                    if (first.Length == 0) continue;
                    Check($"{race} dual wield type {type}", first, CharacterEquipmentResolver.GetDualWieldWaistPackPath(race, type, secondWaistSet: true));
                }
            }
            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
            Assert.True(pairs >= 60, $"{pairs} waist pack pairs");
            // Tarutaru robes and tunics share one skirt motion: their two sets are identical.
            Assert.True(same.All(l => l.StartsWith("TaruMale", StringComparison.Ordinal)), $"{differing} of {pairs} second sets differ from the first; same: {string.Join("; ", same)}");
            Assert.True(differing >= 70, $"{differing} of {pairs} second sets differ from the first");
            Assert.Equal(
                [Path.Combine("ROM", "32", "31.DAT") + " lacks inb2,otb2", Path.Combine("ROM", "56", "33.DAT") + " lacks dbi6,dfi6,gdi6", Path.Combine("ROM", "98", "57.DAT") + " lacks pyi6"],
                lacking.Order().ToArray());
        }

        /// <summary>
        /// The Hume male body DATs with the longest skirts (whose skirt vertices reach lowest in the bind pose) all take the
        /// second waist set (Info byte 9 = 2), and the bodies split about evenly between the two sets.
        /// </summary>
        [Fact]
        public void TheLongestSkirts_TakeTheSecondWaistSet()
        {
            var rm = Retail();
            if (rm == null) return;
            var model = EntityModelLoader.AssembleCharacter(CharacterRace.HumeMale, 1, new ushort[9], rm.LoadDatBytes, rm.LoadDatBytesByFileId)!;
            var bind = SkeletonPoseEvaluator.ComputeBindPose(model.Skeleton!);
            var hems = new List<(int Id, bool Second, float Hem)>();
            int first = 0, second = 0;
            for (int id = 1; id < 1024; id++)
            {
                if (!CharacterEquipmentResolver.TryResolveGearFileId(CharacterRace.HumeMale, CharacterSlot.Body, (ushort)id, out int fid)) continue;
                if (rm.LoadDatBytesByFileId(fid) is not { Length: > 0 } dat) continue;
                bool uses = CharacterEquipmentResolver.UsesSecondWaistSet(dat);
                if (uses) second++; else first++;
                var skirt = EntityModelLoader.ParseDatContainer(dat, "body").Meshes.SelectMany(m => m.Vertices).Where(IsSkirt).ToList();
                if (skirt.Count > 0) hems.Add((id, uses, skirt.Max(v => SkeletonPoseEvaluator.SkinVertex(v, bind).Position.Y))); // +Y is down
            }
            Assert.True(first > 200 && second > 200, $"{first} bodies on the first set, {second} on the second");
            var longest = hems.OrderByDescending(h => h.Hem).Take(30).ToList();
            Assert.True(longest.All(h => h.Second), "first-set bodies among the longest skirts: " + string.Join(",", longest.Where(h => !h.Second).Select(h => h.Id)));
        }

        /// <summary>
        /// Every race's locomotion waist pack has its second set at the next file (Hume male <c>ROM/27/85</c> / <c>27/86</c>):
        /// both hold the idle's skirt part <c>idl2</c> on the same joints.
        /// </summary>
        [Fact]
        public void EveryLocomotionWaistPack_HasASecondSet()
        {
            var rm = Retail();
            if (rm == null) return;
            foreach (var race in Races)
            {
                var first = ClipList(rm, CharacterEquipmentResolver.GetLocomotionPackPaths(race).Waist).SingleOrDefault(c => c.Name == "idl2");
                var second = ClipList(rm, CharacterEquipmentResolver.GetLocomotionPackPaths(race, secondWaistSet: true).Waist).SingleOrDefault(c => c.Name == "idl2");
                Assert.True(first != null && second != null, $"{race}: idl2 missing");
                Assert.True(first!.Tracks.Keys.ToHashSet().SetEquals(second!.Tracks.Keys), $"{race}: the two sets key different joints");
            }
        }

        private static bool IsSkirt(SkinnedVertex v) => v.Joint0 is >= 38 and <= 47 || v.Joint1 is >= 38 and <= 47;

        private static EntityModel HumeMaleWithSword(ResourceManager rm, ushort body)
        {
            var grap = new ushort[9];
            grap[2] = body;
            grap[6] = 5; // sword: battle pack ROM/32/14, waist packs ROM/32/23 and 32/32
            return EntityModelLoader.AssembleCharacter(CharacterRace.HumeMale, 1, grap, rm.LoadDatBytes, rm.LoadDatBytesByFileId)!;
        }

        /// <summary>
        /// Hume male with a sword: the engaged stance <c>btl</c> takes its skirt joints (38-47) from the waist pack's
        /// <c>btl2</c>, the swings from <c>at02</c> / <c>at12</c> and the draw from <c>inb2</c>, not from the idle: the first
        /// set (<c>ROM/32/23</c>) under a short tabard (body 1, Info byte 9 = 1), the second (<c>ROM/32/32</c>) under a robe
        /// (body 338, byte 9 = 2).
        /// </summary>
        [Theory]
        [InlineData((ushort)1, "23")]
        [InlineData((ushort)338, "32")]
        public void TheEngagedStance_TakesItsSkirtFromBtl2(ushort body, string waistFile)
        {
            var rm = Retail();
            if (rm == null) return;
            var model = HumeMaleWithSword(rm, body);
            var waist = ClipList(rm, Path.Combine("ROM", "32", $"{waistFile}.DAT"));
            foreach (var (merged, part) in new[] { ("btl", "btl2"), ("at0", "at02"), ("at1", "at12"), ("inb", "inb2") })
            {
                var clip = model.Animations[merged];
                var source = waist.Single(c => c.Name == part);
                Assert.Equal(source.NumFrames, clip.NumFrames);
                for (int joint = 38; joint <= 47; joint++)
                {
                    Assert.True(clip.Tracks[joint].Rotations.SequenceEqual(source.Tracks[joint].Rotations), $"{merged} joint {joint} is not {part}'s");
                }
            }
            Assert.DoesNotContain("btl2", model.Animations.Keys);
        }

        /// <summary>
        /// A CPU-skinned check on robes (Hume male bodies 338, 415, 308, 261, with a sword): the share of the bare legs'
        /// vertices that stick out past the robe, through the engaged stance, the three swings and the sheathe. With the
        /// second set's skirt parts (as loaded) fewer stick out than with the first set's, and in the two main swings fewer
        /// than with the idle's skirt tracks (what the battle clips carried before). Measured 2026-11-13: stance 0.43-0.58
        /// (first set 0.58-0.69), swing <c>at0</c> 0.46-0.50 (idle skirt 0.58-0.64).
        /// </summary>
        [Theory]
        [InlineData((ushort)338)]
        [InlineData((ushort)415)]
        [InlineData((ushort)308)]
        [InlineData((ushort)261)]
        public void ARobe_FollowsTheLegsThroughTheSwings(ushort body)
        {
            var rm = Retail();
            if (rm == null) return;
            var model = HumeMaleWithSword(rm, body);
            Assert.True(CharacterEquipmentResolver.TryResolveGearFileId(CharacterRace.HumeMale, CharacterSlot.Body, body, out int bodyFid));
            Assert.True(CharacterEquipmentResolver.TryResolveGearFileId(CharacterRace.HumeMale, CharacterSlot.Legs, 0, out int legsFid));
            var skirt = EntityModelLoader.ParseDatContainer(rm.LoadDatBytesByFileId(bodyFid)!, "body").Meshes.SelectMany(m => m.Vertices).Where(IsSkirt).ToArray();
            var legs = EntityModelLoader.ParseDatContainer(rm.LoadDatBytesByFileId(legsFid)!, "legs").Meshes.SelectMany(m => m.Vertices).ToArray();
            var firstSet = ClipList(rm, Path.Combine("ROM", "32", "23.DAT"));
            var idle = model.Animations["idl"];

            AnimationClip WithSkirt(AnimationClip clip, Func<int, BoneAnimationTrack?> track)
            {
                var tracks = new Dictionary<int, BoneAnimationTrack>(clip.Tracks);
                for (int joint = 38; joint <= 47; joint++) if (track(joint) is { } t) tracks[joint] = t;
                return new AnimationClip { Name = clip.Name, NumFrames = clip.NumFrames, KeyFrameDuration = clip.KeyFrameDuration, Tracks = tracks };
            }

            float PokeThrough(AnimationClip clip)
            {
                float sum = 0;
                for (int k = 0; k <= 10; k++)
                {
                    var pose = SkeletonPoseEvaluator.EvaluatePose(model.Skeleton!, clip, clip.DurationSeconds * k / 10f, false, model.ParentOverrides);
                    sum += LegsOutsideSkirt(pose, skirt, legs);
                }
                return sum / 11;
            }

            float loaded = 0, first = 0;
            foreach (var name in new[] { "btl", "at0", "at1", "at2", "otb" })
            {
                var clip = model.Animations[name];
                var part = firstSet.Single(c => c.Name == name + "2");
                loaded += PokeThrough(clip);
                first += PokeThrough(WithSkirt(clip, j => part.Tracks.GetValueOrDefault(j)));
            }
            Assert.True(loaded < first - 0.25f, $"legs outside the robe: {loaded / 5:0.000} with the second set, {first / 5:0.000} with the first");

            foreach (var name in new[] { "at0", "at1" })
            {
                var clip = model.Animations[name];
                float loadedSwing = PokeThrough(clip);
                float withIdle = PokeThrough(WithSkirt(clip, j => idle.Tracks.GetValueOrDefault(j)));
                Assert.True(loadedSwing < withIdle - 0.03f, $"{name}: {loadedSwing:0.000} with the waist pack's skirt, {withIdle:0.000} with the idle's");
            }
        }

        /// <summary>
        /// The share of leg vertices, within the skirt's height (below its top 15%), that lie farther from the root joint's
        /// vertical axis than the skirt reaches in the same height and angle bin (6 x 16 bins).
        /// </summary>
        private static float LegsOutsideSkirt(SkeletonPoseEvaluator.EvaluatedPose pose, SkinnedVertex[] skirt, SkinnedVertex[] legs)
        {
            var skirtPoints = skirt.Select(v => SkeletonPoseEvaluator.SkinVertex(v, pose).Position).ToArray();
            var centre = pose.Translations[0];
            float top = skirtPoints.Min(q => q.Y), bottom = skirtPoints.Max(q => q.Y);
            const int HeightBins = 6, AngleBins = 16;
            var radius = new float[HeightBins, AngleBins];
            int HeightBin(float y) => Math.Clamp((int)((y - top) / (bottom - top) * HeightBins), 0, HeightBins - 1);
            int AngleBin(Vector3 q) => (int)((MathF.Atan2(q.Z - centre.Z, q.X - centre.X) + MathF.PI) / (2 * MathF.PI) * AngleBins) % AngleBins;
            float Radius(Vector3 q) => new Vector2(q.X - centre.X, q.Z - centre.Z).Length();
            foreach (var q in skirtPoints)
            {
                int h = HeightBin(q.Y), a = AngleBin(q);
                radius[h, a] = Math.Max(radius[h, a], Radius(q));
            }
            int inRange = 0, outside = 0;
            foreach (var v in legs)
            {
                var q = SkeletonPoseEvaluator.SkinVertex(v, pose).Position;
                if (q.Y < top + 0.15f * (bottom - top) || q.Y > bottom) continue;
                inRange++;
                if (Radius(q) > radius[HeightBin(q.Y), AngleBin(q)] + 0.01f) outside++;
            }
            return inRange == 0 ? 0 : (float)outside / inRange;
        }
    }
}
