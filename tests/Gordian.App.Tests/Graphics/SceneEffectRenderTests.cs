// tests/Gordian.App.Tests/Graphics/SceneEffectRenderTests.cs
using System;
using System.IO;
using System.Numerics;
using Gordian.App.Graphics;
using Gordian.Core.Events;
using Gordian.Core.Resources.Events;
using Xunit;

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
            devMgr.Initialize(NeoVeldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), 640, 480, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devMgr.Device!;
            try
            {
                var renderer = new ZoneTerrainRenderer(gd); // no zone: a black background
                var camera = new Gordian.Core.Graphics.ViewportCamera { FarClip = 5000f };
                var env = Gordian.Core.Graphics.ZoneEnvironmentSettings.CreateNight();
                var colorTarget = gd.SwapchainFramebuffer.ColorTargets[0].Target;
                var rtColor = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(640, 480, 1, 1, colorTarget.Format, NeoVeldrid.TextureUsage.RenderTarget | NeoVeldrid.TextureUsage.Sampled));
                var rtDepth = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(640, 480, 1, 1, NeoVeldrid.PixelFormat.R32_Float, NeoVeldrid.TextureUsage.DepthStencil));
                var fb = gd.ResourceFactory.CreateFramebuffer(new NeoVeldrid.FramebufferDescription(rtDepth, rtColor));
                var staging = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(640, 480, 1, 1, colorTarget.Format, NeoVeldrid.TextureUsage.Staging));
                var cl = gd.ResourceFactory.CreateCommandList();
                double now = 0;
                var presentation = new EventPresentation { Clock = () => now };
                renderer.EventPresentation = presentation;
                camera.SetEventView(new Vector3(0, 9.6f, 0), new Vector3(0, 9.6f, 10), 1.0f, 0f, 640f / 480f);
                void Run(int frames) { for (int i = 0; i < frames; i++) { now += 1 / 60.0; renderer.Render(camera, env, 1 / 60f, 640, 480, entities: Array.Empty<Gordian.Core.World.WorldEntity>(), resourceManager: rm, present: false, targetFramebuffer: fb); } }
                double CentroidY()
                {
                    cl.Begin(); cl.CopyTexture(rtColor, staging); cl.End(); gd.SubmitCommands(cl); gd.WaitForIdle();
                    var map = gd.Map(staging, NeoVeldrid.MapMode.Read);
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
            devMgr.Initialize(NeoVeldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), 640, 480, GraphicsBackendPreference.Direct3D11, vsync: false);
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
                var rtColor = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(640, 480, 1, 1, colorTarget.Format, NeoVeldrid.TextureUsage.RenderTarget | NeoVeldrid.TextureUsage.Sampled));
                var rtDepth = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(640, 480, 1, 1, NeoVeldrid.PixelFormat.R32_Float, NeoVeldrid.TextureUsage.DepthStencil));
                var fb = gd.ResourceFactory.CreateFramebuffer(new NeoVeldrid.FramebufferDescription(rtDepth, rtColor));
                var staging = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(640, 480, 1, 1, colorTarget.Format, NeoVeldrid.TextureUsage.Staging));
                var cl = gd.ResourceFactory.CreateCommandList();
                double now = 0;
                var presentation = new EventPresentation { Clock = () => now };
                renderer.EventPresentation = presentation;
                void Run(int frames) { for (int i = 0; i < frames; i++) { now += 1 / 60.0; renderer.Render(camera, env, 1 / 60f, 640, 480, entities: Array.Empty<Gordian.Core.World.WorldEntity>(), resourceManager: rm, present: false, targetFramebuffer: fb); } }
                bool[,] Lit()
                {
                    cl.Begin(); cl.CopyTexture(rtColor, staging); cl.End(); gd.SubmitCommands(cl); gd.WaitForIdle();
                    var map = gd.Map(staging, NeoVeldrid.MapMode.Read);
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

        /// <summary>
        /// Port Jeuno 324's blink cards (#208): at frame 88 after <c>open</c> (<c>bk01</c> colour alpha 0.28, <c>md00</c>
        /// 0.21) the maintainer's retail recording shows the scene at about 20-25% brightness inside the slit, which 2 x the
        /// colour alpha gives (0.44 x 0.58 = 26% let through); with the paletted texture alpha and the scene DAT's vertex
        /// colours both doubled (8 x) the slit stayed black. Before <c>open</c> the card <c>bk00</c> (alpha 0x80) is fully
        /// black, as in retail.
        /// </summary>
        [Fact]
        public void BlinkCards_LetTheSceneThroughDimlyMidway()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(4, out var zone, out var textures)) return;
            IntPtr hwnd = CreateWindowExW(0, "static", "Test", unchecked((int)0x80000000), 0, 0, 640, 480, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devMgr = new VeldridDeviceManager();
            devMgr.Initialize(NeoVeldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), 640, 480, GraphicsBackendPreference.Direct3D11, vsync: false);
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
                var rtColor = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(640, 480, 1, 1, colorTarget.Format, NeoVeldrid.TextureUsage.RenderTarget | NeoVeldrid.TextureUsage.Sampled));
                var rtDepth = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(640, 480, 1, 1, NeoVeldrid.PixelFormat.R32_Float, NeoVeldrid.TextureUsage.DepthStencil));
                var fb = gd.ResourceFactory.CreateFramebuffer(new NeoVeldrid.FramebufferDescription(rtDepth, rtColor));
                var staging = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(640, 480, 1, 1, colorTarget.Format, NeoVeldrid.TextureUsage.Staging));
                var cl = gd.ResourceFactory.CreateCommandList();
                double now = 0;
                var presentation = new EventPresentation { Clock = () => now };
                renderer.EventPresentation = presentation;
                void Run(int frames) { for (int i = 0; i < frames; i++) { now += 1 / 60.0; renderer.Render(camera, env, 1 / 60f, 640, 480, entities: Array.Empty<Gordian.Core.World.WorldEntity>(), resourceManager: rm, present: false, targetFramebuffer: fb); } }
                // Summed RGB over the screen's central box (inside the slit once it opens).
                long Centre()
                {
                    cl.Begin(); cl.CopyTexture(rtColor, staging); cl.End(); gd.SubmitCommands(cl); gd.WaitForIdle();
                    var map = gd.Map(staging, NeoVeldrid.MapMode.Read);
                    long sum = 0;
                    var row = new byte[640 * 4];
                    for (int y = 250; y < 290; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(map.Data + (int)(y * map.RowPitch), row, 0, row.Length);
                        for (int x = 240; x < 400; x++) sum += row[x * 4] + row[x * 4 + 1] + row[x * 4 + 2];
                    }
                    gd.Unmap(staging);
                    return sum;
                }
                Run(2);
                long scene = Centre();
                var blink = EventSceneResource.Parse(rm.LoadDatBytesByFileId(51402)!);
                presentation.Play(1, blink, blink.Routines["bl00"], new Vector3(0, -10, 0), 51402, 0x12345, 0x12345, 0f);
                Run(10);
                double closed = Centre() / (double)scene;
                presentation.Play(2, blink, blink.Routines["open"], new Vector3(0, -10, 0), 51402, 0x12345, 0x12345, 0f);
                Run(88);
                double midway = Centre() / (double)scene;
                _out.WriteLine($"centre brightness: closed {closed:P1}, 88 frames after open {midway:P1} of the scene");
                Assert.True(closed < 0.02, $"bk00 should be fully black, got {closed:P1}");
                Assert.InRange(midway, 0.12, 0.40);
                renderer.Dispose();
            }
            finally
            {
                devMgr.Dispose();
                DestroyWindow(hwnd);
            }
        }

        [Theory]
        [InlineData(true, true, 1.0f)]   // the ignore flag wins
        [InlineData(false, true, 2.0f)]  // paletted: halve the decoder's doubled alpha
        [InlineData(false, false, 0.0f)] // DXT: as sampled
        public void ParticleTextureAlphaMode_HalvesOnlyDoubledPalettedAlpha(bool ignore, bool doubled, float expected)
        {
            var texture = new Gordian.Core.Resources.Graphics.DecodedTexture("t", 1, 1, new byte[4]) { AlphaDoubled = doubled };
            Assert.Equal(expected, ZoneTerrainRenderer.ParticleTextureAlphaMode(ignore, texture));
            Assert.Equal(ignore ? 1.0f : 0.0f, ZoneTerrainRenderer.ParticleTextureAlphaMode(ignore, null));
        }

        /// <summary>
        /// An event fade (0x6C, #197): an NPC at half alpha (0x40) over Bibiki Bay's bright sea shows about halfway between
        /// the opaque body and the background, and at alpha 0 it is not drawn at all.
        /// </summary>
        [Fact]
        public void FadedEntity_BlendsOverTheBackground()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(4, out var zone, out var textures)) return;
            var npc = new Gordian.Core.World.WorldEntity(0x01000001, 1, Gordian.Core.World.EntityType.Npc) { Hpp = 100, KeepsEventHeight = true };
            for (uint model = 1; model < 400 && npc.Appearance.ModelId == 0; model++)
            {
                npc.Appearance.ModelId = model;
                if (!rm.TryLoadEntityModel(npc, out var loaded) || loaded?.Skeleton is not { Count: > 0 }) npc.Appearance.ModelId = 0;
            }
            if (npc.Appearance.ModelId == 0) return;
            // Two yalms in front of the camera, a little below the eye: internal Y is the height, drawn at display -Y.
            npc.EventPose = new Gordian.Core.World.EventPose(new Vector3(0, -9f, 4f), 0f, 0f);
            IntPtr hwnd = CreateWindowExW(0, "static", "Test", unchecked((int)0x80000000), 0, 0, 640, 480, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devMgr = new VeldridDeviceManager();
            devMgr.Initialize(NeoVeldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), 640, 480, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devMgr.Device!;
            try
            {
                var renderer = new ZoneTerrainRenderer(gd);
                renderer.LoadZone(zone, textures);
                var camera = new Gordian.Core.Graphics.ViewportCamera { FarClip = 5000f };
                camera.SetEventView(new Vector3(0, 10, 0), new Vector3(0, 10, 10), 1.0f, 0f, 640f / 480f);
                var env = Gordian.Core.Graphics.ZoneEnvironmentSettings.CreateDay();
                env.WeatherId = "fine";
                renderer.SkyDomeRenderer?.UpdateDome(env);
                var colorTarget = gd.SwapchainFramebuffer.ColorTargets[0].Target;
                var rtColor = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(640, 480, 1, 1, colorTarget.Format, NeoVeldrid.TextureUsage.RenderTarget | NeoVeldrid.TextureUsage.Sampled));
                var rtDepth = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(640, 480, 1, 1, NeoVeldrid.PixelFormat.R32_Float, NeoVeldrid.TextureUsage.DepthStencil));
                var fb = gd.ResourceFactory.CreateFramebuffer(new NeoVeldrid.FramebufferDescription(rtDepth, rtColor));
                var staging = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(640, 480, 1, 1, colorTarget.Format, NeoVeldrid.TextureUsage.Staging));
                var cl = gd.ResourceFactory.CreateCommandList();
                var entities = new[] { npc };
                int[] Frame(int alpha)
                {
                    npc.EventAlpha = alpha;
                    for (int i = 0; i < 3; i++) renderer.Render(camera, env, 0f, 640, 480, entities: entities, resourceManager: rm, present: false, targetFramebuffer: fb);
                    cl.Begin(); cl.CopyTexture(rtColor, staging); cl.End(); gd.SubmitCommands(cl); gd.WaitForIdle();
                    var map = gd.Map(staging, NeoVeldrid.MapMode.Read);
                    var sums = new int[640 * 480];
                    var row = new byte[640 * 4];
                    for (int y = 0; y < 480; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(map.Data + (int)(y * map.RowPitch), row, 0, row.Length);
                        for (int x = 0; x < 640; x++) sums[y * 640 + x] = row[x * 4] + row[x * 4 + 1] + row[x * 4 + 2];
                    }
                    gd.Unmap(staging);
                    return sums;
                }
                int[] opaque = Frame(Gordian.Core.World.WorldEntity.OpaqueEventAlpha), half = Frame(0x40), none = Frame(0);
                long bodyDifference = 0, halfDifference = 0;
                int bodyPixels = 0;
                for (int i = 0; i < opaque.Length; i++)
                {
                    if (Math.Abs(opaque[i] - none[i]) < 60) continue; // the background, or body close to it in colour
                    bodyPixels++;
                    bodyDifference += Math.Abs(opaque[i] - none[i]);
                    halfDifference += Math.Abs(half[i] - none[i]);
                }
                _out.WriteLine($"model {npc.Appearance.ModelId}: {bodyPixels} body pixels, half alpha keeps {halfDifference / (double)Math.Max(1, bodyDifference):P0} of the body's contrast");
                Assert.True(bodyPixels > 500, $"the NPC should cover part of the screen, got {bodyPixels} pixels");
                Assert.InRange(halfDifference / (double)bodyDifference, 0.3, 0.7);
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
