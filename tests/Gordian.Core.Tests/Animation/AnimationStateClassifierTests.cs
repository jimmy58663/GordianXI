// tests/Gordian.Core.Tests/Animation/AnimationStateClassifierTests.cs
using Gordian.Core.Animation;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Animation
{
    public class AnimationStateClassifierTests
    {
        private static WorldEntity CreateEntity(byte speed = 0, byte speedBase = 0, byte hpp = 100, uint claimServerId = 0)
        {
            var entity = new WorldEntity(1, 100, EntityType.Npc)
            {
                Speed = speed,
                SpeedBase = speedBase,
                Hpp = hpp,
                ClaimServerId = claimServerId
            };
            return entity;
        }

        [Fact]
        public void Classify_DeadEntity_ReturnsDeath_RegardlessOfOtherState()
        {
            var entity = CreateEntity(speed: 50, hpp: 0, claimServerId: 42);
            Assert.Equal(AnimationCategory.Death, AnimationStateClassifier.Classify(entity, isEngaged: true));
        }

        [Fact]
        public void Classify_Engaged_ReturnsCombat()
        {
            var entity = CreateEntity(speed: 0);
            Assert.Equal(AnimationCategory.Combat, AnimationStateClassifier.Classify(entity, isEngaged: true));
        }

        [Fact]
        public void Classify_ZeroSpeed_ReturnsIdle()
        {
            var entity = CreateEntity(speed: 0);
            Assert.Equal(AnimationCategory.Idle, AnimationStateClassifier.Classify(entity, isEngaged: false));
        }

        [Fact]
        public void Classify_SpeedAtOrBelowHalfBase_ReturnsWalk()
        {
            var entity = CreateEntity(speed: 25, speedBase: 50);
            Assert.Equal(AnimationCategory.Walk, AnimationStateClassifier.Classify(entity, isEngaged: false));
        }

        [Fact]
        public void Classify_SpeedAboveHalfBase_ReturnsRun()
        {
            var entity = CreateEntity(speed: 50, speedBase: 50);
            Assert.Equal(AnimationCategory.Run, AnimationStateClassifier.Classify(entity, isEngaged: false));
        }

        [Fact]
        public void Classify_UnsetSpeedBase_FallsBackToDefaultRunBaseline()
        {
            // SpeedBase == 0 (e.g. a monster that never transmitted it) falls back to a
            // typical retail base run value (50 => 5.0 yalms/sec), so walk threshold is 25.
            var walker = CreateEntity(speed: 20, speedBase: 0);
            var runner = CreateEntity(speed: 40, speedBase: 0);

            Assert.Equal(AnimationCategory.Walk, AnimationStateClassifier.Classify(walker, isEngaged: false));
            Assert.Equal(AnimationCategory.Run, AnimationStateClassifier.Classify(runner, isEngaged: false));
        }

        [Fact]
        public void Classify_NotEngaged_ClaimIgnored_CallerDecidesEngagement()
        {
            // The classifier trusts whatever isEngaged the caller passes (local player uses
            // CombatState.IsEngaged, others use ClaimServerId != 0) - it does not re-derive it.
            var entity = CreateEntity(speed: 0, claimServerId: 99);
            Assert.Equal(AnimationCategory.Idle, AnimationStateClassifier.Classify(entity, isEngaged: false));
        }

        [Fact]
        public void Classify_Monster_RoamingAtWalkSpeed_ReturnsWalk()
        {
            var monster = new WorldEntity(10, 500, EntityType.Monster)
            {
                Speed = 20, // Roaming speed (<= 25 walk threshold)
                Hpp = 100,
                ClaimServerId = 0
            };

            // Unengaged roaming monster moving at walk speed walks
            Assert.Equal(AnimationCategory.Walk, AnimationStateClassifier.Classify(monster, isEngaged: false));
        }

        [Fact]
        public void Classify_Monster_AggroedChasing_ReturnsRun()
        {
            var monster = new WorldEntity(10, 500, EntityType.Monster)
            {
                Speed = 50,
                Hpp = 100,
                ClaimServerId = 12345
            };

            // Engaged or aggroed monster chasing a player runs
            Assert.Equal(AnimationCategory.Run, AnimationStateClassifier.Classify(monster, isEngaged: true));
        }

        [Fact]
        public void Classify_Monster_EngagedStationary_ReturnsCombat()
        {
            var monster = new WorldEntity(10, 500, EntityType.Monster)
            {
                Speed = 0,
                Hpp = 100,
                ClaimServerId = 12345
            };

            // Stationary engaged/claimed monster enters combat stance
            Assert.Equal(AnimationCategory.Combat, AnimationStateClassifier.Classify(monster, isEngaged: true));
        }

        [Fact]
        public void Classify_RemoteEntity_MotionTimeout_ReturnsIdle()
        {
            var now = DateTime.UtcNow;
            var entity = new WorldEntity(20, 200, EntityType.Npc)
            {
                Speed = 50,
                Hpp = 100,
                LastPositionChangeUtc = now - TimeSpan.FromMilliseconds(800) // timed out (>750ms)
            };

            // Timed-out remote entity with no physical distance remaining returns to Idle
            Assert.Equal(AnimationCategory.Idle, AnimationStateClassifier.Classify(entity, isEngaged: false, isLocalPlayer: false, utcNow: now));

            // Local player ignores remote position packet timeout
            Assert.Equal(AnimationCategory.Run, AnimationStateClassifier.Classify(entity, isEngaged: false, isLocalPlayer: true, utcNow: now));
        }

        [Fact]
        public void Classify_RemoteEntity_PhysicalTravelRemaining_PreservesWalkUntilDestination()
        {
            var now = DateTime.UtcNow;
            var entity = new WorldEntity(21, 201, EntityType.Monster)
            {
                Speed = 0, // Server speed byte set to 0 mid-glide or between packets
                Hpp = 100,
                Position = new System.Numerics.Vector3(10f, 0f, 0f),
                TargetPosition = new System.Numerics.Vector3(12f, 0f, 0f), // 2.0 yalms remaining
                LastPositionChangeUtc = now - TimeSpan.FromMilliseconds(900) // even if packet timed out
            };

            // Remote monster physically moving across ground continues locomotion animation instead of resetting to Idle
            Assert.Equal(AnimationCategory.Walk, AnimationStateClassifier.Classify(entity, isEngaged: false, isLocalPlayer: false, utcNow: now));

            // Once destination is reached, it transitions to Idle
            entity.Position = entity.TargetPosition;
            Assert.Equal(AnimationCategory.Idle, AnimationStateClassifier.Classify(entity, isEngaged: false, isLocalPlayer: false, utcNow: now));
        }
    }
}
