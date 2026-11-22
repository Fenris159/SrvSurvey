// SPDX-License-Identifier: GPL-3.0-or-later
// Native Linux OpenXR overlay runtime (XR_EXTX_overlay + headless Vulkan) for WiVRn and Monado.
#include "LinuxOverlay.h"
#include <algorithm>
#include <array>
#include <cctype>
#include <cmath>
#include <dlfcn.h>
#include <sstream>

namespace srvsurvey::linux_openxr
{

namespace
{

constexpr uint32_t OverlayLayerPlacement = 1;

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

bool MatchesPixels(const SwapchainTexture &texture, const Descriptor &descriptor, const uint8_t *source,
                   bool swapRedBlue)
{
    if (texture.pixels.size() != descriptor.length)
        return false;
    if (!swapRedBlue && descriptor.alpha == 1.0f)
        return std::memcmp(texture.pixels.data(), source, descriptor.length) == 0;
    for (size_t i = 0; i < texture.pixels.size(); i += 4)
    {
        uint8_t r = swapRedBlue ? source[i + 2] : source[i];
        uint8_t g = source[i + 1];
        uint8_t b = swapRedBlue ? source[i] : source[i + 2];
        auto a = static_cast<uint8_t>(static_cast<float>(source[i + 3]) * descriptor.alpha);
        if (texture.pixels[i] != r || texture.pixels[i + 1] != g || texture.pixels[i + 2] != b ||
            texture.pixels[i + 3] != a)
            return false;
    }
    return true;
}

void CopyMessage(const std::string &source, char *destination, int32_t capacity)
{
    if (!destination || capacity <= 0)
        return;
    size_t copyLength = std::min(source.size(), static_cast<size_t>(capacity - 1));
    std::memcpy(destination, source.data(), copyLength);
    destination[copyLength] = '\0';
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
        return lowerName.find("monado") != std::string::npos || lowerName.find("wivrn") != std::string::npos;
    return lowerName.find(lowerExpected) != std::string::npos;
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

    SetStatus(OverlayStatus::Ok, 0, 0, "Linux OpenXR overlay session is active.");
    if (startWorkerThread)
    {
        stopRequested.store(false);
        worker = std::thread(&OverlaySession::WorkerLoop, this);
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
    return ok;
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
    {
        for (uint32_t i = 0; i < familyCount; ++i)
        {
            if (families[i].queueCount > 0 && (families[i].queueFlags & VK_QUEUE_TRANSFER_BIT) != 0)
            {
                queueFamilyIndex = i;
                foundQueue = true;
                break;
            }
        }
    }
    if (!foundQueue)
        return {OverlayStatus::Error, 0, {}, "No suitable Vulkan graphics or transfer queue family was found."};

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
        vk.mapMemory(vkDevice, staging.memory, 0, requiredBytes, 0, &staging.mapped) != VK_SUCCESS)
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

void OverlaySession::DestroySwapchains()
{
    if (vkDevice != VK_NULL_HANDLE && vk.deviceWaitIdle)
        vk.deviceWaitIdle(vkDevice);
    for (auto &[id, texture] : textures)
    {
        (void)id;
        if (texture.handle != XR_NULL_HANDLE && xr.destroySwapchain)
            xr.destroySwapchain(texture.handle);
    }
    textures.clear();
}

void OverlaySession::CleanupResources()
{
    DestroySwapchains();
    DestroyStagingBuffer();
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
        if (sessionRunning && xr.endSession)
            xr.endSession(session);
        xr.destroySession(session);
        session = XR_NULL_HANDLE;
    }
    sessionRunning = false;
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
    std::lock_guard lock(packetMutex);
    latestPacket.assign(packet.begin(), packet.end());
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

void OverlaySession::SetStatus(OverlayStatus status, int32_t xrError, uint32_t renderedCount, const std::string &message)
{
    std::lock_guard lock(statusMutex);
    currentStatus.status = status;
    currentStatus.xrError = xrError;
    currentStatus.renderedCount = renderedCount;
    currentStatus.maxLayers = maxLayers;
    currentStatus.message = message;
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
            SetStatus(OverlayStatus::SessionLost, static_cast<int32_t>(result), 0,
                      "The OpenXR overlay event stream failed (OpenXR error " + std::to_string(result) + ").");
            return false;
        }

        if (event.type == XR_TYPE_EVENT_DATA_INSTANCE_LOSS_PENDING)
        {
            SetStatus(OverlayStatus::SessionLost, 0, 0, "The OpenXR runtime disconnected.");
            return false;
        }
        if (event.type == XR_TYPE_EVENT_DATA_REFERENCE_SPACE_CHANGE_PENDING)
        {
            anchored = false;
        }
        else if (event.type == XR_TYPE_EVENT_DATA_MAIN_SESSION_VISIBILITY_CHANGED_EXTX)
        {
            const auto *visibility = reinterpret_cast<const XrEventDataMainSessionVisibilityChangedEXTX *>(&event);
            mainSessionVisible = visibility->visible == XR_TRUE;
        }
        else if (event.type == XR_TYPE_EVENT_DATA_SESSION_STATE_CHANGED)
        {
            const auto *stateChanged = reinterpret_cast<const XrEventDataSessionStateChanged *>(&event);
            if (stateChanged->state == XR_SESSION_STATE_READY)
            {
                XrSessionBeginInfo beginInfo{XR_TYPE_SESSION_BEGIN_INFO};
                beginInfo.primaryViewConfigurationType = XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO;
                XrResult beginRes = xr.beginSession(session, &beginInfo);
                if (XR_FAILED(beginRes))
                {
                    SetStatus(OverlayStatus::Error, static_cast<int32_t>(beginRes), 0,
                              "Could not begin the OpenXR overlay session (OpenXR error " + std::to_string(beginRes) + ").");
                    return false;
                }
                sessionRunning = true;
            }
            else if (stateChanged->state == XR_SESSION_STATE_STOPPING)
            {
                if (sessionRunning)
                {
                    xr.endSession(session);
                    sessionRunning = false;
                }
            }
            else if (stateChanged->state == XR_SESSION_STATE_EXITING ||
                     stateChanged->state == XR_SESSION_STATE_LOSS_PENDING)
            {
                SetStatus(OverlayStatus::SessionLost, 0, 0, "The OpenXR overlay session ended.");
                return false;
            }
        }
    }
}

bool OverlaySession::EnsureSwapchain(SwapchainTexture &texture, uint32_t width, uint32_t height)
{
    if (texture.handle != XR_NULL_HANDLE && (texture.width != width || texture.height != height))
    {
        if (vk.deviceWaitIdle)
            vk.deviceWaitIdle(vkDevice);
        xr.destroySwapchain(texture.handle);
        texture = {};
    }
    if (texture.handle != XR_NULL_HANDLE)
        return true;

    XrSwapchainCreateInfo info{XR_TYPE_SWAPCHAIN_CREATE_INFO};
    info.usageFlags = XR_SWAPCHAIN_USAGE_SAMPLED_BIT | XR_SWAPCHAIN_USAGE_TRANSFER_DST_BIT;
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
        SetStatus(OverlayStatus::Error, static_cast<int32_t>(res), 0,
                  "Could not create a Vulkan OpenXR swapchain (OpenXR error " + std::to_string(res) + ").");
        return false;
    }

    uint32_t imageCount = 0;
    res = xr.enumerateSwapchainImages(texture.handle, 0, &imageCount, nullptr);
    if (XR_FAILED(res) || imageCount == 0)
    {
        xr.destroySwapchain(texture.handle);
        texture = {};
        return false;
    }

    texture.images.assign(imageCount, {XR_TYPE_SWAPCHAIN_IMAGE_VULKAN_KHR});
    res = xr.enumerateSwapchainImages(texture.handle, imageCount, &imageCount,
                                      reinterpret_cast<XrSwapchainImageBaseHeader *>(texture.images.data()));
    if (XR_FAILED(res))
    {
        xr.destroySwapchain(texture.handle);
        texture = {};
        return false;
    }

    texture.width = width;
    texture.height = height;
    return true;
}

bool OverlaySession::UploadPanel(SwapchainTexture &texture, const Descriptor &descriptor, uint32_t sequence,
                                 const uint8_t *pixels)
{
    if (!EnsureSwapchain(texture, descriptor.width, descriptor.height))
        return false;

    if (texture.ready && !texture.acquired)
    {
        if (texture.sourceSequence == sequence)
            return true;
        if (MatchesPixels(texture, descriptor, pixels, swapRedBlue))
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
            return false;
        texture.acquired = true;
    }

    XrSwapchainImageWaitInfo waitInfo{XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO};
    waitInfo.timeout = 0;
    XrResult waitRes = xr.waitSwapchainImage(texture.handle, &waitInfo);
    if (waitRes == XR_TIMEOUT_EXPIRED)
        return true;
    if (XR_FAILED(waitRes) || texture.acquiredIndex >= texture.images.size())
        return false;

    texture.pixels.resize(descriptor.length);
    for (size_t i = 0; i < descriptor.length; i += 4)
    {
        texture.pixels[i] = swapRedBlue ? pixels[i + 2] : pixels[i];
        texture.pixels[i + 1] = pixels[i + 1];
        texture.pixels[i + 2] = swapRedBlue ? pixels[i] : pixels[i + 2];
        texture.pixels[i + 3] = static_cast<uint8_t>(static_cast<float>(pixels[i + 3]) * descriptor.alpha);
    }

    if (!EnsureStagingCapacity(descriptor.length))
        return false;

    std::memcpy(staging.mapped, texture.pixels.data(), descriptor.length);
    if (!staging.hostCoherent && vk.flushMappedMemoryRanges)
    {
        VkMappedMemoryRange range{};
        range.sType = VK_STRUCTURE_TYPE_MAPPED_MEMORY_RANGE;
        range.memory = staging.memory;
        range.offset = 0;
        range.size = VK_WHOLE_SIZE;
        vk.flushMappedMemoryRanges(vkDevice, 1, &range);
    }

    VkImage dstImage = texture.images[texture.acquiredIndex].image;
    vk.resetCommandBuffer(commandBuffer, 0);
    VkCommandBufferBeginInfo cmdBegin{};
    cmdBegin.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
    cmdBegin.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
    if (vk.beginCommandBuffer(commandBuffer, &cmdBegin) != VK_SUCCESS)
        return false;

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

    VkImageMemoryBarrier toShader{};
    toShader.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
    toShader.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
    toShader.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
    toShader.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    toShader.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    toShader.image = dstImage;
    toShader.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
    toShader.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
    toShader.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
    vk.cmdPipelineBarrier(commandBuffer, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT, 0, 0,
                          nullptr, 0, nullptr, 1, &toShader);

    if (vk.endCommandBuffer(commandBuffer) != VK_SUCCESS)
        return false;

    vk.resetFences(vkDevice, 1, &uploadFence);
    VkSubmitInfo submit{};
    submit.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
    submit.commandBufferCount = 1;
    submit.pCommandBuffers = &commandBuffer;
    if (vk.queueSubmit(vkQueue, 1, &submit, uploadFence) != VK_SUCCESS)
        return false;
    if (vk.waitForFences(vkDevice, 1, &uploadFence, VK_TRUE, UINT64_MAX) != VK_SUCCESS)
        return false;

    XrSwapchainImageReleaseInfo releaseInfo{XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO};
    if (XR_FAILED(xr.releaseSwapchainImage(texture.handle, &releaseInfo)))
        return false;

    texture.acquired = false;
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
        SetStatus(OverlayStatus::SessionLost, static_cast<int32_t>(res), 0,
                  "OpenXR waitFrame failed (OpenXR error " + std::to_string(res) + ").");
        return false;
    }

    XrFrameBeginInfo beginInfo{XR_TYPE_FRAME_BEGIN_INFO};
    res = xr.beginFrame(session, &beginInfo);
    if (XR_FAILED(res))
    {
        SetStatus(OverlayStatus::SessionLost, static_cast<int32_t>(res), 0,
                  "OpenXR beginFrame failed (OpenXR error " + std::to_string(res) + ").");
        return false;
    }

    std::vector<uint8_t> packetCopy;
    uint32_t packetSequence = 0;
    {
        std::lock_guard lock(packetMutex);
        packetCopy = latestPacket;
        packetSequence = latestPacketSequence;
    }

    Header header{};
    bool hasPacket = packetCopy.size() >= sizeof(Header);
    if (hasPacket)
        std::memcpy(&header, packetCopy.data(), sizeof(header));

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

    std::vector<XrCompositionLayerQuad> quads;
    std::vector<const XrCompositionLayerBaseHeader *> layerPtrs;
    std::vector<uint64_t> activeIds;

    if (frameState.shouldRender == XR_TRUE && mainSessionVisible && hasPacket && anchored)
    {
        uint32_t limit = std::min(header.count, maxLayers);
        quads.reserve(limit);
        for (uint32_t i = 0; i < limit; ++i)
        {
            Descriptor d{};
            std::memcpy(&d, packetCopy.data() + sizeof(Header) + i * sizeof(Descriptor), sizeof(d));
            activeIds.push_back(d.id);
            SwapchainTexture &texture = textures[d.id];
            if (!UploadPanel(texture, d, packetSequence, packetCopy.data() + d.offset))
                continue;
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
        }
    }

    // Retire swapchains for panels no longer present.
    for (auto it = textures.begin(); it != textures.end();)
    {
        if (std::find(activeIds.begin(), activeIds.end(), it->first) == activeIds.end())
        {
            if (it->second.handle != XR_NULL_HANDLE)
            {
                if (vk.deviceWaitIdle)
                    vk.deviceWaitIdle(vkDevice);
                xr.destroySwapchain(it->second.handle);
            }
            it = textures.erase(it);
        }
        else
        {
            ++it;
        }
    }

    for (const XrCompositionLayerQuad &quad : quads)
        layerPtrs.push_back(reinterpret_cast<const XrCompositionLayerBaseHeader *>(&quad));

    XrFrameEndInfo endInfo{XR_TYPE_FRAME_END_INFO};
    endInfo.displayTime = frameState.predictedDisplayTime;
    endInfo.environmentBlendMode = blendMode;
    endInfo.layerCount = static_cast<uint32_t>(layerPtrs.size());
    endInfo.layers = layerPtrs.empty() ? nullptr : layerPtrs.data();

    res = xr.endFrame(session, &endInfo);
    if (XR_FAILED(res))
    {
        SetStatus(OverlayStatus::Error, static_cast<int32_t>(res), 0,
                  "OpenXR endFrame failed (OpenXR error " + std::to_string(res) + ").");
        return false;
    }

    SetStatus(OverlayStatus::Ok, 0, static_cast<uint32_t>(layerPtrs.size()), "Linux OpenXR overlay session is active.");
    return true;
}

} // namespace srvsurvey::linux_openxr

using namespace srvsurvey::linux_openxr;

extern "C"
{

int32_t srvsurvey_linux_openxr_probe(const char *expectedRuntime, int32_t *outStatus, int32_t *outXrError,
                                     char *outMessage, int32_t messageCapacity)
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

void *srvsurvey_linux_openxr_create(const char *expectedRuntime, int32_t *outStatus, int32_t *outXrError,
                                    char *outMessage, int32_t messageCapacity)
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

int32_t srvsurvey_linux_openxr_publish(void *handle, const uint8_t *packet, uint32_t packetLength)
{
    if (!handle || !packet || packetLength == 0)
        return 0;
    auto *session = static_cast<OverlaySession *>(handle);
    return session->PublishPacket({packet, packetLength}) ? 1 : 0;
}

int32_t srvsurvey_linux_openxr_get_status(void *handle, int32_t *outState, int32_t *outXrError,
                                          uint32_t *outRenderedCount, uint32_t *outMaxLayers, char *outMessage,
                                          int32_t messageCapacity)
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

int32_t srvsurvey_linux_openxr_get_head_pose(void *handle, uint32_t expectedOriginRevision, float *outPosX,
                                             float *outPosY, float *outPosZ, float *outYaw)
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

void srvsurvey_linux_openxr_destroy(void *handle)
{
    if (!handle)
        return;
    delete static_cast<OverlaySession *>(handle);
}

} // extern "C"
