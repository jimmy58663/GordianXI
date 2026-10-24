// tests/Gordian.App.Tests/Graphics/FootEffectRenderTests.cs
using System;
using System.IO;
using System.Numerics;
using Gordian.App.Graphics;
using Gordian.Core.World;
using Gordian.Core.World.Collision;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// Footprints rendered offscreen (#40): a footfall on Bibiki Bay's sand leaves the print <c>fmrk</c> (a modulate-2x
    /// decal that darkens the sand in its own hue) with the brief sand puff, and the print fades out over its 180-frame
    /// life. Skipped without the retail install.
    /// </summary>
    public class FootEffectRenderTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const int Width = 320, Height = 240;
        private readonly ITestOutputHelper _out;
        public FootEffectRenderTests(ITestOutputHelper o) => _out = o;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr CreateWindowExW(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int X, int Y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        static extern bool DestroyWindow(IntPtr hWnd);

        /// <summary>A footfall's frame: the actor's facing, tilted onto the slope, lifted off the ground.</summary>
        [Fact]
        public void FootEffectFrame_SitsOnTheGroundAndFollowsItsSlope()
        {
            var ground = new Vector3(10f, 2f, -5f);
            var flat = ZoneTerrainRenderer.FootEffectFrame(0f, ground, Vector3.UnitY);
            Assert.Equal(ground + Vector3.UnitY * ZoneTerrainRenderer.FootprintLift, flat.Translation);
            // Raw model space is Y down: its -Y axis is the ground's up.
            Assert.Equal(1f, Vector3.Dot(Vector3.TransformNormal(-Vector3.UnitY, flat), Vector3.UnitY), 4);

            var slope = Vector3.Normalize(new Vector3(0.3f, 1f, 0f));
            var tilted = ZoneTerrainRenderer.FootEffectFrame(1.2f, ground, slope);
            Assert.Equal(1f, Vector3.Dot(Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitY, tilted)), slope), 4);
            Assert.Equal(ground + slope * ZoneTerrainRenderer.FootprintLift, tilted.Translation);
        }

        [Fact]
        public void BibikiBaySand_FootfallLeavesAPrintThatFades()
        {
            if (!OperatingSystem.IsWindows() || !Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(4, out var zone, out var textures) || zone.Collision is not { } collision) return;
            Assert.NotNull(zone.FootEffects);

            // The largest flat sand floor (internal space: -Y up).
            CollisionTriangle sand = default;
            float best = 0f;
            foreach (var t in collision.Triangles)
            {
                if (t.IsWall || t.Terrain != CollisionTerrain.Sand || t.Normal.Y > -0.97f) continue;
                float area = Vector3.Cross(t.B - t.A, t.C - t.A).Length() * 0.5f;
                if (area > best) { best = area; sand = t; }
            }
            Assert.True(best > 0.5f, $"no flat sand in Bibiki Bay ({best})");
            Vector3 centre = (sand.A + sand.B + sand.C) / 3f;
            var foot = new Vector3(-centre.X, -centre.Y + 0.05f, centre.Z);

            IntPtr hwnd = CreateWindowExW(0, "static", "Test", unchecked((int)0x80000000), 0, 0, Width, Height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devMgr = new VeldridDeviceManager();
            devMgr.Initialize(NeoVeldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero), Width, Height, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devMgr.Device!;
            try
            {
                var world = new WorldState { CurrentZoneId = 4 };
                var renderer = new ZoneTerrainRenderer(gd) { World = world };
                renderer.LoadZone(zone, textures);
                var camera = new Gordian.Engine.Graphics.ViewportCamera { FarClip = 5000f };
                // Straight down from 2.5 yalms over the print.
                camera.Update(foot, 89.0f, 0.0f, 2.5f, (float)Width / Height);
                var env = Gordian.Engine.Graphics.ZoneEnvironmentSettings.CreateDay();
                env.WeatherId = "fine";
                var colorTarget = gd.SwapchainFramebuffer.ColorTargets[0].Target;
                var rtColor = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(Width, Height, 1, 1, colorTarget.Format, NeoVeldrid.TextureUsage.RenderTarget | NeoVeldrid.TextureUsage.Sampled));
                var rtDepth = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(Width, Height, 1, 1, NeoVeldrid.PixelFormat.R32_Float, NeoVeldrid.TextureUsage.DepthStencil));
                var fb = gd.ResourceFactory.CreateFramebuffer(new NeoVeldrid.FramebufferDescription(rtDepth, rtColor));
                var staging = gd.ResourceFactory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(Width, Height, 1, 1, colorTarget.Format, NeoVeldrid.TextureUsage.Staging));
                var cl = gd.ResourceFactory.CreateCommandList();
                void Run(int frames) { for (int i = 0; i < frames; i++) renderer.Render(camera, env, 1 / 60f, Width, Height, entities: Array.Empty<WorldEntity>(), resourceManager: rm, present: false, targetFramebuffer: fb); }
                float[] Luma()
                {
                    cl.Begin(); cl.CopyTexture(rtColor, staging); cl.End(); gd.SubmitCommands(cl); gd.WaitForIdle();
                    var map = gd.Map(staging, NeoVeldrid.MapMode.Read);
                    var luma = new float[Width * Height];
                    var row = new byte[Width * 4];
                    for (int y = 0; y < Height; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(map.Data + (int)(y * map.RowPitch), row, 0, row.Length);
                        for (int x = 0; x < Width; x++) luma[y * Width + x] = (row[x * 4] + row[x * 4 + 1] + row[x * 4 + 2]) / 3f;
                    }
                    gd.Unmap(staging);
                    return luma;
                }
                (float Darker, float Lighter, int Changed) Compare(float[] before, float[] now)
                {
                    float darker = 0f, lighter = 0f;
                    int changed = 0;
                    for (int i = 0; i < before.Length; i++)
                    {
                        float d = now[i] - before[i];
                        darker = MathF.Min(darker, d);
                        lighter = MathF.Max(lighter, d);
                        if (MathF.Abs(d) > 4f) changed++;
                    }
                    return (darker, lighter, changed);
                }

                Run(30);
                var bare = Luma();
                Assert.Equal(CollisionTerrain.Sand, renderer.StartFootfall(foot, 0f, collision, wet: false, rm));
                Assert.Equal(2, renderer.FootEffectCount); // the sand root 0300 and the print
                Run(1);
                _out.WriteLine($"after 1 frame: {renderer.FootEffectCount} footfalls, {renderer.FootParticleCount} particles");
                Run(19);
                _out.WriteLine($"after 20 frames: {renderer.FootEffectCount} footfalls, {renderer.FootParticleCount} particles");
                var printedLuma = Luma();
                var printed = Compare(bare, printedLuma);
                Run(60);
                _out.WriteLine($"after 80 frames: {renderer.FootEffectCount} footfalls, {renderer.FootParticleCount} particles");
                var oneSecondLuma = Luma();
                var oneSecond = Compare(bare, oneSecondLuma);
                // GORDIAN_FOOT_DUMP=<folder> writes the frames as PGM images to look at.
                if (Environment.GetEnvironmentVariable("GORDIAN_FOOT_DUMP") is { } dump)
                {
                    foreach (var (name, frame) in new[] { ("bare.pgm", bare), ("printed.pgm", printedLuma), ("second.pgm", oneSecondLuma) })
                    {
                        using var f = File.Create(Path.Combine(dump, name));
                        f.Write(System.Text.Encoding.ASCII.GetBytes($"P5 {Width} {Height} 255\n"));
                        foreach (float v in frame) f.WriteByte((byte)Math.Clamp(v, 0f, 255f));
                    }
                }
                Run(150);
                var gone = Compare(bare, Luma());
                _out.WriteLine($"sand at {centre} ({best:F1} sq yalms): after 20 frames {printed}, 80 {oneSecond}, 230 {gone}; {renderer.FootEffectCount} footfalls alive");

                // The puff lightens the sand around the print at first; the print darkens it, still after a second, and is gone
                // after its 180-frame life (with the puff long over).
                Assert.True(printed.Changed > 50 && printed.Lighter > 5f, $"the puff and the print should show: {printed}");
                Assert.True(oneSecond.Changed > 30 && oneSecond.Darker < -4f, $"the print should darken the sand a second on: {oneSecond}");
                Assert.True(oneSecond.Lighter < 4f, $"only the print should be left a second on: {oneSecond}");
                Assert.True(gone.Changed < 5, $"the print should be gone after its life: {gone}");
                Assert.Equal(0, renderer.FootEffectCount);
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
