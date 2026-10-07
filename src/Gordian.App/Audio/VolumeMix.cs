// src/Gordian.App/Audio/VolumeMix.cs
using System;
using System.Collections.Generic;

namespace Gordian.App.Audio
{
    /// <summary>
    /// Maps the config page's two volume sliders (<c>StockUiSettingKey.MusicVolume</c> and <c>SoundEffectsVolume</c>,
    /// 0-100, stored per character in <c>ui_settings/&lt;name&gt;.json</c>) to the mixer buses.
    /// </summary>
    public static class VolumeMix
    {
        /// <summary>The slider maximum.</summary>
        public const int SliderMax = 100;

        /// <summary>A slider value as a linear gain (provisional: retail's curve is not measured).</summary>
        public static float SliderGain(int value) => Math.Clamp(value, 0, SliderMax) / (float)SliderMax;

        /// <summary>The gain of every bus for the two sliders.</summary>
        public static IEnumerable<(AudioCategory Category, float Gain)> CategoryGains(int musicSlider, int effectsSlider)
        {
            float effects = SliderGain(effectsSlider);
            yield return (AudioCategory.Music, SliderGain(musicSlider));
            yield return (AudioCategory.Effects, effects);
            yield return (AudioCategory.System, effects);
            yield return (AudioCategory.Zone, effects);
            yield return (AudioCategory.Notification, effects);
        }
    }
}
