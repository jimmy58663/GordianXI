// src/Gordian.App/Graphics/ViewportRenderSettings.cs
using System;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// The process-wide render settings every viewport window reads each frame (#301, #322), set from
    /// <see cref="ViewportSettings"/> by the view models (load and Settings changes), so a change applies to pop-out
    /// windows at once.
    /// </summary>
    public static class ViewportRenderSettings
    {
        /// <summary>Default frame rate of a viewport window that is not the focused (or last focused) one.</summary>
        public const int DefaultBackgroundFrameRate = 30;

        /// <summary>Default GPU memory budget for resident zones, in megabytes.</summary>
        public const int DefaultZoneCacheBudgetMb = 1536;

        private static volatile int s_backgroundFrameRate = DefaultBackgroundFrameRate;
        private static volatile int s_zoneCacheBudgetMb = DefaultZoneCacheBudgetMb;
        private static volatile bool s_vsync = true;
        private static volatile int s_performanceOverlay = (int)PerformanceOverlayMode.Hidden;
        private static volatile bool s_retailTerrainBrightness;

        /// <summary>
        /// What the performance overlay shows in every viewport window (#30); hidden by default. Setting a new value raises
        /// <see cref="PerformanceOverlayChanged"/> on the setting thread (the shortcut, <c>/perf</c> or Settings), which
        /// the main window's view model saves.
        /// </summary>
        public static PerformanceOverlayMode PerformanceOverlay
        {
            get => (PerformanceOverlayMode)s_performanceOverlay;
            set
            {
                int mode = Enum.IsDefined(value) ? (int)value : (int)PerformanceOverlayMode.Hidden;
                if (System.Threading.Interlocked.Exchange(ref s_performanceOverlay, mode) != mode) PerformanceOverlayChanged?.Invoke((PerformanceOverlayMode)mode);
            }
        }

        /// <summary>Raised after <see cref="PerformanceOverlay"/> changes.</summary>
        public static event Action<PerformanceOverlayMode>? PerformanceOverlayChanged;

        /// <summary>Frames per second of a background viewport window (clamped to 1-240).</summary>
        public static int BackgroundFrameRate
        {
            get => s_backgroundFrameRate;
            set => s_backgroundFrameRate = ClampFrameRate(value);
        }

        /// <summary>
        /// GPU memory (megabytes) the zones kept resident may use (#322); 0 keeps only the zones on screen.
        /// </summary>
        public static int ZoneCacheBudgetMb
        {
            get => s_zoneCacheBudgetMb;
            set => s_zoneCacheBudgetMb = Math.Clamp(value, 0, 65536);
        }

        /// <summary>Whether the focused viewport window presents on the vertical blank (background ones never do).</summary>
        public static bool VsyncEnabled
        {
            get => s_vsync;
            set => s_vsync = value;
        }

        /// <summary>
        /// Opt-in: draw terrain with the retail client's plain clamp, so noon sand clips to white (#7). Off by default:
        /// terrain highlights roll off softly (<see cref="ZoneTerrainRenderer.TerrainHighlightKnee"/>).
        /// </summary>
        public static bool RetailTerrainBrightness
        {
            get => s_retailTerrainBrightness;
            set => s_retailTerrainBrightness = value;
        }

        public static int ClampFrameRate(int value) => Math.Clamp(value, 1, 240);

        /// <summary>Copies a loaded or changed settings file.</summary>
        public static void Apply(ViewportSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            BackgroundFrameRate = settings.BackgroundFrameRate;
            ZoneCacheBudgetMb = settings.ZoneCacheBudgetMb;
            VsyncEnabled = settings.IsVsyncEnabled;
            PerformanceOverlay = settings.PerformanceOverlay;
            RetailTerrainBrightness = settings.RetailTerrainBrightness;
        }
    }
}
