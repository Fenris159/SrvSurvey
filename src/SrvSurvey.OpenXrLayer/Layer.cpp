// SPDX-License-Identifier: GPL-3.0-or-later
// Original SrvSurvey implementation. OpenXR declarations are supplied by the pinned Khronos SDK.
#include "FrameProtocol.h"
#include "OpenXrInteraction.h"
#include <algorithm>
#include <atomic>
#include <d3d11.h>
#include <map>
#include <memory>
#include <mutex>
#include <openxr/openxr.h>
#include <openxr/openxr_loader_negotiation.h>
#include <openxr/openxr_platform.h>
#include <shlobj.h>
#include <string>
#include <vector>
#include <windows.h>

namespace
{
using namespace srvsurvey;
constexpr const char *LayerName = "XR_APILAYER_SRVSURVEY_overlay";
int64_t Now()
{
    FILETIME time{};
    GetSystemTimeAsFileTime(&time);
    ULARGE_INTEGER ticks{};
    ticks.LowPart = time.dwLowDateTime;
    ticks.HighPart = time.dwHighDateTime;
    return static_cast<int64_t>(ticks.QuadPart / 10000 - 11644473600000ULL);
}

class Mailbox
{
    HANDLE file = INVALID_HANDLE_VALUE, mapping = nullptr;
    uint8_t *data = nullptr;
    std::wstring path;
    int64_t nextOpenAttempt = 0;
    uint32_t cachedSequence = 0;
    bool cacheValid = false;

  public:
    explicit Mailbox(std::wstring customPath = {}) : path(std::move(customPath))
    {
    }
    ~Mailbox()
    {
        if (data)
            UnmapViewOfFile(data);
        if (mapping)
            CloseHandle(mapping);
        if (file != INVALID_HANDLE_VALUE)
            CloseHandle(file);
    }
    bool Open()
    {
        if (data)
            return true;
        if (Now() < nextOpenAttempt)
            return false;
        nextOpenAttempt = Now() + 1000;
        if (path.empty())
        {
            PWSTR folder = nullptr;
            if (FAILED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &folder)))
                return false;
            path = folder;
            CoTaskMemFree(folder);
            path += L"\\SrvSurvey\\OpenXR\\frames.bin";
        }
        HANDLE candidate =
            CreateFileW(path.c_str(), GENERIC_READ | GENERIC_WRITE,
                        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, 0, nullptr);
        if (candidate == INVALID_HANDLE_VALUE)
            return false;
        LARGE_INTEGER size{};
        if (!GetFileSizeEx(candidate, &size) || size.QuadPart != Capacity)
        {
            CloseHandle(candidate);
            return false;
        }
        HANDLE mapped = CreateFileMappingW(candidate, nullptr, PAGE_READWRITE, 0, 0, nullptr);
        if (!mapped)
        {
            CloseHandle(candidate);
            return false;
        }
        auto *bytes = static_cast<uint8_t *>(MapViewOfFile(mapped, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, Capacity));
        if (!bytes)
        {
            CloseHandle(mapped);
            CloseHandle(candidate);
            return false;
        }
        file = candidate;
        mapping = mapped;
        data = bytes;
        return true;
    }
    bool Read(std::vector<uint8_t> &packet)
    {
        if (!Open())
            return false;
        auto *sequence = reinterpret_cast<volatile LONG *>(data + 8);
        for (int attempt = 0; attempt < 3; ++attempt)
        {
            LONG before = InterlockedCompareExchange(sequence, 0, 0);
            if (before & 1)
                continue;
            Header current{};
            std::memcpy(&current, data, sizeof(current));
            int64_t now = Now();
            if (current.magic != Magic || current.version != Version || current.timestamp <= 0 ||
                now < current.timestamp || now - current.timestamp > 2000)
                return false;
            if (cacheValid)
            {
                if (cachedSequence == static_cast<uint32_t>(before) &&
                    before == InterlockedCompareExchange(sequence, 0, 0))
                    return true;
            }
            uint32_t length = 0;
            std::memcpy(&length, data + 12, sizeof(length));
            if (length < sizeof(Header) || length > Capacity)
                return false;
            cacheValid = false;
            packet.resize(length);
            // Health fields are written by this layer; the publisher never overwrites them.
            std::memcpy(packet.data(), data, length);
            MemoryBarrier();
            LONG after = InterlockedCompareExchange(sequence, 0, 0);
            if (before == after && ValidatePacket(packet, Now()))
            {
                cachedSequence = static_cast<uint32_t>(before);
                cacheValid = true;
                return true;
            }
        }
        return false;
    }
    bool IsPublishing()
    {
        if (!Open())
            return false;
        auto *header = reinterpret_cast<Header *>(data);
        int64_t age = Now() - header->timestamp;
        return header->magic == Magic && header->version == Version && header->timestamp > 0 && age >= 0 && age <= 2000;
    }
    void Report(uint32_t state, XrResult error = XR_SUCCESS, uint32_t rendered = 0)
    {
        if (!Open())
            return;
        auto *header = reinterpret_cast<Header *>(data);
        header->layerState = state;
        header->renderedCount = rendered;
        header->lastError = error;
        header->processId = GetCurrentProcessId();
        MemoryBarrier();
        InterlockedExchange64(reinterpret_cast<volatile LONG64 *>(data + 32), Now());
    }
};

struct Instance
{
    XrInstance handle{};
    PFN_xrGetInstanceProcAddr getProc{};
    PFN_xrDestroyInstance destroy{};
    PFN_xrCreateSession createSession{};
    PFN_xrDestroySession destroySession{};
    PFN_xrBeginSession beginSession{};
    PFN_xrEndSession endSession{};
    PFN_xrWaitFrame waitFrame{};
    PFN_xrEndFrame endFrame{};
    PFN_xrCreateReferenceSpace createSpace{};
    PFN_xrDestroySpace destroySpace{};
    PFN_xrLocateSpace locateSpace{};
    PFN_xrEnumerateSwapchainFormats formats{};
    PFN_xrCreateSwapchain createSwapchain{};
    PFN_xrDestroySwapchain destroySwapchain{};
    PFN_xrEnumerateSwapchainImages images{};
    PFN_xrAcquireSwapchainImage acquire{};
    PFN_xrWaitSwapchainImage wait{};
    PFN_xrReleaseSwapchainImage release{};
    PFN_xrGetSystemProperties systemProperties{};
    PFN_xrPollEvent pollEvent{};
    PFN_xrSuggestInteractionProfileBindings suggestBindings{};
    PFN_xrAttachSessionActionSets attachSets{};
    PFN_xrSyncActions syncActions{};
    std::shared_ptr<InteractionActions> input;
    std::mutex inputMutex;
    uint32_t inputSessions = 0;
    bool elite = false, vdxr = false;
};
struct Texture
{
    XrSwapchain handle{};
    std::vector<XrSwapchainImageD3D11KHR> images;
    uint32_t width = 0, height = 0, version = 0, acquiredIndex = 0;
    bool acquired = false, ready = false;
};
struct Session
{
    explicit Session(std::wstring mailboxPath = {})
        : mailbox(mailboxPath),
          input(std::make_unique<InteractionSession>(mailboxPath.empty() ? std::wstring{} : mailboxPath + L".input"))
    {
    }
    ~Session()
    {
        if (context)
            context->Release();
        if (device)
            device->Release();
    }
    std::shared_ptr<Instance> instance;
    XrSession handle{};
    ID3D11Device *device = nullptr;
    ID3D11DeviceContext *context = nullptr;
    XrSpace local{}, view{};
    std::map<uint64_t, Texture> textures;
    Mailbox mailbox;
    std::unique_ptr<InteractionSession> input;
    Texture cursorTexture;
    std::vector<uint8_t> packet;
    std::mutex mutex;
    std::atomic<bool> shouldRender{false};
    bool running = false, anchored = false, failed = false;
    bool uploadsPending = false;
    XrResult lastError = XR_SUCCESS;
    uint32_t originRevision = 0, maximumLayers = 0;
    XrTime pendingSpaceChange = 0;
    XrPosef anchor{{0, 0, 0, 1}, {0, 0, 0}};

    XrResult CompleteUploads()
    {
        if (!uploadsPending)
            return XR_SUCCESS;
        D3D11_QUERY_DESC info{D3D11_QUERY_EVENT, 0};
        ID3D11Query *completion = nullptr;
        if (FAILED(device->CreateQuery(&info, &completion)))
            return XR_ERROR_RUNTIME_FAILURE;
        context->End(completion);
        context->Flush();
        HRESULT result;
        do
        {
            result = context->GetData(completion, nullptr, 0, D3D11_ASYNC_GETDATA_DONOTFLUSH);
            if (result == S_FALSE)
                SwitchToThread();
        } while (result == S_FALSE);
        completion->Release();
        if (result != S_OK)
            return XR_ERROR_RUNTIME_FAILURE;
        uploadsPending = false;
        return XR_SUCCESS;
    }

    void Clear()
    {
        // xrDestroySwapchain requires submitted application GPU work to be complete.
        // On a device failure, leave child destruction to the downstream session teardown.
        input->Clear();
        bool completed = CompleteUploads() == XR_SUCCESS;
        if (cursorTexture.handle && completed)
            instance->destroySwapchain(cursorTexture.handle);
        cursorTexture = {};
        for (auto &[id, texture] : textures)
        {
            (void)id;
            if (texture.handle && completed)
                instance->destroySwapchain(texture.handle);
        }
        textures.clear();
        if (local)
            instance->destroySpace(local);
        if (view)
            instance->destroySpace(view);
        local = XR_NULL_HANDLE;
        view = XR_NULL_HANDLE;
        if (context)
            context->Release();
        if (device)
            device->Release();
        context = nullptr;
        device = nullptr;
        uploadsPending = false;
        running = false;
        if (instance->elite)
            mailbox.Report(0);
    }
};
std::mutex dispatchMutex;
std::map<XrInstance, std::shared_ptr<Instance>> instances;
std::map<XrSession, std::shared_ptr<Session>> sessions;

std::shared_ptr<Instance> FindInstance(XrInstance handle)
{
    std::lock_guard lock(dispatchMutex);
    auto found = instances.find(handle);
    return found == instances.end() ? nullptr : found->second;
}
std::shared_ptr<Session> FindSession(XrSession handle)
{
    std::lock_guard lock(dispatchMutex);
    auto found = sessions.find(handle);
    return found == sessions.end() ? nullptr : found->second;
}
template <class T> bool Bind(const Instance &instance, const char *name, T &target)
{
    PFN_xrVoidFunction address = nullptr;
    if (XR_FAILED(instance.getProc(instance.handle, name, &address)) || !address)
        return false;
    target = reinterpret_cast<T>(address);
    return true;
}
void Check(XrResult result)
{
    if (result != XR_SUCCESS)
        throw result;
}

XrQuaternionf Multiply(XrQuaternionf a, XrQuaternionf b)
{
    return {a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y, a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w, a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z};
}
XrPosef Place(const XrPosef &anchor, const Descriptor &descriptor)
{
    XrQuaternionf position{descriptor.position[0], descriptor.position[1], descriptor.position[2], 0};
    XrQuaternionf inverse{-anchor.orientation.x, -anchor.orientation.y, -anchor.orientation.z, anchor.orientation.w};
    auto rotated = Multiply(Multiply(anchor.orientation, position), inverse);
    return {Multiply(anchor.orientation, {descriptor.orientation[0], descriptor.orientation[1],
                                          descriptor.orientation[2], descriptor.orientation[3]}),
            {anchor.position.x + rotated.x, anchor.position.y + rotated.y, anchor.position.z + rotated.z}};
}

void Prepare(Session &session)
{
    if (session.local)
        return;
    XrReferenceSpaceCreateInfo info{XR_TYPE_REFERENCE_SPACE_CREATE_INFO};
    info.poseInReferenceSpace.orientation.w = 1;
    info.referenceSpaceType = XR_REFERENCE_SPACE_TYPE_LOCAL;
    Check(session.instance->createSpace(session.handle, &info, &session.local));
    info.referenceSpaceType = XR_REFERENCE_SPACE_TYPE_VIEW;
    try
    {
        Check(session.instance->createSpace(session.handle, &info, &session.view));
    }
    catch (...)
    {
        session.instance->destroySpace(session.local);
        session.local = XR_NULL_HANDLE;
        throw;
    }
}
void UploadPixels(Session &session, Texture &texture, const Descriptor &descriptor, uint32_t version,
                  const uint8_t *source)
{
    if (texture.handle && (texture.width != descriptor.width || texture.height != descriptor.height))
    {
        Check(session.CompleteUploads());
        Check(session.instance->destroySwapchain(texture.handle));
        texture = {};
    }
    if (!texture.handle)
    {
        uint32_t count = 0;
        Check(session.instance->formats(session.handle, 0, &count, nullptr));
        std::vector<int64_t> formats(count);
        Check(session.instance->formats(session.handle, count, &count, formats.data()));
        if (std::find(formats.begin(), formats.end(), DXGI_FORMAT_R8G8B8A8_UNORM_SRGB) == formats.end())
            throw XR_ERROR_SWAPCHAIN_FORMAT_UNSUPPORTED;
        XrSwapchainCreateInfo info{XR_TYPE_SWAPCHAIN_CREATE_INFO};
        info.usageFlags = XR_SWAPCHAIN_USAGE_SAMPLED_BIT | XR_SWAPCHAIN_USAGE_TRANSFER_DST_BIT;
        info.format = DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;
        info.sampleCount = info.faceCount = info.arraySize = info.mipCount = 1;
        info.width = descriptor.width;
        info.height = descriptor.height;
        Check(session.instance->createSwapchain(session.handle, &info, &texture.handle));
        texture.width = descriptor.width;
        texture.height = descriptor.height;
        Check(session.instance->images(texture.handle, 0, &count, nullptr));
        texture.images.resize(count, {XR_TYPE_SWAPCHAIN_IMAGE_D3D11_KHR});
        Check(session.instance->images(texture.handle, count, &count,
                                       reinterpret_cast<XrSwapchainImageBaseHeader *>(texture.images.data())));
    }
    if (texture.ready && texture.version == version && !texture.acquired)
        return;
    if (!texture.acquired)
    {
        XrSwapchainImageAcquireInfo acquire{XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO};
        Check(session.instance->acquire(texture.handle, &acquire, &texture.acquiredIndex));
        texture.acquired = true;
    }
    XrSwapchainImageWaitInfo wait{XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO};
    wait.timeout = 0;
    XrResult waited = session.instance->wait(texture.handle, &wait);
    if (waited == XR_TIMEOUT_EXPIRED)
        return; // Keep this acquired image and retry waiting next frame.
    Check(waited);
    if (texture.acquiredIndex >= texture.images.size())
        throw XR_ERROR_RUNTIME_FAILURE;
    std::vector<uint8_t> pixels(descriptor.length);
    for (size_t i = 0; i < pixels.size(); i += 4)
    {
        auto alpha = static_cast<uint8_t>(static_cast<float>(source[i + 3]) * descriptor.alpha);
        pixels[i + 3] = alpha;
        // PNG capture is straight-alpha sRGB. Let the compositor premultiply in linear space.
        for (size_t channel = 0; channel < 3; ++channel)
            pixels[i + channel] = source[i + channel];
    }
    session.context->UpdateSubresource(texture.images[texture.acquiredIndex].texture, 0, nullptr, pixels.data(),
                                       descriptor.width * 4, 0);
    session.uploadsPending = true;
    XrSwapchainImageReleaseInfo release{XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO};
    Check(session.instance->release(texture.handle, &release));
    texture.acquired = false;
    texture.ready = true;
    texture.version = version;
}

void Upload(Session &session, const Descriptor &descriptor, uint32_t version)
{
    UploadPixels(session, session.textures[descriptor.id], descriptor, version,
                 session.packet.data() + descriptor.offset);
}
std::optional<XrCompositionLayerQuad> CursorQuad(Session &session, const PointerHit &hit)
{
    std::array<uint8_t, 32 * 32 * 4> pixels{};
    for (int y = 0; y < 32; ++y)
        for (int x = 0; x < 32; ++x)
        {
            float dx = static_cast<float>(x) - 15.5f, dy = static_cast<float>(y) - 15.5f;
            float radius = std::sqrt(dx * dx + dy * dy);
            size_t index = static_cast<size_t>(y * 32 + x) * 4;
            bool edge = radius > 9;
            pixels[index] = edge ? 255 : 32;
            pixels[index + 1] = edge ? 255 : 200;
            pixels[index + 2] = 255;
            pixels[index + 3] = static_cast<uint8_t>(std::clamp(13.5f - radius, 0.0f, 1.0f) * 255);
        }
    Descriptor descriptor{};
    descriptor.width = descriptor.height = 32;
    descriptor.length = static_cast<uint32_t>(pixels.size());
    descriptor.alpha = 1;
    UploadPixels(session, session.cursorTexture, descriptor, 1, pixels.data());
    if (!session.cursorTexture.ready)
        return {};
    XrCompositionLayerQuad quad{XR_TYPE_COMPOSITION_LAYER_QUAD};
    quad.layerFlags =
        XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT | XR_COMPOSITION_LAYER_UNPREMULTIPLIED_ALPHA_BIT;
    quad.space = session.local;
    quad.eyeVisibility = XR_EYE_VISIBILITY_BOTH;
    quad.subImage.swapchain = session.cursorTexture.handle;
    quad.subImage.imageRect.extent = {32, 32};
    auto displacement = Rotate(hit.pose.orientation, {(hit.x - 0.5f) * hit.width, (0.5f - hit.y) * hit.height, 0.002f});
    quad.pose = {{hit.pose.orientation.x, hit.pose.orientation.y, hit.pose.orientation.z, hit.pose.orientation.w},
                 {hit.pose.position.x + displacement.x, hit.pose.position.y + displacement.y,
                  hit.pose.position.z + displacement.z}};
    quad.size = {0.012f, 0.012f};
    return quad;
}

XrResult XRAPI_CALL LayerEndFrame(XrSession handle, const XrFrameEndInfo *info)
{
    auto session = FindSession(handle);
    if (!session)
        return XR_ERROR_HANDLE_INVALID;
    auto &s = *session;
    std::lock_guard lock(s.mutex);
    try
    {
        if (!info || !s.instance->elite)
            return s.instance->endFrame(handle, info);
        if (!s.instance->vdxr || !s.device)
        {
            s.mailbox.Report(s.instance->vdxr ? 2 : 3);
            return s.instance->endFrame(handle, info);
        }
        if (s.failed)
        {
            s.mailbox.Report(4, s.lastError);
            return s.instance->endFrame(handle, info);
        }
        s.mailbox.Report(1);
        bool interaction =
            s.input->Requested(Now(), s.running && s.shouldRender && s.input->focused && s.mailbox.IsPublishing());
        if (!s.running || !s.shouldRender || !info->layerCount || !s.mailbox.Read(s.packet))
        {
            s.input->Cancel();
            return s.instance->endFrame(handle, info);
        }
        Header header{};
        std::memcpy(&header, s.packet.data(), sizeof(header));
        Prepare(s);
        if (s.pendingSpaceChange && info->displayTime >= s.pendingSpaceChange)
        {
            s.anchored = false;
            s.pendingSpaceChange = 0;
        }
        if (!s.anchored || s.originRevision != header.originRevision)
        {
            XrSpaceLocation location{XR_TYPE_SPACE_LOCATION};
            Check(s.instance->locateSpace(s.view, s.local, info->displayTime, &location));
            constexpr XrSpaceLocationFlags valid =
                XR_SPACE_LOCATION_ORIENTATION_VALID_BIT | XR_SPACE_LOCATION_POSITION_VALID_BIT;
            if ((location.locationFlags & valid) != valid)
            {
                s.input->Cancel();
                return s.instance->endFrame(handle, info);
            }
            s.anchor = location.pose;
            s.originRevision = header.originRevision;
            s.anchored = true;
        }
        std::vector<XrCompositionLayerQuad> quads;
        quads.reserve(header.count + 1);
        std::vector<PointerSurface> surfaces;
        std::vector<const XrCompositionLayerBaseHeader *> layers(info->layers, info->layers + info->layerCount);
        std::vector<uint64_t> active;
        for (uint32_t index = 0; index < header.count; ++index)
        {
            Descriptor descriptor{};
            std::memcpy(&descriptor, s.packet.data() + sizeof(Header) + index * sizeof(Descriptor), sizeof(descriptor));
            active.push_back(descriptor.id);
            if (descriptor.alpha <= 0 || layers.size() + (interaction ? 1u : 0u) >= s.maximumLayers)
                continue;
            Upload(s, descriptor, header.sequence);
            auto &texture = s.textures.at(descriptor.id);
            if (!texture.ready)
                continue;
            XrCompositionLayerQuad quad{XR_TYPE_COMPOSITION_LAYER_QUAD};
            quad.layerFlags =
                XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT | XR_COMPOSITION_LAYER_UNPREMULTIPLIED_ALPHA_BIT;
            quad.space = s.local;
            quad.eyeVisibility = XR_EYE_VISIBILITY_BOTH;
            quad.subImage.swapchain = texture.handle;
            quad.subImage.imageRect.extent = {static_cast<int32_t>(descriptor.width),
                                              static_cast<int32_t>(descriptor.height)};
            quad.pose = Place(s.anchor, descriptor);
            quad.size = {descriptor.widthMeters, descriptor.heightMeters};
            surfaces.push_back(
                {descriptor.id, PointerPose(quad.pose), descriptor.widthMeters, descriptor.heightMeters});
            quads.push_back(quad);
            layers.push_back(reinterpret_cast<const XrCompositionLayerBaseHeader *>(&quads.back()));
        }
        for (auto it = s.textures.begin(); it != s.textures.end();)
        {
            if (std::find(active.begin(), active.end(), it->first) == active.end())
            {
                Check(s.CompleteUploads());
                Check(s.instance->destroySwapchain(it->second.handle));
                it = s.textures.erase(it);
            }
            else
                ++it;
        }
        auto pointer = s.input->Update(s.local, surfaces, info->displayTime, Now());
        if (interaction && pointer.cursor && layers.size() < s.maximumLayers)
        {
            try
            {
                auto cursor = CursorQuad(s, *pointer.cursor);
                if (cursor)
                {
                    quads.push_back(*cursor);
                    layers.push_back(reinterpret_cast<const XrCompositionLayerBaseHeader *>(&quads.back()));
                }
                else
                    pointer = s.input->Update(XR_NULL_HANDLE, {}, info->displayTime, Now());
            }
            catch (XrResult error)
            {
                s.input->Fail(error, Now());
                pointer = {};
            }
            catch (...)
            {
                s.input->Fail(XR_ERROR_RUNTIME_FAILURE, Now());
                pointer = {};
            } // Input decoration must never reject Elite's frame.
        }
        s.input->Publish(pointer, Now());
        XrFrameEndInfo augmented = *info;
        augmented.layerCount = static_cast<uint32_t>(layers.size());
        augmented.layers = layers.data();
        XrResult result = s.instance->endFrame(handle, &augmented);
        s.mailbox.Report(XR_SUCCEEDED(result) ? 1 : 4, result, static_cast<uint32_t>(quads.size()));
        if (XR_FAILED(result))
        {
            s.input->Cancel();
            s.failed = true;
            s.lastError = result;
        }
        return result;
    }
    catch (XrResult error)
    {
        s.input->Cancel();
        s.failed = true;
        s.lastError = error;
        s.mailbox.Report(4, error);
    }
    catch (...)
    {
        s.input->Cancel();
        s.failed = true;
        s.lastError = XR_ERROR_RUNTIME_FAILURE;
        s.mailbox.Report(4, s.lastError);
    }
    // Local overlay failures must not prevent the game from submitting its original frame.
    return s.instance->endFrame(handle, info);
}
XrResult XRAPI_CALL LayerWaitFrame(XrSession handle, const XrFrameWaitInfo *info, XrFrameState *state)
{
    auto session = FindSession(handle);
    if (!session)
        return XR_ERROR_HANDLE_INVALID;
    // Do not hold a dispatch/session lock while the runtime blocks in WaitFrame.
    XrResult result = session->instance->waitFrame(handle, info, state);
    if (XR_SUCCEEDED(result) && state)
        session->shouldRender = state->shouldRender != XR_FALSE;
    return result;
}
XrResult XRAPI_CALL LayerBeginSession(XrSession handle, const XrSessionBeginInfo *info)
{
    auto session = FindSession(handle);
    if (!session)
        return XR_ERROR_HANDLE_INVALID;
    std::lock_guard lock(session->mutex);
    XrResult result = session->instance->beginSession(handle, info);
    session->running = XR_SUCCEEDED(result);
    session->anchored = false;
    return result;
}
XrResult XRAPI_CALL LayerEndSession(XrSession handle)
{
    auto session = FindSession(handle);
    if (!session)
        return XR_ERROR_HANDLE_INVALID;
    std::lock_guard lock(session->mutex);
    XrResult result = session->instance->endSession(handle);
    if (XR_SUCCEEDED(result))
    {
        session->running = false;
        session->input->Cancel();
        if (session->instance->elite)
            session->mailbox.Report(0);
    }
    return result;
}
XrResult XRAPI_CALL LayerDestroySession(XrSession handle)
{
    auto session = FindSession(handle);
    if (!session)
        return XR_ERROR_HANDLE_INVALID;
    std::lock_guard lock(session->mutex);
    bool hadInput = session->input->setsAttached;
    session->Clear();
    if (hadInput)
    {
        std::lock_guard inputLock(session->instance->inputMutex);
        --session->instance->inputSessions;
    }
    XrResult result = session->instance->destroySession(handle);
    if (XR_SUCCEEDED(result))
    {
        std::lock_guard dispatchLock(dispatchMutex);
        sessions.erase(handle);
    }
    return result;
}
XrResult XRAPI_CALL LayerCreateSession(XrInstance handle, const XrSessionCreateInfo *info, XrSession *output)
{
    auto instance = FindInstance(handle);
    if (!instance)
        return XR_ERROR_HANDLE_INVALID;
    XrResult result = instance->createSession(handle, info, output);
    if (XR_FAILED(result))
        return result;
    try
    {
        auto session = std::make_shared<Session>();
        session->instance = instance;
        session->handle = *output;
        if (instance->elite && instance->vdxr)
        {
            for (auto *next = reinterpret_cast<const XrBaseInStructure *>(info->next); next; next = next->next)
            {
                if (next->type == XR_TYPE_GRAPHICS_BINDING_D3D11_KHR)
                {
                    session->device = reinterpret_cast<const XrGraphicsBindingD3D11KHR *>(next)->device;
                    if (session->device)
                    {
                        session->device->AddRef();
                        session->device->GetImmediateContext(&session->context);
                    }
                    break;
                }
            }
            XrSystemProperties properties{XR_TYPE_SYSTEM_PROPERTIES};
            if (XR_SUCCEEDED(instance->systemProperties(handle, info->systemId, &properties)))
                session->maximumLayers = properties.graphicsProperties.maxLayerCount;
        }
        std::lock_guard lock(dispatchMutex);
        sessions.emplace(*output, std::move(session));
        return result;
    }
    catch (...)
    {
        instance->destroySession(*output);
        *output = XR_NULL_HANDLE;
        return XR_ERROR_OUT_OF_MEMORY;
    }
}
XrResult XRAPI_CALL LayerPollEvent(XrInstance handle, XrEventDataBuffer *event)
{
    auto instance = FindInstance(handle);
    if (!instance)
        return XR_ERROR_HANDLE_INVALID;
    XrResult result = instance->pollEvent(handle, event);
    if (result == XR_SUCCESS && event && event->type == XR_TYPE_EVENT_DATA_SESSION_STATE_CHANGED)
    {
        auto *state = reinterpret_cast<const XrEventDataSessionStateChanged *>(event);
        auto session = FindSession(state->session);
        if (session)
        {
            std::lock_guard lock(session->mutex);
            session->input->focused = state->state == XR_SESSION_STATE_FOCUSED;
            if (!session->input->focused)
                session->input->Cancel();
        }
    }
    if (result == XR_SUCCESS && event && event->type == XR_TYPE_EVENT_DATA_REFERENCE_SPACE_CHANGE_PENDING)
    {
        const auto *change = reinterpret_cast<const XrEventDataReferenceSpaceChangePending *>(event);
        auto session = FindSession(change->session);
        if (session && change->referenceSpaceType == XR_REFERENCE_SPACE_TYPE_LOCAL)
        {
            std::lock_guard lock(session->mutex);
            session->pendingSpaceChange = change->changeTime;
        }
    }
    return result;
}
XrResult XRAPI_CALL LayerDestroyInstance(XrInstance handle)
{
    auto instance = FindInstance(handle);
    if (!instance)
        return XR_ERROR_HANDLE_INVALID;
    std::vector<std::shared_ptr<Session>> remaining;
    {
        std::lock_guard lock(dispatchMutex);
        for (const auto &[key, session] : sessions)
        {
            (void)key;
            if (session->instance == instance)
                remaining.push_back(session);
        }
    }
    for (auto &session : remaining)
        LayerDestroySession(session->handle);
    instance->input.reset();
    XrResult result = instance->destroy(handle);
    if (XR_SUCCEEDED(result))
    {
        std::lock_guard lock(dispatchMutex);
        instances.erase(handle);
    }
    return result;
}
void EnsureInput(Instance &instance, bool newBindings = false)
{
    if (!instance.elite || !instance.vdxr)
        return;
    if (!instance.input || (newBindings && instance.input->everAttached && instance.inputSessions == 0))
    {
        instance.input.reset();
        instance.input = std::make_shared<InteractionActions>(instance.handle, instance.getProc);
        instance.input->Initialize();
    }
}
XrResult XRAPI_CALL LayerSuggestBindings(XrInstance handle, const XrInteractionProfileSuggestedBinding *info)
{
    auto instance = FindInstance(handle);
    if (!instance)
        return XR_ERROR_HANDLE_INVALID;
    std::lock_guard lock(instance->inputMutex);
    try
    {
        EnsureInput(*instance, true);
        if (instance->input && instance->input->ready)
            return instance->input->Suggest(info);
    }
    catch (...)
    {
        if (instance->input)
            instance->input->ready = false;
    }
    return instance->suggestBindings(handle, info);
}
XrResult XRAPI_CALL LayerAttachSets(XrSession handle, const XrSessionActionSetsAttachInfo *info)
{
    auto session = FindSession(handle);
    if (!session)
        return XR_ERROR_HANDLE_INVALID;
    auto &instance = *session->instance;
    std::lock_guard lock(session->mutex);
    std::lock_guard inputLock(instance.inputMutex);
    try
    {
        EnsureInput(instance);
        if (info && info->countActionSets && info->actionSets && instance.input && instance.input->ready &&
            !session->input->setsAttached)
        {
            std::vector<XrActionSet> sets(info->actionSets, info->actionSets + info->countActionSets);
            sets.insert(sets.end(), instance.input->sets.begin(), instance.input->sets.end());
            auto merged = *info;
            merged.countActionSets = static_cast<uint32_t>(sets.size());
            merged.actionSets = sets.data();
            XrResult result = instance.attachSets(handle, &merged);
            if (XR_SUCCEEDED(result))
            {
                ++instance.inputSessions;
                session->input->Attach(instance.input, handle, result);
                return result;
            }
            instance.input->error = result;
            instance.input->ready = false;
        }
    }
    catch (...)
    {
        if (instance.input)
            instance.input->ready = false;
    }
    return instance.attachSets(handle, info);
}
XrResult XRAPI_CALL LayerSyncActions(XrSession handle, const XrActionsSyncInfo *info)
{
    auto session = FindSession(handle);
    if (!session)
        return XR_ERROR_HANDLE_INVALID;
    std::lock_guard lock(session->mutex);
    try
    {
        bool enabled = session->input->Requested(Now(), session->running && session->input->focused &&
                                                            session->shouldRender && session->mailbox.IsPublishing());
        if (enabled && info)
        {
            std::vector<XrActiveActionSet> active;
            if (info->countActiveActionSets)
                active.assign(info->activeActionSets, info->activeActionSets + info->countActiveActionSets);
            for (auto set : session->input->actions->sets)
                active.push_back({set, XR_NULL_PATH});
            auto merged = *info;
            merged.countActiveActionSets = static_cast<uint32_t>(active.size());
            merged.activeActionSets = active.data();
            XrResult result = session->instance->syncActions(handle, &merged);
            session->input->Synced(result, true);
            return result;
        }
    }
    catch (...)
    {
        session->input->Cancel();
    }
    XrResult result = session->instance->syncActions(handle, info);
    session->input->Synced(result, false);
    return result;
}
XrResult XRAPI_CALL LayerGetInstanceProcAddr(XrInstance handle, const char *name, PFN_xrVoidFunction *output)
{
    if (!name || !output)
        return XR_ERROR_VALIDATION_FAILURE;
#define INTERCEPT(api, function)                                                                                       \
    if (std::strcmp(name, api) == 0)                                                                                   \
    {                                                                                                                  \
        *output = reinterpret_cast<PFN_xrVoidFunction>(function);                                                      \
        return XR_SUCCESS;                                                                                             \
    }
    INTERCEPT("xrGetInstanceProcAddr", LayerGetInstanceProcAddr)
    auto instance = FindInstance(handle);
    if (!instance)
        return XR_ERROR_HANDLE_INVALID;
    INTERCEPT("xrDestroyInstance", LayerDestroyInstance)
    if (!instance->elite)
        return instance->getProc(handle, name, output);
    INTERCEPT("xrCreateSession", LayerCreateSession)
    INTERCEPT("xrDestroySession", LayerDestroySession)
    INTERCEPT("xrBeginSession", LayerBeginSession)
    INTERCEPT("xrEndSession", LayerEndSession)
    INTERCEPT("xrWaitFrame", LayerWaitFrame)
    INTERCEPT("xrEndFrame", LayerEndFrame)
    INTERCEPT("xrPollEvent", LayerPollEvent)
    INTERCEPT("xrSuggestInteractionProfileBindings", LayerSuggestBindings)
    INTERCEPT("xrAttachSessionActionSets", LayerAttachSets)
    INTERCEPT("xrSyncActions", LayerSyncActions)
#undef INTERCEPT
    return instance->getProc(handle, name, output);
}
XrResult XRAPI_CALL LayerCreateApiLayerInstance(const XrInstanceCreateInfo *info, const XrApiLayerCreateInfo *layerInfo,
                                                XrInstance *output)
{
    if (!info || !layerInfo || !layerInfo->nextInfo || !output ||
        layerInfo->structType != XR_LOADER_INTERFACE_STRUCT_API_LAYER_CREATE_INFO ||
        layerInfo->structSize != sizeof(XrApiLayerCreateInfo) ||
        std::strcmp(layerInfo->nextInfo->layerName, LayerName) != 0)
        return XR_ERROR_INITIALIZATION_FAILED;
    const auto *next = layerInfo->nextInfo;
    if (!next->nextCreateApiLayerInstance || !next->nextGetInstanceProcAddr)
        return XR_ERROR_INITIALIZATION_FAILED;
    XrApiLayerCreateInfo advanced = *layerInfo;
    advanced.nextInfo = next->next;
    XrResult result = next->nextCreateApiLayerInstance(info, &advanced, output);
    if (XR_FAILED(result))
        return result;
    try
    {
        auto instance = std::make_shared<Instance>();
        instance->handle = *output;
        instance->getProc = next->nextGetInstanceProcAddr;
#define BIND(field, api)                                                                                               \
    if (!Bind(*instance, api, instance->field))                                                                        \
        throw XR_ERROR_INITIALIZATION_FAILED;
        BIND(destroy, "xrDestroyInstance")
        BIND(createSession, "xrCreateSession")
        BIND(destroySession, "xrDestroySession")
        BIND(suggestBindings, "xrSuggestInteractionProfileBindings")
        BIND(attachSets, "xrAttachSessionActionSets")
        BIND(syncActions, "xrSyncActions")
        BIND(beginSession, "xrBeginSession")
        BIND(endSession, "xrEndSession")
        BIND(waitFrame, "xrWaitFrame")
        BIND(endFrame, "xrEndFrame")
        BIND(createSpace, "xrCreateReferenceSpace")
        BIND(destroySpace, "xrDestroySpace")
        BIND(locateSpace, "xrLocateSpace")
        BIND(formats, "xrEnumerateSwapchainFormats")
        BIND(createSwapchain, "xrCreateSwapchain")
        BIND(destroySwapchain, "xrDestroySwapchain")
        BIND(images, "xrEnumerateSwapchainImages")
        BIND(acquire, "xrAcquireSwapchainImage")
        BIND(wait, "xrWaitSwapchainImage")
        BIND(release, "xrReleaseSwapchainImage")
        BIND(systemProperties, "xrGetSystemProperties")
        BIND(pollEvent, "xrPollEvent")
#undef BIND
        wchar_t executable[32768]{};
        if (GetModuleFileNameW(nullptr, executable, static_cast<DWORD>(std::size(executable))))
        {
            const wchar_t *basename = wcsrchr(executable, L'\\');
            instance->elite = _wcsicmp(basename ? basename + 1 : executable, L"EliteDangerous64.exe") == 0;
        }
        PFN_xrGetInstanceProperties properties = nullptr;
        if (Bind(*instance, "xrGetInstanceProperties", properties))
        {
            XrInstanceProperties value{XR_TYPE_INSTANCE_PROPERTIES};
            if (XR_SUCCEEDED(properties(*output, &value)))
                instance->vdxr = std::strcmp(value.runtimeName, "VirtualDesktopXR") == 0;
        }
        try
        {
            EnsureInput(*instance);
        }
        catch (...)
        {
            instance->input.reset();
        }
        std::lock_guard lock(dispatchMutex);
        instances.emplace(*output, std::move(instance));
        return result;
    }
    catch (...)
    {
        PFN_xrVoidFunction address = nullptr;
        if (XR_SUCCEEDED(next->nextGetInstanceProcAddr(*output, "xrDestroyInstance", &address)) && address)
        {
            reinterpret_cast<PFN_xrDestroyInstance>(address)(*output);
        }
        *output = XR_NULL_HANDLE;
        return XR_ERROR_INITIALIZATION_FAILED;
    }
}
} // namespace

extern "C" __declspec(dllexport) XrResult XRAPI_CALL xrNegotiateLoaderApiLayerInterface(
    const XrNegotiateLoaderInfo *loader, const char *name, XrNegotiateApiLayerRequest *request)
{
    if (!loader || !name || !request || std::strcmp(name, LayerName) != 0 ||
        loader->structType != XR_LOADER_INTERFACE_STRUCT_LOADER_INFO ||
        loader->structVersion != XR_LOADER_INFO_STRUCT_VERSION || loader->structSize != sizeof(XrNegotiateLoaderInfo) ||
        request->structType != XR_LOADER_INTERFACE_STRUCT_API_LAYER_REQUEST ||
        request->structVersion != XR_API_LAYER_INFO_STRUCT_VERSION ||
        request->structSize != sizeof(XrNegotiateApiLayerRequest) ||
        loader->minInterfaceVersion > XR_CURRENT_LOADER_API_LAYER_VERSION ||
        loader->maxInterfaceVersion < XR_CURRENT_LOADER_API_LAYER_VERSION ||
        loader->minApiVersion > XR_CURRENT_API_VERSION || loader->maxApiVersion < XR_MAKE_VERSION(1, 0, 0))
        return XR_ERROR_INITIALIZATION_FAILED;
    request->layerInterfaceVersion = XR_CURRENT_LOADER_API_LAYER_VERSION;
    request->layerApiVersion = std::min(loader->maxApiVersion, XR_CURRENT_API_VERSION);
    request->getInstanceProcAddr = LayerGetInstanceProcAddr;
    request->createApiLayerInstance = LayerCreateApiLayerInstance;
    return XR_SUCCESS;
}
