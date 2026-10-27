// tests/Gordian.App.Tests/Graphics/SubAreaInteriorRenderTests.cs
using System.Numerics;
using System.Runtime.InteropServices;
using Gordian.App.Graphics;
using Gordian.Core.Resources;
using Gordian.Engine.Graphics;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// Building interiors run their own effect generators (#428): Lower Jeuno's Merry Minstrel's Meadhouse (sub-area 462)
    /// glows at each wall lamp with its <c>ltcu</c> halo. Offscreen on D3D11; skipped off Windows or without the retail install.
    /// </summary>
    [Collection("SessionRegistry")] // holds the shared device for seconds; ViewportShellTests expects none open
    public class SubAreaInteriorRenderTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const uint Width = 320, Height = 180;

        [Fact]
        public void Meadhouse_RunsItsLampHalos()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            Assert.True(rm.TryLoadZone(245, out var zone, out var textures));

            IntPtr hwnd = CreateWindowExW(0, "static", "Interior", unchecked((int)0x80000000), 0, 0, (int)Width, (int)Height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devMgr = new VeldridDeviceManager();
            try
            {
                devMgr.Initialize(NeoVeldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), Width, Height, GraphicsBackendPreference.Direct3D11, vsync: false);
                var gd = devMgr.Device;
                if (gd == null) return;
                using var renderer = new ZoneTerrainRenderer(gd);
                var fmt = gd.SwapchainFramebuffer.ColorTargets[0].Target.Format;
                using var color = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(Width, Height, 1, 1, fmt, NeoVeldrid.TextureUsage.RenderTarget));
                using var depth = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(Width, Height, 1, 1, NeoVeldrid.PixelFormat.R32_Float, NeoVeldrid.TextureUsage.DepthStencil));
                using var fb = gd.ResourceFactory.CreateFramebuffer(new NeoVeldrid.FramebufferDescription(depth, color));
                var env = ZoneEnvironmentSettings.CreateDay();
                env.SetTimeOfDay(20f);
                var camera = new ViewportCamera { FarClip = 5000f, AspectRatio = (float)Width / Height };
                renderer.LoadZone(zone!, textures!);
                renderer.SubAreaOverride = [462];
                for (int frame = 0; frame < 300 && renderer.InteriorEffectParticleCount == 0; frame++)
                {
                    camera.SetLookAt(new Vector3(25.6f, 1.7f, -60.2f), new Vector3(23.2f, 2.0f, -65.5f));
                    renderer.Render(camera, env, 0.016f, Width, Height, null, rm, present: false, targetFramebuffer: fb);
                    if (renderer.InteriorsDrawn == 0) Thread.Sleep(20);
                }
                Assert.Equal(1, renderer.InteriorsDrawn);
                Assert.True(renderer.InteriorEffectParticleCount >= 3, "a halo at each of the three wall lamps");
            }
            finally
            {
                devMgr.Dispose();
                DestroyWindow(hwnd);
            }
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);
    }
}
