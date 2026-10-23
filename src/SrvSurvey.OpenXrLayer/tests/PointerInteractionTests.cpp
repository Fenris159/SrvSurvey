#include "../PointerInteraction.h"
#include <iostream>
#include <limits>
#include <stdexcept>
using namespace srvsurvey;
namespace
{
void Require(bool condition, const char *message)
{
    if (!condition)
        throw std::runtime_error(message);
}
bool Has(const PointerUpdate &update, PointerKind kind)
{
    return std::any_of(update.events.begin(), update.events.end(), [kind](auto e) { return e.kind == kind; });
}
} // namespace
int main()
{
    try
    {
        PointerSurface panel{1, {{}, {0, 0, -2}}, 2, 1};
        ControllerSample hand{};
        hand.valid = true;
        auto hit = Intersect(panel, hand, false);
        Require(hit && hit->x == 0.5f && hit->y == 0.5f && hit->distance == 2,
                "Center ray uses negative Z and top-left UV");
        hand.pose.position = {0.5f, 0.25f, 0};
        hit = Intersect(panel, hand, false);
        Require(hit && hit->x == 0.75f && hit->y == 0.25f, "Map physical coordinates to normalized panel coordinates");
        panel.pose.orientation = {0, 0.70710678f, 0, 0.70710678f};
        panel.pose.position = {-2, 0, 0};
        hand.pose = {{0, 0.70710678f, 0, 0.70710678f}, {0, 0, 0}};
        hit = Intersect(panel, hand, false);
        Require(hit && std::abs(hit->x - 0.5f) < 0.00001f, "Rotate aim and surface in stereo space");
        panel = {1, {{}, {0, 0, -2}}, 2, 1};
        hand.pose = {};
        hand.pose.position.x = 3;
        Require(!Intersect(panel, hand, false) && Intersect(panel, hand, true),
                "Capture permits dragging outside panel");
        hand.pose.position = {0, 0, -3};
        Require(!Intersect(panel, hand, true), "Reject back-face hits");
        hand.pose = {{0, 0.70710678f, 0, 0.70710678f}, {}};
        Require(!Intersect(panel, hand, true), "Reject parallel aim");
        hand.pose = {};
        hand.pose.position.x = std::numeric_limits<float>::quiet_NaN();
        Require(!Intersect(panel, hand, true), "Reject nonfinite tracking");
        hand.pose = {};
        PointerInteraction pointer;
        std::array<ControllerSample, 2> hands{hand, hand};
        hands[1].buttons = 1;
        auto update = pointer.Update(true, hands, {&panel, 1}, 1);
        Require(update.cursor && !Has(update, PointerKind::LeftDown), "Held trigger at mode entry cannot click");
        hands[1].buttons = 0;
        pointer.Update(true, hands, {&panel, 1}, 1.01);
        hands[1].buttons = 7;
        update = pointer.Update(true, hands, {&panel, 1}, 1.02);
        Require(Has(update, PointerKind::LeftDown) && Has(update, PointerKind::RightDown) &&
                    Has(update, PointerKind::MiddleDown),
                "Deliver three button edges");
        hands[1].pose.position.x = 3;
        update = pointer.Update(true, hands, {&panel, 1}, 1.03);
        Require(update.cursor && update.cursor->x == 2 && !Has(update, PointerKind::Reset),
                "Preserve captured panel and hand off bounds");
        hands[1].buttons = 0;
        update = pointer.Update(true, hands, {&panel, 1}, 1.04);
        Require(Has(update, PointerKind::LeftUp) && Has(update, PointerKind::RightUp) &&
                    Has(update, PointerKind::MiddleUp),
                "Release at captured off-panel coordinates");
        hands[1].pose = {};
        hands[1].scrollY = 1;
        update = pointer.Update(true, hands, {&panel, 1}, 2);
        Require(Has(update, PointerKind::Wheel) && update.events.back().wheelY <= 0.8f,
                "Bound scrolling after frame stall");
        hands[1].scrollY = 0.2f;
        Require(!Has(pointer.Update(true, hands, {&panel, 1}, 2.01), PointerKind::Wheel),
                "Stick dead zone suppresses drift");
        hands[1].buttons = 1;
        pointer.Update(true, hands, {&panel, 1}, 2.02);
        hands[1].valid = false;
        Require(Has(pointer.Update(true, hands, {&panel, 1}, 2.03), PointerKind::Reset),
                "Tracking loss cancels capture");
        hands[1].valid = true;
        Require(!Has(pointer.Update(true, hands, {&panel, 1}, 2.04), PointerKind::LeftDown),
                "Tracking recovery requires neutral trigger");
        hands[1].buttons = 0;
        pointer.Update(true, hands, {&panel, 1}, 2.05);
        hands[1].buttons = 1;
        pointer.Update(true, hands, {&panel, 1}, 2.06);
        Require(Has(pointer.Update(true, hands, {}, 2.07), PointerKind::Reset),
                "Hiding captured panel cancels buttons");
        Require(!Has(pointer.Update(true, hands, {&panel, 1}, 2.08), PointerKind::LeftDown),
                "Reappearing panel cannot click held trigger");
        Require(Has(pointer.Update(false, hands, {&panel, 1}, 2.09), PointerKind::Reset), "Mode off cancels pointer");
        hands[1].buttons = 0;
        pointer.Update(true, hands, {&panel, 1}, 3);
        hands[0].buttons = 1;
        Require(Has(pointer.Update(true, hands, {&panel, 1}, 3.01), PointerKind::LeftDown),
                "Left controller can take pointer ownership");
        hands[0].buttons = 0;
        pointer.Update(true, hands, {&panel, 1}, 3.02);
        PointerSurface closer{2, {{}, {0, 0, -1}}, 1, 1};
        std::array<PointerSurface, 2> overlapping{closer, panel};
        update = pointer.Update(true, hands, overlapping, 3.03);
        Require(update.cursor && update.cursor->id == 1,
                "Hit the last composed panel even when it is physically farther away");
        update = pointer.Update(true, hands, {&closer, 1}, 3.04);
        Require(update.cursor && update.cursor->id == 2 && Has(update, PointerKind::Reset),
                "Switching panels cancels the previous UI target");
        PointerInteraction recovery;
        hands = {hand, hand};
        recovery.Update(true, hands, {&panel, 1}, 4);
        hands[0].valid = false;
        recovery.Update(true, hands, {&panel, 1}, 4.01);
        hands[0].valid = true;
        hands[0].buttons = 1;
        Require(!Has(recovery.Update(true, hands, {&panel, 1}, 4.02), PointerKind::LeftDown),
                "An unselected controller also requires neutral after tracking recovery");
        std::cout << "Pointer geometry, capture, buttons, scroll, tracking loss and rearming tests passed\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
