// tests/Gordian.Core.Tests/Graphics/SkeletonPoseOverlayTests.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Xunit;

namespace Gordian.Core.Tests.Graphics
{
    public class SkeletonPoseOverlayTests
    {
        private static readonly Skeleton TwoJoints = new(new[]
        {
            new SkeletonJoint(-1, Quaternion.Identity, Vector3.Zero),
            new SkeletonJoint(0, Quaternion.Identity, new Vector3(0f, 1f, 0f))
        });

        private static AnimationClip Pose(string name, params (int Joint, Quaternion Rotation)[] joints)
        {
            var tracks = new Dictionary<int, BoneAnimationTrack>();
            foreach (var (joint, rotation) in joints)
            {
                tracks[joint] = new BoneAnimationTrack { JointIndex = joint, Rotations = new[] { rotation }, Translations = new[] { Vector3.Zero } };
            }
            return new AnimationClip { Name = name, NumFrames = 1, KeyFrameDuration = 1f, Tracks = tracks };
        }

        private static Quaternion AboutX(float degrees) => Quaternion.CreateFromAxisAngle(Vector3.UnitX, degrees * MathF.PI / 180f);

        private static float AngleBetween(Quaternion a, Quaternion b) =>
            2f * MathF.Acos(MathF.Min(1f, MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b))))) * 180f / MathF.PI;

        [Fact]
        public void AbsoluteOverlay_BlendsJointTowardPose()
        {
            var stance = Pose("btl", (1, AboutX(10)));
            var pose = Pose("gud", (1, AboutX(50)));

            var half = SkeletonPoseEvaluator.EvaluatePose(TwoJoints, stance, 0f, false, overlay: new SkeletonPoseEvaluator.PoseOverlay(pose, 0.5f));
            var full = SkeletonPoseEvaluator.EvaluatePose(TwoJoints, stance, 0f, false, overlay: new SkeletonPoseEvaluator.PoseOverlay(pose, 1f));

            Assert.InRange(AngleBetween(half.Rotations[1], AboutX(30)), 0f, 1f);
            Assert.InRange(AngleBetween(full.Rotations[1], AboutX(50)), 0f, 0.5f);
        }

        [Fact]
        public void AdditiveOverlay_AddsThePosesOffsetFromItsReferenceOnTopOfTheClip()
        {
            // The damage pose bends joint 1 by 40 degrees relative to its neutral twin; the actor mid-swing at 70 degrees
            // flinches to 110, not to the pose's own 60.
            var swing = Pose("at0", (1, AboutX(70)));
            var dfm = Pose("dfm", (1, AboutX(60)));
            var dfi = Pose("dfi", (1, AboutX(20)));

            var pose = SkeletonPoseEvaluator.EvaluatePose(TwoJoints, swing, 0f, false,
                overlay: new SkeletonPoseEvaluator.PoseOverlay(dfm, 1f, Reference: dfi));

            Assert.InRange(AngleBetween(pose.Rotations[1], AboutX(110)), 0f, 0.5f);
        }

        [Fact]
        public void Overlay_RespectsJointMask()
        {
            var pose = Pose("dfm", (1, AboutX(45)));

            var masked = SkeletonPoseEvaluator.EvaluatePose(TwoJoints, null, 0f, false,
                overlay: new SkeletonPoseEvaluator.PoseOverlay(pose, 1f, new[] { true, false }));

            Assert.Equal(Quaternion.Identity, masked.Rotations[1]);
        }

        [Fact]
        public void Overlay_AppliesDuringCrossFadeToo()
        {
            var a = Pose("btl", (1, AboutX(0)));
            var b = Pose("at0", (1, AboutX(20)));
            var pose = Pose("gud", (1, AboutX(90)));

            var blended = SkeletonPoseEvaluator.EvaluateBlendedPose(TwoJoints, a, 0f, true, b, 0f, false, 0.5f,
                overlay: new SkeletonPoseEvaluator.PoseOverlay(pose, 1f));

            Assert.InRange(AngleBetween(blended.Rotations[1], AboutX(90)), 0f, 0.5f);
        }
    }
}
