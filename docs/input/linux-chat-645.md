# Linux stock text input regression (#645)

## Scope and cause

Stock chat and stock macro/name editors previously left a key unhandled only
when `KeyEventArgs.KeySymbol` contained printable ASCII. X11 shifted-digit events
can have a valid logical `Key` (`D1`, etc.) but an empty `KeySymbol`. Handling the
key suppresses the following platform text event.

`AvaloniaInputMapper.IsTextInputKey` recognizes printable logical keys only when
the symbol is missing. Characters still come exclusively from platform text input;
there is no US character table or synthesized text. Existing nonempty symbols,
editing keys and Ctrl/Alt shortcut handling are preserved. ImGui/lobby routing is
outside this fix. The underlying defect existed before the Linux viewport patch.

## Independent review scope

This candidate is based directly on upstream main, not PR #646. It contains the
input classification, its window call site, three CPU cases and a native input
fixture. No NeoVeldrid fork, native viewport lifecycle changes, graphics probes,
or local package feed are required or included.

The original native evidence and contributor punctuation/macro acceptance were
on the older combined graphics/input stack. They are historical evidence, not
acceptance of this newly separated candidate. Windows and owner in-game acceptance
of the independent candidate remain separate checks.

## Verification

Use .NET 10 and the normal upstream package sources:

```sh
dotnet build GordianXI.slnx
dotnet test GordianXI.slnx --no-build
dotnet build tests/Gordian.InputProbe
python3 tests/Gordian.InputProbe/run-probe.py --input-layout us
python3 tests/Gordian.InputProbe/run-probe.py --input-layout de
```

The CPU suite adds three `TextInputKeyTests`: missing symbols, editing/shortcut
exclusions, and existing Windows/layout symbol behavior. Suite totals change with
upstream; record actual totals for the tested commit rather than copying the old
stack's total.

Native prerequisites: Linux, Xvfb, `setxkbmap`, libX11 and libXtst/XTEST. `XVFB`
may name an extracted Xvfb executable. The runner uses a fresh authenticated X11
display and private HOME/XDG/TMP directories. It does not borrow an owner session,
read owner profiles, connect to a game server or send messages.

The fixture exercises the production `ViewportWindow` input handlers with its GPU
control hidden before the window opens. It tests native XTEST → X11 → Avalonia
key/text routing, not rendering, Vulkan validation or viewport lifecycle. This
separation allows input verification without bringing PR #646 into the source diff.

US and German cases assert case/Caps Lock, shifted digits, editing, opening-slash
de-duplication, stock editor text and macro isolation. German non-ASCII § remains
excluded by existing policy. Ctrl/Alt/IME/AltGr behavior is not comprehensively
certified. The input fixture never dispatches real macro commands.

## Tab and scope boundaries

Tab/Shift+Tab retain existing target bindings while chat is open. This candidate
does not introduce completion, keyboard presets, camera changes or migrations.
PR #649 separately changes Tab semantics and keyboard behavior. Neither PR should
silently contain the other's production fix.

AI assistance: implementation, test isolation and documentation were prepared with
Hermes Agent. Publication and owner deployment require separate approval.
