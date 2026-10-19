// tests/Gordian.Core.Tests/Resources/DualWieldPackRealDataTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Gordian.Core.Animation;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>
    /// The dual-wield motion packs on the retail data (#308): which pack each weapon uses, what the packs hold, and a
    /// dual-wielding character assembled from them. Skipped without the game install.
    /// </summary>
    public class DualWieldPackRealDataTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private readonly ITestOutputHelper _output;

        public DualWieldPackRealDataTests(ITestOutputHelper output) => _output = output;

        private static ResourceManager? Retail()
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            return rm;
        }

        public static TheoryData<CharacterRace> Races => new()
        {
            CharacterRace.HumeMale, CharacterRace.HumeFemale, CharacterRace.ElvaanMale, CharacterRace.ElvaanFemale,
            CharacterRace.TaruMale, CharacterRace.Mithra, CharacterRace.Galka
        };

        /// <summary>The draw clip letter of a routine (<c>in20</c> plays <c>i1a?</c>: 'a'), or '\0'.</summary>
        private static char DrawLetter(Dictionary<string, RawMotionRoutine> routines, string name) =>
            routines.TryGetValue(name, out var r) && r.Commands.FirstOrDefault(c => c.Op == 0x05).Reference is { Length: 4 } clip ? clip[2] : '\0';

        /// <summary>
        /// Every sub weapon with a left-hand swing sound (<c>skal</c>: the weapons that can be dual wielded) has a type
        /// with a dual-wield pack on its race, and that pack is the off-hand one (<c>bti*</c>, <c>in2N</c>) whose draw
        /// carries the weapon's mount: draw clip <c>c</c> for the katana mount 0x7C, <c>b</c> for the club mount 0x7A,
        /// <c>a</c> for the sword and dagger mounts 0x79 / 0x7B. The main-hand pack of the same type holds <c>ati*</c> and
        /// <c>in1N</c> with the matching letter for the main mounts (0x75, 0x72, 0x71 / 0x74).
        /// </summary>
        [Theory]
        [MemberData(nameof(Races))]
        public void EverySubWeapon_HasThePackWhoseDrawCarriesItsMount(CharacterRace race)
        {
            var rm = Retail();
            if (rm == null) return;
            var seen = new HashSet<(byte Type, byte SubType, byte Mount)>();
            for (ushort id = 0; id < 1200; id++)
            {
                if (!CharacterEquipmentResolver.TryResolveGearFileId(race, CharacterSlot.Sub, id, out int fid)) continue;
                var dat = rm.LoadDatBytesByFileId(fid);
                if (dat == null || EntityModelLoader.ReadWeaponInfo(dat) is not { IsHeldWeapon: true } info) continue;
                if (!EntityModelLoader.ParseDatContainer(dat, "sub").Routines.Any(r => r.Name == "skal")) continue;
                seen.Add((info.Type, info.SubType, info.MountReference));
            }
            Assert.NotEmpty(seen);

            foreach (var (type, subType, mount) in seen)
            {
                Assert.True(CharacterEquipmentResolver.TryGetDualWieldPackPaths(race, type, type, out string mainPath, out string subPath),
                    $"{race} sub type {type} has a dual-wield pack");
                var off = EntityModelLoader.ParseDatContainer(rm.LoadDatBytes(subPath)!, "off").Routines.ToDictionary(r => r.Name);
                var main = EntityModelLoader.ParseDatContainer(rm.LoadDatBytes(mainPath)!, "main").Routines.ToDictionary(r => r.Name);
                Assert.Contains(off.Keys, n => n.StartsWith("bti", StringComparison.Ordinal));
                Assert.Contains(main.Keys, n => n.StartsWith("ati", StringComparison.Ordinal));
                Assert.DoesNotContain(off.Keys, n => n.StartsWith("ati", StringComparison.Ordinal));

                char expected = mount switch { 0x7C => 'c', 0x7A => 'b', _ => 'a' };
                char offLetter = DrawLetter(off, "in2" + subType);
                char mainLetter = DrawLetter(main, "in1" + subType);
                _output.WriteLine($"{race} type {type} sub {subType} mount 0x{mount:X2}: {subPath} in2{subType} '{offLetter}', {mainPath} in1{subType} '{mainLetter}'");
                Assert.Equal(expected, offLetter);
            }
        }

        /// <summary>
        /// Hume male with two daggers (look 157, type 3): the dual-wield packs <c>ROM/108/114</c> and <c>ROM/108/102</c>;
        /// off-hand hits play <c>bti*</c>, the draw is the main hand's then the off hand's (36 + 36 ticks), and the
        /// engaged stance holds the sub weapon's mount (reference 0x7B) in the left hand. A shield keeps the dagger pack.
        /// </summary>
        [Fact]
        public void HumeMaleTwoDaggers_UsesTheDualWieldPacks()
        {
            var rm = Retail();
            if (rm == null) return;
            Assert.True(CharacterEquipmentResolver.TryGetDualWieldPackPaths(CharacterRace.HumeMale, 3, 3, out string mainPath, out string subPath));
            Assert.Equal(Path.Combine("ROM", "108", "114.DAT"), mainPath);
            Assert.Equal(Path.Combine("ROM", "108", "102.DAT"), subPath);

            var grap = new ushort[9];
            grap[6] = 157;
            grap[7] = 157;
            var model = EntityModelLoader.AssembleCharacter(CharacterRace.HumeMale, 1, grap, rm.LoadDatBytes, rm.LoadDatBytesByFileId)!;
            Assert.True(model.IsDualWielding);
            Assert.True(model.IsCharacter);
            Assert.Equal("bti", EntityAnimationState.SwingPrefix(model, 1));
            Assert.Equal("ati", EntityAnimationState.SwingPrefix(model, 0));
            Assert.True(model.MotionRoutines.ContainsKey("btf0"));

            var draw = model.MotionRoutines["in 0"];
            Assert.Equal(2, draw.Segments.Count);
            Assert.Equal(0, draw.Segments[0].StartTick);
            Assert.Equal(36, draw.Segments[1].StartTick);
            Assert.StartsWith("i0", draw.Segments[0].ClipName, StringComparison.Ordinal);
            Assert.StartsWith("i1", draw.Segments[1].ClipName, StringComparison.Ordinal);
            var sheathe = model.MotionRoutines["out0"];
            Assert.StartsWith("o0", sheathe.Segments[0].ClipName, StringComparison.Ordinal);
            Assert.StartsWith("o1", sheathe.Segments[1].ClipName, StringComparison.Ordinal);
            // 36 + 36 ticks, no hold after the second hand (round 1, D5: the sheathe held its pose for another 72 ticks).
            Assert.Equal(72, draw.TotalTicks);
            Assert.Equal(72, sheathe.TotalTicks);
            Assert.Equal(72f, EntityAnimationState.OnePassTicks(sheathe));

            // The order chains pose to pose: the sheathe starts at the battle stance with the off hand's o0?, which ends where
            // the main hand's o1? starts (round 1 had them the other way round, D5); the draw's i0? ends where i1? starts.
            var sk = model.Skeleton!;
            float Gap(string a, bool aEnd, string b, bool bEnd)
            {
                var ca = model.Animations[a]; var cb = model.Animations[b];
                var pa = SkeletonPoseEvaluator.EvaluatePose(sk, ca, aEnd ? ca.DurationSeconds : 0f, false);
                var pb = SkeletonPoseEvaluator.EvaluatePose(sk, cb, bEnd ? cb.DurationSeconds : 0f, false);
                float sum = 0f;
                for (int j = 1; j < sk.Count; j++) sum += Vector3.Distance(pa.Translations[j], pb.Translations[j]);
                return sum;
            }
            string o0 = sheathe.Segments[0].ClipName, o1 = sheathe.Segments[1].ClipName, i0 = draw.Segments[0].ClipName, i1 = draw.Segments[1].ClipName;
            _output.WriteLine($"btl~{o0} start {Gap("btl", false, o0, false):F2} (btl~{o1} start {Gap("btl", false, o1, false):F2}); {o0} end~{o1} start {Gap(o0, true, o1, false):F2}; " +
                $"{i0} end~{i1} start {Gap(i0, true, i1, false):F2}; {i1} end~btl {Gap(i1, true, "btl", false):F2}");
            Assert.True(Gap("btl", false, o0, false) < Gap("btl", false, o1, false), "the sheathe starts with the off hand");
            Assert.True(Gap(o0, true, o1, false) < 6.0f, "the main hand's sheathe starts where the off hand's ends");
            Assert.True(Gap(i0, true, i1, false) < 6.0f, "the off hand's draw starts where the main hand's ends");

            // The off-hand draw and swings link the sub weapon's own sounds (sinl, skal).
            Assert.Contains(model.GetRoutineSounds("bti1"), c => c.Choices.Count > 0 && c.Choices[0] > 0);

            var skeleton = model.Skeleton!;
            int leftHand = skeleton.References[126].Index, subMount = skeleton.References[0x7B].Index;
            foreach (var clipName in new[] { "btl", model.MotionRoutines["bti1"].Segments[0].ClipName, model.MotionRoutines["ati0"].Segments[0].ClipName })
            {
                var clip = model.Animations[clipName];
                for (int i = 0; i <= 10; i++)
                {
                    var pose = SkeletonPoseEvaluator.EvaluatePose(skeleton, clip, clip.DurationSeconds * i / 10f, false);
                    Assert.True(Vector3.Distance(pose.Translations[subMount], pose.Translations[leftHand]) < 0.05f,
                        $"{clipName}: the sub weapon's mount is in the left hand at {i}/10");
                }
            }

            grap[7] = 0; // no sub weapon: the dagger pack
            var single = EntityModelLoader.AssembleCharacter(CharacterRace.HumeMale, 1, grap, rm.LoadDatBytes, rm.LoadDatBytesByFileId)!;
            Assert.False(single.IsDualWielding);
            Assert.Equal("ati", EntityAnimationState.SwingPrefix(single, 1));
        }
    }
}
