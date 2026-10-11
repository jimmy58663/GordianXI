// src/Gordian.App/Graphics/ViewportRenderMode.cs
using System;

namespace Gordian.App.Graphics
{
    /// <summary>How often a viewport window draws (#301).</summary>
    public enum ViewportRenderMode
    {
        /// <summary>The focused viewport window (or the one focused last while another program has the focus): every frame.</summary>
        Full,

        /// <summary>Any other viewport window: at <see cref="ViewportRenderSettings.BackgroundFrameRate"/>, without VSync.</summary>
        Background,

        /// <summary>Minimised, hidden or fully covered: no frames; only the per-frame game logic ticks, slowly.</summary>
        Paused,
    }

    /// <summary>Which <see cref="ViewportRenderMode"/> a viewport window draws in (#301).</summary>
    public static class ViewportRenderPolicy
    {
        /// <summary>How often a paused viewport still ticks its game logic (sound, action playback, zone loads).</summary>
        public static readonly TimeSpan PausedTickInterval = TimeSpan.FromMilliseconds(100);

        /// <summary>How often a background window checks whether other windows cover it.</summary>
        public static readonly TimeSpan OcclusionCheckInterval = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// The mode of a window: paused while minimised or not shown, full for the focus target (the focused viewport
        /// window, or the one focused last while no viewport has the focus), background otherwise.
        /// </summary>
        public static ViewportRenderMode Resolve(bool minimized, bool shown, bool isFocusTarget) =>
            minimized || !shown ? ViewportRenderMode.Paused
            : isFocusTarget ? ViewportRenderMode.Full
            : ViewportRenderMode.Background;

        /// <summary>The time between the starts of two frames at <paramref name="framesPerSecond"/>.</summary>
        public static TimeSpan FrameInterval(int framesPerSecond) =>
            TimeSpan.FromSeconds(1.0 / ViewportRenderSettings.ClampFrameRate(framesPerSecond));
    }
}
