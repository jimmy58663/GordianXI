# GordianXI Project Roadmap & Milestone Tracker

## Current State Summary
- **Target Framework:** .NET 10 (C# 14) + Avalonia UI 12.1.2 + ImGui.NET
- **Test Status:** 54 Passing Unit Tests (`dotnet test`)
- **Active Focus:** Bootloader Session Handoff & Game Server Interception

---

## 🗺️ Phases & Milestones

### ✅ Phase 1: Foundation & Client Infrastructure
- [x] Four-tier decoupled monorepo (`Gordian.Core`, `Gordian.Automation`, `Gordian.Addons`, `Gordian.App`)
- [x] Clean-room reverse engineering boundaries enforced in `AGENTS.md`
- [x] Modern dark HUD UI with Avalonia MVVM
- [x] Encrypted profile storage with master AES key and defensive plaintext migration
- [x] Account profile management (Add, Edit, Delete, Select for Launch)
- [x] Profile & Global session termination commands (`TerminateCommand`, `TerminateAllCommand`)
- [x] Live Packet Inspector UI with Hex/ASCII view, pause/clear, and real-time capture

### 🔄 Phase 2: Bootloader Session Handoff (Current Phase)
- [x] Reverse-engineered `xiloader.exe` and `pol.exe` COM launch mechanisms
- [x] Multi-region 32-bit registry detection (`GameDirectoryDetector.cs` for US/EU/JP under `InstallFolder\0001`)
- [x] Ephemeral proxy swapping & self-healing startup (`ProxyStager.cs` managing `FFXiMain.dll` $\leftrightarrow$ `FFXiMain.dll.orig`)
- [x] COM-compliant 32-bit proxy (`Gordian.Proxy` with `DllGetClassObject`, `DllCanUnloadNow`, `IClassFactory`)
- [x] Named Pipe IPC bridge (`\\.\pipe\GordianXI_Handoff`)
- [ ] Inspect `xiloader` source repository directly (mounted in next chat session)
- [ ] Verify live handoff in-game: launch `Local (Cybin)` -> named pipe receive -> live UDP packet stream

### ⏳ Phase 3: Network Engine & Cryptographic Handshake
- [ ] Full Blowfish cipher suite integration (`LegacyBlowfishCryptoSuite`)
- [ ] Incoming & outgoing UDP packet framing (`0x0A` chunking, sequence tracking, checksum verification)
- [ ] Session keepalive / ping-pong loop
- [ ] Zone connection transition handling

### ⏳ Phase 4: World State, Viewport & Addons
- [ ] High-performance spatial entity tracking
- [ ] Native DirectX/Vulkan/Metal 3D viewport embedding in Avalonia
- [ ] ImGui HUD overlay pipeline
- [ ] Lua (NLua) and JavaScript (QuickJS) isolated plugin VMs
