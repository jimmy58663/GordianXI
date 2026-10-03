# Conventions: Axes, Headings, Units and Clocks

> Scope: the coordinate spaces, heading encodings, units, clocks, byte order and packet offset convention that every GordianXI subsystem shares, and the type that converts between each pair. Each row was checked against `src/` on this pass; facts taken from a public reference cite it (XiPackets, XiEvents, LandSandBoat, xi-model-viewer). Subsystem detail stays in the subsystem docs ([docs/README.md](../README.md)). Date of this pass: 2026-10-01.

## Coordinate spaces

| Space | X | Y | Z | Used by | Conversion |
|---|---|---|---|---|---|
| Wire | east + | vertical, **+ is down** | north + | S2C 0x00D / 0x00E position (payload 8 / 12 / 16), C2S 0x015 (packet 4 / 8 / 12), WPOS 0x05B / 0x065 | read and written as is: `S2C_0x00D_CharPc`, `S2C_0x00E_CharNpc`, `LifecycleOutboundPackets` (0x015 builder) |
| Internal | east + | vertical, + is down | north + | `WorldEntity.Position`, collision (`ZoneCollisionMesh`), locomotion, event VM poses | identical to the wire; no conversion anywhere |
| Raw DAT | as internal | as internal | as internal | zone meshes, collision soup, particle generators | identical to internal (`ZoneCollisionMesh` is queried with internal positions) |
| Model space | facing + | down + | | skeletons, skinned meshes, actor effects | `EntityRenderer` model transform; `HeadLook` works in it (facing +X, Y down) |
| Display (renderer) | -x | -y (so **+ is up**) | z | everything drawn, the viewport camera, event camera Routes | `ZoneTerrainRenderer.ToDisplay` `(-x, -y, z)`; `EntityRenderer` places entities at `(-x, -height, z)`; `ViewportCamera` converts back with `ToInternal` `(-x, -y, z)` before collision raycasts |
| Windower / UI order | x | y = internal Z (north) | z = internal Y (height) | `/pos`, `/moveto`, `!pos`, entity list in the shell | `PlayerActionService.FormatDisplayPosition` prints (X, Z, Y); `ChatCommandRouter.ParseMoveToCommand` maps typed (x, y, z) to internal (x, z, y); `EntityItemViewModel` |
| LandSandBoat `!pos` / `setPos` | x | height | y (north) | GM position commands | `ChatCommandRouter.ToServerCoordinateOrder` swaps the second and third numbers of a typed `!pos` |
| Event script operands | x | north | height | 0x36 / 0x37 / 0xBA / 0x1F / 0x5A / 0x47 operands, in thousandths of a yalm | `EventVm`: operand 1 → X, operand 2 → Z, operand 3 → Y (XiEvents OpCodes/0x0036 stores them in `EventPos[0]`, `[2]`, `[1]`) |

Notes:
- **The vertical axis grows downward** in wire, internal and raw DAT space: a smaller Y is higher. Evidence in code: `ZoneCollisionMesh.TryGetSteppedGround` ("smaller Y is higher"), `PlayerLocomotionController.SnapToGround` (a fall is `ground.Height - position.Y > FallThreshold`), `VanaTime.GetSunDirection` (display +Y up). The display flip `(-x, -y, z)` turns it up and mirrors X, so internal +X (east) is display -X.
- **Windower order keeps the sign.** Windower z is the internal Y unchanged (no negation), so a Windower height is also "smaller is higher". XiPackets names the three wire floats `x`, `z`, `y` in that order (world/server/0x000D, 0x000E), the same naming Windower uses.
- **+Z is north** follows from the heading convention below (192 = north = +Z, `WorldEntity.Direction`) and the sun rising at internal +X (`VanaTime.GetSunDirection`: display -X is east). No separate retail capture of the axis is recorded in the docs.
- Entity facts 0x7F00 / 0x7F01 / 0x7F02 return the event position X, height, Z times 1000 (`EventVm.ResolveKey`; XiEvents "Event VM Functions.md", getworkofs). For the local player 0x7F80+ are answered by `IEventVmHost.GetEntityValue`; their axis order against XiEvents' `LocalX` / `LocalZ` / `LocalY` naming is not checked.
- **Differs (internal):** the field comments in `S2C_0x00D_CharPc` and `S2C_0x00E_CharNpc` describe Y as "Up(+)/Down(-)". Every consumer (collision, locomotion, renderer) treats +Y as down; the comment is wrong, the code is consistent.

## Headings

| Encoding | Steps per turn | 0 points | Increasing | Where | Conversion |
|---|---|---|---|---|---|
| Wire byte | 256 | east (+X) | clockwise seen from above (turns right): 64 = south (-Z), 128 = west, 192 = north (+Z) | 0x00D / 0x00E `dir` (payload 7), 0x015 (packet 20), WPOS, C2S 0x05C heading | `WorldEntity.Direction` (stored wire-native) |
| Radians | 2π | east | same as the wire | `WorldEntity.HeadingRadians`, `RenderHeadingRadians`, `EventPose.Heading`, the event VM's `EventDir` | `HeadingRadians = Direction / 256 · 2π`; `WorldEntity.DirectionFromRadians` rounds back; `HeadingOf(dx, dz) = atan2(-dz, dx)`; `ForwardOf(h) = (cos h, -sin h)` on (X, Z) |
| Degrees | 360 | east | same | camera yaw, input monitor, state inspector | `Direction / 256 · 360` (`PlayerLocomotionController`, `ControlsInputViewModel`, `StateInspectorViewModel`); `WorldEntity.DirectionFromDegrees` |
| Event heading | 4096 | east | same as the wire (no offset) | operands of 0x37, 0x39, 0x4B, 0xBA; entity fact 0x7F03 | `EventVm.ScriptHeading`: `(value & 0xFFF) · 2π / 4096`; 0x7F03 reads back `EventDir · 4096 / 2π` (XiEvents "Event VM Functions.md" gives the same scale) |
| Look axis | 4096 (provisional) | the body's heading | turn x, tilt y (positive = up) | 0x79 sub 2 operands | `HeadLook.RadiansPerStep`, `HeadLook.AxisAngles` ([#188](https://github.com/jimmy58663/GordianXI/issues/188); the unit is not established) |

- Drawing: a model faces +X in model space and is turned by `RotY(-heading - π)` in display space (`EntityRenderer`), which with the X mirror points it along the travel vector.
- C2S 0x05C (event position update) sends the heading byte computed from radians in `EventDialogController.SendEventUpdateXzy`.
- Writing fact 0x7F03 uses `6.283 / 4096` per step (`EventVm.StoreKey`), a rounded 2π; reading uses the exact value. The difference is about 0.003% of a turn.

## Units

| Quantity | Unit | Where | Type |
|---|---|---|---|
| Distance | yalm (one world unit) | wire floats, DAT geometry, collision | everywhere |
| Speed byte | tenths of a yalm per second (50 = 5.0 yalms/s, the base run) | 0x00D / 0x00E `Speed` (payload 24) and `SpeedBase` (25); 0x037 `Speed` is a 12-bit field and `SpeedBase` 8 bits (XiPackets world/server/0x0037) | `WorldEntity.MovementSpeedYalms` (`speed / 10`, fallback base speed then 50, at least 1.0); `InputProfile.RunSpeed = 50`, `WalkSpeed = 25` |
| Event walk speed | tenths of a yalm per second | operand of 0x32 | `EventVm` (`work · 0.1`); the default is the entity's base speed byte / 10 (`EventDialogController`, `IEventVmHost.TryGetEntityPose`), else `EventVm.DefaultWalkSpeed` 4.0 |
| Event position | thousandths of a yalm | operands of 0x36 / 0x37 / 0xBA / 0x1F / 0x5A / 0x47, facts 0x7F00-0x7F02 | `EventVm` (`work · 0.001`), XiEvents OpCodes/0x0036 |
| Knockback push | yalms per 60 Hz tick | the level is bits 2-4 of an S2C 0x028 result's `scale` (`ActionPlaybackQueue.KnockbackLevelOf`; bits 0-1 are the hit distortion) | `KnockbackSettings` (table from LandSandBoat `enums/action/knockback.h`) |
| HP | percent byte | `Hpp` (0x00D / 0x00E payload 26, 0x037 flags0 bits 16-23) | `WorldEntity.Hpp` |
| Texture and particle colour | 0x80 = opaque / neutral (half scale): DXT3 alpha peaks at 0x88, paletted at 0x80; particle mesh colours 0x80 = neutral, doubled for zone DATs only | Section 0x20 textures, 0x1F / 0x25 vertex colours | `TextureDecoder` doubles paletted alpha (`DecodedTexture.AlphaDoubled`), which the particle shader halves back ([particles](../rendering/particles.md#particle-alpha-scale-208)) |
| UI colour | 0x80 = 1.0 (half scale) | UI part colours, linkshell colours, scene fades (scene routine op 0x0F) | `StockUiRenderer`, see [ui/stock-ui.md](../ui/stock-ui.md) |
| UI layout | 512 x 448 layout pixels, drawn 1:1 at UI scale 1 | menu DAT frames | `StockUiLayout` |
| Camera field of view | Route focal length f → vertical FOV `2 · atan2(192, f)` (350 = 57.5 degrees) | scene DAT Section 0x06 | `CameraRoute` (layout from xi-tools `docs/events/scene_dat_writer.md`; see [ui/stock-ui.md](../ui/stock-ui.md#dialog-text-chunk-6)) |
| Dead counter | 1/60 s ticks plus 6 minutes | 0x037 `dead_counter1` | `S2C_0x037_CharStatus.DeadCounterToSeconds` (LandSandBoat `char_status.cpp`) |
| Status icon end time | Vana'diel seconds x 60, meant to wrap a u32 | S2C 0x063 type 0x09 | `LocalPlayerState.GetStatusIconRemainingSeconds`; read from LandSandBoat only, not checked against a capture ([network/session-and-packets.md](../network/session-and-packets.md#s2c-0x063-misc-data)) |
| Transport leg start | Earth seconds since the Vana'diel epoch | 0x00E elevator / ship data (payload 0x34) | `S2C_0x00E_CharNpc.TryGetTransport`, `VanaTime.GetEarthSecondsSinceEpoch` |

## Clocks

| Clock | Rate | Carried in | Type |
|---|---|---|---|
| Move clock (`MovTime`) | 60 ticks per second of continuous movement, counted from the start of the move | 0x00D Flags0 bits 0-12 | `WorldEntity.MovTimeTicksPerSecond` (captured traffic: counter deltas / 60 x 5.0 yalms/s match the reported displacements); a value up to `S2C_0x00D_CharPc.StationaryMovTimeMax` (2) means standing |
| 0x00E Flags0 bits 0-12 | not a clock | LandSandBoat writes NPC database flags there | `EntityPacketModule` sets `LastMovTime = 0` for NPCs and monsters and reads motion from displacement |
| Outbound run count (`MoveFlame`) | accumulating 60 Hz frame count while moving | C2S 0x015 packet 18; `MovTime` at packet 16 is always 0 | `SessionNetworkManager.InitialRunCount` (9 at the start of a move) and `StationaryRunCount` (1), both from retail captures; sent at 4 Hz ([network/session-and-packets.md](../network/session-and-packets.md#movement-packet-0x015-timing)) |
| Event VM frames | 60 Hz; each tick sees the frames since the last tick (ticks are 16-31 ms apart, irregular) | waits 0x1C / 0x6F, 0x57, walks | `EventScene` (retail `GetFrameDelay`, XiEvents) |
| Motion routines | 60 Hz ticks | Section 0x07 routine delays, blend ticks | `EntityAnimationState.RoutineTicksPerSecond`, `MotionRoutine` |
| Effect and scene routines | 60 Hz frames | particle generators, Section 0x07 effect routines, scene DAT routines (header +0x1C) | `ZoneParticleEmitter`, `EffectRoutineDecoder`, `EventSceneResource` |
| Skeletal clips | `KeyFrameDuration x 30` frames per second | Section 0x2B | `AnimationClip.DurationSeconds` (after xi-model-viewer) |
| Vana'diel time | 25 x Earth; epoch Unix 1009810800 (2002-01-01 00:00 JST); 86,400 Vana'diel seconds a day; 8-day week (0 Firesday ... 7 Darksday); 84-day moon cycle | S2C 0x00A `GameTime` (Earth seconds since the Vana'diel epoch) | `VanaTime` (`SynchronizeServerTime` from `LifecyclePackets`; LandSandBoat epoch); event 0x77 stops it at an hour (`WorldState.LockTimeOfDay`) |
| Sun position | angle = hour · π / 12 | | `VanaTime.GetSunDirection` (display `(-sin a, -cos a, 0)`, after xi-model-viewer) |

## Byte and bit order

- Every multi-byte field is little-endian. `src/Gordian.Core` reads and writes only through `BinaryPrimitives.*LittleEndian`; there is no big-endian read in the tree.
- Bit fields count from bit 0 of the lowest byte, as XiPackets' and LandSandBoat's C bitfields do on x86. "Bit n of word W" in these docs means `(W >> n) & 1` of the u32 read little-endian. "Bit n of packet byte B" is used where a source writes single bytes (LandSandBoat `ref<uint8>(0x28) |= 0x45`); byte B of a word at offset O holds bits `8 · (B - O)` and up.
- Exceptions: the linkshell name in S2C 0x0CC is an MSB-first stream of 6-bit values (`LinkshellNameCodec`); DAT section headers and the file table pack fields into dwords (`DatSectionHeader`, `FileTableResolver`, see [world/world-state-and-resources.md](../world/world-state-and-resources.md#dat-decoders)).
- Text is CP932 (Shift-JIS) unless a doc says otherwise; fixed name fields are NUL-padded.

## Packet offsets

- Every world packet starts with a 4-byte header: a u16 of the 9-bit id and the size in 4-byte words in the top 7 bits, then the u16 sync (sequence) number (`PacketHeader`; the size is `(byte1 & 0xFE) · 2` bytes).
- XiPackets and LandSandBoat give offsets from the start of the packet, header included.
- **Decoders** (`S2C_0x*` ref structs) receive the payload after the header from `PacketParser`, so a decoder offset is the XiPackets offset minus 4. Example: 0x00E Flags1 is packet 0x20, payload 28 (0x1C).
- **Builders** write the whole packet, header at 0, so builder offsets equal XiPackets offsets (the 0x015 builder writes x at +4).
- LandSandBoat drops a fixed-size C2S packet whose header size is not `roundUp4(sizeof(struct))`, so a builder's size is part of the contract ([network/session-and-packets.md](../network/session-and-packets.md#xipackets-coverage-audit-2026-09-28)).
- The docs say "payload" or "packet" with every offset. Where only "+0xNN" appears in a DAT context it is a byte offset into the section or record.
