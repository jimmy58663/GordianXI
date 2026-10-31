// tests/Gordian.App.Tests/Graphics/WeaponEffectRenderTests.cs
using System;
using System.IO;
using System.Numerics;
using Gordian.App.Graphics;
using Gordian.Core.World;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// The Afterglow of the REMA and Prime weapons rendered offscreen (#8): Hume males holding a Relic, Mythic, Empyrean,
    /// Ergon and Prime Afterglow weapon glow in their family's colour, and a plain weapon does not. Skipped without the
    /// retail install.
    /// </summary>
    public class WeaponEffectRenderTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const int Width = 600, Height = 200;
        private readonly ITestOutputHelper _out;
        public WeaponEffectRenderTests(ITestOutputHelper o) => _out = o;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        // Main-slot model ids (LandSandBoat item_equipment MId): Ragnarok 119 AG 546 (Relic), Conqueror 555 (Mythic),
        // Caladbolg 573 (Empyrean), Epeolatry AG 833 (Ergon), Prime 918, and the plain Ragnarok 319.
        private static readonly ushort[] Afterglow = { 546, 555, 573, 833, 918 };
        private const ushort PlainWeapon = 319;

        private static PlayerEntity Holder(uint id, ushort mainModel, float x)
        {
            var player = new PlayerEntity(id, (ushort)(id & 0x3FF)) { Hpp = 100, KeepsEventHeight = true };
            player.Appearance.GrapIdTable[0] = (ushort)((1 << 8) | 1);
            player.Appearance.GrapIdTable[6] = (ushort)(0x6000 | mainModel);
            player.EventPose = new EventPose(new Vector3(x, -8.2f, 9f), 0f, 0f);
            return player;
        }

        [Fact]
        public void AfterglowWeapons_GlowInTheirFamilyColour()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(4, out var zone, out var textures)) return;

            var glowing = new WorldEntity[Afterglow.Length];
            var plain = new WorldEntity[Afterglow.Length];
            for (int i = 0; i < Afterglow.Length; i++)
            {
                float x = -4f + i * 2f;
                glowing[i] = Holder(0x01000001u + (uint)i, Afterglow[i], x);
                plain[i] = Holder(0x01000011u + (uint)i, PlainWeapon, x);
            }

            IntPtr hwnd = CreateWindowExW(0, "static", "Test", unchecked((int)0x80000000), 0, 0, Width, Height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devMgr = new VeldridDeviceManager();
            devMgr.Initialize(NeoVeldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), Width, Height, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devMgr.Device!;
            try
            {
                var renderer = new ZoneTerrainRenderer(gd);
                renderer.LoadZone(zone, textures);
                var camera = new Gordian.Engine.Graphics.ViewportCamera { FarClip = 5000f };
                camera.SetEventView(new Vector3(0f, 9.0f, 3.5f), new Vector3(0f, 9.0f, 9f), 1.0f, 0f, Width / (float)Height);
                var env = Gordian.Engine.Graphics.ZoneEnvironmentSettings.CreateDay();
                env.WeatherId = "fine";
                env.SetTimeOfDay(23.0f);
                renderer.SkyDomeRenderer?.UpdateDome(env);
                var colorTarget = gd.SwapchainFramebuffer.ColorTargets[0].Target;
                var rtColor = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(Width, Height, 1, 1, colorTarget.Format, NeoVeldrid.TextureUsage.RenderTarget | NeoVeldrid.TextureUsage.Sampled));
                var rtDepth = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(Width, Height, 1, 1, NeoVeldrid.PixelFormat.R32_Float, NeoVeldrid.TextureUsage.DepthStencil));
                var fb = gd.ResourceFactory.CreateFramebuffer(new NeoVeldrid.FramebufferDescription(rtDepth, rtColor));
                var staging = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(Width, Height, 1, 1, colorTarget.Format, NeoVeldrid.TextureUsage.Staging));
                var cl = gd.ResourceFactory.CreateCommandList();
                byte[] Frame(WorldEntity[] drawn, int frames)
                {
                    for (int i = 0; i < frames; i++) renderer.Render(camera, env, 1 / 60f, Width, Height, entities: drawn, resourceManager: rm, present: false, targetFramebuffer: fb);
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

                byte[] empty = Frame(Array.Empty<WorldEntity>(), 30);
                byte[] without = Frame(plain, 90);
                byte[] with = Frame(glowing, 90);
                if (Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP") is { Length: > 0 } dump)
                {
                    Directory.CreateDirectory(dump);
                    StockUiRendererTests.SavePng(Path.Combine(dump, "gpu_afterglow_plain.png"), without, Width, Height);
                    StockUiRendererTests.SavePng(Path.Combine(dump, "gpu_afterglow.png"), with, Width, Height);
                }

                // Per fifth of the screen (one holder each): the pixels the glow brightens, and the colour it adds.
                int columns = Afterglow.Length;
                // Each holder's screen column (display space is internal (-x, -height, z)).
                var screenX = new float[columns];
                for (int c = 0; c < columns; c++)
                {
                    var clip = Vector4.Transform(new Vector4(4f - c * 2f, 9.2f, 9f, 1f), camera.ViewProjectionMatrix);
                    screenX[c] = (clip.X / clip.W * 0.5f + 0.5f) * Width;
                }
                var changed = new int[columns];
                var added = new Vector3[columns];
                int overBody = 0, glowPixels = 0;
                for (int y = 0; y < Height; y++)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        int i = (y * Width + x) * 4;
                        var d = new Vector3(with[i] - without[i], with[i + 1] - without[i + 1], with[i + 2] - without[i + 2]);
                        if (d.X + d.Y + d.Z < 60f) continue;
                        glowPixels++;
                        // A pixel of the wearer's body (the plain holder differs from the empty view) the glow brightens.
                        if (Math.Abs(without[i] - empty[i]) + Math.Abs(without[i + 1] - empty[i + 1]) + Math.Abs(without[i + 2] - empty[i + 2]) > 30) overBody++;
                        int column = 0;
                        for (int c = 1; c < columns; c++)
                        {
                            if (MathF.Abs(x - screenX[c]) < MathF.Abs(x - screenX[column])) column = c;
                        }
                        if (MathF.Abs(x - screenX[column]) > 30f) continue;
                        changed[column]++;
                        added[column] += Vector3.Max(d, Vector3.Zero);
                    }
                }
                _out.WriteLine($"{overBody} of {glowPixels} brightened pixels lie on a wearer");
                for (int c = 0; c < columns; c++)
                {
                    var mean = changed[c] > 0 ? added[c] / changed[c] : Vector3.Zero;
                    _out.WriteLine($"model {Afterglow[c]} at x {screenX[c]:F0}: {changed[c]} pixels brighter, mean added {mean}");
                }

                // Every holder glows; the colour leans to its family: Relic white, Mythic blue, Empyrean red, Ergon green,
                // Prime purple (red and blue over green).
                for (int c = 0; c < columns; c++) Assert.True(changed[c] > 30, $"model {Afterglow[c]} should glow ({changed[c]} pixels)");
                // The generators' z offset (+0.2, op 0x30) keeps the glow behind its wearer: drawn in front, as before #8 round
                // 2, 32 % of the glow lay over the bodies (477 of 1,495 pixels); pushed back, 23 % (299 of 1,273), at the rim.
                Assert.True(overBody < glowPixels * 0.28f, $"the wearers should cover their glow ({overBody} of {glowPixels} pixels on a body)");
                Vector3 Mean(int c) => added[c] / changed[c];
                Assert.True(Mean(1).Z > Mean(1).X, $"Mythic should be blue: {Mean(1)}");
                Assert.True(Mean(2).X > Mean(2).Y * 1.3f && Mean(2).X > Mean(2).Z * 1.3f, $"Empyrean should be red: {Mean(2)}");
                Assert.True(Mean(3).Y > Mean(3).X * 1.3f && Mean(3).Y > Mean(3).Z * 1.3f, $"Ergon should be green: {Mean(3)}");
                Assert.True(Mean(4).X > Mean(4).Y * 1.5f && Mean(4).Z > Mean(4).Y * 1.5f, $"Prime should be purple: {Mean(4)}");
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
