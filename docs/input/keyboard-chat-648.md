# Keyboard Size and chat entry (#648)

> **Independent-candidate scope:** This branch is based on upstream main without
> PR #646 or PR #647. Older native/GPU and owner results below describe the
> original stacked candidate, not this separated revision. See
> [independent review and test boundaries](independent-648.md).


**Follow-up:** [Keyboard catalog v1](keyboard-presets.md) now implements actual
keyboard layers, frozen legacy maps, explicit conversion and confirmed keyboard-only
restore. The narrative below records the earlier chat-only patch and proposal;
its statements that mode changes never affect bindings describe that earlier revision.
The catalog comparison lists remaining retail unknowns and unimplemented shortcuts.

## Evidence and scope

The issue was observed on the local viewport/punctuation stack, not independently reproduced on upstream main. Investigation started at `d2943ca1791beaec1545bfcfa4c453c36101ff0c`.

- [Square Enix's PC manual](https://support.na.square-enix.com/document/manual/20/FFXI_manual_vc09_AE5.pdf), Game Controls: identifies Config → Misc. 2 and Full-size versus Compact; reserves function keys and Ctrl/Alt macros. It does **not** fully specify the type-to-chat distinction.
- [Horizon Controls](https://horizonffxi.wiki/Controls) lists Space for opening chat in both layouts, but its compact/full movement column headings appear reversed (numpad versus WASD). Do not use that table alone to infer mode semantics.
- Independent retail-era reports corroborate **Full-size: type directly; Compact: explicitly open chat**: [GameFAQs keyboard configuration thread](https://gamefaqs.gamespot.com/boards/555735-final-fantasy-xi/44362857) describes ordinary keys opening chat until Compact is selected, with function/Ctrl/Alt exceptions; [BlueGartr keyboard-not-typing thread](https://www.bluegartr.com/threads/113497-Keyboard-Not-Typing-in-FFXI) describes D moving instead of typing; [2021 new-player discussion](https://www.reddit.com/r/ffxi/comments/p4csco/new_player_and_completely_lost/) explicitly contrasts direct typing in Full with Space in Compact, discussing Compact 1/2.
- Current repository evidence: `StockUiConfigPages` assigns Keyboard Size values 0 Full, 1 Compact 1, 2 Compact 2. The November 7 retail key-assignment evidence in [stock UI](../ui/stock-ui.md#keyboard-key-assignment-495) uses Compact 2. `KeyboardAssignments.ResetDefaults` already identifies the legacy Full preset by its `Full` name prefix. Previously `KeyboardSize` was stored but never read by chat routing; `TryOpen` supported only explicit open keys.

This is a source-triangulated, narrow chat policy, **not a new live retail capture** or a claim that Compact 1 and 2 are identical in movement/mouse behavior. Both compact choices retain explicit-opening chat semantics. Contributor in-game confirmation and Windows remain pending.

## Routing and persistence

`StockUiChat.TryOpenForTyping` reads the character's live `Settings` delegate on each candidate press. `CharacterSession` already binds that delegate to `ActionService.UiSettings`; `StockUiSettingsStore` saves changed client values and loads them for new sessions. No cached copy or extra setting is needed. An explicitly selected Keyboard Size overrides the dashboard preset for **chat entry only**. An untouched/reset page now uses the same default 0 (Full) that the menu displays. Previously `HasValue` made that visible Full row silently follow the Compact dashboard profile until a toggle persisted a value. Only callers without a character settings object retain the legacy profile-name fallback. See the follow-up below.

Full permits ordinary main-keyboard letters, digits and punctuation, with optional Shift/Caps Lock. It does not automatically consume function keys, the numeric keypad, navigation, Ctrl/Alt/Super shortcuts, or modifier keys. `ViewportWindow` opens an empty line before gameplay receives the initiating key, leaves that press unhandled, and inserts **only the subsequent platform TextInput**. This preserves native case/layout and empty X11 key symbols; the initiating character is not synthesized or duplicated. Existing explicit slash handling remains unchanged.

Enter and Space open an empty line outside a menu/Confirm context; existing explicit `OpenChat` bindings still work. Compact Space previously fell through as Confirm even with no target/menu. Its opening space is swallowed, not inserted. Existing Confirm handling with a target, dialog or selected log remains intact. Macro/name fields take priority over auto-opening chat; hidden stock chat captures nothing.

Selecting Keyboard Size does **not** reset the dashboard's keyboard, mouse or gamepad mappings. This preserves custom bindings and sensitivity and avoids conflating two independently stored settings. This change does not implement mode-specific replacement of movement mappings or new Compact 1/2 layouts. The dashboard Full (Numpad) preset continues to supply numpad movement.

## Regression checks

```sh
dotnet test GordianXI.slnx
dotnet build tests/Gordian.InputProbe
python3 tests/Gordian.InputProbe/run-probe.py --input-layout us
python3 tests/Gordian.InputProbe/run-probe.py --input-layout de
```

Use the isolated Xvfb/dependency environment from [the independent input guide](independent-648.md). The input-only fixture does not initialize the GPU viewport. Never run the fixture on an owner's display.

`StockUiKeyboardChatTests` covers explicit modes versus both presets, immediate changes/reset, save/reload, per-character independence, reserved keys/modifiers, Space/Confirm precedence and preservation of bindings. The real XTEST probe covers selected Full versus both Compact modes, the first character exactly once, shifted empty-symbol input, Caps Lock, shortcut/numpad/function-key isolation, hidden chat and stock fields. It also constructs fresh sessions from saved values for all three modes and tests shared-profile refresh without losing the selection. It retains the existing punctuation and explicit-opening checks. These tests use disconnected synthetic sessions, not retail assets or accounts.

Before the fix, the native first-letter assertion failed; after the type-to-chat fix it passed. A second RED showed Compact Space failed to open chat; its narrow fix passed. No manual owner gameplay or Windows acceptance is implied by these automated checks.

Combined camera/keyboard regression: `CameraConfigSessionTests.KeyboardModeSwitch_PreservesCharacterCameraMotionAcrossRefreshAndReload` switches Full → Compact 1 → Compact 2 → Full with each first/third-person camera axis inverted. It checks actual synthetic stick motion, mouse/pad flag parity, an unaffected second character, shared-profile refresh and fresh-session loads from persisted bytes. Disabling either fix independently fails all four cases at the relevant motion or chat assertion. The integrated build passes the full solution suite, the US/German native-input matrix on software Vulkan and RTX 3090, and the default production viewport probe on both drivers with strict validation. These are isolated fixtures; physical controller, owner compositor/game and Windows acceptance remain pending. The known extended NVIDIA warm-retirement failure is outside this change and is not cleared by the default probe pass.

## Keyboard follow-up: initial Full and native keypad identity

Two independent regressions were reproduced against `e9d6984f` without owner data:

1. A new character displays Full (the schema default 0), but `HasValue` was false, so chat followed the default Compact profile instead. Toggling persisted 0 and made it work. Chat now reads `GetValue` whenever character settings exist, including absence/reset. The constructor installs a live delegate after synchronously loading settings; the isolated startup, fresh disk-load and shared-profile-refresh checks found no additional application race. This reproduces the reported symptom, not proof of the owner's private saved bytes.
2. Native XTEST with Num Lock **verified OFF** delivered `Key.Up`, `PhysicalKey.NumPad8`; the old mapper used only `Key`, so movement became camera navigation. With Num Lock ON it delivered `Key.NumPad8`. Keypad Enter was logically Return. Gameplay now uses Avalonia's physical keypad identity on press and release, while ordinary typing/layouts and open text fields retain logical keys/platform text. Dedicated arrows remain distinct. No key hooks or owner input capture are used.

Compatibility: no profile, bindings, sensitivity, camera flag or saved setting is rewritten or migrated. Existing explicit Compact choices stay Compact. Characters with **no saved KeyboardSize** now honor Gordian's displayed Full value for chat, even if the dashboard profile is Compact/custom. This is an intentional consistency correction, not a claim that Full is modern retail's new-character default.

### Authoritative defaults and remaining product decision

Square Enix's [PC manual, Game Controls](https://support.na.square-enix.com/document/manual/20/FFXI_manual_vc09_AE5.pdf) states: keypad 8/2/4/6 move; 7 toggles autorun while moving; / toggles walk/run; * heals without a target or locks the target; + selects the active window; - opens the menu; Tab/0 cycles targets; Page Up/9 zooms in and Page Down/3 zooms out.

Square Enix's [March 27, 2012 update, post 298313](https://forum.square-enix.com/ffxi/threads/22099-March-27-2012-%28JST%29-Version-Update?p=298313&viewfull=1#post298313), **New Keyboard Controls**, specifies Compact 2 WASD movement, Q/E turning in place with camera following, Shift+A/D strafe with camera locked, and arrow-camera priority. Its additional-controls table confirms R versus Full keypad 7 autorun, V versus keypad 5 view toggle, and ./, versus keypad 9/3 zoom. Post 298240's dev1100 section says new characters start in **Compact 2**, not Full. The scheme supports wheel-held mouse movement; Compact 1/2 are not interchangeable presets. These are official published controls, not inferred from a user-copied table or the profile name.

Current `CreateFullNumpad` differs: 7/9 strafe, Ctrl+7 autorun, 0 cancel, 5 confirm, * lock-only, missing plain 9/3 zoom. Selecting Misc. 2 Keyboard Size still changes **chat policy only**, not the retained dashboard bindings; selecting Full while retaining Compact bindings therefore does not enable numpad movement, even after this native event fix. No blanket preset reset was performed. Full/Compact 1/Compact 2 preset parity and a non-destructive custom-binding migration remain open.

Safe proposal for a separately approved change: represent an explicit base keyboard mode separately from per-action user overrides, distinguish inherited defaults from customized/unbound actions, and apply mode defaults only to inherited entries. Legacy files lack that provenance; preserve ambiguous entries and offer explicit reviewed reset/import rather than guessing custom intent from a profile name. Do not change existing saved bindings or the default to Compact 2 silently.

### Verification receipts

Baseline under an operator-owned absolute TMPDIR: 4937 passed. Follow-up: 4954 passed (17 additional mapper cases), none failed/skipped. The modified existing engine default test first failed with expected True/actual False; native old-source controls independently fail `displayed Full default types before any toggle` and `Full keypad reaches native game input`. Restored production source passes.

The reusable native fixture verifies 3 modes × 2 queried Num Lock states × 16 physical keypad keys, releases, a Num Lock transition while held, distinct dedicated arrows, Full-preset MoveForward action resolution, logical editing versus numeric text, fresh startup before toggling, absent and all three persisted settings, and shared-profile refresh. Existing Tab/completion, punctuation and camera suite remain intact. US and German X11 runs pass on isolated software Vulkan and NVIDIA with strict validation. These tests prove event identity independently of preset semantics; they do not claim Compact binds keypad movement. Installed-art completion is not run in this follow-up; no owner game files are read.

Initial non-isolated full-suite attempts hit four pre-existing shared temporary certificate-file permission failures, including on unchanged baseline; private TMPDIR fixes the fixture environment without changing tests. Owner Num Lock state, owner compositor, Windows and in-game acceptance remain unknown. The known graphics warm-retirement failure remains outside scope.


### Next patch design (proposal only; not implemented)

Do not call `KeyboardAssignments.ResetDefaults` from the Keyboard Size change
handler: it removes every keyboard chord, chooses by profile name, and would erase
custom input. Do not fill missing legacy keys heuristically either; an absent key
may be deliberately unbound.

1. Introduce a versioned **keyboard-only** definition: a catalog revision and base
   mode (`Full`, `Compact1`, `Compact2`), plus per-action keyboard overrides. Missing
   override means inherit; an empty list means explicitly unbound; a nonempty list
   replaces that action's keyboard chords. Keep mouse/gamepad chords, camera flags,
   sensitivities and unknown serialized names separate and unchanged. Context is
   part of resolution: explicit chords suppress conflicting inherited chords only
   in the same gameplay/menu context. Never resolve collisions by dictionary order.
2. Preserve legacy files as a **frozen keyboard map**, including missing actions,
   empty lists and unknown data. Do not infer inheritance from names or equality
   to historical defaults. A reviewed opt-in conversion can show a before/after
   diff and mark selected entries inherited; cancel must leave bytes unchanged.
   Merely loading, cloning, refreshing or changing chat mode must not convert it.
   A legacy Full selection can consequently still lack numpad movement: disclose
   this rather than silently claiming compatibility and complete preset parity.
3. Resolve character Keyboard Size once for both chat and inherited keyboard
   defaults: an explicit saved value wins; absence/reset remains the current
   displayed Full default. A separate new-character Compact 2 default change
   needs owner approval, with existing absent-value characters preserved rather
   than reinterpreted. No profile-name fallback when character settings exist.
4. Materialize only a session-local effective profile, after synchronous stock
   settings load and on stock-setting or `InputProfileHub.Changed` notification;
   then apply that character's camera inversion as today. Never write effective
   inherited chords back into the shared hub. Both assignment UIs must display
   the effective character mode and edit explicit keyboard overrides, including
   both sides of the existing assignment swap. A deliberate reset operates only
   on the selected keyboard scope after confirmation, never shoulders or camera.
5. Separate serialization/clone from legacy migration: the current `Clone` path
   re-enters `FromJson` and compatibility helpers such as `EnsureRetailCompactKeys`.
   Those must not refill explicit unbindings or mutate a materialized new-format
   profile. Specify downgrade behavior before adding a schema: older builds can
   retain a flattened effective map, but cannot safely preserve unknown provenance
   after edits. Do not promise lossless old-editor round trips without a tested
   reconciliation rule; refuse or explicitly require conversion instead.
6. Implement independently evidenced Full mappings first: 7 autorun, 9/3 zoom,
   0 target cycle, 5 view, and contextual * heal/lock, retaining editor/menu
   priority and macro chords. Do not merely bind two actions to *: dispatch one
   action from target context, with tests for no target, valid target and loss of
   target. Keep Compact 1/2 distinct; the official 2012 Compact 2 source above is
   not a complete Compact 1 table. Obtain an authoritative complete mapping or
   owner retail observations before claiming all three presets match retail.

Required RED/GREEN gates: absent/explicit/reset startup; all three mode transitions;
legacy frozen custom and deliberately default-identical maps; explicit empty and
unknown entries; same-context collisions versus legal cross-context sharing;
assignment swaps and scoped reset; save/reload and clone idempotence; two characters
with different modes sharing one hub; camera flags and custom LB/RB preserved;
held-key mode changes/releases; native Num Lock ON/OFF in US/German; logical editor
navigation/text; Tab phrase completion, outside-chat targets and Ctrl+Tab sessions.
Verify real resolved gameplay actions, not just key identity or labels. Retain
strict software/NVIDIA probes and separate Windows/physical owner acceptance.

**Decision before implementation:** approve the legacy-frozen + explicit opt-in
conversion policy and establish the missing Compact 1 table. This follow-up does
not change preset mappings, migrate keyboard overrides or reset owner bindings.
