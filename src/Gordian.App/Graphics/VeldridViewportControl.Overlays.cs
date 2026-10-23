// src/Gordian.App/Graphics/VeldridViewportControl.Overlays.cs
using System;
using System.Diagnostics;
using Gordian.Core.Diagnostics;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Tier 3 of the viewport frame (#30): the ImGui overlay layer and the performance overlay's measurements. While the
    /// overlay is hidden (the default) none of this runs beyond reading the mode once a frame: no ImGui frame, no GPU
    /// queries, no stats recorded.
    /// </summary>
    public partial class VeldridViewportControl
    {
        private readonly ViewportPerformanceStats _perfStats = new();
        private ImGuiOverlayLayer? _overlayLayer;
        private bool _overlayLayerFailed;

        /// <summary>
        /// Input for the interactive ImGui overlays (#380) and ImGui's answer: whether it wants the mouse or keyboard. The
        /// hosting window feeds it every event while an overlay is visible and keeps an event from the game when ImGui
        /// wants it.
        /// </summary>
        public ImGuiInputBridge ImGuiInput { get; } = new();
        private GpuPassTimer? _gpuTimer;
        private PerformanceOverlayMode _frameOverlayMode;
        private long _perfFrameStart;
        private long _perfLastFrameStart;
        private long _perfPassStart;
        private float _perfFrameSeconds;
        private readonly float[] _gpuPassScratch = new float[GpuPassTimer.PassCount];

        /// <summary>
        /// Starts a drawn frame's measurements: the overlay mode for this frame and, while the overlay is shown, the interval
        /// since the previous drawn frame (the pacing graph).
        /// </summary>
        private void RecordPerformanceFrame(long frameStart)
        {
            var before = _frameOverlayMode;
            _frameOverlayMode = IsThumbnail ? PerformanceOverlayMode.Hidden : ViewportRenderSettings.PerformanceOverlay;
            // Shown again: start the graph afresh rather than from frames recorded before it was hidden.
            if (before == PerformanceOverlayMode.Hidden && _frameOverlayMode != PerformanceOverlayMode.Hidden) _perfStats.ResetFrames();
            long previous = _perfLastFrameStart;
            _perfLastFrameStart = frameStart;
            _perfFrameStart = frameStart;
            if (_frameOverlayMode == PerformanceOverlayMode.Hidden || previous == 0) return;
            _perfFrameSeconds = (float)((frameStart - previous) / (double)Stopwatch.Frequency);
            _perfStats.RecordFrame(_perfFrameSeconds * 1000f);
        }

        /// <summary>Before the first pass (under the GPU lock): starts the GPU timer while the detailed overlay is shown.</summary>
        private void BeginPerformancePasses()
        {
            if (_frameOverlayMode == PerformanceOverlayMode.Detailed && _deviceManager.Device is { } gd)
            {
                if (_gpuTimer == null)
                {
                    _gpuTimer = GpuPassTimer.Create(gd);
                    _perfStats.GpuTimerDescription = _gpuTimer.Description;
                }
                _gpuTimer.BeginFrame();
            }
            else if (_gpuTimer != null)
            {
                // Closing the detailed overlay stops the queries.
                _gpuTimer.Dispose();
                _gpuTimer = null;
                _perfStats.ClearGpuPasses();
            }
            _perfPassStart = _frameOverlayMode == PerformanceOverlayMode.Hidden ? 0 : Stopwatch.GetTimestamp();
        }

        /// <summary>After a pass is submitted: its CPU time and GPU end mark.</summary>
        private void EndPerformancePass(ViewportPass pass)
        {
            if (_perfPassStart == 0) return;
            long now = Stopwatch.GetTimestamp();
            _perfStats.RecordCpuPass(pass, (float)((now - _perfPassStart) * 1000.0 / Stopwatch.Frequency));
            _perfPassStart = now;
            _gpuTimer?.EndPass(pass);
        }

        /// <summary>After the last pass, before present: closes the GPU frame and gathers the counters for the next overlay.</summary>
        private void EndPerformanceFrame()
        {
            if (_frameOverlayMode == PerformanceOverlayMode.Hidden) return;
            long now = Stopwatch.GetTimestamp();
            if (_gpuTimer != null)
            {
                _gpuTimer.EndFrame();
                if (_gpuTimer.TryReadLatest(_gpuPassScratch)) _perfStats.RecordGpuPasses(_gpuPassScratch);
            }
            _perfStats.RecordCpuFrame((float)((now - _perfFrameStart) * 1000.0 / Stopwatch.Frequency));
            if (_frameOverlayMode == PerformanceOverlayMode.Detailed) _perfStats.SampleMemory(now);
        }

        /// <summary>
        /// Tier 3: the ImGui overlays (the performance overlay and the registered interactive ones), drawn over the stock
        /// UI. Nothing runs while every overlay is hidden, and a hidden overlay captures no input.
        /// </summary>
        private void RenderTier3_ImGuiOverlays()
        {
            bool interactive = ImGuiOverlayRegistry.AnyVisible;
            if (!interactive) ImGuiInput.Reset();
            if ((_frameOverlayMode == PerformanceOverlayMode.Hidden && !interactive) || _overlayLayerFailed) return;
            var gd = _deviceManager.Device;
            var framebuffer = _deviceManager.Framebuffer;
            if (gd == null || framebuffer == null) return;
            try
            {
                _overlayLayer ??= new ImGuiOverlayLayer(gd, framebuffer.OutputDescription);
            }
            catch (Exception ex)
            {
                // A missing native cimgui or a shader the backend cannot take: stop trying for this window.
                _overlayLayerFailed = true;
                GordianLog.Warning("Graphics", $"ImGui overlay layer unavailable: {ex.Message}");
                return;
            }

            _perfStats.SceneDrawCalls = _renderer?.DrawCalls ?? 0;
            _perfStats.VisibleMeshes = _renderer?.VisibleMeshes ?? 0;
            _perfStats.CulledMeshes = _renderer?.CulledMeshes ?? 0;
            _perfStats.StockUiDrawCalls = _stockUiRenderer?.LastDrawCalls ?? 0;
            _perfStats.StockUiQuads = _stockUiRenderer?.LastQuadCount ?? 0;
            _perfStats.OverlayDrawCalls = _overlayLayer.LastDrawCalls;

            var info = new PerformanceOverlayInfo(_frameOverlayMode, _perfStats, _activeBackendName, _gpuDeviceName,
                framebuffer.Width, framebuffer.Height, _swapchainVsync);
            _overlayLayer.Render(framebuffer, _perfFrameSeconds, info, DrawTier3, interactive ? ImGuiInput : null);
        }

        private static void DrawTier3(PerformanceOverlayInfo info)
        {
            if (info.Mode != PerformanceOverlayMode.Hidden) PerformanceOverlay.Draw(info);
            ImGuiOverlayRegistry.DrawAll();
        }

        /// <summary>Releases the overlay layer and the GPU timer (call under the GPU lock, before the device goes).</summary>
        private void DisposeOverlays()
        {
            _overlayLayer?.Dispose();
            _overlayLayer = null;
            _gpuTimer?.Dispose();
            _gpuTimer = null;
            _perfStats.ClearGpuPasses();
        }
    }
}
