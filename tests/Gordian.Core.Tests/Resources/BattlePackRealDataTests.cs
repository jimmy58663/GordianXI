// tests/Gordian.Core.Tests/Resources/BattlePackRealDataTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>
    /// The PC battle packs on the retail data (#136, #138): which pack is hand-to-hand, its off-hand and kick swings, and the
    /// direction of the draw / sheathe clips. Skipped without the game install.
    /// </summary>
    public class BattlePackRealDataTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private readonly ITestOutputHelper _output;

        public BattlePackRealDataTests(ITestOutputHelper output) => _output = output;

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
            CharacterRace.TaruMale, CharacterRace.TaruFemale, CharacterRace.Mithra, CharacterRace.Galka
        };

        private static int InfoByte3(byte[] dat)
        {
            foreach (var h in DatSectionWalker.ReadHeaders(dat))
            {
                if (h.TypeCode == DatSectionType.Info) return dat[h.DataOffset + 3];
            }
            return -1;
        }

        /// <summary>
        /// Unarmed uses the race's hand-to-hand type: the type the hand-to-hand weapons carry (model 1, a cesti look), and the
        /// one pack of the race with the off-hand and kick swings.
        /// </summary>
        [Theory]
        [MemberData(nameof(Races))]
        public void UnarmedType_IsTheHandToHandPack_WithOffHandAndKickSwings(CharacterRace race)
        {
            var rm = Retail();
            if (rm == null) return;
            int unarmed = CharacterEquipmentResolver.GetUnarmedWeaponType(race);

            Assert.True(CharacterEquipmentResolver.TryResolveGearFileId(race, CharacterSlot.Main, 1, out int h2hFile));
            Assert.Equal(unarmed, InfoByte3(rm.LoadDatBytesByFileId(h2hFile)!));

            var routines = EntityModelLoader.ParseDatContainer(rm.LoadDatBytes(CharacterEquipmentResolver.GetBattlePackPath(race, unarmed))!, "pack")
                .Routines!.Select(r => r.Name).ToHashSet();
            Assert.Contains("bti0", routines);
            Assert.Contains("cti0", routines);
            Assert.Contains("dti0", routines);
        }

        private static EntityModel Assemble(ResourceManager rm, CharacterRace race, ushort main)
        {
            var grap = new ushort[9];
            grap[6] = main;
            return EntityModelLoader.AssembleCharacter(race, 1, grap, rm.LoadDatBytes, rm.LoadDatBytesByFileId)!;
        }

        /// <summary>
        /// Hume male hand-to-hand: the off-hand swings are led by the left hand (joint reference 126), the kicks by a foot
        /// (right foot joint 31 for <c>cti0</c>, left foot joint 37 for <c>dti0</c>), each measured as the farthest a joint
        /// travels from the battle stance.
        /// </summary>
        [Fact]
        public void HumeMaleHandToHand_OffHandAndKickSwings_MoveTheLeftHandAndTheFeet()
        {
            var rm = Retail();
            if (rm == null) return;
            var model = Assemble(rm, CharacterRace.HumeMale, 1);
            var skeleton = model.Skeleton!;
            int rightHand = skeleton.References[127].Index, leftHand = skeleton.References[126].Index;
            const int rightFoot = 31, leftFoot = 37;
            var stance = SkeletonPoseEvaluator.EvaluatePose(skeleton, model.Animations["btl"], 0f, true);

            float Travel(string routine, int joint)
            {
                var clip = model.Animations[model.MotionRoutines[routine].Segments[0].ClipName];
                float max = 0f;
                for (int i = 0; i <= 40; i++)
                {
                    var pose = SkeletonPoseEvaluator.EvaluatePose(skeleton, clip, clip.DurationSeconds * i / 40f, false);
                    max = Math.Max(max, Vector3.Distance(pose.Translations[joint], stance.Translations[joint]));
                }
                return max;
            }

            foreach (var routine in new[] { "ati0", "bti0", "bti1", "cti0", "dti0" })
            {
                _output.WriteLine($"{routine}: right hand {Travel(routine, rightHand):F2}, left hand {Travel(routine, leftHand):F2}, " +
                    $"right foot {Travel(routine, rightFoot):F2}, left foot {Travel(routine, leftFoot):F2}");
            }
            Assert.True(Travel("ati0", rightHand) > Travel("ati0", leftHand));
            Assert.True(Travel("bti0", leftHand) > Travel("bti0", rightHand));
            Assert.True(Travel("bti1", leftHand) > Travel("bti1", rightHand));
            Assert.True(Travel("cti0", rightFoot) > 1.5f && Travel("cti0", leftFoot) < 0.5f);
            Assert.True(Travel("dti0", leftFoot) > 0.8f && Travel("dti0", rightFoot) < 0.5f);
        }

        /// <summary>
        /// <c>in 0</c> goes from the idle stance to the battle stance and <c>out0</c> back (the draw is "in", #136), and both
        /// key the weapon's grip joint, carrying the weapon between its mount and the hand. Hume male unarmed, sword (4),
        /// scythe (10), great katana (14); Elvaan male and Galka sword.
        /// </summary>
        [Theory]
        [InlineData(CharacterRace.HumeMale, 0)]
        [InlineData(CharacterRace.HumeMale, 4)]
        [InlineData(CharacterRace.HumeMale, 10)]
        [InlineData(CharacterRace.HumeMale, 14)]
        [InlineData(CharacterRace.ElvaanMale, 4)]
        [InlineData(CharacterRace.Galka, 4)]
        public void DrawIsIn0_FromIdleToBattle_AndCarriesTheWeapon(CharacterRace race, ushort main)
        {
            var rm = Retail();
            if (rm == null) return;
            var model = Assemble(rm, race, main);
            var skeleton = model.Skeleton!;
            SkeletonPoseEvaluator.EvaluatedPose Pose(string clip, float t) => SkeletonPoseEvaluator.EvaluatePose(skeleton, model.Animations[clip], t, false);
            float Distance(SkeletonPoseEvaluator.EvaluatedPose a, SkeletonPoseEvaluator.EvaluatedPose b)
            {
                float sum = 0f;
                for (int j = 1; j < skeleton.Count; j++) sum += Vector3.Distance(a.Translations[j], b.Translations[j]);
                return sum;
            }
            var battle = Pose("btl", 0f);
            var idle = Pose("idl", 0f);

            foreach (var name in new[] { "in 0", "out0" })
            {
                var clip = model.Animations[model.MotionRoutines[name].Segments[0].ClipName];
                var first = Pose(clip.Name, 0f);
                var last = Pose(clip.Name, clip.DurationSeconds);
                _output.WriteLine($"{race} main {main} {name} ({clip.Name}): first~btl {Distance(first, battle):F1} first~idl {Distance(first, idle):F1}, " +
                    $"last~btl {Distance(last, battle):F1} last~idl {Distance(last, idle):F1}");
                // The battle end is the robust one (Galka's idl is far from both ends): in 0 ends there, out0 starts there.
                var battleEnd = name == "in 0" ? last : first;
                var otherEnd = name == "in 0" ? first : last;
                Assert.True(Distance(battleEnd, battle) < 0.4f * Distance(otherEnd, battle),
                    $"{name} meets the battle stance at its {(name == "in 0" ? "end" : "start")}, not at its other end");
                foreach (int grip in (model.ParentOverrides ?? new Dictionary<int, int>()).Keys)
                {
                    Assert.True(clip.Tracks.ContainsKey(grip), $"{name} keys the grip joint {grip}");
                }
            }
        }
    }
}
