// src/Gordian.App/Graphics/PerformanceOverlay.cs
using System;
using System.Globalization;
using System.Numerics;
using System.Text.Unicode;
using Hexa.NET.ImGui;

namespace Gordian.App.Graphics
{
    /// <summary>What the overlay says about the device and view, gathered by the viewport each frame.</summary>
    public readonly record struct PerformanceOverlayInfo(PerformanceOverlayMode Mode, ViewportPerformanceStats Stats,
        string Backend, string Device, uint Width, uint Height, bool Vsync);

    /// <summary>
    /// The viewport performance overlay (#30), drawn with ImGui in the top-right corner: in <see cref="PerformanceOverlayMode.Fps"/>
    /// one line (frame rate and frame time), in <see cref="PerformanceOverlayMode.Detailed"/> the frame pacing graph, the
    /// 1% low, draw call and mesh counters, CPU and GPU time per pass, managed memory and the device. Text is formatted
    /// into stack buffers (UTF-8), so a shown overlay allocates nothing per frame either. The windows take no input.
    /// </summary>
    public static unsafe class PerformanceOverlay
    {
        private const ImGuiWindowFlags OverlayFlags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs
            | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoMove;

        /// <summary>Distance from the screen's top-right corner, in pixels.</summary>
        private const float Margin = 12f;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static readonly Vector4 Good = new(0.45f, 0.90f, 0.55f, 1f);
        private static readonly Vector4 Warning = new(0.95f, 0.80f, 0.35f, 1f);
        private static readonly Vector4 Bad = new(0.95f, 0.40f, 0.35f, 1f);
        private static readonly Vector4 Dim = new(0.65f, 0.70f, 0.78f, 1f);

        /// <summary>Draws the overlay for the mode in <paramref name="info"/> (nothing when hidden).</summary>
        public static void Draw(PerformanceOverlayInfo info)
        {
            if (info.Mode == PerformanceOverlayMode.Hidden) return;
            var stats = info.Stats;
            var summary = stats.Summarize();
            ImGui.SetNextWindowPos(new Vector2(info.Width - Margin, Margin), ImGuiCond.Always, new Vector2(1f, 0f));
            if (ImGui.Begin("##gordian_perf"u8, OverlayFlags))
            {
                Span<byte> buffer = stackalloc byte[256];
                ImGui.PushStyleColor(ImGuiCol.Text, FpsColor(summary.Fps));
                Line(buffer, Inv, $"{summary.Fps,5:0.0} FPS");
                ImGui.PopStyleColor();
                ImGui.SameLine();
                Line(buffer, Inv, $"{summary.AverageMs:0.00} ms");
                if (info.Mode == PerformanceOverlayMode.Detailed) DrawDetails(info, summary, buffer);
            }
            ImGui.End();
        }

        private static void DrawDetails(PerformanceOverlayInfo info, FrameTimeSummary summary, Span<byte> buffer)
        {
            var stats = info.Stats;
            ImGui.PushStyleColor(ImGuiCol.Text, Dim);
            Line(buffer, Inv, $"frame {summary.MinMs:0.0}-{summary.MaxMs:0.0} ms   1% low {summary.OnePercentLowFps:0.0} FPS");
            ImGui.PopStyleColor();

            // Frame pacing: each frame's interval, oldest on the left; the scale keeps at least a 30 FPS frame in view.
            float top = Math.Max(1000f / 30f, summary.MaxMs * 1.1f);
            fixed (float* values = stats.Intervals)
            {
                ImGui.PlotLines("##pacing"u8, values, stats.Count, stats.GraphOffset, "frame pacing (ms)"u8, 0f, top, new Vector2(340, 64));
            }

            ImGui.Separator();
            Line(buffer, Inv, $"Draw calls  scene {stats.SceneDrawCalls}   UI {stats.StockUiDrawCalls}   overlay {stats.OverlayDrawCalls}");
            Line(buffer, Inv, $"Meshes      {stats.VisibleMeshes} drawn, {stats.CulledMeshes} culled   UI quads {stats.StockUiQuads}");

            ImGui.Separator();
            if (ImGui.BeginTable("##passes"u8, 3, ImGuiTableFlags.SizingFixedFit))
            {
                ImGui.TableSetupColumn("Pass"u8, ImGuiTableColumnFlags.WidthFixed, 110f);
                ImGui.TableSetupColumn("CPU ms"u8, ImGuiTableColumnFlags.WidthFixed, 70f);
                ImGui.TableSetupColumn("GPU ms"u8, ImGuiTableColumnFlags.WidthFixed, 70f);
                ImGui.TableHeadersRow();
                PassRow(buffer, "Scene"u8, stats, ViewportPass.Scene);
                PassRow(buffer, "Stock UI"u8, stats, ViewportPass.StockUi);
                PassRow(buffer, "Overlay"u8, stats, ViewportPass.Overlay);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                Line(buffer, Inv, $"Frame (CPU)");
                ImGui.TableNextColumn();
                Line(buffer, Inv, $"{stats.LastCpuFrameMs:0.00}");
                ImGui.TableNextColumn();
                if (stats.GpuTimesValid) Line(buffer, Inv, $"{stats.GpuPassMs[0] + stats.GpuPassMs[1] + stats.GpuPassMs[2]:0.00}");
                else Line(buffer, Inv, $"n/a");
                ImGui.EndTable();
            }
            ImGui.PushStyleColor(ImGuiCol.Text, Dim);
            Line(buffer, Inv, $"GPU: {stats.GpuTimerDescription}");
            ImGui.PopStyleColor();

            ImGui.Separator();
            Line(buffer, Inv, $"Heap {stats.HeapBytes / (1024.0 * 1024.0):0} MB   alloc {stats.AllocatedBytesPerSecond / (1024.0 * 1024.0):0.00} MB/s");
            Line(buffer, Inv, $"GC   gen0 {stats.Gen0Collections}  gen1 {stats.Gen1Collections}  gen2 {stats.Gen2Collections}");
            ImGui.PushStyleColor(ImGuiCol.Text, Dim);
            Line(buffer, Inv, $"{info.Backend}  {info.Width}x{info.Height}  vsync {(info.Vsync ? "on" : "off")}");
            Line(buffer, Inv, $"{info.Device}");
            ImGui.PopStyleColor();
        }

        private static void PassRow(Span<byte> buffer, ReadOnlySpan<byte> name, ViewportPerformanceStats stats, ViewportPass pass)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(name);
            ImGui.TableNextColumn();
            Line(buffer, Inv, $"{stats.CpuPassMs[(int)pass]:0.00}");
            ImGui.TableNextColumn();
            if (stats.GpuTimesValid) Line(buffer, Inv, $"{stats.GpuPassMs[(int)pass]:0.00}");
            else Line(buffer, Inv, $"n/a");
        }

        /// <summary>Green from 55 FPS, amber from 30, red below (the 60 FPS and 30 FPS budgets).</summary>
        private static Vector4 FpsColor(float fps) => fps >= 55f ? Good : fps >= 30f ? Warning : Bad;

        /// <summary>Formats one line into <paramref name="buffer"/> as UTF-8 (invariant culture) and draws it.</summary>
        private static void Line(Span<byte> buffer, IFormatProvider provider,
            [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument(nameof(buffer), nameof(provider))] ref Utf8.TryWriteInterpolatedStringHandler text)
        {
            if (!Utf8.TryWrite(buffer, provider, ref text, out int written)) written = 0;
            fixed (byte* p = buffer)
            {
                ImGui.TextUnformatted(p, p + written);
            }
        }
    }
}
