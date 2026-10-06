@echo off
rem Find an MSVC install with the x86/x64 C++ tools through vswhere (any edition, including Build Tools and
rem the GitHub windows-latest image), then fall back to the usual VS 2022 paths.
set "vcvars_path="
set "vswhere_path=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "%vswhere_path%" (
    for /f "usebackq tokens=*" %%i in (`"%vswhere_path%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "vcvars_path=%%i\VC\Auxiliary\Build\vcvarsall.bat"
)
if not defined vcvars_path set "vcvars_path=C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat"
if not exist "%vcvars_path%" (
    set "vcvars_path=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat"
)
if not exist "%vcvars_path%" (
    echo [ERROR] No MSVC install with the C++ x86/x64 tools was found.
    exit /b 1
)

echo [System] Initializing 32-bit x86 MSVC build environment...
call "%vcvars_path%" x86 >nul

cd /d "%~dp0"

echo [Compiler] Compiling unmanaged 32-bit FFXiMain.dll proxy...
cl.exe /LD /O2 /EHsc /Fe:FFXiMain.dll dllmain.cpp user32.lib advapi32.lib ole32.lib shell32.lib /link /MACHINE:X86 /EXPORT:DllGetClassObject /EXPORT:DllCanUnloadNow /EXPORT:DllRegisterServer /EXPORT:DllUnregisterServer /EXPORT:DoPlayOnlineHardwareCheck /EXPORT:InitializeGameInstance

if %errorlevel% neq 0 (
    echo [ERROR] Compilation failed.
    exit /b 1
)

echo [SUCCESS] FFXiMain.dll proxy compiled successfully!

if exist "dllmain.obj" del "dllmain.obj"
if exist "FFXiMain.exp" del "FFXiMain.exp"
if exist "FFXiMain.lib" del "FFXiMain.lib"
