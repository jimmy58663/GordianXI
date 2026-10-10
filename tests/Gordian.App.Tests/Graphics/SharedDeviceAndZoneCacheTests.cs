// tests/Gordian.App.Tests/Graphics/SharedDeviceAndZoneCacheTests.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Gordian.App.Graphics;
using Gordian.App.Services;
using Gordian.Core.Graphics;
using Gordian.Core.Network;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// The shared graphics device (#300), viewport render throttling (#301) and the GPU zone cache (#322).
    /// </summary>
    public class SharedDeviceAndZoneCacheTests
    {
        private static ZoneResidencyCache.Candidate Zone(long mb, int refs = 0, bool pinned = false, bool stale = false, long lastUse = 0) =>
            new(mb << 20, refs, pinned, stale, lastUse);

        [Fact]
        public void SelectVictims_KeepsEverythingUnderBudget()
        {
            var zones = new[] { Zone(100, lastUse: 1), Zone(100, lastUse: 2) };
            Assert.Empty(ZoneResidencyCache.SelectVictims(zones, 1024L << 20, 12));
        }

        [Fact]
        public void SelectVictims_DropsLeastRecentlyUsedUnpinnedFirst_NeverAZoneOnScreen()
        {
            var zones = new[]
            {
                Zone(400, refs: 1, lastUse: 1),        // on screen: never dropped
                Zone(400, pinned: true, lastUse: 2),   // a character is in it: kept before unpinned zones
                Zone(400, lastUse: 3),                 // older unpinned
                Zone(400, lastUse: 4),                 // newer unpinned
            };
            // 1600 MB resident, 1000 MB budget: dropping the two unpinned zones is enough.
            Assert.Equal(new[] { 2, 3 }, ZoneResidencyCache.SelectVictims(zones, 1000L << 20, 12));
            // 500 MB budget: the pinned zone goes too, the one on screen stays.
            Assert.Equal(new[] { 2, 3, 1 }, ZoneResidencyCache.SelectVictims(zones, 500L << 20, 12));
            // Budget 0 keeps only the zone on screen.
            Assert.Equal(new[] { 2, 3, 1 }, ZoneResidencyCache.SelectVictims(zones, 0, 12));
        }

        [Fact]
        public void SelectVictims_DropsStaleZonesAtOnce_AndHonoursTheZoneCount()
        {
            var zones = new[] { Zone(10, stale: true, lastUse: 5), Zone(10, refs: 1, stale: true, lastUse: 1), Zone(10, lastUse: 2), Zone(10, lastUse: 3) };
            // The unreferenced stale zone goes even under budget; one shown is kept until released.
            Assert.Equal(new[] { 0 }, ZoneResidencyCache.SelectVictims(zones, 1024L << 20, 12));
            // At most two zones: the stale one, then the least recently used unreferenced one.
            Assert.Equal(new[] { 0, 2 }, ZoneResidencyCache.SelectVictims(zones, 1024L << 20, 2));
        }

        [Fact]
        public void SessionZones_AreTheDistinctZonesOfCharactersInTheWorld()
        {
            var zones = ZoneResidencyCache.SessionZones(new (SessionState, int)[]
            {
                (SessionState.ActiveInWorld, 230),
                (SessionState.ActiveInWorld, 245),
                (SessionState.ActiveInWorld, 230),
                (SessionState.LoadingWorldData, 100),
                (SessionState.ActiveInWorld, 0),
            });
            Assert.Equal(new[] { 230, 245 }, zones);
        }

        [Fact]
        public void RenderPolicy_FocusTargetDrawsFull_OthersBackground_MinimisedOrHiddenPaused()
        {
            Assert.Equal(ViewportRenderMode.Full, ViewportRenderPolicy.Resolve(minimized: false, shown: true, isFocusTarget: true));
            Assert.Equal(ViewportRenderMode.Background, ViewportRenderPolicy.Resolve(minimized: false, shown: true, isFocusTarget: false));
            Assert.Equal(ViewportRenderMode.Paused, ViewportRenderPolicy.Resolve(minimized: true, shown: true, isFocusTarget: true));
            Assert.Equal(ViewportRenderMode.Paused, ViewportRenderPolicy.Resolve(minimized: false, shown: false, isFocusTarget: false));
            Assert.Equal(TimeSpan.FromSeconds(1.0 / 30), ViewportRenderPolicy.FrameInterval(30));
            Assert.Equal(TimeSpan.FromSeconds(1.0), ViewportRenderPolicy.FrameInterval(0)); // clamped to 1 fps
        }

        [Fact]
        public void FocusTarget_IsTheFocusedViewport_OrTheLastFocusedWhileNoneIs()
        {
            var tracker = new InputFocusTracker();
            object main = new(), popOut = new();
            tracker.SetWindowSession(main, null);
            tracker.SetWindowSession(popOut, null);
            // Before any viewport has had the focus, every window draws at full rate.
            Assert.True(tracker.IsFocusTarget(main));
            Assert.True(tracker.IsFocusTarget(popOut));

            tracker.Activated(main);
            Assert.True(tracker.IsFocusTarget(main));
            Assert.False(tracker.IsFocusTarget(popOut));

            // The control panel or another program takes the focus: the main window stays the target.
            tracker.Deactivated(main);
            Assert.True(tracker.IsFocusTarget(main));
            Assert.False(tracker.IsFocusTarget(popOut));

            tracker.Activated(popOut);
            Assert.False(tracker.IsFocusTarget(main));
            Assert.True(tracker.IsFocusTarget(popOut));
        }

        [Fact]
        public void RenderSettings_ClampAndApply()
        {
            int rate = ViewportRenderSettings.BackgroundFrameRate;
            int budget = ViewportRenderSettings.ZoneCacheBudgetMb;
            bool vsync = ViewportRenderSettings.VsyncEnabled;
            try
            {
                ViewportRenderSettings.Apply(new ViewportSettings { BackgroundFrameRate = 0, ZoneCacheBudgetMb = -5, IsVsyncEnabled = false });
                Assert.Equal(1, ViewportRenderSettings.BackgroundFrameRate);
                Assert.Equal(0, ViewportRenderSettings.ZoneCacheBudgetMb);
                Assert.False(ViewportRenderSettings.VsyncEnabled);
                Assert.Equal(30, new ViewportSettings().BackgroundFrameRate);
                Assert.Equal(1536, new ViewportSettings().ZoneCacheBudgetMb);
            }
            finally
            {
                ViewportRenderSettings.BackgroundFrameRate = rate;
                ViewportRenderSettings.ZoneCacheBudgetMb = budget;
                ViewportRenderSettings.VsyncEnabled = vsync;
            }
        }

        // ---- GPU tests (Windows, Direct3D 11; no game data needed) ----

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hwnd);

        private static IntPtr HiddenWindow() =>
            CreateWindowExW(0, "static", "Test", unchecked((int)0x80000000), 0, 0, 320, 240, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        /// <summary>A one-quad zone with one texture, for residency tests.</summary>
        private static (ZoneGeometry Zone, Dictionary<string, DecodedTexture> Textures) SyntheticZone(int zoneId)
        {
            var zone = new ZoneGeometry { ZoneId = zoneId };
            zone.MeshGroups.Add(new MeshGroup
            {
                Name = "floor",
                TextureName = "tex",
                Vertices = new[]
                {
                    new MeshVertex(new Vector3(-5, 0, -5), Vector3.UnitY, Vector2.Zero, 0xFF808080),
                    new MeshVertex(new Vector3(5, 0, -5), Vector3.UnitY, Vector2.UnitX, 0xFF808080),
                    new MeshVertex(new Vector3(5, 0, 5), Vector3.UnitY, Vector2.One, 0xFF808080),
                    new MeshVertex(new Vector3(-5, 0, 5), Vector3.UnitY, Vector2.UnitY, 0xFF808080),
                },
                Indices = new[] { 0, 1, 2, 0, 2, 3 },
                MinBounds = new Vector3(-5, -1, -5),
                MaxBounds = new Vector3(5, 1, 5),
            });
            var texture = new DecodedTexture("tex", 4, 4, new byte[4 * 4 * 4]) { Source = $"zone{zoneId}@64" };
            return (zone, new Dictionary<string, DecodedTexture> { ["tex"] = texture });
        }

        [Fact]
        public void SharedDevice_TwoWindowsShareOneDevice_EachWithItsOwnSwapchain()
        {
            if (!OperatingSystem.IsWindows() || SharedGraphicsDevice.Current != null) return;
            IntPtr first = HiddenWindow(), second = HiddenWindow();
            var a = new VeldridDeviceManager();
            var b = new VeldridDeviceManager();
            try
            {
                a.InitializeShared(NeoVeldrid.SwapchainSource.CreateWin32(first, IntPtr.Zero), 320, 240, GraphicsBackendPreference.Direct3D11, vsync: false);
                b.InitializeShared(NeoVeldrid.SwapchainSource.CreateWin32(second, IntPtr.Zero), 200, 100, GraphicsBackendPreference.Direct3D11, vsync: false);
                Assert.Same(a.Device, b.Device);
                Assert.Same(a.GpuLock, b.GpuLock);
                Assert.NotSame(a.Swapchain, b.Swapchain);
                Assert.Null(a.Device!.MainSwapchain);

                var shared = a.SharedDevice!;
                var zone = SyntheticZone(9001);
                ZoneTerrainRenderer rendererA, rendererB;
                lock (shared.Lock)
                {
                    rendererA = new ZoneTerrainRenderer(shared.Resources, a.Framebuffer!.OutputDescription);
                    rendererB = new ZoneTerrainRenderer(shared.Resources, b.Framebuffer!.OutputDescription);
                }
                rendererA.LoadZone(zone.Zone, zone.Textures);
                rendererB.LoadZone(zone.Zone, zone.Textures);
                // One upload of the zone for both windows.
                Assert.Same(rendererA.ActiveResidentZone, rendererB.ActiveResidentZone);
                Assert.Equal(1, shared.Resources.Zones.Count);
                Assert.Equal(1, shared.Resources.ZoneTextures.UploadCount);

                var camera = new ViewportCamera();
                camera.Update(Vector3.Zero, 30f, 0f, 10f, 4f / 3f);
                var env = ZoneEnvironmentSettings.CreateDay();
                lock (shared.Lock)
                {
                    rendererA.Render(camera, env, 1 / 60f, 320, 240, present: false, targetFramebuffer: a.Framebuffer);
                    a.Present();
                    rendererB.Render(camera, env, 1 / 60f, 200, 100, present: false, targetFramebuffer: b.Framebuffer);
                    b.Present();
                    // A resize is this window's alone.
                    b.Resize(400, 300);
                    Assert.Equal(400u, b.Swapchain!.Framebuffer.Width);
                    Assert.Equal(320u, a.Swapchain!.Framebuffer.Width);
                    rendererA.Dispose();
                }

                a.Dispose();
                // The other window keeps the device.
                Assert.NotNull(SharedGraphicsDevice.Current);
                lock (shared.Lock)
                {
                    rendererB.Render(camera, env, 1 / 60f, 400, 300, present: false, targetFramebuffer: b.Framebuffer);
                    b.Present();
                    rendererB.Dispose();
                }
            }
            finally
            {
                a.Dispose();
                b.Dispose();
                DestroyWindow(first);
                DestroyWindow(second);
            }
            Assert.Null(SharedGraphicsDevice.Current);
        }

        [Fact]
        public void ZoneCache_SwitchBackReusesTheResidentZone_AndEvictsUnusedZonesOverBudget()
        {
            if (!OperatingSystem.IsWindows()) return;
            IntPtr hwnd = HiddenWindow();
            var devices = new VeldridDeviceManager();
            try
            {
                devices.Initialize(NeoVeldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), 320, 240, GraphicsBackendPreference.Direct3D11, vsync: false);
                var gd = devices.Device!;
                using var shared = new GpuSharedResources(gd);
                var outputs = gd.SwapchainFramebuffer.OutputDescription;
                var first = new ZoneTerrainRenderer(shared, outputs);
                var second = new ZoneTerrainRenderer(shared, outputs);
                var zoneA = SyntheticZone(1);
                var zoneB = SyntheticZone(2);

                first.LoadZone(zoneA.Zone, zoneA.Textures);
                var residentA = first.ActiveResidentZone!;
                first.LoadZone(zoneB.Zone, zoneB.Textures);
                Assert.Equal(2, shared.Zones.Count);
                Assert.Equal(2, shared.ZoneTextures.UploadCount);

                // Back to A: the same GPU copy, nothing uploaded again.
                first.LoadZone(zoneA.Zone, zoneA.Textures);
                Assert.Same(residentA, first.ActiveResidentZone);
                Assert.Equal(2, shared.ZoneTextures.UploadCount);

                // Budget 0 keeps only the zones on screen: B (shown nowhere) goes with its texture.
                shared.Zones.BudgetBytes = 0;
                shared.Zones.Trim();
                Assert.Equal(new[] { 1 }, shared.Zones.ResidentZoneIds);
                Assert.Equal(1, shared.ZoneTextures.UploadCount);

                // A zone shown in another window is not dropped when this one moves on.
                second.LoadZone(zoneA.Zone, zoneA.Textures);
                first.LoadZone(zoneB.Zone, zoneB.Textures);
                Assert.False(residentA.IsDisposed);
                second.LoadZone(null);
                Assert.True(residentA.IsDisposed);
                Assert.Equal(new[] { 2 }, shared.Zones.ResidentZoneIds);

                first.Dispose();
                second.Dispose();
                Assert.Equal(0, shared.Zones.Count);
            }
            finally
            {
                devices.Dispose();
                DestroyWindow(hwnd);
            }
        }

        [Fact]
        public void TextureCache_EvictFreesUploads_ExceptSourcesStillInUse()
        {
            if (!OperatingSystem.IsWindows()) return;
            IntPtr hwnd = HiddenWindow();
            var devices = new VeldridDeviceManager();
            try
            {
                devices.Initialize(NeoVeldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), 64, 64, GraphicsBackendPreference.Direct3D11, vsync: false);
                using var shared = new GpuSharedResources(devices.Device!);
                var cache = shared.ZoneTextures;
                var kept = new DecodedTexture("a", 2, 2, new byte[16]) { Source = "file1@0" };
                var dropped = new DecodedTexture("b", 2, 2, new byte[16]) { Source = "file1@32" };
                var reread = new DecodedTexture("b", 2, 2, new byte[16]) { Source = "file1@32" };
                var keptSet = cache.GetOrCreateResourceSet(kept);
                var droppedSet = cache.GetOrCreateResourceSet(dropped);
                Assert.Same(droppedSet, cache.GetOrCreateResourceSet(reread));
                Assert.Equal(2, cache.UploadCount);

                Assert.Equal(1, cache.Evict(new[] { kept, dropped }, new HashSet<string> { "file1@0" }));
                Assert.Equal(1, cache.UploadCount);
                Assert.Same(keptSet, cache.GetOrCreateResourceSet(kept));
                // The evicted source uploads again when next drawn (for any decoded instance of it).
                Assert.NotSame(droppedSet, cache.GetOrCreateResourceSet(reread));
                Assert.Equal(2, cache.UploadCount);
            }
            finally
            {
                devices.Dispose();
                DestroyWindow(hwnd);
            }
        }
    }
}
