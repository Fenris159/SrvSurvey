#pragma once

#include "CameraProtocol.h"

namespace srvsurvey::camera
{
constexpr uint32_t CockpitRecordStride = 336;
constexpr uint32_t MaximumCockpitBytes = 8 * 1024 * 1024;
constexpr uint32_t CameraConstantBytes = 336 * 16;
constexpr size_t MaximumNearRecords = 256;

struct RigidReference
{
    Matrix projection{};
    uint32_t recordIndex = 0, members = 0, runnerUp = 0, nearRecords = 0;
};

namespace detail
{
using Point = std::array<double, 3>;
using Quaternion = std::array<double, 4>;
struct Candidate
{
    Point position;
    Quaternion rotation;
    uint32_t index;
};

inline float ReadFloat(std::span<const uint8_t> bytes, size_t offset)
{
    uint32_t bits = 0;
    for (size_t byte = 0; byte < 4; ++byte)
        bits |= uint32_t(bytes[offset + byte]) << (byte * 8);
    return std::bit_cast<float>(bits);
}

inline double ReadScalar(std::span<const uint8_t> bytes, size_t offset, uint32_t format)
{
    if (format == 1)
        return ReadFloat(bytes, offset);
    uint64_t bits = 0;
    for (size_t byte = 0; byte < 8; ++byte)
        bits |= uint64_t(bytes[offset + byte]) << (byte * 8);
    return std::bit_cast<double>(bits);
}

inline double ReadRotation(std::span<const uint8_t> bytes, size_t offset, uint32_t format)
{
    if (format >= 3)
        return ReadScalar(bytes, offset, format - 2);
    uint16_t packed = uint16_t(bytes[offset]) | uint16_t(bytes[offset + 1]) << 8;
    return format == 1 ? packed / 32767.5 - 1 : std::max(-1.0, std::bit_cast<int16_t>(packed) / 32767.0);
}

inline double SquaredDistance(const Point &a, const Point &b)
{
    double result = 0;
    for (size_t axis = 0; axis < 3; ++axis)
        result += (a[axis] - b[axis]) * (a[axis] - b[axis]);
    return result;
}

inline double Dot(const Quaternion &a, const Quaternion &b)
{
    double result = 0;
    for (size_t axis = 0; axis < 4; ++axis)
        result += a[axis] * b[axis];
    return result;
}

inline bool SameRoot(const Candidate &a, const Candidate &b, const CaptureLayout &layout)
{
    // abs(dot) makes q and -q the same rotation.
    double cosine = std::cos(layout.angleToleranceDegrees * 3.141592653589793 / 360);
    return SquaredDistance(a.position, b.position) < layout.positionTolerance * layout.positionTolerance &&
           std::abs(Dot(a.rotation, b.rotation)) >= cosine;
}

inline bool CameraIsUsable(std::span<const uint8_t> bytes, std::array<double, 16> &projection, Point &origin, const CaptureLayout &layout)
{
    if (bytes.size() != layout.cameraBytes)
        return false;
    for (size_t axis = 0; axis < 3; ++axis)
    {
        origin[axis] = ReadScalar(bytes, layout.cameraPositionOffset + axis * CaptureLayout::ScalarBytes(layout.cameraPositionFormat),
                                  layout.cameraPositionFormat) * layout.cameraPositionScale;
        if (!std::isfinite(origin[axis]) || std::abs(origin[axis]) > 1e9)
            return false;
    }
    for (size_t row = 0; row < 4; ++row)
        for (size_t column = 0; column < 4; ++column)
        {
            size_t sourceIndex = layout.projectionTranspose ? column * 4 + row : row * 4 + column;
            double value = ReadScalar(bytes, layout.projectionOffset + sourceIndex * CaptureLayout::ScalarBytes(layout.projectionFormat),
                                      layout.projectionFormat);
            if (!std::isfinite(value) || std::abs(value) > 1e9)
                return false;
            projection[row * 4 + column] = value;
        }
    if (!layout.projectionIncludesOrigin && (std::abs(projection[12]) > layout.cameraTolerance ||
        std::abs(projection[13]) > layout.cameraTolerance || std::abs(projection[15]) > layout.cameraTolerance))
        return false;
    double focal[2]{};
    for (size_t axis = 0; axis < 3; ++axis)
        for (size_t column = 0; column < 2; ++column)
            focal[column] += double(projection[axis * 4 + column]) * projection[axis * 4 + column];
    for (double &value : focal)
    {
        value = std::sqrt(value);
        if (value < 0.1 || value > 20)
            return false;
    }
    double axes[3][3]{};
    for (size_t row = 0; row < 3; ++row)
        for (size_t column = 0; column < 3; ++column)
        {
            size_t offset = layout.axesOffset + (layout.axesTranspose ? column : row) * layout.axesStride +
                            (layout.axesTranspose ? row : column) * CaptureLayout::ScalarBytes(layout.axesFormat);
            axes[row][column] = layout.axesSource == 1
                ? projection[column * 4 + (row < 2 ? row : 3)] / (row < 2 ? focal[row] : 1)
                : ReadScalar(bytes, offset, layout.axesFormat);
            if (!std::isfinite(axes[row][column]))
                return false;
        }
    for (size_t row = 0; row < 3; ++row)
        for (size_t column = 0; column < 3; ++column)
        {
            double dot = 0;
            for (size_t axis = 0; axis < 3; ++axis)
                dot += axes[row][axis] * axes[column][axis];
            if (std::abs(dot - (row == column ? 1.0 : 0.0)) > layout.cameraTolerance)
                return false;
        }
    const double determinant =
        axes[0][0] * (axes[1][1] * axes[2][2] - axes[1][2] * axes[2][1]) -
        axes[0][1] * (axes[1][0] * axes[2][2] - axes[1][2] * axes[2][0]) +
        axes[0][2] * (axes[1][0] * axes[2][1] - axes[1][1] * axes[2][0]);
    if (std::abs(determinant - 1) > layout.cameraTolerance)
        return false;
    for (size_t axis = 0; axis < 3; ++axis)
        if (std::abs(projection[axis * 4] - axes[0][axis] * focal[0]) > layout.cameraTolerance ||
            std::abs(projection[axis * 4 + 1] - axes[1][axis] * focal[1]) > layout.cameraTolerance ||
            std::abs(projection[axis * 4 + 3] - axes[2][axis]) > layout.cameraTolerance)
            return false;
    // Clip depth does not affect screen coordinates. Emit one canonical reversed
    // depth convention so source depth changes do not require an app rebuild.
    for (size_t axis = 0; axis < 3; ++axis)
        projection[axis * 4 + 2] = 0;
    projection[14] = layout.canonicalNearDepth;
    if (layout.projectionIncludesOrigin)
        for (size_t column : {size_t(0), size_t(1), size_t(3)})
            projection[12 + column] *= layout.cameraPositionScale;
    return true;
}
} // namespace detail

// Decode a bounded declarative rigid-instance layout and find one uniquely
// dominant nearby position+orientation group. Instance indices are transient.
inline std::optional<RigidReference> FindCockpitReference(
    std::span<const uint8_t> structured, std::span<const uint8_t> constants, const CaptureLayout &layout = {})
{
    using namespace detail;
    if (!layout.IsUsable() || structured.size() > layout.maximumBufferBytes || structured.size() <= layout.recordDataOffset ||
        (structured.size() - layout.recordDataOffset) % layout.recordStride != 0)
        return std::nullopt;
    std::array<double, 16> cameraProjection{};
    Point camera{};
    if (!CameraIsUsable(constants, cameraProjection, camera, layout))
        return std::nullopt;
    std::array<Candidate, MaximumNearRecords> candidates{};
    size_t count = 0;
    for (size_t offset = layout.recordDataOffset; offset < structured.size(); offset += layout.recordStride)
    {
        Candidate candidate{};
        bool finite = true;
        for (size_t axis = 0; axis < 3; ++axis)
        {
            candidate.position[axis] = ReadScalar(structured, offset + layout.positionOffset + axis * CaptureLayout::ScalarBytes(layout.positionFormat),
                                                  layout.positionFormat) * layout.positionScale;
            finite &= std::isfinite(candidate.position[axis]);
        }
        if (!finite || SquaredDistance(candidate.position, camera) >= layout.nearRadius * layout.nearRadius)
            continue;
        double norm = 0;
        for (size_t axis = 0; axis < 4; ++axis)
        {
            size_t sourceAxis = layout.rotationOrder == 1 ? (axis + 1) % 4 : axis;
            double value = ReadRotation(structured, offset + layout.rotationOffset + sourceAxis * (layout.RotationBytes() / 4),
                                        layout.rotationFormat);
            if (layout.rotationConjugate && axis < 3)
                value = -value;
            candidate.rotation[axis] = value;
            norm += value * value;
        }
        norm = std::sqrt(norm);
        if (!std::isfinite(norm) || std::abs(norm - 1) >= layout.quaternionTolerance)
            continue;
        for (double &value : candidate.rotation)
            value /= norm;
        candidate.index = static_cast<uint32_t>((offset - layout.recordDataOffset) / layout.recordStride);
        if (count == layout.maximumNearRecords)
            return std::nullopt;
        candidates[count++] = candidate;
    }
    if (count < layout.minimumMembers)
        return std::nullopt;
    size_t seed = 0, support = 0;
    for (size_t first = 0; first < count; ++first)
    {
        size_t members = 0;
        for (size_t second = 0; second < count; ++second)
            members += SameRoot(candidates[first], candidates[second], layout);
        if (members > support)
        {
            seed = first;
            support = members;
        }
    }
    std::array<bool, MaximumNearRecords> selected{};
    for (size_t index = 0; index < count; ++index)
        selected[index] = SameRoot(candidates[seed], candidates[index], layout);
    size_t runnerUp = 0;
    for (size_t first = 0; first < count; ++first)
    {
        if (selected[first])
            continue;
        size_t members = 0;
        for (size_t second = 0; second < count; ++second)
            if (!selected[second])
                members += SameRoot(candidates[first], candidates[second], layout);
        runnerUp = std::max(runnerUp, members);
    }
    if (support < layout.minimumMembers || support < layout.dominanceRatio * runnerUp)
        return std::nullopt;
    // Sharing a seed is not transitive: opposite sides of its tolerance can
    // represent different rigid roots. Every selected pair must agree.
    for (size_t first = 0; first < count; ++first)
        if (selected[first])
            for (size_t second = first + 1; second < count; ++second)
                if (selected[second] && !SameRoot(candidates[first], candidates[second], layout))
                    return std::nullopt;
    Candidate root{};
    root.index = candidates[seed].index;
    for (size_t index = 0; index < count; ++index)
    {
        if (!selected[index])
            continue;
        double sign = Dot(candidates[index].rotation, candidates[seed].rotation) < 0 ? -1 : 1;
        for (size_t axis = 0; axis < 3; ++axis)
            root.position[axis] += candidates[index].position[axis] / support;
        for (size_t axis = 0; axis < 4; ++axis)
            root.rotation[axis] += candidates[index].rotation[axis] * sign;
    }
    double norm = std::sqrt(Dot(root.rotation, root.rotation));
    if (!std::isfinite(norm) || norm <= 0)
        return std::nullopt;
    for (double &value : root.rotation)
        value /= norm;
    // A chain of individually near neighbours must not expand the accepted
    // rigid group beyond the actual position/orientation tolerance.
    for (size_t index = 0; index < count; ++index)
        if (selected[index] && !SameRoot(candidates[index], root, layout))
            return std::nullopt;
    if (layout.projectionIncludesOrigin)
        camera = {};
    const auto &[x, y, z, w] = root.rotation;
    const double transform[4][4]{
        {1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w), root.position[0] - camera[0]},
        {2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w), root.position[1] - camera[1]},
        {2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y), root.position[2] - camera[2]},
        {0, 0, 0, 1}};
    RigidReference result{};
    result.recordIndex = root.index;
    result.members = static_cast<uint32_t>(support);
    result.runnerUp = static_cast<uint32_t>(runnerUp);
    result.nearRecords = static_cast<uint32_t>(count);
    for (size_t row = 0; row < 4; ++row)
        for (size_t column = 0; column < 4; ++column)
        {
            double value = 0;
            for (size_t inner = 0; inner < 4; ++inner)
                value += double(cameraProjection[inner * 4 + row]) * transform[inner][column];
            result.projection[row * 4 + column] = static_cast<float>(value);
        }
    if (!IsUsableProjection(result.projection))
        return std::nullopt;
    return result;
}
} // namespace srvsurvey::camera
