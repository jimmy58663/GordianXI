// src/Gordian.App/Graphics/BlurAuraShaders.cs
namespace Gordian.App.Graphics
{
    /// <summary>
    /// SPIR-V cross-compiled shaders (D3D11 and Vulkan) for the blur aura pass (#82, Section 0x5E): a skinned shell of the
    /// actor pushed out along its normals, lit only at its silhouette (a rim term) and drawn additively, once per aura ring.
    /// The vertex shader skins exactly like <see cref="ZoneShaders.SkinnedVertexShaderGlsl"/>; every input and varying is
    /// consumed so the D3D11 cross-compile keeps the stage interface (see <see cref="ZoneShaders.VertexShaderWeatherSkyGlsl"/>).
    /// Scene uniform use: WeatherParams = (screen offset x, y in NDC, shell width in yalms, rim exponent);
    /// SkyTextureFactor = (ring tint, strength); SkyLayerParams = (the section's base colour, unused).
    /// </summary>
    public static class BlurAuraShaders
    {
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
layout(location = 3) out float fsin_Strength;

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
        vec3 p1 = qrot(uRot[j1], uScale[j1].xyz * Position1);
        vec3 n1 = qrot(uRot[j1], Normal1);
        localPos = p0 + (Weights.x * uTrans[j0].xyz) + p1 + (Weights.y * uTrans[j1].xyz);
        localNorm = (n0 * Weights.x) + (n1 * Weights.y);
    }

    vec4 worldPos = World * vec4(localPos, 1.0);
    vec3 n = mat3(World) * localNorm;
    float nl = length(n);
    n = nl > 1e-5 ? n / nl : vec3(0.0, 1.0, 0.0);
    // The shell: the surface pushed out by the ring's width.
    worldPos.xyz += n * WeatherParams.z;

    fsin_WorldPos = worldPos.xyz;
    fsin_Normal = n;
    fsin_TexCoord = TexCoord;
    // Vertex colour alpha is 0x80 (0.5) on actor meshes: x2 keeps the ring strength as authored.
    fsin_Strength = SkyTextureFactor.a * clamp(2.0 * Color.a, 0.0, 1.0);

    vec4 clipPos = Projection * View * worldPos;
    // The ghost: each ring is drawn slightly offset on screen, drifting with the shimmer.
    clipPos.xy += WeatherParams.xy * clipPos.w;
    gl_Position = clipPos;
}
";

        public const string FragmentShaderGlsl = @"#version 450

layout(location = 0) in vec3 fsin_WorldPos;
layout(location = 1) in vec3 fsin_Normal;
layout(location = 2) in vec2 fsin_TexCoord;
layout(location = 3) in float fsin_Strength;

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
    // Cut-out texels (feathers, hair; alpha at half scale, 0x80 = opaque) carry no aura.
    vec4 tex = texture(sampler2D(uTexture, uSampler), fsin_TexCoord);
    if (tex.a < 0.09) discard;

    // Silhouette only: strongest where the shell is seen edge-on, nothing face-on.
    vec3 toEye = normalize(EyePosition.xyz - fsin_WorldPos);
    float facing = abs(dot(normalize(fsin_Normal), toEye));
    float rim = pow(clamp(1.0 - facing, 0.0, 1.0), max(WeatherParams.w, 0.5));
    // The glow takes the body's own colour, modulate-2x tinted by the ring (0x80 grey = neutral) and the section's base
    // colour (SkyLayerParams.rgb, also 0x80 = neutral).
    vec3 tint = clamp(4.0 * tex.rgb * SkyTextureFactor.rgb * SkyLayerParams.rgb, 0.0, 1.0);
    vec3 glow = tint * (fsin_Strength * rim);
    if (max(glow.r, max(glow.g, glow.b)) < 0.002) discard;
    // Premultiplied for the additive (One, One) pipeline.
    fsout_Color = vec4(glow, 0.0);
}
";
    }
}
