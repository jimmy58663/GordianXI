// tests/Gordian.Core.Tests/Animation/HeadLookTests.cs
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Gordian.Core.Animation;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Animation
{
    /// <summary>The event head look (#174). The data test is skipped without the game install.</summary>
    public class HeadLookTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        // The renderer's model-to-display flip (EntityRenderer.EntityRotMatrix) and heading rotation.
        private static readonly Matrix4x4 Flip = new(1, 0, 0, 0, 0, -1, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1);
        private static Matrix4x4 HeadingRotation(float heading) => Matrix4x4.CreateRotationY(-heading - MathF.PI);

        [Fact]
        public void ModelRotation_TurnsLikeTheBodyTurningBySameAngle()
        {
            var point = new Vector3(0.3f, -1.6f, 0.2f);
            const float heading = 1.1f, yaw = 0.7f;
            var turnedHead = Vector3.Transform(Vector3.Transform(point, HeadLook.ModelRotation(yaw)), Flip * HeadingRotation(heading));
            var turnedBody = Vector3.Transform(point, Flip * HeadingRotation(heading + yaw));
            Assert.True(Vector3.Distance(turnedHead, turnedBody) < 1e-4f);
        }

        [Fact]
        public void TargetYaw_IsTheBearingLessTheHeading_ClampedAndWrapped()
        {
            var from = Vector3.Zero;
            var east = new Vector3(5, 0, 0);
            Assert.Equal(0f, HeadLook.TargetYaw(0f, from, east), 4);
            float north = WorldEntity.HeadingOf(0, 1);
            Assert.Equal(HeadLook.MaxYaw, HeadLook.TargetYaw(north, from, east), 4); // a quarter turn, clamped
            Assert.Equal(-0.2f, HeadLook.TargetYaw(0.2f, from, east), 4);
            Assert.Equal(0.1f, HeadLook.TargetYaw(MathF.Tau - 0.1f, from, east), 4); // across the wrap
            Assert.Equal(0f, HeadLook.TargetYaw(1f, from, from));
        }

        [Fact]
        public void Apply_TurnsTheHeadAndWhatHangsFromIt_Only()
        {
            // root 0 -> spine 1 -> head 2 -> face 3; arm 4 under the spine.
            var joints = new[]
            {
                new SkeletonJoint(-1, Quaternion.Identity, Vector3.Zero),
                new SkeletonJoint(0, Quaternion.Identity, new Vector3(0, -1, 0)),
                new SkeletonJoint(1, Quaternion.Identity, new Vector3(0, -0.5f, 0)),
                new SkeletonJoint(2, Quaternion.Identity, new Vector3(0.1f, -0.1f, 0)),
                new SkeletonJoint(1, Quaternion.Identity, new Vector3(0, 0, 0.3f)),
            };
            var refs = Enumerable.Range(0, 6).Select(i => new JointReference((ushort)(i == HeadLook.HeadReference ? 2 : 0), Vector3.Zero)).ToArray();
            var skeleton = new Skeleton(joints, refs);
            Assert.Equal(2, HeadLook.HeadJoint(skeleton));
            var pose = SkeletonPoseEvaluator.ComputeBindPose(skeleton);
            var before = pose.Translations.ToArray();
            HeadLook.Apply(skeleton, pose, 2, MathF.PI / 2);
            Assert.Equal(before[2], pose.Translations[2]); // the pivot
            Assert.Equal(before[4], pose.Translations[4]); // the arm stays
            Assert.Equal(before[1], pose.Translations[1]);
            Assert.True(Vector3.Distance(before[3], pose.Translations[3]) > 0.05f); // the face swings round
            Assert.Equal(Vector3.Distance(before[2], before[3]), Vector3.Distance(pose.Translations[2], pose.Translations[3]), 4);
        }

        /// <summary>Reference 5 is the joint the race's face mesh is bound to (most of its vertices).</summary>
        [Theory]
        [InlineData(CharacterRace.HumeMale, 52)]
        [InlineData(CharacterRace.HumeFemale, 30)]
        [InlineData(CharacterRace.ElvaanMale, 30)]
        [InlineData(CharacterRace.ElvaanFemale, 57)]
        [InlineData(CharacterRace.TaruMale, 7)]
        [InlineData(CharacterRace.Mithra, 40)]
        [InlineData(CharacterRace.Galka, 40)]
        public void HeadJoint_IsTheFaceMeshJoint(CharacterRace race, int head)
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var skeleton = EntityModelLoader.ParseDatContainer(rm.LoadDatBytes(CharacterEquipmentResolver.GetBaseSkeletonPath(race))!, "base").Skeleton!;
            Assert.Equal(head, HeadLook.HeadJoint(skeleton));
            Assert.True(CharacterEquipmentResolver.TryResolveGearFileId(race, CharacterSlot.Face, 1, out int faceFile));
            var face = EntityModelLoader.ParseDatContainer(rm.LoadDatBytesByFileId(faceFile)!, "face");
            int bound = face.Meshes.SelectMany(m => m.Vertices).GroupBy(v => v.Joint0).OrderByDescending(g => g.Count()).First().Key;
            Assert.Equal(head, bound);
        }
    }
}
