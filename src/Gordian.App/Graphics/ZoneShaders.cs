// src/Gordian.App/Graphics/ZoneShaders.cs
using System.Numerics;
using System.Runtime.InteropServices;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Uniform buffer structure containing scene transform matrices, directional sun/moon lighting,
    /// authentic FFXI distance fog parameters, and dynamic weather / cloud scroll parameters.
    /// Matched to GLSL std140 layout (352 bytes).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = (int)SizeInBytes)]
    public struct ZoneSceneUniform
    {
        public const uint SizeInBytes = 352;

        public Matrix4x4 World;
        public Matrix4x4 View;
        public Matrix4x4 Projection;
        public Vector4 SunDirection;
        public Vector4 SunColor;
        public Vector4 AmbientColor;
        public Vector4 FogColor;
        public Vector4 FogParams; // X = FogStart, Y = FogEnd, Z = 1 / (FogEnd - FogStart), W = FogDensity
        public Vector4 EyePosition;
        public Vector4 WeatherParams; // X = UVOffset.X, Y = UVOffset.Y, Z = Time, W = IsCelestial (1.0 = bypass fog)
        public Vector4 SkyTextureFactor; // Weather-sky generator color (texture factor), read only by the weather-sky shaders
        public Vector4 SkyLayerParams; // Weather-sky only: X = fog enabled, Y = fog toward black (additive layers)
        public Vector4 MoonColor; // Moonlight color; the moon shines opposite SunDirection
    }

    /// <summary>
    /// SPIR-V cross-compilable GLSL shaders for FFXI 3D zone terrain rendering.
    /// Supports texture sampling, PS2 modulate2x color math, directional sun/moon lighting,
    /// and distance fog blending.
    /// Derived from community research in xi-model-viewer (https://github.com/vekien/xi-model-viewer).
    /// </summary>
    public static class ZoneShaders
    {
        public const string VertexShaderGlsl = @"#version 450

layout(location = 0) in vec3 Position;
layout(location = 1) in vec3 Normal;
layout(location = 2) in vec2 TexCoord;
layout(location = 3) in vec4 Color;

layout(location = 0) out vec3 fsin_WorldPos;
layout(location = 1) out vec3 fsin_Normal;
layout(location = 2) out vec2 fsin_TexCoord;
layout(location = 3) out vec4 fsin_Color;

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
};

void main()
{
    vec4 worldPos = World * vec4(Position, 1.0);
    fsin_WorldPos = worldPos.xyz;
    fsin_Normal = mat3(World) * Normal;
    fsin_TexCoord = TexCoord + WeatherParams.xy;
    fsin_Color = Color;
    gl_Position = Projection * View * worldPos;
}
";

        /// <summary>
        /// Vertex shader for coplanar blended terrain decals (multi-texture sand, grass, and cliff transitions).
        /// Applies a linear, W-scaled depth bias in NDC depth space to prevent z-fighting with the underlying
        /// base terrain, cleanly replacing the legacy D3DRS_ZBIAS / polygonOffset render states.
        /// </summary>
        public const string VertexShaderDecalGlsl = @"#version 450

layout(location = 0) in vec3 Position;
layout(location = 1) in vec3 Normal;
layout(location = 2) in vec2 TexCoord;
layout(location = 3) in vec4 Color;

layout(location = 0) out vec3 fsin_WorldPos;
layout(location = 1) out vec3 fsin_Normal;
layout(location = 2) out vec2 fsin_TexCoord;
layout(location = 3) out vec4 fsin_Color;

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
};

void main()
{
    vec4 worldPos = World * vec4(Position, 1.0);
    fsin_WorldPos = worldPos.xyz;
    fsin_Normal = mat3(World) * Normal;
    fsin_TexCoord = TexCoord + WeatherParams.xy;
    fsin_Color = Color;
    vec4 clipPos = Projection * View * worldPos;
    // Linear W-scaled depth bias: nudges coincident decal geometry slightly towards camera in NDC
    // so coplanar transitions smoothly win depth comparison without z-fighting or jitter.
    gl_Position = vec4(clipPos.xy, clipPos.z - 0.00015 * clipPos.w, clipPos.w);
}
";

        /// <summary>
        /// Vertex shader for translucent water surfaces (ocean planes, rivers, waterfalls).
        /// Applies a linear, W-scaled depth bias in NDC space (equivalent to D3DRS_ZBIAS / polygonOffset(-5, 1))
        /// so shallow water cleanly wins depth testing over coincident seabed and riverbeds at a distance,
        /// resolving distance z-fighting and patchy holes without affecting dry land occlusion.
        /// </summary>
        public const string VertexShaderWaterGlsl = @"#version 450

layout(location = 0) in vec3 Position;
layout(location = 1) in vec3 Normal;
layout(location = 2) in vec2 TexCoord;
layout(location = 3) in vec4 Color;

layout(location = 0) out vec3 fsin_WorldPos;
layout(location = 1) out vec3 fsin_Normal;
layout(location = 2) out vec2 fsin_TexCoord;
layout(location = 3) out vec4 fsin_Color;

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
};

void main()
{
    vec4 worldPos = World * vec4(Position, 1.0);
    fsin_WorldPos = worldPos.xyz;
    fsin_Normal = mat3(World) * Normal;
    fsin_TexCoord = TexCoord + WeatherParams.xy;
    fsin_Color = Color;
    vec4 clipPos = Projection * View * worldPos;
    // Linear W-scaled depth bias: nudges coincident water geometry slightly towards camera in NDC
    // so shallow water surfaces consistently win depth comparison over submerged seabed without distance z-fighting.
    gl_Position = vec4(clipPos.xy, clipPos.z - 0.00025 * clipPos.w, clipPos.w);
}
";

        /// <summary>
        /// Vertex shader for dynamic weather sky shells (clouds) and celestial discs (sun, moon).
        /// Applies continuous UV offset for cloud drift, centers on camera/celestial orbit,
        /// and projects to the far plane behind terrain.
        /// </summary>
        public const string VertexShaderWeatherSkyGlsl = @"#version 450

// Every declared input and varying must be consumed: on D3D11 the cross-compiler strips unused
// ones and the remaining attributes/varyings shift into the wrong registers. Normal is therefore
// omitted here and from ZoneTerrainRenderer's weather-sky vertex layout.
layout(location = 0) in vec3 Position;
layout(location = 1) in vec2 TexCoord;
layout(location = 2) in vec4 Color;

layout(location = 0) out vec2 fsin_TexCoord;
layout(location = 1) out vec4 fsin_Color;
layout(location = 2) out float fsin_Fog;

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
};

void main()
{
    fsin_Color = Color;

    if (WeatherParams.w > 3.5)
    {
        // Screen-space lens-flare sprite centred at WeatherParams.xy (NDC), sized at 1/16 NDC per sprite unit on
        // both axes; card vertices are in display axes (-x, -y, z), so raw +X maps to screen right and raw +Y down.
        fsin_TexCoord = TexCoord;
        fsin_Fog = 1.0;
        gl_Position = vec4(WeatherParams.xy + vec2(-Position.x, Position.y) * 0.0625, 0.9998, 1.0);
        return;
    }

    vec4 worldPos = World * vec4(Position, 1.0);
    fsin_TexCoord = TexCoord + WeatherParams.xy;

    // Linear distance fog factor (1 = clear), only for generators that enable fog.
    float fog = 1.0;
    if (SkyLayerParams.x > 0.5 && FogParams.y > 0.0)
    {
        fog = clamp((FogParams.y - length(worldPos.xyz - EyePosition.xyz)) * FogParams.z, 0.0, 1.0);
    }
    fsin_Fog = fog;

    vec4 clipPos = Projection * View * worldPos;
    gl_Position = vec4(clipPos.xy, clipPos.w * 0.9998, clipPos.w);
}
";

        /// <summary>
        /// Vertex shader for world-space zone effects driven by Section 0x05 generators (sea surfaces, sunset glints on
        /// the water). Paired with <see cref="FragmentShaderWeatherSkyGlsl"/>, whose two modulate-2x stages it feeds.
        /// With lighting enabled (SkyLayerParams.z), the vertex color is lit like the client's particle shader:
        /// clamp(color * ambient + color * N.L * sun + color * N.(-L) * moon), alpha unchanged. Fog is linear by
        /// eye distance when enabled (SkyLayerParams.x). Unlike the sky shaders this keeps real depth.
        /// Particle lighting referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer,
        /// ui/js/particleDrawer.js, after xim XimParticleShader).
        /// </summary>
        public const string VertexShaderZoneEffectGlsl = @"#version 450

// Every declared input and varying must be consumed (see VertexShaderWeatherSkyGlsl): Normal feeds the lighting.
layout(location = 0) in vec3 Position;
layout(location = 1) in vec3 Normal;
layout(location = 2) in vec2 TexCoord;
layout(location = 3) in vec4 Color;

layout(location = 0) out vec2 fsin_TexCoord;
layout(location = 1) out vec4 fsin_Color;
layout(location = 2) out float fsin_Fog;

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
};

void main()
{
    vec4 worldPos = World * vec4(Position, 1.0);
    fsin_TexCoord = TexCoord + WeatherParams.xy;

    vec3 n = mat3(World) * Normal;
    float nl = length(n);
    n = nl > 1e-5 ? n / nl : vec3(0.0, 1.0, 0.0);
    vec3 L = normalize(SunDirection.xyz);
    vec3 lit = clamp(
        Color.rgb * AmbientColor.rgb +
        Color.rgb * max(dot(n, L), 0.0) * SunColor.rgb +
        Color.rgb * max(dot(n, -L), 0.0) * MoonColor.rgb, 0.0, 1.0);
    fsin_Color = SkyLayerParams.z > 0.5 ? vec4(lit, Color.a) : Color;

    float fog = 1.0;
    if (SkyLayerParams.x > 0.5 && FogParams.y > 0.0)
    {
        fog = clamp((FogParams.y - length(worldPos.xyz - EyePosition.xyz)) * FogParams.z, 0.0, 1.0);
    }
    fsin_Fog = fog;

    gl_Position = Projection * View * worldPos;
}
";

        /// <summary>
        /// Maximum joints in the per-instance skinning palette bound at set 2. Must match
        /// EntityRenderer's palette buffer layout.
        /// </summary>
        public const int MaxPaletteJoints = 200;

        /// <summary>
        /// GPU joint-palette skinning vertex shader for animated entity meshes. Blends up to two
        /// bone influences per vertex (pre-weighted positions, matching FFXI's own SkeletonMesh
        /// format) using a per-instance rotation/translation palette, then outputs the same
        /// varyings as VertexShaderGlsl so all existing fragment shaders are reused unchanged.
        /// Joint indices are passed as floats (cast to int in-shader) rather than an integer vertex
        /// format, for safe cross-compilation across this project's D3D11/Vulkan/Metal/GL backends.
        /// </summary>
        public const string SkinnedVertexShaderGlsl = @"#version 450

layout(location = 0) in vec3 Position0;
layout(location = 1) in vec3 Position1;
layout(location = 2) in vec3 Normal0;
layout(location = 3) in vec3 Normal1;
layout(location = 4) in vec2 Weights;
layout(location = 5) in vec2 Joints;
layout(location = 6) in vec2 TexCoord;
layout(location = 7) in vec4 Color;

layout(location = 0) out vec3 fsin_WorldPos;
layout(location = 1) out vec3 fsin_Normal;
layout(location = 2) out vec2 fsin_TexCoord;
layout(location = 3) out vec4 fsin_Color;

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
};

#define MAX_JOINTS 200
layout(set = 2, binding = 0) uniform JointPalette
{
    vec4 uRot[MAX_JOINTS];
    vec4 uTrans[MAX_JOINTS];
    vec4 uScale[MAX_JOINTS];
};

vec3 qrot(vec4 q, vec3 v)
{
    return v + 2.0 * cross(q.xyz, cross(q.xyz, v) + q.w * v);
}

void main()
{
    int j0 = clamp(int(Joints.x), 0, MAX_JOINTS - 1);
    int j1Raw = int(Joints.y);
    int j1 = clamp(j1Raw, 0, MAX_JOINTS - 1);

    vec3 p0 = qrot(uRot[j0], uScale[j0].xyz * Position0);
    vec3 n0 = qrot(uRot[j0], Normal0);

    vec3 localPos;
    vec3 localNorm;

    if (j1Raw < 0)
    {
        localPos = uTrans[j0].xyz + p0;
        localNorm = n0;
    }
    else
    {
        // Double joint: positions are pre-weighted (p_i = w_i * local), matching the CPU SkinVertex reference.
        vec3 p1 = qrot(uRot[j1], uScale[j1].xyz * Position1);
        vec3 n1 = qrot(uRot[j1], Normal1);
        localPos = p0 + (Weights.x * uTrans[j0].xyz) + p1 + (Weights.y * uTrans[j1].xyz);
        localNorm = (n0 * Weights.x) + (n1 * Weights.y);
    }

    vec4 worldPos = World * vec4(localPos, 1.0);
    fsin_WorldPos = worldPos.xyz;
    fsin_Normal = mat3(World) * localNorm;
    fsin_TexCoord = TexCoord;
    fsin_Color = Color;
    gl_Position = Projection * View * worldPos;
}
";

        public const string FragmentShaderOpaqueGlsl = @"#version 450

layout(location = 0) in vec3 fsin_WorldPos;
layout(location = 1) in vec3 fsin_Normal;
layout(location = 2) in vec2 fsin_TexCoord;
layout(location = 3) in vec4 fsin_Color;

layout(location = 0) out vec4 fsout_Color;

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
};

layout(set = 1, binding = 0) uniform texture2D uTexture;
layout(set = 1, binding = 1) uniform sampler uSampler;

void main()
{
    vec4 tex = texture(sampler2D(uTexture, uSampler), fsin_TexCoord);

    // Directional + Ambient Lighting with baked vertex colors
    vec3 N = normalize(fsin_Normal);
    vec3 L = normalize(SunDirection.xyz);
    float NdotL = max(dot(N, L), 0.0);
    vec3 amb = fsin_Color.rgb * AmbientColor.rgb;
    vec3 df0 = fsin_Color.rgb * NdotL * SunColor.rgb;
    // Moonlight shines opposite the sun (xim terrain lighting: ambient + sun + moon)
    vec3 df1 = fsin_Color.rgb * max(dot(N, -L), 0.0) * MoonColor.rgb;
    vec3 lit = clamp(amb + df0 + df1, 0.0, 1.0);

    // Authentic FFXI PS2 modulate2x color combination
    vec3 litColor = 2.0 * lit * tex.rgb;

    // Authentic FFXI distance fog blending (active when FogParams.y > 0.0)
    vec3 finalRgb = litColor;
    if (FogParams.y > 0.0 && WeatherParams.w < 0.5)
    {
        float dist = distance(EyePosition.xyz, fsin_WorldPos);
        float fogStart = FogParams.x;
        float fogEnd = FogParams.y;
        float fogFactor = clamp((dist - fogStart) / max(0.001, fogEnd - fogStart), 0.0, 1.0);
        finalRgb = mix(litColor, FogColor.rgb, fogFactor);
    }
    fsout_Color = vec4(finalRgb, 1.0);
}
";

        public const string FragmentShaderCutoutGlsl = @"#version 450

layout(location = 0) in vec3 fsin_WorldPos;
layout(location = 1) in vec3 fsin_Normal;
layout(location = 2) in vec2 fsin_TexCoord;
layout(location = 3) in vec4 fsin_Color;

layout(location = 0) out vec4 fsout_Color;

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
};

layout(set = 1, binding = 0) uniform texture2D uTexture;
layout(set = 1, binding = 1) uniform sampler uSampler;

void main()
{
    vec4 tex = texture(sampler2D(uTexture, uSampler), fsin_TexCoord);

    // Alpha-tested foliage discard: 4.0 * vertexAlpha * texAlpha (matches FFXI / xi-model-viewer)
    float alpha = 4.0 * fsin_Color.a * tex.a;
    if (alpha < 0.375)
    {
        discard;
    }

    // Directional + Ambient Lighting with baked vertex colors
    vec3 N = normalize(fsin_Normal);
    vec3 L = normalize(SunDirection.xyz);
    float NdotL = max(dot(N, L), 0.0);
    vec3 amb = fsin_Color.rgb * AmbientColor.rgb;
    vec3 df0 = fsin_Color.rgb * NdotL * SunColor.rgb;
    // Moonlight shines opposite the sun (xim terrain lighting: ambient + sun + moon)
    vec3 df1 = fsin_Color.rgb * max(dot(N, -L), 0.0) * MoonColor.rgb;
    vec3 lit = clamp(amb + df0 + df1, 0.0, 1.0);

    // Authentic FFXI PS2 modulate2x color combination
    vec3 litColor = 2.0 * lit * tex.rgb;

    // Authentic FFXI distance fog blending (active when FogParams.y > 0.0 and not a celestial disc)
    vec3 finalRgb = litColor;
    if (FogParams.y > 0.0 && WeatherParams.w < 0.5)
    {
        float dist = distance(EyePosition.xyz, fsin_WorldPos);
        float fogStart = FogParams.x;
        float fogEnd = FogParams.y;
        float fogFactor = clamp((dist - fogStart) / max(0.001, fogEnd - fogStart), 0.0, 1.0);
        finalRgb = mix(litColor, FogColor.rgb, fogFactor);
    }
    fsout_Color = vec4(finalRgb, 1.0);
}
";

        public const string FragmentShaderBlendGlsl = @"#version 450

layout(location = 0) in vec3 fsin_WorldPos;
layout(location = 1) in vec3 fsin_Normal;
layout(location = 2) in vec2 fsin_TexCoord;
layout(location = 3) in vec4 fsin_Color;

layout(location = 0) out vec4 fsout_Color;

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
};

layout(set = 1, binding = 0) uniform texture2D uTexture;
layout(set = 1, binding = 1) uniform sampler uSampler;

void main()
{
    vec4 tex = texture(sampler2D(uTexture, uSampler), fsin_TexCoord);

    float alpha = clamp(4.0 * fsin_Color.a * tex.a, 0.0, 1.0);
    if (alpha < 0.01)
    {
        discard;
    }

    // Directional + Ambient Lighting with baked vertex colors
    vec3 N = normalize(fsin_Normal);
    vec3 L = normalize(SunDirection.xyz);
    float NdotL = max(dot(N, L), 0.0);
    vec3 amb = fsin_Color.rgb * AmbientColor.rgb;
    vec3 df0 = fsin_Color.rgb * NdotL * SunColor.rgb;
    // Moonlight shines opposite the sun (xim terrain lighting: ambient + sun + moon)
    vec3 df1 = fsin_Color.rgb * max(dot(N, -L), 0.0) * MoonColor.rgb;
    vec3 lit = clamp(amb + df0 + df1, 0.0, 1.0);

    // Authentic FFXI PS2 modulate2x color combination
    vec3 litColor = 2.0 * lit * tex.rgb;

    // Authentic FFXI distance fog blending (active when FogParams.y > 0.0 and not a celestial disc)
    vec3 finalRgb = litColor;
    if (FogParams.y > 0.0 && WeatherParams.w < 0.5)
    {
        float dist = distance(EyePosition.xyz, fsin_WorldPos);
        float fogStart = FogParams.x;
        float fogEnd = FogParams.y;
        float fogFactor = clamp((dist - fogStart) / max(0.001, fogEnd - fogStart), 0.0, 1.0);
        finalRgb = mix(litColor, FogColor.rgb, fogFactor);
    }
    fsout_Color = vec4(finalRgb, alpha);
}
";

        /// <summary>
        /// Fragment shader for Section 0x05 weather sky generators (cloud shells, stars, Milky Way, moon disc and halo,
        /// pole star, sun glow/disc/corona, lens flares).
        /// Uses WeatherParams:
        ///   xy: continuous UV scrolling offset (lens flares: sprite centre in NDC, consumed by the vertex shader)
        ///   z: blend output (0 = straight alpha, 1 = premultiplied for additive / reverse subtract, 2 = darken by alpha)
        ///   w: layer type (4.0 = screen-space lens flare, 3.0 = sky generator; consumed by the vertex shader)
        /// Celestial generators reproduce the client's two modulate-2x texture stages, with the generator
        /// color (day-of-week, moon-phase and time-of-day modulated) as the texture factor.
        /// Stage math referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer,
        /// ui/js/particleDrawer.js, after xim XimParticleShader).
        /// </summary>
        public const string FragmentShaderWeatherSkyGlsl = @"#version 450

layout(location = 0) in vec2 fsin_TexCoord;
layout(location = 1) in vec4 fsin_Color;
layout(location = 2) in float fsin_Fog;

layout(location = 0) out vec4 fsout_Color;

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
};

layout(set = 1, binding = 0) uniform texture2D uTexture;
layout(set = 1, binding = 1) uniform sampler uSampler;

void main()
{
    vec4 tex = texture(sampler2D(uTexture, uSampler), fsin_TexCoord);

    vec4 stage0 = 2.0 * fsin_Color * tex;
    vec3 rgb = clamp(2.0 * stage0.rgb * SkyTextureFactor.rgb, 0.0, 1.0);
    float alpha = clamp(4.0 * stage0.a * SkyTextureFactor.a, 0.0, 1.0);
    rgb = mix(SkyLayerParams.y > 0.5 ? vec3(0.0) : FogColor.rgb, rgb, fsin_Fog);

    if (WeatherParams.z > 1.5)
    {
        // Zero_InvSrc_Add (ZERO, ONE_MINUS_SRC_ALPHA): darken the destination by alpha.
        if (alpha < 0.004)
        {
            discard;
        }
        fsout_Color = vec4(0.0, 0.0, 0.0, alpha);
    }
    else if (WeatherParams.z > 0.5)
    {
        // Src_One_Add / Src_One_RevSub (SRC_ALPHA, ONE), premultiplied for the One/One additive or
        // reverse-subtract pipeline.
        vec3 premultiplied = rgb * alpha;
        if (max(premultiplied.r, max(premultiplied.g, premultiplied.b)) < 0.002)
        {
            discard;
        }
        fsout_Color = vec4(premultiplied, 0.0);
    }
    else
    {
        // Src_InvSrc_Add (SRC_ALPHA, ONE_MINUS_SRC_ALPHA)
        if (alpha < 0.004)
        {
            discard;
        }
        fsout_Color = vec4(rgb, alpha);
    }
}
";

        public const string FragmentShaderGlsl = FragmentShaderCutoutGlsl;

        /// <summary>
        /// Vertex shader for celestial sky dome rendering.
        /// Centers the hemispherical dome at the camera eye and projects to the far plane.
        /// </summary>
        public const string SkyDomeVertexShaderGlsl = @"#version 450

layout(location = 0) in vec3 Position;
layout(location = 1) in vec4 Color;

layout(location = 0) out vec4 fsin_Color;

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
};

void main()
{
    vec4 worldPos = vec4(Position + EyePosition.xyz, 1.0);
    fsin_Color = Color;
    vec4 clipPos = Projection * View * worldPos;
    gl_Position = vec4(clipPos.xy, clipPos.w * 0.9999, clipPos.w);
}
";

        /// <summary>
        /// Fragment shader for celestial sky dome rendering.
        /// Outputs smooth vertex-interpolated celestial gradient.
        /// </summary>
        public const string SkyDomeFragmentShaderGlsl = @"#version 450

layout(location = 0) in vec4 fsin_Color;
layout(location = 0) out vec4 fsout_Color;

void main()
{
    // Screen-space triangular dither (sub-LSB amplitude: +/- 0.5 / 255.0)
    // Emulates authentic PS2 GS / D3D8 hardware rasterizer dithering to eliminate 8-bit color quantization banding in dark gradients.
    float dither = fract(sin(dot(gl_FragCoord.xy, vec2(12.9898, 78.233))) * 43758.5453) - 0.5;
    vec3 color = clamp(fsin_Color.rgb + (dither / 255.0), 0.0, 1.0);
    fsout_Color = vec4(color, 1.0);
}
";
    }
}
