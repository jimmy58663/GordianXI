// src/Gordian.Core/Animation/AnimationStateClassifier.cs
using System;
using System.Numerics;
using Gordian.Core.World;

namespace Gordian.Core.Animation
{
    /// <summary>
    /// Derives a high-level AnimationCategory from live WorldEntity state (speed, HP, claim)
    /// and, for the local player, active combat engagement.
    /// </summary>
    public static class AnimationStateClassifier
    {
        /// <summary>
        /// Classifies an entity's current animation category.
        /// isEngaged should reflect CombatState.IsEngaged for the local player; for any other
        /// entity, callers should pass whether it currently has a claim (entity.ClaimServerId != 0)
        /// or active combat stance.
        /// </summary>
        public static AnimationCategory Classify(WorldEntity entity, bool isEngaged, bool isLocalPlayer = false, DateTime? utcNow = null)
        {
            ArgumentNullException.ThrowIfNull(entity);

            if (entity.Hpp == 0 || entity.AnimationState == 3)
            {
                return AnimationCategory.Death;
            }

            DateTime now = utcNow ?? DateTime.UtcNow;
            bool isTimedOut = !isLocalPlayer && entity.LastPositionChangeUtc != DateTime.MinValue && (now - entity.LastPositionChangeUtc).TotalMilliseconds >= 750;
            bool isPhysicallyMoving = !isLocalPlayer && Vector3.Distance(entity.Position, entity.TargetPosition) > 0.05f;
            bool isMoving = isLocalPlayer ? (entity.Speed > 0) : (isPhysicallyMoving || (entity.Speed > 0 && !isTimedOut));

            if (isMoving)
            {
                // Unified speed-based walk/run threshold for all entities (PCs, NPCs, Monsters):
                // Mirrors PlayerLocomotionController.GetEffectiveWalkSpeed's run/2 split, falling back
                // to typical retail base run value (50 => 5.0 yalms/sec) when SpeedBase is unset
                // (e.g. NPCs/monsters that don't transmit it). Speed <= 25 yalms/s walk, > 25 run.
                int effectiveBase = entity.SpeedBase > 0 ? entity.SpeedBase : 50;
                int walkThreshold = Math.Max(1, effectiveBase / 2);

                return entity.Speed <= walkThreshold ? AnimationCategory.Walk : AnimationCategory.Run;
            }

            if (isEngaged)
            {
                return AnimationCategory.Combat;
            }

            return AnimationCategory.Idle;
        }
    }
}
