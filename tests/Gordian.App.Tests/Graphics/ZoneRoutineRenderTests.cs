// tests/Gordian.App.Tests/Graphics/ZoneRoutineRenderTests.cs
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Gordian.App.Graphics;
using Gordian.Core.World;
using Xunit;

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
            devMgr.Initialize(NeoVeldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), 640, 480, GraphicsBackendPreference.Direct3D11, vsync: false);
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
                var rtColor = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(640, 480, 1, 1, colorTarget.Format, NeoVeldrid.TextureUsage.RenderTarget | NeoVeldrid.TextureUsage.Sampled));
                var rtDepth = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(640, 480, 1, 1, NeoVeldrid.PixelFormat.R32_Float, NeoVeldrid.TextureUsage.DepthStencil));
                var fb = gd.ResourceFactory.CreateFramebuffer(new NeoVeldrid.FramebufferDescription(rtDepth, rtColor));
                var staging = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(640, 480, 1, 1, colorTarget.Format, NeoVeldrid.TextureUsage.Staging));
                var cl = gd.ResourceFactory.CreateCommandList();
                void Run(int frames) { for (int i = 0; i < frames; i++) renderer.Render(camera, env, 1 / 60f, 640, 480, entities: Array.Empty<WorldEntity>(), resourceManager: rm, present: false, targetFramebuffer: fb); }
                double Brightness()
                {
                    cl.Begin(); cl.CopyTexture(rtColor, staging); cl.End(); gd.SubmitCommands(cl); gd.WaitForIdle();
                    var map = gd.Map(staging, NeoVeldrid.MapMode.Read);
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
            /// <summary>
        /// Alzadaal's door _20t (#227, #232): with its door entity in the world, the door's intc plays map1 and its idle
        /// meshes g000-g009 (ZoneActor0xA) are bound to the door and drawn there; opening plays stat on the door.
        /// </summary>
        [Fact]
        public void AlzadaalDoor_DrawsItsIdleMeshesOnTheDoorEntity()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(72, out var zone, out var textures) || !zone.DoorLeaves.TryGetValue("_20t", out var leaves)) return;
            var at = leaves[0].Placement.Position;

            IntPtr hwnd = CreateWindowExW(0, "static", "Test", unchecked((int)0x80000000), 0, 0, 640, 480, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devMgr = new VeldridDeviceManager();
            devMgr.Initialize(NeoVeldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), 640, 480, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devMgr.Device!;
            try
            {
                var world = new WorldState { CurrentZoneId = 72 };
                var door = new WorldEntity(0x01048123, 0x123, EntityType.Door) { TransportId = "_20t", Position = at };
                world.UpsertEntity(door);
                var renderer = new ZoneTerrainRenderer(gd) { World = world };
                renderer.LoadZone(zone, textures);
                var camera = new Gordian.Core.Graphics.ViewportCamera { FarClip = 5000f };
                camera.Update(new Vector3(-at.X, -at.Y + 2f, at.Z), 10.0f, 0.0f, 10.0f, 640f / 480f);
                var env = Gordian.Core.Graphics.ZoneEnvironmentSettings.CreateNight();
                env.WeatherId = "fine";
                var colorTarget = gd.SwapchainFramebuffer.ColorTargets[0].Target;
                var rtColor = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(640, 480, 1, 1, colorTarget.Format, NeoVeldrid.TextureUsage.RenderTarget | NeoVeldrid.TextureUsage.Sampled));
                var rtDepth = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(640, 480, 1, 1, NeoVeldrid.PixelFormat.R32_Float, NeoVeldrid.TextureUsage.DepthStencil));
                var fb = gd.ResourceFactory.CreateFramebuffer(new NeoVeldrid.FramebufferDescription(rtDepth, rtColor));
                void Run(int frames) { for (int i = 0; i < frames; i++) renderer.Render(camera, env, 1 / 60f, 640, 480, entities: world.GetAllEntities(), resourceManager: rm, present: false, targetFramebuffer: fb); }

                Run(20);
                int bound = renderer.BoundAttachedEmitters;
                int closedDraws = renderer.DrawCalls;
                _out.WriteLine($"door _20t closed: {bound} bound emitters, {closedDraws} draws");
                Assert.True(bound >= 10, $"intc / map1 should bind g000-g009 to the door: {bound}");

                door.AnimationState = Gordian.Core.World.Collision.ZoneDoors.StatusOpen;
                Run(30);
                _out.WriteLine($"door _20t opening: {renderer.BoundAttachedEmitters} bound emitters, {renderer.DrawCalls} draws");
                Assert.True(renderer.BoundAttachedEmitters > bound, "open / stat should bind its burst to the door");
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
