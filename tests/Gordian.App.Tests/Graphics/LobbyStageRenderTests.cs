// tests/Gordian.App.Tests/Graphics/LobbyStageRenderTests.cs
using System;
using System.IO;
using System.Runtime.InteropServices;
using Gordian.App.Graphics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Tables;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Graphics;
using Gordian.Engine.Ui.Lobby;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// The lobby stage (<see cref="LobbyStage"/>, #552 round 2) drawn offscreen on D3D11 behind the character list, the delete
    /// list and creation, with the preview character standing on it and the windows over it, each at its screen's hour.
    /// Writes lobby_stage_*.png when GORDIAN_UI_DUMP is set. Skipped off Windows or without the game install.
    /// </summary>
    [Collection(LobbyRenderCollection.Name)]
    public class LobbyStageRenderTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int x, int y,
            int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hwnd);

        /// <summary>
        /// The title screen at the retail capture's size (2558 x 1440): the layout stretched over the window (Select
        /// Character from x 885, the banners at x 1920-2400 from y 150), the selected button in orange, the banners of
        /// expansions the account lacks dimmed, the help line centred in its bar. Writes lobby_title_1440.png when
        /// GORDIAN_UI_DUMP is set.
        /// </summary>
        [Fact]
        public void DrawsTheTitleScreenStretchedWithTheExpansionBanners()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.LoadLobby(rm);
            if (library == null) return;
            // Every expansion through A Shantotto Ascension (bits 1-7); none of the Abyssea ones nor Seekers.
            var backend = new LobbyRenderTests.FakeBackend { Expansions = 0x0FF };
            backend.List.Add(LobbyRenderTests.Character(1, "Knot", 6, 2, 10, 44));
            var lobby = new LobbyController(backend, library, LobbyTextTables.Load(rm.LoadDatBytes), showLicence: false);

            const uint width = 2558, height = 1440;
            IntPtr hwnd = CreateWindowExW(0, "static", "LobbyTitleTest", unchecked((int)0x80000000), 0, 0, (int)width, (int)height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
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
                using var frames = new LobbyFrameRenderer(gd, framebuffer.OutputDescription);
                for (int i = 0; i < 3; i++) frames.Render(lobby, new LobbyPreview(), null, rm, framebuffer, width, height, 1 / 60f);
                var pixels = StockUiRendererTests.ReadBack(gd, color, width, height);
                string? dumpDir = Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP");
                if (!string.IsNullOrEmpty(dumpDir))
                {
                    Directory.CreateDirectory(dumpDir);
                    StockUiRendererTests.SavePng(Path.Combine(dumpDir, "lobby_title_1440.png"), pixels, (int)width, (int)height);
                }

                // Banner n spans y 80 + 46n .. 118 + 46n in the 1024 x 768 layout, x 768-960.
                double Banner(int n)
                {
                    uint top = (uint)((80 + 46 * n) * height / 768f) + 4, bottom = (uint)((118 + 46 * n) * height / 768f) - 4;
                    return TitleFlythroughRenderTests.Luminance(pixels, width, (uint)(768 * width / 1024f) + 8, top, (uint)(960 * width / 1024f) - 8, bottom).Mean;
                }
                Assert.True(Banner(0) > 60, $"Rise of the Zilart: {Banner(0):F1}");
                Assert.True(Banner(8) < Banner(0) * 0.6, $"Scars of Abyssea {Banner(8):F1} vs Rise of the Zilart {Banner(0):F1}");
                // Select Character (354, 443, 143 x 24 in the layout) is selected: its capsule is orange, not the others' blue.
                int RedMinusBlue(float layoutX, float layoutY)
                {
                    int o = ((int)(layoutY * height / 768f) * (int)width + (int)(layoutX * width / 1024f)) * 4;
                    return pixels[o] - pixels[o + 2];
                }
                Assert.True(RedMinusBlue(360, 455) > 30, $"Select Character: red - blue {RedMinusBlue(360, 455)}");
                Assert.True(RedMinusBlue(360, 483) < 0, $"Create Character: red - blue {RedMinusBlue(360, 483)}");
                framebuffer.Dispose(); depth.Dispose(); color.Dispose();
            }
            finally
            {
                devices.Dispose();
                DestroyWindow(hwnd);
            }
        }

        [Fact]
        public void DrawsTheStageBehindSelectDeleteAndCreation()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.LoadLobby(rm);
            if (library == null || !rm.TryLoadZone(LobbyStage.ZoneId, out var zone, out var textures)) return;
            Assert.NotNull(zone.EnvironmentData);

            var backend = new LobbyRenderTests.FakeBackend();
            backend.List.Add(LobbyRenderTests.Character(1, "Knot", 6, 2, 10, 44));
            backend.List.Add(LobbyRenderTests.Free(2));
            var lobby = new LobbyController(backend, library, LobbyTextTables.Load(rm.LoadDatBytes), showLicence: false);

            const uint width = 1280, height = 720;
            IntPtr hwnd = CreateWindowExW(0, "static", "LobbyStageTest", unchecked((int)0x80000000), 0, 0, (int)width, (int)height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
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
                using var frames = new LobbyFrameRenderer(gd, framebuffer.OutputDescription);
                var entities = new EntityRenderer(gd);
                var preview = new LobbyPreview();
                var camera = new ViewportCamera();
                var environment = ZoneEnvironmentSettings.CreateDay();
                string? dumpDir = Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP");
                if (!string.IsNullOrEmpty(dumpDir)) Directory.CreateDirectory(dumpDir);
                string? hours = Environment.GetEnvironmentVariable("GORDIAN_STAGE_HOURS");

                void Frame(string name, float? hourOverride = null)
                {
                    Assert.True(LobbyStage.Shows(lobby));
                    float hour = hourOverride ?? LobbyStage.HourOf(lobby);
                    var keyframe = zone.EnvironmentData!.Interpolate(hour, LobbyStage.Weather);
                    if (keyframe != null) environment.ApplyKeyframe(keyframe);
                    environment.SetTimeOfDay(hour);
                    environment.WeatherId = LobbyStage.Weather;
                    renderer.SkyDomeRenderer?.UpdateDome(environment);
                    VeldridViewportControl.PlaceStageCamera(camera, lobby, environment, width, height);
                    for (int i = 0; i < 3; i++)
                    {
                        renderer.Render(camera, environment, 1 / 60f, width, height, present: false, targetFramebuffer: framebuffer);
                        frames.Render(lobby, preview, entities, rm, framebuffer, width, height, 1 / 60f, 1f,
                            new LobbySceneLayer(Flythrough: true, Drawn: true, Tint: System.Numerics.Vector3.One));
                    }
                    Assert.True(renderer.DrawCalls > 0);
                    var pixels = StockUiRendererTests.ReadBack(gd, color, width, height);
                    if (!string.IsNullOrEmpty(dumpDir)) StockUiRendererTests.SavePng(Path.Combine(dumpDir, name), pixels, (int)width, (int)height);
                    // The stage shows across the bottom of the screen: neither black nor one flat colour.
                    var (mean, spread) = TitleFlythroughRenderTests.Luminance(pixels, width, 0, height * 2 / 3, width, height - 40);
                    Assert.True(mean > 10 && spread > 3, $"{name}: mean {mean:F1}, spread {spread:F1}");
                }

                lobby.HandleInput(LobbyInput.Confirm); // Select Character
                Assert.Equal(LobbyScreen.CharacterList, lobby.Screen);
                Frame("lobby_stage_list.png");
                if (hours != null)
                {
                    foreach (string h in hours.Split(',')) Frame($"lobby_stage_list_{h}.png", float.Parse(h));
                }
                lobby.HandleInput(LobbyInput.Cancel);
                lobby.HandleInput(LobbyInput.Down);
                lobby.HandleInput(LobbyInput.Down);
                lobby.HandleInput(LobbyInput.Confirm); // Delete Character
                Assert.True(lobby.IsDeleteList);
                Frame("lobby_stage_delete.png");
                lobby.HandleInput(LobbyInput.Cancel);
                lobby.HandleInput(LobbyInput.Up);
                lobby.HandleInput(LobbyInput.Confirm); // Create Character
                Assert.Equal(LobbyScreen.Creation, lobby.Screen);
                Frame("lobby_stage_creation.png");
                framebuffer.Dispose(); depth.Dispose(); color.Dispose();
            }
            finally
            {
                devices.Dispose();
                DestroyWindow(hwnd);
            }
        }
    }
}
