# Dependencies

> Scope: every third-party package GordianXI references or has chosen for later, with its version, the latest stable release, licence, maintenance state and the decision taken. Stable releases only, no previews. Checked against NuGet and GitHub on 2026-10-05 for [#263](https://github.com/jimmy58663/GordianXI/issues/263). Licence notices live in [THIRD_PARTY_NOTICES.md](../../THIRD_PARTY_NOTICES.md).

## Referenced packages

"On `main`" is the version merged today; a "pending" decision has its change on the named branch, waiting for the in-game test and its PR.

| Package | On `main` | Latest stable (date) | Licence | Maintenance | Decision |
|---|---|---|---|---|---|
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter | 12.1.2 | 12.1.3 | MIT | Active | **Update** to 12.1.3 (pending: `chore/263-avalonia-12.1.3`) |
| AvaloniaUI.DiagnosticsSupport | 2.2.3 | 2.2.3 (2026-06-21) | Not stated in the package; Debug builds only (excluded from Release output) | Active | Keep |
| Veldrid, Veldrid.SPIRV | 4.9.0 / 1.0.15 | same (2023-02-03) | MIT | Unmaintained since 2023-02 | **Replace** with NeoVeldrid and NeoVeldrid.SPIRV 1.2.1 (2026-08), MIT, same API ([#262](https://github.com/jimmy58663/GordianXI/issues/262), pending: `rendering/262-neoveldrid`) |
| Veldrid.ImGui | 5.72.0 | 5.72.0 | MIT | Tied to Veldrid | **Remove**: unused (the Tier 3 pass is a stub). See [ImGui](#hud-and-addon-ui-imgui-and-stock-ui-sprites) (pending: `rendering/262-neoveldrid`) |
| Silk.NET.SDL | 2.23.0 | 2.23.0 (2026-01-23) | MIT (SDL2: zlib) | Silk.NET active; SDL2 maintenance-only | **Replace** with ppy.SDL3-CS ([#261](https://github.com/jimmy58663/GordianXI/issues/261)) |
| Silk.NET.OpenAL | 2.23.0 | 2.23.0 (2026-01-23) | MIT | Active | Keep |
| Silk.NET.OpenAL.Soft.Native | 1.23.1 | 1.23.1 (2024-04-23) | LGPL-2.0-or-later (OpenAL Soft, loaded dynamically) | Packaging updated rarely | Keep |
| xunit | 2.9.3 | xunit.v3 4.0.1 (2026-09-12) | Apache-2.0 | v2 superseded by v3 | **Replace** with `xunit.v3.mtp-off` 4.0.1 (pending: `chore/263-test-tooling`). The `mtp-off` variant keeps `dotnet test` on VSTest, so CI and coverlet.collector are unchanged; plain `xunit.v3` needs `dotnet test` in Microsoft Testing Platform mode on the .NET 10 SDK |
| xunit.runner.visualstudio | 3.1.4 | 4.0.0 (2026-08-15) | Apache-2.0 | Active | **Update** with the v3 move (pending: `chore/263-test-tooling`) |
| Microsoft.NET.Test.Sdk | 17.14.1 | 18.10.1 | MIT | Active | **Update** (pending: `chore/263-test-tooling`) |
| coverlet.collector | 6.0.4 | 10.1.0 (2026-09-27) | MIT | Active | **Update** and keep for coverage (`dotnet test --collect:"XPlat Code Coverage"`; not used by CI yet) (pending: `chore/263-test-tooling`) |

NeoVeldrid pulls in further Silk.NET 2.23.0 packages, including the native MoltenVK (macOS), shaderc and SPIRV-Cross libraries (Apache-2.0); see THIRD_PARTY_NOTICES.md.

## Chosen for later

| Package | Latest stable (date) | Licence | For | Decision |
|---|---|---|---|---|
| Hexa.NET.ImGui | 2.2.9 (2025-10-10), wraps Dear ImGui 1.92.2b | MIT | Tier 3 overlays and `gordian.imgui` | **Chosen** (2026-10-05). See below |
| KeraLua | 1.4.10 (2026-09-12) | MIT | Phase 6 Lua 5.4 runtime | Actively released; keep as planned. Who builds and signs the native Lua binaries per OS is still open ([post-mvp.md](../design/post-mvp.md)) |
| ATRAC3 | n/a | n/a | Retail music and sound | Own clean-room decoder ([#38](https://github.com/jimmy58663/GordianXI/issues/38)); no FFmpeg |

## HUD and addon UI: ImGui and stock UI sprites

Decided 2026-10-05 (maintainer), after comparing the .NET ImGui bindings for #263.

**The problem.** Dear ImGui itself is actively released upstream (1.92.x in 2026). The .NET wrappers lag: ImGui.NET's last release is 1.91.6.1 (2025-01-06, 2 commits in the six months to 2026-10), and `NeoVeldrid.ImGui` 1.2.1 is a renderer on top of that same ImGui.NET, so it carries the same lag. ImGui.NET also ships no linux-arm64 native library.

**Options compared** (NuGet and GitHub, 2026-10-05):

| | ImGui.NET | Hexa.NET.ImGui | NeoVeldrid.ImGui | Twizzle.ImGui-Bundle.NET |
|---|---|---|---|---|
| Latest stable | 1.91.6.1 (2025-01-06) | 2.2.9 (2025-10-10) | 1.2.1 (2026-08-09) | 1.91.5.2 (2025-03-07) |
| Dear ImGui version | 1.91.6 | 1.92.2b stable; 1.92.9 on its main branch | 1.91.6 (through ImGui.NET) | 1.91.5 |
| Native libraries | win x86/x64/arm64, linux-x64, osx universal; **no linux-arm64** | win x86/x64/arm64, linux x64/arm64, osx x64/arm64 | through ImGui.NET | win/linux/osx x64 and arm64 |
| Activity | Stalled | 18 commits in six months; essentially one maintainer | 93 commits in six months; mostly one maintainer | No commits since 2025-03 |
| Renderer for NeoVeldrid | none | none (write our own) | yes (`ImGuiRenderer`) | none |
| Extras | none | ImPlot, ImGuizmo, ImNodes, node editor | none | ImPlot, ImGuizmo |

**Decision.**
1. **Dear ImGui through Hexa.NET.ImGui**, pinned to its latest stable release. It tracks upstream most closely and covers every target platform. Its stable 2.2.9 wraps Dear ImGui 1.92.2b (about August 2025, so roughly a year behind upstream) against ImGui.NET's 1.91.6 (end of 2024), and its main branch already carries 1.92.9; a new Hexa stable release is the trigger to bump. If none arrives by the time Tier 3 work starts, weigh the `dear_bindings` fallback then. We write the NeoVeldrid renderer ourselves, clean-room from Dear ImGui's documented backend contract (estimate 600-800 lines plus the 1.92 dynamic-font / `ImTextureData` texture updates). Hexa's API differs from ImGui.NET's (generated, pointer-heavy), so nothing is written against ImGui.NET first. ImGui serves Gordian's Tier 3 overlays (performance overlay, debug windows), `gordian.imgui` for Gordian addons, and the Ashita flavor's `imgui` adapter.
2. **Fallback if Hexa stalls:** generate our own thin binding from Dear ImGui's `dear_bindings` (MIT, from the Dear ImGui project) and build the native library per OS in our CI. Keeping our renderer and `gordian.imgui` independent of Hexa's types where practical keeps that switch small.
3. **Stock UI sprites for Gordian addons:** Gordian addons also get an API onto the Tier 2 stock UI renderer (retail-look windows, frames, fonts and sprites from the UI DATs), so an addon can look like the stock interface. Only the Gordian flavor gets it; Windower and Ashita addons keep their own APIs. Built-in Gordian HUD pieces use the same renderer by default (legacy parity).
4. **Not chosen:** Avalonia rendered into the viewport (possible later for in-game settings panels, not per-frame HUD), RmlUi (no maintained .NET binding), Myra (built around MonoGame/FNA/Stride), NoesisGUI and Ultralight (proprietary), CEF (size), Nuklear/microui (same binding problem, less active upstream). These options were not checked in depth on 2026-10-05.

Design detail: [ui/stock-ui.md](../ui/stock-ui.md#tier-3-overlays-and-stock-ui-suppression) (Tier 3) and [design/post-mvp.md](../design/post-mvp.md) (`gordian.imgui`, `gordian.ui`).

## Staying current

Re-run `dotnet list package --outdated` (and check the "Chosen for later" rows by hand) when starting a phase. Dependabot for NuGet is proposed in #263 and ties into Phase 9 CI.
