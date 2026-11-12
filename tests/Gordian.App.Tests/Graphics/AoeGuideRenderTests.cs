// tests/Gordian.App.Tests/Graphics/AoeGuideRenderTests.cs
using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Gordian.App.Graphics;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// The area-of-effect range guide (#356) on real zone geometry, offscreen on D3D11: Ru'Lude Gardens' plaza where the
    /// retail capture stood (Gemini at (-12, 3, -8), a radius-index-8 guide at 17.6 yalms, 09:00). Skipped without
    /// Windows or the game install. Set <c>GORDIAN_AOE_DUMP</c> to a folder to write the frames as PNGs.
    /// </summary>
    public class AoeGuideRenderTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [Fact]
        public void Guide_DrawsTheBandOnTheFloorAndDimsOutside()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            const uint W = 1280, H = 720;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(243, out var zone, out var textures)) return;

            IntPtr hwnd = CreateWindowExW(0, "static", "AoeGuide", unchecked((int)0x80000000), 0, 0, (int)W, (int)H, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devMgr = new VeldridDeviceManager();
            devMgr.Initialize(NeoVeldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), W, H, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devMgr.Device;
            if (gd == null) return;
            try
            {
                using var renderer = new ZoneTerrainRenderer(gd);
                renderer.LoadZone(zone, textures);
                var env = Gordian.Engine.Graphics.ZoneEnvironmentSettings.CreateDay();
                env.SetTimeOfDay(9f);
                env.WeatherId = "fine";
                renderer.SkyDomeRenderer?.UpdateDome(env);
                var format = gd.SwapchainFramebuffer.ColorTargets[0].Target.Format;
                using var color = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(W, H, 1, 1, format, NeoVeldrid.TextureUsage.RenderTarget | NeoVeldrid.TextureUsage.Sampled));
                using var depth = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(W, H, 1, 1, NeoVeldrid.PixelFormat.R32_Float, NeoVeldrid.TextureUsage.DepthStencil));
                using var framebuffer = gd.ResourceFactory.CreateFramebuffer(new NeoVeldrid.FramebufferDescription(depth, color));
                using var staging = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(W, H, 1, 1, format, NeoVeldrid.TextureUsage.Staging));
                using var cl = gd.ResourceFactory.CreateCommandList();

                // Display space is (-x, -height, y) of the server position.
                var center = new Vector3(12f, -3f, -8f);
                byte[] Frame(bool guide)
                {
                    var camera = new Gordian.Engine.Graphics.ViewportCamera { FarClip = 3000f };
                    camera.Update(center, 15f, 0f, 6f, (float)W / H);
                    renderer.AoeGuide = guide ? (1u, 17.6f) : null;
                    renderer.AoeGuideCenterOverride = center;
                    renderer.AoeGuidePhaseOverride = 0.55f;
                    renderer.Render(camera, env, 0.016f, W, H, present: false, targetFramebuffer: framebuffer);
                    cl.Begin();
                    cl.CopyTexture(color, staging);
                    cl.End();
                    gd.SubmitCommands(cl);
                    gd.WaitForIdle();
                    var map = gd.Map(staging, NeoVeldrid.MapMode.Read);
                    var rgba = new byte[W * H * 4];
                    for (int row = 0; row < H; row++) Marshal.Copy(map.Data + (int)(row * map.RowPitch), rgba, (int)(row * W * 4), (int)W * 4);
                    gd.Unmap(staging);
                    if (format == NeoVeldrid.PixelFormat.B8_G8_R8_A8_UNorm)
                        for (int i = 0; i < rgba.Length; i += 4) (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]);
                    for (int i = 3; i < rgba.Length; i += 4) rgba[i] = 255;
                    return rgba;
                }

                var off = Frame(false);
                var on = Frame(true);
                if (Environment.GetEnvironmentVariable("GORDIAN_AOE_DUMP") is { Length: > 0 } dump)
                {
                    StockUiRendererTests.SavePng(Path.Combine(dump, "aoe_guide_off.png"), off, (int)W, (int)H);
                    StockUiRendererTests.SavePng(Path.Combine(dump, "aoe_guide_on.png"), on, (int)W, (int)H);
                }
                (byte R, byte G, byte B) Pixel(byte[] rgba, int x, int y) { int i = (y * (int)W + x) * 4; return (rgba[i], rgba[i + 1], rgba[i + 2]); }

                // The band crosses the plaza ahead (rows 250-300 at this view): retail's (89-91, 87-93, 206-214) on shaded floor.
                foreach (int x in new[] { 300, 450, 600 })
                {
                    var band = Pixel(on, x, 250);
                    for (int y = 251; y < 300; y++) if (Pixel(on, x, y).B - Pixel(on, x, y).R > band.B - band.R) band = Pixel(on, x, y);
                    Assert.InRange(band.B, 190, 230);
                    Assert.InRange(band.R, 75, 105);
                }

                // Outside the circle the scene darkens by about 5 %; the sky (no zone mesh) is left alone.
                var outside = Pixel(on, 30, 200);
                var outsideBefore = Pixel(off, 30, 200);
                Assert.InRange(outside.G, (int)(outsideBefore.G * 0.9f), outsideBefore.G - 1);
                Assert.Equal(Pixel(off, 640, 20), Pixel(on, 640, 20));
            }
            finally
            {
                devMgr.Dispose();
                DestroyWindow(hwnd);
            }
        }
    }
}
