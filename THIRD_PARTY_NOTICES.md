# Third-Party Software Notices and Information

This document contains licensing notices and terms for third-party software, libraries, and reference specifications used in or consulted during the development of GordianXI.

---

## 1. Direct Software Dependencies (Bundled / Linked)

The following software packages are compiled, linked, or distributed with GordianXI under their respective open-source licenses:

### Avalonia UI
* **Project:** [Avalonia](https://github.com/AvaloniaUI/Avalonia)
* **License:** MIT License
* **Copyright:** Copyright (c) 2014-2026 AvaloniaUI OÜ
* **Notice:**
```text
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors
All Rights Reserved

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### FluentAvalonia
* **Project:** [FluentAvalonia](https://github.com/amwx/FluentAvalonia)
* **License:** MIT License
* **Copyright:** Copyright (c) 2021-2026 Luke F
* **Notice:** Provided under the terms of the MIT License (see above).

---

## 2. Reference Specifications & Clean-Room Interoperability Sources

The following open-source projects were consulted as public reference specifications for network wire protocols, packet structures, cryptographic sequences, and bootloader handoff layouts. 

**Important Notice Regarding Derivative Works & Copyleft:**
GordianXI does **not** copy, redistribute, or link source code or binary libraries from these projects. GordianXI is an independent, clean-room reimplementation written in C# (.NET 10) that interoperates with the functional network protocols and data formats documented by these projects. These references are cited in accordance with open-source attribution best practices and fair-use interoperability standards:

### LandSandBoat
* **Repository:** [https://github.com/LandSandBoat/server](https://github.com/LandSandBoat/server)
* **License:** GNU General Public License v3.0 (GPLv3)
* **Copyright:** Copyright (c) LandSandBoat Project Contributors
* **Referenced For:** Network wire protocol schemas, opcode assignments, session initialization handshake, Blowfish ECB cipher suite behavior, MD5 checksum calculation, and bitstream compression jump tables.

### XILoader
* **Repository:** [https://github.com/LandSandBoat/xiloader](https://github.com/LandSandBoat/xiloader)
* **License:** GNU General Public License v3.0 (GPLv3)
* **Copyright:** Copyright (c) Atom0s and LandSandBoat Contributors
* **Referenced For:** PlayOnline bootloader command-line argument structures, Windows registry path detection for retail client installations, and `FFXiMain.dll` parameter block memory layouts.

### DarkStar Project
* **Repository:** [https://github.com/DarkStarProject/darkstar](https://github.com/DarkStarProject/darkstar)
* **License:** GNU General Public License v2.0 (GPLv2)
* **Copyright:** Copyright (c) DarkStar Project Contributors
* **Referenced For:** Historical packet analysis and foundational opcode mapping.

### xi-model-viewer
* **Repository:** [https://github.com/vekien/xi-model-viewer](https://github.com/vekien/xi-model-viewer)
* **License:** GNU General Public License v3.0 (GPLv3)
* **Copyright:** Copyright (c) vekien and xi-model-viewer Contributors
* **Referenced For:** FFXI ROM `.DAT` chunk structures, 3D entity and zone mesh layouts, vertex encoding, skeletal hierarchies, bone weight layouts, and animation timelines for clean-room resource decoding.
