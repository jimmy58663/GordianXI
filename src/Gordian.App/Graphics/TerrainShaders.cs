// src/Gordian.App/Graphics/TerrainShaders.cs
namespace Gordian.App.Graphics
{
    /// <summary>
    /// SPIR-V cross-compiled shaders (D3D11 and Vulkan) for placed zone meshes, lit per vertex (Gouraud) as the legacy
    /// client's fixed-function pipeline does: the vertex shader sums ambient + sun + moon + the placement's point lights,
    /// times the vertex colour, and clamps to 1 at each vertex; the fragment shader interpolates that and applies the
    /// modulate-2x, the highlight shoulder and fog.
    /// <para>
    /// Why per vertex (#7 round 2, F10): retail shows a soft grey band on Valkurm Dunes' sand at the foot of the cliffs
    /// outside Selbina (maintainer capture <c>image-13.png</c>, TESTING-2026-10-18 round 2). The meshes carry no baked
    /// shading there (every vertex colour is the neutral 0x80, offscreen dump 2026-10-19): the band is the cliff-base
    /// vertices, whose normals lean away from the sun, lit dark and interpolated across the large sand triangles. Lit per
    /// pixel, the summed light stays above the clamp over nearly the whole triangle and only a thin line remained.
    /// </para>
    /// Lighting terms referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer, after xim's terrain
    /// shader: ambient + sun + moon opposite the sun).
    /// </summary>
    public static class TerrainShaders
    {
        private const string SceneUniforms = @"
layout(set = 0, binding = 0) uniform ZoneSceneUniforms
{
    mat4 World;
    mat4 View;
    mat4 Projection;
    vec4 SunDirection;
    vec4 SunColor;
    vec4 AmbientColor;
    vec4 FogColor;
    vec4 FogParams;
    vec4 EyePosition;
    vec4 WeatherParams;
    vec4 SkyTextureFactor;
    vec4 SkyLayerParams;
    vec4 MoonColor;
    vec4 TerrainTone;
};
";

        private const string VertexHeader = @"#version 450

layout(location = 0) in vec3 Position;
layout(location = 1) in vec3 Normal;
layout(location = 2) in vec2 TexCoord;
layout(location = 3) in vec4 Color;

layout(location = 0) out vec3 fsin_WorldPos;
layout(location = 1) out vec3 fsin_Lit;
layout(location = 2) out vec2 fsin_TexCoord;
layout(location = 3) out vec4 fsin_Color;
" + SceneUniforms + @"
" + PointLightTableLayout.Glsl + @"

void main()
{
    vec4 worldPos = World * vec4(Position, 1.0);
    vec3 n = mat3(World) * Normal;
    float nl = length(n);
    vec3 N = nl > 1e-5 ? n / nl : vec3(0.0, 1.0, 0.0);
    vec3 L = normalize(SunDirection.xyz);
    // Ambient + sun + moon (opposite the sun) + point lights, times the vertex colour, clamped at the vertex.
    vec3 amb = Color.rgb * AmbientColor.rgb;
    vec3 df0 = Color.rgb * max(dot(N, L), 0.0) * SunColor.rgb;
    vec3 df1 = Color.rgb * max(dot(N, -L), 0.0) * MoonColor.rgb;
    fsin_Lit = clamp(amb + df0 + df1 + PointLighting(worldPos.xyz, N, Color.rgb), 0.0, 1.0);

    fsin_WorldPos = worldPos.xyz;
    fsin_TexCoord = TexCoord + WeatherParams.xy;
    fsin_Color = Color;
    vec4 clipPos = Projection * View * worldPos;
";

        /// <summary>Solid and alpha-tested zone meshes.</summary>
        public const string VertexShaderGlsl = VertexHeader + @"
    gl_Position = clipPos;
}
";

        /// <summary>Coplanar blended decals: a linear W-scaled depth bias so they win against the terrain beneath (D3DRS_ZBIAS).</summary>
        public const string VertexShaderDecalGlsl = VertexHeader + @"
    gl_Position = vec4(clipPos.xy, clipPos.z - 0.00015 * clipPos.w, clipPos.w);
}
";

        /// <summary>Water surfaces: a larger linear W-scaled depth bias so shallow water wins against the seabed at a distance.</summary>
        public const string VertexShaderWaterGlsl = VertexHeader + @"
    gl_Position = vec4(clipPos.xy, clipPos.z - 0.00025 * clipPos.w, clipPos.w);
}
";

        private const string FragmentFooter = @"
// Modulate-2x, the highlight shoulder, then linear distance fog (not for celestial discs).
vec3 Shade(vec3 texel)
{
    vec3 litColor = ToneTerrain(2.0 * fsin_Lit * texel);
    if (FogParams.y > 0.0 && WeatherParams.w < 0.5)
    {
        float dist = distance(EyePosition.xyz, fsin_WorldPos);
        float fogFactor = clamp((dist - FogParams.x) / max(0.001, FogParams.y - FogParams.x), 0.0, 1.0);
        return mix(litColor, FogColor.rgb, fogFactor);
    }
    return litColor;
}
";

        private const string FragmentHeader = @"#version 450

layout(location = 0) in vec3 fsin_WorldPos;
layout(location = 1) in vec3 fsin_Lit;
layout(location = 2) in vec2 fsin_TexCoord;
layout(location = 3) in vec4 fsin_Color;

layout(location = 0) out vec4 fsout_Color;
" + SceneUniforms + @"
layout(set = 1, binding = 0) uniform texture2D uTexture;
layout(set = 1, binding = 1) uniform sampler uSampler;

// TerrainTone: a soft shoulder on the lit colour after the modulate-2x (#7). Below x the colour is untouched; the excess
// above it rolls off toward y instead of clipping at white. y <= x keeps the plain clamp (the retail look).
vec3 ToneTerrain(vec3 c)
{
    float span = TerrainTone.y - TerrainTone.x;
    if (span <= 0.0) return c;
    vec3 over = max(c - TerrainTone.x, vec3(0.0));
    return min(c, TerrainTone.x + over / (1.0 + over / span));
}

// Modulate-2x, the highlight shoulder and fog: defined after main().
vec3 Shade(vec3 texel);
";

        /// <summary>Solid zone meshes: texture alpha ignored, depth written.</summary>
        public const string FragmentShaderOpaqueGlsl = FragmentHeader + @"
void main()
{
    vec4 tex = texture(sampler2D(uTexture, uSampler), fsin_TexCoord);
    // Solid: the vertex alpha does not matter (read so the stage interface keeps the varying).
    float keep = step(-1.0, fsin_Color.a);
    fsout_Color = vec4(Shade(tex.rgb) * keep, 1.0);
}
" + FragmentFooter;

        /// <summary>Alpha-tested meshes (names starting <c>_</c>): 4 x vertex alpha x texel alpha below 0.375 is discarded.</summary>
        public const string FragmentShaderCutoutGlsl = FragmentHeader + @"
void main()
{
    vec4 tex = texture(sampler2D(uTexture, uSampler), fsin_TexCoord);
    if (4.0 * fsin_Color.a * tex.a < 0.375) discard;
    fsout_Color = vec4(Shade(tex.rgb), 1.0);
}
" + FragmentFooter;

        /// <summary>Blended meshes (0x8000), decals and water: alpha = 4 x vertex alpha x texel alpha.</summary>
        public const string FragmentShaderBlendGlsl = FragmentHeader + @"
void main()
{
    vec4 tex = texture(sampler2D(uTexture, uSampler), fsin_TexCoord);
    float alpha = clamp(4.0 * fsin_Color.a * tex.a, 0.0, 1.0);
    if (alpha < 0.01) discard;
    fsout_Color = vec4(Shade(tex.rgb), alpha);
}
" + FragmentFooter;
    }
}
