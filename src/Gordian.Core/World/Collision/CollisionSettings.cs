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
        /// The steepest floor, in degrees, the local player walks up on every terrain but <see cref="CollisionTerrain.Stone"/>
        /// (#610 round 4). Bastok Mines' Path ramp (zone 234, around -146, -44) reaches its landing only above 45 degrees (its
        /// top face is a 45-degree Path triangle; at 46 runs stopped 3.5 yalms up, under the landing), and round 2 of the
        /// 2026-11-20 test found 40 blocking slopes retail climbs; the Valkurm Dunes sand the maintainer walks up is 40-42
        /// degrees. Steep sand is rare (Valkurm Dunes has 25 sand faces past 60 degrees against 9167 stone ones). Provisional.
        /// </summary>
        public const float DefaultMaxClimbSlopeDegrees = 55.0f;

        /// <summary>
        /// The steepest <see cref="CollisionTerrain.Stone"/> floor the local player walks up outside built planes
        /// (<see cref="DefaultMinStonePlaneArea"/>; #610 rounds 4 and 5). Round 3 of the 2026-11-20 test: in retail the
        /// Valkurm Dunes rock at server position (86.78, -0.98, -103.80) stops the player at the edges where its sand
        /// triangles (40-42 degrees, #83697 / #83700 / #82300) meet the stone ones (#83701 at 29, #85523 at 39), the line
        /// between the sand and rock textures (the maintainer's screenshot). Neither side carries the wall bit, and the stone
        /// is gentler than the sand, so only the terrain changes there. 27 degrees sits just above the 1-in-2 grade (26.6
        /// degrees) city ramps are built at, such as Northern San d'Oria's at server (-197, -8, 153), whose faces are too
        /// small to count as a plane, and under the rock's gentlest stopping face (29); runs from the spot climb no higher
        /// than at 25 (2.48 yalms, on the sand). In field zones Stone is the rock and cliff terrain (the steep walls by
        /// Selbina's gate are all Stone). Provisional.
        /// </summary>
        public const float DefaultStoneClimbSlopeDegrees = 27.0f;

        /// <summary>
        /// The smallest flat plane (square yalms, <see cref="ZoneCollisionMesh.GetPlaneArea"/>) whose
        /// <see cref="CollisionTerrain.Stone"/> faces count as built stairs or ramps and follow the general climb limit instead
        /// of <see cref="DefaultStoneClimbSlopeDegrees"/> (#610 round 5). City stairs and ramps are planes of many triangles:
        /// Bastok Mines' 27-degree flights are planes of 107-272 square yalms and its 28-30 degree one 32; its 45-degree flight
        /// at server (-40, 7.6, -112) is one quad, two triangles of 17.7 together, the smallest built flight found. In the
        /// open world 70-74 % of steep Stone faces stand alone and the rest form pieces of up to 64 square yalms; 17 keeps
        /// that flight climbable and, of runs straight up steep stone (351 in Valkurm Dunes, 329 in Konschtat Highlands, 348
        /// in Jugner Forest), lets 0, 13 and 16 climb 2 yalms or more higher than with no exemption (any plane: 10, 19 and
        /// 50). The Valkurm rock's stopping faces have no coplanar partner. Provisional.
        /// </summary>
        public const float DefaultMinStonePlaneArea = 17.0f;

        /// <summary>The built-in climb limit for faces of <paramref name="terrain"/>.</summary>
        public static float DefaultClimbSlope(CollisionTerrain terrain) =>
            terrain == CollisionTerrain.Stone ? DefaultStoneClimbSlopeDegrees : DefaultMaxClimbSlopeDegrees;

        private float _maxClimbSlopeDegrees = DefaultMaxClimbSlopeDegrees;
        private float _minStonePlaneArea = DefaultMinStonePlaneArea;

        /// <summary>
        /// The smallest plane whose Stone faces the player has asked to climb as stairs (0-10000 square yalms; 10000 turns the
        /// exemption off); see <see cref="GetEffectiveMinStonePlaneArea"/>.
        /// </summary>
        public float MinStonePlaneArea
        {
            get => _minStonePlaneArea;
            set => _minStonePlaneArea = Math.Clamp(value, 0.0f, 10000.0f);
        }

        /// <summary>
        /// The plane size in force: the requested one, but never smaller than <see cref="DefaultMinStonePlaneArea"/> while the
        /// server protects wall collision (<see cref="FeatureRestrictions.WallCollisionOverride"/>).
        /// </summary>
        public float GetEffectiveMinStonePlaneArea(SessionProfile? profile) =>
            profile != null && profile.IsRestricted(FeatureRestrictions.WallCollisionOverride)
                ? Math.Max(_minStonePlaneArea, DefaultMinStonePlaneArea)
                : _minStonePlaneArea;

        /// <summary>
        /// The climb limit in force on a floor face: a <see cref="CollisionTerrain.Stone"/> face in a plane of at least
        /// <see cref="GetEffectiveMinStonePlaneArea"/> square yalms follows the general limit, as built stairs and ramps; any
        /// other face its terrain's (<see cref="GetEffectiveMaxClimbSlope(SessionProfile?, CollisionTerrain)"/>).
        /// </summary>
        public float GetEffectiveMaxClimbSlope(SessionProfile? profile, CollisionTerrain terrain, float planeArea) =>
            terrain == CollisionTerrain.Stone && planeArea > 0.0f && planeArea >= GetEffectiveMinStonePlaneArea(profile)
                ? GetEffectiveMaxClimbSlope(profile)
                : GetEffectiveMaxClimbSlope(profile, terrain);
        private readonly float?[] _terrainClimbSlopes = CreateTerrainDefaults();

        private static float?[] CreateTerrainDefaults()
        {
            var limits = new float?[16];
            limits[(int)CollisionTerrain.Stone] = DefaultStoneClimbSlopeDegrees;
            return limits;
        }

        /// <summary>
        /// The steepest slope the player has asked to walk up (1-89 degrees) on terrain without a limit of its own; see
        /// <see cref="GetEffectiveMaxClimbSlope(SessionProfile?, CollisionTerrain)"/>.
        /// </summary>
        public float MaxClimbSlopeDegrees
        {
            get => _maxClimbSlopeDegrees;
            set => _maxClimbSlopeDegrees = Math.Clamp(value, 1.0f, 89.0f);
        }

        /// <summary>A terrain type's own climb limit in degrees, or null when it uses <see cref="MaxClimbSlopeDegrees"/>.</summary>
        public float? GetTerrainClimbSlope(CollisionTerrain terrain) =>
            (uint)terrain < (uint)_terrainClimbSlopes.Length ? _terrainClimbSlopes[(int)terrain] : null;

        /// <summary>Sets a terrain type's own climb limit (1-89 degrees), or with null makes it follow <see cref="MaxClimbSlopeDegrees"/>.</summary>
        public void SetTerrainClimbSlope(CollisionTerrain terrain, float? degrees)
        {
            if ((uint)terrain >= (uint)_terrainClimbSlopes.Length) return;
            _terrainClimbSlopes[(int)terrain] = degrees is { } d ? Math.Clamp(d, 1.0f, 89.0f) : null;
        }

        /// <summary>Puts every climb limit back to the built-in ones.</summary>
        public void ResetClimbSlopes()
        {
            _maxClimbSlopeDegrees = DefaultMaxClimbSlopeDegrees;
            _minStonePlaneArea = DefaultMinStonePlaneArea;
            var defaults = CreateTerrainDefaults();
            Array.Copy(defaults, _terrainClimbSlopes, defaults.Length);
        }

        /// <summary>
        /// The climb limit in force on a face of <paramref name="terrain"/>: its own limit if set, else the general one, but
        /// never steeper than the built-in one (<see cref="DefaultClimbSlope"/>) while the server protects wall collision
        /// (<see cref="FeatureRestrictions.WallCollisionOverride"/>), so the setting cannot be used to climb walls there.
        /// </summary>
        public float GetEffectiveMaxClimbSlope(SessionProfile? profile, CollisionTerrain terrain)
        {
            float requested = GetTerrainClimbSlope(terrain) ?? _maxClimbSlopeDegrees;
            return profile != null && profile.IsRestricted(FeatureRestrictions.WallCollisionOverride)
                ? Math.Min(requested, DefaultClimbSlope(terrain))
                : requested;
        }

        /// <summary>The general climb limit in force (terrain without a limit of its own).</summary>
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
