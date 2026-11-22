// SPDX-License-Identifier: GPL-3.0-or-later
// Native Linux OpenXR overlay runtime (XR_EXTX_overlay + headless Vulkan) for WiVRn and Monado.
#pragma once

#include "FrameProtocol.h"
#include <atomic>
#include <chrono>
#include <cstdint>
#include <cstring>
#include <functional>
#include <map>
#include <memory>
#include <mutex>
#include <optional>
#include <string>
#include <thread>
#include <vector>

#ifndef VK_NO_PROTOTYPES
#define VK_NO_PROTOTYPES
#endif
#include <vulkan/vulkan.h>

#ifndef XR_NO_PROTOTYPES
#define XR_NO_PROTOTYPES
#endif
#ifndef XR_USE_GRAPHICS_API_VULKAN
#define XR_USE_GRAPHICS_API_VULKAN
#endif
#include <openxr/openxr.h>
#include <openxr/openxr_platform.h>

namespace srvsurvey::linux_openxr
{

enum class OverlayStatus : int32_t
{
    Ok = 0,
    LoaderMissing = 1,
    VulkanMissing = 2,
    OverlayExtensionUnsupported = 3,
    GraphicsExtensionUnsupported = 4,
    WrongRuntime = 5,
    HeadsetUnavailable = 6,
    SessionLost = 7,
    Error = 8,
};

struct ProbeResult
{
    OverlayStatus status = OverlayStatus::Error;
    int32_t xrError = 0;
    std::string runtimeName;
    std::string message;
};

struct RuntimeStatus
{
    OverlayStatus status = OverlayStatus::HeadsetUnavailable;
    int32_t xrError = 0;
    uint32_t renderedCount = 0;
    uint32_t maxLayers = MaximumOverlays;
    std::string message;
};

struct HeadPose
{
    bool valid = false;
    uint32_t originRevision = 0;
    float position[3]{0, 0, 0};
    float yaw = 0;
};

struct OpenXrLoader
{
    PFN_xrGetInstanceProcAddr getInstanceProcAddr = nullptr;
    PFN_vkGetInstanceProcAddr vkGetInstanceProcAddr = nullptr;
};

struct SwapchainTexture
{
    XrSwapchain handle = XR_NULL_HANDLE;
    std::vector<XrSwapchainImageVulkanKHR> images;
    std::vector<uint8_t> pixels;
    uint32_t width = 0;
    uint32_t height = 0;
    uint32_t sourceSequence = 0;
    uint32_t acquiredIndex = 0;
    bool acquired = false;
    bool waited = false;
    bool ready = false;
};

struct StagingBuffer
{
    VkBuffer buffer = VK_NULL_HANDLE;
    VkDeviceMemory memory = VK_NULL_HANDLE;
    void *mapped = nullptr;
    VkDeviceSize capacity = 0;
    bool hostCoherent = true;
};

bool MatchesRuntimeFilter(const std::string &runtimeName, const std::string &expectedRuntime);
int64_t SelectSwapchainFormat(const std::vector<int64_t> &formats, bool &swapRedBlue);
bool IsSrgbSwapchainFormat(int64_t format);
ProbeResult ProbeOverlayRuntime(const OpenXrLoader &loader, const std::string &expectedRuntime);

class OverlaySession
{
  public:
    OverlaySession(OpenXrLoader loader, std::string expectedRuntime, bool startWorkerThread = true);
    ~OverlaySession();

    OverlaySession(const OverlaySession &) = delete;
    OverlaySession &operator=(const OverlaySession &) = delete;

    ProbeResult Initialize();
    bool PublishPacket(std::span<const uint8_t> packet);
    bool StepOnce();
    RuntimeStatus GetStatus() const;
    HeadPose GetHeadPose(uint32_t expectedOriginRevision) const;
    void Shutdown();

  private:
    struct XrFunctions
    {
        PFN_xrEnumerateInstanceExtensionProperties enumerateExtensions = nullptr;
        PFN_xrCreateInstance createInstance = nullptr;
        PFN_xrDestroyInstance destroyInstance = nullptr;
        PFN_xrGetInstanceProperties getInstanceProperties = nullptr;
        PFN_xrGetSystem getSystem = nullptr;
        PFN_xrGetSystemProperties getSystemProperties = nullptr;
        PFN_xrEnumerateEnvironmentBlendModes enumerateBlendModes = nullptr;
        PFN_xrCreateSession createSession = nullptr;
        PFN_xrDestroySession destroySession = nullptr;
        PFN_xrBeginSession beginSession = nullptr;
        PFN_xrEndSession endSession = nullptr;
        PFN_xrPollEvent pollEvent = nullptr;
        PFN_xrCreateReferenceSpace createReferenceSpace = nullptr;
        PFN_xrDestroySpace destroySpace = nullptr;
        PFN_xrLocateSpace locateSpace = nullptr;
        PFN_xrEnumerateSwapchainFormats enumerateSwapchainFormats = nullptr;
        PFN_xrCreateSwapchain createSwapchain = nullptr;
        PFN_xrDestroySwapchain destroySwapchain = nullptr;
        PFN_xrEnumerateSwapchainImages enumerateSwapchainImages = nullptr;
        PFN_xrAcquireSwapchainImage acquireSwapchainImage = nullptr;
        PFN_xrWaitSwapchainImage waitSwapchainImage = nullptr;
        PFN_xrReleaseSwapchainImage releaseSwapchainImage = nullptr;
        PFN_xrWaitFrame waitFrame = nullptr;
        PFN_xrBeginFrame beginFrame = nullptr;
        PFN_xrEndFrame endFrame = nullptr;
        PFN_xrGetVulkanGraphicsRequirements2KHR getVulkanRequirements2 = nullptr;
        PFN_xrCreateVulkanInstanceKHR createVulkanInstance = nullptr;
        PFN_xrGetVulkanGraphicsDevice2KHR getVulkanDevice2 = nullptr;
        PFN_xrCreateVulkanDeviceKHR createVulkanDevice = nullptr;
        PFN_xrGetVulkanGraphicsRequirementsKHR getVulkanRequirements1 = nullptr;
        PFN_xrGetVulkanInstanceExtensionsKHR getVulkanInstanceExtensions = nullptr;
        PFN_xrGetVulkanDeviceExtensionsKHR getVulkanDeviceExtensions = nullptr;
        PFN_xrGetVulkanGraphicsDeviceKHR getVulkanDevice1 = nullptr;
    };

    struct VkFunctions
    {
        PFN_vkCreateInstance createInstance = nullptr;
        PFN_vkDestroyInstance destroyInstance = nullptr;
        PFN_vkGetPhysicalDeviceQueueFamilyProperties getQueueFamilyProperties = nullptr;
        PFN_vkGetPhysicalDeviceMemoryProperties getMemoryProperties = nullptr;
        PFN_vkCreateDevice createDevice = nullptr;
        PFN_vkGetDeviceProcAddr getDeviceProcAddr = nullptr;
        PFN_vkDestroyDevice destroyDevice = nullptr;
        PFN_vkDeviceWaitIdle deviceWaitIdle = nullptr;
        PFN_vkGetDeviceQueue getDeviceQueue = nullptr;
        PFN_vkCreateCommandPool createCommandPool = nullptr;
        PFN_vkDestroyCommandPool destroyCommandPool = nullptr;
        PFN_vkAllocateCommandBuffers allocateCommandBuffers = nullptr;
        PFN_vkResetCommandBuffer resetCommandBuffer = nullptr;
        PFN_vkBeginCommandBuffer beginCommandBuffer = nullptr;
        PFN_vkEndCommandBuffer endCommandBuffer = nullptr;
        PFN_vkCmdPipelineBarrier cmdPipelineBarrier = nullptr;
        PFN_vkCmdCopyBufferToImage cmdCopyBufferToImage = nullptr;
        PFN_vkCreateFence createFence = nullptr;
        PFN_vkDestroyFence destroyFence = nullptr;
        PFN_vkResetFences resetFences = nullptr;
        PFN_vkWaitForFences waitForFences = nullptr;
        PFN_vkQueueSubmit queueSubmit = nullptr;
        PFN_vkCreateBuffer createBuffer = nullptr;
        PFN_vkDestroyBuffer destroyBuffer = nullptr;
        PFN_vkGetBufferMemoryRequirements getBufferMemoryRequirements = nullptr;
        PFN_vkAllocateMemory allocateMemory = nullptr;
        PFN_vkFreeMemory freeMemory = nullptr;
        PFN_vkBindBufferMemory bindBufferMemory = nullptr;
        PFN_vkMapMemory mapMemory = nullptr;
        PFN_vkUnmapMemory unmapMemory = nullptr;
        PFN_vkFlushMappedMemoryRanges flushMappedMemoryRanges = nullptr;
    };

    bool LoadInstanceFunctions();
    bool LoadVulkanInstanceFunctions();
    bool LoadVulkanDeviceFunctions();
    ProbeResult CreateVulkanContext(bool useVulkanEnable2);
    uint32_t FindMemoryType(uint32_t typeBits, VkMemoryPropertyFlags required, bool &isHostCoherent) const;
    bool EnsureStagingCapacity(VkDeviceSize requiredBytes);
    void DestroyStagingBuffer();
    void RetireSwapchain(SwapchainTexture &texture);
    void DestroySwapchains();
    void CleanupResources();
    void WorkerLoop();
    bool PollEvents();
    bool RenderFrame();
    bool EnsureSwapchain(SwapchainTexture &texture, uint32_t width, uint32_t height, int32_t &outXrError,
                         std::string &outError);
    bool UploadPanel(SwapchainTexture &texture, const Descriptor &descriptor, uint32_t sequence, const uint8_t *pixels,
                     int32_t &outXrError, std::string &outError);
    void SetStatus(OverlayStatus status, int32_t xrError, uint32_t renderedCount, const std::string &message);
    void ClearHeadPose();

    OpenXrLoader loader;
    std::string expectedRuntime;
    bool startWorkerThread;
    XrFunctions xr{};
    VkFunctions vk{};

    XrInstance instance = XR_NULL_HANDLE;
    XrSystemId systemId = XR_NULL_SYSTEM_ID;
    XrSession session = XR_NULL_HANDLE;
    XrSpace localSpace = XR_NULL_HANDLE;
    XrSpace viewSpace = XR_NULL_HANDLE;
    XrEnvironmentBlendMode blendMode = XR_ENVIRONMENT_BLEND_MODE_OPAQUE;

    VkInstance vkInstance = VK_NULL_HANDLE;
    VkPhysicalDevice vkPhysicalDevice = VK_NULL_HANDLE;
    VkDevice vkDevice = VK_NULL_HANDLE;
    VkQueue vkQueue = VK_NULL_HANDLE;
    uint32_t queueFamilyIndex = 0;
    VkCommandPool commandPool = VK_NULL_HANDLE;
    VkCommandBuffer commandBuffer = VK_NULL_HANDLE;
    VkFence uploadFence = VK_NULL_HANDLE;
    StagingBuffer staging{};

    int64_t swapchainFormat = 0;
    bool swapRedBlue = false;
    bool swapchainIsSrgb = true;
    uint32_t maxLayers = MaximumOverlays;
    std::map<uint64_t, SwapchainTexture> textures;

    bool sessionRunning = false;
    bool mainSessionVisible = true;
    bool anchored = false;
    bool hasPendingSpaceChange = false;
    XrTime pendingSpaceChange = 0;
    uint32_t originRevision = 0;
    XrPosef anchor{{0, 0, 0, 1}, {0, 0, 0}};

    mutable std::mutex packetMutex;
    std::shared_ptr<const std::vector<uint8_t>> latestPacket;
    uint32_t latestPacketSequence = 0;

    mutable std::mutex statusMutex;
    RuntimeStatus currentStatus{};
    HeadPose currentHeadPose{};

    std::atomic<bool> stopRequested{false};
    std::thread worker;
};

} // namespace srvsurvey::linux_openxr

extern "C"
{
    int32_t srvsurvey_linux_openxr_probe(const char *expectedRuntime, int32_t *outStatus, int32_t *outXrError,
                                         char *outMessage, int32_t messageCapacity);

    void *srvsurvey_linux_openxr_create(const char *expectedRuntime, int32_t *outStatus, int32_t *outXrError,
                                        char *outMessage, int32_t messageCapacity);

    int32_t srvsurvey_linux_openxr_publish(void *handle, const uint8_t *packet, uint32_t packetLength);

    int32_t srvsurvey_linux_openxr_get_status(void *handle, int32_t *outState, int32_t *outXrError,
                                              uint32_t *outRenderedCount, uint32_t *outMaxLayers, char *outMessage,
                                              int32_t messageCapacity);

    int32_t srvsurvey_linux_openxr_get_head_pose(void *handle, uint32_t expectedOriginRevision, float *outPosX,
                                                 float *outPosY, float *outPosZ, float *outYaw);

    void srvsurvey_linux_openxr_destroy(void *handle);
}
