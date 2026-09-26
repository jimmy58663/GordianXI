# Desktop Shell & Account Profiles

> Phase 1 foundation of `Gordian.App`: the Avalonia control panel, account profiles and the packet inspector. Status and open work: [ROADMAP.md](../../ROADMAP.md) and GitHub Issues.

## Foundation (Phase 1)

- Four-tier decoupled monorepo (`Gordian.Core`, `Gordian.Automation`, `Gordian.Addons`, `Gordian.App`)
- Clean-room reverse engineering boundaries enforced in `AGENTS.md`
- Modern dark HUD UI with Avalonia MVVM
- Encrypted profile storage with master AES key and defensive plaintext migration
- Account profile management (Add, Edit, Delete, Select for Launch)
- Profile & Global session termination commands (`TerminateCommand`, `TerminateAllCommand`)
- Live Packet Inspector UI with Hex/ASCII view, pause/clear, and real-time capture
