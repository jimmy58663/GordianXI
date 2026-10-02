# Calibration Registry

> Scope: every tuned, fitted or chosen constant in GordianXI that stands for a retail behaviour, with whether it was measured against the legacy client, and what would settle it. It is the to-check list for future retail recordings and Windower captures. The registry was built by sweeping `docs/**/*.md` and `src/` for "measured", "calibrated", "fitted", "capture", "recording", "estimate", "provisional", "guess", "presumed", "not measured" and by reading the constants of the classes those hits point at. A value is `measured` only where a doc or the code comment names the capture, recording or retail data it was taken from; nothing was upgraded on this pass. Date of this pass: 2026-10-01.

**Status values.**
- `measured`: taken from or checked against retail (Windower capture, maintainer's retail recording, retail packet capture, or the retail DATs), with the evidence named. A "deliberate departure" note means GordianXI then chose to differ on purpose.
- `estimated`: derived from a reference (LandSandBoat, XiPackets, xi-tools), a partial observation, or one measurement plus an assumption; plausible but not compared directly.
- `guessed`: picked to look or feel right, or as a safe limit; no evidence recorded.

Dates in the evidence column are the dates the docs give.

## Movement, collision and camera

| Value | Where | Status | Evidence or what would settle it | Doc |
|---|---|---|---|---|
| 0.5 yalm step-up | `PlayerLocomotionController.StepUpHeight` | estimated | chosen a little more forgiving than retail, which blocks the low side walls of Southern San d'Oria's ramps. Settle: the highest ledge retail climbs, from a Windower capture | [collision](../world/collision-and-physics.md) |
| 0.9 yalm foot sphere (stair rounding) | `PlayerLocomotionController.FootRadius`, `ZoneCollisionMesh.TryGetSteppedGround` | measured | Windower capture of Southern San d'Oria's stairs (0.25-yalm risers): mean error 0.034 yalms against 0.217 for per-tread snapping | [collision](../world/collision-and-physics.md) |
| 60 yalm deepest ground snap | `PlayerLocomotionController.MaxGroundDrop` | guessed | a search limit | [collision](../world/collision-and-physics.md) |
| 0.5 yalm fall threshold | `PlayerLocomotionController.FallThreshold` | guessed | "drops deeper than 0.5 yalms fall"; settle with a capture of a small ledge drop | [collision](../world/collision-and-physics.md) |
| 66 yalms/s² gravity | `PlayerLocomotionController.Gravity` | measured | fitted to a Windower capture of three falls (12 and 20 yalms), Southern San d'Oria, 2026-09-25 | [collision](../world/collision-and-physics.md) |
| 30 yalms/s top fall speed | `PlayerLocomotionController.MaxFallSpeed` | measured | same capture: a steady 1.001 yalms per 1/30 s frame | [collision](../world/collision-and-physics.md) |
| 0.53 yalm body radius against walls | `PlayerLocomotionController.BodyRadius` | measured | Windower capture of a character pressed into a Southern San d'Oria corner: 0.529 and 0.535 yalms from the walls | [collision](../world/collision-and-physics.md) |
| 1.6 yalm body column top | `PlayerLocomotionController.BodyHeight` | guessed | | [collision](../world/collision-and-physics.md) |
| entity bump radii 0.35 (player) and 0.35 / 0.7 / 1.4 by size class | `EntityBumpCollision.PlayerRadius`, `RadiusOf` | estimated | the doc: "estimates pending a retail capture". Settle: a capture of the player stopped against small, medium and large characters | [collision](../world/collision-and-physics.md) |
| 0.35 s push before passing through, 1.5 s free movement after | `EntityBumpCollision.HoldSeconds`, `GraceSeconds` | estimated | same | [collision](../world/collision-and-physics.md) |
| 3 yalm height difference, 0.25 s press gap | `EntityBumpCollision.MaxHeightDifference`, `PressGapSeconds` | guessed | | |
| knockback push / damper / ticks per level | `KnockbackSettings.Levels` | estimated | the numbers are LandSandBoat's `enums/action/knockback.h` (same as XiPackets' client table); the per-tick integration is GordianXI's (level 1 about 0.36 yalm, 4 about 1.05, 7 about 3.0). Settle: retail slide distances per level | [entities](../world/entities-and-animation.md) |
| 1.99 yalms/s lift speed | `MovingPlatforms.LiftSpeed` | measured | Windower capture of Metalworks' lift: 11.95 yalms in 6.0 s | [collision](../world/collision-and-physics.md) |
| lift landings (Metalworks 2.0 / -10.0) | `MovingPlatforms` (largest floor beside the shaft) | measured | within 0.04 of a Windower capture | [collision](../world/collision-and-physics.md) |
| 8 s elevator leg | `MovingPlatforms.DefaultTravelSeconds` | estimated | LandSandBoat's leg length; the server sends the real one | [collision](../world/collision-and-physics.md) |
| 1.5-2.9 s update lag after the leg stamp | `MovingPlatforms` clock skew (`RefineClockSkew`) | measured | observed arrival times in the same capture | [collision](../world/collision-and-physics.md) |
| camera follow height: rate 6/s, snap above 8 yalms | `ViewportCamera.FollowHeightRate`, `FollowHeightSnapDistance` | guessed | | [collision](../world/collision-and-physics.md) |
| camera wall margin 0.3 yalm, release 10 yalms/s | `ViewportCamera.CollisionMargin`, `CollisionReleaseSpeed` | guessed | | [collision](../world/collision-and-physics.md) |
| camera swing-in 0.68 per second | `PlayerLocomotionController.CameraFollowRate` | measured | retail: a held W+A run closes a circle in about 11.75 s (45 x rate = 360 / 11.75) | |
| 720 degrees/s facing turn | `PlayerLocomotionController.FacingTurnSpeedDegreesPerSec` | guessed | | |
| remote heading ease 15 per second | `WorldEntity.UpdateHeading` | guessed | | |
| 2.5 yalms/s walk speed, walk at or below half the base speed | `InputProfile.WalkSpeed`, `AnimationStateClassifier` | guessed | | |
| 40 (4.0 yalms/s) speed for a moving NPC that sends speed 0 | `EntityPacketModule.HandleCharNpc` | guessed | | |
| 50 yalm targeting range | `TargetCycling.Range` | estimated | "the monster draw distance"; the cycling order was checked in retail (2026-09-29, #134), the range was not | [input](../input/console-and-input.md#target-cycling) |

## Network timing and remote movement

| Value | Where | Status | Evidence or what would settle it | Doc |
|---|---|---|---|---|
| 60 move-clock ticks per second | `WorldEntity.MovTimeTicksPerSecond` | measured | captured traffic: counter deltas / 60 x 5.0 yalms/s match the reported displacements | [conventions](conventions.md#clocks) |
| `MovTime` up to 2 means standing | `S2C_0x00D_CharPc.StationaryMovTimeMax` | measured | captured traffic shows 1 and 2 at rest (the last update after a run carries 2) | |
| 0x015 run count 9 at the start, 1 standing | `SessionNetworkManager.InitialRunCount`, `StationaryRunCount` | measured | retail protocol captures | [network](../network/session-and-packets.md#movement-packet-0x015-timing) |
| speed byte 50 = 5.0 yalms/s | `WorldEntity.MovementSpeedYalms`, `InputProfile.RunSpeed` | measured | the same traffic check as the move clock | [network](../network/session-and-packets.md#movement-packet-0x015-timing) |
| 1.8 s starting playback delay | `WorldEntity.DefaultPlaybackDelaySeconds` | measured | captured traffic needs about 1.4-2.0 s (a ~1.4 s broadcast interval plus up to ~0.6 s staleness) | |
| playback tuning: 1.5 s max trail, 0.25 s starvation buffer, 0.1 s delay margin, 0.5 s start lead, 15% delay recovery, 20% stop catch-up | `WorldEntity.MaxTrailSeconds`, `StarvationBufferSeconds`, `DelayMarginSeconds`, `StartLeadSeconds`, `DelayRecoveryRate`, `StopCatchUpRate` | guessed | tuned for smoothness; no capture named | |
| dead counter: 1/60 s ticks plus 6 minutes | `S2C_0x037_CharStatus.DeadCounterToSeconds` | estimated | read from LandSandBoat `char_status.cpp`; settle with a capture while dead | [network](../network/session-and-packets.md#xipackets-coverage-audit-2026-09-28) |
| status icon end time = Vana'diel seconds x 60 | `LocalPlayerState.GetStatusIconRemainingSeconds` | estimated | read from LandSandBoat's `0x063_miscdata_status_icons.cpp`; "verify in game before relying on the blink threshold" (#17) | [network](../network/session-and-packets.md#s2c-0x063-misc-data) |
| C2S 0x01A layouts for every action kind | `CombatPacketBuilder.BuildAction` | estimated | XiPackets and LandSandBoat; "not checked against a retail capture" | [network](../network/session-and-packets.md#c2s-0x01a-action-kinds) |
| ShopNo sent as ShopListNum << 8 | `StockUiMenuController` shop buy (C2S 0x083) | estimated | the capture's bytes `00 04` for shop list 4; the meaning is unverified (LandSandBoat ignores it). The capture's second 0x084 carries 0x40 in its padding, sent as 0 here | [ui](../ui/stock-ui.md#shop-window-chunk-6c) |
| treasure pool message wording | `StockUiTreasure` | estimated | from the XiPackets notes, not a capture | [network](../network/session-and-packets.md#treasure-pool-s2c-0x0d2--0x0d3-c2s-0x041--0x042) |
| moon phase (84-day cycle, offsets 26 days and 886 x 360) | `VanaTime.GetMoonPhase`, `GetMoonPhaseIndex` | estimated | LandSandBoat's formula; the 12-card index after xi-model-viewer. Settle against a retail moon-phase readout | [sky](../rendering/sky-and-weather.md) |

## Event playback

| Value | Where | Status | Evidence or what would settle it | Doc |
|---|---|---|---|---|
| event turn ease 8 per second (about 95% in 0.4 s) | `EventPoseSmoother.TurnRate`, `EventVm.TurnEaseRate` | guessed | "retail's turn rate is not measured". Settle: frame-count a scripted turn in a retail recording | [entities](../world/entities-and-animation.md) |
| a turn ends under 0.05 rad (0x76 / 0x70 waits `ln(angle / 0.05) / 8` s) | `EventVm.TurnDoneRadians` | guessed | as above | [ui](../ui/stock-ui.md#dialog-text-chunk-6) |
| catch-up 1.25 x walk speed; a jump beyond half a second of walking (at least 1.5 yalms) is a placement | `EventPoseSmoother.CatchUp`, `EventPoseSmoother` | guessed | | [entities](../world/entities-and-animation.md) |
| 4.0 yalms/s event walk when nothing gives a speed | `EventVm.DefaultWalkSpeed` | guessed | the usual default is the entity's base speed / 10 | [ui](../ui/stock-ui.md#dialog-text-chunk-6) |
| 2.5 yalm event pose step-up above the floor | `EntityGrounding.EventStepUpHeight` | estimated | from the Southern San d'Oria knights' walk (goal height 0 over a floor at -2) | [entities](../world/entities-and-animation.md) |
| 1.5 s wait for event NPCs not yet in the zone | `EventDialogController.EntityWaitSeconds` | guessed | retail waits for all of them (XiEvents InitEvent2); the limit is a safety net | [ui](../ui/stock-ui.md#dialog-text-chunk-6) |
| 15 s wait for a zone-in event's first NPC | `EventDialogController.ZoneInEntityWaitSeconds` | estimated | the maintainer's Southern San d'Oria intro (2026-09-30): the NPCs came 7 s after the event | [ui](../ui/stock-ui.md#dialog-text-chunk-6) |
| 15 s (900 frames) wait for 0x34 / 0x35 to load the scene's zone | `EventVm.ZoneOpenTimeoutFrames` | guessed | a safety limit; retail load time varies by machine | [ui](../ui/stock-ui.md#dialog-text-chunk-6) |
| Windurst Walls intro scene length, 66 s in retail | `MultiEntityEventTests` (script time) | measured | the maintainer's Windurst Waters recording, 2026-10-02. GordianXI's script time is 85 s; the difference is not explained. Settle: time each narration wait in the recording | [ui](../ui/stock-ui.md#dialog-text-chunk-6) |
| timed narration lines close after n seconds of `0x7F 0x34 n` | `EventMessage.AutoCloseSeconds` | measured | the Southern San d'Oria recording shows 9-second lines for 9.2-9.3 s and a 5-second one for 5.1 s | [ui](../ui/stock-ui.md#dialog-text-chunk-6) |
| narration placement from `0x02 x 0x03 y`, scaled from a 1416-pixel client height | `StockUiHud.EventTextReferenceHeight`, `StockUiHud.DrawEventText` | measured | the recording: x 78, top 332 at UI scale 1 for the code's (80, 340) | [ui](../ui/stock-ui.md#dialog-text-chunk-6) |
| fallback placement from 0x67's two values (100 / 380 in Southern San d'Oria) | `StockUiHud.DrawEventText` | guessed | what the values mean is not known | [ui](../ui/stock-ui.md#dialog-text-chunk-6) |
| camera Route easing modes 0-4 (linear, decelerate, accelerate, decelerate-accelerate, ease in-out); Catmull-Rom through more than two keys | `CameraRoute` | estimated | "chosen, not established": the smoothing kinds a client reimplementation names (xi-tools `docs/events/cutscenes.md`) | [ui](../ui/stock-ui.md#dialog-text-chunk-6) |
| a finished camera move holds its last pose | `EventPresentation` | guessed | xi-tools reads retail as reverting to the default framing; the intros never show it | [ui](../ui/stock-ui.md#dialog-text-chunk-6) |
| head look limits: 60 degrees turn, 45 degrees tilt; ease 8 per second | `HeadLook.MaxYaw`, `MaxPitch`, `EaseRate` | guessed | "not measured". The Windurst Waters recording (2026-10-02, about 2:04) shows Ajido-Marujido tilting well back from a yalm and a half: a front-on recording of a look at a tall or close target would settle the tilt | [entities](../world/entities-and-animation.md) |
| a target placed more than 2 yalms above its drawn place is looked at at its placed height | `EntityRenderer.AirborneLookLift` | estimated | Port Jeuno event 324: retail tilts the face up to the marker 50 yalms up; the in-game test (2026-10-02) kept the head level before this rule | [entities](../world/entities-and-animation.md) |
| look axis: 4096 steps to a turn, x turns, y tilts (positive up) | `HeadLook.RadiansPerStep`, `AxisAngles` | estimated | Port Jeuno 324 (recording 2026-10-02) shows a tilt up for (0, 1024) and only a small rise; the unit (4096 or ±1000) and the sign of x are open. Settle: a front recording of Joachim in Port Jeuno 334 or 341 | [entities](../world/entities-and-animation.md) |
| 50 steps per second default head turn speed for a look axis | `HeadLook.DefaultAxisTurnSpeed` | estimated | fits the few degrees retail shows in 2 s in Port Jeuno 324; scripts that set a speed use 20-1500 | [entities](../world/entities-and-animation.md) |
| mouth clip `mou4` once per spoken line | `FaceMotion`, `WorldEntity.SpokenLines` | estimated | the in-game test against the retail recording (2026-10-02): retail moves the mouth a few times per line, then stops. Settle: count flaps per line in a recording | [entities](../world/entities-and-animation.md) |
| blink every 2-6 s (uniform) | `FaceMotion.MinBlinkInterval`, `MaxBlinkInterval` | guessed | "not measured against retail" | [entities](../world/entities-and-animation.md) |
| salute variant chooses one of three slots | `EmoteMotion.Slot` | guessed | "presumably the nation; not verified" | [entities](../world/entities-and-animation.md) |
| blur: op 0x0E's A byte / 128 is the share of the previous frame kept (0x80 = all), B G R / 128 its tint, the float its zoom, all per 60 Hz frame | `EventPresentation.BlurAmountOf`, `ScenePostProcess` | estimated | read from the retail values of `blon` / `blof`, `?dkn`, `fall` (#205); not seen in a recording. Settle: a retail recording of a `blon` scene at 60 fps | [vm](../events/vm.md#post-process-blur-and-cross-dissolve) |
| cross-dissolve: the frame before the command, faded out linearly over its duration | `EventPresentation.CrossDissolve`, `ScenePostProcess` | estimated | `ovl?` carries only a duration (#205). Settle: frame-count an `ovl1` cut in a retail recording | [vm](../events/vm.md#post-process-blur-and-cross-dissolve) |
| weighted-mesh blend is a weighted sum normalized to 1 | `WeightedMesh.Blend` | estimated | a plain sum swelled Alzadaal's fish to 1.74x mid-stroke in the in-game test (2026-10-02); 324's eye matches the recording's shape and timing (#204); xi-model-viewer's note also says normalized | [particles](../rendering/particles.md#weighted-meshes-0x25) |
| robe bodies use the emote file +12 / the package twin `32361 + 2n` | `EmoteMotion`, `EventMotionBank.PackageFileIds` | guessed | "said to"; not done | [entities](../world/entities-and-animation.md) |

## Combat animation

| Value | Where | Status | Evidence or what would settle it | Doc |
|---|---|---|---|---|
| hits show 0.7 s after arrival with no animation, 3 s if queued and never started, hit tick + 0.5 s if the actor stopped animating | `ActionPlaybackQueue.UnacceptedHitSeconds`, `QueuedHitSeconds`, `StartedHitSlackSeconds` | guessed | | [entities](../world/entities-and-animation.md) |
| requests older than 2 s are skipped; at most 3 queued; reactions older than 1 s dropped; sustained actions end after 30 s | `EntityAnimationState.StaleActionSeconds`, `MaxQueuedActions`, `StaleReactionSeconds`, `MaxSustainedSeconds` | guessed | | [entities](../world/entities-and-animation.md) |
| 0.18 s default blend | `EntityAnimationState.DefaultBlendDuration` | guessed | | |
| 24-tick flinch, 16-tick pose flash when a model does not say | `EntityAnimationState.DefaultFlinchTicks`, `DefaultPoseFlashTicks` | measured | the retail routines (`damg` 24 ticks, PC guard routines 16), read from the DATs 2026-09-28 / 29 | [entities](../world/entities-and-animation.md) |
| weapon moves to the hands halfway through the draw / sheathe | `WeaponGripOverride` | estimated | approximates the routine's op 0x1F at tick 36 of 72 | [entities](../world/entities-and-animation.md) |
| a routine without a hit lands at its second clip step or tick 36 | `EntityAnimationState` | guessed | | [entities](../world/entities-and-animation.md) |
| every hand-to-hand and dual-wield swing is a random standing variant | `ActionPlaybackQueue` (swing resolution) | guessed | open: what a left-hand hit, a kick and an off-hand hit look like in retail (Windower) | [entities](../world/entities-and-animation.md) |
| additive hit flinch toward `dfm` / `dbm`, rising over the first quarter | `EntityAnimationState` | estimated | the poses come from the retail DATs and were CPU-skinned offscreen (Hume male, monster 1600, 2026-09-29); "in-game check pending" | [entities](../world/entities-and-animation.md) |

## Lighting, sky and particles

| Value | Where | Status | Evidence or what would settle it | Doc |
|---|---|---|---|---|
| ambient = 0x2F byte / 255; sun and moon = byte / 255 x `light_power`, no lift or clamp | `ZoneEnvironmentSettings.AmbientToLight`, `DiffuseToLight` | measured | Windower capture of Bibiki Bay at 15:58 (Windower (642.7, 879.3, -20), `suny`): sand (182, 105, 77) retail vs (184, 103, 74) here; east cliffs (76, 50, 47) vs (73, 52, 47). Open: noon, where the sand now nearly saturates | [lighting](../rendering/lighting.md) |
| point light falloff exponent 2, power scale 1 | `ZoneTerrainRenderer.PointLightFalloffExponent`, `PointLightPowerScale` | measured | Windower captures of Southern San d'Oria's wall lamps at night (2026-09-24; 22:03 door lamps, 20:30 wall lamp) | [lighting](../rendering/lighting.md) |
| point light colour = min(half-range colour x 2 x power, 1) x 0.75 | `ZoneTerrainRenderer.PointLightStrength` | measured | Windower captures of Southern San d'Oria's auction-house lamps (22:03) and Metalworks' interior lights (2026-09-25); the client's pipeline is not decoded | [lighting](../rendering/lighting.md) |
| actor light scale 2 | `ActorLighting.Scale` | measured | Windower captures in Bibiki Bay: retail characters 2.6-3x brighter at night outdoors (22:46) and 2-2.5x in the entrance cave, as predicted | [lighting](../rendering/lighting.md) |
| actor light cap 0.75 | `ActorLighting.MaxLight` | estimated | "the cap is an estimate": compare characters at noon against a Windower capture of the same spot | [lighting](../rendering/lighting.md) |
| particle daylight tint clamped at 1 | `ZoneTerrainRenderer.StrongestLight` | guessed | | [lighting](../rendering/lighting.md) |
| weather generators emit a third of their authored count (doubled first when batched) | `ZoneParticleEmitter.AuthoredCount` | measured | rain density matched Windower (2026-09-24) | [particles](../rendering/particles.md) |
| 4-15 s (240-900 frames) between lightning strikes | `WeatherRoutinePlayer.MinGapFrames`, `MaxGapFrames` | estimated | "an estimate; an exact retail match was judged unnecessary" | [particles](../rendering/particles.md) |
| weather routines under 1000 frames play one at a time | `ZoneDataLoader.SporadicRoutineMaxFrames` | estimated | a survey of all zone DATs separates strikes (0-480 frames) from loops (1890-9000); the client's logic is undocumented | [particles](../rendering/particles.md) |
| 1200-frame emitter warm-up on zone load | `ZoneTerrainRenderer.EmitterWarmupFrames` | guessed | | [particles](../rendering/particles.md) |
| sky dither ±0.5 / 255 | `SkyDomeFragmentShaderGlsl` | guessed | emulates hardware dithering; not compared | [sky](../rendering/sky-and-weather.md) |
| sun glow behind overcast (`clod/sun1`) and sunshine glare (`suny/sun0`) brightness | weather sky layers (authored generator data) | estimated | layering matches Windower; "the core brightness has not been compared side by side" | [sky](../rendering/sky-and-weather.md) |
| sea-level water plane: tile UV 200, alpha 50 | `ZoneTerrainRenderer` (Ctrl+F9 plane) | guessed | off by default: the legacy client has no such plane | [sky](../rendering/sky-and-weather.md) |

## Stock UI

| Value | Where | Status | Evidence or what would settle it | Doc |
|---|---|---|---|---|
| party row layout, 7/8 font size, gauge tints | `StockUiPartyWindow` (`TextScale`, `HpGaugeColor`, `MpGaugeColor`, `EmptyGaugeColor`) | measured | Windower capture at 1:1 (2026-09-25, 2560 x 1440); positions within a pixel | [ui](../ui/stock-ui.md#renderer-and-layout-model-chunk-2) |
| party TP colour | `StockUiPartyWindow.TpColor` | guessed | opt-in, not in retail | [ui](../ui/stock-ui.md#renderer-and-layout-model-chunk-2) |
| 8 px leader ball | `StockUiPartyWindow.BallSize` | measured, deliberate departure | retail is about 6 px; 8 by request | [ui](../ui/stock-ui.md#working-notes) |
| window border: `hfr1` at about 85%, 16 px end fade | `StockUiRenderer.BorderFade`, `BorderColor` | measured | Windower captures of the party window | [ui](../ui/stock-ui.md#renderer-and-layout-model-chunk-2) |
| target name colours: unclaimed pale yellow, claimed red | `StockUiTargetWindow.UnclaimedNameColor`, `ClaimedNameColor` | measured | sampled from Windower | [ui](../ui/stock-ui.md#live-hud-chunk-3) |
| claimed-by-others name colour (240, 122, 180) | `StockUiTargetWindow.OtherClaimNameColor` | estimated | from an alliance capture of Tiamat; confirm on a capture where the claim is known to be another group's | [ui](../ui/stock-ui.md#live-hud-chunk-3) |
| target and menu cursor step 67 ms (0.8 s cycle) | `StockUiTargetWindow.CursorStepSeconds` | measured | retail capture | [ui](../ui/stock-ui.md#live-hud-chunk-3) |
| target flash: three 0.72 s pulses, +0.5 light at the peak | `TargetFlash.PulseSeconds`, `Pulses`, `PeakLight` | measured | retail capture, 2026-09-26, 30 fps (22 frames a pulse, colour roughly doubles) | [ui](../ui/stock-ui.md#live-hud-chunk-3) |
| status icon grid: frame (142, 48), 26 px pitch, nine a row | `StockUiTargetWindow` (status icon grid) | measured | matches the capture | [ui](../ui/stock-ui.md#live-hud-chunk-3) |
| name plate size: 0.0128 yalms per font pixel, then +10% | `StockUiNamePlates.YalmsPerFontPixel` | estimated, deliberate departure | the local player's 24 px cap height at 2560 x 1440, assuming the default 6-yalm camera at 60 degrees; the +10% is the maintainer's choice after the first in-game test | [ui](../ui/stock-ui.md#name-plates-28) |
| name plate glyph scale clamp 1-4 screen px per font px; hidden nearer than 1 yalm | `StockUiNamePlates.MinScale`, `MaxScale`, `NearestDepth` | guessed | "provisional" | [ui](../ui/stock-ui.md#name-plates-28) |
| 2 font px icon gap; star sizes and spacing | `StockUiNamePlates.IconGap`, star constants | guessed | "provisional"; retail's pearl and seek orb are about 1.7 : 1 wide, drawn square here | [ui](../ui/stock-ui.md#name-plates-28) |
| cursor tip 0.75 line heights above the name | `StockUiNamePlates.CursorGapShare` | measured | Raven / Marine Dhalmel / Island Rarab screenshots, 2026-09-29 | [ui](../ui/stock-ui.md#name-plates-28) |
| plate depth taken 0.5 yalm toward the camera | `StockUiNamePlates` | guessed | listed open ("the depth bias") | [ui](../ui/stock-ui.md#name-plates-28) |
| name plate icon order among the non-pearl icons | `NamePlateStyle.Icon` | guessed | "provisional"; XiPackets only says the pearl gives way to every other icon | [ui](../ui/stock-ui.md#name-plates-28) |
| `ncol` indices #6 claimed red, #7 claimed by others, #8 called for help | `NamePlateStyle.Color` | estimated | "probable"; not yet captured on a name plate | [ui](../ui/stock-ui.md#name-plates-28) |
| linkshell pearl tint uses the colour bytes as a half-scale colour | `NamePlateStyle` | measured | first in-game test: halving them came out 2.1-2.6x darker than retail | [ui](../ui/stock-ui.md#name-plates-28) |
| menu key repeat 0.4 s then 90 ms | `StockUiMenuController.RepeatDelay`, `RepeatInterval` | guessed | | [ui](../ui/stock-ui.md#menu-input-chunk-4) |
| log window scroll repeat 0.4 s then 60 ms | `PlayerLocomotionController.ScrollRepeatDelay`, `ScrollRepeatInterval` | guessed | | [ui](../ui/stock-ui.md#chat-and-log-windows-chunk-5) |
| list slide 0.12 s per entry | `StockUiMenuController.ScrollDuration` | guessed | retail scrolls smoothly; duration not measured | [ui](../ui/stock-ui.md#config-pages-and-settings-chunk-4c) |
| scrollbar 6 px, thumb (255, 221, 228); slider fill tint (0x68, 0x60, 0x84) | `StockUiMenuWindow.ScrollbarWidth`, slider drawing | measured | retail captures, 2026-09-26 | [ui](../ui/stock-ui.md#config-pages-and-settings-chunk-4c) |
| config pages drawn opaque below the 20 px title band | `StockUiRenderer.MenuBandHeight`, `StockUiRenderer.DrawMenu` (`opaqueBody`) | estimated | in-game comparison 2026-09-26; retail's mechanism is undecoded and its body colours read a little darker | [ui](../ui/stock-ui.md#config-pages-and-settings-chunk-4c) |
| retail config defaults: multi-window OFF, damage display Both, volumes, gamma | `StockUiSettings` | guessed | "not yet confirmed against a capture" | [ui](../ui/stock-ui.md#config-pages-and-settings-chunk-4c) |
| log layout: 22 px + 16 per line, rows 16 px, text 6 px in, title ends 45 px from the right | `StockUiChatWindow` | measured | 1:1 retail capture, Bibiki Bay, 2026-09-27 | [ui](../ui/stock-ui.md#chat-and-log-windows-chunk-5) |
| log font spacing: pen to 2 px past the ink, space 7 px, digits at the widest digit | `StockUiLogFont` | measured | fitted to a retail capture of every keyboard character, 2026-09-27 ("Tarudrake" 77 px) | [ui](../ui/stock-ui.md#chat-and-log-windows-chunk-5) |
| colours: timestamps (255, 255, 228), say / system white, server text (200, 100, 255), own tell (255, 150, 255) | `StockUiChatWindow` | measured | retail captures, 2026-09-27 | [ui](../ui/stock-ui.md#chat-and-log-windows-chunk-5) |
| the other chat channel colours; dialog lines white | `StockUiChatWindow` | estimated | "approximations of the default Font Colors page, not yet captured" (#53) | [ui](../ui/stock-ui.md#chat-and-log-windows-chunk-5) |
| new rows slide in over 0.3 s | `StockUiChatWindow.RowSlideSeconds` | measured | the maintainer's recording: about ten 30 fps frames a row | [ui](../ui/stock-ui.md#chat-and-log-windows-chunk-5) |
| 120-character input line | `StockUiChatInput.MaxLength` | estimated | C2S 0x0B5 carries 128 bytes; retail stops "at about this length" | [ui](../ui/stock-ui.md#chat-and-log-windows-chunk-5) |
| 0.5 s caret blink | `StockUiChatWindow` (input line caret) | guessed | | [ui](../ui/stock-ui.md#chat-and-log-windows-chunk-5) |
| page-wait arrow 2 px after the line, 3 px below the row top | `StockUiChatWindow.WaitArrowGap`, `WaitArrowTop` | measured | the retail recording shows it centred on the text height | [ui](../ui/stock-ui.md#dialog-text-chunk-6) |
| query window: question 14 px in at y 8, 16 px pitch, options 37 px in, three rows | `StockUiMenuWindow.QueryComment*`, `StockUiMenuController.QueryMaxRows` | measured | the maintainer's recording of the home point menu, 2026-09-28 | [ui](../ui/stock-ui.md#dialog-text-chunk-6) |
| command menu lists for yourself, another player and a monster | `StockUiCommandMenu.Compose` | measured | the maintainer's in-game checks, 2026-09-28 | [ui](../ui/stock-ui.md#target-command-menu-chunk-6b) |
| Disengage's place in the engaged self list; the engaged monster order; pet and trust lists | `StockUiCommandMenu.Compose` | guessed | "a guess" / "unverified" / "to be checked" | [ui](../ui/stock-ui.md#target-command-menu-chunk-6b) |
| shop row insets (icon x 3, text 22, right 8) and the price format | `StockUiMenuWindow.ShopIconX`, `ShopRowTextX`, `ShopRowRightInset` | guessed | "not captured yet: retail's exact text insets" | [ui](../ui/stock-ui.md#shop-window-chunk-6c) |
| item info window beside the gil window (x 130) | `StockUiMenuWindow.InfoOffsetX` | estimated | placement checked in game (2026-09-28); "still to be measured against a capture" | [ui](../ui/stock-ui.md#shop-window-chunk-6c) |
| quantity field "1 /12" layout | `StockUiMenuWindow.QuantityCountRight`, `QuantityTotalX` | measured | the maintainer's in-game capture, 2026-09-28 | [ui](../ui/stock-ui.md#shop-window-chunk-6c) |
| sell list frame reuses the buy frame | `StockUiShop` | guessed | "the sell frame is a guess" | [ui](../ui/stock-ui.md#shop-window-chunk-6c) |

## Suggested next captures

The open items above cluster around a few recordings, each of which would settle several rows:
- A Windower capture of the player walking into small, medium and large characters (entity bump radii and timings).
- A retail knockback of each level on open ground (knockback integration).
- A front-on recording of an NPC turning and looking in a cutscene, for example Joachim in Port Jeuno 334 or 341 (event turn rate, head look limits, look axis unit and sign, mouth flaps).
- Characters at noon in the Bibiki Bay capture spot (actor light cap, terrain noon saturation).
- The Font Colors config page (chat channel colours, dialog line colour).
- A name plate on a monster claimed by another party, a GM, and a dead player (`ncol` indices, icon order).
