// tests/Gordian.App.Tests/Graphics/VesselRenderTests.cs
using System.Numerics;
using System.Runtime.InteropServices;
using Gordian.App.Graphics;
using Gordian.Engine.Graphics;
using Gordian.Core.Resources;
using Gordian.Core.World;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// Vessels on the GPU (#403): a voyage zone draws its vessel, a dock zone draws a calling ship with the vessel of the
    /// zone it sails to. Offscreen on D3D11; skipped off Windows or without the retail install.
    /// </summary>
    public class VesselRenderTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const uint Width = 640, Height = 360;

        [Fact]
        public void ShadowLight_EasesBetweenTheStreetAndTheRoom()
        {
            var outdoor = Vector3.Normalize(new Vector3(0.6f, 0.8f, 0f));
            var indoor = Vector3.UnitY;
            var step = EntityRenderer.EaseShadowLight(outdoor, indoor, 1f / 60f);
            Assert.True(Vector3.Dot(step, outdoor) > 0.99f, "one frame barely moves it");
            var later = outdoor;
            for (int i = 0; i < 180; i++) later = EntityRenderer.EaseShadowLight(later, indoor, 1f / 60f);
            Assert.True(Vector3.Dot(later, indoor) > 0.99f, "three seconds reach the room's light");
        }

        [Fact]
        public void TransportVesselWorld_PlacesTheVesselAtTheEntity()
        {
            var world = ZoneTerrainRenderer.TransportVesselWorld(new Vector3(-125.042f, 0f, 72.786f), 0f);
            var origin = Vector3.Transform(Vector3.Zero, world);
            Assert.Equal(new Vector3(125.042f, 0f, 72.786f), origin);
        }

        [Fact]
        public void CarpentersLanding_DrawsTheBargeAtItsDock_AndPhanauetChannelItsDeck()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            Assert.True(rm.TryLoadZone(2, out var dock, out var dockTextures));
            Assert.True(rm.TryLoadZone(1, out var channel, out var channelTextures));

            IntPtr hwnd = CreateWindowExW(0, "static", "Vessel", unchecked((int)0x80000000), 0, 0, (int)Width, (int)Height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
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
                var camera = new ViewportCamera { FarClip = 5000f };

                // LandSandBoat's barge at the central landing (data/zones/carpenters_landing/npcs.yaml, ship 16).
                renderer.LoadZone(dock, dockTextures);
                var ship = new WorldEntity(0x01001054, 0x54, EntityType.Ship) { ShipNumber = 16, Position = new Vector3(-125.042f, 0f, 72.786f), Direction = 160 };
                camera.Update(new Vector3(125.042f, 0f, 72.786f), 60f, 0f, 40f, (float)Width / Height);
                var entities = new[] { ship };
                for (int frame = 0; frame < 200 && renderer.TransportVesselsDrawn == 0; frame++)
                {
                    renderer.Render(camera, env, 0.016f, Width, Height, entities, rm, present: false, targetFramebuffer: fb);
                    if (renderer.TransportVesselsDrawn == 0) Thread.Sleep(25);
                }
                Assert.Equal(1, renderer.TransportVesselsDrawn);

                // An unmapped ship number draws nothing.
                ship.ShipNumber = 999;
                renderer.Render(camera, env, 0.016f, Width, Height, entities, rm, present: false, targetFramebuffer: fb);
                Assert.Equal(0, renderer.TransportVesselsDrawn);

                // The channel draws its own vessel on top of the scenery it moves.
                renderer.LoadZone(channel, channelTextures);
                renderer.VoyageClockOverride = 1600;
                camera.Update(new Vector3(0f, 2f, -1f), 10f, 0f, 12f, (float)Width / Height);
                renderer.Render(camera, env, 0.016f, Width, Height, present: false, targetFramebuffer: fb);
                Assert.NotNull(renderer.ActiveResidentZone);
                Assert.True(renderer.ActiveResidentZone!.VesselSubmeshes.Count > 0);
                Assert.True(renderer.DrawCalls > renderer.ActiveResidentZone.VesselSubmeshes.Count / 2);
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
