// tests/Gordian.App.Tests/Graphics/LobbyRenderTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Gordian.App.Graphics;
using Gordian.Core.Network.LandSandBoat;
using Gordian.Core.Resources.Tables;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui.Lobby;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// The character lobby drawn offscreen on D3D11 from the retail lobby DAT: the title menu, the character list with
    /// the preview model, and a prompt. Writes lobby_*.png when GORDIAN_UI_DUMP is set. Skipped off Windows or without
    /// the game install.
    /// </summary>
    public class LobbyRenderTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int x, int y,
            int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hwnd);

        internal sealed class FakeBackend : ILobbyBackend
        {
            public List<LobbyCharacter> List { get; } = new();
            public IReadOnlyList<LobbyCharacter> Characters => List;
            public bool IsConnected => true;
            public Task<IReadOnlyList<LobbyCharacter>> RefreshCharactersAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<LobbyCharacter>>(List);
            public Task<IReadOnlyList<LobbyWorld>> GetWorldsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<LobbyWorld>>(new[] { new LobbyWorld(0x20, "Gordian") });
            public Task CheckNameAsync(LobbyCharacter freeSlot, string name, string worldName, CancellationToken ct = default) => Task.CompletedTask;
            public Task CreateCharacterAsync(LobbyCharacter freeSlot, LobbyCharacterCreation creation, CancellationToken ct = default) => Task.CompletedTask;
            public Task DeleteCharacterAsync(LobbyCharacter character, CancellationToken ct = default) => Task.CompletedTask;
            public Task<LsbSessionTicket> RenameAndSelectAsync(LobbyCharacter character, string newName, CancellationToken ct = default) => Task.FromResult(new LsbSessionTicket());
            public Task<LsbSessionTicket> SelectCharacterAsync(LobbyCharacter character, CancellationToken ct = default) => new TaskCompletionSource<LsbSessionTicket>().Task;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        internal static LobbyCharacter Character(int slot, string name, byte race, byte face, byte job, byte level, ushort body = 0x2000) =>
            new(slot, (uint)(1000 + slot), (uint)(1000 + slot), 0, 1, false, slot == 2, name, "Gordian", race, job, 0, level, face, 0, 1, 230,
                LobbyEquipment.From(new ushort[] { 0x1000, body, 0x3000, 0x4000, 0x5000, 0x6000, 0x7000 }), default);

        internal static LobbyCharacter Free(int slot) =>
            new(slot, 0, 0, 0, 1, false, false, string.Empty, string.Empty, 0, 0, 0, 0, 0, 0, 0, 0, default, default);

        /// <summary>
        /// The licence page at the retail capture's size (image-8, 2559 x 1439): the window at one screen pixel per layout
        /// pixel, its frame origin at (1280, 624) in a 512 x 448 box centred on the window, the backdrop stretched over the
        /// whole window. Writes
        /// lobby_licence_1440.png when GORDIAN_UI_DUMP is set.
        /// </summary>
        [Fact]
        public void RendersLicencePageLikeRetail()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.LoadLobby(rm);
            if (library == null) return;
            var backend = new FakeBackend();
            backend.List.Add(Character(1, "Knot", 2, 5, 4, 75));
            var lobby = new LobbyController(backend, library, LobbyTextTables.Load(rm.LoadDatBytes));
            Assert.True(library.TryGetMenu(LobbyController.LicencePromptMenu, out var licence));
            var placement = StockUiLobby.PlaceWindow(lobby, licence, StockUiLobby.Fit(2559, 1439), 2559, 1439);
            Assert.Equal(1f, placement.Scale);
            Assert.InRange(placement.X, 1278f, 1281f); // the capture: the window's centre at x 1279, its top at y 623
            Assert.InRange(placement.Y, 622f, 625f);

            const uint width = 2559, height = 1439;
            IntPtr hwnd = CreateWindowExW(0, "static", "LobbyLicenceTest", unchecked((int)0x80000000), 0, 0, (int)width, (int)height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devices = new VeldridDeviceManager();
            devices.Initialize(Veldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), width, height, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devices.Device;
            if (gd == null) { DestroyWindow(hwnd); return; }
            try
            {
                var format = gd.SwapchainFramebuffer.ColorTargets[0].Target.Format;
                var color = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(width, height, 1, 1, format, Veldrid.TextureUsage.RenderTarget | Veldrid.TextureUsage.Sampled));
                var depth = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(width, height, 1, 1, Veldrid.PixelFormat.R32_Float, Veldrid.TextureUsage.DepthStencil));
                var framebuffer = gd.ResourceFactory.CreateFramebuffer(new Veldrid.FramebufferDescription(depth, color));
                using var frames = new LobbyFrameRenderer(gd, framebuffer.OutputDescription);
                for (int i = 0; i < 3; i++) frames.Render(lobby, new LobbyPreview(), null, rm, framebuffer, width, height, 1 / 60f);
                var pixels = StockUiRendererTests.ReadBack(gd, color, width, height);
                string? dumpDir = Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP");
                if (!string.IsNullOrEmpty(dumpDir))
                {
                    Directory.CreateDirectory(dumpDir);
                    StockUiRendererTests.SavePng(Path.Combine(dumpDir, "lobby_licence_1440.png"), pixels, (int)width, (int)height);
                }
                framebuffer.Dispose(); depth.Dispose(); color.Dispose();
            }
            finally
            {
                devices.Dispose();
                DestroyWindow(hwnd);
            }
        }

        /// <summary>
        /// Character creation offscreen: the race window with the Mithra under the cursor (caption and model follow it),
        /// the nation step with its flag and description, the name field with typed text, the world list and the
        /// register prompt. Writes lobby_create_*.png when GORDIAN_UI_DUMP is set.
        /// </summary>
        [Fact]
        public void RendersCreationSteps()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.LoadLobby(rm);
            if (library == null) return;
            var text = LobbyTextTables.Load(rm.LoadDatBytes);
            var backend = new FakeBackend();
            backend.List.Add(Character(1, "Knot", 2, 5, 4, 75));
            backend.List.Add(Free(2));
            var lobby = new LobbyController(backend, library, text, showLicence: false);

            const uint width = 1280, height = 720;
            IntPtr hwnd = CreateWindowExW(0, "static", "LobbyCreateTest", unchecked((int)0x80000000), 0, 0, (int)width, (int)height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devices = new VeldridDeviceManager();
            devices.Initialize(Veldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), width, height, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devices.Device;
            if (gd == null) { DestroyWindow(hwnd); return; }
            try
            {
                var format = gd.SwapchainFramebuffer.ColorTargets[0].Target.Format;
                var color = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(width, height, 1, 1, format, Veldrid.TextureUsage.RenderTarget | Veldrid.TextureUsage.Sampled));
                var depth = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(width, height, 1, 1, Veldrid.PixelFormat.R32_Float, Veldrid.TextureUsage.DepthStencil));
                var framebuffer = gd.ResourceFactory.CreateFramebuffer(new Veldrid.FramebufferDescription(depth, color));
                using var frames = new LobbyFrameRenderer(gd, framebuffer.OutputDescription);
                var entities = new EntityRenderer(gd);
                var preview = new LobbyPreview();
                string? dumpDir = Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP");
                if (!string.IsNullOrEmpty(dumpDir)) Directory.CreateDirectory(dumpDir);
                void Frame(string name)
                {
                    for (int i = 0; i < 3; i++) frames.Render(lobby, preview, entities, rm, framebuffer, width, height, 1 / 60f);
                    var pixels = StockUiRendererTests.ReadBack(gd, color, width, height);
                    if (!string.IsNullOrEmpty(dumpDir)) StockUiRendererTests.SavePng(Path.Combine(dumpDir, name), pixels, (int)width, (int)height);
                }
                void WaitIdle() { for (int i = 0; i < 200 && lobby.IsBusy; i++) Thread.Sleep(10); }

                lobby.HandleInput(LobbyInput.Down);
                lobby.HandleInput(LobbyInput.Confirm); // Create Character
                for (int i = 0; i < 6; i++) lobby.HandleInput(LobbyInput.Down);
                Frame("lobby_create_race.png");
                Assert.NotNull(preview.Entity);
                Assert.Equal((ushort)(7 << 8), preview.Entity!.Appearance.GrapIdTable[0]);

                for (int i = 0; i < 5; i++) lobby.HandleInput(LobbyInput.Confirm); // race .. job
                lobby.HandleInput(LobbyInput.Down);
                Frame("lobby_create_nation.png");
                lobby.HandleInput(LobbyInput.Confirm);
                lobby.HandleText("Gordian");
                Frame("lobby_create_name.png");
                lobby.HandleInput(LobbyInput.Confirm);
                WaitIdle();
                Frame("lobby_create_world.png");
                lobby.HandleInput(LobbyInput.Confirm);
                WaitIdle();
                Assert.NotNull(lobby.Prompt);
                Frame("lobby_create_confirm.png");

                framebuffer.Dispose(); depth.Dispose(); color.Dispose();
                entities.Dispose();
            }
            finally
            {
                devices.Dispose();
                DestroyWindow(hwnd);
            }
        }

        [Fact]
        public void RendersTitleMenuCharacterListAndPreview()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.LoadLobby(rm);
            if (library == null) return;
            var text = LobbyTextTables.Load(rm.LoadDatBytes);

            var backend = new FakeBackend();
            backend.List.Add(Character(1, "Knot", 2, 5, 4, 75));
            backend.List.Add(Character(2, "Gemini", 7, 2, 1, 30));
            backend.List.Add(Free(3));
            var lobby = new LobbyController(backend, library, text);

            const uint width = 1280, height = 720;
            IntPtr hwnd = CreateWindowExW(0, "static", "LobbyTest", unchecked((int)0x80000000), 0, 0, (int)width, (int)height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devices = new VeldridDeviceManager();
            devices.Initialize(Veldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), width, height, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devices.Device;
            if (gd == null) { DestroyWindow(hwnd); return; }
            try
            {
                var format = gd.SwapchainFramebuffer.ColorTargets[0].Target.Format;
                var color = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(width, height, 1, 1, format, Veldrid.TextureUsage.RenderTarget | Veldrid.TextureUsage.Sampled));
                var depth = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(width, height, 1, 1, Veldrid.PixelFormat.R32_Float, Veldrid.TextureUsage.DepthStencil));
                var framebuffer = gd.ResourceFactory.CreateFramebuffer(new Veldrid.FramebufferDescription(depth, color));
                using var frames = new LobbyFrameRenderer(gd, framebuffer.OutputDescription);
                var entities = new EntityRenderer(gd);
                var preview = new LobbyPreview();
                string? dumpDir = Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP");
                if (!string.IsNullOrEmpty(dumpDir)) Directory.CreateDirectory(dumpDir);

                byte[] Frame(string name)
                {
                    for (int i = 0; i < 3; i++) frames.Render(lobby, preview, entities, rm, framebuffer, width, height, 1 / 60f);
                    var pixels = StockUiRendererTests.ReadBack(gd, color, width, height);
                    if (!string.IsNullOrEmpty(dumpDir)) StockUiRendererTests.SavePng(Path.Combine(dumpDir, name), pixels, (int)width, (int)height);
                    return pixels;
                }

                // Title: the FINAL FANTASY XI art and the five buttons; Select Character highlighted.
                // The licence page comes first (ptc8lice over the title art), Accept shows the title menu.
                Assert.True(lobby.IsLicencePending);
                Frame("lobby_licence.png");
                lobby.HandleInput(LobbyInput.Confirm);
                Assert.False(lobby.IsLicencePending);

                var title = Frame("lobby_title.png");
                Assert.Equal(LobbyScreen.MainMenu, lobby.Screen);
                Assert.Contains("Select a character", lobby.HelpText);

                lobby.HandleInput(LobbyInput.Confirm);
                Assert.Equal(LobbyScreen.CharacterList, lobby.Screen);
                Assert.Equal("Knot", lobby.PreviewCharacter?.Name);
                var list = Frame("lobby_list.png");
                Assert.NotNull(preview.Entity);

                // The preview model stands in the left part of the 4:3 area: some lit pixels there that the title lacks.
                var rect = StockUiLobby.Fit(width, height);
                var (cx, feetY, topY) = StockUiLobby.PreviewArea(rect);
                int lit = 0;
                for (int y = (int)topY; y < (int)feetY; y += 2)
                {
                    for (int x = (int)(cx - 60); x < (int)(cx + 60); x += 2)
                    {
                        int o = (y * (int)width + x) * 4;
                        if (list[o] + list[o + 1] + list[o + 2] > 150) lit++;
                    }
                }
                Assert.True(lit > 50, $"preview area has {lit} lit pixels");

                lobby.HandleInput(LobbyInput.Down);
                Assert.Equal("Gemini", lobby.PreviewCharacter?.Name);
                lobby.HandleInput(LobbyInput.Down); // slot 3 is free: the cursor wraps to slot 1
                Assert.Equal("Knot", lobby.PreviewCharacter?.Name);
                lobby.HandleInput(LobbyInput.Up);
                Frame("lobby_list_second.png");

                lobby.HandleInput(LobbyInput.Confirm); // selecting: the status window stays up (the fake never answers)
                Assert.NotNull(lobby.Prompt);
                Frame("lobby_selecting.png");

                framebuffer.Dispose(); depth.Dispose(); color.Dispose();
                entities.Dispose();
            }
            finally
            {
                devices.Dispose();
                DestroyWindow(hwnd);
            }
        }
    }
}
