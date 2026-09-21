// src/Gordian.Core/Graphics/ZoneEnvironmentSettings.cs
using System;
using System.Numerics;

namespace Gordian.Core.Graphics
{
    /// <summary>
    /// Encapsulates lighting and authentic FFXI distance fog parameters for 3D zone rendering.
    /// Matched to the environment uniform buffer layout consumed by the zone shader pipeline.
    /// </summary>
    public sealed class ZoneEnvironmentSettings
    {
        /// <summary>
        /// Direction towards the directional light source (Sun or Moon), normalized.
        /// </summary>
        public Vector3 SunDirection { get; set; } = Vector3.Normalize(new Vector3(0.4f, 0.8f, 0.4f));

        /// <summary>
        /// RGB intensity and color of the primary directional sunlight.
        /// </summary>
        public Vector3 SunColor { get; set; } = new(1.0f, 0.98f, 0.92f);

        /// <summary>
        /// RGB intensity and color of the ambient light preventing completely black shadows.
        /// </summary>
        public Vector3 AmbientColor { get; set; } = new(0.35f, 0.38f, 0.45f);

        /// <summary>
        /// Controls whether atmospheric distance fog blending is enabled.
        /// When false, fog blending is skipped in shaders.
        /// </summary>
        public bool FogEnabled { get; set; } = false;

        /// <summary>
        /// RGBA color of the atmospheric distance fog.
        /// </summary>
        public Vector4 FogColor { get; set; } = new(0.55f, 0.65f, 0.75f, 1.0f);

        /// <summary>
        /// Distance from the camera where fog begins to blend in (in yalms).
        /// </summary>
        public float FogStart { get; set; } = 400.0f;

        /// <summary>
        /// Distance from the camera where scene is fully obscured by fog (in yalms).
        /// </summary>
        public float FogEnd { get; set; } = 1200.0f;

        /// <summary>
        /// Exponential fog density factor.
        /// </summary>
        public float FogDensity { get; set; } = 0.005f;

        /// <summary>
        /// Color at the celestial zenith of the sky dome (+Y).
        /// </summary>
        public Vector4 SkyZenithColor { get; set; } = new(0.25f, 0.48f, 0.85f, 1.0f);

        /// <summary>
        /// Color at the horizon ring of the sky dome.
        /// </summary>
        public Vector4 SkyHorizonColor { get; set; } = new(0.58f, 0.72f, 0.88f, 1.0f);

        /// <summary>
        /// Optional explicit elevation slices decoded from DAT Section 0x2F.
        /// When empty, the sky dome renderer smoothly gradients from SkyHorizonColor to SkyZenithColor.
        /// </summary>
        public System.Collections.Generic.List<Resources.Graphics.SkySlice> SkySlices { get; } = new();

        /// <summary>
        /// Framebuffer clear color.
        /// </summary>
        public Vector4 ClearColor { get; set; } = new(0.55f, 0.65f, 0.75f, 1.0f);

        /// <summary>
        /// Effective terrain draw distance in yalms.
        /// </summary>
        public float DrawDistance { get; set; } = 300.0f;

        /// <summary>
        /// Radial spokes for sky dome geometry.
        /// </summary>
        public ushort Spokes { get; set; } = 16;

        /// <summary>
        /// Applies an authentic decoded FFXI Section 0x2F keyframe to these environment settings.
        /// </summary>
        public void ApplyKeyframe(Resources.Graphics.EnvironmentKeyframe keyframe)
        {
            if (keyframe == null) return;

            SunColor = new Vector3(keyframe.TerrainSunColor.X, keyframe.TerrainSunColor.Y, keyframe.TerrainSunColor.Z);
            AmbientColor = new Vector3(keyframe.TerrainAmbientColor.X, keyframe.TerrainAmbientColor.Y, keyframe.TerrainAmbientColor.Z);
            FogColor = keyframe.TerrainFogColor;

            // FFXI retail keyframes often author FogStart=0 because original PS2 hardware used an
            // exponential fog curve that remained transparent until near the horizon.
            // For linear GPU fog, push FogStart to 75% of FogEnd (or keyframe.TerrainFogStart) to preserve clear vision across the zone.
            bool hasFog = keyframe.TerrainFogEnd > keyframe.TerrainFogStart && keyframe.TerrainFogEnd > 0;
            FogEnabled = hasFog;
            if (hasFog && keyframe.TerrainFogStart <= 0)
            {
                FogStart = MathF.Max(keyframe.TerrainFogStart, keyframe.TerrainFogEnd * 0.75f);
            }
            else
            {
                FogStart = keyframe.TerrainFogStart;
            }
            FogEnd = keyframe.TerrainFogEnd;

            ClearColor = keyframe.ClearColor;
            DrawDistance = keyframe.DrawDistance;
            Spokes = keyframe.Spokes;

            SkySlices.Clear();
            for (int i = 0; i < keyframe.Slices.Count; i++)
            {
                SkySlices.Add(keyframe.Slices[i]);
            }

            if (SkySlices.Count >= 2)
            {
                SkyHorizonColor = SkySlices[0].Color;
                SkyZenithColor = SkySlices[^1].Color;
            }
        }

        public static ZoneEnvironmentSettings CreateDay() => new()
        {
            SunDirection = Vector3.Normalize(new Vector3(0.4f, 0.8f, 0.4f)),
            SunColor = new Vector3(1.0f, 0.98f, 0.92f),
            AmbientColor = new Vector3(0.38f, 0.40f, 0.46f),
            FogEnabled = false,
            FogColor = new Vector4(0.55f, 0.68f, 0.82f, 1.0f),
            FogStart = 400.0f,
            FogEnd = 1200.0f,
            SkyZenithColor = new Vector4(0.22f, 0.45f, 0.85f, 1.0f),
            SkyHorizonColor = new Vector4(0.58f, 0.72f, 0.88f, 1.0f)
        };

        public static ZoneEnvironmentSettings CreateNight() => new()
        {
            SunDirection = Vector3.Normalize(new Vector3(-0.2f, 0.7f, -0.3f)),
            SunColor = new Vector3(0.25f, 0.30f, 0.45f),
            AmbientColor = new Vector3(0.12f, 0.15f, 0.22f),
            FogEnabled = false,
            FogColor = new Vector4(0.08f, 0.10f, 0.16f, 1.0f),
            FogStart = 300.0f,
            FogEnd = 900.0f,
            SkyZenithColor = new Vector4(0.02f, 0.03f, 0.08f, 1.0f),
            SkyHorizonColor = new Vector4(0.08f, 0.12f, 0.20f, 1.0f)
        };

        public static ZoneEnvironmentSettings CreateDusk() => new()
        {
            SunDirection = Vector3.Normalize(new Vector3(0.7f, 0.3f, 0.2f)),
            SunColor = new Vector3(1.0f, 0.65f, 0.45f),
            AmbientColor = new Vector3(0.30f, 0.25f, 0.35f),
            FogEnabled = false,
            FogColor = new Vector4(0.65f, 0.45f, 0.40f, 1.0f),
            FogStart = 350.0f,
            FogEnd = 1000.0f,
            SkyZenithColor = new Vector4(0.20f, 0.18f, 0.42f, 1.0f),
            SkyHorizonColor = new Vector4(0.85f, 0.42f, 0.25f, 1.0f)
        };

        public static ZoneEnvironmentSettings CreateOvercast() => new()
        {
            SunDirection = Vector3.Normalize(new Vector3(0.0f, 1.0f, 0.0f)),
            SunColor = new Vector3(0.55f, 0.55f, 0.58f),
            AmbientColor = new Vector3(0.40f, 0.42f, 0.45f),
            FogEnabled = true,
            FogColor = new Vector4(0.48f, 0.52f, 0.58f, 1.0f),
            FogStart = 150.0f,
            FogEnd = 600.0f,
            SkyZenithColor = new Vector4(0.42f, 0.45f, 0.50f, 1.0f),
            SkyHorizonColor = new Vector4(0.52f, 0.55f, 0.60f, 1.0f)
        };
    }
}
