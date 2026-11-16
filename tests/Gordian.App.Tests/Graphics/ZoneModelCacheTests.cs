// tests/Gordian.App.Tests/Graphics/ZoneModelCacheTests.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Gordian.App.Graphics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Xunit;
using Kind = Gordian.App.Graphics.ZoneModelCache.ModelKind;
using Key = Gordian.App.Graphics.ZoneModelCache.ModelKey;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// #585: ship models, building interiors and Mog House furniture have one GPU copy per device, shared by every window
    /// (one upload however many hold it), their CPU copy is released after the upload on the app's device, a released copy
    /// gone from the GPU is refused (read again), and the zone cache purge frees the copies no window holds.
    /// Offscreen on D3D11; skipped off Windows.
    /// </summary>
    public class ZoneModelCacheTests
    {
        [Fact]
        public void TwoWindows_ShareOneUpload_AndTheCpuCopyGoesAfterIt()
        {
            WithDevice(shared =>
            {
                shared.ZoneModels.ReleaseCpuCopies = true;
                var (geometry, textures) = Interior("file500");
                var key = new Key(Kind.Interior, 42, 0);

                var first = shared.ZoneModels.Acquire(key, 230, geometry.MeshGroups, textures, ownLightSlots: false, releaseCpu: () => geometry.ReleaseRenderData(textures, keepRayMesh: false));
                Assert.NotNull(first);
                Assert.True(geometry.IsRenderDataReleased);
                Assert.True(textures["tex"].PixelsReleased);
                Assert.Null(geometry.MeshGroups[0].RayPositions);

                // A second window: the resident copy, no upload, whatever the state of the CPU copy.
                Assert.True(shared.ZoneModels.TryAcquire(key, out var second));
                Assert.Same(first, second);
                Assert.Same(first, shared.ZoneModels.Acquire(key, 230, geometry.MeshGroups, textures));
                Assert.Equal(1, shared.ZoneModels.Uploads);
                Assert.Equal(1, shared.ZoneModels.Count);
                Assert.Single(first!.Submeshes);
                Assert.Null(first.Submeshes[0].LightSet);   // an interior's light slots are each window's
                Assert.Equal(new[] { 3 }, first.PointLightSlots[0]);
                Assert.True(first.Bytes > 0);

                // A held interior keeps its zone in use; it is not dropped while held, even when its interior is purged.
                Assert.Equal(new[] { 230 }, shared.ZoneModels.ZonesInUse);
                Assert.Equal(0, shared.ZoneModels.Purge([230], [42], idleSeconds: 0));
                shared.ZoneModels.Release(first);
                shared.ZoneModels.Release(second);
                shared.ZoneModels.Release(first);
                Assert.Empty(shared.ZoneModels.ZonesInUse);

                // Unheld: an interior stays until its interior is purged, whatever the idle time.
                Assert.Equal(0, shared.ZoneModels.Purge([230], [], idleSeconds: 0));
                Assert.Equal(1, shared.ZoneModels.Purge([], [42]));
                Assert.False(shared.ZoneModels.IsResident(key));
                Assert.Equal(0, shared.ZoneTextures.UploadCount);

                // Released and gone from the GPU: refused, so the window reads the interior again.
                Assert.Null(shared.ZoneModels.Acquire(key, 230, geometry.MeshGroups, textures));
                var (reread, rereadTextures) = Interior("file500");
                var again = shared.ZoneModels.Acquire(key, 230, reread.MeshGroups, rereadTextures);
                Assert.NotNull(again);
                Assert.Equal(2, shared.ZoneModels.Uploads);
                shared.ZoneModels.Release(again);
            });
        }

        [Fact]
        public void Purge_DropsShipsOfDroppedZones_AndIdleShipsAndFurniture_NeverHeldOnes()
        {
            WithDevice(shared =>
            {
                var (ship, shipTextures) = Interior("file11");
                var (bed, bedTextures) = Interior("file33");
                var (chair, chairTextures) = Interior("file34");
                var shipKey = new Key(Kind.Vessel, 1, 0);
                var bedKey = new Key(Kind.Furniture, 7, 0);
                var chairKey = new Key(Kind.Furniture, 8, 0);
                var heldShip = shared.ZoneModels.Acquire(shipKey, 1, ship.MeshGroups, shipTextures);
                var heldBed = shared.ZoneModels.Acquire(bedKey, 0, bed.MeshGroups, bedTextures);
                shared.ZoneModels.Release(shared.ZoneModels.Acquire(chairKey, 0, chair.MeshGroups, chairTextures));
                Assert.NotNull(heldShip!.Submeshes[0].LightSet);   // its own light slots
                // A held ship model does not keep its ship zone: it was read from a copy of its own.
                Assert.Empty(shared.ZoneModels.ZonesInUse);

                // Not idle yet: nothing goes but what a dropped zone owns, and that only once unheld.
                Assert.Equal(0, shared.ZoneModels.Purge([1], []));
                Assert.Equal(1, shared.ZoneModels.Purge([], [], idleSeconds: 0));   // the unheld chair
                Assert.False(shared.ZoneModels.IsResident(chairKey));

                shared.ZoneModels.Release(heldShip);
                Assert.Equal(1, shared.ZoneModels.Purge([1], []));
                Assert.False(shared.ZoneModels.IsResident(shipKey));
                Assert.True(shared.ZoneModels.IsResident(bedKey));
                shared.ZoneModels.Release(heldBed);
                Assert.Equal(0, shared.ZoneModels.Purge([], []));   // not idle for a minute yet
                Assert.Equal(1, shared.ZoneModels.Purge([], [], idleSeconds: 0));
                Assert.Equal(0, shared.ZoneModels.Count);
            });
        }

        [Fact]
        public void ZoneLeavingTheGpu_KeepsTheTextureUploadsAModelDrawsWith_AndBackAgain()
        {
            WithDevice(shared =>
            {
                shared.Zones.ReleaseCpuCopies = true;
                shared.ZoneModels.ReleaseCpuCopies = true;
                // The voyage zone and a ship model read from a copy of its own: both name the same texture source.
                var (zone, zoneTextures) = Interior("file100");
                var voyage = new ZoneGeometry { ZoneId = 1, DatSource = "file100" };
                voyage.MeshGroups.AddRange(zone.MeshGroups);
                var (vessel, vesselTextures) = Interior("file100");
                var resident = shared.Zones.Acquire(voyage, zoneTextures);
                var ship = shared.ZoneModels.Acquire(new Key(Kind.Vessel, 1, 0), 1, vessel.MeshGroups, vesselTextures);
                Assert.Equal(1, shared.ZoneTextures.UploadCount);   // shared by source

                shared.Zones.Release(resident);
                Assert.Equal(1, shared.Zones.Drop([voyage]));
                Assert.Equal(1, shared.ZoneTextures.UploadCount);   // the ship still draws with it

                shared.ZoneModels.Release(ship);
                Assert.Equal(1, shared.ZoneModels.Purge([1], []));
                Assert.Equal(0, shared.ZoneTextures.UploadCount);
            });
        }

        [Fact]
        public void PrivateDevice_KeepsTheCpuCopy_AndAVfsReloadDropsStaleCopiesOnceUnheld()
        {
            WithDevice(shared =>
            {
                var (bed, bedTextures) = Interior("file33");
                var model = new FurnitureModel(7, bed.MeshGroups, bedTextures);
                var old = shared.ZoneModels.Acquire(new Key(Kind.Furniture, 7, 0), 0, model.Meshes, model.Textures, releaseCpu: model.ReleaseRenderData);
                Assert.False(model.IsRenderDataReleased);
                Assert.False(bedTextures["tex"].PixelsReleased);

                // Read again after a VFS reload (generation 1): the old copy goes once no window holds it.
                var (newBed, newTextures) = Interior("file33");
                var fresh = shared.ZoneModels.Acquire(new Key(Kind.Furniture, 7, 1), 0, newBed.MeshGroups, newTextures);
                Assert.Equal(2, shared.ZoneModels.Count);
                shared.ZoneModels.Release(old);
                Assert.Equal(new[] { new Key(Kind.Furniture, 7, 1) }, shared.ZoneModels.Keys);
                shared.ZoneModels.Release(fresh);
            });
        }

        [Fact]
        public void TwoRenderers_DrawFurnitureFromOneUpload()
        {
            WithDevice(shared =>
            {
                shared.ZoneModels.ReleaseCpuCopies = true;
                var gd = shared.Device;
                var output = gd.SwapchainFramebuffer.OutputDescription;
                using var first = new ZoneTerrainRenderer(shared, output);
                using var second = new ZoneTerrainRenderer(shared, output);
                var (bed, bedTextures) = Interior("file33");
                var model = new FurnitureModel(7, bed.MeshGroups, bedTextures);
                var furniture = new[] { new Gordian.App.Graphics.FurnitureInstance(model, Matrix4x4.Identity) };
                var camera = new Gordian.Engine.Graphics.ViewportCamera();
                camera.Update(Vector3.Zero, 30f, 0f, 10f, 4f / 3f);
                var room = Zone(5);
                foreach (var renderer in new[] { first, second })
                {
                    renderer.LoadZone(room.Geometry, room.Textures);
                    renderer.Furniture = furniture;
                    renderer.Render(camera, Gordian.Engine.Graphics.ZoneEnvironmentSettings.CreateDay(), 1 / 60f, 320, 240, present: false);
                }
                Assert.Equal(1, shared.ZoneModels.Uploads);
                Assert.True(model.IsRenderDataReleased);

                // A window switching zone gives its reference back; the other still holds the model.
                var street = Zone(6);
                first.LoadZone(street.Geometry, street.Textures);
                Assert.Equal(0, shared.ZoneModels.Purge([], [], idleSeconds: 0));
                second.Dispose();
                Assert.Equal(1, shared.ZoneModels.Purge([], [], idleSeconds: 0));
            });
        }

        private static (ZoneGeometry Geometry, Dictionary<string, DecodedTexture> Textures) Interior(string datSource)
        {
            var geometry = new ZoneGeometry { DatSource = datSource };
            geometry.MeshGroups.Add(new MeshGroup
            {
                Name = "wall",
                TextureName = "tex",
                PointLightSlots = [3],
                Vertices =
                [
                    new MeshVertex(new Vector3(-1, 0, -1), Vector3.UnitY, Vector2.Zero, 0xFF808080),
                    new MeshVertex(new Vector3(1, 0, -1), Vector3.UnitY, Vector2.UnitX, 0xFF808080),
                    new MeshVertex(new Vector3(1, 0, 1), Vector3.UnitY, Vector2.One, 0xFF808080),
                ],
                Indices = [0, 1, 2],
                MinBounds = new Vector3(-1, 0, -1),
                MaxBounds = new Vector3(1, 0, 1),
            });
            var texture = new DecodedTexture("tex", 4, 4, new byte[4 * 4 * 4]) { Source = datSource + "@64" };
            return (geometry, new Dictionary<string, DecodedTexture>(StringComparer.OrdinalIgnoreCase) { ["tex"] = texture });
        }

        private static (ZoneGeometry Geometry, Dictionary<string, DecodedTexture> Textures) Zone(int zoneId)
        {
            var (parts, textures) = Interior($"file{zoneId}");
            var zone = new ZoneGeometry { ZoneId = zoneId, DatSource = $"file{zoneId}" };
            zone.MeshGroups.AddRange(parts.MeshGroups);
            return (zone, textures);
        }

        private static void WithDevice(Action<GpuSharedResources> test)
        {
            if (!OperatingSystem.IsWindows()) return;
            IntPtr hwnd = CreateWindowExW(0, "static", "Test", unchecked((int)0x80000000), 0, 0, 320, 240, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devices = new VeldridDeviceManager();
            try
            {
                devices.Initialize(NeoVeldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), 320, 240, GraphicsBackendPreference.Direct3D11, vsync: false);
                using var shared = new GpuSharedResources(devices.Device!);
                test(shared);
            }
            finally
            {
                devices.Dispose();
                DestroyWindow(hwnd);
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hwnd);
    }
}
