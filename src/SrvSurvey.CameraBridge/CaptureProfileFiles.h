#pragma once

#include "CameraProtocol.h"
#include <windows.h>
#include <cwchar>
#include <string>
#include <unordered_set>
#include <memory>

namespace srvsurvey::camera
{
namespace detail
{
struct CloseHandleDeleter { void operator()(void *handle) const { if (handle) CloseHandle(handle); } };
using Handle = std::unique_ptr<void, CloseHandleDeleter>;

inline CaptureProfile ReadCaptureProfileFile(const wchar_t *path)
{
    HANDLE opened = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (opened == INVALID_HANDLE_VALUE)
        return {};
    Handle source(opened);
    LARGE_INTEGER size{};
    if (!GetFileSizeEx(source.get(), &size) || size.QuadPart < 1 || size.QuadPart > 8192)
        return {};
    // The read-only sharing lease prevents replacement while the INI APIs
    // collect the fields, giving selection one coherent source snapshot.
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
inline void AppendField(std::string &text, const char *name, uint64_t value)
{
    text += name;
    text += '=';
    text += std::to_string(value);
    text += '\n';
}

inline std::string SerializeProfile(const CaptureProfile &profile)
{
    std::string text = "[Capture]\n";
    AppendField(text, "ProfileVersion", profile.profileVersion);
    AppendField(text, "ProtocolVersion", profile.protocolVersion);
    AppendField(text, "Validated", profile.validated ? 1 : 0);
    char digits[16]{};
    auto hash = std::to_chars(digits, digits + 16, profile.shaderHash, 16);
    text += "VertexShaderHash=" + std::string(16 - static_cast<size_t>(hash.ptr - digits), '0');
    text.append(digits, hash.ptr);
    text += '\n';
    AppendField(text, "RigidLayoutVersion", profile.layoutVersion);
    AppendField(text, "DrawKind", profile.drawKind);
    if (profile.layoutVersion == 2)
    {
        text += "\n[Layout]\n";
        for (const auto &field : LayoutIntegers)
            AppendField(text, field.name, profile.layout.*field.member);
        for (const auto &field : LayoutReals)
        {
            char value[128]{};
            auto formatted = std::to_chars(value, value + sizeof(value), profile.layout.*field.member);
            text += std::string(field.name) + '=';
            text.append(value, formatted.ptr);
            text += '\n';
        }
        for (const auto &field : LayoutChoices)
            if (profile.layout.*field.member == field.number)
                text += std::string(field.key) + '=' + field.value + '\n';
    }
    return text;
}
} // namespace detail

// Persists a complete selected value, independently of mutable source paths.
// A filesystem sharing lock coordinates readers across processes; the revision
// floor is rechecked under that lock before the atomic replacement.
inline CaptureProfile RetainSupportedProfile(const std::wstring &destination, const CaptureProfile &selected)
{
    if (!selected.IsUsable())
        return {};
    HANDLE opened = INVALID_HANDLE_VALUE;
    for (int attempt = 0; attempt < 100; ++attempt)
    {
        opened = CreateFileW((destination + L".lock").c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr,
                             OPEN_ALWAYS, FILE_ATTRIBUTE_TEMPORARY | FILE_FLAG_DELETE_ON_CLOSE, nullptr);
        if (opened != INVALID_HANDLE_VALUE)
            break;
        DWORD error = GetLastError();
        if (error != ERROR_SHARING_VIOLATION && error != ERROR_LOCK_VIOLATION)
            break;
        Sleep(10);
    }
    if (opened == INVALID_HANDLE_VALUE)
        return detail::ReadCaptureProfileFile(destination.c_str());
    detail::Handle lock(opened);
    CaptureProfile existing = detail::ReadCaptureProfileFile(destination.c_str());
    if (existing.IsUsable() && existing.profileVersion >= selected.profileVersion)
        return existing;
    std::wstring temporary = destination + L"." + std::to_wstring(GetCurrentProcessId()) + L"." +
                             std::to_wstring(GetTickCount64()) + L".tmp";
    std::string bytes = detail::SerializeProfile(selected);
    HANDLE file = CreateFileW(temporary.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
    bool written = false;
    if (file != INVALID_HANDLE_VALUE)
    {
        detail::Handle target(file);
        DWORD count = 0;
        written = bytes.size() <= 8192 && WriteFile(target.get(), bytes.data(), static_cast<DWORD>(bytes.size()), &count, nullptr)
                  && count == bytes.size() && FlushFileBuffers(target.get());
    }
    CaptureProfile copied = written ? detail::ReadCaptureProfileFile(temporary.c_str()) : CaptureProfile{};
    bool replaced = copied.IsUsable() && copied == selected &&
                    MoveFileExW(temporary.c_str(), destination.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH);
    (void)DeleteFileW(temporary.c_str());
    return replaced ? selected : existing;
}

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
    std::wstring cache(cachedPath);
    std::wstring readerPath = cache + L".reader-" + std::to_wstring(BridgeReaderVersion);
    CaptureProfile selected = bundled;
    for (const std::wstring &path : {cache + L".previous", readerPath, cache})
    {
        CaptureProfile candidate = detail::ReadCaptureProfileFile(path.c_str());
        if (candidate.IsUsable() && candidate.profileVersion > selected.profileVersion)
        {
            selected = candidate;
        }
    }
    return ChooseCaptureProfile(bundled, RetainSupportedProfile(readerPath, selected));
}
} // namespace srvsurvey::camera
