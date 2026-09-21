// tests/Gordian.Core.Tests/Animation/EntityAnimationStateTests.cs
using Gordian.Core.Animation;
using Xunit;

namespace Gordian.Core.Tests.Animation
{
    public class EntityAnimationStateTests
    {
        [Fact]
        public void Advance_StartsAtIdleWithZeroElapsed()
        {
            var state = new EntityAnimationState();
            Assert.Equal(AnimationCategory.Idle, state.Current);
            Assert.Equal(0f, state.ElapsedSeconds);
        }

        [Fact]
        public void Advance_SameCategory_AccumulatesElapsedSeconds()
        {
            var state = new EntityAnimationState();
            state.Advance(0.5f, AnimationCategory.Walk); // transition into Walk: resets to 0
            state.Advance(0.25f, AnimationCategory.Walk);

            Assert.Equal(AnimationCategory.Walk, state.Current);
            Assert.Equal(0.25f, state.ElapsedSeconds, 3);
        }

        [Fact]
        public void Advance_CategoryChange_ResetsElapsedSecondsToZero()
        {
            var state = new EntityAnimationState();

            // First transition into Run: dt is discarded on the very frame the category changes,
            // so playback starts cleanly at frame 0 rather than mid-clip.
            state.Advance(2.0f, AnimationCategory.Run);
            Assert.Equal(AnimationCategory.Run, state.Current);
            Assert.Equal(0f, state.ElapsedSeconds);

            state.Advance(0.3f, AnimationCategory.Run);
            Assert.Equal(0.3f, state.ElapsedSeconds, 3);

            // Switching to Combat resets again.
            state.Advance(0.1f, AnimationCategory.Combat);

            Assert.Equal(AnimationCategory.Combat, state.Current);
            Assert.Equal(0f, state.ElapsedSeconds);
        }

        [Fact]
        public void Advance_WithModel_PerformsCrossFadeOverBlendDuration()
        {
            var model = new Gordian.Core.Resources.Models.EntityModel { Name = "TestMob" };
            var idleClip = new Gordian.Core.Resources.Models.AnimationClip { Name = "idl0", NumFrames = 30, KeyFrameDuration = 1.0f };
            var runClip = new Gordian.Core.Resources.Models.AnimationClip { Name = "run0", NumFrames = 30, KeyFrameDuration = 1.0f };
            model.Animations["idl0"] = idleClip;
            model.Animations["run0"] = runClip;

            var state = new EntityAnimationState { BlendDuration = 0.20f };

            // Initial frame sets idle
            state.Advance(0f, AnimationCategory.Idle, 0, model);
            Assert.Equal(idleClip, state.CurrentClip);
            Assert.False(state.IsBlending);

            // Transition to Run initiates cross-fade
            state.Advance(0.05f, AnimationCategory.Run, 0, model);
            Assert.Equal(runClip, state.CurrentClip);
            Assert.Equal(idleClip, state.PreviousClip);
            Assert.True(state.IsBlending);
            Assert.Equal(0.25f, state.BlendWeight, 2); // 0.05 / 0.20 = 0.25

            // Advance further into blend
            state.Advance(0.05f, AnimationCategory.Run, 0, model);
            Assert.Equal(0.50f, state.BlendWeight, 2); // (0.05+0.05) / 0.20 = 0.50

            // Complete blend duration
            state.Advance(0.10f, AnimationCategory.Run, 0, model);
            Assert.Equal(1.0f, state.BlendWeight, 2);
            Assert.False(state.IsBlending);
            Assert.Null(state.PreviousClip);
        }

        [Fact]
        public void Advance_WithModel_PlaysTransitionClipToCompletionThenSwitchesToStanceClip()
        {
            var omega = new Gordian.Core.Resources.Models.EntityModel { Name = "Omega" };
            var idl0 = new Gordian.Core.Resources.Models.AnimationClip { Name = "idl0", NumFrames = 30, KeyFrameDuration = 1.0f };
            var btl0 = new Gordian.Core.Resources.Models.AnimationClip { Name = "btl0", NumFrames = 30, KeyFrameDuration = 1.0f };
            var sp00 = new Gordian.Core.Resources.Models.AnimationClip { Name = "sp00", NumFrames = 6, KeyFrameDuration = 1.0f }; // (6-1)/30 = 0.1667s
            var btl1 = new Gordian.Core.Resources.Models.AnimationClip { Name = "1tl0", NumFrames = 30, KeyFrameDuration = 1.0f };

            omega.Animations["omeg"] = idl0;
            omega.Animations["idl0"] = idl0;
            omega.Animations["btl0"] = btl0;
            omega.Animations["sp00"] = sp00;
            omega.Animations["1tl0"] = btl1;

            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Combat, 0, omega);
            Assert.Equal("btl0", state.CurrentClip?.Name);
            Assert.False(state.IsPlayingTransition);

            // Change stance to bipedal (AnimationSub = 1) -> triggers sp00 transition
            state.Advance(0.05f, AnimationCategory.Combat, 1, omega);
            Assert.True(state.IsPlayingTransition);
            Assert.Equal("sp00", state.CurrentClip?.Name);
            Assert.Equal(1, state.CurrentStance);

            // Advance through transition clip duration
            state.Advance(0.15f, AnimationCategory.Combat, 1, omega); // total 0.20s > sp00 duration (0.1667s)
            Assert.False(state.IsPlayingTransition);
            Assert.Equal("1tl0", state.CurrentClip?.Name);
        }

        [Fact]
        public void Advance_WithModel_LocomotionInterruptsInFlightTransition()
        {
            var omega = new Gordian.Core.Resources.Models.EntityModel { Name = "Omega" };
            var idl0 = new Gordian.Core.Resources.Models.AnimationClip { Name = "idl0", NumFrames = 30, KeyFrameDuration = 1.0f };
            var sp00 = new Gordian.Core.Resources.Models.AnimationClip { Name = "sp00", NumFrames = 30, KeyFrameDuration = 1.0f };
            var wlk0 = new Gordian.Core.Resources.Models.AnimationClip { Name = "wlk0", NumFrames = 30, KeyFrameDuration = 1.0f };

            omega.Animations["omeg"] = idl0;
            omega.Animations["idl0"] = idl0;
            omega.Animations["sp00"] = sp00;
            omega.Animations["wlk0"] = wlk0;

            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Idle, 0, omega);

            // Shift into stance 1 -> starts sp00
            state.Advance(0.05f, AnimationCategory.Idle, 1, omega);
            Assert.True(state.IsPlayingTransition);
            Assert.Equal("sp00", state.CurrentClip?.Name);

            // Sudden locomotion (Walk) immediately interrupts the transition
            state.Advance(0.05f, AnimationCategory.Walk, 1, omega);
            Assert.False(state.IsPlayingTransition);
            Assert.Equal("wlk0", state.CurrentClip?.Name);
        }

        [Fact]
        public void Advance_ModelChanged_ImmediatelySwitchesClipAndResetsElapsed()
        {
            var humeModel = new Gordian.Core.Resources.Models.EntityModel { Name = "HumeModel" };
            var humeIdle = new Gordian.Core.Resources.Models.AnimationClip { Name = "idl0", NumFrames = 30, KeyFrameDuration = 1.0f };
            humeModel.Animations["idl0"] = humeIdle;

            var elvaanModel = new Gordian.Core.Resources.Models.EntityModel { Name = "ElvaanModel" };
            var elvaanIdle = new Gordian.Core.Resources.Models.AnimationClip { Name = "idl0", NumFrames = 40, KeyFrameDuration = 1.0f };
            elvaanModel.Animations["idl0"] = elvaanIdle;

            var state = new EntityAnimationState();

            // Initial evaluation with Hume model
            state.Advance(0f, AnimationCategory.Idle, 0, humeModel);
            Assert.Same(humeIdle, state.CurrentClip);
            Assert.Equal(0f, state.ElapsedSeconds);

            state.Advance(0.5f, AnimationCategory.Idle, 0, humeModel);
            Assert.Equal(0.5f, state.ElapsedSeconds, 3);

            // Appearance packet arrives: Elvaan model is provided without category or stance changing
            state.Advance(0f, AnimationCategory.Idle, 0, elvaanModel);

            // Must immediately bind Elvaan's clip and reset playback time to 0 to prevent skeleton distortion
            Assert.Same(elvaanIdle, state.CurrentClip);
            Assert.Equal(0f, state.ElapsedSeconds);
            Assert.Null(state.PreviousClip);
            Assert.False(state.IsBlending);
        }

        [Fact]
        public void Advance_FirstEvaluation_InitializesElapsedAndBlendWeightCleanly()
        {
            var model = new Gordian.Core.Resources.Models.EntityModel { Name = "TestModel" };
            var idleClip = new Gordian.Core.Resources.Models.AnimationClip { Name = "idl0", NumFrames = 30, KeyFrameDuration = 1.0f };
            model.Animations["idl0"] = idleClip;

            var state = new EntityAnimationState();

            // Legacy frames before model is loaded accumulate dt
            state.Advance(0.1f, AnimationCategory.Idle, 0, null);
            state.Advance(0.1f, AnimationCategory.Idle, 0, null);

            // Model arrives
            state.Advance(0f, AnimationCategory.Idle, 0, model);

            Assert.Same(idleClip, state.CurrentClip);
            Assert.Equal(0f, state.ElapsedSeconds);
            Assert.Null(state.PreviousClip);
            Assert.False(state.IsBlending);
        }
    }
}
