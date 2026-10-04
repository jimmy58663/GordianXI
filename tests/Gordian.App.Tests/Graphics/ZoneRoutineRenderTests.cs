// tests/Gordian.App.Tests/Graphics/ZoneRoutineRenderTests.cs
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Gordian.App.Graphics;
using Gordian.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// On-demand zone routines rendered offscreen (#225): Alzadaal Undersea Ruins' Runic Portal meshes are absent until a
    /// map scheduler plays the idle routine, and gone after the end routine. Skipped without the retail install.
    /// </summary>
    public class ZoneRoutineRenderTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private readonly ITestOutputHelper _out;
        public ZoneRoutineRenderTests(ITestOutputHelper o) => _out = o;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr CreateWindowExW(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int X, int Y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        static extern bool DestroyWindow(IntPtr hWnd);

        [Fact]
        public void AlzadaalPortal_IsDrawnOnlyBetweenItsIdleAndEndRoutines()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(72, out var zone, out var textures)) return;
            // The glow 1pa1 lights (the zone has a second portal, 1pbb / 1pb1).
            Assert.True(zone.MapRoutines.TryResolveGenerator("d_at/effe/pba1/1pba", "g0b1", out var glow));
            var raw = glow.RawBasePosition;
            var target = new Vector3(-raw.X, -raw.Y, raw.Z);

            IntPtr hwnd = CreateWindowExW(0, "static", "Test", unchecked((int)0x80000000), 0, 0, 640, 480, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devMgr = new VeldridDeviceManager();
            devMgr.Initialize(Veldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), 640, 480, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devMgr.Device!;
            try
            {
                var world = new WorldState { CurrentZoneId = 72 };
                var renderer = new ZoneTerrainRenderer(gd) { World = world };
                renderer.LoadZone(zone, textures);
                var camera = new Gordian.Core.Graphics.ViewportCamera { FarClip = 5000f };
                camera.Update(target, 35.0f, 0.0f, 10.0f, 640f / 480f);
                var env = Gordian.Core.Graphics.ZoneEnvironmentSettings.CreateNight();
                env.WeatherId = "fine";
                renderer.SkyDomeRenderer?.UpdateDome(env);
                var colorTarget = gd.SwapchainFramebuffer.ColorTargets[0].Target;
                var rtColor = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(640, 480, 1, 1, colorTarget.Format, Veldrid.TextureUsage.RenderTarget | Veldrid.TextureUsage.Sampled));
                var rtDepth = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(640, 480, 1, 1, Veldrid.PixelFormat.R32_Float, Veldrid.TextureUsage.DepthStencil));
                var fb = gd.ResourceFactory.CreateFramebuffer(new Veldrid.FramebufferDescription(rtDepth, rtColor));
                var staging = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(640, 480, 1, 1, colorTarget.Format, Veldrid.TextureUsage.Staging));
                var cl = gd.ResourceFactory.CreateCommandList();
                void Run(int frames) { for (int i = 0; i < frames; i++) renderer.Render(camera, env, 1 / 60f, 640, 480, entities: Array.Empty<WorldEntity>(), resourceManager: rm, present: false, targetFramebuffer: fb); }
                double Brightness()
                {
                    cl.Begin(); cl.CopyTexture(rtColor, staging); cl.End(); gd.SubmitCommands(cl); gd.WaitForIdle();
                    var map = gd.Map(staging, Veldrid.MapMode.Read);
                    double sum = 0;
                    var row = new byte[640 * 4];
                    for (int y = 120; y < 360; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(map.Data + (int)(y * map.RowPitch), row, 0, row.Length);
                        for (int x = 160; x < 480; x++) sum += row[x * 4] + row[x * 4 + 1] + row[x * 4 + 2];
                    }
                    gd.Unmap(staging);
                    return sum / (320.0 * 240.0 * 3.0);
                }

                Run(30);
                (double Brightness, int Draws) before = (Brightness(), renderer.DrawCalls);
                world.PostMapScheduler("1pa1", 0, 0);
                Run(30);
                (double Brightness, int Draws) idle = (Brightness(), renderer.DrawCalls);
                world.PostMapScheduler("1pak", 0, 0);
                Run(30);
                (double Brightness, int Draws) after = (Brightness(), renderer.DrawCalls);
                _out.WriteLine($"portal: before {before}, idle {idle}, after 1pak {after}");
                // The idle glows g0b1 / g0c1 (additive, never expiring) brighten the frame only between 1pa1 and 1pak
                // (measured: 107.6 before, 116.4 lit, 107.6 after; 194 / 196 / 197 draws, the count drifting with the
                // zone's own auto-running sparkles).
                Assert.True(idle.Brightness > before.Brightness + 2.0, $"1pa1 should light the portal: before {before.Brightness:F2}, idle {idle.Brightness:F2}");
                Assert.True(idle.Draws >= before.Draws + 2, $"1pa1 should draw the portal glows: {before.Draws} draws before, {idle.Draws} after");
                Assert.InRange(after.Brightness, before.Brightness - 1.0, before.Brightness + 1.0);
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
