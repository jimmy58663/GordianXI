// src/Gordian.Proxy/dllmain.cpp
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <unknwn.h>
#include <shellapi.h>
#include <string>
#include <sstream>
#include <iostream>
#include <vector>

// Forward declarations
typedef void(__stdcall* LPFN_DoHardwareCheck)();
typedef int(__stdcall* LPFN_InitializeInstance)(void* lpParams);
typedef HRESULT(__stdcall* LPFN_DllGetClassObject)(REFCLSID, REFIID, LPVOID*);

LPFN_DoHardwareCheck Real_DoHardwareCheck = nullptr;
LPFN_InitializeInstance Real_InitializeInstance = nullptr;
LPFN_DllGetClassObject Real_DllGetClassObject = nullptr;
HMODULE hOriginalDll = nullptr;
static bool g_HandoffDispatched = false;

void LogDebug(const std::string& msg) {
    wchar_t appData[MAX_PATH] = { 0 };
    if (GetEnvironmentVariableW(L"APPDATA", appData, MAX_PATH) > 0) {
        std::wstring logDir = std::wstring(appData) + L"\\GordianXI\\logs";
        CreateDirectoryW(logDir.c_str(), nullptr);
        std::wstring logFile = logDir + L"\\proxy_debug.log";
        FILE* f = _wfopen(logFile.c_str(), L"a");
        if (f) {
            SYSTEMTIME st;
            GetLocalTime(&st);
            fprintf(f, "[%02d:%02d:%02d.%03d] [PID %lu] %s\n", st.wHour, st.wMinute, st.wSecond, st.wMilliseconds, GetCurrentProcessId(), msg.c_str());
            fclose(f);
        }
    }
}

// REVERSE-ENGINEERED STRUCT MAPPING
// This memory structure maps to the data layout emitted by bootloaders during session handoff.
struct FfxiHandoffParams {
    uint32_t Size;                // Size indicator of this parameter block
    uint32_t CharacterId;         // The unique database index tracking the chosen character
    char CharacterName[24];       // Null-terminated string containing the character name
    char ServerIp[16];            // Null-terminated string containing the Game World target IP
    uint16_t ServerPort;          // Destination connection port (usually 54230 for map server UDP)
    uint32_t SessionKeyLength;    // Sizing metric for the secure login seed token array
    uint8_t SessionKey[128];      // The raw cryptographically secure binary token array
};

std::string Base64Encode(const uint8_t* data, size_t length) {
    static const char lookup[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    std::string result;
    result.reserve(((length + 2) / 3) * 4);
    
    uint32_t value = 0;
    int bits = -6;
    for (size_t i = 0; i < length; ++i) {
        value = (value << 8) + data[i];
        bits += 8;
        while (bits >= 0) {
            result.push_back(lookup[(value >> bits) & 0x3F]);
            bits -= 6;
        }
    }
    if (bits > -6) result.push_back(lookup[((value << 8) >> (bits + 8)) & 0x3F]);
    while (result.size() % 4 != 0) result.push_back('=');
    return result;
}

std::string EscapeJsonString(const std::string& input) {
    std::ostringstream ss;
    for (char c : input) {
        if (c == '\\' || c == '"') ss << '\\' << c;
        else if (c == '\b') ss << "\\b";
        else if (c == '\f') ss << "\\f";
        else if (c == '\n') ss << "\\n";
        else if (c == '\r') ss << "\\r";
        else if (c == '\t') ss << "\\t";
        else if (c < 32) ss << "";
        else ss << c;
    }
    return ss.str();
}

void SendJsonPayloadToPipe(const std::string& jsonPayload) {
    if (g_HandoffDispatched) return;
    g_HandoffDispatched = true;

    LogDebug(std::string("SendJsonPayloadToPipe: Connecting to \\\\.\\pipe\\GordianXI_Handoff with payload: ") + jsonPayload);

    // Connect to GordianXI's Named Pipe with resilient retry loop
    HANDLE hPipe = INVALID_HANDLE_VALUE;
    for (int retry = 0; retry < 15; ++retry) {
        hPipe = CreateFileW(
            L"\\\\.\\pipe\\GordianXI_Handoff",
            GENERIC_WRITE,
            0,
            nullptr,
            OPEN_EXISTING,
            0,
            nullptr
        );
        if (hPipe != INVALID_HANDLE_VALUE) break;
        if (GetLastError() == ERROR_PIPE_BUSY) {
            WaitNamedPipeW(L"\\\\.\\pipe\\GordianXI_Handoff", 1000);
        } else {
            Sleep(100);
        }
    }

    if (hPipe != INVALID_HANDLE_VALUE) {
        DWORD bytesWritten = 0;
        WriteFile(hPipe, jsonPayload.c_str(), (DWORD)jsonPayload.length(), &bytesWritten, nullptr);
        FlushFileBuffers(hPipe);
        CloseHandle(hPipe);
        LogDebug("SendJsonPayloadToPipe: Successfully wrote payload to named pipe. Bootloader exiting in 100ms.");
    } else {
        LogDebug("SendJsonPayloadToPipe: Failed to connect to named pipe after retries.");
    }

    // Terminate the bootloader process cleanly now that handoff is complete
    Sleep(100);
    ExitProcess(0);
}

void ForwardSessionParametersToGordianCore(FfxiHandoffParams* params) {
    if (!params) return;

    std::string charName(params->CharacterName);
    std::string serverIp(params->ServerIp);
    uint16_t serverPort = params->ServerPort != 0 ? params->ServerPort : 54230;
    std::string b64Token = Base64Encode(params->SessionKey, params->SessionKeyLength);

    std::ostringstream jsonStream;
    jsonStream << "{"
               << "\"TargetCharacterName\":\"" << EscapeJsonString(charName) << "\","
               << "\"ServerIp\":\"" << EscapeJsonString(serverIp) << "\","
               << "\"ServerPort\":" << serverPort << ","
               << "\"CharacterId\":" << params->CharacterId << ","
               << "\"Base64SessionToken\":\"" << b64Token << "\""
               << "}";

    SendJsonPayloadToPipe(jsonStream.str());
}

static std::string ConvertWStringToString(const std::wstring& wstr) {
    if (wstr.empty()) return "";
    int sizeNeeded = WideCharToMultiByte(CP_UTF8, 0, wstr.c_str(), (int)wstr.length(), nullptr, 0, nullptr, nullptr);
    std::string str(sizeNeeded, 0);
    WideCharToMultiByte(CP_UTF8, 0, wstr.c_str(), (int)wstr.length(), &str[0], sizeNeeded, nullptr, nullptr);
    return str;
}

// Inspect command line parameters from the host process using standard CommandLineToArgvW
void NotifySessionFromEnvironment() {
    if (g_HandoffDispatched) return;

    std::string targetChar = "Player";
    std::string serverIp = "127.0.0.1";
    uint16_t serverPort = 54230; // Aligned with LandSandBoat xi_map UDP port
    uint32_t charId = 1;
    std::string sessionToken = "";

    int numArgs = 0;
    LPWSTR* argv = CommandLineToArgvW(GetCommandLineW(), &numArgs);
    if (argv) {
        for (int i = 1; i < numArgs; ++i) {
            if (_wcsicmp(argv[i], L"--user") == 0 || _wcsicmp(argv[i], L"--username") == 0) {
                if (i + 1 < numArgs) targetChar = ConvertWStringToString(argv[++i]);
            } else if (_wcsicmp(argv[i], L"--server") == 0) {
                if (i + 1 < numArgs) serverIp = ConvertWStringToString(argv[++i]);
            } else if (_wcsicmp(argv[i], L"--dataport") == 0 || _wcsicmp(argv[i], L"--port") == 0 || _wcsicmp(argv[i], L"--gameport") == 0) {
                if (i + 1 < numArgs) {
                    try {
                        int p = std::stoi(argv[++i]);
                        if (p > 0 && p <= 65535) serverPort = (uint16_t)p;
                    } catch (...) {}
                }
            }
        }
        LocalFree(argv);
    }

    std::ostringstream jsonStream;
    jsonStream << "{"
               << "\"TargetCharacterName\":\"" << EscapeJsonString(targetChar) << "\","
               << "\"ServerIp\":\"" << EscapeJsonString(serverIp) << "\","
               << "\"ServerPort\":" << serverPort << ","
               << "\"CharacterId\":" << charId << ","
               << "\"Base64SessionToken\":\"" << sessionToken << "\""
               << "}";

    SendJsonPayloadToPipe(jsonStream.str());
}

// FFXI GameMain COM interface UUIDs from ffximain.h
static const GUID CLSID_GameMain_Local = { 0x1027DC46, 0x750D, 0x4B1F, { 0x88, 0x34, 0x1D, 0x25, 0xB8, 0xBE, 0xBA, 0xB8 } };
static const GUID IID_IGameMain_Local = { 0x493BF7B9, 0x0C3A, 0x43B5, { 0xBF, 0xA6, 0x28, 0xFB, 0xEE, 0x25, 0x1E, 0x3D } };

MIDL_INTERFACE("493BF7B9-0C3A-43B5-BFA6-28FBEE251E3D")
IGameMain : public IUnknown
{
public:
    virtual HRESULT __stdcall FFXiGameMain(IUnknown* pPol, IUnknown* pFFXi) = 0;
    virtual HRESULT __stdcall FFXiParaGet(IUnknown** pFFXiPara) = 0;
    virtual HRESULT __stdcall PolLogoutInit(void) = 0;
    virtual HRESULT __stdcall PolLogoutEnd(void) = 0;
};

// Implementation of IGameMain for FFXi.dll invocation
class CProxyGameMain : public IGameMain
{
    LONG m_refCount;
    IGameMain* m_pRealGameMain;
public:
    CProxyGameMain(IGameMain* pReal) : m_refCount(1), m_pRealGameMain(pReal) {
        if (m_pRealGameMain) m_pRealGameMain->AddRef();
    }

    ~CProxyGameMain() {
        if (m_pRealGameMain) {
            m_pRealGameMain->Release();
            m_pRealGameMain = nullptr;
        }
    }

    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        if (!ppv) return E_POINTER;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IGameMain_Local))
        {
            *ppv = static_cast<IGameMain*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }

    STDMETHODIMP_(ULONG) AddRef() override
    {
        return InterlockedIncrement(&m_refCount);
    }

    STDMETHODIMP_(ULONG) Release() override
    {
        LONG count = InterlockedDecrement(&m_refCount);
        if (count == 0) delete this;
        return count;
    }

    virtual HRESULT __stdcall FFXiGameMain(IUnknown* pPol, IUnknown* pFFXi) override
    {
        LogDebug("CProxyGameMain::FFXiGameMain: Called by FFXi.dll! Character selection complete. Initiating handoff to GordianXI...");
        NotifySessionFromEnvironment();
        return S_OK;
    }

    virtual HRESULT __stdcall FFXiParaGet(IUnknown** pFFXiPara) override
    {
        LogDebug("CProxyGameMain::FFXiParaGet called");
        if (m_pRealGameMain) {
            HRESULT hr = m_pRealGameMain->FFXiParaGet(pFFXiPara);
            LogDebug(std::string("FFXiParaGet from genuine DLL returned hr=0x") + std::to_string((unsigned long)hr));
            return hr;
        }
        if (pFFXiPara) *pFFXiPara = nullptr;
        return S_OK;
    }

    virtual HRESULT __stdcall PolLogoutInit(void) override {
        LogDebug("CProxyGameMain::PolLogoutInit called");
        if (m_pRealGameMain) return m_pRealGameMain->PolLogoutInit();
        return S_OK;
    }

    virtual HRESULT __stdcall PolLogoutEnd(void) override {
        LogDebug("CProxyGameMain::PolLogoutEnd called");
        if (m_pRealGameMain) return m_pRealGameMain->PolLogoutEnd();
        return S_OK;
    }
};

// COM Class Factory Implementation
class CProxyClassFactory : public IClassFactory
{
    LONG m_refCount;
public:
    CProxyClassFactory() : m_refCount(1) {}

    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        if (!ppv) return E_POINTER;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IClassFactory))
        {
            *ppv = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }

    STDMETHODIMP_(ULONG) AddRef() override
    {
        return InterlockedIncrement(&m_refCount);
    }

    STDMETHODIMP_(ULONG) Release() override
    {
        LONG count = InterlockedDecrement(&m_refCount);
        if (count == 0) delete this;
        return count;
    }

    STDMETHODIMP CreateInstance(IUnknown* pUnkOuter, REFIID riid, void** ppv) override
    {
        LogDebug("CProxyClassFactory::CreateInstance called");
        if (pUnkOuter != nullptr) return CLASS_E_NOAGGREGATION;
        if (!ppv) return E_POINTER;

        IGameMain* pRealGameMain = nullptr;
        if (Real_DllGetClassObject) {
            IClassFactory* pRealFactory = nullptr;
            HRESULT hrFact = Real_DllGetClassObject(CLSID_GameMain_Local, IID_IClassFactory, (void**)&pRealFactory);
            if (SUCCEEDED(hrFact) && pRealFactory) {
                HRESULT hrCreate = pRealFactory->CreateInstance(nullptr, IID_IGameMain_Local, (void**)&pRealGameMain);
                pRealFactory->Release();
                if (SUCCEEDED(hrCreate) && pRealGameMain) {
                    LogDebug("CProxyClassFactory: Successfully created genuine IGameMain from FFXiMain.dll.orig");
                } else {
                    LogDebug("CProxyClassFactory: Failed to create genuine IGameMain from genuine factory");
                }
            } else {
                LogDebug("CProxyClassFactory: Failed to get genuine IClassFactory from FFXiMain.dll.orig");
            }
        }

        CProxyGameMain* pGame = new CProxyGameMain(pRealGameMain);
        HRESULT hr = pGame->QueryInterface(riid, ppv);
        pGame->Release();
        return hr;
    }

    STDMETHODIMP LockServer(BOOL fLock) override
    {
        return S_OK;
    }
};

// ============================================================================
// EXPORTED FUNCTIONS
// ============================================================================

STDAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, LPVOID* ppv)
{
    LogDebug("DllGetClassObject called");
    if (!ppv) return E_POINTER;
    *ppv = nullptr;

    CProxyClassFactory* pFactory = new CProxyClassFactory();
    HRESULT hr = pFactory->QueryInterface(riid, ppv);
    pFactory->Release();
    return hr;
}

STDAPI DllCanUnloadNow(void)
{
    return S_OK;
}

STDAPI DllRegisterServer(void)
{
    return S_OK;
}

STDAPI DllUnregisterServer(void)
{
    return S_OK;
}

extern "C" __declspec(dllexport) void __stdcall DoPlayOnlineHardwareCheck()
{
    if (Real_DoHardwareCheck) Real_DoHardwareCheck();
}

extern "C" __declspec(dllexport) int __stdcall InitializeGameInstance(void* lpParams)
{
    LogDebug("InitializeGameInstance called");
    if (lpParams != nullptr) {
        FfxiHandoffParams* params = reinterpret_cast<FfxiHandoffParams*>(lpParams);
        ForwardSessionParametersToGordianCore(params);
    } else {
        NotifySessionFromEnvironment();
    }

    if (Real_InitializeInstance) return Real_InitializeInstance(lpParams);
    return -1;
}

static HMODULE g_hProxyModule = nullptr;

// Optional link to original DLL if available
void InterceptAndLinkOriginalDll()
{
    wchar_t modulePath[MAX_PATH] = { 0 };
    if (GetModuleFileNameW(g_hProxyModule, modulePath, MAX_PATH) > 0) {
        std::wstring path(modulePath);
        size_t lastSlash = path.find_last_of(L"\\/");
        if (lastSlash != std::wstring::npos) {
            std::wstring origPath = path.substr(0, lastSlash + 1) + L"FFXiMain.dll.orig";
            hOriginalDll = LoadLibraryW(origPath.c_str());
            if (hOriginalDll) {
                Real_DllGetClassObject = (LPFN_DllGetClassObject)GetProcAddress(hOriginalDll, "DllGetClassObject");
                Real_DoHardwareCheck = (LPFN_DoHardwareCheck)GetProcAddress(hOriginalDll, "DoPlayOnlineHardwareCheck");
                Real_InitializeInstance = (LPFN_InitializeInstance)GetProcAddress(hOriginalDll, "InitializeGameInstance");
                LogDebug("InterceptAndLinkOriginalDll: Successfully linked genuine FFXiMain.dll.orig");
            } else {
                LogDebug(std::string("InterceptAndLinkOriginalDll: Could not load FFXiMain.dll.orig at ") + ConvertWStringToString(origPath));
            }
        }
    }
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    if (ul_reason_for_call == DLL_PROCESS_ATTACH) {
        g_hProxyModule = hModule;
        DisableThreadLibraryCalls(hModule);
        wchar_t hostExe[MAX_PATH] = { 0 };
        GetModuleFileNameW(nullptr, hostExe, MAX_PATH);
        char hostNameA[MAX_PATH] = { 0 };
        WideCharToMultiByte(CP_UTF8, 0, hostExe, -1, hostNameA, MAX_PATH, nullptr, nullptr);
        LogDebug(std::string("DllMain: DLL_PROCESS_ATTACH by process: ") + hostNameA);
        InterceptAndLinkOriginalDll();
    } else if (ul_reason_for_call == DLL_PROCESS_DETACH) {
        LogDebug("DllMain: DLL_PROCESS_DETACH");
        if (hOriginalDll) FreeLibrary(hOriginalDll);
    }
    return TRUE;
}
