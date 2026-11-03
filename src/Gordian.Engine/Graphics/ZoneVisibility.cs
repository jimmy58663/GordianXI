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
        /// The client's configuration multiplier on the environment's draw distance, on placements' own distances and on
        /// the outdoor fog distances (xi-tools: <c>GetAnotherSomething</c>, a registry float; the fog coupling per its "Fog
        /// and draw distance coupling"). Not measured directly. 3 is the smallest whole value at which the Manaclipper's
        /// far islands, which retail draws (capture 2026-10-21), all draw offscreen, and with the fog scaled by it the far
        /// terrain stays textured as in the round 1 captures (2026-10-31). It is the default of
        /// <see cref="ZoneEnvironmentSettings.DrawDistanceMultiplier"/>, the one value a draw-distance setting (#195) changes.
        /// </summary>
        public const float DefaultDrawDistanceMultiplier = 3.0f;

        /// <summary>
        /// How far below the camera eye (yalms) a floor still picks the culling table; with no floor that close, no table
        /// applies and everything draws. GordianXI's bound (*inference*): retail seen from 200 yalms over Valkurm draws the
        /// whole area around (round 1 capture image-20, 2026-10-31), where the hilltop 200 yalms below would pick a table
        /// that leaves most of it out. A camera in play sits well within it (the Metalworks ledge view, 24 yalms over the
        /// hall floor below, passed in game).
        /// </summary>
        public const float CameraFloorReach = 30.0f;

        /// <summary>
        /// Scale on the records' +0x38 / +0x3C detail thresholds. **Not retail** (the client uses them as they are): the
        /// maintainer asked for full detail out to about 50 yalms (round 1, 2026-10-31), and 5/3 moves West Ronfaure's
        /// 30-yalm tree threshold to 50. The one value a detail setting (#195) changes.
        /// </summary>
        public const float DefaultLodDistanceScale = 5.0f / 3.0f;

        /// <summary>
        /// The global draw distance (far clip and cull distance for placements without their own): the environment's
        /// (0x2F keyframe +0x58) times the multiplier, at least <see cref="MinimumDrawDistance"/>. It is not stretched to
        /// the fog end: retail cuts terrain off in blocks before the fog covers it (round 1 capture image-20, Valkurm from
        /// 200 yalms up).
        /// </summary>
        public static float GlobalDrawDistance(float environmentDrawDistance, float multiplier = DefaultDrawDistanceMultiplier) =>
            MathF.Max(MinimumDrawDistance, environmentDrawDistance * multiplier);

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
                                     float multiplier = DefaultDrawDistanceMultiplier, float lodScale = DefaultLodDistanceScale)
        {
            if (visibility.IsUnplaced) return true;
            if (table >= 0 && visibility.Record >= 0 && tables != null && !tables.IsListed(table, visibility.Record)) return false;

            float distanceSquared = Vector3.DistanceSquared(eye, visibility.Origin);
            float limit = visibility.OwnDrawDistance > 0.0f ? visibility.OwnDrawDistance * multiplier : globalDistance;
            if (distanceSquared > limit * limit) return false;

            return visibility.Slots == LodSlots.All
                || (visibility.Slots & SelectSlot(distanceSquared, visibility.LodNear * lodScale, visibility.LodMid * lodScale)) != 0;
        }
    }
}
