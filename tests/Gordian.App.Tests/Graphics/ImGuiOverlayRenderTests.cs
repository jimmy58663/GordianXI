// tests/Gordian.App.Tests/Graphics/ImGuiOverlayRenderTests.cs
using System;
using System.IO;
using System.Runtime.InteropServices;
using Gordian.App.Graphics;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// The Tier 3 ImGui layer drawn offscreen on D3D11 (#30): the performance overlay in the top-right corner over a
    /// green clear, and the D3D11 GPU pass timer. Skipped off Windows or without a device; set GORDIAN_UI_DUMP to a
    /// folder to save the frames as PNG.
    /// </summary>
    [Collection("ImGuiOverlayRegistry")]
    public class ImGuiOverlayRenderTests
    {
        internal const uint Width = 800, Height = 600;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int x, int y,
            int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hwnd);

        internal sealed class Target : IDisposable
        {
            private readonly IntPtr _hwnd;
            private readonly VeldridDeviceManager _devices;
            public readonly NeoVeldrid.GraphicsDevice Device;
            public readonly NeoVeldrid.Texture Color;
            private readonly NeoVeldrid.Texture _depth;
            public readonly NeoVeldrid.Framebuffer Framebuffer;
            private readonly NeoVeldrid.CommandList _cl;

            private Target(IntPtr hwnd, VeldridDeviceManager devices, NeoVeldrid.GraphicsDevice gd)
            {
                _hwnd = hwnd;
                _devices = devices;
                Device = gd;
                var format = gd.SwapchainFramebuffer.ColorTargets[0].Target.Format;
                Color = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(Width, Height, 1, 1, format,
                    NeoVeldrid.TextureUsage.RenderTarget | NeoVeldrid.TextureUsage.Sampled));
                _depth = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(Width, Height, 1, 1,
                    NeoVeldrid.PixelFormat.R32_Float, NeoVeldrid.TextureUsage.DepthStencil));
                Framebuffer = gd.ResourceFactory.CreateFramebuffer(new NeoVeldrid.FramebufferDescription(_depth, Color));
                _cl = gd.ResourceFactory.CreateCommandList();
            }

            public static Target? TryCreate()
            {
                if (!OperatingSystem.IsWindows()) return null;
                IntPtr hwnd = CreateWindowExW(0, "static", "ImGuiOverlayTest", unchecked((int)0x80000000), 0, 0, (int)Width, (int)Height,
                    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                var devices = new VeldridDeviceManager();
                devices.Initialize(NeoVeldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), Width, Height, GraphicsBackendPreference.Direct3D11, vsync: false);
                if (devices.Device is not { } gd)
                {
                    devices.Dispose();
                    DestroyWindow(hwnd);
                    return null;
                }
                return new Target(hwnd, devices, gd);
            }

            public void Clear()
            {
                _cl.Begin();
                _cl.SetFramebuffer(Framebuffer);
                _cl.ClearColorTarget(0, new NeoVeldrid.RgbaFloat(0.16f, 0.24f, 0.16f, 1.0f));
                _cl.ClearDepthStencil(1f);
                _cl.End();
                Device.SubmitCommands(_cl);
            }

            public void Dispose()
            {
                _cl.Dispose();
                Framebuffer.Dispose();
                _depth.Dispose();
                Color.Dispose();
                _devices.Dispose();
                DestroyWindow(_hwnd);
            }
        }

        private static PerformanceOverlayInfo Info(PerformanceOverlayMode mode, ViewportPerformanceStats stats) =>
            new(mode, stats, "Direct3D11", "Test GPU", Width, Height, Vsync: true);

        private static ViewportPerformanceStats SampleStats()
        {
            var stats = new ViewportPerformanceStats { SceneDrawCalls = 1234, VisibleMeshes = 800, CulledMeshes = 300, StockUiDrawCalls = 40, StockUiQuads = 512 };
            for (int i = 0; i < ViewportPerformanceStats.Capacity; i++) stats.RecordFrame(i % 30 == 0 ? 33f : 16.6f);
            stats.RecordCpuPass(ViewportPass.Scene, 3.2f);
            stats.RecordCpuPass(ViewportPass.StockUi, 0.4f);
            stats.RecordCpuPass(ViewportPass.Overlay, 0.2f);
            stats.RecordCpuFrame(4.1f);
            stats.SampleMemory(System.Diagnostics.Stopwatch.GetTimestamp());
            return stats;
        }

        private static bool AnyLight(byte[] rgba, int x0, int y0, int x1, int y1)
        {
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    int o = (int)(y * Width + x) * 4;
                    if (rgba[o] > 200 && rgba[o + 1] > 200 && rgba[o + 2] > 200) return true;
                }
            }
            return false;
        }

        private static void Dump(string name, byte[] rgba)
        {
            string? dir = Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            StockUiRendererTests.SavePng(Path.Combine(dir, name), rgba, (int)Width, (int)Height);
        }

        [Fact]
        public void PerformanceOverlay_DrawsInTheTopRightCorner()
        {
            using var target = Target.TryCreate();
            if (target == null) return;
            using var layer = new ImGuiOverlayLayer(target.Device, target.Framebuffer.OutputDescription);
            var stats = SampleStats();

            // The first frame builds and uploads the font atlas (the 1.92 texture requests); draw a few like the viewport does.
            byte[] fps = Array.Empty<byte>();
            for (int frame = 0; frame < 3; frame++)
            {
                target.Clear();
                layer.Render(target.Framebuffer, 1 / 60f, Info(PerformanceOverlayMode.Fps, stats), PerformanceOverlay.Draw);
            }
            fps = StockUiRendererTests.ReadBack(target.Device, target.Color, Width, Height);
            Dump("perf_overlay_fps.png", fps);
            Assert.True(layer.LastDrawCalls > 0);
            Assert.True(AnyLight(fps, (int)Width - 200, 0, (int)Width, 40), "the FPS line is drawn top right");
            Assert.False(AnyLight(fps, 0, 0, (int)Width - 220, (int)Height), "nothing elsewhere");

            for (int frame = 0; frame < 3; frame++)
            {
                target.Clear();
                layer.Render(target.Framebuffer, 1 / 60f, Info(PerformanceOverlayMode.Detailed, stats), PerformanceOverlay.Draw);
            }
            var detailed = StockUiRendererTests.ReadBack(target.Device, target.Color, Width, Height);
            Dump("perf_overlay_detailed.png", detailed);
            Assert.True(AnyLight(detailed, (int)Width - 340, 150, (int)Width, 300), "the detailed panel reaches down the right side");
            Assert.False(AnyLight(detailed, 0, 0, 300, (int)Height));

            target.Clear();
            layer.Render(target.Framebuffer, 1 / 60f, Info(PerformanceOverlayMode.Hidden, stats), PerformanceOverlay.Draw);
            var hidden = StockUiRendererTests.ReadBack(target.Device, target.Color, Width, Height);
            Assert.False(AnyLight(hidden, 0, 0, (int)Width, (int)Height), "hidden draws nothing");
        }

        [Fact]
        public void D3D11PassTimer_ReportsPassTimes()
        {
            using var target = Target.TryCreate();
            if (target == null) return;
            using var timer = GpuPassTimer.Create(target.Device);
            Assert.True(timer.IsSupported, timer.Description);

            Span<float> passes = stackalloc float[GpuPassTimer.PassCount];
            Assert.False(timer.TryReadLatest(passes));
            for (int frame = 0; frame < 4; frame++)
            {
                timer.BeginFrame();
                target.Clear();
                timer.EndPass(ViewportPass.Scene);
                target.Clear();
                timer.EndPass(ViewportPass.StockUi);
                timer.EndPass(ViewportPass.Overlay);
                timer.EndFrame();
                // The viewport presents after each frame, which flushes the queries to the GPU (they are read without flushing).
                target.Device.SwapBuffers();
                target.Device.WaitForIdle();
            }
            // Collect the finished frames. The timer reads results without waiting; a software adapter (WARP, as on
            // the CI runners) can take more frames to report them, or report every frame as disjoint, so poll a while.
            bool read = false;
            for (int frame = 0; frame < 100 && !read; frame++)
            {
                timer.BeginFrame();
                timer.EndFrame();
                target.Device.SwapBuffers();
                target.Device.WaitForIdle();
                read = timer.TryReadLatest(passes);
            }
            if (!read && target.Device.DeviceName.Contains("Basic Render", StringComparison.OrdinalIgnoreCase)) return;
            Assert.True(read, $"no pass times from '{target.Device.DeviceName}'");
            foreach (float ms in passes) Assert.InRange(ms, 0f, 1000f);
        }
    }
}
