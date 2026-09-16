# GordianXI Resources & Modding Guide

Welcome to the GordianXI resource pipeline. GordianXI features a high-performance, cross-platform **Modular Virtual File System (VFS)** that simultaneously supports:
1. **Legacy FFXI DAT Mods:** 100% backward-compatible drop-in support for existing community mods (XIPivot / Pivot style).
2. **Modern Industry-Standard Assets:** Full support for modern 3D models (**glTF 2.0 / `.glb`**), PBR textures (**DDS BC7/BC5**, **PNG/WebP**), and audio (**OGG Vorbis / FLAC**), with direct DAT aliasing and custom entity replacement.

---

## 📁 Directory Architecture

All user mods and assets reside in the `resources/` directory:

```text
resources/
├── dats/                       <-- Legacy DAT Overlays (XIPivot drop-in)
│   ├── HD_UI/
│   │   └── ROM/
│   │       └── 0/
│   │           └── 0.DAT
│   └── AshenbubsHD/
│       ├── ROM/
│       └── ROM2/
│
├── assets/                     <-- Modern Asset Packs (glTF 2.0, DDS, OGG)
│   ├── PBR_Characters/
│   │   ├── manifest.json       <-- Pack metadata and DAT aliases
│   │   └── models/
│   │       └── characters/hume_m/body/haubergeon.glb
│   ├── Orchestral_Music/
│   │   ├── manifest.json
│   │   └── audio/
│   │       └── music/zone_102.ogg
│   └── Remastered_Weapons/
│       ├── manifest.json
│       └── models/
│           └── weapons/excalibur.glb
│
├── vfs.json                    <-- Master player load order and toggles
├── manifest.example.json       <-- Example pack manifest for modders
└── vfs.example.json            <-- Example player configuration
```

---

## 🔄 1. Porting Existing Legacy DAT Mods (XIPivot Compatible)

If you already use mods from Windower / Ashita / XIPivot, you can bring them over immediately without changes:
1. Create a subfolder inside `resources/dats/` named after your mod pack (e.g. `resources/dats/MyMod/`).
2. Copy your mod's `ROM`, `ROM2` ... `ROM9` folders or root files (`FTABLE.DAT`, `VTABLE.DAT`) directly into that subfolder.
3. Start GordianXI (or reload the VFS). The pack will be detected and added to `vfs.json`.

> [!TIP]
> **Cross-Platform Casing Safety:**
> Unlike legacy tools that can fail on Linux or Steam Deck when file paths have inconsistent casing, GordianXI automatically normalizes all paths to uppercase invariant keys (`ROM/118/106.DAT`) at mount time. Your mods will work identically on Windows, Linux, and macOS.

---

## 🎨 2. Authoring Modern Asset Packs (`resources/assets/`)

A modern asset pack is a self-contained folder inside `resources/assets/<PackName>/`. A pack can provide models, textures, audio, or all three combined!

### Supported Formats
*   **3D Models & Skeletons:** **glTF 2.0 (`.glb` binary or `.gltf` + `.bin`)**.
    *   Supports embedded PBR materials (BaseColor, Metallic, Roughness, Normal, Emissive, Occlusion).
    *   Supports skeletal hierarchies, vertex skinning weights, and blend shapes.
    *   Compatible with standard FFXI bone hierarchies or custom embedded animation clips.
*   **Textures:** **DDS (DirectDraw Surface with BC7, BC5, or BC1 compression)** for instant GPU mipmapped streaming, or standard **PNG / WebP**.
*   **Audio:** **OGG Vorbis** (`.ogg`) or **FLAC** (`.flac`) for high-fidelity music, ambiances, and sound effects.

### Directory Conventions
Within your pack, you can organize assets using standard conventions:
```text
resources/assets/<PackName>/
├── manifest.json
├── models/
│   ├── characters/
│   │   └── <race>/               # hume_m, hume_f, elvaan_m, elvaan_f, taru_m, taru_f, mithra, galka
│   │       ├── body/             # e.g., 10240.glb (by FFXI Item ID)
│   │       ├── hands/
│   │       ├── legs/
│   │       ├── feet/
│   │       ├── head/
│   │       └── face/             # e.g., face_01a.glb
│   ├── monsters/
│   │   └── <model_id_or_name>/   # e.g., behemoth/model.glb
│   ├── weapons/
│   │   └── <item_id_or_name>/    # e.g., 16384.glb (Excalibur)
│   └── zones/
│       └── <zone_id>/            # e.g., 102/terrain.glb
├── textures/
│   └── pbr/
└── audio/
    └── music/
```

---

## 📜 3. The `manifest.json` Specification

An optional `manifest.json` at the root of your modern pack allows you to declare metadata, define entity overrides, and **replace legacy DATs directly with modern glTF assets**:

```json
{
  "id": "pbr_haubergeon_overhaul",
  "name": "PBR Haubergeon Remaster",
  "version": "1.0.0",
  "author": "VanaDielArtist",
  "description": "High-poly physically-based rendering overhaul for Haubergeon armor.",
  "overrides": {
    "items": {
      "12502": { "model": "models/characters/hume_m/body/haubergeon.glb" }
    },
    "datAliases": {
      "ROM/28/52.DAT": "models/characters/hume_m/body/haubergeon.glb"
    }
  },
  "skeleton": {
    "type": "ffxi_standard",
    "retargeting": "auto"
  }
}
```

### Direct DAT Aliasing (`datAliases`)
When GordianXI requests a DAT file (e.g. `ROM/28/52.DAT`), the VFS checks `datAliases`. If a mapping exists, the VFS intercepts the call and serves your `.glb` model instead of parsing the 2002 Square Enix DAT chunk!

---

## ⚙️ 4. Master Load Order & Configuration (`vfs.json`)

The master configuration file `resources/vfs.json` controls which packs are active and their priority stacking:

```json
{
  "version": 1,
  "enabled": true,
  "hotReload": true,
  "dats": [
    { "id": "HD_UI",          "enabled": true,  "priority": 100 },
    { "id": "AshenbubsHD",    "enabled": true,  "priority": 50 }
  ],
  "assets": [
    { "id": "PBR_Characters", "enabled": true,  "priority": 100 },
    { "id": "Orchestral_Music","enabled": true,  "priority": 50 }
  ]
}
```

*   **Priority Stacking:** Higher priority values override lower priority values. If both `HD_UI` (100) and `AshenbubsHD` (50) replace the same DAT, the file from `HD_UI` is loaded.
*   **Base Game Fallback:** Any file not found in an enabled mod pack falls back to the genuine base FFXI install directory.
*   **Hot-Reloading:** When `"hotReload": true`, adding or modifying files in `resources/` automatically reloads assets in real-time.
