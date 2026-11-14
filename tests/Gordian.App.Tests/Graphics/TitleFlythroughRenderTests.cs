// tests/Gordian.App.Tests/Graphics/TitleFlythroughRenderTests.cs
using System;
using System.IO;
using System.Runtime.InteropServices;
using Gordian.App.Graphics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Events;
using Gordian.Core.Resources.Tables;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Graphics;
using Gordian.Engine.Ui.Lobby;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// The title fly-through (#552) drawn offscreen on D3D11 behind the title menu: the opening segment (12, North
    /// Gustaberg) at several of its shots, lit by the zone's environment at the segment's hour and weather, with the lobby's
    /// art and buttons over it. Writes lobby_title_*.png when GORDIAN_UI_DUMP is set. Skipped off Windows or without the
    /// game install.
    /// </summary>
    [Collection(LobbyRenderCollection.Name)]
    public class TitleFlythroughRenderTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int x, int y,
            int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hwnd);

        [Fact]
        public void DrawsTheOpeningSegmentBehindTheTitleMenu()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.LoadLobby(rm);
            var scene = TitleSceneResource.Parse(rm.LoadDatBytesByFileId(TitleSceneResource.FileId));
            if (library == null || scene == null) return;
            var title = new TitleFlythrough(scene, zone => rm.TryResolveFile(ZoneDataLoader.GetZoneModelFileId(zone), out _), new Random(1));
            Assert.Equal(TitleSceneResource.OpeningSegmentNumber, title.Segment!.Number);
            Assert.True(rm.TryLoadZone(title.ZoneId, out var zone, out var textures));

            var lobby = new LobbyController(new LobbyRenderTests.FakeBackend(), library, LobbyTextTables.Load(rm.LoadDatBytes), showLicence: false);
            const uint width = 1280, height = 720;
            IntPtr hwnd = CreateWindowExW(0, "static", "TitleFlythroughTest", unchecked((int)0x80000000), 0, 0, (int)width, (int)height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devices = new VeldridDeviceManager();
            devices.Initialize(NeoVeldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), width, height, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devices.Device;
            if (gd == null) { DestroyWindow(hwnd); return; }
            try
            {
                var format = gd.SwapchainFramebuffer.ColorTargets[0].Target.Format;
                var color = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(width, height, 1, 1, format, NeoVeldrid.TextureUsage.RenderTarget | NeoVeldrid.TextureUsage.Sampled));
                var depth = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(width, height, 1, 1, NeoVeldrid.PixelFormat.R32_Float, NeoVeldrid.TextureUsage.DepthStencil));
                var framebuffer = gd.ResourceFactory.CreateFramebuffer(new NeoVeldrid.FramebufferDescription(depth, color));
                using var renderer = new ZoneTerrainRenderer(gd);
                renderer.LoadZone(zone, textures);
                renderer.EventPresentation = title.Presentation;
                using var frames = new LobbyFrameRenderer(gd, framebuffer.OutputDescription);
                var camera = new ViewportCamera();
                var environment = ZoneEnvironmentSettings.CreateDay();
                string? dumpDir = Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP");

                title.Update(0, zoneShown: true);
                Assert.False(title.IsWaitingForZone);
                int frame = 0;
                foreach (int shot in new[] { 400, 2100, 3000, 6000, 9600 })
                {
                    for (; frame < shot; frame++) title.Update(1 / 60.0, zoneShown: true);
                    Assert.Equal(TitleSceneResource.OpeningSegmentNumber, title.Segment!.Number);
                    var keyframe = zone.EnvironmentData?.Interpolate(title.Hour, title.Weather);
                    if (keyframe != null) environment.ApplyKeyframe(keyframe);
                    environment.SetTimeOfDay(title.Hour);
                    environment.WeatherId = title.Weather;
                    renderer.SkyDomeRenderer?.UpdateDome(environment);
                    Assert.True(title.Presentation.TryGetCamera(out var pose));
                    VeldridViewportControl.PlaceTitleCamera(camera, pose, environment, width / (float)height);
                    renderer.Render(camera, environment, 1 / 60f, width, height, present: false, targetFramebuffer: framebuffer);
                    Assert.True(renderer.DrawCalls > 0);
                    frames.Render(lobby, new LobbyPreview(), null, rm, framebuffer, width, height, 1 / 60f, 1f,
                        new LobbySceneLayer(Flythrough: true, Drawn: true, Tint: title.Presentation.SceneColor));
                    var pixels = StockUiRendererTests.ReadBack(gd, color, width, height);
                    if (!string.IsNullOrEmpty(dumpDir))
                    {
                        Directory.CreateDirectory(dumpDir);
                        StockUiRendererTests.SavePng(Path.Combine(dumpDir, $"lobby_title_{shot}.png"), pixels, (int)width, (int)height);
                    }
                    // The scene shows through below the art: the bottom-left quarter is neither black nor one flat colour.
                    var (mean, spread) = Luminance(pixels, width, 0, height * 3 / 4, width / 4, height - 40);
                    Assert.True(mean > 10 && spread > 3, $"frame {shot}: mean {mean:F1}, spread {spread:F1}");
                }
                framebuffer.Dispose(); depth.Dispose(); color.Dispose();
            }
            finally
            {
                devices.Dispose();
                DestroyWindow(hwnd);
            }
        }

        /// <summary>The mean and standard deviation of the luminance in a rectangle of an RGBA image.</summary>
        internal static (double Mean, double Spread) Luminance(byte[] rgba, uint width, uint left, uint top, uint right, uint bottom)
        {
            double sum = 0, squares = 0;
            int count = 0;
            for (uint y = top; y < bottom; y++)
            {
                for (uint x = left; x < right; x++)
                {
                    int i = (int)((y * width + x) * 4);
                    double l = 0.299 * rgba[i] + 0.587 * rgba[i + 1] + 0.114 * rgba[i + 2];
                    sum += l;
                    squares += l * l;
                    count++;
                }
            }
            double mean = sum / count;
            return (mean, Math.Sqrt(Math.Max(0, squares / count - mean * mean)));
        }
    }
}
