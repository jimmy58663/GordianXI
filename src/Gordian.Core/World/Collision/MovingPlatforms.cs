// src/Gordian.Core/World/Collision/MovingPlatforms.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Graphics;

namespace Gordian.Core.World.Collision
{
    /// <summary>
    /// A moving platform (an elevator) of the zone: the ZoneDef placements whose BlockID FourCC starts with <c>@</c>.
    /// It travels vertically between <see cref="UpperHeight"/> and <see cref="LowerHeight"/> (internal space, -Y up) over
    /// its XZ footprint; its parts are moved so their placement origin sits at the current height (the stops of the
    /// zone's Section 0x36 record are absolute heights, which Davoi's car, authored between its stops, confirms against
    /// the landings beside its shaft).
    /// BlockID semantics referenced from xi-tools (docs/zone/format.md, https://github.com/vekien/xi-tools).
    /// </summary>
    /// <param name="Id">The BlockID FourCC, which the server's elevator entity names as its door id (e.g. <c>@6l0</c>).</param>
    /// <param name="Min">Footprint minimum (internal X, Z).</param>
    /// <param name="Max">Footprint maximum (internal X, Z).</param>
    /// <param name="AuthoredHeight">The floor height the platform's parts are authored at.</param>
    /// <param name="FromRecord">Whether the travel came from the zone's Section 0x36 <c>@</c> record (otherwise the
    /// landing heuristic).</param>
    /// <param name="Floor0">The record's floor 0 (+0x34), the stop elevator animation 10 sends the car to.</param>
    /// <param name="Floor1">The record's floor 1 (+0x36), the stop elevator animation 11 sends the car to.</param>
    public sealed record MovingPlatform(string Id, Vector2 Min, Vector2 Max, float AuthoredHeight, float UpperHeight, float LowerHeight,
                                        bool FromRecord = false, float Floor0 = 0.0f, float Floor1 = 0.0f)
    {
        public bool Contains(float x, float z) => x >= Min.X && x <= Max.X && z >= Min.Y && z <= Max.Y;

        /// <summary>
        /// Where elevator animation 10 sends the car: the record's floor 0 (for every retail lift the bottom stop), or the
        /// upper stop for a car built by the landing heuristic. See <see cref="MovingPlatforms.HeightAt"/>.
        /// </summary>
        public float Animation10Height => FromRecord ? Floor0 : UpperHeight;

        /// <summary>Where elevator animation 11 sends the car: the record's floor 1, or the lower stop without a record.</summary>
        public float Animation11Height => FromRecord ? Floor1 : LowerHeight;

        /// <summary>
        /// Where the platform rests while no elevator entity moves it: the stop nearest its authored floor. Most cars are
        /// authored at a stop; Davoi's is authored a yalm below its upper stop (-8.19 against the record's -9.17).
        /// </summary>
        public float RestHeight => MathF.Abs(AuthoredHeight - UpperHeight) <= MathF.Abs(AuthoredHeight - LowerHeight) ? UpperHeight : LowerHeight;
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
    /// time in seconds; the animation (10 / 11) names the stop the leg goes to (see <see cref="HeightAt"/>). The client moves the platform itself at a constant speed
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
        /// Builds a platform from its parts' footprint and authored floor. Its travel comes from the zone's Section 0x36
        /// <c>@</c> record of the same id when there is one (<see cref="ZoneInteraction.TryGetLiftFloors"/>, the two
        /// stops the legacy client's lift actor uses; Metalworks' match a Windower capture exactly). A car without a
        /// usable record falls back to <see cref="CreateFromLanding"/>. Null when neither gives a travel.
        /// </summary>
        public static MovingPlatform? Create(string id, Vector2 min, Vector2 max, float authoredHeight, ZoneCollisionMesh collision,
                                             ZoneInteraction? record = null)
        {
            if (record is { } lift && lift.TryGetLiftFloors(out float floor0, out float floor1)
                && MathF.Abs(floor0 - floor1) >= LevelTolerance)
            {
                return new MovingPlatform(id, min, max, authoredHeight, MathF.Min(floor0, floor1), MathF.Max(floor0, floor1),
                                          FromRecord: true, Floor0: floor0, Floor1: floor1);
            }
            return CreateFromLanding(id, min, max, authoredHeight, collision);
        }

        /// <summary>
        /// The fallback for a car with no Section 0x36 record: finds the other end of its travel as the landing, the floor
        /// level with the most walkable area beside the footprint (not the pit under it), at least
        /// <see cref="MinimumTravel"/> from the authored floor (stair treads beside a shaft are small; landings are not).
        /// This matched the Metalworks lifts to within 0.04 yalms (2.0 / -10.0 against the records' 1.9625 / -9.983).
        /// Null when there is no other landing.
        /// </summary>
        public static MovingPlatform? CreateFromLanding(string id, Vector2 min, Vector2 max, float authoredHeight, ZoneCollisionMesh collision)
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
        /// entity describes it, or its <see cref="MovingPlatform.RestHeight"/> when there is no entity or it has no leg.
        /// Animation 10 sends the car to the record's floor 0 and 11 to its floor 1 (<see cref="MovingPlatform.Animation10Height"/>):
        /// one rule for the timed and the lever lifts. LandSandBoat's elevator handler (transports/elevator_handler.cpp)
        /// sends 10 for "up" on Metalworks' lifts but flags the lever lifts (Davoi, Palborough Mines, Fort Ghelsba) as
        /// "reversed", sending 10 while they descend; every one of those records has its floor 0 at the bottom, and with the
        /// lifts paired by FourCC (<see cref="PlatformOf"/>) Metalworks' antiphase shafts play exactly as before. The
        /// reading is an inference from the server data, checked against retail side by side (#66).
        /// </summary>
        public static float HeightAt(MovingPlatform platform, WorldEntity? elevator, double earthSecondsSinceEpoch, double clockSkewSeconds = 0.0)
        {
            if (elevator == null) return platform.RestHeight;
            bool to10 = elevator.AnimationState == AnimationUp;
            if (!to10 && elevator.AnimationState != AnimationDown) return platform.RestHeight;

            // The platform moves at the retail lift speed; the server's leg time (doors and wait included) caps it.
            float legSeconds = elevator.TransportTravelSeconds > 0 ? elevator.TransportTravelSeconds : DefaultTravelSeconds;
            float travel = MathF.Min(legSeconds, (platform.LowerHeight - platform.UpperHeight) / LiftSpeed);
            float progress = (float)Math.Clamp((earthSecondsSinceEpoch - LegStart(elevator, clockSkewSeconds)) / travel, 0.0, 1.0);
            return to10
                ? float.Lerp(platform.Animation11Height, platform.Animation10Height, progress)
                : float.Lerp(platform.Animation10Height, platform.Animation11Height, progress);
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
        /// The platform an elevator entity moves: the one with a Section 0x36 record whose FourCC the entity carries, else
        /// the one whose footprint holds (or lies nearest to) the entity. LandSandBoat's Metalworks data places each lift
        /// entity in the other shaft (<c>@6l0</c> at z +12, its record and car at z -12); a Windower capture shows the car
        /// beside the entity going up on animation 10. Paired by FourCC, animation 10 sends car <c>@6l0</c> to floor 0
        /// (down) while the other entity's 11 sends the car beside it up: the same picture, since the two shafts always run
        /// opposite legs. A car without a record keeps the footprint pairing and the old 10 = up reading.
        /// </summary>
        public static MovingPlatform? PlatformOf(IReadOnlyList<MovingPlatform> platforms, WorldEntity elevator)
        {
            foreach (var platform in platforms)
            {
                if (platform.FromRecord && platform.Id == elevator.TransportId) return platform;
            }

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
