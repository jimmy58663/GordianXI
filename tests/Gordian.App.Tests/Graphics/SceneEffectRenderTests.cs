// tests/Gordian.App.Tests/Graphics/SceneEffectRenderTests.cs
using System;
using System.IO;
using System.Numerics;
using Gordian.App.Graphics;
using Gordian.Core.Events;
using Gordian.Core.Resources.Events;
using Xunit;
using Xunit.Abstractions;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// Cutscene scene effects drawn offscreen (#192): the camera-space sparkles of Port Jeuno 324 (file 70443 <c>s002</c>)
    /// must rise, and its blink (51402) must open as an eye (#204). Skipped off Windows or without the game install.
    /// </summary>
    public class SceneEffectRenderTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private readonly ITestOutputHelper _out;
        public SceneEffectRenderTests(ITestOutputHelper o) => _out = o;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr CreateWindowExW(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int X, int Y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        static extern bool DestroyWindow(IntPtr hWnd);

        /// <summary>
        /// Right after the start the sparkles sit near their spawn point; as they age they spread along their path, so the
        /// bright pixels' centroid moves up the screen. With the display flip applied twice they fell instead (in-game
        /// test, 2026-10-02).
        /// </summary>
        [Fact]
        public void CameraSpaceSparkles_RiseOnScreen()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            IntPtr hwnd = CreateWindowExW(0, "static", "Test", unchecked((int)0x80000000), 0, 0, 640, 480, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devMgr = new VeldridDeviceManager();
            devMgr.Initialize(Veldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), 640, 480, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devMgr.Device!;
            try
            {
                var renderer = new ZoneTerrainRenderer(gd); // no zone: a black background
                var camera = new Gordian.Core.Graphics.ViewportCamera { FarClip = 5000f };
                var env = Gordian.Core.Graphics.ZoneEnvironmentSettings.CreateNight();
                var colorTarget = gd.SwapchainFramebuffer.ColorTargets[0].Target;
                var rtColor = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(640, 480, 1, 1, colorTarget.Format, Veldrid.TextureUsage.RenderTarget | Veldrid.TextureUsage.Sampled));
                var rtDepth = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(640, 480, 1, 1, Veldrid.PixelFormat.R32_Float, Veldrid.TextureUsage.DepthStencil));
                var fb = gd.ResourceFactory.CreateFramebuffer(new Veldrid.FramebufferDescription(rtDepth, rtColor));
                var staging = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(640, 480, 1, 1, colorTarget.Format, Veldrid.TextureUsage.Staging));
                var cl = gd.ResourceFactory.CreateCommandList();
                double now = 0;
                var presentation = new EventPresentation { Clock = () => now };
                renderer.EventPresentation = presentation;
                camera.SetEventView(new Vector3(0, 9.6f, 0), new Vector3(0, 9.6f, 10), 1.0f, 0f, 640f / 480f);
                void Run(int frames) { for (int i = 0; i < frames; i++) { now += 1 / 60.0; renderer.Render(camera, env, 1 / 60f, 640, 480, entities: Array.Empty<Gordian.Core.World.WorldEntity>(), resourceManager: rm, present: false, targetFramebuffer: fb); } }
                double CentroidY()
                {
                    cl.Begin(); cl.CopyTexture(rtColor, staging); cl.End(); gd.SubmitCommands(cl); gd.WaitForIdle();
                    var map = gd.Map(staging, Veldrid.MapMode.Read);
                    double sum = 0, weight = 0;
                    var row = new byte[640 * 4];
                    for (int y = 0; y < 480; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(map.Data + (int)(y * map.RowPitch), row, 0, row.Length);
                        for (int x = 0; x < 640; x++)
                        {
                            int v = row[x * 4] + row[x * 4 + 1] + row[x * 4 + 2];
                            if (v > 120) { sum += y * v; weight += v; }
                        }
                    }
                    gd.Unmap(staging);
                    return weight > 0 ? sum / weight : double.NaN;
                }
                Run(2);
                var sparkles = EventSceneResource.Parse(rm.LoadDatBytesByFileId(70443)!);
                presentation.Play(6, sparkles, sparkles.Routines["s002"], new Vector3(0, -8, 3), 70443, 0x12345, 0x12345, 0f);
                Run(8);
                double early = CentroidY();
                Run(100);
                double late = CentroidY();
                _out.WriteLine($"centroid y early {early:F1} late {late:F1}");
                Assert.True(late < early, $"sparkles should rise: early {early:F1}, late {late:F1}");
                renderer.Dispose();
            }
            finally
            {
                devMgr.Dispose();
                DestroyWindow(hwnd);
            }
        }

        /// <summary>
        /// Port Jeuno 324's blink (file 51402, #204): after <c>open</c> the scene shows through an eye-shaped window, the
        /// weighted mesh <c>mb</c> blended open: lit across most of the width at the middle, much less tall, with the
        /// corners still black. Without the mask the whole screen came back.
        /// </summary>
        [Fact]
        public void BlinkOpen_ShowsTheSceneThroughAnEyeShapedWindow()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(4, out var zone, out var textures)) return;
            IntPtr hwnd = CreateWindowExW(0, "static", "Test", unchecked((int)0x80000000), 0, 0, 640, 480, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devMgr = new VeldridDeviceManager();
            devMgr.Initialize(Veldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), 640, 480, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devMgr.Device!;
            try
            {
                var renderer = new ZoneTerrainRenderer(gd);
                renderer.LoadZone(zone, textures); // Bibiki Bay's sea and sky: a bright background
                var camera = new Gordian.Core.Graphics.ViewportCamera { FarClip = 5000f };
                camera.Update(new Vector3(0, 10, 0), 15.0f, 180.0f, 6.0f, 640f / 480f);
                var env = Gordian.Core.Graphics.ZoneEnvironmentSettings.CreateDay();
                env.WeatherId = "fine";
                renderer.SkyDomeRenderer?.UpdateDome(env);
                var colorTarget = gd.SwapchainFramebuffer.ColorTargets[0].Target;
                var rtColor = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(640, 480, 1, 1, colorTarget.Format, Veldrid.TextureUsage.RenderTarget | Veldrid.TextureUsage.Sampled));
                var rtDepth = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(640, 480, 1, 1, Veldrid.PixelFormat.R32_Float, Veldrid.TextureUsage.DepthStencil));
                var fb = gd.ResourceFactory.CreateFramebuffer(new Veldrid.FramebufferDescription(rtDepth, rtColor));
                var staging = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(640, 480, 1, 1, colorTarget.Format, Veldrid.TextureUsage.Staging));
                var cl = gd.ResourceFactory.CreateCommandList();
                double now = 0;
                var presentation = new EventPresentation { Clock = () => now };
                renderer.EventPresentation = presentation;
                void Run(int frames) { for (int i = 0; i < frames; i++) { now += 1 / 60.0; renderer.Render(camera, env, 1 / 60f, 640, 480, entities: Array.Empty<Gordian.Core.World.WorldEntity>(), resourceManager: rm, present: false, targetFramebuffer: fb); } }
                bool[,] Lit()
                {
                    cl.Begin(); cl.CopyTexture(rtColor, staging); cl.End(); gd.SubmitCommands(cl); gd.WaitForIdle();
                    var map = gd.Map(staging, Veldrid.MapMode.Read);
                    var lit = new bool[640, 480];
                    var row = new byte[640 * 4];
                    for (int y = 0; y < 480; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(map.Data + (int)(y * map.RowPitch), row, 0, row.Length);
                        for (int x = 0; x < 640; x++) lit[x, y] = row[x * 4] + row[x * 4 + 1] + row[x * 4 + 2] > 60;
                    }
                    gd.Unmap(staging);
                    return lit;
                }
                var blink = EventSceneResource.Parse(rm.LoadDatBytesByFileId(51402)!);
                Run(2);
                presentation.Play(1, blink, blink.Routines["bl00"], new Vector3(0, -10, 0), 51402, 0x12345, 0x12345, 0f);
                presentation.Play(2, blink, blink.Routines["open"], new Vector3(0, -10, 0), 51402, 0x12345, 0x12345, 0f);
                Run(220); // past the black card's fade (150 frames); the mask holds open at weights 0.7 / 0.24
                var lit = Lit();

                int litRow = 0, litColumn = 0;
                for (int x = 0; x < 640; x++) if (lit[x, 270]) litRow++;
                for (int y = 0; y < 480; y++) if (lit[320, y]) litColumn++;
                _out.WriteLine($"eye: {litRow} px wide at its middle row, {litColumn} px tall at the centre column");
                Assert.True(litRow > 400, $"the eye should span most of the width, got {litRow}");
                Assert.InRange(litColumn, 60, 360);
                Assert.True(litRow > 2 * litColumn);
                foreach (var (x, y) in new[] { (5, 5), (634, 5), (5, 474), (634, 474), (40, 160), (600, 380) })
                {
                    Assert.False(lit[x, y], $"({x}, {y}) lies outside the eye and should stay black");
                }
                renderer.Dispose();
            }
            finally
            {
                devMgr.Dispose();
                DestroyWindow(hwnd);
            }
        }
    }
}
