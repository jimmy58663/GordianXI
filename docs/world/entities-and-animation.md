# Entity Models & Animation

> Modular character assembly, NPC/monster models, skeletal animation and the planned combat action dispatcher (Phases 5C, 5D and 5D.1). Status and open work: [ROADMAP.md](../../ROADMAP.md) and GitHub Issues.

## Models and equipment (Phase 5C)

- Dynamic character mesh decoder stitching Race + Face + 5 Armor Slots (Head, Body, Hands, Legs, Feet) + Weapons from distinct DATs (`CharacterSlot`: Face=0, Head=1, Body=2, Hands=3, Legs=4, Feet=5, Main=6, Sub=7, Ranged=8; playable `CharacterRace`: Hume M/F 1/2, Elvaan M/F 3/4, Taru M/F 5/6, Mithra 7, Galka 8).
- Bind-pose entity rendering at live `WorldEntity` coordinates
- NPC, Monster, and Trust model rendering from DAT resource caches (monsters/NPC models resolve via `EntityModelOffset = 98239` to `98239 + modelId`).
- NPC-only child races (look race 29 Mithra kitten, 30 girl, 31 boy; 115 server NPCs such as Southern San d'Oria's Authere and Blendare): skeletons `ROM/61/110`, `ROM/61/58`, `ROM/61/85` carry their own idle / walk / run clips, and each outfit slot resolves to `slotBase + modelId` (0-19 Hume, 20+ Elvaan variants).
- [ ] **Actor status visuals:** effects attached to characters by status effects (e.g. the Refresh/Regen afterglow on the characters in the Bibiki Bay captures). Shares the actor-attached generator work below.
- [ ] **Model-embedded effect routines:** some NPC models are drawn mostly by particle effects inside their own DAT, played by the model's Section 0x07 routines (e.g. the Home Point crystal, model 51 `ROM/3/25.DAT`: 13 generators, particle meshes and sprite sheets started by routines `bind` / `aper`; its skeleton mesh is only a small placeholder). Also covers actor-attached weather effects (e.g. `weat/clod/tobi` birds). Needs actor-attached generators on the zone particle runtime; shares machinery with spell and ability effects.

## Skeletal animation (Phase 5D)

- FFXI bone hierarchy & joint matrix tree parser (Section `0x2B` `SkeletonAnimationDecoder`, GPU joint-palette skinning in `EntityRenderer`/`ZoneShaders`)
- Quaternion normalized-lerp (NLERP, matching documented retail behavior) rotation & translation keyframe interpolation (`AnimationClip.TrySample`, `SkeletonPoseEvaluator.EvaluatePose`)
- Full-body locomotion & battle motion packs enabled by default (`EnableSpeculativeMotionPacks = true`). Resolved origin blob collapse root cause (additive translation kinematics $t = t_{\text{bind}} + \Delta t$, Hamilton rotation composition, normalized multi-rate track sampling phase alignment, and folder-carry legacy battle pack file number resolution). Resting locomotion is preserved against battle locomotion clobbering, and in-game renderer supports battle stance (`"btl"`) and death (`"ded"`).
- Authentic entity animation state & motion resolution: monster suffix resolution (`idl0`, `wlk0`, `run0`), packet `MovTime` parsing ensuring stationary NPCs idle instead of running in place, and zero-jitter camera-synchronized player mesh positioning with boundary-accurate keyframe interval interpolation.
- Generalized NPC stance & dual-channel animation blending (`NpcStanceResolver`, `SkeletonPoseEvaluator.EvaluateBlendedPose`, `EntityAnimationState`): convention-based auto-discovery across multi-stance mob families (Omega quadruped/biped, Uragnite shell, Hpemde open/submerged, Adamantoise guard), one-shot transition sequencing (`sp00`/`sp10`/`sp20`/`sp30`), strict priority interruption (`Death` > locomotion > stance transition), and zero-distortion local-space NLERP pose blending.

## Planned: transient combat and action animation (Phase 5D.1)

- [ ] **Combat Action Event Queue (`ActionPlaybackQueue`):** Bridge incoming S2C `0x028` (`S2C_0x028_CombatAction`) events from `CombatPacketModule` into entity animation dispatchers.
- [ ] **Weapon-Specific Attack Swing Resolution:** Resolve weapon swing animation clips (`atk`, `atk0`, `atk1`) based on the entity's equipped weapon type / combat skill (Hand-to-Hand, Daggers, 1H/2H Swords, Axes, Scythes, Polearms, Katanas, Clubs, Staves, Archery, Marksmanship).
- [ ] **One-Shot Transient Playback & Battle Stance Recovery:** Play transient attack swings, weaponskills (`ws`), casting gestures (`cas`), and job abilities to completion before seamlessly returning to the continuous `"btl"` ready stance.
- [ ] **Damage & Hit Reactions:** Trigger brief target hit-reaction flinch clips (`dam`, `hit`) when incoming attack/damage battle messages and actions occur.
- [ ] **Knockback Playback:** Knockback is not a server placement: it is the upper 3 bits of each target result's `scale` field in S2C `0x028` (shared with hit distortion), and the client slides the character itself, then reports the new position via `0x015` (LandSandBoat trusts it). Levels 1-7 carry push vector / damper / duration (LSB `enums/action/knockback.h`: e.g. Level 1 = vec 0.083, damper 0.075, timer 5.0; Level 7 = vec 0.167, damper 0.05, timer 45.0). Drive the local player's slide through the Phase 5F wall collision (`ZoneCollisionMesh.ResolveWalls` + ground following) so knockback stops at walls and never pushes into geometry; remote entities follow their server positions as usual.
  - [ ] **Anchor Toggle (built-in Windower "Anchor"):** Client option to ignore the knockback field (the addon zeroes it in incoming `0x028`), default off for legacy parity.
  - [ ] **Server Kill-Switch:** New `FeatureRestrictions.KnockbackOverride` bit that forces knockback on, following the collision-toggle pattern (`CollisionSettings`).
  - Out of scope by design: discarding WPOS (`0x05B`/`0x065`) placements. Ignoring them would defeat monster draw-in, GM/script teleports and event positions, so GordianXI always honors them and offers no toggle.
