# Sky, Weather & Celestial Bodies

> Sky dome, stars, moon, clouds, sun and lens flares, all driven by the weather directories' Section 0x05 generators (Phase 5E Tier 1, chunks 2-5). Lighting is in [lighting.md](lighting.md); particle emitters in [particles.md](particles.md). Status and open work: [ROADMAP.md](../../ROADMAP.md) and GitHub Issues.

## Sky dome and dithering (chunk 2)

- Authentically rendered 8-slice vertex-interpolated hemispherical sky dome (`SkyDomeRenderer`).
- Integrated triangular screen-space dither (`+/- 0.5/255.0` in `SkyDomeFragmentShaderGlsl`) emulating PS2 GS / D3D8 hardware rasterizer dithering, eliminating 8-bit color quantization banding in dark gradients.
- Synchronized horizon clear colors and distance fog across Day, Dusk, Night, and Overcast.
- Eliminated the overhead concentric ring artifact at dusk by separating the inverted `stardust` dome geometry from additive celestial passes.

## Night sky: stars and moon (chunk 3)

- Decoupled celestial night bodies from daytime sun and cloud layers via discrete renderer flags (`EnableWeatherCelestialBodies = true`, `EnableCelestialMoon = true`, `EnableCelestialSun = false`, `EnableWeatherClouds = false`).
- **Root cause of the chromatic star tints and invisible clouds:** a D3D11 shader stage-interface mismatch. The weather-sky fragment shader never read `fsin_WorldPos`/`fsin_Normal`, so SPIR-V cross-compilation stripped them and D3D11 (which links stages by register) fed world position into the UVs and world normals into the vertex color (alpha 0 → clouds discarded). Fixed by matching the sky VS/FS varyings exactly and giving the sky pipeline a Normal-less vertex layout; `ShaderStageInterfaces_MatchAndConsumeEveryInput` now guards every shader pair against this class of bug.
- Sky layers are matched to the generator whose `LinkedDataId` draws them (not a same-named generator): `weat/*/star` cross-links generator `star` → mesh `sta1` (666-star field) and generator `sta1` → mesh `star` (`stardust` shell).
- Generator-driven celestial rendering (`DrawCelestialGenerator`): initial rotation (Sec 2 `0x09`), base color (`0x16`), blend (`0x1E`), time-of-day alpha curve (Sec 2 keyframe link + Sec 3 `0x3F`, e.g. `ksta` / `k000`), weekday tints (`0x4E`) and moon-phase tints (`0x4F`), composited with the client's two modulate-2x texture stages.
- Moon disc decoded from the Section 0x21 SpriteSheetMesh (`SpriteSheetDecoder`): 12 phase cards on `moonshap`, card selected by moon phase (`0x45`), camera-facing, tinted by the elemental weekday. The `moonsphere` 0x2E mesh is correctly the untextured `kasa` halo (visible only near full moon).
- `stardust` shell re-enabled (`EnableMilkyWay = true`); its former ring artifact was the same shader-interface bug.
- Pole star: generator `weat/*/star/pole` draws sprite sheet `hit6`, which lives only in the shared ROM/0/0.DAT `syst/effe` tree (`SharedEffectResources`, loaded once by `ResourceManager`; link resolution falls back local → zone → shared). Rendered as a camera-facing card at its authored camera-relative offset with its warm generator tint and the `ksta` time-of-day curve.
- Moon lens flare: generator `kas1` draws lens-flare sheet `molf` in screen space (1/16 NDC per sprite unit, sprites strung from the moon's screen position through the screen centre by their per-card offsets), visible only near full moon per its authored moon-phase alpha. Veldrid exposes no occlusion queries, so instead of the client's all-or-nothing visibility test the flare is drawn at sky depth and terrain occludes it per-pixel; sun flares (`lf01`–`lf03`, multi-sprite streaks) will need a real visibility test in Chunk 5.

## Cloud shells (chunk 4)

- Cloud shells are generator-driven: every camera-following Section 0x05 generator declared directly in a weather directory that draws a 0x2E cloud mesh is its own layer, so meshes drawn by several generators composite as authored (e.g. `weat/fine`: `cld1` alpha-blended + `cld2` additive over `cld_`; `cld3` draws the `ykum` sunset band). Rain, lightning, smoke and tornado generators in the same directories are excluded (future weather-particle work).
- Authored placement and motion: raw base position (cloud domes sit below the camera so the rim meets the horizon; the old `|y|` flip is gone), initial rotation, rotation velocity (Sec 2 `0x0B` + Sec 3 `0x05`, e.g. `fine/cld1` slowly spins instead of scrolling), per-frame UV scroll (`0x27`/`0x28`), and time-of-day position curves (`0x6B`–`0x6D`, e.g. `ykum` height via `k007`).
- Authored color: time-of-day R/G/B curves (`0x3C`–`0x3E`, e.g. `kcr1/kcg1/kcb1`, `k00r/k00g/k00b`) replace the old day-factor/night-slate shader heuristic; alpha curves via `0x3F`.
- All particle blend functions (`ParticleBlendFunc`): alpha, additive, reverse-subtract (new pipeline; darkens for the `ykum` sunset band) and darken-by-alpha; painter's order follows the authored DAT order within each weather directory (verified against Windower: every weather authors its daytime sun glow before its clouds, so overcast veils the sun; the reference viewer's projection-bias sort (`0x30`) would paint the sun over the clouds).
- Per-generator distance fog (renderState bit `0x0200` clear, e.g. `mist/cld2`), fogging toward black for additive layers.
- Weather gating uses the zone's directory for the exact active weather (e.g. `thdr`, `dust`) and falls back to the canonical category (`clod`, `suny`, `fine`, `mist`) only when the zone authors none; `EnableWeatherClouds` now defaults to true.
- Celestial layers are weather-scoped as authored (verified against a live Windower client in Bibiki Bay): stars, stardust, moon, halo, pole star and moon flare exist only under the weather directories that author them (e.g. only `fine`/`suny` carry `star`/`moon`), so overcast and mist skies show none (`WeatherSkyLayer.WeatherIds`).

## Sun, horizon and lens flares (chunk 5)

- Sun path matches the client: `VanaTime.GetSunDirection` now orbits as the client does (display `(-sin a, -cos a, 0)`, a = hour * pi / 12), rising in the east (display -X) and setting in the west (+X) with no invented inclination; terrain lighting direction follows, and the moon (`-sunDir`) now rises in the east at dusk. The authored `ykum` sunset band confirms the orientation.
- Sun is generator-driven per weather: every Sun-attached generator in a weather directory that draws the 0x2E sun mesh is its own layer (e.g. `weat/fine`: `sun1` daytime glow with `ksr1/ksg1/ksb1` color and `k006` alpha, `sun2` sunset disc and `sun3` sunset corona with `k002` alpha and time-of-day scale curves `k003`/`k004` via Sec 3 `0x40`-`0x42`; `clod`/`mist` draw a single large diffuse glow). The hand-tuned golden sun-disc shader branch and inferred `sunsphere` texture are gone.
- No horizon gating for sun or moon: clock alpha curves fade them, so the sunset corona correctly outlives the sun's centre crossing the horizon.
- Sun lens flares (`lf01`/`lf02`/`lf03`/`lf31`, weather-specific) and the moon flare are drawn in screen space over the finished scene (Pass 4, no depth test) with all-or-nothing visibility from a terrain raycast toward the light (`ZoneRaycaster`, ~0.1-0.25 ms, re-cast only when the eye or light moves), standing in for the client's occlusion query.
- Verify against retail: sun glow brightness behind overcast (`clod/sun1`) and sunshine (`suny/sun0`, scale-50 additive glare). Both now sit beneath their weather's clouds in authored order and match Windower's layering, but the core brightness has not been compared side by side.
- Out of scope for Tier 1: `weat/*/yuhi` (sunset sparkles/glows) are world-positioned, draw-distance-culled particle emitters rather than sky layers, and belong with general zone particle effects.

## Live time and weather

- **Step: Live Weather & Water Dynamic Simulation:** Dynamic Vana'diel time clock and weather synchronization (`VanaTime`, `WorldState.WeatherId`, `0x00A` login ack, `0x057` weather packet). Automatic per-frame keyframe interpolation and sky dome update in `VeldridViewportControl`. Water surface particle generator instancing (Phase 2b in `ZoneDataLoader` for `shi1..shi5`, `hum1`, `mizu`, `hna0`), per-submesh and zone master UV scroll animations (`UVScrollVelocity`), and high-definition ocean water plane tuning (tile UV 200) matching retail FFXI.

## Sea-level water plane

- **Step: Base Sea-Level Ocean Water Plane:** Implement an ocean water plane at sea level ($Y=0.0$) in Pass 5 so that island beaches and bays show translucent ocean water over the seabed while awaiting the particle generator engine. *Now off by default (Ctrl+F9 toggles):* the legacy client has no such plane. Zone water uses the plain blend shader with its own texture and authored 60 fps UV scroll (removed the invented glow floor, Fresnel, crest highlights and texture substitution).
