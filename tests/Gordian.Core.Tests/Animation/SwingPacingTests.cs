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
            model.RawMotionRoutines["ati0"] = MotionRoutineDecoder.Decode(MotionRoutineBuilder.Routine(98,
                MotionRoutineBuilder.PlayClip("at0?", 36, 98, 12, 20, 1),
                MotionRoutineBuilder.Link(0x03, "dada", 62)), "ati0")!;
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

        /// <summary>A character's swing plays whole at twice its authored speed: 98 ticks in 49 frames.</summary>
        [Fact]
        public void CharacterSwing_PlaysWholeAtTwiceItsAuthoredSpeed()
        {
            var model = Model(character: true);
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Combat, 0, model);
            state.EnqueueAction(Swing(Stopwatch.GetTimestamp()));
            state.Advance(Tick, AnimationCategory.Combat, 0, model);

            Assert.Equal(EntityAnimationState.CharacterSwingSpeed, state.ActionSpeed);
            int frames = 0;
            while (state.IsPlayingAction && frames < 200)
            {
                state.Advance(Tick, AnimationCategory.Combat, 0, model);
                frames++;
            }
            Assert.InRange(frames, 47, 50);
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

        /// <summary>The hits of one round play back to back, each swing whole: the second starts as the first ends.</summary>
        [Fact]
        public void HitsOfARound_PlayBackToBack()
        {
            var model = Model(character: true);
            var sink = new CountingSink();
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Combat, 0, model);
            long t0 = Stopwatch.GetTimestamp();
            state.EnqueueAction(Swing(t0, sink));
            state.EnqueueAction(Swing(t0, sink));
            state.Advance(Tick, AnimationCategory.Combat, 0, model);
            Assert.InRange(FramesUntilNextSwing(state, model), 47, 50);
            Assert.Equal(1, sink.Deliveries);
        }

        /// <summary>
        /// Rounds that leave more time per hit than the swing takes (the full-haste side-by-side capture: two hits every
        /// 1.6-2.0 s) keep the character's speed; haste only shortens the wait between rounds.
        /// </summary>
        [Fact]
        public void RoundsWithTimeToSpare_KeepTheCharacterSpeed()
        {
            var model = Model(character: true);
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Combat, 0, model);
            long t1 = Stopwatch.GetTimestamp();
            state.EnqueueAction(Swing(t1 - Seconds(1.8)));
            state.EnqueueAction(Swing(t1 - Seconds(1.8)));
            state.Advance(Tick, AnimationCategory.Combat, 0, model);
            state.EnqueueAction(Swing(t1));
            state.Advance(Tick, AnimationCategory.Combat, 0, model);
            Assert.Equal(0.9f, state.SwingBudgetSeconds, 2);
            FramesUntilNextSwing(state, model);
            Assert.Equal(EntityAnimationState.CharacterSwingSpeed, state.ActionSpeed);
        }

        /// <summary>Rounds closer than their swings (two hits every 0.6 s: 0.3 s per hit) speed the swings up to fit, at most 4x.</summary>
        [Fact]
        public void RoundsCloserThanTheirSwings_SpeedThemUp()
        {
            var model = Model(character: true);
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Combat, 0, model);
            long t1 = Stopwatch.GetTimestamp();
            state.EnqueueAction(Swing(t1 - Seconds(0.6)));
            state.EnqueueAction(Swing(t1 - Seconds(0.6)));
            state.Advance(Tick, AnimationCategory.Combat, 0, model);
            state.EnqueueAction(Swing(t1));
            state.Advance(Tick, AnimationCategory.Combat, 0, model);
            Assert.Equal(0.3f, state.SwingBudgetSeconds, 2);
            FramesUntilNextSwing(state, model);
            Assert.Equal(System.Math.Min(98 / 60f / 0.3f, EntityAnimationState.MaxSwingSpeed), state.ActionSpeed, 2);
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
        /// Two viewports drawing one world each claim the frame's time: the second gets about none, so the entity advances
        /// once per frame (round 2, D11: a pop-out viewport and the main window on one character doubled every motion).
        /// </summary>
        [Fact]
        public void ClaimFrameTime_GivesTheSecondViewportOfAFrameNothing()
        {
            var state = new EntityAnimationState();
            Assert.Equal(Tick, state.ClaimFrameTime(Tick)); // first claim: the frame's own time
            Assert.True(state.ClaimFrameTime(Tick) < Tick / 2); // same frame, second viewport
            System.Threading.Thread.Sleep(20);
            Assert.Equal(Tick, state.ClaimFrameTime(Tick), 3); // next frame: capped at its delta
        }

        /// <summary>
        /// A frozen entity (MotStopFlag: the renderer does not advance it) still takes its hit reactions, so their sounds play,
        /// without a flinch (#328, D29: the Macro Test dummies are kept in Terror).
        /// </summary>
        [Fact]
        public void FrozenEntity_StillTakesItsHitReactions()
        {
            var state = new EntityAnimationState();
            var hit = new HitReaction(ActionResolution.Hit, 1f, 0, true, ActionReactKind.None, 3, 0f, 0f, Stopwatch.GetTimestamp(),
                default, ActionCategory.BasicAttack);
            state.EnqueueReaction(hit);
            state.TakeReactionsWhileStopped();
            Assert.Equal(1, state.ReactionSerial);
            Assert.Equal(hit, state.LastReaction);
            Assert.Null(state.Overlay);

            state.EnqueueReaction(hit with { Timestamp = Stopwatch.GetTimestamp() - Seconds(5) }); // stale: dropped
            state.TakeReactionsWhileStopped();
            Assert.Equal(1, state.ReactionSerial);
        }
    }
}
