// tests/Gordian.App.Tests/Graphics/StockUiOffscreen.cs
using System;
using System.IO;
using System.Runtime.InteropServices;
using Gordian.App.Graphics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Ui;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// A D3D11 offscreen target for stock UI window tests: loads the retail UI resources, renders what a test draws over a
    /// green clear colour, reads the frame back and, with GORDIAN_UI_DUMP set, saves it as a PNG. Null (the test is
    /// skipped) without Windows, the retail install or a device.
    /// </summary>
    internal sealed class StockUiOffscreen : IDisposable
    {
        public const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int x, int y,
            int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hwnd);

        private readonly IntPtr _hwnd;
        private readonly VeldridDeviceManager _devices;
        private readonly NeoVeldrid.GraphicsDevice _gd;
        private readonly NeoVeldrid.Texture _color;
        private readonly NeoVeldrid.Framebuffer _framebuffer;
        private readonly NeoVeldrid.CommandList _cl;

        public uint Width { get; }
        public uint Height { get; }
        public ResourceManager Resources { get; }
        public UiResourceLibrary Library { get; }
        public UiFont Font { get; }
        public StockUiLogFont? LogFont { get; }
        public StockUiRenderer Renderer { get; }

        private StockUiOffscreen(uint width, uint height, ResourceManager resources, UiResourceLibrary library, UiFont font, IntPtr hwnd,
            VeldridDeviceManager devices, NeoVeldrid.GraphicsDevice gd)
        {
            Width = width;
            Height = height;
            Resources = resources;
            Library = library;
            Font = font;
            LogFont = StockUiLogFont.FromLibrary(library);
            _hwnd = hwnd;
            _devices = devices;
            _gd = gd;
            var format = gd.SwapchainFramebuffer.ColorTargets[0].Target.Format;
            _color = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(width, height, 1, 1, format, NeoVeldrid.TextureUsage.RenderTarget | NeoVeldrid.TextureUsage.Sampled));
            var depth = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(width, height, 1, 1, NeoVeldrid.PixelFormat.R32_Float, NeoVeldrid.TextureUsage.DepthStencil));
            _framebuffer = gd.ResourceFactory.CreateFramebuffer(new NeoVeldrid.FramebufferDescription(depth, _color));
            _cl = gd.ResourceFactory.CreateCommandList();
            Renderer = new StockUiRenderer(gd, _framebuffer.OutputDescription);
        }

        public static StockUiOffscreen? TryCreate(uint width = 1024, uint height = 768)
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var library = UiResourceLibrary.Load(rm);
            var font = library != null ? UiFont.FromLibrary(library) : null;
            if (library == null || font == null) return null;

            IntPtr hwnd = CreateWindowExW(0, "static", "StockUiOffscreen", unchecked((int)0x80000000), 0, 0, (int)width, (int)height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devices = new VeldridDeviceManager();
            devices.Initialize(NeoVeldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), width, height, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devices.Device;
            if (gd == null)
            {
                devices.Dispose();
                DestroyWindow(hwnd);
                return null;
            }
            return new StockUiOffscreen(width, height, rm, library, font, hwnd, devices, gd);
        }

        /// <summary>Clears, runs <paramref name="draw"/> between the renderer's Begin and End, and returns the RGBA frame.</summary>
        public byte[] Render(Action<StockUiRenderer> draw, string? dumpName = null)
        {
            _cl.Begin();
            _cl.SetFramebuffer(_framebuffer);
            _cl.ClearColorTarget(0, new NeoVeldrid.RgbaFloat(0.16f, 0.24f, 0.16f, 1.0f));
            _cl.End();
            _gd.SubmitCommands(_cl);
            Renderer.Begin(Library);
            draw(Renderer);
            Renderer.End(_framebuffer, Width, Height);
            var shot = StockUiRendererTests.ReadBack(_gd, _color, Width, Height);
            string? dumpDir = Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP");
            if (!string.IsNullOrEmpty(dumpDir) && dumpName != null)
            {
                Directory.CreateDirectory(dumpDir);
                StockUiRendererTests.SavePng(Path.Combine(dumpDir, dumpName), shot, (int)Width, (int)Height);
            }
            return shot;
        }

        public (byte R, byte G, byte B) Pixel(byte[] rgba, int x, int y)
        {
            int o = (int)(y * Width + x) * 4;
            return (rgba[o], rgba[o + 1], rgba[o + 2]);
        }

        /// <summary>Whether any pixel of a horizontal run is light (text drawn in white).</summary>
        public bool HasLightPixel(byte[] rgba, int x0, int x1, int y, int threshold = 180)
        {
            for (int x = x0; x < x1; x++)
            {
                var p = Pixel(rgba, x, y);
                if (p.R > threshold && p.G > threshold && p.B > threshold) return true;
            }
            return false;
        }

        public void Dispose()
        {
            Renderer.Dispose();
            _cl.Dispose();
            _framebuffer.Dispose();
            _color.Dispose();
            _devices.Dispose();
            DestroyWindow(_hwnd);
        }
    }
}
