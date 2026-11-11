#pragma once

#include "CameraProtocol.h"
#include <windows.h>
#include <cwchar>
#include <string>
#include <unordered_set>

namespace srvsurvey::camera
{
namespace detail
{
inline CaptureProfile ReadCaptureProfileFile(const wchar_t *path)
{
    WIN32_FILE_ATTRIBUTE_DATA attributes{};
    if (!GetFileAttributesExW(path, GetFileExInfoStandard, &attributes) ||
        attributes.nFileSizeHigh != 0 || attributes.nFileSizeLow > 8192)
        return {};
    auto number = [&](const wchar_t *key, int fallback)
    { return GetPrivateProfileIntW(L"Capture", key, fallback, path); };
    wchar_t hashText[32]{};
    GetPrivateProfileStringW(L"Capture", L"VertexShaderHash", L"0", hashText, 32, path);
    wchar_t *end = nullptr;
    uint64_t hash = std::wcstoull(hashText, &end, 16);
    if (!end || *end != 0)
        return {};
    CaptureProfile profile;
    profile.validated = number(L"Validated", 0) == 1;
    profile.shaderHash = hash;
    profile.layoutVersion = number(L"RigidLayoutVersion", 0);
    profile.drawKind = number(L"DrawKind", 0);
    profile.profileVersion = number(L"ProfileVersion", 1);
    profile.protocolVersion = number(L"ProtocolVersion", 1);
    wchar_t fields[8192]{};
    DWORD length = GetPrivateProfileSectionW(L"Layout", fields, 8192, path);
    if (length >= 8190 || (profile.layoutVersion == 1 && length != 0))
        return {};
    if (profile.layoutVersion == 2)
    {
        std::unordered_set<std::string> seen;
        for (const wchar_t *field = fields; *field; field += std::wcslen(field) + 1)
        {
            std::string entry;
            for (const wchar_t *character = field; *character; ++character)
            {
                if (*character < 32 || *character > 126)
                    return {};
                entry += static_cast<char>(*character);
            }
            size_t separator = entry.find('=');
            if (separator == std::string::npos)
                return {};
            std::string name = LayoutLower(entry.substr(0, separator));
            if (!seen.insert(name).second || !ApplyLayoutField(profile.layout, name, entry.substr(separator + 1)))
                return {};
        }
        profile.layoutDeclared = seen.contains("schemaversion") && seen.contains("requiredbridgeversion");
    }
    return profile;
}
} // namespace detail

// The installed files stay immutable. A newer compatible machine-local cache
// is read once when the game creates its graphics device.
inline CaptureProfile LoadInstalledCaptureProfile(const std::wstring &directory)
{
    CaptureProfile bundled = detail::ReadCaptureProfileFile((directory + L"\\SrvSurveyCameraBridge.ini").c_str());
    if (!bundled.IsUsable())
        return bundled;
    wchar_t cachedPath[32768]{};
    DWORD length = GetPrivateProfileStringW(L"Updates", L"CachedProfilePath", L"", cachedPath, 32768,
                                           (directory + L"\\SrvSurveyCameraBridge.paths.ini").c_str());
    if (length == 0 || length >= 32767)
        return bundled;
    CaptureProfile predecessor = detail::ReadCaptureProfileFile((std::wstring(cachedPath) + L".previous").c_str());
    return ChooseCaptureProfile(ChooseCaptureProfile(bundled, predecessor), detail::ReadCaptureProfileFile(cachedPath));
}
} // namespace srvsurvey::camera
