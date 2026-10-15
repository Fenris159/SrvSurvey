# Quest 3, VDXR, and SrvSurvey overlay compatibility

Date checked: 2026-10-14

This note expands the source review for [PR #223](https://github.com/Fenris159/SrvSurvey/pull/223).
The branch implements a Windows x64 Direct3D 11 OpenXR API layer that appends
SrvSurvey panels to Elite's VDXR session. Setup is in [the VDXR guide](../vr-vdxr.md).
Source compatibility is established for the contracts below; a real Quest 3,
Elite, Virtual Desktop, and SrvSurvey session still needs validation. Passing a
mock-runtime test does not prove headset readability, driver timing, or game
compatibility.

## Findings

- **VDXR provides the required rendering features.** Its developer documents
  Direct3D 11, quad layers, and Oculus Touch motion-controller support
  ([VDXR developer features](https://github.com/mbucchia/VirtualDesktop-OpenXR/wiki/Developers)).
- **The panel design is shared.** VR renders the same registered Avalonia panel
  visual used by desktop presentation into a texture. VDXR receives a flat
  rectangle with preserved image aspect ratio. There is no separate VDXR theme
  or curved-panel conversion in this implementation.
- **The VR interaction shortcut does not enable interaction on the VDXR route.**
  The shortcut reaches the backend, which currently returns an unsupported
  result. Rendering support alone does not provide controller pointer events.
- **The remaining end-to-end check is physical.** The source review supports the
  chosen integration, but cannot guarantee the user's installed runtime,
  translation layer, drivers, and headset behave identically to the reviewed
  sources.

## Source versions and evidence limits

The source review uses VirtualDesktop-OpenXR commit
[`6da20fe478e331c05672243fd82a9a1500cd39f5`](https://github.com/mbucchia/VirtualDesktop-OpenXR/tree/6da20fe478e331c05672243fd82a9a1500cd39f5),
OpenComposite's `openxr` branch at
[`cff07db75c4823afe93ed7027b03d5f7bc86f164`](https://gitlab.com/znixian/OpenOVR/-/tree/cff07db75c4823afe93ed7027b03d5f7bc86f164),
and Khronos OpenXR specification sources at
[`5a82d45bc9e0adbaa24b6c4a3e5b6d50e08c887d`](https://github.com/KhronosGroup/OpenXR-Docs/tree/5a82d45bc9e0adbaa24b6c4a3e5b6d50e08c887d).
The native build pins SDK release 1.1.63. The VDXR binary shipped in the user's
Virtual Desktop Streamer package may differ from the public commit; record the
actual installed versions during validation. Wiki pages are maintained first-party
documentation and may change independently of the source snapshots.

## Runtime route and activation

VDXR runs OpenXR applications through Virtual Desktop without SteamVR and lists
Quest 3 as supported. Its performance focus makes the user's preference
plausible; it is not a measured guarantee of better frame time or image quality.
Confirm the actual game's runtime using Virtual Desktop's performance overlay:
its Runtime field must read `VDXR`
([VDXR overview and setup](https://github.com/mbucchia/VirtualDesktop-OpenXR/wiki)).

OpenComposite implements the game's OpenVR API and forwards to OpenXR. The
SrvSurvey layer is inserted between those OpenXR calls and VDXR:

```text
Elite OpenVR calls → OpenComposite → OpenXR loader → SrvSurvey API layer → VDXR
                                                   ↑
                                   SrvSurvey panel pixels and calibration
```

This is an integration into the game's existing session. SrvSurvey does not
switch its separate OpenVR overlay process to OpenComposite. The latter would
not establish cross-process overlay submission
([OpenComposite architecture](https://gitlab.com/znixian/OpenOVR/-/blob/cff07db75c4823afe93ed7027b03d5f7bc86f164/README.md)).

OpenComposite initially creates a temporary graphics session, potentially Vulkan,
and recreates it using the game's D3D11 texture device when DirectX frames arrive.
An initial unsupported-graphics status therefore need not describe the final
rendering session. Its frame path submits an opaque environment and the game's
composition layers
([graphics transition and submission](https://gitlab.com/znixian/OpenOVR/-/blob/cff07db75c4823afe93ed7027b03d5f7bc86f164/DrvOpenXR/XrBackend.cpp),
[temporary session setup](https://gitlab.com/znixian/OpenOVR/-/blob/cff07db75c4823afe93ed7027b03d5f7bc86f164/DrvOpenXR/DrvOpenXR.cpp)).
That establishes a compatible code path, not a certification of every Elite
build. Preserve the user's working Elite-to-VDXR setup and record its exact
OpenComposite version rather than replacing it speculatively.

Khronos allows API layers to intercept, modify, and insert OpenXR calls. Layers
load when the game creates its OpenXR instance. The per-user installer registers
an absolute manifest path in the native 64-bit Windows implicit-layer registry;
the manifest has a disable environment variable. Restart Elite after installing,
moving, or updating that registration. The existing UI route dropdown selects
the publisher; it does not install the native layer
([loader model and Windows discovery](https://registry.khronos.org/OpenXR/specs/1.1/loader.html#api-layers),
[installer](../../src/SrvSurvey.OpenXrLayer/Install-OpenXrLayer.ps1)).

The reviewed OpenComposite build statically compiles its bundled OpenXR loader
by default (`USE_SYSTEM_OPENXR=OFF`). Its pinned SDK revision
[`91a8a8d9d70f4b469bca0726122c3b5a6096010e`](https://github.com/KhronosGroup/OpenXR-SDK/tree/91a8a8d9d70f4b469bca0726122c3b5a6096010e)
does discover HKCU implicit layers during an ordinary launch, but intentionally
skips HKCU when the process integrity level is above medium. **Launch Elite
normally, without Run as administrator**, including its launcher. Running the
per-user registration script as administrator does not change that loader rule
([OpenComposite build option](https://gitlab.com/znixian/OpenOVR/-/blob/cff07db75c4823afe93ed7027b03d5f7bc86f164/CMakeLists.txt),
[bundled loader registry discovery](https://github.com/KhronosGroup/OpenXR-SDK/blob/91a8a8d9d70f4b469bca0726122c3b5a6096010e/src/loader/manifest_file.cpp#L409-L415),
[integrity detection](https://github.com/KhronosGroup/OpenXR-SDK/blob/91a8a8d9d70f4b469bca0726122c3b5a6096010e/src/common/platform_utils.hpp#L201-L226)).

## What the panel looks like in VR

[OverlayWindowRegistry](../../src/SrvSurvey.Desktop/Platform/Overlay/OverlayWindowRegistry.cs)
selects each panel's current `PresentationVisual`, or its window when separately
hosted. Both the desktop stream compositor and
[VrOverlayCoordinator](../../src/SrvSurvey.Desktop/Platform/Overlay/VrOverlayCoordinator.cs)
use that `RenderSource`.
[VrOverlayFrameRenderer](../../src/SrvSurvey.Desktop/Platform/Overlay/VrOverlayFrameRenderer.cs)
rasterizes it at the source's pixel density and decodes straight-alpha RGBA.
Consequently the fonts, borders, backgrounds, icons, layout, and panel content
come from the existing desktop presentation. It captures each visible panel,
not the whole transparent combined desktop host and its empty spacing.

[OpenXrFramePacket](../../src/SrvSurvey.Desktop/Platform/Overlay/OpenXrFramePacket.cs)
preserves `height / width` when calculating physical dimensions. The native
[Layer.cpp](../../src/SrvSurvey.OpenXrLayer/Layer.cpp) uploads the same color bytes,
scales alpha by the overlay opacity, and submits `XrCompositionLayerQuad` for both
eyes. Position, rotation, and physical width change how that rectangle sits in
3D; they do not reflow its controls or bend its surface.

The audit also found that the shared VR publisher applied host opacity a second
time when capturing a standalone window: Avalonia's window bitmap already
contains that opacity. The branch now matches the desktop stream compositor:
standalone captures use an additional alpha of one, while separately hosted
panel visuals receive the host opacity once. A rendered-pixel regression test
checks color, aspect ratio, and effective alpha for both presentation modes.

Khronos defines a quad as a planar rectangle for two-dimensional content. The
runtime performs normal stereo placement, sampling, reprojection, and lens
correction. A rotated panel can appear foreshortened, and text sharpness can
change with distance, texture resolution, headset resolution, and streaming.
Those are presentation effects; pixel-for-pixel equality with a desktop monitor
is not promised
([quad composition](https://github.com/KhronosGroup/OpenXR-Docs/blob/5a82d45bc9e0adbaa24b6c4a3e5b6d50e08c887d/specification/sources/chapters/rendering.adoc#rendering-composition-layer-types)).

The required `R8G8B8A8_UNORM_SRGB` swapchain format appears in VDXR's format list
([swapchain implementation](https://github.com/mbucchia/VirtualDesktop-OpenXR/blob/6da20fe478e331c05672243fd82a9a1500cd39f5/virtualdesktop-openxr/swapchain.cpp#L224-L239)).
The layer flags request source-alpha blending and declare straight alpha. VDXR's
shader converts sRGB to linear color, premultiplies by alpha, then restores the
sRGB encoding. That matches the publisher's straight-alpha input and avoids
premultiplying the sRGB byte values in SrvSurvey
([alpha processing](https://github.com/mbucchia/VirtualDesktop-OpenXR/blob/6da20fe478e331c05672243fd82a9a1500cd39f5/virtualdesktop-openxr/AlphaBlendingCS.hlsl#L45-L70)).
Check translucent text edges and dark backgrounds on the actual headset.

## VR interaction shortcut and controller input

The existing `ToggleVrOverlayInteraction` binding dispatches through
[DesktopRuntime](../../src/SrvSurvey.Desktop/Runtime/DesktopRuntime.Composition.cs)
to `VrOverlayCoordinator.ToggleInteraction()`. That calls the selected
runtime's `SetInteractionEnabled()`.
[OpenXrOverlayRuntime](../../src/SrvSurvey.Desktop/Platform/Overlay/OpenXrOverlayRuntime.cs)
currently rejects that request and its `PollPointerEvents()` returns an empty
list. Therefore the configured keyboard, HOTAS, or gamepad shortcut can still be
detected, but it cannot turn VDXR panels into clickable controller surfaces.
The separate desktop-overlay interaction shortcut remains a desktop operation.
Neither shortcut automatically creates a VDXR pointer.

This is a missing SrvSurvey input implementation, not a lack of controllers in
VDXR. An OpenXR composition quad has no built-in hit-test, cursor, click, scroll,
or Avalonia event delivery. A complete implementation would need controller aim
poses and button actions, ray-to-panel hit testing, pixel/logical coordinate
mapping, a pointer display, and return events through IPC to
[VrOverlayInputRouter](../../src/SrvSurvey.Desktop/Platform/Overlay/VrOverlayInputRouter.cs).

OpenXR input is owned by the application's session. Action sets attach only once;
action bindings cannot be added after attachment; resuggesting bindings replaces
the previous bindings for that interaction profile. An API layer implementing
interaction must merge its actions into the game's attachment and synchronization
flow before those operations occur, respect focus, and manage action priority
without unintentionally consuming Elite input. Simply calling attach again when
the user presses the shortcut is invalid
([OpenXR action rules](https://github.com/KhronosGroup/OpenXR-Docs/blob/5a82d45bc9e0adbaa24b6c4a3e5b6d50e08c887d/specification/sources/chapters/input.adoc)).
OpenComposite already attaches and synchronizes its own action sets, so that
coexistence is a concrete requirement
([OpenComposite input](https://gitlab.com/znixian/OpenOVR/-/blob/cff07db75c4823afe93ed7027b03d5f7bc86f164/OpenOVR/Reimpl/BaseInput.cpp)).

## Native rendering audit

The reviewed [layer](../../src/SrvSurvey.OpenXrLayer/Layer.cpp) has the following
contracts and remaining limits:

| Area | Code/source result | Remaining validation or limitation |
| --- | --- | --- |
| Process and runtime | Submission activates only inside `EliteDangerous64.exe`, with runtime name `VirtualDesktopXR` and a D3D11 graphics binding. | Other executables, native Oculus sessions, D3D12, Vulkan, and Linux are outside this implementation. |
| Graphics device | Uses the game's bound D3D11 device and runtime swapchain textures. `UpdateSubresource` uses the immediate context without changing pipeline state. | Check real driver/device behavior; this is not a separate asynchronous graphics worker. |
| Swapchain ownership | Acquire, wait for `XR_SUCCESS`, upload, then release; a zero-timeout wait keeps the acquisition and retries next frame. Previously released content is retained while waiting. | Exercise repeated timeouts and resize/hide transitions on the user's driver. |
| Composition | Appends quads after existing game layers; preserves the original frame metadata and bounds additions by the reported layer count. | Later layers paint over earlier layers regardless of virtual distance; this is not game-depth occlusion. Other API layers may change final ordering. |
| Placement | Captures `VIEW` relative to `LOCAL`, then keeps the panels stable in `LOCAL`. Reset requests a new anchor. Pending recenter events take effect at their specified time. | This is world-stable placement, not a permanently head-locked HUD. Verify recentering and loss/recovery of tracking. |
| Failure | Local overlay preparation failures forward the original game frame and stop additions. An error returned by the final runtime submission stops future additions. | A runtime-rejected frame is not resubmitted; the original frame cannot be guaranteed displayed on that failed submission. Restart Elite to retry a failed layer. |
| Publication | Managed publisher limits packets to 16 panels, 4096 pixels per dimension, and 64 MiB; stale pixel data expires. | Available runtime slots can be fewer after the game/other layers. The UI's live count describes queued panels and is not proof that every quad reached the headset. |
| Update cadence | Existing managed synchronization captures pixels about every 250 ms; native composition follows each game frame. | Panels are not captured at headset refresh rate. Measure UI capture/encoding and in-game GPU cost with representative panel counts. |

The D3D11 extension requires default-usage swapchain textures and a device matching
the runtime's graphics requirements. Using the already accepted session binding
meets the device-selection path; the native code does not create an unrelated
adapter/device
([D3D11 contract](https://github.com/KhronosGroup/OpenXR-Docs/blob/5a82d45bc9e0adbaa24b6c4a3e5b6d50e08c887d/specification/sources/chapters/extensions/khr/khr_d3d11_enable.adoc)).

Microsoft requires serialization of concurrent immediate-context use. The
OpenXR core rule serializes `xrBeginFrame` and `xrEndFrame` per session, but the
review did not find a D3D11-specific rule extending that guarantee to arbitrary
application rendering threads
([Microsoft threading contract](https://learn.microsoft.com/en-us/windows/win32/direct3d11/overviews-direct3d-11-render-multi-thread-intro),
[OpenXR frame host synchronization](https://registry.khronos.org/OpenXR/specs/1.0/man/html/xrEndFrame.html)).
VDXR already accesses the application context in its own submission path and
uses fences when crossing to a different submission device. Our uploads occur
inside that same call, before forwarding it; this supports the approach but does
not establish the behavior of an untested game's parallel render threads
([VDXR D3D11 synchronization](https://github.com/mbucchia/VirtualDesktop-OpenXR/blob/6da20fe478e331c05672243fd82a9a1500cd39f5/virtualdesktop-openxr/d3d11_native.cpp)).

Khronos requires application GPU work referencing a swapchain to complete **before
calling** destruction. `UpdateSubresource` followed by image release does not by
itself prove GPU completion. This review found a missing wait in the initial
layer. The branch now completes pending uploads with a D3D11 event query before
retiring swapchains on resize, panel removal, or teardown. This wait occurs only
when resources are retired. VDXR also waits internally, but the layer must meet
its own caller-side precondition
([swapchain destruction contract](https://registry.khronos.org/OpenXR/specs/1.0/man/html/xrDestroySwapchain.html),
[VDXR destruction](https://github.com/mbucchia/VirtualDesktop-OpenXR/blob/6da20fe478e331c05672243fd82a9a1500cd39f5/virtualdesktop-openxr/swapchain.cpp#L428-L466)).
`LOCAL` and `VIEW` are mandatory reference spaces; only layers using `VIEW`
directly are implicitly head locked
([reference-space contract](https://github.com/KhronosGroup/OpenXR-Docs/blob/5a82d45bc9e0adbaa24b6c4a3e5b6d50e08c887d/specification/sources/chapters/spaces.adoc)).

## Evidence required from the actual headset

The native tests cover loader negotiation/chaining, software-D3D11 pixel upload,
timeout handling, frame preservation, placement, recenter scheduling, stale-data
expiry, GPU completion before resource retirement, and resource cleanup. They use a mocked OpenXR runtime, so their scope
stops before VDXR's compositor and the headset
([native tests](../../src/SrvSurvey.OpenXrLayer/tests/LayerTests.cpp)).

Record this acceptance evidence before describing the combination as verified:

1. Windows, Quest, Virtual Desktop Streamer/headset, GPU driver, Elite, and
   OpenComposite versions; confirm actual `Runtime: VDXR` with SteamVR closed.
2. Install per user, restart Elite, connect from the existing route dropdown,
   and render real live panels, including combined-desktop presentation mode.
3. Compare fonts, colors, borders, translucent edges, and aspect ratio against
   the desktop panel. Check both eyes, oblique angles, readable distance, and
   opacity levels rather than relying only on a desktop mirror screenshot.
4. Exercise per-panel placement, hide/show, content resize, manual orientation
   reset, Virtual Desktop recenter, temporary tracking loss, headset sleep/wake,
   Elite restart, and SrvSurvey restart while the game remains running.
5. Check representative multi-panel load and coexistence with the user's other
   API layers. Compare frame time and dropped frames with overlays off and on.
6. Verify the VR interaction shortcut reports the present limitation without
   enabling a misleading interactive state; controller clicking is outside the
   current route's acceptance criteria.
7. Unregister or set `SRVSURVEY_OPENXR_DISABLE`, restart Elite, and confirm the
   user's prior game behavior returns. Moving an install folder requires
   registering its new manifest path.

No physical Quest 3/VDXR result was obtained in this investigation. The reviewed
architecture is feasible and the rendering contracts align; the evidence does
not support promising that every user configuration will work without a real
session test.
