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
        /// RGBA color of the atmospheric distance fog.
        /// </summary>
        public Vector4 FogColor { get; set; } = new(0.55f, 0.65f, 0.75f, 1.0f);

        /// <summary>
        /// Distance from the camera where fog begins to blend in (in yalms).
        /// </summary>
        public float FogStart { get; set; } = 40.0f;

        /// <summary>
        /// Distance from the camera where scene is fully obscured by fog (in yalms).
        /// </summary>
        public float FogEnd { get; set; } = 250.0f;

        /// <summary>
        /// Exponential fog density factor.
        /// </summary>
        public float FogDensity { get; set; } = 0.005f;

        public static ZoneEnvironmentSettings CreateDay() => new()
        {
            SunDirection = Vector3.Normalize(new Vector3(0.4f, 0.8f, 0.4f)),
            SunColor = new Vector3(1.0f, 0.98f, 0.92f),
            AmbientColor = new Vector3(0.38f, 0.40f, 0.46f),
            FogColor = new Vector4(0.55f, 0.68f, 0.82f, 1.0f),
            FogStart = 50.0f,
            FogEnd = 300.0f
        };

        public static ZoneEnvironmentSettings CreateNight() => new()
        {
            SunDirection = Vector3.Normalize(new Vector3(-0.2f, 0.7f, -0.3f)),
            SunColor = new Vector3(0.25f, 0.30f, 0.45f),
            AmbientColor = new Vector3(0.12f, 0.15f, 0.22f),
            FogColor = new Vector4(0.08f, 0.10f, 0.16f, 1.0f),
            FogStart = 25.0f,
            FogEnd = 160.0f
        };

        public static ZoneEnvironmentSettings CreateDusk() => new()
        {
            SunDirection = Vector3.Normalize(new Vector3(0.7f, 0.3f, 0.2f)),
            SunColor = new Vector3(1.0f, 0.65f, 0.45f),
            AmbientColor = new Vector3(0.30f, 0.25f, 0.35f),
            FogColor = new Vector4(0.65f, 0.45f, 0.40f, 1.0f),
            FogStart = 35.0f,
            FogEnd = 220.0f
        };

        public static ZoneEnvironmentSettings CreateOvercast() => new()
        {
            SunDirection = Vector3.Normalize(new Vector3(0.0f, 1.0f, 0.0f)),
            SunColor = new Vector3(0.55f, 0.55f, 0.58f),
            AmbientColor = new Vector3(0.40f, 0.42f, 0.45f),
            FogColor = new Vector4(0.48f, 0.52f, 0.58f, 1.0f),
            FogStart = 20.0f,
            FogEnd = 140.0f
        };
    }
}
