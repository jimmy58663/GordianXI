// src/Gordian.Core/World/Collision/CollisionSettings.cs
using System;
using Gordian.Core.Config;

namespace Gordian.Core.World.Collision
{
    /// <summary>
    /// The kinds of collision local movement obeys.
    /// </summary>
    [Flags]
    public enum CollisionLayers
    {
        None = 0,

        /// <summary>Follow the floor: climb steps and slopes, drop off ledges.</summary>
        Ground = 1 << 0,

        /// <summary>Stop at walls and slide along them.</summary>
        Walls = 1 << 1,

        /// <summary>Be blocked by NPCs and monsters.</summary>
        Entities = 1 << 2,

        All = Ground | Walls | Entities,
    }

    /// <summary>
    /// A session's collision toggles. The player may turn layers off (e.g. entity collision, as Windower's JA0Wait does,
    /// or walls to walk through them), but a layer the server's <see cref="FeatureRestrictions"/> protects stays on.
    /// </summary>
    public sealed class CollisionSettings
    {
        /// <summary>
        /// The layers the player has asked for; <see cref="GetEffective"/> applies the server policy on top.
        /// </summary>
        public CollisionLayers Requested { get; set; } = CollisionLayers.All;

        /// <summary>
        /// Layers that cannot be turned off under the given restrictions.
        /// </summary>
        public static CollisionLayers GetLocked(SessionProfile? profile)
        {
            var locked = CollisionLayers.None;
            if (profile == null) return locked;
            if (profile.IsRestricted(FeatureRestrictions.WallCollisionOverride)) locked |= CollisionLayers.Ground | CollisionLayers.Walls;
            if (profile.IsRestricted(FeatureRestrictions.EntityCollisionOverride)) locked |= CollisionLayers.Entities;
            return locked;
        }

        /// <summary>
        /// The layers in force: the requested ones plus any the server forbids turning off.
        /// </summary>
        public CollisionLayers GetEffective(SessionProfile? profile) => Requested | GetLocked(profile);

        /// <summary>
        /// The steepest floor, in degrees, the local player walks up (#610). LandSandBoat's navmesh walkable slope
        /// (<c>navmesh_config.h</c> <c>walkableSlopeAngle</c>, 46, "Previous xiNavmeshes value"). Round 1 of the 2026-11-20 test
        /// climbed a rock in Valkurm Dunes (zone 103, server position 86.78, -0.98, -103.80) at 55: its way up crosses a
        /// 47-degree stone face; at 46 or 50 the climb stops on the 43-44 degree ledge below the top. Bastok Markets' Path
        /// ramp (zone 234, around -146, -44) climbs on faces of at most about 41 degrees. Provisional: <c>/collision slope</c>
        /// tunes it in game until a retail check sets it.
        /// </summary>
        public const float DefaultMaxClimbSlopeDegrees = 46.0f;

        private float _maxClimbSlopeDegrees = DefaultMaxClimbSlopeDegrees;

        /// <summary>The steepest slope the player has asked to walk up (1-89 degrees); see <see cref="GetEffectiveMaxClimbSlope"/>.</summary>
        public float MaxClimbSlopeDegrees
        {
            get => _maxClimbSlopeDegrees;
            set => _maxClimbSlopeDegrees = Math.Clamp(value, 1.0f, 89.0f);
        }

        /// <summary>
        /// The steepest slope in force: the requested one, but never steeper than <see cref="DefaultMaxClimbSlopeDegrees"/>
        /// while the server protects wall collision (<see cref="FeatureRestrictions.WallCollisionOverride"/>), so the setting
        /// cannot be used to climb walls there.
        /// </summary>
        public float GetEffectiveMaxClimbSlope(SessionProfile? profile) =>
            profile != null && profile.IsRestricted(FeatureRestrictions.WallCollisionOverride)
                ? Math.Min(_maxClimbSlopeDegrees, DefaultMaxClimbSlopeDegrees)
                : _maxClimbSlopeDegrees;

        /// <summary>
        /// Parses a layer name used by the <c>/collision</c> command.
        /// </summary>
        public static bool TryParseLayer(string name, out CollisionLayers layer)
        {
            layer = name.Trim().ToLowerInvariant() switch
            {
                "ground" or "floor" => CollisionLayers.Ground,
                "walls" or "wall" => CollisionLayers.Walls,
                "entities" or "entity" or "mobs" or "npcs" => CollisionLayers.Entities,
                "all" => CollisionLayers.All,
                _ => CollisionLayers.None,
            };
            return layer != CollisionLayers.None;
        }
    }
}
