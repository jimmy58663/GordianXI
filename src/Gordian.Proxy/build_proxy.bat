@echo off
setlocal enabledelayedexpansion

echo ========================================================
echo   GordianXI Proxy Compiler Automation Shell
echo ========================================================

:: 🔍 1. Locate the native MSVC environment variables automatically
set "vcvars_path=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat"

if not exist "!vcvars_path!" (
    :: Fallback check if you have the full Visual Studio Community IDE installed instead
    set "vcvars_path=C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat"
)

if not exist "!vcvars_path!" (
    echo [ERROR] Microsoft C++ Build Tools could not be located.
    echo Please run the winget command first to install MSVC.
    pause
    exit /b 1
)

:: 🔌 2. Force the shell to load the native 32-bit x86 cross-compilation environment counters
echo [System] Initializing 32-bit x86 MSVC build variables...
call "!vcvars_path!" x86 >nul

:: 📂 3. Navigate straight to the proxy source directory root
cd /d "%~dp0"

:: 🚀 4. Fire the one-shot optimized compiler directive
echo [Compiler] Compiling unmanaged 32-bit FFXiMain.dll proxy...
cl.exe /LD /O2 /Fe:FFXiMain.dll dllmain.cpp user32.lib advapi32.lib /link /MACHINE:X86 >nul

if %errorlevel% neq 0 (
    echo [ERROR] Compilation failed. Check your dllmain.cpp syntax loops.
    pause
    exit /b 1
)

echo ========================================================
echo   SUCCESS: FFXiMain.dll compiled allocation-free!
echo ========================================================
echo Output: %~dp0FFXiMain.dll
echo Copy this file directly to your target xiloader.exe folder.
echo ========================================================

:: Clean up intermediate linker output files to keep the workspace pristine
if exist "dllmain.obj" del "dllmain.obj"
if exist "FFXiMain.exp" del "FFXiMain.exp"
if exist "FFXiMain.lib" del "FFXiMain.lib"
pause
