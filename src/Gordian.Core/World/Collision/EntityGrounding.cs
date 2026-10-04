// src/Gordian.Core/World/Collision/EntityGrounding.cs
using System;
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
        public static float GetDisplayHeight(WorldEntity entity, ZoneCollisionMesh? collision) =>
            GetDisplayHeight(entity, collision, ReadOnlySpan<PlatformHeight>.Empty);

        /// <summary>
        /// The walkable floor at or below a position (at most a step above it), or its own height without one: where an
        /// event places an entity (retail calibrates event positions to the floor, VCalibrate).
        /// </summary>
        public static float GetDisplayHeight(Vector3 position, ZoneCollisionMesh? collision) =>
            GetDisplayHeight(position, collision, PlayerLocomotionController.StepUpHeight);

        /// <summary>How far above an event position its floor may be (event walks move their height in a line between floors).</summary>
        public const float EventStepUpHeight = 2.5f;

        /// <summary>
        /// Where an event draws an entity it does not keep at its placed height: the floor within a step of the event
        /// position, as for any character, else a floor up to <see cref="EventStepUpHeight"/> above it (a walk whose height
        /// runs below a floor it is crossing). Trying the near floor first keeps an actor standing at the foot of a ledge on
        /// the ground: Port Jeuno 324's Buntz stands at his server spot, beside a ledge 1.2 yalms high, and the wide search
        /// alone drew him on top of it.
        /// </summary>
        public static float GetEventDisplayHeight(Vector3 position, ZoneCollisionMesh? collision) =>
            GetEventDisplayHeight(position, collision, ReadOnlySpan<PlatformHeight>.Empty);

        /// <summary>
        /// As <see cref="GetEventDisplayHeight(Vector3, ZoneCollisionMesh?)"/>, with moving platforms: an actor whose event
        /// position is on a platform (a rider answering Palborough Mines' lever question on the lift, #66) stands on the
        /// platform, not on the shaft floor below it, by the same rule as <see cref="GetDisplayHeight(WorldEntity,
        /// ZoneCollisionMesh?, ReadOnlySpan{PlatformHeight})"/>; one riding <paramref name="ridingPlatformId"/> keeps riding it
        /// while the event position stays over its footprint.
        /// </summary>
        public static float GetEventDisplayHeight(Vector3 position, ZoneCollisionMesh? collision, ReadOnlySpan<PlatformHeight> platforms,
                                                  string ridingPlatformId = "")
        {
            if (ridingPlatformId.Length > 0)
            {
                foreach (var platform in platforms)
                {
                    if (platform.Platform.Id == ridingPlatformId && platform.Platform.Contains(position.X, position.Z)) return platform.Height;
                }
            }

            GroundHit near = default;
            bool found = collision != null && collision.TryGetSteppedGround(position, PlayerLocomotionController.StepUpHeight,
                PlayerLocomotionController.MaxFallDistance, PlayerLocomotionController.FootRadius, out near);
            if (MovingPlatforms.TryGetPlatformUnder(platforms, position, PlayerLocomotionController.StepUpHeight,
                    PlayerLocomotionController.MaxFallDistance, out var under) &&
                (!found || under.Height <= near.Height + MovingPlatforms.LevelTolerance))
                return under.Height;
            if (found) return near.Height;
            return collision == null ? position.Y : GetDisplayHeight(position, collision, EventStepUpHeight);
        }

        /// <summary>As <see cref="GetDisplayHeight(Vector3, ZoneCollisionMesh?)"/>, with the floor allowed up to <paramref name="stepUp"/> above.</summary>
        public static float GetDisplayHeight(Vector3 position, ZoneCollisionMesh? collision, float stepUp) =>
            collision != null && collision.TryGetSteppedGround(position, stepUp,
                PlayerLocomotionController.MaxFallDistance, PlayerLocomotionController.FootRadius, out var ground)
                ? ground.Height
                : position.Y;

        /// <summary>
        /// As <see cref="GetDisplayHeight(WorldEntity, ZoneCollisionMesh?)"/>, with moving platforms: a character
        /// standing on one is drawn on its floor and keeps riding it while it stays within the platform's footprint,
        /// however far it moves from the character's reported height (a Windower capture shows a player standing still
        /// on a Metalworks lift drawn riding it up and down on another client).
        /// </summary>
        public static float GetDisplayHeight(WorldEntity entity, ZoneCollisionMesh? collision, ReadOnlySpan<PlatformHeight> platforms)
        {
            var position = entity.Position;
            if (!IsGrounded(entity))
            {
                entity.RidingPlatformId = string.Empty;
                return position.Y;
            }

            if (entity.RidingPlatformId.Length > 0)
            {
                foreach (var platform in platforms)
                {
                    if (platform.Platform.Id == entity.RidingPlatformId && platform.Platform.Contains(position.X, position.Z))
                    {
                        return platform.Height;
                    }
                }
                entity.RidingPlatformId = string.Empty;
            }

            float stepUp = PlayerLocomotionController.StepUpHeight, maxDrop = PlayerLocomotionController.MaxFallDistance;
            GroundHit ground = default;
            bool found = collision != null &&
                         collision.TryGetSteppedGround(position, stepUp, maxDrop, PlayerLocomotionController.FootRadius, out ground);
            if (MovingPlatforms.TryGetPlatformUnder(platforms, position, stepUp, maxDrop, out var under) &&
                (!found || under.Height <= ground.Height + MovingPlatforms.LevelTolerance))
            {
                entity.RidingPlatformId = under.Platform.Id;
                return under.Height;
            }
            return found ? ground.Height : position.Y;
        }
    }
}
