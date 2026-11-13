// tests/Gordian.Core.Tests/Animation/LocomotionLayeringTests.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Gordian.Core.Animation;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Gordian.Tests.Shared;
using Xunit;

namespace Gordian.Core.Tests.Animation
{
    /// <summary>
    /// A melee swing while moving plays on the upper body over the walk or run on the lower body (#511 rounds 2-4): the body is
    /// split by the skeleton's hierarchy, and the locomotion keeps its own clip and clock under the swing.
    /// </summary>
    public class LocomotionLayeringTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const float Tick = 1f / 60f;

        // A small biped laid out like the retail PC skeletons: 0 root; 1 carries the mount joint 13 (as Elvaan male joint 1
        // carries 25 mounts); 2 the pelvis with the leg 3-6 (four deep) and the spine 7, chest 8, hands 9 / 10 and head 11; the
        // grip 12 on joint 1, re-parented into the right hand; the off-hand mount 14 on the pelvis, which the clips carry.
        private const int Pelvis = 2, Hips = 3, Leg = 5, Foot = 6, Spine = 7, Chest = 8, LeftHand = 9, Arm = 10, Head = 11, Grip = 12, Mount = 13, OffHand = 14;
        private static readonly IReadOnlyDictionary<int, int> GripInHand = new Dictionary<int, int> { [Grip] = Arm };
        private const int JointCount = 15;

        private static Skeleton Biped()
        {
            int[] parents = { -1, 0, 1, 2, 3, 4, 5, 2, 7, 8, 8, 8, 1, 1, 2 };
            var joints = parents.Select((parent, i) => new SkeletonJoint(parent, Quaternion.Identity, i == 0 ? Vector3.Zero : new Vector3(0, -0.2f, 0.05f * i))).ToArray();
            var references = Enumerable.Range(0, 128).Select(r => new JointReference((ushort)(r == 126 ? LeftHand : r == 127 ? Arm : 0), Vector3.Zero)).ToArray();
            return new Skeleton(joints, references);
        }

        /// <summary>A 31-frame clip turning each given joint about Y by its angle, alternating with no turn frame to frame.</summary>
        private static AnimationClip Clip(string name, params (int Joint, float Angle)[] turns)
        {
            var tracks = new Dictionary<int, BoneAnimationTrack>();
            for (int joint = 0; joint < JointCount; joint++)
            {
                float angle = turns.FirstOrDefault(t => t.Joint == joint).Angle;
                tracks[joint] = new BoneAnimationTrack
                {
                    JointIndex = joint,
                    Rotations = Enumerable.Range(0, 31).Select(f => Quaternion.CreateFromAxisAngle(Vector3.UnitY, f % 2 == 0 ? angle : angle * 0.5f)).ToArray(),
                    Translations = new Vector3[31],
                };
            }
            return new AnimationClip { Name = name, NumFrames = 31, KeyFrameDuration = 1f, Tracks = tracks };
        }

        [Fact]
        public void TheSplitFollowsTheHierarchy_FromTheHandsDownToTheLegs()
        {
            var skeleton = Biped();
            Assert.Equal(Spine, LocomotionLayering.UpperBodyRoot(skeleton));
            Assert.Equal(Pelvis, LocomotionLayering.Pelvis(skeleton));
            Assert.Equal(Chest, LocomotionLayering.Chest(skeleton));
            Assert.True(LocomotionLayering.IsSpine(skeleton, Spine) && LocomotionLayering.IsSpine(skeleton, Chest));
            Assert.False(LocomotionLayering.IsSpine(skeleton, Head) || LocomotionLayering.IsSpine(skeleton, Pelvis));
            foreach (int j in new[] { 0, 1, Pelvis, Hips, 4, Leg, Foot }) Assert.False(LocomotionLayering.IsUpperBody(skeleton, j, GripInHand));
            // The spine, the grip in the hand, and the mounts the clips move (on joint 1 or on the pelvis) follow the swing.
            foreach (int j in new[] { Spine, Chest, LeftHand, Arm, Head, Grip, Mount, OffHand }) Assert.True(LocomotionLayering.IsUpperBody(skeleton, j, GripInHand));
        }

        [Fact]
        public void ASkeletonWithoutHandReferences_IsNotSplit()
        {
            var skeleton = new Skeleton(Biped().Joints);
            Assert.Equal(-1, LocomotionLayering.UpperBodyRoot(skeleton));
        }

        [Fact]
        public void Combine_KeepsTheLocomotionsLegsAndTorso_AndTheSwingsArmsAroundTheChest()
        {
            var skeleton = Biped();
            var strafe = Clip("cmvl", (Pelvis, 0.8f), (Spine, -0.8f), (Leg, 0.5f), (OffHand, 0.3f));
            var swing = Clip("aml", (Pelvis, 0f), (Spine, 0.9f), (Arm, 1.2f), (OffHand, -0.6f));
            var lower = SkeletonPoseEvaluator.EvaluatePose(skeleton, strafe, 0f, true, GripInHand);
            var upper = SkeletonPoseEvaluator.EvaluatePose(skeleton, swing, 0f, false, GripInHand);

            var pose = LocomotionLayering.Combine(skeleton, upper, lower, GripInHand);

            // The legs and the torso up to the chest: the strafe's, twist included (round 6: the swing's square torso is not taken).
            foreach (int j in new[] { 0, 1, Pelvis, Hips, Leg, Foot, Spine, Chest })
            {
                Assert.Equal(lower.Rotations[j], pose.Rotations[j]);
                Assert.Equal(lower.Translations[j], pose.Translations[j]);
            }
            // The arms, head, grip and off-hand mount: the swing's, turned with the chest onto the strafe's chest.
            var turn = Quaternion.Normalize(lower.Rotations[Chest] * Quaternion.Inverse(upper.Rotations[Chest]));
            foreach (int j in new[] { LeftHand, Arm, Head, Grip, OffHand })
            {
                AssertClose(Quaternion.Normalize(turn * upper.Rotations[j]), pose.Rotations[j]);
                AssertClose(Vector3.Transform(upper.Translations[j] - upper.Translations[Chest], turn), pose.Translations[j] - pose.Translations[Chest]);
            }
            AssertClose(Vector3.Transform(upper.Translations[OffHand] - upper.Translations[LeftHand], turn), pose.Translations[OffHand] - pose.Translations[LeftHand]);
        }

        private static void AssertClose(Vector3 expected, Vector3 actual) => Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"{expected} vs {actual}");

        private static void AssertClose(Quaternion expected, Quaternion actual) =>
            Assert.True(MathF.Abs(Quaternion.Dot(expected, actual)) > 0.9999f, $"{expected} vs {actual}");

        private static EntityModel Model()
        {
            var model = new EntityModel { Name = "Test", Skeleton = Biped() };
            model.Animations["idl"] = Clip("idl");
            model.Animations["btl"] = Clip("btl");
            foreach (var name in new[] { "run", "wlk", "crun", "cwlk", "cmvl", "cmvr", "cmvb" }) model.Animations[name] = Clip(name, (Leg, 0.4f));
            foreach (var (routine, clip) in new[] { ("atf0", "amf"), ("atb0", "amb"), ("atl0", "aml"), ("atr0", "amr") })
            {
                model.Animations[clip] = Clip(clip, (Arm, 1f));
                model.RawMotionRoutines[routine] = MotionRoutineDecoder.Decode(MotionRoutineBuilder.Routine(68,
                    MotionRoutineBuilder.PlayClip(clip + "?", 40, 68, 4, 4, 1), MotionRoutineBuilder.Link(0x03, "dada", 28)), routine)!;
            }
            model.Animations["at0"] = Clip("at0", (Arm, 1f), (Leg, 0.2f));
            model.RawMotionRoutines["ati0"] = MotionRoutineDecoder.Decode(MotionRoutineBuilder.Routine(98, MotionRoutineBuilder.PlayClip("at0?", 0, 98, 4, 4, 1)), "ati0")!;
            model.RebuildMotionRoutines();
            return model;
        }

        private static ActionRequest Swing() => new()
        {
            Motion = ActionMotion.Swing, Category = ActionCategory.BasicAttack, ReceivedTimestamp = Stopwatch.GetTimestamp()
        };

        /// <summary>
        /// Round 4: a strafe, back step, walk or run swing keeps the exact leg motion the player had: the same clip, its time
        /// carrying straight on from before the swing.
        /// </summary>
        [Theory]
        [InlineData(AnimationCategory.CombatMoveLeft, "atl0", "cmvl")]
        [InlineData(AnimationCategory.CombatMoveRight, "atr0", "cmvr")]
        [InlineData(AnimationCategory.CombatMoveBackward, "atb0", "cmvb")]
        [InlineData(AnimationCategory.CombatWalk, "atf0", "cwlk")]
        [InlineData(AnimationCategory.CombatRun, "atf0", "crun")]
        public void ASwingWhileMoving_KeepsTheLegMotionThePlayerHad(AnimationCategory moving, string routine, string locomotion)
        {
            var model = Model();
            var state = new EntityAnimationState();
            for (int i = 0; i < 20; i++) state.Advance(Tick, moving, 0, model);
            Assert.Equal(locomotion, state.CurrentClip!.Name);
            float before = state.ElapsedSeconds;

            state.EnqueueAction(Swing());
            for (int i = 0; i < 10; i++) state.Advance(Tick, moving, 0, model);

            Assert.Equal(routine, state.ActiveRoutine?.Name);
            Assert.Same(model.Animations[locomotion], state.LowerBodyClip);
            Assert.Equal(before + 9 * Tick, state.LowerBodyElapsedSeconds, 4); // the swing's first frame starts it, nine more advance it
        }

        /// <summary>The lower body follows a change of locomotion during the swing, keeping the cycle's phase; stopping gives the legs back.</summary>
        [Fact]
        public void TheLowerBodyFollowsTheLocomotionAsItChanges()
        {
            var model = Model();
            var state = new EntityAnimationState();
            for (int i = 0; i < 5; i++) state.Advance(Tick, AnimationCategory.CombatRun, 0, model);
            state.EnqueueAction(Swing());
            state.Advance(Tick, AnimationCategory.CombatRun, 0, model);
            Assert.Same(model.Animations["crun"], state.LowerBodyClip);

            state.Advance(Tick, AnimationCategory.CombatMoveLeft, 0, model);
            Assert.Equal("atf0", state.ActiveRoutine?.Name);
            Assert.Same(model.Animations["cmvl"], state.LowerBodyClip);
            state.Advance(Tick, AnimationCategory.CombatMoveBackward, 0, model);
            Assert.Same(model.Animations["cmvb"], state.LowerBodyClip);

            state.Advance(Tick, AnimationCategory.Combat, 0, model); // stopped before the hit: the swing's own legs
            Assert.Equal("atf0", state.ActiveRoutine?.Name);
            Assert.Null(state.LowerBodyClip);
        }

        /// <summary>When the swing ends, the legs carry on in the same walk or run at the same time, without a restart.</summary>
        [Fact]
        public void AfterTheSwing_TheLegsCarryOnWithoutARestart()
        {
            var model = Model();
            var state = new EntityAnimationState();
            for (int i = 0; i < 5; i++) state.Advance(Tick, AnimationCategory.CombatMoveLeft, 0, model);
            state.EnqueueAction(Swing());
            float lowerTime = 0f;
            for (int i = 0; i < 200 && (state.ActiveRoutine != null || i == 0); i++)
            {
                lowerTime = state.LowerBodyElapsedSeconds;
                state.Advance(Tick, AnimationCategory.CombatMoveLeft, 0, model);
            }

            Assert.Null(state.ActiveRoutine);
            Assert.Same(model.Animations["cmvl"], state.CurrentClip);
            Assert.InRange(state.ElapsedSeconds, lowerTime, lowerTime + 2 * Tick);
            Assert.Same(model.Animations["cmvl"], state.LowerBodyClip); // through the upper body's blend back
        }

        [Fact]
        public void AStandingSwing_HasNoLowerBodyLayer()
        {
            var model = Model();
            var state = new EntityAnimationState();
            for (int i = 0; i < 5; i++) state.Advance(Tick, AnimationCategory.Combat, 0, model);

            state.EnqueueAction(Swing());
            state.Advance(Tick, AnimationCategory.Combat, 0, model);

            Assert.Equal("ati0", state.ActiveRoutine?.Name);
            Assert.Null(state.LowerBodyClip);
        }
    }
}
