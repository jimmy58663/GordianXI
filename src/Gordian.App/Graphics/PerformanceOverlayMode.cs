// src/Gordian.App/Graphics/PerformanceOverlayMode.cs
using System;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// What the viewport's performance overlay shows (#30). Hidden by default, as the retail client shows no such thing;
    /// Ctrl+F12 and <c>/perf</c> step through the modes, and the choice is saved with the viewport settings.
    /// </summary>
    public enum PerformanceOverlayMode
    {
        /// <summary>Nothing drawn and nothing measured beyond the frame clock the viewport keeps anyway.</summary>
        Hidden,

        /// <summary>A small FPS and frame time readout in the top-right corner.</summary>
        Fps,

        /// <summary>The frame pacing graph, draw call and mesh counters, CPU and GPU pass timings, memory and device.</summary>
        Detailed,
    }

    /// <summary>Steps and parses <see cref="PerformanceOverlayMode"/> for the shortcut and <c>/perf</c>.</summary>
    public static class PerformanceOverlayModes
    {
        /// <summary>The mode after <paramref name="mode"/>: Hidden, FPS, detailed, then Hidden again.</summary>
        public static PerformanceOverlayMode Next(PerformanceOverlayMode mode) => mode switch
        {
            PerformanceOverlayMode.Hidden => PerformanceOverlayMode.Fps,
            PerformanceOverlayMode.Fps => PerformanceOverlayMode.Detailed,
            _ => PerformanceOverlayMode.Hidden,
        };

        /// <summary>Parses a <c>/perf</c> argument (off, fps, full and a few aliases).</summary>
        public static bool TryParse(string text, out PerformanceOverlayMode mode)
        {
            switch (text.Trim().ToLowerInvariant())
            {
                case "off" or "hide" or "hidden" or "none" or "0":
                    mode = PerformanceOverlayMode.Hidden;
                    return true;
                case "fps" or "on" or "1":
                    mode = PerformanceOverlayMode.Fps;
                    return true;
                case "full" or "detailed" or "detail" or "stats" or "all" or "2":
                    mode = PerformanceOverlayMode.Detailed;
                    return true;
                default:
                    mode = PerformanceOverlayMode.Hidden;
                    return false;
            }
        }

        /// <summary>The mode's name as <c>/perf</c> takes it.</summary>
        public static string Name(PerformanceOverlayMode mode) => mode switch
        {
            PerformanceOverlayMode.Fps => "fps",
            PerformanceOverlayMode.Detailed => "full",
            _ => "off",
        };

        /// <summary>
        /// Handles <c>/perf</c>: no argument or <c>next</c> steps to the next mode, otherwise sets the named one. The mode is
        /// process-wide (every viewport window) and saved with the viewport settings. Returns the reply.
        /// </summary>
        public static string HandleCommand(string args)
        {
            string arg = (args ?? string.Empty).Trim();
            PerformanceOverlayMode mode;
            if (arg.Length == 0 || arg.Equals("next", StringComparison.OrdinalIgnoreCase) || arg.Equals("cycle", StringComparison.OrdinalIgnoreCase))
            {
                mode = Next(ViewportRenderSettings.PerformanceOverlay);
            }
            else if (!TryParse(arg, out mode))
            {
                return "Usage: /perf [off | fps | full | next] (Ctrl+F12 cycles).";
            }
            ViewportRenderSettings.PerformanceOverlay = mode;
            return mode switch
            {
                PerformanceOverlayMode.Fps => "Performance overlay: FPS.",
                PerformanceOverlayMode.Detailed => "Performance overlay: full (frame graph, draw calls, pass timings).",
                _ => "Performance overlay: hidden.",
            };
        }
    }
}
