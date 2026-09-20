// tests/Gordian.Core.Tests/Graphics/SkeletonPoseEvaluatorBlendingTests.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Xunit;

namespace Gordian.Core.Tests.Graphics
{
    public class SkeletonPoseEvaluatorBlendingTests
    {
        [Fact]
        public void EvaluateBlendedPose_WeightZeroAndOne_MatchIndividualClipsExactly()
        {
            var joints = new[]
            {
                new SkeletonJoint(-1, Quaternion.Identity, Vector3.Zero),
                new SkeletonJoint(0, Quaternion.Identity, new Vector3(0f, 10f, 0f))
            };
            var skeleton = new Skeleton(joints);

            var trackA = new BoneAnimationTrack
            {
                JointIndex = 1,
                Rotations = new[] { Quaternion.Identity },
                Translations = new[] { new Vector3(0f, 0f, 10f) }
            };
            var clipA = new AnimationClip
            {
                Name = "clipA",
                NumFrames = 1,
                KeyFrameDuration = 1.0f,
                Tracks = new Dictionary<int, BoneAnimationTrack> { [1] = trackA }
            };

            var trackB = new BoneAnimationTrack
            {
                JointIndex = 1,
                Rotations = new[] { Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f) },
                Translations = new[] { new Vector3(0f, 0f, 30f) }
            };
            var clipB = new AnimationClip
            {
                Name = "clipB",
                NumFrames = 1,
                KeyFrameDuration = 1.0f,
                Tracks = new Dictionary<int, BoneAnimationTrack> { [1] = trackB }
            };

            var poseA = SkeletonPoseEvaluator.EvaluatePose(skeleton, clipA, 0f, false);
            var poseB = SkeletonPoseEvaluator.EvaluatePose(skeleton, clipB, 0f, false);

            var blendedZero = SkeletonPoseEvaluator.EvaluateBlendedPose(skeleton, clipA, 0f, false, clipB, 0f, false, 0f);
            var blendedOne = SkeletonPoseEvaluator.EvaluateBlendedPose(skeleton, clipA, 0f, false, clipB, 0f, false, 1f);

            Assert.Equal(poseA.Translations[1], blendedZero.Translations[1]);
            Assert.Equal(poseA.Rotations[1], blendedZero.Rotations[1]);

            Assert.Equal(poseB.Translations[1], blendedOne.Translations[1]);
            Assert.Equal(poseB.Rotations[1], blendedOne.Rotations[1]);
        }

        [Fact]
        public void EvaluateBlendedPose_WeightHalf_InterpolatesTranslationAndRotation()
        {
            var joints = new[]
            {
                new SkeletonJoint(-1, Quaternion.Identity, Vector3.Zero),
                new SkeletonJoint(0, Quaternion.Identity, Vector3.Zero)
            };
            var skeleton = new Skeleton(joints);

            var clipA = new AnimationClip
            {
                Name = "clipA",
                NumFrames = 1,
                KeyFrameDuration = 1.0f,
                Tracks = new Dictionary<int, BoneAnimationTrack>
                {
                    [1] = new BoneAnimationTrack
                    {
                        JointIndex = 1,
                        Rotations = new[] { Quaternion.Identity },
                        Translations = new[] { new Vector3(10f, 0f, 0f) }
                    }
                }
            };

            var clipB = new AnimationClip
            {
                Name = "clipB",
                NumFrames = 1,
                KeyFrameDuration = 1.0f,
                Tracks = new Dictionary<int, BoneAnimationTrack>
                {
                    [1] = new BoneAnimationTrack
                    {
                        JointIndex = 1,
                        Rotations = new[] { Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f) }, // 90 deg yaw
                        Translations = new[] { new Vector3(30f, 0f, 0f) }
                    }
                }
            };

            var blended = SkeletonPoseEvaluator.EvaluateBlendedPose(skeleton, clipA, 0f, false, clipB, 0f, false, 0.5f);

            // Translation midpoint: Lerp(10, 30, 0.5) = 20
            Assert.Equal(20f, blended.Translations[1].X, 3);
            Assert.Equal(0f, blended.Translations[1].Y, 3);
            Assert.Equal(0f, blended.Translations[1].Z, 3);

            // Rotation midpoint: NLERP of 0 deg and 90 deg yaw is 45 deg yaw
            var expectedRot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4f);
            Assert.True(MathF.Abs(Quaternion.Dot(expectedRot, blended.Rotations[1])) > 0.999f);
        }

        [Fact]
        public void EvaluateBlendedPose_ConservesBoneLengths()
        {
            float boneLength = 15.0f;
            var joints = new[]
            {
                new SkeletonJoint(-1, Quaternion.Identity, Vector3.Zero), // root
                new SkeletonJoint(0, Quaternion.Identity, new Vector3(0f, boneLength, 0f)) // child bone
            };
            var skeleton = new Skeleton(joints);

            // Clip A rotates bone by 30 degrees
            var clipA = new AnimationClip
            {
                Name = "clipA",
                NumFrames = 1,
                KeyFrameDuration = 1.0f,
                Tracks = new Dictionary<int, BoneAnimationTrack>
                {
                    [0] = new BoneAnimationTrack
                    {
                        JointIndex = 0,
                        Rotations = new[] { Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 6f) },
                        Translations = new[] { Vector3.Zero }
                    }
                }
            };

            // Clip B rotates bone by -60 degrees
            var clipB = new AnimationClip
            {
                Name = "clipB",
                NumFrames = 1,
                KeyFrameDuration = 1.0f,
                Tracks = new Dictionary<int, BoneAnimationTrack>
                {
                    [0] = new BoneAnimationTrack
                    {
                        JointIndex = 0,
                        Rotations = new[] { Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI / 3f) },
                        Translations = new[] { Vector3.Zero }
                    }
                }
            };

            // Across various blend weights, distance from joint 0 to joint 1 must remain exactly boneLength
            float[] weights = { 0.0f, 0.25f, 0.5f, 0.75f, 1.0f };
            foreach (float w in weights)
            {
                var pose = SkeletonPoseEvaluator.EvaluateBlendedPose(skeleton, clipA, 0f, false, clipB, 0f, false, w);
                float distance = Vector3.Distance(pose.Translations[0], pose.Translations[1]);
                Assert.Equal(boneLength, distance, 3);
            }
        }
    }
}
