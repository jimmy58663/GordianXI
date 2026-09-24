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
        /// Vana'diel time of day in hours [0, 24). Drives the time-of-day keyframe curves
        /// (Section 0x19, sampled by generator clock updaters) of stars and the moon.
        /// </summary>
        public float TimeOfDayHours { get; set; } = 12.0f;

        /// <summary>
        /// RGB intensity and color of the primary directional sunlight.
        /// </summary>
        public Vector3 SunColor { get; set; } = new(0.5f, 0.49f, 0.46f);

        /// <summary>
        /// RGB intensity and color of the directional moonlight, shining opposite the sun (outdoors only).
        /// </summary>
        public Vector3 MoonColor { get; set; } = Vector3.Zero;

        /// <summary>
        /// Model (actor and effect) sun and moon light colors from the 0x2F model lighting block, converted like the
        /// terrain lights. Daylight-tinted particles take the stronger of the two.
        /// </summary>
        public Vector3 ModelSunColor { get; set; } = new(0.5f, 0.49f, 0.46f);

        /// <inheritdoc cref="ModelSunColor"/>
        public Vector3 ModelMoonColor { get; set; } = Vector3.Zero;

        /// <summary>
        /// RGB intensity and color of the ambient light preventing completely black shadows.
        /// </summary>
        public Vector3 AmbientColor { get; set; } = new(0.18f, 0.19f, 0.23f);

        /// <summary>
        /// Controls whether atmospheric distance fog blending is enabled.
        /// When false, fog blending is skipped in shaders.
        /// </summary>
        public bool FogEnabled { get; set; } = false;

        /// <summary>
        /// RGBA color of the atmospheric distance fog.
        /// </summary>
        public Vector4 FogColor { get; set; } = new(0.58f, 0.72f, 0.88f, 1.0f);

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
        /// Framebuffer clear color. Synchronized with SkyHorizonColor for seamless outdoor horizon blending.
        /// </summary>
        public Vector4 ClearColor { get; set; } = new(0.58f, 0.72f, 0.88f, 1.0f);

        /// <summary>
        /// Effective terrain draw distance in yalms.
        /// </summary>
        public float DrawDistance { get; set; } = 300.0f;

        /// <summary>
        /// Radial spokes for sky dome geometry.
        /// </summary>
        public ushort Spokes { get; set; } = 16;

        /// <summary>
        /// Active weather ID (e.g. "fine", "suny", "clod", "mist", "rain", "snow", "thdr").
        /// Used by the weather sky renderer to select active cloud layers.
        /// </summary>
        public string WeatherId { get; set; } = "fine";

        /// <summary>
        /// Indicates whether this environment configuration represents an indoor or cave area (no celestial skybox or ocean plane).
        /// </summary>
        public bool Indoors { get; set; } = false;

        /// <summary>
        /// Sets the Vana'diel time of day and the matching celestial sun direction.
        /// </summary>
        public void SetTimeOfDay(float vanaHour)
        {
            TimeOfDayHours = vanaHour;
            SunDirection = World.VanaTime.GetSunDirection(vanaHour);
        }

        /// <summary>
        /// Applies an authentic decoded FFXI Section 0x2F keyframe to these environment settings.
        /// </summary>
        public void ApplyKeyframe(Resources.Graphics.EnvironmentKeyframe keyframe)
        {
            if (keyframe == null) return;

            Indoors = keyframe.Indoors;
            float diffuseMult = keyframe.TerrainDiffuseMult > 0f ? keyframe.TerrainDiffuseMult : 1.0f;
            SunColor = DiffuseToLight(keyframe.TerrainSunColor, diffuseMult);
            // Indoors the moon slot packs a signed light direction rather than a color.
            MoonColor = keyframe.Indoors ? Vector3.Zero : DiffuseToLight(keyframe.TerrainMoonColor, diffuseMult);
            AmbientColor = AmbientToLight(keyframe.TerrainAmbientColor);
            float modelDiffuseMult = keyframe.ModelDiffuseMult > 0f ? keyframe.ModelDiffuseMult : 1.0f;
            ModelSunColor = DiffuseToLight(keyframe.ModelSunColor, modelDiffuseMult);
            ModelMoonColor = keyframe.Indoors ? Vector3.Zero : DiffuseToLight(keyframe.ModelMoonColor, modelDiffuseMult);
            FogColor = keyframe.TerrainFogColor;

            // FFXI retail keyframes often author FogStart=0 because original PS2 hardware used an
            // exponential fog curve that remained transparent until near the horizon.
            // For linear GPU fog in outdoor environments, push FogStart to 75% of FogEnd (or keyframe.TerrainFogStart)
            // so islands and distant mountains remain crisp and authentic while far terrain blends cleanly.
            bool hasFog = keyframe.TerrainFogEnd > keyframe.TerrainFogStart && keyframe.TerrainFogEnd > 0;
            FogEnabled = hasFog;
            if (hasFog)
            {
                if (!keyframe.Indoors)
                {
                    FogStart = MathF.Max(keyframe.TerrainFogStart, keyframe.TerrainFogEnd * 0.75f);
                }
                else
                {
                    FogStart = keyframe.TerrainFogStart;
                }
            }
            else
            {
                FogStart = keyframe.TerrainFogStart;
            }
            FogEnd = keyframe.TerrainFogEnd;

            DrawDistance = keyframe.DrawDistance;
            Spokes = keyframe.Spokes;

            SkySlices.Clear();
            for (int i = 0; i < keyframe.Slices.Count; i++)
            {
                SkySlices.Add(keyframe.Slices[i]);
            }

            if (SkySlices.Count >= 1)
            {
                SkyHorizonColor = SkySlices[0].Color;
                if (SkySlices.Count >= 2)
                {
                    SkyZenithColor = SkySlices[^1].Color;
                }
            }

            // In retail FFXI (and xi-model-viewer / xim getBackgroundColor), the outdoor clear/horizon color
            // is synchronized with the lowest sky dome slice (horizon ring) for seamless background composition.
            if (!keyframe.Indoors && SkySlices.Count >= 1)
            {
                ClearColor = SkyHorizonColor;
            }
            else if (keyframe.Indoors && keyframe.ClearColor != Vector4.Zero)
            {
                ClearColor = keyframe.ClearColor;
            }
            else
            {
                ClearColor = keyframe.ClearColor != Vector4.Zero ? keyframe.ClearColor : SkyHorizonColor;
            }
        }

        // Colors whose channels all sit below 0xCC are lifted by this per-channel bias.
        private const float DarkLightThreshold = 0xCC / 255.0f;
        private static readonly Vector3 DarkLightBias = new(1.4f, 1.36f, 1.45f);

        /// <summary>
        /// Converts an authored 0x2F ambient color (normalized RGBA) into the shader ambient term:
        /// half the byte value, dark-color bias, clamped to 0.5.
        /// Lighting conversion referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer,
        /// ui/js/environment.js, after xim EnvironmentLighting.ambientToColor).
        /// </summary>
        public static Vector3 AmbientToLight(Vector4 authored)
        {
            var c = new Vector3(authored.X, authored.Y, authored.Z);
            var bias = IsDark(c) ? DarkLightBias : Vector3.One;
            return Vector3.Min(new Vector3(0.5f), bias * c * 0.5f);
        }

        /// <summary>
        /// Converts an authored 0x2F sun/moon color (normalized RGBA) and diffuse multiplier into the shader
        /// directional light color: scaled by the multiplier, dark-color bias, clamped to 1.
        /// Lighting conversion referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer,
        /// ui/js/environment.js, after xim EnvironmentLighting.diffuseToColor).
        /// </summary>
        public static Vector3 DiffuseToLight(Vector4 authored, float diffuseMult)
        {
            var c = new Vector3(authored.X, authored.Y, authored.Z) * diffuseMult;
            var bias = IsDark(c) ? DarkLightBias : Vector3.One;
            return Vector3.Min(Vector3.One, bias * c);
        }

        private static bool IsDark(Vector3 c) =>
            c.X < DarkLightThreshold && c.Y < DarkLightThreshold && c.Z < DarkLightThreshold;

        public static ZoneEnvironmentSettings CreateDay() => new()
        {
            TimeOfDayHours = 12.0f,
            SunDirection = Vector3.Normalize(new Vector3(0.4f, 0.8f, 0.4f)),
            SunColor = new Vector3(0.5f, 0.49f, 0.46f),
            AmbientColor = new Vector3(0.19f, 0.2f, 0.23f),
            FogEnabled = false,
            FogColor = new Vector4(0.58f, 0.72f, 0.88f, 1.0f),
            FogStart = 400.0f,
            FogEnd = 1200.0f,
            SkyZenithColor = new Vector4(0.22f, 0.45f, 0.85f, 1.0f),
            SkyHorizonColor = new Vector4(0.58f, 0.72f, 0.88f, 1.0f),
            ClearColor = new Vector4(0.58f, 0.72f, 0.88f, 1.0f)
        };

        public static ZoneEnvironmentSettings CreateNight() => new()
        {
            TimeOfDayHours = 0.0f,
            SunDirection = Vector3.Normalize(new Vector3(-0.2f, -0.7f, -0.3f)),
            SunColor = new Vector3(0.125f, 0.15f, 0.225f),
            AmbientColor = new Vector3(0.06f, 0.075f, 0.11f),
            FogEnabled = false,
            FogColor = new Vector4(0.08f, 0.12f, 0.20f, 1.0f),
            FogStart = 300.0f,
            FogEnd = 900.0f,
            SkyZenithColor = new Vector4(0.02f, 0.03f, 0.08f, 1.0f),
            SkyHorizonColor = new Vector4(0.08f, 0.12f, 0.20f, 1.0f),
            ClearColor = new Vector4(0.08f, 0.12f, 0.20f, 1.0f)
        };

        public static ZoneEnvironmentSettings CreateDusk() => new()
        {
            TimeOfDayHours = 18.0f,
            SunDirection = Vector3.Normalize(new Vector3(0.7f, 0.3f, 0.2f)),
            SunColor = new Vector3(0.5f, 0.325f, 0.225f),
            AmbientColor = new Vector3(0.15f, 0.125f, 0.175f),
            FogEnabled = false,
            FogColor = new Vector4(0.85f, 0.42f, 0.25f, 1.0f),
            FogStart = 350.0f,
            FogEnd = 1000.0f,
            SkyZenithColor = new Vector4(0.20f, 0.18f, 0.42f, 1.0f),
            SkyHorizonColor = new Vector4(0.85f, 0.42f, 0.25f, 1.0f),
            ClearColor = new Vector4(0.85f, 0.42f, 0.25f, 1.0f)
        };

        public static ZoneEnvironmentSettings CreateOvercast() => new()
        {
            TimeOfDayHours = 12.0f,
            SunDirection = Vector3.Normalize(new Vector3(0.0f, 1.0f, 0.0f)),
            SunColor = new Vector3(0.275f, 0.275f, 0.29f),
            AmbientColor = new Vector3(0.2f, 0.21f, 0.225f),
            FogEnabled = true,
            FogColor = new Vector4(0.52f, 0.55f, 0.60f, 1.0f),
            FogStart = 150.0f,
            FogEnd = 600.0f,
            SkyZenithColor = new Vector4(0.42f, 0.45f, 0.50f, 1.0f),
            SkyHorizonColor = new Vector4(0.52f, 0.55f, 0.60f, 1.0f),
            ClearColor = new Vector4(0.52f, 0.55f, 0.60f, 1.0f)
        };
    }
}
