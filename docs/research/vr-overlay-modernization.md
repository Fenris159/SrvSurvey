# VR overlay modernization and headset interoperability

Date checked: 2026-09-20

Current SrvSurvey revision: `0da966b9c863d686c8696298bfd4cf2635b83812`

## Conclusion

Modernize SrvSurvey's existing OpenVR integration as a **SteamVR overlay**
feature for Windows and Linux, and put a capability-driven connection assistant
in front of it. Do not replace it with a generic OpenXR backend or promise that
SrvSurvey can pair every headset itself.

That boundary follows the platform contracts:

- Valve documents `IVROverlay` specifically as a way for one application to
  place 2D content over another application, and `VRApplication_Overlay` is a
  first-class OpenVR application type
  ([OpenVR API documentation](https://github.com/ValveSoftware/openvr/wiki/API-Documentation),
  [overlay overview](https://github.com/ValveSoftware/openvr/wiki/IVROverlay_Overview)).
- OpenXR is the right portable API for a new immersive application, but the
  core specification neither recognizes nor requires simultaneous use by
  multiple processes. Its cross-application overlay facility,
  `XR_EXTX_overlay`, is still a provisional multi-vendor extension; an `EXTX`
  name explicitly denotes provisional/experimental/preview status
  ([OpenXR multiprocessing behavior](https://registry.khronos.org/OpenXR/specs/1.0-khr/html/xrspec.html#fundamentals-multiprocessing-behavior),
  [extension process](https://registry.khronos.org/OpenXR/specs/1.0/extprocess.html),
  [`XR_EXTX_overlay`](https://registry.khronos.org/OpenXR/specs/1.1/man/html/XR_EXTX_overlay.html)).
- OpenComposite translates OpenVR calls to an OpenXR runtime, but its separate
  overlay-application implementation is still an unmerged proposal, not a
  dependable released path
  ([OpenComposite project](https://gitlab.com/znixian/OpenOVR),
  [overlay application merge request](https://gitlab.com/znixian/OpenOVR/-/merge_requests/101)).

The product should therefore say **SteamVR overlays**, not imply that OpenVR is
a headset or that SrvSurvey pairs hardware. The headset/vendor software pairs
and streams the device; SrvSurvey connects to the running SteamVR compositor.
The UI can guide both jobs while keeping ownership clear.

## What the existing implementation does

At the checked revision:

- `OpenVrRuntime` initializes `VRApplication_Overlay`, creates one named
  OpenVR overlay per live SrvSurvey overlay, sends an RGBA CPU buffer with
  `SetOverlayRaw`, applies alpha/size/absolute transforms, and shows it
  (`src/SrvSurvey.Desktop/Platform/Overlay/OpenVrRuntime.cs:40-129`). This is
  conceptually the correct transport for the feature. Valve lists raw buffers,
  native textures, transforms, events, and dashboard overlays in the same
  `IVROverlay` interface
  ([Valve overlay overview](https://github.com/ValveSoftware/openvr/wiki/IVROverlay_Overview)).
- `VrOverlayCoordinator` gates initialization on a user-editable process name,
  polls every 250 ms, and collapses all connection information into one status
  string (`src/SrvSurvey.Desktop/Platform/Overlay/VrOverlayCoordinator.cs:20-39,105-178`).
  The screenshot supplied with the request exposes that implementation detail
  as `VR RUNTIME PROCESS` / `vrserver`.
- Linux native-library discovery checks an environment override, the app
  directory, and three conventional Steam library locations
  (`src/SrvSurvey.Desktop/Platform/Overlay/OpenVrNativeLibraryResolver.cs:7-127`).
- The project depends on `OVRSharp` 1.2.0 and carries a `System.Drawing.Common`
  override solely because of that package
  (`src/SrvSurvey.Desktop/SrvSurvey.Desktop.csproj:25-33`). NuGet dates that
  package to 2021, while Valve continues to release its SDK and supplies an
  official generated C# binding in the repository
  ([OVRSharp 1.2.0](https://www.nuget.org/packages/OVRSharp),
  [current Valve releases](https://github.com/ValveSoftware/openvr/releases),
  [Valve `openvr_api.cs`](https://github.com/ValveSoftware/openvr/blob/master/headers/openvr_api.cs)).

The main weaknesses are consequently discovery, diagnostics, binding age, and
UX—not the decision to use OpenVR for a SteamVR overlay.

## Follow-up: Meta/Oculus compatibility bridges

The requested direct Meta OpenXR overlay backend is not currently a supportable
product path. OpenXR core does not guarantee simultaneous processes, and its
cross-process overlay contract remains the provisional `XR_EXTX_overlay`
extension. Meta's current PC OpenXR documentation does not advertise that
extension, and the current official Meta OpenXR SDK contains no
`XR_EXTX_overlay` implementation or sample
([Khronos multiprocessing behavior](https://registry.khronos.org/OpenXR/specs/1.1-khr/html/xrspec.html#fundamentals-multiprocessing-behavior),
[`XR_EXTX_overlay`](https://registry.khronos.org/OpenXR/specs/1.1/man/html/XR_EXTX_overlay.html),
[Meta PC OpenXR support](https://developers.meta.com/horizon/documentation/native/pc/dg-openxr/),
[Meta OpenXR SDK](https://github.com/meta-quest/Meta-OpenXR-SDK)).

The implementation therefore does not offer an OpenXR backend. It keeps one
OpenVR overlay publisher and lets Meta users select the bridge that presents
their headset to SteamVR: Meta Horizon Link/Air Link, Valve Steam Link, Virtual
Desktop in SteamVR mode, or ALVR. SteamVR remains the compositor in every
supported choice.

OpenComposite is not an alternative for this use case. It translates OpenVR
scene applications to OpenXR, while its overlay-application work remains in
draft merge requests rather than the released runtime
([OpenComposite project](https://gitlab.com/znixian/OpenOVR),
[overlay support merge requests](https://gitlab.com/znixian/OpenOVR/-/merge_requests?scope=all&state=opened&search=overlay)).

## Recommended technical shape

### 1. Keep a runtime-neutral application seam, ship one production backend

Introduce an internal overlay-runtime contract whose public state is structured
rather than a formatted string. A useful probe result includes:

- backend (`SteamVR/OpenVR` initially);
- native client library availability and resolved path;
- runtime installed/registered;
- headset present;
- connection state (`Disabled`, `NotInstalled`, `RuntimeStopped`,
  `HeadsetUnavailable`, `Connecting`, `Ready`, `Faulted`);
- stable native error code/symbol plus user-facing explanation;
- live overlay count and last successful frame time.

Keep calibration and frame production independent of that contract. This makes
an OpenXR experiment possible later without representing it as supported now.
An OpenXR backend should only enable itself after enumerating
`XR_EXTX_overlay`; extensions are optional and applications must query them
before enabling them
([OpenXR extension rules](https://registry.khronos.org/OpenXR/specs/1.1-khr/html/xrspec.html#extensions)).

### 2. Replace process-name gating with OpenVR's own probes

Valve provides `VR_IsRuntimeInstalled`, `VR_IsHmdPresent`, `VR_GetRuntimePath`,
`VR_Init`, and symbolic/descriptive initialization errors. `VR_IsHmdPresent`
is deliberately only a quick filter; Valve notes that it can succeed when
`VR_Init` still fails, so initialization remains the authoritative test
([OpenVR initialization and helper functions](https://github.com/ValveSoftware/openvr/wiki/API-Documentation#initialization-and-cleanup)).

Use those APIs instead of requiring a process called `vrserver`. Keep a custom
process name only as a migrated, hidden diagnostic override if compatibility
evidence shows it is still needed. Retry failed initialization with bounded
backoff and reset the backoff on an explicit **Check again** action; do not
reinitialize four times per second.

### 3. Update and pin the Valve binding and client library

Replace OVRSharp with the official generated Valve C# binding from a pinned
OpenVR SDK release, plus the very small SrvSurvey-owned matrix conversion it
currently consumes from `OVRSharp.Math`. Ship or resolve the matching official
`openvr_api` client library for `win-x64` and `linux-x64` deterministically and
preserve `SRVSURVEY_OPENVR_LIBRARY` as an advanced Linux escape hatch. Valve's
SDK and samples carry both Windows and `linux64` paths, and Valve describes
OpenVR's Linux API support as complete, although SteamVR for Linux itself is a
development release with limited hardware support
([OpenVR SDK](https://github.com/ValveSoftware/openvr),
 [Valve Linux sample configuration](https://github.com/ValveSoftware/openvr/blob/master/samples/CMakeLists.txt),
 [SteamVR for Linux status and requirements](https://github.com/ValveSoftware/SteamVR-for-Linux)).

Do not equate “the API builds on Linux” with “every headset works on Linux.”
Publish a tested hardware/streamer matrix and label untested routes as such.

### 4. Preserve the CPU path first, then optimize behind the seam

`SetOverlayRaw` gives Windows/Linux parity and matches the current Avalonia
frame source. Keep it for the first modernization pass. Stop rendering and
uploading unchanged hidden content on every timer tick; track dirty/revision
state, use a target refresh cadence, and publish connection health separately
from frame cadence. A later native-texture path can use
`SetOverlayTexture`, but D3D/Vulkan/OpenGL handle ownership should not be mixed
into runtime discovery or the setup UX. Both submission paths are part of the
documented `IVROverlay` interface
([Valve overlay overview](https://github.com/ValveSoftware/openvr/wiki/IVROverlay_Overview)).

### 5. Consider SteamVR registration and an in-headset dashboard later

OpenVR supports dashboard overlays in addition to free-standing scene overlays.
`CreateDashboardOverlay` creates a system-dashboard tab, while the current
`CreateOverlay` call creates a non-dashboard overlay
([`CreateDashboardOverlay`](https://github.com/ValveSoftware/openvr/wiki/IVROverlay%3A%3ACreateDashboardOverlay),
 [`CreateOverlay`](https://github.com/ValveSoftware/openvr/wiki/IVROverlay%3A%3ACreateOverlay)).

A small dashboard surface could eventually expose enable/test/reset/calibrate
actions in-headset. It is a second phase, not a prerequisite for fixing the
desktop settings card. SteamVR application-manifest registration and optional
auto-launch also need an explicit lifecycle and rollback plan; historical Valve
issues show that manifest changes may not become fully visible until SteamVR is
restarted
([Valve manifest behavior discussion](https://github.com/ValveSoftware/openvr/issues/106)).

## Compatibility and setup paths

SrvSurvey should model a **connection path**, not a claimed headset protocol.
The common denominator is whether the final compositor is SteamVR.

| User path | OS | Overlay expectation | Guidance in SrvSurvey |
|---|---|---|---|
| Valve Index, Vive, or another headset already tracked by SteamVR | Windows; Linux where Valve supports the hardware | First-class | Pair/configure hardware in SteamVR or the vendor utility, start SteamVR, then enable and test SrvSurvey. Valve describes SteamVR as owning device status, firmware, pairing, and room setup ([SteamVR overview](https://partner.steamgames.com/doc/features/steamvr/info?language=english)); Vive's setup also installs and validates through SteamVR ([Vive setup](https://www.vive.com/us/support/vive/category_howto/setting-up-for-the-first-time.html)). |
| Meta Quest through Steam Link | Windows 10 or newer | Expected when SteamVR is the compositor | Pair the PC in Steam Link, confirm SteamVR sees the headset, then launch Elite in its SteamVR mode. Valve's current requirements list a Windows PC running Steam and SteamVR plus Quest 2, 3, or Pro ([Valve Steam Link support](https://help.steampowered.com/en/faqs/view/0E2C-406B-9135-38A4)). |
| Meta Quest through Quest Link or Air Link | Windows only | Expected only after SteamVR is started and Elite is launched through SteamVR; not in a Meta-runtime-direct session | Let Meta Horizon Link connect the headset, then start SteamVR and use the SteamVR launch path. Meta documents that Link turns Quest into a PC-VR headset and is Windows-only ([Meta Link guide](https://developers.meta.com/horizon/documentation/unity/unity-link/)). |
| Meta Quest through Virtual Desktop | Windows only for PC-VR streaming | Expected only in Virtual Desktop's SteamVR mode | Connect with the Quest app and Windows Streamer, launch SteamVR from Virtual Desktop, then launch Elite. Virtual Desktop officially supports SteamVR games and lists Quest headsets among its supported devices ([Virtual Desktop](https://www.vrdesktop.net/)). |
| Standalone headset through ALVR | Windows or Linux, subject to ALVR/SteamVR support | Community-supported/tested route | ALVR owns trust/pairing and registers a SteamVR driver. Only show Ready after the ordinary OpenVR probe succeeds. ALVR's own guide requires SteamVR and tells the user to trust the headset in its PC dashboard ([ALVR installation guide](https://github.com/alvr-org/ALVR/wiki/Installation-guide)). |
| Pimax through Pimax Play/Pitool in SteamVR mode | Windows (test exact devices) | Expected in SteamVR mode | Complete headset/controller setup in Pimax software, start its SteamVR path, then test SrvSurvey. Pimax documents both device pairing and starting SteamVR in its utility ([Pimax connection manual](https://support.pimax.com/en/support/solutions/articles/60000812306-pitool-operation-manual)). |
| PimaxXR, OpenComposite, Virtual Desktop VDXR, or another direct OpenXR path that bypasses SteamVR | Windows or Linux depending on runtime | Unsupported for SrvSurvey overlays unless the runtime actually exposes and passes the required cross-process overlay extension | Detect or explain “OpenXR/direct mode bypasses SteamVR; switch the game/session to SteamVR to use SrvSurvey overlays.” Pimax itself describes PimaxXR + OpenComposite specifically as a way to run without SteamVR ([Pimax OpenXR guide](https://support.pimax.com/en/support/solutions/articles/60000907379-how-to-run-openxr-on-the-pimax-crystal)). |
| Windows Mixed Reality / Reverb through WMR for SteamVR | Windows 11 23H2 legacy only | Legacy, time-limited | Keep a guide entry with a retirement warning, not a normal recommended path. Microsoft removed WMR and WMR for SteamVR from Windows 11 24H2; retained 23H2 installations work with Steam only through November 2026 ([Microsoft removed features](https://learn.microsoft.com/en-us/windows/whats-new/removed-features)). |

Elite's current Steam store metadata identifies SteamVR as the VR runtime and
also names Oculus Rift in the recommended notes
([Elite Dangerous store page](https://store.steampowered.com/app/359320/Elite_Dangerous/)).
That supports a SteamVR-first overlay guide, but does not prove every community
launcher/runtime combination. Maintain exact tested combinations in the app or
release notes rather than claiming blanket compatibility.

The same store page lists only Windows requirements. A native Linux SrvSurvey
overlay backend is achievable, but the full Elite + Proton + SteamVR path must
be labeled experimental rather than presented as Frontier-supported.

## Desktop panel UX

Replace the current checkbox + process textbox with a small connection
assistant that remains useful after setup:

1. **Header:** “SteamVR overlays” with one master switch and a status badge.
2. **Connection summary:** separate rows for `SteamVR`, `Headset`, and
   `SrvSurvey overlays`, each with a concrete state and next action.
3. **Primary action:** context-sensitive `Install SteamVR`, `Start SteamVR`,
   `Check again`, `Show test overlay`, or `Adjust overlays`.
4. **Headset path:** “How is your headset connected?” with concise choices such
   as `Direct SteamVR`, `Meta Quest`, `Pimax`, `Wireless/standalone`, and
   `Windows Mixed Reality (legacy)`. Choices tailor help; they must not override
   the runtime probe or claim to pair hardware.
5. **Compatibility warning:** if a direct OpenXR/OpenComposite path is detected
   or selected, explain that it bypasses SteamVR and offer the SteamVR steps.
6. **Advanced disclosure:** resolved native-library path, native error symbol,
   optional Linux library override, and copied diagnostics. Do not put the
   process name in the normal workflow.
7. **Calibration:** unlock only after a test overlay is successfully published.
   Provide `I can see it`, `Reset headset origin`, and the existing per-overlay
   adjustment controls. Keep the visible test until the user confirms or
   cancels so “API call succeeded” is not mistaken for “user can see it.”

Recommended status language is operational: “SteamVR is not installed,”
“SteamVR is installed but not running,” “SteamVR is running; connect or wake
your headset,” “Connected; test overlay not yet confirmed,” and “Ready — 4
SrvSurvey overlays active.” Avoid raw enum names in the main card; include them
in copied diagnostics.

## VR guide section under Guides

Add one `VR & headset overlays` category with the following sections:

1. **What SrvSurvey connects to** — one diagram-equivalent explanation:
   `Headset/vendor streamer → SteamVR → Elite + SrvSurvey overlays`.
2. **Five-minute setup** — complete vendor pairing, start SteamVR, verify the
   headset is green/awake, enable SrvSurvey, show the test overlay, calibrate,
   then launch Elite in SteamVR mode.
3. **Choose your headset path** — the same capability matrix as the settings
   assistant, with Windows/Linux badges and tested/legacy/unsupported labels.
4. **Meta Quest** — separate Steam Link from Quest Link/Air Link; in both cases
   the required SrvSurvey route ends in SteamVR.
5. **Linux and wireless headsets** — state that Valve calls SteamVR for Linux a
   development release with limited hardware support; link ALVR as an advanced
   community route and keep distro-specific commands out of the core quick
   start.
6. **OpenXR/OpenComposite/direct-runtime mode** — explain why a game may run
   faster yet SrvSurvey overlays disappear, and tell the user to select the
   SteamVR path for overlay sessions.
7. **Test and calibration** — visibility confirmation, recenter/origin,
   per-overlay scale/position/rotation, vehicle-mode overrides, and how to exit
   adjustment mode safely.
8. **Elite-specific expectations** — ships and SRVs use full VR, while Odyssey
   on-foot play is shown on a projected flat screen, as Frontier specified
   ([Frontier Odyssey VR announcement](https://forums.frontier.co.uk/threads/odyssey-update-on-vr-and-ship-interiors.554223/)).
9. **Troubleshooting** — native client missing, runtime not installed, runtime
   stopped, headset absent/asleep, compositor ready but test invisible,
   OpenComposite detected, Linux custom library, and diagnostics to copy.
10. **WMR retirement notice** — the Microsoft dates above, clearly separated
   from current recommended setups.

Link to vendor instructions rather than cloning pairing procedures that change
outside SrvSurvey. The guide should be driven by the same platform-profile
catalog as the settings assistant so labels, support levels, steps, and links
cannot drift.

## Verification plan

Automated tests should cover:

- every runtime probe/error combination mapping to one stable connection state
  and primary action;
- process-name independence and bounded retry/backoff;
- Windows and Linux native-library resolution, including the Linux override;
- settings migration from the existing `Enabled` and process-name fields;
- each platform profile's support level, warning, steps, and official links;
- test-overlay lifecycle, visibility confirmation, cancellation, calibration
  gating, and cleanup;
- dirty-frame behavior and removal of stale overlays;
- localization catalog freshness for all new UI/guide text.

Manual acceptance needs at least Windows + SteamVR and Linux + SteamVR on real
hardware. Add Quest/Steam Link, Quest Link→SteamVR, and ALVR Linux only when
someone has actually exercised them. CI without a compositor should validate
the state machine with a fake runtime and should not pretend to verify headset
compatibility.

## Sources that were leads, not authority

The supplied Grok conversation was inspected for candidate links. Its claims
were not treated as instructions or evidence. The attached screenshot was used
only to identify the current panel's usability problem. Recommendations above
are grounded in the linked specifications, vendor documentation, source
repositories, and the checked SrvSurvey revision.
