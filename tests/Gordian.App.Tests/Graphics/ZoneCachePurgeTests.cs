// tests/Gordian.App.Tests/Graphics/ZoneCachePurgeTests.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Gordian.App.Graphics;
using Gordian.Core.Network;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// #576: which zones count as in use for the zone cache purge, and the GPU zone cache's side of it: a zone's CPU copy
    /// is released after its upload, the purge drops GPU copies no window holds, and a released copy cannot be uploaded again.
    /// </summary>
    public class ZoneCachePurgeTests
    {
        [Fact]
        public void CharacterZones_CountEverySessionNotLoggedOut_WhetherOrNotDrawn()
        {
            var zones = ZoneCachePurger.CharacterZones(new (SessionState, int, int, int)[]
            {
                (SessionState.ActiveInWorld, 230, 230, 230),       // the character on screen
                (SessionState.ActiveInWorld, 245, 245, 245),       // a multi-box character no window draws
                (SessionState.LoadingWorldData, 100, 100, 100),    // zoning in: its new zone is already named
                (SessionState.ActiveInWorld, 231, 5001, 231),      // in its Mog House: the city and the room's key
                (SessionState.ActiveInWorld, 238, 238, 239),       // an event showing another zone
                (SessionState.Disconnected, 102, 102, 102),        // logged out: no longer counts
                (SessionState.CharacterSelection, 0, 0, 0),        // in the lobby: no zone
            });
            Assert.Equal(new HashSet<int> { 230, 245, 100, 231, 5001, 238, 239 }, zones);
        }

        [Fact]
        public void ResidencyCache_ReleasesTheCpuCopy_DropsUnheldZones_AndRefusesReleasedCopies()
        {
            if (!OperatingSystem.IsWindows()) return;
            IntPtr hwnd = HiddenWindow();
            var devices = new VeldridDeviceManager();
            try
            {
                devices.Initialize(NeoVeldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), 320, 240, GraphicsBackendPreference.Direct3D11, vsync: false);
                var gd = devices.Device!;
                using var shared = new GpuSharedResources(gd);
                shared.Zones.ReleaseCpuCopies = true;
                var renderer = new ZoneTerrainRenderer(shared, gd.SwapchainFramebuffer.OutputDescription);
                Assert.Contains(renderer, shared.Renderers);
                var a = SyntheticZone(7);
                var b = SyntheticZone(8);

                renderer.LoadZone(a.Zone, a.Textures);
                Assert.True(a.Zone.IsRenderDataReleased);
                Assert.True(a.Textures["tex"].PixelsReleased);
                Assert.NotNull(a.Zone.MeshGroups[0].RayPositions);
                Assert.Equal(new[] { 7 }, shared.Zones.ZonesInUse);

                // Draws from the GPU copy with the CPU copy gone.
                var camera = new Gordian.Engine.Graphics.ViewportCamera();
                camera.Update(Vector3.Zero, 30f, 0f, 10f, 4f / 3f);
                renderer.Render(camera, Gordian.Engine.Graphics.ZoneEnvironmentSettings.CreateDay(), 1 / 60f, 320, 240, present: false);

                // Switch to B: A stays resident (budget) but no window holds it, so the purge may drop it.
                renderer.LoadZone(b.Zone, b.Textures);
                Assert.Equal(new[] { 8 }, shared.Zones.ZonesInUse);
                Assert.Equal(0, shared.Zones.Drop([b.Zone]));   // shown: kept
                Assert.Equal(1, shared.Zones.Drop([a.Zone]));
                Assert.False(shared.Zones.IsResident(a.Zone));
                Assert.Equal(1, shared.ZoneTextures.UploadCount);

                // A released copy no longer resident cannot be uploaded again: it is read from its DAT anew.
                Assert.Throws<InvalidOperationException>(() => shared.Zones.Acquire(a.Zone, a.Textures));

                renderer.Dispose();
                Assert.DoesNotContain(renderer, shared.Renderers);
            }
            finally
            {
                devices.Dispose();
                DestroyWindow(hwnd);
            }
        }

        [Fact]
        public void ResidencyCache_KeepsTheCpuCopy_OnAPrivateDevice()
        {
            if (!OperatingSystem.IsWindows()) return;
            IntPtr hwnd = HiddenWindow();
            var devices = new VeldridDeviceManager();
            try
            {
                devices.Initialize(NeoVeldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), 64, 64, GraphicsBackendPreference.Direct3D11, vsync: false);
                using var shared = new GpuSharedResources(devices.Device!);
                var zone = SyntheticZone(9);
                var resident = shared.Zones.Acquire(zone.Zone, zone.Textures);
                Assert.False(zone.Zone.IsRenderDataReleased);
                Assert.False(zone.Textures["tex"].PixelsReleased);
                shared.Zones.Release(resident);
            }
            finally
            {
                devices.Dispose();
                DestroyWindow(hwnd);
            }
        }

        private static (ZoneGeometry Zone, Dictionary<string, DecodedTexture> Textures) SyntheticZone(int zoneId)
        {
            var zone = new ZoneGeometry { ZoneId = zoneId, DatSource = $"zone{zoneId}" };
            zone.MeshGroups.Add(new MeshGroup
            {
                Name = "floor",
                TextureName = "tex",
                Vertices =
                [
                    new MeshVertex(new Vector3(-5, 0, -5), Vector3.UnitY, Vector2.Zero, 0xFF808080),
                    new MeshVertex(new Vector3(5, 0, -5), Vector3.UnitY, Vector2.UnitX, 0xFF808080),
                    new MeshVertex(new Vector3(5, 0, 5), Vector3.UnitY, Vector2.One, 0xFF808080),
                    new MeshVertex(new Vector3(-5, 0, 5), Vector3.UnitY, Vector2.UnitY, 0xFF808080),
                ],
                Indices = [0, 1, 2, 0, 2, 3],
                MinBounds = new Vector3(-5, -1, -5),
                MaxBounds = new Vector3(5, 1, 5),
            });
            var texture = new DecodedTexture("tex", 4, 4, new byte[4 * 4 * 4]) { Source = $"zone{zoneId}@64" };
            return (zone, new Dictionary<string, DecodedTexture> { ["tex"] = texture });
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hwnd);

        private static IntPtr HiddenWindow() =>
            CreateWindowExW(0, "static", "Test", unchecked((int)0x80000000), 0, 0, 320, 240, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
    }
}
