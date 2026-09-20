// tests/Gordian.Core.Tests/Resources/SkeletonPoseEvaluatorTests.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class SkeletonPoseEvaluatorTests
    {
        [Fact]
        public void SkeletonPoseEvaluator_ComputeBindPose_ComputesHierarchyTransforms()
        {
            // Joint 0 (root): Translation (10, 20, 30) -> in FFXI root space becomes (-30, 20, 10)
            // Joint 1: child of 0, Translation (0, 5, 0), Identity rotation
            var joints = new[]
            {
                new SkeletonJoint(-1, Quaternion.Identity, new Vector3(10f, 20f, 30f)),
                new SkeletonJoint(0, Quaternion.Identity, new Vector3(0f, 5f, 0f))
            };
            var skeleton = new Skeleton(joints);

            var pose = SkeletonPoseEvaluator.ComputeBindPose(skeleton);

            Assert.Equal(2, pose.Rotations.Length);
            Assert.Equal(2, pose.Translations.Length);

            // Root translation is (-z, y, x)
            Assert.Equal(new Vector3(-30f, 20f, 10f), pose.Translations[0]);
            Assert.Equal(Quaternion.Identity, pose.Rotations[0]);

            // Child translation is parent trans + local trans
            Assert.Equal(new Vector3(-30f, 25f, 10f), pose.Translations[1]);
            Assert.Equal(Quaternion.Identity, pose.Rotations[1]);
        }

        [Fact]
        public void SkeletonPoseEvaluator_SkinVertex_TransformsSingleAndDoubleJoints()
        {
            var joints = new[]
            {
                new SkeletonJoint(-1, Quaternion.Identity, new Vector3(0f, 10f, 0f)), // trans -> (0, 10, 0)
                new SkeletonJoint(0, Quaternion.Identity, new Vector3(5f, 0f, 0f))    // trans -> (5, 10, 0)
            };
            var skeleton = new Skeleton(joints);
            var pose = SkeletonPoseEvaluator.ComputeBindPose(skeleton);

            // Single joint vertex attached to joint 1 (world trans = 5, 10, 0)
            var vSingle = new SkinnedVertex
            {
                Position0 = new Vector3(1f, 2f, 3f),
                Normal0 = Vector3.UnitY,
                Joint0 = 1,
                Joint1 = -1
            };

            var (posSingle, normSingle) = SkeletonPoseEvaluator.SkinVertex(vSingle, pose);
            Assert.Equal(new Vector3(6f, 12f, 3f), posSingle);
            Assert.Equal(Vector3.UnitY, normSingle);

            // Double joint vertex attached to joint 0 (0,10,0) and joint 1 (5,10,0)
            // Pre-weighted p0 = (0.5, 0, 0), p1 = (0.5, 0, 0), w0 = 0.5, w1 = 0.5
            var vDouble = new SkinnedVertex
            {
                Position0 = new Vector3(0.5f, 0f, 0f),
                Position1 = new Vector3(0.5f, 0f, 0f),
                Normal0 = Vector3.UnitY,
                Normal1 = Vector3.UnitY,
                Weight0 = 0.5f,
                Weight1 = 0.5f,
                Joint0 = 0,
                Joint1 = 1
            };

            var (posDouble, _) = SkeletonPoseEvaluator.SkinVertex(vDouble, pose);
            // p0 + w0*t0 + p1 + w1*t1 = (0.5,0,0) + 0.5*(0,10,0) + (0.5,0,0) + 0.5*(5,10,0)
            // = (1, 0, 0) + (2.5, 10, 0) = (3.5, 10, 0)
            Assert.Equal(new Vector3(3.5f, 10f, 0f), posDouble);
        }

        [Fact]
        public void SkeletonPoseEvaluator_EvaluateMeshGroup_UnrollsTriangleStrip()
        {
            var joints = new[] { new SkeletonJoint(-1, Quaternion.Identity, Vector3.Zero) };
            var skeleton = new Skeleton(joints);
            var pose = SkeletonPoseEvaluator.ComputeBindPose(skeleton);

            var group = new SkeletonMeshGroup
            {
                Vertices = new[]
                {
                    new SkinnedVertex { Position0 = new Vector3(0, 0, 0), Joint0 = 0, Joint1 = -1 },
                    new SkinnedVertex { Position0 = new Vector3(1, 0, 0), Joint0 = 0, Joint1 = -1 },
                    new SkinnedVertex { Position0 = new Vector3(0, 1, 0), Joint0 = 0, Joint1 = -1 },
                    new SkinnedVertex { Position0 = new Vector3(1, 1, 0), Joint0 = 0, Joint1 = -1 },
                }
            };

            // Triangle strip with 2 triangles (4 corners)
            group.Pieces.Add(new SkeletonMeshPiece
            {
                Topology = MeshTopology.TriangleStrip,
                TextureName = "strip_tex",
                Corners = new[]
                {
                    new MeshCorner(0, Vector2.Zero, 0xFFFFFFFF),
                    new MeshCorner(1, Vector2.Zero, 0xFFFFFFFF),
                    new MeshCorner(2, Vector2.Zero, 0xFFFFFFFF),
                    new MeshCorner(3, Vector2.Zero, 0xFFFFFFFF),
                }
            });

            var submeshes = SkeletonPoseEvaluator.EvaluateMeshGroup(group, pose);
            Assert.Single(submeshes);

            var sm = submeshes[0];
            Assert.Equal("strip_tex", sm.TextureName);
            Assert.Equal(4, sm.Vertices.Length);
            // 2 triangles = 6 indices
            Assert.Equal(6, sm.Indices.Length);
            Assert.Equal(2, sm.TriangleCount);
        }

        [Fact]
        public void SkeletonPoseEvaluator_EvaluatePose_SamplesClipAndFallsBackToBindPoseForUntrackedJoints()
        {
            var joints = new[]
            {
                new SkeletonJoint(-1, Quaternion.Identity, new Vector3(1f, 0f, 0f)), // root, untouched by clip below
                new SkeletonJoint(0, Quaternion.Identity, new Vector3(0f, 5f, 0f))   // child, untracked in clip
            };
            var skeleton = new Skeleton(joints);

            var track = new BoneAnimationTrack
            {
                JointIndex = 0,
                Rotations = new[] { Quaternion.Identity, Quaternion.Identity },
                Translations = new[] { new Vector3(100f, 0f, 0f), new Vector3(200f, 0f, 0f) }
            };
            var clip = new AnimationClip
            {
                Name = "test",
                NumFrames = 2,
                KeyFrameDuration = 1.0f, // -> 30 fps -> DurationSeconds = 2/30
                Tracks = new Dictionary<int, BoneAnimationTrack> { [0] = track }
            };

            var pose = SkeletonPoseEvaluator.EvaluatePose(skeleton, clip, timeSeconds: 0f, loop: true);

            // Root (joint 0) additively applies the clip's frame-0 translation (100,0,0) onto the skeleton's bind
            // value (1,0,0) -> (101,0,0), passed through the FFXI root coordinate flip (x,y,z) -> (-z,y,x).
            Assert.Equal(new Vector3(0f, 0f, 101f), pose.Translations[0]);

            // Joint 1 has no track in this clip -> falls back to its own bind-pose local translation
            // (0,5,0), accumulated onto the now clip-driven parent.
            Assert.Equal(new Vector3(0f, 5f, 101f), pose.Translations[1]);

            // Bind pose (no clip) is unaffected and still returns the skeleton's static values.
            var bindPose = SkeletonPoseEvaluator.ComputeBindPose(skeleton);
            Assert.Equal(new Vector3(0f, 0f, 1f), bindPose.Translations[0]);
        }
    }
}
