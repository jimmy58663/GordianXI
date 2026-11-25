// tests/Gordian.App.Tests/Graphics/WindSwayRenderTests.cs
using System;
using System.IO;
using System.Numerics;
using Gordian.App.Graphics;
using Gordian.Core.World;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// Zone meshes with wind offsets sway (#606), rendered offscreen: the maintainer's three cases (Crawlers' Nest's webs,
    /// La Theine Plateau's grass tufts, the Valkurm Dunes trees outside Selbina) move between the wind factor's 0 and 1, and
    /// stand still with <see cref="ZoneTerrainRenderer.EnableWindSway"/> off; and West Ronfaure's brazier heat haze (#603),
    /// the other moving zone effect checked alongside it. Skipped without the retail install.
    /// </summary>
    public class WindSwayRenderTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const int Width = 320, Height = 240;
        private readonly ITestOutputHelper _out;
        public WindSwayRenderTests(ITestOutputHelper o) => _out = o;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr CreateWindowExW(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int X, int Y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        static extern bool DestroyWindow(IntPtr hWnd);

        /// <summary>
        /// West Ronfaure's brazier heat haze (#603): <c>f_ro/mode/ligh/s_li/dis2</c>, a Distortion generator just above the
        /// flame <c>fir2</c>, shimmers the wall behind it at night. Successive frames change more with the haze on than off
        /// (the flame itself flickers in both), and the haze pass draws its quads.
        /// </summary>
        [Fact]
        public void WestRonfaureTorchHaze_ShimmersTheWallAtNight()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(100, out var zone, out var textures)) return;
            var haze = Assert.Single(zone.EffectLayers, l => l.IsDistortion && l.Name == "dis2" && l.GeneratorPath.EndsWith("s_li"));
            Assert.Equal(0.02f, haze.HazeOffset, 4);

            IntPtr hwnd = CreateWindowExW(0, "static", "Test", unchecked((int)0x80000000), 0, 0, Width, Height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devMgr = new VeldridDeviceManager();
            devMgr.Initialize(NeoVeldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), Width, Height, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devMgr.Device!;
            try
            {
                var renderer = new ZoneTerrainRenderer(gd) { World = new WorldState { CurrentZoneId = 100 } };
                renderer.LoadZone(zone, textures);
                var camera = new Gordian.Engine.Graphics.ViewportCamera { FarClip = 5000f };
                // The brazier by the gate (server -174.7, -63.2, 251.3), seen from 5 yalms with the castle wall behind it.
                camera.Update(haze.Position + new Vector3(0f, -0.5f, 0f), 5f, 270f, 5f, (float)Width / Height);
                var env = Gordian.Engine.Graphics.ZoneEnvironmentSettings.CreateNight();
                env.SetTimeOfDay(22.0f);
                env.WeatherId = "fine";
                var colorTarget = gd.SwapchainFramebuffer.ColorTargets[0].Target;
                var rtColor = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(Width, Height, 1, 1, colorTarget.Format, NeoVeldrid.TextureUsage.RenderTarget | NeoVeldrid.TextureUsage.Sampled));
                var rtDepth = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(Width, Height, 1, 1, NeoVeldrid.PixelFormat.R32_Float, NeoVeldrid.TextureUsage.DepthStencil));
                var fb = gd.ResourceFactory.CreateFramebuffer(new NeoVeldrid.FramebufferDescription(rtDepth, rtColor));
                var staging = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(Width, Height, 1, 1, colorTarget.Format, NeoVeldrid.TextureUsage.Staging));
                var cl = gd.ResourceFactory.CreateCommandList();
                byte[] Frame()
                {
                    renderer.Render(camera, env, 1 / 60f, Width, Height, entities: Array.Empty<WorldEntity>(), resourceManager: rm, present: false, targetFramebuffer: fb);
                    cl.Begin(); cl.CopyTexture(rtColor, staging); cl.End(); gd.SubmitCommands(cl); gd.WaitForIdle();
                    var map = gd.Map(staging, NeoVeldrid.MapMode.Read);
                    var rgba = new byte[Width * Height * 4];
                    for (int row = 0; row < Height; row++) System.Runtime.InteropServices.Marshal.Copy(map.Data + (int)(row * map.RowPitch), rgba, row * Width * 4, Width * 4);
                    gd.Unmap(staging);
                    return rgba;
                }
                static int Changed(byte[] a, byte[] b)
                {
                    int changed = 0;
                    for (int i = 0; i < a.Length; i += 4)
                    {
                        if (Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]) > 9) changed++;
                    }
                    return changed;
                }
                // Changed pixels between frames four apart, summed over a few pairs.
                int Shimmer()
                {
                    int total = 0;
                    for (int pair = 0; pair < 6; pair++)
                    {
                        var a = Frame();
                        for (int i = 0; i < 3; i++) Frame();
                        total += Changed(a, Frame());
                    }
                    return total;
                }

                for (int i = 0; i < 60; i++) Frame();
                int withHaze = Shimmer();
                int hazeDraws = renderer.DistortionDraws;
                if (Environment.GetEnvironmentVariable("GORDIAN_WIND_DUMP") is { } dump) StockUiRendererTests.SavePng(Path.Combine(dump, "haze-100.png"), Frame(), Width, Height);
                renderer.EnableHeatHaze = false;
                for (int i = 0; i < 10; i++) Frame();
                int without = Shimmer();
                _out.WriteLine($"torch: {withHaze} changed pixels with the haze ({hazeDraws} haze draws), {without} without");

                // From across the field the warp is under half a pixel: no haze is drawn, so the frame stays on screen (#603 round 1).
                renderer.EnableHeatHaze = true;
                camera.Update(haze.Position + new Vector3(0f, -0.5f, 0f), 5f, 270f, 120f, (float)Width / Height);
                for (int i = 0; i < 10; i++) Frame();
                int farDraws = renderer.DistortionDraws;
                renderer.Dispose();
                Assert.Equal(0, farDraws);

                Assert.True(hazeDraws > 0, "the haze pass should draw the brazier's quads");

                // Measured 2026-11-22: 23,926 changed pixels with the haze, 21,930 without (the flame flickers in both).
                Assert.True(withHaze > without + 500, $"the haze should shimmer the scene: {withHaze} changed pixels with it, {without} without");
            }
            finally
            {
                devMgr.Dispose();
                DestroyWindow(hwnd);
            }
        }

        [Theory]
        // Server order (x, height, y), the maintainer's spots; camera yaw and pitch frame the swaying meshes.
        [InlineData(197, 331.837f, -32.823f, -12.645f, 90f, 10f, 6f)]  // Crawlers' Nest: the webs either side (_dad_w09_m)
        [InlineData(102, -558.723f, 0.0f, 676.550f, 0f, 30f, 6f)]       // La Theine Plateau: grass tufts (_kusa_00x_m)
        public void SwayingMeshes_MoveWithTheWindFactor(int zoneId, float x, float height, float y, float yaw, float pitch, float distance)
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(zoneId, out var zone, out var textures)) return;

            IntPtr hwnd = CreateWindowExW(0, "static", "Test", unchecked((int)0x80000000), 0, 0, Width, Height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devMgr = new VeldridDeviceManager();
            devMgr.Initialize(NeoVeldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), Width, Height, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devMgr.Device!;
            try
            {
                var renderer = new ZoneTerrainRenderer(gd) { World = new WorldState { CurrentZoneId = (ushort)zoneId }, EnableZoneEffects = false };
                renderer.LoadZone(zone, textures);
                var camera = new Gordian.Engine.Graphics.ViewportCamera { FarClip = 5000f };
                // Display space is (-x, -height, y); the target sits a little above the ground.
                camera.Update(new Vector3(-x, -height + 1.0f, y), pitch, yaw, distance, (float)Width / Height);
                var env = Gordian.Engine.Graphics.ZoneEnvironmentSettings.CreateDay();
                env.SetTimeOfDay(12.0f);
                env.WeatherId = "fine";
                var colorTarget = gd.SwapchainFramebuffer.ColorTargets[0].Target;
                var rtColor = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(Width, Height, 1, 1, colorTarget.Format, NeoVeldrid.TextureUsage.RenderTarget | NeoVeldrid.TextureUsage.Sampled));
                var rtDepth = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(Width, Height, 1, 1, NeoVeldrid.PixelFormat.R32_Float, NeoVeldrid.TextureUsage.DepthStencil));
                var fb = gd.ResourceFactory.CreateFramebuffer(new NeoVeldrid.FramebufferDescription(rtDepth, rtColor));
                var staging = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(Width, Height, 1, 1, colorTarget.Format, NeoVeldrid.TextureUsage.Staging));
                var cl = gd.ResourceFactory.CreateCommandList();
                // One frame after the given step of the renderer's clock (the wind factor follows it).
                byte[] Frame(float step)
                {
                    renderer.Render(camera, env, step, Width, Height, entities: Array.Empty<WorldEntity>(), resourceManager: rm, present: false, targetFramebuffer: fb);
                    cl.Begin(); cl.CopyTexture(rtColor, staging); cl.End(); gd.SubmitCommands(cl); gd.WaitForIdle();
                    var map = gd.Map(staging, NeoVeldrid.MapMode.Read);
                    var rgba = new byte[Width * Height * 4];
                    for (int row = 0; row < Height; row++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(map.Data + (int)(row * map.RowPitch), rgba, row * Width * 4, Width * 4);
                    }
                    gd.Unmap(staging);
                    return rgba;
                }
                static int Changed(byte[] a, byte[] b)
                {
                    int changed = 0;
                    for (int i = 0; i < a.Length; i += 4)
                    {
                        if (Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]) > 24) changed++;
                    }
                    return changed;
                }

                // The clock starts at 0; each placement is at its own point of the gust (ZoneWind.PlacementPhase), so
                // between two clock times most of them have moved.
                var calm = Frame(0.0f);
                var leaning = Frame((float)Gordian.Engine.Graphics.ZoneWind.PeakSeconds);
                renderer.EnableWindSway = false;
                var stillA = Frame(0.0f);
                var stillB = Frame((float)Gordian.Engine.Graphics.ZoneWind.PeakSeconds / 2);
                int swayed = Changed(calm, leaning), still = Changed(stillA, stillB);
                _out.WriteLine($"zone {zoneId}: {swayed} pixels move with the wind, {still} with the sway off");
                if (Environment.GetEnvironmentVariable("GORDIAN_WIND_DUMP") is { } dump)
                {
                    StockUiRendererTests.SavePng(Path.Combine(dump, $"wind-{zoneId}-calm.png"), calm, Width, Height);
                    StockUiRendererTests.SavePng(Path.Combine(dump, $"wind-{zoneId}-leaning.png"), leaning, Width, Height);
                }
                renderer.Dispose();

                Assert.True(swayed > 200, $"the swaying meshes should move between rest and the full lean: {swayed} pixels");
                Assert.True(still < 20, $"nothing should move with the sway off: {still} pixels");
            }
            finally
            {
                devMgr.Dispose();
                DestroyWindow(hwnd);
            }
        }
    }
}
