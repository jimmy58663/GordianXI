// tests/Gordian.App.Tests/Graphics/PerformanceOverlayTests.cs
using System;
using System.IO;
using Gordian.App.Graphics;
using Gordian.App.ViewModels;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>The performance overlay's modes, its persisted setting and its frame statistics (#30).</summary>
    // The overlay mode is process-wide (ViewportRenderSettings) and every view model built elsewhere resets it: run alone (#451).
    [Collection(ViewportRenderSettingsCollection.Name)]
    public class PerformanceOverlayTests
    {
        [Fact]
        public void Modes_CycleHiddenFpsDetailed()
        {
            Assert.Equal(PerformanceOverlayMode.Fps, PerformanceOverlayModes.Next(PerformanceOverlayMode.Hidden));
            Assert.Equal(PerformanceOverlayMode.Detailed, PerformanceOverlayModes.Next(PerformanceOverlayMode.Fps));
            Assert.Equal(PerformanceOverlayMode.Hidden, PerformanceOverlayModes.Next(PerformanceOverlayMode.Detailed));
        }

        [Theory]
        [InlineData("off", PerformanceOverlayMode.Hidden)]
        [InlineData("FPS", PerformanceOverlayMode.Fps)]
        [InlineData("full", PerformanceOverlayMode.Detailed)]
        [InlineData("detailed", PerformanceOverlayMode.Detailed)]
        public void TryParse_ReadsTheCommandNames(string text, PerformanceOverlayMode expected)
        {
            Assert.True(PerformanceOverlayModes.TryParse(text, out var mode));
            Assert.Equal(expected, mode);
        }

        [Fact]
        public void Default_IsHiddenLikeRetail()
        {
            Assert.Equal(PerformanceOverlayMode.Hidden, new ViewportSettings().PerformanceOverlay);
        }

        [Fact]
        public void Command_SetsOrStepsTheProcessWideMode()
        {
            var before = ViewportRenderSettings.PerformanceOverlay;
            try
            {
                ViewportRenderSettings.PerformanceOverlay = PerformanceOverlayMode.Hidden;
                PerformanceOverlayMode? raised = null;
                void OnChanged(PerformanceOverlayMode m) => raised = m;
                ViewportRenderSettings.PerformanceOverlayChanged += OnChanged;
                try
                {
                    Assert.Contains("FPS", PerformanceOverlayModes.HandleCommand(""));
                    Assert.Equal(PerformanceOverlayMode.Fps, ViewportRenderSettings.PerformanceOverlay);
                    Assert.Equal(PerformanceOverlayMode.Fps, raised);

                    Assert.Contains("full", PerformanceOverlayModes.HandleCommand("full"));
                    Assert.Equal(PerformanceOverlayMode.Detailed, ViewportRenderSettings.PerformanceOverlay);

                    Assert.Contains("hidden", PerformanceOverlayModes.HandleCommand("next"));
                    Assert.Equal(PerformanceOverlayMode.Hidden, ViewportRenderSettings.PerformanceOverlay);

                    Assert.Contains("Usage", PerformanceOverlayModes.HandleCommand("loud"));
                    Assert.Equal(PerformanceOverlayMode.Hidden, ViewportRenderSettings.PerformanceOverlay);
                }
                finally
                {
                    ViewportRenderSettings.PerformanceOverlayChanged -= OnChanged;
                }
            }
            finally
            {
                ViewportRenderSettings.PerformanceOverlay = before;
            }
        }

        [Fact]
        public void Mode_IsSavedWhenTheShortcutChangesIt()
        {
            string path = Path.Combine(Path.GetTempPath(), $"gordian_perf_overlay_{Guid.NewGuid():N}.json");
            var before = ViewportRenderSettings.PerformanceOverlay;
            try
            {
                var vm = new ViewportViewModel(path);
                Assert.Equal(PerformanceOverlayMode.Hidden, vm.PerformanceOverlay);

                // Ctrl+F12 / /perf set the process-wide mode; the saving view model writes it to the settings file.
                ViewportRenderSettings.PerformanceOverlay = PerformanceOverlayMode.Detailed;
                Assert.Equal(PerformanceOverlayMode.Detailed, vm.PerformanceOverlay);
                Assert.Equal(PerformanceOverlayMode.Detailed, ViewportSettings.LoadOrDefault(path).PerformanceOverlay);

                vm.PerformanceOverlay = PerformanceOverlayMode.Fps;
                Assert.Equal(PerformanceOverlayMode.Fps, ViewportRenderSettings.PerformanceOverlay);
                Assert.Equal(PerformanceOverlayMode.Fps, ViewportSettings.LoadOrDefault(path).PerformanceOverlay);

                // A new session starts in the saved mode.
                ViewportRenderSettings.PerformanceOverlay = PerformanceOverlayMode.Hidden;
                var reloaded = new ViewportViewModel(path, enableAutoSave: false);
                Assert.Equal(PerformanceOverlayMode.Hidden, reloaded.PerformanceOverlay); // the first view model saved Hidden
            }
            finally
            {
                ViewportRenderSettings.PerformanceOverlay = before;
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void SavedOpenGLPreference_LoadsAsAuto()
        {
            string path = Path.Combine(Path.GetTempPath(), $"gordian_backend_{Guid.NewGuid():N}.json");
            try
            {
                new ViewportSettings { SelectedBackend = GraphicsBackendPreference.OpenGL }.SaveToFile(path);
                var vm = new ViewportViewModel(path, enableAutoSave: false);
                Assert.Equal(GraphicsBackendPreference.Auto, vm.SelectedBackend);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void Stats_SummarizeTheRecentFrames()
        {
            var stats = new ViewportPerformanceStats();
            Assert.Equal(0, stats.Summarize().Frames);

            for (int i = 0; i < 99; i++) stats.RecordFrame(10f);
            stats.RecordFrame(50f); // one hitch
            var summary = stats.Summarize();
            Assert.Equal(100, summary.Frames);
            Assert.Equal(10.4f, summary.AverageMs, 3);
            Assert.Equal(10f, summary.MinMs);
            Assert.Equal(50f, summary.MaxMs);
            Assert.Equal(100f, summary.OnePercentLowFps, 3); // the 99th percentile of 100 frames is the 99th slowest: 10 ms
            Assert.Equal(1000f / 10.4f, summary.Fps, 2);

            stats.RecordFrame(0f);
            stats.RecordFrame(float.NaN);
            Assert.Equal(100, stats.Count); // ignored
        }

        [Fact]
        public void Stats_RingKeepsTheNewestFrames()
        {
            var stats = new ViewportPerformanceStats();
            for (int i = 0; i < ViewportPerformanceStats.Capacity + 10; i++) stats.RecordFrame(i < ViewportPerformanceStats.Capacity ? 20f : 5f);
            Assert.Equal(ViewportPerformanceStats.Capacity, stats.Count);
            Assert.Equal(10, stats.GraphOffset);
            Assert.Equal(5f, stats.Intervals[9]);
            Assert.Equal(20f, stats.Intervals[stats.GraphOffset]); // the oldest still kept
            Assert.Equal(5f, stats.Summarize().MinMs);

            stats.ResetFrames();
            Assert.Equal(0, stats.Count);
            Assert.Equal(0, stats.GraphOffset);
        }

        [Fact]
        public void Stats_SmoothPassTimes()
        {
            var stats = new ViewportPerformanceStats();
            stats.RecordCpuPass(ViewportPass.Scene, 4f);
            Assert.Equal(4f, stats.CpuPassMs[(int)ViewportPass.Scene]); // the first sample is taken as is
            stats.RecordCpuPass(ViewportPass.Scene, 14f);
            Assert.Equal(5f, stats.CpuPassMs[(int)ViewportPass.Scene], 3);

            Assert.False(stats.GpuTimesValid);
            stats.RecordGpuPasses(new[] { 2f, 0.5f, 0.1f });
            Assert.True(stats.GpuTimesValid);
            Assert.Equal(0.5f, stats.GpuPassMs[(int)ViewportPass.StockUi]);
            stats.ClearGpuPasses();
            Assert.False(stats.GpuTimesValid);
        }

        [Fact]
        public void Stats_SampleMemoryAtMostTwiceASecond()
        {
            var stats = new ViewportPerformanceStats();
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            stats.SampleMemory(t0);
            Assert.True(stats.HeapBytes > 0);
            long heap = stats.HeapBytes;
            _ = new byte[1 << 20];
            stats.SampleMemory(t0 + 1); // too soon: unchanged
            Assert.Equal(heap, stats.HeapBytes);
            stats.SampleMemory(t0 + System.Diagnostics.Stopwatch.Frequency);
            Assert.True(stats.AllocatedBytesPerSecond > 0);
        }
    }
}
