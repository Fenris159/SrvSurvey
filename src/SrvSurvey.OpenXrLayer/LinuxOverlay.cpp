// SPDX-License-Identifier: GPL-3.0-or-later
// Native Linux OpenXR overlay runtime (XR_EXTX_overlay + headless Vulkan) for WiVRn and Monado.
#include "LinuxOverlay.h"
#include <algorithm>
#include <array>
#include <cctype>
#include <cmath>
#include <dlfcn.h>
#include <sstream>
#include <type_traits>

static_assert(std::is_trivially_copyable_v<srvsurvey::PointerEvent>);

namespace srvsurvey::linux_openxr
{

namespace
{

constexpr uint32_t OverlayLayerPlacement = 1;
constexpr size_t MaxQueuedPointerEvents = 256;

const std::array<uint8_t, 32 * 32 * 4> &GetCursorPixels()
{
    static const std::array<uint8_t, 32 * 32 * 4> pixels = []() {
        std::array<uint8_t, 32 * 32 * 4> data{};
        for (int y = 0; y < 32; ++y)
        {
            for (int x = 0; x < 32; ++x)
            {
                float dx = static_cast<float>(x) - 15.5f;
                float dy = static_cast<float>(y) - 15.5f;
                float radius = std::sqrt(dx * dx + dy * dy);
                size_t index = static_cast<size_t>(y * 32 + x) * 4;
                bool edge = radius > 9.0f;
                data[index] = edge ? 255 : 32;
                data[index + 1] = edge ? 255 : 200;
                data[index + 2] = 255;
                data[index + 3] = static_cast<uint8_t>(std::clamp(13.5f - radius, 0.0f, 1.0f) * 255.0f);
            }
        }
        return data;
    }();
    return pixels;
}

std::string ToLowerAscii(std::string value)
{
    std::transform(value.begin(), value.end(), value.begin(),
                   [](unsigned char ch) { return static_cast<char>(std::tolower(ch)); });
    return value;
}

template <class T> bool BindXr(PFN_xrGetInstanceProcAddr getProc, XrInstance instance, const char *name, T &target)
{
    PFN_xrVoidFunction fn = nullptr;
    if (!getProc || XR_FAILED(getProc(instance, name, &fn)) || !fn)
        return false;
    target = reinterpret_cast<T>(fn);
    return true;
}

template <class T> bool BindVkInstance(PFN_vkGetInstanceProcAddr getProc, VkInstance instance, const char *name, T &target)
{
    if (!getProc)
        return false;
    PFN_vkVoidFunction fn = getProc(instance, name);
    if (!fn)
        return false;
    target = reinterpret_cast<T>(fn);
    return true;
}

template <class T> bool BindVkDevice(PFN_vkGetDeviceProcAddr getProc, VkDevice device, const char *name, T &target)
{
    if (!getProc)
        return false;
    PFN_vkVoidFunction fn = getProc(device, name);
    if (!fn)
        return false;
    target = reinterpret_cast<T>(fn);
    return true;
}

std::vector<std::string> SplitWhitespace(const std::string &text)
{
    std::vector<std::string> tokens;
    std::istringstream stream(text);
    std::string token;
    while (stream >> token)
        tokens.push_back(token);
    return tokens;
}

XrQuaternionf Multiply(XrQuaternionf a, XrQuaternionf b)
{
    return {a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y, a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w, a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z};
}

float ExtractYaw(const XrQuaternionf &q)
{
    return std::atan2(2 * (q.w * q.y + q.x * q.z), 1 - 2 * (q.x * q.x + q.y * q.y));
}

XrPosef HeadingAnchor(XrPosef head)
{
    float yaw = ExtractYaw(head.orientation);
    head.orientation = {0, std::sin(yaw / 2), 0, std::cos(yaw / 2)};
    return head;
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

bool ValidateOverlayPacketStructure(std::span<const uint8_t> bytes)
{
    if (bytes.size() < sizeof(Header) || bytes.size() > Capacity)
        return false;
    Header header{};
    std::memcpy(&header, bytes.data(), sizeof(header));
    if (header.magic != Magic || header.version != Version || (header.sequence & 1) || header.length != bytes.size() ||
        header.count > MaximumOverlays)
        return false;
    uint64_t pixelStart = sizeof(Header) + header.count * sizeof(Descriptor);
    if (pixelStart > bytes.size())
        return false;
    for (uint32_t i = 0; i < header.count; ++i)
    {
        Descriptor d{};
        std::memcpy(&d, bytes.data() + sizeof(Header) + i * sizeof(Descriptor), sizeof(d));
        if (!d.width || !d.height || d.width > 4096 || d.height > 4096 ||
            uint64_t(d.width) * d.height * 4 != d.length || d.offset < pixelStart ||
            uint64_t(d.offset) + d.length > bytes.size() || !std::isfinite(d.widthMeters) ||
            !std::isfinite(d.heightMeters) || d.widthMeters <= 0 || d.heightMeters <= 0 || !std::isfinite(d.alpha) ||
            d.alpha < 0 || d.alpha > 1)
            return false;
        for (float value : d.position)
            if (!std::isfinite(value))
                return false;
        float norm = 0;
        for (float value : d.orientation)
        {
            if (!std::isfinite(value))
                return false;
            norm += value * value;
        }
        if (std::abs(norm - 1) > 0.01f)
            return false;
        for (uint32_t j = 0; j < i; ++j)
        {
            Descriptor previous{};
            std::memcpy(&previous, bytes.data() + sizeof(Header) + j * sizeof(Descriptor), sizeof(previous));
            if (previous.id == d.id)
                return false;
        }
    }
    return true;
}

uint8_t SrgbByteToLinearByte(uint8_t value)
{
    static const std::array<uint8_t, 256> lut = []() {
        std::array<uint8_t, 256> table{};
        for (int i = 0; i < 256; ++i)
        {
            double c = static_cast<double>(i) / 255.0;
            double linear = (c <= 0.04045) ? (c / 12.92) : std::pow((c + 0.055) / 1.055, 2.4);
            table[static_cast<size_t>(i)] = static_cast<uint8_t>(std::lround(std::clamp(linear, 0.0, 1.0) * 255.0));
        }
        return table;
    }();
    return lut[value];
}

uint32_t XrVersionToVulkanApiVersion(XrVersion version)
{
    uint32_t major = static_cast<uint32_t>(std::min<uint64_t>(XR_VERSION_MAJOR(version), 127));
    uint32_t minor = static_cast<uint32_t>(std::min<uint64_t>(XR_VERSION_MINOR(version), 1023));
    return VK_MAKE_API_VERSION(0, major, minor, 0);
}

bool SelectVulkanApiVersion(PFN_vkGetInstanceProcAddr vkGetInstanceProcAddr, XrVersion minRequired,
                            XrVersion maxSupported, uint32_t &outApiVersion, std::string &outError)
{
    uint32_t minVk = minRequired != 0 ? XrVersionToVulkanApiVersion(minRequired) : VK_API_VERSION_1_0;
    uint32_t maxVk = maxSupported != 0 ? XrVersionToVulkanApiVersion(maxSupported) : 0;
    if (maxSupported != 0 && minVk > maxVk)
    {
        outError = "The OpenXR runtime reported an invalid Vulkan API version range.";
        return false;
    }

    uint32_t loaderVersion = VK_API_VERSION_1_0;
    PFN_vkEnumerateInstanceVersion enumerateInstanceVersion = nullptr;
    if (BindVkInstance(vkGetInstanceProcAddr, VK_NULL_HANDLE, "vkEnumerateInstanceVersion", enumerateInstanceVersion))
    {
        uint32_t queriedVersion = 0;
        if (enumerateInstanceVersion(&queriedVersion) == VK_SUCCESS && queriedVersion != 0)
        {
            loaderVersion =
                VK_MAKE_API_VERSION(0, VK_API_VERSION_MAJOR(queriedVersion), VK_API_VERSION_MINOR(queriedVersion), 0);
        }
    }

    if (minVk > loaderVersion)
    {
        outError = "The installed Vulkan loader is older than the OpenXR runtime's minimum required Vulkan version.";
        return false;
    }

    uint32_t effectiveMax = maxSupported != 0 ? std::min(maxVk, loaderVersion) : loaderVersion;
    if (minVk > effectiveMax)
    {
        outError = "No compatible Vulkan API version is supported by both the OpenXR runtime and Vulkan loader.";
        return false;
    }

    outApiVersion = std::clamp(static_cast<uint32_t>(VK_API_VERSION_1_1), minVk, effectiveMax);
    return true;
}

bool MatchesPixels(const SwapchainTexture &texture, const Descriptor &descriptor, const uint8_t *source,
                   bool swapRedBlue, bool convertSrgbToLinear)
{
    if (texture.pixels.size() != descriptor.length)
        return false;
    if (!swapRedBlue && !convertSrgbToLinear && descriptor.alpha == 1.0f)
        return std::memcmp(texture.pixels.data(), source, descriptor.length) == 0;
    for (size_t i = 0; i < texture.pixels.size(); i += 4)
    {
        uint8_t r = swapRedBlue ? source[i + 2] : source[i];
        uint8_t g = source[i + 1];
        uint8_t b = swapRedBlue ? source[i] : source[i + 2];
        if (convertSrgbToLinear)
        {
            r = SrgbByteToLinearByte(r);
            g = SrgbByteToLinearByte(g);
            b = SrgbByteToLinearByte(b);
        }
        auto a = static_cast<uint8_t>(static_cast<float>(source[i + 3]) * descriptor.alpha);
        if (texture.pixels[i] != r || texture.pixels[i + 1] != g || texture.pixels[i + 2] != b ||
            texture.pixels[i + 3] != a)
            return false;
    }
    return true;
}

void CopyMessage(std::string_view source, char *destination, int32_t capacity) noexcept
{
    if (!destination || capacity <= 0)
        return;
    size_t copyLength = std::min(source.size(), static_cast<size_t>(capacity - 1));
    if (copyLength > 0)
        std::memcpy(destination, source.data(), copyLength);
    destination[copyLength] = '\0';
}

void CopyExceptionMessage(std::string_view prefix, const char *detail, char *destination, int32_t capacity) noexcept
{
    if (!destination || capacity <= 0)
        return;
    size_t remaining = static_cast<size_t>(capacity - 1);
    size_t prefixLen = std::min(prefix.size(), remaining);
    if (prefixLen > 0)
        std::memcpy(destination, prefix.data(), prefixLen);
    remaining -= prefixLen;
    size_t detailLen = 0;
    if (detail && *detail != '\0' && remaining > 0)
    {
        detailLen = std::min(std::strlen(detail), remaining);
        std::memcpy(destination + prefixLen, detail, detailLen);
    }
    destination[prefixLen + detailLen] = '\0';
}

struct DynamicLoaders
{
    void *openxrHandle = nullptr;
    void *vulkanHandle = nullptr;
    OpenXrLoader loader{};
    OverlayStatus failureStatus = OverlayStatus::Ok;
    std::string failureMessage;
};

DynamicLoaders &GetSystemLoaders()
{
    static DynamicLoaders instance = []() {
        DynamicLoaders state{};
        state.openxrHandle = dlopen("libopenxr_loader.so.1", RTLD_NOW | RTLD_LOCAL);
        if (!state.openxrHandle)
            state.openxrHandle = dlopen("libopenxr_loader.so", RTLD_NOW | RTLD_LOCAL);
        if (!state.openxrHandle)
        {
            state.failureStatus = OverlayStatus::LoaderMissing;
            state.failureMessage =
                "The OpenXR loader (libopenxr_loader.so.1) is not installed. Install your distribution's openxr package.";
            return state;
        }
        state.loader.getInstanceProcAddr =
            reinterpret_cast<PFN_xrGetInstanceProcAddr>(dlsym(state.openxrHandle, "xrGetInstanceProcAddr"));
        if (!state.loader.getInstanceProcAddr)
        {
            state.failureStatus = OverlayStatus::LoaderMissing;
            state.failureMessage = "libopenxr_loader.so.1 did not export xrGetInstanceProcAddr.";
            return state;
        }
        state.vulkanHandle = dlopen("libvulkan.so.1", RTLD_NOW | RTLD_LOCAL);
        if (!state.vulkanHandle)
            state.vulkanHandle = dlopen("libvulkan.so", RTLD_NOW | RTLD_LOCAL);
        if (!state.vulkanHandle)
        {
            state.failureStatus = OverlayStatus::VulkanMissing;
            state.failureMessage =
                "The Vulkan loader (libvulkan.so.1) is not installed. Install your distribution's Vulkan loader package.";
            return state;
        }
        state.loader.vkGetInstanceProcAddr =
            reinterpret_cast<PFN_vkGetInstanceProcAddr>(dlsym(state.vulkanHandle, "vkGetInstanceProcAddr"));
        if (!state.loader.vkGetInstanceProcAddr)
        {
            state.failureStatus = OverlayStatus::VulkanMissing;
            state.failureMessage = "libvulkan.so.1 did not export vkGetInstanceProcAddr.";
            return state;
        }
        return state;
    }();
    return instance;
}

struct ExtensionSupport
{
    bool overlay = false;
    bool vulkanEnable2 = false;
    bool vulkanEnable1 = false;
};

ProbeResult InspectExtensions(PFN_xrGetInstanceProcAddr getProc, ExtensionSupport &support)
{
    PFN_xrEnumerateInstanceExtensionProperties enumerate = nullptr;
    if (!BindXr(getProc, XR_NULL_HANDLE, "xrEnumerateInstanceExtensionProperties", enumerate))
    {
        return {OverlayStatus::LoaderMissing, 0, {}, "OpenXR loader does not expose xrEnumerateInstanceExtensionProperties."};
    }

    uint32_t count = 0;
    XrResult result = enumerate(nullptr, 0, &count, nullptr);
    if (XR_FAILED(result))
    {
        return {OverlayStatus::Error, static_cast<int32_t>(result), {},
                "Could not query OpenXR extensions (OpenXR error " + std::to_string(result) + ")."};
    }

    std::vector<XrExtensionProperties> extensions(count, {XR_TYPE_EXTENSION_PROPERTIES});
    if (count > 0)
    {
        result = enumerate(nullptr, count, &count, extensions.data());
        if (XR_FAILED(result))
        {
            return {OverlayStatus::Error, static_cast<int32_t>(result), {},
                    "Could not read OpenXR extensions (OpenXR error " + std::to_string(result) + ")."};
        }
    }

    for (const XrExtensionProperties &ext : extensions)
    {
        if (std::strcmp(ext.extensionName, XR_EXTX_OVERLAY_EXTENSION_NAME) == 0)
            support.overlay = true;
        else if (std::strcmp(ext.extensionName, XR_KHR_VULKAN_ENABLE2_EXTENSION_NAME) == 0)
            support.vulkanEnable2 = true;
        else if (std::strcmp(ext.extensionName, XR_KHR_VULKAN_ENABLE_EXTENSION_NAME) == 0)
            support.vulkanEnable1 = true;
    }

    if (!support.overlay)
    {
        return {OverlayStatus::OverlayExtensionUnsupported, 0, {},
                "The active OpenXR runtime does not support XR_EXTX_overlay. Start WiVRn or Monado and make it the active OpenXR runtime."};
    }

    if (!support.vulkanEnable2 && !support.vulkanEnable1)
    {
        return {OverlayStatus::GraphicsExtensionUnsupported, 0, {},
                "The active OpenXR runtime does not advertise Vulkan graphics support (XR_KHR_vulkan_enable2 / XR_KHR_vulkan_enable)."};
    }

    return {OverlayStatus::Ok, 0, {}, {}};
}

} // namespace

bool MatchesRuntimeFilter(const std::string &runtimeName, const std::string &expectedRuntime)
{
    std::string lowerName = ToLowerAscii(runtimeName);
    std::string lowerExpected = ToLowerAscii(expectedRuntime);
    if (lowerName.find("steamvr") != std::string::npos || lowerName.find("valve") != std::string::npos)
        return false;
    if (lowerExpected.empty() || lowerExpected == "any")
        return true;
    if (lowerExpected == "wivrn")
    {
        // WiVRn builds on Monado's XRT state tracker and may report either "WiVRn" or "Monado(XRT)".
        return lowerName.find("wivrn") != std::string::npos || lowerName.find("monado") != std::string::npos;
    }
    if (lowerExpected == "monado")
        return lowerName.find("monado") != std::string::npos;
    return lowerName.find(lowerExpected) != std::string::npos;
}

bool IsSrgbSwapchainFormat(int64_t format)
{
    return format == VK_FORMAT_R8G8B8A8_SRGB || format == VK_FORMAT_B8G8R8A8_SRGB;
}

int64_t SelectSwapchainFormat(const std::vector<int64_t> &formats, bool &swapRedBlue)
{
    constexpr std::array<std::pair<int64_t, bool>, 4> preferred = {{
        {VK_FORMAT_R8G8B8A8_SRGB, false},
        {VK_FORMAT_B8G8R8A8_SRGB, true},
        {VK_FORMAT_R8G8B8A8_UNORM, false},
        {VK_FORMAT_B8G8R8A8_UNORM, true},
    }};
    for (const auto &[format, swizzle] : preferred)
    {
        if (std::find(formats.begin(), formats.end(), format) != formats.end())
        {
            swapRedBlue = swizzle;
            return format;
        }
    }
    return 0;
}

ProbeResult ProbeOverlayRuntime(const OpenXrLoader &loader, const std::string &expectedRuntime)
{
    if (!loader.getInstanceProcAddr)
    {
        return {OverlayStatus::LoaderMissing, 0, {}, "The OpenXR loader is unavailable."};
    }
    if (!loader.vkGetInstanceProcAddr)
    {
        return {OverlayStatus::VulkanMissing, 0, {}, "The Vulkan loader is unavailable."};
    }

    ExtensionSupport support{};
    ProbeResult extResult = InspectExtensions(loader.getInstanceProcAddr, support);
    if (extResult.status != OverlayStatus::Ok)
        return extResult;

    PFN_xrCreateInstance createInstance = nullptr;
    if (!BindXr(loader.getInstanceProcAddr, XR_NULL_HANDLE, "xrCreateInstance", createInstance))
        return {OverlayStatus::LoaderMissing, 0, {}, "OpenXR loader does not expose xrCreateInstance."};

    std::array<const char *, 2> enabledExtensions = {
        XR_EXTX_OVERLAY_EXTENSION_NAME,
        support.vulkanEnable2 ? XR_KHR_VULKAN_ENABLE2_EXTENSION_NAME : XR_KHR_VULKAN_ENABLE_EXTENSION_NAME,
    };

    XrInstanceCreateInfo createInfo{XR_TYPE_INSTANCE_CREATE_INFO};
    std::snprintf(createInfo.applicationInfo.applicationName, sizeof(createInfo.applicationInfo.applicationName), "%s",
                  "SrvSurvey");
    createInfo.applicationInfo.applicationVersion = 1;
    std::snprintf(createInfo.applicationInfo.engineName, sizeof(createInfo.applicationInfo.engineName), "%s",
                  "SrvSurvey");
    createInfo.applicationInfo.engineVersion = 1;
    createInfo.applicationInfo.apiVersion = XR_MAKE_VERSION(1, 0, 0);
    createInfo.enabledExtensionCount = static_cast<uint32_t>(enabledExtensions.size());
    createInfo.enabledExtensionNames = enabledExtensions.data();

    XrInstance instance = XR_NULL_HANDLE;
    XrResult result = createInstance(&createInfo, &instance);
    if (XR_FAILED(result))
    {
        if (result == XR_ERROR_RUNTIME_UNAVAILABLE || result == XR_ERROR_INSTANCE_LOST)
        {
            return {OverlayStatus::HeadsetUnavailable, static_cast<int32_t>(result), {},
                    "Waiting for the Linux OpenXR runtime service to accept connections."};
        }
        return {OverlayStatus::Error, static_cast<int32_t>(result), {},
                "Could not create an OpenXR overlay instance (OpenXR error " + std::to_string(result) + ")."};
    }

    PFN_xrDestroyInstance destroyInstance = nullptr;
    PFN_xrGetInstanceProperties getProps = nullptr;
    PFN_xrGetSystem getSystem = nullptr;
    BindXr(loader.getInstanceProcAddr, instance, "xrDestroyInstance", destroyInstance);
    BindXr(loader.getInstanceProcAddr, instance, "xrGetInstanceProperties", getProps);
    BindXr(loader.getInstanceProcAddr, instance, "xrGetSystem", getSystem);

    auto cleanup = [&]() {
        if (destroyInstance && instance != XR_NULL_HANDLE)
            destroyInstance(instance);
    };

    std::string runtimeName;
    if (getProps)
    {
        XrInstanceProperties props{XR_TYPE_INSTANCE_PROPERTIES};
        if (XR_SUCCEEDED(getProps(instance, &props)))
            runtimeName = props.runtimeName;
    }

    if (!runtimeName.empty() && !MatchesRuntimeFilter(runtimeName, expectedRuntime))
    {
        cleanup();
        return {OverlayStatus::WrongRuntime, 0, runtimeName,
                "The active OpenXR runtime is '" + runtimeName + "', which does not match the selected Linux OpenXR route."};
    }

    if (!getSystem)
    {
        cleanup();
        return {OverlayStatus::Error, 0, runtimeName, "OpenXR instance does not expose xrGetSystem."};
    }

    XrSystemGetInfo systemInfo{XR_TYPE_SYSTEM_GET_INFO};
    systemInfo.formFactor = XR_FORM_FACTOR_HEAD_MOUNTED_DISPLAY;
    XrSystemId systemId = XR_NULL_SYSTEM_ID;
    result = getSystem(instance, &systemInfo, &systemId);
    cleanup();

    if (result == XR_ERROR_FORM_FACTOR_UNAVAILABLE)
    {
        return {OverlayStatus::HeadsetUnavailable, static_cast<int32_t>(result), runtimeName,
                "The OpenXR runtime is running, but no headset is connected yet."};
    }
    if (XR_FAILED(result))
    {
        return {OverlayStatus::Error, static_cast<int32_t>(result), runtimeName,
                "Could not query the OpenXR headset system (OpenXR error " + std::to_string(result) + ")."};
    }

    return {OverlayStatus::Ok, 0, runtimeName, "Linux OpenXR overlay runtime is ready."};
}

OverlaySession::OverlaySession(OpenXrLoader loader, std::string expectedRuntime, bool startWorkerThread)
    : loader(loader), expectedRuntime(std::move(expectedRuntime)), startWorkerThread(startWorkerThread)
{
}

OverlaySession::~OverlaySession()
{
    Shutdown();
}

ProbeResult OverlaySession::Initialize()
{
    if (!loader.getInstanceProcAddr)
        return {OverlayStatus::LoaderMissing, 0, {}, "The OpenXR loader is unavailable."};
    if (!loader.vkGetInstanceProcAddr)
        return {OverlayStatus::VulkanMissing, 0, {}, "The Vulkan loader is unavailable."};

    ExtensionSupport support{};
    ProbeResult extResult = InspectExtensions(loader.getInstanceProcAddr, support);
    if (extResult.status != OverlayStatus::Ok)
        return extResult;

    if (!BindXr(loader.getInstanceProcAddr, XR_NULL_HANDLE, "xrCreateInstance", xr.createInstance))
        return {OverlayStatus::LoaderMissing, 0, {}, "OpenXR loader does not expose xrCreateInstance."};

    const bool useVulkanEnable2 = support.vulkanEnable2;
    std::array<const char *, 2> enabledExtensions = {
        XR_EXTX_OVERLAY_EXTENSION_NAME,
        useVulkanEnable2 ? XR_KHR_VULKAN_ENABLE2_EXTENSION_NAME : XR_KHR_VULKAN_ENABLE_EXTENSION_NAME,
    };

    XrInstanceCreateInfo createInfo{XR_TYPE_INSTANCE_CREATE_INFO};
    std::snprintf(createInfo.applicationInfo.applicationName, sizeof(createInfo.applicationInfo.applicationName), "%s",
                  "SrvSurvey");
    createInfo.applicationInfo.applicationVersion = 1;
    std::snprintf(createInfo.applicationInfo.engineName, sizeof(createInfo.applicationInfo.engineName), "%s",
                  "SrvSurvey");
    createInfo.applicationInfo.engineVersion = 1;
    createInfo.applicationInfo.apiVersion = XR_MAKE_VERSION(1, 0, 0);
    createInfo.enabledExtensionCount = static_cast<uint32_t>(enabledExtensions.size());
    createInfo.enabledExtensionNames = enabledExtensions.data();

    XrResult result = xr.createInstance(&createInfo, &instance);
    if (XR_FAILED(result))
    {
        return {OverlayStatus::Error, static_cast<int32_t>(result), {},
                "Could not create the OpenXR overlay instance (OpenXR error " + std::to_string(result) + ")."};
    }

    if (!LoadInstanceFunctions())
    {
        CleanupResources();
        return {OverlayStatus::Error, 0, {}, "Required OpenXR overlay entry points are missing."};
    }

    XrInstanceProperties props{XR_TYPE_INSTANCE_PROPERTIES};
    std::string runtimeName;
    if (XR_SUCCEEDED(xr.getInstanceProperties(instance, &props)))
        runtimeName = props.runtimeName;

    if (!runtimeName.empty() && !MatchesRuntimeFilter(runtimeName, expectedRuntime))
    {
        CleanupResources();
        return {OverlayStatus::WrongRuntime, 0, runtimeName,
                "The active OpenXR runtime is '" + runtimeName + "', which does not match the selected Linux OpenXR route."};
    }

    XrSystemGetInfo systemInfo{XR_TYPE_SYSTEM_GET_INFO};
    systemInfo.formFactor = XR_FORM_FACTOR_HEAD_MOUNTED_DISPLAY;
    result = xr.getSystem(instance, &systemInfo, &systemId);
    if (result == XR_ERROR_FORM_FACTOR_UNAVAILABLE)
    {
        CleanupResources();
        return {OverlayStatus::HeadsetUnavailable, static_cast<int32_t>(result), runtimeName,
                "The OpenXR runtime is running, but no headset is connected yet."};
    }
    if (XR_FAILED(result))
    {
        CleanupResources();
        return {OverlayStatus::Error, static_cast<int32_t>(result), runtimeName,
                "Could not query the OpenXR headset system (OpenXR error " + std::to_string(result) + ")."};
    }

    XrSystemProperties systemProps{XR_TYPE_SYSTEM_PROPERTIES};
    if (XR_SUCCEEDED(xr.getSystemProperties(instance, systemId, &systemProps)) &&
        systemProps.graphicsProperties.maxLayerCount > 0)
    {
        maxLayers = std::min(systemProps.graphicsProperties.maxLayerCount, MaximumOverlays);
    }

    if (xr.enumerateBlendModes)
    {
        uint32_t blendCount = 0;
        if (XR_SUCCEEDED(
                xr.enumerateBlendModes(instance, systemId, XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO, 0, &blendCount, nullptr)) &&
            blendCount > 0)
        {
            std::vector<XrEnvironmentBlendMode> modes(blendCount);
            if (XR_SUCCEEDED(xr.enumerateBlendModes(instance, systemId, XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO,
                                                    blendCount, &blendCount, modes.data())))
            {
                if (std::find(modes.begin(), modes.end(), XR_ENVIRONMENT_BLEND_MODE_OPAQUE) != modes.end())
                    blendMode = XR_ENVIRONMENT_BLEND_MODE_OPAQUE;
                else
                    blendMode = modes[0];
            }
        }
    }

    ProbeResult vulkanResult = CreateVulkanContext(useVulkanEnable2);
    if (vulkanResult.status != OverlayStatus::Ok)
    {
        CleanupResources();
        return vulkanResult;
    }

    XrGraphicsBindingVulkanKHR graphicsBinding{XR_TYPE_GRAPHICS_BINDING_VULKAN_KHR};
    graphicsBinding.instance = vkInstance;
    graphicsBinding.physicalDevice = vkPhysicalDevice;
    graphicsBinding.device = vkDevice;
    graphicsBinding.queueFamilyIndex = queueFamilyIndex;
    graphicsBinding.queueIndex = 0;

    XrSessionCreateInfoOverlayEXTX overlayInfo{XR_TYPE_SESSION_CREATE_INFO_OVERLAY_EXTX};
    overlayInfo.next = &graphicsBinding;
    overlayInfo.createFlags = 0;
    overlayInfo.sessionLayersPlacement = OverlayLayerPlacement;

    XrSessionCreateInfo sessionInfo{XR_TYPE_SESSION_CREATE_INFO};
    sessionInfo.next = &overlayInfo;
    sessionInfo.systemId = systemId;

    result = xr.createSession(instance, &sessionInfo, &session);
    if (XR_FAILED(result))
    {
        CleanupResources();
        return {OverlayStatus::Error, static_cast<int32_t>(result), runtimeName,
                "Could not create the XR_EXTX_overlay session (OpenXR error " + std::to_string(result) + ")."};
    }

    XrReferenceSpaceCreateInfo spaceInfo{XR_TYPE_REFERENCE_SPACE_CREATE_INFO};
    spaceInfo.poseInReferenceSpace.orientation.w = 1;
    spaceInfo.referenceSpaceType = XR_REFERENCE_SPACE_TYPE_LOCAL;
    result = xr.createReferenceSpace(session, &spaceInfo, &localSpace);
    if (XR_FAILED(result))
    {
        CleanupResources();
        return {OverlayStatus::Error, static_cast<int32_t>(result), runtimeName,
                "Could not create the OpenXR LOCAL reference space (OpenXR error " + std::to_string(result) + ")."};
    }

    spaceInfo.referenceSpaceType = XR_REFERENCE_SPACE_TYPE_VIEW;
    result = xr.createReferenceSpace(session, &spaceInfo, &viewSpace);
    if (XR_FAILED(result))
    {
        CleanupResources();
        return {OverlayStatus::Error, static_cast<int32_t>(result), runtimeName,
                "Could not create the OpenXR VIEW reference space (OpenXR error " + std::to_string(result) + ")."};
    }

    uint32_t formatCount = 0;
    result = xr.enumerateSwapchainFormats(session, 0, &formatCount, nullptr);
    if (XR_FAILED(result) || formatCount == 0)
    {
        CleanupResources();
        return {OverlayStatus::Error, static_cast<int32_t>(result), runtimeName,
                "Could not enumerate OpenXR Vulkan swapchain formats."};
    }

    std::vector<int64_t> formats(formatCount);
    result = xr.enumerateSwapchainFormats(session, formatCount, &formatCount, formats.data());
    if (XR_FAILED(result))
    {
        CleanupResources();
        return {OverlayStatus::Error, static_cast<int32_t>(result), runtimeName,
                "Could not read OpenXR Vulkan swapchain formats."};
    }

    swapchainFormat = SelectSwapchainFormat(formats, swapRedBlue);
    if (swapchainFormat == 0)
    {
        CleanupResources();
        return {OverlayStatus::Error, static_cast<int32_t>(XR_ERROR_SWAPCHAIN_FORMAT_UNSUPPORTED), runtimeName,
                "The OpenXR runtime does not support an RGBA8 or BGRA8 Vulkan swapchain format."};
    }
    swapchainIsSrgb = IsSrgbSwapchainFormat(swapchainFormat);

    InitializeInteraction();

    SetStatus(OverlayStatus::Ok, 0, 0, "Linux OpenXR overlay session is active.");
    if (startWorkerThread)
    {
        stopRequested.store(false);
        try
        {
            worker = std::thread(&OverlaySession::WorkerLoop, this);
        }
        catch (const std::exception &ex)
        {
            CleanupResources();
            return {OverlayStatus::Error, 0, runtimeName,
                    std::string("Could not start the Linux OpenXR overlay worker thread: ") + ex.what()};
        }
        catch (...)
        {
            CleanupResources();
            return {OverlayStatus::Error, 0, runtimeName,
                    "Could not start the Linux OpenXR overlay worker thread."};
        }
    }

    return {OverlayStatus::Ok, 0, runtimeName, "Linux OpenXR overlay session is active."};
}

bool OverlaySession::LoadInstanceFunctions()
{
    bool ok = true;
    ok &= BindXr(loader.getInstanceProcAddr, instance, "xrDestroyInstance", xr.destroyInstance);
    ok &= BindXr(loader.getInstanceProcAddr, instance, "xrGetInstanceProperties", xr.getInstanceProperties);
    ok &= BindXr(loader.getInstanceProcAddr, instance, "xrGetSystem", xr.getSystem);
    ok &= BindXr(loader.getInstanceProcAddr, instance, "xrGetSystemProperties", xr.getSystemProperties);
    ok &= BindXr(loader.getInstanceProcAddr, instance, "xrCreateSession", xr.createSession);
    ok &= BindXr(loader.getInstanceProcAddr, instance, "xrDestroySession", xr.destroySession);
    ok &= BindXr(loader.getInstanceProcAddr, instance, "xrBeginSession", xr.beginSession);
    ok &= BindXr(loader.getInstanceProcAddr, instance, "xrEndSession", xr.endSession);
    ok &= BindXr(loader.getInstanceProcAddr, instance, "xrPollEvent", xr.pollEvent);
    ok &= BindXr(loader.getInstanceProcAddr, instance, "xrCreateReferenceSpace", xr.createReferenceSpace);
    ok &= BindXr(loader.getInstanceProcAddr, instance, "xrDestroySpace", xr.destroySpace);
    ok &= BindXr(loader.getInstanceProcAddr, instance, "xrLocateSpace", xr.locateSpace);
    ok &= BindXr(loader.getInstanceProcAddr, instance, "xrEnumerateSwapchainFormats", xr.enumerateSwapchainFormats);
    ok &= BindXr(loader.getInstanceProcAddr, instance, "xrCreateSwapchain", xr.createSwapchain);
    ok &= BindXr(loader.getInstanceProcAddr, instance, "xrDestroySwapchain", xr.destroySwapchain);
    ok &= BindXr(loader.getInstanceProcAddr, instance, "xrEnumerateSwapchainImages", xr.enumerateSwapchainImages);
    ok &= BindXr(loader.getInstanceProcAddr, instance, "xrAcquireSwapchainImage", xr.acquireSwapchainImage);
    ok &= BindXr(loader.getInstanceProcAddr, instance, "xrWaitSwapchainImage", xr.waitSwapchainImage);
    ok &= BindXr(loader.getInstanceProcAddr, instance, "xrReleaseSwapchainImage", xr.releaseSwapchainImage);
    ok &= BindXr(loader.getInstanceProcAddr, instance, "xrWaitFrame", xr.waitFrame);
    ok &= BindXr(loader.getInstanceProcAddr, instance, "xrBeginFrame", xr.beginFrame);
    ok &= BindXr(loader.getInstanceProcAddr, instance, "xrEndFrame", xr.endFrame);
    BindXr(loader.getInstanceProcAddr, instance, "xrEnumerateEnvironmentBlendModes", xr.enumerateBlendModes);
    BindXr(loader.getInstanceProcAddr, instance, "xrGetVulkanGraphicsRequirements2KHR", xr.getVulkanRequirements2);
    BindXr(loader.getInstanceProcAddr, instance, "xrCreateVulkanInstanceKHR", xr.createVulkanInstance);
    BindXr(loader.getInstanceProcAddr, instance, "xrGetVulkanGraphicsDevice2KHR", xr.getVulkanDevice2);
    BindXr(loader.getInstanceProcAddr, instance, "xrCreateVulkanDeviceKHR", xr.createVulkanDevice);
    BindXr(loader.getInstanceProcAddr, instance, "xrGetVulkanGraphicsRequirementsKHR", xr.getVulkanRequirements1);
    BindXr(loader.getInstanceProcAddr, instance, "xrGetVulkanInstanceExtensionsKHR", xr.getVulkanInstanceExtensions);
    BindXr(loader.getInstanceProcAddr, instance, "xrGetVulkanDeviceExtensionsKHR", xr.getVulkanDeviceExtensions);
    BindXr(loader.getInstanceProcAddr, instance, "xrGetVulkanGraphicsDeviceKHR", xr.getVulkanDevice1);
    BindXr(loader.getInstanceProcAddr, instance, "xrCreateActionSet", xr.createActionSet);
    BindXr(loader.getInstanceProcAddr, instance, "xrDestroyActionSet", xr.destroyActionSet);
    BindXr(loader.getInstanceProcAddr, instance, "xrCreateAction", xr.createAction);
    BindXr(loader.getInstanceProcAddr, instance, "xrStringToPath", xr.stringToPath);
    BindXr(loader.getInstanceProcAddr, instance, "xrSuggestInteractionProfileBindings",
           xr.suggestInteractionProfileBindings);
    BindXr(loader.getInstanceProcAddr, instance, "xrAttachSessionActionSets", xr.attachSessionActionSets);
    BindXr(loader.getInstanceProcAddr, instance, "xrCreateActionSpace", xr.createActionSpace);
    BindXr(loader.getInstanceProcAddr, instance, "xrSyncActions", xr.syncActions);
    BindXr(loader.getInstanceProcAddr, instance, "xrGetActionStateBoolean", xr.getActionStateBoolean);
    BindXr(loader.getInstanceProcAddr, instance, "xrGetActionStateVector2f", xr.getActionStateVector2f);
    BindXr(loader.getInstanceProcAddr, instance, "xrGetActionStatePose", xr.getActionStatePose);
    return ok;
}

void OverlaySession::InitializeInteraction()
{
    auto failInput = [&](XrResult xrResult, std::string message) {
        for (XrSpace &space : aimSpaces)
        {
            if (space != XR_NULL_HANDLE && xr.destroySpace)
                xr.destroySpace(space);
            space = XR_NULL_HANDLE;
        }
        if (actionSet != XR_NULL_HANDLE && xr.destroyActionSet)
            xr.destroyActionSet(actionSet);
        actionSet = XR_NULL_HANDLE;
        aimAction = XR_NULL_HANDLE;
        selectAction = XR_NULL_HANDLE;
        secondaryAction = XR_NULL_HANDLE;
        middleAction = XR_NULL_HANDLE;
        scrollAction = XR_NULL_HANDLE;
        handPaths = {XR_NULL_PATH, XR_NULL_PATH};

        std::lock_guard lock(inputMutex);
        inputReady = false;
        inputError = static_cast<int32_t>(xrResult);
        inputErrorMessage = std::move(message);
    };

    if (!xr.createActionSet || !xr.destroyActionSet || !xr.createAction || !xr.stringToPath ||
        !xr.suggestInteractionProfileBindings || !xr.attachSessionActionSets || !xr.createActionSpace ||
        !xr.syncActions || !xr.getActionStateBoolean || !xr.getActionStateVector2f || !xr.getActionStatePose)
    {
        failInput(XR_ERROR_FUNCTION_UNSUPPORTED,
                  "The active OpenXR runtime does not expose controller action entry points.");
        return;
    }

    XrActionSetCreateInfo setInfo{XR_TYPE_ACTION_SET_CREATE_INFO};
    std::snprintf(setInfo.actionSetName, sizeof(setInfo.actionSetName), "%s", "srvsurvey_overlay_input");
    std::snprintf(setInfo.localizedActionSetName, sizeof(setInfo.localizedActionSetName), "%s",
                  "SrvSurvey Overlay Input");
    setInfo.priority = 100;
    XrResult res = xr.createActionSet(instance, &setInfo, &actionSet);
    if (XR_FAILED(res))
    {
        failInput(res, "Could not create the OpenXR overlay action set (OpenXR error " + std::to_string(res) + ").");
        return;
    }

    res = xr.stringToPath(instance, "/user/hand/left", &handPaths[0]);
    if (XR_SUCCEEDED(res))
        res = xr.stringToPath(instance, "/user/hand/right", &handPaths[1]);
    if (XR_FAILED(res))
    {
        failInput(res, "Could not resolve OpenXR controller hand paths (OpenXR error " + std::to_string(res) + ").");
        return;
    }

    auto createAction = [&](const char *name, const char *label, XrActionType type, XrAction &outAction) -> XrResult {
        XrActionCreateInfo actionInfo{XR_TYPE_ACTION_CREATE_INFO};
        std::snprintf(actionInfo.actionName, sizeof(actionInfo.actionName), "%s", name);
        std::snprintf(actionInfo.localizedActionName, sizeof(actionInfo.localizedActionName), "%s", label);
        actionInfo.actionType = type;
        actionInfo.countSubactionPaths = static_cast<uint32_t>(handPaths.size());
        actionInfo.subactionPaths = handPaths.data();
        return xr.createAction(actionSet, &actionInfo, &outAction);
    };

    if (XR_FAILED(res = createAction("aim", "Pointer aim", XR_ACTION_TYPE_POSE_INPUT, aimAction)) ||
        XR_FAILED(res = createAction("select", "Primary click", XR_ACTION_TYPE_BOOLEAN_INPUT, selectAction)) ||
        XR_FAILED(res = createAction("secondary", "Secondary click", XR_ACTION_TYPE_BOOLEAN_INPUT, secondaryAction)) ||
        XR_FAILED(res = createAction("middle", "Middle click", XR_ACTION_TYPE_BOOLEAN_INPUT, middleAction)) ||
        XR_FAILED(res = createAction("scroll", "Scroll", XR_ACTION_TYPE_VECTOR2F_INPUT, scrollAction)))
    {
        failInput(res, "Could not create OpenXR overlay controller actions (OpenXR error " + std::to_string(res) + ").");
        return;
    }

    struct ActionBindingSpec
    {
        XrAction action;
        const char *suffix;
    };

    auto suggestProfile = [&](const char *profileName, std::initializer_list<ActionBindingSpec> specs) -> XrResult {
        XrPath profilePath = XR_NULL_PATH;
        XrResult pathRes = xr.stringToPath(instance, profileName, &profilePath);
        if (XR_FAILED(pathRes))
            return pathRes;

        std::vector<XrActionSuggestedBinding> bindings;
        bindings.reserve(specs.size() * 2);
        for (const char *handPrefix : {"/user/hand/left", "/user/hand/right"})
        {
            for (const ActionBindingSpec &spec : specs)
            {
                XrPath bindingPath = XR_NULL_PATH;
                std::string fullPath = std::string(handPrefix) + spec.suffix;
                pathRes = xr.stringToPath(instance, fullPath.c_str(), &bindingPath);
                if (XR_FAILED(pathRes))
                    return pathRes;
                bindings.push_back({spec.action, bindingPath});
            }
        }

        XrInteractionProfileSuggestedBinding suggested{XR_TYPE_INTERACTION_PROFILE_SUGGESTED_BINDING};
        suggested.interactionProfile = profilePath;
        suggested.countSuggestedBindings = static_cast<uint32_t>(bindings.size());
        suggested.suggestedBindings = bindings.data();
        return xr.suggestInteractionProfileBindings(instance, &suggested);
    };

    int suggestedProfiles = 0;
    XrResult lastSuggestError = XR_SUCCESS;
    auto trySuggest = [&](const char *profileName, std::initializer_list<ActionBindingSpec> specs) {
        XrResult suggestRes = suggestProfile(profileName, specs);
        if (XR_SUCCEEDED(suggestRes))
            ++suggestedProfiles;
        else
            lastSuggestError = suggestRes;
    };

    trySuggest("/interaction_profiles/oculus/touch_controller",
               {{aimAction, "/input/aim/pose"},
                {selectAction, "/input/trigger/value"},
                {secondaryAction, "/input/squeeze/value"},
                {middleAction, "/input/thumbstick/click"},
                {scrollAction, "/input/thumbstick"}});
    trySuggest("/interaction_profiles/valve/index_controller",
               {{aimAction, "/input/aim/pose"},
                {selectAction, "/input/trigger/value"},
                {secondaryAction, "/input/squeeze/value"},
                {middleAction, "/input/thumbstick/click"},
                {scrollAction, "/input/thumbstick"}});
    trySuggest("/interaction_profiles/microsoft/motion_controller",
               {{aimAction, "/input/aim/pose"},
                {selectAction, "/input/trigger/value"},
                {secondaryAction, "/input/squeeze/click"},
                {middleAction, "/input/thumbstick/click"},
                {scrollAction, "/input/thumbstick"}});
    trySuggest("/interaction_profiles/htc/vive_controller",
               {{aimAction, "/input/aim/pose"},
                {selectAction, "/input/trigger/value"},
                {secondaryAction, "/input/squeeze/click"},
                {middleAction, "/input/trackpad/click"},
                {scrollAction, "/input/trackpad"}});
    trySuggest("/interaction_profiles/khr/simple_controller",
               {{aimAction, "/input/aim/pose"}, {selectAction, "/input/select/click"}});

    if (suggestedProfiles == 0)
    {
        failInput(lastSuggestError,
                  "Could not bind any OpenXR controller profile for overlay interaction (OpenXR error " +
                      std::to_string(lastSuggestError) + ").");
        return;
    }

    XrSessionActionSetsAttachInfo attachInfo{XR_TYPE_SESSION_ACTION_SETS_ATTACH_INFO};
    attachInfo.countActionSets = 1;
    attachInfo.actionSets = &actionSet;
    res = xr.attachSessionActionSets(session, &attachInfo);
    if (XR_FAILED(res))
    {
        failInput(res, "Could not attach the OpenXR overlay action set (OpenXR error " + std::to_string(res) + ").");
        return;
    }

    for (size_t hand = 0; hand < aimSpaces.size(); ++hand)
    {
        XrActionSpaceCreateInfo spaceInfo{XR_TYPE_ACTION_SPACE_CREATE_INFO};
        spaceInfo.action = aimAction;
        spaceInfo.subactionPath = handPaths[hand];
        spaceInfo.poseInActionSpace.orientation.w = 1.0f;
        res = xr.createActionSpace(session, &spaceInfo, &aimSpaces[hand]);
        if (XR_FAILED(res))
        {
            failInput(res,
                      "Could not create an OpenXR controller aim space (OpenXR error " + std::to_string(res) + ").");
            return;
        }
    }

    std::lock_guard lock(inputMutex);
    inputReady = true;
    inputError = 0;
    inputErrorMessage.clear();
}

bool OverlaySession::LoadVulkanInstanceFunctions()
{
    bool ok = true;
    ok &= BindVkInstance(loader.vkGetInstanceProcAddr, vkInstance, "vkDestroyInstance", vk.destroyInstance);
    ok &= BindVkInstance(loader.vkGetInstanceProcAddr, vkInstance, "vkGetPhysicalDeviceQueueFamilyProperties",
                         vk.getQueueFamilyProperties);
    ok &= BindVkInstance(loader.vkGetInstanceProcAddr, vkInstance, "vkGetPhysicalDeviceMemoryProperties",
                         vk.getMemoryProperties);
    ok &= BindVkInstance(loader.vkGetInstanceProcAddr, vkInstance, "vkGetDeviceProcAddr", vk.getDeviceProcAddr);
    BindVkInstance(loader.vkGetInstanceProcAddr, vkInstance, "vkCreateDevice", vk.createDevice);
    return ok;
}

bool OverlaySession::LoadVulkanDeviceFunctions()
{
    bool ok = true;
    ok &= BindVkDevice(vk.getDeviceProcAddr, vkDevice, "vkDestroyDevice", vk.destroyDevice);
    ok &= BindVkDevice(vk.getDeviceProcAddr, vkDevice, "vkDeviceWaitIdle", vk.deviceWaitIdle);
    ok &= BindVkDevice(vk.getDeviceProcAddr, vkDevice, "vkGetDeviceQueue", vk.getDeviceQueue);
    ok &= BindVkDevice(vk.getDeviceProcAddr, vkDevice, "vkCreateCommandPool", vk.createCommandPool);
    ok &= BindVkDevice(vk.getDeviceProcAddr, vkDevice, "vkDestroyCommandPool", vk.destroyCommandPool);
    ok &= BindVkDevice(vk.getDeviceProcAddr, vkDevice, "vkAllocateCommandBuffers", vk.allocateCommandBuffers);
    ok &= BindVkDevice(vk.getDeviceProcAddr, vkDevice, "vkResetCommandBuffer", vk.resetCommandBuffer);
    ok &= BindVkDevice(vk.getDeviceProcAddr, vkDevice, "vkBeginCommandBuffer", vk.beginCommandBuffer);
    ok &= BindVkDevice(vk.getDeviceProcAddr, vkDevice, "vkEndCommandBuffer", vk.endCommandBuffer);
    ok &= BindVkDevice(vk.getDeviceProcAddr, vkDevice, "vkCmdPipelineBarrier", vk.cmdPipelineBarrier);
    ok &= BindVkDevice(vk.getDeviceProcAddr, vkDevice, "vkCmdCopyBufferToImage", vk.cmdCopyBufferToImage);
    ok &= BindVkDevice(vk.getDeviceProcAddr, vkDevice, "vkCreateFence", vk.createFence);
    ok &= BindVkDevice(vk.getDeviceProcAddr, vkDevice, "vkDestroyFence", vk.destroyFence);
    ok &= BindVkDevice(vk.getDeviceProcAddr, vkDevice, "vkResetFences", vk.resetFences);
    ok &= BindVkDevice(vk.getDeviceProcAddr, vkDevice, "vkWaitForFences", vk.waitForFences);
    ok &= BindVkDevice(vk.getDeviceProcAddr, vkDevice, "vkQueueSubmit", vk.queueSubmit);
    ok &= BindVkDevice(vk.getDeviceProcAddr, vkDevice, "vkCreateBuffer", vk.createBuffer);
    ok &= BindVkDevice(vk.getDeviceProcAddr, vkDevice, "vkDestroyBuffer", vk.destroyBuffer);
    ok &= BindVkDevice(vk.getDeviceProcAddr, vkDevice, "vkGetBufferMemoryRequirements", vk.getBufferMemoryRequirements);
    ok &= BindVkDevice(vk.getDeviceProcAddr, vkDevice, "vkAllocateMemory", vk.allocateMemory);
    ok &= BindVkDevice(vk.getDeviceProcAddr, vkDevice, "vkFreeMemory", vk.freeMemory);
    ok &= BindVkDevice(vk.getDeviceProcAddr, vkDevice, "vkBindBufferMemory", vk.bindBufferMemory);
    ok &= BindVkDevice(vk.getDeviceProcAddr, vkDevice, "vkMapMemory", vk.mapMemory);
    ok &= BindVkDevice(vk.getDeviceProcAddr, vkDevice, "vkUnmapMemory", vk.unmapMemory);
    BindVkDevice(vk.getDeviceProcAddr, vkDevice, "vkFlushMappedMemoryRanges", vk.flushMappedMemoryRanges);
    return ok;
}

ProbeResult OverlaySession::CreateVulkanContext(bool useVulkanEnable2)
{
    VkApplicationInfo appInfo{};
    appInfo.sType = VK_STRUCTURE_TYPE_APPLICATION_INFO;
    appInfo.pApplicationName = "SrvSurvey";
    appInfo.applicationVersion = VK_MAKE_VERSION(1, 0, 0);
    appInfo.pEngineName = "SrvSurvey";
    appInfo.engineVersion = VK_MAKE_VERSION(1, 0, 0);
    appInfo.apiVersion = VK_API_VERSION_1_1;

    if (useVulkanEnable2)
    {
        if (!xr.getVulkanRequirements2 || !xr.createVulkanInstance || !xr.getVulkanDevice2 || !xr.createVulkanDevice)
            return {OverlayStatus::GraphicsExtensionUnsupported, 0, {}, "OpenXR XR_KHR_vulkan_enable2 functions are missing."};

        XrGraphicsRequirementsVulkan2KHR reqs{XR_TYPE_GRAPHICS_REQUIREMENTS_VULKAN2_KHR};
        XrResult xrRes = xr.getVulkanRequirements2(instance, systemId, &reqs);
        if (XR_FAILED(xrRes))
            return {OverlayStatus::Error, static_cast<int32_t>(xrRes), {}, "Could not query Vulkan graphics requirements."};

        std::string versionError;
        if (!SelectVulkanApiVersion(loader.vkGetInstanceProcAddr, reqs.minApiVersionSupported,
                                    reqs.maxApiVersionSupported, appInfo.apiVersion, versionError))
            return {OverlayStatus::Error, 0, {}, versionError};

        VkInstanceCreateInfo vkInstInfo{};
        vkInstInfo.sType = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO;
        vkInstInfo.pApplicationInfo = &appInfo;

        XrVulkanInstanceCreateInfoKHR xrVkInst{XR_TYPE_VULKAN_INSTANCE_CREATE_INFO_KHR};
        xrVkInst.systemId = systemId;
        xrVkInst.pfnGetInstanceProcAddr = loader.vkGetInstanceProcAddr;
        xrVkInst.vulkanCreateInfo = &vkInstInfo;

        VkResult vkRes = VK_SUCCESS;
        xrRes = xr.createVulkanInstance(instance, &xrVkInst, &vkInstance, &vkRes);
        if (XR_FAILED(xrRes) || vkRes != VK_SUCCESS || vkInstance == VK_NULL_HANDLE)
            return {OverlayStatus::Error, static_cast<int32_t>(xrRes), {}, "Could not create a headless Vulkan instance."};

        if (!LoadVulkanInstanceFunctions())
            return {OverlayStatus::VulkanMissing, 0, {}, "Required Vulkan instance functions are missing."};

        XrVulkanGraphicsDeviceGetInfoKHR deviceGetInfo{XR_TYPE_VULKAN_GRAPHICS_DEVICE_GET_INFO_KHR};
        deviceGetInfo.systemId = systemId;
        deviceGetInfo.vulkanInstance = vkInstance;
        xrRes = xr.getVulkanDevice2(instance, &deviceGetInfo, &vkPhysicalDevice);
        if (XR_FAILED(xrRes) || vkPhysicalDevice == VK_NULL_HANDLE)
            return {OverlayStatus::Error, static_cast<int32_t>(xrRes), {}, "Could not select the OpenXR Vulkan GPU."};
    }
    else
    {
        if (!xr.getVulkanRequirements1 || !xr.getVulkanInstanceExtensions || !xr.getVulkanDevice1 ||
            !xr.getVulkanDeviceExtensions)
            return {OverlayStatus::GraphicsExtensionUnsupported, 0, {}, "OpenXR XR_KHR_vulkan_enable functions are missing."};

        XrGraphicsRequirementsVulkanKHR reqs{XR_TYPE_GRAPHICS_REQUIREMENTS_VULKAN_KHR};
        XrResult xrRes = xr.getVulkanRequirements1(instance, systemId, &reqs);
        if (XR_FAILED(xrRes))
            return {OverlayStatus::Error, static_cast<int32_t>(xrRes), {}, "Could not query Vulkan graphics requirements."};

        std::string versionError;
        if (!SelectVulkanApiVersion(loader.vkGetInstanceProcAddr, reqs.minApiVersionSupported,
                                    reqs.maxApiVersionSupported, appInfo.apiVersion, versionError))
            return {OverlayStatus::Error, 0, {}, versionError};

        uint32_t extLen = 0;
        xrRes = xr.getVulkanInstanceExtensions(instance, systemId, 0, &extLen, nullptr);
        if (XR_FAILED(xrRes))
            return {OverlayStatus::Error, static_cast<int32_t>(xrRes), {}, "Could not query Vulkan instance extensions."};

        std::string extBuf(extLen, '\0');
        if (extLen > 0)
            xr.getVulkanInstanceExtensions(instance, systemId, extLen, &extLen, extBuf.data());
        std::vector<std::string> instExtNames = SplitWhitespace(extBuf);
        std::vector<const char *> instExtPtrs;
        for (const std::string &name : instExtNames)
            instExtPtrs.push_back(name.c_str());

        if (!BindVkInstance(loader.vkGetInstanceProcAddr, VK_NULL_HANDLE, "vkCreateInstance", vk.createInstance))
            return {OverlayStatus::VulkanMissing, 0, {}, "Vulkan loader does not expose vkCreateInstance."};

        VkInstanceCreateInfo vkInstInfo{};
        vkInstInfo.sType = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO;
        vkInstInfo.pApplicationInfo = &appInfo;
        vkInstInfo.enabledExtensionCount = static_cast<uint32_t>(instExtPtrs.size());
        vkInstInfo.ppEnabledExtensionNames = instExtPtrs.empty() ? nullptr : instExtPtrs.data();
        if (vk.createInstance(&vkInstInfo, nullptr, &vkInstance) != VK_SUCCESS || vkInstance == VK_NULL_HANDLE)
            return {OverlayStatus::Error, 0, {}, "Could not create a headless Vulkan instance."};

        if (!LoadVulkanInstanceFunctions())
            return {OverlayStatus::VulkanMissing, 0, {}, "Required Vulkan instance functions are missing."};

        xrRes = xr.getVulkanDevice1(instance, systemId, vkInstance, &vkPhysicalDevice);
        if (XR_FAILED(xrRes) || vkPhysicalDevice == VK_NULL_HANDLE)
            return {OverlayStatus::Error, static_cast<int32_t>(xrRes), {}, "Could not select the OpenXR Vulkan GPU."};
    }

    uint32_t familyCount = 0;
    vk.getQueueFamilyProperties(vkPhysicalDevice, &familyCount, nullptr);
    std::vector<VkQueueFamilyProperties> families(familyCount);
    if (familyCount > 0)
        vk.getQueueFamilyProperties(vkPhysicalDevice, &familyCount, families.data());

    bool foundQueue = false;
    for (uint32_t i = 0; i < familyCount; ++i)
    {
        if (families[i].queueCount > 0 && (families[i].queueFlags & VK_QUEUE_GRAPHICS_BIT) != 0)
        {
            queueFamilyIndex = i;
            foundQueue = true;
            break;
        }
    }
    if (!foundQueue)
        return {OverlayStatus::Error, 0, {}, "No suitable Vulkan graphics queue family was found."};

    float queuePriority = 1.0f;
    VkDeviceQueueCreateInfo queueInfo{};
    queueInfo.sType = VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO;
    queueInfo.queueFamilyIndex = queueFamilyIndex;
    queueInfo.queueCount = 1;
    queueInfo.pQueuePriorities = &queuePriority;

    VkDeviceCreateInfo devInfo{};
    devInfo.sType = VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO;
    devInfo.queueCreateInfoCount = 1;
    devInfo.pQueueCreateInfos = &queueInfo;

    if (useVulkanEnable2)
    {
        XrVulkanDeviceCreateInfoKHR xrVkDev{XR_TYPE_VULKAN_DEVICE_CREATE_INFO_KHR};
        xrVkDev.systemId = systemId;
        xrVkDev.pfnGetInstanceProcAddr = loader.vkGetInstanceProcAddr;
        xrVkDev.vulkanPhysicalDevice = vkPhysicalDevice;
        xrVkDev.vulkanCreateInfo = &devInfo;

        VkResult vkRes = VK_SUCCESS;
        XrResult xrRes = xr.createVulkanDevice(instance, &xrVkDev, &vkDevice, &vkRes);
        if (XR_FAILED(xrRes) || vkRes != VK_SUCCESS || vkDevice == VK_NULL_HANDLE)
            return {OverlayStatus::Error, static_cast<int32_t>(xrRes), {}, "Could not create a Vulkan logical device."};
    }
    else
    {
        uint32_t devExtLen = 0;
        XrResult xrRes = xr.getVulkanDeviceExtensions(instance, systemId, 0, &devExtLen, nullptr);
        if (XR_FAILED(xrRes))
            return {OverlayStatus::Error, static_cast<int32_t>(xrRes), {}, "Could not query Vulkan device extensions."};

        std::string devExtBuf(devExtLen, '\0');
        if (devExtLen > 0)
            xr.getVulkanDeviceExtensions(instance, systemId, devExtLen, &devExtLen, devExtBuf.data());
        std::vector<std::string> devExtNames = SplitWhitespace(devExtBuf);
        std::vector<const char *> devExtPtrs;
        for (const std::string &name : devExtNames)
            devExtPtrs.push_back(name.c_str());

        devInfo.enabledExtensionCount = static_cast<uint32_t>(devExtPtrs.size());
        devInfo.ppEnabledExtensionNames = devExtPtrs.empty() ? nullptr : devExtPtrs.data();
        if (!vk.createDevice || vk.createDevice(vkPhysicalDevice, &devInfo, nullptr, &vkDevice) != VK_SUCCESS ||
            vkDevice == VK_NULL_HANDLE)
            return {OverlayStatus::Error, 0, {}, "Could not create a Vulkan logical device."};
    }

    if (!LoadVulkanDeviceFunctions())
        return {OverlayStatus::VulkanMissing, 0, {}, "Required Vulkan device functions are missing."};

    vk.getDeviceQueue(vkDevice, queueFamilyIndex, 0, &vkQueue);

    VkCommandPoolCreateInfo poolInfo{};
    poolInfo.sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
    poolInfo.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
    poolInfo.queueFamilyIndex = queueFamilyIndex;
    if (vk.createCommandPool(vkDevice, &poolInfo, nullptr, &commandPool) != VK_SUCCESS)
        return {OverlayStatus::Error, 0, {}, "Could not create a Vulkan command pool."};

    VkCommandBufferAllocateInfo allocInfo{};
    allocInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
    allocInfo.commandPool = commandPool;
    allocInfo.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
    allocInfo.commandBufferCount = 1;
    if (vk.allocateCommandBuffers(vkDevice, &allocInfo, &commandBuffer) != VK_SUCCESS)
        return {OverlayStatus::Error, 0, {}, "Could not allocate a Vulkan command buffer."};

    VkFenceCreateInfo fenceInfo{};
    fenceInfo.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
    fenceInfo.flags = VK_FENCE_CREATE_SIGNALED_BIT;
    if (vk.createFence(vkDevice, &fenceInfo, nullptr, &uploadFence) != VK_SUCCESS)
        return {OverlayStatus::Error, 0, {}, "Could not create a Vulkan upload fence."};

    return {OverlayStatus::Ok, 0, {}, {}};
}

uint32_t OverlaySession::FindMemoryType(uint32_t typeBits, VkMemoryPropertyFlags required, bool &isHostCoherent) const
{
    VkPhysicalDeviceMemoryProperties props{};
    vk.getMemoryProperties(vkPhysicalDevice, &props);
    VkMemoryPropertyFlags preferred = required | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT;
    for (uint32_t i = 0; i < props.memoryTypeCount; ++i)
    {
        if ((typeBits & (1u << i)) != 0 && (props.memoryTypes[i].propertyFlags & preferred) == preferred)
        {
            isHostCoherent = true;
            return i;
        }
    }
    for (uint32_t i = 0; i < props.memoryTypeCount; ++i)
    {
        if ((typeBits & (1u << i)) != 0 && (props.memoryTypes[i].propertyFlags & required) == required)
        {
            isHostCoherent = (props.memoryTypes[i].propertyFlags & VK_MEMORY_PROPERTY_HOST_COHERENT_BIT) != 0;
            return i;
        }
    }
    return UINT32_MAX;
}

bool OverlaySession::EnsureStagingCapacity(VkDeviceSize requiredBytes)
{
    if (staging.buffer != VK_NULL_HANDLE && staging.capacity >= requiredBytes)
        return true;

    if (gpuWorkInFlight && !WaitForGpuIdle())
        return false;

    DestroyStagingBuffer();

    VkBufferCreateInfo bufferInfo{};
    bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
    bufferInfo.size = requiredBytes;
    bufferInfo.usage = VK_BUFFER_USAGE_TRANSFER_SRC_BIT;
    bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    if (vk.createBuffer(vkDevice, &bufferInfo, nullptr, &staging.buffer) != VK_SUCCESS)
        return false;

    VkMemoryRequirements memReqs{};
    vk.getBufferMemoryRequirements(vkDevice, staging.buffer, &memReqs);
    uint32_t typeIndex = FindMemoryType(memReqs.memoryTypeBits, VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT, staging.hostCoherent);
    if (typeIndex == UINT32_MAX)
    {
        DestroyStagingBuffer();
        return false;
    }

    VkMemoryAllocateInfo allocInfo{};
    allocInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    allocInfo.allocationSize = memReqs.size;
    allocInfo.memoryTypeIndex = typeIndex;
    if (vk.allocateMemory(vkDevice, &allocInfo, nullptr, &staging.memory) != VK_SUCCESS)
    {
        DestroyStagingBuffer();
        return false;
    }

    if (vk.bindBufferMemory(vkDevice, staging.buffer, staging.memory, 0) != VK_SUCCESS ||
        vk.mapMemory(vkDevice, staging.memory, 0, VK_WHOLE_SIZE, 0, &staging.mapped) != VK_SUCCESS)
    {
        DestroyStagingBuffer();
        return false;
    }

    staging.capacity = requiredBytes;
    return true;
}

void OverlaySession::DestroyStagingBuffer()
{
    if (vkDevice == VK_NULL_HANDLE)
        return;
    if (staging.mapped && staging.memory != VK_NULL_HANDLE && vk.unmapMemory)
        vk.unmapMemory(vkDevice, staging.memory);
    if (staging.buffer != VK_NULL_HANDLE && vk.destroyBuffer)
        vk.destroyBuffer(vkDevice, staging.buffer, nullptr);
    if (staging.memory != VK_NULL_HANDLE && vk.freeMemory)
        vk.freeMemory(vkDevice, staging.memory, nullptr);
    staging = {};
}

bool OverlaySession::WaitForGpuIdle()
{
    if (vkDevice == VK_NULL_HANDLE)
    {
        gpuWorkInFlight = false;
        return true;
    }
    if (!vk.deviceWaitIdle)
        return !gpuWorkInFlight;
    VkResult result = vk.deviceWaitIdle(vkDevice);
    if (result == VK_SUCCESS || result == VK_ERROR_DEVICE_LOST)
    {
        gpuWorkInFlight = false;
        return true;
    }
    return !gpuWorkInFlight;
}

bool OverlaySession::RetireSwapchain(SwapchainTexture &texture)
{
    if (texture.handle != XR_NULL_HANDLE)
    {
        if (!WaitForGpuIdle())
            return false;
        if (xr.destroySwapchain)
            xr.destroySwapchain(texture.handle);
    }
    texture = {};
    return true;
}

void OverlaySession::DestroySwapchains()
{
    if (WaitForGpuIdle())
    {
        for (auto &[id, texture] : textures)
        {
            (void)id;
            if (texture.handle != XR_NULL_HANDLE && xr.destroySwapchain)
                xr.destroySwapchain(texture.handle);
        }
        if (cursorTexture.handle != XR_NULL_HANDLE && xr.destroySwapchain)
            xr.destroySwapchain(cursorTexture.handle);
    }
    textures.clear();
    cursorTexture = {};
}

void OverlaySession::CleanupResources()
{
    ClearHeadPose();
    interactionEnabled.store(false, std::memory_order_release);
    sessionFocused = false;
    previousHandButtons = {0, 0};
    (void)pointer.Reset();
    {
        std::lock_guard lock(inputMutex);
        inputReady = false;
        pointerEvents.clear();
    }
    if (!WaitForGpuIdle())
    {
        // GPU work completion is unconfirmed (for example VK_ERROR_OUT_OF_HOST_MEMORY while an upload is in flight).
        // Detach without destroying GPU-referenced Vulkan or OpenXR handles to avoid use-after-free teardown.
        textures.clear();
        cursorTexture = {};
        staging = {};
        aimSpaces = {XR_NULL_HANDLE, XR_NULL_HANDLE};
        actionSet = XR_NULL_HANDLE;
        aimAction = XR_NULL_HANDLE;
        selectAction = XR_NULL_HANDLE;
        secondaryAction = XR_NULL_HANDLE;
        middleAction = XR_NULL_HANDLE;
        scrollAction = XR_NULL_HANDLE;
        handPaths = {XR_NULL_PATH, XR_NULL_PATH};
        localSpace = XR_NULL_HANDLE;
        viewSpace = XR_NULL_HANDLE;
        session = XR_NULL_HANDLE;
        sessionRunning = false;
        anchored = false;
        pendingLocalSpaceChanges.clear();
        uploadFence = VK_NULL_HANDLE;
        commandPool = VK_NULL_HANDLE;
        commandBuffer = VK_NULL_HANDLE;
        vkDevice = VK_NULL_HANDLE;
        vkInstance = VK_NULL_HANDLE;
        instance = XR_NULL_HANDLE;
        return;
    }
    DestroySwapchains();
    DestroyStagingBuffer();
    for (XrSpace &space : aimSpaces)
    {
        if (space != XR_NULL_HANDLE && xr.destroySpace)
            xr.destroySpace(space);
        space = XR_NULL_HANDLE;
    }
    if (localSpace != XR_NULL_HANDLE && xr.destroySpace)
    {
        xr.destroySpace(localSpace);
        localSpace = XR_NULL_HANDLE;
    }
    if (viewSpace != XR_NULL_HANDLE && xr.destroySpace)
    {
        xr.destroySpace(viewSpace);
        viewSpace = XR_NULL_HANDLE;
    }
    if (session != XR_NULL_HANDLE && xr.destroySession)
    {
        xr.destroySession(session);
        session = XR_NULL_HANDLE;
    }
    if (actionSet != XR_NULL_HANDLE && xr.destroyActionSet)
    {
        xr.destroyActionSet(actionSet);
        actionSet = XR_NULL_HANDLE;
    }
    aimAction = XR_NULL_HANDLE;
    selectAction = XR_NULL_HANDLE;
    secondaryAction = XR_NULL_HANDLE;
    middleAction = XR_NULL_HANDLE;
    scrollAction = XR_NULL_HANDLE;
    handPaths = {XR_NULL_PATH, XR_NULL_PATH};
    sessionRunning = false;
    anchored = false;
    gpuWorkInFlight = false;
    pendingLocalSpaceChanges.clear();
    if (vkDevice != VK_NULL_HANDLE)
    {
        if (uploadFence != VK_NULL_HANDLE && vk.destroyFence)
        {
            vk.destroyFence(vkDevice, uploadFence, nullptr);
            uploadFence = VK_NULL_HANDLE;
        }
        if (commandPool != VK_NULL_HANDLE && vk.destroyCommandPool)
        {
            vk.destroyCommandPool(vkDevice, commandPool, nullptr);
            commandPool = VK_NULL_HANDLE;
            commandBuffer = VK_NULL_HANDLE;
        }
        if (vk.destroyDevice)
            vk.destroyDevice(vkDevice, nullptr);
        vkDevice = VK_NULL_HANDLE;
    }
    if (vkInstance != VK_NULL_HANDLE && vk.destroyInstance)
    {
        vk.destroyInstance(vkInstance, nullptr);
        vkInstance = VK_NULL_HANDLE;
    }
    if (instance != XR_NULL_HANDLE && xr.destroyInstance)
    {
        xr.destroyInstance(instance);
        instance = XR_NULL_HANDLE;
    }
}

void OverlaySession::Shutdown()
{
    stopRequested.store(true);
    if (worker.joinable())
        worker.join();
    CleanupResources();
}

bool OverlaySession::PublishPacket(std::span<const uint8_t> packet)
{
    if (!ValidateOverlayPacketStructure(packet))
        return false;
    auto nextPacket = std::make_shared<const std::vector<uint8_t>>(packet.begin(), packet.end());
    std::lock_guard lock(packetMutex);
    latestPacket = std::move(nextPacket);
    ++latestPacketSequence;
    return true;
}

RuntimeStatus OverlaySession::GetStatus() const
{
    std::lock_guard lock(statusMutex);
    RuntimeStatus copy = currentStatus;
    copy.maxLayers = maxLayers;
    return copy;
}

HeadPose OverlaySession::GetHeadPose(uint32_t expectedOriginRevision) const
{
    std::lock_guard lock(statusMutex);
    if (!currentHeadPose.valid || currentHeadPose.originRevision != expectedOriginRevision)
        return {};
    return currentHeadPose;
}

bool OverlaySession::SetInteractionEnabled(bool enabled, int32_t &outXrError, std::string &outMessage)
{
    outXrError = 0;
    std::lock_guard lock(inputMutex);
    if (!enabled)
    {
        interactionEnabled.store(false, std::memory_order_release);
        interactionGeneration.fetch_add(1, std::memory_order_acq_rel);
        pointerEvents.clear();
        outMessage = "VR overlays are click-through again.";
        return true;
    }

    if (!inputReady)
    {
        outXrError = inputError;
        outMessage = inputErrorMessage.empty()
                         ? "OpenXR controller input is unavailable for this overlay session."
                         : inputErrorMessage;
        return false;
    }

    if (maxLayers <= 1)
    {
        outXrError = 0;
        outMessage =
            "The active OpenXR runtime supports only 1 composition layer; at least 2 layers are required for controller-pointer interaction.";
        return false;
    }

    pointerEvents.clear();
    interactionEnabled.store(true, std::memory_order_release);
    interactionGeneration.fetch_add(1, std::memory_order_acq_rel);
    outMessage = "Controller-pointer interaction is enabled. Point at a panel and use the trigger to click.";
    return true;
}

size_t OverlaySession::PollPointerEvents(std::span<PointerEvent> outEvents)
{
    if (outEvents.empty())
        return 0;
    std::lock_guard lock(inputMutex);
    size_t count = std::min(outEvents.size(), pointerEvents.size());
    for (size_t i = 0; i < count; ++i)
    {
        outEvents[i] = pointerEvents.front();
        pointerEvents.pop_front();
    }
    return count;
}

OverlaySession::EnqueueResult OverlaySession::QueuePointerEvents(const std::vector<PointerEvent> &events,
                                                                 std::optional<uint64_t> expectedGeneration)
{
    if (events.empty())
        return EnqueueResult::Ok;
    std::lock_guard lock(inputMutex);
    if (expectedGeneration && interactionGeneration.load(std::memory_order_acquire) != *expectedGeneration)
        return EnqueueResult::StaleGeneration;
    for (const PointerEvent &ev : events)
    {
        if (ev.kind == PointerKind::Move && !pointerEvents.empty() &&
            pointerEvents.back().kind == PointerKind::Move && pointerEvents.back().id == ev.id)
        {
            pointerEvents.back() = ev;
            continue;
        }
        if (ev.kind == PointerKind::Reset && !pointerEvents.empty() &&
            pointerEvents.back().kind == PointerKind::Reset)
        {
            continue;
        }
        if (pointerEvents.size() >= MaxQueuedPointerEvents)
        {
            auto oldestMove = std::find_if(pointerEvents.begin(), pointerEvents.end(),
                                           [](const PointerEvent &queued) { return queued.kind == PointerKind::Move; });
            if (oldestMove != pointerEvents.end())
            {
                pointerEvents.erase(oldestMove);
            }
            else
            {
                pointerEvents.clear();
                pointerEvents.push_back({0, PointerKind::Reset});
                return EnqueueResult::Overflow;
            }
        }
        pointerEvents.push_back(ev);
    }
    return EnqueueResult::Ok;
}

void OverlaySession::ResetInteractionState(bool forceResetEvent)
{
    previousHandButtons = {0, 0};
    PointerUpdate reset = pointer.Reset();
    if (!reset.events.empty())
    {
        (void)QueuePointerEvents(reset.events);
    }
    else if (forceResetEvent)
    {
        std::vector<PointerEvent> synthetic{{0, PointerKind::Reset}};
        (void)QueuePointerEvents(synthetic);
    }
}

OverlaySession::SampledInteraction OverlaySession::SampleInteraction(std::span<const PointerSurface> surfaces,
                                                                     XrTime displayTime)
{
    uint64_t generation = 0;
    bool enabled = false;
    bool ready = false;
    {
        std::lock_guard lock(inputMutex);
        generation = interactionGeneration.load(std::memory_order_acquire);
        enabled = interactionEnabled.load(std::memory_order_acquire);
        ready = inputReady;
    }

    if (generation != appliedInteractionGeneration)
    {
        appliedInteractionGeneration = generation;
        previousHandButtons = {0, 0};
        (void)pointer.Reset();
    }

    if (!enabled || !ready || !sessionFocused || displayTime <= 0)
    {
        previousHandButtons = {0, 0};
        return {pointer.Reset(), generation, true};
    }

    XrActiveActionSet activeSet{actionSet, XR_NULL_PATH};
    XrActionsSyncInfo syncInfo{XR_TYPE_ACTIONS_SYNC_INFO};
    syncInfo.countActiveActionSets = 1;
    syncInfo.activeActionSets = &activeSet;
    XrResult syncRes = xr.syncActions(session, &syncInfo);
    if (syncRes != XR_SUCCESS)
    {
        previousHandButtons = {0, 0};
        return {pointer.Reset(), generation, true};
    }

    std::array<ControllerSample, 2> hands{};
    bool actionReadError = false;
    for (size_t hand = 0; hand < hands.size(); ++hand)
    {
        XrActionStateGetInfo getInfo{XR_TYPE_ACTION_STATE_GET_INFO};
        getInfo.subactionPath = handPaths[hand];
        getInfo.action = aimAction;
        XrActionStatePose poseState{XR_TYPE_ACTION_STATE_POSE};
        XrResult poseRes = xr.getActionStatePose(session, &getInfo, &poseState);
        if (XR_FAILED(poseRes))
        {
            actionReadError = true;
            break;
        }
        if (!poseState.isActive)
        {
            previousHandButtons[hand] = 0;
            continue;
        }

        XrSpaceLocation location{XR_TYPE_SPACE_LOCATION};
        XrResult locRes = xr.locateSpace(aimSpaces[hand], localSpace, displayTime, &location);
        if (XR_FAILED(locRes))
        {
            actionReadError = true;
            break;
        }
        constexpr XrSpaceLocationFlags validBits =
            XR_SPACE_LOCATION_ORIENTATION_VALID_BIT | XR_SPACE_LOCATION_POSITION_VALID_BIT;
        if ((location.locationFlags & validBits) != validBits)
        {
            previousHandButtons[hand] = 0;
            continue;
        }

        bool handReadable = true;
        uint32_t nextButtons = 0;
        uint32_t bit = 1;
        for (XrAction buttonAction : {selectAction, secondaryAction, middleAction})
        {
            getInfo.action = buttonAction;
            XrActionStateBoolean buttonState{XR_TYPE_ACTION_STATE_BOOLEAN};
            XrResult btnRes = xr.getActionStateBoolean(session, &getInfo, &buttonState);
            if (XR_FAILED(btnRes))
            {
                actionReadError = true;
                handReadable = false;
                break;
            }
            if (!buttonState.isActive)
            {
                // Cancel capture instead of synthesizing a release if a held button becomes inactive.
                if ((previousHandButtons[hand] & bit) != 0)
                {
                    handReadable = false;
                    break;
                }
            }
            else if (buttonState.currentState == XR_TRUE)
            {
                nextButtons |= bit;
            }
            bit <<= 1;
        }
        if (actionReadError)
            break;
        if (!handReadable)
        {
            previousHandButtons[hand] = 0;
            continue;
        }

        getInfo.action = scrollAction;
        XrActionStateVector2f scrollState{XR_TYPE_ACTION_STATE_VECTOR2F};
        XrResult scrollRes = xr.getActionStateVector2f(session, &getInfo, &scrollState);
        if (XR_FAILED(scrollRes))
        {
            actionReadError = true;
            break;
        }

        hands[hand].pose = {
            {location.pose.orientation.x, location.pose.orientation.y, location.pose.orientation.z,
             location.pose.orientation.w},
            {location.pose.position.x, location.pose.position.y, location.pose.position.z},
        };
        hands[hand].buttons = nextButtons;
        hands[hand].valid = true;
        previousHandButtons[hand] = nextButtons;
        if (scrollState.isActive && std::isfinite(scrollState.currentState.x) &&
            std::isfinite(scrollState.currentState.y))
        {
            hands[hand].scrollX = std::clamp(scrollState.currentState.x, -1.0f, 1.0f);
            hands[hand].scrollY = std::clamp(scrollState.currentState.y, -1.0f, 1.0f);
        }
    }

    if (actionReadError)
    {
        previousHandButtons = {0, 0};
        return {pointer.Reset(), generation, true};
    }

    return {pointer.Update(true, hands, surfaces, static_cast<double>(displayTime) * 1e-9), generation, false};
}

std::optional<XrCompositionLayerQuad> OverlaySession::BuildCursorQuad(const PointerHit &hit, int32_t &outXrError,
                                                                      std::string &outError, bool &outFatal)
{
    const auto &pixels = GetCursorPixels();
    Descriptor descriptor{};
    descriptor.width = 32;
    descriptor.height = 32;
    descriptor.length = static_cast<uint32_t>(pixels.size());
    descriptor.alpha = 1.0f;

    if (!UploadPanel(cursorTexture, descriptor, 1, pixels.data(), outXrError, outError, outFatal))
        return std::nullopt;
    if (!cursorTexture.ready)
        return std::nullopt;

    auto displacement = Rotate(hit.pose.orientation,
                               {(hit.x - 0.5f) * hit.width, (0.5f - hit.y) * hit.height, 0.002f});
    XrCompositionLayerQuad quad{XR_TYPE_COMPOSITION_LAYER_QUAD};
    quad.layerFlags = XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT |
                      XR_COMPOSITION_LAYER_UNPREMULTIPLIED_ALPHA_BIT;
    quad.space = localSpace;
    quad.eyeVisibility = XR_EYE_VISIBILITY_BOTH;
    quad.subImage.swapchain = cursorTexture.handle;
    quad.subImage.imageRect.offset = {0, 0};
    quad.subImage.imageRect.extent = {32, 32};
    quad.subImage.imageArrayIndex = 0;
    quad.pose = {{hit.pose.orientation.x, hit.pose.orientation.y, hit.pose.orientation.z, hit.pose.orientation.w},
                 {hit.pose.position.x + displacement.x, hit.pose.position.y + displacement.y,
                  hit.pose.position.z + displacement.z}};
    quad.size = {0.012f, 0.012f};
    return quad;
}

void OverlaySession::SetStatus(OverlayStatus status, int32_t xrError, uint32_t renderedCount, const std::string &message)
{
    std::lock_guard lock(statusMutex);
    currentStatus.status = status;
    currentStatus.xrError = xrError;
    currentStatus.renderedCount = renderedCount;
    currentStatus.maxLayers = maxLayers;
    currentStatus.message = message;
}

void OverlaySession::ClearHeadPose()
{
    std::lock_guard lock(statusMutex);
    currentHeadPose = {};
}

void OverlaySession::WorkerLoop()
{
    while (!stopRequested.load())
    {
        if (!StepOnce())
            break;
        if (!sessionRunning && !stopRequested.load())
            std::this_thread::sleep_for(std::chrono::milliseconds(20));
    }
    ClearHeadPose();
}

bool OverlaySession::StepOnce()
{
    if (!PollEvents())
        return false;
    if (sessionRunning)
        return RenderFrame();
    return true;
}

bool OverlaySession::PollEvents()
{
    while (true)
    {
        XrEventDataBuffer event{XR_TYPE_EVENT_DATA_BUFFER};
        XrResult result = xr.pollEvent(instance, &event);
        if (result == XR_EVENT_UNAVAILABLE)
            return true;
        if (XR_FAILED(result))
        {
            sessionFocused = false;
            ResetInteractionState();
            ClearHeadPose();
            SetStatus(OverlayStatus::SessionLost, static_cast<int32_t>(result), 0,
                      "The OpenXR overlay event stream failed (OpenXR error " + std::to_string(result) + ").");
            return false;
        }

        if (event.type == XR_TYPE_EVENT_DATA_INSTANCE_LOSS_PENDING)
        {
            sessionFocused = false;
            ResetInteractionState();
            ClearHeadPose();
            SetStatus(OverlayStatus::SessionLost, 0, 0, "The OpenXR runtime disconnected.");
            return false;
        }
        if (event.type == XR_TYPE_EVENT_DATA_REFERENCE_SPACE_CHANGE_PENDING)
        {
            const auto *change = reinterpret_cast<const XrEventDataReferenceSpaceChangePending *>(&event);
            if (change->referenceSpaceType == XR_REFERENCE_SPACE_TYPE_LOCAL)
            {
                pendingLocalSpaceChanges.push_back(change->changeTime);
            }
        }
        else if (event.type == XR_TYPE_EVENT_DATA_MAIN_SESSION_VISIBILITY_CHANGED_EXTX)
        {
            const auto *visibility = reinterpret_cast<const XrEventDataMainSessionVisibilityChangedEXTX *>(&event);
            mainSessionVisible = visibility->visible == XR_TRUE;
        }
        else if (event.type == XR_TYPE_EVENT_DATA_SESSION_STATE_CHANGED)
        {
            const auto *stateChanged = reinterpret_cast<const XrEventDataSessionStateChanged *>(&event);
            bool nextFocused = stateChanged->state == XR_SESSION_STATE_FOCUSED;
            if (sessionFocused && !nextFocused)
                ResetInteractionState();
            sessionFocused = nextFocused;

            if (stateChanged->state == XR_SESSION_STATE_READY)
            {
                XrSessionBeginInfo beginInfo{XR_TYPE_SESSION_BEGIN_INFO};
                beginInfo.primaryViewConfigurationType = XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO;
                XrResult beginRes = xr.beginSession(session, &beginInfo);
                if (XR_FAILED(beginRes))
                {
                    ResetInteractionState();
                    ClearHeadPose();
                    SetStatus(OverlayStatus::Error, static_cast<int32_t>(beginRes), 0,
                              "Could not begin the OpenXR overlay session (OpenXR error " + std::to_string(beginRes) + ").");
                    return false;
                }
                sessionRunning = true;
            }
            else if (stateChanged->state == XR_SESSION_STATE_STOPPING)
            {
                ResetInteractionState();
                ClearHeadPose();
                if (sessionRunning)
                {
                    xr.endSession(session);
                    sessionRunning = false;
                }
            }
            else if (stateChanged->state == XR_SESSION_STATE_EXITING ||
                     stateChanged->state == XR_SESSION_STATE_LOSS_PENDING)
            {
                ResetInteractionState();
                ClearHeadPose();
                SetStatus(OverlayStatus::SessionLost, 0, 0, "The OpenXR overlay session ended.");
                return false;
            }
        }
    }
}

bool OverlaySession::EnsureSwapchain(SwapchainTexture &texture, uint32_t width, uint32_t height, int32_t &outXrError,
                                     std::string &outError, bool &outFatal)
{
    if (texture.handle != XR_NULL_HANDLE && (texture.width != width || texture.height != height))
    {
        if (!RetireSwapchain(texture))
        {
            outXrError = 0;
            outError = "Could not confirm Vulkan GPU idle before resizing an overlay swapchain.";
            outFatal = true;
            return false;
        }
    }
    if (texture.handle != XR_NULL_HANDLE)
        return true;

    XrSwapchainCreateInfo info{XR_TYPE_SWAPCHAIN_CREATE_INFO};
    info.usageFlags = XR_SWAPCHAIN_USAGE_COLOR_ATTACHMENT_BIT | XR_SWAPCHAIN_USAGE_SAMPLED_BIT |
                      XR_SWAPCHAIN_USAGE_TRANSFER_DST_BIT;
    info.format = swapchainFormat;
    info.sampleCount = 1;
    info.width = width;
    info.height = height;
    info.faceCount = 1;
    info.arraySize = 1;
    info.mipCount = 1;

    XrResult res = xr.createSwapchain(session, &info, &texture.handle);
    if (XR_FAILED(res))
    {
        outXrError = static_cast<int32_t>(res);
        outError = "Could not create a Vulkan OpenXR swapchain (OpenXR error " + std::to_string(res) + ").";
        return false;
    }

    uint32_t imageCount = 0;
    res = xr.enumerateSwapchainImages(texture.handle, 0, &imageCount, nullptr);
    if (XR_FAILED(res) || imageCount == 0)
    {
        if (!RetireSwapchain(texture))
            outFatal = true;
        outXrError = static_cast<int32_t>(res);
        outError = "Could not enumerate OpenXR swapchain images.";
        return false;
    }

    texture.images.assign(imageCount, {XR_TYPE_SWAPCHAIN_IMAGE_VULKAN_KHR});
    res = xr.enumerateSwapchainImages(texture.handle, imageCount, &imageCount,
                                      reinterpret_cast<XrSwapchainImageBaseHeader *>(texture.images.data()));
    if (XR_FAILED(res))
    {
        if (!RetireSwapchain(texture))
            outFatal = true;
        outXrError = static_cast<int32_t>(res);
        outError = "Could not read OpenXR swapchain images.";
        return false;
    }

    texture.width = width;
    texture.height = height;
    return true;
}

bool OverlaySession::UploadPanel(SwapchainTexture &texture, const Descriptor &descriptor, uint32_t sequence,
                                 const uint8_t *pixels, int32_t &outXrError, std::string &outError, bool &outFatal)
{
    if (!EnsureSwapchain(texture, descriptor.width, descriptor.height, outXrError, outError, outFatal))
        return false;

    const bool convertSrgbToLinear = !swapchainIsSrgb;
    if (texture.ready && !texture.acquired && !texture.waited)
    {
        if (texture.sourceSequence == sequence)
            return true;
        if (MatchesPixels(texture, descriptor, pixels, swapRedBlue, convertSrgbToLinear))
        {
            texture.sourceSequence = sequence;
            return true;
        }
    }

    if (!texture.acquired)
    {
        XrSwapchainImageAcquireInfo acquireInfo{XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO};
        XrResult res = xr.acquireSwapchainImage(texture.handle, &acquireInfo, &texture.acquiredIndex);
        if (XR_FAILED(res))
        {
            outXrError = static_cast<int32_t>(res);
            outError = "Could not acquire an OpenXR swapchain image (OpenXR error " + std::to_string(res) + ").";
            return false;
        }
        texture.acquired = true;
        texture.waited = false;
    }

    if (!texture.waited)
    {
        XrSwapchainImageWaitInfo waitInfo{XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO};
        waitInfo.timeout = 0;
        XrResult waitRes = xr.waitSwapchainImage(texture.handle, &waitInfo);
        if (waitRes == XR_TIMEOUT_EXPIRED)
            return true;
        if (XR_FAILED(waitRes) || texture.acquiredIndex >= texture.images.size())
        {
            outXrError = XR_FAILED(waitRes) ? static_cast<int32_t>(waitRes) : 0;
            outError = XR_FAILED(waitRes)
                           ? "Could not wait for an OpenXR swapchain image (OpenXR error " + std::to_string(waitRes) + ")."
                           : "OpenXR returned an out-of-range swapchain image index.";
            if (!RetireSwapchain(texture))
                outFatal = true;
            return false;
        }
        texture.waited = true;
    }

    texture.pixels.resize(descriptor.length);
    for (size_t i = 0; i < descriptor.length; i += 4)
    {
        uint8_t r = swapRedBlue ? pixels[i + 2] : pixels[i];
        uint8_t g = pixels[i + 1];
        uint8_t b = swapRedBlue ? pixels[i] : pixels[i + 2];
        if (convertSrgbToLinear)
        {
            r = SrgbByteToLinearByte(r);
            g = SrgbByteToLinearByte(g);
            b = SrgbByteToLinearByte(b);
        }
        texture.pixels[i] = r;
        texture.pixels[i + 1] = g;
        texture.pixels[i + 2] = b;
        texture.pixels[i + 3] = static_cast<uint8_t>(static_cast<float>(pixels[i + 3]) * descriptor.alpha);
    }

    if (!EnsureStagingCapacity(descriptor.length))
    {
        outXrError = 0;
        outError = "Could not allocate Vulkan staging memory for the overlay upload.";
        return false;
    }

    std::memcpy(staging.mapped, texture.pixels.data(), descriptor.length);
    if (!staging.hostCoherent)
    {
        if (!vk.flushMappedMemoryRanges)
        {
            outXrError = 0;
            outError = "Vulkan device does not expose vkFlushMappedMemoryRanges for non-coherent staging memory.";
            return false;
        }

        VkMappedMemoryRange range{};
        range.sType = VK_STRUCTURE_TYPE_MAPPED_MEMORY_RANGE;
        range.memory = staging.memory;
        range.offset = 0;
        range.size = VK_WHOLE_SIZE;
        if (vk.flushMappedMemoryRanges(vkDevice, 1, &range) != VK_SUCCESS)
        {
            outXrError = 0;
            outError = "Could not flush non-coherent Vulkan staging memory.";
            return false;
        }
    }

    VkImage dstImage = texture.images[texture.acquiredIndex].image;
    vk.resetCommandBuffer(commandBuffer, 0);
    VkCommandBufferBeginInfo cmdBegin{};
    cmdBegin.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
    cmdBegin.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
    if (vk.beginCommandBuffer(commandBuffer, &cmdBegin) != VK_SUCCESS)
    {
        outXrError = 0;
        outError = "Could not begin the Vulkan upload command buffer.";
        if (!RetireSwapchain(texture))
            outFatal = true;
        return false;
    }

    VkImageMemoryBarrier toTransfer{};
    toTransfer.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
    toTransfer.oldLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    toTransfer.newLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
    toTransfer.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    toTransfer.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    toTransfer.image = dstImage;
    toTransfer.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
    toTransfer.srcAccessMask = 0;
    toTransfer.dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
    vk.cmdPipelineBarrier(commandBuffer, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, VK_PIPELINE_STAGE_TRANSFER_BIT, 0, 0,
                          nullptr, 0, nullptr, 1, &toTransfer);

    VkBufferImageCopy region{};
    region.imageSubresource = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 0, 1};
    region.imageExtent = {descriptor.width, descriptor.height, 1};
    vk.cmdCopyBufferToImage(commandBuffer, staging.buffer, dstImage, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &region);

    VkImageMemoryBarrier toColorAttachment{};
    toColorAttachment.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
    toColorAttachment.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
    toColorAttachment.newLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
    toColorAttachment.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    toColorAttachment.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    toColorAttachment.image = dstImage;
    toColorAttachment.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
    toColorAttachment.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
    toColorAttachment.dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
    vk.cmdPipelineBarrier(commandBuffer, VK_PIPELINE_STAGE_TRANSFER_BIT,
                          VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT, 0, 0, nullptr, 0, nullptr, 1,
                          &toColorAttachment);

    if (vk.endCommandBuffer(commandBuffer) != VK_SUCCESS)
    {
        outXrError = 0;
        outError = "Could not finish the Vulkan upload command buffer.";
        if (!RetireSwapchain(texture))
            outFatal = true;
        return false;
    }

    vk.resetFences(vkDevice, 1, &uploadFence);
    VkSubmitInfo submit{};
    submit.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
    submit.commandBufferCount = 1;
    submit.pCommandBuffers = &commandBuffer;
    if (vk.queueSubmit(vkQueue, 1, &submit, uploadFence) != VK_SUCCESS)
    {
        outXrError = 0;
        outError = "Could not submit the Vulkan overlay upload.";
        if (!RetireSwapchain(texture))
            outFatal = true;
        return false;
    }
    gpuWorkInFlight = true;
    if (vk.waitForFences(vkDevice, 1, &uploadFence, VK_TRUE, UINT64_MAX) != VK_SUCCESS)
    {
        outXrError = 0;
        outError = "Could not wait for the Vulkan overlay upload fence.";
        if (!RetireSwapchain(texture))
            outFatal = true;
        return false;
    }
    gpuWorkInFlight = false;

    XrSwapchainImageReleaseInfo releaseInfo{XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO};
    XrResult releaseRes = xr.releaseSwapchainImage(texture.handle, &releaseInfo);
    if (XR_FAILED(releaseRes))
    {
        outXrError = static_cast<int32_t>(releaseRes);
        outError = "Could not release an OpenXR swapchain image (OpenXR error " + std::to_string(releaseRes) + ").";
        if (!RetireSwapchain(texture))
            outFatal = true;
        return false;
    }

    texture.acquired = false;
    texture.waited = false;
    texture.ready = true;
    texture.sourceSequence = sequence;
    return true;
}

bool OverlaySession::RenderFrame()
{
    XrFrameWaitInfo waitInfo{XR_TYPE_FRAME_WAIT_INFO};
    XrFrameState frameState{XR_TYPE_FRAME_STATE};
    XrResult res = xr.waitFrame(session, &waitInfo, &frameState);
    if (XR_FAILED(res))
    {
        ResetInteractionState();
        ClearHeadPose();
        SetStatus(OverlayStatus::SessionLost, static_cast<int32_t>(res), 0,
                  "OpenXR waitFrame failed (OpenXR error " + std::to_string(res) + ").");
        return false;
    }

    XrFrameBeginInfo beginInfo{XR_TYPE_FRAME_BEGIN_INFO};
    res = xr.beginFrame(session, &beginInfo);
    if (XR_FAILED(res))
    {
        ResetInteractionState();
        ClearHeadPose();
        SetStatus(OverlayStatus::SessionLost, static_cast<int32_t>(res), 0,
                  "OpenXR beginFrame failed (OpenXR error " + std::to_string(res) + ").");
        return false;
    }

    std::shared_ptr<const std::vector<uint8_t>> packetSnapshot;
    uint32_t packetSequence = 0;
    {
        std::lock_guard lock(packetMutex);
        packetSnapshot = latestPacket;
        packetSequence = latestPacketSequence;
    }

    Header header{};
    bool hasPacket = packetSnapshot && packetSnapshot->size() >= sizeof(Header);
    if (hasPacket)
        std::memcpy(&header, packetSnapshot->data(), sizeof(header));

    if (!pendingLocalSpaceChanges.empty())
    {
        bool triggered = false;
        std::erase_if(pendingLocalSpaceChanges, [&](XrTime changeTime) {
            if (frameState.predictedDisplayTime >= changeTime)
            {
                triggered = true;
                return true;
            }
            return false;
        });
        if (triggered)
            anchored = false;
    }

    XrSpaceLocation headLocation{XR_TYPE_SPACE_LOCATION};
    bool headValid = false;
    if (frameState.predictedDisplayTime > 0 &&
        XR_SUCCEEDED(xr.locateSpace(viewSpace, localSpace, frameState.predictedDisplayTime, &headLocation)))
    {
        constexpr XrSpaceLocationFlags required =
            XR_SPACE_LOCATION_POSITION_VALID_BIT | XR_SPACE_LOCATION_ORIENTATION_VALID_BIT;
        headValid = (headLocation.locationFlags & required) == required;
    }

    if (headValid)
    {
        if (!anchored || (hasPacket && header.originRevision != originRevision))
        {
            anchor = HeadingAnchor(headLocation.pose);
            originRevision = hasPacket ? header.originRevision : originRevision;
            anchored = true;
        }

        float heading = ExtractYaw(anchor.orientation);
        float dx = headLocation.pose.position.x - anchor.position.x;
        float dz = headLocation.pose.position.z - anchor.position.z;
        HeadPose pose{};
        pose.valid = true;
        pose.originRevision = originRevision;
        pose.position[0] = std::cos(heading) * dx - std::sin(heading) * dz;
        pose.position[1] = headLocation.pose.position.y - anchor.position.y;
        pose.position[2] = std::sin(heading) * dx + std::cos(heading) * dz;
        pose.yaw = ExtractYaw(headLocation.pose.orientation) - heading;
        std::lock_guard lock(statusMutex);
        currentHeadPose = pose;
    }
    else
    {
        ClearHeadPose();
    }

    std::vector<XrCompositionLayerQuad> quads;
    std::vector<PointerSurface> surfaces;
    std::vector<const XrCompositionLayerBaseHeader *> layerPtrs;
    std::vector<uint64_t> activeIds;
    bool uploadFailed = false;
    bool uploadFatal = false;
    int32_t uploadXrError = 0;
    std::string uploadErrorMessage;

    bool interactionWillRun = false;
    {
        std::lock_guard lock(inputMutex);
        interactionWillRun = interactionEnabled.load(std::memory_order_acquire) && inputReady && sessionFocused &&
                             frameState.predictedDisplayTime > 0;
    }
    bool reserveCursorSlot = interactionWillRun && maxLayers > 1;
    uint32_t maxPanelLayers = reserveCursorSlot ? (maxLayers - 1) : maxLayers;

    if (frameState.shouldRender == XR_TRUE && mainSessionVisible && hasPacket && anchored &&
        header.originRevision == originRevision)
    {
        uint32_t reserveCount = std::min(header.count, maxPanelLayers);
        quads.reserve(reserveCount + 1);
        surfaces.reserve(reserveCount);
        uint32_t submittedPanels = 0;
        for (uint32_t i = 0; i < header.count && submittedPanels < maxPanelLayers; ++i)
        {
            Descriptor d{};
            std::memcpy(&d, packetSnapshot->data() + sizeof(Header) + i * sizeof(Descriptor), sizeof(d));
            activeIds.push_back(d.id);
            if (d.alpha <= 0.0f)
                continue;
            SwapchainTexture &texture = textures[d.id];
            int32_t panelXrError = 0;
            std::string panelError;
            bool panelFatal = false;
            if (!UploadPanel(texture, d, packetSequence, packetSnapshot->data() + d.offset, panelXrError, panelError,
                             panelFatal))
            {
                if (!uploadFailed)
                {
                    uploadFailed = true;
                    uploadXrError = panelXrError;
                    uploadErrorMessage = std::move(panelError);
                }
                if (panelFatal)
                {
                    uploadFatal = true;
                    break;
                }
                continue;
            }
            if (!texture.ready)
                continue;

            XrCompositionLayerQuad quad{XR_TYPE_COMPOSITION_LAYER_QUAD};
            quad.layerFlags = XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT |
                              XR_COMPOSITION_LAYER_UNPREMULTIPLIED_ALPHA_BIT;
            quad.space = localSpace;
            quad.eyeVisibility = XR_EYE_VISIBILITY_BOTH;
            quad.subImage.swapchain = texture.handle;
            quad.subImage.imageRect.offset = {0, 0};
            quad.subImage.imageRect.extent = {static_cast<int32_t>(d.width), static_cast<int32_t>(d.height)};
            quad.subImage.imageArrayIndex = 0;
            quad.pose = Place(anchor, d);
            quad.size = {d.widthMeters, d.heightMeters};
            quads.push_back(quad);
            surfaces.push_back({d.id,
                                {{quad.pose.orientation.x, quad.pose.orientation.y, quad.pose.orientation.z,
                                  quad.pose.orientation.w},
                                 {quad.pose.position.x, quad.pose.position.y, quad.pose.position.z}},
                                d.widthMeters, d.heightMeters});
            ++submittedPanels;
        }
    }

    uint32_t panelLayerCount = static_cast<uint32_t>(quads.size());

    SampledInteraction sampled{};
    bool needsInteractionReset = uploadFatal || (interactionWillRun && maxLayers <= 1);
    if (!needsInteractionReset)
    {
        sampled = SampleInteraction(surfaces, frameState.predictedDisplayTime);
        if (!sampled.resetOnly && sampled.update.cursor && quads.size() < maxLayers)
        {
            int32_t cursorXrError = 0;
            std::string cursorError;
            bool cursorFatal = false;
            if (auto cursorQuad = BuildCursorQuad(*sampled.update.cursor, cursorXrError, cursorError, cursorFatal))
            {
                quads.push_back(*cursorQuad);
            }
            else if (cursorFatal)
            {
                uploadFailed = true;
                uploadFatal = true;
                if (uploadErrorMessage.empty())
                {
                    uploadXrError = cursorXrError;
                    uploadErrorMessage = std::move(cursorError);
                }
            }
        }
    }

    // Retire swapchains for panels no longer present unless GPU completion is unconfirmed.
    if (!uploadFatal)
    {
        for (auto it = textures.begin(); it != textures.end();)
        {
            if (std::find(activeIds.begin(), activeIds.end(), it->first) == activeIds.end())
            {
                if (!RetireSwapchain(it->second))
                {
                    uploadFailed = true;
                    uploadFatal = true;
                    if (uploadErrorMessage.empty())
                        uploadErrorMessage = "Could not confirm Vulkan GPU idle before retiring an overlay swapchain.";
                    break;
                }
                it = textures.erase(it);
            }
            else
            {
                ++it;
            }
        }
    }

    if (!uploadFatal)
    {
        for (const XrCompositionLayerQuad &quad : quads)
            layerPtrs.push_back(reinterpret_cast<const XrCompositionLayerBaseHeader *>(&quad));
    }

    XrFrameEndInfo endInfo{XR_TYPE_FRAME_END_INFO};
    endInfo.displayTime = frameState.predictedDisplayTime;
    endInfo.environmentBlendMode = blendMode;
    endInfo.layerCount = static_cast<uint32_t>(layerPtrs.size());
    endInfo.layers = layerPtrs.empty() ? nullptr : layerPtrs.data();

    res = xr.endFrame(session, &endInfo);
    if (XR_FAILED(res))
    {
        ResetInteractionState(/*forceResetEvent=*/!sampled.update.events.empty());
        ClearHeadPose();
        SetStatus(OverlayStatus::Error, static_cast<int32_t>(res), 0,
                  "OpenXR endFrame failed (OpenXR error " + std::to_string(res) + ").");
        return false;
    }

    if (uploadFatal)
    {
        ResetInteractionState(/*forceResetEvent=*/!sampled.update.events.empty());
        ClearHeadPose();
        SetStatus(OverlayStatus::Error, uploadXrError, 0,
                  uploadErrorMessage.empty() ? "Vulkan overlay upload completion could not be confirmed."
                                             : uploadErrorMessage);
        return false;
    }

    if (needsInteractionReset)
    {
        ResetInteractionState();
    }
    else if (!sampled.update.events.empty())
    {
        EnqueueResult enqueued = QueuePointerEvents(sampled.update.events, sampled.generation);
        if (enqueued == EnqueueResult::Overflow)
        {
            previousHandButtons = {0, 0};
            (void)pointer.Reset();
        }
    }

    if (uploadFailed)
    {
        SetStatus(OverlayStatus::Error, uploadXrError, panelLayerCount,
                  uploadErrorMessage.empty() ? "Could not upload an overlay panel texture." : uploadErrorMessage);
    }
    else
    {
        SetStatus(OverlayStatus::Ok, 0, panelLayerCount, "Linux OpenXR overlay session is active.");
    }
    return true;
}

} // namespace srvsurvey::linux_openxr

using namespace srvsurvey::linux_openxr;

extern "C"
{

int32_t srvsurvey_linux_openxr_probe(const char *expectedRuntime, int32_t *outStatus, int32_t *outXrError,
                                     char *outMessage, int32_t messageCapacity)
{
    try
    {
        DynamicLoaders &loaders = GetSystemLoaders();
        if (loaders.failureStatus != OverlayStatus::Ok)
        {
            if (outStatus)
                *outStatus = static_cast<int32_t>(loaders.failureStatus);
            if (outXrError)
                *outXrError = 0;
            CopyMessage(loaders.failureMessage, outMessage, messageCapacity);
            return 0;
        }

        ProbeResult result = ProbeOverlayRuntime(loaders.loader, expectedRuntime ? expectedRuntime : "");
        if (outStatus)
            *outStatus = static_cast<int32_t>(result.status);
        if (outXrError)
            *outXrError = result.xrError;
        CopyMessage(result.message, outMessage, messageCapacity);
        return result.status == OverlayStatus::Ok ? 1 : 0;
    }
    catch (const std::exception &ex)
    {
        if (outStatus)
            *outStatus = static_cast<int32_t>(OverlayStatus::Error);
        if (outXrError)
            *outXrError = 0;
        CopyExceptionMessage("Linux OpenXR probe failed: ", ex.what(), outMessage, messageCapacity);
        return 0;
    }
    catch (...)
    {
        if (outStatus)
            *outStatus = static_cast<int32_t>(OverlayStatus::Error);
        if (outXrError)
            *outXrError = 0;
        CopyMessage("Linux OpenXR probe failed unexpectedly.", outMessage, messageCapacity);
        return 0;
    }
}

void *srvsurvey_linux_openxr_create(const char *expectedRuntime, int32_t *outStatus, int32_t *outXrError,
                                    char *outMessage, int32_t messageCapacity)
{
    try
    {
        DynamicLoaders &loaders = GetSystemLoaders();
        if (loaders.failureStatus != OverlayStatus::Ok)
        {
            if (outStatus)
                *outStatus = static_cast<int32_t>(loaders.failureStatus);
            if (outXrError)
                *outXrError = 0;
            CopyMessage(loaders.failureMessage, outMessage, messageCapacity);
            return nullptr;
        }

        auto session = std::make_unique<OverlaySession>(loaders.loader, expectedRuntime ? expectedRuntime : "", true);
        ProbeResult result = session->Initialize();
        if (outStatus)
            *outStatus = static_cast<int32_t>(result.status);
        if (outXrError)
            *outXrError = result.xrError;
        CopyMessage(result.message, outMessage, messageCapacity);
        if (result.status != OverlayStatus::Ok)
            return nullptr;
        return session.release();
    }
    catch (const std::exception &ex)
    {
        if (outStatus)
            *outStatus = static_cast<int32_t>(OverlayStatus::Error);
        if (outXrError)
            *outXrError = 0;
        CopyExceptionMessage("Could not create the Linux OpenXR overlay session: ", ex.what(), outMessage,
                             messageCapacity);
        return nullptr;
    }
    catch (...)
    {
        if (outStatus)
            *outStatus = static_cast<int32_t>(OverlayStatus::Error);
        if (outXrError)
            *outXrError = 0;
        CopyMessage("Could not create the Linux OpenXR overlay session.", outMessage, messageCapacity);
        return nullptr;
    }
}

int32_t srvsurvey_linux_openxr_publish(void *handle, const uint8_t *packet, uint32_t packetLength)
{
    try
    {
        if (!handle || !packet || packetLength == 0)
            return 0;
        auto *session = static_cast<OverlaySession *>(handle);
        return session->PublishPacket({packet, packetLength}) ? 1 : 0;
    }
    catch (...)
    {
        return 0;
    }
}

int32_t srvsurvey_linux_openxr_get_status(void *handle, int32_t *outState, int32_t *outXrError,
                                          uint32_t *outRenderedCount, uint32_t *outMaxLayers, char *outMessage,
                                          int32_t messageCapacity)
{
    try
    {
        if (!handle)
            return 0;
        auto *session = static_cast<OverlaySession *>(handle);
        RuntimeStatus status = session->GetStatus();
        if (outState)
            *outState = static_cast<int32_t>(status.status);
        if (outXrError)
            *outXrError = status.xrError;
        if (outRenderedCount)
            *outRenderedCount = status.renderedCount;
        if (outMaxLayers)
            *outMaxLayers = status.maxLayers;
        CopyMessage(status.message, outMessage, messageCapacity);
        return status.status == OverlayStatus::Ok ? 1 : 0;
    }
    catch (...)
    {
        if (outState)
            *outState = static_cast<int32_t>(OverlayStatus::Error);
        if (outXrError)
            *outXrError = 0;
        CopyMessage("Could not query Linux OpenXR overlay status.", outMessage, messageCapacity);
        return 0;
    }
}

int32_t srvsurvey_linux_openxr_get_head_pose(void *handle, uint32_t expectedOriginRevision, float *outPosX,
                                             float *outPosY, float *outPosZ, float *outYaw)
{
    try
    {
        if (!handle)
            return 0;
        auto *session = static_cast<OverlaySession *>(handle);
        HeadPose pose = session->GetHeadPose(expectedOriginRevision);
        if (!pose.valid)
            return 0;
        if (outPosX)
            *outPosX = pose.position[0];
        if (outPosY)
            *outPosY = pose.position[1];
        if (outPosZ)
            *outPosZ = pose.position[2];
        if (outYaw)
            *outYaw = pose.yaw;
        return 1;
    }
    catch (...)
    {
        return 0;
    }
}

int32_t srvsurvey_linux_openxr_set_interaction_enabled(void *handle, int32_t enabled, int32_t *outXrError,
                                                       char *outMessage, int32_t messageCapacity)
{
    try
    {
        if (!handle)
        {
            if (outXrError)
                *outXrError = 0;
            CopyMessage("Linux OpenXR overlay session is not active.", outMessage, messageCapacity);
            return 0;
        }
        auto *session = static_cast<OverlaySession *>(handle);
        int32_t xrError = 0;
        std::string message;
        bool ok = session->SetInteractionEnabled(enabled != 0, xrError, message);
        if (outXrError)
            *outXrError = xrError;
        CopyMessage(message, outMessage, messageCapacity);
        return ok ? 1 : 0;
    }
    catch (const std::exception &ex)
    {
        if (outXrError)
            *outXrError = 0;
        CopyExceptionMessage("Could not update Linux OpenXR controller interaction: ", ex.what(), outMessage,
                             messageCapacity);
        return 0;
    }
    catch (...)
    {
        if (outXrError)
            *outXrError = 0;
        CopyMessage("Could not update Linux OpenXR controller interaction.", outMessage, messageCapacity);
        return 0;
    }
}

uint32_t srvsurvey_linux_openxr_poll_pointer_events(void *handle, void *outEvents, uint32_t capacity)
{
    try
    {
        if (!handle || !outEvents || capacity == 0)
            return 0;
        auto *session = static_cast<OverlaySession *>(handle);
        auto *events = static_cast<srvsurvey::PointerEvent *>(outEvents);
        return static_cast<uint32_t>(session->PollPointerEvents({events, capacity}));
    }
    catch (...)
    {
        return 0;
    }
}

void srvsurvey_linux_openxr_destroy(void *handle)
{
    try
    {
        if (!handle)
            return;
        delete static_cast<OverlaySession *>(handle);
    }
    catch (...)
    {
        // Never allow exceptions to unwind across the C ABI boundary.
    }
}

} // extern "C"
