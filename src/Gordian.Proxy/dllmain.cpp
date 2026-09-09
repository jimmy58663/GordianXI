// dllmain.cpp
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <string>
#include <sstream>
#include <iostream>

// Define function pointer signatures matching the genuine FFXI module exports
typedef void(__stdcall* LPFN_DoHardwareCheck)();
typedef int(__stdcall* LPFN_InitializeInstance)(void* lpParams);

LPFN_DoHardwareCheck Real_DoHardwareCheck = nullptr;
LPFN_InitializeInstance Real_InitializeInstance = nullptr;
HMODULE hOriginalDll = nullptr;

// REVERSE-ENGINEERED STRUCT MAPPING
// This memory structure maps exactly to the data layout xiloader emits
// right inside its core authentication handoff function.
struct FfxiHandoffParams {
    uint32_t Size;                // Size indicator of this parameter block
    uint32_t CharacterId;         // The unique database index tracking the chosen character
    char CharacterName[24];       // Null-terminated string containing the character name
    char ServerIp[16];            // Null-terminated string containing the Game World target IP
    uint16_t ServerPort;          // Destination connection port (usually 54231)
    uint32_t SessionKeyLength;    // Sizing metric for the secure login seed token array
    uint8_t SessionKey[128];      // The raw cryptographically secure binary token array
};

std::wstring GetFFXiMainPathFromRegistry() {
    std::wstring subKey = L"SOFTWARE\\PlayOnlineUS\\InstallFolder";
    HKEY hKey = nullptr;
    wchar_t pathBuffer[MAX_PATH] = { 0 };
    DWORD bufferSize = sizeof(pathBuffer);

    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, subKey.c_str(), 0, KEY_READ, &hKey) == ERROR_SUCCESS) {
        if (RegQueryValueExW(hKey, L"0001", nullptr, nullptr, (LPBYTE)pathBuffer, &bufferSize) == ERROR_SUCCESS) {
            std::wstring baseDir(pathBuffer);
            RegCloseKey(hKey);
            if (!baseDir.empty() && baseDir.back() != L'\\') {
                baseDir += L"\\";
            }
            return baseDir + L"FFXiMain.dll";
        }
        RegCloseKey(hKey);
    }
    return L"C:\\Program Files (x86)\\PlayOnline\\SquareEnix\\FINAL FANTASY XI\\FFXiMain.dll";
}

void InterceptAndLinkOriginalDll() {
    std::wstring absolutePathToOriginal = GetFFXiMainPathFromRegistry();
    hOriginalDll = LoadLibraryW(absolutePathToOriginal.c_str());
    if (!hOriginalDll) return;

    Real_DoHardwareCheck = (LPFN_DoHardwareCheck)GetProcAddress(hOriginalDll, "DoPlayOnlineHardwareCheck");
    Real_InitializeInstance = (LPFN_InitializeInstance)GetProcAddress(hOriginalDll, "InitializeGameInstance");
}

// Convert binary arrays to a Base64 string so .NET 10 can safely ingest it via JSON
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

// Helper to escape potentially broken text layouts into standardized JSON formatting strings
std::string EscapeJsonString(const std::string& input) {
    std::ostringstream ss;
    for (char c : input) {
        if (c == '\\' || c == '"') ss << '\\' << c;
        else if (c == '\b') ss << "\\b";
        else if (c == '\f') ss << "\\f";
        else if (c == '\n') ss << "\\n";
        else if (c == '\r') ss << "\\r";
        else if (c == '\t') ss << "\\t";
        else if (c < 32) ss << ""; // Strip hidden symbols
        else ss << c;
    }
    return ss.str();
}

void ForwardSessionParametersToGordianCore(FfxiHandoffParams* params) {
    if (!params) return;

    // 1. Extract and convert parameters from structural memory blocks safely
    std::string charName(params->CharacterName);
    std::string serverIp(params->ServerIp);
    std::string b64Token = Base64Encode(params->SessionKey, params->SessionKeyLength);

    // 2. Compile an explicit, optimized JSON string matching our C# 'SessionHandoffArgs' mapping
    std::ostringstream jsonStream;
    jsonStream << "{"
               << "\"TargetCharacterName\":\"" << EscapeJsonString(charName) << "\","
               << "\"ServerIp\":\"" << EscapeJsonString(serverIp) << "\","
               << "\"ServerPort\":" << params->ServerPort << ","
               << "\"CharacterId\":" << params->CharacterId << ","
               << "\"Base64SessionToken\":\"" << b64Token << "\""
               << "}";
    std::string jsonPayload = jsonStream.str();

    // 3. Establish a fast connection to our C# App's local OS Named Pipe server channel
    HANDLE hPipe = CreateFileW(L"\\\\.\\pipe\\GordianXI_Handoff", GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, 0, nullptr);
    if (hPipe != INVALID_HANDLE_VALUE) {
        DWORD bytesWritten = 0;
        // Transmit the JSON raw payload across the local process boundary wire instantly
        WriteFile(hPipe, jsonPayload.c_str(), (DWORD)jsonPayload.length(), &bytesWritten, nullptr);
        CloseHandle(hPipe);
    }

    // 4. THE KILL SWITCH: Instantly terminate the old bootloader process container.
    // This wipes xiloader off the screen completely, as control has safely shifted to GordianXI!
    ExitProcess(0);
}

extern "C" __declspec(dllexport) void __stdcall DoPlayOnlineHardwareCheck() {
    if (Real_DoHardwareCheck) Real_DoHardwareCheck();
}

extern "C" __declspec(dllexport) int __stdcall InitializeGameInstance(void* lpParams) {
    if (lpParams != nullptr) {
        // Safe typecast of the unmanaged context memory pointer straight to our defined schema layout
        FfxiHandoffParams* params = reinterpret_cast<FfxiHandoffParams*>(lpParams);
        
        // Intercept, pipe data to C#, and short-circuit the old client process
        ForwardSessionParametersToGordianCore(params);
    }

    // Fallback passthrough safety gate execution 
    if (Real_InitializeInstance) return Real_InitializeInstance(lpParams);
    return -1;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved) {
    if (ul_reason_for_call == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(hModule);
        InterceptAndLinkOriginalDll();
    }
    else if (ul_reason_for_call == DLL_PROCESS_DETACH) {
        if (hOriginalDll) FreeLibrary(hOriginalDll);
    }
    return TRUE;
}
