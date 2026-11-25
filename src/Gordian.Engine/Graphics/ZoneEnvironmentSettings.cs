// src/Gordian.Engine/Graphics/ZoneEnvironmentSettings.cs
using System;
using System.Numerics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.World;

namespace Gordian.Engine.Graphics
{
    /// <summary>
    /// Encapsulates lighting and authentic FFXI distance fog parameters for 3D zone rendering.
    /// Matched to the environment uniform buffer layout consumed by the zone shader pipeline.
    /// </summary>
    public sealed class ZoneEnvironmentSettings
    {
        /// <summary>
        /// Direction towards the directional light source (Sun or Moon), normalized.
        /// </summary>
        public Vector3 SunDirection { get; set; } = Vector3.Normalize(new Vector3(0.4f, 0.8f, 0.4f));

        /// <summary>
        /// Vana'diel time of day in hours [0, 24). Drives the time-of-day keyframe curves
        /// (Section 0x19, sampled by generator clock updaters) of stars and the moon.
        /// </summary>
        public float TimeOfDayHours { get; set; } = 12.0f;

        /// <summary>
        /// RGB intensity and color of the primary directional sunlight.
        /// </summary>
        public Vector3 SunColor { get; set; } = new(0.5f, 0.49f, 0.46f);

        /// <summary>
        /// RGB intensity and color of the directional moonlight, shining opposite the sun (outdoors only).
        /// </summary>
        public Vector3 MoonColor { get; set; } = Vector3.Zero;

        /// <summary>
        /// Model (actor and effect) sun and moon light colors from the 0x2F model lighting block, converted like the
        /// terrain lights. Daylight-tinted particles take the stronger of the two.
        /// </summary>
        public Vector3 ModelSunColor { get; set; } = new(0.5f, 0.49f, 0.46f);

        /// <inheritdoc cref="ModelSunColor"/>
        public Vector3 ModelMoonColor { get; set; } = Vector3.Zero;

        /// <summary>
        /// Model (actor) ambient light from the 0x2F model lighting block, converted like the terrain ambient.
        /// </summary>
        public Vector3 ModelAmbientColor { get; set; } = new(0.25f, 0.25f, 0.25f);

        /// <summary>
        /// RGB intensity and color of the ambient light preventing completely black shadows.
        /// </summary>
        public Vector3 AmbientColor { get; set; } = new(0.18f, 0.19f, 0.23f);

        /// <summary>
        /// Controls whether atmospheric distance fog blending is enabled.
        /// When false, fog blending is skipped in shaders.
        /// </summary>
        public bool FogEnabled { get; set; } = false;

        /// <summary>
        /// RGBA color of the atmospheric distance fog.
        /// </summary>
        public Vector4 FogColor { get; set; } = new(0.58f, 0.72f, 0.88f, 1.0f);

        /// <summary>
        /// Distance from the camera where fog begins to blend in (in yalms).
        /// </summary>
        public float FogStart { get; set; } = 400.0f;

        /// <summary>
        /// Distance from the camera where scene is fully obscured by fog (in yalms).
        /// </summary>
        public float FogEnd { get; set; } = 1200.0f;

        /// <summary>
        /// Exponential fog density factor.
        /// </summary>
        public float FogDensity { get; set; } = 0.005f;

        /// <summary>
        /// Color at the celestial zenith of the sky dome (+Y).
        /// </summary>
        public Vector4 SkyZenithColor { get; set; } = new(0.25f, 0.48f, 0.85f, 1.0f);

        /// <summary>
        /// Color at the horizon ring of the sky dome.
        /// </summary>
        public Vector4 SkyHorizonColor { get; set; } = new(0.58f, 0.72f, 0.88f, 1.0f);

        /// <summary>
        /// Optional explicit elevation slices decoded from DAT Section 0x2F.
        /// When empty, the sky dome renderer smoothly gradients from SkyHorizonColor to SkyZenithColor.
        /// </summary>
        public System.Collections.Generic.List<SkySlice> SkySlices { get; } = new();

        /// <summary>
        /// Framebuffer clear color. Synchronized with SkyHorizonColor for seamless outdoor horizon blending.
        /// </summary>
        public Vector4 ClearColor { get; set; } = new(0.58f, 0.72f, 0.88f, 1.0f);

        /// <summary>
        /// Effective terrain draw distance in yalms.
        /// </summary>
        public float DrawDistance { get; set; } = 300.0f;

        /// <summary>
        /// The large-asset draw-distance multiplier (#70, #606 round 4): scales the global draw distance (the far clip and
        /// the cull distance of placements without their own) and, outdoors, the fog distances <see cref="ApplyKeyframe"/>
        /// sets. Set by the draw-distance preset (<see cref="ZoneDrawPolicy"/>); see
        /// <see cref="ZoneVisibility.DefaultDrawDistanceMultiplier"/>.
        /// </summary>
        public float DrawDistanceMultiplier { get; set; } = ZoneVisibility.DefaultDrawDistanceMultiplier;

        /// <summary>
        /// The small-asset draw-distance multiplier (#606 round 4): scales placements' own draw distances. See
        /// <see cref="ZoneVisibility.DefaultSmallDrawDistanceMultiplier"/>.
        /// </summary>
        public float SmallDrawDistanceMultiplier { get; set; } = ZoneVisibility.DefaultSmallDrawDistanceMultiplier;

        /// <summary>
        /// Scale on the authored outdoor fog distances (#606 round 4): 1 at the Retail preset, as vanilla retail. Set by
        /// the draw-distance preset (<see cref="ZoneDrawPolicy"/>); read by <see cref="ApplyKeyframe"/>.
        /// </summary>
        public float FogDistanceMultiplier { get; set; } = 1.0f;

        /// <summary>
        /// Scale on dungeons' authored fog (#612 round 2, calibrated against a capture with Windower's DrawDistance plugin
        /// on; PROVISIONAL until a vanilla retail check). The same at every preset.
        /// </summary>
        public const float IndoorFogMultiplier = 3.0f;

        /// <summary>
        /// Where vanilla retail's outdoor fog closes, as a multiple of the authored fog end (#606 round 5, PROVISIONAL):
        /// the fog runs linearly from the authored start (0 in every Konschtat keyframe) to 1.25 x the authored end.
        /// Measured at Konschtat (-227, 71, 770) heading 22-33 in thunder (fog 0-203, colour about 0.2), vanilla retail
        /// image-20 against GordianXI image-19 and offscreen renders at the matched camera: retail fogs the autumn tree
        /// 182 yalms off about 74 % (from its red-blue contrast against the fog colour) and closes the fog on the hill
        /// under the ridge trees (246-267 yalms, flat fog colour), which a linear fog from 0 to 1.2-1.25 x 203 gives;
        /// image-14 (vanilla, round 4) fits 1.25-1.35. The old rule (start pushed to 75 % of the end, full fog at the end)
        /// fogged the tree only 55 % and closed the fog before the hill, too early.
        /// </summary>
        public const float OutdoorFogEndScale = 1.25f;

        /// <summary>
        /// Scale on zone particle generators' own distances (Section 1 op 0x0A cull, op 0x2E fade): #612 calibrated it at 3
        /// (The Eldieme Necropolis' torch light pools ahead of the camera), against a capture with Windower's DrawDistance
        /// plugin on, so it stays 3 at every preset until a vanilla retail check (PROVISIONAL, #606 round 4).
        /// </summary>
        public const float GeneratorDistanceMultiplier = 3.0f;

        /// <summary>
        /// Scale on the placements' detail thresholds (#70); see <see cref="ZoneVisibility.DefaultLodDistanceScale"/>.
        /// </summary>
        public float LodDistanceScale { get; set; } = ZoneVisibility.DefaultLodDistanceScale;

        /// <summary>
        /// Radial spokes for sky dome geometry.
        /// </summary>
        public ushort Spokes { get; set; } = 16;

        /// <summary>
        /// Active weather ID (e.g. "fine", "suny", "clod", "mist", "rain", "snow", "thdr").
        /// Used by the weather sky renderer to select active cloud layers.
        /// </summary>
        public string WeatherId { get; set; } = "fine";

        /// <summary>
        /// The weather fading out during a weather change (<see cref="WeatherCrossFade"/>), or null when none is under way.
        /// Its sky layers draw at 1 - <see cref="WeatherBlend"/>.
        /// </summary>
        public string? PreviousWeatherId { get; set; }

        /// <summary>How far a weather change has gone: the sky layers of <see cref="WeatherId"/> draw at this opacity.</summary>
        public float WeatherBlend { get; set; } = 1.0f;

        /// <summary>
        /// Indicates whether this environment configuration represents an indoor or cave area (no celestial skybox or ocean plane).
        /// </summary>
        public bool Indoors { get; set; } = false;

        /// <summary>
        /// Sets the Vana'diel time of day and, outdoors, the matching celestial sun direction. Indoors the light direction
        /// is the keyframe's own (<see cref="ApplyKeyframe"/>) and the clock leaves it alone (#612).
        /// </summary>
        public void SetTimeOfDay(float vanaHour)
        {
            TimeOfDayHours = vanaHour;
            if (!Indoors) SunDirection = VanaTime.GetSunDirection(vanaHour);
        }

        /// <summary>
        /// Applies an authentic decoded FFXI Section 0x2F keyframe to these environment settings.
        /// </summary>
        public void ApplyKeyframe(EnvironmentKeyframe keyframe)
        {
            if (keyframe == null) return;

            Indoors = keyframe.Indoors;
            float diffuseMult = keyframe.TerrainDiffuseMult > 0f ? keyframe.TerrainDiffuseMult : 1.0f;
            SunColor = DiffuseToLight(keyframe.TerrainSunColor, diffuseMult);
            // Indoors the moon slot packs a signed light direction rather than a color.
            MoonColor = keyframe.Indoors ? Vector3.Zero : DiffuseToLight(keyframe.TerrainMoonColor, diffuseMult);
            if (keyframe.Indoors) SunDirection = IndoorLightDirection(keyframe.TerrainMoonColor);
            AmbientColor = AmbientToLight(keyframe.TerrainAmbientColor);
            bool dungeonLight = IsDungeonLight(keyframe);
            if (dungeonLight)
            {
                SunColor = IndoorDiffuseToLight(keyframe.TerrainSunColor, diffuseMult);
                AmbientColor = IndoorAmbientToLight(keyframe.TerrainAmbientColor);
            }
            float modelDiffuseMult = keyframe.ModelDiffuseMult > 0f ? keyframe.ModelDiffuseMult : 1.0f;
            ModelSunColor = DiffuseToLight(keyframe.ModelSunColor, modelDiffuseMult);
            ModelMoonColor = keyframe.Indoors ? Vector3.Zero : DiffuseToLight(keyframe.ModelMoonColor, modelDiffuseMult);
            ModelAmbientColor = AmbientToLight(keyframe.ModelAmbientColor);
            FogColor = keyframe.TerrainFogColor;

            // Outdoors the fog runs linearly from the authored start to OutdoorFogEndScale x the authored end, as vanilla
            // retail draws it (#606 round 5, Konschtat in thunder). Until round 5 the start was pushed to 75 % of the end
            // and the fog closed at the authored end: too little fog in the middle distance and full fog too soon.
            bool hasFog = keyframe.TerrainFogEnd > keyframe.TerrainFogStart && keyframe.TerrainFogEnd > 0;
            FogEnabled = hasFog;
            FogStart = keyframe.TerrainFogStart;
            FogEnd = keyframe.TerrainFogEnd;
            // The fog scales by its own multiplier (#606 round 4). At the Retail preset (1) the fog is vanilla retail's. The
            // textured far terrain and light haze of the #70 captures (round 1 image-17/19/20, round 2 image-52) came from
            // Windower's DrawDistance plugin, which raised both.
            if (dungeonLight)
            {
                // Dungeon fog keeps the scale #612 round 2 calibrated it at (Garlaige's far corridor at about 45 yalms
                // against retail's 53; black with the authored 0-61.7 yalm fog). That capture also had Windower's
                // DrawDistance plugin on, so vanilla's indoor fog is still to be checked (PROVISIONAL).
                FogStart *= IndoorFogMultiplier;
                FogEnd *= IndoorFogMultiplier;
            }
            else if (!keyframe.Indoors)
            {
                FogStart *= FogDistanceMultiplier;
                // The fog may end past the draw-distance cut, as vanilla retail's does: at Konschtat at night (fog 0-464,
                // cut 302) retail shows the ridge trees 290 yalms off in colour against the sky (image-27, #606 round 5).
                // Until round 5 both ends shrank to the cut, which drew them as dark silhouettes.
                FogEnd *= FogDistanceMultiplier * (hasFog ? OutdoorFogEndScale : 1.0f);
            }

            DrawDistance = keyframe.DrawDistance;
            Spokes = keyframe.Spokes;

            SkySlices.Clear();
            for (int i = 0; i < keyframe.Slices.Count; i++)
            {
                SkySlices.Add(keyframe.Slices[i]);
            }

            if (SkySlices.Count >= 1)
            {
                SkyHorizonColor = SkySlices[0].Color;
                if (SkySlices.Count >= 2)
                {
                    SkyZenithColor = SkySlices[^1].Color;
                }
            }

            // In retail FFXI (and xi-model-viewer / xim getBackgroundColor), the outdoor clear/horizon color
            // is synchronized with the lowest sky dome slice (horizon ring) for seamless background composition.
            if (!keyframe.Indoors && SkySlices.Count >= 1)
            {
                ClearColor = SkyHorizonColor;
            }
            else if (keyframe.Indoors && keyframe.ClearColor != Vector4.Zero)
            {
                ClearColor = keyframe.ClearColor;
            }
            else
            {
                ClearColor = keyframe.ClearColor != Vector4.Zero ? keyframe.ClearColor : SkyHorizonColor;
            }
        }

        /// <summary>
        /// Decodes an indoor keyframe's directional light: the moon RGB bytes are a signed direction in FFXI space
        /// (+Y down) along which the light shines, returned as the display-space direction toward the light (Metalworks'
        /// interiors author (0, 0x7F, 0): light from straight above).
        /// Encoding referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer, ui/js/environment.js).
        /// </summary>
        public static Vector3 IndoorLightDirection(Vector4 moonSlot)
        {
            static float Signed(float channel)
            {
                int b = (int)MathF.Round(channel * 255.0f);
                return (b > 127 ? b - 256 : b) / 128.0f;
            }
            var raw = new Vector3(Signed(moonSlot.X), Signed(moonSlot.Y), Signed(moonSlot.Z));
            if (raw.LengthSquared() < 1e-6f) return Vector3.UnitY;
            raw = Vector3.Normalize(raw);
            return new Vector3(raw.X, raw.Y, -raw.Z);
        }

        /// <summary>
        /// Converts an authored 0x2F ambient color (normalized RGBA) into the shader ambient term, as xim's
        /// EnvironmentLighting.ambientToColor does: byte / 510, lifted by <see cref="DarkLightBias"/> when every channel
        /// is under <see cref="DarkLightThreshold"/> (0xCC), and capped at <see cref="MaxAmbient"/>. For the usual
        /// ambient bytes that is 0.70 / 0.68 / 0.725 of byte / 255.
        /// Conversion referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer, ui/js/environment.js
        /// ambientToColor, a port of xim; the viewer's own 1.35 display boost and 0.65 cap are left out).
        /// <para>
        /// Calibration (#516, #7, 2026-11-18, offscreen against retail captures on local LSB): with byte / 255 every face
        /// the sun does not reach was 1.3-1.5x retail. With this conversion Valkurm Dunes' west-facing cliff
        /// (`Valkurm_Sand_Retail_*`, six hours 06:22-18:25, `suny`) renders at 0.90-0.98 of retail (was 0.68-0.85), and
        /// with retail's hue; Eastern Adoulin's wall (`Waypoint_Retail_*_Near`) at 1.01-1.03 at 01:15 and 13:05 (was
        /// 0.71-0.72) and the street at 0.91 (was 0.80-0.89); actors' shaded sides drop to 0.71x, as retail's are
        /// 0.67-0.87x ours (`7-noon-actors.png`). Byte / 255 came from a 2026-09-26 Windower capture of Bibiki Bay at
        /// 15:58 that is no longer on disk; Bibiki's dusk captures (`Bibiki_Lighting_Outside.png`) go from 0.84-1.00
        /// to 1.07-1.14 of retail.
        /// </para>
        /// </summary>
        public static Vector3 AmbientToLight(Vector4 authored)
        {
            var c = new Vector3(authored.X, authored.Y, authored.Z);
            var bias = IsDark(c) ? DarkLightBias : Vector3.One;
            return Vector3.Min(c * 0.5f * bias, new Vector3(MaxAmbient));
        }

        /// <summary>
        /// Converts an authored 0x2F sun/moon color (normalized RGBA) and light power (<c>light_power</c> in the
        /// XiColorEnv record) into the shader directional light color: byte / 255 times the power, lifted by
        /// <see cref="DarkLightBias"/> when every channel of that is under <see cref="DarkLightThreshold"/>, as xim's
        /// EnvironmentLighting.diffuseToColor does (referenced from xi-model-viewer, ui/js/environment.js). Not clamped:
        /// only the lit vertex result is clamped (in the shaders), as fixed-function vertex lighting does; clamping the
        /// light itself had capped the 16:00 sun's red at 1.0 instead of 1.33 and lost the dusk saturation retail shows
        /// (and turned Valkurm's 16:23 cliff greener than retail's, #516). The lift keeps moonlit ground at night as
        /// bright as retail's once the ambient is lowered (Eastern Adoulin's street at 01:15: 0.91 of retail).
        /// Field naming referenced from xi-tools (https://github.com/vekien/xi-tools, docs/reference/ps2_decomp_crosscheck.md).
        /// </summary>
        public static Vector3 DiffuseToLight(Vector4 authored, float diffuseMult)
        {
            var d = new Vector3(authored.X, authored.Y, authored.Z) * diffuseMult;
            return IsDark(d) ? d * DarkLightBias : d;
        }

        /// <summary>
        /// Whether an indoor keyframe lights like a dungeon: it authors a directional light (a terrain sun that is not black).
        /// Those keyframes take <see cref="IndoorAmbientToLight"/>, <see cref="IndoorDiffuseToLight"/> and fog scaled by the
        /// draw-distance multiplier. An indoor keyframe whose terrain sun is 00 00 00 is a room lit by its placed lamps (every
        /// Mog House room, 199 building interiors such as Windurst Woods' interior 423, Heavens Tower, stage 593, the
        /// <c>ev01</c> caves and houses of outdoor zones) and keeps the outdoor light conversion and its authored fog.
        /// <para>
        /// Survey (#612 round 3, 2026-11-21, every indoor keyframe at noon in zones 0-299 and 500-699, the Mog House rooms
        /// and interiors 259-640, 628 keyframes): 308 author a black sun, all of them rooms, interiors or sub-environments;
        /// every dungeon authors one of at least 0x35 (Garlaige 0x45, Crawlers' Nest 0x6C). The near-black suns 0x07-0x0E
        /// (stage 560's ev10, stage 559, zone 39's ev01) count as dungeon light. Round 3 in game: with the dungeon conversion
        /// the Mog Houses were too bright; with the outdoor one (round 2) they matched retail. *Inference* from the data.
        /// </para>
        /// </summary>
        public static bool IsDungeonLight(EnvironmentKeyframe keyframe) =>
            keyframe.Indoors && (keyframe.TerrainSunColor.X > 0f || keyframe.TerrainSunColor.Y > 0f || keyframe.TerrainSunColor.Z > 0f);

        /// <summary>
        /// The indoor terrain ambient: byte / 255 (not / 510) with the dark lift, capped at <see cref="MaxAmbient"/>.
        /// <para>
        /// Calibration (#612 round 2, 2026-11-21, offscreen at the LSB arrival points against retail captures on local LSB,
        /// `612-garlaige-zone-in-retail.png`, `612-eldieme-zone-in-retail.png`, `612-crawlers-nest-zone-in-retail.png`, and
        /// the maintainer's image-1..6): with the outdoor conversion every wall the indoor light does not reach rendered at
        /// about half of retail (Garlaige's low walls 24 against 46, its brick walls 25 against 47) while floors matched.
        /// Doubling the ambient and dropping the dark lift from the directional light (<see cref="IndoorDiffuseToLight"/>)
        /// brings the walls to 40-51 and keeps the floor at 73 against 78. An inference fitted to retail, not a documented
        /// formula.
        /// </para>
        /// </summary>
        public static Vector3 IndoorAmbientToLight(Vector4 authored)
        {
            var c = new Vector3(authored.X, authored.Y, authored.Z);
            var bias = IsDark(c) ? DarkLightBias : Vector3.One;
            return Vector3.Min(c * bias, new Vector3(MaxAmbient));
        }

        /// <summary>
        /// The indoor directional light: byte / 255 times the light power, without the dark lift outdoor lights take
        /// (see <see cref="IndoorAmbientToLight"/> for the calibration).
        /// </summary>
        public static Vector3 IndoorDiffuseToLight(Vector4 authored, float diffuseMult) =>
            new Vector3(authored.X, authored.Y, authored.Z) * diffuseMult;

        /// <summary>The per-channel lift xim gives dark environment lights (xi-model-viewer environment.js <c>BIAS</c>).</summary>
        public static readonly Vector3 DarkLightBias = new(1.40f, 1.36f, 1.45f);

        /// <summary>A light is dark, and lifted, when every channel is under 0xCC (xi-model-viewer environment.js <c>TH</c>).</summary>
        public const float DarkLightThreshold = 0xCC / 255.0f;

        /// <summary>The cap on the converted ambient (xim's clamp at 0.5).</summary>
        public const float MaxAmbient = 0.5f;

        private static bool IsDark(Vector3 c) => c.X < DarkLightThreshold && c.Y < DarkLightThreshold && c.Z < DarkLightThreshold;

        public static ZoneEnvironmentSettings CreateDay() => new()
        {
            TimeOfDayHours = 12.0f,
            SunDirection = Vector3.Normalize(new Vector3(0.4f, 0.8f, 0.4f)),
            SunColor = new Vector3(0.5f, 0.49f, 0.46f),
            AmbientColor = new Vector3(0.19f, 0.2f, 0.23f),
            FogEnabled = false,
            FogColor = new Vector4(0.58f, 0.72f, 0.88f, 1.0f),
            FogStart = 400.0f,
            FogEnd = 1200.0f,
            SkyZenithColor = new Vector4(0.22f, 0.45f, 0.85f, 1.0f),
            SkyHorizonColor = new Vector4(0.58f, 0.72f, 0.88f, 1.0f),
            ClearColor = new Vector4(0.58f, 0.72f, 0.88f, 1.0f)
        };

        public static ZoneEnvironmentSettings CreateNight() => new()
        {
            TimeOfDayHours = 0.0f,
            SunDirection = Vector3.Normalize(new Vector3(-0.2f, -0.7f, -0.3f)),
            SunColor = new Vector3(0.125f, 0.15f, 0.225f),
            AmbientColor = new Vector3(0.06f, 0.075f, 0.11f),
            FogEnabled = false,
            FogColor = new Vector4(0.08f, 0.12f, 0.20f, 1.0f),
            FogStart = 300.0f,
            FogEnd = 900.0f,
            SkyZenithColor = new Vector4(0.02f, 0.03f, 0.08f, 1.0f),
            SkyHorizonColor = new Vector4(0.08f, 0.12f, 0.20f, 1.0f),
            ClearColor = new Vector4(0.08f, 0.12f, 0.20f, 1.0f)
        };

        public static ZoneEnvironmentSettings CreateDusk() => new()
        {
            TimeOfDayHours = 18.0f,
            SunDirection = Vector3.Normalize(new Vector3(0.7f, 0.3f, 0.2f)),
            SunColor = new Vector3(0.5f, 0.325f, 0.225f),
            AmbientColor = new Vector3(0.15f, 0.125f, 0.175f),
            FogEnabled = false,
            FogColor = new Vector4(0.85f, 0.42f, 0.25f, 1.0f),
            FogStart = 350.0f,
            FogEnd = 1000.0f,
            SkyZenithColor = new Vector4(0.20f, 0.18f, 0.42f, 1.0f),
            SkyHorizonColor = new Vector4(0.85f, 0.42f, 0.25f, 1.0f),
            ClearColor = new Vector4(0.85f, 0.42f, 0.25f, 1.0f)
        };

        public static ZoneEnvironmentSettings CreateOvercast() => new()
        {
            TimeOfDayHours = 12.0f,
            SunDirection = Vector3.Normalize(new Vector3(0.0f, 1.0f, 0.0f)),
            SunColor = new Vector3(0.275f, 0.275f, 0.29f),
            AmbientColor = new Vector3(0.2f, 0.21f, 0.225f),
            FogEnabled = true,
            FogColor = new Vector4(0.52f, 0.55f, 0.60f, 1.0f),
            FogStart = 150.0f,
            FogEnd = 600.0f,
            SkyZenithColor = new Vector4(0.42f, 0.45f, 0.50f, 1.0f),
            SkyHorizonColor = new Vector4(0.52f, 0.55f, 0.60f, 1.0f),
            ClearColor = new Vector4(0.52f, 0.55f, 0.60f, 1.0f)
        };
    }
}
