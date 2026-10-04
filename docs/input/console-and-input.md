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

### Engage versus lock-on (#137, provisional)

- Engaging (`/attack`, the command menu, S2C engage) no longer locks on. `CombatState.Engage` / `Disengage` only touch the engaged flag and target; `PlayerActionService.IsLockedOn` changes only on `ToggleLockOn` (T / NumPad `*` / `/lockon`), target clear, `/attackoff`, or Cancel. `CombatState.IsLockedOn` just mirrors the action service's flag.
- The single decision is `PlayerLocomotionController.GetLockOnTarget()`: a target only when `IsLockedOn`. Both the lock-on locomotion (heading toward the target every tick, strafe/backpedal relative to it) and the orbital camera tracking use it. Engaged but not locked on is ordinary free movement with a free camera. The stock UI lock overlay already read `ActionService.IsLockedOn` only.
- **PROVISIONAL, not confirmed against retail.** The maintainer must check in retail/Windower: (1) does engaging turn the character at all; (2) while engaged, unlocked, does walking turn you freely; (3) does the character auto-turn to the target when a swing lands or when the server reports "unable to see"; (4) does the camera auto-follow while engaged; (5) whether disengaging or the target dying clears lock-on (currently the target-clear path does, plain disengage does not); (6) whether retail locks on automatically in some engage paths (e.g. the engage key vs a menu). To restore the old behaviour change only `GetLockOnTarget` and `PlayerActionService.AttackAsync` (the old `SetLockOn(true)`).

**Update after the first in-game test (#137).**
- *Retail-confirmed (maintainer, in-game):* engaging without lock-on does not turn the character.
- *Auto lock-on:* no retail config page has an auto lock-on row (the ten decoded pages were checked; "Controls: Auto-target during battle" is the server's re-target-after-a-kill flag, LandSandBoat `AutoTargetOffFlg`, not lock-on). GordianXI therefore has its own setting `StockUiSettingKey.AutoLockOnEngage` (client scope, saved per character, **default off**), set with `/lockon auto [on|off]`. When on, engaging (`CombatState.EngagementChanged`, so a server-started engage counts too) selects the fought target if nothing is selected and locks on. Not retail-confirmed: that retail has such an option, and its default.
- *Turning:* lock-on now turns the character toward the target at 720 degrees/s (`PlayerLocomotionController.FaceTarget`, the same turn rate as stick turning) instead of snapping, so neither a manual toggle nor auto-lock turns the character in one frame. Unconfirmed: whether retail's lock-on turns gradually or at once.
- *Lock-on toggle:* `ToggleLockOn` with nothing selected but an engagement running (server-started) now selects the engaged target and locks on, instead of doing nothing. Saved input profiles with no `ToggleLockOn` binding get T and NumPad `*` on load (`InputProfile.EnsureLockOnBindings`). The reported failure of T, NumPad `*` and `/lockon` could not be reproduced in unit tests driving the real key path (engaged, key down/up, target selected), so these two gaps are the candidates.
