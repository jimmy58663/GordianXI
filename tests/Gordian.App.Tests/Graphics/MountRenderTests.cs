// tests/Gordian.App.Tests/Graphics/MountRenderTests.cs
using System;
using System.IO;
using System.Numerics;
using Gordian.App.Graphics;
using Gordian.Core.World;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// Mounted players drawn offscreen on D3D11 against the retail data (#87): the rider sits on the mount's back (one
    /// origin, the riding pack lifting the rider), the mount under them. Set GORDIAN_UI_DUMP to a directory to write
    /// <c>gpu_mounts.png</c>. Skipped without the game install.
    /// </summary>
    public class MountRenderTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const int Width = 960, Height = 480;
        private readonly ITestOutputHelper _out;
        public MountRenderTests(ITestOutputHelper o) => _out = o;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        private static PlayerEntity Rider(uint id, byte race, MountAppearance mount, float x)
        {
            var player = new PlayerEntity(id, (ushort)(id & 0x3FF)) { Hpp = 100, KeepsEventHeight = true, Mount = mount };
            player.Appearance.GrapIdTable[0] = (ushort)((race << 8) | 1);
            // Facing east (heading 0), seen side-on from the south; internal Y is the height (drawn at display -Y).
            player.EventPose = new EventPose(new Vector3(x, -8.2f, 9f), 0f, 0f);
            return player;
        }

        /// <summary>
        /// From the left: a Hume male standing, on a chocobo, on a raptor (mount 1); a Tarutaru and a Galka on a raptor.
        /// Each mounted column has more body pixels than the standing one and reaches higher (the rider on its seat).
        /// </summary>
        [Fact]
        public void RidersSitOnTheirMounts()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(4, out var zone, out var textures)) return;
            var chocobo = MountAppearance.FromWire(MountAppearance.StatusChocobo, 0, 1);
            var raptor = MountAppearance.FromWire(MountAppearance.StatusMount, 2, 2);
            Assert.Equal((MountKind.Mount, (byte)1), (raptor.Kind, raptor.MountId));
            var entities = new WorldEntity[]
            {
                Rider(0x01000001, 1, MountAppearance.None, -8f),
                Rider(0x01000002, 1, chocobo, -4f),
                Rider(0x01000003, 1, raptor, 0.5f),
                Rider(0x01000004, 5, raptor, 5f),
                Rider(0x01000005, 8, raptor, 9.5f),
            };
            IntPtr hwnd = CreateWindowExW(0, "static", "Test", unchecked((int)0x80000000), 0, 0, Width, Height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devMgr = new VeldridDeviceManager();
            devMgr.Initialize(NeoVeldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), Width, Height, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devMgr.Device!;
            try
            {
                var renderer = new ZoneTerrainRenderer(gd);
                renderer.LoadZone(zone, textures);
                var camera = new Gordian.Core.Graphics.ViewportCamera { FarClip = 5000f };
                camera.SetEventView(new Vector3(1f, 9.6f, -4f), new Vector3(1f, 9.4f, 9f), 1.0f, 0f, Width / (float)Height);
                var env = Gordian.Core.Graphics.ZoneEnvironmentSettings.CreateDay();
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
                if (Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP") is { Length: > 0 } dump)
                {
                    Directory.CreateDirectory(dump);
                    StockUiRendererTests.SavePng(Path.Combine(dump, "gpu_mounts.png"), scene, Width, Height);
                    // The same riders running (an event walk speed over the walk limit): rider and mount both run.
                    foreach (var e in entities) e.EventPose = e.EventPose! with { Speed = 5f };
                    StockUiRendererTests.SavePng(Path.Combine(dump, "gpu_mounts_running.png"), Frame(entities), Width, Height);
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
                // The Hume riders and the Galka cover more than the standing Hume; the Tarutaru's raptor is a little smaller than
                // a Hume's (0.85 x its race factor 0.955, round 7), the pair about a standing Hume's size.
                foreach (int c in new[] { 1, 2, 4 })
                {
                    Assert.True(pixels[c] > pixels[0], $"mounted column {c} should cover more than the standing rider ({pixels[c]} vs {pixels[0]})");
                }
                Assert.True(pixels[3] > pixels[0] * 4 / 5, $"the Tarutaru on its raptor ({pixels[3]} vs {pixels[0]})");
                // The Hume rider sits higher on the chocobo and the raptor than the standing Hume's head.
                Assert.True(top[1] < top[0] && top[2] < top[0], $"rider tops {top[1]}, {top[2]} vs standing {top[0]}");
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
