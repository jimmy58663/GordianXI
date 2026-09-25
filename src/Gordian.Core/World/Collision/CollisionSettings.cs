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
