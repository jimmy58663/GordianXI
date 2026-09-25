// src/Gordian.Core/World/Collision/EntityGrounding.cs
using System.Numerics;
using Gordian.Core.Input;

namespace Gordian.Core.World.Collision
{
    /// <summary>
    /// Where other characters are drawn vertically. The legacy client places every actor on the zone's collision floor
    /// under its reported position rather than at the reported height: a Windower capture (2026-09-25) shows a player
    /// holding itself 10 yalms up by position hacking drawn walking on the plaza floor on another client, before and
    /// after a teleport. Only actors whose server GroundFlag says they ignore world collision, and transports, keep the
    /// reported height. Display only: the server's positions are left untouched.
    /// </summary>
    public static class EntityGrounding
    {
        /// <summary>
        /// Whether <paramref name="entity"/> is drawn on the floor.
        /// </summary>
        public static bool IsGrounded(WorldEntity entity) =>
            !entity.IgnoresWorldCollision &&
            entity.Type is not (EntityType.Door or EntityType.Elevator or EntityType.Ship);

        /// <summary>
        /// The height to draw <paramref name="entity"/> at (internal space, -Y up): the walkable floor at or below its
        /// reported position (at most a step above it), rounded over stair edges like the local player, or the reported
        /// height when there is no collision, no floor below, or the entity is not grounded.
        /// </summary>
        public static float GetDisplayHeight(WorldEntity entity, ZoneCollisionMesh? collision)
        {
            var position = entity.Position;
            if (collision == null || !IsGrounded(entity)) return position.Y;
            return collision.TryGetSteppedGround(position, PlayerLocomotionController.StepUpHeight,
                                                 PlayerLocomotionController.MaxFallDistance,
                                                 PlayerLocomotionController.FootRadius, out var ground)
                ? ground.Height
                : position.Y;
        }
    }
}
