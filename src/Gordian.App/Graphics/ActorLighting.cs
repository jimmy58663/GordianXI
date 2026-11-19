// src/Gordian.App/Graphics/ActorLighting.cs
using System.Numerics;
using Gordian.Engine.Graphics;

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
    /// At noon the doubled lights would drive sun-facing surfaces to twice their texel and flatten skin tones, so the
    /// summed light is capped at <see cref="MaxLight"/>. Night, dusk and cave levels sit below the cap.
    /// </para>
    /// <para>
    /// The cap was 0.75 (an estimate) until #7's retail capture of two characters at Valkurm Dunes at noon
    /// (`7-noon-actors.png`, sunshine, 11:44): retail's sun-facing parts were 1.14-1.26x ours (a hat top (158, 117, 123)
    /// against (125, 94, 97), the gold hat's median (173, 128, 80) against (152, 111, 69)). With the lowered ambient
    /// (<see cref="ZoneEnvironmentSettings.AmbientToLight"/>) a cap of 0.9 lifts fully lit faces 1.2x over the old
    /// one and a cap of 1 lifts them 1.33x (offscreen, four characters at that spot, 2026-11-18).
    /// </para>
    /// </summary>
    public readonly record struct ActorLighting(Vector3 SunDirection, Vector3 SunColor, Vector3 MoonColor, Vector3 AmbientColor)
    {
        public const float Scale = 2.0f;

        /// <summary>Cap on an actor's summed light (vertex colour x lights) before the modulate-2x.</summary>
        public const float MaxLight = 0.9f;

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
