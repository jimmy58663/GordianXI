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
        /// (#209); the others are not located.
        /// </summary>
        [Fact]
        public void PackageFiles_PicksTheTableByPackage()
        {
            Assert.Equal(new[] { 32378, 32721 }, EventMotionBank.PackageFiles(9));
            Assert.Equal(new[] { 32498, 32781 }, EventMotionBank.PackageFiles(69));
            Assert.Equal(new[] { 61241 }, EventMotionBank.PackageFiles(70));
            Assert.Equal(new[] { 61281 }, EventMotionBank.PackageFiles(110));
            Assert.Equal(new[] { 61310 }, EventMotionBank.PackageFiles(139));
            Assert.Empty(EventMotionBank.PackageFiles(140));
            Assert.Empty(EventMotionBank.PackageFiles(-1));
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
