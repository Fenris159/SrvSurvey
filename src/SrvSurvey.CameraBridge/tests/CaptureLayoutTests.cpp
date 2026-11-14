#include "../CockpitReference.h"
#include <iostream>
#include <limits>
#include <stdexcept>
#include <vector>

using namespace srvsurvey::camera;
namespace
{
void Require(bool condition, const char *message)
{
    if (!condition)
        throw std::runtime_error(message);
}

void Put(std::span<uint8_t> bytes, size_t offset, double value, uint32_t format)
{
    if (format == 1)
        Write32(bytes, offset, std::bit_cast<uint32_t>(static_cast<float>(value)));
    else
        Write64(bytes, offset, std::bit_cast<uint64_t>(value));
}

struct RelocatedFrame
{
    CaptureLayout layout;
    std::vector<uint8_t> instances, constants;
    RelocatedFrame() : instances(32 + 8 * 512), constants(8192)
    {
        layout.recordStride = 512;
        layout.bufferElementStride = 4;
        layout.bufferKind = 1;
        layout.recordDataOffset = 32;
        layout.positionOffset = 64;
        layout.positionFormat = 2;
        layout.rotationOffset = 96;
        layout.rotationFormat = 3;
        layout.rotationOrder = 1;
        layout.cameraBytes = 8192;
        layout.cameraPositionOffset = 256;
        layout.cameraPositionFormat = 2;
        layout.axesOffset = 320;
        layout.axesFormat = 2;
        layout.axesStride = 32;
        layout.projectionOffset = 512;
        layout.projectionFormat = 2;
        layout.projectionTranspose = 1;
        layout.canonicalNearDepth = 0.05;
        for (size_t index = 0; index < 8; ++index)
        {
            size_t offset = 32 + index * 512;
            Put(instances, offset + 64, 10, 2);
            Put(instances, offset + 72, 20, 2);
            Put(instances, offset + 80, 30, 2);
            Put(instances, offset + 96, 1, 1); // float WXYZ identity
        }
        Put(constants, 256, 10, 2);
        Put(constants, 264, 21.5, 2);
        Put(constants, 272, 30.125, 2);
        Put(constants, 320, 1, 2);
        Put(constants, 360, 1, 2);
        Put(constants, 400, 1, 2);
        // Column-major storage, ordinary finite/forward depth. Only X/Y/W
        // determine the screen projection; the bridge normalizes clip depth.
        const double projection[]{2, 0, 0, 0, 0, 3, 0, 0, 0, 0, 1, -0.1, 0, 0, 1, 0};
        for (size_t index = 0; index < 16; ++index)
            Put(constants, 512 + index * 8, projection[index], 2);
    }
};

void ExpectProjection(const RelocatedFrame &frame, const Matrix &expected)
{
    auto result = FindCockpitReference(frame.instances, frame.constants, frame.layout);
    Require(result.has_value() && result->members == 8 && result->recordIndex == 0,
            "The declarative layout did not locate the eight cockpit records");
    for (size_t index = 0; index < 16; ++index)
        Require(std::abs(result->projection[index] - expected[index]) < 0.0002,
                "The declarative layout produced an incorrect camera projection");
}

void RelocatedTypedTransposedLayoutPreservesTheExpectedProjection()
{
    RelocatedFrame frame;
    ExpectProjection(frame, {2, 0, 0, 0, 0, 3, 0, -4.5f, 0, 0, 0, 0.05f, 0, 0, 1, -0.125f});
    frame.layout.axesSource = 1;
    std::fill(frame.constants.begin() + 320, frame.constants.begin() + 424, uint8_t{0});
    ExpectProjection(frame, {2, 0, 0, 0, 0, 3, 0, -4.5f, 0, 0, 0, 0.05f, 0, 0, 1, -0.125f});
}

void WorldProjectionDoesNotSubtractTheOriginTwice()
{
    RelocatedFrame frame;
    frame.layout.projectionIncludesOrigin = 1;
    Put(frame.constants, 512 + 3 * 8, -20, 2);
    Put(frame.constants, 512 + 7 * 8, -64.5, 2);
    Put(frame.constants, 512 + 15 * 8, -30.125, 2);
    ExpectProjection(frame, {2, 0, 0, 0, 0, 3, 0, -4.5f, 0, 0, 0, 0.05f, 0, 0, 1, -0.125f});
}

void LargeWorldCoordinatesCancelBeforeProjectionNarrowsToTheWireFormat()
{
    RelocatedFrame frame;
    frame.layout.projectionIncludesOrigin = 1;
    for (size_t index = 0; index < 8; ++index)
        Put(frame.instances, 32 + index * 512 + 64, 100000000.25, 2);
    Put(frame.constants, 256, 100000000.25, 2);
    Put(frame.constants, 512 + 3 * 8, -200000000.5, 2);
    Put(frame.constants, 512 + 7 * 8, -64.5, 2);
    Put(frame.constants, 512 + 15 * 8, -30.125, 2);
    ExpectProjection(frame, {2, 0, 0, 0, 0, 3, 0, -4.5f, 0, 0, 0, 0.05f, 0, 0, 1, -0.125f});
}

void QuaternionFormatsAndConjugationKeepTheConfiguredOrientation()
{
    for (uint32_t format = 1; format <= 4; ++format)
    {
        RelocatedFrame frame;
        frame.layout.rotationFormat = format;
        frame.layout.rotationConjugate = 1;
        for (size_t index = 0; index < 8; ++index)
        {
            size_t offset = 32 + index * 512 + 96;
            for (size_t axis = 0; axis < 4; ++axis)
            {
                double value = axis == 0 || axis == 2 ? 0.7071067811865475 : 0;
                if (format <= 2)
                {
                    uint16_t packed = format == 1 ? static_cast<uint16_t>(std::round((value + 1) * 32767.5))
                                                  : static_cast<uint16_t>(std::round(value * 32767));
                    frame.instances[offset + axis * 2] = static_cast<uint8_t>(packed);
                    frame.instances[offset + axis * 2 + 1] = static_cast<uint8_t>(packed >> 8);
                }
                else
                    Put(frame.instances, offset + axis * (format == 3 ? 4 : 8), value, format - 2);
            }
        }
        ExpectProjection(frame, {0, 0, -2, 0, 0, 3, 0, -4.5f, 0, 0, 0, 0.05f, 1, 0, 0, -0.125f});
    }
}

void UnitScalesAndSelectionThresholdsApplyToTheDecodedData()
{
    RelocatedFrame frame;
    frame.instances.resize(32 + 5 * 512);
    for (size_t index = 0; index < 5; ++index)
    {
        size_t offset = 32 + index * 512;
        Put(frame.instances, offset + 64, 0.01, 2);
        Put(frame.instances, offset + 72, 0.02, 2);
        Put(frame.instances, offset + 80, 0.03, 2);
    }
    Put(frame.constants, 256, 0.01, 2);
    Put(frame.constants, 264, 0.0215, 2);
    Put(frame.constants, 272, 0.030125, 2);
    frame.layout.positionScale = frame.layout.cameraPositionScale = 1000;
    Require(!FindCockpitReference(frame.instances, frame.constants, frame.layout), "Five members met the default eight-member minimum");
    frame.layout.minimumMembers = 5;
    frame.layout.maximumNearRecords = 5;
    frame.layout.nearRadius = 1.5;
    Require(!FindCockpitReference(frame.instances, frame.constants, frame.layout), "The configured candidate radius was ignored");
    frame.layout.nearRadius = 2;
    auto result = FindCockpitReference(frame.instances, frame.constants, frame.layout);
    Require(result && result->members == 5 && result->nearRecords == 5,
            "The configured support/candidate limits were ignored");
    const Matrix expected{2, 0, 0, 0, 0, 3, 0, -4.5f, 0, 0, 0, 0.05f, 0, 0, 1, -0.125f};
    for (size_t index = 0; index < 16; ++index)
        Require(std::abs(result->projection[index] - expected[index]) < 0.0002,
                "Catalog unit scales changed the physical camera projection");
}

void UnsafeOrUnknownLayoutsNeverDecode()
{
    RelocatedFrame frame;
    for (const auto &[key, value] : std::array<std::pair<const char *, const char *>, 6>{{
        {"PositionOffset", "4294967295"}, {"AxesFormat", "float128"}, {"SchemaVersion", "2"},
        {"MaximumBufferBytes", "8388609"}, {"RequiredBridgeVersion", "999"}, {"CameraSlot", "14"}}})
    {
        CaptureLayout candidate = frame.layout;
        bool applied = ApplyLayoutField(candidate, key, value);
        Require(!applied || !FindCockpitReference(frame.instances, frame.constants, candidate),
                "An unsafe or unsupported layout was decoded");
    }
    Require(!ApplyLayoutField(frame.layout, "UnknownPacking", "1"), "Unknown native layout fields were ignored");
    Require(!ApplyLayoutField(frame.layout, "PositionScale", "nan"), "A nonfinite catalog scale was accepted");
    frame.layout.axesFormat = 0;
    Require(!frame.layout.IsUsable(), "An unsupported axes scalar format was accepted");
}

void ResourceViewsAreBoundedAndMayStartInsideABuffer()
{
    CaptureLayout layout;
    auto view = GetRecordViewRange(layout, 4032, 336, 2, 8);
    Require(view && view->offset == 672 && view->bytes == 2688, "A partial structured resource view was mislocated");
    Require(!GetRecordViewRange(layout, 4032, 336, 5, 8), "An overflowing view was accepted");
    Require(!GetRecordViewRange(layout, 4032, 336, 0xffffffffU, 8), "A wrapped view offset was accepted");
    layout.bufferKind = 1;
    layout.bufferElementStride = 4;
    layout.recordStride = 512;
    layout.recordDataOffset = 32;
    auto raw = GetRecordViewRange(layout, 8192, 4, 16, 1032);
    Require(raw && raw->offset == 64 && raw->bytes == 4128, "A raw view with a record header was rejected");
    Require(!GetRecordViewRange(layout, 8192, 4, 16, 1031), "A truncated raw record was accepted");
}
} // namespace

int main()
{
    try
    {
        RelocatedTypedTransposedLayoutPreservesTheExpectedProjection();
        WorldProjectionDoesNotSubtractTheOriginTwice();
        LargeWorldCoordinatesCancelBeforeProjectionNarrowsToTheWireFormat();
        QuaternionFormatsAndConjugationKeepTheConfiguredOrientation();
        UnitScalesAndSelectionThresholdsApplyToTheDecodedData();
        UnsafeOrUnknownLayoutsNeverDecode();
        ResourceViewsAreBoundedAndMayStartInsideABuffer();
        std::cout << "Capture layout: seven behavior checks passed\n";
        return 0;
    }
    catch (const std::exception &exception)
    {
        std::cerr << exception.what() << '\n';
        return 1;
    }
}
