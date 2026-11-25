# Independent PR #649 review and verification

This branch isolates the follow-up changes originally between
`d2943ca1791beaec1545bfcfa4c453c36101ff0c` and
`f8121fc7462a3b5077a6397685d01ad854bd39dd` onto upstream main
`e9f047c85c669db0b8a1ec2b24df62bedc2613a4`.
The later PR merge `2dd0951abded064241f7e8507c1626b2457279f9` only manually
resolved ROADMAP.md; its upstream collision work is already in the new base.

## Deliberate exclusions and real dependency

No PR #646 graphics fork, package feed, viewport fixes or graphics probes are
included. No PR #647 `IsTextInputKey` helper or its window call site is copied.
The physical keypad overload is this PR's own mapper change. The production
window retains main's `IsTypedSymbol` classification.

The camera, controller, keyboard catalog and auto-translate code can compile and
be unit-tested independently. **Complete native text-entry acceptance still needs
#647:** without it X11 empty-symbol shifted digits are suppressed. Full-size
open-on-typing also uses that classification, so this is a genuine behavioral
prerequisite for the previously advertised complete keyboard behavior, not a
reason to duplicate #647 here. A native US run of the isolated candidate passed
the preset UI/movement checks and then failed shifted digits (`actual=aAAa`).
The regression assertion is intentionally retained, not skipped to call this green.

Clean integration options:

1. Merge the independently scoped #647 first, then rebase this independently
   reviewed branch on updated main. The resulting diff still contains only #649.
2. Review this branch on current main with the known text-entry limitation, without
   presenting the original combined native acceptance as independent acceptance.
3. If parallel all-green acceptance is required, extract the text-classification
   prerequisite into an explicitly shared minimal PR, not both branches.

## Tests and native boundaries

```sh
dotnet build GordianXI.slnx
dotnet test GordianXI.slnx --no-build
dotnet build tests/Gordian.InputProbe
dotnet build tests/Gordian.ControllerProbe
python3 tests/Gordian.InputProbe/run-probe.py --input-layout us
python3 tests/Gordian.InputProbe/run-probe.py --input-layout de
```

Use an absolute private TMPDIR for unit tests. Native input needs Linux, Xvfb,
setxkbmap, libX11 and libXtst; `XVFB` may point to an extracted executable.
The runner creates a private authenticated X11 display and HOME/XDG/TMP roots.
It exercises production window input with the GPU viewport hidden before opening,
so keyboard tests do not import the unrelated graphics stack. It requires a final
completion marker as well as process exit zero. No game connection, owner profile,
physical controller or owner graphical session is used.

The optional installed-art completion renderer remains a separate test; without
explicit `GORDIAN_COMPLETION_GAME_ROOT` it does not read game assets. Its historical
GPU results in the other guides apply only to the original stacked revision.
Windows, physical hardware, rendering and owner in-game acceptance of this
independent candidate are not inferred from unit or keyboard-fixture results.

An initial full-suite run had one failure in the existing shared-hub autosave test
`ControlsInputViewModelTests.PropertyChanges_AutoSavesAndPersistsAcrossInstances`;
its isolated unchanged retry passed. Retain that receipt and report full-suite
reruns separately rather than claiming the initial run passed.

AI-assisted separation and verification: Hermes Agent. No publication or owner
settings migration is performed by these build/test instructions.
