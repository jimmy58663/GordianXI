# Controller follow-up (#648)

## Evidence and scope

The owner reports that Autorun's assignment row displays **RB**, but physical **LB** activates it, and D-pad Down does not select self. The owner has accepted the corrected camera Normal directions. This follow-up does not change camera math, settings defaults, driver mappings or the owner's files.

### Proven vertical D-pad defect

The old profile bound D-pad Up/Down to `TargetParty1`/`TargetParty2`. Those are **fixed other-member slots** (F2/F3), not cycling actions. `UpdateActionTriggers` called `SetTargetByPartySlot(1/2)`: solo players get no target, and parties jump straight to another member. The displayed “Target Party Up/Down” function did not describe that implementation.

`TargetPartyUp`/`TargetPartyDown` now have separate action IDs. Outside menus, either direction starts at self when no own-party member is selected; subsequent presses step through own-party window order in opposite directions and wrap, including self. Members absent/unspawned in the current world and other alliance parties are excluded. Solo self does not require a party packet. Menu navigation and the existing sub-target cursor keep their menu-context behavior. F1–F6 remain fixed target shortcuts. The initial Down-to-self expectation is owner-reported; exact cycling/wrapping policy remains subject to owner retail acceptance rather than independently established controller documentation.

Saved profiles migrate only the unambiguous *old-default-shaped* pairs (D-pad Up + MenuUp/TargetParty1, Down + MenuDown/TargetParty2, with no competing gameplay chord). Either cycling action's presence, including an empty list, marks migration complete. Arbitrary custom buttons, modifiers, keyboard bindings, shoulders and camera settings are not reset. Existing custom fixed-slot bindings remain available as two explicit F2/F3 assignment rows; an old profile deliberately configured identically to the old defaults is indistinguishable and will migrate. Remapped old fixed-slot rows remain fixed-slot rather than being silently reinterpreted as cycling; users can assign the new cycling row explicitly.

### Shoulder report: cause not established

- Pinned `ppy.SDL3-CS` **2026.1002.1**, repository commit `442a5a2346600f574f654e95a12d6bf4305a4732`, defines SDL LEFT_SHOULDER=9, RIGHT_SHOULDER=10, DPAD_UP=11, DPAD_DOWN=12 (`SDL3/ClangSharp/SDL_gamepad.g.cs`).
- `SdlGamepadDriver.Poll` explicitly maps these enum names to Gordian `LeftShoulder`/`RightShoulder` flags (bits 8/9); it does not shift the SDL numeric value. Its native synthetic test preserves 9→LB and 10→RB, with correct up/down and up-positive stick normalization.
- `XInputGamepadDriver` uses the separate XInput masks 0x0100→LB and 0x0200→RB. Windows fallback is source-inspected, not runtime-tested here.
- `InputChord` LB/RB aliases, default `InputProfile` RB→Autorun/LB→Next Character, and `GamepadAssignments.ButtonName` agree. The stock assignment menu obtains its displayed value from `GamepadAssignments.Describe` on the live profile; capture reads `GamepadButtonsPressed`, edits the hub, and sessions receive new copies.
- Synthetic session tests verify the label matches the button actually toggling autorun with both defaults and an intentional LB custom assignment, including disk reload and hub refresh. LT/RT macro-palette consumption suppresses shoulder autorun as before.

This does **not** identify the currently connected physical controller, its mode, OS/Steam remapping, or the actual owner profile. Historical device names are not current-device evidence. No speculative global LB/RB swap is warranted.

**Next owner-only read-only check:** in the existing **Controls & Input** live monitor, record the connected-device text and Buttons value while pressing/releasing physical LB once, then physical RB once, without changing any assignments. Record the Autorun row too, in the same running client/session. If physical LB reports `RightShoulder`, the mismatch is before logical binding resolution; if it reports `LeftShoulder` but RB remains displayed while autorun toggles, investigate the live session/profile association next. This monitor is app-scoped; no global keyboard capture, raw-device grab or settings reset is needed.

## Regression verification

`ControllerFollowupTests` exercises real disconnected sessions: no target/self/party/other-party targets, solo mode, both directions, wrapping, absent-zone members, menu and sub-target context, macro suppression, fixed keyboard slots, custom mappings, one-time migration, disk persistence and copied-session refresh. Existing camera direction/config/session tests remain unchanged and run in the full solution.

RED against the initial source: both vertical directions select another party member instead of self. After correction they pass. Disabling the Down dispatch independently makes Down fail again. Swapping the production SDL shoulder mapping independently makes the native probe fail at SDL 9→LeftShoulder; restoring the source passes. A deliberately repeated migration fails the custom fixed-slot reload test; the one-time guard passes.

## Native SDL virtual-input probe (Linux)

`tests/Gordian.ControllerProbe` is a standalone executable, not part of normal unit-test discovery. It feeds an SDL virtual joystick through the actual pinned native SDL and production driver, then `InputState`. It does not synthesize system-wide input. It requires a private device namespace and refuses ordinary desktop execution. Build before removing network access; run from the repository root:

```sh
dotnet build tests/Gordian.ControllerProbe
mkdir -p .artifacts/controller-probe/home
bwrap --ro-bind / / --bind "$PWD" "$PWD" --dev /dev \
  --tmpfs /run --tmpfs /tmp --unshare-net \
  --unsetenv DISPLAY --unsetenv WAYLAND_DISPLAY --unsetenv DBUS_SESSION_BUS_ADDRESS \
  --setenv HOME "$PWD/.artifacts/controller-probe/home" \
  --setenv XDG_CONFIG_HOME "$PWD/.artifacts/controller-probe/home/config" \
  --setenv XDG_DATA_HOME "$PWD/.artifacts/controller-probe/home/data" \
  --setenv GORDIAN_ISOLATED_CONTROLLER_PROBE 1 -- \
  dotnet tests/Gordian.ControllerProbe/bin/Debug/net10.0/ControllerProbe.dll
```

The probe first asserts no physical joystick is visible, then checks native shoulder/d-pad ingress, release and stick axes. Native virtual-input success is not physical-device or live-game acceptance. No graphics changes or graphics-driver crash claims are made by this follow-up.
