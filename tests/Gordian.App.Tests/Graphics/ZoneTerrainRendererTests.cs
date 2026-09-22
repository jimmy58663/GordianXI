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
        public void FragmentShaders_AreNonEmptyAndValidGlsl()
        {
            Assert.False(string.IsNullOrWhiteSpace(ZoneShaders.FragmentShaderOpaqueGlsl));
            Assert.False(string.IsNullOrWhiteSpace(ZoneShaders.FragmentShaderBlendGlsl));
            Assert.False(string.IsNullOrWhiteSpace(ZoneShaders.FragmentShaderCutoutGlsl));
            Assert.Contains("discard", ZoneShaders.FragmentShaderCutoutGlsl);
            Assert.Contains("discard", ZoneShaders.FragmentShaderBlendGlsl);
        }
    }
}
