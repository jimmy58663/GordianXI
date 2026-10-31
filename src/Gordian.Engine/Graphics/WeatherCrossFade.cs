// src/Gordian.Engine/Graphics/WeatherCrossFade.cs
using System;
using Gordian.Core.Resources.Graphics;

namespace Gordian.Engine.Graphics
{
    /// <summary>
    /// Fades the zone environment from one weather to the next instead of switching at once: the outgoing weather's
    /// 0x2F keyframe (lights, fog, sky dome) is blended into the incoming one, and its sky layers fade out as the new
    /// ones fade in. Arriving in a zone takes its weather at once.
    /// Cross-fade as xim's EnvironmentManager.switchWeather, referenced from xi-model-viewer
    /// (https://github.com/vekien/xi-model-viewer, ui/js/environment.js).
    /// </summary>
    public sealed class WeatherCrossFade
    {
        /// <summary>
        /// Length of a weather change, in real seconds (12.5 Vana'diel minutes). *Calibration* from the maintainer's
        /// side-by-side watch of a change in Bibiki Bay (round 1 of 2026-10-29, clouds to sunshine at 22:06): retail
        /// eased over about 10-15 Vana'diel minutes where GordianXI switched at once. xi-model-viewer uses 3.33 seconds.
        /// </summary>
        public const float DurationSeconds = 30.0f;

        /// <summary>The weather being faded in (or fully shown).</summary>
        public string? Weather { get; private set; }

        /// <summary>The weather being faded out, or null when no change is under way.</summary>
        public string? PreviousWeather { get; private set; }

        /// <summary>How far the change has gone: 0 shows only <see cref="PreviousWeather"/>, 1 only <see cref="Weather"/>.</summary>
        public float Blend { get; private set; } = 1.0f;

        /// <summary>Shows <paramref name="weather"/> at once, ending any fade (zone arrival, debug weather and time presets).</summary>
        public void Reset(string weather)
        {
            Weather = weather;
            PreviousWeather = null;
            Blend = 1.0f;
        }

        /// <summary>
        /// Advances the fade by <paramref name="deltaSeconds"/> and starts a new one when the server's weather differs from
        /// the shown one. Returns true while the environment needs re-evaluating (a fade started or is running).
        /// </summary>
        public bool Advance(string weather, float deltaSeconds)
        {
            if (Weather == null)
            {
                Reset(weather);
                return true;
            }
            if (!string.Equals(weather, Weather, StringComparison.OrdinalIgnoreCase))
            {
                // A change during a fade starts from the weather that was fading in.
                PreviousWeather = Weather;
                Weather = weather;
                Blend = 0.0f;
                return true;
            }
            if (PreviousWeather == null) return false;

            Blend = Math.Min(1.0f, Blend + Math.Max(0.0f, deltaSeconds) / DurationSeconds);
            if (Blend >= 1.0f) PreviousWeather = null;
            return true;
        }

        /// <summary>The zone keyframe at <paramref name="hour"/>, blended across a running weather change.</summary>
        public EnvironmentKeyframe? Resolve(ZoneEnvironmentData data, float hour)
        {
            var current = data.Interpolate(hour, Weather);
            if (PreviousWeather == null || current == null) return current;
            var previous = data.Interpolate(hour, PreviousWeather);
            return previous == null ? current : EnvironmentKeyframe.Lerp(previous, current, Blend, current.Hour);
        }

        /// <summary>Hands the weathers and the blend to the renderer, which fades the two weathers' sky layers.</summary>
        public void ApplyTo(ZoneEnvironmentSettings environment)
        {
            environment.WeatherId = Weather ?? environment.WeatherId;
            environment.PreviousWeatherId = PreviousWeather;
            environment.WeatherBlend = Blend;
        }
    }
}
