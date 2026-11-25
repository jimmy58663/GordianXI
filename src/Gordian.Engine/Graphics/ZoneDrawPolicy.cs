// src/Gordian.Engine/Graphics/ZoneDrawPolicy.cs
using System;

namespace Gordian.Engine.Graphics
{
    /// <summary>The draw-distance presets (#606 round 4, #195): what a zone draws, how far, and how far it sways.</summary>
    public enum ZoneDrawPreset
    {
        /// <summary>The vanilla retail client's distances (the default).</summary>
        Retail = 0,

        /// <summary>
        /// Three times retail's distances, near what Windower's DrawDistance plugin gave (the far crags and islands draw,
        /// textured): GordianXI's default before round 4. Costs the most.
        /// </summary>
        Extended = 1,

        /// <summary>For older cards: small assets end sooner, full detail only at the authored distances, no sway far out.</summary>
        Low = 2,

        /// <summary>For integrated graphics: the shortest distances, sway only close by.</summary>
        Minimum = 3,
    }

    /// <summary>
    /// What a zone draws and how far (#606 round 4). Zone placements fall in two classes by the data, not by size:
    /// <list type="bullet">
    /// <item><b>Large assets</b>: placements without an own draw distance (terrain blocks, cliffs, buildings), culled at
    /// the zone environment's draw distance times <see cref="LargeMultiplier"/>. The outdoor fog has its own
    /// <see cref="FogMultiplier"/>: past the fog end and inside the cut, terrain shows as fog-coloured silhouettes.</item>
    /// <item><b>Small assets</b>: placements the authors gave an own distance (record <c>+0x40</c> with <c>+0x46</c> bit 0:
    /// grass, bushes, flowers, trees, rocks; in Konschtat 50, 70, 200 and 300 yalms), culled at that distance times
    /// <see cref="SmallMultiplier"/>.</item>
    /// </list>
    /// A submesh smaller than about a pixel on screen is skipped when <see cref="SubPixelCull"/> is on, and swaying
    /// meshes farther than <see cref="SwayRange"/> hold their authored pose. See docs/rendering/viewport-and-terrain.md
    /// "Draw policy (#606 round 4)".
    /// </summary>
    /// <param name="LargeMultiplier">Scale on the environment's draw distance (far clip, large assets).</param>
    /// <param name="SmallMultiplier">Scale on placements' own draw distances.</param>
    /// <param name="FogMultiplier">Scale on the authored outdoor fog distances (never past the large-asset cut).</param>
    /// <param name="LodDistanceScale">Scale on the placements' detail thresholds (<see cref="ZoneVisibility.DefaultLodDistanceScale"/>).</param>
    /// <param name="SubPixelCull">Skip placed submeshes whose bounds cover less than <see cref="SubPixelRadius"/> pixels.</param>
    /// <param name="SwayRange">Yalms from the camera within which swaying meshes move; infinity everywhere (retail).</param>
    public readonly record struct ZoneDrawPolicy(
        float LargeMultiplier,
        float SmallMultiplier,
        float FogMultiplier,
        float LodDistanceScale,
        bool SubPixelCull,
        float SwayRange)
    {
        /// <summary>
        /// The on-screen radius (pixels) below which <see cref="SubPixelCull"/> skips a placed submesh: its whole bounding
        /// sphere fits in a pixel. Konschtat's valley view, Retail preset: no visible change (docs).
        /// </summary>
        public const float SubPixelRadius = 0.5f;

        /// <summary>
        /// The vanilla retail client (no Windower, no add-ons; TESTING-2026-11-22 C4, image-14 to image-17): large assets to
        /// twice the environment's distance (Konschtat's skyline matches from 1.5 to 2.25, PROVISIONAL), own distances as
        /// authored, vanilla retail's outdoor fog (multiplier 1; the curve is <see cref="ZoneEnvironmentSettings.OutdoorFogEndScale"/>),
        /// with full detail out to 5/3 of the authored thresholds as the maintainer asked (#70 round 1).
        /// </summary>
        public static readonly ZoneDrawPolicy Retail = new(ZoneVisibility.DefaultDrawDistanceMultiplier, ZoneVisibility.DefaultSmallDrawDistanceMultiplier, 1.0f,
                                                           ZoneVisibility.DefaultLodDistanceScale, true, float.PositiveInfinity);

        /// <summary>GordianXI's distances before round 4 (3 on everything), near what Windower's DrawDistance plugin gave.</summary>
        public static readonly ZoneDrawPolicy Extended = new(3.0f, 3.0f, 3.0f, ZoneVisibility.DefaultLodDistanceScale, true, float.PositiveInfinity);

        /// <summary>For older cards.</summary>
        public static readonly ZoneDrawPolicy Low = new(1.25f, 0.75f, 1.0f, 1.0f, true, 60.0f);

        /// <summary>For integrated graphics.</summary>
        public static readonly ZoneDrawPolicy Minimum = new(1.0f, 0.5f, 1.0f, 1.0f, true, 30.0f);

        /// <summary>The policy of a preset (Retail for an unknown value).</summary>
        public static ZoneDrawPolicy For(ZoneDrawPreset preset) => preset switch
        {
            ZoneDrawPreset.Extended => Extended,
            ZoneDrawPreset.Low => Low,
            ZoneDrawPreset.Minimum => Minimum,
            _ => Retail,
        };

        /// <summary>Copies the distances into an environment (the next <see cref="ZoneEnvironmentSettings.ApplyKeyframe"/> scales the fog).</summary>
        public void ApplyTo(ZoneEnvironmentSettings environment)
        {
            ArgumentNullException.ThrowIfNull(environment);
            environment.DrawDistanceMultiplier = LargeMultiplier;
            environment.SmallDrawDistanceMultiplier = SmallMultiplier;
            environment.FogDistanceMultiplier = FogMultiplier;
            environment.LodDistanceScale = LodDistanceScale;
        }
    }
}
