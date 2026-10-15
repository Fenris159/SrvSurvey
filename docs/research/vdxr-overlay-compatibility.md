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
- **VDXR interaction requires its own input path.** The requested extension uses
  the existing interaction shortcut, native OpenXR controller actions, panel hit
  testing, and IPC events delivered through the shared Avalonia input router.
  The implementation contracts and headset acceptance checks are recorded below.
- **The remaining end-to-end check is physical.** The source review supports the
  chosen integration, but cannot guarantee the user's installed runtime,
  translation layer, drivers, and headset behave identically to the reviewed
  sources.

## Supported baseline and dated release snapshot

Per the requested support scope, the target is **Quest 3 on the current stable
Quest-store Virtual Desktop client, the matching current Windows x64 Streamer
from the official download, its bundled VDXR, and the current OpenComposite
`openxr` build for Elite**. Use the normal release channels and update the client
and Streamer together. Keep VDXR bundled with the Streamer rather than copying a
runtime built from a newer source commit. Validate the real game's
`Runtime: VDXR` indication and record the installed component versions; this
baseline is a support target, not an already completed headset test
([official downloads](https://www.vrdesktop.net/),
[Quest product](https://www.meta.com/experiences/virtual-desktop/2017050365004772/),
[VDXR distribution](https://github.com/mbucchia/VirtualDesktop-OpenXR/wiki)).

Snapshot on **2026-10-14**:

- The official homepage advertises **1.34.22**. The normal
  [Windows Streamer installer](https://files.vrdesktop.net/files/VirtualDesktop.Streamer.Setup.exe)
  was inspected without execution using an HTTP range read of its PE resources:
  `ProductName=Virtual Desktop Streamer`, `FileVersion=1.34.22.0`, and
  `ProductVersion=1.34.22.0`. HTTP `Last-Modified` was
  `2026-08-25 18:48:32 UTC`; that is artifact metadata, not a verified release
  announcement date. The inspected public Quest product page does not expose
  its deployed client version.
- The first-party GitHub release metadata is behind that artifact: the newest
  published entry is **1.34.16**, dated **2026-04-01**, with
  `draft=false` and `prerelease=false`; `/releases/latest` still identifies
  **1.34.10**, dated **2025-10-20**. None of the 20 visible release entries was
  marked prerelease. Accordingly, the GitHub Latest badge cannot identify the
  current Quest/Streamer pair in this snapshot
  ([release list](https://github.com/guygodin/VirtualDesktop/releases),
  [1.34.16](https://github.com/guygodin/VirtualDesktop/releases/tag/v1.34.16),
  [Latest endpoint](https://github.com/guygodin/VirtualDesktop/releases/latest)).
- OpenComposite's current remote `openxr` HEAD remains
  `cff07db75c4823afe93ed7027b03d5f7bc86f164` (2025-07-21). Public VDXR `main`
  remains `6da20fe478e331c05672243fd82a9a1500cd39f5` (2026-10-03). These are
  source review snapshots, not proof of the revision inside the current
  Streamer installer.

Recent upstream changes worth retaining in regression testing are Virtual
Desktop's color-gradient/codec improvements in 1.34.16 and VDXR's October 3
session/swapchain leak fixes. VDXR also added OpenXR 1.1 support on August 6;
SrvSurvey's layer manifest declares API 1.0 compatibility and uses the established
D3D11/quad path. These changes do not add SrvSurvey controller interaction
([Virtual Desktop changes](https://github.com/guygodin/VirtualDesktop/releases/tag/v1.34.16),
[VDXR resource fix](https://github.com/mbucchia/VirtualDesktop-OpenXR/commit/6da20fe478e331c05672243fd82a9a1500cd39f5),
[VDXR 1.1 change](https://github.com/mbucchia/VirtualDesktop-OpenXR/commit/666a46be41b98ba23d2a269b1d1644bcc3b4334f)).

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
build. Use the current OpenComposite `openxr` build for the support target above
and record its exact version during real-session validation.

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

## VR interaction shortcut: implementation design contracts

The existing `ToggleVrOverlayInteraction` binding dispatches through
[DesktopRuntime](../../src/SrvSurvey.Desktop/Runtime/DesktopRuntime.Composition.cs)
to `VrOverlayCoordinator.ToggleInteraction()` and the selected runtime's
`SetInteractionEnabled()`. The VDXR interaction work extends that path with native
controller actions, hit testing and cursor feedback, and a bounded IPC event
stream read by `PollPointerEvents()` and dispatched through
[VrOverlayInputRouter](../../src/SrvSurvey.Desktop/Platform/Overlay/VrOverlayInputRouter.cs).
This section records the required behavior; the physical-headset acceptance
items below remain necessary after implementation. A composition quad itself
supplies no cursor or click events. The separate desktop interaction shortcut
continues to control desktop windows.

### Coexist with OpenComposite's actions

- **Attach once per session.** Append SrvSurvey's prepared action set to the
  application's first `xrAttachSessionActionSets` call and preserve every game
  set. Do not attach a second set when the user presses the shortcut. An attached
  set becomes immutable; the reviewed normative text provides no exception that
  permits changing its actions after destroying a session. Create a fresh owned
  input generation when new binding suggestions follow session replacement;
  preserve the generation for graphics-only restarts that reuse existing sets.
  Never re-suggest previously attached owned actions
  ([attachment contract](https://registry.khronos.org/OpenXR/specs/1.0/man/html/xrAttachSessionActionSets.html),
  [action creation restriction](https://registry.khronos.org/OpenXR/specs/1.0/man/html/xrCreateAction.html)).
- **Merge profile suggestions.** Re-suggesting a profile replaces its earlier
  bindings. Intercept each legal Touch suggestion and append the layer's valid
  bindings to that request, preserving the application's entries. If the game
  never suggests Touch, prepare the owned Touch bindings before attachment.
  Never turn an otherwise valid game request into an error by adding immutable
  or destroyed owned actions. A late or incompatible setup should make overlay
  input unavailable while preserving the game request
  ([suggestion contract](https://registry.khronos.org/OpenXR/specs/1.0/man/html/xrSuggestInteractionProfileBindings.html)).
- **Join the existing synchronization.** Append the enabled owned action set to
  intercepted `xrSyncActions` calls, preserving the application's active sets,
  subaction paths and extension chain. An overlay-only synchronization would
  deactivate the game sets omitted from that call. Do not add frame wait/begin/end
  calls for input. Consume input only after an actual `XR_SUCCESS` and a focused
  session: `XR_SESSION_NOT_FOCUSED` is a positive result, yet all inputs must be
  inactive. Repeated reads between syncs describe the same input snapshot
  ([synchronization contract](https://registry.khronos.org/OpenXR/specs/1.0/man/html/xrSyncActions.html)).

OpenComposite's normal `BindInputsForSession()` creates fresh action spaces,
collects the game sets plus its legacy set, then attaches them. Legacy
`InternalUpdate()` synchronizes after each submitted frame through
`BaseSystem::_OnPostFrame()`; modern `UpdateActionState()` synchronizes game sets
plus legacy inputs. Therefore reading actions during the next end-frame uses the
application's most recent synchronization; the first frame may have none
([OpenComposite input](https://gitlab.com/znixian/OpenOVR/-/blob/cff07db75c4823afe93ed7027b03d5f7bc86f164/OpenOVR/Reimpl/BaseInput.cpp),
[post-frame update](https://gitlab.com/znixian/OpenOVR/-/blob/cff07db75c4823afe93ed7027b03d5f7bc86f164/OpenOVR/Reimpl/BaseSystem.cpp#L529-L550)).

There is also an early discovery path: `QueryForInteractionProfile()` creates an
internal information set, suggests profiles, and attaches that set alone. When
real inputs load, `MaybeRestartForInputs()` invokes session replacement;
`PrepareForSessionShutdown()` destroys the information set, then the session is
destroyed and recreated. The input loader temporarily suppresses automatic
attachment, creates and suggests the real actions, and finally attaches them.
The fake-runtime regression covers the information-only → replacement session →
real inputs flow and a graphics restart that reuses the original sets
([discovery and shutdown flow](https://gitlab.com/znixian/OpenOVR/-/blob/cff07db75c4823afe93ed7027b03d5f7bc86f164/DrvOpenXR/XrBackend.cpp),
[session replacement](https://gitlab.com/znixian/OpenOVR/-/blob/cff07db75c4823afe93ed7027b03d5f7bc86f164/DrvOpenXR/DrvOpenXR.cpp#L316-L380)).

The reviewed OpenComposite game sets use their default priority zero, as does its
legacy set. Higher-priority active actions suppress colliding lower-priority
sources; equal priorities allow both consumers. Collision is by input identifier,
so a thumbstick vector collides with the game's individual thumbstick axes.
Reserve pointer buttons/scroll only while interaction is enabled and restore the
original active sets when disabled. Tracking poses can use a separate equal
priority set if the implementation must preserve the game's aim poses during
interaction
([action-set priority](https://registry.khronos.org/OpenXR/specs/1.0/man/html/XrActionSetCreateInfo.html)).

### Quest controller bindings

Use `/interaction_profiles/oculus/touch_controller` with both
`/user/hand/left` and `/user/hand/right` subaction paths:

| Purpose | Relative binding path | OpenXR action type |
| --- | --- | --- |
| Pointer ray | `/input/aim/pose` | `POSE_INPUT` |
| Primary click | `/input/trigger/value` | `BOOLEAN_INPUT` |
| Secondary click | `/input/squeeze/value` | `BOOLEAN_INPUT` |
| Middle click | `/input/thumbstick/click` | `BOOLEAN_INPUT` |
| Scroll | `/input/thumbstick` | `VECTOR2F_INPUT` |

These paths exist in the core Touch profile and in VDXR's Touch mapping. A scalar
source bound to a boolean action is thresholded by the runtime; VDXR's reviewed
implementation uses greater than 0.5, while the core specification leaves the
threshold and hysteresis to the runtime. A vector binding uses the thumbstick
parent path, whose x/y components supply the vector
([Touch profile](https://github.com/KhronosGroup/OpenXR-Docs/blob/5a82d45bc9e0adbaa24b6c4a3e5b6d50e08c887d/specification/sources/chapters/semantic_paths.adoc#L1137-L1176),
[input conversions](https://github.com/KhronosGroup/OpenXR-Docs/blob/5a82d45bc9e0adbaa24b6c4a3e5b6d50e08c887d/specification/sources/chapters/input.adoc#L509-L545),
[VDXR path mappings](https://github.com/mbucchia/VirtualDesktop-OpenXR/blob/6da20fe478e331c05672243fd82a9a1500cd39f5/virtualdesktop-openxr/mappings.cpp),
[VDXR boolean conversion](https://github.com/mbucchia/VirtualDesktop-OpenXR/blob/6da20fe478e331c05672243fd82a9a1500cd39f5/virtualdesktop-openxr/action.cpp#L620-L650)).

### Pointer state and failure handling

These are implementation requirements inferred from the shared single-pointer
Avalonia router and the composition path:

- Locate each active aim pose in the same `LOCAL` space and display time as the
  quad. Require valid position and orientation, then intersect the controller's
  forward ray with the transformed flat panel. Preserve aspect ratio and map
  hits to the panel's logical dimensions; image pixels have a top-left origin.
- Resolve overlapping panels consistently with the actual composition order,
  which paints later layers over earlier ones. Keep one stable pointer owner
  during a press/drag so the other hand cannot steal its capture.
- Enable without generating clicks from already held buttons. Require a neutral
  release before arming presses after enable, focus restoration or input recovery.
  Emit releases/cancellation and clear capture on disable, focus/pose loss, hidden
  panels, stale IPC, session replacement or application shutdown.
- Keep press, release, motion and scroll events ordered and bounded. Overflow or
  a disconnected consumer must reset interaction rather than leave a captured
  mouse button. Draw cursor feedback from native input at frame cadence, without
  waiting for the slower panel recapture timer.
- Forward the application's original input calls/results if owned input setup
  fails. Rendering and the original game frame loop must remain usable.

The implemented bridge uses a separate 4 KiB input mapping with a publisher
nonce, control/native heartbeats, a seqlock, and a 64-record event ring. Torn,
stale, malformed, or overflowed event streams cancel capture and discard events.
Portable native tests cover rotated ray geometry, composition order, button
edges, scroll limits, off-panel capture, tracking/panel loss, and neutral rearming.
The software-D3D11 runtime tests cover Touch binding merges, original extension
chains and game subaction paths, discovery-to-real input generations, inactive
mode, `XR_SESSION_NOT_FOCUSED`, partial action-space failure, cursor pixel upload,
and native click/reset delivery. Managed tests cover the ring protocol, logical
coordinates, both shortcut directions, and cancellation of an Avalonia button
without firing a click. The router explicitly captures the hit control on press,
matching the capture step normally supplied by Avalonia's mouse device; releasing
capture then raises the control's capture-loss handling and clears pressed state
([Avalonia 12.1.3 mouse dispatch](https://github.com/AvaloniaUI/Avalonia/blob/12.1.3/src/Avalonia.Base/Input/MouseDevice.cs#L128-L158),
[capture-loss notifications](https://github.com/AvaloniaUI/Avalonia/blob/12.1.3/src/Avalonia.Base/Input/Pointer.cs#L118-L124)).
These checks do not replace the headset acceptance list.

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
6. Verify the existing VR interaction shortcut enables and disables a visible
   controller pointer; test primary/secondary click, scroll, drag and release on
   real panel controls. Check held buttons on enable, hand changes, focus loss,
   tracking loss, hidden panels and session replacement. Confirm the game's own
   controller input resumes when interaction is disabled.
7. Unregister or set `SRVSURVEY_OPENXR_DISABLE`, restart Elite, and confirm the
   user's prior game behavior returns. Moving an install folder requires
   registering its new manifest path.

No physical Quest 3/VDXR result was obtained in this investigation. The reviewed
architecture is feasible and the rendering contracts align; the evidence does
not support promising that every user configuration will work without a real
session test.

## Route picker platform availability

The settings menu retains all eight routes and disables incompatible choices.
Linux permits SteamVR, ALVR and custom OpenVR; Meta Link, Quest Steam Link,
Virtual Desktop, VDXR and Microsoft's WMR bridge require Windows. VDXR also
requires an x64 process. WMR is disabled on Windows builds 26100 and newer,
where Microsoft removed that bridge
([Valve requirements](https://help.steampowered.com/en/faqs/view/0E2C-406B-9135-38A4),
[Meta Link requirements](https://developers.meta.com/vr/documentation/unity/unity-development-requirements/),
[WMR removal](https://learn.microsoft.com/en-us/windows/whats-new/deprecated-features)).

The picker uses disabled `ComboBoxItem` controls so native closed-menu arrow and
wheel navigation can see availability before visual containers are realized.
Avalonia's built-in type-ahead does not exclude disabled matches, so the picker
searches available, translated labels. Headless tests cover open/closed keyboard
navigation, wheel and click input, type-ahead, template updates and a saved
unsupported selection
([ComboBox navigation](https://github.com/AvaloniaUI/Avalonia/blob/12.1.3/src/Avalonia.Controls/ComboBox.cs#L554-L588),
[type-ahead lookup](https://github.com/AvaloniaUI/Avalonia/blob/12.1.3/src/Avalonia.Controls/Primitives/SelectingItemsControl.cs#L1476-L1497)).

A saved incompatible route remains readable; its enabled flag is cleared and
Enable VR overlays stays disabled until the user chooses an available route.
The view model also validates route IDs against its host-specific catalog.
Installation and headset readiness continue to be runtime checks, so a compatible
route can be configured before its runtime starts.
