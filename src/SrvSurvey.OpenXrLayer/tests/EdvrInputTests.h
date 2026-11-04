// Included by LayerTests after its fake runtime and frame fixtures.
namespace
{
class EdvrFrameFixture
{
    bool originalIdentity;
    const XrFrameEndInfo &frame;

  public:
    int64_t now = 100000;
    std::wstring path;
    InputFixture input;
    std::shared_ptr<Session> session;

    EdvrFrameFixture(const std::shared_ptr<Instance> &instance, const XrFrameEndInfo &frame,
                     const std::wstring &basePath, bool optIn = true, bool recognized = true)
        : originalIdentity(instance->edvr), frame(frame), path(basePath + L".edvr"),
          input(path, [this] { return now; })
    {
        Require(instance->inputSessions == 0, "Previous session released its action attachment");
        formatSupported = true;
        failActionSpace = failMergedAttach = false;
        attachResult = syncResult = XR_SUCCESS;
        controllerButtons = 0;
        pendingSessionState.reset();
        attachedSets.clear();
        synchronizedSets.clear();
        headPose = {{0, 0, 0, 1}, {1, 2, 3}};
        WritePacket(path, 2, {TestPanel{}}, now);
        input.EnableEdvr(optIn);
        instance->edvr = originalIdentity && recognized;
        instance->gameInputObserved = false;
        instance->input.reset();
        StartSession(instance);
    }
    void StartSession(const std::shared_ptr<Instance> &instance)
    {
        session = std::make_shared<Session>(path, [this] { return now; });
        session->handle = reinterpret_cast<XrSession>(uintptr_t(5));
        session->instance = instance;
        session->device = testDevice;
        testDevice->AddRef();
        testDevice->GetImmediateContext(&session->context);
        session->shouldRender = true;
        session->maximumLayers = 3;
        sessions.emplace(session->handle, session);
        uploadOwner = session.get();
        XrSessionBeginInfo begin{XR_TYPE_SESSION_BEGIN_INFO};
        Require(LayerBeginSession(session->handle, &begin) == XR_SUCCESS, "Start EDVR fixture session");
        Focus(true);
    }
    void RecreateSession()
    {
        auto instance = session->instance;
        Require(LayerDestroySession(session->handle) == XR_SUCCESS, "Destroy the first EDVR session");
        session.reset();
        attachedSets.clear();
        synchronizedSets.clear();
        StartSession(instance);
    }
    ~EdvrFrameFixture()
    {
        auto instance = session->instance;
        LayerDestroySession(session->handle);
        session.reset();
        uploadOwner = nullptr;
        instance->edvr = originalIdentity;
        DeleteFileW(path.c_str());
    }
    void Focus(bool focused)
    {
        XrEventDataSessionStateChanged state{XR_TYPE_EVENT_DATA_SESSION_STATE_CHANGED};
        state.session = session->handle;
        state.state = focused ? XR_SESSION_STATE_FOCUSED : XR_SESSION_STATE_VISIBLE;
        pendingSessionState = state;
        XrEventDataBuffer event{XR_TYPE_EVENT_DATA_BUFFER};
        Require(LayerPollEvent(session->instance->handle, &event) == XR_SUCCESS,
                "Deliver session focus event");
    }
    void Frame()
    {
        Require(LayerEndFrame(session->handle, &frame) == XR_SUCCESS, "Preserve EDVR frame submission");
    }
};

void TestEdvrFrameDrivenInput(const std::shared_ptr<Instance> &instance, const XrFrameEndInfo &frame,
                              const std::wstring &path)
{
    EdvrFrameFixture fixture(instance, frame, path);
    auto &input = fixture.input;
    fixture.Frame();
    Require(submittedLayers == 2 && input.header->state == 1 && input.header->inputSource == 2,
            "EDVR input becomes ready while off without any game action attach or sync calls");
    uint32_t attached = attachCalls;
    input.Enable(true);
    controllerButtons = 1;
    uint32_t start = input.header->eventCount;
    fixture.Frame();
    Require(!input.Has(start, PointerKind::LeftDown),
            "Held buttons cannot activate a panel when enabling EDVR input");
    controllerButtons = 0;
    syncResult = XR_SESSION_NOT_FOCUSED;
    fixture.Frame();
    Require(submittedLayers == 2 && input.header->error == XR_SUCCESS,
            "A positive not-focused sync result suspends the cursor without a permanent input error");
    syncResult = XR_SUCCESS;
    fixture.Frame();
    Require(submittedLayers == 3, "Frame-driven input draws the controller cursor");
    controllerButtons = 1;
    fixture.Frame();
    Require(input.Has(start, PointerKind::LeftDown), "EDVR frame loop returns a controller click");
    start = input.header->eventCount;
    fixture.Focus(false);
    fixture.Frame();
    Require(input.Has(start, PointerKind::Reset) && synchronizedSets.empty() && submittedLayers == 2,
            "Focus loss cancels captured input and deactivates owned actions");
    fixture.Focus(true);
    start = input.header->eventCount;
    fixture.Frame();
    Require(!input.Has(start, PointerKind::LeftDown), "Focus recovery requires release before rearming");
    controllerButtons = 0;
    fixture.Frame();
    controllerButtons = 1;
    fixture.Frame();
    start = input.header->eventCount;
    input.Enable(false);
    fixture.Frame();
    Require(input.Has(start, PointerKind::Reset) && synchronizedSets.empty() && submittedLayers == 2,
            "Disabling EDVR interaction cancels held input and deactivates owned actions");
    controllerButtons = 0;
    input.Enable(true);
    fixture.Frame();
    fixture.now += 3001;
    WritePacket(fixture.path, 4, {TestPanel{}}, fixture.now);
    fixture.Frame();
    Require(synchronizedSets.empty() && submittedLayers == 2 && input.header->state == 0,
            "Stale publisher control cannot keep independent actions active");
    input.Enable(true);
    fixture.Frame();
    Require(submittedLayers == 3, "A fresh publisher can reconnect to the existing EDVR attachment");
    Require(LayerEndSession(fixture.session->handle) == XR_SUCCESS && synchronizedSets.empty(),
            "Stopping a session deactivates owned input");
    XrSessionBeginInfo begin{XR_TYPE_SESSION_BEGIN_INFO};
    Require(LayerBeginSession(fixture.session->handle, &begin) == XR_SUCCESS,
            "Restart the same EDVR session");
    fixture.Focus(true);
    fixture.Frame();
    Require(attachCalls == attached && submittedLayers == 3, "Stop/start preserves the immutable attachment");
    input.EnableEdvr(false);
    fixture.Frame();
    Require(synchronizedSets.empty() && submittedLayers == 2 && input.header->state == 0,
            "Disabling the experimental source stops its pointer without detaching action sets");
}

void TestEdvrInputGatesAndFailures(const std::shared_ptr<Instance> &instance, const XrFrameEndInfo &frame,
                                   const std::wstring &path)
{
    for (bool identity : {false, true})
    {
        EdvrFrameFixture fixture(instance, frame, path, !identity, identity);
        fixture.Frame();
        Require(submittedLayers == 2 && fixture.input.header->state == 0 && attachedSets.empty(),
                "Explicit opt-in and recognized EDVR identity are both required");
    }
    {
        EdvrFrameFixture fixture(instance, frame, path);
        ++fixture.input.header->generation;
        fixture.input.Enable(false);
        fixture.Frame();
        Require(submittedLayers == 2 && fixture.input.header->state == 0 && attachedSets.empty(),
                "A stale opt-in from an older publisher cannot initialize EDVR controller input");
    }
    for (int failure : {0, 1, 2})
    {
        EdvrFrameFixture fixture(instance, frame, path);
        attachResult = failure == 0 ? XR_ERROR_RUNTIME_FAILURE : XR_SUCCESS;
        failActionSpace = failure == 1;
        syncResult = failure == 2 ? XR_ERROR_RUNTIME_FAILURE : XR_SUCCESS;
        fixture.Frame();
        fixture.Frame();
        Require(submittedLayers == 2 && fixture.input.header->state == 2 &&
                    fixture.input.header->error == XR_ERROR_RUNTIME_FAILURE,
                "Attach, action-space and sync errors preserve rendering without a cursor");
    }
    failActionSpace = false;
    attachResult = syncResult = XR_SUCCESS;
}

void TestEdvrRenderingFailureReleasesControllerActions(const std::shared_ptr<Instance> &instance,
                                                       const XrFrameEndInfo &frame, const std::wstring &path)
{
    EdvrFrameFixture fixture(instance, frame, path);
    fixture.Frame();
    fixture.input.Enable(true);
    fixture.Frame();
    Require(submittedLayers == 3, "EDVR capture is active before the graphics failure");
    formatSupported = false;
    TestPanel changed;
    changed.id = 2;
    WritePacket(fixture.path, 4, {changed}, fixture.now);
    fixture.Frame();
    Require(submittedLayers == 1 && synchronizedSets.empty(),
            "A graphics failure forwards the game frame and deactivates EDVR controller actions");
    fixture.Frame();
    Require(submittedLayers == 1 && synchronizedSets.empty(),
            "A failed overlay never reactivates controller capture on later frames");
    formatSupported = true;
}

void TestEdvrPreservesApplicationInput(const std::shared_ptr<Instance> &instance, const XrFrameEndInfo &frame,
                                       const std::wstring &path)
{
    EdvrFrameFixture fixture(instance, frame, path);
    XrActionSet gameSet = reinterpret_cast<XrActionSet>(uintptr_t(77));
    XrSessionActionSetsAttachInfo attach{XR_TYPE_SESSION_ACTION_SETS_ATTACH_INFO};
    attach.countActionSets = 1;
    attach.actionSets = &gameSet;
    Require(LayerAttachSets(fixture.session->handle, &attach) == XR_SUCCESS,
            "Attach application-owned action sets");
    XrActiveActionSet active{gameSet, XR_NULL_PATH};
    XrActionsSyncInfo sync{XR_TYPE_ACTIONS_SYNC_INFO};
    sync.countActiveActionSets = 1;
    sync.activeActionSets = &active;
    Require(LayerSyncActions(fixture.session->handle, &sync) == XR_SUCCESS, "Synchronize application input");
    fixture.Frame();
    Require(fixture.input.header->inputSource == 1 && synchronizedSets.size() == 1 &&
                synchronizedSets[0].actionSet == gameSet && submittedLayers == 2,
            "An EDVR identity with application actions retains the application-driven path");
    fixture.input.Enable(true);
    LayerSyncActions(fixture.session->handle, &sync);
    fixture.Frame();
    Require(submittedLayers == 3 && synchronizedSets.size() == 3 && synchronizedSets[0].actionSet == gameSet,
            "Frame submission never overwrites the application's active action sets");
}

void TestEdvrPriorApplicationInputSurvivesSessionRecreation(const std::shared_ptr<Instance> &instance,
                                                            const XrFrameEndInfo &frame,
                                                            const std::wstring &path, bool throughSync)
{
    EdvrFrameFixture fixture(instance, frame, path);
    if (throughSync)
    {
        XrActionsSyncInfo sync{XR_TYPE_ACTIONS_SYNC_INFO};
        Require(LayerSyncActions(fixture.session->handle, &sync) == XR_SUCCESS,
                "Observe application synchronization in the first session");
    }
    else
    {
        XrActionSet gameSet = reinterpret_cast<XrActionSet>(uintptr_t(77));
        XrSessionActionSetsAttachInfo attach{XR_TYPE_SESSION_ACTION_SETS_ATTACH_INFO};
        attach.countActionSets = 1;
        attach.actionSets = &gameSet;
        Require(LayerAttachSets(fixture.session->handle, &attach) == XR_SUCCESS,
                "Observe application attachment in the first session");
    }
    fixture.RecreateSession();
    uint32_t beforeFrame = attachCalls;
    fixture.Frame();
    Require(attachCalls == beforeFrame && submittedLayers == 2,
            throughSync ? "Prior game sync prevents independent attachment in a recreated session"
                        : "Prior game attachment prevents independent attachment in a recreated session");
}

void TestEdvrSuggestedApplicationBindingsPreventIndependentAttachment(
    const std::shared_ptr<Instance> &instance, const XrFrameEndInfo &frame, const std::wstring &path)
{
    EdvrFrameFixture fixture(instance, frame, path);
    EnsureInput(*instance, true);
    XrActionSuggestedBinding gameBinding{reinterpret_cast<XrAction>(uintptr_t(44)), 45};
    XrInteractionProfileSuggestedBinding bindings{XR_TYPE_INTERACTION_PROFILE_SUGGESTED_BINDING};
    bindings.interactionProfile = instance->input->touchProfile;
    bindings.countSuggestedBindings = 1;
    bindings.suggestedBindings = &gameBinding;
    Require(LayerSuggestBindings(instance->handle, &bindings) == XR_SUCCESS,
            "Suggest application bindings before any session attachment");
    uint32_t beforeFrame = attachCalls;
    fixture.Frame();
    Require(attachCalls == beforeFrame && submittedLayers == 2,
            "Application binding suggestions alone prevent independent EDVR attachment");
}

void TestEdvrFrameServicesPendingHeadPoseRequest(const std::shared_ptr<Instance> &instance,
                                                 const XrFrameEndInfo &frame, const std::wstring &path)
{
    EdvrFrameFixture fixture(instance, frame, path, false);
    WritePacket(fixture.path, 4, {TestPanel{}}, fixture.now, 7);
    fixture.Frame();
    HANDLE file = CreateFileW(fixture.path.c_str(), GENERIC_READ | GENERIC_WRITE,
                              FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, 0, nullptr);
    Require(file != INVALID_HANDLE_VALUE, "Open frame-driven pose telemetry fixture");
    HANDLE mapping = CreateFileMappingW(file, nullptr, PAGE_READWRITE, 0, 0, nullptr);
    Require(mapping != nullptr, "Map frame-driven pose telemetry fixture");
    auto *header = static_cast<Header *>(MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(Header)));
    Require(header != nullptr, "View frame-driven pose telemetry fixture");
    header->poseRequestTimestamp = 100000;
    headPose = {HeadOrientation(1.57079632679f, 0.3f, -0.2f), {2, 3, 3}};
    fixture.Frame();
    Require(header->poseTimestamp == 100000 && header->poseOriginRevision == 7 &&
                std::abs(header->headPosition[0] - 1) < 0.001f &&
                std::abs(header->headPosition[1] - 1) < 0.001f &&
                std::abs(header->headPosition[2]) < 0.001f &&
                std::abs(header->headYaw - 1.57079632679f) < 0.001f,
            "A pending request publishes the located head pose through the frame loop");
    UnmapViewOfFile(header);
    CloseHandle(mapping);
    CloseHandle(file);
}

void TestEdvrLateApplicationInput(const std::shared_ptr<Instance> &instance, const XrFrameEndInfo &frame,
                                  const std::wstring &path)
{
    for (bool lateAttach : {true, false})
    {
        EdvrFrameFixture fixture(instance, frame, path);
        fixture.Frame();
        fixture.input.Enable(true);
        fixture.Frame();
        controllerButtons = 1;
        fixture.Frame();
        uint32_t start = fixture.input.header->eventCount;
        XrActionSet gameSet = reinterpret_cast<XrActionSet>(uintptr_t(77));
        if (lateAttach)
        {
            XrSessionActionSetsAttachInfo attach{XR_TYPE_SESSION_ACTION_SETS_ATTACH_INFO};
            attach.countActionSets = 1;
            attach.actionSets = &gameSet;
            attachResult = XR_ERROR_ACTIONSETS_ALREADY_ATTACHED;
            Require(LayerAttachSets(fixture.session->handle, &attach) ==
                            XR_ERROR_ACTIONSETS_ALREADY_ATTACHED &&
                        attachedSets.size() == 1 && attachedSets[0] == gameSet,
                    "Expose the late-attachment runtime error without rewriting the application's request");
        }
        else
        {
            XrActiveActionSet active{gameSet, XR_NULL_PATH};
            XrActionsSyncInfo sync{XR_TYPE_ACTIONS_SYNC_INFO};
            sync.countActiveActionSets = 1;
            sync.activeActionSets = &active;
            syncResult = XR_ERROR_ACTIONSET_NOT_ATTACHED;
            Require(LayerSyncActions(fixture.session->handle, &sync) == XR_ERROR_ACTIONSET_NOT_ATTACHED &&
                        synchronizedSets.size() == 1 && synchronizedSets[0].actionSet == gameSet,
                    "Forward a late game sync unchanged rather than merging or replacing its actions");
        }
        fixture.Frame();
        Require(submittedLayers == 2 && fixture.input.header->state == 2 &&
                    fixture.input.Has(start, PointerKind::Reset),
                "A late application input conflict cancels capture and leaves panels rendering");
        if (!lateAttach)
            Require(synchronizedSets.size() == 1 && synchronizedSets[0].actionSet == gameSet,
                    "Subsequent frames never overwrite the late application's sync request");
    }
    attachResult = syncResult = XR_SUCCESS;
    controllerButtons = 0;
}

} // namespace
