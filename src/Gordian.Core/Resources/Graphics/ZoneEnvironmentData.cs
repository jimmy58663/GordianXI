// src/Gordian.Core/Resources/Graphics/ZoneEnvironmentData.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.World;

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

        /// <summary>
        /// Blends two keyframes component by component: <paramref name="t"/> 0 gives <paramref name="a"/>, 1 gives
        /// <paramref name="b"/>. Used between a weather's time-of-day keyframes and across a weather change.
        /// Interpolation as xim's computeInterpolatedEnvResource, referenced from xi-model-viewer
        /// (https://github.com/vekien/xi-model-viewer, ui/js/environment.js resolveEnvironment / lerpEnv).
        /// </summary>
        public static EnvironmentKeyframe Lerp(EnvironmentKeyframe a, EnvironmentKeyframe b, float t, int hour)
        {
            var result = new EnvironmentKeyframe
            {
                Hour = hour,
                Indoors = a.Indoors,
                ClearColor = Vector4.Lerp(a.ClearColor, b.ClearColor, t),
                DrawDistance = MathF.Max(10f, float.Lerp(a.DrawDistance, b.DrawDistance, t)),
                Spokes = a.Spokes > 0 ? a.Spokes : (ushort)16,
                Radius = float.Lerp(a.Radius, b.Radius, t),

                TerrainSunColor = Vector4.Lerp(a.TerrainSunColor, b.TerrainSunColor, t),
                TerrainMoonColor = Vector4.Lerp(a.TerrainMoonColor, b.TerrainMoonColor, t),
                TerrainAmbientColor = Vector4.Lerp(a.TerrainAmbientColor, b.TerrainAmbientColor, t),
                TerrainFogColor = Vector4.Lerp(a.TerrainFogColor, b.TerrainFogColor, t),
                TerrainFogStart = float.Lerp(a.TerrainFogStart, b.TerrainFogStart, t),
                TerrainFogEnd = float.Lerp(a.TerrainFogEnd, b.TerrainFogEnd, t),
                TerrainDiffuseMult = float.Lerp(a.TerrainDiffuseMult, b.TerrainDiffuseMult, t),

                ModelSunColor = Vector4.Lerp(a.ModelSunColor, b.ModelSunColor, t),
                ModelMoonColor = Vector4.Lerp(a.ModelMoonColor, b.ModelMoonColor, t),
                ModelAmbientColor = Vector4.Lerp(a.ModelAmbientColor, b.ModelAmbientColor, t),
                ModelFogColor = Vector4.Lerp(a.ModelFogColor, b.ModelFogColor, t),
                ModelFogStart = float.Lerp(a.ModelFogStart, b.ModelFogStart, t),
                ModelFogEnd = float.Lerp(a.ModelFogEnd, b.ModelFogEnd, t),
                ModelDiffuseMult = float.Lerp(a.ModelDiffuseMult, b.ModelDiffuseMult, t)
            };

            // Interpolate sky slices
            int sliceCount = Math.Min(a.Slices.Count, b.Slices.Count);
            for (int i = 0; i < sliceCount; i++)
            {
                var s0 = a.Slices[i];
                var s1 = b.Slices[i];
                var c = Vector4.Lerp(s0.Color, s1.Color, t);
                var elev = float.Lerp(s0.Elevation, s1.Elevation, t);
                result.Slices.Add(new SkySlice(c, elev));
            }

            return result;
        }
    }

    /// <summary>
    /// Container holding all decoded Section 0x2F environment keyframes for a zone, grouped by weather.
    /// Provides smooth time-of-day interpolation.
    /// </summary>
    public sealed class ZoneEnvironmentData
    {
        private readonly Dictionary<string, List<EnvironmentKeyframe>> _weatherKeyframes =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, KeyFrameCurve> _keyFrameCurves =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ParticleGeneratorDefinition> _particleGenerators =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly List<WeatherSkyLayer> _weatherSkyLayers = new();

        public IReadOnlyDictionary<string, List<EnvironmentKeyframe>> WeatherKeyframes => _weatherKeyframes;
        public IReadOnlyDictionary<string, KeyFrameCurve> KeyFrameCurves => _keyFrameCurves;
        public IReadOnlyDictionary<string, ParticleGeneratorDefinition> ParticleGenerators => _particleGenerators;
        public IReadOnlyList<WeatherSkyLayer> WeatherSkyLayers => _weatherSkyLayers;

        /// <summary>
        /// Continuous horizontal (U) and vertical (V) drift velocity for zone water surfaces (rivers, ocean plane).
        /// Extracted from Section 0x05 water generators (e.g. umi, shi, sea) or default ocean drift velocity.
        /// </summary>
        public Vector2 WaterUVScroll { get; set; } = new Vector2(0.015f, -0.045f);

        private readonly Dictionary<string, Dictionary<string, List<EnvironmentKeyframe>>> _subEnvironments =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Ids of the zone's sub-environments (e.g. <c>ev01</c>): indoor areas whose placements link to them through
        /// ZoneDef record +0x4C and are lit by their own keyframes instead of the outdoor weather.
        /// </summary>
        public IEnumerable<string> SubEnvironmentIds => _subEnvironments.Keys;

        public void AddKeyframe(string weather, EnvironmentKeyframe keyframe) => Add(_weatherKeyframes, weather, keyframe);

        /// <summary>
        /// Adds a keyframe of sub-environment <paramref name="environmentId"/> (a <c>ev??</c> directory) for a weather.
        /// </summary>
        public void AddSubEnvironmentKeyframe(string environmentId, string weather, EnvironmentKeyframe keyframe)
        {
            if (string.IsNullOrEmpty(environmentId)) return;
            if (!_subEnvironments.TryGetValue(environmentId, out var byWeather))
            {
                byWeather = new Dictionary<string, List<EnvironmentKeyframe>>(StringComparer.OrdinalIgnoreCase);
                _subEnvironments[environmentId] = byWeather;
            }
            Add(byWeather, weather, keyframe);
        }

        /// <summary>
        /// Interpolates sub-environment <paramref name="environmentId"/>'s lighting for the time and weather, or null when
        /// the zone has no such sub-environment.
        /// </summary>
        public EnvironmentKeyframe? InterpolateSubEnvironment(string environmentId, float timeOfDayHours, string? weather = null) =>
            _subEnvironments.TryGetValue(environmentId, out var byWeather) ? Interpolate(byWeather, timeOfDayHours, weather) : null;

        private static void Add(Dictionary<string, List<EnvironmentKeyframe>> source, string weather, EnvironmentKeyframe keyframe)
        {
            if (!source.TryGetValue(weather, out var list))
            {
                list = new List<EnvironmentKeyframe>();
                source[weather] = list;
            }

            list.Add(keyframe);
            list.Sort((a, b) => a.Hour.CompareTo(b.Hour));
        }

        public void AddKeyFrameCurve(string key, KeyFrameCurve curve)
        {
            if (!string.IsNullOrEmpty(key) && curve != null)
            {
                _keyFrameCurves[key] = curve;
            }
        }

        public void AddParticleGenerator(string key, ParticleGeneratorDefinition generator)
        {
            if (!string.IsNullOrEmpty(key) && generator != null)
            {
                _particleGenerators[key] = generator;
            }
        }

        public void AddWeatherSkyLayer(WeatherSkyLayer layer)
        {
            if (layer != null)
            {
                _weatherSkyLayers.Add(layer);
            }
        }

        /// <summary>
        /// Interpolates lighting, fog, and sky dome slices for the given time of day (in hours, e.g. 14.5 for 2:30 PM).
        /// </summary>
        public EnvironmentKeyframe? Interpolate(float timeOfDayHours, string? weather = null) =>
            Interpolate(_weatherKeyframes, timeOfDayHours, weather);

        private static EnvironmentKeyframe? Interpolate(Dictionary<string, List<EnvironmentKeyframe>> source, float timeOfDayHours, string? weather)
        {
            if (source.Count == 0) return null;

            List<EnvironmentKeyframe>? frames = null;
            if (!string.IsNullOrEmpty(weather))
            {
                source.TryGetValue(weather, out frames);
                if (frames == null || frames.Count == 0)
                {
                    string canonical = VanaTime.GetCanonicalWeatherCategory(weather);
                    if (!string.Equals(canonical, weather, StringComparison.OrdinalIgnoreCase))
                    {
                        source.TryGetValue(canonical, out frames);
                    }
                }
            }

            if (frames == null || frames.Count == 0)
            {
                // Fallback: If requested weather maps to an overcast or foggy category, prioritize "clod" or "mist"
                // before "fine" so stormy weathers receive dark overcast lighting and fog instead of sunny daylight.
                string canonical = VanaTime.GetCanonicalWeatherCategory(weather);
                string[] fallbackOrder = string.Equals(canonical, "clod", StringComparison.OrdinalIgnoreCase)
                    ? new[] { "clod", "mist", "fine", "suny", "weat", "default" }
                    : string.Equals(canonical, "mist", StringComparison.OrdinalIgnoreCase)
                        ? new[] { "mist", "clod", "fine", "suny", "weat", "default" }
                        : new[] { "fine", "suny", "clod", "mist", "weat", "default" };

                foreach (var pref in fallbackOrder)
                {
                    if (source.TryGetValue(pref, out frames) && frames.Count > 0)
                    {
                        break;
                    }
                }

                if (frames == null || frames.Count == 0)
                {
                    using var it = source.Values.GetEnumerator();
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

            return EnvironmentKeyframe.Lerp(floor, ceil, factor, (int)tHours);
        }
    }
}
