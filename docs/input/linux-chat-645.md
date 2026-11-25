# Linux stock text input regression (#645)

## Scope and cause

The stock chat and stock macro/name editor left a key unhandled only when
`KeyEventArgs.KeySymbol` contained printable ASCII. On the pinned Avalonia
12.1.5 X11 backend, real Shift+digit events can have a valid logical `Key` (`D1`
etc.) but an empty `KeySymbol`. Gordian consequently marked these presses handled.
Avalonia's [X11 dispatch](https://github.com/AvaloniaUI/Avalonia/blob/5825bf8c66fba0c11e67c980d8ece6444576bffa/src/Avalonia.X11/X11Window.cs)
only delivers the subsequent `RawTextInputEventArgs` when the key was **not**
handled. The resulting text never reached the stock field.

A real XTEST -> X11 -> Avalonia -> production `ViewportWindow` -> stock chat
regression reproduced ordinary `aA` working and all `!@#$%^&*()` disappearing.
Restoring the exact previous window source reproduces the failure with the final
fixture; restoring the repair passes. The faulty routing also exists in upstream
`714f1bc53249412d2dcd312d3d5788755fe529f9` and originated with the stock chat work
(PR #54), not the Linux viewport patch. Runtime reproduction still uses the
working #644 viewport stack; this is not a claim of unmodified upstream runtime
acceptance.

`AvaloniaInputMapper.IsTextInputKey` now recognizes printable logical keys as a
fallback **only when the symbol is missing**. Characters still come exclusively
from platform text input; there is no US symbol table, synthesized character,
keyboard hook or global interception in production. Existing nonempty symbols,
editing keys and Ctrl/Alt shortcut handling are preserved. The stock editor uses
the same classification. ImGui overlay/lobby routing is outside this fix.

## Tab is not an autocomplete regression

Tab/Shift+Tab explicitly feed the target-right/target-left bindings while chat is
open. This is documented in [stock UI](../ui/stock-ui.md#chat-and-log-windows-chunk-5),
[input](console-and-input.md#target-cycling), and PR #54. The native fixture
checks those bindings through the production locomotion tick without modifying
the chat text. There is no stock chat autocomplete UI or completion provider in
this revision. Auto-translate packet builders/decoders are not such a provider.
No target defaults or saved keybindings were changed. Completion/auto-translation
would need a separately agreed feature design, including its interaction with Tab.

## Verification

Build the pinned dependency first on a fresh checkout:

```sh
python3 third_party/NeoVeldrid/build.py
dotnet build GordianXI.slnx
dotnet test GordianXI.slnx --no-build
dotnet build tests/Gordian.InputProbe
```

The CPU suite includes three new `TextInputKeyTests` (missing symbols,
editing/shortcut exclusions, existing Windows/layout symbol behavior).
The full Linux suite passes **4845 tests: 4842 existing plus 3 new**, none failed
or skipped. Full rebuild retains the existing warnings; no Windows hardware
execution is implied by platform-neutral CPU tests.

Native prerequisites are those of the [viewport probe](../rendering/linux-viewport-644.md),
plus `libXtst`/XTEST and `setxkbmap`. Run on Linux:

```sh
# XVFB may point to an extracted executable. Select a Vulkan ICD with VK_DRIVER_FILES.
# For external layers, also set VK_LAYER_PATH.
VK_INSTANCE_LAYERS=VK_LAYER_KHRONOS_validation python3 tests/Gordian.ViewportProbe/run-probe.py --input --input-layout us --strict-validation
VK_INSTANCE_LAYERS=VK_LAYER_KHRONOS_validation python3 tests/Gordian.ViewportProbe/run-probe.py --input --input-layout de --strict-validation
```

The runner creates a fresh authenticated Xvfb and private HOME/XDG/TMP directories,
then sets the layout **only on that display**. Input runs use Xvfb `-noreset` so
closing `setxkbmap` does not reset the layout before Avalonia connects. The fixture
opens a real production viewport/native child and a disconnected synthetic session;
it never connects/logs in, loads game resources, reads an owner profile or starts
the game App. It feeds XTEST key events, not hand-created Avalonia text events.
Its bounded output concerns synthetic input only. Native retirement is drained
even on assertion failure.

The US and German cases each exercise Compact and Full presets:

- Enter/Full Space opens chat without inserting the opening Space.
- Lower/upper case and Caps Lock, then real Shift+1..0, entered exactly once.
- US `!@#$%^&*()`; German printable ASCII `!"$%&/()=` (the existing ASCII-only
  stock font policy rejects German Shift+3 `§`; this fix does not add Unicode).
- Tab/Shift+Tab retain target bindings with chat open, without changing its text.
- Ctrl+1 starts exactly one synthetic titled, empty macro with chat closed and
  starts none/inserts no text with chat open. No macro commands are dispatched.
- Backspace/Escape editing; US opening slash de-duplication.
- Shift+1 reaches the stock macro/name editor; Escape closes it without commit.

All four strict native configurations pass: software Vulkan and isolated RTX 3090,
each US and German. The original production viewport probe also passes strict
validation on both drivers. These are isolated X11 tests, **not owner XWayland,
Windows, live-game or IME/AltGr acceptance**. The existing Ctrl/Alt policy and
ASCII-only text limits remain. German chat-open key rebinding is not redesigned.
The separately recorded extended NVIDIA warm-reopen retirement crash in #644
remains unresolved; this input fix does not change initialization or teardown.

## Owner acceptance

Use a separate checkout based on the supplied input commit, retaining the working
#644 checkout. Build the pinned dependency and solution, then launch manually with
`--profiles-dir /absolute/path/to/existing/profiles` (not `--profile`, which logs in
automatically). In chat, type a synthetic line such as `aA !@#$%^&*()` without
sending it, test Backspace and Escape, and confirm Ctrl+1 remains a gameplay macro
only when chat is closed. Tab should still cycle targets, not complete text.
Test the owner's normal layout/compositor; keep actual chat and account logs private.
This branch is stacked on the viewport work and must be rebased or clearly labeled
before any later input PR. Publication requires separate owner approval.

AI assistance: implementation, tests and this guide were prepared with Hermes Agent.
