// src/Gordian.Core/Resources/Graphics/ZoneEnvironmentData.cs
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// Represents one elevation slice of the FFXI sky dome.
    /// Elevation is normalized (0.0 at the horizon, 1.0 at the celestial zenith).
    /// </summary>
    public readonly record struct SkySlice(Vector4 Color, float Elevation);

    /// <summary>
    /// Decoded time-of-day lighting, fog, and sky dome keyframe from FFXI DAT Section 0x2F.
    /// Protocol and binary chunk layout referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer).
    /// </summary>
    public sealed class EnvironmentKeyframe
    {
        /// <summary>
        /// Time of day hour (0 to 23).
        /// </summary>
        public int Hour { get; set; } = 12;

        /// <summary>
        /// Indicates whether this environment applies to an indoor or cave area (no celestial skybox).
        /// </summary>
        public bool Indoors { get; set; }

        /// <summary>
        /// Background clear color (RGBA).
        /// </summary>
        public Vector4 ClearColor { get; set; } = new(0.55f, 0.65f, 0.75f, 1.0f);

        /// <summary>
        /// Maximum view/draw distance for terrain and objects (in yalms).
        /// </summary>
        public float DrawDistance { get; set; } = 300.0f;

        /// <summary>
        /// Number of radial spokes in the sky dome tessellation.
        /// </summary>
        public ushort Spokes { get; set; } = 16;

        /// <summary>
        /// Radius of the sky dome (in yalms).
        /// </summary>
        public float Radius { get; set; } = 1000.0f;

        // Terrain Light Config
        public Vector4 TerrainSunColor { get; set; } = Vector4.One;
        public Vector4 TerrainMoonColor { get; set; } = Vector4.Zero;
        public Vector4 TerrainAmbientColor { get; set; } = new(0.35f, 0.38f, 0.45f, 1.0f);
        public Vector4 TerrainFogColor { get; set; } = new(0.55f, 0.65f, 0.75f, 1.0f);
        public float TerrainFogStart { get; set; } = 40.0f;
        public float TerrainFogEnd { get; set; } = 250.0f;
        public float TerrainDiffuseMult { get; set; } = 1.0f;

        // Model Light Config
        public Vector4 ModelSunColor { get; set; } = Vector4.One;
        public Vector4 ModelMoonColor { get; set; } = Vector4.Zero;
        public Vector4 ModelAmbientColor { get; set; } = new(0.35f, 0.38f, 0.45f, 1.0f);
        public Vector4 ModelFogColor { get; set; } = new(0.55f, 0.65f, 0.75f, 1.0f);
        public float ModelFogStart { get; set; } = 40.0f;
        public float ModelFogEnd { get; set; } = 250.0f;
        public float ModelDiffuseMult { get; set; } = 1.0f;

        /// <summary>
        /// Elevation slices (up to 8) defining the celestial sky gradient from horizon to zenith.
        /// </summary>
        public List<SkySlice> Slices { get; } = new();
    }

    /// <summary>
    /// Container holding all decoded Section 0x2F environment keyframes for a zone, grouped by weather.
    /// Provides smooth time-of-day interpolation.
    /// </summary>
    public sealed class ZoneEnvironmentData
    {
        private readonly Dictionary<string, List<EnvironmentKeyframe>> _weatherKeyframes =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, List<EnvironmentKeyframe>> WeatherKeyframes => _weatherKeyframes;

        public void AddKeyframe(string weather, EnvironmentKeyframe keyframe)
        {
            if (!_weatherKeyframes.TryGetValue(weather, out var list))
            {
                list = new List<EnvironmentKeyframe>();
                _weatherKeyframes[weather] = list;
            }

            list.Add(keyframe);
            list.Sort((a, b) => a.Hour.CompareTo(b.Hour));
        }

        /// <summary>
        /// Interpolates lighting, fog, and sky dome slices for the given time of day (in hours, e.g. 14.5 for 2:30 PM).
        /// </summary>
        public EnvironmentKeyframe? Interpolate(float timeOfDayHours, string? weather = null)
        {
            if (_weatherKeyframes.Count == 0) return null;

            List<EnvironmentKeyframe>? frames = null;
            if (!string.IsNullOrEmpty(weather))
            {
                _weatherKeyframes.TryGetValue(weather, out frames);
            }

            if (frames == null || frames.Count == 0)
            {
                // Fallback to "weat", "fine", "suny", "default", or first available weather
                foreach (var pref in new[] { "weat", "fine", "suny", "clod", "default" })
                {
                    if (_weatherKeyframes.TryGetValue(pref, out frames) && frames.Count > 0)
                    {
                        break;
                    }
                }

                if (frames == null || frames.Count == 0)
                {
                    using var it = _weatherKeyframes.Values.GetEnumerator();
                    if (it.MoveNext()) frames = it.Current;
                }
            }

            if (frames == null || frames.Count == 0) return null;
            if (frames.Count == 1) return frames[0];

            // Normalize time to [0, 24)
            float tHours = ((timeOfDayHours % 24f) + 24f) % 24f;

            // Find floor and ceil keyframes
            EnvironmentKeyframe floor = frames[^1];
            EnvironmentKeyframe ceil = frames[0];

            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i].Hour <= tHours)
                {
                    floor = frames[i];
                }
            }

            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i].Hour > tHours)
                {
                    ceil = frames[i];
                    break;
                }
            }

            if (floor.Hour == ceil.Hour) return floor;

            float t0 = floor.Hour;
            float t1 = ceil.Hour <= floor.Hour ? ceil.Hour + 24f : ceil.Hour;
            float currentT = tHours < floor.Hour ? tHours + 24f : tHours;
            float factor = Math.Clamp((currentT - t0) / (t1 - t0), 0.0f, 1.0f);

            var result = new EnvironmentKeyframe
            {
                Hour = (int)tHours,
                Indoors = floor.Indoors,
                ClearColor = Vector4.Lerp(floor.ClearColor, ceil.ClearColor, factor),
                DrawDistance = MathF.Max(10f, float.Lerp(floor.DrawDistance, ceil.DrawDistance, factor)),
                Spokes = floor.Spokes > 0 ? floor.Spokes : (ushort)16,
                Radius = float.Lerp(floor.Radius, ceil.Radius, factor),

                TerrainSunColor = Vector4.Lerp(floor.TerrainSunColor, ceil.TerrainSunColor, factor),
                TerrainMoonColor = Vector4.Lerp(floor.TerrainMoonColor, ceil.TerrainMoonColor, factor),
                TerrainAmbientColor = Vector4.Lerp(floor.TerrainAmbientColor, ceil.TerrainAmbientColor, factor),
                TerrainFogColor = Vector4.Lerp(floor.TerrainFogColor, ceil.TerrainFogColor, factor),
                TerrainFogStart = float.Lerp(floor.TerrainFogStart, ceil.TerrainFogStart, factor),
                TerrainFogEnd = float.Lerp(floor.TerrainFogEnd, ceil.TerrainFogEnd, factor),
                TerrainDiffuseMult = float.Lerp(floor.TerrainDiffuseMult, ceil.TerrainDiffuseMult, factor),

                ModelSunColor = Vector4.Lerp(floor.ModelSunColor, ceil.ModelSunColor, factor),
                ModelMoonColor = Vector4.Lerp(floor.ModelMoonColor, ceil.ModelMoonColor, factor),
                ModelAmbientColor = Vector4.Lerp(floor.ModelAmbientColor, ceil.ModelAmbientColor, factor),
                ModelFogColor = Vector4.Lerp(floor.ModelFogColor, ceil.ModelFogColor, factor),
                ModelFogStart = float.Lerp(floor.ModelFogStart, ceil.ModelFogStart, factor),
                ModelFogEnd = float.Lerp(floor.ModelFogEnd, ceil.ModelFogEnd, factor),
                ModelDiffuseMult = float.Lerp(floor.ModelDiffuseMult, ceil.ModelDiffuseMult, factor)
            };

            // Interpolate sky slices
            int sliceCount = Math.Min(floor.Slices.Count, ceil.Slices.Count);
            for (int i = 0; i < sliceCount; i++)
            {
                var s0 = floor.Slices[i];
                var s1 = ceil.Slices[i];
                var c = Vector4.Lerp(s0.Color, s1.Color, factor);
                var elev = float.Lerp(s0.Elevation, s1.Elevation, factor);
                result.Slices.Add(new SkySlice(c, elev));
            }

            return result;
        }
    }
}
