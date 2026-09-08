# GordianXI

[![Target Framework](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Avalonia-8A2BE2)](https://avaloniatechnologies.com/)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

**GordianXI** is a next-generation, open-source custom client architecture for Final Fantasy XI built from scratch in modern **C# (.NET 10)**. 

By replacing the legacy 2002 executable entirely, GordianXI cuts through decades of technical debt, shifts multi-boxing from a brittle hacking exercise into a native design feature, and permanently shatters the structural memory constraints of the original client.

> ⚠️ **Disclaimer & Legal Boundary:** GordianXI is an independent, clean-room software preservation project. It does **not** contain, host, or distribute any copyrighted assets, 3D models, textures, or proprietary code belonging to Square Enix. Users must possess a legally installed, official retail copy of Final Fantasy XI. GordianXI functions purely as an alternative execution runtime that reads local resource data files directly from the user's hard drive.

---

## ✨ Core Pillars & Architectural Features

*   **⚡ Permanent 64-Bit Memory Scaling:** Compiling natively as an `x64` application completely eliminates the legacy 32-bit 4 GB RAM ceiling. GordianXI can stream extensive community high-definition texture packages and heavy zone geometries directly into memory simultaneously without encountering allocation crashes.
*   **👥 Native Single-Process Multi-Boxing:** Bypasses all unstable Windows IPC methods (`//send`) entirely. All characters exist as native entities in a single, unified memory block. While your active viewport gets full 3D rendering rendering, background characters are transitioned into a highly efficient **"headless" state**—processing only their packet streams, positioning, and script logic to minimize hardware overhead.
*   **🤖 Integrated Group Coordinator:** Eliminates dropped macros and action synchronization lag by embedding core coordination parameters directly into the engine's main execution loop. Background character entities read a shared team data array instantaneously, executing synchronized actions down to the exact millisecond.
*   **🧩 Dual Scripting Sandbox & Dear ImGui:** Houses a secure, dual-runtime virtual machine supporting legacy Lua loops (for full backward compatibility with user-built `GearSwap` logic rules) alongside modern, fast JavaScript execution. It includes a native **ImGui.NET** layer to render modern, rounded, translucent HUD overlays with zero Garbage Collection allocation overhead.
*   **🛠️ Clean Separation of Concerns:** Designed as a clean three-tier project solution workspace mapping to modern software design patterns.

---

## 🏗️ Repository Architecture

GordianXI is organized as a unified monorepo solution workspace:

```text
GordianXI/
├── GordianXI.sln          # Central .NET Solution configuration compiler
└── src/
    ├── Gordian.Core/      # Class Library: Sockets, packet serialization, and multi-box engine
    ├── Gordian.App/       # Avalonia UI App:       Modern desktop forms, tabs, and 3D graphics viewports
    └── Gordian.Addons/    # Class Library: Scripting engines (Lua/JS) and type-safe API schemas
```

*   **`Gordian.Core`:** The backbone of the application. Manages raw network streams (`TcpClient`), slices binary packet fragments safely via `Span<byte>`, and processes background headless automation routines safely on dedicated worker threads.
*   **`Gordian.App`:** The graphical user interface built with **Avalonia UI** and **FluentAvalonia**. Handles true desktop-level window scaling, pop-out layout configurations for background characters, and hosts the underlying GPU-accelerated graphics viewport.
*   **`Gordian.Addons`:** The extension ecosystem sandbox. Isolates community-built scripts and modules, ensuring that an error or infinite loop inside a user script can never freeze the main client loop or crash the engine.

---

## 🗺️ Roadmap & Phase Strategy

To maintain a sustainable, incremental implementation velocity, development follows a strict minimum viable product (MVP) lifecycle:

*   **Phase 1: The Headless Multi-Bot (Current Focus)**
    *   Establish raw C# `TcpClient` logic to communicate directly with LandSandBoat private servers.
    *   Implement type-safe binary slicing arrays to process the core Blowfish packet encryption handshake.
    *   Simultaneously sustain a stable, 6-character text-based network connection state loop without loading graphics.
*   **Phase 2: The Unified Event Bus**
    *   Build internal group management telemetry to sync combat triggers, pathing, and movements across entities.
    *   Verify packet delivery thresholds to guarantee zero dropped commands under heavy battle data load.
*   **Phase 3: The Graphical Viewport**
    *   Introduce Avalonia window layouts and separate pop-out dashboard panels.
    *   Integrate a high-performance 3D graphics backend canvas to read and display local game asset `.dat` directories.
    *   Bind `ImGui.NET` hooks to expose a modern, stylized visual presentation overlay to the addon layer.

---

## 🛠️ Local Development Pre-requisites

To open, compile, and run this solution locally, you require:
1.  **[.NET 10.0 SDK](https://microsoft.com)** or newer (Targeting Long-Term Support)
2.  **[VS Code](https://visualstudio.com)** with the following extensions installed:
    *   `C# Dev Kit` (Microsoft)
    *   `Avalonia for VS Code` (AvaloniaUI)
    *   `Lua` (sumneko)

### Getting Started

Clone the repository and compile the initial project workspace solution files using the native CLI:

```bash
git clone https://github.com
cd GordianXI
dotnet restore
dotnet build
```

---

## 🤝 Contributing

Contributions from systems engineers, reverse-engineers, and community script creators are incredibly welcome. Please ensure that all contributions strictly practice **Clean-Room Reverse Engineering methodologies**. Do not reference or commit leaked material, internal company data briefs, or copyrighted binary fragments.

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## AI Assistance

This project is being developed largely with the use of AI agents. All changes are being reviewed and directed by human authors.
