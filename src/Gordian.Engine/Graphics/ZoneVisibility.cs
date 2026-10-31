// src/Gordian.Engine/Graphics/ZoneVisibility.cs
using System;
using System.Numerics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;

namespace Gordian.Engine.Graphics
{
    /// <summary>
    /// Which zone placements draw this frame (#70), in the client's order: the culling table picked by the camera's floor,
    /// then the distance from the camera eye to the placement origin against the placement's own draw distance (record
    /// +0x40, when record +0x46 bit 0 is set) or the global draw distance, then the level-of-detail slot by the record's
    /// +0x38 / +0x3C thresholds. Frustum culling is the renderer's and comes after.
    /// Order and rules referenced from xi-tools (docs/zone/lod-draw-distance.md "Culling pipeline", docs/zone/format.md
    /// "Visibility", https://github.com/vekien/xi-tools; offsets checked on the retail data, see
    /// docs/rendering/viewport-and-terrain.md "Zone visibility").
    /// </summary>
    public static class ZoneVisibility
    {
        /// <summary>The client's floor for the global draw distance (xi-tools: <c>MinimumDrawDistance</c> = 43).</summary>
        public const float MinimumDrawDistance = 43.0f;

        /// <summary>
        /// The client's configuration multiplier on the environment's draw distance and on placements' own distances
        /// (xi-tools: <c>GetAnotherSomething</c>, a registry float). Its retail value is not measured yet (#70 asks the
        /// maintainer for a capture). 3 meanwhile, an *interim* value: the smallest whole multiplier at which the
        /// Manaclipper's far islands, which retail draws (maintainer's capture, 2026-10-21), are all drawn offscreen; at 1
        /// they all vanish and at 2 the eastern group does. 1 would also leave the retail fog ends past the draw distance
        /// in most outdoor zones (see <see cref="GlobalDrawDistance"/>).
        /// </summary>
        public const float DefaultDrawDistanceMultiplier = 3.0f;

        /// <summary>
        /// The global draw distance (far clip and cull distance for placements without their own): the environment's
        /// (0x2F keyframe +0x58) times <see cref="DefaultDrawDistanceMultiplier"/>, at least <see cref="MinimumDrawDistance"/>,
        /// and never short of the fog end. The retail data's fog ends run past the raw +0x58 value in most outdoor zones
        /// (West Ronfaure 252.5 against fog up to 450, Valkurm 302 against 450, the Manaclipper 802 against 1,000), and
        /// the client's fog is authored to cover the draw distance (xi-tools "Fog and draw distance coupling"), so the far
        /// edge is kept under the fog instead of cutting clear terrain (*inference*, pending the retail capture).
        /// </summary>
        public static float GlobalDrawDistance(float environmentDrawDistance, float fogEnd, float multiplier = DefaultDrawDistanceMultiplier)
        {
            float distance = MathF.Max(MinimumDrawDistance, environmentDrawDistance * multiplier);
            return float.IsFinite(fogEnd) && fogEnd > distance ? fogEnd : distance;
        }

        /// <summary>
        /// The detail slot a placement at squared distance <paramref name="distanceSquared"/> draws: high inside
        /// <paramref name="near"/>, mid up to <paramref name="mid"/>, low beyond (the client's <c>RenderChunk2</c>).
        /// </summary>
        public static LodSlots SelectSlot(float distanceSquared, float near, float mid)
        {
            if (distanceSquared <= mid * mid) return distanceSquared < near * near ? LodSlots.High : LodSlots.Mid;
            return LodSlots.Low;
        }

        /// <summary>
        /// Whether a placed submesh draws for a camera at <paramref name="eye"/> (display space, the frame its
        /// <see cref="PlacementVisibility.Origin"/> is in). <paramref name="table"/> is the camera floor's culling table
        /// (-1 for none); <paramref name="globalDistance"/> is <see cref="GlobalDrawDistance"/>.
        /// </summary>
        public static bool IsVisible(in PlacementVisibility visibility, Vector3 eye, ZoneCullingTables? tables, int table, float globalDistance,
                                     float multiplier = DefaultDrawDistanceMultiplier)
        {
            if (visibility.IsUnplaced) return true;
            if (table >= 0 && visibility.Record >= 0 && tables != null && !tables.IsListed(table, visibility.Record)) return false;

            float distanceSquared = Vector3.DistanceSquared(eye, visibility.Origin);
            float limit = visibility.OwnDrawDistance > 0.0f ? visibility.OwnDrawDistance * multiplier : globalDistance;
            if (distanceSquared > limit * limit) return false;

            return visibility.Slots == LodSlots.All
                || (visibility.Slots & SelectSlot(distanceSquared, visibility.LodNear, visibility.LodMid)) != 0;
        }
    }
}
