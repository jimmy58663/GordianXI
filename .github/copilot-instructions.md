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
3.  `src/Gordian.Addons/` (Class Library): Scripting runtime sandbox layer. Manages virtual machines for legacy Lua (NLua) and JavaScript (QuickJS) plugins.
4.  `src/Gordian.App/` (Avalonia UI Executable): Desktop shell interface container. Handles pop-out multi-window management, settings dashboards, and hosts the GPU rendering canvas.

*   🛑 **CRITICAL ENFORCEMENT:** `Gordian.Addons` and `Gordian.Automation` must NEVER reference each other. They are completely decoupled. Automation must remain entirely optional and blockable.

---

## 🔒 Security & Server Kill-Switch Policy
*   `Gordian.Core` exposes an internal write-once configuration flag: `internal set ServerAutomationPolicy AutomationPolicy`.
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

## 🛑 AI Assistant Prohibitions (NEVER DO THIS)
1.  **NO Cross-Reference Corruption:** Do not link `Gordian.Addons` directly to `Gordian.Automation`.
2.  **NO Windows-only code:** Do not inject code blocks that lock execution to the Win32 API.
3.  **NO text string formatting for paths:** Refuse suggestions that use `"\\"`. Use `Path.Combine`.
4.  **NO Brute-force packet array allocation:** Never return `new byte[]` allocations inside packet parsers. Use `Span<byte>` arrays.
