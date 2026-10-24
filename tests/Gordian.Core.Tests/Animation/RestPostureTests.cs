// tests/Gordian.Core.Tests/Animation/RestPostureTests.cs
using Gordian.Core.Animation;
using Gordian.Core.Resources.Models;
using Gordian.Core.World;
using Gordian.Engine.Animation;
using Xunit;

namespace Gordian.Core.Tests.Animation
{
    /// <summary>
    /// /heal and /sit postures (#343): the server status picks the seated category, which sits down once, loops the seated
    /// clip, and stands up when the status ends. The routine shapes follow the retail race skeletons (see
    /// <c>RestPostureRealDataTests</c>).
    /// </summary>
    public class RestPostureTests
    {
        /// <summary>A clip of <paramref name="frames"/> frames at 30 fps: (frames - 1) / 30 seconds.</summary>
        private static AnimationClip Clip(string name, int frames) => new() { Name = name, NumFrames = frames, KeyFrameDuration = 1.0f };

        private static MotionRoutine Routine(string name, params (string Clip, int Start, int Duration, int Loops)[] steps)
        {
            var segments = new MotionSegment[steps.Length];
            for (int i = 0; i < steps.Length; i++) segments[i] = new MotionSegment(steps[i].Clip, steps[i].Start, steps[i].Duration, 10, 0, steps[i].Loops, 1f);
            return new MotionRoutine { Name = name, TotalTicks = steps[^1].Start + steps[^1].Duration, Segments = segments };
        }

        /// <summary>A model shaped like a retail race skeleton: idle, walk, the rest routines and the sit routines.</summary>
        private static EntityModel Model()
        {
            var model = new EntityModel { Name = "Race" };
            foreach (var (name, frames) in new[] { ("idl", 31), ("wlk", 31), ("rx0", 31), ("rx1", 31), ("rx2", 16), ("si0", 31), ("si1", 31), ("si2", 31) })
            {
                model.Animations[name] = Clip(name, frames);
            }
            model.MotionRoutines["res0"] = Routine("res0", ("rx0", 0, 52, 1), ("rx1", 52, 93, 0));
            model.MotionRoutines["res1"] = Routine("res1", ("rx1", 0, 93, 0));
            model.MotionRoutines["res2"] = Routine("res2", ("rx2", 0, 56, 1));
            model.MotionRoutines["sit0"] = Routine("sit0", ("si0", 0, 120, 1), ("si1", 120, 150, 0));
            model.MotionRoutines["sit1"] = Routine("sit1", ("si1", 0, 150, 0));
            model.MotionRoutines["sit2"] = Routine("sit2", ("si2", 0, 120, 1));
            return model;
        }

        [Theory]
        [InlineData(RestPosture.StatusHealing, AnimationCategory.Healing)]
        [InlineData(RestPosture.StatusSitting, AnimationCategory.Sitting)]
        public void Classify_RestingStatus_StandingStill_IsSeated(byte status, AnimationCategory expected)
        {
            var entity = new WorldEntity(1, 100, EntityType.Player) { Hpp = 100, AnimationState = status };
            Assert.Equal(expected, AnimationStateClassifier.Classify(entity, isEngaged: false));
            Assert.Equal(expected, AnimationStateClassifier.Classify(entity, isEngaged: false, isLocalPlayer: true));
        }

        [Fact]
        public void Classify_LocalPlayerMovingWhileHealing_Walks()
        {
            // The player moved: /heal off is on its way, but the status still says Healing for a moment.
            var entity = new WorldEntity(1, 100, EntityType.Player) { Hpp = 100, AnimationState = RestPosture.StatusHealing, Speed = 10, SpeedBase = 50 };
            Assert.Equal(AnimationCategory.Walk, AnimationStateClassifier.Classify(entity, isEngaged: false, isLocalPlayer: true));
        }

        [Fact]
        public void Classify_DeadWhileHealingStatus_IsDeath()
        {
            var entity = new WorldEntity(1, 100, EntityType.Player) { Hpp = 0, AnimationState = RestPosture.StatusHealing };
            Assert.Equal(AnimationCategory.Death, AnimationStateClassifier.Classify(entity, isEngaged: false));
        }

        [Fact]
        public void Heal_SitsDown_ThenLoopsTheRestClip()
        {
            var model = Model();
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Idle, 0, model);
            Assert.Same(model.Animations["idl"], state.CurrentClip);

            state.Advance(0.016f, AnimationCategory.Healing, 0, model);
            Assert.True(state.IsPlayingTransition);
            Assert.Same(model.Animations["rx0"], state.CurrentClip);
            Assert.False(state.LoopsCurrentClip);

            // rx0 is 1 s; past it the seated loop plays and loops.
            state.Advance(0.6f, AnimationCategory.Healing, 0, model);
            state.Advance(0.6f, AnimationCategory.Healing, 0, model);
            Assert.False(state.IsPlayingTransition);
            Assert.Same(model.Animations["rx1"], state.CurrentClip);
            Assert.True(state.LoopsCurrentClip);
        }

        [Fact]
        public void HealEnds_StandsUp_ThenIdles()
        {
            var model = Model();
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Healing, 0, model);
            Assert.Same(model.Animations["rx1"], state.CurrentClip);

            state.Advance(0.016f, AnimationCategory.Idle, 0, model);
            Assert.True(state.IsPlayingTransition);
            Assert.Same(model.Animations["rx2"], state.CurrentClip);

            // rx2 is 0.5 s.
            state.Advance(0.3f, AnimationCategory.Idle, 0, model);
            state.Advance(0.3f, AnimationCategory.Idle, 0, model);
            Assert.False(state.IsPlayingTransition);
            Assert.Same(model.Animations["idl"], state.CurrentClip);
        }

        [Fact]
        public void AlreadyHealing_WhenFirstSeen_ShowsTheLoopWithoutSittingDown()
        {
            var model = Model();
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Healing, 0, model);
            Assert.False(state.IsPlayingTransition);
            Assert.Same(model.Animations["rx1"], state.CurrentClip);
        }

        [Fact]
        public void MovingOutOfHeal_GoesStraightToTheWalk()
        {
            var model = Model();
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Healing, 0, model);
            state.Advance(0.016f, AnimationCategory.Walk, 0, model);
            Assert.False(state.IsPlayingTransition);
            Assert.Same(model.Animations["wlk"], state.CurrentClip);
        }

        [Fact]
        public void Sit_UsesTheSitRoutines()
        {
            var model = Model();
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Idle, 0, model);
            state.Advance(0.016f, AnimationCategory.Sitting, 0, model);
            Assert.Same(model.Animations["si0"], state.CurrentClip);
            state.Advance(1.1f, AnimationCategory.Sitting, 0, model);
            Assert.Same(model.Animations["si1"], state.CurrentClip);
            state.Advance(0.016f, AnimationCategory.Idle, 0, model);
            Assert.Same(model.Animations["si2"], state.CurrentClip);
        }

        [Fact]
        public void ModelWithoutRestClips_StandsIdle()
        {
            var model = new EntityModel { Name = "Mob" };
            model.Animations["idl0"] = Clip("idl0", 31);
            // A routine naming a clip the model lacks (retail monsters 86-89 carry sit0 without si0) changes nothing.
            model.MotionRoutines["sit0"] = Routine("sit0", ("si0", 0, 120, 1));
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Idle, 0, model);
            state.Advance(0.016f, AnimationCategory.Healing, 0, model);
            state.Advance(0.016f, AnimationCategory.Sitting, 0, model);
            Assert.False(state.IsPlayingTransition);
            Assert.Same(model.Animations["idl0"], state.CurrentClip);
            Assert.Same(model.Animations["idl0"], NpcStanceResolver.ResolveTargetClip(model, AnimationCategory.Healing));
        }

        [Fact]
        public void IsStandingUp_FromTheStatusChangeUntilTheStandUpEnds()
        {
            var model = Model();
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Healing, 0, model);
            Assert.False(state.IsStandingUp);

            state.Advance(0.016f, AnimationCategory.Idle, 0, model);
            Assert.True(state.IsStandingUp);
            state.Advance(0.3f, AnimationCategory.Idle, 0, model);
            Assert.True(state.IsStandingUp);
            state.Advance(0.3f, AnimationCategory.Idle, 0, model); // rx2 is 0.5 s
            Assert.False(state.IsStandingUp);
        }

        [Fact]
        public void IsStandingUp_NotWhileSittingDown()
        {
            var model = Model();
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Idle, 0, model);
            state.Advance(0.016f, AnimationCategory.Healing, 0, model);
            Assert.True(state.IsPlayingTransition);
            Assert.False(state.IsStandingUp);
        }

        [Fact]
        public void Hold_RootsWhileSeated_ThroughTheStandUp_ThenReleases()
        {
            var model = Model();
            var state = new EntityAnimationState();
            var hold = new Gordian.Engine.Input.SeatedMovementHold();
            state.Advance(0f, AnimationCategory.Healing, 0, model);

            Assert.True(hold.IsRooted(RestPosture.StatusHealing, state, 0.0));
            // /heal off arrived; the renderer has not drawn the change yet: still rooted.
            Assert.True(hold.IsRooted(0, state, 0.1));
            state.Advance(0.016f, AnimationCategory.Idle, 0, model);
            Assert.True(hold.IsRooted(0, state, 0.2));
            state.Advance(0.6f, AnimationCategory.Idle, 0, model); // the stand-up ends
            Assert.False(hold.IsRooted(0, state, 0.8));
            Assert.False(hold.IsRooted(0, state, 0.9));
        }

        [Fact]
        public void Hold_SitTooAndNeverPastTheCap()
        {
            var state = new EntityAnimationState(); // never drawn: stays "seated" forever
            state.Advance(0f, AnimationCategory.Sitting, 0, Model());
            var hold = new Gordian.Engine.Input.SeatedMovementHold();
            Assert.True(hold.IsRooted(RestPosture.StatusSitting, state, 10.0));
            Assert.True(hold.IsRooted(0, state, 10.5));
            Assert.False(hold.IsRooted(0, state, 10.5 + Gordian.Engine.Input.SeatedMovementHold.MaxStandUpSeconds));
        }

        [Fact]
        public void Hold_NeverSeated_NeverRoots()
        {
            var hold = new Gordian.Engine.Input.SeatedMovementHold();
            Assert.False(hold.IsRooted(0, new EntityAnimationState(), 0.0));
            Assert.False(hold.IsRooted(1, new EntityAnimationState(), 1.0));
        }

        [Fact]
        public void LoopClip_FallsBackToTheEnterRoutinesLastStep()
        {
            var model = Model();
            model.MotionRoutines.Remove("res1");
            Assert.Same(model.Animations["rx1"], RestPosture.LoopClip(model, AnimationCategory.Healing));
        }
    }
}
