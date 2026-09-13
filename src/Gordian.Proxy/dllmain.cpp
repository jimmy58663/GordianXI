// src/Gordian.Proxy/dllmain.cpp
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <unknwn.h>
#include <string>
#include <sstream>
#include <iostream>
#include <vector>

// Forward declarations
typedef void(__stdcall* LPFN_DoHardwareCheck)();
typedef int(__stdcall* LPFN_InitializeInstance)(void* lpParams);

LPFN_DoHardwareCheck Real_DoHardwareCheck = nullptr;
LPFN_InitializeInstance Real_InitializeInstance = nullptr;
HMODULE hOriginalDll = nullptr;
static bool g_HandoffDispatched = false;

// REVERSE-ENGINEERED STRUCT MAPPING
// This memory structure maps to the data layout emitted by bootloaders during session handoff.
struct FfxiHandoffParams {
    uint32_t Size;                // Size indicator of this parameter block
    uint32_t CharacterId;         // The unique database index tracking the chosen character
    char CharacterName[24];       // Null-terminated string containing the character name
    char ServerIp[16];            // Null-terminated string containing the Game World target IP
    uint16_t ServerPort;          // Destination connection port (usually 54231)
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

    // Connect to GordianXI's Named Pipe
    HANDLE hPipe = CreateFileW(
        L"\\\\.\\pipe\\GordianXI_Handoff",
        GENERIC_WRITE,
        0,
        nullptr,
        OPEN_EXISTING,
        0,
        nullptr
    );

    if (hPipe != INVALID_HANDLE_VALUE) {
        DWORD bytesWritten = 0;
        WriteFile(hPipe, jsonPayload.c_str(), (DWORD)jsonPayload.length(), &bytesWritten, nullptr);
        FlushFileBuffers(hPipe);
        CloseHandle(hPipe);
    }

    // Terminate the dummy bootloader process cleanly so the DLL file is released immediately
    Sleep(100);
    ExitProcess(0);
}

void ForwardSessionParametersToGordianCore(FfxiHandoffParams* params) {
    if (!params) return;

    std::string charName(params->CharacterName);
    std::string serverIp(params->ServerIp);
    std::string b64Token = Base64Encode(params->SessionKey, params->SessionKeyLength);

    std::ostringstream jsonStream;
    jsonStream << "{"
               << "\"TargetCharacterName\":\"" << EscapeJsonString(charName) << "\","
               << "\"ServerIp\":\"" << EscapeJsonString(serverIp) << "\","
               << "\"ServerPort\":" << params->ServerPort << ","
               << "\"CharacterId\":" << params->CharacterId << ","
               << "\"Base64SessionToken\":\"" << b64Token << "\""
               << "}";

    SendJsonPayloadToPipe(jsonStream.str());
}

// Inspect command line parameters from the host process
void NotifySessionFromEnvironment() {
    if (g_HandoffDispatched) return;

    std::wstring cmdLine = GetCommandLineW();
    std::string targetChar = "Player";
    std::string serverIp = "127.0.0.1";
    uint16_t serverPort = 54231;
    uint32_t charId = 1;
    std::string sessionToken = "";

    // Parse simple flags from command line if present:
    // e.g. --user <username>, --server <ip>, --port <port>, -port <port>
    auto findArgValue = [&](const std::wstring& key) -> std::string {
        size_t pos = cmdLine.find(key);
        if (pos != std::wstring::npos) {
            size_t valStart = cmdLine.find_first_not_of(L" \t=", pos + key.length());
            if (valStart != std::wstring::npos) {
                size_t valEnd = cmdLine.find_first_of(L" \t\"", valStart);
                if (valEnd == std::wstring::npos) valEnd = cmdLine.length();
                std::wstring val = cmdLine.substr(valStart, valEnd - valStart);
                std::string result;
                result.reserve(val.length());
                for (wchar_t wc : val) result.push_back(static_cast<char>(wc));
                return result;
            }
        }
        return "";
    };

    std::string user = findArgValue(L"--user");
    if (user.empty()) user = findArgValue(L"--username");
    if (!user.empty()) targetChar = user;

    std::string srv = findArgValue(L"--server");
    if (!srv.empty()) serverIp = srv;

    std::string portStr = findArgValue(L"--serverport");
    if (portStr.empty()) portStr = findArgValue(L"-port");
    if (portStr.empty()) portStr = findArgValue(L"--port");
    if (!portStr.empty()) {
        try {
            int p = std::stoi(portStr);
            if (p > 0 && p <= 65535) serverPort = (uint16_t)p;
        } catch (...) {}
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
        if (pUnkOuter != nullptr) return CLASS_E_NOAGGREGATION;
        if (!ppv) return E_POINTER;

        NotifySessionFromEnvironment();
        *ppv = nullptr;
        return S_OK;
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
    if (!ppv) return E_POINTER;
    *ppv = nullptr;

    NotifySessionFromEnvironment();

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
    if (lpParams != nullptr) {
        FfxiHandoffParams* params = reinterpret_cast<FfxiHandoffParams*>(lpParams);
        ForwardSessionParametersToGordianCore(params);
    } else {
        NotifySessionFromEnvironment();
    }

    if (Real_InitializeInstance) return Real_InitializeInstance(lpParams);
    return -1;
}

// Optional link to original DLL if available
void InterceptAndLinkOriginalDll()
{
    // Check if FFXiMain.dll.orig exists next to this proxy
    wchar_t modulePath[MAX_PATH] = { 0 };
    if (GetModuleFileNameW(nullptr, modulePath, MAX_PATH) > 0) {
        std::wstring path(modulePath);
        size_t lastSlash = path.find_last_of(L"\\/");
        if (lastSlash != std::wstring::npos) {
            std::wstring origPath = path.substr(0, lastSlash + 1) + L"FFXiMain.dll.orig";
            hOriginalDll = LoadLibraryW(origPath.c_str());
            if (hOriginalDll) {
                Real_DoHardwareCheck = (LPFN_DoHardwareCheck)GetProcAddress(hOriginalDll, "DoPlayOnlineHardwareCheck");
                Real_InitializeInstance = (LPFN_InitializeInstance)GetProcAddress(hOriginalDll, "InitializeGameInstance");
            }
        }
    }
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    if (ul_reason_for_call == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(hModule);
        InterceptAndLinkOriginalDll();
    } else if (ul_reason_for_call == DLL_PROCESS_DETACH) {
        if (hOriginalDll) FreeLibrary(hOriginalDll);
    }
    return TRUE;
}
