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
* **Project:** [Silk.NET](https://github.com/dotnet/Silk.NET)
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

### Veldrid & Veldrid.ImGui
* **Project:** [Veldrid](https://github.com/veldrid/veldrid)
* **License:** MIT License
* **Copyright:** Copyright (c) 2017 Eric Mellino and contributors
* **Notice:**
```text
The MIT License (MIT)

Copyright (c) 2017 Eric Mellino and Veldrid contributors

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

### Veldrid.SPIRV
* **Project:** [Veldrid-SPIRV](https://github.com/veldrid/veldrid-spirv)
* **License:** MIT License
* **Copyright:** Copyright (c) 2017 Eric Mellino and contributors
* **Notice:**
```text
The MIT License (MIT)

Copyright (c) 2017 Eric Mellino and Veldrid.SPIRV contributors

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

### Disclaimer of Affiliation
The citations provided above are purely for technical documentation, transparency, and interoperability reference purposes. Reference to these open-source repositories does not constitute or imply endorsement, sponsorship, recommendation, or affiliation with GordianXI by the respective project maintainers, authors, or contributors.

