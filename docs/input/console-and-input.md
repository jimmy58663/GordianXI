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

`TargetCycling` (Core) picks the target for the cycling keys: Tab / Shift+Tab, the right / left triggers and d-pad right / left (the `TargetNearest` / `TargetPrevious` actions, labelled Next Target (Right) / (Left)), and for Confirm (gamepad A, Space in the Compact layout) with nothing targeted.

- **Candidates:** spawned, not hidden or invisible, named entities other than elevators and ships, within 50 yalms of the player, in front of the camera and inside its horizontal field of view. The player is a candidate (self-target). Each gets a horizontal screen position (-1 left edge to +1 right edge, taken 1 yalm above its feet) and its distance from the player.
- **Nothing targeted** (or the target is off screen): the nearest candidate by distance on the pressed side of the screen centre; Confirm takes the nearest on either side. When that side is empty the other side's nearest is taken. The player is only picked when nobody else is on screen.
- **A target on screen:** the next candidate over in the pressed direction, ordered left to right on screen (at the same position, nearest first), wrapping from one edge to the other so every candidate is reachable. Picking only the nearest on that side would skip a far target between two nearer ones.
- **Evidence:** the issue's (#134) description of the retail feel and the maintainer's choice of the hybrid rule with wrapping; not yet checked against retail or Windower captures for tie-breaks.
