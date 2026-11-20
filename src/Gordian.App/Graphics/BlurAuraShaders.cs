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
    /// <para>
    /// The edge blur (#82 round 4) is three passes: <see cref="LayerFragmentShaderGlsl"/> draws the avatars alone into a
    /// half-size layer, keeping their own pixels from a copy of the scene drawn so far (set 3); <see cref="BlurFragmentShaderGlsl"/>
    /// blurs the layer, once across and once down; <see cref="CompositeFragmentShaderGlsl"/> lays the blurred layer over the
    /// scene, premultiplied, the body itself kept sharp. Where it runs, the additive glow is not drawn.
    /// </para>
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

        /// <summary>
        /// The edge blur's layer (#82 round 4): the avatar's own pixels of the scene drawn so far (set 3, read at this pixel of
        /// the half-size layer: <c>SkyLayerParams.x</c> is the downscale), premultiplied by the aura's opacity
        /// (<c>SkyTextureFactor.a</c>). Cut-out texels are left out, as the glow leaves them.
        /// </summary>
        public const string LayerFragmentShaderGlsl = @"#version 450

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
layout(set = 3, binding = 0) uniform texture2D SceneTex;
layout(set = 3, binding = 1) uniform sampler SceneSampler;

void main()
{
    vec4 tex = texture(sampler2D(uTexture, uSampler), fsin_TexCoord);
    if (tex.a < 0.09) discard;
    // Every varying is read so the D3D11 cross-compile keeps the stage interface; this never discards.
    if (fsin_Strength < -1.0 || dot(fsin_Normal, fsin_Normal) < 0.0 || fsin_WorldPos.x != fsin_WorldPos.x) discard;

    vec2 size = vec2(textureSize(sampler2D(SceneTex, SceneSampler), 0));
    vec2 uv = gl_FragCoord.xy * max(SkyLayerParams.x, 1.0) / size;
    vec3 color = texture(sampler2D(SceneTex, SceneSampler), uv).rgb;
    float opacity = clamp(SkyTextureFactor.a, 0.0, 1.0);
    fsout_Color = vec4(color * opacity, opacity);
}
";

        /// <summary>One triangle covering the target; the post passes read <c>gl_FragCoord</c>, so no UV is passed.</summary>
        public const string FullscreenVertexShaderGlsl = @"#version 450
void main()
{
    vec2 corner = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
    gl_Position = vec4(corner * 2.0 - 1.0, 0.0, 1.0);
}
";

        /// <summary>
        /// One direction of the edge blur's Gaussian over the half-size layer: <c>Params.xy</c> the direction, <c>Params.z</c>
        /// sigma in layer pixels, <c>Params.w</c> the gain of the second pass (0: none); 25 taps spread over three sigma.
        /// </summary>
        public const string BlurFragmentShaderGlsl = @"#version 450

layout(set = 0, binding = 0) uniform AuraPostParams
{
    vec4 Params;
};
layout(set = 0, binding = 1) uniform texture2D TexA;
layout(set = 0, binding = 2) uniform texture2D TexB;
layout(set = 0, binding = 3) uniform sampler PostSampler;

layout(location = 0) out vec4 fsout_Color;

void main()
{
    vec2 size = vec2(textureSize(sampler2D(TexA, PostSampler), 0));
    vec2 uv = gl_FragCoord.xy / size;
    float sigma = max(Params.z, 0.5);
    float spacing = max(1.0, 3.0 * sigma / 12.0);
    vec2 stepUv = Params.xy * spacing / size;
    vec4 sum = vec4(0.0);
    float total = 0.0;
    for (int i = -12; i <= 12; i++)
    {
        float d = float(i) * spacing;
        float w = exp(-0.5 * d * d / (sigma * sigma));
        sum += texture(sampler2D(TexA, PostSampler), uv + stepUv * float(i)) * w;
        total += w;
    }
    vec4 blurred = sum / total;
    // The second pass strengthens the blur (premultiplied, so colour and coverage together), never past opaque.
    if (Params.w > 0.0) blurred *= min(Params.w, 1.0 / max(blurred.a, 1e-4));
    fsout_Color = blurred;
}
";

        /// <summary>
        /// Lays the blurred layer (<c>TexA</c>, premultiplied) over the scene. Where the sharp layer (<c>TexB</c>) covers, the
        /// avatar's own body, only <c>Params.w</c> of it is laid on, so the body stays sharp; <c>Params.xy</c> is the scene's
        /// size, <c>Params.z</c> the largest opacity (the sharp layer's alpha over the body).
        /// </summary>
        public const string CompositeFragmentShaderGlsl = @"#version 450

layout(set = 0, binding = 0) uniform AuraPostParams
{
    vec4 Params;
};
layout(set = 0, binding = 1) uniform texture2D TexA;
layout(set = 0, binding = 2) uniform texture2D TexB;
layout(set = 0, binding = 3) uniform sampler PostSampler;

layout(location = 0) out vec4 fsout_Color;

void main()
{
    vec2 uv = gl_FragCoord.xy / max(Params.xy, vec2(1.0));
    vec4 halo = texture(sampler2D(TexA, PostSampler), uv);
    float body = clamp(texture(sampler2D(TexB, PostSampler), uv).a / max(Params.z, 0.01), 0.0, 1.0);
    fsout_Color = halo * (1.0 - body * (1.0 - Params.w));
}
";
    }
}
