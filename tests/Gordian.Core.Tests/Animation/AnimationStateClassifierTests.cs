// tests/Gordian.Core.Tests/Animation/AnimationStateClassifierTests.cs
using System.Numerics;
using Gordian.Core.Animation;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Animation
{
    public class AnimationStateClassifierTests
    {
        private static WorldEntity CreateEntity(byte speed = 0, byte speedBase = 0, byte hpp = 100, uint claimServerId = 0, EntityType type = EntityType.Player)
        {
            var entity = new WorldEntity(1, 100, type)
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
        public void Classify_RemotePlayer_MotionTimeout_ReturnsIdle()
        {
            var now = DateTime.UtcNow;
            var entity = new PlayerEntity(20, 200)
            {
                Speed = 50,
                Hpp = 100,
                Position = Vector3.Zero,
                TargetPosition = new Vector3(10f, 0f, 0f),
                LastPositionChangeUtc = now - TimeSpan.FromMilliseconds(1500) // not timed out (<1750ms)
            };

            // Not timed out yet with physical distance remaining: still running
            Assert.Equal(AnimationCategory.Run, AnimationStateClassifier.Classify(entity, isEngaged: false, isLocalPlayer: false, utcNow: now));

            // Remote entity that arrived at TargetPosition with LastMovTime <= 1 switches to Idle immediately without running in place
            entity.Position = entity.TargetPosition;
            entity.LastMovTime = 1;
            Assert.Equal(AnimationCategory.Idle, AnimationStateClassifier.Classify(entity, isEngaged: false, isLocalPlayer: false, utcNow: now));

            // Active runner (LastMovTime > 1) that only just reached its target keeps Run briefly, bridging a slightly late update
            entity.LastMovTime = 50;
            entity.LastPositionChangeUtc = now - TimeSpan.FromMilliseconds(100);
            Assert.Equal(AnimationCategory.Run, AnimationStateClassifier.Classify(entity, isEngaged: false, isLocalPlayer: false, utcNow: now));

            // ...but once past the arrival grace it idles rather than running in place waiting for a stop update
            entity.LastPositionChangeUtc = now - TimeSpan.FromMilliseconds(AnimationStateClassifier.RemoteArrivalGraceMs + 50);
            Assert.Equal(AnimationCategory.Idle, AnimationStateClassifier.Classify(entity, isEngaged: false, isLocalPlayer: false, utcNow: now));

            entity.LastMovTime = 1;
            entity.LastPositionChangeUtc = now - TimeSpan.FromMilliseconds(1850);

            // Still physically travelling after a long gap keeps Run rather than sliding across the ground in Idle
            entity.Position = Vector3.Zero;
            Assert.Equal(AnimationCategory.Run, AnimationStateClassifier.Classify(entity, isEngaged: false, isLocalPlayer: false, utcNow: now));

            // Local player ignores remote position packet timeout
            Assert.Equal(AnimationCategory.Run, AnimationStateClassifier.Classify(entity, isEngaged: false, isLocalPlayer: true, utcNow: now));
        }

        [Fact]
        public void Classify_StationaryNpc_WithNonZeroMovTime_ReturnsIdle()
        {
            var now = DateTime.UtcNow;
            var npc = new WorldEntity(30, 300, EntityType.Npc)
            {
                Speed = 0,
                Hpp = 100,
                Position = new Vector3(5f, 0f, 5f),
                TargetPosition = new Vector3(5f, 0f, 5f), // Physically stationary
                LastMovTime = 8, // LSB stores database flag in Flags0, so movTime was non-zero
                LastPositionChangeUtc = now - TimeSpan.FromMilliseconds(100)
            };

            // Stationary NPCs in towns must NEVER run in place
            Assert.Equal(AnimationCategory.Idle, AnimationStateClassifier.Classify(npc, isEngaged: false, isLocalPlayer: false, utcNow: now));
        }

        [Fact]
        public void Classify_RoamingMonster_AtBaseSpeed50_ReturnsWalk()
        {
            var now = DateTime.UtcNow;
            var monster = new WorldEntity(40, 400, EntityType.Monster)
            {
                Speed = 50, // LSB baseSpeed is 50
                SpeedBase = 50,
                Hpp = 100,
                Position = new Vector3(0f, 0f, 0f),
                TargetPosition = new Vector3(5f, 0f, 0f), // Moving
                LastPositionChangeUtc = now - TimeSpan.FromMilliseconds(100)
            };

            // Roaming monster at normal speed must walk, not run
            Assert.Equal(AnimationCategory.Walk, AnimationStateClassifier.Classify(monster, isEngaged: false, isLocalPlayer: false, utcNow: now));
        }

        [Fact]
        public void Classify_ChasingMonster_AtBaseSpeed50_ReturnsRun()
        {
            var now = DateTime.UtcNow;
            var monster = new WorldEntity(40, 400, EntityType.Monster)
            {
                Speed = 50,
                SpeedBase = 50,
                Hpp = 100,
                ClaimServerId = 999,
                Position = new Vector3(0f, 0f, 0f),
                TargetPosition = new Vector3(5f, 0f, 0f), // Moving
                LastPositionChangeUtc = now - TimeSpan.FromMilliseconds(100)
            };

            // Engaged/chasing monster runs towards its target
            Assert.Equal(AnimationCategory.Run, AnimationStateClassifier.Classify(monster, isEngaged: true, isLocalPlayer: false, utcNow: now));
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

        [Fact]
        public void Classify_Player_EngagedMovingForwardRun_ReturnsCombatRun()
        {
            var player = new PlayerEntity(1, 100)
            {
                Speed = 50,
                Hpp = 100,
                LocomotionDirection = LocomotionDirection.Forward
            };

            Assert.Equal(AnimationCategory.CombatRun, AnimationStateClassifier.Classify(player, isEngaged: true, isLocalPlayer: true));
        }

        [Fact]
        public void Classify_Player_EngagedMovingForwardWalk_ReturnsCombatWalk()
        {
            var player = new PlayerEntity(1, 100)
            {
                Speed = 20,
                Hpp = 100,
                LocomotionDirection = LocomotionDirection.Forward
            };

            Assert.Equal(AnimationCategory.CombatWalk, AnimationStateClassifier.Classify(player, isEngaged: true, isLocalPlayer: true));
        }

        [Theory]
        [InlineData(LocomotionDirection.Backward, AnimationCategory.CombatMoveBackward)]
        [InlineData(LocomotionDirection.Left, AnimationCategory.CombatMoveLeft)]
        [InlineData(LocomotionDirection.Right, AnimationCategory.CombatMoveRight)]
        public void Classify_Player_EngagedMovingDirectional_ReturnsCombatDirectional(LocomotionDirection dir, AnimationCategory expected)
        {
            var player = new PlayerEntity(1, 100)
            {
                Speed = 50,
                Hpp = 100,
                LocomotionDirection = dir
            };

            Assert.Equal(expected, AnimationStateClassifier.Classify(player, isEngaged: true, isLocalPlayer: true));
        }

        [Theory]
        [InlineData(LocomotionDirection.Backward, AnimationCategory.MoveBackward)]
        [InlineData(LocomotionDirection.Left, AnimationCategory.MoveLeft)]
        [InlineData(LocomotionDirection.Right, AnimationCategory.MoveRight)]
        public void Classify_Player_DisengagedMovingDirectional_ReturnsDirectional(LocomotionDirection dir, AnimationCategory expected)
        {
            var player = new PlayerEntity(1, 100)
            {
                Speed = 50,
                Hpp = 100,
                LocomotionDirection = dir
            };

            Assert.Equal(expected, AnimationStateClassifier.Classify(player, isEngaged: false, isLocalPlayer: true));
        }

        [Fact]
        public void Classify_RemotePlayer_EngagedDeadReckoning_MaintainsCombatRun()
        {
            var now = DateTime.UtcNow;
            var player = new PlayerEntity(1, 100)
            {
                Speed = 0, // packet arrived with Speed=0 or paused between ticks
                Hpp = 100,
                Position = new Vector3(10f, 0f, 10f),
                TargetPosition = new Vector3(10f, 0f, 10f), // physically at target position
                LastMovTime = 5, // actively running (> 1)
                LastPositionChangeUtc = now - TimeSpan.FromMilliseconds(200),
                LocomotionDirection = LocomotionDirection.Forward
            };

            // While engaged and actively running (LastMovTime > 1), dead reckoning must maintain CombatRun
            Assert.Equal(AnimationCategory.CombatRun, AnimationStateClassifier.Classify(player, isEngaged: true, isLocalPlayer: false, utcNow: now));
        }
    }
}
