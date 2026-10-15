# Quest 3, VDXR, and SrvSurvey overlay compatibility

Date checked: 2026-10-14

This note records the starting compatibility investigation. The experimental
implementation on this branch is described in [the VDXR setup guide](../vr-vdxr.md);
physical headset validation remains pending.

## Baseline before the experimental integration

A Quest 3 user can reasonably prefer VDXR: its developer describes a lightweight
runtime built for performance, avoiding SteamVR's additional overhead. That is
an architectural explanation, not a benchmark or proof that every configuration
is faster or sharper. Confirm the actual route with Virtual Desktop's performance
overlay: its runtime field should say `VDXR`
([VDXR wiki](https://github.com/mbucchia/VirtualDesktop-OpenXR/wiki)).

SrvSurvey currently publishes **SteamVR/OpenVR overlays**. It does not currently
publish into an Elite session running through VDXR. Supporting that session is
technically possible through an OpenXR API layer or a suitable capture bridge;
it needs additional implementation and hardware validation.

## Why the existing publisher does not follow the game

[OpenVrRuntime](../../src/SrvSurvey.Desktop/Platform/Overlay/OpenVrRuntime.cs)
initializes `VRApplication_Overlay`, creates `IVROverlay` surfaces, and uploads
pixels with `SetOverlayRaw`. These calls target the OpenVR overlay compositor
([Valve overlay documentation](https://github.com/ValveSoftware/openvr/wiki/IVROverlay_Overview)).
VDXR runs OpenXR applications without SteamVR. Merely starting SteamVR alongside
that session therefore does not insert SrvSurvey's overlays into it; that follows
from the two separate submission paths
([VDXR project](https://github.com/mbucchia/VirtualDesktop-OpenXR)).

Elite does not natively support OpenXR, according to OpenKneeboard's compatibility
documentation. OpenComposite translates OpenVR game calls to OpenXR, providing
a possible Elite → OpenComposite → VDXR route. Translating the game's calls does
not establish a bridge for another process's SteamVR overlays
([Elite compatibility](https://openkneeboard.com/compatibility/games/),
[OpenComposite README](https://gitlab.com/znixian/OpenOVR/-/blob/openxr/README.md)).

## OpenXR layers provide a feasible implementation path

Khronos documents API layers as libraries between an application's OpenXR calls
and its runtime; they can intercept, change, and insert calls
([OpenXR loader design](https://registry.khronos.org/OpenXR/specs/1.1/loader.html#api-layers)).
OpenKneeboard's developer explicitly describes intercepting `xrEndFrame` and
appending composition layers, including copying external overlay pixels into
swapchains. VDXR's source accepts `XrCompositionLayerQuad` alongside projection
layers
([OpenKneeboard developer FAQ](https://openkneeboard.com/faq/third-party-developers/),
[VDXR frame submission](https://github.com/mbucchia/VirtualDesktop-OpenXR/blob/main/virtualdesktop-openxr/frame.cpp)).

This operates within the game's session; it does not require a second independent
overlay session. Accordingly, the `XR_EXTX_overlay` prerequisite in the earlier
[modernization note](vr-overlay-modernization.md) applies to a standalone
cross-process OpenXR overlay backend, not every way to display third-party pixels
in OpenXR. A SrvSurvey API layer would still need native graphics/IPC code,
positioning and lifecycle handling, installation, compatibility checks, and tests
on the real Elite/OpenComposite/VDXR combination. It is a development option,
not shipped support.

## OpenKneeboard capture: an experiment with specific limitations

OpenKneeboard can bring an ordinary desktop window into VR through a Window
Capture tab. Its own compatibility page says Elite is **reported** to work via
OpenComposite, while explicitly declining support for OpenComposite with any
game. This makes it a concrete experimental direction, not a supported,
verified Quest 3 + Elite + VDXR + SrvSurvey recipe
([Window Capture](https://openkneeboard.com/features/window-capture/),
[game compatibility and support boundary](https://openkneeboard.com/compatibility/games/)).

SrvSurvey already has a combined capture candidate:
[StreamOverlayCoordinator](../../src/SrvSurvey.Desktop/Platform/Overlay/StreamOverlayCoordinator.cs)
composes visible panels into `SrvSurveyWindowOne`. However,
[StreamOverlayWindow](../../src/SrvSurvey.Desktop/StreamOverlayWindow.axaml)
sets `ShowInTaskbar="False"` and requests transparency. OpenKneeboard warns that
Windows capture generally requires taskbar visibility, and preserving alpha
requires DirectComposition with premultiplied RGBA. The existing window cannot
be assumed selectable, capturable, or transparent in that pipeline
([capture requirements](https://openkneeboard.com/faq/third-party-developers/)).
Captured content can also stop updating when the source framework stops rendering
an obscured window
([capture troubleshooting](https://openkneeboard.com/troubleshooting/window-capture-compatibility/)).

The next useful validation would be a capturable SrvSurvey surface, then a test
of capture selection, live updates, alpha, placement, text readability, and frame
time on the user's existing VDXR setup. No physical-headset test was performed
for this note. The shipped recommendation remains SteamVR for SrvSurvey's native
VR overlays; an API-layer integration could let users keep VDXR later.

## Native Windows D3D11 implementation contracts

- **Loader chaining:** export `xrNegotiateLoaderApiLayerInterface` using OpenXR's
  ABI; validate negotiation structures/version overlap. During instance creation,
  copy `XrApiLayerCreateInfo`, advance the copy's `nextInfo`, and call the saved
  next create function. Store next-function dispatch by instance and session;
  forward unhandled queries through the next `xrGetInstanceProcAddr`
  ([Khronos reference layer](https://github.com/KhronosGroup/OpenXR-SDK-Source/blob/main/src/api_layers/api_dump.cpp#L498-L670)).
- **Images:** use the game's `XrGraphicsBindingD3D11KHR` device. Enumerate supported
  formats and `XrSwapchainImageD3D11KHR` textures; these are default-usage resources,
  potentially typeless, requiring compatible typed views. Match upload channel
  order and color encoding. Usage flags must match the graphics operations
  ([D3D11 extension contract](https://github.com/KhronosGroup/OpenXR-Docs/blob/main/specification/sources/chapters/extensions/khr/khr_d3d11_enable.adoc),
  [VDXR allocation](https://github.com/mbucchia/VirtualDesktop-OpenXR/blob/main/virtualdesktop-openxr/swapchain.cpp)).
- **Frame submission:** acquire → wait for `XR_SUCCESS` → upload → release before
  referencing the image. `XR_TIMEOUT_EXPIRED` passes `XR_SUCCEEDED`; it does not
  grant image access. Retry the same acquisition. Previously released unchanged
  images may be reused. Append the quad each visible frame, respect
  `maxLayerCount`, preserve the game's frame metadata, and omit extra work when
  `shouldRender` is false. Set source-alpha blending; set unpremultiplied-alpha
  only for straight-alpha pixels
  ([rendering contract](https://github.com/KhronosGroup/OpenXR-Docs/blob/main/specification/sources/chapters/rendering.adoc)).
- **Lifecycle:** follow the game's session/frame loop, observe events without
  consuming them, and synchronize dispatch and resource teardown. Destroy owned
  spaces/swapchains before forwarding session destruction; session destruction
  also destroys remaining children
  ([session contract](https://github.com/KhronosGroup/OpenXR-Docs/blob/main/specification/sources/chapters/session.adoc#L291-L309)).
- **Opt-in installation:** native x64 per-user registration uses
  `HKCU\SOFTWARE\Khronos\OpenXR\1\ApiLayers\Implicit`, with absolute manifest
  path as value name and `REG_DWORD` zero. `disable_environment` is mandatory;
  optional `enable_environment` must exist in the game's inherited environment
  before initialization. Disable wins even with value `0`. Manifest
  `api_version` and `implementation_version` are strings
  ([loader discovery](https://registry.khronos.org/OpenXR/specs/1.1/loader.html#windows-manifest-registry-usage),
  [manifest parser](https://github.com/KhronosGroup/OpenXR-SDK-Source/blob/main/src/loader/manifest_file.cpp#L805-L845)).
