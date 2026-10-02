// tests/Gordian.App.Tests/Graphics/ScenePostProcessTests.cs
using System;
using System.Collections.Generic;
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
    /// The cutscene post-process (#205) drawn offscreen over Bibiki Bay: a pass-through while nothing runs, the shared
    /// cross-dissolve <c>ovl1</c> and blur <c>blon</c> / <c>blof</c> of file 30904. Skipped off Windows or without the
    /// game install.
    /// </summary>
    public class ScenePostProcessTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const int Width = 640, Height = 480;
        private readonly ITestOutputHelper _out;
        public ScenePostProcessTests(ITestOutputHelper o) => _out = o;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr CreateWindowExW(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int X, int Y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        static extern bool DestroyWindow(IntPtr hWnd);

        private sealed class Harness : IDisposable
        {
            private readonly IntPtr _hwnd;
            private readonly VeldridDeviceManager _devMgr;
            private readonly Veldrid.GraphicsDevice _gd;
            private readonly Veldrid.Framebuffer _fb;
            private readonly Veldrid.Texture _color, _depth, _staging, _depthStaging;
            private readonly Veldrid.CommandList _cl;
            private readonly Gordian.Core.Resources.ResourceManager _rm;
            private readonly Gordian.Core.Graphics.ZoneEnvironmentSettings _env;
            public readonly ZoneTerrainRenderer Renderer;
            public readonly Gordian.Core.Graphics.ViewportCamera Camera = new() { FarClip = 5000f };
            public readonly EventPresentation Presentation;
            public double Now;

            public Harness(Gordian.Core.Resources.ResourceManager rm, Gordian.Core.Resources.Models.ZoneGeometry zone,
                Dictionary<string, Gordian.Core.Resources.Graphics.DecodedTexture> textures)
            {
                _rm = rm;
                _hwnd = CreateWindowExW(0, "static", "Test", unchecked((int)0x80000000), 0, 0, Width, Height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                _devMgr = new VeldridDeviceManager();
                _devMgr.Initialize(Veldrid.SwapchainSource.CreateWin32(_hwnd, IntPtr.Zero), Width, Height, GraphicsBackendPreference.Direct3D11, vsync: false);
                _gd = _devMgr.Device!;
                // A still picture: no drifting clouds or rolling surf between the frames compared.
                Renderer = new ZoneTerrainRenderer(_gd) { EnableWeatherClouds = false, EnableZoneEffects = false };
                Renderer.LoadZone(zone, textures);
                _env = Gordian.Core.Graphics.ZoneEnvironmentSettings.CreateDay();
                _env.WeatherId = "fine";
                Renderer.SkyDomeRenderer?.UpdateDome(_env);
                var format = _gd.SwapchainFramebuffer.ColorTargets[0].Target.Format;
                _color = _gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(Width, Height, 1, 1, format, Veldrid.TextureUsage.RenderTarget | Veldrid.TextureUsage.Sampled));
                _depth = _gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(Width, Height, 1, 1, Veldrid.PixelFormat.R32_Float, Veldrid.TextureUsage.DepthStencil));
                _fb = _gd.ResourceFactory.CreateFramebuffer(new Veldrid.FramebufferDescription(_depth, _color));
                _staging = _gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(Width, Height, 1, 1, format, Veldrid.TextureUsage.Staging));
                _depthStaging = _gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(Width, Height, 1, 1, Veldrid.PixelFormat.R32_Float, Veldrid.TextureUsage.Staging));
                _cl = _gd.ResourceFactory.CreateCommandList();
                Presentation = new EventPresentation { Clock = () => Now };
                Renderer.EventPresentation = Presentation;
            }

            public void Look(float yaw) => Camera.Update(new Vector3(0, 10, 0), 15.0f, yaw, 6.0f, (float)Width / Height);

            public void Run(int frames)
            {
                for (int i = 0; i < frames; i++)
                {
                    Now += 1 / 60.0;
                    Renderer.Render(Camera, _env, 1 / 60f, Width, Height, entities: Array.Empty<Gordian.Core.World.WorldEntity>(),
                        resourceManager: _rm, present: false, targetFramebuffer: _fb);
                }
            }

            public byte[] Capture()
            {
                _cl.Begin(); _cl.CopyTexture(_color, _staging); _cl.End(); _gd.SubmitCommands(_cl); _gd.WaitForIdle();
                var map = _gd.Map(_staging, Veldrid.MapMode.Read);
                var pixels = new byte[Width * Height * 4];
                for (int y = 0; y < Height; y++) System.Runtime.InteropServices.Marshal.Copy(map.Data + (int)(y * map.RowPitch), pixels, y * Width * 4, Width * 4);
                _gd.Unmap(_staging);
                return pixels;
            }

            /// <summary>The target's depth buffer (what the name plates test against).</summary>
            public float[] CaptureDepth()
            {
                _cl.Begin(); _cl.CopyTexture(_depth, _depthStaging); _cl.End(); _gd.SubmitCommands(_cl); _gd.WaitForIdle();
                var map = _gd.Map(_depthStaging, Veldrid.MapMode.Read);
                var depth = new float[Width * Height];
                for (int y = 0; y < Height; y++) System.Runtime.InteropServices.Marshal.Copy(map.Data + (int)(y * map.RowPitch), depth, y * Width, Width);
                _gd.Unmap(_depthStaging);
                return depth;
            }

            public void Dispose()
            {
                Renderer.Dispose();
                _cl.Dispose(); _fb.Dispose(); _color.Dispose(); _depth.Dispose(); _staging.Dispose(); _depthStaging.Dispose();
                _devMgr.Dispose();
                DestroyWindow(_hwnd);
            }
        }

        /// <summary>Mean absolute difference per channel, 0-255.</summary>
        private static double Difference(byte[] a, byte[] b)
        {
            long sum = 0;
            for (int i = 0; i < a.Length; i += 4) sum += Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]);
            return sum / (a.Length / 4 * 3.0);
        }

        private static double Brightness(byte[] a)
        {
            long sum = 0;
            for (int i = 0; i < a.Length; i += 4) sum += a[i] + a[i + 1] + a[i + 2];
            return sum / (a.Length / 4 * 3.0);
        }

        private static Harness? Open(out Gordian.Core.Resources.ResourceManager rm)
        {
            rm = null!;
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return null;
            rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(4, out var zone, out var textures)) return null;
            return new Harness(rm, zone, textures);
        }

        [Fact]
        public void HeldCamera_DrawsOffscreen_ButShowsTheSceneUnchanged()
        {
            using var h = Open(out _);
            if (h == null) return;
            h.Look(180f);
            h.Renderer.EnablePostProcess = false;
            h.Run(3);
            var direct = h.Capture();
            h.Renderer.EnablePostProcess = true;
            h.Presentation.SetCameraHeld(true);
            h.Run(3);
            var post = h.Capture();
            double diff = Difference(direct, post);
            _out.WriteLine($"pass-through difference {diff:F3}");
            Assert.True(diff < 0.5, $"the pass-through should not change the scene, mean difference {diff:F3}");
        }

        /// <summary>
        /// The name plates draw after the scene and test the target's depth buffer: with the scene drawn offscreen, that
        /// buffer must still hold this frame's scene. It held a stale frame from before the cutscene, which cut or hid
        /// plates (in-game test, 2026-10-02).
        /// </summary>
        [Fact]
        public void HeldCamera_LeavesTheScenesDepthInTheTarget()
        {
            using var h = Open(out _);
            if (h == null) return;
            h.Look(180f);
            h.Renderer.EnablePostProcess = false;
            h.Run(2);
            var direct = h.CaptureDepth();
            // The last frame drawn straight to the target looks the other way, so a stale buffer cannot pass.
            h.Look(0f);
            h.Run(2);
            h.Renderer.EnablePostProcess = true;
            h.Presentation.SetCameraHeld(true);
            h.Look(180f);
            h.Run(2);
            var post = h.CaptureDepth();
            int differ = 0, near = 0;
            for (int i = 0; i < direct.Length; i++)
            {
                if (MathF.Abs(direct[i] - post[i]) > 1e-5f) differ++;
                if (direct[i] < 1f) near++;
            }
            _out.WriteLine($"depth: {near} pixels with scene depth, {differ} differ");
            Assert.True(near > 5000, "the view should have geometry");
            Assert.Equal(0, differ);
        }

        [Fact]
        public void CrossDissolve_StartsOnTheShotBefore_AndEndsOnTheNewOne()
        {
            using var h = Open(out var rm);
            if (h == null) return;
            var shared = EventSceneResource.Parse(rm.LoadDatBytesByFileId(30904)!);
            h.Presentation.SetCameraHeld(true);
            h.Look(180f);
            h.Run(5);
            var before = h.Capture();

            // The cut and the dissolve start together, as a scene's next shot and ovl1 (60 frames) do.
            h.Presentation.Play(1, shared, shared.Routines["ovl1"], Vector3.Zero);
            h.Look(0f);
            h.Run(1);
            var first = h.Capture();
            h.Run(29);
            var middle = h.Capture();
            h.Run(40);
            var after = h.Capture();
            h.Run(2);
            var settled = h.Capture();

            double firstToBefore = Difference(first, before), endToBefore = Difference(after, before), endToSettled = Difference(after, settled);
            double middleToBefore = Difference(middle, before);
            _out.WriteLine($"first vs before {firstToBefore:F2}, middle vs before {middleToBefore:F2}, end vs before {endToBefore:F2} / settled {endToSettled:F2}");
            Assert.True(endToBefore > 10, "the two shots should differ");
            Assert.True(firstToBefore < endToBefore * 0.1, "the dissolve should start on the shot before the cut");
            Assert.True(endToSettled < 0.5, "the dissolve should end on the new shot");
            Assert.InRange(middleToBefore, endToBefore * 0.25, endToBefore * 0.75);
        }

        [Fact]
        public void BlurOn_KeepsATintedTrail_AndBlurOffClearsIt()
        {
            using var h = Open(out var rm);
            if (h == null) return;
            var shared = EventSceneResource.Parse(rm.LoadDatBytesByFileId(30904)!);
            h.Presentation.SetCameraHeld(true);
            h.Look(180f);
            h.Run(5);
            var plain = h.Capture();

            // blon: A0 A0 A0 30 / 0.98 over 15 frames. The trail is tinted brighter (A0 = 1.25) and zoomed.
            h.Presentation.Play(1, shared, shared.Routines["blon"], Vector3.Zero);
            h.Run(40);
            var blurred = h.Capture();
            Assert.True(h.Presentation.Blur.IsActive);

            h.Presentation.Play(2, shared, shared.Routines["blof"], Vector3.Zero);
            h.Run(40);
            var cleared = h.Capture();
            Assert.False(h.Presentation.Blur.IsActive);

            _out.WriteLine($"brightness plain {Brightness(plain):F1}, blurred {Brightness(blurred):F1}, cleared {Brightness(cleared):F1}; " +
                           $"blurred vs plain {Difference(blurred, plain):F2}, cleared vs plain {Difference(cleared, plain):F2}");
            Assert.True(Difference(blurred, plain) > 2, "the blur should change the picture");
            Assert.True(Brightness(blurred) > Brightness(plain), "the A0 tint brightens the trail");
            Assert.True(Difference(cleared, plain) < 0.5, "blof should leave the scene as drawn");
        }
    }
}
