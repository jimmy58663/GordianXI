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

        // A small biped: 0 root, 1 pelvis; the hips 2 with the leg 3; the spine 4 with the arm 5 and the head 6; the grip 7 on
        // the pelvis, re-parented into the arm's hand.
        private const int Pelvis = 1, Hips = 2, Leg = 3, Spine = 4, Arm = 5, Head = 6, Grip = 7;
        private static readonly IReadOnlyDictionary<int, int> GripInHand = new Dictionary<int, int> { [Grip] = Arm };

        private static Skeleton Biped() => new(new[]
        {
            new SkeletonJoint(-1, Quaternion.Identity, Vector3.Zero),
            new SkeletonJoint(0, Quaternion.Identity, new Vector3(0, -1, 0)),
            new SkeletonJoint(Pelvis, Quaternion.Identity, new Vector3(0, 0.1f, 0)),
            new SkeletonJoint(Hips, Quaternion.Identity, new Vector3(0, 0.5f, 0)),
            new SkeletonJoint(Pelvis, Quaternion.Identity, new Vector3(0, -0.4f, 0)),
            new SkeletonJoint(Spine, Quaternion.Identity, new Vector3(0.3f, 0, 0)),
            new SkeletonJoint(Spine, Quaternion.Identity, new Vector3(0, -0.3f, 0)),
            new SkeletonJoint(Pelvis, Quaternion.Identity, Vector3.Zero),
        });

        /// <summary>A 31-frame clip turning each given joint about Y by its angle, alternating with no turn frame to frame.</summary>
        private static AnimationClip Clip(string name, params (int Joint, float Angle)[] turns)
        {
            var tracks = new Dictionary<int, BoneAnimationTrack>();
            for (int joint = 0; joint < 8; joint++)
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
        public void TheSplitFollowsTheHierarchy()
        {
            var skeleton = Biped();
            Assert.Equal(Spine, LocomotionLayering.UpperBodyRoot(skeleton));
            foreach (int j in new[] { 0, Pelvis, Hips, Leg }) Assert.False(LocomotionLayering.IsUpperBody(skeleton, j, GripInHand));
            foreach (int j in new[] { Spine, Arm, Head, Grip }) Assert.True(LocomotionLayering.IsUpperBody(skeleton, j, GripInHand));
            Assert.False(LocomotionLayering.IsUpperBody(skeleton, Grip)); // only a grip in the hand goes with the swing
        }

        [Fact]
        public void Combine_KeepsTheLocomotionsLowerBody_AndTheSwingsUpperBodyFacing()
        {
            var skeleton = Biped();
            var strafe = Clip("cmvl", (Pelvis, 0.8f), (Spine, -0.8f), (Leg, 0.5f));
            var swing = Clip("aml", (Pelvis, 0f), (Arm, 1.2f));
            var lower = SkeletonPoseEvaluator.EvaluatePose(skeleton, strafe, 0f, true, GripInHand);
            var upper = SkeletonPoseEvaluator.EvaluatePose(skeleton, swing, 0f, false, GripInHand);

            var pose = LocomotionLayering.Combine(skeleton, upper, lower, GripInHand);

            foreach (int j in new[] { 0, Pelvis, Hips, Leg })
            {
                Assert.Equal(lower.Rotations[j], pose.Rotations[j]);
                Assert.Equal(lower.Translations[j], pose.Translations[j]);
            }
            foreach (int j in new[] { Spine, Arm, Head, Grip }) Assert.Equal(upper.Rotations[j], pose.Rotations[j]);
            Assert.Equal(lower.Translations[Spine], pose.Translations[Spine]); // the torso sits on the strafe's pelvis
            Assert.Equal(upper.Translations[Arm] - upper.Translations[Spine], pose.Translations[Arm] - pose.Translations[Spine]);
        }

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

        /// <summary>
        /// Retail data: on every race the split is the spine at the pelvis (Hume male joint 48 under joint 2), and the strafe
        /// clips turn the pelvis that every swing clip holds forward, which is why the legs read as forward until round 4.
        /// </summary>
        [Fact]
        public void RetailSkeletons_SplitAtTheSpine_AndTheStrafeTurnsThePelvis()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            foreach (var race in new[] { CharacterRace.HumeMale, CharacterRace.HumeFemale, CharacterRace.ElvaanMale, CharacterRace.TaruMale, CharacterRace.Mithra, CharacterRace.Galka })
            {
                var model = EntityModelLoader.AssembleCharacter(race, 1, new ushort[9], rm.LoadDatBytes, rm.LoadDatBytesByFileId)!;
                var skeleton = model.Skeleton!;
                int spine = LocomotionLayering.UpperBodyRoot(skeleton);
                Assert.True(spine > 0, $"{race}");
                int upper = Enumerable.Range(0, skeleton.Count).Count(j => LocomotionLayering.IsUpperBody(skeleton, j));
                Assert.True(upper > skeleton.Count / 3 && upper < skeleton.Count, $"{race}: {upper} of {skeleton.Count}");
            }

            var hume = EntityModelLoader.AssembleCharacter(CharacterRace.HumeMale, 1, new ushort[9], rm.LoadDatBytes, rm.LoadDatBytesByFileId)!;
            Assert.Equal(48, LocomotionLayering.UpperBodyRoot(hume.Skeleton!));
            Vector3 PelvisFacing(string clip) => Vector3.Transform(Vector3.UnitX,
                SkeletonPoseEvaluator.EvaluatePose(hume.Skeleton!, hume.Animations[clip], 0.2f, true).Rotations[2]);
            Assert.True(Vector3.Dot(PelvisFacing("cmvl"), PelvisFacing("aml")) < 0.8f); // about 45 degrees apart
            Assert.True(Vector3.Dot(PelvisFacing("crun"), PelvisFacing("amf")) > 0.99f);
        }
    }
}
