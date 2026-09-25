// src/Gordian.Core/World/Collision/MovingPlatforms.cs
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gordian.Core.World.Collision
{
    /// <summary>
    /// A moving platform (an elevator) of the zone: the ZoneDef placements whose BlockID FourCC starts with <c>@</c>.
    /// The platform floor is authored at one landing; it travels vertically between <see cref="UpperHeight"/> and
    /// <see cref="LowerHeight"/> (internal space, -Y up) over its XZ footprint.
    /// BlockID semantics referenced from xi-tools (docs/zone/format.md, https://github.com/vekien/xi-tools).
    /// </summary>
    /// <param name="Id">The BlockID FourCC, which the server's elevator entity names as its door id (e.g. <c>@6l0</c>).</param>
    /// <param name="Min">Footprint minimum (internal X, Z).</param>
    /// <param name="Max">Footprint maximum (internal X, Z).</param>
    /// <param name="AuthoredHeight">The floor height the platform's parts are authored at.</param>
    public sealed record MovingPlatform(string Id, Vector2 Min, Vector2 Max, float AuthoredHeight, float UpperHeight, float LowerHeight)
    {
        public bool Contains(float x, float z) => x >= Min.X && x <= Max.X && z >= Min.Y && z <= Max.Y;
    }

    /// <summary>
    /// A platform and its floor height at one moment.
    /// </summary>
    public readonly record struct PlatformHeight(MovingPlatform Platform, float Height)
    {
        /// <summary>How far the platform is from its authored pose (internal Y).</summary>
        public float Offset => Height - Platform.AuthoredHeight;
    }

    /// <summary>
    /// Elevator motion as the legacy client plays it. LandSandBoat sends each elevator as an NPC with the elevator look,
    /// its platform's FourCC, the Earth second (since the Vana'diel epoch) its current leg started, and the leg's travel
    /// time in seconds; the animation says up (10) or down (11). The client moves the platform itself at a constant speed
    /// (a Windower capture of Metalworks' lift shows ~2 yalms/s over its 8-second leg, no easing) and carries whoever
    /// stands on it. Packet layout referenced from LandSandBoat (https://github.com/LandSandBoat/server,
    /// packets/entity_update.cpp getTransportNPCName, transports/elevator_handler.cpp).
    /// </summary>
    public static class MovingPlatforms
    {
        public const byte AnimationUp = 10;
        public const byte AnimationDown = 11;

        /// <summary>
        /// Platform speed in yalms per second: a Windower capture of Metalworks' lift shows a steady 1.99 yalms/s
        /// (11.95 yalms in 6.0 s), finishing about two seconds inside the server's 8-second leg.
        /// </summary>
        public const float LiftSpeed = 1.99f;

        /// <summary>Leg time used when the server sends none.</summary>
        public const float DefaultTravelSeconds = 8.0f;

        /// <summary>A platform within this of the static floor under it (resting at a landing) counts as the floor.</summary>
        public const float LevelTolerance = 0.1f;

        /// <summary>A landing must be at least this far (yalms) from the authored floor to count as the other end.</summary>
        public const float MinimumTravel = 2.0f;

        /// <summary>How far around the footprint (yalms) to look for the other landing.</summary>
        public const float LandingSearchMargin = 1.5f;

        /// <summary>
        /// Builds a platform from its parts' footprint and authored floor, finding the other end of its travel as the
        /// landing: the floor level with the most walkable area beside the footprint (not the pit under it), at least
        /// <see cref="MinimumTravel"/> from the authored floor (stair treads beside a shaft are small; landings are not).
        /// The client's own source for the travel distance is not decoded (its move routines carry only the duration);
        /// this matches the Metalworks lifts to within 0.04 yalms. Null when there is no other landing.
        /// </summary>
        public static MovingPlatform? Create(string id, Vector2 min, Vector2 max, float authoredHeight, ZoneCollisionMesh collision)
        {
            var areaByLevel = new Dictionary<int, (float Height, float Area)>();
            foreach (var (height, area, centroid) in collision.FloorsNear(min - new Vector2(LandingSearchMargin), max + new Vector2(LandingSearchMargin)))
            {
                if (MathF.Abs(height - authoredHeight) < MinimumTravel) continue;
                // Landings sit beside the shaft; the pit under the platform is lower than the landing it stops at.
                if (centroid.X > min.X && centroid.X < max.X && centroid.Y > min.Y && centroid.Y < max.Y) continue;
                int level = (int)MathF.Round(height * 10.0f); // 0.1-yalm levels
                areaByLevel.TryGetValue(level, out var sum);
                areaByLevel[level] = (height, sum.Area + area);
            }
            if (areaByLevel.Count == 0) return null;

            float other = 0.0f, best = -1.0f;
            foreach (var (height, area) in areaByLevel.Values)
            {
                if (area > best) { best = area; other = height; }
            }
            return new MovingPlatform(id, min, max, authoredHeight, MathF.Min(authoredHeight, other), MathF.Max(authoredHeight, other));
        }

        /// <summary>
        /// The floor height of <paramref name="platform"/> at <paramref name="earthSecondsSinceEpoch"/> as its elevator
        /// entity describes it, or its authored height when the entity has no leg.
        /// </summary>
        public static float HeightAt(MovingPlatform platform, WorldEntity? elevator, double earthSecondsSinceEpoch, double clockSkewSeconds = 0.0)
        {
            if (elevator == null) return platform.AuthoredHeight;
            bool up = elevator.AnimationState == AnimationUp;
            if (!up && elevator.AnimationState != AnimationDown) return platform.AuthoredHeight;

            // The platform moves at the retail lift speed; the server's leg time (doors and wait included) caps it.
            float legSeconds = elevator.TransportTravelSeconds > 0 ? elevator.TransportTravelSeconds : DefaultTravelSeconds;
            float travel = MathF.Min(legSeconds, (platform.LowerHeight - platform.UpperHeight) / LiftSpeed);
            float progress = (float)Math.Clamp((earthSecondsSinceEpoch - LegStart(elevator, clockSkewSeconds)) / travel, 0.0, 1.0);
            return up
                ? float.Lerp(platform.LowerHeight, platform.UpperHeight, progress)
                : float.Lerp(platform.UpperHeight, platform.LowerHeight, progress);
        }

        /// <summary>
        /// Refines a session's learned gap (seconds) between a leg's stamp and when its update arrives on this client's
        /// clock (<see cref="WorldState.TransportClockSkewSeconds"/>). The server sends each leg as it starts, so a leg
        /// seen arriving plays from its arrival; the gap (1.5-2.9 s in a Metalworks capture: the stamp is truncated to
        /// whole seconds and the synced clocks differ) only places legs already under way on zone-in.
        /// </summary>
        public static double RefineClockSkew(double current, double arrival, uint stamp)
        {
            double gap = arrival - stamp;
            if (gap < 0.0 || gap > 6.0) return current; // a delayed or foreign update: not a clock measurement
            return current <= 0.0 ? gap : (current * 0.7) + (gap * 0.3);
        }

        /// <summary>
        /// When the elevator's current leg is played from (Earth seconds since the Vana'diel epoch): its arrival when it
        /// was seen arriving, otherwise its stamp shifted by the session's clock gap.
        /// </summary>
        public static double LegStart(WorldEntity elevator, double clockSkewSeconds = 0.0) =>
            elevator.TransportObservedSeconds > 0.0 ? elevator.TransportObservedSeconds : elevator.TransportStartSeconds + clockSkewSeconds;

        /// <summary>
        /// The platform an elevator entity moves: the one whose footprint holds (or lies nearest to) the entity, as the
        /// legacy client pairs them; the FourCC only as a fallback. LandSandBoat's Metalworks data names each lift by the
        /// other shaft's BlockID, and a Windower capture shows the lift beside the entity moving.
        /// </summary>
        public static MovingPlatform? PlatformOf(IReadOnlyList<MovingPlatform> platforms, WorldEntity elevator)
        {
            var position = elevator.Position;
            MovingPlatform? nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (var platform in platforms)
            {
                if (platform.Contains(position.X, position.Z)) return platform;
                var center = (platform.Min + platform.Max) * 0.5f;
                float distance = Vector2.Distance(center, new Vector2(position.X, position.Z));
                if (distance < nearestDistance) { nearestDistance = distance; nearest = platform; }
            }
            if (nearest != null && nearestDistance <= PairingDistance) return nearest;
            foreach (var platform in platforms)
            {
                if (platform.Id == elevator.TransportId) return platform;
            }
            return null;
        }

        /// <summary>An elevator entity farther than this (yalms) from every platform pairs by FourCC instead.</summary>
        public const float PairingDistance = 8.0f;

        /// <summary>
        /// Every platform of the zone with its current height, for one tick or frame.
        /// </summary>
        public static PlatformHeight[] Evaluate(ZoneCollisionMesh? collision, WorldState world, double earthSecondsSinceEpoch)
        {
            if (collision == null || collision.MovingPlatforms.Count == 0) return Array.Empty<PlatformHeight>();

            var elevators = new Dictionary<MovingPlatform, WorldEntity>(ReferenceEqualityComparer.Instance);
            foreach (var entity in world.Entities)
            {
                if (!entity.IsSpawned || entity.TransportId.Length == 0 || entity.Type != EntityType.Elevator) continue;
                var platform = PlatformOf(collision.MovingPlatforms, entity);
                if (platform != null) elevators[platform] = entity;
            }

            var heights = new PlatformHeight[collision.MovingPlatforms.Count];
            for (int i = 0; i < heights.Length; i++)
            {
                var platform = collision.MovingPlatforms[i];
                elevators.TryGetValue(platform, out var elevator);
                heights[i] = new PlatformHeight(platform, HeightAt(platform, elevator, earthSecondsSinceEpoch, world.TransportClockSkewSeconds));
            }
            return heights;
        }

        /// <summary>
        /// The highest platform under (<paramref name="feet"/>'s XZ) whose floor is at most <paramref name="stepUp"/>
        /// above and <paramref name="maxDrop"/> below the feet (internal -Y up).
        /// </summary>
        public static bool TryGetPlatformUnder(ReadOnlySpan<PlatformHeight> platforms, Vector3 feet, float stepUp, float maxDrop, out PlatformHeight hit)
        {
            hit = default;
            bool found = false;
            foreach (var platform in platforms)
            {
                if (!platform.Platform.Contains(feet.X, feet.Z)) continue;
                if (platform.Height < feet.Y - stepUp || platform.Height > feet.Y + maxDrop) continue;
                if (!found || platform.Height < hit.Height)
                {
                    hit = platform;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>
        /// Whether a move to <paramref name="target"/> enters a shaft whose platform is not there: the legacy client's
        /// shaft doors keep a character out of an elevator shaft unless the platform is within a step of its feet.
        /// Moves that start inside the footprint (riding, stepping off) are never refused.
        /// </summary>
        public static bool EntersEmptyShaft(ReadOnlySpan<PlatformHeight> platforms, Vector3 start, Vector3 target, float stepUp)
        {
            foreach (var platform in platforms)
            {
                if (!platform.Platform.Contains(target.X, target.Z) || platform.Platform.Contains(start.X, start.Z)) continue;
                if (MathF.Abs(platform.Height - target.Y) > stepUp) return true;
            }
            return false;
        }

        /// <summary>
        /// The platform, if any, that the static <paramref name="ground"/> (or its absence) loses to: a platform floor above
        /// the static floor under the same point wins, as the character stands on the platform rather than the shaft.
        /// </summary>
        public static bool TryOverride(ReadOnlySpan<PlatformHeight> platforms, Vector3 feet, float stepUp, float maxDrop,
                                       bool hasGround, ref GroundHit ground)
        {
            if (!TryGetPlatformUnder(platforms, feet, stepUp, maxDrop, out var platform)) return false;
            if (hasGround && ground.Height < platform.Height - LevelTolerance) return false; // a clearly higher static floor
            ground = new GroundHit(platform.Height, new Vector3(0.0f, -1.0f, 0.0f), CollisionTerrain.Metal, -1);
            return true;
        }
    }
}
