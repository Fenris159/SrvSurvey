#include <winsock2.h>
#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <dxgi1_2.h>

#include "CameraProtocol.h"
#include "CaptureProfileFiles.h"
#include "CockpitReference.h"
#include <atomic>
#include <cwchar>
#include <memory>
#include <mutex>
#include <unordered_map>

namespace
{
using namespace srvsurvey::camera;
HMODULE moduleHandle = nullptr;
// Private shader identity avoids a process-lifetime shader-pointer registry.
constexpr GUID ShaderIdentity{0x18e2394d, 0x488c, 0x4059, {0x95, 0xdf, 0xc3, 0x60, 0x49, 0x40, 0x91, 0x5a}};

template <typename T> void Release(T *&value)
{
    T *previous = value;
    value = nullptr;
    if (previous)
        previous->Release();
}

int64_t UnixMilliseconds()
{
    FILETIME value{};
    GetSystemTimeAsFileTime(&value);
    ULARGE_INTEGER ticks{};
    ticks.LowPart = value.dwLowDateTime;
    ticks.HighPart = value.dwHighDateTime;
    return static_cast<int64_t>(ticks.QuadPart / 10000ULL - 11644473600000ULL);
}

CaptureProfile LoadProfile()
{
    wchar_t path[MAX_PATH]{};
    DWORD length = GetModuleFileNameW(moduleHandle, path, MAX_PATH);
    if (length == 0 || length >= MAX_PATH)
        return {};
    wchar_t *separator = std::wcsrchr(path, L'\\');
    if (!separator)
        return {};
    *separator = L'\0';
    return LoadInstalledCaptureProfile(path);
}

using DrawIndexed = void(STDMETHODCALLTYPE *)(ID3D11DeviceContext *, UINT, UINT, INT);
using Draw = void(STDMETHODCALLTYPE *)(ID3D11DeviceContext *, UINT, UINT);
using DrawIndexedInstanced = void(STDMETHODCALLTYPE *)(ID3D11DeviceContext *, UINT, UINT, UINT, INT, UINT);
using DrawInstanced = void(STDMETHODCALLTYPE *)(ID3D11DeviceContext *, UINT, UINT, UINT, UINT);
using ContextRelease = ULONG(STDMETHODCALLTYPE *)(ID3D11DeviceContext *);
using CreateVertexShader = HRESULT(STDMETHODCALLTYPE *)(ID3D11Device *, const void *, SIZE_T, ID3D11ClassLinkage *,
                                                       ID3D11VertexShader **);
using CreatePixelShader = HRESULT(STDMETHODCALLTYPE *)(ID3D11Device *, const void *, SIZE_T, ID3D11ClassLinkage *,
                                                      ID3D11PixelShader **);
using GetImmediateContext = void(STDMETHODCALLTYPE *)(ID3D11Device *, ID3D11DeviceContext **);
using Present = HRESULT(STDMETHODCALLTYPE *)(IDXGISwapChain *, UINT, UINT);
using CreateSwapChain = HRESULT(STDMETHODCALLTYPE *)(IDXGIFactory *, IUnknown *, DXGI_SWAP_CHAIN_DESC *, IDXGISwapChain **);
using CreateSwapChainForHwnd = HRESULT(STDMETHODCALLTYPE *)(IDXGIFactory2 *, IUnknown *, HWND,
                                                          const DXGI_SWAP_CHAIN_DESC1 *,
                                                          const DXGI_SWAP_CHAIN_FULLSCREEN_DESC *, IDXGIOutput *,
                                                          IDXGISwapChain1 **);

struct ContextHooks
{
    ContextRelease release = nullptr;
    DrawIndexed drawIndexed = nullptr;
    Draw draw = nullptr;
    DrawIndexedInstanced drawIndexedInstanced = nullptr;
    DrawInstanced drawInstanced = nullptr;
};

struct FactoryHooks
{
    CreateSwapChain create = nullptr;
    CreateSwapChainForHwnd createForHwnd = nullptr;
};

struct DeviceHooks
{
    CreateVertexShader createVertexShader = nullptr;
    CreatePixelShader createPixelShader = nullptr;
    GetImmediateContext getImmediateContext = nullptr;
};

struct Readback
{
    ID3D11Buffer *staging = nullptr;
    ID3D11Buffer *cameraStaging = nullptr;
    ID3D11Query *completed = nullptr;
    uint32_t byteWidth = 0, cameraBytes = 0, viewOffset = 0, viewBytes = 0;
    uint64_t submittedAt = 0, generation = 0, submission = 0;
    int64_t timestamp = 0;
    bool pending = false;

    ~Readback()
    {
        Release(completed);
        Release(staging);
        Release(cameraStaging);
    }

    void Clear()
    {
        Release(completed);
        Release(staging);
        Release(cameraStaging);
        byteWidth = cameraBytes = viewOffset = viewBytes = 0;
        pending = false;
    }
};

struct ContextState
{
    std::mutex mutex;
    std::array<Readback, 3> ring;
    ContextHooks originals;
    uint64_t lastFrame = UINT64_MAX, lastSubmit = 0;
    std::atomic<bool> disabled{false};

    void Clear()
    {
        for (Readback &entry : ring)
            entry.Clear();
        lastFrame = UINT64_MAX;
        lastSubmit = 0;
    }
};

struct LeaseSnapshot
{
    bool active;
    uint64_t generation;
};

class Runtime
{
  public:
    const CaptureProfile profile = LoadProfile();
    std::mutex mutex;
    std::unordered_map<void **, ContextHooks> contextHooks;
    std::unordered_map<void **, DeviceHooks> deviceHooks;
    std::unordered_map<void **, Present> presentHooks;
    std::unordered_map<void **, FactoryHooks> factoryHooks;
    std::unordered_map<ID3D11DeviceContext *, std::shared_ptr<ContextState>> contexts;
    std::atomic<uint64_t> frame{0};
    std::atomic<bool> hasPresent{false};
    std::atomic<uint64_t> nextSubmission{0}, publishedSubmission{0};

    void OpenTransport()
    {
        if (socket_ != INVALID_SOCKET || attemptedTransport_)
            return;
        attemptedTransport_ = true;
        WSADATA data{};
        if (WSAStartup(MAKEWORD(2, 2), &data) != 0)
            return;
        winsockStarted_ = true;
        socket_ = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
        if (socket_ == INVALID_SOCKET)
            return;
        u_long nonblocking = 1;
        sockaddr_in endpoint{};
        endpoint.sin_family = AF_INET;
        endpoint.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
        endpoint.sin_port = htons(47765);
        if (ioctlsocket(socket_, FIONBIO, &nonblocking) != 0 ||
            bind(socket_, reinterpret_cast<const sockaddr *>(&endpoint), sizeof(endpoint)) != 0)
        {
            closesocket(socket_);
            socket_ = INVALID_SOCKET;
            OutputDebugStringW(L"SrvSurvey camera prototype: local bridge port is unavailable.\n");
        }
    }

    void CloseTransport()
    {
        if (socket_ != INVALID_SOCKET)
            closesocket(socket_);
        socket_ = INVALID_SOCKET;
        if (winsockStarted_)
            WSACleanup();
        winsockStarted_ = false;
        attemptedTransport_ = false;
        lease_ = {};
        discardQueuedRequests_ = true;
    }

    LeaseSnapshot PollLease()
    {
        std::lock_guard guard(mutex);
        uint64_t now = GetTickCount64();
        if (socket_ != INVALID_SOCKET && now - lastPoll_ >= 5)
        {
            // A paused/minimized game can leave old heartbeats queued in the
            // socket. Do not revive capture from those after the app opted out.
            // Drain to would-block, then require a newly arriving heartbeat.
            if (now - lastPoll_ >= ClientLeaseMilliseconds)
            {
                discardQueuedRequests_ = true;
                lease_.hasClient = false;
            }
            lastPoll_ = now;
            // Bound work even if another local program floods the port.
            for (int message = 0; message < 8; ++message)
            {
                std::array<uint8_t, RequestBytes + 1> request{};
                sockaddr_in from{};
                int fromLength = sizeof(from);
                int length = recvfrom(socket_, reinterpret_cast<char *>(request.data()), static_cast<int>(request.size()),
                                      0, reinterpret_cast<sockaddr *>(&from), &fromLength);
                if (length == SOCKET_ERROR)
                {
                    if (WSAGetLastError() == WSAEWOULDBLOCK)
                        discardQueuedRequests_ = false;
                    break;
                }
                if (discardQueuedRequests_)
                    continue;
                Nonce nonce{};
                if (fromLength != sizeof(from) || from.sin_family != AF_INET ||
                    from.sin_addr.s_addr != htonl(INADDR_LOOPBACK) ||
                    !ReadRequest(std::span(request).first(static_cast<size_t>(length)), nonce))
                    continue;
                // A different local endpoint cannot inherit another receiver's
                // in-flight samples, even if it supplies the same nonce.
                if (lease_.hasClient && (peer_.sin_addr.s_addr != from.sin_addr.s_addr || peer_.sin_port != from.sin_port))
                    lease_.hasClient = false;
                lease_.Renew(nonce, now);
                peer_ = from;
            }
        }
        return {lease_.IsActive(now), lease_.generation};
    }

    void Publish(const CompletedSample &sample)
    {
        std::lock_guard guard(mutex);
        uint64_t now = GetTickCount64();
        if (socket_ == INVALID_SOCKET || !lease_.IsActive(now) || sample.generation != lease_.generation ||
            sample.submission <= publishedSubmission.load())
            return;
        publishedSubmission = sample.submission;
        auto packet = MakePacket(++sequence_, sample.timestamp, sample.recordIndex, sample.matrix, lease_.nonce);
        (void)sendto(socket_, reinterpret_cast<const char *>(packet.data()), static_cast<int>(packet.size()), 0,
                     reinterpret_cast<const sockaddr *>(&peer_), sizeof(peer_));
    }

  private:
    SOCKET socket_ = INVALID_SOCKET;
    bool winsockStarted_ = false, attemptedTransport_ = false;
    bool discardQueuedRequests_ = true;
    ClientLease lease_;
    sockaddr_in peer_{};
    uint64_t lastPoll_ = 0, sequence_ = 0;
};

Runtime &State()
{
    // Hooks and original function pointers live for the game process lifetime.
    // Do not run COM/WinSock teardown from the Windows loader lock at DLL detach.
    static Runtime *runtime = new Runtime();
    return *runtime;
}

void **Vtable(void *object)
{
    return *static_cast<void ***>(object);
}

bool ReplaceSlot(void **table, size_t slot, void *function)
{
    DWORD previous = 0;
    if (!VirtualProtect(table + slot, sizeof(void *), PAGE_EXECUTE_READWRITE, &previous))
        return false;
    InterlockedExchangePointer(table + slot, function);
    DWORD unused = 0;
    (void)VirtualProtect(table + slot, sizeof(void *), previous, &unused);
    return true;
}

ContextHooks OriginalContextHooks(ID3D11DeviceContext *context)
{
    Runtime &runtime = State();
    std::lock_guard guard(runtime.mutex);
    auto observed = runtime.contexts.find(context);
    if (observed != runtime.contexts.end())
        return observed->second->originals;
    auto found = runtime.contextHooks.find(Vtable(context));
    return found == runtime.contextHooks.end() ? ContextHooks{} : found->second;
}

bool IsMainViewport(ID3D11DeviceContext *context, const CaptureLayout &layout)
{
    D3D11_VIEWPORT viewport{};
    UINT count = 1;
    context->RSGetViewports(&count, &viewport);
    if (count != 1 || viewport.TopLeftX != 0 || viewport.TopLeftY != 0 || viewport.Width < layout.minimumViewportWidth || viewport.Height < layout.minimumViewportHeight)
        return false;
    if (!layout.viewportMatchesTarget)
        return true;
    ID3D11RenderTargetView *view = nullptr;
    context->OMGetRenderTargets(1, &view, nullptr);
    if (!view)
        return false;
    ID3D11Resource *resource = nullptr;
    view->GetResource(&resource);
    Release(view);
    ID3D11Texture2D *texture = nullptr;
    HRESULT result = resource->QueryInterface(__uuidof(ID3D11Texture2D), reinterpret_cast<void **>(&texture));
    Release(resource);
    if (FAILED(result))
        return false;
    D3D11_TEXTURE2D_DESC description{};
    texture->GetDesc(&description);
    Release(texture);
    return std::abs(viewport.Width - description.Width) < 1 && std::abs(viewport.Height - description.Height) < 1;
}

void DrainReadbacks(ID3D11DeviceContext *context, ContextState &state, LeaseSnapshot lease, uint64_t now)
{
    const CaptureLayout &layout = State().profile.layout;
    std::array<CompletedSample, 3> completed{};
    size_t count = 0;
    for (Readback &entry : state.ring)
    {
        if (!entry.pending || context->GetData(entry.completed, nullptr, 0, D3D11_ASYNC_GETDATA_DONOTFLUSH) != S_OK)
            continue;
        D3D11_MAPPED_SUBRESOURCE mapped{};
        HRESULT result = context->Map(entry.staging, 0, D3D11_MAP_READ, D3D11_MAP_FLAG_DO_NOT_WAIT, &mapped);
        if (result == DXGI_ERROR_WAS_STILL_DRAWING)
            continue;
        if (FAILED(result))
        {
            entry.pending = false;
            continue;
        }
        D3D11_MAPPED_SUBRESOURCE cameraMapped{};
        result = context->Map(entry.cameraStaging, 0, D3D11_MAP_READ, D3D11_MAP_FLAG_DO_NOT_WAIT, &cameraMapped);
        if (FAILED(result))
        {
            context->Unmap(entry.staging, 0);
            if (result != DXGI_ERROR_WAS_STILL_DRAWING)
                entry.pending = false;
            continue;
        }
        auto reference = FindCockpitReference({static_cast<const uint8_t *>(mapped.pData) + entry.viewOffset, entry.viewBytes},
                                              {static_cast<const uint8_t *>(cameraMapped.pData), entry.cameraBytes}, layout);
        context->Unmap(entry.cameraStaging, 0);
        context->Unmap(entry.staging, 0);
        entry.pending = false;
        if (reference)
            completed[count++] = {reference->projection, entry.submission, entry.generation, entry.submittedAt,
                                  entry.timestamp, reference->recordIndex};
    }
    Runtime &runtime = State();
    auto newest = NewestCompletedSample(std::span(completed).first(count), lease.generation, now,
                                        runtime.publishedSubmission.load());
    if (newest)
        runtime.Publish(completed[*newest]);
}

bool PrepareReadback(ID3D11DeviceContext *context, Readback &entry, uint32_t byteWidth, uint32_t cameraBytes)
{
    if (entry.staging && entry.cameraStaging && entry.byteWidth == byteWidth && entry.cameraBytes == cameraBytes)
        return true;
    entry.Clear();
    ID3D11Device *device = nullptr;
    context->GetDevice(&device);
    D3D11_BUFFER_DESC description{};
    description.ByteWidth = byteWidth;
    description.Usage = D3D11_USAGE_STAGING;
    description.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    HRESULT result = device->CreateBuffer(&description, nullptr, &entry.staging);
    if (SUCCEEDED(result))
    {
        description.ByteWidth = cameraBytes;
        result = device->CreateBuffer(&description, nullptr, &entry.cameraStaging);
    }
    if (SUCCEEDED(result))
    {
        D3D11_QUERY_DESC query{D3D11_QUERY_EVENT, 0};
        result = device->CreateQuery(&query, &entry.completed);
    }
    Release(device);
    if (FAILED(result))
    {
        entry.Clear();
        return false;
    }
    entry.byteWidth = byteWidth;
    entry.cameraBytes = cameraBytes;
    return true;
}

ID3D11ShaderResourceView *RecordView(ID3D11DeviceContext *context, const CaptureLayout &layout)
{
    ID3D11ShaderResourceView *view = nullptr;
    switch (layout.recordStage)
    {
    case 0: context->VSGetShaderResources(layout.recordSlot, 1, &view); break;
    case 1: context->PSGetShaderResources(layout.recordSlot, 1, &view); break;
    case 2: context->GSGetShaderResources(layout.recordSlot, 1, &view); break;
    case 3: context->HSGetShaderResources(layout.recordSlot, 1, &view); break;
    case 4: context->DSGetShaderResources(layout.recordSlot, 1, &view); break;
    case 5: context->CSGetShaderResources(layout.recordSlot, 1, &view); break;
    }
    return view;
}

ID3D11Buffer *CameraBuffer(ID3D11DeviceContext *context, const CaptureLayout &layout)
{
    ID3D11Buffer *buffer = nullptr;
    switch (layout.cameraStage)
    {
    case 0: context->VSGetConstantBuffers(layout.cameraSlot, 1, &buffer); break;
    case 1: context->PSGetConstantBuffers(layout.cameraSlot, 1, &buffer); break;
    case 2: context->GSGetConstantBuffers(layout.cameraSlot, 1, &buffer); break;
    case 3: context->HSGetConstantBuffers(layout.cameraSlot, 1, &buffer); break;
    case 4: context->DSGetConstantBuffers(layout.cameraSlot, 1, &buffer); break;
    case 5: context->CSGetConstantBuffers(layout.cameraSlot, 1, &buffer); break;
    }
    return buffer;
}

ID3D11DeviceChild *CurrentShader(ID3D11DeviceContext *context, const CaptureLayout &layout)
{
    if (layout.shaderStage == 1)
    {
        ID3D11PixelShader *shader = nullptr;
        context->PSGetShader(&shader, nullptr, nullptr);
        return shader;
    }
    ID3D11VertexShader *shader = nullptr;
    context->VSGetShader(&shader, nullptr, nullptr);
    return shader;
}

void ObserveContext(ID3D11DeviceContext *context);

void Capture(ID3D11DeviceContext *context, const DrawSignature &signature)
{
    Runtime &runtime = State();
    if (!runtime.profile.IsUsable() || signature.kind != runtime.profile.drawKind || signature.vertices == 0 ||
        signature.instances == 0)
        return;
    std::shared_ptr<ContextState> state;
    {
        std::lock_guard guard(runtime.mutex);
        auto found = runtime.contexts.find(context);
        if (found != runtime.contexts.end())
            state = found->second;
    }
    if (!state)
    {
        // External Release0 can race a privately retained context's reacquisition.
        // Drawing proves the caller owns a live context, so observation recovers
        // here even if that earlier release retired the old bookkeeping session.
        ObserveContext(context);
        std::lock_guard guard(runtime.mutex);
        auto found = runtime.contexts.find(context);
        if (found == runtime.contexts.end())
            return;
        state = found->second;
    }
    std::unique_lock guard(state->mutex, std::try_to_lock);
    if (!guard || state->disabled)
        return;
    LeaseSnapshot lease = runtime.PollLease();
    uint64_t now = GetTickCount64();
    if (!lease.active)
    {
        state->Clear();
        return;
    }
    DrainReadbacks(context, *state, lease, now);
    uint64_t frame = runtime.frame.load();
    bool hasPresent = runtime.hasPresent.load();
    if ((hasPresent && state->lastFrame == frame) || (!hasPresent && now - state->lastSubmit < 8))
        return;

    const CaptureLayout &layout = runtime.profile.layout;
    ID3D11DeviceChild *shader = CurrentShader(context, layout);
    if (!shader)
        return;
    uint64_t hash = 0;
    UINT hashBytes = sizeof(hash);
    HRESULT identified = shader->GetPrivateData(ShaderIdentity, &hashBytes, &hash);
    Release(shader);
    if (FAILED(identified) || hashBytes != sizeof(hash) || hash != runtime.profile.shaderHash || !IsMainViewport(context, layout))
        return;

    ID3D11ShaderResourceView *view = RecordView(context, layout);
    if (!view)
        return;
    D3D11_SHADER_RESOURCE_VIEW_DESC viewDescription{};
    view->GetDesc(&viewDescription);
    ID3D11Resource *resource = nullptr;
    view->GetResource(&resource);
    Release(view);
    if ((layout.bufferKind == 0 && viewDescription.ViewDimension != D3D11_SRV_DIMENSION_BUFFER) ||
        (layout.bufferKind == 1 && (viewDescription.ViewDimension != D3D11_SRV_DIMENSION_BUFFEREX ||
         viewDescription.Format != DXGI_FORMAT_R32_TYPELESS || !(viewDescription.BufferEx.Flags & D3D11_BUFFEREX_SRV_FLAG_RAW))))
    {
        Release(resource);
        return;
    }
    ID3D11Buffer *buffer = nullptr;
    HRESULT queried = resource->QueryInterface(__uuidof(ID3D11Buffer), reinterpret_cast<void **>(&buffer));
    Release(resource);
    if (FAILED(queried))
        return;
    D3D11_BUFFER_DESC description{};
    buffer->GetDesc(&description);
    bool structured = (description.MiscFlags & D3D11_RESOURCE_MISC_BUFFER_STRUCTURED) != 0;
    bool raw = (description.MiscFlags & D3D11_RESOURCE_MISC_BUFFER_ALLOW_RAW_VIEWS) != 0;
    uint32_t first = layout.bufferKind == 0 ? viewDescription.Buffer.FirstElement : viewDescription.BufferEx.FirstElement;
    uint32_t count = layout.bufferKind == 0 ? viewDescription.Buffer.NumElements : viewDescription.BufferEx.NumElements;
    auto range = GetRecordViewRange(layout, description.ByteWidth, layout.bufferKind == 0 ? description.StructureByteStride : 4,
                                    first, count);
    if ((layout.bufferKind == 0 ? !structured : !raw) || !range)
    {
        Release(buffer);
        return;
    }
    ID3D11Buffer *cameraBuffer = CameraBuffer(context, layout);
    if (!cameraBuffer)
    {
        Release(buffer);
        return;
    }
    D3D11_BUFFER_DESC cameraDescription{};
    cameraBuffer->GetDesc(&cameraDescription);
    if (cameraDescription.ByteWidth != layout.cameraBytes || !(cameraDescription.BindFlags & D3D11_BIND_CONSTANT_BUFFER))
    {
        Release(cameraBuffer);
        Release(buffer);
        return;
    }
    for (Readback &entry : state->ring)
    {
        if (entry.pending)
            continue;
        if (!PrepareReadback(context, entry, description.ByteWidth, layout.cameraBytes))
            break;
        // Copy the resource before its owning draw. The event query and staging
        // Map below are asynchronous; no Flush, blocking Map or write-map read.
        context->CopyResource(entry.staging, buffer);
        context->CopyResource(entry.cameraStaging, cameraBuffer);
        context->End(entry.completed);
        entry.viewOffset = range->offset;
        entry.viewBytes = range->bytes;
        entry.submittedAt = now;
        entry.generation = lease.generation;
        entry.submission = ++runtime.nextSubmission;
        entry.timestamp = UnixMilliseconds();
        entry.pending = true;
        state->lastFrame = frame;
        state->lastSubmit = now;
        break;
    }
    Release(cameraBuffer);
    Release(buffer);
}

void SafeCapture(ID3D11DeviceContext *context, DrawSignature signature) noexcept
{
    try
    {
        Capture(context, signature);
    }
    catch (...)
    {
        // The experimental observer must never turn a capture failure into a
        // game rendering failure. Subsequent draws still forward unchanged.
        Runtime &runtime = State();
        std::lock_guard guard(runtime.mutex);
        auto found = runtime.contexts.find(context);
        if (found != runtime.contexts.end())
            found->second->disabled = true;
        OutputDebugStringW(L"SrvSurvey camera prototype: observation disabled after a failure; draw forwarded.\n");
    }
}

void STDMETHODCALLTYPE HookDrawIndexed(ID3D11DeviceContext *context, UINT vertices, UINT start, INT base)
{
    SafeCapture(context, {1, vertices, start, 1, 0, base});
    OriginalContextHooks(context).drawIndexed(context, vertices, start, base);
}

void STDMETHODCALLTYPE HookDraw(ID3D11DeviceContext *context, UINT vertices, UINT start)
{
    SafeCapture(context, {2, vertices, start, 1, 0, 0});
    OriginalContextHooks(context).draw(context, vertices, start);
}

void STDMETHODCALLTYPE HookDrawIndexedInstanced(ID3D11DeviceContext *context, UINT vertices, UINT instances, UINT start,
                                              INT base, UINT firstInstance)
{
    SafeCapture(context, {3, vertices, start, instances, firstInstance, base});
    OriginalContextHooks(context).drawIndexedInstanced(context, vertices, instances, start, base, firstInstance);
}

void STDMETHODCALLTYPE HookDrawInstanced(ID3D11DeviceContext *context, UINT vertices, UINT instances, UINT start,
                                       UINT firstInstance)
{
    SafeCapture(context, {4, vertices, start, instances, firstInstance, 0});
    OriginalContextHooks(context).drawInstanced(context, vertices, instances, start, firstInstance);
}

ULONG STDMETHODCALLTYPE HookContextRelease(ID3D11DeviceContext *context)
{
    Runtime &runtime = State();
    ContextHooks originals = OriginalContextHooks(context);
    std::shared_ptr<ContextState> observed;
    {
        std::lock_guard guard(runtime.mutex);
        auto found = runtime.contexts.find(context);
        if (found != runtime.contexts.end())
        {
            observed = found->second;
        }
    }
    ULONG references = originals.release(context);
    if (references == 0)
    {
        std::shared_ptr<ContextState> released;
        {
            std::lock_guard guard(runtime.mutex);
            auto found = runtime.contexts.find(context);
            // Retire the actual final external release, even when an intervening
            // acquisition was already released. A still-live draw can register a
            // fresh observation after this retirement without another Get call.
            if (found != runtime.contexts.end() && found->second == observed)
            {
                released = std::move(found->second);
                runtime.contexts.erase(found);
                if (runtime.contexts.empty())
                    runtime.CloseTransport();
            }
        }
    }
    return references;
}

void ObserveContext(ID3D11DeviceContext *context)
{
    if (!context || context->GetType() != D3D11_DEVICE_CONTEXT_IMMEDIATE)
        return;
    Runtime &runtime = State();
    std::lock_guard guard(runtime.mutex);
    void **table = Vtable(context);
    if (!runtime.contextHooks.contains(table))
    {
        runtime.contextHooks[table] = {reinterpret_cast<ContextRelease>(table[2]), reinterpret_cast<DrawIndexed>(table[12]),
                                      reinterpret_cast<Draw>(table[13]), reinterpret_cast<DrawIndexedInstanced>(table[20]),
                                      reinterpret_cast<DrawInstanced>(table[21])};
        // Retain originals before the first patch becomes visible to other threads.
        (void)ReplaceSlot(table, 2, reinterpret_cast<void *>(HookContextRelease));
        (void)ReplaceSlot(table, 12, reinterpret_cast<void *>(HookDrawIndexed));
        (void)ReplaceSlot(table, 13, reinterpret_cast<void *>(HookDraw));
        (void)ReplaceSlot(table, 20, reinterpret_cast<void *>(HookDrawIndexedInstanced));
        (void)ReplaceSlot(table, 21, reinterpret_cast<void *>(HookDrawInstanced));
    }
    if (!runtime.contexts.contains(context))
    {
        auto state = std::make_shared<ContextState>();
        state->originals = runtime.contextHooks.at(table);
        runtime.contexts.emplace(context, std::move(state));
    }
    runtime.OpenTransport();
}

void STDMETHODCALLTYPE HookGetImmediateContext(ID3D11Device *device, ID3D11DeviceContext **context)
{
    Runtime &runtime = State();
    GetImmediateContext original;
    {
        std::lock_guard guard(runtime.mutex);
        original = runtime.deviceHooks.at(Vtable(device)).getImmediateContext;
    }
    original(device, context);
    if (context && *context)
    {
        try
        {
            // A device can privately own and re-expose an immediate context
            // after its external reference count returned zero (e.g. DXVK).
            // Reacquisition therefore creates a fresh observation session.
            ObserveContext(*context);
        }
        catch (...)
        {
            OutputDebugStringW(L"SrvSurvey camera prototype: reacquired context observation unavailable.\n");
        }
    }
}

HRESULT STDMETHODCALLTYPE HookCreateVertexShader(ID3D11Device *device, const void *bytecode, SIZE_T length,
                                                ID3D11ClassLinkage *linkage, ID3D11VertexShader **shader)
{
    Runtime &runtime = State();
    CreateVertexShader original;
    {
        std::lock_guard guard(runtime.mutex);
        original = runtime.deviceHooks.at(Vtable(device)).createVertexShader;
    }
    HRESULT result = original(device, bytecode, length, linkage, shader);
    if (SUCCEEDED(result) && shader && *shader && bytecode && length <= 16 * 1024 * 1024)
    {
        uint64_t hash = ShaderHash({static_cast<const uint8_t *>(bytecode), length});
        if (hash == runtime.profile.shaderHash)
            (void)(*shader)->SetPrivateData(ShaderIdentity, sizeof(hash), &hash);
    }
    return result;
}

HRESULT STDMETHODCALLTYPE HookCreatePixelShader(ID3D11Device *device, const void *bytecode, SIZE_T length,
                                               ID3D11ClassLinkage *linkage, ID3D11PixelShader **shader)
{
    Runtime &runtime = State();
    CreatePixelShader original;
    {
        std::lock_guard guard(runtime.mutex);
        original = runtime.deviceHooks.at(Vtable(device)).createPixelShader;
    }
    HRESULT result = original(device, bytecode, length, linkage, shader);
    if (SUCCEEDED(result) && shader && *shader && bytecode && length <= 16 * 1024 * 1024)
    {
        uint64_t hash = ShaderHash({static_cast<const uint8_t *>(bytecode), length});
        if (hash == runtime.profile.shaderHash)
            (void)(*shader)->SetPrivateData(ShaderIdentity, sizeof(hash), &hash);
    }
    return result;
}

HRESULT STDMETHODCALLTYPE HookPresent(IDXGISwapChain *swapChain, UINT interval, UINT flags)
{
    Runtime &runtime = State();
    Present original;
    {
        std::lock_guard guard(runtime.mutex);
        original = runtime.presentHooks.at(Vtable(swapChain));
    }
    ++runtime.frame;
    return original(swapChain, interval, flags);
}

void ObserveSwapChain(IDXGISwapChain *swapChain)
{
    if (!swapChain)
        return;
    Runtime &runtime = State();
    std::lock_guard guard(runtime.mutex);
    void **table = Vtable(swapChain);
    if (!runtime.presentHooks.contains(table))
    {
        runtime.presentHooks[table] = reinterpret_cast<Present>(table[8]);
        if (ReplaceSlot(table, 8, reinterpret_cast<void *>(HookPresent)))
            runtime.hasPresent = true;
        else
            runtime.presentHooks.erase(table);
    }
}

HRESULT STDMETHODCALLTYPE HookCreateSwapChain(IDXGIFactory *factory, IUnknown *device, DXGI_SWAP_CHAIN_DESC *description,
                                            IDXGISwapChain **swapChain)
{
    Runtime &runtime = State();
    CreateSwapChain original;
    {
        std::lock_guard guard(runtime.mutex);
        original = runtime.factoryHooks.at(Vtable(factory)).create;
    }
    HRESULT result = original(factory, device, description, swapChain);
    if (SUCCEEDED(result) && swapChain && *swapChain)
    {
        try
        {
            ObserveSwapChain(*swapChain);
        }
        catch (...)
        {
            OutputDebugStringW(L"SrvSurvey camera prototype: swap-chain observation unavailable.\n");
        }
    }
    return result;
}

HRESULT STDMETHODCALLTYPE HookCreateSwapChainForHwnd(
    IDXGIFactory2 *factory, IUnknown *device, HWND window, const DXGI_SWAP_CHAIN_DESC1 *description,
    const DXGI_SWAP_CHAIN_FULLSCREEN_DESC *fullscreen, IDXGIOutput *output, IDXGISwapChain1 **swapChain)
{
    Runtime &runtime = State();
    CreateSwapChainForHwnd original;
    {
        std::lock_guard guard(runtime.mutex);
        original = runtime.factoryHooks.at(Vtable(factory)).createForHwnd;
    }
    HRESULT result = original(factory, device, window, description, fullscreen, output, swapChain);
    if (SUCCEEDED(result) && swapChain && *swapChain)
    {
        try
        {
            ObserveSwapChain(*swapChain);
        }
        catch (...)
        {
            OutputDebugStringW(L"SrvSurvey camera prototype: swap-chain observation unavailable.\n");
        }
    }
    return result;
}

void ObserveFactory(ID3D11Device *device)
{
    IDXGIDevice *dxgi = nullptr;
    if (FAILED(device->QueryInterface(__uuidof(IDXGIDevice), reinterpret_cast<void **>(&dxgi))))
        return;
    IDXGIAdapter *adapter = nullptr;
    HRESULT result = dxgi->GetAdapter(&adapter);
    Release(dxgi);
    if (FAILED(result))
        return;
    IDXGIFactory *factory = nullptr;
    result = adapter->GetParent(__uuidof(IDXGIFactory), reinterpret_cast<void **>(&factory));
    Release(adapter);
    if (FAILED(result))
        return;
    Runtime &runtime = State();
    {
        std::lock_guard guard(runtime.mutex);
        void **table = Vtable(factory);
        if (!runtime.factoryHooks.contains(table))
        {
            runtime.factoryHooks[table].create = reinterpret_cast<CreateSwapChain>(table[10]);
            (void)ReplaceSlot(table, 10, reinterpret_cast<void *>(HookCreateSwapChain));
        }
    }
    IDXGIFactory2 *extended = nullptr;
    result = factory->QueryInterface(__uuidof(IDXGIFactory2), reinterpret_cast<void **>(&extended));
    Release(factory);
    if (FAILED(result))
        return;
    {
        std::lock_guard guard(runtime.mutex);
        void **table = Vtable(extended);
        auto &hooks = runtime.factoryHooks[table];
        if (!hooks.create)
        {
            hooks.create = reinterpret_cast<CreateSwapChain>(table[10]);
            (void)ReplaceSlot(table, 10, reinterpret_cast<void *>(HookCreateSwapChain));
        }
        if (!hooks.createForHwnd)
        {
            hooks.createForHwnd = reinterpret_cast<CreateSwapChainForHwnd>(table[15]);
            (void)ReplaceSlot(table, 15, reinterpret_cast<void *>(HookCreateSwapChainForHwnd));
        }
    }
    Release(extended);
}

void ObserveDevice(ID3D11Device *device, ID3D11DeviceContext *context, IDXGISwapChain *swapChain)
{
    ID3D11Device *recovered = nullptr;
    if (!device && context)
        context->GetDevice(&recovered);
    if (!device && !recovered && swapChain)
        (void)swapChain->GetDevice(__uuidof(ID3D11Device), reinterpret_cast<void **>(&recovered));
    auto releaseDevice = [](ID3D11Device *value) { if (value) value->Release(); };
    std::unique_ptr<ID3D11Device, decltype(releaseDevice)> recovery(recovered, releaseDevice);
    if (!device)
        device = recovered;
    if (!device)
        return;
    Runtime &runtime = State();
    if (!runtime.profile.IsUsable())
    {
        OutputDebugStringW(L"SrvSurvey camera prototype: waiting for a validated cockpit capture profile; capture disabled.\n");
        return;
    }
    ID3D11DeviceContext *borrowed = nullptr;
    if (!context)
    {
        device->GetImmediateContext(&borrowed);
        context = borrowed;
    }
    if (!context)
        return;
    {
        std::lock_guard guard(runtime.mutex);
        void **deviceTable = Vtable(device);
        if (!runtime.deviceHooks.contains(deviceTable))
        {
            runtime.deviceHooks[deviceTable] = {reinterpret_cast<CreateVertexShader>(deviceTable[12]),
                                                reinterpret_cast<CreatePixelShader>(deviceTable[15]),
                                                reinterpret_cast<GetImmediateContext>(deviceTable[40])};
            (void)ReplaceSlot(deviceTable, 12, reinterpret_cast<void *>(HookCreateVertexShader));
            (void)ReplaceSlot(deviceTable, 15, reinterpret_cast<void *>(HookCreatePixelShader));
            (void)ReplaceSlot(deviceTable, 40, reinterpret_cast<void *>(HookGetImmediateContext));
        }
    }
    ObserveContext(context);
    ObserveFactory(device);
    ObserveSwapChain(swapChain);
    Release(borrowed);
}

HMODULE RealD3D11()
{
    static HMODULE real = []
    {
        wchar_t path[MAX_PATH]{};
        UINT length = GetSystemDirectoryW(path, MAX_PATH);
        if (!length || length + 11 >= MAX_PATH)
            return static_cast<HMODULE>(nullptr);
        return LoadLibraryExW((std::wstring(path) + L"\\d3d11.dll").c_str(), nullptr, 0);
    }();
    return real;
}
} // namespace

extern "C" HRESULT WINAPI D3D11CreateDevice(IDXGIAdapter *adapter, D3D_DRIVER_TYPE driverType, HMODULE software,
                                           UINT flags, const D3D_FEATURE_LEVEL *levels, UINT levelCount, UINT sdkVersion,
                                           ID3D11Device **device, D3D_FEATURE_LEVEL *selectedLevel,
                                           ID3D11DeviceContext **immediateContext)
{
    HMODULE real = RealD3D11();
    if (!real)
        return E_FAIL;
    auto create = std::bit_cast<PFN_D3D11_CREATE_DEVICE>(GetProcAddress(real, "D3D11CreateDevice"));
    if (!create)
        return E_FAIL;
    HRESULT result = create(adapter, driverType, software, flags, levels, levelCount, sdkVersion, device, selectedLevel,
                            immediateContext);
    if (SUCCEEDED(result))
    {
        try
        {
            ObserveDevice(device ? *device : nullptr, immediateContext ? *immediateContext : nullptr, nullptr);
        }
        catch (...)
        {
            OutputDebugStringW(L"SrvSurvey camera prototype: device observation unavailable; game device retained.\n");
        }
    }
    return result;
}

extern "C" HRESULT WINAPI D3D11CreateDeviceAndSwapChain(
    IDXGIAdapter *adapter, D3D_DRIVER_TYPE driverType, HMODULE software, UINT flags, const D3D_FEATURE_LEVEL *levels,
    UINT levelCount, UINT sdkVersion, const DXGI_SWAP_CHAIN_DESC *swapDescription, IDXGISwapChain **swapChain,
    ID3D11Device **device, D3D_FEATURE_LEVEL *selectedLevel, ID3D11DeviceContext **immediateContext)
{
    HMODULE real = RealD3D11();
    if (!real)
        return E_FAIL;
    auto create = std::bit_cast<PFN_D3D11_CREATE_DEVICE_AND_SWAP_CHAIN>(
        GetProcAddress(real, "D3D11CreateDeviceAndSwapChain"));
    if (!create)
        return E_FAIL;
    HRESULT result = create(adapter, driverType, software, flags, levels, levelCount, sdkVersion, swapDescription, swapChain,
                            device, selectedLevel, immediateContext);
    if (SUCCEEDED(result))
    {
        try
        {
            ObserveDevice(device ? *device : nullptr, immediateContext ? *immediateContext : nullptr,
                          swapChain ? *swapChain : nullptr);
        }
        catch (...)
        {
            OutputDebugStringW(L"SrvSurvey camera prototype: device observation unavailable; game device retained.\n");
        }
    }
    return result;
}

BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        moduleHandle = module;
        DisableThreadLibraryCalls(module);
    }
    return TRUE;
}
