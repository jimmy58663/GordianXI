// tests/Gordian.Core.Tests/Animation/LocomotionLayeringTests.cs
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
    /// A melee swing while moving keeps the walk or run on the legs (#511 round 2): the moving swings animate only the upper
    /// body and hold the legs still, so the joints they leave still come from the locomotion clip.
    /// </summary>
    public class LocomotionLayeringTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const float Tick = 1f / 60f;
        private const int Arm = 1, Leg = 2;

        /// <summary>A 31-frame clip turning the given joints back and forth, and holding joints 0-2's others still.</summary>
        private static AnimationClip Clip(string name, params int[] moving)
        {
            var tracks = new Dictionary<int, BoneAnimationTrack>();
            for (int joint = 0; joint < 3; joint++)
            {
                bool moves = moving.Contains(joint);
                tracks[joint] = new BoneAnimationTrack
                {
                    JointIndex = joint,
                    Rotations = Enumerable.Range(0, 31).Select(f => moves ? Quaternion.CreateFromAxisAngle(Vector3.UnitX, f % 2 == 0 ? 0f : 0.8f) : Quaternion.Identity).ToArray(),
                    Translations = new Vector3[31],
                };
            }
            return new AnimationClip { Name = name, NumFrames = 31, KeyFrameDuration = 1f, Tracks = tracks };
        }

        [Fact]
        public void Over_TakesTheJointsTheActionHoldsStill_FromTheLocomotion()
        {
            var swing = Clip("amf", Arm);
            var run = Clip("crun", Arm, Leg);

            var layered = LocomotionLayering.Over(swing, run, clipRate: 2f);

            Assert.Same(swing.Tracks[Arm], layered.Tracks[Arm]); // the swing keeps the arm
            Assert.Same(run.Tracks[Leg].Rotations, layered.Tracks[Leg].Rotations); // the run keeps the leg
            Assert.True(layered.Tracks[Leg].LoopsAlways);
            Assert.Equal(run.DurationSeconds * 2f, layered.Tracks[Leg].PartDurationSeconds, 4);
            Assert.Same(swing.Tracks[0], layered.Tracks[0]); // neither moves joint 0
            Assert.Same(swing, LocomotionLayering.Over(swing, Clip("idl"), 1f));
        }

        private static EntityModel Model()
        {
            var model = new EntityModel { Name = "Test" };
            foreach (var name in new[] { "idl", "btl", "wlk", "run", "crun", "cwlk", "cmvl", "cmvr", "cmvb" }) model.Animations[name] = Clip(name, Arm, Leg);
            foreach (var (routine, clip) in new[] { ("atf0", "amf"), ("atb0", "amb"), ("atl0", "aml"), ("atr0", "amr") })
            {
                model.Animations[clip] = Clip(clip, Arm);
                model.RawMotionRoutines[routine] = MotionRoutineDecoder.Decode(MotionRoutineBuilder.Routine(68, MotionRoutineBuilder.PlayClip(clip + "?", 40, 68, 4, 4, 1), MotionRoutineBuilder.Link(0x03, "dada", 28)), routine)!;
            }
            model.Animations["at0"] = Clip("at0", Arm, Leg);
            model.RawMotionRoutines["ati0"] = MotionRoutineDecoder.Decode(MotionRoutineBuilder.Routine(98, MotionRoutineBuilder.PlayClip("at0?", 0, 98, 4, 4, 1)), "ati0")!;
            model.RebuildMotionRoutines();
            return model;
        }

        private static ActionRequest Swing() => new()
        {
            Motion = ActionMotion.Swing, Category = ActionCategory.BasicAttack, ReceivedTimestamp = Stopwatch.GetTimestamp()
        };

        [Fact]
        public void ASwingWhileRunning_LeavesTheRunOnTheLegs()
        {
            var model = Model();
            var state = new EntityAnimationState();
            for (int i = 0; i < 5; i++) state.Advance(Tick, AnimationCategory.CombatRun, 0, model);

            state.EnqueueAction(Swing());
            state.Advance(Tick, AnimationCategory.CombatRun, 0, model);

            Assert.Equal("atf0", state.ActiveRoutine?.Name);
            Assert.Equal("amf", state.CurrentClip!.Name);
            Assert.Same(model.Animations["crun"].Tracks[Leg].Rotations, state.CurrentClip.Tracks[Leg].Rotations);
            Assert.Same(model.Animations["amf"].Tracks[Arm], state.CurrentClip.Tracks[Arm]);
        }

        private static void AssertLegsFrom(EntityModel model, EntityAnimationState state, string locomotion) =>
            Assert.Same(model.Animations[locomotion].Tracks[Leg].Rotations, state.CurrentClip!.Tracks[Leg].Rotations);

        /// <summary>Round 3: the legs under a swing follow the strafe and the back step, not the forward run.</summary>
        [Theory]
        [InlineData(AnimationCategory.CombatMoveLeft, "atl0", "aml", "cmvl")]
        [InlineData(AnimationCategory.CombatMoveRight, "atr0", "amr", "cmvr")]
        [InlineData(AnimationCategory.CombatMoveBackward, "atb0", "amb", "cmvb")]
        [InlineData(AnimationCategory.CombatWalk, "atf0", "amf", "cwlk")]
        public void ASwingWhileStrafingOrBacking_KeepsThatLocomotionOnTheLegs(AnimationCategory moving, string routine, string clip, string locomotion)
        {
            var model = Model();
            var state = new EntityAnimationState();
            for (int i = 0; i < 5; i++) state.Advance(Tick, moving, 0, model);

            state.EnqueueAction(Swing());
            state.Advance(Tick, moving, 0, model);

            Assert.Equal(routine, state.ActiveRoutine?.Name);
            Assert.Equal(clip, state.CurrentClip!.Name);
            AssertLegsFrom(model, state, locomotion);
            Assert.Same(model.Animations[clip].Tracks[Arm], state.CurrentClip.Tracks[Arm]);
        }

        /// <summary>
        /// Round 3: the legs follow a change of direction, walk or run and stopping while the swing plays, keeping the swing's
        /// own clip and its time.
        /// </summary>
        [Fact]
        public void TheLegsFollowTheLocomotionAsItChangesDuringTheSwing()
        {
            var model = Model();
            var state = new EntityAnimationState();
            for (int i = 0; i < 5; i++) state.Advance(Tick, AnimationCategory.CombatRun, 0, model);
            state.EnqueueAction(Swing());
            state.Advance(Tick, AnimationCategory.CombatRun, 0, model);
            AssertLegsFrom(model, state, "crun");

            state.Advance(Tick, AnimationCategory.CombatMoveLeft, 0, model);
            Assert.Equal("atf0", state.ActiveRoutine?.Name); // the swing goes on
            AssertLegsFrom(model, state, "cmvl");
            float elapsed = state.ElapsedSeconds;
            state.Advance(Tick, AnimationCategory.CombatMoveBackward, 0, model);
            AssertLegsFrom(model, state, "cmvb");
            Assert.True(state.ElapsedSeconds > elapsed); // the swing's clip time carries on
            state.Advance(Tick, AnimationCategory.CombatWalk, 0, model);
            AssertLegsFrom(model, state, "cwlk");

            state.Advance(Tick, AnimationCategory.Combat, 0, model); // stopped before the hit: the swing's own legs
            Assert.Equal("atf0", state.ActiveRoutine?.Name);
            Assert.Same(model.Animations["amf"], state.CurrentClip);
        }

        [Fact]
        public void AStandingSwing_KeepsItsOwnLegs()
        {
            var model = Model();
            var state = new EntityAnimationState();
            for (int i = 0; i < 5; i++) state.Advance(Tick, AnimationCategory.Combat, 0, model);

            state.EnqueueAction(Swing());
            state.Advance(Tick, AnimationCategory.Combat, 0, model);

            Assert.Same(model.Animations["at0"], state.CurrentClip);
        }

        /// <summary>
        /// The retail Hume male: <c>amf</c> holds the legs still (joint 30, a knee, keeps one angle), and a swing while running
        /// moves joint 30 as the run does.
        /// </summary>
        [Fact]
        public void RetailHumeMale_RunningSwingMovesTheLegs()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var model = EntityModelLoader.AssembleCharacter(CharacterRace.HumeMale, 1, new ushort[9], rm.LoadDatBytes, rm.LoadDatBytesByFileId)!;
            Assert.True(LocomotionLayering.HoldsStill(model.Animations["amf"], 30));
            Assert.False(LocomotionLayering.HoldsStill(model.Animations["crun"], 30));

            var state = new EntityAnimationState();
            for (int i = 0; i < 30; i++) state.Advance(Tick, AnimationCategory.CombatRun, 0, model);
            state.EnqueueAction(Swing());
            var angles = new List<float>();
            for (int i = 0; i < 30; i++)
            {
                state.Advance(Tick, AnimationCategory.CombatRun, 0, model);
                Assert.Equal("amf", state.CurrentClip!.Name);
                angles.Add(SkeletonPoseEvaluator.EvaluatePose(model.Skeleton!, state.CurrentClip, state.ElapsedSeconds, state.LoopsCurrentClip).Rotations[30].X);
            }
            Assert.True(angles.Max() - angles.Min() > 0.3f);
        }
    }
}
