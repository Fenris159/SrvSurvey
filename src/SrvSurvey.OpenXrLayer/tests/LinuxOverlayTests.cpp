// SPDX-License-Identifier: GPL-3.0-or-later
#include "../LinuxOverlay.h"
#include <cmath>
#include <cstdlib>
#include <cstring>
#include <deque>
#include <iostream>
#include <vector>

#define REQUIRE(expr)                                                                                                  \
    do                                                                                                                 \
    {                                                                                                                  \
        if (!(expr))                                                                                                   \
        {                                                                                                              \
            std::cerr << "FAILED at " << __FILE__ << ":" << __LINE__ << ": " << #expr << "\n";                        \
            std::exit(1);                                                                                              \
        }                                                                                                              \
    } while (false)

using namespace srvsurvey;
using namespace srvsurvey::linux_openxr;

namespace
{

struct MockRuntime
{
    std::vector<std::string> extensions = {XR_EXTX_OVERLAY_EXTENSION_NAME, XR_KHR_VULKAN_ENABLE2_EXTENSION_NAME};
    std::string runtimeName = "Monado(XRT) by Collabora et al";
    XrResult getSystemResult = XR_SUCCESS;
    uint32_t maxLayerCount = 16;
    std::vector<int64_t> swapchainFormats = {VK_FORMAT_B8G8R8A8_SRGB};
    std::deque<XrEventDataBuffer> events;

    bool sawOverlaySessionCreateInfo = false;
    uint32_t overlayPlacement = 0;
    bool sawVulkanRequirements = false;
    bool beganSession = false;
    bool endedSession = false;
    int endFrameCount = 0;
    uint32_t lastSubmittedLayerCount = 0;
    XrCompositionLayerQuad lastSubmittedQuad{};
    int createdSwapchains = 0;
    int destroyedSwapchains = 0;
    int uploadedCopies = 0;
    std::vector<uint8_t> stagingMemory;
    XrPosef headPose{{0, 0, 0, 1}, {1.0f, 1.5f, -0.5f}};
};

MockRuntime *gMock = nullptr;

XrResult MockEnumerateExtensions(const char *, uint32_t cap, uint32_t *out, XrExtensionProperties *props)
{
    *out = static_cast<uint32_t>(gMock->extensions.size());
    if (cap == 0)
        return XR_SUCCESS;
    for (size_t i = 0; i < gMock->extensions.size() && i < cap; ++i)
    {
        props[i].type = XR_TYPE_EXTENSION_PROPERTIES;
        std::snprintf(props[i].extensionName, sizeof(props[i].extensionName), "%s", gMock->extensions[i].c_str());
        props[i].extensionVersion = 1;
    }
    return XR_SUCCESS;
}

XrResult MockCreateInstance(const XrInstanceCreateInfo *, XrInstance *out)
{
    *out = reinterpret_cast<XrInstance>(0x1001);
    return XR_SUCCESS;
}

XrResult MockDestroyInstance(XrInstance)
{
    return XR_SUCCESS;
}

XrResult MockGetInstanceProperties(XrInstance, XrInstanceProperties *props)
{
    std::snprintf(props->runtimeName, sizeof(props->runtimeName), "%s", gMock->runtimeName.c_str());
    return XR_SUCCESS;
}

XrResult MockGetSystem(XrInstance, const XrSystemGetInfo *, XrSystemId *out)
{
    if (gMock->getSystemResult != XR_SUCCESS)
        return gMock->getSystemResult;
    *out = 1;
    return XR_SUCCESS;
}

XrResult MockGetSystemProperties(XrInstance, XrSystemId, XrSystemProperties *props)
{
    props->graphicsProperties.maxLayerCount = gMock->maxLayerCount;
    return XR_SUCCESS;
}

XrResult MockEnumerateBlendModes(XrInstance, XrSystemId, XrViewConfigurationType, uint32_t cap, uint32_t *out,
                                 XrEnvironmentBlendMode *modes)
{
    *out = 1;
    if (cap > 0)
        modes[0] = XR_ENVIRONMENT_BLEND_MODE_OPAQUE;
    return XR_SUCCESS;
}

XrResult MockGetVulkanRequirements2(XrInstance, XrSystemId, XrGraphicsRequirementsVulkan2KHR *reqs)
{
    gMock->sawVulkanRequirements = true;
    reqs->minApiVersionSupported = XR_MAKE_VERSION(1, 0, 0);
    reqs->maxApiVersionSupported = XR_MAKE_VERSION(1, 3, 0);
    return XR_SUCCESS;
}

XrResult MockCreateVulkanInstance(XrInstance, const XrVulkanInstanceCreateInfoKHR *, VkInstance *out, VkResult *vkRes)
{
    *out = reinterpret_cast<VkInstance>(0x2001);
    *vkRes = VK_SUCCESS;
    return XR_SUCCESS;
}

XrResult MockGetVulkanDevice2(XrInstance, const XrVulkanGraphicsDeviceGetInfoKHR *, VkPhysicalDevice *out)
{
    *out = reinterpret_cast<VkPhysicalDevice>(0x2002);
    return XR_SUCCESS;
}

XrResult MockCreateVulkanDevice(XrInstance, const XrVulkanDeviceCreateInfoKHR *, VkDevice *out, VkResult *vkRes)
{
    *out = reinterpret_cast<VkDevice>(0x2003);
    *vkRes = VK_SUCCESS;
    return XR_SUCCESS;
}

XrResult MockGetVulkanRequirements1(XrInstance, XrSystemId, XrGraphicsRequirementsVulkanKHR *reqs)
{
    gMock->sawVulkanRequirements = true;
    reqs->minApiVersionSupported = XR_MAKE_VERSION(1, 0, 0);
    reqs->maxApiVersionSupported = XR_MAKE_VERSION(1, 3, 0);
    return XR_SUCCESS;
}

XrResult MockGetVulkanInstanceExtensions(XrInstance, XrSystemId, uint32_t cap, uint32_t *out, char *buf)
{
    const char *exts = "VK_KHR_external_memory_capabilities";
    *out = static_cast<uint32_t>(std::strlen(exts) + 1);
    if (cap >= *out)
        std::memcpy(buf, exts, *out);
    return XR_SUCCESS;
}

XrResult MockGetVulkanDeviceExtensions(XrInstance, XrSystemId, uint32_t cap, uint32_t *out, char *buf)
{
    const char *exts = "VK_KHR_external_memory";
    *out = static_cast<uint32_t>(std::strlen(exts) + 1);
    if (cap >= *out)
        std::memcpy(buf, exts, *out);
    return XR_SUCCESS;
}

XrResult MockGetVulkanDevice1(XrInstance, XrSystemId, VkInstance, VkPhysicalDevice *out)
{
    *out = reinterpret_cast<VkPhysicalDevice>(0x2002);
    return XR_SUCCESS;
}

XrResult MockCreateSession(XrInstance, const XrSessionCreateInfo *info, XrSession *out)
{
    const auto *node = reinterpret_cast<const XrBaseInStructure *>(info->next);
    while (node)
    {
        if (node->type == XR_TYPE_SESSION_CREATE_INFO_OVERLAY_EXTX)
        {
            const auto *overlay = reinterpret_cast<const XrSessionCreateInfoOverlayEXTX *>(node);
            gMock->sawOverlaySessionCreateInfo = true;
            gMock->overlayPlacement = overlay->sessionLayersPlacement;
        }
        node = node->next;
    }
    *out = reinterpret_cast<XrSession>(0x1002);
    return XR_SUCCESS;
}

XrResult MockDestroySession(XrSession)
{
    return XR_SUCCESS;
}

XrResult MockBeginSession(XrSession, const XrSessionBeginInfo *)
{
    gMock->beganSession = true;
    return XR_SUCCESS;
}

XrResult MockEndSession(XrSession)
{
    gMock->endedSession = true;
    return XR_SUCCESS;
}

XrResult MockPollEvent(XrInstance, XrEventDataBuffer *out)
{
    if (gMock->events.empty())
        return XR_EVENT_UNAVAILABLE;
    *out = gMock->events.front();
    gMock->events.pop_front();
    return XR_SUCCESS;
}

XrResult MockCreateReferenceSpace(XrSession, const XrReferenceSpaceCreateInfo *info, XrSpace *out)
{
    *out = reinterpret_cast<XrSpace>(info->referenceSpaceType == XR_REFERENCE_SPACE_TYPE_LOCAL ? 0x1003 : 0x1004);
    return XR_SUCCESS;
}

XrResult MockDestroySpace(XrSpace)
{
    return XR_SUCCESS;
}

XrResult MockLocateSpace(XrSpace, XrSpace, XrTime, XrSpaceLocation *loc)
{
    loc->locationFlags = XR_SPACE_LOCATION_POSITION_VALID_BIT | XR_SPACE_LOCATION_ORIENTATION_VALID_BIT;
    loc->pose = gMock->headPose;
    return XR_SUCCESS;
}

XrResult MockEnumerateSwapchainFormats(XrSession, uint32_t cap, uint32_t *out, int64_t *formats)
{
    *out = static_cast<uint32_t>(gMock->swapchainFormats.size());
    if (cap == 0)
        return XR_SUCCESS;
    for (size_t i = 0; i < gMock->swapchainFormats.size() && i < cap; ++i)
        formats[i] = gMock->swapchainFormats[i];
    return XR_SUCCESS;
}

XrResult MockCreateSwapchain(XrSession, const XrSwapchainCreateInfo *, XrSwapchain *out)
{
    ++gMock->createdSwapchains;
    *out = reinterpret_cast<XrSwapchain>(0x3000 + gMock->createdSwapchains);
    return XR_SUCCESS;
}

XrResult MockDestroySwapchain(XrSwapchain)
{
    ++gMock->destroyedSwapchains;
    return XR_SUCCESS;
}

XrResult MockEnumerateSwapchainImages(XrSwapchain, uint32_t cap, uint32_t *out, XrSwapchainImageBaseHeader *images)
{
    *out = 1;
    if (cap > 0)
    {
        auto *vkImg = reinterpret_cast<XrSwapchainImageVulkanKHR *>(images);
        vkImg[0].type = XR_TYPE_SWAPCHAIN_IMAGE_VULKAN_KHR;
        vkImg[0].image = reinterpret_cast<VkImage>(0x4001);
    }
    return XR_SUCCESS;
}

XrResult MockAcquireSwapchainImage(XrSwapchain, const XrSwapchainImageAcquireInfo *, uint32_t *index)
{
    *index = 0;
    return XR_SUCCESS;
}

XrResult MockWaitSwapchainImage(XrSwapchain, const XrSwapchainImageWaitInfo *)
{
    return XR_SUCCESS;
}

XrResult MockReleaseSwapchainImage(XrSwapchain, const XrSwapchainImageReleaseInfo *)
{
    return XR_SUCCESS;
}

XrResult MockWaitFrame(XrSession, const XrFrameWaitInfo *, XrFrameState *state)
{
    state->predictedDisplayTime = 1000;
    state->predictedDisplayPeriod = 11111111;
    state->shouldRender = XR_TRUE;
    return XR_SUCCESS;
}

XrResult MockBeginFrame(XrSession, const XrFrameBeginInfo *)
{
    return XR_SUCCESS;
}

XrResult MockEndFrame(XrSession, const XrFrameEndInfo *info)
{
    ++gMock->endFrameCount;
    gMock->lastSubmittedLayerCount = info->layerCount;
    if (info->layerCount > 0 && info->layers[0]->type == XR_TYPE_COMPOSITION_LAYER_QUAD)
        gMock->lastSubmittedQuad = *reinterpret_cast<const XrCompositionLayerQuad *>(info->layers[0]);
    return XR_SUCCESS;
}

XrResult MockXrGetInstanceProcAddr(XrInstance, const char *name, PFN_xrVoidFunction *fn)
{
    auto match = [&](const char *expected, auto ptr) -> bool {
        if (std::strcmp(name, expected) == 0)
        {
            *fn = reinterpret_cast<PFN_xrVoidFunction>(ptr);
            return true;
        }
        return false;
    };
    if (match("xrEnumerateInstanceExtensionProperties", MockEnumerateExtensions) ||
        match("xrCreateInstance", MockCreateInstance) || match("xrDestroyInstance", MockDestroyInstance) ||
        match("xrGetInstanceProperties", MockGetInstanceProperties) || match("xrGetSystem", MockGetSystem) ||
        match("xrGetSystemProperties", MockGetSystemProperties) ||
        match("xrEnumerateEnvironmentBlendModes", MockEnumerateBlendModes) ||
        match("xrCreateSession", MockCreateSession) || match("xrDestroySession", MockDestroySession) ||
        match("xrBeginSession", MockBeginSession) || match("xrEndSession", MockEndSession) ||
        match("xrPollEvent", MockPollEvent) || match("xrCreateReferenceSpace", MockCreateReferenceSpace) ||
        match("xrDestroySpace", MockDestroySpace) || match("xrLocateSpace", MockLocateSpace) ||
        match("xrEnumerateSwapchainFormats", MockEnumerateSwapchainFormats) ||
        match("xrCreateSwapchain", MockCreateSwapchain) || match("xrDestroySwapchain", MockDestroySwapchain) ||
        match("xrEnumerateSwapchainImages", MockEnumerateSwapchainImages) ||
        match("xrAcquireSwapchainImage", MockAcquireSwapchainImage) ||
        match("xrWaitSwapchainImage", MockWaitSwapchainImage) ||
        match("xrReleaseSwapchainImage", MockReleaseSwapchainImage) || match("xrWaitFrame", MockWaitFrame) ||
        match("xrBeginFrame", MockBeginFrame) || match("xrEndFrame", MockEndFrame) ||
        match("xrGetVulkanGraphicsRequirements2KHR", MockGetVulkanRequirements2) ||
        match("xrCreateVulkanInstanceKHR", MockCreateVulkanInstance) ||
        match("xrGetVulkanGraphicsDevice2KHR", MockGetVulkanDevice2) ||
        match("xrCreateVulkanDeviceKHR", MockCreateVulkanDevice) ||
        match("xrGetVulkanGraphicsRequirementsKHR", MockGetVulkanRequirements1) ||
        match("xrGetVulkanInstanceExtensionsKHR", MockGetVulkanInstanceExtensions) ||
        match("xrGetVulkanDeviceExtensionsKHR", MockGetVulkanDeviceExtensions) ||
        match("xrGetVulkanGraphicsDeviceKHR", MockGetVulkanDevice1))
        return XR_SUCCESS;
    *fn = nullptr;
    return XR_ERROR_FUNCTION_UNSUPPORTED;
}

VkResult MockVkCreateInstance(const VkInstanceCreateInfo *, const VkAllocationCallbacks *, VkInstance *out)
{
    *out = reinterpret_cast<VkInstance>(0x2001);
    return VK_SUCCESS;
}
void MockVkDestroyInstance(VkInstance, const VkAllocationCallbacks *) {}
void MockVkGetQueueFamilyProperties(VkPhysicalDevice, uint32_t *count, VkQueueFamilyProperties *props)
{
    *count = 1;
    if (props)
    {
        props[0].queueCount = 1;
        props[0].queueFlags = VK_QUEUE_GRAPHICS_BIT | VK_QUEUE_TRANSFER_BIT;
    }
}
void MockVkGetMemoryProperties(VkPhysicalDevice, VkPhysicalDeviceMemoryProperties *props)
{
    props->memoryTypeCount = 1;
    props->memoryTypes[0].propertyFlags = VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT;
}
VkResult MockVkCreateDevice(VkPhysicalDevice, const VkDeviceCreateInfo *, const VkAllocationCallbacks *, VkDevice *out)
{
    *out = reinterpret_cast<VkDevice>(0x2003);
    return VK_SUCCESS;
}
void MockVkDestroyDevice(VkDevice, const VkAllocationCallbacks *) {}
VkResult MockVkDeviceWaitIdle(VkDevice) { return VK_SUCCESS; }
void MockVkGetDeviceQueue(VkDevice, uint32_t, uint32_t, VkQueue *out) { *out = reinterpret_cast<VkQueue>(0x2004); }
VkResult MockVkCreateCommandPool(VkDevice, const VkCommandPoolCreateInfo *, const VkAllocationCallbacks *,
                                 VkCommandPool *out)
{
    *out = reinterpret_cast<VkCommandPool>(0x2005);
    return VK_SUCCESS;
}
void MockVkDestroyCommandPool(VkDevice, VkCommandPool, const VkAllocationCallbacks *) {}
VkResult MockVkAllocateCommandBuffers(VkDevice, const VkCommandBufferAllocateInfo *, VkCommandBuffer *out)
{
    *out = reinterpret_cast<VkCommandBuffer>(0x2006);
    return VK_SUCCESS;
}
VkResult MockVkResetCommandBuffer(VkCommandBuffer, VkCommandBufferResetFlags) { return VK_SUCCESS; }
VkResult MockVkBeginCommandBuffer(VkCommandBuffer, const VkCommandBufferBeginInfo *) { return VK_SUCCESS; }
VkResult MockVkEndCommandBuffer(VkCommandBuffer) { return VK_SUCCESS; }
void MockVkCmdPipelineBarrier(VkCommandBuffer, VkPipelineStageFlags, VkPipelineStageFlags, VkDependencyFlags, uint32_t,
                              const VkMemoryBarrier *, uint32_t, const VkBufferMemoryBarrier *, uint32_t,
                              const VkImageMemoryBarrier *)
{
}
void MockVkCmdCopyBufferToImage(VkCommandBuffer, VkBuffer, VkImage, VkImageLayout, uint32_t, const VkBufferImageCopy *)
{
    ++gMock->uploadedCopies;
}
VkResult MockVkCreateFence(VkDevice, const VkFenceCreateInfo *, const VkAllocationCallbacks *, VkFence *out)
{
    *out = reinterpret_cast<VkFence>(0x2007);
    return VK_SUCCESS;
}
void MockVkDestroyFence(VkDevice, VkFence, const VkAllocationCallbacks *) {}
VkResult MockVkResetFences(VkDevice, uint32_t, const VkFence *) { return VK_SUCCESS; }
VkResult MockVkWaitForFences(VkDevice, uint32_t, const VkFence *, VkBool32, uint64_t) { return VK_SUCCESS; }
VkResult MockVkQueueSubmit(VkQueue, uint32_t, const VkSubmitInfo *, VkFence) { return VK_SUCCESS; }
VkResult MockVkCreateBuffer(VkDevice, const VkBufferCreateInfo *info, const VkAllocationCallbacks *, VkBuffer *out)
{
    gMock->stagingMemory.resize(static_cast<size_t>(info->size));
    *out = reinterpret_cast<VkBuffer>(0x2008);
    return VK_SUCCESS;
}
void MockVkDestroyBuffer(VkDevice, VkBuffer, const VkAllocationCallbacks *) {}
void MockVkGetBufferMemoryRequirements(VkDevice, VkBuffer, VkMemoryRequirements *reqs)
{
    reqs->size = gMock->stagingMemory.size();
    reqs->alignment = 4;
    reqs->memoryTypeBits = 1;
}
VkResult MockVkAllocateMemory(VkDevice, const VkMemoryAllocateInfo *, const VkAllocationCallbacks *,
                              VkDeviceMemory *out)
{
    *out = reinterpret_cast<VkDeviceMemory>(0x2009);
    return VK_SUCCESS;
}
void MockVkFreeMemory(VkDevice, VkDeviceMemory, const VkAllocationCallbacks *) {}
VkResult MockVkBindBufferMemory(VkDevice, VkBuffer, VkDeviceMemory, VkDeviceSize) { return VK_SUCCESS; }
VkResult MockVkMapMemory(VkDevice, VkDeviceMemory, VkDeviceSize, VkDeviceSize, VkMemoryMapFlags, void **ppData)
{
    *ppData = gMock->stagingMemory.data();
    return VK_SUCCESS;
}
void MockVkUnmapMemory(VkDevice, VkDeviceMemory) {}

PFN_vkVoidFunction MockVkGetDeviceProcAddr(VkDevice, const char *name)
{
    auto match = [&](const char *expected, auto ptr) -> PFN_vkVoidFunction {
        return std::strcmp(name, expected) == 0 ? reinterpret_cast<PFN_vkVoidFunction>(ptr) : nullptr;
    };
    if (auto p = match("vkDestroyDevice", MockVkDestroyDevice)) return p;
    if (auto p = match("vkDeviceWaitIdle", MockVkDeviceWaitIdle)) return p;
    if (auto p = match("vkGetDeviceQueue", MockVkGetDeviceQueue)) return p;
    if (auto p = match("vkCreateCommandPool", MockVkCreateCommandPool)) return p;
    if (auto p = match("vkDestroyCommandPool", MockVkDestroyCommandPool)) return p;
    if (auto p = match("vkAllocateCommandBuffers", MockVkAllocateCommandBuffers)) return p;
    if (auto p = match("vkResetCommandBuffer", MockVkResetCommandBuffer)) return p;
    if (auto p = match("vkBeginCommandBuffer", MockVkBeginCommandBuffer)) return p;
    if (auto p = match("vkEndCommandBuffer", MockVkEndCommandBuffer)) return p;
    if (auto p = match("vkCmdPipelineBarrier", MockVkCmdPipelineBarrier)) return p;
    if (auto p = match("vkCmdCopyBufferToImage", MockVkCmdCopyBufferToImage)) return p;
    if (auto p = match("vkCreateFence", MockVkCreateFence)) return p;
    if (auto p = match("vkDestroyFence", MockVkDestroyFence)) return p;
    if (auto p = match("vkResetFences", MockVkResetFences)) return p;
    if (auto p = match("vkWaitForFences", MockVkWaitForFences)) return p;
    if (auto p = match("vkQueueSubmit", MockVkQueueSubmit)) return p;
    if (auto p = match("vkCreateBuffer", MockVkCreateBuffer)) return p;
    if (auto p = match("vkDestroyBuffer", MockVkDestroyBuffer)) return p;
    if (auto p = match("vkGetBufferMemoryRequirements", MockVkGetBufferMemoryRequirements)) return p;
    if (auto p = match("vkAllocateMemory", MockVkAllocateMemory)) return p;
    if (auto p = match("vkFreeMemory", MockVkFreeMemory)) return p;
    if (auto p = match("vkBindBufferMemory", MockVkBindBufferMemory)) return p;
    if (auto p = match("vkMapMemory", MockVkMapMemory)) return p;
    if (auto p = match("vkUnmapMemory", MockVkUnmapMemory)) return p;
    return nullptr;
}

PFN_vkVoidFunction MockVkGetInstanceProcAddr(VkInstance, const char *name)
{
    auto match = [&](const char *expected, auto ptr) -> PFN_vkVoidFunction {
        return std::strcmp(name, expected) == 0 ? reinterpret_cast<PFN_vkVoidFunction>(ptr) : nullptr;
    };
    if (auto p = match("vkCreateInstance", MockVkCreateInstance)) return p;
    if (auto p = match("vkDestroyInstance", MockVkDestroyInstance)) return p;
    if (auto p = match("vkGetPhysicalDeviceQueueFamilyProperties", MockVkGetQueueFamilyProperties)) return p;
    if (auto p = match("vkGetPhysicalDeviceMemoryProperties", MockVkGetMemoryProperties)) return p;
    if (auto p = match("vkCreateDevice", MockVkCreateDevice)) return p;
    if (auto p = match("vkGetDeviceProcAddr", MockVkGetDeviceProcAddr)) return p;
    return nullptr;
}

OpenXrLoader MakeMockLoader()
{
    return {MockXrGetInstanceProcAddr, MockVkGetInstanceProcAddr};
}

std::vector<uint8_t> BuildPacket(uint32_t originRevision, uint64_t id, uint32_t width, uint32_t height, float alpha)
{
    uint32_t pixelBytes = width * height * 4;
    uint32_t total = sizeof(Header) + sizeof(Descriptor) + pixelBytes;
    std::vector<uint8_t> packet(total, 0);
    Header header{};
    header.magic = Magic;
    header.version = Version;
    header.length = total;
    header.count = 1;
    header.originRevision = originRevision;
    std::memcpy(packet.data(), &header, sizeof(header));

    Descriptor d{};
    d.id = id;
    d.width = width;
    d.height = height;
    d.offset = sizeof(Header) + sizeof(Descriptor);
    d.length = pixelBytes;
    d.widthMeters = 0.8f;
    d.heightMeters = 0.4f;
    d.position[0] = 0.0f;
    d.position[1] = 0.2f;
    d.position[2] = -1.0f;
    d.orientation[3] = 1.0f;
    d.alpha = alpha;
    std::memcpy(packet.data() + sizeof(Header), &d, sizeof(d));

    for (uint32_t i = 0; i < pixelBytes; i += 4)
    {
        packet[d.offset + i] = 200;     // R
        packet[d.offset + i + 1] = 100; // G
        packet[d.offset + i + 2] = 50;  // B
        packet[d.offset + i + 3] = 250; // A
    }
    return packet;
}

std::vector<uint8_t> BuildEmptyPacket(uint32_t originRevision)
{
    std::vector<uint8_t> packet(sizeof(Header), 0);
    Header header{};
    header.magic = Magic;
    header.version = Version;
    header.length = sizeof(Header);
    header.count = 0;
    header.originRevision = originRevision;
    std::memcpy(packet.data(), &header, sizeof(header));
    return packet;
}

void PushSessionState(XrSessionState state)
{
    XrEventDataBuffer buf{XR_TYPE_EVENT_DATA_SESSION_STATE_CHANGED};
    auto *ev = reinterpret_cast<XrEventDataSessionStateChanged *>(&buf);
    ev->type = XR_TYPE_EVENT_DATA_SESSION_STATE_CHANGED;
    ev->state = state;
    gMock->events.push_back(buf);
}

void TestProbeContracts()
{
    MockRuntime mock;
    gMock = &mock;
    OpenXrLoader loader = MakeMockLoader();

    // Missing XR_EXTX_overlay
    mock.extensions = {XR_KHR_VULKAN_ENABLE2_EXTENSION_NAME};
    REQUIRE(ProbeOverlayRuntime(loader, "wivrn").status == OverlayStatus::OverlayExtensionUnsupported);

    // Missing Vulkan graphics extension
    mock.extensions = {XR_EXTX_OVERLAY_EXTENSION_NAME};
    REQUIRE(ProbeOverlayRuntime(loader, "wivrn").status == OverlayStatus::GraphicsExtensionUnsupported);

    // SteamVR rejected for Linux OpenXR overlay route
    mock.extensions = {XR_EXTX_OVERLAY_EXTENSION_NAME, XR_KHR_VULKAN_ENABLE2_EXTENSION_NAME};
    mock.runtimeName = "SteamVR/OpenXR";
    REQUIRE(ProbeOverlayRuntime(loader, "wivrn").status == OverlayStatus::WrongRuntime);

    // Headset unavailable
    mock.runtimeName = "WiVRn";
    mock.getSystemResult = XR_ERROR_FORM_FACTOR_UNAVAILABLE;
    REQUIRE(ProbeOverlayRuntime(loader, "wivrn").status == OverlayStatus::HeadsetUnavailable);

    // Ready on WiVRn / Monado
    mock.getSystemResult = XR_SUCCESS;
    REQUIRE(ProbeOverlayRuntime(loader, "wivrn").status == OverlayStatus::Ok);
    mock.runtimeName = "Monado(XRT) by Collabora et al";
    REQUIRE(ProbeOverlayRuntime(loader, "wivrn").status == OverlayStatus::Ok);
    REQUIRE(ProbeOverlayRuntime(loader, "monado").status == OverlayStatus::Ok);
}

void TestOverlaySessionLifecycleAndBgraSwizzle()
{
    MockRuntime mock;
    gMock = &mock;
    OpenXrLoader loader = MakeMockLoader();

    OverlaySession session(loader, "wivrn", false);
    ProbeResult init = session.Initialize();
    REQUIRE(init.status == OverlayStatus::Ok);
    REQUIRE(mock.sawOverlaySessionCreateInfo);
    REQUIRE(mock.overlayPlacement == 1);
    REQUIRE(mock.sawVulkanRequirements);

    PushSessionState(XR_SESSION_STATE_READY);
    REQUIRE(session.PublishPacket(BuildPacket(0, 42, 2, 1, 0.5f)));
    REQUIRE(session.StepOnce());
    REQUIRE(mock.beganSession);
    REQUIRE(mock.endFrameCount == 1);
    REQUIRE(mock.lastSubmittedLayerCount == 1);
    REQUIRE(mock.uploadedCopies == 1);
    REQUIRE(mock.lastSubmittedQuad.size.width == 0.8f);
    REQUIRE(mock.lastSubmittedQuad.size.height == 0.4f);
    REQUIRE((mock.lastSubmittedQuad.layerFlags & XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT) != 0);
    REQUIRE((mock.lastSubmittedQuad.layerFlags & XR_COMPOSITION_LAYER_UNPREMULTIPLIED_ALPHA_BIT) != 0);

    // Verify BGRA swizzle + alpha scaling: source (200, 100, 50, 250) * 0.5 -> (50, 100, 200, 125).
    REQUIRE(mock.stagingMemory.size() >= 4);
    REQUIRE(mock.stagingMemory[0] == 50);
    REQUIRE(mock.stagingMemory[1] == 100);
    REQUIRE(mock.stagingMemory[2] == 200);
    REQUIRE(mock.stagingMemory[3] == 125);

    // Unchanged frame skips upload.
    REQUIRE(session.StepOnce());
    REQUIRE(mock.uploadedCopies == 1);

    // Head pose matches current origin revision and invalidates on origin reset.
    HeadPose pose = session.GetHeadPose(0);
    REQUIRE(pose.valid);
    REQUIRE(std::abs(pose.position[0]) < 1e-4f);
    REQUIRE(!session.GetHeadPose(1).valid);

    // Origin reset re-anchors on next frame.
    mock.headPose.position.x = 2.0f;
    REQUIRE(session.PublishPacket(BuildPacket(1, 42, 2, 1, 1.0f)));
    REQUIRE(session.StepOnce());
    REQUIRE(session.GetHeadPose(1).valid);

    // Removing overlay destroys swapchain cleanly.
    REQUIRE(session.PublishPacket(BuildEmptyPacket(1)));
    REQUIRE(session.StepOnce());
    REQUIRE(mock.lastSubmittedLayerCount == 0);
    REQUIRE(mock.destroyedSwapchains == 1);

    // Main session hidden suppresses quads cleanly.
    REQUIRE(session.PublishPacket(BuildPacket(1, 42, 2, 1, 1.0f)));
    XrEventDataBuffer visBuf{XR_TYPE_EVENT_DATA_MAIN_SESSION_VISIBILITY_CHANGED_EXTX};
    auto *vis = reinterpret_cast<XrEventDataMainSessionVisibilityChangedEXTX *>(&visBuf);
    vis->type = XR_TYPE_EVENT_DATA_MAIN_SESSION_VISIBILITY_CHANGED_EXTX;
    vis->visible = XR_FALSE;
    mock.events.push_back(visBuf);
    REQUIRE(session.StepOnce());
    REQUIRE(mock.lastSubmittedLayerCount == 0);

    // Session loss ends loop cleanly.
    PushSessionState(XR_SESSION_STATE_LOSS_PENDING);
    REQUIRE(!session.StepOnce());
    REQUIRE(session.GetStatus().status == OverlayStatus::SessionLost);

    session.Shutdown();
    REQUIRE(mock.endedSession);
}

void TestVulkanEnable1Fallback()
{
    MockRuntime mock;
    mock.extensions = {XR_EXTX_OVERLAY_EXTENSION_NAME, XR_KHR_VULKAN_ENABLE_EXTENSION_NAME};
    mock.swapchainFormats = {VK_FORMAT_R8G8B8A8_SRGB};
    gMock = &mock;

    OverlaySession session(MakeMockLoader(), "monado", false);
    REQUIRE(session.Initialize().status == OverlayStatus::Ok);
    REQUIRE(mock.sawOverlaySessionCreateInfo);
    REQUIRE(mock.sawVulkanRequirements);

    PushSessionState(XR_SESSION_STATE_READY);
    REQUIRE(session.PublishPacket(BuildPacket(0, 7, 1, 1, 1.0f)));
    REQUIRE(session.StepOnce());
    REQUIRE(mock.stagingMemory[0] == 200);
    REQUIRE(mock.stagingMemory[1] == 100);
    REQUIRE(mock.stagingMemory[2] == 50);
    REQUIRE(mock.stagingMemory[3] == 250);
    session.Shutdown();
}

} // namespace

int main()
{
    TestProbeContracts();
    TestOverlaySessionLifecycleAndBgraSwizzle();
    TestVulkanEnable1Fallback();
    std::cout << "All LinuxOpenXrOverlay tests passed.\n";
    return 0;
}
