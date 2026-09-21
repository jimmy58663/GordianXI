# AGENTS.md - AI Coding Instructions for GordianXI

This file provides critical system-level instructions, architectural boundaries, and coding standards for AI assistants (Claude, Copilot, Cursor, Gemini, Codex) working on the GordianXI codebase. Adhere to these constraints strictly.

---

## 🎯 Project Overview & Context
GordianXI is a 64-bit cross-platform custom client for Final Fantasy XI built from scratch using modern **C# (.NET 10)** and **Avalonia UI**.
*   **Goal:** Replace the legacy 2002 32-bit executable entirely to remove the 4 GB memory ceiling, integrate native single-process multi-boxing, and provide zero-drop macro automation.
*   **Legal Boundary:** This is a strict **Clean-Room Reverse Engineering** project. NEVER generate code that copies or patches official `ffxi.exe` or `pol.exe` memory strings directly, or uses leaked corporate assets. Interact exclusively via the public network packet protocols and resource `.dat` file specs mapped by open-source server emulators (LandSandBoat).

---

## 🏗️ Repository Architecture & Dependency Rules
The solution uses a single-repo Monorepo structure with four strictly decoupled project layers. AI tools MUST respect these boundary rules:

```text
Dependency Tree:
Gordian.App ──► Gordian.Addons ──► Gordian.Core
Gordian.App ──► Gordian.Automation ──► Gordian.Core
```

1.  `src/Gordian.Core/` (Class Library): High-performance networking core. Manages TcpClient streams, Blowfish key handshakes, binary array slicing, and acts as a passive, neutral shared state data bus. Has ZERO awareness of automation or addons.
2.  `src/Gordian.Automation/` (Class Library): The Gambit and group-coordination engine. Pulls state updates from Core and handles positional tracking. Is an OPTIONAL, separate passenger module.
3.  `src/Gordian.Addons/` (Class Library): Sandboxed Lua scripting runtime layer. Hosts a single NLua virtual machine per addon, executing all scripts inside an allowlist-only `_ENV` exposing exclusively the curated addon API surface — CLR reflection (`luanet`), raw filesystem APIs (`io.*`/`os.*`), process, and arbitrary OS access are never reachable from script code. Addons instead get a scoped storage API confined to a per-addon subdirectory under `GordianStorage.AddonsDirectory` for config and logs.
4.  `src/Gordian.App/` (Avalonia UI Executable): Desktop shell interface container. Handles pop-out multi-window management, settings dashboards, and hosts the GPU rendering canvas.

*   🛑 **CRITICAL ENFORCEMENT:** `Gordian.Addons` and `Gordian.Automation` must NEVER reference each other. They are completely decoupled. Automation must remain entirely optional and blockable.

---

## 🔒 Security & Server Kill-Switch Policy
*   `Gordian.Core` exposes an internal write-once configuration flag: `internal set FeatureRestrictions FeatureRestrictions`.
*   User-facing script runtimes (`Gordian.Addons`) and graphical views (`Gordian.App`) physically lack compile-time rights to modify this flag.
*   If a private server transmits an automation restriction packet, the authenticated `PacketParser` inside `Gordian.Core` flips this flag, immediately forcing the execution loop inside `Gordian.Automation` to short-circuit and sleep.

---

## 💻 Tech Stack & Memory Constraints
*   **Target Framework:** .NET 10 (`net10.0`). Leverage C# 14 language features natively.
*   **Memory Efficiency:** Avoid allocations in the performance-critical packet pipeline. Use `Span<byte>`, `ReadOnlySpan<byte>`, and `ref struct` schemas for packet manipulation to prevent Garbage Collection overhead.
*   **Server GC:** The project uses concurrent Server GC profiles to manage heavy 64-bit HD texture streaming without causing frame stutters.
*   **Cross-Platform Agnosticism:** The codebase must natively support Windows, Linux, and macOS. 
    *   NEVER hardcode directory paths using string slashes. Always use `Path.Combine()`.
    *   Enforce absolute string casing for file system lookups to prevent crashes on case-sensitive Unix systems.
    *   Avoid Windows-only Win32 P/Invoke APIs (like `kernel32.dll`). Use Avalonia’s native abstractions.

---

## 🎨 UI & Addon Standards
*   **Desktop App Shell:** Handled strictly via Avalonia UI using the MVVM design pattern. Supporting pop-out windows for character settings is an architectural core layout requirement.
*   **In-game HUD Overlays:** Handled strictly via **ImGui.NET** inside the 3D viewport thread.
*   **Visual Aesthetic:** Maintain a dark, sleek, translucent profile with rounded corners (`WindowRounding = 6.0f`) to emulate modern custom automation dashboards.

---

## 🚀 High-Frequency CLI Commands
When executing tasks or testing code changes, run these native .NET CLI commands from the project root:
*   **Restore Dependencies:** `dotnet restore`
*   **Compile Workspace:** `dotnet build`
*   **Run Desktop Client App:** `dotnet run --project src/Gordian.App/Gordian.App.csproj`
*   **Run Unit Tests:** `dotnet test`

---

## 🔄 Bootloader Handoff & Interception Architecture
GordianXI intercepts game sessions without permanent modifications to user game folders:
1. **Registry Detection:** `GameDirectoryDetector.cs` queries 32-bit `InstallFolder\0001` across `PlayOnlineUS`, `PlayOnlineEU`, and `PlayOnline`.
2. **Ephemeral Proxy Swap:** `ProxyStager.cs` renames genuine `FFXiMain.dll` to `FFXiMain.dll.orig`, deploys the COM proxy `FFXiMain.dll`, spawns the bootloader (`xiloader` or `pol`), awaits handoff, and restores the original DLL.
3. **Startup Self-Healing:** On app startup, `ProxyStager.SelfHealStartup()` immediately checks for and restores any orphaned `FFXiMain.dll.orig`.
4. **IPC Channel:** The proxy passes session credentials to GordianXI via local Named Pipe `\\.\pipe\GordianXI_Handoff`.

---

## 📋 Context Resumption & Tracking
*   **Always check `ROADMAP.md`** at the solution root at the start of any new session or feature implementation to verify the current phase, completed tasks, and active priorities.
*   **Always run `dotnet test`** to confirm all test suites pass before starting new feature development.

---


## 🛡️ Workspace Access & Directory Permissions
*   **Primary Development Workspace (`GordianXI`):**
    *   Full read and write permissions.
    *   Automated file creation, modifications, deletions, and standard build/test tasks (`dotnet build`, `dotnet test`) are permitted.
*   **Reference Workspaces (`LSBserver`, `xiloader`, `xi-model-viewer`):**
    *   **Strict Read-Only Enforcement:** Permitted operations are limited to reading, searching, and schema inspection (`view_file`, `grep_search`, `find_by_name`, read-only `git` status/log).
    *   **NEVER** create, modify, or delete files, or run mutating commands in reference repositories.

---

## 🛑 AI Assistant Prohibitions (NEVER DO THIS)
1.  **NO Cross-Reference Corruption:** Do not link `Gordian.Addons` directly to `Gordian.Automation`.
2.  **NO Windows-only code in class libraries:** Keep class libraries cross-platform agnostic. Guard OS-specific calls behind `OperatingSystem.IsWindows()`.
3.  **NO text string formatting for paths:** Refuse suggestions that use `"\\"`. Use `Path.Combine`.
4.  **NO Brute-force packet array allocation:** Never return `new byte[]` allocations inside packet parsers. Use `Span<byte>` arrays.
5.  **NO Permanent game modifications:** Never overwrite game directory files without the ephemeral backup/restore pattern managed by `ProxyStager`.
6.  **NO Modifying Reference Workspaces:** Never make edits, write files, or execute mutating commands in reference repositories (`LSBserver`, `xiloader`, `xi-model-viewer`).
7.  **NO GPL Code Copying:** Never copy or translate verbatim C++, Rust, or JavaScript code from reference repositories (`LSBserver`, `xiloader`, `xi-model-viewer`). GordianXI is an independent, clean-room C# implementation under the MIT license. Wire formats and functional binary schemas (e.g., packet opcodes, DAT chunk formats) may be referenced for interoperability, but must be authored from scratch.
8.  **NO Undocumented Protocol Additions:** Whenever adding packet definitions, opcode mappings, crypto/compression routines, or DAT chunk parsers derived from community research, always add an XML doc-comment citing the reference source.

---

## 📜 Licensing, Clean-Room & Protocol Attribution Standards
* **Independent Clean-Room Implementation:** GordianXI is licensed under the **MIT License**. Reference projects (`LSBserver`, `xiloader`, `xi-model-viewer`) are licensed under **GPLv3**.
* **Protocol Interoperability Citation:** When implementing packet schemas, opcode mappings, cryptographic steps, bootloader handoff structures, or DAT chunk decoders based on community research:
  * Always document the reference source in the class or method XML doc-comment (e.g., `/// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server)` or `/// DAT chunk format referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer)`).
  * Never copy or translate source blocks verbatim; implement natively using modern C# (.NET 10) idioms (`Span<byte>`, `ref struct`, BCL cryptography).
* **Dependency Notice Maintenance:** If introducing any new third-party NuGet package or external library, update `THIRD_PARTY_NOTICES.md` to preserve its copyright notice and license.
