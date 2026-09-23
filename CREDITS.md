# Credits & Acknowledgements

GordianXI is an independent, clean-room client architecture for Final Fantasy XI. It stands upon decades of tireless research, documentation, and reverse-engineering conducted by the open-source community. We gratefully acknowledge and credit the following projects, research groups, and tools.

---

## 🌐 Community Research & Reverse Engineering

* **[LandSandBoat](https://github.com/LandSandBoat/server)** (Licensed under GPLv3)
  * Extensive documentation and mapping of the modern FFXI server architecture, network wire protocols, packet opcodes, Blowfish cryptographic handshakes, and bitstream compression tables.
  * LandSandBoat's publicly available protocol specifications served as the primary reference for GordianXI's network interoperability engine.

* **[XILoader](https://github.com/LandSandBoat/xiloader)** (Licensed under GPLv3)
  * Crucial research into the PlayOnline bootloader handoff mechanism, registry key detection across international client releases, and memory parameter structures passed to `FFXiMain.dll`.

* **[DarkStar Project](https://github.com/DarkStarProject/darkstar)** (Licensed under GPLv2)
  * Foundational private server emulation, network packet analysis, and packet opcode cataloging that formed the bedrock of modern FFXI server research.

* **[Windower](https://github.com/Windower)** & **[Ashita](https://github.com/AshitaXI)**
  * Decades of pioneering work in client hook research, packet structures, UI overlays, and addon scripting ecosystems that established user interface and automation standards across the community.

* **[xi-model-viewer](https://github.com/vekien/xi-model-viewer)** (Licensed under GPLv3)
  * Extensive documentation and reverse-engineering of FFXI 3D formats, DAT chunk hierarchies, skeletal systems, and animation data.

* **[XiPackets](https://github.com/atom0s/XiPackets)** (Licensed under AGPLv3)
  * Reverse-engineered documentation of FFXI network packet functions, data, and systems, used as a cross-reference for GordianXI's packet schema research.

* **[XiEvents](https://github.com/atom0s/XiEvents)** (Licensed under AGPLv3)
  * Reverse-engineered documentation of the FFXI event virtual machine opcode set and event/cutscene byte-code structures, used as a cross-reference for GordianXI's clean-room event script decoding.

* **[xi-tools](https://github.com/vekien/xi-tools)** (Licensed under GPLv3)
  * Knowledgebase and toolset documentation for FFXI DAT modifications, referenced for skeletal animation clip format research.

---

## 🛠️ Frameworks, Libraries & Runtime Tools

* **[.NET & C#](https://github.com/dotnet)** (MIT License) — Microsoft Corporation and the .NET Foundation.
* **[Avalonia UI](https://github.com/AvaloniaUI/Avalonia)** (MIT License) — The Avalonia UI team, providing the cross-platform desktop UI framework and rendering infrastructure.
* **[FluentAvalonia](https://github.com/amwx/FluentAvalonia)** (MIT License) — Modern WinUI 3 controls and themes for Avalonia.
* **[ImGui.NET](https://github.com/ImGuiNET/ImGui.NET)** (MIT License) — C# wrapper for Dear ImGui, providing zero-allocation HUD overlays.

---

## 👥 Authors & Contributors

* **GordianXI Core Contributors & Maintainers**
* Developed with the assistance of advanced AI pair-programming systems directed and verified by human maintainers.
