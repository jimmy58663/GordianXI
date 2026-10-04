# World Packet Registry

> One row for every world (map server) packet: what it is, how big it is, and what GordianXI does with it. The handshake, framing and Blowfish layers are in [session-and-packets.md](session-and-packets.md), which also keeps the longer layout findings this registry links to. Open work is in GitHub Issues.

This pass was made on 2026-10-01. It lists every opcode in XiPackets `world/server/` and `world/client/`, plus the opcodes GordianXI handles that XiPackets lacks. LandSandBoat (LSB) `src/map/packets/{s2c,c2s}/` and `src/map/enums/packet_{s2c,c2s}.h` add no world opcode that XiPackets lacks, so they only add names, sizes and notes here.

## Coverage (2026-10-01)

| Direction | XiPackets opcodes | GordianXI handles | Used | Unused | Not handled |
|---|---|---|---|---|---|
| S2C | 168 | 93 decoders (91 XiPackets opcodes, plus `0x015` and `0x0EE`) | 52 `decoded` | 41 `decoded, unused` | 77 `dropped` |
| C2S | 153 | 80 builders' opcodes, all in XiPackets | 36 `built` | 44 `built, unused` | 73 `not built` |

Of the 77 dropped S2C opcodes, 18 have no LSB definition, and LSB names or declares 3 more without ever sending them (`0x072`, `0x081`, `0x0AB`), so 56 can arrive from an LSB server. Of the 73 C2S opcodes we do not build, 23 have no LSB handler.

The audit of 2026-09-28 counted 169 S2C and 154 C2S XiPackets packets; that count included the `README.md` in each folder. The folders hold 168 and 153 packets.

## Conventions

- **Id**: the 9-bit opcode.
- **Name**: the XiPackets command name without its `GP_SERV_COMMAND_` / `GP_CLI_COMMAND_` prefix. When XiPackets has no name, the LSB enum name is given and marked "(LSB)".
- **Size**: the whole packet in bytes, 4-byte header included, as XiPackets gives it. `var` = sized to its content, `?` = XiPackets gives no size. A size taken from LSB says so. Two values mean two forms.
- **Offsets**: XiPackets offsets count from the start of the packet. Our decoders get the payload after the 4-byte header (`PacketParser`), so **our payload offset = XiPackets offset - 4**. Notes say "payload" when they use our offsets; offsets written as `0x2C` without "payload" are XiPackets packet offsets.
- **GordianXI status**:
  - `decoded`: an `S2C_0x*` decoder is registered with the dispatcher and something reads its data (state that a window, the HUD, the event VM or a reply uses).
  - `decoded, unused`: the decoder is registered and stores or raises the data, but nothing outside the packet module and its state class reads it. The UI or feature that needs it is usually the linked issue.
  - `dropped`: no decoder. If the packet arrives, `PacketParser` logs `Unhandled Packet ID` and discards it. Rows say "Not in LSB" when an LSB server cannot send it.
  - `built`: a C2S `Build*` method exists and a feature sends it (a command, a window, an automatic reply).
  - `built, unused`: the builder and its packet-module send method exist, but nothing calls the send method.
  - `not built`: no builder.
- **Handler / builder**: the GordianXI decoder type (registered by the matching `*PacketModule`) or the static builder method.
- **Notes**: short facts, the issue that tracks the gap, and **Beyond XiPackets:** / **Differs from ...:** where GordianXI's findings go past or disagree with the references. The evidence for those stays in [session-and-packets.md](session-and-packets.md) and in the decoder doc-comments.

**LSB size checks.** For C2S packets the size matters more than the name. LSB's `ValidatedPacketHandler` (`src/map/packet_system.cpp`) drops a fixed-size packet unless its header size equals its struct size rounded up to 4, and a variable-length packet (`GP_CLI_PACKET_VLA`: `0x01E`, `0x01F`, `0x02B`, `0x02C`, `0x0A0`, `0x0A1`, `0x0B5`, `0x0B6`, `0x0D3`) unless it is at least the fixed part and at most that rounded size. A builder whose size differs from LSB's is a dead packet. Every builder's size was checked against XiPackets, rounded up to 4; the rows for `0x02B`, `0x0B7` and `0x11C` say where XiPackets and LSB disagree and which one we follow.

**How the status was checked.** Decoders: every `S2C_0x*` struct in `src/Gordian.Core/Network/Packets` and its `dispatcher.Register` call; then, per handler, which state it writes or which event it raises, and whether any code outside the module and the state class reads that state or subscribes to that event (`src/Gordian.Core`, `src/Gordian.App`; `Gordian.Addons` and `Gordian.Automation` read none of it). Builders: every opcode written by `PacketHeader.Write(...)` or `(0xNNN | (size << 9))`, the module method that sends it, and whether that method has a caller. The "Logged unhandled" notes come from `src/Gordian.App/bin/Debug/net10.0/logs/gordian_system_20260930.log` and `gordian_system_20261001.log`.

## Keeping it current

- Change the row in the same PR as the code: a new decoder or builder, a first caller, a corrected size or field, a new issue.
- When a finding goes past or disagrees with XiPackets or LSB, put the short form in the row's notes and the evidence (capture, log, source path) in [session-and-packets.md](session-and-packets.md) or the decoder's doc-comment.
- `PacketDecoderRegistrationTests` fails if a decoder is not registered, so a new `S2C_0x*` type cannot stay invisible; it does not check whether the data is used.
- To repeat the diff: list `XiPackets/world/{server,client}`; `grep -rhoE "struct S2C_0x[0-9A-Fa-f]+_\w+" src/Gordian.Core`; and grep `src/Gordian.Core/Network` for `PacketHeader.Write(destination, 0x` and `(ushort)(0x` header words. Then grep each module send method for callers.

## Findings beyond or against the references

The rows carry the details; these are the ones most useful to other client and server authors.

- S2C `0x00A`: LSB starts a zone-in cutscene through the login packet's event fields, not 0x032/0x034.
- S2C `0x00E`: field meanings XiPackets leaves open (SubKind / status word, the monster flag LSB sets, hitbox size at 0x25, the Trust marker at 0x28).
- S2C `0x05E`: a 176-byte payload layout from LSB where XiPackets has none.
- S2C `0x061`: `ExpNow` / `ExpNext` must be read unsigned although both references say int16.
- S2C `0x0FA` and C2S `0x02B`, `0x0B7`, `0x11C`: XiPackets and LSB disagree on layout or size; the row says which form works with LSB.
- S2C `0x026`, `0x0DD`, `0x0E2`: LSB sends more (or fewer) bytes than XiPackets' fixed layout.
- S2C `0x067`, `0x05D`: unnamed in XiPackets; LSB uses them for char sync / entity rename and event string parameters.
- S2C `0x0CC`: the 6-bit packed linkshell name, bit order confirmed against a retail capture.

## Server to client (S2C)

| Id | Name | Size | GordianXI status | Handler / builder | Notes |
|---|---|---|---|---|---|
| `0x005` | `PACKETCONTROL` | 28 | `dropped` |  | LSB defines it with one call site. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x006` | `NARAKU` | ? | `dropped` |  | LSB defines it with one call site. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x008` | `ENTERZONE` | 52 | `decoded` | `S2C_0x008_EnterZone` | Fields not read: its arrival makes `LifecyclePacketModule` send C2S 0x00D, 0x011 and 0x061. |
| `0x009` | `MESSAGE` | var | `decoded` | `S2C_0x009_SysMessage` | `SystemMessageReceived` to the log. Para0-3 come back as one raw string and the Attr 0x10 blacklist test is not applied [#116](https://github.com/jimmy58663/GordianXI/issues/116). |
| `0x00A` | `LOGIN` | 260 | `decoded` | `S2C_0x00A_LoginAck` | Appearance, position, zone, weather, clock sync; answered with C2S 0x00C. **Beyond XiPackets:** LSB puts a zone-in cutscene in the event fields (payload `EventNo` 60, `EventNum` 94, `EventPara` 96, `EventMode` 98) instead of sending 0x032/0x034 [#122](https://github.com/jimmy58663/GordianXI/issues/122). Unread fields (music, sub-map, MyRoom flags) [#116](https://github.com/jimmy58663/GordianXI/issues/116), [#114](https://github.com/jimmy58663/GordianXI/issues/114). |
| `0x00B` | `LOGOUT` | 28 | `decoded` | `S2C_0x00B_Logout` | Logout / zone change / Mog House (`State` 1/2/3) with the next map server's IP and port. |
| `0x00D` | `CHAR_PC` | var | `decoded` | `S2C_0x00D_CharPc` | LSB builds it in `packets/char_update.cpp`, outside `s2c/`. Flags4-6 and YellFlag not read [#116](https://github.com/jimmy58663/GordianXI/issues/116). |
| `0x00E` | `CHAR_NPC` | var | `decoded` | `S2C_0x00E_CharNpc` | LSB `packets/entity_update.cpp`. **Beyond XiPackets:** the u16 at 0x2C is `SubKind:3` plus `Status:13`; byte 0x20 bit 0 is set by LSB for mob-allegiance entities; byte 0x25 is the hitbox size x 10, not a living-mob flag; LSB writes `0x45` at byte 0x28 for every Trust. A door's id (SubKind 2, payload 0x30) is read as its `TransportId` ([#15](https://github.com/jimmy58663/GordianXI/issues/15)). Name read from 0x40, 0x31 or 0x30 [#100](https://github.com/jimmy58663/GordianXI/issues/100), [#133](https://github.com/jimmy58663/GordianXI/issues/133). |
| `0x011` | `CHAR_DEL` | ? | `dropped` |  | Not in LSB. |
| `0x012` | `GM` | var | `dropped` |  | GM. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x013` | `GMCOMMAND` | var | `dropped` |  | GM. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x014` | `TELL` | var | `dropped` |  | Not in LSB. |
| `0x015` | (none) | ? | `decoded` | `S2C_0x015_PosPing` | **Beyond XiPackets:** not documented by XiPackets or LSB, and no reference server sends it. GordianXI answers it with a C2S 0x015 if it arrives. |
| `0x016` | `TALK` | var | `dropped` |  | Not in LSB. |
| `0x017` | `CHAT_STD` | var | `decoded` | `S2C_0x017_ChatStd` | Sized to the message; guarded on the fixed part only. Type enum, Attr 0x08 and `Data` gaps [#116](https://github.com/jimmy58663/GordianXI/issues/116). |
| `0x01B` | `JOB_INFO` (LSB) | 132 | `decoded` | `S2C_0x01B_JobInfo` | Job, levels, max HP/MP, base stats. Mastery, Unity and item level not read [#116](https://github.com/jimmy58663/GordianXI/issues/116). |
| `0x01C` | `ITEM_MAX` | 100 | `decoded` | `S2C_0x01C_ItemMax` | Container sizes. |
| `0x01D` | `ITEM_SAME` | 12 | `decoded, unused` | `S2C_0x01D_ItemSame` | Logged only. |
| `0x01E` | `ITEM_NUM` | 12 | `decoded` | `S2C_0x01E_ItemNum` | Item count; `InventoryState`. |
| `0x01F` | `ITEM_LIST` | 16 | `decoded` | `S2C_0x01F_ItemList` | Item in a slot; `InventoryState`. |
| `0x020` | `ITEM_ATTR` | 44 | `decoded` | `S2C_0x020_ItemAttr` | Item with price and ExtData; `InventoryState`. |
| `0x021` | `ITEM_TRADE_REQ` | 12 | `decoded, unused` | `S2C_0x021_ItemTradeReq` | Trade state is kept in `InventoryState` but no window or subscriber reads it. |
| `0x022` | `ITEM_TRADE_RES` | 16 | `decoded, unused` | `S2C_0x022_ItemTradeRes` | As 0x021. |
| `0x023` | `ITEM_TRADE_LIST` | 40 | `decoded, unused` | `S2C_0x023_ItemTradeList` | As 0x021. |
| `0x024` | `ITEM_PRESENT` | ? | `dropped` |  | Not in LSB. |
| `0x025` | `ITEM_TRADE_MYLIST` | 12 | `decoded, unused` | `S2C_0x025_ItemTradeMyList` | As 0x021. |
| `0x026` | `ITEM_SUBCONTAINER` (LSB) | 28 | `decoded, unused` | `S2C_0x026_ItemSubcontainer` | Mannequin: race/hair 6, gear 8-22 (payload). **Differs from XiPackets:** XiPackets ends at 24 bytes; LSB appends race 24 and pose 25, read only when present [#100](https://github.com/jimmy58663/GordianXI/issues/100). |
| `0x027` | `TALKNUMWORK2` | 112 | `dropped` |  | Dialog message with numbers and a speaker. [#110](https://github.com/jimmy58663/GordianXI/issues/110) |
| `0x028` | `BATTLE2` | var | `decoded` | `S2C_0x028_CombatAction` | Bit-packed actions; `CombatState`, action playback. Add-effect, skillchain, `info` and flag gaps [#116](https://github.com/jimmy58663/GordianXI/issues/116). |
| `0x029` | `BATTLE_MESSAGE` | 28 | `decoded` | `S2C_0x029_BattleMessage` | `CombatState` to the log; also the monster `/check` reply. |
| `0x02A` | `TALKNUMWORK` | 64 | `decoded` | `S2C_0x02A_TalkNumWork` | Event dialog text. `String` is a speaker only when Flag != 0 and UniqueNo == 0; we always treat it as one [#116](https://github.com/jimmy58663/GordianXI/issues/116). |
| `0x02B` | `CHANNEL_ITEM` | ? | `dropped` |  | Not in LSB. |
| `0x02C` | `CHANNEL_STATE` | ? | `dropped` |  | Not in LSB. |
| `0x02D` | `BATTLE_MESSAGE2` | 28 | `decoded` | `S2C_0x02D_BattleMessage2` | As 0x029, ends the combat message. |
| `0x02E` | `OPENMOGMENU` | 4 | `decoded, unused` | `S2C_0x02E_OpenMogMenu` | Sets `ProgressionState.IsInMogHouse` / `MogMenuPending`; nothing reads them [#93](https://github.com/jimmy58663/GordianXI/issues/93). |
| `0x02F` | `DIG` | 12 | `decoded` | `S2C_0x02F_Dig` | Answered with C2S 0x063 for the local player. |
| `0x030` | `EFFECT` | 16 | `decoded, unused` | `S2C_0x030_Effect` | Crafting animation, not status effects. `EffectNum` is the crystal element 0x10-0x17, `Type` the result (LSB `synthesis_effect.h`) [#100](https://github.com/jimmy58663/GordianXI/issues/100). `CraftEffectChanged` has no subscriber. |
| `0x031` | `RECIPE` | 52 | `dropped` |  | Guild recipe. [#112](https://github.com/jimmy58663/GordianXI/issues/112) |
| `0x032` | `EVENT` | 20 | `decoded` | `S2C_0x032_Event` | Starts the event VM (`ProgressionState.StartEvent`). |
| `0x033` | `EVENTSTR` | 112 | `decoded` | `S2C_0x033_EventStr` | As 0x032 with 4 strings and 8 values. `EventNum2` not passed [#116](https://github.com/jimmy58663/GordianXI/issues/116). |
| `0x034` | `EVENTNUM` | 52 | `decoded` | `S2C_0x034_EventNum` | As 0x032 with 8 numbers. `EventNum2` not passed [#116](https://github.com/jimmy58663/GordianXI/issues/116). |
| `0x036` | `TALKNUM` | 16 | `decoded` | `S2C_0x036_TalkNum` | Event dialog text. |
| `0x037` | `SERVERSTATUS` | 96 | `decoded` | `S2C_0x037_CharStatus` | LSB `packets/char_status.cpp`, outside `s2c/`. Status icons, speed, name plate. `dead_counter1` (payload 56) is 1/60 s ticks plus 6 minutes [#100](https://github.com/jimmy58663/GordianXI/issues/100); FreezeFlag, `dead_counter2` and GmLevel not read [#116](https://github.com/jimmy58663/GordianXI/issues/116). |
| `0x038` | `SCHEDULOR` | 20 | `dropped` |  | Actor scheduler (despawn fades, Home Point `bind`). [#109](https://github.com/jimmy58663/GordianXI/issues/109) Logged unhandled 2026-10-01. |
| `0x039` | `MAPSCHEDULOR` | 20 | `decoded` | `S2C_0x039_MapSchedulor` | Map scheduler: plays the zone routine named by the FourCC at payload +0x08 (`WorldState.PostMapScheduler` → `ZoneRoutinePlayer`, [#210](https://github.com/jimmy58663/GordianXI/issues/210)); the caster / target ids are kept but not used. **Beyond XiPackets:** the routine is a Section 0x07 routine of the zone DAT, found by name anywhere outside the weather and door directories (LSB sends `1pa1` / `1pb1` / `2pb1` with both actors 0 after each Alzadaal zone-in); see [particles.md](../rendering/particles.md#how-zone-routines-start-210). The event opcodes 0x2D / 0x51 / 0x54 and 0x60 sub 2 play and end zone routines through the same queue ([#226](https://github.com/jimmy58663/GordianXI/issues/226)); `MapSchedulerRequest.Stop` marks an end. |
| `0x03A` | `MAGICSCHEDULOR` | 20 | `dropped` |  | Magic scheduler. [#109](https://github.com/jimmy58663/GordianXI/issues/109) |
| `0x03B` | `EVENTMES` | 12 | `dropped` |  | [#110](https://github.com/jimmy58663/GordianXI/issues/110) |
| `0x03C` | `SHOP_LIST` | var | `decoded` | `S2C_0x03C_ShopList` | NPC shop list: 12-byte entries; flags 0x89 on the last packet. |
| `0x03D` | `SHOP_SELL` | 16 | `decoded` | `S2C_0x03D_ShopSell` | Appraisal price. |
| `0x03E` | `SHOP_OPEN` | 8 | `decoded` | `S2C_0x03E_ShopOpen` | Opens the NPC shop. |
| `0x03F` | `SHOP_BUY` | 12 | `decoded` | `S2C_0x03F_ShopBuy` | Purchase result; prints the buy line. |
| `0x040` | (unknown) | ? | `dropped` |  | Not in LSB. |
| `0x041` | `BLACK_LIST` | 248 | `dropped` |  | LSB sends it at every login. [#113](https://github.com/jimmy58663/GordianXI/issues/113) Logged unhandled 2026-09-30 and 2026-10-01. |
| `0x042` | `BLACK_EDIT` | 28 | `dropped` |  | [#113](https://github.com/jimmy58663/GordianXI/issues/113) |
| `0x043` | `TALKNUMNAME` | 32 | `dropped` |  | [#110](https://github.com/jimmy58663/GordianXI/issues/110) |
| `0x044` | `EXTENDED_JOB` (LSB) | 160 | `dropped` |  | BLU / PUP / Monstrosity job data (LSB `0x044_extended_job_*`). [#115](https://github.com/jimmy58663/GordianXI/issues/115) Logged unhandled 2026-09-30 and 2026-10-01. |
| `0x047` | `TRANSLATE` (LSB) | 136 | `decoded` | `S2C_0x047_Translate` | `TranslateReceived` to the chat view. Nothing sends the C2S 0x02B request yet. |
| `0x048` | `LINK_CONCIERGE` (LSB) | 128 | `dropped` |  | Linkshell concierge. [#113](https://github.com/jimmy58663/GordianXI/issues/113) |
| `0x049` | `ITEMSEARCH` (LSB) | 72 | `dropped` |  | `/itemsearch` reply. [#113](https://github.com/jimmy58663/GordianXI/issues/113) |
| `0x04B` | `PBX_RESULT` | var | `dropped` |  | Delivery box. [#113](https://github.com/jimmy58663/GordianXI/issues/113) |
| `0x04C` | `AUC` | 60 | `decoded, unused` | `S2C_0x04C_Auc` | No Auction House window [#90](https://github.com/jimmy58663/GordianXI/issues/90). `Param` union and several commands not decoded [#116](https://github.com/jimmy58663/GordianXI/issues/116). |
| `0x04D` | `FRAGMENTS` | var | `dropped` |  | LSB: fishing ranking and server message; sized to its string. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x04F` | `EQUIP_CLEAR` | 8 | `decoded` | `S2C_0x04F_EquipClear` | Empties every equip slot; LSB sends it at login and on `resyncEquipment` [#108](https://github.com/jimmy58663/GordianXI/issues/108). |
| `0x050` | `EQUIP_LIST` | 8 | `decoded` | `S2C_0x050_EquipList` | Equip slot; `InventoryState`. |
| `0x051` | `GRAP_LIST` | 24 | `decoded` | `S2C_0x051_GrapList` | Payload 0-17: nine grap ids, the local player's appearance [#153](https://github.com/jimmy58663/GordianXI/issues/153). |
| `0x052` | `EVENTUCOFF` | 8 | `decoded` | `S2C_0x052_EventUcOff` | Event update ack, server cancel, fishing end. Modes 0 and 3 ignored; mode 2 event id not compared [#116](https://github.com/jimmy58663/GordianXI/issues/116). |
| `0x053` | `SYSTEMMES` | 16 | `dropped` |  | [#110](https://github.com/jimmy58663/GordianXI/issues/110) Logged unhandled 2026-10-01. |
| `0x054` | `DEBUGPRINT` | 12 | `dropped` |  | Not in LSB. |
| `0x055` | `SCENARIOITEM` | 136 | `decoded, unused` | `S2C_0x055_ScenarioItem` | Key item tables in `ProgressionState`; only the unused C2S 0x064 path reads them [#91](https://github.com/jimmy58663/GordianXI/issues/91). |
| `0x056` | `MISSION` | 40 | `decoded, unused` | `S2C_0x056_Mission` | Only port 0xFFFF is used; TVR and quest ports dropped [#116](https://github.com/jimmy58663/GordianXI/issues/116). `MissionsUpdated` has no subscriber. |
| `0x057` | `WEATHER` | 12 | `decoded` | `S2C_0x057_Weather` | Weather; `StartTime` / `OffsetTime` decoded but not used [#116](https://github.com/jimmy58663/GordianXI/issues/116). |
| `0x058` | `ASSIST` | 16 | `dropped` |  | Target-of-target reply to C2S 0x0B7. [#110](https://github.com/jimmy58663/GordianXI/issues/110) Logged unhandled in September 2026. |
| `0x059` | `FRIENDPASS` | 36 | `dropped` |  | Friend pass. [#113](https://github.com/jimmy58663/GordianXI/issues/113) |
| `0x05A` | `MOTIONMES` | 56 | `dropped` |  | Emote echo, including our own. [#110](https://github.com/jimmy58663/GordianXI/issues/110) |
| `0x05B` | `WPOS` | 28 | `decoded` | `S2C_0x05B_WPos` | Position set; modes 3/6 place then remove, 7 places [#101](https://github.com/jimmy58663/GordianXI/issues/101). |
| `0x05C` | `PENDINGNUM` | 36 | `dropped` |  | Updates the running event's numbers. [#110](https://github.com/jimmy58663/GordianXI/issues/110) Logged unhandled 2026-10-01. |
| `0x05D` | `PENDINGSTR` (LSB) | 104 | `dropped` |  | **Beyond XiPackets:** unnamed there; LSB names it `PENDINGSTR` (the event's string parameters). [#110](https://github.com/jimmy58663/GordianXI/issues/110) |
| `0x05E` | `CONQUEST` | 180 (LSB) | `decoded, unused` | `S2C_0x05E_Conquest` | **Beyond XiPackets:** XiPackets has no layout; 176-byte payload from LSB `0x05e_conquest.h` [#99](https://github.com/jimmy58663/GordianXI/issues/99). `ConquestUpdated` has no subscriber. |
| `0x05F` | `MUSIC` | 8 | `dropped` |  | [#114](https://github.com/jimmy58663/GordianXI/issues/114) |
| `0x060` | `MUSICVOLUME` | 8 | `dropped` |  | [#114](https://github.com/jimmy58663/GordianXI/issues/114) |
| `0x061` | `CLISTATUS` | 112 | `decoded` | `S2C_0x061_CliStatus` | Stats. **Differs from XiPackets:** `ExpNow`/`ExpNext` read unsigned (both sources say int16; LSB's table exceeds 32767). Payload 0x6C [#100](https://github.com/jimmy58663/GordianXI/issues/100). |
| `0x062` | `CLISTATUS2` | 256 | `decoded, unused` | `S2C_0x062_CliStatus2` | Skills and command recasts stored in `LocalPlayerState`; nothing reads them. Crafting skills are `level * 0x20 + rank` (LSB `BuildingCharSkillsTable`) [#100](https://github.com/jimmy58663/GordianXI/issues/100). |
| `0x063` | `MISCDATA` (LSB) | var | `decoded, unused` | `S2C_0x063_MiscData` | Types 0x02/0x05/0x06/0x07/0x09 decoded into state ([table](session-and-packets.md#s2c-0x063-misc-data)); nothing reads them (the status icon HUD uses 0x037). Monstrosity 0x03/0x04 not decoded [#105](https://github.com/jimmy58663/GordianXI/issues/105). |
| `0x064` | `PREFERENCE_DATA` | ? | `dropped` |  | Not in LSB. |
| `0x065` | `WPOS2` | 28 | `decoded` | `S2C_0x065_WPos2` | As 0x05B. |
| `0x067` | `ENTITY_UPDATE1` (LSB) | var | `dropped` |  | **Beyond XiPackets:** unnamed there; LSB sends char sync (`char_sync.cpp`) and entity rename (`entity_set_name.cpp`) on this id. [#107](https://github.com/jimmy58663/GordianXI/issues/107) Logged unhandled 2026-09-30 and 2026-10-01. |
| `0x068` | `ENTITY_UPDATE2` (LSB) | var | `dropped` |  | LSB `pet_sync.cpp`: the local pet. [#107](https://github.com/jimmy58663/GordianXI/issues/107) |
| `0x069` | `CHOCOBO_RACING` (LSB) | 200 | `dropped` |  | Chocobo racing. [#117](https://github.com/jimmy58663/GordianXI/issues/117), [#95](https://github.com/jimmy58663/GordianXI/issues/95) |
| `0x06F` | `COMBINE_ANS` | 56 | `dropped` |  | [#112](https://github.com/jimmy58663/GordianXI/issues/112) |
| `0x070` | `COMBINE_INF` | 48 | `dropped` |  | [#112](https://github.com/jimmy58663/GordianXI/issues/112) |
| `0x071` | `INFLUENCE` (LSB) | 204 | `dropped` |  | Campaign / colonization map. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x072` | `UNKNOWN_072` (LSB) | ? | `dropped` |  | LSB names it `UNKNOWN_072` and has no packet for it. |
| `0x073` | `CHOCOBO_TOTEBOARD` (LSB) | ? | `decoded, unused` | `S2C_0x073_ChocoboToteboard` | `ProgressionState` toteboard; `ToteboardUpdated` has no subscriber [#1](https://github.com/jimmy58663/GordianXI/issues/1), [#95](https://github.com/jimmy58663/GordianXI/issues/95). |
| `0x074` | `CHOCOBO_LIST` (LSB) | ? | `dropped` |  | Chocobo list. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x075` | `BATTLEFIELD` (LSB) | 172 | `dropped` |  | Battlefield data. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x076` | `GROUP_EFFECTS` (LSB) | 244 | `decoded` | `S2C_0x076_GroupEffects` | Party member status icons (`PartyState`), drawn by the party window. |
| `0x077` | `ENTITY_VIS` (LSB) | 136 | `decoded, unused` | `S2C_0x077_EntityVis` | Read only when `Flags` is 1 (UniqueNo list) [#100](https://github.com/jimmy58663/GordianXI/issues/100); `EntityVisibilityReceived` has no subscriber. |
| `0x078` | `SWITCH_START` | var | `dropped` |  | Vote start; sized to its string. [#111](https://github.com/jimmy58663/GordianXI/issues/111) |
| `0x079` | `SWITCH_PROC` | var | `dropped` |  | Vote progress; sized to its string. [#111](https://github.com/jimmy58663/GordianXI/issues/111) |
| `0x081` | `UNKNOWN_081` (LSB) | ? | `dropped` |  | LSB names it `UNKNOWN_081` and has no packet for it. |
| `0x082` | `GUILD_BUY` | 8 | `decoded, unused` | `S2C_0x082_GuildBuy` | Guild shop. No C2S 0x0AA-0x0AD requests yet [#112](https://github.com/jimmy58663/GordianXI/issues/112). |
| `0x083` | `GUILD_BUYLIST` | 248 | `decoded, unused` | `S2C_0x083_GuildBuyList` | Rows go into the NPC shop list, which only opens on 0x03E [#112](https://github.com/jimmy58663/GordianXI/issues/112). |
| `0x084` | `GUILD_SELL` | 8 | `decoded, unused` | `S2C_0x084_GuildSell` | As 0x082. |
| `0x085` | `GUILD_SELLLIST` | 248 | `decoded, unused` | `S2C_0x085_GuildSellList` | As 0x082. |
| `0x086` | `GUILD_OPEN` | 12 | `decoded, unused` | `S2C_0x086_GuildOpen` | As 0x082; `Time` left raw [#116](https://github.com/jimmy58663/GordianXI/issues/116). |
| `0x08C` | `MERIT` (LSB) | var | `decoded, unused` | `S2C_0x08C_Merit` | `merit_count` is the number of entries in this packet, not a point total [#100](https://github.com/jimmy58663/GordianXI/issues/100). `MeritsUpdated` has no subscriber. |
| `0x08D` | `JOB_POINTS` (LSB) | 260 | `decoded, unused` | `S2C_0x08D_JobPoints` | `JobPointsUpdated` has no subscriber. |
| `0x08E` | `alter_ego_points` (LSB) | 104 | `dropped` |  | Alter Ego (Trust) points. LSB has `s2c/0x08e_alter_ego_points` but no entry for it in `enums/packet_s2c.h`. [#115](https://github.com/jimmy58663/GordianXI/issues/115) Logged unhandled 2026-09-30 and 2026-10-01. |
| `0x096` | `MYROOM_ENTER` | 8 | `decoded, unused` | `S2C_0x096_MyRoomEnter` | Mog House enter result; nothing reads it [#93](https://github.com/jimmy58663/GordianXI/issues/93). |
| `0x097` | `MYROOM_EXIT` | 8 | `dropped` |  | LSB defines it. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x098` | `MYROOM_IS` | 8 | `dropped` |  | Not in LSB. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x099` | `MYROOM_EXIST` | 8 | `dropped` |  | Not in LSB. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x09A` | `MYROOM_PLANT` | 8 | `dropped` |  | Not in LSB. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x09B` | `MYROOM_RAISE` | 8 | `dropped` |  | Not in LSB. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x09C` | `MYROOM_HARVEST` | 8 | `dropped` |  | Not in LSB. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x09D` | `MYROOM_DIARY` | 52 | `dropped` |  | Not in LSB. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x09E` | `MYROOM_PLACE` | 24 | `dropped` |  | Not in LSB. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x0A0` | `MAP_GROUP` | 24 | `dropped` |  | Party map positions. [#113](https://github.com/jimmy58663/GordianXI/issues/113) |
| `0x0AA` | `MAGIC_DATA` | 132 | `decoded, unused` | `S2C_0x0AA_MagicData` | Learned spells stored in `LocalPlayerState`; nothing reads them. |
| `0x0AB` | `FEAT_DATA` | 24 | `dropped` |  | LSB declares `s2c/0x0ab_feat_data.h` but never sends it. |
| `0x0AC` | `COMMAND_DATA` | 228 | `decoded, unused` | `S2C_0x0AC_CommandData` | Abilities, weapon skills, traits stored in `LocalPlayerState`; nothing reads them. |
| `0x0AD` | `DUNGEON` (LSB) | 132 | `dropped` |  | Moblin Maze Mongers data. [#115](https://github.com/jimmy58663/GordianXI/issues/115) Logged unhandled 2026-09-30 and 2026-10-01. |
| `0x0AE` | `MOUNT_DATA` (LSB) | 12 | `dropped` |  | Unlocked mounts. [#115](https://github.com/jimmy58663/GordianXI/issues/115), [#87](https://github.com/jimmy58663/GordianXI/issues/87) Logged unhandled 2026-09-30 and 2026-10-01. |
| `0x0B4` | `CONFIG` | 24 | `decoded` | `S2C_0x0B4_Config` | `PlayerConfigState`; its flag word is echoed by C2S 0x0DB and 0x0DC. |
| `0x0B5` | `FAQ_GMPARAM` | 32 | `dropped` |  | GM. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x0B6` | `SET_GMMSG` | var | `dropped` |  | GM. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x0B7` | `GMSCITEM` | ? | `dropped` |  | GM. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x0BF` | `REGISTRATION` (LSB) | 28 | `dropped` |  | Battlefield registration. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x0C8` | `GROUP_TBL` | 248 | `decoded` | `S2C_0x0C8_GroupTbl` | Party roster. Leader flags partly exposed [#116](https://github.com/jimmy58663/GordianXI/issues/116). Each entry's `ZoneNo` is the member's zone (same-zone members included, unlike 0x0DD), stored on `PartyMember.ZoneId` and used by the party window to show "(zone)" for members elsewhere [#146](https://github.com/jimmy58663/GordianXI/issues/146). |
| `0x0C9` | `EQUIP_INSPECT` | var | `dropped` |  | Player `/check` reply (LSB `equip_inspect_equipment` / `_general`). [#64](https://github.com/jimmy58663/GordianXI/issues/64) |
| `0x0CA` | `INSPECT_MESSAGE` | 148 | `dropped` |  | Bazaar / check comment. [#110](https://github.com/jimmy58663/GordianXI/issues/110) Logged unhandled 2026-09-30 and 2026-10-01. |
| `0x0CC` | `LINKSHELL_MESSAGE` | 176 | `decoded` | `S2C_0x0CC_LinkshellMessage` | Linkshell message. `encodedLsName` (payload 156) is the 6-bit packed name, confirmed against a retail capture of 2026-09-28 [#100](https://github.com/jimmy58663/GordianXI/issues/100). |
| `0x0D2` | `TROPHY_LIST` | 60 | `decoded` | `S2C_0x0D2_TrophyList` | Treasure pool item (`TreasurePoolState`). |
| `0x0D3` | `TROPHY_SOLUTION` | 60 | `decoded` | `S2C_0x0D3_TrophySolution` | Treasure pool lot / result. |
| `0x0DC` | `GROUP_SOLICIT_REQ` | 32 | `decoded` | `S2C_0x0DC_GroupSolicitReq` | Party invite. |
| `0x0DD` | `GROUP_LIST` | var | `decoded` | `S2C_0x0DD_GroupList` | Member list, sized to the name: LSB cuts `Name[16]` (payload 36) to the name rounded up to 4 plus 4 bytes; a fixed 52-byte guard used to drop short names [#159](https://github.com/jimmy58663/GordianXI/issues/159). `ZoneNo` (offset 28) is set only for a member in a different zone from yours, whose HP/MP/TP/percent/jobs are then zero: the party window shows the zone name instead, and the stored vitals are not overwritten [#146](https://github.com/jimmy58663/GordianXI/issues/146). |
| `0x0DE` | `GROUP_SOLICIT_NO` | 8 | `decoded` | `S2C_0x0DE_GroupSolicitNo` | Clears the pending invite. |
| `0x0DF` | `GROUP_ATTR` | 36/40 | `decoded` | `S2C_0x0DF_GroupAttr` | Member HP/MP/TP; local vitals. |
| `0x0E0` | `GROUP_COMLINK` | 8 | `decoded` | `S2C_0x0E0_GroupComlink` | Linkshell item location; LSB sends it at zone-in and on equip. |
| `0x0E1` | `GROUP_CHECKID` | 8 | `dropped` |  | [#113](https://github.com/jimmy58663/GordianXI/issues/113) |
| `0x0E2` | `GROUP_LIST2` | var | `decoded` | `S2C_0x0E2_GroupList2` | Member list; fixed fields end at payload 36 and the packet is sized to the name [#100](https://github.com/jimmy58663/GordianXI/issues/100). |
| `0x0E6` | `BALLISTA` (LSB) | var | `dropped` |  | Ballista. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x0EE` | `FeatureRestrictions` (GordianXI) | 12 | `decoded` | `S2C_0x0EE_FeatureRestrictions` | **Beyond XiPackets:** GordianXI's own server policy packet: a u64 `FeatureRestrictions` mask written to `SessionProfile.FeatureRestrictions` (the automation kill switch). Not in XiPackets or LSB. |
| `0x0F4` | `TRACKING_LIST` | 28 | `dropped` |  | Widescan. [#111](https://github.com/jimmy58663/GordianXI/issues/111) |
| `0x0F5` | `TRACKING_POS` | 24 | `dropped` |  | Widescan. [#111](https://github.com/jimmy58663/GordianXI/issues/111) |
| `0x0F6` | `TRACKING_STATE` | 8 | `dropped` |  | Widescan. [#111](https://github.com/jimmy58663/GordianXI/issues/111) |
| `0x0F9` | `RES` | 12 | `dropped` |  | Raise / Tractor offer. [#103](https://github.com/jimmy58663/GordianXI/issues/103) |
| `0x0FA` | `MYROOM_OPERATION` | 16 | `decoded, unused` | `S2C_0x0FA_MyRoomOperation` | **Differs between references:** XiPackets u32 item, u32 result, index 8, category 9; LSB u16 item, u8 result, index 6, category 7. The decoder picks the layout from bytes 6-7 [#100](https://github.com/jimmy58663/GordianXI/issues/100). Nothing reads the result [#93](https://github.com/jimmy58663/GordianXI/issues/93). |
| `0x105` | `BAZAAR_LIST` | 44 | `decoded, unused` | `S2C_0x105_BazaarList` | Bazaar items kept in `InventoryState`; no bazaar window. |
| `0x106` | `BAZAAR_BUY` | 28 | `decoded, unused` | `S2C_0x106_BazaarBuy` | As 0x105. |
| `0x107` | `BAZAAR_CLOSE` | 24 | `decoded, unused` | `S2C_0x107_BazaarClose` | As 0x105. |
| `0x108` | `BAZAAR_SHOPPING` | 32 | `decoded, unused` | `S2C_0x108_BazaarShopping` | As 0x105. |
| `0x109` | `BAZAAR_SELL` | 36 | `decoded, unused` | `S2C_0x109_BazaarSell` | As 0x105. |
| `0x10A` | `BAZAAR_SALE` | 28 | `decoded, unused` | `S2C_0x10A_BazaarSale` | As 0x105. |
| `0x10E` | `REQSUBMAPNUM` | 8 | `dropped` |  | [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x10F` | `REQLOGOUTINFO` | 8 | `dropped` |  | Not in LSB. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x110` | `UNITY` (LSB) | 20 | `decoded, unused` | `S2C_0x110_Unity` | Unity; `UnityUpdated` has no subscriber [#65](https://github.com/jimmy58663/GordianXI/issues/65), [#94](https://github.com/jimmy58663/GordianXI/issues/94). |
| `0x111` | `ROE_ACTIVELOG` (LSB) | 260 | `decoded, unused` | `S2C_0x111_RoeActiveLog` | Records of Eminence active objectives; nothing reads them. |
| `0x112` | `ROE_LOG` (LSB) | 136 | `decoded, unused` | `S2C_0x112_RoeLog` | `Offset` (payload 128) is a chunk index 0-3 into the 512-byte table [#100](https://github.com/jimmy58663/GordianXI/issues/100); nothing reads it. |
| `0x113` | `CURRENCIES_1` (LSB) | 252 | `decoded` | `S2C_0x113_Currencies1` | Currencies page 1 (inventory view). Several currencies skipped [#116](https://github.com/jimmy58663/GordianXI/issues/116). |
| `0x115` | `FISH` (LSB) | 24 | `decoded, unused` | `S2C_0x115_Fish` | Fishing; `FishingUpdated` has no subscriber. |
| `0x116` | `EQUIPSET_VALID` (LSB) | 72 | `decoded, unused` | `S2C_0x116_EquipsetValid` | Equip set validation; nothing reads it [#92](https://github.com/jimmy58663/GordianXI/issues/92). |
| `0x117` | `EQUIPSET_RES` (LSB) | 136 | `decoded, unused` | `S2C_0x117_EquipsetRes` | Equip set result, both 16-entry arrays [#5](https://github.com/jimmy58663/GordianXI/issues/5), [#92](https://github.com/jimmy58663/GordianXI/issues/92). |
| `0x118` | `CURRENCIES_2` (LSB) | 148 | `decoded` | `S2C_0x118_Currencies2` | Currencies page 2 (inventory view). Several currencies skipped [#116](https://github.com/jimmy58663/GordianXI/issues/116). |
| `0x119` | `ABIL_RECAST` (LSB) | 260 | `decoded, unused` | `S2C_0x119_AbilRecast` | Recasts stored in `LocalPlayerState` and `CombatState`; nothing reads them. |
| `0x11A` | `EMOTE_LIST` (LSB) | 12 | `dropped` |  | Unlocked job emotes. [#111](https://github.com/jimmy58663/GordianXI/issues/111) |
| `0x11B` | (unknown) | ? | `dropped` |  | Not in LSB. |
| `0x11C` | `LOCKSTYLE_ERROR` (LSB) | ? | `dropped` |  | Lockstyle error (LSB `0x11c_lockstyle_error`, sent from C2S 0x053). |
| `0x11D` | `PARTYREQ` (LSB) | 32 | `decoded, unused` | `S2C_0x11D_PartyReq` | Another player asks to join (`Status` 0 asking, 1 withdrawn) [#5](https://github.com/jimmy58663/GordianXI/issues/5); `SnapshotJoinRequests` has no reader. |
| `0x11E` | `JUMP` (LSB) | 8 | `dropped` |  | Another player's `/jump`. [#111](https://github.com/jimmy58663/GordianXI/issues/111) |

## Client to server (C2S)

| Id | Name | Size | GordianXI status | Handler / builder | Notes |
|---|---|---|---|---|---|
| `0x00A` | `LOGIN` | 92 | `built` | `HandshakePackets.BuildLoginSubPacket` | First packet of every map connection (`BuildLoginDatagram`). Login fields corrected [#102](https://github.com/jimmy58663/GordianXI/issues/102). |
| `0x00B` | `LOGOUT` | 28 | `not built` |  | Not in LSB. |
| `0x00C` | `GAMEOK` | 12 | `built` | `LifecycleOutboundPackets.BuildGameOk` | Sent on S2C 0x00A. |
| `0x00D` | `NETEND` | 8 | `built` | `LifecycleOutboundPackets.BuildNetEnd` | Sent on S2C 0x008. |
| `0x00F` | `CLSTAT` | 36 | `built, unused` | `EntityOutboundPackets.BuildClStat` | `SendClientStatusAsync`: No caller of its module send method. |
| `0x011` | `ZONE_TRANSITION` (LSB) | 6 | `built` | `LifecycleOutboundPackets.BuildZoneTransition` | XiPackets has it unnamed (6 bytes); LSB calls it `ZONE_TRANSITION`. Sent on S2C 0x008, padded to 8. |
| `0x015` | `POS` | 32 | `built` | `LifecycleOutboundPackets.BuildPos`, `HandshakePackets.BuildPosPingPongSubPacket` | Position and heartbeat ([timing](session-and-packets.md#movement-packet-0x015-timing)); also the reply to S2C 0x015. |
| `0x016` | `CHARREQ` | 8 | `built` | `EntityOutboundPackets.BuildCharReq` | Name requests for unnamed entities and event actors. |
| `0x017` | `CHARREQ2` | 20 | `built, unused` | `EntityOutboundPackets.BuildCharReq2` | `RequestEntityUnexpectedAsync`: No caller of its module send method. |
| `0x01A` | `ACTION` | 28 | `built` | `CombatPacketBuilder.BuildAction` | Every action kind ([kinds](session-and-packets.md#c2s-0x01a-action-kinds)). Home Point / Raise / Tractor menus not sent [#103](https://github.com/jimmy58663/GordianXI/issues/103). |
| `0x01B` | `FRIENDPASS` | 28 | `not built` |  | [#113](https://github.com/jimmy58663/GordianXI/issues/113) |
| `0x01C` | `UNKNOWN` (LSB) | 12 | `not built` |  | LSB has a handler (`0x01c_unknown`). |
| `0x01E` | `GM` | var | `not built` |  | GM; LSB variable length. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x01F` | `GMCOMMAND` | var | `not built` |  | GM; LSB variable length. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x028` | `ITEM_DUMP` | 12 | `built, unused` | `InventoryPacketBuilders.BuildItemDump` | `DropItemAsync`: No caller of its module send method. |
| `0x029` | `ITEM_MOVE` | 12 | `built, unused` | `InventoryPacketBuilders.BuildItemMove` | `MoveItemAsync`: No caller of its module send method. |
| `0x02A` | `ITEM_ATTR` | 6 | `not built` |  | Not in LSB. |
| `0x02B` | `TRANSLATE` (LSB) | 72 | `built, unused` | `ChatOutboundPackets.BuildTranslateRequest` | **Differs between references:** XiPackets fixed 72 bytes (`Name[64]`); LSB variable, minimum 12. We send the variable form. `RequestTranslateAsync`: No caller of its module send method. |
| `0x02C` | `ITEMSEARCH` (LSB) | 72 | `not built` |  | LSB variable length. [#113](https://github.com/jimmy58663/GordianXI/issues/113) |
| `0x032` | `ITEM_TRADE_REQ` | 12 | `built, unused` | `InventoryPacketBuilders.BuildTradeReq` | `RequestTradeAsync`: No caller of its module send method. |
| `0x033` | `ITEM_TRADE_RES` | 12 | `built, unused` | `InventoryPacketBuilders.BuildTradeRes` | `RespondTradeAsync`: No caller of its module send method. |
| `0x034` | `ITEM_TRADE_LIST` | 12 | `built, unused` | `InventoryPacketBuilders.BuildTradeList` | `SetTradeItemAsync`: No caller of its module send method. |
| `0x035` | `ITEM_PRESENT` | 8 | `not built` |  | Not in LSB. |
| `0x036` | `ITEM_TRANSFER` | 64 | `built, unused` | `InventoryPacketBuilders.BuildItemTransfer` | NPC trade. `TransferNpcTradeAsync`: No caller of its module send method. |
| `0x037` | `ITEM_USE` | 20 | `built, unused` | `InventoryPacketBuilders.BuildItemUse` | `UseItemAsync`: No caller of its module send method. |
| `0x038` | `ITEM_MAKE` | 12 | `not built` |  | Not in LSB. |
| `0x039` | `ITEM_LIST` | 4 | `not built` |  | Not in LSB. |
| `0x03A` | `ITEM_STACK` | 8 | `built` | `InventoryPacketBuilders.BuildItemStack` | Sort (inventory view). |
| `0x03B` | `SUBCONTAINER` (LSB) | 32 | `built, unused` | `InventoryPacketBuilders.BuildSubcontainer` | Mannequin (LSB `subcontainer`). `UseSubcontainerAsync`: No caller of its module send method. [#93](https://github.com/jimmy58663/GordianXI/issues/93) |
| `0x03C` | `BLACK_LIST` | 28 | `not built` |  | [#113](https://github.com/jimmy58663/GordianXI/issues/113) |
| `0x03D` | `BLACK_EDIT` | 28 | `not built` |  | [#113](https://github.com/jimmy58663/GordianXI/issues/113) |
| `0x041` | `TROPHY_ENTRY` | 8 | `built` | `TreasurePacketBuilder.BuildLot` | `/lot`. LSB ignores the second field (first empty bag slot). |
| `0x042` | `TROPHY_ABSENCE` | 6 | `built` | `TreasurePacketBuilder.BuildPass` | `/pass`. The 6-byte struct goes out as 8. |
| `0x04B` | `FRAGMENTS` | 24 | `not built` |  | [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x04D` | `PBX` | 32 | `not built` |  | Delivery box. [#113](https://github.com/jimmy58663/GordianXI/issues/113) |
| `0x04E` | `AUC` | 60 | `built, unused` | `InventoryPacketBuilders.BuildAuctionRequest` | 60 bytes, the size XiPackets gives (was 52) [#4](https://github.com/jimmy58663/GordianXI/issues/4). AskCommit / LotIn missing; `BidAuctionAsync`: No caller of its module send method. [#90](https://github.com/jimmy58663/GordianXI/issues/90) |
| `0x050` | `EQUIP_SET` | 8 | `built, unused` | `InventoryPacketBuilders.BuildEquipSet` | `EquipItemAsync`: No caller of its module send method. |
| `0x051` | `EQUIPSET_SET` (LSB) | 72 | `built, unused` | `InventoryPacketBuilders.BuildEquipsetSet` | `EquipSetAsync`: No caller of its module send method. [#92](https://github.com/jimmy58663/GordianXI/issues/92) |
| `0x052` | `EQUIPSET_CHECK` (LSB) | 76 | `built, unused` | `InventoryPacketBuilders.BuildEquipsetCheck` | `RemoveItemFlg`, `Equipment[16]` fixed [#102](https://github.com/jimmy58663/GordianXI/issues/102). `CheckEquipsetAsync`: No caller of its module send method. |
| `0x053` | `LOCKSTYLE` (LSB) | 136 | `built` | `InventoryPacketBuilders.BuildLockstyle` | `/lockstyle`, `/lockstyle on`, `/lockstyle off`, `/lockstyleset`. Echo flag fixed [#102](https://github.com/jimmy58663/GordianXI/issues/102). |
| `0x058` | `RECIPE` | 20 | `not built` |  | [#112](https://github.com/jimmy58663/GordianXI/issues/112) |
| `0x059` | `EFFECTEND` | 16 | `not built` |  | [#111](https://github.com/jimmy58663/GordianXI/issues/111) |
| `0x05A` | `REQCONQUEST` | 4 | `built, unused` | `ProgressionPacketBuilder.BuildReqConquest` | `SendReqConquestAsync`: No caller of its module send method. [#111](https://github.com/jimmy58663/GordianXI/issues/111) |
| `0x05B` | `EVENTEND` | 20 | `built` | `ProgressionPacketBuilder.BuildEventEnd` | Event end and update (`EventDialogController`). |
| `0x05C` | `EVENTENDXZY` | 32 | `built` | `ProgressionPacketBuilder.BuildEventEndXzy` | Event update with a position; `SendEventEndXzyAsync` has no caller. |
| `0x05D` | `MOTION` | 16 | `built` | `CombatPacketBuilder.BuildEmoteRequest` | Emotes; protocol ids from LSB `enums/emote.h` [#178](https://github.com/jimmy58663/GordianXI/issues/178). |
| `0x05E` | `MAPRECT` | 24 | `built` | `LifecycleOutboundPackets.BuildMapRect` | Zone lines and Mog House exit. |
| `0x060` | `PASSWARDS` | 28 | `not built` |  | [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x061` | `CLISTATUS` | 6 | `built` | `EntityOutboundPackets.BuildCliStatus` | Sent on S2C 0x008, padded to 8. |
| `0x063` | `DIG` | 16 | `built` | `CombatPacketBuilder.BuildDigFinishedRequest` | Sent at once after S2C 0x02F for the local player; LSB ignores it. |
| `0x064` | `SCENARIOITEM` | 76 | `built, unused` | `ProgressionPacketBuilder.BuildScenarioItemRead` | `PlayerActionService.MarkKeyItemSeenAsync` has no caller [#91](https://github.com/jimmy58663/GordianXI/issues/91). |
| `0x066` | `FISHING` | 20 | `not built` |  | Old fishing packet; LSB still has a handler (`0x066_fishing`). GordianXI uses 0x110. |
| `0x06E` | `GROUP_SOLICIT_REQ` | 12 | `built` | `PartyPacketBuilder.BuildGroupSolicitReq` | `/invite`. `PartyKind.Alliance` is 5 [#98](https://github.com/jimmy58663/GordianXI/issues/98). |
| `0x06F` | `GROUP_LEAVE` | 6 | `built` | `PartyPacketBuilder.BuildGroupLeave` | `/leave`. |
| `0x070` | `GROUP_BREAKUP` | 6 | `built` | `PartyPacketBuilder.BuildGroupBreakup` | `/disband`. |
| `0x071` | `GROUP_STRIKE` | 28 | `built` | `PartyPacketBuilder.BuildGroupStrike` | `/pcmd kick`. |
| `0x072` | `GROUP_KICK` | 28 | `not built` |  | Not in LSB. |
| `0x073` | `GROUP_CHANGE` | 12 | `not built` |  | Not in LSB. |
| `0x074` | `GROUP_SOLICIT_RES` | 6 | `built` | `PartyPacketBuilder.BuildGroupSolicitRes` | `/join`, `/decline`. |
| `0x075` | `GROUP_TALK` | var | `not built` |  | Not in LSB. |
| `0x076` | `GROUP_LIST_REQ` | 6 | `built, unused` | `PartyPacketBuilder.BuildGroupListReq` | `SendGroupListReqAsync`: No caller of its module send method. |
| `0x077` | `GROUP_CHANGE2` | 22 | `built, unused` | `PartyPacketBuilder.BuildGroupChange2` | 22 bytes, sent as 24. `SendGroupChange2Async`: No caller of its module send method. |
| `0x078` | `GROUP_CHECKID` | 4 | `not built` |  | [#113](https://github.com/jimmy58663/GordianXI/issues/113) |
| `0x082` | `SHOP_REQ` | 8 | `not built` |  | Not in LSB. |
| `0x083` | `SHOP_BUY` | 16 | `built` | `InventoryPacketBuilders.BuildShopBuy` | Shop buy (shop window). |
| `0x084` | `SHOP_SELL_REQ` | 12 | `built` | `InventoryPacketBuilders.BuildShopSellReq` | Shop appraise. |
| `0x085` | `SHOP_SELL_SET` | 6 | `built` | `InventoryPacketBuilders.BuildShopSellSet` | Shop sell confirm. |
| `0x08C` | `PREFERENCE_READ` | 6 | `not built` |  | Not in LSB. |
| `0x08D` | `PREFERENCE_SAVE` | 64 | `not built` |  | Not in LSB. |
| `0x096` | `COMBINE_ASK` | 34 | `not built` |  | Synthesis. [#112](https://github.com/jimmy58663/GordianXI/issues/112) |
| `0x09B` | `CHOCOBO_RACE_REQ` (LSB) | 12 | `built, unused` | `ProgressionPacketBuilder.BuildChocoboRaceReq` | `SendChocoboRaceReqAsync`: No caller of its module send method. [#95](https://github.com/jimmy58663/GordianXI/issues/95) |
| `0x0A0` | `SWITCH_PROPOSAL` | var | `not built` |  | LSB variable length. [#111](https://github.com/jimmy58663/GordianXI/issues/111) |
| `0x0A1` | `SWITCH_VOTE` | var | `not built` |  | LSB variable length. [#111](https://github.com/jimmy58663/GordianXI/issues/111) |
| `0x0A2` | `DICE` | 8 | `not built` |  | `/random`. [#111](https://github.com/jimmy58663/GordianXI/issues/111) |
| `0x0AA` | `GUILD_BUY` | 8 | `not built` |  | [#112](https://github.com/jimmy58663/GordianXI/issues/112) |
| `0x0AB` | `GUILD_BUYLIST` | 4 | `not built` |  | [#112](https://github.com/jimmy58663/GordianXI/issues/112) |
| `0x0AC` | `GUILD_SELL` | 8 | `not built` |  | [#112](https://github.com/jimmy58663/GordianXI/issues/112) |
| `0x0AD` | `GUILD_SELLLIST` | 4 | `not built` |  | [#112](https://github.com/jimmy58663/GordianXI/issues/112) |
| `0x0B5` | `CHAT_STD` | var | `built` | `ChatOutboundPackets.BuildChatStd` | Chat; LSB variable length. |
| `0x0B6` | `CHAT_NAME` | var | `built` | `ChatOutboundPackets.BuildChatTell` | `/tell`; LSB variable length. |
| `0x0B7` | `ASSIST_CHANNEL` (LSB) | var | `built, unused` | `ChatOutboundPackets.BuildAssistChannel` | **Differs between references:** XiPackets variable; LSB fixed (24 bytes). We send 24. `SendAssistActionAsync`: No caller of its module send method. |
| `0x0BE` | `MERITS` (LSB) | 12 | `built, unused` | `ProgressionPacketBuilder.BuildMerits` | `SendMeritsAsync`: No caller of its module send method. |
| `0x0BF` | `JOB_POINTS_SPEND` (LSB) | 8 | `built, unused` | `ProgressionPacketBuilder.BuildJobPointsSpend` | `SendJobPointsSpendAsync`: No caller of its module send method. |
| `0x0C0` | `JOB_POINTS_REQ` (LSB) | 4 | `built, unused` | `ProgressionPacketBuilder.BuildJobPointsReq` | `SendJobPointsReqAsync`: No caller of its module send method. |
| `0x0C1` | `ALTER_EGO_POINTS` (LSB) | 8 | `not built` |  | [#115](https://github.com/jimmy58663/GordianXI/issues/115) |
| `0x0C3` | `GROUP_COMLINK_MAKE` | 6 | `not built` |  | [#113](https://github.com/jimmy58663/GordianXI/issues/113) |
| `0x0C4` | `GROUP_COMLINK_ACTIVE` | 28 | `not built` |  | [#113](https://github.com/jimmy58663/GordianXI/issues/113) |
| `0x0C9` | `MYROOM_ENTER` | 8 | `not built` |  | Not in LSB. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x0CA` | `MYROOM_EXIT` | 8 | `not built` |  | Not in LSB. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x0CB` | `MYROOM_IS` | 8 | `built, unused` | `ProgressionPacketBuilder.BuildMyRoomIs` | `SendMyRoomIsAsync`: No caller of its module send method. [#93](https://github.com/jimmy58663/GordianXI/issues/93) |
| `0x0CD` | `MYROOM_PLANT` | 10 | `not built` |  | Not in LSB. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x0CE` | `MYROOM_RAISE` | 10 | `not built` |  | Not in LSB. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x0CF` | `MYROOM_HARVEST` | 10 | `not built` |  | Not in LSB. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x0D0` | `MYROOM_DIARY` | 4 | `not built` |  | Not in LSB. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x0D1` | `MYROOM_PLACE` | 12 | `not built` |  | Not in LSB. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x0D2` | `MAP_GROUP` | 8 | `not built` |  | [#113](https://github.com/jimmy58663/GordianXI/issues/113) |
| `0x0D3` | `FAQ_GMCALL` | var | `not built` |  | GM call; LSB variable length. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x0D4` | `FAQ_GMPARAM` | 8 | `not built` |  | [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x0D5` | `ACK_GMMSG` | 12 | `not built` |  | [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x0D8` | `DUNGEON_PARAM` (LSB) | 40 | `not built` |  | [#115](https://github.com/jimmy58663/GordianXI/issues/115) |
| `0x0DB` | `CONFIG_LANGUAGE` (LSB) | 40 | `built` | `ConfigOutboundPackets.BuildChatFilters`, `BuildPartyLanguages` | Kind 0 (chat filters, system message filter level) must echo the server's flag word: LSB stores the whole word. Kind 1 (party languages) has no caller. |
| `0x0DC` | `CONFIG` | 20 | `built` | `ConfigOutboundPackets.BuildConfig` | Sets or clears one config flag. |
| `0x0DD` | `EQUIP_INSPECT` | 16 | `built` | `CombatPacketBuilder.BuildCheckRequest` | `/check`. |
| `0x0DE` | `INSPECT_MESSAGE` | 128 | `not built` |  | [#110](https://github.com/jimmy58663/GordianXI/issues/110) |
| `0x0E0` | `SET_USERMSG` | 152 | `built, unused` | `ChatOutboundPackets.BuildSetUserMsg` | 152 bytes. `SetSearchMessageAsync`: No caller of its module send method. |
| `0x0E1` | `GET_LSMSG` | 144 | `built` | `ChatOutboundPackets.BuildGetLsMsg` | 144 bytes (`0x90`) [#97](https://github.com/jimmy58663/GordianXI/issues/97). |
| `0x0E2` | `SET_LSMSG` | 144 | `built, unused` | `ChatOutboundPackets.BuildSetLsMsg`, `BuildSetLsWriteLevel` | 144 bytes. `BuildSetLsMsg` sets byte 4 bit 6, `BuildSetLsWriteLevel` bit 5; LSB ignores the packet unless one is set [#97](https://github.com/jimmy58663/GordianXI/issues/97). No caller of its module send method. |
| `0x0E4` | `GET_LSPRIV` | 144 | `built, unused` | `ChatOutboundPackets.BuildGetLsPriv` | 144 bytes [#97](https://github.com/jimmy58663/GordianXI/issues/97). `RequestLinkshellPrivilegesAsync`: No caller of its module send method. |
| `0x0E7` | `REQLOGOUT` | 8 | `built` | `LifecycleOutboundPackets.BuildReqLogout` | Logout and shutdown from the stock menu (`StockUiMenuController.LogoutRequested`). |
| `0x0E8` | `CAMP` | 8 | `not built` |  | `/heal`. [#111](https://github.com/jimmy58663/GordianXI/issues/111) |
| `0x0E9` | `GLOBALUNIQUENO_REQ` | 12 | `not built` |  | Not in LSB. |
| `0x0EA` | `SIT` | 8 | `not built` |  | `/sit`. [#111](https://github.com/jimmy58663/GordianXI/issues/111) |
| `0x0EB` | `REQSUBMAPNUM` | 4 | `not built` |  | [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x0EC` | `REQLOGOUTINFO` | 4 | `not built` |  | Not in LSB. |
| `0x0F0` | `RESCUE` | 8 | `not built` |  | LSB has a handler (`0x0f0_rescue`). |
| `0x0F1` | `BUFFCANCEL` | 8 | `built` | `CombatPacketBuilder.BuildBuffCancelRequest` | Cancel a status effect. |
| `0x0F2` | `SUBMAPCHANGE` | 8 | `not built` |  | [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x0F4` | `TRACKING_LIST` | 8 | `not built` |  | Widescan. [#111](https://github.com/jimmy58663/GordianXI/issues/111) |
| `0x0F5` | `TRACKING_START` | 8 | `not built` |  | [#111](https://github.com/jimmy58663/GordianXI/issues/111) |
| `0x0F6` | `TRACKING_END` | 8 | `not built` |  | [#111](https://github.com/jimmy58663/GordianXI/issues/111) |
| `0x0FA` | `MYROOM_LAYOUT` | 16 | `built, unused` | `ProgressionPacketBuilder.BuildMyRoomLayout` | `SendMyRoomLayoutAsync`: No caller of its module send method. [#93](https://github.com/jimmy58663/GordianXI/issues/93) |
| `0x0FB` | `MYROOM_BANKIN` | 8 | `not built` |  | [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x0FC` | `MYROOM_PLANT_ADD` | 12 | `not built` |  | [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x0FD` | `MYROOM_PLANT_CHECK` | 8 | `not built` |  | [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x0FE` | `MYROOM_PLANT_CROP` | 12 | `not built` |  | [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x0FF` | `MYROOM_PLANT_STOP` | 8 | `not built` |  | [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x100` | `MYROOM_JOB` | 6 | `built, unused` | `ProgressionPacketBuilder.BuildMyRoomJob` | Mog House job change. `SendMyRoomJobChangeAsync`: No caller of its module send method. |
| `0x101` | `MYROOM_DANCER` | 4 | `not built` |  | Not in LSB. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x102` | `EXTENDED_JOB` (LSB) | 164 | `not built` |  | BLU / PUP set (LSB `extended_job`). [#115](https://github.com/jimmy58663/GordianXI/issues/115) |
| `0x104` | `BAZAAR_EXIT` | 4 | `built, unused` | `InventoryPacketBuilders.BuildBazaarExit` | Bazaar. No caller of its module send method. |
| `0x105` | `BAZAAR_LIST` | 12 | `built, unused` | `InventoryPacketBuilders.BuildBazaarList` | No caller of its module send method. |
| `0x106` | `BAZAAR_BUY` | 12 | `built, unused` | `InventoryPacketBuilders.BuildBazaarBuy` | No caller of its module send method. |
| `0x109` | `BAZAAR_OPEN` | 4 | `built, unused` | `InventoryPacketBuilders.BuildBazaarOpen` | No caller of its module send method. |
| `0x10A` | `BAZAAR_ITEMSET` | 12 | `built, unused` | `InventoryPacketBuilders.BuildBazaarItemSet` | No caller of its module send method. |
| `0x10B` | `BAZAAR_CLOSE` | 8 | `built, unused` | `InventoryPacketBuilders.BuildBazaarClose` | No caller of its module send method. |
| `0x10C` | `ROE_START` (LSB) | 8 | `built, unused` | `ProgressionPacketBuilder.BuildRoeStart` | Records of Eminence. No caller of its module send method. |
| `0x10D` | `ROE_REMOVE` (LSB) | 8 | `built, unused` | `ProgressionPacketBuilder.BuildRoeRemove` | No caller of its module send method. |
| `0x10E` | `ROE_CLAIM` (LSB) | 8 | `built, unused` | `ProgressionPacketBuilder.BuildRoeClaim` | No caller of its module send method. |
| `0x10F` | `CURRENCIES_1` (LSB) | 4 | `built` | `InventoryPacketBuilders.BuildCurrencies1Request` | Currencies page 1 request (inventory view). |
| `0x110` | `FISHING_2` (LSB) | 20 | `built, unused` | `ProgressionPacketBuilder.BuildFishingAction` | Fishing (LSB `fishing_2`). `SendFishingActionAsync`: No caller of its module send method. |
| `0x111` | (unknown) | 8 | `not built` |  | Not in LSB. |
| `0x112` | `BATTLEFIELD_REQ` (LSB) | 6 | `not built` |  | Battlefield request. [#117](https://github.com/jimmy58663/GordianXI/issues/117) |
| `0x113` | `SITCHAIR` (LSB) | 12 | `not built` |  | `/sitchair`. [#111](https://github.com/jimmy58663/GordianXI/issues/111) |
| `0x114` | `MAP_MARKERS` (LSB) | 4 | `not built` |  | Map markers. [#115](https://github.com/jimmy58663/GordianXI/issues/115) |
| `0x115` | `CURRENCIES_2` (LSB) | 4 | `built` | `InventoryPacketBuilders.BuildCurrencies2Request` | Currencies page 2 request (inventory view). |
| `0x116` | `UNITY_MENU` (LSB) | 8 | `built, unused` | `ProgressionPacketBuilder.BuildUnityMenu` | Unity. No caller of its module send method. [#94](https://github.com/jimmy58663/GordianXI/issues/94) |
| `0x117` | `UNITY_QUEST` (LSB) | 8 | `built, unused` | `ProgressionPacketBuilder.BuildUnityQuest` | No caller of its module send method. [#94](https://github.com/jimmy58663/GordianXI/issues/94) |
| `0x118` | `UNITY_TOGGLE` (LSB) | 8 | `built, unused` | `ProgressionPacketBuilder.BuildUnityToggle` | No caller of its module send method. [#94](https://github.com/jimmy58663/GordianXI/issues/94) |
| `0x119` | `EMOTE_LIST` (LSB) | 4 | `not built` |  | Emote list request. [#111](https://github.com/jimmy58663/GordianXI/issues/111) |
| `0x11A` | (unknown) | 4 | `not built` |  | Not in LSB. |
| `0x11B` | `MASTERY_DISPLAY` (LSB) | 8 | `not built` |  | Mastery display. [#115](https://github.com/jimmy58663/GordianXI/issues/115) |
| `0x11C` | `PARTY_REQUEST` (LSB) | 12 | `built, unused` | `PartyPacketBuilder.BuildPartyRequest` | **Differs between references:** XiPackets 12 bytes; LSB's struct adds `padding01` for 16, which it enforces. We send 16. `SendPartyRequestAsync`: No caller of its module send method. |
| `0x11D` | `JUMP` (LSB) | 12 | `built` | `CombatPacketBuilder.BuildJumpRequest` | `/jump`. |

## Lobby and login server

`LsbLoginClient` (`src/Gordian.Core/Network/LandSandBoat`) logs in to LSB without the retail bootloader ([session-and-packets.md](session-and-packets.md#bootloader-handoff-and-direct-login-phase-2)). It talks to three LSB services. Only the xi_view packets are in XiPackets `lobby/`; there the command byte sits at offset 8, after a u32 size and the `IXFF` magic. Sizes are whole packets in bytes.

| Service | Dir | Id | Name | Size | GordianXI status | Notes |
|---|---|---|---|---|---|---|
| xi_connect (TLS, 54231) | C2S | `0x10` | login attempt (LSB) | JSON | `built` | LSB-only JSON command; returns the account id and session hash. Not in XiPackets. |
| xi_data (54230) | C2S | `0xA1` | character list request (LSB) | 28 | `built` | Account id and session hash. Not in XiPackets. |
| xi_data | S2C | `0x03` | character list (LSB) | 328 | `decoded` | Not the xi_view `0x03` below. |
| xi_data | C2S | `0xA2` | character select + Blowfish key (LSB) | 28 | `built` | 20-byte client key and the character id; LSB then sends `0x0B` on xi_view. |
| xi_data | S2C | `0x02` | select confirmation (LSB) | 5 | `decoded` | Awaited after xi_view `0x07`, before `0xA2`. |
| xi_view (54001) | C2S | `0x26` | `RequestLobbyLogin` | 152 | `built` | We send 128 bytes (session hash, version string at 0x74); LSB's `view_session.cpp` does not check the length. |
| xi_view | S2C | `0x05` | `ResponseKey` | 40 | `decoded` | The expected reply to `0x26`. |
| xi_view | S2C | `0x04` | `ResponseError` | 36 | `decoded` | The u16 error code at 32 is reported and the login stops. |
| xi_view | S2C | `0x20` | `ResponseChrInfo2` | var | `decoded` | 140-byte slots after a u32 count at 28; names and ids for the profile's character ([#179](https://github.com/jimmy58663/GordianXI/issues/179)). |
| xi_view | C2S | `0x07` | `RequestSelectChr` | 88 | `built` | We send 64 bytes (character id 28, name 36); accepted by LSB. |
| xi_view | S2C | `0x0B` | `ResponseNextLogin` | 72 | `decoded` | Character name 36, map server IP 56 and port 60; the session moves to the world protocol. |
| xi_view | S2C | `0x03` | `ResponseOk` | 32 | not handled | |
| xi_view | C2S | `0x1F` | `RequestGetChr` | 44 | `not built` | [#35](https://github.com/jimmy58663/GordianXI/issues/35) |
| xi_view | C2S | `0x24` / S2C `0x23` | `RequestQueryWorldList` / `ResponseWorldList` | 44 / var | `not built` | [#35](https://github.com/jimmy58663/GordianXI/issues/35) |
| xi_view | C2S | `0x22`, `0x21` | `RequestCreateChrPre`, `RequestCreateChr` | 96, 144 | `not built` | [#33](https://github.com/jimmy58663/GordianXI/issues/33), [#35](https://github.com/jimmy58663/GordianXI/issues/35) |
| xi_view | C2S | `0x14` | `RequestDeleteChr` | 52 | `not built` | [#34](https://github.com/jimmy58663/GordianXI/issues/34), [#35](https://github.com/jimmy58663/GordianXI/issues/35) |
| xi_view | C2S | `0x28` | `RequestRenameChr` | 68 | `not built` | [#35](https://github.com/jimmy58663/GordianXI/issues/35) |
| xi_view | C2S | `0x2B` | `RequestMoveGMChr` | 68 | `not built` | GM only. |

The other XiPackets folders are covered in [session-and-packets.md](session-and-packets.md#xipackets-coverage-audit-2026-09-28): `cache/` is the search server ([#119](https://github.com/jimmy58663/GordianXI/issues/119)) and `patch/` is not needed with LSB.
