// tests/Gordian.Core.Tests/Resources/RestPostureRealDataTests.cs
using System.IO;
using System.Linq;
using Gordian.Core.Animation;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>
    /// The /heal and /sit postures (#343) and the first-person eye height (#305) on the retail race skeletons. Skipped
    /// without the game install.
    /// </summary>
    public class RestPostureRealDataTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        private static ResourceManager? Retail()
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            return rm;
        }

        private static EntityModel Assemble(ResourceManager rm, CharacterRace race) =>
            EntityModelLoader.AssembleCharacter(race, 1, new ushort[9], rm.LoadDatBytes, rm.LoadDatBytesByFileId)!;

        public static TheoryData<CharacterRace> Races => new()
        {
            CharacterRace.HumeMale, CharacterRace.HumeFemale, CharacterRace.ElvaanMale, CharacterRace.ElvaanFemale,
            CharacterRace.TaruMale, CharacterRace.TaruFemale, CharacterRace.Mithra, CharacterRace.Galka
        };

        /// <summary>
        /// Every race: <c>res0</c> sits down with <c>rx0</c> (52 ticks) and holds <c>rx1</c>, <c>res2</c> stands up with
        /// <c>rx2</c> (56 ticks); <c>sit0</c> / <c>sit2</c> likewise with <c>si0</c> / <c>si1</c> / <c>si2</c>.
        /// </summary>
        [Theory]
        [MemberData(nameof(Races))]
        public void RaceSkeleton_HasTheRestAndSitRoutines(CharacterRace race)
        {
            var rm = Retail();
            if (rm == null) return;
            var model = Assemble(rm, race);

            var res0 = model.MotionRoutines["res0"];
            Assert.Equal(["rx0", "rx1"], res0.Segments.Select(s => s.ClipName));
            Assert.Equal(52, res0.Segments[0].DurationTicks);
            Assert.True(res0.HoldsLastClip);
            Assert.Equal(["rx2"], model.MotionRoutines["res2"].Segments.Select(s => s.ClipName));
            Assert.Equal(56, model.MotionRoutines["res2"].Segments[0].DurationTicks);
            Assert.Equal(["si0", "si1"], model.MotionRoutines["sit0"].Segments.Select(s => s.ClipName));
            Assert.Equal(["si2"], model.MotionRoutines["sit2"].Segments.Select(s => s.ClipName));

            Assert.Same(model.Animations["rx0"], RestPosture.EnterClip(model, AnimationCategory.Healing));
            Assert.Same(model.Animations["rx1"], RestPosture.LoopClip(model, AnimationCategory.Healing));
            Assert.Same(model.Animations["rx2"], RestPosture.ExitClip(model, AnimationCategory.Healing));
            Assert.Same(model.Animations["si1"], RestPosture.LoopClip(model, AnimationCategory.Sitting));
        }

        /// <summary>The seated loops really sit: the head is at least 0.19 yalms lower than in the idle (Tarutaru's rx1, the least).</summary>
        [Theory]
        [MemberData(nameof(Races))]
        public void SeatedLoops_LowerTheHead(CharacterRace race)
        {
            var rm = Retail();
            if (rm == null) return;
            var model = Assemble(rm, race);
            var skeleton = model.Skeleton!;
            int head = HeadLook.HeadJoint(skeleton);
            float Height(string clip) => -SkeletonPoseEvaluator.EvaluatePose(skeleton, model.Animations[clip], 0f, true).Translations[head].Y;

            float idle = Height("idl");
            Assert.True(idle - Height("rx1") > 0.19f, $"rx1 head {Height("rx1"):F2} vs idle {idle:F2}");
            Assert.True(idle - Height("si1") > 0.19f, $"si1 head {Height("si1"):F2} vs idle {idle:F2}");
        }

        /// <summary>
        /// The first-person eye height is the eyelid joints' height in the idle (yalms, before the size scale), between the
        /// head joint and the top of the head.
        /// </summary>
        [Theory]
        [InlineData(CharacterRace.HumeMale, 1.657f)]
        [InlineData(CharacterRace.HumeFemale, 1.544f)]
        [InlineData(CharacterRace.ElvaanMale, 1.943f)]
        [InlineData(CharacterRace.ElvaanFemale, 1.801f)]
        [InlineData(CharacterRace.TaruMale, 0.708f)]
        [InlineData(CharacterRace.TaruFemale, 0.708f)]
        [InlineData(CharacterRace.Mithra, 1.535f)]
        [InlineData(CharacterRace.Galka, 2.138f)]
        public void FirstPersonEye_IsAtTheEyelids(CharacterRace race, float expected)
        {
            var rm = Retail();
            if (rm == null) return;
            var model = Assemble(rm, race);
            float? eye = FirstPersonEye.HeightOf(model);
            Assert.NotNull(eye);
            Assert.Equal(expected, eye!.Value, 2);

            var idle = SkeletonPoseEvaluator.EvaluatePose(model.Skeleton!, model.Animations["idl"], 0f, true);
            float head = -idle.Translations[HeadLook.HeadJoint(model.Skeleton!)].Y;
            float top = idle.Translations.Max(t => -t.Y);
            Assert.InRange(eye.Value, head, top);
        }

        /// <summary>
        /// The synthesis posture (#315, status 44 = Healing's routines): retail's crafter (a Tarutaru in the maintainer's video,
        /// 2026-10-16) rocks back onto its back before standing up, which Tarutaru's rx2 does: the head joint falls below
        /// 0.2 yalms and 0.5 behind the root in the middle of the clip, then ends standing.
        /// </summary>
        [Fact]
        public void TarutaruStandUp_RocksBackOntoItsBack_AsRetailsSynthesisEnds()
        {
            var rm = Retail();
            if (rm == null) return;
            var model = Assemble(rm, CharacterRace.TaruMale);
            var skeleton = model.Skeleton!;
            int head = HeadLook.HeadJoint(skeleton);
            var clip = RestPosture.ExitClip(model, RestPosture.CategoryFor(RestPosture.StatusSynthesis)!.Value)!;
            var lowest = Enumerable.Range(0, 20).Select(i => SkeletonPoseEvaluator.EvaluatePose(skeleton, clip, clip.DurationSeconds * i / 19f, false).Translations[head])
                .OrderBy(t => -t.Y).First();
            Assert.True(-lowest.Y < 0.2f, $"head {-lowest.Y:F2}");
            Assert.True(lowest.X < -0.5f, $"head x {lowest.X:F2}");
            float end = -SkeletonPoseEvaluator.EvaluatePose(skeleton, clip, clip.DurationSeconds, false).Translations[head].Y;
            Assert.True(end > 0.55f, $"standing head {end:F2}");
        }
    }
}
