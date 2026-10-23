// Included by LayerTests inside its fixture namespace.
std::map<std::string, XrPath> testPaths;
uintptr_t nextInputHandle = 1000;
uint32_t actionSetCount = 0, destroyedActionSets = 0;
std::vector<uint32_t> priorities;
std::vector<XrActionSuggestedBinding> suggested;
std::vector<XrActionSet> attachedSets;
std::vector<XrActiveActionSet> synchronizedSets;
const void *inputNext = nullptr;
XrResult syncResult = XR_SUCCESS, attachResult = XR_SUCCESS;
uint32_t attachCalls = 0;
bool failMergedSuggestion = false, failActionSpace = false, failMergedAttach = false;
uint32_t controllerButtons = 0;
XrResult XRAPI_CALL InputPath(XrInstance, const char *name, XrPath *output)
{
    auto [it, inserted] = testPaths.emplace(name, testPaths.size() + 1);
    (void)inserted;
    *output = it->second;
    return XR_SUCCESS;
}
XrResult XRAPI_CALL InputCreateSet(XrInstance, const XrActionSetCreateInfo *info, XrActionSet *output)
{
    ++actionSetCount;
    priorities.push_back(info->priority);
    *output = reinterpret_cast<XrActionSet>(nextInputHandle++);
    return XR_SUCCESS;
}
XrResult XRAPI_CALL InputDestroySet(XrActionSet)
{
    ++destroyedActionSets;
    return XR_SUCCESS;
}
XrResult XRAPI_CALL InputCreateAction(XrActionSet, const XrActionCreateInfo *info, XrAction *output)
{
    Require(info->countSubactionPaths == 2, "Create two-handed actions");
    *output = reinterpret_cast<XrAction>(nextInputHandle++);
    return XR_SUCCESS;
}
XrResult XRAPI_CALL InputSuggest(XrInstance, const XrInteractionProfileSuggestedBinding *info)
{
    suggested.assign(info->suggestedBindings, info->suggestedBindings + info->countSuggestedBindings);
    inputNext = info->next;
    return failMergedSuggestion && suggested.size() > 1 ? XR_ERROR_PATH_UNSUPPORTED : XR_SUCCESS;
}
XrResult XRAPI_CALL InputAttach(XrSession, const XrSessionActionSetsAttachInfo *info)
{
    ++attachCalls;
    attachedSets.assign(info->actionSets, info->actionSets + info->countActionSets);
    inputNext = info->next;
    return failMergedAttach && attachedSets.size() > 1 ? XR_ERROR_RUNTIME_FAILURE : attachResult;
}
XrResult XRAPI_CALL InputSync(XrSession, const XrActionsSyncInfo *info)
{
    synchronizedSets.assign(info->activeActionSets, info->activeActionSets + info->countActiveActionSets);
    inputNext = info->next;
    return syncResult;
}
XrResult XRAPI_CALL InputSpace(XrSession, const XrActionSpaceCreateInfo *, XrSpace *output)
{
    if (failActionSpace)
        return XR_ERROR_RUNTIME_FAILURE;
    *output = reinterpret_cast<XrSpace>(nextInputHandle++);
    return XR_SUCCESS;
}
XrResult XRAPI_CALL InputPose(XrSession, const XrActionStateGetInfo *, XrActionStatePose *value)
{
    value->isActive = XR_TRUE;
    return XR_SUCCESS;
}
XrResult XRAPI_CALL InputBoolean(XrSession, const XrActionStateGetInfo *, XrActionStateBoolean *value)
{
    value->isActive = XR_TRUE;
    value->currentState = controllerButtons ? XR_TRUE : XR_FALSE;
    return XR_SUCCESS;
}
XrResult XRAPI_CALL InputVector(XrSession, const XrActionStateGetInfo *, XrActionStateVector2f *value)
{
    value->isActive = XR_TRUE;
    value->currentState = {0, 0};
    return XR_SUCCESS;
}
class InputFixture
{
    HANDLE file{}, mapping{};

  public:
    std::wstring path;
    InputHeader *header = nullptr;
    explicit InputFixture(const std::wstring &framePath) : path(framePath + L".input")
    {
        file = CreateFileW(path.c_str(), GENERIC_READ | GENERIC_WRITE,
                           FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, CREATE_ALWAYS, 0, nullptr);
        Require(file != INVALID_HANDLE_VALUE, "Create input fixture");
        mapping = CreateFileMappingW(file, nullptr, PAGE_READWRITE, 0, InputCapacity, nullptr);
        Require(mapping != nullptr, "Map input fixture");
        header = static_cast<InputHeader *>(MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, InputCapacity));
        Require(header != nullptr, "View input fixture");
        *header = {};
        header->magic = InputMagic;
        header->version = InputVersion;
        header->generation = 123;
        header->controlTime = Now();
        header->eventSequence = 1; // Recover an interrupted native writer after a game restart.
    }
    void Enable(bool enabled)
    {
        ++header->controlSequence;
        header->enabled = enabled;
        header->controlTime = Now();
        MemoryBarrier();
        ++header->controlSequence;
    }
    bool Has(uint32_t start, PointerKind kind)
    {
        for (uint32_t i = start; i < header->eventCount; ++i)
            if (header->events[i % InputEventCapacity].kind == kind)
                return true;
        return false;
    }
    ~InputFixture()
    {
        UnmapViewOfFile(header);
        CloseHandle(mapping);
        CloseHandle(file);
        DeleteFileW(path.c_str());
    }
};
void TestBindings(Session &session, InputFixture &input)
{
    session.input = std::make_unique<InteractionSession>(input.path);
    auto &instance = *session.instance;
    EnsureInput(instance);
    Require(instance.input && instance.input->ready && suggested.size() == 10,
            "Suggest own Touch bindings even for HOTAS-only games");
    Require(priorities == std::vector<uint32_t>({0, 1000}),
            "Share aim poses and capture buttons only in active high-priority set");
    int chain = 0;
    XrActionSuggestedBinding gameBinding{reinterpret_cast<XrAction>(uintptr_t(44)), 45};
    XrInteractionProfileSuggestedBinding bindings{XR_TYPE_INTERACTION_PROFILE_SUGGESTED_BINDING};
    bindings.next = &chain;
    bindings.interactionProfile = instance.input->touchProfile;
    bindings.countSuggestedBindings = 1;
    bindings.suggestedBindings = &gameBinding;
    Require(LayerSuggestBindings(instance.handle, &bindings) == XR_SUCCESS && suggested.size() == 11 &&
                suggested[0].action == gameBinding.action && inputNext == &chain,
            "Merge app Touch suggestions without replacing game bindings or extension chain");
    XrActionSet gameSet = reinterpret_cast<XrActionSet>(uintptr_t(77));
    XrSessionActionSetsAttachInfo attach{XR_TYPE_SESSION_ACTION_SETS_ATTACH_INFO};
    attach.next = &chain;
    attach.countActionSets = 1;
    attach.actionSets = &gameSet;
    Require(LayerAttachSets(session.handle, &attach) == XR_SUCCESS && attachedSets.size() == 3 &&
                attachedSets[0] == gameSet && inputNext == &chain && instance.inputSessions == 1,
            "Append own sets to OpenComposite's one-shot attachment");
    Require(attach.countActionSets == 1 && attach.actionSets == &gameSet, "Preserve app attach arguments");
    auto firstGeneration = instance.input->sets;
    session.input->Clear();
    --instance.inputSessions;
    EnsureInput(instance);
    Require(instance.input->sets == firstGeneration, "Graphics restart reuses existing action sets");
    Require(LayerSuggestBindings(instance.handle, &bindings) == XR_SUCCESS && instance.input->sets != firstGeneration &&
                destroyedActionSets == 2 && suggested.size() == 11,
            "Replace attached discovery generation when real input bindings follow session restart");
    Require(LayerAttachSets(session.handle, &attach) == XR_SUCCESS, "Attach fresh real-input generation");
    session.input->focused = true;
    XrActiveActionSet active{gameSet, 66};
    XrActionsSyncInfo sync{XR_TYPE_ACTIONS_SYNC_INFO};
    sync.next = &chain;
    sync.countActiveActionSets = 1;
    sync.activeActionSets = &active;
    Require(LayerSyncActions(session.handle, &sync) == XR_SUCCESS && synchronizedSets.size() == 1 &&
                synchronizedSets[0].subactionPath == 66,
            "Inactive mode forwards original game sync");
    Require(input.header->state == 1 && input.header->acknowledgedGeneration == 123 &&
                !(input.header->eventSequence & 1),
            "Handshake input readiness and recover torn writer sequence");
    input.Enable(true);
    Require(LayerSyncActions(session.handle, &sync) == XR_SUCCESS && synchronizedSets.size() == 3 &&
                synchronizedSets[0].actionSet == gameSet && synchronizedSets[0].subactionPath == 66 &&
                inputNext == &chain,
            "Activate own sets alongside unchanged app action sets");
    Require(sync.countActiveActionSets == 1 && sync.activeActionSets == &active, "Preserve app sync arguments");
    syncResult = XR_SESSION_NOT_FOCUSED;
    Require(LayerSyncActions(session.handle, &sync) == XR_SESSION_NOT_FOCUSED, "Preserve positive not-focused result");
    PointerSurface surface{1, {{}, {1, 2, 1}}, 1, 1};
    auto update = session.input->Update(session.local, {&surface, 1}, 10, Now());
    Require(!update.cursor, "Not-focused success code must never generate input");
    syncResult = XR_SUCCESS;
    input.Enable(false);
    LayerSyncActions(session.handle, &sync);
    Require(synchronizedSets.size() == 1, "Toggle off immediately restores game-only input sync");
    input.Enable(true);
    input.header->controlTime = Now() - 4000;
    LayerSyncActions(session.handle, &sync);
    Require(synchronizedSets.size() == 1, "Expired publisher cannot capture game controls");
    input.Enable(true);
    session.input->focused = false;
    LayerSyncActions(session.handle, &sync);
    Require(synchronizedSets.size() == 1, "Unfocused session never activates owned input sets");
    session.input->Clear();
    --instance.inputSessions;
    LayerSuggestBindings(instance.handle, &bindings);
    uint32_t beforeAttach = attachCalls;
    attachResult = XR_SESSION_LOSS_PENDING;
    Require(LayerAttachSets(session.handle, &attach) == XR_SESSION_LOSS_PENDING && attachCalls == beforeAttach + 1 &&
                session.input->setsAttached && instance.inputSessions == 1,
            "Positive session-loss attach succeeds once, retains ownership, and never retries the game call");
    attachResult = XR_SUCCESS;
    // Keep bindings/attach structures immutable even when own action-space setup fails.
    session.input->Clear();
    --instance.inputSessions;
    LayerSuggestBindings(instance.handle, &bindings);
    failActionSpace = true;
    Require(LayerAttachSets(session.handle, &attach) == XR_SUCCESS && session.input->setsAttached &&
                !session.input->attached && instance.inputSessions == 1,
            "Space failure cannot lose ownership of attached immutable sets");
    failActionSpace = false;
    session.input->Clear();
    --instance.inputSessions;
    LayerSuggestBindings(instance.handle, &bindings);
    Require(LayerAttachSets(session.handle, &attach) == XR_SUCCESS, "Recover input on fresh session generation");
    session.input->focused = true;
}
