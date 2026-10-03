// tests/Gordian.Core.Tests/Animation/EntityAnimationActionTests.cs
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
    public class EntityAnimationActionTests
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

        /// <summary>A model with the stance clips, the given extra clips and the given Section 0x07 routines.</summary>
        private static EntityModel Model(IEnumerable<(string Name, float Seconds)> clips, params (string Name, byte[] Payload)[] routines)
        {
            var model = new EntityModel { Name = "Test" };
            foreach (var name in new[] { "idl", "btl", "wlk", "run" }) model.Animations[name] = Clip(name, 1f);
            foreach (var (name, seconds) in clips) model.Animations[name] = Clip(name, seconds);
            foreach (var (name, payload) in routines) model.RawMotionRoutines[name] = MotionRoutineDecoder.Decode(payload, name)!;
            model.RebuildMotionRoutines();
            return model;
        }

        private static (string, byte[]) Swing(string name, string clip, int total, int hitTick) =>
            (name, MotionRoutineDecoderTests.Routine(total,
                MotionRoutineDecoderTests.PlayClip(clip + "?", hitTick, total, 12, 20, 1),
                MotionRoutineDecoderTests.Link(0x03, "dada", total - hitTick)));

        private static ActionRequest Request(ActionMotion motion, IActionHitSink? sink = null, string routine = "", long? received = null) => new()
        {
            Motion = motion,
            Routine = routine,
            Hits = sink != null ? [new ActionHit(2, default)] : [],
            Sink = sink,
            ReceivedTimestamp = received ?? Stopwatch.GetTimestamp()
        };

        private static void Run(EntityAnimationState state, EntityModel model, AnimationCategory category, int ticks)
        {
            for (int i = 0; i < ticks; i++) state.Advance(Tick, category, 0, model);
        }

        /// <summary>
        /// An event gesture (#165) plays from the event motion bank loaded onto the entity, with the bank's clip, cutting
        /// off the action that was playing; a named stop ends it.
        /// </summary>
        [Fact]
        public void EventMotion_PlaysFromTheBank_ReplacesTheAction_AndStopsByName()
        {
            var model = Model([("at0", 2f)], Swing("ati0", "at0", 120, 60));
            var bankRoutine = MotionRoutineDecoder.Decode(MotionRoutineDecoderTests.Routine(40, MotionRoutineDecoderTests.PlayClip("tl1?", 0, 40, 4, 4, 1)), "tlk0")!;
            var bank = new EventMotionBank(32174, [Clip("tl10", 0.6f)], [bankRoutine]);
            Assert.Equal(40, bank.GetRoutineFrames("tlk0"));

            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Idle, 0, model);
            state.EnqueueAction(Request(ActionMotion.Swing));
            Run(state, model, AnimationCategory.Idle, 5);
            Assert.Equal("at0", state.CurrentClip!.Name);

            state.AddEventMotionBank(bank);
            Assert.Equal(40, state.GetRoutineFrames("tlk0"));
            state.EnqueueAction(Request(ActionMotion.EventMotion, routine: "tlk0"));
            state.Advance(Tick, AnimationCategory.Idle, 0, model);
            Assert.Equal("tlk0", state.ActiveRoutine!.Name);
            Assert.Equal("tl10", state.CurrentClip!.Name);

            state.EnqueueAction(Request(ActionMotion.EventMotionStop, routine: "tlk0"));
            state.Advance(Tick, AnimationCategory.Idle, 0, model);
            Assert.False(state.IsPlayingAction);

            state.ClearEventMotionBanks();
            Assert.Equal(0, state.GetRoutineFrames("tlk0"));
        }

        /// <summary>
        /// A gesture whose last clip loops until replaced (loop count 0, #193: package 9's <c>sha0</c> kneels down with
        /// <c>sm0</c> and holds the kneel <c>sm1</c>) keeps the pose past its routine's length, until the next gesture
        /// (<c>sha1</c> gets up) or a stop; the event still waits only one pass.
        /// </summary>
        [Fact]
        public void EventMotion_LoopingLastClip_HoldsThePoseUntilTheNextGesture()
        {
            var model = Model([]);
            var kneel = MotionRoutineDecoder.Decode(MotionRoutineDecoderTests.Routine(216,
                MotionRoutineDecoderTests.PlayClip("sm0?", 104, 104, 30, 0, 1),
                MotionRoutineDecoderTests.PlayClip("sm1?", 112, 112, 30, 0, 0)), "sha0")!;
            var getUp = MotionRoutineDecoder.Decode(MotionRoutineDecoderTests.Routine(108,
                MotionRoutineDecoderTests.PlayClip("sm2?", 108, 108, 30, 0, 1)), "sha1")!;
            var bank = new EventMotionBank(32721, [Clip("sm00", 1.7f), Clip("sm10", 1.8f), Clip("sm20", 1.8f)], [kneel, getUp]);
            Assert.True(bank.Routines["sha0"].HoldsLastClip);
            Assert.Equal(216, bank.GetRoutineFrames("sha0"));

            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Idle, 0, model);
            state.AddEventMotionBank(bank);
            state.EnqueueAction(Request(ActionMotion.EventMotion, routine: "sha0"));
            Run(state, model, AnimationCategory.Idle, 600);
            Assert.Equal("sha0", state.ActiveRoutine!.Name);
            Assert.Equal("sm10", state.CurrentClip!.Name);
            Assert.True(state.LoopsCurrentClip);

            state.EnqueueAction(Request(ActionMotion.EventMotion, routine: "sha1"));
            Run(state, model, AnimationCategory.Idle, 120);
            Assert.False(state.IsPlayingAction);
            Assert.Equal("idl", state.CurrentClip!.Name);

            state.EnqueueAction(Request(ActionMotion.EventMotion, routine: "sha0"));
            Run(state, model, AnimationCategory.Idle, 600);
            state.EnqueueAction(Request(ActionMotion.EventMotionStop)); // the event's end, or a 0x5E / 0x6B reset
            state.Advance(Tick, AnimationCategory.Idle, 0, model);
            Assert.False(state.IsPlayingAction);
        }

        /// <summary>
        /// A gesture given while the entity was off screen (not advanced) is not dropped as stale: it starts where it would
        /// be by now (#193: Joachim's kneel arrives during Port Jeuno 324's blink and must already be held when he is shown),
        /// and a short one that would be over has ended.
        /// </summary>
        [Fact]
        public void EventMotion_GivenOffScreen_CatchesUpInsteadOfBeingDropped()
        {
            var model = Model([]);
            var kneel = MotionRoutineDecoder.Decode(MotionRoutineDecoderTests.Routine(216,
                MotionRoutineDecoderTests.PlayClip("sm0?", 104, 104, 30, 0, 1),
                MotionRoutineDecoderTests.PlayClip("sm1?", 112, 112, 30, 0, 0)), "sha0")!;
            var talk = MotionRoutineDecoder.Decode(MotionRoutineDecoderTests.Routine(52,
                MotionRoutineDecoderTests.PlayClip("tl2?", 52, 52, 30, 0, 1)), "tlk1")!;
            var bank = new EventMotionBank(32721, [Clip("sm00", 1.7f), Clip("sm10", 1.8f), Clip("tl20", 0.8f)], [kneel, talk]);
            long fiveSecondsAgo = Stopwatch.GetTimestamp() - 5 * Stopwatch.Frequency;

            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Idle, 0, model);
            state.AddEventMotionBank(bank);
            state.EnqueueAction(Request(ActionMotion.EventMotion, routine: "sha0", received: fiveSecondsAgo));
            state.Advance(Tick, AnimationCategory.Idle, 0, model);
            Assert.Equal("sha0", state.ActiveRoutine!.Name);
            Assert.Equal("sm10", state.CurrentClip!.Name);

            state.EnqueueAction(Request(ActionMotion.EventMotion, routine: "tlk1", received: fiveSecondsAgo));
            state.Advance(Tick, AnimationCategory.Idle, 0, model);
            Assert.False(state.IsPlayingAction);
        }

        /// <summary>Outside an event, an action whose last clip loops until replaced still ends with its routine.</summary>
        [Fact]
        public void Action_LoopingLastClip_EndsWithItsRoutine()
        {
            var model = Model([("cor", 0.1f)], ("corp", MotionRoutineDecoderTests.Routine(2, MotionRoutineDecoderTests.PlayClip("cor?", 2, 2, 0, 0, 0))));
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Idle, 0, model);
            state.EnqueueAction(Request(ActionMotion.Routine, routine: "corp"));
            Run(state, model, AnimationCategory.Idle, 10);
            Assert.False(state.IsPlayingAction);
        }

        [Fact]
        public void Swing_PlaysRoutineClip_LandsHitAtHitTick_ThenBlendsBackToStance()
        {
            var model = Model([("at0", 1f)], Swing("ati0", "at0", 60, 30));
            var sink = new CountingSink();
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Combat, 0, model);
            Assert.Equal("btl", state.CurrentClip!.Name);

            state.EnqueueAction(Request(ActionMotion.Swing, sink));
            state.Advance(Tick, AnimationCategory.Combat, 0, model);

            Assert.True(state.IsPlayingAction);
            Assert.Equal("at0", state.CurrentClip!.Name);
            Assert.Equal("btl", state.PreviousClip!.Name); // blending in
            Assert.False(state.LoopsCurrentClip);
            Assert.Equal(0, sink.Deliveries);

            Run(state, model, AnimationCategory.Combat, 30);
            Assert.Equal(1, sink.Deliveries);

            Run(state, model, AnimationCategory.Combat, 30);
            Assert.False(state.IsPlayingAction);
            Assert.Equal("btl", state.CurrentClip!.Name);
            Assert.Equal("at0", state.PreviousClip!.Name);
            Assert.False(state.PreviousClipLoops); // the finished swing holds its last frame while fading out
            Assert.True(state.LoopsCurrentClip);
            Assert.Equal(1, sink.Deliveries);
        }

        [Fact]
        public void Swing_PicksAmongStandingSwingsWithRandomIndex()
        {
            var model = Model([("at0", 1f), ("at1", 1f)], Swing("ati0", "at0", 60, 30), Swing("ati1", "at1", 60, 30));
            var state = new EntityAnimationState { RandomIndex = count => count - 1 };
            state.Advance(0f, AnimationCategory.Combat, 0, model);

            state.EnqueueAction(Request(ActionMotion.Swing));
            state.Advance(Tick, AnimationCategory.Combat, 0, model);

            Assert.Equal("ati1", state.ActiveRoutine!.Name);
        }

        [Fact]
        public void Swing_WhileMoving_PlaysMovingSwingAndEndsWhenActorStopsAfterHit()
        {
            var model = Model([("at0", 1f), ("amf", 1f)], Swing("ati0", "at0", 60, 30), Swing("atf0", "amf", 60, 20));
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.CombatRun, 0, model);

            state.EnqueueAction(Request(ActionMotion.Swing));
            state.Advance(Tick, AnimationCategory.CombatRun, 0, model);
            Assert.Equal("atf0", state.ActiveRoutine!.Name);

            Run(state, model, AnimationCategory.CombatRun, 25); // past the hit, still running: keeps swinging
            Assert.True(state.IsPlayingAction);

            state.Advance(Tick, AnimationCategory.Combat, 0, model); // stops
            Assert.False(state.IsPlayingAction);
        }

        [Fact]
        public void Swing_WhileMovingWithoutMovingSwing_PlaysNothingButLandsHit()
        {
            var model = Model([("at0", 1f)], Swing("ati0", "at0", 60, 30));
            var sink = new CountingSink();
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.CombatRun, 0, model);

            state.EnqueueAction(Request(ActionMotion.Swing, sink));
            state.Advance(Tick, AnimationCategory.CombatRun, 0, model);

            Assert.False(state.IsPlayingAction);
            Assert.Equal(1, sink.Deliveries);
        }

        [Fact]
        public void StandingSwing_IsCutShortByMovingAndByDeath_AndStillLandsItsHit()
        {
            var model = Model([("at0", 1f), ("ded", 1f)], Swing("ati0", "at0", 60, 30));
            var sink = new CountingSink();
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Combat, 0, model);
            state.EnqueueAction(Request(ActionMotion.Swing, sink));
            state.Advance(Tick, AnimationCategory.Combat, 0, model);

            state.Advance(Tick, AnimationCategory.CombatRun, 0, model);
            Assert.False(state.IsPlayingAction);
            Assert.Equal(1, sink.Deliveries);

            var sink2 = new CountingSink();
            state.EnqueueAction(Request(ActionMotion.Swing, sink2));
            state.Advance(Tick, AnimationCategory.Combat, 0, model);
            Assert.True(state.IsPlayingAction);
            state.Advance(Tick, AnimationCategory.Death, 0, model);
            Assert.False(state.IsPlayingAction);
            Assert.Equal(1, sink2.Deliveries);
        }

        [Fact]
        public void Chant_LoopsUntilItsReleaseReplacesIt()
        {
            var model = Model([("mb0", 0.5f), ("mb1", 1f)],
                ("cabk", MotionRoutineDecoderTests.Routine(33, MotionRoutineDecoderTests.PlayClip("mb0?", 0, 33, 16, 10, 63))),
                ("shbk", MotionRoutineDecoderTests.Routine(60, MotionRoutineDecoderTests.PlayClip("mb1?", 0, 60, 10, 15, 1))));
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Combat, 0, model);

            state.EnqueueAction(Request(ActionMotion.Routine, routine: "cabk"));
            Run(state, model, AnimationCategory.Combat, 300);
            Assert.Equal("cabk", state.ActiveRoutine!.Name);
            Assert.True(state.LoopsCurrentClip);

            state.EnqueueAction(Request(ActionMotion.CastRelease));
            state.Advance(Tick, AnimationCategory.Combat, 0, model);
            Assert.Equal("shbk", state.ActiveRoutine!.Name);
            Assert.Equal("cabk", state.LastChantRoutine);
        }

        [Fact]
        public void Interrupt_EndsTheChant()
        {
            var model = Model([("mb0", 0.5f)],
                ("cabk", MotionRoutineDecoderTests.Routine(33, MotionRoutineDecoderTests.PlayClip("mb0?", 0, 33, 16, 10, 63))));
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Combat, 0, model);
            state.EnqueueAction(Request(ActionMotion.Routine, routine: "cabk"));
            Run(state, model, AnimationCategory.Combat, 10);

            state.EnqueueAction(Request(ActionMotion.Interrupt, routine: "spbk"));
            state.Advance(Tick, AnimationCategory.Combat, 0, model);

            Assert.False(state.IsPlayingAction);
            Assert.Equal("btl", state.CurrentClip!.Name);
        }

        [Fact]
        public void StaleRequest_IsSkippedAndItsHitShownAtOnce()
        {
            var model = Model([("at0", 1f)], Swing("ati0", "at0", 60, 30));
            var sink = new CountingSink();
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Combat, 0, model);

            long old = Stopwatch.GetTimestamp() - (long)(Stopwatch.Frequency * 3.0);
            state.EnqueueAction(Request(ActionMotion.Swing, sink, received: old));
            state.Advance(Tick, AnimationCategory.Combat, 0, model);

            Assert.False(state.IsPlayingAction);
            Assert.Equal(1, sink.Deliveries);
        }

        [Fact]
        public void HitReaction_FlinchesTowardFrontOrBackDamagePose()
        {
            var model = Model([("dfm", 0f), ("dbm", 0f)],
                ("damg", MotionRoutineDecoderTests.Routine(2, MotionRoutineDecoderTests.Flinch(24))));
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Combat, 0, model);

            state.EnqueueReaction(new HitReaction(ActionResolution.Hit, 0.5f, 0, false, ActionReactKind.None, 1, 0f, 0f, Stopwatch.GetTimestamp()));
            Run(state, model, AnimationCategory.Combat, 6); // a quarter of the 24-tick flinch: the peak

            var overlay = state.Overlay;
            Assert.NotNull(overlay);
            Assert.Equal("dbm", overlay!.Value.Clip.Name);
            Assert.InRange(overlay.Value.Weight, 0.45f, 0.5f);
            Assert.Null(overlay.Value.Reference); // no neutral twin on this model: an absolute blend

            Run(state, model, AnimationCategory.Combat, 20);
            Assert.Null(state.Overlay);
        }

        [Fact]
        public void HitReaction_UsesTheNeutralTwinAsReference()
        {
            var model = Model([("dfm", 0f), ("dfi", 0f), ("dbm", 0f), ("dbi6", 0f)]);
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Combat, 0, model);

            state.EnqueueReaction(new HitReaction(ActionResolution.Hit, 1f, 0, true, ActionReactKind.None, 1, 0f, 0f, Stopwatch.GetTimestamp()));
            Run(state, model, AnimationCategory.Combat, 6);
            Assert.Equal("dfi", state.Overlay!.Value.Reference!.Name);

            state.EnqueueReaction(new HitReaction(ActionResolution.Hit, 1f, 0, false, ActionReactKind.None, 1, 0f, 0f, Stopwatch.GetTimestamp()));
            Run(state, model, AnimationCategory.Combat, 6);
            Assert.Equal("dbi6", state.Overlay!.Value.Reference!.Name); // the PC motion packs name it with a 6
        }

        [Fact]
        public void HitReaction_WithoutDistortionOrOnMiss_DoesNotFlinch()
        {
            var model = Model([("dfm", 0f)]);
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Combat, 0, model);

            state.EnqueueReaction(new HitReaction(ActionResolution.Hit, 0f, 0, true, ActionReactKind.None, 1, 0f, 0f, Stopwatch.GetTimestamp()));
            state.EnqueueReaction(new HitReaction(ActionResolution.Miss, 1f, 0, true, ActionReactKind.None, 1, 0f, 0f, Stopwatch.GetTimestamp()));
            Run(state, model, AnimationCategory.Combat, 4);

            Assert.Null(state.Overlay);
        }

        [Fact]
        public void Parry_FlashesTheParryPose()
        {
            var model = Model([("gdm", 0f), ("pym", 0f)],
                ("pary", MotionRoutineDecoderTests.Routine(0, MotionRoutineDecoderTests.PoseFlash(1, 16))));
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Combat, 0, model);

            state.EnqueueReaction(new HitReaction(ActionResolution.Parry, 0f, 0, true, ActionReactKind.None, 1, 0f, 0f, Stopwatch.GetTimestamp()));
            Run(state, model, AnimationCategory.Combat, 4);

            Assert.Equal("pym", state.Overlay!.Value.Clip.Name);
        }

        [Fact]
        public void Counter_MakesTheTargetStrikeBack()
        {
            var model = Model([("at1", 1f)],
                ("cni0", MotionRoutineDecoderTests.Routine(60, MotionRoutineDecoderTests.PlayClip("at1?", 0, 60, 10, 20, 1))));
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Combat, 0, model);

            state.EnqueueReaction(new HitReaction(ActionResolution.Miss, 0f, 0, true, ActionReactKind.Counter, 1, 0f, 0f, Stopwatch.GetTimestamp()));
            state.Advance(Tick, AnimationCategory.Combat, 0, model);

            Assert.Equal("cni0", state.ActiveRoutine!.Name);
        }

        [Fact]
        public void Engaging_DrawsTheWeapon_AndDisengaging_SheathesIt()
        {
            var model = Model([("otd", 1.2f), ("ind", 1.2f)],
                ("out0", MotionRoutineDecoderTests.Routine(72, MotionRoutineDecoderTests.PlayClip("otd?", 36, 72, 8, 8, 1))),
                ("in 0", MotionRoutineDecoderTests.Routine(72, MotionRoutineDecoderTests.PlayClip("ind?", 36, 72, 8, 8, 1))));
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Idle, 0, model);
            Assert.Null(state.WeaponGripOverride);

            state.Advance(Tick, AnimationCategory.Combat, 0, model);
            Assert.Equal("out0", state.ActiveRoutine!.Name);
            Assert.False(state.WeaponGripOverride); // still at rest

            Run(state, model, AnimationCategory.Combat, 40);
            Assert.True(state.WeaponGripOverride); // in hand from halfway

            Run(state, model, AnimationCategory.Combat, 40);
            Assert.False(state.IsPlayingAction);
            Assert.Null(state.WeaponGripOverride);
            Assert.Equal("btl", state.CurrentClip!.Name);

            state.Advance(Tick, AnimationCategory.Idle, 0, model);
            Assert.Equal("in 0", state.ActiveRoutine!.Name);
            Assert.True(state.WeaponGripOverride); // still in hand
            Run(state, model, AnimationCategory.Idle, 80);
            Assert.Equal("idl", state.CurrentClip!.Name);
        }

        [Fact]
        public void OverlayEnvelope_RisesToPeakAtAQuarterAndFallsToZero()
        {
            Assert.Equal(0f, EntityAnimationState.OverlayEnvelope(0f));
            Assert.Equal(1f, EntityAnimationState.OverlayEnvelope(0.25f), 3);
            Assert.InRange(EntityAnimationState.OverlayEnvelope(0.6f), 0.2f, 0.8f);
            Assert.Equal(0f, EntityAnimationState.OverlayEnvelope(1f));
        }
    }
}
