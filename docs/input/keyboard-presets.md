# Keyboard catalog v1 (#648)

This implements **binding layers**, not a claim of complete retail parity. New
input-profile storage is layered Full, matching the unchanged character page's
absent/reset value. Existing files remain frozen until explicit conversion.
Modern retail's new-character Compact 2 default is **not** silently applied.

## Sources and complete published-control comparison

* **F**: [Square Enix PC manual, printed pp. 2–3](https://support.na.square-enix.com/document/manual/20/FFXI_manual_vc09_AE5.pdf).
* **C2**: [Square Enix March 27 2012 update, New Keyboard Controls, post 298313](https://forum.square-enix.com/ffxi/threads/22099?p=298313#post298313).
* **C1?**: [FFXIclopedia Starters Guide, List of controls](https://ffxiclopedia.fandom.com/wiki/Starters_Guide#List_of_controls).
  This table says “Compact”, not an authoritative complete Compact 1 capture.
  It leaves autorun/target cycling blank and conflicts with C2 on R and zoom.
* [Horizon Controls](https://horizonffxi.wiki/Controls) repeats those conflicts,
  and its movement column headings appear reversed. It does not establish retail defaults.
* Existing repository maintainer retail observations (November 7 key-assignment
  capture, described in `KeyboardAssignments`) corroborate Compact 2's Y/N,
  V/H/F/Z, period/comma, A/D left-stick and Q/E right-stick behavior. They are
  not a fresh retail capture of all three layouts by this implementation.

“Fallback” below is intentionally not a retail assertion. `Num` means the
physical numeric keypad, independently of Num Lock. All entries refer to the
inherited layer; explicit overrides or unbinding can replace them.

| Function | Full source → v1 | Compact 1 evidence → v1 | Compact 2 source → v1 |
|---|---|---|---|
| Forward/back/left/right | F Num 8/2/4/6 → same | C1? W/S/A/D → same | C2 W/S/A/D → same |
| Turn in place, camera follows | No separate Q/E default specified | C1? omits Q/E → unbound | C2 Q/E → existing virtual-right-stick pan; exact simultaneous-arrow priority remains unverified |
| Strafe | F Shift while moving, first person → Shift+Num4/6 existing strafe action | C1? Shift, first person → Shift+A/D fallback | C2 Shift+A/D → existing strafe actions |
| Camera | F arrows → arrows | C1? I/J/K/L → I/J/K/L **plus arrows fallback** | C2 arrows → arrows (no inherited IJKL) |
| Autorun | F/C2 Num7 while moving → Num7 | C1? blank → **unbound**, retail check required | C2 R → R |
| Walk/run | F Num/ → Num/ | C1? R → R, community-only | C2 Z → Z, Num/ retained fallback |
| Heal/lock target | F Num* conditional → existing single LockOnOrHeal action | C1? H conditional → H; legacy T/Num* lock-only fallback remains | Maintainer H heal → H; T/Num* lock-only fallback remains |
| View | C2 Full column Num5 → Num5 | C1? V → V | C2 V → V |
| Confirm | F either Enter → Enter/NumEnter | C1? Y → Y, Enter/Space fallback | C2 Enter → Enter; Y/Space retained fallback |
| Cancel | F Esc → Esc | C1? N → N/Esc fallback | C2 Esc → Esc; N retained fallback |
| Open chat | F Space → Space; Full ordinary text opens natively | C1? Space → explicit Space/Enter opening | C2 Space → explicit Space/Enter opening |
| Select active window | F Num+ → Num+ | C1? F → F plus Num+ fallback | Maintainer F → F plus Num+ fallback |
| Main menu | F Num- → Num- (not decimal) | C1? - → main minus | Existing compact -, M, Num- retained fallback; Ctrl/Alt+- now opens the main menu |
| Cycle target | F Tab/Num0 → Tab/Num0; Shift+Tab previous retained | C1? blank → Tab/Shift+Tab fallback | C2 omits plain target cycle → Tab/Shift+Tab fallback |
| Zoom in/out | F/C2 PageUp or Num9 / PageDown or Num3 → same | C1? comma/period conflicts with C2; **punctuation left unbound**, PageUp/Down fallback | C2 period/comma → same plus PageUp/Down fallback |
| Center/fixed view | F Home → existing ResetCamera action on Home, not claimed exact fixed-view parity | C1? Home; v1 retains old End fallback (discrepancy) | C2 unspecified; End fallback |
| Hide/show windows | F ScrollLock, C1? ScrollLock, C2 ] → transient stock-UI hide/show; preserves other visibility owners |
| Screenshot | F PrtScr, C1? PrtScr, C2 [ → existing viewport screenshot handler |
| Logout | F PauseBreak → existing logout toggle; compact Pause remains unbound; controller unchanged |
| Party slots | F/C1?/C2 F1–F6 → existing self + party slots |
| F7 self | C1?/C2 F7 → self (also documented in C2’s Full column) |
| F8 nearest NPC | F/C1?/C2 → nearest targetable non-player (includes monsters), not Tab cycling |
| F9 nearest PC | F/C1?/C2 → nearest other targetable player |
| F10 first party member | F/C1?/C2 → self, the first entry in the client’s own-party display |
| F11/F12 first allied party members | F/C1?/C2 → first member of each other party in party-number/member-number display order; **not leader flags** |
| Ctrl/Alt + 1–0 macros | F/C1?/C2 → existing macro actions, editors retain priority |
| Ctrl/Alt + P/S/L/T | C2 and C1? Party/Say/Linkshell/Tell → implemented draft-only actions; Tell opens `/tell ` for recipient entry |
| Ctrl/Alt + R | C2/C1? → unsent `/tell <received sender> ` draft; outgoing tells never replace reply recipient; no sender opens `/tell ` |
| Ctrl/Alt + A | C2/C1? → existing attack/disengage service toggle |
| Ctrl/Alt + B | C2 pet commands → existing pet-command list (shared shortcut fallback in C1) |
| Ctrl/Alt + C | C2/C1? → existing check service |
| Ctrl/Alt + D | C1? ranged attack, omitted C2 → **unbound** |
| Ctrl/Alt + E | C2/C1? → existing Equipment window |
| Ctrl/Alt + F | C1? select window, omitted C2 → **unbound** |
| Ctrl/Alt + G | C1? help, omitted C2 → **unbound** |
| Ctrl/Alt + H | C2 heal → existing heal service (shared shortcut fallback in C1) |
| Ctrl/Alt + I/J/M/W | C2 → existing Items/job-ability/spell/weapon-skill lists; C1 uses these shared shortcuts as fallback |
| Ctrl/Alt + N/Y/Z | C1? cancel/confirm/walk, omitted C2 → **unbound** |
| Ctrl/Alt + V | C2/C1? → existing view action |
| Ctrl/Alt + [ / ] | C1? screenshot/windows, C2 plain [ / ] → **unbound** |
| Mouse movement | F/C1? left held vs C2 wheel held → **unchanged**, keyboard selection does not implement mouse-scheme parity |

The C1? Full zoom-out row says Num6; F and C2 say Num3. Num6 is movement,
so v1 follows the official Num3 source. C1? punctuation zoom directions and R
must not be treated as verified C2 defaults. The runtime preserves existing
camera action implementations, including the approved controller Normal signs;
these mappings alone do not certify retail rotation rates, Q/E turning behavior,
arrow priority, view-cycle free-camera behavior or autorun's moving-only gate.

## Persistence and editing contract

`keyboardLayout` has catalog `revision`, numeric `mode` (dashboard preview), and
`overrides` keyed by action name. Missing means inherit; `[]` means unbound;
nonempty replaces that action's keyboard list. Unknown actions/chords and JSON
extension fields survive current-version clone/save/reload. Unknown revisions
are frozen; this build refuses editing/reset/conversion, rather than applying a
new interpretation. Do not edit these files with older builds: those builds do
not understand provenance and cannot promise lossless downgrade round trips.
The retained `bindings` map is **not** a modern resolved keyboard export.

Character `KeyboardSize` always wins over shared preview mode, including absent
or reset (Full). Sessions and both assignment displays resolve their own copies.
The dashboard explicitly says when no character is selected: selecting then
changes its preview, not every character's persisted KeyboardSize. Keyboard
assignment overrides are shared, as before. Camera settings remain character
local. Effective inherited chords never write back to the shared definition.

Explicit chords suppress inherited collisions only within the same menu/game
context. Explicit/explicit conflicts retain both lists with enum-order keyboard
resolution, not dictionary-order resolution. Menu navigation retains its context
priority. Assigning through the in-game UI records both sides of a swap as
explicit overrides. Full type-to-chat yields to an explicit gameplay chord.
A layout/profile refresh blocks held keys until release to avoid new actions
from a key pressed under the previous map. Open drafts/completion are not reset.

Loading a legacy profile preserves every keyboard chord and absence (including
custom values identical to old defaults); no name-based inference. Existing
controller compatibility migrations remain unchanged, including the previously
disclosed old-default-identical D-pad caveat. Clone does not run migrations.
The dashboard offers **Convert legacy (keep every key)**, with Confirm/Cancel.
Conversion archives original JSON and marks every known action explicit,
including missing/unbound actions. Thus conversion itself changes no keyboard
behavior. A separate **Restore selected defaults** confirmation releases known
overrides into inheritance, across the shared keyboard scope; unknown future
entries remain explicit. This is intentionally not automatic custom detection.
Controller, mouse, sensitivity, camera and the original archive remain intact.
The in-game Default action uses the selected layout and the same reset policy;
legacy users are directed to review conversion in the dashboard first.

## Verification and limits

`KeyboardLayoutTests`, `KeyboardPresetUiTests`, and `StockUiKeyAssignTests` cover
legacy freeze, session mode resolution, inheritance/explicit/unbound, collision
contexts, both swap sides, conversion/cancel/restore, unknown entries, persisted
settings/profile reload, two-character isolation, drafts and held-key changes.
The native InputProbe uses real dashboard preset commands, then XTEST through
the production viewport, all three modes and both queried Num Lock states.
Existing US/German text/completion/keypad and strict graphics probes remain.
No owner settings, profiles, game installs, login, GUI or physical devices are
used. Windows, real retail assignment tables and owner gameplay remain pending.

Required retail evidence to close the ambiguity: a clean **Compact 1 Movement
and Commands assignment table**, specifically autorun, R/Z, comma/period, Q/E,
target cycle and Home/End; then the same table for Full and Compact 2 for omitted
keys. Separately validate Compact 2 Q/E versus arrows and mouse-wheel movement.
Unimplemented shortcut rows above are engineering gaps, not requests to guess
keys or a claim that #648 is complete.

## Verified shortcut follow-up

The official March 27 table explicitly places F7–F12 and Ctrl/Alt shortcuts in
both the new-controls and Full-keyboard columns. F11/F12 say **first character**,
not party leader; leader flags must not override displayed slot order. Compact1
still has community-only provenance. Its unknown movement/zoom rows above stay
unbound, not inferred from Compact2. Community-only Ctrl/Alt D/G/F/N/Y/Z and
modifier-bracket rows remain unimplemented; they are not claimed official defaults.

New gameplay actions run through the existing action service/menu constructors,
not arbitrary command strings or new packet callbacks. Chat/completion/editor
routing retains priority; open menu contexts gate gameplay actions. Attack/check also work through the engaged command menu; other menu shortcuts
remain gated while a menu is open (including that command menu). Exact retail
menu-switching policy remains unverified. F8/F9 use the
existing targetability and 50-yalm range guards and nearest distance, not Tab
cycling; exact retail camera/frustum eligibility is not certified.

Plain F7–F12 in layered profiles are reserved for the character, including explicit
unbinding. This fixes the window’s old F9 weather/F10 time/F11 fullscreen interception.
Legacy/no-session shortcuts and modified diagnostic/emergency shortcuts remain.
Full-screen selection remains available in the window controls. German AltGr
brackets use their native symbol only for bound display shortcuts after editor/chat
routing; physical-key releases release the translated key even if modifiers changed.
No keyboard-global hotkeys are installed. Hide/show only affects stock UI, not an
addon’s own custom windows. Its separate transient owner preserves user/addon and
persistent per-window hide choices.

`KeyboardShortcutTests` exercises all three materialized presets, both modifier
families, real locomotion/menu routing, synthetic outbound attack/check/heal/logout
requests, held-edge behavior, unsent reply, visibility restoration and screenshot
handler routing. Native InputProbe adds real F7–F12 targets, screenshot-handler routing, Ctrl+J menus, Alt+R
reply, and window visibility (including German AltGr) to the existing preset UI,
Num Lock and completion tests. Native action-service requests do not contact a
realm; the packet tests use an in-memory send sink. Full Windows/owner/retail
acceptance and complete Compact1 assignments are still pending.
