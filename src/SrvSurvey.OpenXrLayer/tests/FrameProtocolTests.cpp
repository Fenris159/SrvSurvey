#include "../FrameProtocol.h"
#include <iostream>
#include <limits>
#include <stdexcept>
#include <vector>

using namespace srvsurvey;
void Require(bool condition)
{
    if (!condition)
        throw std::runtime_error("Frame protocol assertion failed");
}
std::vector<uint8_t> Packet()
{
    std::vector<uint8_t> packet(sizeof(Header) + sizeof(Descriptor) + 8);
    Header h{};
    h.magic = Magic;
    h.version = Version;
    h.sequence = 2;
    h.length = static_cast<uint32_t>(packet.size());
    h.timestamp = 1000;
    h.count = 1;
    Descriptor d{};
    d.id = 42;
    d.width = 2;
    d.height = 1;
    d.offset = sizeof(Header) + sizeof(Descriptor);
    d.length = 8;
    d.widthMeters = 1;
    d.heightMeters = 0.5f;
    d.orientation[3] = 1;
    d.alpha = 1;
    std::memcpy(packet.data(), &h, sizeof(h));
    std::memcpy(packet.data() + sizeof(h), &d, sizeof(d));
    return packet;
}
int main()
{
    auto packet = Packet();
    Require(ValidatePacket(packet, 1000));
    Require(ValidatePacket(packet, 3000));
    Require(!ValidatePacket(packet, 3001));
    Require(!ValidatePacket(packet, 999));
    Require(!ValidatePacket(std::span(packet).first(12), 1000));
    for (size_t offset : {size_t(0), size_t(4), size_t(8), size_t(12), size_t(24)})
    {
        auto invalid = packet;
        invalid[offset] = 255;
        Require(!ValidatePacket(invalid, 1000));
    }
    for (size_t offset : {size_t(8), size_t(12), size_t(16), size_t(20)})
    {
        auto invalid = packet;
        uint32_t value = 0xffffffff;
        std::memcpy(invalid.data() + sizeof(Header) + offset, &value, sizeof(value));
        Require(!ValidatePacket(invalid, 1000));
    }
    for (size_t offset = 24; offset < sizeof(Descriptor); offset += 4)
    {
        auto invalid = packet;
        float value = std::numeric_limits<float>::quiet_NaN();
        std::memcpy(invalid.data() + sizeof(Header) + offset, &value, sizeof(value));
        Require(!ValidatePacket(invalid, 1000));
    }
    std::cout << "Frame protocol rejects stale, torn, truncated, oversized, and non-finite frames\n";
}
