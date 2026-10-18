// tests/Gordian.Core.Tests/Animation/SwingPacingTests.cs
using System.Collections.Generic;
using System.Diagnostics;
using Gordian.Core.Animation;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.Tests.Resources;
using Xunit;

namespace Gordian.Core.Tests.Animation
{
    /// <summary>
    /// Melee swing pacing (#307): a character's swing plays whole at the base pace, faster when its rounds come closer
    /// than the swings, and a swing with another waiting finishes its return to guard quickly once its hit has landed.
    /// </summary>
    public class SwingPacingTests
    {
        private const float Tick = 1f / 60f;

        private sealed class CountingSink : IActionHitSink
        {
            public int Deliveries { get; private set; }
            public void DeliverHits(ActionRequest request) => Deliveries++;
        }

        private static AnimationClip Clip(string name, float seconds) => new()
        {
            Name = name,
            KeyFrameDuration = 1f,
            NumFrames = (int)(seconds * 30f) + 1
        };

        /// <summary>A model with a 98-tick swing <c>ati0</c> (clip <c>at0</c>) hitting at tick 36, like Hume male's.</summary>
        private static EntityModel Model(bool character)
        {
            var model = new EntityModel { Name = "Test", IsCharacter = character };
            foreach (var name in new[] { "idl", "btl", "wlk", "run" }) model.Animations[name] = Clip(name, 1f);
            model.Animations["at0"] = Clip("at0", 98 / 60f);
            model.RawMotionRoutines["ati0"] = MotionRoutineDecoder.Decode(MotionRoutineDecoderTests.Routine(98,
                MotionRoutineDecoderTests.PlayClip("at0?", 36, 98, 12, 20, 1),
                MotionRoutineDecoderTests.Link(0x03, "dada", 62)), "ati0")!;
            model.RebuildMotionRoutines();
            return model;
        }

        private static ActionRequest Swing(long received, IActionHitSink? sink = null) => new()
        {
            Motion = ActionMotion.Swing,
            Category = ActionCategory.BasicAttack,
            Hits = sink != null ? [new ActionHit(2, default)] : [],
            Sink = sink,
            ReceivedTimestamp = received
        };

        private static long Seconds(double s) => (long)(s * Stopwatch.Frequency);

        [Fact]
        public void CharacterSwing_PlaysWholeAtTheBasePace()
        {
            var model = Model(character: true);
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Combat, 0, model);
            state.EnqueueAction(Swing(Stopwatch.GetTimestamp()));
            state.Advance(Tick, AnimationCategory.Combat, 0, model);

            float expected = 98 / 60f / EntityAnimationState.BaseSwingSeconds;
            Assert.Equal(expected, state.ActionSpeed, 3);
            int frames = 0;
            while (state.IsPlayingAction && frames < 200)
            {
                state.Advance(Tick, AnimationCategory.Combat, 0, model);
                frames++;
            }
            Assert.InRange(frames * Tick, EntityAnimationState.BaseSwingSeconds - 0.05f, EntityAnimationState.BaseSwingSeconds + 0.05f);
        }

        [Fact]
        public void FixedModelSwing_KeepsItsAuthoredSpeed_WithoutACadence()
        {
            var model = Model(character: false);
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Combat, 0, model);
            state.EnqueueAction(Swing(Stopwatch.GetTimestamp()));
            state.Advance(Tick, AnimationCategory.Combat, 0, model);
            Assert.Equal(1f, state.ActionSpeed);
        }

        /// <summary>Two-hit rounds 0.8 s apart leave 0.4 s per hit: the next swings fit into it (about 4.5x, capped at 4).</summary>
        [Fact]
        public void HastedRounds_SpeedTheSwingsUpToFitTheTimePerHit()
        {
            var model = Model(character: true);
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Combat, 0, model);
            long t0 = Stopwatch.GetTimestamp();
            state.EnqueueAction(Swing(t0));
            state.EnqueueAction(Swing(t0));
            state.EnqueueAction(Swing(t0 + Seconds(0.8)));
            state.Advance(Tick, AnimationCategory.Combat, 0, model);

            Assert.Equal(0.4f, state.SwingBudgetSeconds, 2);
            float wanted = 98 / 60f / (0.4f * EntityAnimationState.SwingBudgetShare);
            Assert.Equal(System.Math.Min(wanted, EntityAnimationState.MaxSwingSpeed), state.ActionSpeed, 3);
        }

        [Fact]
        public void RoundsFarApart_SetNoCadence()
        {
            var model = Model(character: true);
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Combat, 0, model);
            long t0 = Stopwatch.GetTimestamp();
            state.EnqueueAction(Swing(t0 - Seconds(20)));
            state.EnqueueAction(Swing(t0));
            state.Advance(Tick, AnimationCategory.Combat, 0, model);
            Assert.Equal(0f, state.SwingBudgetSeconds);
        }

        /// <summary>
        /// A swing whose hit has landed with another swing waiting plays its return to guard within
        /// <see cref="EntityAnimationState.SwingCatchUpTicks"/> (at most <see cref="EntityAnimationState.MaxSwingSpeed"/>),
        /// and the waiting one starts; nothing is cut off mid-strike.
        /// </summary>
        [Fact]
        public void WaitingSwing_ShortensTheReturnToGuard_AfterTheHit()
        {
            var model = Model(character: false);
            var sink = new CountingSink();
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Combat, 0, model);
            long now = Stopwatch.GetTimestamp();
            state.EnqueueAction(Swing(now, sink));
            state.Advance(Tick, AnimationCategory.Combat, 0, model);
            int serial = state.ActionSerial;

            // Before its hit a waiting swing changes nothing.
            state.EnqueueAction(Swing(now, sink));
            for (int i = 0; i < 20; i++) state.Advance(Tick, AnimationCategory.Combat, 0, model);
            Assert.Equal(1f, state.ActionSpeed);
            Assert.Equal(0, sink.Deliveries);

            int frames = 0;
            while (state.ActionSerial == serial && frames < 200)
            {
                state.Advance(Tick, AnimationCategory.Combat, 0, model);
                frames++;
            }
            Assert.Equal(1, sink.Deliveries);
            // Hit at tick 36 (about 16 more frames), then the 62 ticks back to guard at the 4x cap (about 16 frames) instead of 62.
            Assert.InRange(frames, 16, 16 + (int)(62 / EntityAnimationState.MaxSwingSpeed) + 3);
        }
    }
}
