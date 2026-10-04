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

- **Candidates:** spawned, not hidden or invisible, named entities other than elevators and ships, within 50 yalms of the player (the monster draw distance) and inside the camera's horizontal field of view, ordered left to right on screen (at the same position, nearest first). The screen side is worked out on the ground plane in the renderer's mirrored display space ((-x, z); the camera's yaw is a display-space yaw, `TargetCycling.ViewBasis`), with the orbital eye `Distance` behind the player; projecting raw world positions instead mirrors left and right and misplaces the eye (the first in-game test: d-pad reversed, only a Rarab 5 yalms away targetable). `Gather_ScreenSideMatchesTheRenderedCamera` checks the side against the display camera's own matrices.
- **A current target that leaves the screen** (or the 50-yalm range) stays in the order past the edge it left by, so the next press carries on from that side (maintainer's in-game test).
- **Tab / Shift+Tab** (`TargetNearest` / `TargetPrevious`, also the right / left triggers): with nothing targeted, the closest candidate by distance; otherwise the next candidate to the right / left of the current target, wrapping to the left-most / right-most. Your own character is never picked.
- **D-pad right / left** (`TargetCursorRight` / `TargetCursorLeft`): with nothing targeted, yourself; otherwise the next candidate to the right / left, with you in the order at your own screen position (the centre in first person); past the edge of the screen it returns to you. Profiles saved before these actions existed have the d-pad moved onto them on load (`InputProfile.EnsureTargetCursorBindings`).
- **Gamepad A** (Confirm from a gamepad button, `InputState.WasActionTriggeredByGamepad`) with nothing targeted targets the closest candidate. Confirm from the keyboard (Space, Enter, keypad 5) never targets; on the keyboard only Tab and Shift+Tab do.
- **F1-F6:** F1 targets yourself, F2-F6 (`TargetParty1`-`TargetParty5`) the other members of your own party in party window order (`PlayerActionService.SetTargetByPartySlot`); an empty slot or a member outside the zone does nothing.

### Engage versus lock-on (#137)

Evidence: the maintainer's retail recording of 2026-10-03 (29 s, an Island Rarab in a canyon, 1 fps frames f01-f29).

**Retail-confirmed from the recording:**
- Engaging (f03-f05: "Start auto-attack", weapon drawn, target window turning red) does not turn the character. It stays facing the camera, away from the Rarab, through f05-f09.
- Facing away from the target the client keeps reporting "Unable to see the Island Rarab" / "is out of range" (chat log from f09): auto-attack needs the player to face the target, and the client does not turn you for it.
- The command menu stays open the whole time while engaged (f05-f29).
- Moving while engaged with the target window in its red state (f11-f29): the character runs in its input direction and faces where it runs (not held toward the Rarab), and the camera stays exactly where the player put it; it neither swings back behind the character nor toward the target (it only moves when the player rotates it, f11 to f19).
- So retail lock-on does not force the character's facing and does not move the camera. Anything else it does (target retention, Tab behaviour) is not visible in the recording and is not implemented.

**What GordianXI does:**
- Engage / disengage touch only the engaged flag and target. `PlayerActionService.IsLockedOn` changes only on the lock-on key (T / NumPad `*`), `/lockon`, target clear, `/attackoff`, Cancel, or auto lock-on. It draws the stock "Locked" overlay and nothing else: `PlayerLocomotionController` no longer turns the character or the camera for it (the earlier lock-on locomotion and camera tracking were removed after the recording).
- `AutoLockOnEngage` (`/lockon auto [on|off]`, default **on**, saved per character): engaging selects the fought target if none is selected and locks on. This is the maintainer's request; no retail config page has such a row (the ten decoded pages were checked; "Controls: Auto-target during battle" is the server's re-target-after-a-kill flag, LandSandBoat `AutoTargetOffFlg`). Not confirmed: whether retail locks on automatically, and what the red target window in the recording means exactly.
- `ToggleLockOn` with nothing selected but a server-started engagement selects the engaged target and locks on.

**Why T and NumPad `*` did nothing in-game (found 2026-10-04):** `PlayerLocomotionController.Update` only evaluates `UpdateActionTriggers` when no stock menu is open (the menu owns Confirm/Cancel and target keys), and the lock-on toggle lived there. While engaged the command menu is open (as in retail), so the key was dropped; `/lockon` goes through the chat path and was unaffected. The toggle is now evaluated before that gate (`UpdateLockOnToggle`). The key path is traced in `logs/gordian_system_<date>.log` under `LockOn`: `KeyDown ... reached the game input` (ViewportWindow), `Lock-on key held (action bound and held=..., triggered=..., menuOpen=...)` and `Lock-on toggled by key`. A missing first line means Avalonia never delivered the key; a missing second with `bound and held=False` means the loaded profile lacks the binding (saved profiles with none get T and NumPad `*` on load, `InputProfile.EnsureLockOnBindings`).
