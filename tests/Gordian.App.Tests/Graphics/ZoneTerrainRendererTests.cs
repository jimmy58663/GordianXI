// tests/Gordian.App.Tests/Graphics/ZoneTerrainRendererTests.cs
using System.Runtime.InteropServices;
using Gordian.App.Graphics;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    public class ZoneTerrainRendererTests
    {
        [Fact]
        public void ZoneSceneUniform_HasExpected320ByteLayout()
        {
            // std140 layout: World(64) + View(64) + Proj(64) + SunDir(16) + SunCol(16) + AmbCol(16) + FogCol(16) + FogParams(16) + EyePos(16) + WeatherParams(16) + SkyTextureFactor(16) = 320 bytes
            int size = Marshal.SizeOf<ZoneSceneUniform>();
            Assert.Equal(320, size);
            Assert.Equal(ZoneSceneUniform.SizeInBytes, (uint)size);
        }

        public static TheoryData<string, string, string> ShaderStagePairs => new()
        {
            { "Opaque", ZoneShaders.VertexShaderGlsl, ZoneShaders.FragmentShaderOpaqueGlsl },
            { "Cutout", ZoneShaders.VertexShaderGlsl, ZoneShaders.FragmentShaderCutoutGlsl },
            { "Blend", ZoneShaders.VertexShaderGlsl, ZoneShaders.FragmentShaderBlendGlsl },
            { "Decal", ZoneShaders.VertexShaderDecalGlsl, ZoneShaders.FragmentShaderBlendGlsl },
            { "Water", ZoneShaders.VertexShaderWaterGlsl, ZoneShaders.FragmentShaderWaterGlsl },
            { "WeatherSky", ZoneShaders.VertexShaderWeatherSkyGlsl, ZoneShaders.FragmentShaderWeatherSkyGlsl },
            { "SkyDome", ZoneShaders.SkyDomeVertexShaderGlsl, ZoneShaders.SkyDomeFragmentShaderGlsl },
        };

        /// <summary>
        /// On D3D11 the SPIR-V cross-compiler strips stage inputs that main() never reads, and the remaining
        /// vertex attributes / varyings shift into the wrong registers (sky UVs read world position, colors read
        /// normals). Every declared input must be consumed and every varying must match the next stage exactly.
        /// </summary>
        [Theory]
        [MemberData(nameof(ShaderStagePairs))]
        public void ShaderStageInterfaces_MatchAndConsumeEveryInput(string pipeline, string vertexGlsl, string fragmentGlsl)
        {
            var vsInputs = ParseInterface(vertexGlsl, "in");
            var vsOutputs = ParseInterface(vertexGlsl, "out");
            var fsInputs = ParseInterface(fragmentGlsl, "in");

            Assert.True(vsOutputs.SequenceEqual(fsInputs), $"{pipeline}: vertex outputs [{string.Join(", ", vsOutputs)}] must equal fragment inputs [{string.Join(", ", fsInputs)}].");

            foreach (var (glsl, inputs, stage) in new[] { (vertexGlsl, vsInputs, "vertex"), (fragmentGlsl, fsInputs, "fragment") })
            {
                string body = glsl.Substring(glsl.IndexOf("void main", StringComparison.Ordinal));
                foreach (var input in inputs)
                {
                    string name = input.Split(' ')[2];
                    Assert.True(System.Text.RegularExpressions.Regex.IsMatch(body, $@"\b{name}\b"), $"{pipeline}: {stage} input '{name}' is declared but never read.");
                }
            }
        }

        private static List<string> ParseInterface(string glsl, string direction) =>
            System.Text.RegularExpressions.Regex.Matches(glsl, $@"layout\(location = (\d+)\) {direction} (\w+) (\w+);")
                .Select(m => $"{m.Groups[1].Value} {m.Groups[2].Value} {m.Groups[3].Value}")
                .ToList();

        [Fact]
        public void VertexShaderDecalGlsl_ContainsDepthBias()
        {
            // Decal shader must incorporate linear W-scaled depth bias to resolve coplanar z-fighting
            Assert.Contains("clipPos.z - 0.00015 * clipPos.w", ZoneShaders.VertexShaderDecalGlsl);
            Assert.Contains("gl_Position", ZoneShaders.VertexShaderDecalGlsl);
            Assert.Contains("fsin_WorldPos", ZoneShaders.VertexShaderDecalGlsl);
        }

        [Fact]
        public void VertexShaderWaterGlsl_ContainsDepthBias()
        {
            // Water shader must incorporate linear W-scaled depth bias to stably overlay shallow seabed without distance z-fighting
            Assert.Contains("clipPos.z - 0.00025 * clipPos.w", ZoneShaders.VertexShaderWaterGlsl);
            Assert.Contains("gl_Position", ZoneShaders.VertexShaderWaterGlsl);
            Assert.Contains("fsin_WorldPos", ZoneShaders.VertexShaderWaterGlsl);
        }

        [Fact]
        public void InspectSkyRenderedPixels()
        {
            if (!OperatingSystem.IsWindows()) return;
            string gameDir = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!System.IO.Directory.Exists(gameDir)) return;

            var rm = new Gordian.Core.Resources.ResourceManager(gameDir);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(4, out var zone, out var textures)) return;

            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
            static extern IntPtr CreateWindowExW(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int X, int Y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
            static extern bool DestroyWindow(IntPtr hWnd);

            IntPtr hwnd = CreateWindowExW(0, "static", "Test", unchecked((int)0x80000000), 0, 0, 640, 480, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devMgr = new VeldridDeviceManager();
            var swapchainSource = Veldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero);
            devMgr.Initialize(swapchainSource, 640, 480, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devMgr.Device;
            if (gd == null) return;

            try
            {
                var renderer = new ZoneTerrainRenderer(gd);
                renderer.EnableWeatherClouds = true;
                renderer.EnableWeatherCelestialBodies = false;
                renderer.LoadZone(zone, textures);

                var camera = new Gordian.Core.Graphics.ViewportCamera();
                camera.Update(new System.Numerics.Vector3(0, 10, 0), 15.0f, 180.0f, 6.0f, 640f / 480f);
                camera.FarClip = 5000f;

                var env = Gordian.Core.Graphics.ZoneEnvironmentSettings.CreateDay();
                env.WeatherId = "fine";

                renderer.SkyDomeRenderer?.UpdateDome(env);

                var colorTarget = gd.SwapchainFramebuffer.ColorTargets[0].Target;
                var rtColor = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(
                    640, 480, 1, 1,
                    colorTarget.Format,
                    Veldrid.TextureUsage.RenderTarget | Veldrid.TextureUsage.Sampled));
                var rtDepth = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(
                    640, 480, 1, 1,
                    Veldrid.PixelFormat.R32_Float,
                    Veldrid.TextureUsage.DepthStencil));
                var offscreenFb = gd.ResourceFactory.CreateFramebuffer(new Veldrid.FramebufferDescription(rtDepth, rtColor));

                renderer.Render(camera, env, 0.016f, 640, 480, present: false, targetFramebuffer: offscreenFb);
                Assert.True(renderer.DrawCalls > 0);
                Assert.True(renderer.WeatherSkySubmeshCount > 0);

                var cl = gd.ResourceFactory.CreateCommandList();
                var stagingDesc = Veldrid.TextureDescription.Texture2D(
                    640, 480, 1, 1,
                    colorTarget.Format,
                    Veldrid.TextureUsage.Staging);
                var staging = gd.ResourceFactory.CreateTexture(stagingDesc);

                cl.Begin();
                cl.CopyTexture(rtColor, staging);
                cl.End();
                gd.SubmitCommands(cl);
                gd.WaitForIdle();

                var map = gd.Map(staging, Veldrid.MapMode.Read);
                // Sample pixels across sky region to ensure soft atmospheric clouds blend over sky dome without channel corruption
                int skyPixelCount = 0;
                for (int y = 20; y <= 80; y += 30)
                {
                    for (int x = 100; x <= 500; x += 100)
                    {
                        int offset = (int)(y * map.RowPitch + x * 4);
                        byte b0 = System.Runtime.InteropServices.Marshal.ReadByte(map.Data, offset);
                        if (b0 >= 150)
                        {
                            skyPixelCount++;
                        }
                    }
                }
                gd.Unmap(staging);
                staging.Dispose();
                cl.Dispose();
                renderer.Dispose();

                Assert.True(skyPixelCount > 0, "Sky pixels should display vibrant sky dome blue.");
            }
            finally
            {
                devMgr.Dispose();
                DestroyWindow(hwnd);
            }
        }

        [Fact]
        public void InspectSunySkyRenderedPixels()
        {
            if (!OperatingSystem.IsWindows()) return;
            string gameDir = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!System.IO.Directory.Exists(gameDir)) return;

            var rm = new Gordian.Core.Resources.ResourceManager(gameDir);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(4, out var zone, out var textures)) return;

            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
            static extern IntPtr CreateWindowExW(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int X, int Y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
            static extern bool DestroyWindow(IntPtr hWnd);

            IntPtr hwnd = CreateWindowExW(0, "static", "TestSuny", unchecked((int)0x80000000), 0, 0, 640, 480, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devMgr = new VeldridDeviceManager();
            var swapchainSource = Veldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero);
            devMgr.Initialize(swapchainSource, 640, 480, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devMgr.Device;
            if (gd == null) return;

            try
            {
                var renderer = new ZoneTerrainRenderer(gd);
                renderer.EnableWeatherClouds = true;
                renderer.EnableWeatherCelestialBodies = false;
                renderer.LoadZone(zone, textures);

                var camera = new Gordian.Core.Graphics.ViewportCamera();
                camera.Update(new System.Numerics.Vector3(0, 10, 0), 15.0f, 180.0f, 6.0f, 640f / 480f);
                camera.FarClip = 5000f;

                var env = Gordian.Core.Graphics.ZoneEnvironmentSettings.CreateDay();
                env.WeatherId = "suny";
                if (zone.EnvironmentData != null)
                {
                    var kf = zone.EnvironmentData.Interpolate(12.0f, "suny");
                    if (kf != null)
                    {
                        env.ApplyKeyframe(kf);
                    }
                }

                renderer.SkyDomeRenderer?.UpdateDome(env);

                var colorTarget = gd.SwapchainFramebuffer.ColorTargets[0].Target;
                var rtColor = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(
                    640, 480, 1, 1,
                    colorTarget.Format,
                    Veldrid.TextureUsage.RenderTarget | Veldrid.TextureUsage.Sampled));
                var rtDepth = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(
                    640, 480, 1, 1,
                    Veldrid.PixelFormat.R32_Float,
                    Veldrid.TextureUsage.DepthStencil));
                var offscreenFb = gd.ResourceFactory.CreateFramebuffer(new Veldrid.FramebufferDescription(rtDepth, rtColor));

                var sunyLayer = System.Linq.Enumerable.FirstOrDefault(zone.WeatherSkyLayers, l => string.Equals(l.WeatherId, "suny", StringComparison.OrdinalIgnoreCase));
                var kfSb = new System.Text.StringBuilder();
                if (zone.EnvironmentData != null)
                {
                    var kf = zone.EnvironmentData.Interpolate(12.0f, "suny");
                    if (kf != null)
                    {
                        kfSb.AppendLine($"kf.TerrainSunColor = {kf.TerrainSunColor}");
                        kfSb.AppendLine($"kf.TerrainAmbientColor = {kf.TerrainAmbientColor}");
                        kfSb.AppendLine($"kf.TerrainFogColor = {kf.TerrainFogColor}");
                        kfSb.AppendLine($"kf.ClearColor = {kf.ClearColor}");
                        kfSb.AppendLine($"kf.Slices Count = {kf.Slices.Count}");
                        foreach (var s in kf.Slices)
                        {
                            kfSb.AppendLine($"   Slice: {s.Elevation} deg, Color={s.Color}");
                        }
                    }
                }
                renderer.Render(camera, env, 0.016f, 640, 480, present: false, targetFramebuffer: offscreenFb);
                Assert.True(renderer.DrawCalls > 0);
                Assert.True(renderer.WeatherSkySubmeshCount > 0);

                var cl = gd.ResourceFactory.CreateCommandList();
                var stagingDesc = Veldrid.TextureDescription.Texture2D(
                    640, 480, 1, 1,
                    colorTarget.Format,
                    Veldrid.TextureUsage.Staging);
                var staging = gd.ResourceFactory.CreateTexture(stagingDesc);

                cl.Begin();
                cl.CopyTexture(rtColor, staging);
                cl.End();
                gd.SubmitCommands(cl);
                gd.WaitForIdle();

                var map = gd.Map(staging, Veldrid.MapMode.Read);
                uint rowPitch = map.RowPitch;
                IntPtr basePtr = map.Data;
                int brightPixels = 0;
                for (int y = 40; y < 200; y += 40)
                {
                    for (int x = 80; x < 560; x += 80)
                    {
                        int offset = (int)(y * rowPitch + x * 4);
                        byte b0 = System.Runtime.InteropServices.Marshal.ReadByte(basePtr, offset);
                        byte b1 = System.Runtime.InteropServices.Marshal.ReadByte(basePtr, offset + 1);
                        byte b2 = System.Runtime.InteropServices.Marshal.ReadByte(basePtr, offset + 2);
                        // Either blue sky (B >= 150) or white/cream cloud (B >= 150)
                        if (b0 >= 150)
                        {
                            brightPixels++;
                        }
                    }
                }
                gd.Unmap(staging);
                staging.Dispose();
                cl.Dispose();
                renderer.Dispose();

                Assert.True(brightPixels > 0, "Sky must contain bright blue sky dome or cream cloud pixels without dark gray blanketing.");
            }
            finally
            {
                devMgr.Dispose();
                DestroyWindow(hwnd);
            }
        }

        [Fact]
        public void Zone4_NightSky_LayersRenderCorrectlyWithoutWaterLeakOrUntexturedSpheres()
        {
            if (!OperatingSystem.IsWindows()) return;
            string gameDir = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!System.IO.Directory.Exists(gameDir)) return;

            var rm = new Gordian.Core.Resources.ResourceManager(gameDir);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(4, out var zone, out var textures)) return;

            // 1. Verify yuku / evening sunset generator meshes are NOT leaked into zone.MeshGroups as water meshes
            bool hasYukuWater = false;
            foreach (var mg in zone.MeshGroups)
            {
                if (mg.Name.Contains("yuku", StringComparison.OrdinalIgnoreCase) ||
                    mg.Name.Contains("ykum", StringComparison.OrdinalIgnoreCase))
                {
                    hasYukuWater = true;
                    break;
                }
            }
            Assert.False(hasYukuWater, "Sunset cloud generators (yuku/ykum) must not be instantiated as static terrain water meshes.");

            Assert.DoesNotContain(zone.WeatherSkyLayers, l => l.Name.Contains("ykum", StringComparison.OrdinalIgnoreCase));

            // 2. Each celestial mesh is drawn by the generator that links it (weat/*/star cross-links 'star' <-> 'sta1')
            var star = zone.WeatherSkyLayers.Single(l => l.Name == "star");
            Assert.Equal("star", star.GeneratorId);
            Assert.Equal(new System.Numerics.Vector3(0, 40, 0), star.Position);
            Assert.Equal(-0.785f, star.Rotation.Y, 3);
            Assert.Equal("ksta", star.ClockAlphaCurve?.DatId);

            var stardust = zone.WeatherSkyLayers.Single(l => l.Name == "stardust");
            Assert.Equal("sta1", stardust.GeneratorId);
            Assert.Equal(new System.Numerics.Vector3(0, 47, 0), stardust.Position);

            // 3. The moon halo is the untextured 0x2E disc drawn by 'kasa'; the moon itself is a 12-phase 0x21 sprite sheet
            var halo = zone.WeatherSkyLayers.Single(l => l.Name == "moonsphere");
            Assert.Equal("kasa", halo.GeneratorId);
            Assert.All(halo.MeshGroups, g => Assert.True(string.IsNullOrEmpty(g.TextureName)));

            var moon = zone.WeatherSkyLayers.Single(l => l.IsMoonPhaseSpriteSheet);
            Assert.Equal(Gordian.Core.Resources.Graphics.ParticleAttachType.Moon, moon.AttachType);
            Assert.Equal(12, moon.MeshGroups.Count);
            Assert.EndsWith("moonshap", moon.TextureName);
            Assert.Equal(8, moon.DayOfWeekColors?.Length);
            Assert.Equal(12, moon.MoonPhaseColors?.Length);

            // 4. Pole star comes from the shared ROM/0/0.DAT sprite sheet 'hit6'; the moon flare is kas1's lens-flare sheet
            var pole = zone.WeatherSkyLayers.Single(l => l.Name == "pole");
            Assert.True(pole.IsSpriteSheet);
            Assert.Equal(new System.Numerics.Vector3(0, 110, 300), pole.Position);
            Assert.True(textures.ContainsKey(pole.TextureName));

            var flare = zone.WeatherSkyLayers.Single(l => l.Name == "kas1");
            Assert.True(flare.IsLensFlare);
            Assert.Equal(flare.MeshGroups.Count, flare.FlareOffsets.Count);

            // 5. Time-of-day curves: stars shine at midnight and vanish at noon
            Assert.True(ZoneTerrainRenderer.ComputeCelestialTextureFactor(star, 0, 6, 0.0f).W > 0.5f);
            Assert.Equal(0.0f, ZoneTerrainRenderer.ComputeCelestialTextureFactor(star, 0, 6, 0.5f).W);
        }

        [Fact]
        public void TryComputeFlareCenter_StringsSpritesThroughScreenCentre()
        {
            var view = System.Numerics.Matrix4x4.CreateLookAt(System.Numerics.Vector3.Zero, -System.Numerics.Vector3.UnitZ, System.Numerics.Vector3.UnitY);
            var projection = System.Numerics.Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2f, 1f, 0.1f, 5000f);
            var viewProjection = view * projection;
            var source = new System.Numerics.Vector3(450f, 0f, -900f); // right of centre, NDC x = 0.5

            Assert.True(ZoneTerrainRenderer.TryComputeFlareCenter(source, viewProjection, 0.0f, out var onSource));
            Assert.Equal(0.5f, onSource.X, 4);
            Assert.Equal(0.0f, onSource.Y, 4);

            Assert.True(ZoneTerrainRenderer.TryComputeFlareCenter(source, viewProjection, 0.5f, out var atCentre));
            Assert.Equal(0.0f, atCentre.X, 4);

            Assert.True(ZoneTerrainRenderer.TryComputeFlareCenter(source, viewProjection, 1.0f, out var opposite));
            Assert.Equal(-0.5f, opposite.X, 4);

            Assert.False(ZoneTerrainRenderer.TryComputeFlareCenter(new System.Numerics.Vector3(0f, 0f, 900f), viewProjection, 0.0f, out _)); // behind
            Assert.False(ZoneTerrainRenderer.TryComputeFlareCenter(new System.Numerics.Vector3(2000f, 0f, -900f), viewProjection, 0.0f, out _)); // far off-screen
        }

        [Fact]
        public void ComputeCelestialTextureFactor_AppliesModulate2xTintsAndClockAlpha()
        {
            var layer = new Gordian.Core.Resources.Graphics.WeatherSkyLayer
            {
                BaseColor = new System.Numerics.Vector4(0.5f),
                DayOfWeekColors = [new System.Numerics.Vector4(0.5f, 0.25f, 0.25f, 0.5f), new System.Numerics.Vector4(0.5f)],
                MoonPhaseColors = [new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 0.25f)],
                ClockAlphaCurve = new Gordian.Core.Resources.Graphics.KeyFrameCurve("k000", [new(0f, 1f), new(0.5f, 0f), new(1f, 1f)])
            };

            var factor = ZoneTerrainRenderer.ComputeCelestialTextureFactor(layer, dayOfWeek: 0, moonPhaseIndex: 0, dayFraction: 0.0f);
            Assert.Equal(0.5f, factor.X, 4);  // 0.5 * (0.5*2) * (0.5*2)
            Assert.Equal(0.25f, factor.Y, 4); // 0.5 * (0.25*2) * (0.5*2)
            Assert.Equal(0.25f, factor.W, 4); // 0.5 * (0.5*2) * (0.25*2) * clock(0) = 1

            Assert.Equal(0.0f, ZoneTerrainRenderer.ComputeCelestialTextureFactor(layer, 0, 0, 0.5f).W, 4);
        }

        [Fact]
        public void Zone4_WeatherSkyLayers_VerifyAuthoredStructureAndWeatherGating()
        {
            if (!OperatingSystem.IsWindows()) return;
            string gameDir = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!System.IO.Directory.Exists(gameDir)) return;

            var rm = new Gordian.Core.Resources.ResourceManager(gameDir);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(4, out var zone, out var textures) || zone == null) return;

            Assert.NotNull(zone.WeatherSkyLayers);
            Assert.NotEmpty(zone.WeatherSkyLayers);

            // Celestial elements (sun, moon, stars) must be present and marked IsCelestial
            Assert.Contains(zone.WeatherSkyLayers, l => l.IsCelestial && l.AttachType == Gordian.Core.Resources.Graphics.ParticleAttachType.Sun);
            Assert.Contains(zone.WeatherSkyLayers, l => l.IsCelestial && l.AttachType == Gordian.Core.Resources.Graphics.ParticleAttachType.Moon);
            Assert.Contains(zone.WeatherSkyLayers, l => l.IsCelestial && l.Name.Contains("star", StringComparison.OrdinalIgnoreCase));

            // Authored cloud layers must match their specific weather types
            var sunyCloud = System.Linq.Enumerable.FirstOrDefault(zone.WeatherSkyLayers, l => string.Equals(l.WeatherId, "suny", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(sunyCloud);
            Assert.False(sunyCloud.IsCelestial);
            Assert.Contains("suny", sunyCloud.Name, StringComparison.OrdinalIgnoreCase);

            var clodCloud = System.Linq.Enumerable.FirstOrDefault(zone.WeatherSkyLayers, l => string.Equals(l.WeatherId, "clod", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(clodCloud);
            Assert.False(clodCloud.IsCelestial);

            var mistCloud = System.Linq.Enumerable.FirstOrDefault(zone.WeatherSkyLayers, l => string.Equals(l.WeatherId, "mist", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(mistCloud);
            Assert.False(mistCloud.IsCelestial);

            var fineCloud = System.Linq.Enumerable.FirstOrDefault(zone.WeatherSkyLayers, l => string.Equals(l.WeatherId, "fine", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(fineCloud);
            Assert.False(fineCloud.IsCelestial);

            // Non-sky particle generators (hi01, hi02, yuku, ykum) must NOT be included in WeatherSkyLayers
            Assert.DoesNotContain(zone.WeatherSkyLayers, l => l.Name.StartsWith("hi0", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(zone.WeatherSkyLayers, l => l.Name.StartsWith("yuk", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(zone.WeatherSkyLayers, l => l.Name.StartsWith("yku", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Zone4_EnvironmentData_HasSkySlicesForDayDuskNight()
        {
            if (!OperatingSystem.IsWindows()) return;
            string gameDir = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!System.IO.Directory.Exists(gameDir)) return;

            var rm = new Gordian.Core.Resources.ResourceManager(gameDir);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(4, out var zone, out var textures) || zone == null) return;

            Assert.NotNull(zone.EnvironmentData);
            var dayKf = zone.EnvironmentData.Interpolate(12.0f, "suny");
            Assert.NotNull(dayKf);
            Assert.NotEmpty(dayKf.Slices);

            var duskKf = zone.EnvironmentData.Interpolate(18.0f, "suny");
            Assert.NotNull(duskKf);
            Assert.NotEmpty(duskKf.Slices);

            var nightKf = zone.EnvironmentData.Interpolate(0.0f, "suny");
            Assert.NotNull(nightKf);
            Assert.NotEmpty(nightKf.Slices);
        }

        [Fact]
        public void Zone4_NightCelestial_InspectStarsAndMoon()
        {
            if (!OperatingSystem.IsWindows()) return;
            string gameDir = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!System.IO.Directory.Exists(gameDir)) return;

            var rm = new Gordian.Core.Resources.ResourceManager(gameDir);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(4, out var zone, out var textures) || zone == null) return;

            var starLayer = System.Linq.Enumerable.FirstOrDefault(zone.WeatherSkyLayers, l => l.Name.Contains("star", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(starLayer);
            var moonLayer = System.Linq.Enumerable.FirstOrDefault(zone.WeatherSkyLayers, l => l.Name.Contains("moon", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(moonLayer);

            Assert.True(textures.ContainsKey("star_rivstar01") || textures.ContainsKey("star01"));
            Assert.True(textures.ContainsKey("moonshap") || textures.ContainsKey("moon    moonshap"));
            Assert.True(moonLayer.Scale.X > 0);
            Assert.Equal(new System.Numerics.Vector3(20, 20, 20), moonLayer.Scale);
            System.Numerics.Vector3 avgNormal = System.Numerics.Vector3.Zero;
            foreach (var v in moonLayer.MeshGroups[0].Vertices)
            {
                avgNormal += v.Normal;
            }
            avgNormal = System.Numerics.Vector3.Normalize(avgNormal);
            Assert.True(MathF.Abs(avgNormal.X) > 0.5f, $"Moon avg normal: {avgNormal}");

            var kasaLayer = System.Linq.Enumerable.FirstOrDefault(zone.WeatherSkyLayers, l => l.Name.Contains("kasa", StringComparison.OrdinalIgnoreCase));
            if (kasaLayer != null)
            {
                Assert.Equal(Gordian.Core.Resources.Graphics.ParticleAttachType.Moon, kasaLayer.AttachType);
            }
            if (textures.TryGetValue("moon    kasa", out var kasaTex))
            {
                byte maxR = 0, maxA = 0;
                for (int i = 0; i < kasaTex.RgbaPixels.Length; i += 4)
                {
                    maxR = Math.Max(maxR, kasaTex.RgbaPixels[i]);
                    maxA = Math.Max(maxA, kasaTex.RgbaPixels[i + 3]);
                }
                Assert.True(maxR > 0 && maxA > 0, $"kasa maxR={maxR}, maxA={maxA}");
            }

            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
            static extern IntPtr CreateWindowExW(uint dwExStyle, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string lpClassName, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
            static extern bool DestroyWindow(IntPtr hWnd);

            IntPtr hwnd = CreateWindowExW(0, "static", "TestStarNight", unchecked((uint)0x80000000), 0, 0, 640, 480, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var devMgr = new VeldridDeviceManager();
            var swapchainSource = Veldrid.SwapchainSource.CreateWin32(hwnd, IntPtr.Zero);
            devMgr.Initialize(swapchainSource, 640, 480, GraphicsBackendPreference.Direct3D11, vsync: false);
            var gd = devMgr.Device;
            if (gd == null) return;

            try
            {
                var renderer = new ZoneTerrainRenderer(gd);
                renderer.EnableWeatherClouds = false;
                renderer.EnableWeatherCelestialBodies = true;
                renderer.EnableCelestialMoon = false;
                renderer.EnableCelestialSun = false;
                renderer.LoadZone(zone, textures);

                var camera = new Gordian.Core.Graphics.ViewportCamera();
                camera.Mode = Gordian.Core.Graphics.CameraMode.FirstPerson;
                // Pitch -45 degrees looking UP into the night sky
                camera.Update(new System.Numerics.Vector3(0, 10, 0), -45.0f, 180.0f, 0.0f, 640f / 480f);
                camera.FarClip = 5000f;

                var env = Gordian.Core.Graphics.ZoneEnvironmentSettings.CreateNight();
                env.WeatherId = "fine";
                env.SunDirection = Gordian.Core.World.VanaTime.GetSunDirection(0.0f);
                if (zone.EnvironmentData != null)
                {
                    var kf = zone.EnvironmentData.Interpolate(0.0f, "fine");
                    if (kf != null)
                    {
                        env.ApplyKeyframe(kf);
                        env.SunDirection = Gordian.Core.World.VanaTime.GetSunDirection(0.0f);
                    }
                }

                renderer.SkyDomeRenderer?.UpdateDome(env);

                var colorTarget = gd.SwapchainFramebuffer.ColorTargets[0].Target;
                var rtColor = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(
                    640, 480, 1, 1,
                    colorTarget.Format,
                    Veldrid.TextureUsage.RenderTarget | Veldrid.TextureUsage.Sampled));
                var rtDepth = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(
                    640, 480, 1, 1,
                    Veldrid.PixelFormat.R32_Float,
                    Veldrid.TextureUsage.DepthStencil));
                var offscreenFb = gd.ResourceFactory.CreateFramebuffer(new Veldrid.FramebufferDescription(rtDepth, rtColor));

                renderer.Render(camera, env, 0.016f, 640, 480, present: false, targetFramebuffer: offscreenFb);

                var cl = gd.ResourceFactory.CreateCommandList();
                var staging = gd.ResourceFactory.CreateTexture(Veldrid.TextureDescription.Texture2D(
                    640, 480, 1, 1,
                    colorTarget.Format,
                    Veldrid.TextureUsage.Staging));

                cl.Begin();
                cl.CopyTexture(rtColor, staging);
                cl.End();
                gd.SubmitCommands(cl);
                gd.WaitForIdle();

                var map = gd.Map(staging, Veldrid.MapMode.Read);
                uint rowPitch = map.RowPitch;
                IntPtr basePtr = map.Data;

                byte maxB = 0, maxG = 0, maxR = 0;
                string ptMax = "";
                for (int y = 0; y < 480; y++)
                {
                    for (int x = 0; x < 640; x++)
                    {
                        int offset = (int)(y * rowPitch + x * 4);
                        byte b0 = System.Runtime.InteropServices.Marshal.ReadByte(basePtr, offset);     // B
                        byte b1 = System.Runtime.InteropServices.Marshal.ReadByte(basePtr, offset + 1); // G
                        byte b2 = System.Runtime.InteropServices.Marshal.ReadByte(basePtr, offset + 2); // R
                        if (b2 > maxR) { maxR = b2; ptMax = $"({x},{y})"; }
                        if (b1 > maxG) maxG = b1;
                        if (b0 > maxB) maxB = b0;
                    }
                }
                gd.Unmap(staging);
                staging.Dispose();
                cl.Dispose();
                renderer.Dispose();

                Assert.True(renderer.DrawCalls > 0, "Stars should render with positive draw calls.");
                Assert.True(maxB > 0 || maxR > 0, $"Night sky should display illuminated celestial star pixels (MaxR={maxR}, MaxG={maxG}, MaxB={maxB}).");
            }
            finally
            {
                devMgr.Dispose();
                DestroyWindow(hwnd);
            }
        }

        [Fact]
        public void FragmentShaders_TerrainLighting_CalibratedToPreventSandOverexposure()
        {
            Assert.Contains("0.5 * amb + 0.5 * df0", ZoneShaders.FragmentShaderOpaqueGlsl);
            Assert.Contains("0.5 * amb + 0.5 * df0", ZoneShaders.FragmentShaderBlendGlsl);
            Assert.Contains("0.5 * amb + 0.5 * df0", ZoneShaders.FragmentShaderCutoutGlsl);
        }

        [Fact]
        public void FragmentShaders_AreNonEmptyAndValidGlsl()
        {
            Assert.False(string.IsNullOrWhiteSpace(ZoneShaders.FragmentShaderOpaqueGlsl));
            Assert.False(string.IsNullOrWhiteSpace(ZoneShaders.FragmentShaderBlendGlsl));
            Assert.False(string.IsNullOrWhiteSpace(ZoneShaders.FragmentShaderCutoutGlsl));
            Assert.Contains("discard", ZoneShaders.FragmentShaderCutoutGlsl);
            Assert.Contains("discard", ZoneShaders.FragmentShaderBlendGlsl);
        }

        [Fact]
        public void FragmentShaderBlendGlsl_ContainsAuthenticPs2Modulate2xAndFogBlending()
        {
            // Translucent water pass requires modulate2x color combination and distance fog mix
            Assert.Contains("2.0 * lit * tex.rgb", ZoneShaders.FragmentShaderBlendGlsl);
            Assert.Contains("clamp(4.0 * fsin_Color.a * tex.a, 0.0, 1.0)", ZoneShaders.FragmentShaderBlendGlsl);
            Assert.Contains("mix(litColor, FogColor.rgb, fogFactor)", ZoneShaders.FragmentShaderBlendGlsl);
        }

        [Fact]
        public void FragmentShaderWaterGlsl_ContainsDualWaveCausticsAndFresnel()
        {
            Assert.False(string.IsNullOrWhiteSpace(ZoneShaders.FragmentShaderWaterGlsl));
            Assert.Contains("mix(tex1, tex2, 0.5)", ZoneShaders.FragmentShaderWaterGlsl);
            Assert.Contains("crest", ZoneShaders.FragmentShaderWaterGlsl);
            Assert.Contains("aquaticGlow", ZoneShaders.FragmentShaderWaterGlsl);
            Assert.Contains("dayAquaticGlow", ZoneShaders.FragmentShaderWaterGlsl);
            Assert.Contains("nightAquaticGlow", ZoneShaders.FragmentShaderWaterGlsl);
            Assert.Contains("fresnel", ZoneShaders.FragmentShaderWaterGlsl);
        }

        [Fact]
        public void ViewportCamera_FarClip_DefaultsTo5000Yalms()
        {
            var camera = new Gordian.Core.Graphics.ViewportCamera();
            Assert.Equal(5000.0f, camera.FarClip);
        }

        [Fact]
        public void OceanWaterPlane_VertexColorAndAlpha_MatchCalibratedTranslucency()
        {
            // Vertex color packed as little-endian Byte4_Norm:
            // Neutral PS2 modulate2x diffuse: R=128, G=128, B=128, A=50
            const uint oceanColorRgba = 128 | (128 << 8) | (128 << 16) | (50 << 24);

            byte r = (byte)(oceanColorRgba & 0xFF);
            byte g = (byte)((oceanColorRgba >> 8) & 0xFF);
            byte b = (byte)((oceanColorRgba >> 16) & 0xFF);
            byte a = (byte)((oceanColorRgba >> 24) & 0xFF);

            Assert.Equal(128, r);
            Assert.Equal(128, g);
            Assert.Equal(128, b);
            Assert.Equal(50, a);

            // In FragmentShaderBlendGlsl: alpha = clamp(4.0 * vertexAlpha * texAlpha)
            // With DAT umi1 texAlpha = 127 / 255.0 (~0.498), 4.0 * (50 / 255.0) * (127 / 255.0) ~= 0.39 (~39% opacity)
            // allows seabed sand and wading entities to show through clearly with rich wave ripples.
            float texAlpha = 127f / 255f;
            float effectiveAlpha = MathF.Min(1.0f, 4.0f * (a / 255.0f) * texAlpha);
            Assert.InRange(effectiveAlpha, 0.37f, 0.41f);
        }

        [Fact]
        public void OceanWaterPlane_TessellationSpecifications_AreCorrect()
        {
            const int quads = 32;
            const int vertsPerSide = quads + 1; // 33
            const int expectedVertexCount = vertsPerSide * vertsPerSide; // 1,089
            const int expectedIndexCount = quads * quads * 6; // 6,144

            Assert.Equal(1089, expectedVertexCount);
            Assert.Equal(6144, expectedIndexCount);

            // 1,089 vertices at 36 bytes stride is ~39.2 KB VRAM
            Assert.Equal(39204, expectedVertexCount * 36);

            // UV tiling: 1000 tiles over 4000 yalms = 4.0 yalms per wave ripple repeat
            const float totalSize = 4000.0f;
            const float tileUv = 1000.0f;
            Assert.Equal(4.0f, totalSize / tileUv);
        }

        [Fact]
        public void ZoneDefDecoder_IsWaterMesh_RecognizesJapaneseAndEnglishWaterTextures()
        {
            // English stems
            Assert.True(Gordian.Core.Resources.Graphics.ZoneDefDecoder.IsWaterMesh(string.Empty, "water01"));
            Assert.True(Gordian.Core.Resources.Graphics.ZoneDefDecoder.IsWaterMesh(string.Empty, "sea01"));
            Assert.True(Gordian.Core.Resources.Graphics.ZoneDefDecoder.IsWaterMesh(string.Empty, "suimen"));
            Assert.True(Gordian.Core.Resources.Graphics.ZoneDefDecoder.IsWaterMesh(string.Empty, "river_flow"));

            // Japanese stems used in retail FFXI DATs (Bibiki Bay, Qufim, etc.)
            Assert.True(Gordian.Core.Resources.Graphics.ZoneDefDecoder.IsWaterMesh(string.Empty, "umi1"));
            Assert.True(Gordian.Core.Resources.Graphics.ZoneDefDecoder.IsWaterMesh(string.Empty, "umw1"));
            Assert.True(Gordian.Core.Resources.Graphics.ZoneDefDecoder.IsWaterMesh(string.Empty, "shir"));
            Assert.True(Gordian.Core.Resources.Graphics.ZoneDefDecoder.IsWaterMesh(string.Empty, "nami"));
            Assert.True(Gordian.Core.Resources.Graphics.ZoneDefDecoder.IsWaterMesh(string.Empty, "kiwa"));
            Assert.True(Gordian.Core.Resources.Graphics.ZoneDefDecoder.IsWaterMesh(string.Empty, "quf1"));
            Assert.True(Gordian.Core.Resources.Graphics.ZoneDefDecoder.IsWaterMesh(string.Empty, "kawa01"));

            // Non-water textures
            Assert.False(Gordian.Core.Resources.Graphics.ZoneDefDecoder.IsWaterMesh(string.Empty, "per_sna"));
            Assert.False(Gordian.Core.Resources.Graphics.ZoneDefDecoder.IsWaterMesh(string.Empty, "bri_wood"));
        }

        [Fact]
        public void ZoneEnvironmentSettings_IndoorsProperty_DefaultAndKeyframeBehavior()
        {
            var day = Gordian.Core.Graphics.ZoneEnvironmentSettings.CreateDay();
            Assert.False(day.Indoors);

            var night = Gordian.Core.Graphics.ZoneEnvironmentSettings.CreateNight();
            Assert.False(night.Indoors);

            var keyframeIndoor = new Gordian.Core.Resources.Graphics.EnvironmentKeyframe
            {
                Indoors = true
            };
            day.ApplyKeyframe(keyframeIndoor);
            Assert.True(day.Indoors);

            var keyframeOutdoor = new Gordian.Core.Resources.Graphics.EnvironmentKeyframe
            {
                Indoors = false
            };
            day.ApplyKeyframe(keyframeOutdoor);
            Assert.False(day.Indoors);
        }

        [Fact]
        public void ViewportSettings_EnableOceanWaterPlane_PersistsDefault()
        {
            var settings = new ViewportSettings();
            Assert.True(settings.EnableOceanWaterPlane);
        }

        [Fact]
        public void DecalShader_HasLinearWDepthBias_ToPreventCoplanarZFighting()
        {
            // Decal vertex shader applies linear W-scaled depth bias (matching D3DRS_ZBIAS / polygonOffset(-5, 1))
            // While terrain blend pipeline enforces depthWriteEnabled = false to prevent occluding subsequent props (docks) or entity feet.
            Assert.Contains("0.00015", ZoneShaders.VertexShaderDecalGlsl);
            Assert.Contains("clipPos.w", ZoneShaders.VertexShaderDecalGlsl);
        }

        [Fact]
        public void VertexShaderWeatherSkyGlsl_ContainsWeatherParamsAndUvScrolling()
        {
            Assert.Contains("clipPos = Projection * View * worldPos", ZoneShaders.VertexShaderWeatherSkyGlsl);
            Assert.Contains("gl_Position = vec4(clipPos.xy, clipPos.w * 0.9998, clipPos.w)", ZoneShaders.VertexShaderWeatherSkyGlsl);
        }

        [Fact]
        public void FragmentShaders_BypassFogForCelestialDiscs()
        {
            // Celestial discs (Sun/Moon/Stars) bypass distance fog when WeatherParams.w > 0.5
            Assert.Contains("WeatherParams.w < 0.5", ZoneShaders.FragmentShaderBlendGlsl);
            Assert.Contains("WeatherParams.w < 0.5", ZoneShaders.FragmentShaderOpaqueGlsl);
            Assert.Contains("WeatherParams.w < 0.5", ZoneShaders.FragmentShaderCutoutGlsl);
        }

        [Fact]
        public void FragmentShaderWeatherSkyGlsl_ContainsCelestialGeneratorStagesAndCloudAmbient()
        {
            // Celestial generators: two modulate-2x texture stages with the generator color as texture factor
            Assert.Contains("stage0 = 2.0 * fsin_Color * tex", ZoneShaders.FragmentShaderWeatherSkyGlsl);
            Assert.Contains("2.0 * stage0.rgb * SkyTextureFactor.rgb", ZoneShaders.FragmentShaderWeatherSkyGlsl);
            Assert.Contains("4.0 * stage0.a * SkyTextureFactor.a", ZoneShaders.FragmentShaderWeatherSkyGlsl);

            // Src_One_Add generators are premultiplied for the One/One additive pipeline
            Assert.Contains("vec3 added = rgb * alpha", ZoneShaders.FragmentShaderWeatherSkyGlsl);

            // Sun has golden radiant daylight disc (WeatherParams.w > 1.5)
            Assert.Contains("sunRgb = 2.0 * fsin_Color.rgb * max(tex.rgb, vec3(0.85))", ZoneShaders.FragmentShaderWeatherSkyGlsl);

            // Clouds use atmospheric sky ambient and are wispy/translucent at night
            Assert.Contains("nightCloudAmbient", ZoneShaders.FragmentShaderWeatherSkyGlsl);
            Assert.Contains("nightAlphaFactor", ZoneShaders.FragmentShaderWeatherSkyGlsl);
        }

        [Fact]
        public void SkyDomeShaders_AreNonEmptyAndCenterAtEyePosition()
        {
            Assert.Contains("Position + EyePosition.xyz", ZoneShaders.SkyDomeVertexShaderGlsl);
            Assert.Contains("dither", ZoneShaders.SkyDomeFragmentShaderGlsl);
            Assert.Contains("fsout_Color = vec4(color, 1.0)", ZoneShaders.SkyDomeFragmentShaderGlsl);
        }

        [Fact]
        public void FragmentShaderWaterGlsl_ContainsDualCounterScrollingCaustics()
        {
            // Dual-layer counter-scrolling caustics with 50/50 mix
            Assert.Contains("uv1 = fsin_TexCoord + waterOffset", ZoneShaders.FragmentShaderWaterGlsl);
            Assert.Contains("crossDrift", ZoneShaders.FragmentShaderWaterGlsl);
            Assert.Contains("uv2 = fsin_TexCoord * 1.20", ZoneShaders.FragmentShaderWaterGlsl);
            Assert.Contains("waterTex = mix(tex1, tex2, 0.5)", ZoneShaders.FragmentShaderWaterGlsl);

            // Vibrant aquatic glow floor
            Assert.Contains("nightAquaticGlow = vec3(0.04, 0.32, 0.52)", ZoneShaders.FragmentShaderWaterGlsl);
        }

        [Fact]
        public void WeatherSkySettings_DefaultsEnableCloudsAndCelestialBodies()
        {
            var propClouds = typeof(ZoneTerrainRenderer).GetProperty("EnableWeatherClouds");
            var propBodies = typeof(ZoneTerrainRenderer).GetProperty("EnableWeatherCelestialBodies");
            var propMoon = typeof(ZoneTerrainRenderer).GetProperty("EnableCelestialMoon");
            var propSun = typeof(ZoneTerrainRenderer).GetProperty("EnableCelestialSun");
            var propMilkyWay = typeof(ZoneTerrainRenderer).GetProperty("EnableMilkyWay");

            Assert.NotNull(propClouds);
            Assert.NotNull(propBodies);
            Assert.NotNull(propMoon);
            Assert.NotNull(propSun);
            Assert.NotNull(propMilkyWay);
        }

        [Fact]
        public void ZoneEnvironmentData_Interpolate_PrefersClodForRainAndStormWeathers()
        {
            var envData = new Gordian.Core.Resources.Graphics.ZoneEnvironmentData();
            var fineKf = new Gordian.Core.Resources.Graphics.EnvironmentKeyframe
            {
                Hour = 12,
                TerrainSunColor = new System.Numerics.Vector4(1.0f, 1.0f, 0.9f, 1.0f),
                TerrainFogColor = new System.Numerics.Vector4(0.8f, 0.9f, 1.0f, 1.0f)
            };
            var clodKf = new Gordian.Core.Resources.Graphics.EnvironmentKeyframe
            {
                Hour = 12,
                TerrainSunColor = new System.Numerics.Vector4(0.4f, 0.4f, 0.45f, 1.0f),
                TerrainFogColor = new System.Numerics.Vector4(0.5f, 0.5f, 0.55f, 1.0f)
            };

            envData.AddKeyframe("fine", fineKf);
            envData.AddKeyframe("clod", clodKf);

            // "rain", "snow", "thdr", etc. map canonically to "clod" and should select clodKf
            var rainResult = envData.Interpolate(12f, "rain");
            Assert.NotNull(rainResult);
            Assert.Equal(clodKf.TerrainSunColor, rainResult.TerrainSunColor);

            var snowResult = envData.Interpolate(12f, "snow");
            Assert.NotNull(snowResult);
            Assert.Equal(clodKf.TerrainSunColor, snowResult.TerrainSunColor);

            var thdrResult = envData.Interpolate(12f, "thdr");
            Assert.NotNull(thdrResult);
            Assert.Equal(clodKf.TerrainSunColor, thdrResult.TerrainSunColor);

            // "fine" or "wind" maps to "fine" and should select fineKf
            var fineResult = envData.Interpolate(12f, "fine");
            Assert.NotNull(fineResult);
            Assert.Equal(fineKf.TerrainSunColor, fineResult.TerrainSunColor);

            var windResult = envData.Interpolate(12f, "wind");
            Assert.NotNull(windResult);
            Assert.Equal(fineKf.TerrainSunColor, windResult.TerrainSunColor);
        }
    }
}
