// tests/Gordian.App.Tests/Graphics/StaticBatchingRenderTests.cs
using System;
using System.IO;
using System.Numerics;
using Gordian.App.Graphics;
using Gordian.Core.World;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// Pass 1's static batching (#606 round 2, <see cref="ZoneTerrainRenderer.EnableStaticBatching"/>), rendered offscreen
    /// at the maintainer's Konschtat Highlands spot looking down the valley: far fewer draw calls, the same picture.
    /// Skipped without the retail install.
    /// </summary>
    public class StaticBatchingRenderTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const int Width = 640, Height = 342;
        private readonly ITestOutputHelper _out;
        public StaticBatchingRenderTests(ITestOutputHelper o) => _out = o;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr CreateWindowExW(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int X, int Y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        static extern bool DestroyWindow(IntPtr hWnd);

        [Fact]
        public void KonschtatValley_DrawsTheSamePictureInFewerCalls()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(108, out var zone, out var textures)) return;

            IntPtr hwnd = CreateWindowExW(0, "static", "Test", unchecked((int)0x80000000), 0, 0, Width, Height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devMgr = new VeldridDeviceManager();
            devMgr.Initialize(NeoVeldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), Width, Height, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devMgr.Device!;
            try
            {
                var renderer = new ZoneTerrainRenderer(gd) { World = new WorldState { CurrentZoneId = 108 } };
                renderer.LoadZone(zone, textures);
                var env = Gordian.Engine.Graphics.ZoneEnvironmentSettings.CreateDay();
                if (zone.EnvironmentData?.Interpolate(12.0f, "fine") is { } keyframe) env.ApplyKeyframe(keyframe);
                env.SetTimeOfDay(12.0f);
                env.WeatherId = "fine";
                var camera = new Gordian.Engine.Graphics.ViewportCamera();
                // Server (-227.63, 70.98, 769.83), heading 33 (46 degrees): 5,300 submeshes in view, 2,371 of them flower quads.
                camera.Update(new Vector3(227.63f, -70.98f, 769.83f), 10f, 46f, 6f, (float)Width / Height);
                camera.FarClip = Gordian.Engine.Graphics.ZoneVisibility.GlobalDrawDistance(env.DrawDistance, env.DrawDistanceMultiplier);

                var colorTarget = gd.SwapchainFramebuffer.ColorTargets[0].Target;
                var rtColor = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(Width, Height, 1, 1, colorTarget.Format, NeoVeldrid.TextureUsage.RenderTarget | NeoVeldrid.TextureUsage.Sampled));
                var rtDepth = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(Width, Height, 1, 1, NeoVeldrid.PixelFormat.R32_Float, NeoVeldrid.TextureUsage.DepthStencil));
                var fb = gd.ResourceFactory.CreateFramebuffer(new NeoVeldrid.FramebufferDescription(rtDepth, rtColor));
                var staging = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(Width, Height, 1, 1, colorTarget.Format, NeoVeldrid.TextureUsage.Staging));
                var cl = gd.ResourceFactory.CreateCommandList();
                // The clock stands still, so the sway, clouds and emitters are the same in every frame.
                (byte[] Rgba, int Draws) Frame(bool batching)
                {
                    renderer.EnableStaticBatching = batching;
                    for (int i = 0; i < 4; i++) renderer.Render(camera, env, 0.0f, Width, Height, entities: Array.Empty<WorldEntity>(), resourceManager: rm, present: false, targetFramebuffer: fb);
                    cl.Begin(); cl.CopyTexture(rtColor, staging); cl.End(); gd.SubmitCommands(cl); gd.WaitForIdle();
                    var map = gd.Map(staging, NeoVeldrid.MapMode.Read);
                    var rgba = new byte[Width * Height * 4];
                    for (int row = 0; row < Height; row++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(map.Data + (int)(row * map.RowPitch), rgba, row * Width * 4, Width * 4);
                    }
                    gd.Unmap(staging);
                    return (rgba, renderer.DrawCalls);
                }

                Frame(false); // warm-up: the first frames after a load still settle
                var single = Frame(false);
                var batched = Frame(true);
                int differing = 0;
                for (int i = 0; i < single.Rgba.Length; i += 4)
                {
                    if (single.Rgba[i] != batched.Rgba[i] || single.Rgba[i + 1] != batched.Rgba[i + 1] || single.Rgba[i + 2] != batched.Rgba[i + 2]) differing++;
                }
                _out.WriteLine($"draw calls {single.Draws} one by one, {batched.Draws} batched; {differing} pixels differ");
                renderer.Dispose();

                Assert.True(single.Draws > 4000, $"the valley view should draw thousands of submeshes: {single.Draws}");
                Assert.True(batched.Draws < single.Draws * 6 / 10, $"batching should save over 40% of the calls: {batched.Draws} of {single.Draws}");
                // Only ties between crossed leaf cards at equal depth may come out the other way.
                Assert.True(differing <= Width * Height / 10000, $"the picture should not change: {differing} pixels differ");
            }
            finally
            {
                devMgr.Dispose();
                DestroyWindow(hwnd);
            }
        }
    }
}
