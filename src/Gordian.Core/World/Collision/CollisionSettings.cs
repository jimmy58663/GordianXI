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
        public const float MaxClimbSlopeDegrees = 55.0f;

        /// <summary>
        /// The steepest <see cref="CollisionTerrain.Stone"/> floor the local player walks up outside built planes
        /// (<see cref="MinStonePlaneArea"/>; #610 rounds 4 and 5). Round 3 of the 2026-11-20 test: in retail the
        /// Valkurm Dunes rock at server position (86.78, -0.98, -103.80) stops the player at the edges where its sand
        /// triangles (40-42 degrees, #83697 / #83700 / #82300) meet the stone ones (#83701 at 29, #85523 at 39), the line
        /// between the sand and rock textures (the maintainer's screenshot). Neither side carries the wall bit, and the stone
        /// is gentler than the sand, so only the terrain changes there. 27 degrees sits just above the 1-in-2 grade (26.6
        /// degrees) city ramps are built at, such as Northern San d'Oria's at server (-197, -8, 153), whose faces are too
        /// small to count as a plane, and under the rock's gentlest stopping face (29); runs from the spot climb no higher
        /// than at 25 (2.48 yalms, on the sand). In field zones Stone is the rock and cliff terrain (the steep walls by
        /// Selbina's gate are all Stone). Provisional.
        /// </summary>
        public const float StoneClimbSlopeDegrees = 27.0f;

        /// <summary>
        /// The smallest flat plane (square yalms, <see cref="ZoneCollisionMesh.GetPlaneArea"/>) whose
        /// <see cref="CollisionTerrain.Stone"/> faces count as built stairs or ramps and follow <see cref="MaxClimbSlopeDegrees"/> instead
        /// of <see cref="StoneClimbSlopeDegrees"/> (#610 round 5). City stairs and ramps are planes of many triangles:
        /// Bastok Mines' 27-degree flights are planes of 107-272 square yalms and its 28-30 degree one 32; its 45-degree flight
        /// at server (-40, 7.6, -112) is one quad, two triangles of 17.7 together, the smallest built flight found. In the
        /// open world 70-74 % of steep Stone faces stand alone and the rest form pieces of up to 64 square yalms; 17 keeps
        /// that flight climbable and, of runs straight up steep stone (351 in Valkurm Dunes, 329 in Konschtat Highlands, 348
        /// in Jugner Forest), lets 0, 13 and 16 climb 2 yalms or more higher than with no exemption (any plane: 10, 19 and
        /// 50). The Valkurm rock's stopping faces have no coplanar partner. Provisional.
        /// </summary>
        public const float MinStonePlaneArea = 17.0f;

        /// <summary>
        /// The steepest floor, in degrees, the local player walks up: <see cref="StoneClimbSlopeDegrees"/> on a
        /// <see cref="CollisionTerrain.Stone"/> face outside a flat plane of <see cref="MinStonePlaneArea"/> square yalms or
        /// more (<paramref name="planeArea"/>, from <see cref="ZoneCollisionMesh.GetPlaneArea"/>), else
        /// <see cref="MaxClimbSlopeDegrees"/>. Fixed values, not player settings, so they need no cap under the server's
        /// <see cref="FeatureRestrictions.WallCollisionOverride"/> (#610).
        /// </summary>
        public static float ClimbSlopeDegrees(CollisionTerrain terrain, float planeArea) =>
            terrain == CollisionTerrain.Stone && !(planeArea > 0.0f && planeArea >= MinStonePlaneArea)
                ? StoneClimbSlopeDegrees
                : MaxClimbSlopeDegrees;

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
