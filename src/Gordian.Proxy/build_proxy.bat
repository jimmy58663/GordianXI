@echo off
set "vcvars_path=C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat"
if not exist "%vcvars_path%" (
    set "vcvars_path=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat"
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
