# Legal Information & Clean-Room Disclaimers

---

## ⚖️ Clean-Room Reverse Engineering & Interoperability

GordianXI is an independent, non-commercial research and software preservation project aimed at creating a clean-room, 64-bit client architecture capable of interoperating with Final Fantasy XI network protocols.

The implementation was designed and authored from scratch using publicly available network protocol specifications, packet captures, and open-source server emulator documentation (such as LandSandBoat).

* **United States Law (DMCA 17 U.S.C. § 1201(f)):** Under the Digital Millennium Copyright Act's reverse engineering exemption, circumvention or analysis performed strictly for the purpose of achieving interoperability of an independently created computer program with other programs is expressly permitted.
* **Judicial Precedents:** The clean-room analysis and functional reimplementation of communication protocols and file formats is protected under established copyright precedents, including *Sega Enterprises Ltd. v. Accolade, Inc.*, 977 F.2d 1510 (9th Cir. 1992), *Sony Computer Entertainment, Inc. v. Connectix Corp.*, 203 F.3d 596 (9th Cir. 2000), and *Google LLC v. Oracle America, Inc.*, 141 S. Ct. 1183 (2021).
* **European Union Law:** Reverse engineering for the purpose of achieving interoperability is protected under Article 6 of Directive 2009/24/EC of the European Parliament and of the Council on the legal protection of computer programs.

---

## 🚫 No Proprietary Assets Distributed

GordianXI does **not** contain, host, distribute, or bundle:
* Any proprietary binary executables (`pol.exe`, `ffxi.exe`, `FFXiMain.dll`, etc.) belonging to Square Enix Co., Ltd.
* Any game data archives (`.dat` files), textures, 3D character meshes, level geometries, sound files, or musical compositions.
* Any leaked internal development tools, internal documentation, or source code.

---

## 💿 Requirement of Genuine Retail Client Files

To render graphics, play audio, and execute gameplay scenarios, GordianXI requires access to the genuine data files installed on the user's computer from an officially licensed copy of Final Fantasy XI.
* Users are solely responsible for obtaining and installing an official, licensed retail distribution of the game.
* GordianXI acts solely as an alternative, independent runtime engine reading locally present data files supplied by the user.

---

## 🏷️ Trademark & Copyright Acknowledgement

* **FINAL FANTASY**, **PlayOnline**, **Vana'diel**, and all associated characters, names, and distinctive likenesses are registered trademarks or copyrights of **Square Enix Co., Ltd.**
* GordianXI is not affiliated with, authorized, sponsored, endorsed, or approved by Square Enix Co., Ltd. or any of its affiliates.
* All trademarks and registered trademarks appearing in this repository are the property of their respective owners.

---

## 🛡️ Server Rules & Automation Policy

GordianXI is built with a server-authoritative automation compliance system (`ServerAutomationPolicy`). Private server operators may transmit policy control packets that automatically disable client-side automation features in real time. GordianXI respects server operator autonomy and enforces compliance with server terms of service.
