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
    }
}
