#include <winsock2.h>
#include <windows.h>
#include <d3d11.h>
#include <d3dcompiler.h>
#include <dxgi.h>
#include "../CameraProtocol.h"
#include "../CockpitReference.h"
#include "CapturedReferenceFixtures.h"
#include <atomic>
#include <thread>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <utility>
#include <vector>

using namespace srvsurvey::camera;
namespace
{
LONG CALLBACK DescribeException(PEXCEPTION_POINTERS info)
{
    DWORD code = info->ExceptionRecord->ExceptionCode;
    if (code == EXCEPTION_ACCESS_VIOLATION || code == EXCEPTION_INT_DIVIDE_BY_ZERO || code == EXCEPTION_STACK_OVERFLOW)
    {
        MEMORY_BASIC_INFORMATION memory{};
        VirtualQuery(info->ExceptionRecord->ExceptionAddress, &memory, sizeof(memory));
        wchar_t module[MAX_PATH]{};
        GetModuleFileNameW(static_cast<HMODULE>(memory.AllocationBase), module, MAX_PATH);
        uintptr_t offset = reinterpret_cast<uintptr_t>(info->ExceptionRecord->ExceptionAddress) -
                           reinterpret_cast<uintptr_t>(memory.AllocationBase);
        std::wcerr << L"Native exception code=0x" << std::hex << code << L" module=" << module << L" offset=0x" << offset
                   << std::dec << std::endl;
    }
    return EXCEPTION_CONTINUE_SEARCH;
}
void Require(bool condition, const char *message)
{
    if (!condition)
    {
        std::cerr << "FAIL: " << message << std::endl;
        throw std::runtime_error(message);
    }
}
void Check(HRESULT result, const char *message)
{
    if (FAILED(result))
    {
        std::cerr << message << " HRESULT=0x" << std::hex << static_cast<uint32_t>(result) << std::dec << '\n';
        throw std::runtime_error(message);
    }
}
template <typename T> struct Com
{
    T *value = nullptr;
    Com() = default;
    Com(const Com &) = delete;
    Com &operator=(const Com &) = delete;
    Com(Com &&other) noexcept : value(std::exchange(other.value, nullptr)) {}
    Com &operator=(Com &&other) noexcept
    {
        if (this != &other) { Reset(); value = std::exchange(other.value, nullptr); }
        return *this;
    }
    ~Com() { Reset(); }
    T *operator->() const { return value; }
    T **Out() { return &value; }
    void Reset() { if (value) { value->Release(); value = nullptr; } }
};
// Delay the external COM boundary after Release0 but before the observer sees
// its return. This deterministically drives the real reacquisition race.
using NativeRelease = ULONG(STDMETHODCALLTYPE *)(ID3D11DeviceContext *);
NativeRelease boundaryRelease = nullptr;
HANDLE releaseReached = nullptr, releaseContinue = nullptr;
std::atomic<bool> delayRelease{false};
ULONG STDMETHODCALLTYPE DelayedRelease(ID3D11DeviceContext *context)
{
    ULONG references = boundaryRelease(context);
    if (references == 0 && delayRelease.exchange(false))
    {
        SetEvent(releaseReached);
        (void)WaitForSingleObject(releaseContinue, 5000);
    }
    return references;
}

void InstallReleaseBoundary()
{
    wchar_t system[MAX_PATH]{};
    Require(GetSystemDirectoryW(system, MAX_PATH) > 0, "Cannot locate the native D3D11 library");
    HMODULE native = LoadLibraryW((std::wstring(system) + L"\\d3d11.dll").c_str());
    Require(native != nullptr, "Cannot load the native D3D11 library");
    auto create = std::bit_cast<PFN_D3D11_CREATE_DEVICE>(GetProcAddress(native, "D3D11CreateDevice"));
    Require(create != nullptr, "Cannot create the COM boundary fixture");
    Com<ID3D11Device> device;
    Com<ID3D11DeviceContext> context;
    Check(create(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, nullptr, 0, D3D11_SDK_VERSION,
                 device.Out(), nullptr, context.Out()), "Cannot create the COM boundary device");
    void **table = *reinterpret_cast<void ***>(context.value);
    boundaryRelease = std::bit_cast<NativeRelease>(table[2]);
    releaseReached = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    releaseContinue = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    Require(releaseReached && releaseContinue, "Cannot create release/reacquisition barriers");
    DWORD previous = 0, ignored = 0;
    Require(VirtualProtect(table + 2, sizeof(void *), PAGE_EXECUTE_READWRITE, &previous) != 0,
            "Cannot instrument the COM boundary");
    InterlockedExchangePointer(table + 2, std::bit_cast<void *>(&DelayedRelease));
    (void)VirtualProtect(table + 2, sizeof(void *), previous, &ignored);
}

uint32_t Read32(const uint8_t *bytes)
{
    uint32_t value = 0;
    for (size_t index = 0; index < 4; ++index)
        value |= uint32_t(bytes[index]) << (index * 8);
    return value;
}
uint64_t Read64(const uint8_t *bytes)
{
    return Read32(bytes) | uint64_t(Read32(bytes + 4)) << 32;
}
int64_t NowMilliseconds()
{
    FILETIME value{};
    GetSystemTimeAsFileTime(&value);
    ULARGE_INTEGER ticks{};
    ticks.LowPart = value.dwLowDateTime;
    ticks.HighPart = value.dwHighDateTime;
    return static_cast<int64_t>(ticks.QuadPart / 10000 - 11644473600000ULL);
}
std::vector<uint8_t> CapturedInstances()
{
    std::vector<uint8_t> bytes(captured::center.bytes);
    for (const auto &record : captured::center.records)
        for (size_t index = 0; index < 32; ++index)
        {
            char hex[3]{record.first32[index * 2], record.first32[index * 2 + 1], 0};
            bytes[record.index * CockpitRecordStride + index] = static_cast<uint8_t>(std::strtoul(hex, nullptr, 16));
        }
    return bytes;
}
std::vector<uint8_t> CapturedConstants()
{
    std::vector<uint8_t> bytes(CameraConstantBytes);
    for (size_t index = 0; index < captured::center.cameraRows.size(); ++index)
        Write32(bytes, 270 * 16 + index * 4, std::bit_cast<uint32_t>(captured::center.cameraRows[index]));
    return bytes;
}

struct Probe
{
    SOCKET socket = INVALID_SOCKET;
    Nonce nonce{};
    uint64_t lastSent = 0;
    Probe()
    {
        WSADATA data{};
        Require(WSAStartup(MAKEWORD(2, 2), &data) == 0, "Probe WSAStartup failed");
        socket = ::socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
        Require(socket != INVALID_SOCKET, "Probe socket failed");
        sockaddr_in address{};
        address.sin_family = AF_INET;
        address.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
        Require(bind(socket, reinterpret_cast<sockaddr *>(&address), sizeof(address)) == 0, "Probe bind failed");
        u_long nonblocking = 1;
        Require(ioctlsocket(socket, FIONBIO, &nonblocking) == 0, "Probe nonblocking mode failed");
        nonce.fill(0x35);
    }
    ~Probe() { if (socket != INVALID_SOCKET) closesocket(socket); WSACleanup(); }
    void Renew()
    {
        uint64_t now = GetTickCount64();
        if (lastSent && now - lastSent < 100)
            return;
        std::array<uint8_t, RequestBytes> request{};
        std::copy(Magic.begin(), Magic.end(), request.begin());
        std::copy(nonce.begin(), nonce.end(), request.begin() + 8);
        sockaddr_in bridge{};
        bridge.sin_family = AF_INET;
        bridge.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
        bridge.sin_port = htons(47765);
        Require(sendto(socket, reinterpret_cast<const char *>(request.data()), static_cast<int>(request.size()), 0,
                       reinterpret_cast<const sockaddr *>(&bridge), sizeof(bridge)) == static_cast<int>(request.size()),
                "Probe heartbeat send failed");
        lastSent = now;
    }
    uint64_t Receive(bool verify)
    {
        uint64_t latest = 0;
        for (size_t count = 0; count < 32; ++count)
        {
            std::array<uint8_t, PacketBytes + 1> bytes{};
            sockaddr_in from{};
            int fromSize = sizeof(from);
            int length = recvfrom(socket, reinterpret_cast<char *>(bytes.data()), static_cast<int>(bytes.size()), 0,
                                  reinterpret_cast<sockaddr *>(&from), &fromSize);
            if (length == SOCKET_ERROR)
            {
                int error = WSAGetLastError();
                Require(error == WSAEWOULDBLOCK || error == WSAECONNRESET, "Probe receive failed");
                break;
            }
            if (length != PacketBytes || !std::equal(Magic.begin(), Magic.end(), bytes.begin()) ||
                !std::equal(nonce.begin(), nonce.end(), bytes.begin() + 104))
                continue;
            if (verify)
            {
                Require(Read32(bytes.data() + 8) == 1 && Read32(bytes.data() + 12) == 136 && Read32(bytes.data() + 32) == 1,
                        "Live response has incorrect protocol fields");
                int64_t timestamp = static_cast<int64_t>(Read64(bytes.data() + 24));
                Require(timestamp <= NowMilliseconds() + 100 && NowMilliseconds() - timestamp < 1000,
                        "Live camera sample is stale or future dated");
                Matrix matrix{};
                for (size_t index = 0; index < 16; ++index)
                    matrix[index] = std::bit_cast<float>(Read32(bytes.data() + 40 + index * 4));
                Require(IsUsableProjection(matrix), "Live WVP is not finite and invertible");
                Require(std::abs(matrix[0] - 1.032662f) < 0.001 && std::abs(matrix[5] - 1.835844f) < 0.001 &&
                            std::abs(matrix[11] - 0.025f) < 0.0001 && std::abs(matrix[14] - 1) < 0.001,
                        "Live WVP does not match the captured centered cockpit");
                double denominator = matrix[13] * 1.61 + matrix[14] + matrix[15];
                double x = (matrix[1] * 1.61 + matrix[2] + matrix[3]) / denominator;
                double y = (matrix[5] * 1.61 + matrix[6] + matrix[7]) / denominator;
                Require(std::abs(x) < 0.001 && std::abs(y) < 0.001,
                        "A known cockpit point is not at the expected centered screen location");
            }
            latest = std::max(latest, Read64(bytes.data() + 16));
        }
        return latest;
    }
};

struct Scene
{
    UINT presentFlags = 0;
    bool catalogLayout = false;
    Com<ID3D11Device> device;
    Com<ID3D11DeviceContext> context;
    Com<IDXGISwapChain> swap;
    Com<ID3D11RenderTargetView> target;
    Com<ID3D11VertexShader> vertexShader;
    Com<ID3D11PixelShader> pixelShader;
    Com<ID3D11InputLayout> layout;
    Com<ID3D11Buffer> vertices, indices, constants, instances;
    Com<ID3D11ShaderResourceView> instancesView;
    void Bind()
    {
        UINT stride = sizeof(float) * 2, offset = 0;
        context->IASetInputLayout(layout.value);
        context->IASetVertexBuffers(0, 1, &vertices.value, &stride, &offset);
        context->IASetIndexBuffer(indices.value, DXGI_FORMAT_R16_UINT, 0);
        context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        context->VSSetShader(vertexShader.value, nullptr, 0);
        context->PSSetShader(pixelShader.value, nullptr, 0);
        if (catalogLayout)
        {
            context->PSSetConstantBuffers(3, 1, &constants.value);
            context->PSSetShaderResources(7, 1, &instancesView.value);
        }
        else
        {
            context->VSSetConstantBuffers(1, 1, &constants.value);
            context->VSSetShaderResources(33, 1, &instancesView.value);
        }
        context->OMSetRenderTargets(1, &target.value, nullptr);
        D3D11_VIEWPORT viewport{0, 0, 640, 480, 0, 1};
        context->RSSetViewports(1, &viewport);
    }
    void Frame()
    {
        const float clear[4]{};
        context->ClearRenderTargetView(target.value, clear);
        context->DrawIndexedInstanced(3, 1, 0, 0, 0);
        // This hidden test window can be occluded: submit the harness renderer's
        // work explicitly. The bridge itself still never calls Flush or waits.
        context->Flush();
        Check(swap->Present(0, presentFlags), "Hidden swap-chain Present failed");
    }
};

Com<ID3DBlob> Compile(pD3DCompile compile, const char *source, const char *profile)
{
    Com<ID3DBlob> shader, errors;
    HRESULT result = compile(source, std::strlen(source), "camera-integration", nullptr, nullptr, "main", profile,
                             D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, shader.Out(), errors.Out());
    if (FAILED(result) && errors.value)
        std::cerr << static_cast<const char *>(errors->GetBufferPointer()) << '\n';
    Check(result, "Harness shader compile failed");
    return shader;
}

uint64_t ReceiveFrame(Scene &scene, Probe &probe)
{
    uint64_t started = GetTickCount64();
    while (GetTickCount64() - started < 7000)
    {
        probe.Renew();
        scene.Frame();
        if (uint64_t sequence = probe.Receive(true))
            return sequence;
        Sleep(10);
    }
    std::cerr << "FAIL: No verified live camera packet within seven seconds" << std::endl;
    throw std::runtime_error("No verified live camera packet within seven seconds");
}
} // namespace

int wmain(int argc, wchar_t **argv)
{
    try
    {
        AddVectoredExceptionHandler(1, DescribeException);
        Require(argc >= 2 && argc <= 9, "Pass the built DLL path, optionally --present-test, --without-observer, --mapped-window --catalog-layout, --omit-device-output, --swapchain-only or --concurrent-reacquire");
        bool presentTest = false, observerDisabled = false, mappedWindow = false, catalogLayout = false;
        bool omitDevice = false, swapchainOnly = false, concurrentReacquire = false;
        for (int index = 2; index < argc; ++index)
        {
            if (std::wcscmp(argv[index], L"--present-test") == 0) presentTest = true;
            else if (std::wcscmp(argv[index], L"--without-observer") == 0) observerDisabled = true;
            else if (std::wcscmp(argv[index], L"--mapped-window") == 0) mappedWindow = true;
            else if (std::wcscmp(argv[index], L"--catalog-layout") == 0) catalogLayout = true;
            else if (std::wcscmp(argv[index], L"--omit-device-output") == 0) omitDevice = true;
            else if (std::wcscmp(argv[index], L"--swapchain-only") == 0) swapchainOnly = true;
            else if (std::wcscmp(argv[index], L"--concurrent-reacquire") == 0) concurrentReacquire = true;
            else throw std::runtime_error("Unknown harness option");
        }
        HMODULE compiler = LoadLibraryW(L"d3dcompiler_47.dll");
        Require(compiler != nullptr, "d3dcompiler_47.dll is unavailable");
        auto compile = std::bit_cast<pD3DCompile>(GetProcAddress(compiler, "D3DCompile"));
        Require(compile != nullptr, "D3DCompile is unavailable");
        auto vertex = Compile(compile, catalogLayout ?
            "float4 main(float2 p:POSITION):SV_POSITION{return float4(p,0.5,1);}" :
            "struct I { float4 d[21]; }; StructuredBuffer<I> s : register(t33);"
            "cbuffer C : register(b1) { float4 c[336]; };"
            "float4 main(float2 p:POSITION,uint i:SV_InstanceID):SV_POSITION"
            "{return float4(p+c[270].xy*0.000001+s[i].d[1].xy*0.000001,0.5,1);}", "vs_5_0");
        auto pixel = Compile(compile, catalogLayout ?
            "struct I { float4 d[22]; }; StructuredBuffer<I> s : register(t7);"
            "cbuffer C : register(b3) { float4 c[340]; };"
            "float4 main():SV_TARGET{return float4(c[274].xy*0.000001+s[0].d[2].xy*0.000001,0,0);}" :
            "float4 main():SV_TARGET{return float4(0,0,0,0);}", "ps_5_0");
        ID3DBlob *identity = catalogLayout ? pixel.value : vertex.value;
        uint64_t shaderHash = ShaderHash({static_cast<const uint8_t *>(identity->GetBufferPointer()), identity->GetBufferSize()});
        wchar_t temporary[MAX_PATH]{};
        DWORD temporaryLength = GetTempPathW(MAX_PATH, temporary);
        Require(temporaryLength > 0 && temporaryLength < MAX_PATH, "Windows temporary directory unavailable");
        std::wstring directory = std::wstring(temporary) + L"SrvSurveyCameraIntegration-" + std::to_wstring(GetCurrentProcessId()) +
                                 L"-" + std::to_wstring(GetTickCount64());
        Require(CreateDirectoryW(directory.c_str(), nullptr) != 0, "Harness temporary directory already exists");
        std::wstring dll = directory + L"\\SrvSurveyCameraBridge.dll", ini = directory + L"\\SrvSurveyCameraBridge.ini";
        std::wstring cachedIni = directory + L"\\profile.ini", paths = directory + L"\\SrvSurveyCameraBridge.paths.ini";
        Require(CopyFileW(argv[1], dll.c_str(), TRUE) != 0, "Cannot copy bridge into the isolated harness directory");
        wchar_t hash[32]{};
        std::swprintf(hash, 32, L"%016llx", static_cast<unsigned long long>(shaderHash));
        wchar_t oldHash[32]{};
        std::swprintf(oldHash, 32, L"%016llx", static_cast<unsigned long long>(shaderHash ^ 1));
        Require(WritePrivateProfileStringW(L"Capture", L"Validated", observerDisabled ? L"0" : L"1", ini.c_str()) != 0 &&
                    WritePrivateProfileStringW(L"Capture", L"VertexShaderHash", oldHash, ini.c_str()) != 0 &&
                    WritePrivateProfileStringW(L"Capture", L"RigidLayoutVersion", L"1", ini.c_str()) != 0 &&
                    WritePrivateProfileStringW(L"Capture", L"DrawKind", L"3", ini.c_str()) != 0 &&
                    WritePrivateProfileStringW(L"Capture", L"ProfileVersion", L"1", ini.c_str()) != 0,
                "Cannot write isolated harness profile");
        Require(WritePrivateProfileStringW(L"Capture", L"Validated", L"1", cachedIni.c_str()) != 0 &&
                    WritePrivateProfileStringW(L"Capture", L"VertexShaderHash", hash, cachedIni.c_str()) != 0 &&
                    WritePrivateProfileStringW(L"Capture", L"RigidLayoutVersion", L"1", cachedIni.c_str()) != 0 &&
                    WritePrivateProfileStringW(L"Capture", L"DrawKind", L"3", cachedIni.c_str()) != 0 &&
                    WritePrivateProfileStringW(L"Capture", L"ProtocolVersion", L"1", cachedIni.c_str()) != 0 &&
                    WritePrivateProfileStringW(L"Capture", L"ProfileVersion", L"2", cachedIni.c_str()) != 0 &&
                    WritePrivateProfileStringW(L"Updates", L"CachedProfilePath", cachedIni.c_str(), paths.c_str()) != 0,
                "Cannot write isolated cached profile and locator");
        if (catalogLayout)
        {
            Require(WritePrivateProfileStringW(L"Capture", L"RigidLayoutVersion", L"2", cachedIni.c_str()) != 0,
                    "Cannot select the declarative decoder");
            const std::pair<const wchar_t *, const wchar_t *> fields[]{
                {L"SchemaVersion", L"1"}, {L"RequiredBridgeVersion", L"2"}, {L"ShaderStage", L"ps"},
                {L"RecordStage", L"ps"}, {L"RecordSlot", L"7"}, {L"CameraStage", L"ps"}, {L"CameraSlot", L"3"},
                {L"BufferElementStride", L"352"}, {L"RecordStride", L"352"}, {L"PositionOffset", L"32"},
                {L"RotationOffset", L"24"}, {L"CameraBytes", L"5440"}, {L"ProjectionOffset", L"4384"},
                {L"CameraPositionOffset", L"4464"}, {L"AxesOffset", L"4496"}};
            for (const auto &[key, value] : fields)
                Require(WritePrivateProfileStringW(L"Layout", key, value, cachedIni.c_str()) != 0,
                        "Cannot write a declarative harness layout");
        }
        if (concurrentReacquire)
            InstallReleaseBoundary();
        HMODULE bridge = LoadLibraryW(dll.c_str());
        Require(bridge != nullptr, "Cannot load the built bridge DLL");
        auto create = std::bit_cast<PFN_D3D11_CREATE_DEVICE>(GetProcAddress(bridge, "D3D11CreateDevice"));
        Require(create != nullptr, "Bridge device-creation export is unavailable");
        Scene scene;
        scene.catalogLayout = catalogLayout;
        scene.presentFlags = presentTest ? DXGI_PRESENT_TEST : 0;
        D3D_FEATURE_LEVEL level{};
        WNDCLASSW windowClass{};
        windowClass.lpfnWndProc = DefWindowProcW;
        windowClass.hInstance = GetModuleHandleW(nullptr);
        windowClass.lpszClassName = L"SrvSurveyHiddenCameraIntegration";
        Require(RegisterClassW(&windowClass) != 0, "Hidden harness window registration failed");
        HWND window = CreateWindowExW(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW, windowClass.lpszClassName, L"Camera bridge integration",
                                      WS_POPUP, 0, 0, 640, 480, nullptr, nullptr, windowClass.hInstance, nullptr);
        Require(window != nullptr, "Hidden harness window creation failed");
        if (mappedWindow)
        {
            // Only use this opt-in harness mode on an isolated test display.
            // Some Wine/X11 WSI paths require a mapped HWND for valid teardown.
            ShowWindow(window, SW_SHOWNOACTIVATE);
            UpdateWindow(window);
        }
        DXGI_SWAP_CHAIN_DESC description{};
        description.BufferDesc = {640, 480, {60, 1}, DXGI_FORMAT_R8G8B8A8_UNORM, DXGI_MODE_SCANLINE_ORDER_UNSPECIFIED,
                                  DXGI_MODE_SCALING_UNSPECIFIED};
        description.SampleDesc = {1, 0};
        description.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
        description.BufferCount = 2;
        description.OutputWindow = window;
        description.Windowed = TRUE;
        description.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;
        if (swapchainOnly)
        {
            auto createSwap = std::bit_cast<PFN_D3D11_CREATE_DEVICE_AND_SWAP_CHAIN>(GetProcAddress(bridge, "D3D11CreateDeviceAndSwapChain"));
            Require(createSwap != nullptr, "Bridge swap-chain creation export unavailable");
            Check(createSwap(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, nullptr, 0, D3D11_SDK_VERSION,
                             &description, scene.swap.Out(), nullptr, &level, nullptr), "Swap-chain-only creation failed");
            Check(scene.swap->GetDevice(__uuidof(ID3D11Device), reinterpret_cast<void **>(scene.device.Out())),
                  "Cannot recover the omitted device from the swap chain");
            scene.device->GetImmediateContext(scene.context.Out());
        }
        else if (omitDevice)
        {
            Check(create(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, nullptr, 0, D3D11_SDK_VERSION,
                         nullptr, &level, scene.context.Out()), "Context-only creation failed");
            scene.context->GetDevice(scene.device.Out());
        }
        else
        {
            Check(create(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, nullptr, 0, D3D11_SDK_VERSION,
                         scene.device.Out(), &level, nullptr), "Device-only creation failed");
            scene.device->GetImmediateContext(scene.context.Out());
        }
        Require(scene.device.value && scene.context.value, "Omitted output recovery failed");
        Com<IDXGIDevice> dxgi;
        Com<IDXGIAdapter> adapter;
        Com<IDXGIFactory> factory;
        Check(scene.device->QueryInterface(__uuidof(IDXGIDevice), reinterpret_cast<void **>(dxgi.Out())), "DXGI device unavailable");
        Check(dxgi->GetAdapter(adapter.Out()), "DXGI adapter unavailable");
        Check(adapter->GetParent(__uuidof(IDXGIFactory), reinterpret_cast<void **>(factory.Out())), "DXGI factory unavailable");
        std::cerr << "Phase: create real DXGI swap chain" << std::endl;
        if (!scene.swap.value)
            Check(factory->CreateSwapChain(scene.device.value, &description, scene.swap.Out()), "Real DXGI swap-chain creation failed");
        std::cerr << "Phase: create real shaders and GPU resources" << std::endl;
        Com<ID3D11Texture2D> backBuffer;
        Check(scene.swap->GetBuffer(0, __uuidof(ID3D11Texture2D), reinterpret_cast<void **>(backBuffer.Out())), "Swap-chain backbuffer unavailable");
        Check(scene.device->CreateRenderTargetView(backBuffer.value, nullptr, scene.target.Out()), "Render target creation failed");
        Check(scene.device->CreateVertexShader(vertex->GetBufferPointer(), vertex->GetBufferSize(), nullptr, scene.vertexShader.Out()), "VS creation failed");
        Check(scene.device->CreatePixelShader(pixel->GetBufferPointer(), pixel->GetBufferSize(), nullptr, scene.pixelShader.Out()), "PS creation failed");
        D3D11_INPUT_ELEMENT_DESC element{"POSITION", 0, DXGI_FORMAT_R32G32_FLOAT, 0, 0, D3D11_INPUT_PER_VERTEX_DATA, 0};
        Check(scene.device->CreateInputLayout(&element, 1, vertex->GetBufferPointer(), vertex->GetBufferSize(), scene.layout.Out()), "Input layout creation failed");
        float vertices[]{-0.5f, -0.5f, 0, 0.5f, 0.5f, -0.5f};
        uint16_t indices[]{0, 1, 2};
        auto buffer = [&](const void *bytes, UINT size, UINT bind, UINT stride, ID3D11Buffer **target)
        {
            D3D11_BUFFER_DESC desc{};
            desc.ByteWidth = size;
            desc.Usage = D3D11_USAGE_DEFAULT;
            desc.BindFlags = bind;
            if (stride) { desc.MiscFlags = D3D11_RESOURCE_MISC_BUFFER_STRUCTURED; desc.StructureByteStride = stride; }
            D3D11_SUBRESOURCE_DATA data{bytes, 0, 0};
            Check(scene.device->CreateBuffer(&desc, &data, target), "Harness GPU buffer creation failed");
        };
        auto instanceData = CapturedInstances(), constantData = CapturedConstants();
        UINT instanceStride = CockpitRecordStride;
        if (catalogLayout)
        {
            std::vector<uint8_t> relocated((instanceData.size() / 336 + 1) * 352);
            for (size_t index = 0; index < instanceData.size() / 336; ++index)
                std::copy_n(instanceData.begin() + index * 336, 336, relocated.begin() + (index + 1) * 352 + 16);
            instanceData = std::move(relocated);
            constantData.insert(constantData.begin(), 64, 0);
            instanceStride = 352;
        }
        buffer(vertices, sizeof(vertices), D3D11_BIND_VERTEX_BUFFER, 0, scene.vertices.Out());
        buffer(indices, sizeof(indices), D3D11_BIND_INDEX_BUFFER, 0, scene.indices.Out());
        buffer(instanceData.data(), static_cast<UINT>(instanceData.size()), D3D11_BIND_SHADER_RESOURCE, instanceStride, scene.instances.Out());
        buffer(constantData.data(), static_cast<UINT>(constantData.size()), D3D11_BIND_CONSTANT_BUFFER, 0, scene.constants.Out());
        D3D11_SHADER_RESOURCE_VIEW_DESC view{};
        view.Format = DXGI_FORMAT_UNKNOWN;
        view.ViewDimension = D3D11_SRV_DIMENSION_BUFFER;
        view.Buffer.FirstElement = catalogLayout ? 1 : 0;
        view.Buffer.NumElements = static_cast<UINT>(instanceData.size() / instanceStride) - view.Buffer.FirstElement;
        Check(scene.device->CreateShaderResourceView(scene.instances.value, &view, scene.instancesView.Out()), "Structured view creation failed");
        scene.Bind();
        if (observerDisabled)
        {
            scene.Frame();
            std::cout << "PASS: unhooked baseline rendering and Present" << std::endl;
            return 0;
        }
        Probe probe;
        std::cerr << "Phase: render until live camera response" << std::endl;
        uint64_t first = ReceiveFrame(scene, probe);
        std::cout << "PASS: actual DLL, device-only creation, factory swap-chain hook and coherent live GPU/UDP frame" << std::endl;
        std::cerr << "Phase: release external context reference" << std::endl;
        ULONG remaining = 0;
        if (concurrentReacquire)
        {
            ID3D11DeviceContext *old = std::exchange(scene.context.value, nullptr);
            delayRelease = true;
            std::thread release([&] { remaining = old->Release(); });
            bool reached = WaitForSingleObject(releaseReached, 5000) == WAIT_OBJECT_0;
            if (reached)
                scene.device->GetImmediateContext(scene.context.Out());
            SetEvent(releaseContinue);
            release.join();
            Require(reached && remaining == 0 && scene.context.value, "Concurrent Release0 barrier was not reached");
        }
        else
        {
            remaining = scene.context.value->Release();
            scene.context.value = nullptr;
            Require(remaining == 0, "The runtime did not expose the external-refcount-zero context lifetime edge");
            scene.device->GetImmediateContext(scene.context.Out());
            Require(scene.context.value != nullptr, "Context reacquisition failed");
        }
        scene.Bind();
        probe.nonce.fill(0x76);
        probe.lastSent = 0;
        Require(ReceiveFrame(scene, probe) > first, "Reacquired context did not resume with a newer live frame");
        if (concurrentReacquire)
            std::cout << "PASS: concurrent Release0 cannot erase reacquired observation" << std::endl;
        if (omitDevice || swapchainOnly)
            std::cout << "PASS: omitted device output retains live camera observation" << std::endl;
        std::cout << "PASS: external Release0 followed by GetImmediateContext resumes live capture" << std::endl;
        uint64_t stopped = GetTickCount64();
        while (GetTickCount64() - stopped < ClientLeaseMilliseconds + 500)
        {
            scene.Frame();
            (void)probe.Receive(false);
            Sleep(10);
        }
        stopped = GetTickCount64();
        while (GetTickCount64() - stopped < 400)
        {
            scene.Frame();
            Require(probe.Receive(false) == 0, "Bridge kept emitting after the opt-in lease expired");
            Sleep(10);
        }
        std::cout << "PASS: stopping app heartbeats stops camera packets after lease expiry" << std::endl;
        std::cerr << "Phase: clear context state" << std::endl;
        scene.context->ClearState();
        std::cerr << "Phase: release context" << std::endl;
        scene.context.Reset();
        std::cerr << "Phase: release shader-resource view and buffers" << std::endl;
        scene.instancesView.Reset();
        scene.instances.Reset();
        scene.constants.Reset();
        scene.indices.Reset();
        scene.vertices.Reset();
        scene.layout.Reset();
        std::cerr << "Phase: release shaders and render target" << std::endl;
        scene.pixelShader.Reset();
        scene.vertexShader.Reset();
        scene.target.Reset();
        backBuffer.Reset();
        std::cerr << "Phase: release swap chain and DXGI objects" << std::endl;
        scene.swap.Reset();
        factory.Reset();
        adapter.Reset();
        dxgi.Reset();
        std::cerr << "Phase: release device" << std::endl;
        scene.device.Reset();
        std::cerr << "Phase: native graphics resources released" << std::endl;
        DestroyWindow(window);
        UnregisterClassW(windowClass.lpszClassName, windowClass.hInstance);
        std::cout << "PASS: clean native graphics teardown" << std::endl;
        return 0;
    }
    catch (const std::exception &exception)
    {
        std::cerr << "FAIL: " << exception.what() << '\n';
        return 1;
    }
}
