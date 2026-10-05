// tests/Gordian.App.Tests/Graphics/StockUiRendererTests.cs
using System;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.InteropServices;
using Gordian.App.Graphics;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    public class StockUiRendererTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        [Fact]
        public void NormalizeAlpha_DoublesHalfScaleTexturesOnly()
        {
            var half = new byte[] { 10, 20, 30, 0x88, 1, 2, 3, 0x40 };
            var normalized = StockUiRenderer.NormalizeAlpha(half);
            Assert.Equal(255, normalized[3]);
            Assert.Equal(0x80, normalized[7]);
            Assert.Equal(0x88, half[3]); // the source is not modified

            var full = new byte[] { 0, 0, 0, 0xFF, 0, 0, 0, 0 };
            Assert.Same(full, StockUiRenderer.NormalizeAlpha(full));
        }

        [Theory]
        [InlineData("Cybin", "Cybin")]
        [InlineData("Tarudra", "Tarudra")]
        [InlineData("Tarudrake", "Tarudra..")] // as the retail party list shows it beside 9999 HP
        public void FitName_TruncatesLikeRetail(string name, string expected)
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            var font = library != null ? UiFont.FromLibrary(library) : null;
            if (font == null) return;
            Assert.Equal(expected, StockUiPartyWindow.FitName(font, name));
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int x, int y,
            int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hwnd);

        /// <summary>
        /// Renders the retail log, party, target and main-menu windows with the GPU pipeline at 1024 x 768 and checks
        /// the corner anchoring. Set GORDIAN_UI_DUMP to a directory to also save the frame as a PNG.
        /// </summary>
        [Fact]
        public void RendersAnchoredRetailWindows()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            var font = library != null ? UiFont.FromLibrary(library) : null;
            if (library == null || font == null) return;

            const uint width = 1024, height = 768;
            IntPtr hwnd = CreateWindowExW(0, "static", "StockUiTest", unchecked((int)0x80000000), 0, 0, (int)width, (int)height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
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

                var cl = gd.ResourceFactory.CreateCommandList();
                cl.Begin();
                cl.SetFramebuffer(framebuffer);
                cl.ClearColorTarget(0, new Veldrid.RgbaFloat(0.16f, 0.24f, 0.16f, 1.0f));
                cl.End();
                gd.SubmitCommands(cl);

                var layout = new StockUiLayout { Scale = 1.5f };
                using var renderer = new StockUiRenderer(gd, framebuffer.OutputDescription);
                renderer.Begin(library);
                Assert.True(library.TryGetMenu("ptw0", out var solo));
                var party = layout.Resolve(StockUiWindowIds.Party, solo.Frame, width, height);
                renderer.DrawMenu(solo, party, includeButtons: false);
                renderer.DrawText(font, "Gordian", party.X + 7 * party.Scale, party.Y + 8 * party.Scale, party.Scale);

                Assert.True(library.TryGetMenu("logwindo", out var log));
                var logPlacement = layout.Resolve(StockUiWindowIds.Log, log.Frame, width, height);
                float logWidth = (party.X - 2 * party.Scale - logPlacement.X) / logPlacement.Scale;
                renderer.DrawMenu(log, logPlacement, includeButtons: false, logWidth);

                foreach (var (id, menuName) in new[] { (StockUiWindowIds.Target, "targetwi"), (StockUiWindowIds.MainMenu, "menuwind") })
                {
                    Assert.True(library.TryGetMenu(menuName, out var menu));
                    renderer.DrawMenu(menu, layout.Resolve(id, menu.Frame, width, height), includeButtons: menuName == "menuwind");
                }
                renderer.End(framebuffer, width, height);
                Assert.True(renderer.LastQuadCount > 100, $"{renderer.LastQuadCount} quads");

                // Bottom-right anchoring: the party window keeps its authored 16-pixel margins, scaled.
                Assert.Equal(width - 16 * 1.5f, party.Right(112), 3);
                Assert.Equal(height - 16 * 1.5f, party.Bottom(34), 3);

                var pixels = ReadBack(gd, color, width, height);
                string? dumpDir = Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP");
                if (!string.IsNullOrEmpty(dumpDir))
                {
                    Directory.CreateDirectory(dumpDir);
                    SavePng(Path.Combine(dumpDir, "gpu_hud.png"), pixels, (int)width, (int)height);
                }

                // The stretched log window reaches the party window: just left of the party window is log background.
                var logEdge = Pixel(pixels, width, (int)(party.X - 6), (int)(party.Y + 30));
                Assert.True(logEdge.B > logEdge.G, $"log window right edge {logEdge}");

                // Inside the party window the navy background replaces the clear colour; above it the clear colour stays.
                var inside = Pixel(pixels, width, (int)(party.X + 30), (int)(party.Y + 40));
                var outside = Pixel(pixels, width, (int)(party.X + 30), (int)(party.Y - 30));
                Assert.True(inside.B > inside.G, $"inside party window {inside}");
                Assert.True(outside.G > outside.B, $"outside party window {outside}");

                framebuffer.Dispose(); depth.Dispose(); color.Dispose(); cl.Dispose();
            }
            finally
            {
                devices.Dispose();
                DestroyWindow(hwnd);
            }
        }

        /// <summary>
        /// Renders the open main menu (cursor on the third entry) and a "Log out?" prompt at 1:1 through the menu
        /// controller and <see cref="StockUiMenuWindow"/>; writes gpu_menu.png when GORDIAN_UI_DUMP is set.
        /// </summary>
        [Fact]
        public async Task RendersOpenMainMenuWithCursor()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            var font = library != null ? UiFont.FromLibrary(library) : null;
            if (library == null || font == null) return;

            const uint width = 1024, height = 768;
            IntPtr hwnd = CreateWindowExW(0, "static", "StockUiMenuTest", unchecked((int)0x80000000), 0, 0, (int)width, (int)height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
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

                var cl = gd.ResourceFactory.CreateCommandList();
                cl.Begin();
                cl.SetFramebuffer(framebuffer);
                cl.ClearColorTarget(0, new Veldrid.RgbaFloat(0.16f, 0.24f, 0.16f, 1.0f));
                cl.End();
                gd.SubmitCommands(cl);

                var menus = new StockUiMenuController { Library = library };
                Assert.True(menus.OpenMainMenu());
                menus.Move(Gordian.Core.Input.InputAction.MenuDown);
                menus.Move(Gordian.Core.Input.InputAction.MenuDown);
                var prompt = menus.PromptYesNoAsync("Log out?", defaultYes: false);

                var layout = new StockUiLayout();
                using var renderer = new StockUiRenderer(gd, framebuffer.OutputDescription);
                renderer.Begin(library);
                var main = menus.OpenMenus[0];
                var placement = layout.Resolve(StockUiWindowIds.MainMenu, main.Menu.Frame, width, height);
                StockUiMenuWindow.Draw(renderer, library, font, main, placement, 0);
                var promptMenu = menus.OpenMenus[1];
                var promptFrame = promptMenu.Menu.Frame;
                var promptPlacement = StockUiLayout.Place(promptFrame.Anchor, promptFrame.X, promptFrame.Y, promptFrame.Width, promptFrame.Height, 1, width, height);
                StockUiMenuWindow.Draw(renderer, library, font, promptMenu, promptPlacement, 0);
                // The hover pointer (the arrow with the grey ring over its tip) over the clear colour, left of the prompt.
                const float pointerX = 40, pointerY = 40;
                StockUiMenuWindow.DrawHoverPointer(renderer, pointerX, pointerY);
                renderer.End(framebuffer, width, height);
                Assert.True(renderer.LastQuadCount > 60, $"{renderer.LastQuadCount} quads");

                var pixels = ReadBack(gd, color, width, height);
                string? dumpDir = Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP");
                if (!string.IsNullOrEmpty(dumpDir))
                {
                    Directory.CreateDirectory(dumpDir);
                    SavePng(Path.Combine(dumpDir, "gpu_menu.png"), pixels, (int)width, (int)height);
                }

                // The cursor (gold arrow) sits left of the third entry, in the frame's margin: the row through its
                // middle holds a pixel redder than the green clear colour and than the navy window background.
                var selected = main.SelectedButton!;
                Assert.Equal(3, selected.ButtonId);
                int rowY = (int)(placement.Y + selected.Y + 8);
                bool gold = false;
                for (int x = (int)placement.X - 16; x < (int)placement.X + 16 && !gold; x++)
                {
                    var p = Pixel(pixels, width, x, rowY);
                    gold = p.R > p.G + 20 && p.R > p.B + 20;
                }
                Assert.True(gold, "no cursor pixel beside the selected entry");

                // The selected label's glyphs are tinted orange (retail FFC05C-ish): brighter red than blue in its text.
                bool orange = false;
                for (int x = (int)placement.X + selected.X + 6; x < (int)placement.X + selected.X + 40 && !orange; x++)
                {
                    var p = Pixel(pixels, width, x, rowY);
                    orange = p.R > 200 && p.R > p.B + 80;
                }
                Assert.True(orange, "selected label is not tinted");

                // The arrow's yellow body runs down from the pointer below the ring; the ring is light grey above it.
                bool yellow = false;
                for (int y = (int)pointerY + 10; y < (int)pointerY + 16 && !yellow; y++)
                {
                    for (int x = (int)pointerX; x < (int)pointerX + 8 && !yellow; x++)
                    {
                        var p = Pixel(pixels, width, x, y);
                        yellow = p.R > 200 && p.G > 140 && p.B < 140;
                    }
                }
                Assert.True(yellow, "no arrow below the pointer");
                var ringTop = Pixel(pixels, width, (int)(pointerX + StockUiPointerArt.RingOffsetX), (int)(pointerY + StockUiPointerArt.RingOffsetY - StockUiPointerArt.RingOuterRadius + 0.5f));
                Assert.True(ringTop.R > 150 && Math.Abs(ringTop.R - ringTop.B) < 24, $"no grey ring above the pointer: {ringTop}");
                menus.CloseAll();
                Assert.False(await prompt);

                framebuffer.Dispose(); depth.Dispose(); color.Dispose(); cl.Dispose();
            }
            finally
            {
                devices.Dispose();
                DestroyWindow(hwnd);
            }
        }

        /// <summary>
        /// Renders the target command menu composed for yourself (Chat, Magic, Abilities, Trust, Items, Trade, Check)
        /// at the bottom left with the chat-mode list opened from its Chat entry beside it: the rebuilt seven-row
        /// frame, the tell partner's name after Tell's red arrow, and the greyed Linkshell 2 / Unity rows with their
        /// "No Linkshell" / "No Unity" text. Writes gpu_command_menu.png when GORDIAN_UI_DUMP is set.
        /// </summary>
        [Fact]
        public void RendersShopBuyListWithQuantityPrompt()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            var font = library != null ? UiFont.FromLibrary(library) : null;
            if (library == null || font == null) return;

            const uint width = 1024, height = 768;
            IntPtr hwnd = CreateWindowExW(0, "static", "StockUiShopTest", unchecked((int)0x80000000), 0, 0, (int)width, (int)height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
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

                var cl = gd.ResourceFactory.CreateCommandList();
                cl.Begin();
                cl.SetFramebuffer(framebuffer);
                cl.ClearColorTarget(0, new Veldrid.RgbaFloat(0.16f, 0.24f, 0.16f, 1.0f));
                cl.End();
                gd.SubmitCommands(cl);

                // The maintainer's captured shop (Corua, Southern San d'Oria): list 4 with four items; 5,000 gil in hand.
                var inventory = new Gordian.Core.World.InventoryState();
                inventory.SetItem(Gordian.Core.Network.Packets.ContainerId.Inventory, 0, StockUiShop.GilItemId, 5000, Gordian.Core.Network.Packets.ItemLockFlag.Normal);
                var menus = new StockUiMenuController
                {
                    Library = library,
                    Inventory = inventory,
                    ItemLookup = id => rm.TryGetItem(id, out var record) ? record : null,
                };
                inventory.ShopChanged += menus.OnShopChanged;
                inventory.OpenShop(4);
                inventory.AddShopItems(new[]
                {
                    new Gordian.Core.World.ShopItemEntry(34, 4389, 0, 0, 0),
                    new Gordian.Core.World.ShopItemEntry(82, 4431, 1, 0, 0),
                    new Gordian.Core.World.ShopItemEntry(128, 639, 2, 0, 0),
                    new Gordian.Core.World.ShopItemEntry(64, 610, 3, 0, 0),
                });
                Assert.True(menus.Top!.IsShopMenu);
                menus.Activate(); // Buy
                menus.Move(Gordian.Core.Input.InputAction.MenuDown);
                menus.Move(Gordian.Core.Input.InputAction.MenuDown); // Ronfaure Chestnut

                var layout = new StockUiLayout();
                using var renderer = new StockUiRenderer(gd, framebuffer.OutputDescription);
                string? dumpDir = Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP");
                // Draws the open shop windows as the HUD would: the root at its layout slot, the rest at their authored places.
                StockUiPlacement PlaceMenu(StockUiOpenMenu menu, bool root)
                {
                    var frame = menu.Menu.Frame;
                    return root ? layout.Resolve(StockUiWindowIds.Shop, frame, width, height)
                        : StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, 1, width, height);
                }
                byte[] Render(string name)
                {
                    cl.Begin();
                    cl.SetFramebuffer(framebuffer);
                    cl.ClearColorTarget(0, new Veldrid.RgbaFloat(0.16f, 0.24f, 0.16f, 1.0f));
                    cl.End();
                    gd.SubmitCommands(cl);
                    renderer.Begin(library);
                    for (int i = 0; i < menus.OpenMenus.Count; i++)
                    {
                        // As the HUD: a window whose authored rectangle a later one covers is not drawn (the list's
                        // gil window replaces the Buy / Sell window in its corner).
                        var menu = menus.OpenMenus[i];
                        bool covered = false;
                        for (int j = i + 1; j < menus.OpenMenus.Count && !covered; j++) covered = menus.OpenMenus[j].OverlapsAuthored(menu);
                        if (!covered) StockUiMenuWindow.Draw(renderer, library, font, menu, PlaceMenu(menu, i == 0), 0);
                    }
                    renderer.End(framebuffer, width, height);
                    var shot = ReadBack(gd, color, width, height);
                    if (!string.IsNullOrEmpty(dumpDir))
                    {
                        Directory.CreateDirectory(dumpDir);
                        SavePng(Path.Combine(dumpDir, name), shot, (int)width, (int)height);
                    }
                    return shot;
                }

                // The list with the gil and item info windows under it.
                Assert.Equal(2, menus.OpenMenus.Count);
                var listOnly = Render("gpu_shop_list.png");
                Assert.True(renderer.LastQuadCount > 60, $"{renderer.LastQuadCount} quads");
                var list = menus.OpenMenus[1];
                var listPlacement = PlaceMenu(list, false);
                // The gil amount is drawn right-aligned in the "Current Gil" window under the list (light digits).
                bool gilDigits = false;
                int gilY = (int)listPlacement.Y + 192 + 30 + 6;
                for (int x = (int)listPlacement.X + 60; x < (int)listPlacement.X + 104 && !gilDigits; x++)
                {
                    var p = Pixel(listOnly, width, x, gilY);
                    gilDigits = p.R > 180 && p.G > 180 && p.B > 180;
                }
                Assert.True(gilDigits, "no gil amount under the list");

                menus.Activate(); // the quantity prompt
                menus.Move(Gordian.Core.Input.InputAction.MenuLeft); // All: 12
                Assert.Equal(3, menus.OpenMenus.Count);
                var pixels = Render("gpu_shop_buy.png");
                Assert.True(renderer.LastQuadCount > 50, $"{renderer.LastQuadCount} quads");
                var quantity = menus.OpenMenus[2];
                var quantityPlacement = PlaceMenu(quantity, false);

                // The first row's name is drawn after the icon slot (light glyph pixels), and its icon fills the slot.
                bool name = false;
                int rowY = (int)listPlacement.Y + 5 + 8;
                for (int x = (int)listPlacement.X + 24; x < (int)listPlacement.X + 120 && !name; x++)
                {
                    var p = Pixel(pixels, width, x, rowY);
                    name = p.R > 180 && p.G > 180 && p.B > 180;
                }
                Assert.True(name, "no item name on the first row");
                var icon = Pixel(pixels, width, (int)listPlacement.X + 3 + 8, (int)listPlacement.Y + 5 + 8);
                var plate = Pixel(pixels, width, (int)listPlacement.X + 200, (int)listPlacement.Y + 5 + 8);
                Assert.True(Math.Abs(icon.R - plate.R) + Math.Abs(icon.G - plate.G) + Math.Abs(icon.B - plate.B) > 30, $"no icon in the slot: {icon} vs {plate}");

                // The quantity prompt shows "12 /12" from its field (light pixels inside the 24 x 16 field at (34,22)).
                bool count = false;
                for (int x = (int)quantityPlacement.X + 34; x < (int)quantityPlacement.X + 58 && !count; x++)
                {
                    var p = Pixel(pixels, width, x, (int)quantityPlacement.Y + 22 + 8);
                    count = p.R > 150 && p.G > 100;
                }
                Assert.True(count, "no count in the quantity field");

                framebuffer.Dispose(); depth.Dispose(); color.Dispose(); cl.Dispose();
            }
            finally
            {
                devices.Dispose();
                DestroyWindow(hwnd);
            }
        }

        [Fact]
        public void RendersTargetCommandMenuWithChatModes()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            var font = library != null ? UiFont.FromLibrary(library) : null;
            if (library == null || font == null) return;

            const uint width = 1024, height = 768;
            IntPtr hwnd = CreateWindowExW(0, "static", "StockUiCommandMenuTest", unchecked((int)0x80000000), 0, 0, (int)width, (int)height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
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

                var cl = gd.ResourceFactory.CreateCommandList();
                cl.Begin();
                cl.SetFramebuffer(framebuffer);
                cl.ClearColorTarget(0, new Veldrid.RgbaFloat(0.16f, 0.24f, 0.16f, 1.0f));
                cl.End();
                gd.SubmitCommands(cl);

                var menus = new StockUiMenuController
                {
                    Library = library,
                    TellTarget = () => "Ayame",
                    HasLinkshell = slot => slot == 1,
                };
                Assert.True(menus.OpenCommandMenu(new StockUiTargetContext(StockUiTargetKind.Self, 1, "Me")));
                menus.Activate(); // Chat -> the chat-mode list
                Assert.Equal(2, menus.OpenMenus.Count);
                menus.Move(Gordian.Core.Input.InputAction.MenuDown); // Tell

                var layout = new StockUiLayout();
                using var renderer = new StockUiRenderer(gd, framebuffer.OutputDescription);
                renderer.Begin(library);
                var command = menus.OpenMenus[0];
                var placement = layout.Resolve(StockUiWindowIds.CommandMenu, command.Menu.Frame, width, height);
                StockUiMenuWindow.Draw(renderer, library, font, command, placement, 0);
                var modes = menus.OpenMenus[1];
                var modesFrame = modes.Menu.Frame;
                var modesPlacement = StockUiLayout.Place(modesFrame.Anchor, modesFrame.X, modesFrame.Y, modesFrame.Width, modesFrame.Height, 1, width, height);
                StockUiMenuWindow.Draw(renderer, library, font, modes, modesPlacement, 0);
                renderer.End(framebuffer, width, height);
                Assert.True(renderer.LastQuadCount > 60, $"{renderer.LastQuadCount} quads");

                var pixels = ReadBack(gd, color, width, height);
                string? dumpDir = Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP");
                if (!string.IsNullOrEmpty(dumpDir))
                {
                    Directory.CreateDirectory(dumpDir);
                    SavePng(Path.Combine(dumpDir, "gpu_command_menu.png"), pixels, (int)width, (int)height);
                }

                // The composed frame: seven rows, its bottom 2 px above the eight-line log window's top (layout y 296).
                Assert.Equal(8 + 16 * 7, command.Menu.Frame.Height);
                Assert.Equal(height - (448 - 296), placement.Y + command.Menu.Frame.Height);
                // Its body is the window plate (navy: blue above red) at the last row, where the template's 168-tall
                // frame would otherwise have ended long before; the green clear colour shows below the window.
                var body = Pixel(pixels, width, (int)placement.X + 4, (int)placement.Y + 5 + 16 * 6 + 8);
                Assert.True(body.B > body.R + 10 && body.B > body.G, $"no window plate on the last row: {body}");
                var below = Pixel(pixels, width, (int)placement.X + 4, (int)(placement.Y + command.Menu.Frame.Height) + 1);
                Assert.True(below.G > below.B + 10, $"window plate extends past the composed frame: {below}");

                // The tell partner's name is drawn after the red arrow (light glyph pixels right of x 81 on Tell's row).
                bool name = false;
                int tellY = (int)modesPlacement.Y + 21 + 8;
                for (int x = (int)modesPlacement.X + 101; x < (int)modesPlacement.X + 140 && !name; x++)
                {
                    var p = Pixel(pixels, width, x, tellY);
                    name = p.R > 180 && p.G > 180 && p.B > 180;
                }
                Assert.True(name, "no tell partner name after the arrow");

                framebuffer.Dispose(); depth.Dispose(); color.Dispose(); cl.Dispose();
            }
            finally
            {
                devices.Dispose();
                DestroyWindow(hwnd);
            }
        }

        /// <summary>
        /// Renders the Windows config flow (Config -> Windows -> Shared) at 1:1: the "Shared" list replaces the config
        /// list in the top-right corner while the Window Settings page opens at the left, with skin 3's digit
        /// highlighted under the cursor and the red bar under the skin in effect (1). Writes gpu_settings.png when
        /// GORDIAN_UI_DUMP is set.
        /// </summary>
        [Fact]
        public void RendersWindowSettingsPage()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            var font = library != null ? UiFont.FromLibrary(library) : null;
            if (library == null || font == null) return;

            const uint width = 1024, height = 768;
            IntPtr hwnd = CreateWindowExW(0, "static", "StockUiSettingsTest", unchecked((int)0x80000000), 0, 0, (int)width, (int)height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
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

                var cl = gd.ResourceFactory.CreateCommandList();
                cl.Begin();
                cl.SetFramebuffer(framebuffer);
                cl.ClearColorTarget(0, new Veldrid.RgbaFloat(0.16f, 0.24f, 0.16f, 1.0f));
                cl.End();
                gd.SubmitCommands(cl);

                var menus = new StockUiMenuController { Library = library, CurrentWindowSkin = () => 1 };
                Assert.True(menus.Open(StockUiMenuEntries.ConfigMenu));
                Assert.True(menus.Open(StockUiMenuEntries.WindowsMenu));
                Assert.True(menus.Open(StockUiMenuEntries.WindowSettingsPage));
                for (int i = 0; i < 2; i++) menus.Move(Gordian.Core.Input.InputAction.MenuDown); // to the Window Type row
                for (int i = 0; i < 2; i++) menus.Move(Gordian.Core.Input.InputAction.MenuRight); // digit 3
                Assert.Equal(StockUiMenuEntries.WindowSkinFirstButton + 2, menus.Top!.SelectedButtonId);

                var layout = new StockUiLayout();
                using var renderer = new StockUiRenderer(gd, framebuffer.OutputDescription);
                renderer.Begin(library);
                var open = menus.OpenMenus;
                for (int i = 0; i < open.Count; i++)
                {
                    bool covered = false;
                    for (int j = i + 1; j < open.Count && !covered; j++) covered = open[j].OverlapsAuthored(open[i]);
                    if (covered) continue;
                    var frame = open[i].Menu.Frame;
                    var placement = StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, 1, width, height);
                    StockUiMenuWindow.Draw(renderer, library, font, open[i], placement, 0);
                }
                renderer.End(framebuffer, width, height);

                var pixels = ReadBack(gd, color, width, height);
                string? dumpDir = Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP");
                if (!string.IsNullOrEmpty(dumpDir))
                {
                    Directory.CreateDirectory(dumpDir);
                    SavePng(Path.Combine(dumpDir, "gpu_settings.png"), pixels, (int)width, (int)height);
                }

                // The red bar sits just under digit 1 (button 7 at (78, 126), 15 x 15) of the page at (16, 48).
                var settings = open[^1].Menu;
                var digit1 = settings.FindButton(StockUiMenuEntries.WindowSkinFirstButton)!;
                var bar = Pixel(pixels, width, 16 + digit1.X + 7, 48 + digit1.Y + digit1.Height);
                Assert.True(bar.R > bar.G + 40 && bar.R > bar.B + 40, $"red bar {bar}");

                framebuffer.Dispose(); depth.Dispose(); color.Dispose(); cl.Dispose();
            }
            finally
            {
                devices.Dispose();
                DestroyWindow(hwnd);
            }
        }

        /// <summary>
        /// Renders the Gameplay config page (option markers and slider fills) and the Chat Filters list (client-drawn
        /// rows) offscreen; writes gpu_config_*.png when GORDIAN_UI_DUMP is set.
        /// </summary>
        [Fact]
        public void RendersConfigPagesWithSlidersAndFilterRows()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            var font = library != null ? UiFont.FromLibrary(library) : null;
            if (library == null || font == null) return;

            const uint width = 1024, height = 768;
            IntPtr hwnd = CreateWindowExW(0, "static", "StockUiConfigTest", unchecked((int)0x80000000), 0, 0, (int)width, (int)height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
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
                var cl = gd.ResourceFactory.CreateCommandList();

                byte[] Render(StockUiMenuController menus, string name)
                {
                    cl.Begin();
                    cl.SetFramebuffer(framebuffer);
                    cl.ClearColorTarget(0, new Veldrid.RgbaFloat(0.16f, 0.24f, 0.16f, 1.0f));
                    cl.End();
                    gd.SubmitCommands(cl);
                    using var renderer = new StockUiRenderer(gd, framebuffer.OutputDescription);
                    renderer.Begin(library);
                    var top = menus.Top!;
                    var frame = top.Menu.Frame;
                    var placement = StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, 1, width, height);
                    StockUiMenuWindow.Draw(renderer, library, font, top, placement, 0);
                    renderer.End(framebuffer, width, height);
                    var pixels = ReadBack(gd, color, width, height);
                    string? dumpDir = Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP");
                    if (!string.IsNullOrEmpty(dumpDir))
                    {
                        Directory.CreateDirectory(dumpDir);
                        SavePng(Path.Combine(dumpDir, $"gpu_{name}.png"), pixels, (int)width, (int)height);
                    }
                    return pixels;
                }

                var menus = new StockUiMenuController { Library = library };
                Assert.True(menus.Open(StockUiConfigPages.GameplayPage));
                var page = menus.Top!.Menu;
                var pixels = Render(menus, "config_gameplay");

                // Sound Effects Volume (button 3, the 192-wide bar) defaults to 100: the fill reaches the bar's right end.
                var bar = page.FindButton(3)!;
                var fullFill = Pixel(pixels, width, 16 + bar.X + bar.Width - 4, 48 + bar.Y + 6);
                Assert.True(fullFill.B > 200 && fullFill.B > fullFill.R + 30, $"slider fill {fullFill}");
                var onMark = page.FindButton(1)!;
                var red = Pixel(pixels, width, 16 + onMark.X + 8, 48 + onMark.Y + onMark.Height);
                Assert.True(red.R > red.G + 40 && red.R > red.B + 40, $"ON marker {red}");

                // Left on the bar moves the value down a step; past the value the bar shows the authored translucent strip.
                menus.Settings.SetValue(StockUiSettingKey.SoundEffectsVolume, 50);
                menus.Move(Gordian.Core.Input.InputAction.MenuDown);           // ON -> the Sound Effects Volume bar
                Assert.Equal(3, menus.Top!.SelectedButtonId);
                menus.Move(Gordian.Core.Input.InputAction.MenuLeft);
                Assert.Equal(45, menus.Settings.GetValue(StockUiSettingKey.SoundEffectsVolume));
                pixels = Render(menus, "config_gameplay_half");
                var pastValue = Pixel(pixels, width, 16 + bar.X + bar.Width - 4, 48 + bar.Y + 6);
                Assert.False(pastValue.B > 200, $"no fill past the value {pastValue}");
                var beforeValue = Pixel(pixels, width, 16 + bar.X + 20, 48 + bar.Y + 6);
                Assert.True(beforeValue.B > 200 && beforeValue.B > beforeValue.R + 30, $"fill before the value {beforeValue}");

                menus.CloseAll();
                Assert.True(menus.Open(StockUiConfigPages.ChatFiltersPage));
                menus.Activate();                                              // filter "Say": marked row
                pixels = Render(menus, "config_chat_filters");
                // The ON ball (blue) sits at the first row's origin; the second row shows OFF (grey).
                var row1 = menus.Top!.Menu.FindButton(1)!;
                bool blue = false;
                for (int x = 2; x < 14 && !blue; x++)
                {
                    for (int y = 2; y < 14 && !blue; y++)
                    {
                        var p = Pixel(pixels, width, 16 + row1.X + x, 48 + row1.Y + y);
                        blue = p.B > 150 && p.B > p.R + 60;
                    }
                }
                Assert.True(blue, "ON ball");
                // Row text is drawn 34 px in: some pixel of the second row ("Tell") is bright.
                var row2 = menus.Top.Menu.FindButton(2)!;
                bool bright = false;
                for (int x = 34; x < 74 && !bright; x++)
                {
                    for (int y = 0; y < row2.Height && !bright; y++)
                    {
                        var p = Pixel(pixels, width, 16 + row2.X + x, 48 + row2.Y + y);
                        bright = p.R > 180 && p.G > 180 && p.B > 180;
                    }
                }
                Assert.True(bright, "row text");

                framebuffer.Dispose(); depth.Dispose(); color.Dispose(); cl.Dispose();
            }
            finally
            {
                devices.Dispose();
                DestroyWindow(hwnd);
            }
        }

        /// <summary>
        /// Renders a two-member party window at 1:1 with the values of a Windower capture (Tarudrake 9999 / 2794, leader, shown "Tarudra..";
        /// Cybin 1658 / 571) for side-by-side comparison; writes party_rows.png when GORDIAN_UI_DUMP is set.
        /// </summary>
        [Fact]
        public void RendersPartyRowsAtRetailScale()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            var font = library != null ? UiFont.FromLibrary(library) : null;
            if (library == null || font == null || !library.TryGetMenu("ptw2", out var menu)) return;

            const uint width = 128, height = 80;
            IntPtr hwnd = CreateWindowExW(0, "static", "StockUiPartyTest", unchecked((int)0x80000000), 0, 0, (int)width, (int)height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devices = new VeldridDeviceManager();
            devices.Initialize(Veldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), width, height, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devices.Device;
            if (gd == null) { DestroyWindow(hwnd); return; }

            try
            {
                var format = gd.SwapchainFramebuffer.ColorTargets[0].Target.Format;
                var color = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(width, height, 1, 1, format, Veldrid.TextureUsage.RenderTarget | Veldrid.TextureUsage.Sampled));
                var framebuffer = gd.ResourceFactory.CreateFramebuffer(new Veldrid.FramebufferDescription(null, color));
                var cl = gd.ResourceFactory.CreateCommandList();
                cl.Begin();
                cl.SetFramebuffer(framebuffer);
                cl.ClearColorTarget(0, new Veldrid.RgbaFloat(0.2f, 0.19f, 0.18f, 1.0f));
                cl.End();
                gd.SubmitCommands(cl);

                using var renderer = new StockUiRenderer(gd, framebuffer.OutputDescription);
                renderer.Begin(library);
                var placement = new StockUiPlacement(3, 8, 1, false);
                renderer.DrawMenu(menu, placement, includeButtons: false);
                StockUiPartyWindow.Draw(renderer, font, menu, placement, new[]
                {
                    new PartyRowVitals("Tarudrake", 9999, 100, 2794, 100, 1000, IsLeader: true),
                    new PartyRowVitals("Cybin", 1658, 100, 571, 100, 0, IsLeader: false),
                }, showTp: false);
                renderer.End(framebuffer, width, height);
                Assert.True(renderer.LastQuadCount > 40);

                var pixels = ReadBack(gd, color, width, height);
                string? dumpDir = Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP");
                if (!string.IsNullOrEmpty(dumpDir))
                {
                    Directory.CreateDirectory(dumpDir);
                    SavePng(Path.Combine(dumpDir, "party_rows.png"), pixels, (int)width, (int)height);
                }

                // The full HP gauge is pink in its middle (retail fill 255, 155, 155).
                var hp = Pixel(pixels, width, (int)placement.X + 3 + 60, (int)placement.Y + 7 + 11);
                Assert.True(hp.R > 200 && hp.G < 190, $"HP gauge {hp}");

                framebuffer.Dispose(); color.Dispose(); cl.Dispose();
            }
            finally
            {
                devices.Dispose();
                DestroyWindow(hwnd);
            }
        }

        /// <summary>
        /// A member in another zone shows "(SSandOria)" on the second row instead of the HP/MP numbers and gauges (#146):
        /// the row has no gauge pixels, the in-zone row does. Writes party_rows_zone.png when GORDIAN_UI_DUMP is set.
        /// </summary>
        [Fact]
        public void RendersPartyRowForAMemberInAnotherZone()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            var font = library != null ? UiFont.FromLibrary(library) : null;
            if (library == null || font == null || !library.TryGetMenu("ptw2", out var menu)) return;

            const uint width = 128, height = 80;
            IntPtr hwnd = CreateWindowExW(0, "static", "StockUiPartyTest", unchecked((int)0x80000000), 0, 0, (int)width, (int)height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devices = new VeldridDeviceManager();
            devices.Initialize(Veldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), width, height, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devices.Device;
            if (gd == null) { DestroyWindow(hwnd); return; }

            try
            {
                var format = gd.SwapchainFramebuffer.ColorTargets[0].Target.Format;
                var color = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(width, height, 1, 1, format, Veldrid.TextureUsage.RenderTarget | Veldrid.TextureUsage.Sampled));
                var framebuffer = gd.ResourceFactory.CreateFramebuffer(new Veldrid.FramebufferDescription(null, color));
                var cl = gd.ResourceFactory.CreateCommandList();
                cl.Begin();
                cl.SetFramebuffer(framebuffer);
                cl.ClearColorTarget(0, new Veldrid.RgbaFloat(0.2f, 0.19f, 0.18f, 1.0f));
                cl.End();
                gd.SubmitCommands(cl);

                using var renderer = new StockUiRenderer(gd, framebuffer.OutputDescription);
                renderer.Begin(library);
                var placement = new StockUiPlacement(3, 8, 1, false);
                renderer.DrawMenu(menu, placement, includeButtons: false);
                StockUiPartyWindow.Draw(renderer, font, menu, placement, new[]
                {
                    new PartyRowVitals("Tarudrake", 9999, 100, 2794, 100, 1000, IsLeader: true),
                    new PartyRowVitals("Cybin", 0, 0, 0, 0, 0, IsLeader: false, ZoneName: StockUiPartyWindow.ZoneRowText("SSandOria")),
                }, showTp: false);
                renderer.End(framebuffer, width, height);
                Assert.True(renderer.LastQuadCount > 20);

                var pixels = ReadBack(gd, color, width, height);
                string? dumpDir = Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP");
                if (!string.IsNullOrEmpty(dumpDir))
                {
                    Directory.CreateDirectory(dumpDir);
                    SavePng(Path.Combine(dumpDir, "party_rows_zone.png"), pixels, (int)width, (int)height);
                }

                // Row 0 (in zone) has its pink HP gauge; row 1 (another zone) draws no gauge: nothing pink on its second line.
                var button1 = menu.Buttons[1];
                int rowX = (int)placement.X + button1.X, rowY = (int)placement.Y + button1.Y;
                bool anyPink = false;
                for (int x = rowX + 25; x < rowX + 90; x++)
                {
                    var px = Pixel(pixels, width, x, rowY + 7 + 3);
                    if (px.R > 200 && px.G < 190) anyPink = true;
                }
                Assert.False(anyPink, "a member in another zone must not draw an HP gauge");

                // The zone text is right-aligned to the MP number's edge (row x + 96): bright glyph pixels at the right,
                // none over the first 20 px of the second line.
                bool Bright(int x0, int x1)
                {
                    for (int x = x0; x < x1; x++)
                        for (int y = rowY + 10; y < rowY + 22; y++)
                        {
                            var px = Pixel(pixels, width, x, y);
                            if (px.R > 220 && px.G > 220 && px.B > 220) return true;
                        }
                    return false;
                }
                Assert.True(Bright(rowX + 80, rowX + 97), "zone text should reach the right edge");
                Assert.False(Bright(rowX, rowX + 20), "zone text must not start at the left edge");
                var button0 = menu.Buttons[0];
                var hp = Pixel(pixels, width, (int)placement.X + button0.X + 25 + 40, (int)placement.Y + button0.Y + 7 + 4);
                Assert.True(hp.R > 200 && hp.G < 190, $"HP gauge {hp}");

                framebuffer.Dispose(); color.Dispose(); cl.Dispose();
            }
            finally
            {
                devices.Dispose();
                DestroyWindow(hwnd);
            }
        }

        [Fact]
        public void ZoneRowText_PutsTheCompactNameInParentheses()
        {
            Assert.Equal("(SSandOria)", StockUiPartyWindow.ZoneRowText("SSandOria"));
        }

        /// <summary>
        /// Renders the split log as the retail capture of 2026-09-27 shows it (two eight-line windows side by side,
        /// titled "Window 1:Say" and "Window 2", timestamps, the input line over Window 1's bottom with its mode tab)
        /// at 1:1; writes chat_log.png when GORDIAN_UI_DUMP is set.
        /// </summary>
        [Fact]
        public void RendersChatLogAndInputLine()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            var font = library != null ? UiFont.FromLibrary(library) : null;
            var logFont = library != null ? StockUiLogFont.FromLibrary(library) : null;
            if (library == null || font == null || logFont == null || !library.TryGetMenu("log8", out var log)
                || !library.TryGetMenu("inline", out var inline)) return;

            // Proportional spacing as the retail capture: "Tarudrake" from the T's cell to the pen after the e is 77 px
            // (the spacing rule fits the capture to about a pixel per glyph), and a space is 7.
            Assert.InRange(logFont.MeasureWidth("Tarudrake"), 76, 78);
            Assert.Equal(7, logFont.GetAdvance(' '), 0);
            // Digits share one advance, so timestamps line up whatever their digits.
            Assert.Equal(logFont.MeasureWidth("[13:43:34]"), logFont.MeasureWidth("[11:11:31]"));

            const uint width = 1100, height = 330;
            IntPtr hwnd = CreateWindowExW(0, "static", "StockUiChatTest", unchecked((int)0x80000000), 0, 0, (int)width, (int)height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devices = new VeldridDeviceManager();
            devices.Initialize(Veldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), width, height, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devices.Device;
            if (gd == null) { DestroyWindow(hwnd); return; }

            try
            {
                var format = gd.SwapchainFramebuffer.ColorTargets[0].Target.Format;
                var color = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(width, height, 1, 1, format, Veldrid.TextureUsage.RenderTarget | Veldrid.TextureUsage.Sampled));
                var framebuffer = gd.ResourceFactory.CreateFramebuffer(new Veldrid.FramebufferDescription(null, color));
                var cl = gd.ResourceFactory.CreateCommandList();
                cl.Begin();
                cl.SetFramebuffer(framebuffer);
                cl.ClearColorTarget(0, new Veldrid.RgbaFloat(0.2f, 0.19f, 0.18f, 1.0f));
                cl.End();
                gd.SubmitCommands(cl);

                var t = new DateTime(2026, 9, 27, 7, 37, 35);
                var window1Lines = new[]
                {
                    new ChatLogLine(ChatLogChannel.System, "=== Area: Bibiki Bay ===", t),
                    new ChatLogLine(ChatLogChannel.Notice, ">> /lockstyleset 1", t.AddSeconds(7)),
                    new ChatLogLine(ChatLogChannel.System, "...A command error occurred.", t.AddSeconds(7)),
                    new ChatLogLine(ChatLogChannel.ServerMessage, "<<< Welcome to Nameless! >>>", t.AddSeconds(12)),
                    new ChatLogLine(ChatLogChannel.ServerMessage, "Please visit https://github.com/LandSandBoat/server for the latest information on the project.", t.AddSeconds(12)),
                    new ChatLogLine(ChatLogChannel.ServerMessage, "Thank you, and we hope you enjoy sailing the sands!", t.AddSeconds(12)),
                };
                // Every keyboard character, lower then upper case, as a second retail capture shows them.
                var window2Lines = new[]
                {
                    new ChatLogLine(ChatLogChannel.Say, "Tarudrake : hello", t.AddSeconds(152)),
                    new ChatLogLine(ChatLogChannel.Tell, ">>Cybin : hello", t.AddSeconds(162)),
                    new ChatLogLine(ChatLogChannel.Say, "Tarudrake : `1234567890-=qwertyuiop[]\\asdfghjkl;'zxcvbnm,./", t.AddSeconds(170)),
                    new ChatLogLine(ChatLogChannel.Say, "Tarudrake : ~!@#$%^&*()_+QWERTYUIOP{}|ASDFGHJKL:\"ZXCVBNM<>?", t.AddSeconds(174)),
                };
                var input = new StockUiChatInput();
                input.Open();

                using var renderer = new StockUiRenderer(gd, framebuffer.OutputDescription);
                renderer.Begin(library);
                float frameHeight = log.Frame.Height + StockUiChatWindow.TitleBand;
                const float width1 = 420, width2 = 660;
                var window1 = new StockUiPlacement(4, height - 4 - frameHeight, 1, false);
                var window2 = new StockUiPlacement(4 + width1 + 2, window1.Y, 1, false);
                int rows1 = StockUiChatWindow.RowsThatFit(frameHeight - inline.Frame.Height - 1, 8);
                Assert.Equal(6, rows1); // as the capture: six rows above the input line
                StockUiChatWindow.DrawLog(renderer, library, log, logFont, font, window1, width1, frameHeight, rows1, window1Lines,
                    timestampMode: 2, scrolledBack: false, "Window 1:Say", selected: false);
                StockUiChatWindow.DrawLog(renderer, library, log, logFont, font, window2, width2, frameHeight,
                    StockUiChatWindow.RowsThatFit(frameHeight - StockUiChatWindow.BottomPadding, 8), window2Lines,
                    timestampMode: 2, scrolledBack: true, "Window 2", selected: false);
                // A timestamp of 0 keeps the caret in its "on" half-second.
                StockUiChatWindow.DrawInput(renderer, library, logFont, inline,
                    new StockUiPlacement(4, window1.Y + (frameHeight - inline.Frame.Height), 1, false), width1, input, 0);
                renderer.End(framebuffer, width, height);
                Assert.True(renderer.LastQuadCount > 200, $"{renderer.LastQuadCount} quads");

                var pixels = ReadBack(gd, color, width, height);
                string? dumpDir = Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP");
                if (!string.IsNullOrEmpty(dumpDir))
                {
                    Directory.CreateDirectory(dumpDir);
                    SavePng(Path.Combine(dumpDir, "chat_log.png"), pixels, (int)width, (int)height);
                }

                // The input line is opaque: its body is the window navy, not the clear colour mixed in.
                var body = Pixel(pixels, width, 300, (int)(window1.Y + frameHeight - 6));
                Assert.True(body.B > body.R + 20, $"input line body {body}");

                framebuffer.Dispose(); color.Dispose(); cl.Dispose();
            }
            finally
            {
                devices.Dispose();
                DestroyWindow(hwnd);
            }
        }

        /// <summary>
        /// Renders the target window (claimed "Island Rarab" at 30%) above the Solo party window, and a row of status
        /// icons, at 1:1 for comparison with a Windower capture; writes target_status.png when GORDIAN_UI_DUMP is set.
        /// </summary>
        [Fact]
        public void RendersTargetWindowAndStatusIcons()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            var font = library != null ? UiFont.FromLibrary(library) : null;
            var icons = StatusIconLibrary.Load(rm);
            if (library == null || font == null || icons == null) return;
            Assert.True(library.TryGetMenu("targetwi", out var target));
            Assert.True(library.TryGetMenu("ptw0", out var solo));
            Assert.True(library.TryGetMenu("buff", out var grid));

            const uint width = 512, height = 448;
            IntPtr hwnd = CreateWindowExW(0, "static", "StockUiTargetTest", unchecked((int)0x80000000), 0, 0, (int)width, (int)height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devices = new VeldridDeviceManager();
            devices.Initialize(Veldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), width, height, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devices.Device;
            if (gd == null) { DestroyWindow(hwnd); return; }

            try
            {
                var format = gd.SwapchainFramebuffer.ColorTargets[0].Target.Format;
                var color = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(width, height, 1, 1, format, Veldrid.TextureUsage.RenderTarget | Veldrid.TextureUsage.Sampled));
                var framebuffer = gd.ResourceFactory.CreateFramebuffer(new Veldrid.FramebufferDescription(null, color));
                var cl = gd.ResourceFactory.CreateCommandList();
                cl.Begin();
                cl.SetFramebuffer(framebuffer);
                cl.ClearColorTarget(0, new Veldrid.RgbaFloat(0.55f, 0.5f, 0.5f, 1.0f));
                cl.End();
                gd.SubmitCommands(cl);

                var layout = new StockUiLayout();
                using var renderer = new StockUiRenderer(gd, framebuffer.OutputDescription);
                renderer.Begin(library);
                var party = layout.Resolve(StockUiWindowIds.Party, solo.Frame, width, height);
                renderer.DrawMenu(solo, party, includeButtons: false);
                StockUiPartyWindow.Draw(renderer, font, solo, party, new[] { new PartyRowVitals("Tarudrake", 9999, 100, 2794, 100, 0, false) }, showTp: false);
                var targetPlacement = layout.Resolve(StockUiWindowIds.Target, target.Frame, width, height) with { Y = party.Y - (target.Frame.Height + 2) };
                renderer.DrawMenu(target, targetPlacement, includeButtons: false);
                StockUiTargetWindow.Draw(renderer, font, target, targetPlacement, "Island Rarab", 30, TargetNameKind.ClaimedByParty);
                StockUiTargetWindow.DrawStatusIcons(renderer, icons, grid, layout.Resolve(StockUiWindowIds.StatusIcons, grid.Frame, width, height),
                    new ushort[] { 42, 42, 40, 40, 43, 116, 41, 41, 42, 42, 44, 91 });
                renderer.End(framebuffer, width, height);

                var pixels = ReadBack(gd, color, width, height);
                string? dumpDir = Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP");
                if (!string.IsNullOrEmpty(dumpDir))
                {
                    Directory.CreateDirectory(dumpDir);
                    SavePng(Path.Combine(dumpDir, "target_status.png"), pixels, (int)width, (int)height);
                }

                // The first status icon lands at (144, 50) (buff frame (142, 48) + slot (2, 2)); its centre differs from the clear colour.
                var icon = Pixel(pixels, width, 144 + 12, 50 + 12);
                Assert.NotEqual(Pixel(pixels, width, 100, 100), icon);

                framebuffer.Dispose(); color.Dispose(); cl.Dispose();
            }
            finally
            {
                devices.Dispose();
                DestroyWindow(hwnd);
            }
        }

        /// <summary>
        /// Renders name plates at several distances: colours from "ncol", the tinted linkshell pearl, the seeking orb,
        /// the new player "?", the job mastery stars, and the target cursor over a name; writes name_plates.png when
        /// GORDIAN_UI_DUMP is set.
        /// </summary>
        [Fact]
        public void RendersNamePlatesWithIconsAndStars()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            var font = library != null ? UiFont.FromLibrary(library) : null;
            if (library == null || font == null) return;

            const uint width = 640, height = 360;
            IntPtr hwnd = CreateWindowExW(0, "static", "StockUiNamePlateTest", unchecked((int)0x80000000), 0, 0, (int)width, (int)height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devices = new VeldridDeviceManager();
            devices.Initialize(Veldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), width, height, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devices.Device;
            if (gd == null) { DestroyWindow(hwnd); return; }

            try
            {
                var format = gd.SwapchainFramebuffer.ColorTargets[0].Target.Format;
                var color = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(width, height, 1, 1, format, Veldrid.TextureUsage.RenderTarget | Veldrid.TextureUsage.Sampled));
                var framebuffer = gd.ResourceFactory.CreateFramebuffer(new Veldrid.FramebufferDescription(null, color));
                var cl = gd.ResourceFactory.CreateCommandList();
                cl.Begin();
                cl.SetFramebuffer(framebuffer);
                cl.ClearColorTarget(0, new Veldrid.RgbaFloat(0.35f, 0.42f, 0.3f, 1.0f));
                cl.End();
                gd.SubmitCommands(cl);

                // 208 pixels per yalm is the local player at ~6 yalms on a 1440-pixel screen (scale 2.67).
                using var renderer = new StockUiRenderer(gd, framebuffer.OutputDescription);
                renderer.Begin(library);
                var pearl = StockUiNamePlates.PearlTint(0x8F, 0xDF, 0xCF);
                StockUiNamePlates.DrawPlate(renderer, font, "Tarudrake", new NamePlateAnchor(1, new Vector2(170, 60), 6, 208),
                    StockUiNamePlates.ColorOf(library, NamePlateColor.Anonymous), NamePlateIcon.Linkshell, pearl, false, width, height);
                StockUiNamePlates.DrawPlate(renderer, font, "Selh'teus", new NamePlateAnchor(2, new Vector2(470, 60), 6, 208),
                    StockUiNamePlates.ColorOf(library, NamePlateColor.Party), NamePlateIcon.None, null, false, width, height);
                StockUiNamePlates.DrawPlate(renderer, font, "Cybin", new NamePlateAnchor(3, new Vector2(170, 170), 8, 156),
                    StockUiNamePlates.ColorOf(library, NamePlateColor.Party), NamePlateIcon.NewPlayer, null, true, width, height);
                StockUiNamePlates.DrawPlate(renderer, font, "Yoran-Oran", new NamePlateAnchor(4, new Vector2(470, 170), 12, 104),
                    StockUiNamePlates.ColorOf(library, NamePlateColor.SeekingParty), NamePlateIcon.SeekingParty, null, false, width, height);
                var rarab = StockUiNamePlates.DrawPlate(renderer, font, "Island Rarab", new NamePlateAnchor(5, new Vector2(170, 290), 20, 62),
                    StockUiNamePlates.ColorOf(library, NamePlateColor.UnclaimedMonster), NamePlateIcon.None, null, false, width, height);
                StockUiNamePlates.DrawPlate(renderer, font, "A.M.A.N. Liaison", new NamePlateAnchor(6, new Vector2(470, 290), 7, 178),
                    StockUiNamePlates.ColorOf(library, NamePlateColor.Npc), NamePlateIcon.Info, null, false, width, height);
                Assert.NotNull(rarab);
                var plate = rarab!.Value;
                var tip = new Vector2(plate.Center.X, Math.Min(plate.Center.Y - plate.GlyphHeight * StockUiNamePlates.CursorGapShare, plate.Top));
                StockUiTargetWindow.DrawCursor(renderer, library, tip, 1.0f, 0);
                renderer.End(framebuffer, width, height);

                var pixels = ReadBack(gd, color, width, height);
                string? dumpDir = Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP");
                if (!string.IsNullOrEmpty(dumpDir))
                {
                    Directory.CreateDirectory(dumpDir);
                    SavePng(Path.Combine(dumpDir, "name_plates.png"), pixels, (int)width, (int)height);
                }

                // Something is drawn along the middle of the first name.
                bool drawn = false;
                var clear = Pixel(pixels, width, 5, 5);
                for (int x = 120; x < 220 && !drawn; x++) drawn = Pixel(pixels, width, x, 60) != clear;
                Assert.True(drawn);

                framebuffer.Dispose(); color.Dispose(); cl.Dispose();
            }
            finally
            {
                devices.Dispose();
                DestroyWindow(hwnd);
            }
        }

        /// <summary>
        /// Name plates are depth-tested against the scene: with the depth buffer at 0.3, a plate at depth 0.5 is hidden,
        /// one at 0.2 shows, and ordinary UI (no depth) draws over everything.
        /// </summary>
        [Fact]
        public void NamePlatesAreHiddenBehindSceneDepth()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            var font = library != null ? UiFont.FromLibrary(library) : null;
            if (library == null || font == null) return;

            const uint width = 480, height = 120;
            IntPtr hwnd = CreateWindowExW(0, "static", "StockUiNamePlateDepthTest", unchecked((int)0x80000000), 0, 0, (int)width, (int)height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
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
                var cl = gd.ResourceFactory.CreateCommandList();
                cl.Begin();
                cl.SetFramebuffer(framebuffer);
                cl.ClearColorTarget(0, new Veldrid.RgbaFloat(0.35f, 0.42f, 0.3f, 1.0f));
                cl.ClearDepthStencil(0.3f);
                cl.End();
                gd.SubmitCommands(cl);

                using var renderer = new StockUiRenderer(gd, framebuffer.OutputDescription);
                renderer.Begin(library);
                var white = StockUiNamePlates.ColorOf(library, NamePlateColor.Player);
                StockUiNamePlates.DrawPlate(renderer, font, "Behind", new NamePlateAnchor(1, new Vector2(80, 40), 6, 208, 0.5f),
                    white, NamePlateIcon.None, null, false, width, height);
                StockUiNamePlates.DrawPlate(renderer, font, "Front", new NamePlateAnchor(2, new Vector2(240, 40), 6, 208, 0.2f),
                    white, NamePlateIcon.None, null, false, width, height);
                renderer.DrawText(font, "Overlay", 330, 30, 2.0f, white);
                renderer.End(framebuffer, width, height);

                var pixels = ReadBack(gd, color, width, height);
                string? dumpDir = Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP");
                if (!string.IsNullOrEmpty(dumpDir))
                {
                    Directory.CreateDirectory(dumpDir);
                    SavePng(Path.Combine(dumpDir, "name_plates_depth.png"), pixels, (int)width, (int)height);
                }

                var clear = Pixel(pixels, width, 5, 5);
                bool Drawn(int x0, int x1)
                {
                    for (int x = x0; x < x1; x++)
                        for (int y = 25; y < 55; y++)
                            if (Pixel(pixels, width, x, y) != clear) return true;
                    return false;
                }
                Assert.False(Drawn(10, 160));
                Assert.True(Drawn(180, 300));
                Assert.True(Drawn(330, 470));

                framebuffer.Dispose(); depth.Dispose(); color.Dispose(); cl.Dispose();
            }
            finally
            {
                devices.Dispose();
                DestroyWindow(hwnd);
            }
        }

        /// <summary>
        /// Renders an alliance at 1:1 (the retail layout space): your party of six with the alliance leader, the two
        /// alliance windows, the locked-on target window, the target cursor and the opt-in party status icons; writes
        /// alliance_lock.png when GORDIAN_UI_DUMP is set, for comparison with retail alliance captures.
        /// </summary>
        [Fact]
        public void RendersAllianceWindowsLockOverlayAndTargetCursor()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            var font = library != null ? UiFont.FromLibrary(library) : null;
            var icons = StatusIconLibrary.Load(rm);
            if (library == null || font == null || icons == null) return;
            Assert.True(library.TryGetMenu("ptw6", out var own));
            Assert.True(library.TryGetMenu("raid1", out var raid1));
            Assert.True(library.TryGetMenu("raid2", out var raid2));
            Assert.True(library.TryGetMenu("targetwi", out var target));

            const uint width = 512, height = 448;
            IntPtr hwnd = CreateWindowExW(0, "static", "StockUiAllianceTest", unchecked((int)0x80000000), 0, 0, (int)width, (int)height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devices = new VeldridDeviceManager();
            devices.Initialize(Veldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), width, height, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devices.Device;
            if (gd == null) { DestroyWindow(hwnd); return; }

            try
            {
                var format = gd.SwapchainFramebuffer.ColorTargets[0].Target.Format;
                var color = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(width, height, 1, 1, format, Veldrid.TextureUsage.RenderTarget | Veldrid.TextureUsage.Sampled));
                var framebuffer = gd.ResourceFactory.CreateFramebuffer(new Veldrid.FramebufferDescription(null, color));
                var cl = gd.ResourceFactory.CreateCommandList();
                cl.Begin();
                cl.SetFramebuffer(framebuffer);
                cl.ClearColorTarget(0, new Veldrid.RgbaFloat(0.45f, 0.5f, 0.3f, 1.0f));
                cl.End();
                gd.SubmitCommands(cl);

                var layout = new StockUiLayout();
                using var renderer = new StockUiRenderer(gd, framebuffer.OutputDescription);
                renderer.Begin(library);

                // A capture's alliance (retail 2026-09-26): Gigie leads the alliance from your party; Aikiko leads the upper party.
                var ownRows = new[]
                {
                    new PartyRowVitals("Inga", 774, 60, 289, 40, 0, false, StatusIds: new ushort[] { 40, 42, 43 }),
                    new PartyRowVitals("Gigie", 819, 70, 694, 90, 0, true, IsAllianceLeader: true, StatusIds: new ushort[] { 116 }),
                    new PartyRowVitals("Cybin", 857, 72, 350, 50, 0, false),
                    new PartyRowVitals("Ioto", 1053, 100, 783, 100, 0, false),
                    new PartyRowVitals("Kedamonah", 996, 74, 339, 60, 0, false),
                    new PartyRowVitals("Tarudrake", 9999, 100, 2794, 100, 0, false),
                };
                var party = layout.Resolve(StockUiWindowIds.Party, own.Frame, width, height);
                renderer.DrawMenu(own, party, includeButtons: false);
                StockUiPartyWindow.Draw(renderer, font, own, party, ownRows, showTp: false);
                StockUiPartyWindow.DrawStatusIcons(renderer, icons, own, party, ownRows, PartyStatusIconSide.Left);

                var upper = layout.Resolve(StockUiWindowIds.Alliance1, raid1.Frame, width, height);
                renderer.DrawMenu(raid1, upper, includeButtons: false);
                StockUiPartyWindow.DrawAllianceRows(renderer, font, raid1, upper, new[]
                {
                    new PartyRowVitals("Gunshin", 836, 100, 0, 0, 0, false),
                    new PartyRowVitals("Aikiko", 1077, 100, 0, 0, 0, true),
                    new PartyRowVitals("Ngt", 788, 100, 0, 0, 0, false),
                    new PartyRowVitals("Siobahnn", 475, 60, 0, 0, 0, false),
                    new PartyRowVitals("Ajani", 800, 90, 0, 0, 0, false),
                });
                var lower = layout.Resolve(StockUiWindowIds.Alliance2, raid2.Frame, width, height);
                renderer.DrawMenu(raid2, lower, includeButtons: false);
                StockUiPartyWindow.DrawAllianceRows(renderer, font, raid2, lower, new[] { new PartyRowVitals("Myargin", 0, 0, 0, 0, 0, true) });

                var targetPlacement = layout.Resolve(StockUiWindowIds.Target, target.Frame, width, height) with { Y = party.Y - (target.Frame.Height + 2) };
                renderer.DrawMenu(target, targetPlacement, includeButtons: false);
                StockUiTargetWindow.Draw(renderer, font, target, targetPlacement, "Marine Dhalmel", 60, TargetNameKind.ClaimedByParty);
                StockUiTargetWindow.DrawLockOverlay(renderer, library, targetPlacement);

                var tip = new System.Numerics.Vector2(160, 200);
                StockUiTargetWindow.DrawCursor(renderer, library, tip, 1.0f, timestamp: 0);
                renderer.End(framebuffer, width, height);

                var pixels = ReadBack(gd, color, width, height);
                string? dumpDir = Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP");
                if (!string.IsNullOrEmpty(dumpDir))
                {
                    Directory.CreateDirectory(dumpDir);
                    SavePng(Path.Combine(dumpDir, "alliance_lock.png"), pixels, (int)width, (int)height);
                }

                // The alliance windows stack above the target window's authored slot, 2 pixels apart.
                Assert.Equal(upper.Bottom(raid1.Frame.Height) + 2, lower.Y, 3);
                Assert.Equal(252, lower.Bottom(raid2.Frame.Height) + 2, 3);

                // The cursor points down at its tip: the sprite lies above it, nothing below it.
                var clear = Pixel(pixels, width, 100, 20);
                Assert.NotEqual(clear, Pixel(pixels, width, (int)tip.X, (int)tip.Y - 8));
                Assert.Equal(clear, Pixel(pixels, width, (int)tip.X, (int)tip.Y + 6));

                // Status icons sit left of the party window, the first nearest to it.
                Assert.NotEqual(clear, Pixel(pixels, width, (int)party.X - 10, (int)party.Y + 13));

                framebuffer.Dispose(); color.Dispose(); cl.Dispose();
            }
            finally
            {
                devices.Dispose();
                DestroyWindow(hwnd);
            }
        }

        /// <summary>
        /// Renders the unlocked UI's editing overlay (chunk 4b) at 1:1: the party window with its outline and label,
        /// and the empty status icon grid as a filled placeholder; writes unlocked_overlay.png when GORDIAN_UI_DUMP is set.
        /// </summary>
        [Fact]
        public void RendersUnlockedOverlayOutlinesAndPlaceholders()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            var font = library != null ? UiFont.FromLibrary(library) : null;
            if (library == null || font == null) return;
            Assert.True(library.TryGetMenu("ptw0", out var solo));
            Assert.True(library.TryGetMenu("buff", out var grid));

            const uint width = 512, height = 448;
            IntPtr hwnd = CreateWindowExW(0, "static", "StockUiOverlayTest", unchecked((int)0x80000000), 0, 0, (int)width, (int)height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devices = new VeldridDeviceManager();
            devices.Initialize(Veldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), width, height, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devices.Device;
            if (gd == null) { DestroyWindow(hwnd); return; }

            try
            {
                var format = gd.SwapchainFramebuffer.ColorTargets[0].Target.Format;
                var color = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(width, height, 1, 1, format, Veldrid.TextureUsage.RenderTarget | Veldrid.TextureUsage.Sampled));
                var framebuffer = gd.ResourceFactory.CreateFramebuffer(new Veldrid.FramebufferDescription(null, color));
                var cl = gd.ResourceFactory.CreateCommandList();
                cl.Begin();
                cl.SetFramebuffer(framebuffer);
                cl.ClearColorTarget(0, new Veldrid.RgbaFloat(0.2f, 0.3f, 0.2f, 1.0f));
                cl.End();
                gd.SubmitCommands(cl);

                var layout = new StockUiLayout { Unlocked = true };
                var drag = new StockUiDragController();
                drag.BeginFrame(layout, width, height);
                using var renderer = new StockUiRenderer(gd, framebuffer.OutputDescription);
                renderer.Begin(library);

                var party = layout.Resolve(StockUiWindowIds.Party, solo.Frame, width, height);
                renderer.DrawMenu(solo, party, includeButtons: false);
                drag.Register(StockUiWindowIds.Party, solo.Frame, party);
                var button = StockUiDragOverlay.ResetButtonRect(font, width, layout.Scale);
                drag.RegisterButton(StockUiDragController.ResetPositionsButton, button.X, button.Y, button.Width, button.Height, layout.Scale);
                var statusPlacement = layout.Resolve(StockUiWindowIds.StatusIcons, grid.Frame, width, height);
                var status = StockUiTargetWindow.StatusGridExtent(grid, statusPlacement, 0);
                Assert.True(status.Height >= 24 && status.Width >= 9 * 24); // the icon slots, not the frame strip
                drag.Register(StockUiWindowIds.StatusIcons, grid.Frame, statusPlacement, status.X, status.Y, status.Width, status.Height, placeholder: true);
                drag.EndFrame();

                drag.OnMouseMove(party.X + 5, party.Y + 5); // hovering the party window
                Assert.Equal(StockUiWindowIds.Party, drag.HoveredWindow);
                StockUiDragOverlay.Draw(renderer, font, drag.Regions, drag.HoveredWindow, drag.DraggingWindow);
                renderer.End(framebuffer, width, height);

                var pixels = ReadBack(gd, color, width, height);
                string? dumpDir = Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP");
                if (!string.IsNullOrEmpty(dumpDir))
                {
                    Directory.CreateDirectory(dumpDir);
                    SavePng(Path.Combine(dumpDir, "unlocked_overlay.png"), pixels, (int)width, (int)height);
                }

                var clear = Pixel(pixels, width, 10, 200);
                // The status grid has no art of its own: its placeholder fill and outline are what make it visible.
                Assert.NotEqual(clear, Pixel(pixels, width, (int)status.X, (int)status.Y));
                Assert.NotEqual(clear, Pixel(pixels, width, (int)(status.X + status.Width / 2), (int)(status.Y + status.Height / 2)));
                // The party window's outline is brighter than its translucent background beside it.
                var outline = Pixel(pixels, width, (int)party.X, (int)party.Y + 20);
                var body = Pixel(pixels, width, (int)party.X + 6, (int)party.Y + 20);
                Assert.True(outline.R + outline.G + outline.B > body.R + body.G + body.B);

                framebuffer.Dispose(); color.Dispose(); cl.Dispose();
            }
            finally
            {
                devices.Dispose();
                DestroyWindow(hwnd);
            }
        }

        internal static byte[] ReadBack(Veldrid.GraphicsDevice gd, Veldrid.Texture source, uint width, uint height)
        {
            var staging = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(width, height, 1, 1, source.Format, Veldrid.TextureUsage.Staging));
            var cl = gd.ResourceFactory.CreateCommandList();
            cl.Begin();
            cl.CopyTexture(source, staging);
            cl.End();
            gd.SubmitCommands(cl);
            gd.WaitForIdle();

            var map = gd.Map(staging, Veldrid.MapMode.Read);
            var rgba = new byte[width * height * 4];
            bool bgra = source.Format == Veldrid.PixelFormat.B8_G8_R8_A8_UNorm;
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(map.Data + (int)(y * map.RowPitch), rgba, (int)(y * width * 4), (int)(width * 4));
            }
            gd.Unmap(staging);
            if (bgra)
            {
                for (int i = 0; i < rgba.Length; i += 4) (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]);
            }
            staging.Dispose();
            cl.Dispose();
            return rgba;
        }

        private static (byte R, byte G, byte B) Pixel(byte[] rgba, uint width, int x, int y)
        {
            int o = (int)(y * width + x) * 4;
            return (rgba[o], rgba[o + 1], rgba[o + 2]);
        }

        internal static void SavePng(string path, byte[] rgba, int width, int height)
        {
            using var raw = new MemoryStream();
            for (int y = 0; y < height; y++)
            {
                raw.WriteByte(0);
                raw.Write(rgba, y * width * 4, width * 4);
            }
            using var compressed = new MemoryStream();
            using (var z = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true)) raw.WriteTo(z);

            using var file = File.Create(path);
            file.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            var ihdr = new byte[13];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
            ihdr[8] = 8; ihdr[9] = 6;
            WriteChunk(file, "IHDR", ihdr);
            WriteChunk(file, "IDAT", compressed.ToArray());
            WriteChunk(file, "IEND", Array.Empty<byte>());
        }

        private static void WriteChunk(Stream s, string type, byte[] data)
        {
            var header = new byte[8];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, data.Length);
            System.Text.Encoding.ASCII.GetBytes(type).CopyTo(header, 4);
            s.Write(header);
            s.Write(data);
            uint crc = 0xFFFFFFFF;
            foreach (byte b in header.AsSpan(4)) crc = Crc(crc, b);
            foreach (byte b in data) crc = Crc(crc, b);
            var tail = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(tail, crc ^ 0xFFFFFFFF);
            s.Write(tail);
        }

        private static uint Crc(uint crc, byte b)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            return crc;
        }
    }
}
