#include "../CockpitReference.h"
#include "CapturedReferenceFixtures.h"
#include <iostream>
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

struct Frame
{
    std::vector<uint8_t> instances, constants;
};

Frame Load(const captured::Pose &pose)
{
    Frame frame{std::vector<uint8_t>(pose.bytes), std::vector<uint8_t>(CameraConstantBytes)};
    for (const auto &record : pose.records)
        for (size_t byte = 0; byte < 32; ++byte)
        {
            char text[3]{record.first32[byte * 2], record.first32[byte * 2 + 1], 0};
            frame.instances[record.index * CockpitRecordStride + byte] = static_cast<uint8_t>(std::strtoul(text, nullptr, 16));
        }
    for (size_t index = 0; index < pose.cameraRows.size(); ++index)
        Write32(frame.constants, 270 * 16 + index * 4, std::bit_cast<uint32_t>(pose.cameraRows[index]));
    return frame;
}

std::array<double, 2> Project(const Matrix &matrix, double x, double y, double z)
{
    double denominator = matrix[12] * x + matrix[13] * y + matrix[14] * z + matrix[15];
    return {(matrix[0] * x + matrix[1] * y + matrix[2] * z + matrix[3]) / denominator,
            (matrix[4] * x + matrix[5] * y + matrix[6] * z + matrix[7]) / denominator};
}

void CapturedCenteredViewFacesForward()
{
    auto frame = Load(captured::center);
    auto reference = FindCockpitReference(frame.instances, frame.constants);
    Require(reference.has_value(), "Captured centered cockpit reference was rejected");
    Require(reference->members == 21 && reference->runnerUp == 6 && reference->nearRecords == 53,
            "Centered capture selected a different nearby body");
    double yaw = std::atan2(reference->projection[2], reference->projection[0]) * 180 / 3.141592653589793;
    Require(std::abs(yaw) < 0.1, "Centered view does not face forward in cockpit coordinates");
}

void CapturedFreelookMovesTheCockpitProjection()
{
    auto frame = Load(captured::left);
    auto reference = FindCockpitReference(frame.instances, frame.constants);
    Require(reference.has_value() && reference->members == 22 && reference->runnerUp == 6 && reference->nearRecords == 54,
            "Freelook capture selected a different nearby body");
    double yaw = std::atan2(reference->projection[2], reference->projection[0]) * 180 / 3.141592653589793;
    Require(std::abs(yaw - 43.6277) < 0.02, "Native freelook did not rotate the cockpit projection by the observed angle");
}

void CapturedShipTurnDoesNotMoveACockpitAnchoredPoint()
{
    auto center = Load(captured::center), turned = Load(captured::shipturnedcentered);
    auto first = FindCockpitReference(center.instances, center.constants);
    auto second = FindCockpitReference(turned.instances, turned.constants);
    Require(first.has_value() && second.has_value() && second->members == 23 && second->runnerUp == 6,
            "Ship-turn capture did not recover the cockpit root");
    auto before = Project(first->projection, 0, 1.61, 1), after = Project(second->projection, 0, 1.61, 1);
    Require(std::abs(before[0] - after[0]) < 0.0005 && std::abs(before[1] - after[1]) < 0.0005,
            "A cockpit-anchored point drifted when the ship turned with centered freelook");
}

void InstanceTableCompactionDoesNotChangeTheReference()
{
    auto original = Load(captured::center);
    auto expected = FindCockpitReference(original.instances, original.constants);
    std::vector<uint8_t> compacted(captured::center.records.size() * CockpitRecordStride);
    for (size_t index = 0; index < captured::center.records.size(); ++index)
        std::copy_n(original.instances.begin() + captured::center.records[index].index * CockpitRecordStride, 32,
                    compacted.begin() + (captured::center.records.size() - index - 1) * CockpitRecordStride);
    auto actual = FindCockpitReference(compacted, original.constants);
    Require(expected.has_value() && actual.has_value() && actual->recordIndex != expected->recordIndex,
            "Compacted instance table did not resolve a new live root index");
    for (size_t index = 0; index < 16; ++index)
        Require(std::abs(expected->projection[index] - actual->projection[index]) < 0.00001,
                "Moving records within the instance table changed the cockpit pose");
}

void EquivalentQuaternionHemispheresDoNotSplitTheBody()
{
    auto frame = Load(captured::center);
    auto expected = FindCockpitReference(frame.instances, frame.constants);
    for (size_t index = 0; index < captured::center.records.size(); index += 2)
    {
        size_t offset = captured::center.records[index].index * CockpitRecordStride + 8;
        for (size_t byte = 0; byte < 8; ++byte)
            frame.instances[offset + byte] = static_cast<uint8_t>(255 - frame.instances[offset + byte]);
    }
    auto actual = FindCockpitReference(frame.instances, frame.constants);
    Require(expected.has_value() && actual.has_value() && actual->members == 21,
            "Equivalent quaternion signs split the cockpit root");
    for (size_t index = 0; index < 16; ++index)
        Require(std::abs(expected->projection[index] - actual->projection[index]) < 0.00001,
                "Equivalent quaternion signs changed the cockpit pose");
}

void TwoSimilarlySupportedNearbyBodiesAreRejected()
{
    auto frame = Load(captured::center);
    // A second nearby body with 22 rigid records competes with the captured
    // cockpit's21. Neither has the required two-to-one support advantage.
    for (size_t index = 0; index < 22; ++index)
    {
        size_t offset = index * CockpitRecordStride;
        std::copy_n(frame.instances.begin() + 394 * CockpitRecordStride, 32, frame.instances.begin() + offset);
        Write32(frame.instances, offset + 16, std::bit_cast<uint32_t>(-23.604643f));
    }
    Require(!FindCockpitReference(frame.instances, frame.constants), "An ambiguous nearby body was reported as the cockpit");
}

void InsufficientMalformedAndNonPerspectiveSourcesAreRejected()
{
    auto frame = Load(captured::center);
    std::vector<uint8_t> sparse(7 * CockpitRecordStride);
    for (size_t index = 0; index < 7; ++index)
        std::copy_n(frame.instances.begin() + 394 * CockpitRecordStride, 32, sparse.begin() + index * CockpitRecordStride);
    Require(!FindCockpitReference(sparse, frame.constants), "Fewer than eight rigid members were accepted");
    frame.instances.pop_back();
    Require(!FindCockpitReference(frame.instances, frame.constants), "A truncated structured record was accepted");
    frame = Load(captured::center);
    Write32(frame.constants, 273 * 16 + 12, std::bit_cast<uint32_t>(1.0f));
    Require(!FindCockpitReference(frame.instances, frame.constants), "An orthographic shadow pass was accepted");
    frame = Load(captured::center);
    Write32(frame.constants, 275 * 16, 0x7fc00000);
    Require(!FindCockpitReference(frame.instances, frame.constants), "A nonfinite camera origin was accepted");
}
} // namespace

int main()
{
    try
    {
        CapturedCenteredViewFacesForward();
        CapturedFreelookMovesTheCockpitProjection();
        CapturedShipTurnDoesNotMoveACockpitAnchoredPoint();
        InstanceTableCompactionDoesNotChangeTheReference();
        EquivalentQuaternionHemispheresDoNotSplitTheBody();
        TwoSimilarlySupportedNearbyBodiesAreRejected();
        InsufficientMalformedAndNonPerspectiveSourcesAreRejected();
        std::cout << "Cockpit reference: seven behavior checks passed\n";
        return 0;
    }
    catch (const std::exception &exception)
    {
        std::cerr << exception.what() << '\n';
        return 1;
    }
}
