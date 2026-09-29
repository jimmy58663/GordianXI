// tests/Gordian.Core.Tests/Animation/ActionPlaybackQueueTests.cs
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Gordian.Core.Animation;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Animation
{
    public class ActionPlaybackQueueTests
    {
        private const uint ActorId = 0x01000001;
        private const uint TargetId = 0x02000002;

        private readonly CombatState _combat = new();
        private readonly WorldState _world = new();
        private readonly ActionPlaybackQueue _queue;
        private readonly List<(WorldEntity Target, HitReaction Reaction)> _landed = new();

        public ActionPlaybackQueueTests()
        {
            _queue = new ActionPlaybackQueue(_combat, _world);
            _queue.HitLanded += (target, reaction) => _landed.Add((target, reaction));
            // Target at the origin facing East (+X); actor 3 yalms East of it, in front.
            _world.UpsertEntity(new WorldEntity(ActorId, 1, EntityType.Player) { Position = new Vector3(3f, 0f, 0f), Hpp = 100 });
            _world.UpsertEntity(new WorldEntity(TargetId, 2, EntityType.Monster) { Position = Vector3.Zero, Direction = 0, Hpp = 100 });
        }

        private static CombatActionResult Result(ActionResolution resolution = ActionResolution.Hit, byte scale = 0, ushort animation = 0) =>
            new() { Resolution = resolution, Scale = scale, Animation = animation };

        private static CombatActionRecord Record(ActionCategory category, uint actionId, params CombatActionResult[] results)
        {
            var record = new CombatActionRecord { ActorId = ActorId, Category = category, ActionId = actionId };
            var target = new CombatActionTargetRecord { TargetId = TargetId };
            target.Results.AddRange(results);
            record.Targets.Add(target);
            return record;
        }

        private static uint FourCc(string s) => (uint)(s[0] | (s[1] << 8) | (s[2] << 16) | (s[3] << 24));

        [Fact]
        public void FourCcOf_ReadsRoutineNamesAndRejectsIds()
        {
            Assert.Equal("atk0", ActionPlaybackQueue.FourCcOf(812348513)); // XiPackets 0x0028 cmd_arg for a basic attack
            Assert.Equal("cabk", ActionPlaybackQueue.FourCcOf(1801609571));
            Assert.Equal(string.Empty, ActionPlaybackQueue.FourCcOf(144)); // a spell id
        }

        [Theory]
        [InlineData(0b000_00, 0f, 0)]
        [InlineData(0b000_01, 0.25f, 0)]
        [InlineData(0b000_10, 0.5f, 0)]
        [InlineData(0b000_11, 1f, 0)]
        [InlineData(0b001_00, 0f, 1)]
        [InlineData(0b111_10, 0.5f, 7)]
        public void Scale_SplitsIntoDistortionAndKnockback(byte scale, float distortion, byte knockback)
        {
            Assert.Equal(distortion, ActionPlaybackQueue.DistortionOf(scale));
            Assert.Equal(knockback, ActionPlaybackQueue.KnockbackLevelOf(scale));
        }

        [Fact]
        public void BasicAttack_PostsOneSwingPerResult()
        {
            _combat.RecordAction(Record(ActionCategory.BasicAttack, 812348513, Result(animation: 0), Result(animation: 1)));

            Assert.Equal(2, _queue.OutstandingCount);
        }

        [Theory]
        [InlineData(ActionCategory.MagicStart, "cabk", ActionMotion.Routine)]
        [InlineData(ActionCategory.MagicStart, "spbk", ActionMotion.Interrupt)]
        [InlineData(ActionCategory.ItemStart, "cait", ActionMotion.Routine)]
        [InlineData(ActionCategory.SkillStart, "cate", ActionMotion.Routine)]
        [InlineData(ActionCategory.RangedStart, "calg", ActionMotion.Routine)]
        public void StartPackets_PlayTheRoutineTheyName(ActionCategory category, string routine, ActionMotion motion)
        {
            var (m, r) = ActionPlaybackQueue.MotionFor(Record(category, FourCc(routine), Result()));

            Assert.Equal(motion, m);
            Assert.Equal(routine, r);
        }

        [Fact]
        public void FinishPackets_PlayTheReleaseAbilityOrStandInSwing()
        {
            Assert.Equal(ActionMotion.CastRelease, ActionPlaybackQueue.MotionFor(Record(ActionCategory.MagicFinish, 144, Result())).Motion);
            Assert.Equal((ActionMotion.Routine, "shit"), ActionPlaybackQueue.MotionFor(Record(ActionCategory.ItemFinish, 4112, Result())));
            Assert.Equal(ActionMotion.Ability, ActionPlaybackQueue.MotionFor(Record(ActionCategory.AbilityFinish, 35, Result())).Motion);
            Assert.Equal(ActionMotion.Swing, ActionPlaybackQueue.MotionFor(Record(ActionCategory.SkillFinish, 32, Result())).Motion);
        }

        [Fact]
        public void StartPackets_ShowNoHits()
        {
            _combat.RecordAction(Record(ActionCategory.MagicStart, FourCc("cabk"), Result()));

            Assert.Equal(0, _queue.OutstandingCount);
        }

        [Fact]
        public void Update_ShowsHitsOfUnanimatedActorAfterFallbackDelay()
        {
            _combat.RecordAction(Record(ActionCategory.BasicAttack, 812348513, Result(scale: 0b000_11)));

            _queue.Update(Stopwatch.GetTimestamp());
            Assert.Empty(_landed); // not yet due

            _queue.Update(Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * 1.0));
            var (target, reaction) = Assert.Single(_landed);
            Assert.Equal(TargetId, target.ServerId);
            Assert.Equal(1f, reaction.Distortion);
            Assert.True(reaction.FromFront);
            Assert.Equal(0, _queue.OutstandingCount);

            _queue.Update(Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * 2.0));
            Assert.Single(_landed); // shown once
        }

        [Fact]
        public void AnimatedActorWithNothingToPlay_LandsTheHitAtOnce()
        {
            _combat.RecordAction(Record(ActionCategory.BasicAttack, 812348513, Result()));
            _world.TryGetByServerId(ActorId, out var actor);

            // A model without swing routines or clips: the actor takes the request, plays nothing, and the hit lands.
            actor!.Animation.Advance(0f, AnimationCategory.Combat, 0, new Gordian.Core.Resources.Models.EntityModel());

            Assert.Single(_landed);
            _queue.Update(Stopwatch.GetTimestamp());
            Assert.Equal(0, _queue.OutstandingCount);
        }

        [Fact]
        public void BuildReaction_AttackerBehindTarget_UsesBackPoseAndPushesAway()
        {
            _world.TryGetByServerId(TargetId, out var target);
            var behind = new WorldEntity(3, 3, EntityType.Player) { Position = new Vector3(-2f, 0f, 0f) };

            var reaction = ActionPlaybackQueue.BuildReaction(behind, target!, new ActionHit(TargetId, Result(scale: 0b010_01)), 0);

            Assert.False(reaction.FromFront);
            Assert.Equal(0.25f, reaction.Distortion);
            Assert.Equal(2, reaction.KnockbackLevel);
            Assert.Equal(1f, reaction.PushDirectionX, 3); // away from the attacker: East
            Assert.Equal(0f, reaction.PushDirectionZ, 3);
        }

        [Fact]
        public void Dispose_StopsListening()
        {
            _queue.Dispose();
            _combat.RecordAction(Record(ActionCategory.BasicAttack, 812348513, Result()));

            Assert.Equal(0, _queue.OutstandingCount);
        }
    }
}
