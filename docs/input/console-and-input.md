# Command Console & Input

> The in-client command console, `PlayerActionService`, and keyboard/mouse/gamepad input (Phase 5). Status and open work: [ROADMAP.md](../../ROADMAP.md) and GitHub Issues.

## Command console and action service

- Unified `PlayerActionService` in `Gordian.Core` (typed methods for combat, magic, abilities, targeting, and locomotion)
- In-client interactive console tab in `Gordian.App` with history navigation (Up/Down arrows), color-coded output, autoscroll, and slash commands (`/pos`, `/target`, `/attack`, `/ws`, `/magic`, `/vitals`, `/nearby`, `/moveto x y z`, `/collision [ground|walls|entities|all] [on|off]`, `/uilayout [scale n | reset | <window> <args>]`, `/lockstyle [on|off]`)
- Command permission & policy gating: distinction between standard vanilla commands (always allowed), server admin passthrough (`!pos`, `!zone`), and synthetic locomotion gated behind `FeatureRestrictions.Movement`
- Console text selection & copy support (`SelectableTextBlock`), robust comma/parenthesis coordinate parsing for `/moveto`, categorized command discovery (`/help`, `/commands`), GM command discovery (`/gmhelp`, `/gmcommands`) gated by GM permissions (`"You are not a GM."`), and `FeatureRestrictions` filtering
- Headless/early verification of movement, combat, and zoning against live server without requiring 3D rendering
- Robust locomotion keepalive synchronization and automated C2S 0x016 CharReq entity discovery for reliable live server and Windower multi-session pairing

## Input subsystem

- **Keyboard & Mouse:** Default layouts for FFXI Compact (WASD + IJKL camera) and FFXI Full (Numpad) with smart text-input isolation
- **Gamepad / Controller:** Full XInput, DirectInput, and SDL/Silk gamepad support (Xbox, PlayStation, generic HID) with deadzone, rumble, and axis calibration
- In-game chat line: Enter (outside menus) or the `OpenChat` key (slash; Space in the Full layout; Back on a gamepad, B closes it) opens the stock chat input, which then owns the keyboard and runs lines through the same dispatcher as the console (see [ui/stock-ui.md](../ui/stock-ui.md#chat-and-log-windows-chunk-5)); walk/run toggle is on keypad `/`; keypad + / gamepad Y select a log window to scroll; Tab / Shift+Tab cycle targets by distance
- Rebindable control mapping engine with JSON persistence and modifier key support (`keybinds.json`)
- Real-time 60Hz locomotion and camera controller updating `WorldEntity` coordinates and dispatching to server Pos loop
- Dedicated "Controls & Input" dashboard tab with live input monitor and preset switcher
