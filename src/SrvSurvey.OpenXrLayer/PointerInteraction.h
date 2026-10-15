#pragma once
#include <algorithm>
#include <array>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <optional>
#include <span>
#include <vector>

namespace srvsurvey
{
constexpr uint32_t InputMagic = 0x49565253, InputVersion = 1, InputCapacity = 4096, InputEventCapacity = 64;
enum class PointerKind : uint32_t
{
    Move,
    LeftDown,
    LeftUp,
    RightDown,
    RightUp,
    MiddleDown,
    MiddleUp,
    Wheel,
    Reset
};
struct PointerEvent
{
    uint64_t id;
    PointerKind kind;
    uint32_t reserved = 0;
    float x = 0, y = 0, wheelX = 0, wheelY = 0;
};
static_assert(sizeof(PointerEvent) == 32);
struct InputHeader
{
    uint32_t magic, version, controlSequence, enabled;
    int64_t controlTime;
    uint64_t generation;
    int64_t heartbeat;
    uint32_t state;
    int32_t error;
    uint32_t eventSequence, eventCount;
    uint64_t acknowledgedGeneration;
    uint8_t padding[64];
    PointerEvent events[InputEventCapacity];
};
static_assert(offsetof(InputHeader, events) == 128);
static_assert(sizeof(InputHeader) <= InputCapacity);
struct Point3
{
    float x = 0, y = 0, z = 0;
};
struct Quaternion
{
    float x = 0, y = 0, z = 0, w = 1;
};
struct Pose
{
    Quaternion orientation;
    Point3 position;
};
inline Point3 Rotate(Quaternion q, Point3 p)
{
    Point3 t{2 * (q.y * p.z - q.z * p.y), 2 * (q.z * p.x - q.x * p.z), 2 * (q.x * p.y - q.y * p.x)};
    return {p.x + q.w * t.x + q.y * t.z - q.z * t.y, p.y + q.w * t.y + q.z * t.x - q.x * t.z,
            p.z + q.w * t.z + q.x * t.y - q.y * t.x};
}
struct PointerSurface
{
    uint64_t id;
    Pose pose;
    float width, height;
};
struct ControllerSample
{
    Pose pose;
    uint32_t buttons = 0;
    float scrollX = 0, scrollY = 0;
    bool valid = false;
};
struct PointerHit
{
    uint64_t id;
    Pose pose;
    float x, y, distance, width, height;
};
inline std::optional<PointerHit> Intersect(const PointerSurface &surface, const ControllerSample &hand, bool captured)
{
    const auto &q = surface.pose.orientation;
    Quaternion inverse{-q.x, -q.y, -q.z, q.w};
    auto origin =
        Rotate(inverse, {hand.pose.position.x - surface.pose.position.x, hand.pose.position.y - surface.pose.position.y,
                         hand.pose.position.z - surface.pose.position.z});
    auto direction = Rotate(inverse, Rotate(hand.pose.orientation, {0, 0, -1}));
    if (direction.z >= -0.0001f || origin.z < 0 || surface.width <= 0 || surface.height <= 0)
        return {};
    float distance = -origin.z / direction.z;
    float x = (origin.x + distance * direction.x) / surface.width + 0.5f;
    float y = 0.5f - (origin.y + distance * direction.y) / surface.height;
    if (!std::isfinite(x) || !std::isfinite(y) || !std::isfinite(distance) || distance > 100 ||
        (!captured && (x < 0 || x > 1 || y < 0 || y > 1)))
        return {};
    return PointerHit{
        surface.id,    surface.pose,  std::clamp(x, -10.0f, 10.0f), std::clamp(y, -10.0f, 10.0f), distance,
        surface.width, surface.height};
}
struct PointerUpdate
{
    std::vector<PointerEvent> events;
    std::optional<PointerHit> cursor;
};
class PointerInteraction
{
    bool active = false;
    std::array<bool, 2> armed{};
    size_t selectedHand = 1;
    uint64_t target = 0;
    uint32_t buttons = 0;
    double previousTime = 0;

  public:
    PointerUpdate Reset()
    {
        PointerUpdate result;
        if (active || target || buttons)
            result.events.push_back({0, PointerKind::Reset});
        active = false;
        armed = {};
        target = 0;
        buttons = 0;
        previousTime = 0;
        return result;
    }
    PointerUpdate Update(bool enabled, const std::array<ControllerSample, 2> &hands,
                         std::span<const PointerSurface> surfaces, double time)
    {
        if (!enabled)
            return Reset();
        PointerUpdate result;
        double delta = previousTime ? std::clamp(time - previousTime, 0.0, 0.1) : 0;
        previousTime = time;
        active = true;
        std::array<std::optional<PointerHit>, 2> hits;
        for (size_t index = 0; index < hands.size(); ++index)
        {
            if (!hands[index].valid)
            {
                armed[index] = false;
                continue;
            }
            if (!hands[index].buttons)
                armed[index] = true;
            for (const auto &surface : surfaces)
            {
                if (buttons && (index != selectedHand || surface.id != target))
                    continue;
                auto hit = Intersect(surface, hands[index], buttons != 0);
                if (hit) // Later composition layers paint over earlier quads.
                    hits[index] = hit;
            }
        }
        for (size_t index = 0; index < hands.size(); ++index)
            if (!hits[index] && hands[index].buttons)
                armed[index] = false;
        if (!buttons)
        {
            for (size_t index = 0; index < hands.size(); ++index)
                if (hits[index] && armed[index] && hands[index].buttons)
                    selectedHand = index;
            if (!hits[selectedHand] && hits[1 - selectedHand])
                selectedHand = 1 - selectedHand;
        }
        if (!hits[selectedHand])
        {
            if (target || buttons)
                result.events.push_back({0, PointerKind::Reset});
            if (buttons)
                armed[selectedHand] = false;
            target = 0;
            buttons = 0;
            if (!hands[selectedHand].valid)
                armed[selectedHand] = false;
            return result;
        }
        auto hit = *hits[selectedHand];
        if (target && target != hit.id)
            result.events.push_back({0, PointerKind::Reset});
        target = hit.id;
        result.cursor = hit;
        result.events.push_back({target, PointerKind::Move, 0, hit.x, hit.y});
        uint32_t next = armed[selectedHand] ? hands[selectedHand].buttons : 0;
        for (uint32_t button = 0; button < 3; ++button)
        {
            uint32_t mask = 1u << button;
            if ((buttons & mask) != (next & mask))
                result.events.push_back(
                    {target, static_cast<PointerKind>(1 + button * 2 + ((next & mask) ? 0 : 1)), 0, hit.x, hit.y});
        }
        buttons = next;
        auto scroll = [](float axis) { return std::abs(axis) <= 0.25f ? 0.0f : axis; };
        float x = scroll(hands[selectedHand].scrollX), y = scroll(hands[selectedHand].scrollY);
        if (armed[selectedHand] && delta > 0 && (x || y))
            result.events.push_back({target, PointerKind::Wheel, 0, hit.x, hit.y, x * static_cast<float>(delta) * 8,
                                     y * static_cast<float>(delta) * 8});
        return result;
    }
};
} // namespace srvsurvey
