#pragma once
#include <cmath>
#include <cstdint>
#include <cstring>
#include <span>

namespace srvsurvey
{
constexpr uint32_t Magic = 0x58565253;
constexpr uint32_t Version = 1;
constexpr uint32_t Capacity = 64 * 1024 * 1024;
constexpr uint32_t MaximumOverlays = 16;
struct Descriptor
{
    uint64_t id;
    uint32_t width, height, offset, length;
    float widthMeters, heightMeters;
    float position[3], orientation[4], alpha;
};
static_assert(sizeof(Descriptor) == 64);
struct Header
{
    uint32_t magic, version, sequence, length;
    int64_t timestamp;
    uint32_t count, originRevision;
    int64_t layerHeartbeat;
    uint32_t layerState, reserved, renderedCount;
    int32_t lastError;
    uint32_t processId;
    uint8_t padding[68];
};
static_assert(sizeof(Header) == 128);
static_assert(offsetof(Header, layerHeartbeat) == 32);

inline bool ValidatePacket(std::span<const uint8_t> bytes, int64_t now)
{
    if (bytes.size() < sizeof(Header) || bytes.size() > Capacity)
        return false;
    Header header{};
    std::memcpy(&header, bytes.data(), sizeof(header));
    if (header.magic != Magic || header.version != Version || (header.sequence & 1) || header.length != bytes.size() ||
        header.count > MaximumOverlays || header.timestamp <= 0 || now < header.timestamp ||
        now - header.timestamp > 2000)
        return false;
    uint64_t pixelStart = sizeof(Header) + header.count * sizeof(Descriptor);
    if (pixelStart > bytes.size())
        return false;
    for (uint32_t i = 0; i < header.count; ++i)
    {
        Descriptor d{};
        std::memcpy(&d, bytes.data() + sizeof(Header) + i * sizeof(Descriptor), sizeof(d));
        if (!d.width || !d.height || d.width > 4096 || d.height > 4096 ||
            uint64_t(d.width) * d.height * 4 != d.length || d.offset < pixelStart ||
            uint64_t(d.offset) + d.length > bytes.size() || !std::isfinite(d.widthMeters) ||
            !std::isfinite(d.heightMeters) || d.widthMeters <= 0 || d.heightMeters <= 0 || !std::isfinite(d.alpha) ||
            d.alpha < 0 || d.alpha > 1)
            return false;
        for (float value : d.position)
            if (!std::isfinite(value))
                return false;
        float norm = 0;
        for (float value : d.orientation)
        {
            if (!std::isfinite(value))
                return false;
            norm += value * value;
        }
        if (std::abs(norm - 1) > 0.01f)
            return false;
        for (uint32_t j = 0; j < i; ++j)
        {
            Descriptor previous{};
            std::memcpy(&previous, bytes.data() + sizeof(Header) + j * sizeof(Descriptor), sizeof(previous));
            if (previous.id == d.id)
                return false;
        }
    }
    return true;
}
} // namespace srvsurvey
