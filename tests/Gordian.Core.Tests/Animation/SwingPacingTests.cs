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

        private static int FramesUntilNextSwing(EntityAnimationState state, EntityModel model)
        {
            int serial = state.ActionSerial, frames = 0;
            while (state.ActionSerial == serial && frames < 400)
            {
                state.Advance(Tick, AnimationCategory.Combat, 0, model);
                frames++;
            }
            return frames;
        }

        [Fact]
        public void CharacterSwing_PlaysWholeAtTheBasePace()
        {
            var model = Model(character: true);
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Combat, 0, model);
            state.EnqueueAction(Swing(Stopwatch.GetTimestamp()));
            state.Advance(Tick, AnimationCategory.Combat, 0, model);

            Assert.Equal(98 / 60f / EntityAnimationState.BaseSwingSeconds, state.ActionSpeed, 3);
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

        /// <summary>
        /// The hits of one round start <see cref="EntityAnimationState.BaseHitSpacingSeconds"/> apart at base speed: the
        /// second waits for its slot, then blends in over the first swing's last 0.1 s (round 1 played them back to back).
        /// </summary>
        [Fact]
        public void HitsOfARound_StartOneSpacingApart()
        {
            var model = Model(character: true);
            var sink = new CountingSink();
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Combat, 0, model);
            long t0 = Stopwatch.GetTimestamp();
            state.EnqueueAction(Swing(t0, sink));
            state.EnqueueAction(Swing(t0, sink));
            state.Advance(Tick, AnimationCategory.Combat, 0, model);
            int frames = FramesUntilNextSwing(state, model);
            Assert.InRange(frames, 52, 56); // 0.9 s
            Assert.Equal(1, sink.Deliveries);
        }

        /// <summary>
        /// The maintainer's full-haste side-by-side capture (2026-10-18): two-hit rounds about 1.55 s apart. Retail lands a
        /// hit about every 0.78 s; each swing fills its slot (plus <see cref="EntityAnimationState.SwingOverlapSeconds"/>).
        /// </summary>
        [Fact]
        public void HastedRounds_SpreadTheirHitsOverTheRound()
        {
            var model = Model(character: true);
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Combat, 0, model);
            long t1 = Stopwatch.GetTimestamp();
            long t0 = t1 - Seconds(1.55);
            state.EnqueueAction(Swing(t0));
            state.EnqueueAction(Swing(t0));
            state.Advance(Tick, AnimationCategory.Combat, 0, model);
            // The next round arrives 1.55 s after the first: two hits per 1.55 s.
            state.EnqueueAction(Swing(t1));
            state.EnqueueAction(Swing(t1));
            state.Advance(Tick, AnimationCategory.Combat, 0, model);
            Assert.Equal(0.775f, state.SwingBudgetSeconds, 2);

            FramesUntilNextSwing(state, model); // round 1's second hit (its slot was set before the cadence was known)
            FramesUntilNextSwing(state, model); // round 2's first hit, as round 1's second swing ends
            float expectedSpeed = 98 / 60f / (0.775f + EntityAnimationState.SwingOverlapSeconds);
            Assert.Equal(expectedSpeed, state.ActionSpeed, 2);
            Assert.InRange(FramesUntilNextSwing(state, model), 45, 49); // round 2's second hit 0.775 s after its first
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

        /// <summary>A fixed model without a known cadence plays its round's swings back to back, each whole.</summary>
        [Fact]
        public void FixedModel_PlaysItsRoundBackToBack()
        {
            var model = Model(character: false);
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Combat, 0, model);
            long now = Stopwatch.GetTimestamp();
            state.EnqueueAction(Swing(now));
            state.EnqueueAction(Swing(now));
            state.Advance(Tick, AnimationCategory.Combat, 0, model);
            // It gives way once its hit has landed and at most 0.3 s (18 ticks) of it is left: tick 80 of 98.
            Assert.InRange(FramesUntilNextSwing(state, model), 78, 82);
        }
    }
}
