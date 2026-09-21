// src/Gordian.App/Graphics/ZoneShaders.cs
using System.Numerics;
using System.Runtime.InteropServices;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Uniform buffer structure containing scene transform matrices, directional sun/moon lighting,
    /// and authentic FFXI distance fog parameters.
    /// Matched to GLSL std140 layout (288 bytes).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 288)]
    public struct ZoneSceneUniform
    {
        public Matrix4x4 World;
        public Matrix4x4 View;
        public Matrix4x4 Projection;
        public Vector4 SunDirection;
        public Vector4 SunColor;
        public Vector4 AmbientColor;
        public Vector4 FogColor;
        public Vector4 FogParams; // X = FogStart, Y = FogEnd, Z = 1 / (FogEnd - FogStart), W = FogDensity
        public Vector4 EyePosition;
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
};

void main()
{
    vec4 worldPos = World * vec4(Position, 1.0);
    fsin_WorldPos = worldPos.xyz;
    fsin_Normal = mat3(World) * Normal;
    fsin_TexCoord = TexCoord;
    fsin_Color = Color;
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
    vec3 lit = clamp(amb + df0, 0.0, 1.0);

    // Authentic FFXI PS2 modulate2x color combination
    vec3 litColor = 2.0 * lit * tex.rgb;

    // Authentic FFXI distance fog blending (active when FogParams.y > 0.0)
    vec3 finalRgb = litColor;
    if (FogParams.y > 0.0)
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
    vec3 lit = clamp(amb + df0, 0.0, 1.0);

    // Authentic FFXI PS2 modulate2x color combination
    vec3 litColor = 2.0 * lit * tex.rgb;

    // Authentic FFXI distance fog blending (active when FogParams.y > 0.0)
    vec3 finalRgb = litColor;
    if (FogParams.y > 0.0)
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
    vec3 lit = clamp(amb + df0, 0.0, 1.0);

    // Authentic FFXI PS2 modulate2x color combination
    vec3 litColor = 2.0 * lit * tex.rgb;

    // Authentic FFXI distance fog blending (active when FogParams.y > 0.0)
    vec3 finalRgb = litColor;
    if (FogParams.y > 0.0)
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
    fsout_Color = vec4(fsin_Color.rgb, 1.0);
}
";
    }
}
