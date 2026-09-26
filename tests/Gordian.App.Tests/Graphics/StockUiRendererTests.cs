// tests/Gordian.App.Tests/Graphics/StockUiRendererTests.cs
using System;
using System.IO;
using System.IO.Compression;
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

        private static byte[] ReadBack(Veldrid.GraphicsDevice gd, Veldrid.Texture source, uint width, uint height)
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

        private static void SavePng(string path, byte[] rgba, int width, int height)
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
