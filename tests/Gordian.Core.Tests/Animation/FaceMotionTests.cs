// tests/Gordian.Core.Tests/Animation/FaceMotionTests.cs
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

namespace Gordian.Core.Tests.Animation
{
    /// <summary>The talking mouth and the blink (#185). The data test is skipped without the game install.</summary>
    public class FaceMotionTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        // root 0 -> head 1 -> jaw 2 -> lip 3; eyelid 4 under the head.
        private static Skeleton Skeleton() => new(new[]
        {
            new SkeletonJoint(-1, Quaternion.Identity, Vector3.Zero),
            new SkeletonJoint(0, Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.4f), new Vector3(0, -1.5f, 0)),
            new SkeletonJoint(1, Quaternion.Identity, new Vector3(0.05f, 0.04f, 0)),
            new SkeletonJoint(2, Quaternion.Identity, new Vector3(0.06f, 0, 0)),
            new SkeletonJoint(1, Quaternion.Identity, new Vector3(0.06f, -0.07f, 0.03f)),
        });

        /// <summary>A two-frame clip turning <paramref name="joint"/> about Z from rest to <paramref name="angle"/> (frame duration 1/30 s).</summary>
        private static AnimationClip Clip(string name, int joint, float angle) => new()
        {
            Name = name,
            NumFrames = 2,
            KeyFrameDuration = 1f,
            Tracks = new Dictionary<int, BoneAnimationTrack>
            {
                [joint] = new BoneAnimationTrack
                {
                    JointIndex = joint,
                    Rotations = new[] { Quaternion.Identity, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle) },
                    Translations = new[] { Vector3.Zero, Vector3.Zero },
                    Scales = new[] { Vector3.One, Vector3.One },
                },
            },
        };

        private static EntityModel Model()
        {
            var model = new EntityModel { Skeleton = Skeleton() };
            model.Animations[FaceMotion.MouthClip] = Clip(FaceMotion.MouthClip, 2, 0.5f);
            model.Animations[FaceMotion.BlinkClip] = Clip(FaceMotion.BlinkClip, 4, 0.6f);
            return model;
        }

        [Fact]
        public void Layer_OnTheRestPose_MatchesPlayingTheClip()
        {
            var skeleton = Skeleton();
            var mouth = Clip(FaceMotion.MouthClip, 2, 0.5f);
            const float time = 0.02f;
            var layered = SkeletonPoseEvaluator.ComputeBindPose(skeleton);
            FaceMotion.Layer(skeleton, layered, mouth, time, loop: false);
            var played = SkeletonPoseEvaluator.EvaluatePose(skeleton, mouth, time, loop: false);
            for (int j = 0; j < skeleton.Count; j++)
            {
                Assert.True(Vector3.Distance(played.Translations[j], layered.Translations[j]) < 1e-5f, $"joint {j} position");
                Assert.True(MathF.Abs(Quaternion.Dot(played.Rotations[j], layered.Rotations[j])) > 0.99999f, $"joint {j} rotation");
            }
        }

        [Fact]
        public void Layer_MovesTheJawAndLipOnly()
        {
            var skeleton = Skeleton();
            var pose = SkeletonPoseEvaluator.ComputeBindPose(skeleton);
            var before = pose.Translations.ToArray();
            FaceMotion.Layer(skeleton, pose, Clip(FaceMotion.MouthClip, 2, 0.5f), 1f, loop: false);
            Assert.Equal(before[1], pose.Translations[1]);
            Assert.Equal(before[2], pose.Translations[2]); // the pivot
            Assert.Equal(before[4], pose.Translations[4]);
            Assert.True(Vector3.Distance(before[3], pose.Translations[3]) > 0.01f);
        }

        [Fact]
        public void Mouth_PlaysOncePerSpokenLine_ThenStops()
        {
            var model = Model();
            float duration = model.Animations[FaceMotion.MouthClip].DurationSeconds;
            var face = new FaceMotion(new Random(1));
            face.Advance(0.5f, spokenLines: 3, canBlink: false, model); // the first call only takes the count
            Assert.Equal(-1f, face.MouthTime);
            face.Advance(0.5f, spokenLines: 4, canBlink: false, model);
            Assert.Equal(0f, face.MouthTime);
            face.Advance(duration / 2, spokenLines: 4, canBlink: false, model);
            Assert.Equal(duration / 2, face.MouthTime, 4);
            face.Advance(duration, spokenLines: 4, canBlink: false, model);
            Assert.Equal(-1f, face.MouthTime); // one play, no loop
            face.Advance(10f, spokenLines: 4, canBlink: false, model);
            Assert.Equal(-1f, face.MouthTime);
        }

        [Theory]
        [InlineData(10, 1)]   // Deraquien: "Intruders!"
        [InlineData(45, 3)]   // Joachim, Port Jeuno 324
        [InlineData(97, 5)]
        [InlineData(98, 5)]   // counted 6
        [InlineData(141, 8)]
        [InlineData(220, 12)] // Deraquien's long line
        [InlineData(0, 1)]
        public void FlapsFor_MatchesTheRetailCounts(int characters, int flaps)
        {
            // Flaps counted by eye in retail (2026-10-03, #198).
            Assert.Equal(flaps, FaceMotion.FlapsFor(characters));
        }

        [Fact]
        public void Mouth_FlapsTheLinesCount_ThenStops()
        {
            var model = Model();
            float flap = model.Animations[FaceMotion.MouthClip].DurationSeconds / FaceMotion.FlapsPerClip;
            var face = new FaceMotion(new Random(1));
            face.Advance(0f, spokenLines: 0, canBlink: false, model);
            face.Advance(0f, spokenLines: 1, canBlink: false, model, lineFlaps: 8);
            face.Advance(flap * 7.5f, spokenLines: 1, canBlink: false, model, lineFlaps: 8);
            Assert.True(face.MouthTime > 0f); // past one play of the clip: it loops
            face.Advance(flap, spokenLines: 1, canBlink: false, model, lineFlaps: 8);
            Assert.Equal(-1f, face.MouthTime);
        }

        [Fact]
        public void Mouth_StopsWhenThePlayerClosesTheLine()
        {
            // Retail 0x23 calls SpeakStop on the speaker once the line is confirmed (XiEvents OpCodes/0x0023).
            var model = Model();
            float flap = model.Animations[FaceMotion.MouthClip].DurationSeconds / FaceMotion.FlapsPerClip;
            var face = new FaceMotion(new Random(1));
            face.Advance(0f, spokenLines: 0, canBlink: false, model, speechStops: 0);
            face.Advance(0f, spokenLines: 1, canBlink: false, model, lineFlaps: 12, speechStops: 0);
            face.Advance(flap, spokenLines: 1, canBlink: false, model, lineFlaps: 12, speechStops: 0);
            Assert.True(face.MouthTime > 0f);
            face.Advance(0.01f, spokenLines: 1, canBlink: false, model, lineFlaps: 12, speechStops: 1);
            Assert.Equal(-1f, face.MouthTime);
        }

        [Fact]
        public void Mouth_ANewLineMidPlay_StartsAgain()
        {
            var model = Model();
            float duration = model.Animations[FaceMotion.MouthClip].DurationSeconds;
            var face = new FaceMotion(new Random(1));
            face.Advance(0f, spokenLines: 0, canBlink: false, model);
            face.Advance(0f, spokenLines: 1, canBlink: false, model);
            face.Advance(duration * 0.75f, spokenLines: 1, canBlink: false, model);
            face.Advance(0.01f, spokenLines: 2, canBlink: false, model);
            Assert.Equal(0f, face.MouthTime);
        }

        [Fact]
        public void Blink_StartsWithinTheInterval_AndEndsWithTheClip()
        {
            var model = Model();
            var face = new FaceMotion(new Random(7));
            float waited = 0f;
            while (face.BlinkTime < 0f && waited < FaceMotion.MaxBlinkInterval + 1f)
            {
                face.Advance(0.05f, spokenLines: 0, canBlink: true, model);
                waited += 0.05f;
            }
            Assert.InRange(waited, FaceMotion.MinBlinkInterval, FaceMotion.MaxBlinkInterval + 0.05f);
            face.Advance(model.Animations[FaceMotion.BlinkClip].DurationSeconds, spokenLines: 0, canBlink: true, model);
            Assert.Equal(-1f, face.BlinkTime);
        }

        [Fact]
        public void Blink_NeverStartsWhileItCannot()
        {
            var model = Model();
            var face = new FaceMotion(new Random(7));
            for (int i = 0; i < 400; i++) face.Advance(0.05f, spokenLines: 0, canBlink: false, model);
            Assert.Equal(-1f, face.BlinkTime);
        }

        [Fact]
        public void NoFaceClips_NoMotion()
        {
            var model = new EntityModel { Skeleton = Skeleton() };
            var face = new FaceMotion(new Random(7));
            for (int i = 0; i < 400; i++) face.Advance(0.05f, spokenLines: i, canBlink: true, model);
            Assert.Equal(-1f, face.MouthTime);
            Assert.Equal(-1f, face.BlinkTime);
        }

        /// <summary>Every race skeleton carries the mouth clip on one joint under the head and the blink clip.</summary>
        [Theory]
        [InlineData(CharacterRace.HumeMale, 54)]
        [InlineData(CharacterRace.HumeFemale, 32)]
        [InlineData(CharacterRace.ElvaanMale, 34)]
        [InlineData(CharacterRace.ElvaanFemale, 63)]
        [InlineData(CharacterRace.TaruMale, 11)]
        [InlineData(CharacterRace.TaruFemale, 11)]
        [InlineData(CharacterRace.Mithra, 46)]
        [InlineData(CharacterRace.Galka, 44)]
        public void RaceSkeleton_HasTheMouthAndBlinkClips(CharacterRace race, int mouthJoint)
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var container = EntityModelLoader.ParseDatContainer(rm.LoadDatBytes(CharacterEquipmentResolver.GetBaseSkeletonPath(race))!, "base");
            var skeleton = container.Skeleton!;
            var mouth = container.Animations.Single(c => c.Name == FaceMotion.MouthClip);
            Assert.Equal(new[] { mouthJoint }, mouth.Tracks.Keys.ToArray());
            Assert.Equal(2f, mouth.DurationSeconds, 3);
            Assert.Equal(HeadLook.HeadJoint(skeleton), skeleton.Joints[mouthJoint].Parent);
            var blink = container.Animations.Single(c => c.Name == FaceMotion.BlinkClip);
            Assert.InRange(blink.DurationSeconds, 0.1f, 0.4f);

            // The talking mouth moves the lips: the face vertices bound to the mouth joint shift during the clip.
            Assert.True(CharacterEquipmentResolver.TryResolveGearFileId(race, CharacterSlot.Face, 1, out int faceFile));
            var lips = EntityModelLoader.ParseDatContainer(rm.LoadDatBytesByFileId(faceFile)!, "face").Meshes
                .SelectMany(m => m.Vertices).Where(v => v.Joint0 == mouthJoint || v.Joint1 == mouthJoint).ToList();
            Assert.NotEmpty(lips);
            var rest = SkeletonPoseEvaluator.ComputeBindPose(skeleton);
            float moved = 0f;
            for (float time = 0f; time < mouth.DurationSeconds; time += 0.05f)
            {
                var open = SkeletonPoseEvaluator.ComputeBindPose(skeleton);
                FaceMotion.Layer(skeleton, open, mouth, time, loop: true);
                moved = Math.Max(moved, lips.Max(v => Vector3.Distance(SkeletonPoseEvaluator.SkinVertex(v, rest).Position, SkeletonPoseEvaluator.SkinVertex(v, open).Position)));
            }
            Assert.True(moved > 1e-3f, $"lips moved {moved}");
        }
    }
}
