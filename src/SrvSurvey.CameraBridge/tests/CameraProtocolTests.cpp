#include "../CameraProtocol.h"
#include <iostream>
#include <limits>
#include <stdexcept>

using namespace srvsurvey::camera;

namespace
{
void Require(bool condition, const char *message)
{
    if (!condition)
        throw std::runtime_error(message);
}

void RequestMustBeCompleteAndCarryANonce()
{
    std::array<uint8_t, 40> request{'S', 'R', 'V', 'C', 'A', 'M', '0', '1'};
    request[8] = 42;
    Nonce nonce{};
    Require(ReadRequest(request, nonce) && nonce[0] == 42 && nonce[31] == 0, "Valid request rejected or nonce altered");
    Require(!ReadRequest(std::span(request).first(39), nonce), "Truncated request accepted");
    request[0] = 'X';
    Require(!ReadRequest(request, nonce), "Unrelated datagram accepted");
    request[0] = 'S';
    request[8] = 0;
    Require(!ReadRequest(request, nonce), "Missing session nonce accepted");
}

void PacketUsesTheDocumentedWireOffsetsAndLittleEndianFields()
{
    Matrix matrix{1, 0, 0, 2, 0, 3, 0, 4, 0, 0, 0, 0, 0.5f, 0, 0, 1};
    Nonce nonce{};
    nonce[0] = 0x5a;
    nonce[31] = 0x7b;
    auto packet = MakePacket(0x0102030405060708ULL, 1234567890, 7, matrix, nonce);
    const std::array<uint8_t, 16> expectedHeader{'S', 'R', 'V', 'C', 'A', 'M', '0', '1', 1, 0, 0, 0, 136, 0, 0, 0};
    Require(std::equal(expectedHeader.begin(), expectedHeader.end(), packet.begin()), "Wrong packet header");
    Require(packet[16] == 8 && packet[23] == 1, "Sequence is not little endian at offset16");
    Require(packet[24] == 0xd2 && packet[25] == 0x02 && packet[26] == 0x96 && packet[27] == 0x49,
            "Timestamp is not little endian at offset24");
    Require(packet[32] == 1 && packet[36] == 7, "Wrong confidence flag or selected matrix index");
    Require(packet[40] == 0 && packet[41] == 0 && packet[42] == 0x80 && packet[43] == 0x3f,
            "First float is not IEEE754 little endian at offset40");
    Require(packet[92] == 0 && packet[93] == 0 && packet[94] == 0 && packet[95] == 0,
            "Matrix rows were transposed");
    Require(packet[104] == 0x5a && packet[135] == 0x7b, "Nonce is not at offset104");
}

void ProjectionRejectsSingularTransformsButAllowsZeroDepthAtTheCamera()
{
    Matrix matrix{1, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0.025f, 0, 0, 1, 0};
    Require(IsUsableProjection(matrix), "An invertible perspective with its local z=0 plane at the camera was rejected");
    matrix[0] = std::numeric_limits<float>::quiet_NaN();
    Require(!IsUsableProjection(matrix), "NaN accepted");
    matrix[0] = 1;
    matrix[14] = 0;
    Require(!IsUsableProjection(matrix), "A singular full3D camera transform was accepted");
}

void CaptureRequiresAVerifiedShaderAndDrawProfile()
{
    CaptureProfile profile{0x69cbbf4aae46746fULL, 1, 3, false};
    Require(!profile.IsUsable(), "Unverified profile enabled capture");
    profile.validated = true;
    Require(profile.IsUsable(), "Complete verified profile rejected");
    profile.shaderHash = 0;
    Require(!profile.IsUsable(), "Unspecified shader accepted");
    profile.shaderHash = 1;
    profile.layoutVersion = 2;
    Require(!profile.IsUsable(), "An unverified packed-transform layout accepted");
    profile.layoutVersion = 1;
    profile.drawKind = 0;
    Require(!profile.IsUsable(), "Missing draw signature accepted");
}

void LeaseExpiresAndNewSessionsInvalidatePendingSamples()
{
    ClientLease lease;
    Require(!lease.IsActive(100), "Capture enabled before an opt-in request");
    Nonce nonce{};
    nonce[0] = 1;
    Require(lease.Renew(nonce, 100) && lease.generation == 1, "First session did not start");
    Require(lease.IsActive(2099) && !lease.IsActive(2100) && !lease.IsActive(99), "Wrong lease boundaries");
    Require(!lease.Renew(nonce, 300) && lease.generation == 1, "Heartbeat replaced the session");
    nonce[1] = 2;
    Require(lease.Renew(nonce, 400) && lease.generation == 2, "New nonce did not invalidate pending samples");
}

void NewCompatibleCacheSupersedesTheBundledCaptureProfile()
{
    CaptureProfile bundled{0x69cbbf4aae46746fULL, 1, 3, true};
    CaptureProfile cached{0x0123456789abcdefULL, 1, 3, true, 2, 1};
    CaptureProfile selected = ChooseCaptureProfile(bundled, cached);
    Require(selected.shaderHash == 0x0123456789abcdefULL && selected.profileVersion == 2,
            "A newer compatible cached profile was not selected");
    cached.profileVersion = 1;
    Require(ChooseCaptureProfile(bundled, cached).shaderHash == 0x69cbbf4aae46746fULL,
            "An equal-version cache changed the bundled shader identity");
}

void UnsupportedCacheKeepsTheBundledDecoder()
{
    CaptureProfile bundled{0x69cbbf4aae46746fULL, 1, 3, true};
    CaptureProfile cached{0x0123456789abcdefULL, 2, 3, true, 2, 1};
    Require(ChooseCaptureProfile(bundled, cached).shaderHash == 0x69cbbf4aae46746fULL,
            "An unsupported cached layout replaced the bundled profile");
    cached.layoutVersion = 1;
    cached.protocolVersion = 2;
    Require(ChooseCaptureProfile(bundled, cached).shaderHash == 0x69cbbf4aae46746fULL,
            "An unsupported cached protocol replaced the bundled profile");
}

void CachedProfilesCannotEnableAnInactiveInstallation()
{
    CaptureProfile bundled{0x69cbbf4aae46746fULL, 1, 3, false};
    CaptureProfile cached{0x0123456789abcdefULL, 1, 3, true, 2, 1};
    Require(!ChooseCaptureProfile(bundled, cached).IsUsable(), "A downloaded cache enabled an inactive installation");
}

void ShaderIdentityMatchesMigotosZeroSeededFnvOne()
{
    const std::array<uint8_t, 2> bytes{65, 66};
    Require(ShaderHash(std::span(bytes).first(1)) == 65, "Shader hash uses the wrong seed");
    Require(ShaderHash(bytes) == 71468255833649ULL, "Shader hash is not multiply-before-XOR FNV-1");
}

void CompletedReadbacksNeverRewindAfterSlotReuse()
{
    Matrix matrix{1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1};
    std::array<CompletedSample, 3> ring{{{matrix, 4, 9, 104, 104000},
                                         {matrix, 2, 9, 102, 102000},
                                         {matrix, 3, 9, 103, 103000}}};
    Require(NewestCompletedSample(ring, 9, 105, 0) == 0, "Ring slot reuse published an older completed pose");
    Require(!NewestCompletedSample(std::span(ring).subspan(1), 9, 106, 4),
            "A delayed readback rewound an already published camera pose");
}

void CompletedReadbacksRejectExpiredAndPreviousSessionSamples()
{
    Matrix matrix{1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1};
    std::array<CompletedSample, 2> samples{{{matrix, 5, 8, 350, 350000}, {matrix, 4, 9, 100, 100000}}};
    Require(!NewestCompletedSample(samples, 9, 351, 0), "Expired or previous-session GPU sample accepted");
    samples[1].submittedAt = 101;
    Require(NewestCompletedSample(samples, 9, 351, 0) == 1, "A sample at the 250ms freshness boundary was rejected");
}
} // namespace

int main()
{
    try
    {
        RequestMustBeCompleteAndCarryANonce();
        PacketUsesTheDocumentedWireOffsetsAndLittleEndianFields();
        ProjectionRejectsSingularTransformsButAllowsZeroDepthAtTheCamera();
        CaptureRequiresAVerifiedShaderAndDrawProfile();
        NewCompatibleCacheSupersedesTheBundledCaptureProfile();
        UnsupportedCacheKeepsTheBundledDecoder();
        CachedProfilesCannotEnableAnInactiveInstallation();
        LeaseExpiresAndNewSessionsInvalidatePendingSamples();
        ShaderIdentityMatchesMigotosZeroSeededFnvOne();
        CompletedReadbacksNeverRewindAfterSlotReuse();
        CompletedReadbacksRejectExpiredAndPreviousSessionSamples();
        std::cout << "Camera protocol: eleven behavior checks passed\n";
        return 0;
    }
    catch (const std::exception &exception)
    {
        std::cerr << exception.what() << '\n';
        return 1;
    }
}
