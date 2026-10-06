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
* **Notice:**
```text
MIT License

Copyright (c) 2025 amwx

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

### Silk.NET
* **Project:** [Silk.NET](https://github.com/dotnet/Silk.NET) (Silk.NET.SDL, Silk.NET.OpenAL 2.23.0, the graphics bindings NeoVeldrid uses, and the Silk.NET.OpenAL.Soft.Native 1.23.1 packaging of OpenAL Soft below)
* **License:** MIT License
* **Copyright:** Copyright (c) 2020-.NET Foundation and Contributors
* **Notice:**
```text
MIT License

- Copyright (c) 2019-2020 Ultz Limited
- Copyright (c) 2021- .NET Foundation and Contributors

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

### NeoVeldrid & NeoVeldrid.SPIRV
* **Project:** [NeoVeldrid](https://github.com/jhm-ciberman/neo-veldrid) (NeoVeldrid and NeoVeldrid.SPIRV 1.2.1), a maintained fork of [Veldrid](https://github.com/veldrid/veldrid)
* **License:** MIT License
* **Copyright:** Copyright (c) 2017 Eric Mellino and Veldrid contributors; Copyright (c) 2026 Javier Mora and NeoVeldrid contributors
* **Notice:**
```text
The MIT License (MIT)

Copyright (c) 2017 Eric Mellino and Veldrid contributors
Copyright (c) 2026 Javier Mora and NeoVeldrid contributors

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

### Native graphics libraries (through NeoVeldrid's Silk.NET packages)
NeoVeldrid pulls in Silk.NET packages (MIT, see Silk.NET above) that ship these unmodified native libraries, loaded dynamically at run time:
* **[MoltenVK](https://github.com/KhronosGroup/MoltenVK)** (`Silk.NET.MoltenVK.Native`, macOS only: Vulkan on Metal). Apache License 2.0; copyright as stated in the linked project's LICENSE.
* **[shaderc](https://github.com/google/shaderc)** (`Silk.NET.Shaderc.Native`: GLSL to SPIR-V for NeoVeldrid.SPIRV). Apache License 2.0; copyright as stated in the linked project's LICENSE.
* **[SPIRV-Cross](https://github.com/KhronosGroup/SPIRV-Cross)** (`Silk.NET.SPIRV.Cross.Native`: SPIR-V to HLSL/GLSL/MSL for NeoVeldrid.SPIRV). Apache License 2.0; copyright as stated in the linked project's LICENSE.

The packages declare the licence as `Apache-2.0` but carry no licence or NOTICE file, so the text is at https://www.apache.org/licenses/LICENSE-2.0 and the copyright and NOTICE text are in each linked project.

### Simple DirectMedia Layer (SDL)
* **Project:** [SDL](https://www.libsdl.org/)
* **License:** zlib License
* **Copyright:** Copyright (C) 1997-2026 Sam Lantinga
* **Notice:**
```text
This software is provided 'as-is', without any express or implied
warranty.  In no event will the authors be held liable for any damages
arising from the use of this software.

Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:

1. The origin of this software must not be misrepresented; you must not
   claim that you wrote the original software. If you use this software
   in a product, an acknowledgment in the product documentation would be
   appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be
   misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.
```

### OpenAL Soft
* **Project:** [OpenAL Soft](https://github.com/kcat/openal-soft) (https://openal-soft.org), shipped as the native library of the `Silk.NET.OpenAL.Soft.Native` 1.23.1 NuGet package: `runtimes/<rid>/native/soft_oal.dll` (Windows), `libopenal.so` (Linux), `libopenal.dylib` (macOS)
* **License:** GNU Library / Lesser General Public License, version 2 or later (used under LGPL-2.1)
* **Copyright:** Copyright (C) 1999-2023 the OpenAL Soft authors (Chris Robinson and contributors)
* **How GordianXI uses it:** the unmodified library is loaded dynamically at run time (P/Invoke through Silk.NET) and is the audio output device only. GordianXI does not link it statically, modify it or include any of its source. Users may replace the library file beside the executable with any compatible build of OpenAL Soft (or another OpenAL implementation); GordianXI falls back to silent output if it cannot be loaded.
* **Source:** the corresponding source for the shipped version is available from https://github.com/kcat/openal-soft (tag 1.23.1).
* **Notice:**
```text
OpenAL Soft is free software; you can redistribute it and/or modify it under
the terms of the GNU Library General Public License as published by the Free
Software Foundation; either version 2 of the License, or (at your option) any
later version.

This library is distributed in the hope that it will be useful, but WITHOUT ANY
WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A
PARTICULAR PURPOSE. See the GNU Library General Public License for more details.

The full licence text: https://github.com/kcat/openal-soft/blob/master/COPYING
```
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
* **License:** GNU General Public License v3.0 (GPLv3)
* **Copyright:** Copyright (c) DarkStar Project Contributors
* **Referenced For:** Historical packet analysis and foundational opcode mapping.

### xi-model-viewer
* **Repository:** [https://github.com/vekien/xi-model-viewer](https://github.com/vekien/xi-model-viewer)
* **License:** GNU General Public License v3.0 (GPLv3)
* **Copyright:** Copyright (c) vekien and xi-model-viewer Contributors
* **Referenced For:** FFXI ROM `.DAT` chunk structures, 3D entity and zone mesh layouts, vertex encoding, skeletal hierarchies, bone weight layouts, and animation timelines for clean-room resource decoding.

### XiPackets
* **Repository:** [https://github.com/atom0s/XiPackets](https://github.com/atom0s/XiPackets)
* **License:** GNU Affero General Public License v3.0 (AGPLv3)
* **Copyright:** Copyright (c) atom0s and XiPackets Contributors
* **Referenced For:** Cross-reference documentation of FFXI network packet functions, sub-packet layouts, and world/client protocol structures.

### xi-tools
* **Repository:** [https://github.com/vekien/xi-tools](https://github.com/vekien/xi-tools)
* **License:** GNU General Public License v3.0 (GPLv3)
* **Copyright:** Copyright (c) vekien and xi-tools Contributors
* **Referenced For:** FFXI skeletal animation clip format documentation (`docs/anim/format.md`) for clean-room animation decoding.

### XiEvents
* **Repository:** [https://github.com/atom0s/XiEvents](https://github.com/atom0s/XiEvents)
* **License:** GNU Affero General Public License v3.0 (AGPLv3)
* **Copyright:** Copyright (c) atom0s and XiEvents Contributors
* **Referenced For:** Cross-reference documentation of the FFXI event virtual machine opcode set and event/cutscene byte-code structures for clean-room event script decoding.

### Disclaimer of Affiliation
The citations provided above are purely for technical documentation, transparency, and interoperability reference purposes. Reference to these open-source repositories does not constitute or imply endorsement, sponsorship, recommendation, or affiliation with GordianXI by the respective project maintainers, authors, or contributors.

