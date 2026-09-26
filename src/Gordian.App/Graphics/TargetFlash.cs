// src/Gordian.App/Graphics/TargetFlash.cs
using System;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// The flash a newly selected target plays: three short pulses that brighten its model. Changing target restarts
    /// the flash on the new target, cutting the old one short.
    /// <para>
    /// Measured from a retail capture (2026-09-26, 30 fps): each pulse rises and falls linearly over about 0.72 s
    /// (22 frames), three in a row, and at the peak the model's colour roughly doubles per channel, most for the
    /// channels the scene light is weakest in. That is light added before the texture is modulated, not a colour laid
    /// over the model, so the flash adds <see cref="PeakLight"/> to the actor's light at its peak.
    /// </para>
    /// </summary>
    public static class TargetFlash
    {
        public const double PulseSeconds = 0.72;
        public const int Pulses = 3;
        public const float PeakLight = 0.5f;

        public static double DurationSeconds => PulseSeconds * Pulses;

        /// <summary>
        /// The light added to the target <paramref name="secondsSinceSelected"/> after it was selected (0 once done).
        /// </summary>
        public static float Intensity(double secondsSinceSelected)
        {
            if (secondsSinceSelected < 0 || secondsSinceSelected >= DurationSeconds) return 0.0f;
            double phase = secondsSinceSelected % PulseSeconds / PulseSeconds;
            return PeakLight * (float)(1.0 - Math.Abs(2.0 * phase - 1.0));
        }
    }
}
