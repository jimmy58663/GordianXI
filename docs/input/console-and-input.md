# Command Console & Input

> The in-client command console, `PlayerActionService`, and keyboard/mouse/gamepad input (Phase 5). Status and open work: [ROADMAP.md](../../ROADMAP.md) and GitHub Issues.

## Command console and action service

- Unified `PlayerActionService` in `Gordian.Core` (typed methods for combat, magic, abilities, targeting, and locomotion)
- In-client interactive console tab in `Gordian.App` with history navigation (Up/Down arrows), color-coded output, autoscroll, and slash commands (`/pos`, `/target`, `/attack`, `/ws`, `/magic`, `/vitals`, `/nearby`, `/moveto x y z`, `/collision [ground|walls|entities|all] [on|off]`, `/anchor [on|off]`, `/uilayout [scale n | reset | <window> <args>]`, `/lockstyle [on|off]`, and the C2S 0x01A kinds `/fish`, `/dig`, `/sprint`, `/blockaid [on|off]`, `/callforhelp`, `/monsterskill <id>`, `/refa <name|all>`; see [session-and-packets.md](../network/session-and-packets.md#c2s-0x01a-action-kinds))
- Command permission & policy gating: distinction between standard vanilla commands (always allowed), server admin passthrough (`!pos`, `!zone`), and synthetic locomotion gated behind `FeatureRestrictions.Movement`
- Console text selection & copy support (`SelectableTextBlock`), robust comma/parenthesis coordinate parsing for `/moveto`, categorized command discovery (`/help`, `/commands`), GM command discovery (`/gmhelp`, `/gmcommands`) gated by GM permissions (`"You are not a GM."`), and `FeatureRestrictions` filtering
- Headless/early verification of movement, combat, and zoning against live server without requiring 3D rendering
- Robust locomotion keepalive synchronization and automated C2S 0x016 CharReq entity discovery for reliable live server and Windower multi-session pairing

## Input subsystem

- **Keyboard & Mouse:** Default layouts for FFXI Compact (WASD + IJKL camera) and FFXI Full (Numpad) with smart text-input isolation
- **Gamepad / Controller:** Full XInput, DirectInput, and SDL/Silk gamepad support (Xbox, PlayStation, generic HID) with deadzone, rumble, and axis calibration
- In-game chat line: Enter (outside menus) or the `OpenChat` key (slash; Space in the Full layout; Back on a gamepad, B closes it) opens the stock chat input, which then owns the keyboard and runs lines through the same dispatcher as the console (see [ui/stock-ui.md](../ui/stock-ui.md#chat-and-log-windows-chunk-5)); walk/run toggle is on keypad `/`; keypad + / gamepad Y select a log window to scroll; Tab / Shift+Tab cycle targets left and right on screen (see [Target cycling](#target-cycling))
- Rebindable control mapping engine with JSON persistence and modifier key support (`keybinds.json`)
- Real-time 60Hz locomotion and camera controller updating `WorldEntity` coordinates and dispatching to server Pos loop
- Dedicated "Controls & Input" dashboard tab with live input monitor and preset switcher

### Target cycling

`TargetCycling` (Core) picks the target for the targeting keys, matching retail (checked in retail by the maintainer, 2026-09-29, #134).

- **Candidates:** spawned, not hidden or invisible, named entities other than elevators and ships, within 50 yalms of the player, in front of the camera and inside its horizontal field of view, ordered left to right on screen (screen position taken 1 yalm above the feet; at the same position, nearest first). A target that is off screen counts as nothing targeted.
- **Tab / Shift+Tab** (`TargetNearest` / `TargetPrevious`, also the right / left triggers): with nothing targeted, the closest candidate by distance; otherwise the next candidate to the right / left of the current target, wrapping to the left-most / right-most. Your own character is never picked.
- **D-pad right / left** (`TargetCursorRight` / `TargetCursorLeft`): with nothing targeted, yourself; otherwise the next candidate to the right / left, with you in the order at your own screen position (the centre in first person); past the edge of the screen it returns to you. Profiles saved before these actions existed have the d-pad moved onto them on load (`InputProfile.EnsureTargetCursorBindings`).
- **Gamepad A** (Confirm from a gamepad button, `InputState.WasActionTriggeredByGamepad`) with nothing targeted targets the closest candidate. Confirm from the keyboard (Space, Enter, keypad 5) never targets; on the keyboard only Tab and Shift+Tab do.
- **F1-F6:** F1 targets yourself, F2-F6 (`TargetParty1`-`TargetParty5`) the other members of your own party in party window order (`PlayerActionService.SetTargetByPartySlot`); an empty slot or a member outside the zone does nothing.

