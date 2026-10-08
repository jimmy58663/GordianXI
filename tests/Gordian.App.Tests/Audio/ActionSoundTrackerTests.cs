// tests/Gordian.App.Tests/Audio/ActionSoundTrackerTests.cs
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gordian.App.Audio;
using Gordian.Core.Animation;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Models;
using Xunit;

namespace Gordian.App.Tests.Audio
{
    /// <summary>Combat and action sounds on the routine clock (#41).</summary>
    public class ActionSoundTrackerTests
    {
        private static readonly EntityModel Monster = new() { Name = "monster" };
        private static readonly EntityModel Fighter = new() { Name = "fighter" };

        private static readonly Dictionary<(EntityModel, string, bool), RoutineSoundCue[]> Sounds = new()
        {
            [(Monster, "ati0", false)] = new[] { new RoutineSoundCue(0, new[] { 6062 }, 0x0A), new RoutineSoundCue(20, new[] { 6064 }, 0x0A) },
            [(Monster, "atk0", false)] = new[] { new RoutineSoundCue(1, new[] { 0, 0, 0, 220003, 220004, 220005, 220006 }, 0x0A) },
            [(Monster, "damg", false)] = new[] { new RoutineSoundCue(0, System.Array.Empty<int>(), 0x09, "chit"), new RoutineSoundCue(2, new[] { 0, 0, 7001 }, 0x0A) },
            [(Monster, "dead", false)] = new[] { new RoutineSoundCue(0, new[] { 7100 }, 0x0A) },
            [(Fighter, "chit", false)] = new[] { new RoutineSoundCue(0, new[] { 6031 }, 0x0A) },
            [(Fighter, "chit", true)] = new[] { new RoutineSoundCue(0, new[] { 6033 }, 0x0A) },
            [(Fighter, "ati0", false)] = new[] { new RoutineSoundCue(34, new[] { 6030 }, 0x0A) },
            [(Fighter, "ati0", true)] = new[] { new RoutineSoundCue(34, new[] { 6032 }, 0x0A) },
        };

        private static ActionSoundTracker NewTracker(int pick = 0) =>
            new(_ => pick, (model, name, offHand) => Sounds.TryGetValue((model, name, offHand), out var cues) ? cues : System.Array.Empty<RoutineSoundCue>());

        private static ActorSoundState Actor(uint id, EntityModel model, string? routine = null, int serial = 0, float ticks = 0,
            int reactions = 0, HitReaction reaction = default, bool dead = false, bool attack = false, bool offHand = false) =>
            new(id, new Vector3(id, 0, 0), model, routine, serial, ticks, reactions, reaction, dead, attack, offHand);

        private static List<int> Step(ActionSoundTracker tracker, ActorSoundState actor, params ActorSoundState[] others)
        {
            var into = new List<ActionSoundEvent>();
            tracker.Step(actor, id => others.Where(o => o.Id == id).Select(o => (ActorSoundState?)o).FirstOrDefault(), into);
            tracker.Forget();
            return into.Select(e => e.SoundId).ToList();
        }

        private static HitReaction HitFrom(uint attacker) => new(ActionResolution.Hit, 0.5f, 0, true, default, attacker, 0, 0, 0);

        [Fact]
        public void Swing_PlaysItsCuesAsTheRoutineClockPassesThem()
        {
            var tracker = NewTracker();
            Assert.Empty(Step(tracker, Actor(1, Monster)));                                  // first seen
            Assert.Equal(new[] { 6062 }, Step(tracker, Actor(1, Monster, "ati0", 1, 2)));    // started: tick 0
            Assert.Empty(Step(tracker, Actor(1, Monster, "ati0", 1, 10)));
            Assert.Equal(new[] { 6064 }, Step(tracker, Actor(1, Monster, "ati0", 1, 25)));
            Assert.Equal(new[] { 6062 }, Step(tracker, Actor(1, Monster, "ati0", 2, 1)));    // the same routine again
        }

        [Fact]
        public void AttackRound_AddsTheAttackCry()
        {
            var tracker = NewTracker(pick: 4);
            Step(tracker, Actor(1, Monster));
            Assert.Equal(new[] { 6062, 220004 }, Step(tracker, Actor(1, Monster, "ati0", 1, 2, attack: true)));
            Assert.Empty(Step(tracker, Actor(1, Monster, "ati0", 1, 10, attack: true)));      // once per swing
            Assert.Equal(new[] { 6062 }, Step(tracker, Actor(1, Monster, "ati0", 2, 2)));     // not a melee attack: no cry
        }

        [Fact]
        public void AttackCry_CanBeSilent()
        {
            var tracker = NewTracker(pick: 1);
            Step(tracker, Actor(1, Monster));
            Assert.Equal(new[] { 6062 }, Step(tracker, Actor(1, Monster, "ati0", 1, 2, attack: true)));
        }

        [Fact]
        public void FirstSighting_DoesNotReplayARoutineAlreadyPlaying()
        {
            var tracker = NewTracker();
            Assert.Empty(Step(tracker, Actor(1, Monster, "ati0", 5, 10)));
            Assert.Equal(new[] { 6064 }, Step(tracker, Actor(1, Monster, "ati0", 5, 30)));
        }

        [Fact]
        public void Hit_PlaysTheAttackersHitSoundAndTheTargetsCry()
        {
            var tracker = NewTracker(pick: 2);
            var attacker = Actor(7, Fighter);
            Step(tracker, Actor(1, Monster), attacker);
            Assert.Equal(new[] { 6031, 7001 }, Step(tracker, Actor(1, Monster, reactions: 1, reaction: HitFrom(7)), attacker));
            Assert.Empty(Step(tracker, Actor(1, Monster, reactions: 1, reaction: HitFrom(7)), attacker));
        }

        [Fact]
        public void DualWield_OffHandUsesTheSubWeapon()
        {
            var tracker = NewTracker(pick: 0);
            Step(tracker, Actor(7, Fighter));
            Assert.Equal(new[] { 6030 }, Step(tracker, Actor(7, Fighter, "ati0", 1, 40, attack: true)));
            Assert.Equal(new[] { 6032 }, Step(tracker, Actor(7, Fighter, "ati0", 2, 40, attack: true, offHand: true)));

            var target = NewTracker(pick: 0);
            Step(target, Actor(1, Monster));
            var offHandSwing = Actor(7, Fighter, "ati0", 2, 40, attack: true, offHand: true);
            Assert.Equal(new[] { 6033 }, Step(target, Actor(1, Monster, reactions: 1, reaction: HitFrom(7)), offHandSwing));
            var mainSwing = Actor(7, Fighter, "ati0", 3, 40, attack: true);
            Assert.Equal(new[] { 6031 }, Step(target, Actor(1, Monster, reactions: 2, reaction: HitFrom(7)), mainSwing));
        }

        [Fact]
        public void RandomChoice_CanBeSilent()
        {
            var tracker = NewTracker(pick: 0);
            Step(tracker, Actor(1, Monster));
            Assert.Empty(Step(tracker, Actor(1, Monster, reactions: 1, reaction: HitFrom(99)))); // attacker unknown, cry pick silent
        }

        [Fact]
        public void Miss_PlaysNoReaction() =>
            Assert.Null(ActionSoundTracker.ReactionRoutine(ActionResolution.Miss));

        [Fact]
        public void Death_PlaysTheDeathRoutineOnce()
        {
            var tracker = NewTracker();
            Step(tracker, Actor(1, Monster));
            Assert.Equal(new[] { 7100 }, Step(tracker, Actor(1, Monster, dead: true)));
            Assert.Empty(Step(tracker, Actor(1, Monster, dead: true)));
        }

        [Fact]
        public void Range_IsSilentAt30Yalms()
        {
            Assert.Equal(30f, ActionSoundTracker.HearingRange);
            Assert.Equal(0f, AudioMixer.Attenuation(30f, GameAudioService.ActionSoundRange.Near, GameAudioService.ActionSoundRange.Far));
            Assert.InRange(AudioMixer.Attenuation(15f, GameAudioService.ActionSoundRange.Near, GameAudioService.ActionSoundRange.Far), 0.1f, 0.9f);
        }
    }
}
