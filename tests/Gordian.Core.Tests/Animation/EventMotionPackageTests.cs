// tests/Gordian.Core.Tests/Animation/EventMotionPackageTests.cs
using System;
using System.IO;
using System.Numerics;
using Gordian.Core.Animation;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Animation
{
    /// <summary>The 0x66 motion packages (#176). Skipped without the game install.</summary>
    public class EventMotionPackageTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        private static EventMotionBank? Load(ResourceManager rm, int package)
        {
            foreach (int fileId in EventMotionBank.PackageFiles(package))
            {
                if (rm.LoadDatBytesByFileId(fileId) is { } bytes && EventMotionBank.Parse(bytes, fileId) is { } bank) return bank;
            }
            return null;
        }

        /// <summary>
        /// Packages 0-69 are in the first two tables, 70-139 (ten per player race) in the race sets table at 61171 + n
        /// (#209), 140-209 in the second at 87685 + n and 210-279 in the third at 102029 + n (#228); from 280 on the
        /// number is a 0x5B bank number.
        /// </summary>
        [Fact]
        public void PackageFiles_PicksTheTableByPackage()
        {
            Assert.Equal(new[] { 32378, 32721 }, EventMotionBank.PackageFiles(9));
            Assert.Equal(new[] { 32498, 32781 }, EventMotionBank.PackageFiles(69));
            Assert.Equal(new[] { 61241 }, EventMotionBank.PackageFiles(70));
            Assert.Equal(new[] { 61281 }, EventMotionBank.PackageFiles(110));
            Assert.Equal(new[] { 61310 }, EventMotionBank.PackageFiles(139));
            Assert.Equal(new[] { 87825 }, EventMotionBank.PackageFiles(140));
            Assert.Equal(new[] { 87834 }, EventMotionBank.PackageFiles(149));
            Assert.Equal(new[] { 87894 }, EventMotionBank.PackageFiles(209));
            Assert.Equal(new[] { 102239 }, EventMotionBank.PackageFiles(210));
            Assert.Equal(new[] { 102242 }, EventMotionBank.PackageFiles(213));
            Assert.Equal(new[] { 102308 }, EventMotionBank.PackageFiles(279));
            // 280 and up: the 0x5B bands (32104 + n, 49135 + n from 512, 56345 + n from 1024, 59739 + n from 2048).
            Assert.Equal(new[] { 32384 }, EventMotionBank.PackageFiles(280));
            Assert.Equal(new[] { 49762 }, EventMotionBank.PackageFiles(627));
            Assert.Equal(new[] { 57788 }, EventMotionBank.PackageFiles(1443));
            Assert.Equal(new[] { 61900 }, EventMotionBank.PackageFiles(2161));
            Assert.Empty(EventMotionBank.PackageFiles(-1));
            Assert.Equal(-1, EventMotionBank.RaceSetFileId(69));
            Assert.Equal(-1, EventMotionBank.RaceSetFileId(280));
        }

        /// <summary>
        /// The second and third race sets tables (#228) hold the same kinds of gesture at the same place in each race's
        /// ten, as the first does: n0 <c>orz0</c> / <c>mab0</c> and n9 the race's talk set in 140-209, n2 <c>wlk1</c>, n3
        /// <c>fyu0</c>-<c>fyu3</c> and n4 <c>mae0</c> / <c>mae1</c> in 210-279. Upper Jeuno 10221 plays <c>orz0</c> from
        /// 140 + 10 · slot on the player; Northern San d'Oria 898, Bastok Markets 591 and Windurst Walls 532 the talk
        /// from 149 + 10 · slot; the Rhapsodies finale (Desuetia, Reisenjima Sanctorium) the 210-279 sets.
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        public void RaceSetTables_HoldTheSameKindsForEveryRace(int slot)
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var despair = Load(rm, 140 + 10 * slot)!;
            Assert.Equal(87825 + 10 * slot, despair.FileId);
            Assert.All(new[] { "orz0", "mab0", "mab1" }, r => Assert.True(despair.GetRoutineFrames(r) > 0, r));
            var talk = Load(rm, 149 + 10 * slot)!;
            Assert.Equal(87834 + 10 * slot, talk.FileId);
            Assert.All(new[] { "tlk0", "tlk1" }, r => Assert.True(talk.GetRoutineFrames(r) > 0, r));
            var walk = Load(rm, 212 + 10 * slot)!;
            Assert.Equal(102241 + 10 * slot, walk.FileId);
            Assert.True(walk.GetRoutineFrames("wlk1") > 0);
            var fyu = Load(rm, 213 + 10 * slot)!;
            Assert.All(new[] { "fyu0", "fyu1", "fyu2", "fyu3" }, r => Assert.True(fyu.GetRoutineFrames(r) > 0, r));
            var mae = Load(rm, 214 + 10 * slot)!;
            Assert.All(new[] { "mae0", "mae1" }, r => Assert.True(mae.GetRoutineFrames(r) > 0, r));
            // n6-n9 of the third table are empty placeholders.
            Assert.Null(Load(rm, 218 + 10 * slot));
        }

        /// <summary>
        /// Port Jeuno 324's look up (#209): every race's package (skeleton slot · 10 + 70) has <c>atp0</c>, one clip held
        /// until the next motion that tilts the face up by about 28 degrees from the bind pose, where the idle holds it
        /// level (the head joint in model space), as the player holds it in retail's front shot under the flash.
        /// </summary>
        [Theory]
        [InlineData(70, CharacterRace.HumeMale)]
        [InlineData(80, CharacterRace.HumeFemale)]
        [InlineData(90, CharacterRace.ElvaanMale)]
        [InlineData(100, CharacterRace.ElvaanFemale)]
        [InlineData(110, CharacterRace.TaruMale)]
        [InlineData(120, CharacterRace.Mithra)]
        [InlineData(130, CharacterRace.Galka)]
        public void RacePackage_LooksUpAndHolds(int package, CharacterRace race)
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var bank = Load(rm, package);
            Assert.NotNull(bank);
            Assert.Equal(61171 + package, bank!.FileId);
            var atp = bank.Routines["atp0"];
            Assert.True(atp.HoldsLastClip);
            var skeleton = EntityModelLoader.ParseDatContainer(rm.LoadDatBytes(CharacterEquipmentResolver.GetBaseSkeletonPath(race))!, "base").Skeleton!;
            int head = HeadLook.HeadJoint(skeleton);
            var clip = bank.Clips[atp.Segments[^1].ClipName];
            var rest = SkeletonPoseEvaluator.ComputeBindPose(skeleton).Rotations[head];
            var pose = SkeletonPoseEvaluator.EvaluatePose(skeleton, clip, clip.DurationSeconds / 2f, loop: true);
            // The model faces +X with Y down: a face tilted up turns +X toward -Y.
            var face = Vector3.Transform(Vector3.UnitX, pose.Rotations[head] * Quaternion.Inverse(rest));
            float pitch = MathF.Atan2(-face.Y, MathF.Sqrt(face.X * face.X + face.Z * face.Z)) * 180f / MathF.PI;
            Assert.InRange(pitch, 20f, 40f);
        }

        /// <summary>
        /// The Southern San d'Oria intro's gestures: Ceraule's talk and thought (package 20), the gate guards' and
        /// Ceraule's salute (21) and the Royal Knights' and Rahal's talk (29, only in the second table).
        /// </summary>
        [Theory]
        [InlineData(20, 32400, "tlk0")]
        [InlineData(20, 32400, "thk1")]
        [InlineData(21, 32402, "sl00")]
        [InlineData(29, 32741, "tlk0")]
        [InlineData(29, 32741, "thk2")]
        [InlineData(12, 32384, "kka0")] // Cornelia's, per xi-tools
        [InlineData(9, 32721, "sha0")] // the Hume kneel of Port Jeuno 324 (#193)
        [InlineData(9, 32721, "sha1")]
        // The second race sets table (#228): Hume male despair and talk, Galka talk, Mithra kizm (Port Jeuno 431).
        [InlineData(140, 87825, "orz0")]
        [InlineData(149, 87834, "tlk0")]
        [InlineData(149, 87834, "thk0")]
        [InlineData(209, 87894, "tlk1")]
        [InlineData(191, 87876, "kizm")]
        [InlineData(148, 87833, "bed0")]
        // The third (the Rhapsodies finale): Hume male fyu / mae, Galka walk.
        [InlineData(213, 102242, "fyu3")]
        [InlineData(214, 102243, "mae0")]
        [InlineData(272, 102301, "wlk1")]
        // Numbers from 280 on are 0x5B bank numbers: 1443's tla / tlb, 2158's and 2161's sai1 (Windurst Waters 991).
        // Ru'Lude Gardens 10050's 627 names won2 / wof2, which 49762 holds as routines over clips it does not have.
        [InlineData(1443, 57788, "tlb0")]
        [InlineData(2158, 61897, "sai1")]
        [InlineData(2161, 61900, "sai1")]
        [InlineData(2161, 61900, "tlk0")]
        public void Package_HasTheGesture(int package, int fileId, string routine)
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var bank = Load(rm, package);
            Assert.NotNull(bank);
            Assert.Equal(fileId, bank!.FileId);
            Assert.True(bank.GetRoutineFrames(routine) > 0);
        }

        /// <summary>
        /// Port Jeuno 324 picks the kneel set from the actor's race (package = skeleton slot · 10 + 9, #193): Hume male is
        /// package 9, whose <c>sha0</c> kneels down and holds the kneel until <c>sha1</c> gets up; the PC <c>corp</c> holds
        /// the corpse pose the same way.
        /// </summary>
        [Fact]
        public void HumeKneel_HoldsItsLastClip()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var bank = Load(rm, 9)!;
            Assert.True(bank.Routines["sha0"].HoldsLastClip);
            Assert.False(bank.Routines["sha1"].HoldsLastClip);
            Assert.Equal(216, bank.GetRoutineFrames("sha0"));
        }

        [Fact]
        public void SaluteOfTheFirstTable_JoinsTheWaistPart()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var bank = Load(rm, 21)!;
            Assert.True(bank.Clips.ContainsKey("sl12"));
            Assert.True(bank.Clips["sl1"].Tracks.Count > bank.Clips["sl11"].Tracks.Count);
        }
    }
}
