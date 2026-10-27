# Contributing to GordianXI

Thanks for helping. This page covers how to report problems, how to send a change, what a change must meet, and the test
policy. How the code is organized is in [docs/architecture.md](docs/architecture.md), and every subsystem has its own doc
under [docs/](docs/README.md).

## Reporting bugs and asking for features

- Open a [GitHub issue](https://github.com/jimmy58663/GordianXI/issues). Include the GordianXI version or commit, your OS,
  the server (retail or a LandSandBoat build), what you did, what you expected, and what happened.
- Attach the log for the session: `logs/gordian_system_<date>.log` next to the app (or under `%APPDATA%\GordianXI` when it
  is installed in Program Files). Remove anything private first.
- **Security problems are not reported in public issues.** Follow [SECURITY.md](SECURITY.md).

## Sending a change

1. **Start from an issue.** Open work is in [Issues](https://github.com/jimmy58663/GordianXI/issues), grouped by milestone and
   `area:*` label; `next-up` marks what comes next. For anything not already an issue, open one first, so the approach
   can be agreed before you write the code. Current status is in [ROADMAP.md](ROADMAP.md).
2. **Fork and branch.** One short-lived branch per issue, named `<area>/<issue>-<slug>` (for example
   `network/1-register-0x073`), or `docs/<slug>` / `chore/<slug>` / `ci/<slug>` for work without an issue. Nothing is
   committed to `main` directly; every change, docs included, goes through a pull request.
3. **Build and test locally** with the .NET 10 SDK:
   ```bash
   dotnet build
   dotnet test
   ```
   Some tests read a local Final Fantasy XI install and are skipped when none is found.
4. **Open a pull request** against `main`. Say `Closes #N` for each issue it finishes, and describe what you changed and
   how you checked it.
5. **Review and merge.** CI builds and tests every PR on Windows and Ubuntu, and CodeQL scans it. A code change is merged
   only after CI passes, a maintainer has reviewed it, and the maintainer has tested it in game. PRs are squash-merged.

## What a change must meet

- **Clean room.** GordianXI is an independent, MIT-licensed implementation. Never copy or translate code from the official
  client (`ffxi.exe`, `pol.exe`), leaked material, or GPL / AGPL projects (LandSandBoat, xiloader, xi-model-viewer,
  XiEvents, XiPackets and others). Wire formats and file layouts may be used for interoperability, but the code must be
  written from scratch. See [LEGAL.md](LEGAL.md).
- **Cite your sources.** Packet layouts, opcodes, crypto steps, event VM opcodes and DAT formats taken from community research
  carry an XML doc comment naming the reference (for example
  `/// Packet structure referenced from XiPackets (https://github.com/atom0s/XiPackets)`).
- **Respect the layers.** `Gordian.Core` never references the engine; `Gordian.Addons` and `Gordian.Automation` reference
  Core only and never each other; GPU, audio, input-device and UI packages belong in `Gordian.App` only. The rules and
  where new code goes are in [docs/architecture.md](docs/architecture.md); `LayeringTests` enforce them.
- **Leave the server kill switch alone.** Nothing may clear or overwrite `FeatureRestrictions` or add another way to write
  it (see [AGENTS.md](AGENTS.md), "Security & Server Kill-Switch Policy").
- **Cross-platform.** Build paths with `Path.Combine`, match file-name case exactly, and guard any OS-specific call with
  `OperatingSystem.IsWindows()` (or the other platforms). No Win32 P/Invoke in the class libraries.
- **No allocations in the packet pipeline.** Parse and build packets over `Span<byte>` / `ReadOnlySpan<byte>`.
- **Match the surrounding code**: its naming, comment style and idioms. Shared conventions (axes, headings, units, clocks)
  are in [docs/reference/conventions.md](docs/reference/conventions.md).
- **Keep the docs current in the same PR.** A change to how a subsystem works updates its doc under `docs/`. A change that
  implements a packet, event opcode, DAT file, flag or calibrated constant updates its row in the reference registries
  (`docs/network/packets.md`, `docs/events/opcodes.md`, `docs/world/dat-files.md`, `docs/reference/`).
- **New dependencies** need a FLOSS licence compatible with MIT and an entry in
  [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). The choices so far are in
  [docs/reference/dependencies.md](docs/reference/dependencies.md).
- **No compiler warnings** in the code you add.

## Test policy

- **New functionality comes with automated tests** in the same pull request, covering its normal behaviour and its edge and
  error cases. A pull request that adds behaviour without tests is not merged unless the PR explains why it cannot be
  tested automatically (for example, it only draws to the GPU), and how it was checked instead.
- **Every bug fix comes with a regression test** that fails without the fix, where the bug can be reproduced in a test.
- **`dotnet test` must pass** before you open the pull request, and CI must pass before it is merged.
- **Tests follow the layers.** Each assembly has its own project under `tests/`. A test that touches any engine type goes in
  `tests/Gordian.Engine.Tests`, even when its subject is a Core type; `tests/Gordian.Core.Tests` never references the
  engine. Test-data builders and helpers that both projects need go in `tests/Gordian.Core.Tests/Shared`.
- **Tests use xUnit v3** and must not depend on a network server or on the tester's own settings. Use loopback stand-ins
  for servers and scratch folders for files. Tests that need the retail game data skip themselves when it is not
  installed.

## Licence of contributions

By sending a pull request you agree that your contribution is licensed under the project's [MIT License](LICENSE), and that
you have the right to submit it. After your first merged pull request you are welcome to add yourself to
[CONTRIBUTORS.md](CONTRIBUTORS.md).

## AI-assisted contributions

AI coding tools are welcome; much of this project is written with them. The project's instructions for them are in
[AGENTS.md](AGENTS.md). You are responsible for what you submit: review it, test it, and make sure it meets the rules above.
