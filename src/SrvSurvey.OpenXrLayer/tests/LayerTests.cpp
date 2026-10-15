// Includes the implementation to exercise loader/session dispatch against a fake runtime.
// D3D11 pixel uploads use the Windows software (WARP) device, not a headset.
#include "../Layer.cpp"
#include <iostream>
#include <stdexcept>

namespace
{
void Require(bool condition, const char *message)
{
    if (!condition)
        throw std::runtime_error(message);
}
ID3D11Device *testDevice = nullptr;
Session *uploadOwner = nullptr;
bool omitProc = false, advancedChain = false, timeoutNext = false, formatSupported = true;
uint32_t destroyedInstances = 0, destroyedTextures = 0, locateCount = 0, endCount = 0;
uint32_t submittedLayers = 0;
XrCompositionLayerFlags submittedFlags = 0;
XrPosef submittedPose{};
const XrFrameEndInfo *lastInfo = nullptr;
XrApiLayerNextInfo tail{};
struct TestTexture
{
    ID3D11Texture2D *texture = nullptr;
};

#include "InputTestRuntime.h"

XrResult XRAPI_CALL Success()
{
    return XR_SUCCESS;
}
XrResult XRAPI_CALL Create(const XrInstanceCreateInfo *, const XrApiLayerCreateInfo *info, XrInstance *output)
{
    advancedChain = info->nextInfo == &tail;
    *output = reinterpret_cast<XrInstance>(uintptr_t(1));
    return XR_SUCCESS;
}
XrResult XRAPI_CALL Destroy(XrInstance)
{
    ++destroyedInstances;
    return XR_SUCCESS;
}
XrResult XRAPI_CALL Properties(XrInstance, XrInstanceProperties *output)
{
    std::copy_n("VirtualDesktopXR", sizeof("VirtualDesktopXR"), output->runtimeName);
    return XR_SUCCESS;
}
XrResult XRAPI_CALL CreateSession(XrInstance, const XrSessionCreateInfo *, XrSession *output)
{
    *output = reinterpret_cast<XrSession>(uintptr_t(2));
    return XR_SUCCESS;
}
XrResult XRAPI_CALL DestroySession(XrSession)
{
    return XR_SUCCESS;
}
XrResult XRAPI_CALL CreateSpace(XrSession, const XrReferenceSpaceCreateInfo *info, XrSpace *output)
{
    *output = reinterpret_cast<XrSpace>(uintptr_t(info->referenceSpaceType));
    return XR_SUCCESS;
}
XrResult XRAPI_CALL DestroySpace(XrSpace)
{
    return XR_SUCCESS;
}
XrResult XRAPI_CALL Locate(XrSpace, XrSpace, XrTime, XrSpaceLocation *output)
{
    ++locateCount;
    output->locationFlags = XR_SPACE_LOCATION_ORIENTATION_VALID_BIT | XR_SPACE_LOCATION_POSITION_VALID_BIT;
    output->pose = {{0, 0, 0, 1}, {1, 2, 3}};
    return XR_SUCCESS;
}
XrResult XRAPI_CALL Formats(XrSession, uint32_t capacity, uint32_t *count, int64_t *output)
{
    *count = 1;
    if (capacity)
        *output = formatSupported ? DXGI_FORMAT_R8G8B8A8_UNORM_SRGB : DXGI_FORMAT_B8G8R8A8_UNORM;
    return XR_SUCCESS;
}
XrResult XRAPI_CALL CreateTexture(XrSession, const XrSwapchainCreateInfo *info, XrSwapchain *output)
{
    auto texture = std::make_unique<TestTexture>();
    D3D11_TEXTURE2D_DESC desc{};
    desc.Width = info->width;
    desc.Height = info->height;
    desc.MipLevels = desc.ArraySize = 1;
    desc.Format = static_cast<DXGI_FORMAT>(info->format);
    desc.SampleDesc.Count = 1;
    desc.Usage = D3D11_USAGE_DEFAULT;
    desc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
    Require(SUCCEEDED(testDevice->CreateTexture2D(&desc, nullptr, &texture->texture)),
            "Create software swapchain texture");
    *output = reinterpret_cast<XrSwapchain>(texture.release());
    return XR_SUCCESS;
}
XrResult XRAPI_CALL DestroyTexture(XrSwapchain handle)
{
    Require(!uploadOwner || !uploadOwner->uploadsPending, "Complete GPU uploads before destroying a swapchain");
    auto *texture = reinterpret_cast<TestTexture *>(handle);
    texture->texture->Release();
    delete texture;
    ++destroyedTextures;
    return XR_SUCCESS;
}
XrResult XRAPI_CALL Images(XrSwapchain handle, uint32_t capacity, uint32_t *count, XrSwapchainImageBaseHeader *output)
{
    *count = 1;
    if (capacity)
        reinterpret_cast<XrSwapchainImageD3D11KHR *>(output)->texture =
            reinterpret_cast<TestTexture *>(handle)->texture;
    return XR_SUCCESS;
}
XrResult XRAPI_CALL Acquire(XrSwapchain, const XrSwapchainImageAcquireInfo *, uint32_t *index)
{
    *index = 0;
    return XR_SUCCESS;
}
XrResult XRAPI_CALL Wait(XrSwapchain, const XrSwapchainImageWaitInfo *)
{
    if (timeoutNext)
    {
        timeoutNext = false;
        return XR_TIMEOUT_EXPIRED;
    }
    return XR_SUCCESS;
}
XrResult XRAPI_CALL Release(XrSwapchain handle, const XrSwapchainImageReleaseInfo *)
{
    auto *texture = reinterpret_cast<TestTexture *>(handle);
    D3D11_TEXTURE2D_DESC desc{};
    texture->texture->GetDesc(&desc);
    desc.Usage = D3D11_USAGE_STAGING;
    desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    desc.BindFlags = 0;
    ID3D11Texture2D *staging = nullptr;
    Require(SUCCEEDED(testDevice->CreateTexture2D(&desc, nullptr, &staging)), "Create staging readback");
    ID3D11DeviceContext *context = nullptr;
    testDevice->GetImmediateContext(&context);
    context->CopyResource(staging, texture->texture);
    D3D11_MAPPED_SUBRESOURCE mapped{};
    Require(SUCCEEDED(context->Map(staging, 0, D3D11_MAP_READ, 0, &mapped)), "Read uploaded pixels");
    const auto *pixel = static_cast<const uint8_t *>(mapped.pData);
    if (desc.Width == 1)
        Require(pixel[0] == 255 && pixel[1] == 128 && pixel[2] == 64 && pixel[3] == 127,
                "Preserve straight sRGB, scale only alpha");
    else
    {
        const auto *center = pixel + 16 * mapped.RowPitch + 16 * 4;
        Require(desc.Width == 32 && center[0] == 32 && center[1] == 200 && center[3] == 255 && pixel[3] == 0,
                "Upload visible cyan cursor with transparent corners");
    }
    context->Unmap(staging, 0);
    staging->Release();
    context->Release();
    return XR_SUCCESS;
}
XrResult XRAPI_CALL EndFrame(XrSession, const XrFrameEndInfo *info)
{
    ++endCount;
    lastInfo = info;
    submittedLayers = info->layerCount;
    if (info->layerCount == 2)
    {
        const auto *quad = reinterpret_cast<const XrCompositionLayerQuad *>(info->layers[1]);
        Require(quad->type == XR_TYPE_COMPOSITION_LAYER_QUAD, "Append a quad after game layers");
        submittedFlags = quad->layerFlags;
        submittedPose = quad->pose;
    }
    return XR_SUCCESS;
}
XrResult XRAPI_CALL GetProc(XrInstance, const char *name, PFN_xrVoidFunction *output)
{
    if (omitProc && std::strcmp(name, "xrCreateSession") == 0)
        return XR_ERROR_FUNCTION_UNSUPPORTED;
#define PROC(api, function)                                                                                            \
    if (std::strcmp(name, api) == 0)                                                                                   \
    {                                                                                                                  \
        *output = reinterpret_cast<PFN_xrVoidFunction>(function);                                                      \
        return XR_SUCCESS;                                                                                             \
    }
    PROC("xrStringToPath", InputPath)
    PROC("xrCreateActionSet", InputCreateSet)
    PROC("xrDestroyActionSet", InputDestroySet)
    PROC("xrCreateAction", InputCreateAction)
    PROC("xrSuggestInteractionProfileBindings", InputSuggest)
    PROC("xrAttachSessionActionSets", InputAttach)
    PROC("xrSyncActions", InputSync)
    PROC("xrCreateActionSpace", InputSpace)
    PROC("xrGetActionStatePose", InputPose)
    PROC("xrGetActionStateBoolean", InputBoolean)
    PROC("xrGetActionStateVector2f", InputVector)
    PROC("xrDestroyInstance", Destroy)
    PROC("xrGetInstanceProperties", Properties)
    PROC("xrCreateSession", CreateSession)
    PROC("xrDestroySession", DestroySession)
    PROC("xrCreateReferenceSpace", CreateSpace)
    PROC("xrDestroySpace", DestroySpace)
    PROC("xrLocateSpace", Locate)
    PROC("xrEnumerateSwapchainFormats", Formats)
    PROC("xrCreateSwapchain", CreateTexture)
    PROC("xrDestroySwapchain", DestroyTexture)
    PROC("xrEnumerateSwapchainImages", Images)
    PROC("xrAcquireSwapchainImage", Acquire)
    PROC("xrWaitSwapchainImage", Wait)
    PROC("xrReleaseSwapchainImage", Release)
    PROC("xrEndFrame", EndFrame)
#undef PROC
    *output = reinterpret_cast<PFN_xrVoidFunction>(Success);
    return XR_SUCCESS;
}
void WritePacket(const std::wstring &path, uint32_t sequence, uint32_t count = 1, int64_t timestamp = Now())
{
    std::vector<uint8_t> bytes(sizeof(Header) + count * (sizeof(Descriptor) + 4));
    Header header{};
    header.magic = Magic;
    header.version = Version;
    header.sequence = sequence;
    header.length = static_cast<uint32_t>(bytes.size());
    header.timestamp = timestamp;
    header.count = count;
    std::memcpy(bytes.data(), &header, sizeof(header));
    if (count)
    {
        Descriptor d{};
        d.id = 1;
        d.width = d.height = 1;
        d.offset = sizeof(Header) + sizeof(Descriptor);
        d.length = 4;
        d.widthMeters = d.heightMeters = 1;
        d.position[2] = -2;
        d.orientation[3] = 1;
        d.alpha = 0.5f;
        std::memcpy(bytes.data() + sizeof(Header), &d, sizeof(d));
        uint8_t pixels[] = {255, 128, 64, 255};
        std::memcpy(bytes.data() + d.offset, pixels, sizeof(pixels));
    }
    HANDLE file = CreateFileW(path.c_str(), GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr,
                              OPEN_ALWAYS, 0, nullptr);
    Require(file != INVALID_HANDLE_VALUE, "Open test mailbox");
    LARGE_INTEGER size{};
    GetFileSizeEx(file, &size);
    if (size.QuadPart != Capacity)
    {
        size.QuadPart = Capacity;
        SetFilePointerEx(file, size, nullptr, FILE_BEGIN);
        Require(SetEndOfFile(file), "Size mailbox");
    }
    size.QuadPart = 0;
    SetFilePointerEx(file, size, nullptr, FILE_BEGIN);
    DWORD written = 0;
    Require(WriteFile(file, bytes.data(), static_cast<DWORD>(bytes.size()), &written, nullptr), "Publish fixture");
    CloseHandle(file);
}
} // namespace

int main()
{
    try
    {
        XrNegotiateLoaderInfo loader{XR_LOADER_INTERFACE_STRUCT_LOADER_INFO,
                                     XR_LOADER_INFO_STRUCT_VERSION,
                                     sizeof(XrNegotiateLoaderInfo),
                                     1,
                                     1,
                                     XR_MAKE_VERSION(1, 0, 0),
                                     XR_CURRENT_API_VERSION};
        XrNegotiateApiLayerRequest request{XR_LOADER_INTERFACE_STRUCT_API_LAYER_REQUEST,
                                           XR_API_LAYER_INFO_STRUCT_VERSION, sizeof(XrNegotiateApiLayerRequest)};
        Require(xrNegotiateLoaderApiLayerInterface(&loader, LayerName, &request) == XR_SUCCESS,
                "Negotiate loader interface");
        Require(xrNegotiateLoaderApiLayerInterface(&loader, "wrong", &request) == XR_ERROR_INITIALIZATION_FAILED,
                "Reject wrong layer");
        XrApiLayerNextInfo next{XR_LOADER_INTERFACE_STRUCT_API_LAYER_NEXT_INFO, XR_API_LAYER_NEXT_INFO_STRUCT_VERSION,
                                sizeof(XrApiLayerNextInfo)};
        std::copy_n(LayerName, std::strlen(LayerName) + 1, next.layerName);
        next.nextGetInstanceProcAddr = GetProc;
        next.nextCreateApiLayerInstance = Create;
        next.next = &tail;
        XrApiLayerCreateInfo layer{XR_LOADER_INTERFACE_STRUCT_API_LAYER_CREATE_INFO,
                                   XR_API_LAYER_CREATE_INFO_STRUCT_VERSION, sizeof(XrApiLayerCreateInfo)};
        layer.nextInfo = &next;
        XrInstanceCreateInfo createInfo{XR_TYPE_INSTANCE_CREATE_INFO};
        XrInstance instanceHandle{};
        omitProc = true;
        Require(LayerCreateApiLayerInstance(&createInfo, &layer, &instanceHandle) == XR_ERROR_INITIALIZATION_FAILED,
                "Fail bad dispatch");
        Require(destroyedInstances == 1 && instanceHandle == XR_NULL_HANDLE,
                "Release downstream instance on layer init failure");
        omitProc = false;
        Require(LayerCreateApiLayerInstance(&createInfo, &layer, &instanceHandle) == XR_SUCCESS && advancedChain,
                "Advance copied loader chain");
        Require(layer.nextInfo == &next, "Preserve caller chain");
        auto instance = FindInstance(instanceHandle);
        Require(instance && instance->vdxr, "Detect actual VDXR runtime");
        XrSessionCreateInfo sessionInfo{XR_TYPE_SESSION_CREATE_INFO};
        XrSession sessionHandle{};
        Require(LayerCreateSession(instanceHandle, &sessionInfo, &sessionHandle) == XR_SUCCESS,
                "Track created session");
        Require(LayerDestroySession(sessionHandle) == XR_SUCCESS, "Destroy passthrough session");

        UINT levelCount = 0;
        (void)levelCount;
        Require(SUCCEEDED(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, 0, nullptr, 0, D3D11_SDK_VERSION,
                                            &testDevice, nullptr, nullptr)),
                "Create D3D11 WARP device");
        wchar_t directory[MAX_PATH]{};
        GetTempPathW(MAX_PATH, directory);
        std::wstring path =
            std::wstring(directory) + L"SrvSurvey-OpenXR-" + std::to_wstring(GetCurrentProcessId()) + L".bin";
        WritePacket(path, 2);
        auto session = std::make_shared<Session>(path);
        uploadOwner = session.get();
        session->handle = reinterpret_cast<XrSession>(uintptr_t(3));
        session->instance = instance;
        instance->elite = true;
        session->device = testDevice;
        testDevice->AddRef();
        testDevice->GetImmediateContext(&session->context);
        session->running = true;
        session->shouldRender = true;
        session->maximumLayers = 2;
        sessions.emplace(session->handle, session);
        XrCompositionLayerProjection game{XR_TYPE_COMPOSITION_LAYER_PROJECTION};
        const XrCompositionLayerBaseHeader *gameLayers[] = {
            reinterpret_cast<const XrCompositionLayerBaseHeader *>(&game)};
        XrFrameEndInfo frame{XR_TYPE_FRAME_END_INFO};
        frame.displayTime = 1;
        frame.layerCount = 1;
        frame.layers = gameLayers;
        timeoutNext = true;
        Require(LayerEndFrame(session->handle, &frame) == XR_SUCCESS && submittedLayers == 1,
                "Timeout defers upload without blocking game");
        Require(LayerEndFrame(session->handle, &frame) == XR_SUCCESS && submittedLayers == 2,
                "Retry acquired image and append panel");
        Require(session->uploadsPending, "Defer GPU completion waits until resource retirement");
        Require((submittedFlags & XR_COMPOSITION_LAYER_UNPREMULTIPLIED_ALPHA_BIT) != 0,
                "Correct straight-alpha composition flag");
        Require(submittedPose.position.x == 1 && submittedPose.position.y == 2 && submittedPose.position.z == 1,
                "Anchor calibration to captured headset pose");
        Require(frame.layerCount == 1 && frame.layers == gameLayers, "Never mutate game's frame info");
        Require(LayerEndFrame(session->handle, &frame) == XR_SUCCESS && locateCount == 1,
                "Reuse unchanged textures and anchor");
        WritePacket(path, 3); // Odd sequence simulates a writer in flight.
        Require(LayerEndFrame(session->handle, &frame) == XR_SUCCESS && lastInfo == &frame,
                "Pass through torn snapshot");
        WritePacket(path, 4);
        session->shouldRender = false;
        Require(LayerEndFrame(session->handle, &frame) == XR_SUCCESS && lastInfo == &frame,
                "Respect shouldRender=false");
        session->shouldRender = true;
        session->pendingSpaceChange = 10;
        frame.displayTime = 9;
        LayerEndFrame(session->handle, &frame);
        Require(locateCount == 1, "Do not recenter on transient suppression or before space change time");
        frame.displayTime = 10;
        LayerEndFrame(session->handle, &frame);
        Require(locateCount == 2, "Recenter when reference-space change takes effect");
        WritePacket(path, 6, 1, Now() - 3000);
        Require(LayerEndFrame(session->handle, &frame) == XR_SUCCESS && lastInfo == &frame,
                "Expire abandoned overlay packets");
        WritePacket(path, 8, 0);
        LayerEndFrame(session->handle, &frame);
        Require(destroyedTextures == 1 && submittedLayers == 1, "Remove stale overlay resources");
        Require(!session->uploadsPending, "Wait for GPU completion when retiring a panel");
        WritePacket(path, 10);
        {
            InputFixture input(path);
            TestBindings(*session, input);
            session->maximumLayers = 3;
            input.Enable(true);
            XrActiveActionSet appSet{reinterpret_cast<XrActionSet>(uintptr_t(77)), XR_NULL_PATH};
            XrActionsSyncInfo sync{XR_TYPE_ACTIONS_SYNC_INFO};
            sync.countActiveActionSets = 1;
            sync.activeActionSets = &appSet;
            LayerSyncActions(session->handle, &sync);
            Require(LayerEndFrame(session->handle, &frame) == XR_SUCCESS && submittedLayers == 3,
                    "Compose cursor above panel while preserving game projection");
            uint32_t start = input.header->eventCount;
            controllerButtons = 1;
            LayerSyncActions(session->handle, &sync);
            LayerEndFrame(session->handle, &frame);
            Require(input.Has(start, PointerKind::LeftDown), "Return controller click through native mailbox");
            start = input.header->eventCount;
            input.Enable(false);
            LayerSyncActions(session->handle, &sync);
            LayerEndFrame(session->handle, &frame);
            Require(input.Has(start, PointerKind::Reset) && submittedLayers == 2,
                    "Disabling mode cancels held click and removes cursor");
            controllerButtons = 0;
            session->input->Clear();
            --instance->inputSessions;
            // A rejected augmentation must preserve the application's original suggestion.
            EnsureInput(*instance, true);
            XrActionSuggestedBinding gameBinding{reinterpret_cast<XrAction>(uintptr_t(44)), 45};
            XrInteractionProfileSuggestedBinding bindings{XR_TYPE_INTERACTION_PROFILE_SUGGESTED_BINDING};
            bindings.interactionProfile = instance->input->touchProfile;
            bindings.countSuggestedBindings = 1;
            bindings.suggestedBindings = &gameBinding;
            failMergedSuggestion = true;
            Require(LayerSuggestBindings(instanceHandle, &bindings) == XR_SUCCESS && suggested.size() == 1 &&
                        !instance->input->ready,
                    "Binding augmentation failure falls back to original game bindings");
            failMergedSuggestion = false;
            instance->input.reset();
            EnsureInput(*instance);
            XrActionSet gameSet = reinterpret_cast<XrActionSet>(uintptr_t(77));
            XrSessionActionSetsAttachInfo attach{XR_TYPE_SESSION_ACTION_SETS_ATTACH_INFO};
            attach.countActionSets = 1;
            attach.actionSets = &gameSet;
            uint32_t beforeAttach = attachCalls;
            failMergedAttach = true;
            Require(LayerAttachSets(session->handle, &attach) == XR_SUCCESS && attachCalls == beforeAttach + 2 &&
                        attachedSets == std::vector<XrActionSet>({gameSet}) && instance->inputSessions == 0,
                    "Rejected owned action sets retry only the original game attachment without claiming ownership");
            failMergedAttach = false;
        }
        WritePacket(path, 12, 0);
        LayerEndFrame(session->handle, &frame);
        WritePacket(path, 14);
        formatSupported = false;
        Require(LayerEndFrame(session->handle, &frame) == XR_SUCCESS && lastInfo == &frame,
                "Unsupported overlay format forwards the original game frame");
        Require(session->lastError == XR_ERROR_SWAPCHAIN_FORMAT_UNSUPPORTED, "Keep the native failure code");
        Require(LayerEndFrame(session->handle, &frame) == XR_SUCCESS && lastInfo == &frame,
                "A failed overlay remains passive on subsequent frames");
        Require(LayerDestroySession(session->handle) == XR_SUCCESS, "Release session resources before runtime destroy");
        session.reset();
        uploadOwner = nullptr;
        DeleteFileW(path.c_str());
        testDevice->Release();
        testDevice = nullptr;
        Require(LayerDestroyInstance(instanceHandle) == XR_SUCCESS && destroyedInstances == 2,
                "Release instance dispatch");
        std::cout << "Loader, D3D11 pixels, frame forwarding, placement, cleanup, action generation, binding/sync "
                     "merges, input cancellation and cursor tests passed\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
