// src/Gordian.App/Graphics/ActorShadowShaders.cs
namespace Gordian.App.Graphics
{
    /// <summary>
    /// SPIR-V cross-compiled shaders (D3D11 and Vulkan) for actor shadows: the skinned body flattened onto the ground plane
    /// under the actor along the light (silhouette), or a soft disc at the feet (blob). Both write only the colour target's
    /// alpha or multiply by it (see <c>EntityRenderer.Shadow.cs</c>), so their colour output is black. Every input and
    /// varying is consumed (D3D11 stage interface).
    /// Scene uniform use: SunDirection.xyz = direction toward the light (display space), SunDirection.w = ground height;
    /// WeatherParams.x = shadow opacity, .y = the most the shadow is pulled toward the eye (yalms), .z = the pass (0 reset
    /// or apply: output alpha 1; 1 mask: output alpha = light left; 2 unmask: the caster's own body, unflattened, writing
    /// alpha 1 where it is the nearest surface), .w = disc radius in yalms (blob only; World holds the disc centre);
    /// SkyTextureFactor.xy = the feet (display X, Z), .z = the pull at the feet, .w = how much the pull grows per yalm
    /// from the feet (up to WeatherParams.y).
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
    vec4 SkyTextureFactor;
};
";

        // Pulled toward the eye so the flat shadow stays over ground that rises or dips around the feet; a wall thicker
        // than the pull still hides it. The pull grows with the distance from the feet (#559): the ground meets the plane
        // at the feet, so it can only have risen or fallen in proportion to how far away it is, and a small pull there
        // leaves the chair, the mount's legs and a wall beside the actor in front of the shadow.
        private const string PullTowardEye = @"
vec4 PullTowardEye(vec3 worldPos, float pull)
{
    vec3 toEye = EyePosition.xyz - worldPos;
    float eyeDistance = length(toEye);
    worldPos += toEye / max(eyeDistance, 1e-4) * min(pull, 0.5 * eyeDistance);
    return Projection * View * vec4(worldPos, 1.0);
}

float GroundPull(vec3 worldPos)
{
    float fromFeet = length(worldPos.xz - SkyTextureFactor.xy);
    return min(WeatherParams.y, SkyTextureFactor.z + SkyTextureFactor.w * fromFeet);
}
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
layout(location = 2) out float fsin_VertexAlpha;
" + SceneUniforms + PullTowardEye + @"
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
    fsin_TexCoord = TexCoord;
    fsin_Opacity = WeatherParams.x * keep;
    fsin_VertexAlpha = Color.a;
    if (WeatherParams.z > 1.5)
    {
        // Unmask: the caster's body where the actor shader drew it, nudged WeatherParams.y toward the eye so it passes
        // the depth test against itself (and only itself).
        gl_Position = PullTowardEye(worldPos.xyz, WeatherParams.y);
        return;
    }
    // Flatten onto the ground plane along the light: p - L * (height above the plane / L.y).
    vec3 L = SunDirection.xyz;
    float above = max(worldPos.y - SunDirection.w, 0.0);
    worldPos.xyz -= L * (above / L.y);
    worldPos.y = SunDirection.w;
    gl_Position = PullTowardEye(worldPos.xyz, GroundPull(worldPos.xyz));
}
";

        public const string FragmentShaderGlsl = @"#version 450

layout(location = 0) in vec2 fsin_TexCoord;
layout(location = 1) in float fsin_Opacity;
layout(location = 2) in float fsin_VertexAlpha;

layout(location = 0) out vec4 fsout_Color;
" + SceneUniforms + @"
layout(set = 1, binding = 0) uniform texture2D uTexture;
layout(set = 1, binding = 1) uniform sampler uSampler;

void main()
{
    float texAlpha = texture(sampler2D(uTexture, uSampler), fsin_TexCoord).a;
    if (WeatherParams.z > 1.5)
    {
        // Unmask: keep the actor shader's cut-out holes (4 x vertex alpha x texel alpha under 0.375), so the shadow stays
        // on the ground seen through them.
        if (4.0 * fsin_VertexAlpha * texAlpha < 0.375) discard;
        fsout_Color = vec4(0.0, 0.0, 0.0, 1.0);
        return;
    }
    // The flattened silhouette casts whatever the texture alpha; the texel is read only to keep the texture coordinate.
    float keep = step(-1e30, texAlpha);
    // Mask pass: the share of light left (1 - opacity); the reset and apply passes write 1.
    float remaining = WeatherParams.z > 0.5 ? 1.0 - fsin_Opacity * keep : 1.0;
    fsout_Color = vec4(0.0, 0.0, 0.0, remaining);
}
";

        public const string DiscVertexShaderGlsl = @"#version 450

layout(location = 0) in vec2 Corner;

layout(location = 0) out vec2 fsin_Disc;
" + SceneUniforms + PullTowardEye + @"
void main()
{
    // A disc of radius WeatherParams.w at the feet (World's translation), on the ground plane.
    vec4 worldPos = World * vec4(Corner.x * WeatherParams.w, 0.0, Corner.y * WeatherParams.w, 1.0);
    worldPos.y = SunDirection.w;
    fsin_Disc = Corner;
    gl_Position = PullTowardEye(worldPos.xyz, GroundPull(worldPos.xyz));
}
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
