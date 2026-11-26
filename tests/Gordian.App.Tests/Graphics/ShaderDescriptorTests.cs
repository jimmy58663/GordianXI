using System.Runtime.InteropServices;
using Gordian.App.Graphics;
using NeoVeldrid;
using NeoVeldrid.SPIRV;

namespace Gordian.App.Tests.Graphics;

public class ShaderDescriptorTests
{
    [Fact]
    public void EntityFragment_UsesOnlySceneAndTextureSets()
    {
        Assert.Equal(new[] { (0u, 0u), (1u, 0u), (1u, 1u) }, Bindings(ZoneShaders.FragmentShaderGlsl, ShaderStages.Fragment));
    }

    [Fact]
    public void SkinnedVertex_ReservesSetTwoForJoints()
    {
        Assert.Equal(new[] { (0u, 0u), (2u, 0u) }, Bindings(ZoneShaders.SkinnedVertexShaderGlsl, ShaderStages.Vertex));
    }

    [Fact]
    public void LegacyCutout_RetainsPointLightTableAndReferences()
    {
        Assert.Equal(new[] { (0u, 0u), (1u, 0u), (1u, 1u), (2u, 0u), (2u, 1u) },
            Bindings(ZoneShaders.FragmentShaderCutoutGlsl, ShaderStages.Fragment));
    }

    [Fact]
    public void TerrainVertex_RetainsPointLightTableAndReferences()
    {
        Assert.Equal(new[] { (0u, 0u), (2u, 0u), (2u, 1u) },
            Bindings(TerrainShaders.VertexShaderGlsl, ShaderStages.Vertex));
        Assert.Equal(new[] { (0u, 0u), (1u, 0u), (1u, 1u) },
            Bindings(TerrainShaders.FragmentShaderCutoutGlsl, ShaderStages.Fragment));
    }

    // Inspect real compiled SPIR-V OpDecorate instructions, not GLSL text (which can
    // contain inactive preprocessor branches). Binding=33, DescriptorSet=34.
    private static (uint Set, uint Binding)[] Bindings(string source, ShaderStages stage)
    {
        var bytes = SpirvCompilation.CompileGlslToSpirv(source, "descriptor-regression", stage, new GlslCompileOptions()).SpirvBytes;
        var words = MemoryMarshal.Cast<byte, uint>(bytes.AsSpan());
        var sets = new Dictionary<uint, uint>();
        var bindings = new Dictionary<uint, uint>();
        for (int i = 5; i < words.Length;)
        {
            int length = (int)(words[i] >> 16);
            Assert.True(length > 0);
            if ((words[i] & 0xffff) == 71 && length == 4)
            {
                if (words[i + 2] == 33) bindings[words[i + 1]] = words[i + 3];
                if (words[i + 2] == 34) sets[words[i + 1]] = words[i + 3];
            }
            i += length;
        }
        return sets.Select(pair => (pair.Value, bindings[pair.Key])).OrderBy(x => x.Value).ThenBy(x => x.Item2).ToArray();
    }
}
