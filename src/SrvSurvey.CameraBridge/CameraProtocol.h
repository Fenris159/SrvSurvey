#pragma once

#include "CaptureLayout.h"

#include <algorithm>
#include <array>
#include <bit>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <optional>
#include <span>

namespace srvsurvey::camera
{
constexpr std::array<uint8_t, 8> Magic{'S', 'R', 'V', 'C', 'A', 'M', '0', '1'};
constexpr uint32_t Version = 1;
constexpr uint32_t VerifiedCockpitProjection = 1;
constexpr size_t RequestBytes = 40;
constexpr size_t PacketBytes = 136;
constexpr uint64_t ClientLeaseMilliseconds = 2000;
constexpr uint64_t MaximumReadbackAgeMilliseconds = 250;
using Nonce = std::array<uint8_t, 32>;
using Matrix = std::array<float, 16>;

// This is the zero-seeded FNV-1 used for 3DMigoto shader filenames. It is
// deliberately not FNV-1a or the more usual offset-basis-seeded FNV-1.
inline uint64_t ShaderHash(std::span<const uint8_t> bytes)
{
    uint64_t hash = 0;
    for (uint8_t byte : bytes)
        hash = hash * 0x100000001b3ULL ^ byte;
    return hash;
}

inline bool ReadRequest(std::span<const uint8_t> bytes, Nonce &nonce)
{
    if (bytes.size() != RequestBytes || !std::equal(Magic.begin(), Magic.end(), bytes.begin()))
        return false;
    std::copy_n(bytes.begin() + Magic.size(), nonce.size(), nonce.begin());
    return std::any_of(nonce.begin(), nonce.end(), [](uint8_t value) { return value != 0; });
}

// The rigid cockpit source is a full 3D projection. Its local z=0 plane can
// legitimately pass through the camera, so validate the full transform rather
// than demanding that this one arbitrary plane have an invertible homography.
inline bool IsUsableProjection(const Matrix &matrix)
{
    double work[4][4]{};
    for (float value : matrix)
        if (!std::isfinite(value) || std::abs(value) > 1.0e9f)
            return false;
    for (size_t row = 0; row < 4; ++row)
        for (size_t column = 0; column < 4; ++column)
            work[row][column] = matrix[row * 4 + column];
    double determinant = 1;
    for (size_t column = 0; column < 4; ++column)
    {
        size_t pivot = column;
        for (size_t row = column + 1; row < 4; ++row)
            if (std::abs(work[row][column]) > std::abs(work[pivot][column]))
                pivot = row;
        if (std::abs(work[pivot][column]) <= 1.0e-12)
            return false;
        if (pivot != column)
        {
            for (size_t axis = 0; axis < 4; ++axis)
                std::swap(work[pivot][axis], work[column][axis]);
            determinant = -determinant;
        }
        determinant *= work[column][column];
        for (size_t row = column + 1; row < 4; ++row)
        {
            double factor = work[row][column] / work[column][column];
            for (size_t axis = column; axis < 4; ++axis)
                work[row][axis] -= factor * work[column][axis];
        }
    }
    return std::isfinite(determinant) && std::abs(determinant) > 1.0e-12;
}

struct CompletedSample
{
    Matrix matrix{};
    uint64_t submission = 0, generation = 0, submittedAt = 0;
    int64_t timestamp = 0;
    uint32_t recordIndex = 0;
};

// Ring slots are reused independently, so slot order is not capture order.
// A delayed older readback must never rewind an already published camera pose.
inline std::optional<size_t> NewestCompletedSample(
    std::span<const CompletedSample> samples, uint64_t generation, uint64_t now, uint64_t publishedSubmission)
{
    std::optional<size_t> newest;
    for (size_t index = 0; index < samples.size(); ++index)
    {
        const auto &sample = samples[index];
        if (sample.submission <= publishedSubmission || sample.generation != generation || now < sample.submittedAt ||
            now - sample.submittedAt > MaximumReadbackAgeMilliseconds || !IsUsableProjection(sample.matrix))
            continue;
        if (!newest || sample.submission > samples[*newest].submission)
            newest = index;
    }
    return newest;
}

struct DrawSignature
{
    // 1=DrawIndexed, 2=Draw, 3=DrawIndexedInstanced, 4=DrawInstanced.
    uint32_t kind = 0, vertices = 0, start = 0, instances = 1, firstInstance = 0;
    int32_t baseVertex = 0;
    bool operator==(const DrawSignature &) const = default;
};

struct CaptureProfile
{
    uint64_t shaderHash = 0;
    uint32_t layoutVersion = 0, drawKind = 3;
    bool validated = false;
    uint32_t profileVersion = 1, protocolVersion = 1;
    bool layoutDeclared = false;
    CaptureLayout layout{};
    bool operator==(const CaptureProfile &) const = default;

    bool IsUsable() const
    {
        return validated && shaderHash != 0 && (layoutVersion == 1 || (layoutVersion == 2 && layoutDeclared)) &&
               layout.IsUsable() && drawKind >= 1 && drawKind <= 4 &&
               profileVersion > 0 && profileVersion <= 0x7fffffffU && protocolVersion == 1;
    }
};

inline CaptureProfile ChooseCaptureProfile(const CaptureProfile &bundled, const CaptureProfile &cached)
{
    return bundled.IsUsable() && cached.IsUsable() && cached.profileVersion > bundled.profileVersion ? cached : bundled;
}

struct ClientLease
{
    Nonce nonce{};
    uint64_t renewedAt = 0, generation = 0;
    bool hasClient = false;

    bool Renew(const Nonce &next, uint64_t now)
    {
        bool changed = !hasClient || next != nonce;
        if (changed)
            ++generation;
        nonce = next;
        renewedAt = now;
        hasClient = true;
        return changed;
    }

    bool IsActive(uint64_t now) const
    {
        return hasClient && now >= renewedAt && now - renewedAt < ClientLeaseMilliseconds;
    }
};

inline void Write32(std::span<uint8_t> target, size_t offset, uint32_t value)
{
    for (size_t byte = 0; byte < 4; ++byte)
        target[offset + byte] = static_cast<uint8_t>(value >> (byte * 8));
}

inline void Write64(std::span<uint8_t> target, size_t offset, uint64_t value)
{
    for (size_t byte = 0; byte < 8; ++byte)
        target[offset + byte] = static_cast<uint8_t>(value >> (byte * 8));
}

inline std::array<uint8_t, PacketBytes> MakePacket(
    uint64_t sequence, int64_t unixMilliseconds, uint32_t matrixIndex, const Matrix &matrix, const Nonce &nonce)
{
    std::array<uint8_t, PacketBytes> packet{};
    std::copy(Magic.begin(), Magic.end(), packet.begin());
    Write32(packet, 8, Version);
    Write32(packet, 12, PacketBytes);
    Write64(packet, 16, sequence);
    Write64(packet, 24, static_cast<uint64_t>(unixMilliseconds));
    Write32(packet, 32, VerifiedCockpitProjection);
    Write32(packet, 36, matrixIndex);
    for (size_t index = 0; index < matrix.size(); ++index)
        Write32(packet, 40 + index * 4, std::bit_cast<uint32_t>(matrix[index]));
    std::copy(nonce.begin(), nonce.end(), packet.begin() + 104);
    return packet;
}
} // namespace srvsurvey::camera
