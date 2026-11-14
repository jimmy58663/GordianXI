// tests/Gordian.App.Tests/Graphics/ChairRenderTests.cs
using System;
using System.IO;
using System.Numerics;
using Gordian.App.Graphics;
using Gordian.Core.World;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// Players in a <c>/sitchair</c> status drawn offscreen on D3D11 against the retail data (#379): the race's chair pack
    /// seats the character and the chair model is drawn under it. Set GORDIAN_UI_DUMP to a directory to write
    /// <c>gpu_chairs.png</c>. Skipped without the game install.
    /// </summary>
    public class ChairRenderTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const int Width = 960, Height = 480;
        private readonly ITestOutputHelper _out;
        public ChairRenderTests(ITestOutputHelper o) => _out = o;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        private static PlayerEntity Sitter(uint id, byte race, byte status, float x)
        {
            var player = new PlayerEntity(id, (ushort)(id & 0x3FF)) { Hpp = 100, IsSpawned = true, AnimationState = status };
            player.Appearance.GrapIdTable[0] = (ushort)((race << 8) | 1);
            // Facing east (heading 0), seen side-on from the south; internal Y is the height (drawn at display -Y). Placed by
            // position, not an event pose: an event pose drives the motion itself and would hide the seated status.
            player.Position = player.TargetPosition = new Vector3(x, -8.2f, 9f);
            return player;
        }

        /// <summary>
        /// From the left: a Hume male standing, then in chair 1 (the Imperial Chair), a Tarutaru in chair 6 (the Chocobo Chair),
        /// a Galka in chair 0 (the plain chair) and a Mithra in chair 10 (the Astral Cube). Checked by eye in the dump
        /// (2026-11-13): each sits on its chair's seat, the Tarutaru's feet hanging.
        /// </summary>
        [Fact]
        public void SittersSitOnTheirChairs()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(4, out var zone, out var textures)) return;
            var entities = new WorldEntity[]
            {
                Sitter(0x01000001, 1, 0, -8f),
                Sitter(0x01000002, 1, 64, -4f),
                Sitter(0x01000003, 5, 69, 0.5f),
                Sitter(0x01000004, 8, 63, 5f),
                Sitter(0x01000005, 7, 73, 9.5f),
            };
            IntPtr hwnd = CreateWindowExW(0, "static", "Test", unchecked((int)0x80000000), 0, 0, Width, Height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devMgr = new VeldridDeviceManager();
            devMgr.Initialize(NeoVeldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), Width, Height, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devMgr.Device!;
            try
            {
                var renderer = new ZoneTerrainRenderer(gd);
                renderer.LoadZone(zone, textures);
                var camera = new Gordian.Engine.Graphics.ViewportCamera { FarClip = 5000f };
                camera.SetEventView(new Vector3(1f, 9.6f, -4f), new Vector3(1f, 9.4f, 9f), 1.0f, 0f, Width / (float)Height);
                var env = Gordian.Engine.Graphics.ZoneEnvironmentSettings.CreateDay();
                env.WeatherId = "fine";
                renderer.SkyDomeRenderer?.UpdateDome(env);
                var colorTarget = gd.SwapchainFramebuffer.ColorTargets[0].Target;
                var rtColor = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(Width, Height, 1, 1, colorTarget.Format, NeoVeldrid.TextureUsage.RenderTarget | NeoVeldrid.TextureUsage.Sampled));
                var rtDepth = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(Width, Height, 1, 1, NeoVeldrid.PixelFormat.R32_Float, NeoVeldrid.TextureUsage.DepthStencil));
                var fb = gd.ResourceFactory.CreateFramebuffer(new NeoVeldrid.FramebufferDescription(rtDepth, rtColor));
                var staging = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(Width, Height, 1, 1, colorTarget.Format, NeoVeldrid.TextureUsage.Staging));
                var cl = gd.ResourceFactory.CreateCommandList();
                byte[] Frame(WorldEntity[] drawn)
                {
                    for (int i = 0; i < 3; i++) renderer.Render(camera, env, 0.1f, Width, Height, entities: drawn, resourceManager: rm, present: false, targetFramebuffer: fb);
                    cl.Begin(); cl.CopyTexture(rtColor, staging); cl.End(); gd.SubmitCommands(cl); gd.WaitForIdle();
                    var map = gd.Map(staging, NeoVeldrid.MapMode.Read);
                    var rgba = new byte[Width * Height * 4];
                    for (int y = 0; y < Height; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(map.Data + (int)(y * map.RowPitch), rgba, y * Width * 4, Width * 4);
                    }
                    gd.Unmap(staging);
                    if (colorTarget.Format == NeoVeldrid.PixelFormat.B8_G8_R8_A8_UNorm)
                    {
                        for (int i = 0; i < rgba.Length; i += 4) (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]);
                    }
                    return rgba;
                }
                byte[] scene = Frame(entities), empty = Frame(Array.Empty<WorldEntity>());
                foreach (var e in entities) _out.WriteLine($"{e.ServerId:X} status {e.AnimationState} {e.Animation.Current} {e.Animation.CurrentClip?.Name}");
                if (Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP") is { Length: > 0 } dump)
                {
                    Directory.CreateDirectory(dump);
                    StockUiRendererTests.SavePng(Path.Combine(dump, "gpu_chairs.png"), scene, Width, Height);
                }

                // Body pixels and the topmost body row per fifth of the screen (one entity each).
                var pixels = new int[5];
                var top = new int[] { Height, Height, Height, Height, Height };
                for (int y = 0; y < Height; y++)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        int i = (y * Width + x) * 4;
                        int diff = Math.Abs(scene[i] - empty[i]) + Math.Abs(scene[i + 1] - empty[i + 1]) + Math.Abs(scene[i + 2] - empty[i + 2]);
                        if (diff < 40) continue;
                        int column = Math.Min(4, x * 5 / Width);
                        pixels[column]++;
                        top[column] = Math.Min(top[column], y);
                    }
                }
                _out.WriteLine($"body pixels {string.Join(", ", pixels)}; top rows {string.Join(", ", top)}");
                // Every sitter holds the race's seated chair loop and is drawn; the Hume in the Imperial Chair and the Galka on the
                // plain chair cover more than the standing Hume, the chair under them.
                for (int c = 1; c < 5; c++)
                {
                    Assert.Equal(Gordian.Core.Animation.AnimationCategory.ChairSitting, entities[c].Animation.Current);
                    Assert.Equal("ci1", entities[c].Animation.CurrentClip?.Name);
                    Assert.True(pixels[c] > 0, $"column {c} drew nothing");
                }
                Assert.True(pixels[1] > pixels[0] && pixels[3] > pixels[0], $"chair columns {pixels[1]}, {pixels[3]} vs standing {pixels[0]}");
                renderer.Dispose();
            }
            finally
            {
                devMgr.Dispose();
                DestroyWindow(hwnd);
            }
        }
    }
}
