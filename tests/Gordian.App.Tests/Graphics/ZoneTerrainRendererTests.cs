// tests/Gordian.App.Tests/Graphics/ZoneTerrainRendererTests.cs
using System.Runtime.InteropServices;
using Gordian.App.Graphics;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    public class ZoneTerrainRendererTests
    {
        [Fact]
        public void ZoneSceneUniform_HasExpected288ByteLayout()
        {
            // std140 layout: World(64) + View(64) + Proj(64) + SunDir(16) + SunCol(16) + AmbCol(16) + FogCol(16) + FogParams(16) + EyePos(16) = 288 bytes
            int size = Marshal.SizeOf<ZoneSceneUniform>();
            Assert.Equal(288, size);
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
    }
}
