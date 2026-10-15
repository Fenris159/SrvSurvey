#pragma once
#include "PointerInteraction.h"
#include <atomic>
#include <openxr/openxr.h>
#include <shlobj.h>
#include <string>
#include <windows.h>

class InputMailbox
{
    HANDLE file = INVALID_HANDLE_VALUE, mapping = nullptr;
    srvsurvey::InputHeader *data = nullptr;
    std::wstring path;
    uint64_t generation = 0;
    int64_t nextAttempt = 0;

  public:
    explicit InputMailbox(std::wstring customPath = {}) : path(std::move(customPath))
    {
    }
    ~InputMailbox()
    {
        if (data)
            UnmapViewOfFile(data);
        if (mapping)
            CloseHandle(mapping);
        if (file != INVALID_HANDLE_VALUE)
            CloseHandle(file);
    }
    bool Open(int64_t now)
    {
        if (data)
            return true;
        if (now < nextAttempt)
            return false;
        nextAttempt = now + 1000;
        if (path.empty())
        {
            PWSTR folder = nullptr;
            if (FAILED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &folder)))
                return false;
            path = folder;
            CoTaskMemFree(folder);
            path += L"\\SrvSurvey\\OpenXR\\frames.bin.input";
        }
        file = CreateFileW(path.c_str(), GENERIC_READ | GENERIC_WRITE,
                           FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, 0, nullptr);
        if (file == INVALID_HANDLE_VALUE)
            return false;
        LARGE_INTEGER length{};
        if (!GetFileSizeEx(file, &length) || length.QuadPart != srvsurvey::InputCapacity)
        {
            CloseHandle(file);
            file = INVALID_HANDLE_VALUE;
            return false;
        }
        mapping = CreateFileMappingW(file, nullptr, PAGE_READWRITE, 0, 0, nullptr);
        if (mapping)
            data = static_cast<srvsurvey::InputHeader *>(
                MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, srvsurvey::InputCapacity));
        if (!data)
        {
            if (mapping)
                CloseHandle(mapping);
            mapping = nullptr;
            CloseHandle(file);
            file = INVALID_HANDLE_VALUE;
        }
        if (data && (data->eventSequence & 1u))
            InterlockedIncrement(reinterpret_cast<volatile LONG *>(&data->eventSequence));
        return data != nullptr;
    }
    bool Requested(int64_t now, bool &newPublisher)
    {
        newPublisher = false;
        if (!Open(now))
            return false;
        for (int attempt = 0; attempt < 3; ++attempt)
        {
            auto before = InterlockedCompareExchange(reinterpret_cast<volatile LONG *>(&data->controlSequence), 0, 0);
            if (before & 1)
                continue;
            uint32_t magic = data->magic, version = data->version, enabled = data->enabled;
            int64_t timestamp = data->controlTime;
            uint64_t token = data->generation;
            MemoryBarrier();
            if (before != InterlockedCompareExchange(reinterpret_cast<volatile LONG *>(&data->controlSequence), 0, 0))
                continue;
            if (magic != srvsurvey::InputMagic || version != srvsurvey::InputVersion || timestamp <= 0 ||
                now < timestamp || now - timestamp > 3000)
                return false;
            newPublisher = generation != token;
            generation = token;
            return enabled != 0;
        }
        return false;
    }
    void Report(int64_t now, uint32_t state, XrResult error = XR_SUCCESS)
    {
        if (!Open(now))
            return;
        data->state = state;
        data->error = error;
        data->acknowledgedGeneration = generation;
        MemoryBarrier();
        InterlockedExchange64(reinterpret_cast<volatile LONG64 *>(&data->heartbeat), now);
    }
    void Write(std::span<const srvsurvey::PointerEvent> events)
    {
        if (!data || events.empty())
            return;
        auto *sequence = reinterpret_cast<volatile LONG *>(&data->eventSequence);
        InterlockedIncrement(sequence);
        uint32_t count = data->eventCount;
        for (const auto &event : events)
            data->events[count++ % srvsurvey::InputEventCapacity] = event;
        data->eventCount = count;
        MemoryBarrier();
        InterlockedIncrement(sequence);
    }
};
