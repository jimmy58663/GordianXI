// src/Gordian.App/Graphics/ActorLighting.cs
using System.Numerics;
using Gordian.Core.Graphics;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// The lights an actor (character, NPC, monster) is drawn with: the 0x2F model lighting block (sun, moon, ambient
    /// and direction) scaled by <see cref="Scale"/>.
    /// <para>
    /// The scale is 2 because character meshes carry no vertex colours (we feed the neutral 0x80) while the client
    /// lights actors at full material strength: Windower captures in Bibiki Bay showed retail characters 2-3x brighter
    /// than the 0x80-scaled result, both at night outdoors (22:46: hair 154 vs 60, skin 73 vs 24; predicted 2.6-2.7x
    /// with model lights at scale 2) and in the entrance cave (predicted 2x, measured 2-2.5x).
    /// </para>
    /// <para>
    /// At noon the doubled lights (sun 1.0 + ambient ~0.5, x2) would drive sun-facing surfaces to twice their texel
    /// and flatten skin tones, so the summed light is capped at <see cref="MaxLight"/> (1.5x the texel after the
    /// modulate-2x, the pre-doubling noon peak). Night, dusk and cave levels sit below the cap.
    /// </para>
    /// </summary>
    public readonly record struct ActorLighting(Vector3 SunDirection, Vector3 SunColor, Vector3 MoonColor, Vector3 AmbientColor)
    {
        public const float Scale = 2.0f;

        /// <summary>Cap on an actor's summed light (vertex colour x lights) before the modulate-2x.</summary>
        public const float MaxLight = 0.75f;

        public static ActorLighting From(ZoneEnvironmentSettings environment) => new(
            environment.SunDirection,
            environment.ModelSunColor * Scale,
            environment.ModelMoonColor * Scale,
            environment.ModelAmbientColor * Scale);

        public void ApplyTo(ref ZoneSceneUniform uniform)
        {
            uniform.SunDirection = new Vector4(SunDirection, 0.0f);
            uniform.SunColor = new Vector4(SunColor, 1.0f);
            uniform.MoonColor = new Vector4(MoonColor, 1.0f);
            uniform.AmbientColor = new Vector4(AmbientColor, 1.0f);
            uniform.WeatherParams = new Vector4(0.0f, 0.0f, MaxLight, 0.0f);
        }
    }
}
