// src/Gordian.Core/Animation/AnimationStateClassifier.cs
using System;
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
        /// entity, callers should pass whether it currently has a claim (entity.ClaimServerId != 0),
        /// the only server-driven engagement signal already decoded onto WorldEntity.
        /// </summary>
        public static AnimationCategory Classify(WorldEntity entity, bool isEngaged)
        {
            ArgumentNullException.ThrowIfNull(entity);

            if (entity.Hpp == 0)
            {
                return AnimationCategory.Death;
            }

            if (isEngaged)
            {
                return AnimationCategory.Combat;
            }

            if (entity.Speed == 0)
            {
                return AnimationCategory.Idle;
            }

            // Mirrors PlayerLocomotionController.GetEffectiveWalkSpeed's run/2 split, falling back
            // to a typical retail base run value (50 => 5.0 yalms/sec) when SpeedBase is unset
            // (e.g. NPCs/monsters that don't transmit it).
            int effectiveBase = entity.SpeedBase > 0 ? entity.SpeedBase : 50;
            int walkThreshold = Math.Max(1, effectiveBase / 2);

            return entity.Speed <= walkThreshold ? AnimationCategory.Walk : AnimationCategory.Run;
        }
    }
}
