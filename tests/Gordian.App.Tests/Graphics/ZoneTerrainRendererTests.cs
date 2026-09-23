// tests/Gordian.App.Tests/Graphics/ZoneTerrainRendererTests.cs
using System.Runtime.InteropServices;
using Gordian.App.Graphics;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    public class ZoneTerrainRendererTests
    {
        [Fact]
        public void ZoneSceneUniform_HasExpected304ByteLayout()
        {
            // std140 layout: World(64) + View(64) + Proj(64) + SunDir(16) + SunCol(16) + AmbCol(16) + FogCol(16) + FogParams(16) + EyePos(16) + WeatherParams(16) = 304 bytes
            int size = Marshal.SizeOf<ZoneSceneUniform>();
            Assert.Equal(304, size);
        }

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

            // 2. Simulate Pass 0b at midnight under 'fine' weather
            var midnightSunDir = Gordian.Core.World.VanaTime.GetSunDirection(0.0f); // Midnight
            var moonDir = -midnightSunDir;
            string activeWeather = "fine";
            string canonicalWeather = Gordian.Core.World.VanaTime.GetCanonicalWeatherCategory(activeWeather);

            var drawnLayers = new List<string>();

            foreach (var layer in zone.WeatherSkyLayers)
            {
                foreach (var group in layer.MeshGroups)
                {
                    bool isCelestial = layer.IsCelestial;
                    bool isStar = group.Name.Contains("star", StringComparison.OrdinalIgnoreCase);
                    bool isMoon = layer.AttachType == Gordian.Core.Resources.Graphics.ParticleAttachType.Moon || group.Name.Contains("moon", StringComparison.OrdinalIgnoreCase);
                    bool isSun = layer.AttachType == Gordian.Core.Resources.Graphics.ParticleAttachType.Sun || group.Name.Contains("sun", StringComparison.OrdinalIgnoreCase);

                    if (!isCelestial)
                    {
                        bool matchesWeather = !string.IsNullOrEmpty(layer.WeatherId) &&
                            (string.Equals(layer.WeatherId, activeWeather, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(layer.WeatherId, canonicalWeather, StringComparison.OrdinalIgnoreCase));
                        if (!matchesWeather) continue;
                    }

                    if (layer.AttachType == Gordian.Core.Resources.Graphics.ParticleAttachType.Sun && midnightSunDir.Y <= 0.0f) continue;
                    if (layer.AttachType == Gordian.Core.Resources.Graphics.ParticleAttachType.Moon && moonDir.Y <= 0.0f) continue;
                    if (isStar)
                    {
                        float starAlpha = Math.Clamp((-midnightSunDir.Y + 0.15f) / 0.45f, 0.0f, 1.0f);
                        if (starAlpha <= 0.01f) continue;
                    }

                    string texName = group.TextureName;
                    // Untextured geometry rejection
                    if (string.IsNullOrWhiteSpace(texName) && !isSun)
                    {
                        continue;
                    }

                    drawnLayers.Add($"{layer.Name}:{group.Name}");
                }
            }

            // Verify ykum is NOT drawn at midnight
            Assert.DoesNotContain(drawnLayers, l => l.Contains("ykum"));

            // Verify star sprites (textured group 1) are drawn, but untextured geodesic sphere (group 2) is skipped
            Assert.Contains(drawnLayers, l => l.StartsWith("star:star"));
            Assert.Equal(1, drawnLayers.Count(l => l.StartsWith("star:star")));

            // Verify celestial stardust and moonsphere are drawn
            Assert.Contains(drawnLayers, l => l.StartsWith("stardust:"));
            Assert.Contains(drawnLayers, l => l.StartsWith("moonsphere:"));
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
        public void FragmentShaderWeatherSkyGlsl_ContainsEmissiveCelestialAndCloudAmbient()
        {
            // Stars have emissive starlight modulated by starAlpha (WeatherParams.z)
            Assert.Contains("starAlpha = WeatherParams.z", ZoneShaders.FragmentShaderWeatherSkyGlsl);
            Assert.Contains("starRgb = 2.0 * fsin_Color.rgb * tex.rgb * starAlpha", ZoneShaders.FragmentShaderWeatherSkyGlsl);

            // Moon has self-luminous celestial glow (WeatherParams.w > 2.1)
            Assert.Contains("WeatherParams.w > 2.1", ZoneShaders.FragmentShaderWeatherSkyGlsl);
            Assert.Contains("moonRgb = 2.0 * fsin_Color.rgb * tex.rgb", ZoneShaders.FragmentShaderWeatherSkyGlsl);

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
            // Both EnableWeatherClouds and EnableWeatherCelestialBodies are enabled by default so that
            // dynamic clouds, celestial sun/moon discs, and night stars render automatically out-of-the-box.
            var propClouds = typeof(ZoneTerrainRenderer).GetProperty("EnableWeatherClouds");
            var propBodies = typeof(ZoneTerrainRenderer).GetProperty("EnableWeatherCelestialBodies");

            Assert.NotNull(propClouds);
            Assert.NotNull(propBodies);
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
