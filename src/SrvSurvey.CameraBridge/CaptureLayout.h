#pragma once

#include <algorithm>
#include <charconv>
#include <cmath>
#include <cstdint>
#include <string>
#include <string_view>
#include <optional>

namespace srvsurvey::camera
{
constexpr uint32_t BridgeReaderVersion = 2;
constexpr uint32_t HardMaximumBufferBytes = 8 * 1024 * 1024;
constexpr uint32_t HardMaximumCameraBytes = 65536;
constexpr uint32_t HardMaximumNearRecords = 256;

// All offsets are bytes. Stages: VS, PS, GS, HS, DS, CS. Scalar formats:
// 1=float32, 2=float64. Rotations: 1=unorm16, 2=snorm16, 3=float32, 4=float64.
struct CaptureLayout
{
    uint32_t schemaVersion = 1, requiredBridgeVersion = 2;
    uint32_t shaderStage = 0, recordStage = 0, recordSlot = 33, cameraStage = 0, cameraSlot = 1;
    uint32_t bufferKind = 0, bufferElementStride = 336, recordStride = 336, recordDataOffset = 0;
    uint32_t cameraBytes = 5376, maximumBufferBytes = HardMaximumBufferBytes;
    uint32_t positionOffset = 16, positionFormat = 1, rotationOffset = 8, rotationFormat = 1;
    uint32_t rotationOrder = 0, rotationConjugate = 0;
    uint32_t projectionOffset = 4320, projectionFormat = 1, projectionTranspose = 0, projectionIncludesOrigin = 0;
    uint32_t cameraPositionOffset = 4400, cameraPositionFormat = 1;
    uint32_t axesOffset = 4432, axesFormat = 1, axesStride = 16, axesTranspose = 0, axesSource = 0;
    uint32_t minimumMembers = 8, maximumNearRecords = 256;
    uint32_t minimumViewportWidth = 640, minimumViewportHeight = 480, viewportMatchesTarget = 1;
    double positionScale = 1, cameraPositionScale = 1, canonicalNearDepth = 0.025;
    double nearRadius = 3, positionTolerance = 0.001, angleToleranceDegrees = 0.25;
    double quaternionTolerance = 0.001, cameraTolerance = 0.001, dominanceRatio = 2;

    bool operator==(const CaptureLayout &) const = default;

    static uint32_t ScalarBytes(uint32_t format) { return format == 1 ? 4 : format == 2 ? 8 : 0; }
    uint32_t RotationBytes() const
    {
        return rotationFormat <= 2 ? 8 : rotationFormat == 3 ? 16 : rotationFormat == 4 ? 32 : 0;
    }
    static bool Fits(uint32_t offset, uint32_t bytes, uint32_t limit)
    {
        return bytes > 0 && offset <= limit && bytes <= limit - offset;
    }
    static bool Between(double value, double minimum, double maximum)
    {
        return std::isfinite(value) && value >= minimum && value <= maximum;
    }
    bool IsUsable() const
    {
        if (schemaVersion != 1 || requiredBridgeVersion < 1 || requiredBridgeVersion > BridgeReaderVersion ||
            shaderStage > 1 || recordStage > 5 || cameraStage > 5 || recordSlot >= 128 || cameraSlot >= 14 ||
            bufferKind > 1 || recordStride < 8 || recordStride > 2048 ||
            (bufferElementStride != 0 && (bufferElementStride > 2048 || bufferElementStride % 4 != 0)) ||
            maximumBufferBytes > HardMaximumBufferBytes || maximumBufferBytes < recordStride ||
            recordDataOffset >= maximumBufferBytes || cameraBytes < 16 || cameraBytes > HardMaximumCameraBytes ||
            cameraBytes % 16 != 0)
            return false;
        if (!Fits(positionOffset, 3 * ScalarBytes(positionFormat), recordStride) ||
            !Fits(rotationOffset, RotationBytes(), recordStride) || rotationFormat < 1 || rotationOrder > 1 ||
            rotationConjugate > 1 || !Fits(projectionOffset, 16 * ScalarBytes(projectionFormat), cameraBytes) ||
            !Fits(cameraPositionOffset, 3 * ScalarBytes(cameraPositionFormat), cameraBytes) ||
            projectionTranspose > 1 || projectionIncludesOrigin > 1 || axesSource > 1 || axesTranspose > 1)
            return false;
        if (axesSource == 0 && (ScalarBytes(axesFormat) == 0 || axesStride < 3 * ScalarBytes(axesFormat) || axesStride > cameraBytes ||
                               !Fits(axesOffset, 2 * axesStride + 3 * ScalarBytes(axesFormat), cameraBytes)))
            return false;
        if (minimumMembers < 3 || maximumNearRecords > HardMaximumNearRecords ||
            minimumMembers > maximumNearRecords || minimumViewportWidth < 64 || minimumViewportWidth > 16384 ||
            minimumViewportHeight < 64 || minimumViewportHeight > 16384 || viewportMatchesTarget > 1)
            return false;
        return Between(positionScale, 1e-9, 1e9) && Between(cameraPositionScale, 1e-9, 1e9) &&
               Between(canonicalNearDepth, 0.000001, 10) && Between(nearRadius, 0.01, 1000) &&
               Between(positionTolerance, 0.000001, 0.1) && Between(angleToleranceDegrees, 0.001, 5) &&
               Between(quaternionTolerance, 0.000001, 0.01) && Between(cameraTolerance, 0.000001, 0.01) &&
               Between(dominanceRatio, 2, 16);
    }
};

struct RecordViewRange
{
    uint32_t offset, bytes;
};

inline std::optional<RecordViewRange> GetRecordViewRange(
    const CaptureLayout &layout, uint32_t bufferBytes, uint32_t elementStride, uint32_t first, uint32_t count)
{
    if (!layout.IsUsable() || bufferBytes > layout.maximumBufferBytes || elementStride == 0 ||
        (layout.bufferElementStride != 0 && elementStride != layout.bufferElementStride) || count == 0)
        return std::nullopt;
    uint64_t offset = uint64_t(first) * elementStride, bytes = uint64_t(count) * elementStride;
    if (offset > bufferBytes || bytes > bufferBytes - offset || bytes <= layout.recordDataOffset ||
        (bytes - layout.recordDataOffset) % layout.recordStride != 0)
        return std::nullopt;
    return RecordViewRange{static_cast<uint32_t>(offset), static_cast<uint32_t>(bytes)};
}

inline std::string LayoutLower(std::string_view value)
{
    std::string result(value);
    for (char &character : result)
        if (character >= 'A' && character <= 'Z')
            character = static_cast<char>(character - 'A' + 'a');
    return result;
}

struct IntegerField { const char *name; uint32_t CaptureLayout::*member; };
inline constexpr IntegerField LayoutIntegers[]{
    {"schemaversion", &CaptureLayout::schemaVersion}, {"requiredbridgeversion", &CaptureLayout::requiredBridgeVersion},
    {"recordslot", &CaptureLayout::recordSlot}, {"cameraslot", &CaptureLayout::cameraSlot},
    {"bufferelementstride", &CaptureLayout::bufferElementStride}, {"recordstride", &CaptureLayout::recordStride},
    {"recorddataoffset", &CaptureLayout::recordDataOffset}, {"camerabytes", &CaptureLayout::cameraBytes},
    {"maximumbufferbytes", &CaptureLayout::maximumBufferBytes}, {"positionoffset", &CaptureLayout::positionOffset},
    {"rotationoffset", &CaptureLayout::rotationOffset}, {"rotationconjugate", &CaptureLayout::rotationConjugate},
    {"projectionoffset", &CaptureLayout::projectionOffset}, {"projectiontranspose", &CaptureLayout::projectionTranspose},
    {"projectionincludesorigin", &CaptureLayout::projectionIncludesOrigin},
    {"camerapositionoffset", &CaptureLayout::cameraPositionOffset}, {"axesoffset", &CaptureLayout::axesOffset},
    {"axesstride", &CaptureLayout::axesStride}, {"axestranspose", &CaptureLayout::axesTranspose},
    {"minimummembers", &CaptureLayout::minimumMembers}, {"maximumnearrecords", &CaptureLayout::maximumNearRecords},
    {"minimumviewportwidth", &CaptureLayout::minimumViewportWidth},
    {"minimumviewportheight", &CaptureLayout::minimumViewportHeight},
    {"viewportmatchestarget", &CaptureLayout::viewportMatchesTarget}};

struct RealField { const char *name; double CaptureLayout::*member; };
inline constexpr RealField LayoutReals[]{
    {"positionscale", &CaptureLayout::positionScale}, {"camerapositionscale", &CaptureLayout::cameraPositionScale},
    {"canonicalneardepth", &CaptureLayout::canonicalNearDepth}, {"nearradius", &CaptureLayout::nearRadius},
    {"positiontolerance", &CaptureLayout::positionTolerance}, {"angletolerancedegrees", &CaptureLayout::angleToleranceDegrees},
    {"quaterniontolerance", &CaptureLayout::quaternionTolerance}, {"cameratolerance", &CaptureLayout::cameraTolerance},
    {"dominanceratio", &CaptureLayout::dominanceRatio}};

struct Choice { const char *key; const char *value; uint32_t CaptureLayout::*member; uint32_t number; };
inline constexpr Choice LayoutChoices[]{
    {"shaderstage", "vs", &CaptureLayout::shaderStage, 0}, {"shaderstage", "ps", &CaptureLayout::shaderStage, 1},
    {"recordstage", "vs", &CaptureLayout::recordStage, 0}, {"recordstage", "ps", &CaptureLayout::recordStage, 1},
    {"recordstage", "gs", &CaptureLayout::recordStage, 2}, {"recordstage", "hs", &CaptureLayout::recordStage, 3},
    {"recordstage", "ds", &CaptureLayout::recordStage, 4}, {"recordstage", "cs", &CaptureLayout::recordStage, 5},
    {"camerastage", "vs", &CaptureLayout::cameraStage, 0}, {"camerastage", "ps", &CaptureLayout::cameraStage, 1},
    {"camerastage", "gs", &CaptureLayout::cameraStage, 2}, {"camerastage", "hs", &CaptureLayout::cameraStage, 3},
    {"camerastage", "ds", &CaptureLayout::cameraStage, 4}, {"camerastage", "cs", &CaptureLayout::cameraStage, 5},
    {"bufferkind", "structured", &CaptureLayout::bufferKind, 0}, {"bufferkind", "raw", &CaptureLayout::bufferKind, 1},
    {"positionformat", "float32", &CaptureLayout::positionFormat, 1}, {"positionformat", "float64", &CaptureLayout::positionFormat, 2},
    {"camerapositionformat", "float32", &CaptureLayout::cameraPositionFormat, 1}, {"camerapositionformat", "float64", &CaptureLayout::cameraPositionFormat, 2},
    {"projectionformat", "float32", &CaptureLayout::projectionFormat, 1}, {"projectionformat", "float64", &CaptureLayout::projectionFormat, 2},
    {"axesformat", "float32", &CaptureLayout::axesFormat, 1}, {"axesformat", "float64", &CaptureLayout::axesFormat, 2},
    {"rotationformat", "unorm16", &CaptureLayout::rotationFormat, 1}, {"rotationformat", "snorm16", &CaptureLayout::rotationFormat, 2},
    {"rotationformat", "float32", &CaptureLayout::rotationFormat, 3}, {"rotationformat", "float64", &CaptureLayout::rotationFormat, 4},
    {"rotationorder", "xyzw", &CaptureLayout::rotationOrder, 0}, {"rotationorder", "wxyz", &CaptureLayout::rotationOrder, 1},
    {"axessource", "buffer", &CaptureLayout::axesSource, 0}, {"axessource", "projection", &CaptureLayout::axesSource, 1}};

// Unknown fields are rejected by this DLL. The app preserves data-only fields
// for newer DLL readers; it does not need to understand their decoding rules.
inline bool ApplyLayoutField(CaptureLayout &layout, std::string_view name, std::string_view text)
{
    const std::string key = LayoutLower(name), value = LayoutLower(text);

    for (const auto &field : LayoutIntegers)
        if (key == field.name)
        {
            uint32_t number = 0;
            auto parsed = std::from_chars(value.data(), value.data() + value.size(), number);
            if (parsed.ec != std::errc{} || parsed.ptr != value.data() + value.size())
                return false;
            layout.*field.member = number;
            return true;
        }

    for (const auto &field : LayoutReals)
        if (key == field.name)
        {
            double number = 0;
            auto parsed = std::from_chars(value.data(), value.data() + value.size(), number);
            if (parsed.ec != std::errc{} || parsed.ptr != value.data() + value.size() || !std::isfinite(number))
                return false;
            layout.*field.member = number;
            return true;
        }

    for (const auto &choice : LayoutChoices)
        if (key == choice.key && value == choice.value)
        {
            layout.*choice.member = choice.number;
            return true;
        }
    return false;
}
} // namespace srvsurvey::camera
