# AGENTS.md - AI Coding Instructions for GordianXI

This file provides critical system-level instructions, architectural boundaries, and coding standards for AI assistants (Claude, Copilot, Cursor, Gemini, Codex) working on the GordianXI codebase. Adhere to these constraints strictly.

---

## 🎯 Project Overview & Context
GordianXI is a 64-bit cross-platform custom client for Final Fantasy XI built from scratch using modern **C# (.NET 10)** and **Avalonia UI**.
*   **Goal:** Replace the legacy 2002 32-bit executable entirely to remove the 4 GB memory ceiling, integrate native single-process multi-boxing, and provide zero-drop macro automation.
*   **Legal Boundary:** This is a strict **Clean-Room Reverse Engineering** project. NEVER generate code that copies or patches official `ffxi.exe` or `pol.exe` memory strings directly, or uses leaked corporate assets. Interact exclusively via the public network packet protocols and resource `.dat` file specs mapped by open-source server emulators (LandSandBoat).

---

## 🏗️ Repository Architecture
The solution uses a single-repo Monorepo structure with three distinct .NET 10 project layers:
1.  `src/Gordian.Core/` (Class Library): High-performance networking core. Manages TcpClient streams, binary serialization, headless character array loops, and automation data states.
2.  `src/Gordian.App/` (Avalonia UI Executable): Desktop shell interface container. Handles pop-out multi-window management and hosts the GPU rendering canvas.
3.  `src/Gordian.Addons/` (Class Library): Scripting runtime sandbox layer. Manages virtual machines for legacy Lua (NLua) and JavaScript (QuickJS) plugins.

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
*   **Desktop App Shell:** Handled strictly via Avalonia UI using the MVVM design pattern.
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
1.  **NO Windows-only code:** Do not inject code blocks that lock execution to the Win32 API.
2.  **NO text string formatting for paths:** Refuse suggestions that use `"\\"`. Use `Path.Combine`.
3.  **NO old .NET versions:** Do not use obsolete .NET Framework or .NET Core 3.1 structure patterns. Target .NET 10 constructs.
4.  **NO Brute-force packet array allocation:** Never return `new byte[]` allocations inside packet parsers. Use `Span<byte>` arrays.
