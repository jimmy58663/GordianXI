// src/Gordian.App/Graphics/ActorShadowShaders.cs
namespace Gordian.App.Graphics
{
    /// <summary>
    /// SPIR-V cross-compiled shaders (D3D11 and Vulkan) for actor shadows: the skinned body flattened onto the ground plane
    /// under the actor along the light (silhouette), or a soft disc at the feet (blob). Both write only the colour target's
    /// alpha or multiply by it (see <c>EntityRenderer.Shadow.cs</c>), so their colour output is black. Every input and
    /// varying is consumed (D3D11 stage interface).
    /// Scene uniform use: SunDirection.xyz = direction toward the light (display space), SunDirection.w = ground height;
    /// WeatherParams.x = shadow opacity, .y = pull toward the eye in yalms, .z = 1 for the alpha mask pass (else the
    /// output alpha is 1), .w = disc radius in yalms (blob only; World holds the disc centre).
    /// </summary>
    public static class ActorShadowShaders
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
};
";

        // Pulled toward the eye (WeatherParams.y yalms) so the flat shadow stays over ground that rises or dips around the
        // feet; a wall thicker than that still hides it.
        private const string PullTowardEye = @"
    vec3 toEye = EyePosition.xyz - worldPos.xyz;
    float eyeDistance = length(toEye);
    worldPos.xyz += toEye / max(eyeDistance, 1e-4) * min(WeatherParams.y, 0.5 * eyeDistance);
    gl_Position = Projection * View * worldPos;
";

        public const string SkinnedVertexShaderGlsl = @"#version 450

layout(location = 0) in vec3 Position0;
layout(location = 1) in vec3 Position1;
layout(location = 2) in vec3 Normal0;
layout(location = 3) in vec3 Normal1;
layout(location = 4) in vec2 Weights;
layout(location = 5) in vec2 Joints;
layout(location = 6) in vec2 TexCoord;
layout(location = 7) in vec4 Color;

layout(location = 0) out vec2 fsin_TexCoord;
layout(location = 1) out float fsin_Opacity;
" + SceneUniforms + @"
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
    vec3 localPos;
    if (j1Raw < 0)
    {
        localPos = uTrans[j0].xyz + p0;
    }
    else
    {
        vec3 p1 = qrot(uRot[j1], uScale[j1].xyz * Position1);
        localPos = p0 + (Weights.x * uTrans[j0].xyz) + p1 + (Weights.y * uTrans[j1].xyz);
    }
    // Normals and the vertex colour do not shape a shadow; they are read so the cross-compiler keeps every attribute.
    float keep = step(-1e30, Normal0.x + Normal1.x + Color.a);

    vec4 worldPos = World * vec4(localPos, 1.0);
    // Flatten onto the ground plane along the light: p - L * (height above the plane / L.y).
    vec3 L = SunDirection.xyz;
    float above = max(worldPos.y - SunDirection.w, 0.0);
    worldPos.xyz -= L * (above / L.y);
    worldPos.y = SunDirection.w;

    fsin_TexCoord = TexCoord;
    fsin_Opacity = WeatherParams.x * keep;
" + PullTowardEye + @"}
";

        public const string FragmentShaderGlsl = @"#version 450

layout(location = 0) in vec2 fsin_TexCoord;
layout(location = 1) in float fsin_Opacity;

layout(location = 0) out vec4 fsout_Color;
" + SceneUniforms + @"
layout(set = 1, binding = 0) uniform texture2D uTexture;
layout(set = 1, binding = 1) uniform sampler uSampler;

void main()
{
    // The actor shaders draw bodies solid whatever their texture alpha (often 0), so the whole silhouette casts a
    // shadow; the texel is read only to keep the texture coordinate.
    float keep = step(-1e30, texture(sampler2D(uTexture, uSampler), fsin_TexCoord).a);
    // Mask pass: the share of light left (1 - opacity); the reset and apply passes write 1.
    float remaining = WeatherParams.z > 0.5 ? 1.0 - fsin_Opacity * keep : 1.0;
    fsout_Color = vec4(0.0, 0.0, 0.0, remaining);
}
";

        public const string DiscVertexShaderGlsl = @"#version 450

layout(location = 0) in vec2 Corner;

layout(location = 0) out vec2 fsin_Disc;
" + SceneUniforms + @"
void main()
{
    // A disc of radius WeatherParams.w at the feet (World's translation), on the ground plane.
    vec4 worldPos = World * vec4(Corner.x * WeatherParams.w, 0.0, Corner.y * WeatherParams.w, 1.0);
    worldPos.y = SunDirection.w;
    fsin_Disc = Corner;
" + PullTowardEye + @"}
";

        public const string DiscFragmentShaderGlsl = @"#version 450

layout(location = 0) in vec2 fsin_Disc;

layout(location = 0) out vec4 fsout_Color;
" + SceneUniforms + @"
void main()
{
    float r = length(fsin_Disc);
    if (r > 1.0) discard;
    // Dark in the middle, soft toward the rim (retail Normal-setting circles, image-12.png).
    float shade = WeatherParams.x * (1.0 - smoothstep(0.55, 1.0, r));
    float remaining = WeatherParams.z > 0.5 ? 1.0 - shade : 1.0;
    fsout_Color = vec4(0.0, 0.0, 0.0, remaining);
}
";
    }
}
