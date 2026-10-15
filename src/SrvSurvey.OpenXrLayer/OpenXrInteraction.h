#pragma once
#include "InputMailbox.h"
#include <array>
#include <atomic>
#include <cstring>
#include <memory>
#include <openxr/openxr.h>
#include <vector>

namespace srvsurvey
{
class InteractionActions
{
    template <class T> bool Load(const char *name, T &function)
    {
        PFN_xrVoidFunction address = nullptr;
        if (getProc(instance, name, &address) != XR_SUCCESS || !address)
            return false;
        function = reinterpret_cast<T>(address);
        return true;
    }
    static void Check(XrResult result)
    {
        if (result != XR_SUCCESS)
            throw result;
    }
    void CreateSet(const char *name, const char *label, uint32_t priority, XrActionSet &set)
    {
        XrActionSetCreateInfo info{XR_TYPE_ACTION_SET_CREATE_INFO};
        std::copy_n(name, std::strlen(name) + 1, info.actionSetName);
        std::copy_n(label, std::strlen(label) + 1, info.localizedActionSetName);
        info.priority = priority;
        Check(createSet(instance, &info, &set));
    }
    void Create(XrActionSet set, const char *name, XrActionType type, XrAction &action)
    {
        XrActionCreateInfo info{XR_TYPE_ACTION_CREATE_INFO};
        std::copy_n(name, std::strlen(name) + 1, info.actionName);
        std::copy_n(name, std::strlen(name) + 1, info.localizedActionName);
        info.actionType = type;
        info.countSubactionPaths = 2;
        info.subactionPaths = hands.data();
        Check(createAction(set, &info, &action));
    }

  public:
    XrInstance instance{};
    PFN_xrGetInstanceProcAddr getProc{};
    PFN_xrCreateActionSet createSet{};
    PFN_xrDestroyActionSet destroySet{};
    PFN_xrCreateAction createAction{};
    PFN_xrStringToPath stringPath{};
    PFN_xrSuggestInteractionProfileBindings suggest{};
    PFN_xrCreateActionSpace createSpace{};
    PFN_xrDestroySpace destroySpace{};
    PFN_xrGetActionStateBoolean booleanState{};
    PFN_xrGetActionStateVector2f vectorState{};
    PFN_xrGetActionStatePose poseState{};
    PFN_xrLocateSpace locate{};
    std::array<XrActionSet, 2> sets{};
    std::array<XrPath, 2> hands{};
    XrAction aim{}, select{}, secondary{}, middle{}, scroll{};
    XrPath touchProfile{};
    std::vector<XrActionSuggestedBinding> bindings;
    std::atomic<bool> ready{false};
    bool everAttached = false;
    XrResult error = XR_SUCCESS;

    InteractionActions(XrInstance handle, PFN_xrGetInstanceProcAddr next) : instance(handle), getProc(next)
    {
    }
    ~InteractionActions()
    {
        if (destroySet)
            for (auto set : sets)
                if (set)
                    destroySet(set);
    }
    void Initialize()
    {
        try
        {
            if (!Load("xrCreateActionSet", createSet) || !Load("xrDestroyActionSet", destroySet) ||
                !Load("xrCreateAction", createAction) || !Load("xrStringToPath", stringPath) ||
                !Load("xrSuggestInteractionProfileBindings", suggest) || !Load("xrCreateActionSpace", createSpace) ||
                !Load("xrDestroySpace", destroySpace) || !Load("xrGetActionStateBoolean", booleanState) ||
                !Load("xrGetActionStateVector2f", vectorState) || !Load("xrGetActionStatePose", poseState) ||
                !Load("xrLocateSpace", locate))
                throw XR_ERROR_FUNCTION_UNSUPPORTED;
            CreateSet("srvsurvey_aim", "SrvSurvey pointer tracking", 0, sets[0]);
            CreateSet("srvsurvey_buttons", "SrvSurvey panel interaction", 1000, sets[1]);
            Check(stringPath(instance, "/user/hand/left", &hands[0]));
            Check(stringPath(instance, "/user/hand/right", &hands[1]));
            Create(sets[0], "aim", XR_ACTION_TYPE_POSE_INPUT, aim);
            Create(sets[1], "select", XR_ACTION_TYPE_BOOLEAN_INPUT, select);
            Create(sets[1], "secondary", XR_ACTION_TYPE_BOOLEAN_INPUT, secondary);
            Create(sets[1], "middle", XR_ACTION_TYPE_BOOLEAN_INPUT, middle);
            Create(sets[1], "scroll", XR_ACTION_TYPE_VECTOR2F_INPUT, scroll);
            Check(stringPath(instance, "/interaction_profiles/oculus/touch_controller", &touchProfile));
            for (const auto *hand : {"/user/hand/left", "/user/hand/right"})
                for (auto [action, suffix] :
                     std::array<std::pair<XrAction, const char *>, 5>{{{aim, "/input/aim/pose"},
                                                                       {select, "/input/trigger/value"},
                                                                       {secondary, "/input/squeeze/value"},
                                                                       {middle, "/input/thumbstick/click"},
                                                                       {scroll, "/input/thumbstick"}}})
                {
                    XrPath path{};
                    Check(stringPath(instance, (std::string(hand) + suffix).c_str(), &path));
                    bindings.push_back({action, path});
                }
            XrInteractionProfileSuggestedBinding info{XR_TYPE_INTERACTION_PROFILE_SUGGESTED_BINDING};
            info.interactionProfile = touchProfile;
            info.countSuggestedBindings = static_cast<uint32_t>(bindings.size());
            info.suggestedBindings = bindings.data();
            Check(suggest(instance, &info));
            ready = true;
        }
        catch (XrResult failure)
        {
            error = failure;
            ready = false;
        }
        catch (...)
        {
            error = XR_ERROR_RUNTIME_FAILURE;
            ready = false;
        }
    }
    XrResult Suggest(const XrInteractionProfileSuggestedBinding *info)
    {
        if (!ready || !info || info->interactionProfile != touchProfile)
            return suggest(instance, info);
        if (everAttached)
        {
            ready = false;
            error = XR_ERROR_ACTIONSETS_ALREADY_ATTACHED;
            return suggest(instance,
                           info); // Preserve a late application request without mutating attached own actions.
        }
        std::vector<XrActionSuggestedBinding> combined;
        if (info->countSuggestedBindings)
            combined.assign(info->suggestedBindings, info->suggestedBindings + info->countSuggestedBindings);
        combined.insert(combined.end(), bindings.begin(), bindings.end());
        auto augmented = *info;
        augmented.countSuggestedBindings = static_cast<uint32_t>(combined.size());
        augmented.suggestedBindings = combined.data();
        XrResult result = suggest(instance, &augmented);
        if (result != XR_SUCCESS)
        {
            ready = false;
            error = result;
            return suggest(instance, info);
        }
        return result;
    }
};
inline Pose PointerPose(const XrPosef &pose)
{
    return {{pose.orientation.x, pose.orientation.y, pose.orientation.z, pose.orientation.w},
            {pose.position.x, pose.position.y, pose.position.z}};
}
class InteractionSession
{
    std::array<XrSpace, 2> spaces{};
    PointerInteraction pointer;
    bool active = false;
    XrResult error = XR_SUCCESS;
    static void Check(XrResult result)
    {
        if (result != XR_SUCCESS)
            throw result;
    }

  public:
    InputMailbox mailbox;
    std::shared_ptr<InteractionActions> actions;
    XrSession session{};
    bool attached = false, setsAttached = false, focused = false;
    explicit InteractionSession(std::wstring path = {}) : mailbox(std::move(path))
    {
    }
    ~InteractionSession()
    {
        Clear();
    }
    void Clear()
    {
        Cancel();
        if (actions)
            for (auto &space : spaces)
            {
                if (space)
                    actions->destroySpace(space);
                space = XR_NULL_HANDLE;
            }
        attached = setsAttached = false;
        error = XR_SUCCESS;
        mailbox.Report(0, 0);
        actions.reset();
    }
    void Cancel()
    {
        active = false;
        auto update = pointer.Reset();
        mailbox.Write(update.events);
    }
    void Attach(std::shared_ptr<InteractionActions> owned, XrSession handle, XrResult result = XR_SUCCESS)
    {
        actions = std::move(owned);
        session = handle;
        actions->everAttached = setsAttached = true;
        if (result != XR_SUCCESS)
        {
            error = result;
            return;
        }
        try
        {
            for (size_t hand = 0; hand < spaces.size(); ++hand)
            {
                XrActionSpaceCreateInfo info{XR_TYPE_ACTION_SPACE_CREATE_INFO};
                info.action = actions->aim;
                info.subactionPath = actions->hands[hand];
                info.poseInActionSpace.orientation.w = 1;
                Check(actions->createSpace(session, &info, &spaces[hand]));
            }
            attached = true;
        }
        catch (XrResult failure)
        {
            error = failure;
        }
        catch (...)
        {
            error = XR_ERROR_RUNTIME_FAILURE;
        }
    }
    bool Requested(int64_t now, bool available)
    {
        bool publisherChanged = false;
        bool requested = mailbox.Requested(now, publisherChanged);
        if (publisherChanged || !requested || !available)
            Cancel();
        Report(now);
        return requested && available && attached && actions->ready && error == XR_SUCCESS;
    }
    void Synced(XrResult result, bool enabled)
    {
        active = enabled && result == XR_SUCCESS;
        if (!active)
            Cancel();
        if (XR_FAILED(result))
            error = result;
    }
    void Fail(XrResult failure, int64_t now)
    {
        error = failure;
        Cancel();
        mailbox.Report(now, 2, error);
    }
    void Report(int64_t now)
    {
        XrResult failure = error != XR_SUCCESS ? error : (actions ? actions->error : XR_SUCCESS);
        mailbox.Report(now, failure != XR_SUCCESS ? 2u : (attached && actions->ready ? 1u : 0u), failure);
    }
    void Publish(const PointerUpdate &update, int64_t now)
    {
        mailbox.Write(update.events);
        Report(now);
    }
    PointerUpdate Update(XrSpace local, std::span<const PointerSurface> surfaces, XrTime time, int64_t now)
    {
        std::array<ControllerSample, 2> hands{};
        try
        {
            if (active && focused && attached && actions->ready && local)
                for (size_t hand = 0; hand < hands.size(); ++hand)
                {
                    XrActionStateGetInfo info{XR_TYPE_ACTION_STATE_GET_INFO};
                    info.subactionPath = actions->hands[hand];
                    info.action = actions->aim;
                    XrActionStatePose pose{XR_TYPE_ACTION_STATE_POSE};
                    Check(actions->poseState(session, &info, &pose));
                    if (!pose.isActive)
                        continue;
                    XrSpaceLocation location{XR_TYPE_SPACE_LOCATION};
                    Check(actions->locate(spaces[hand], local, time, &location));
                    constexpr auto valid =
                        XR_SPACE_LOCATION_ORIENTATION_VALID_BIT | XR_SPACE_LOCATION_POSITION_VALID_BIT;
                    if ((location.locationFlags & valid) != valid)
                        continue;
                    hands[hand].pose = PointerPose(location.pose);
                    hands[hand].valid = true;
                    uint32_t bit = 1;
                    for (auto action : {actions->select, actions->secondary, actions->middle})
                    {
                        info.action = action;
                        XrActionStateBoolean value{XR_TYPE_ACTION_STATE_BOOLEAN};
                        Check(actions->booleanState(session, &info, &value));
                        if (value.isActive && value.currentState)
                            hands[hand].buttons |= bit;
                        bit <<= 1;
                    }
                    info.action = actions->scroll;
                    XrActionStateVector2f scroll{XR_TYPE_ACTION_STATE_VECTOR2F};
                    Check(actions->vectorState(session, &info, &scroll));
                    if (scroll.isActive && std::isfinite(scroll.currentState.x) && std::isfinite(scroll.currentState.y))
                    {
                        hands[hand].scrollX = std::clamp(scroll.currentState.x, -1.0f, 1.0f);
                        hands[hand].scrollY = std::clamp(scroll.currentState.y, -1.0f, 1.0f);
                    }
                }
        }
        catch (XrResult failure)
        {
            error = failure;
            active = false;
        }
        catch (...)
        {
            error = XR_ERROR_RUNTIME_FAILURE;
            active = false;
        }
        auto update = pointer.Update(active && focused, hands, surfaces, static_cast<double>(now) / 1000);
        return update;
    }
};
} // namespace srvsurvey
