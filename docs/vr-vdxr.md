# VDXR overlays on Windows

SrvSurvey can publish overlay panels and return Quest controller input into an Elite Dangerous OpenXR
session using Virtual Desktop's VDXR runtime, without SteamVR in the rendering
path. This integration requires Windows x64 and Direct3D 11. Physical
Quest 3 validation is still required before treating it as supported.

## Version target

The integration targets the latest stable Virtual Desktop app available for
Quest 3 through the [Meta store](https://www.meta.com/experiences/virtual-desktop/2017050365004772/),
paired with the normal Windows Streamer download from
[Virtual Desktop](https://www.vrdesktop.net/) and its bundled VDXR runtime.
Use the current OpenComposite `openxr` build when Elite needs OpenVR-to-OpenXR
translation. Beta channels and older-version downgrades are not prerequisites.

At the 2026-10-14 source check, the official Windows Streamer installer reports
version **1.34.22.0**. That is a dated compatibility target, not a requirement
to stay on that version after stable updates. Record the actual Quest app,
Streamer, and OpenComposite versions when performing headset acceptance checks;
those results establish which installed combination has been verified.

## Try the integration

1. Use an Elite setup that already works with VDXR. Elite's OpenVR-to-OpenXR
   translation, if used, remains part of that game setup. Confirm Virtual
   Desktop's performance overlay actually reports **Runtime: VDXR**.
2. Extract the complete Windows SrvSurvey package into a permanent folder.
   In its `OpenXR` folder, run the registration script with 64-bit PowerShell:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\Install-OpenXrLayer.ps1
   ```

   Registration uses your Windows account's OpenXR implicit-layer registry key;
   administrator access is unnecessary. Register again after moving the package
   or changing versions. The script retires older SrvSurvey registrations.
   Launch Elite normally, without **Run as administrator**: OpenComposite's
   bundled OpenXR loader ignores per-user layer registrations in an elevated
   game process. The same applies if an elevated launcher starts Elite.
3. Restart Elite so its OpenXR loader discovers the layer. In SrvSurvey's VR
   settings, choose **Meta Quest — VDXR** and enable VR overlays.
   The route menu keeps every option visible. Unsupported options are dimmed and
   cannot be selected, with a reason such as **Windows only**. VDXR requires an
   x64 process; Windows Mixed Reality is unavailable on Windows 11 24H2 or newer.
   On Linux, SteamVR, ALVR, and custom OpenVR remain selectable. A saved
   incompatible route is still shown, but overlays are off until an available
   route is chosen. Runtime installation and headset readiness are checked after
   choosing a compatible route.
4. Wait for the connection status to identify an active VDXR session. Trigger
   a visible gameplay panel, then adjust its saved VR position, rotation, scale,
   and opacity. **Reset headset orientation** captures a new placement origin.

Enable global keyboard input and use **Toggle VR overlay interaction** to
activate the Quest controller cursor. Release any held buttons first, then point
at a panel. Trigger is left-click, grip is right-click, thumbstick press is
middle-click, and thumbstick tilt scrolls vertically or horizontally. Either
controller can operate the single pointer. Use the shortcut again to restore
click-through mode and return these controller inputs to Elite. Interaction is
limited to controls already exposed by each panel; placement still uses the
calibration controls.

Only `EliteDangerous64.exe` with
the actual runtime name `VirtualDesktopXR` receives panels. Other processes and
runtimes are forwarded without overlay rendering.

## Panel appearance and shortcuts

VDXR receives a bitmap of the same live Avalonia panel presentation used by the
desktop overlay host and the SteamVR route. Fonts, colors, backgrounds, borders,
layout, and in-panel content therefore follow the existing overlay style
settings. The layer preserves RGB bytes and the panel's aspect ratio; it applies
the saved opacity once and submits a flat OpenXR quad for both eyes. It does
not curve, stretch, or redesign the panel.

The VR calibration controls set that quad's physical width, position, and
rotation. Viewing it at an angle produces ordinary perspective foreshortening.
Text sharpness and perceived color can still differ with captured pixel
resolution, angular size, runtime filtering, headset optics, and Virtual Desktop
streaming settings. This path currently captures panels every 250 ms; it is not
an independent headset-rate animation renderer. The compositor presents the
latest texture each game frame.

### 3D placement and perspective

A flat panel can still be angled in the headset. Its image stays rectangular;
the runtime projects its position and rotation into each eye's view. Moving it
to the side, tilting it toward the pilot, or changing its distance gives normal
stereo perspective. The source image does not need an artificial perspective
warp or curved geometry
([OpenXR quad geometry](https://registry.khronos.org/OpenXR/specs/1.1/man/html/XrCompositionLayerQuad.html)).

Both implemented backends, SteamVR/OpenVR and VDXR/OpenXR, apply the same saved
per-panel calibration. In **Settings > Global overlays > VR overlays**, choose
**Adjust overlays**, select a panel and vehicle/game mode, then change:

- **Position X / Y / Z** to move it sideways, vertically, and nearer or farther.
- **Pitch / yaw / roll** to tilt, turn, and rotate it.
- **Scale** to change its physical size while preserving its image aspect ratio.

Changes preview in the connected VR session. Save preserves the calibration for
that panel and mode. Editing a panel's 3D placement and clicking controls inside
that panel are separate operations.

Recenter captures the headset's position and heading. Looking down or tilting
your head during reset does not tilt the whole panel arrangement; each panel's
saved pitch and roll still apply.

**Toggle VR overlay interaction** uses the selected SteamVR or VDXR backend. On
VDXR the native layer adds its own OpenXR actions alongside OpenComposite's game
actions, intersects controller aim rays with the submitted panels, draws a
cursor, and returns clicks and scrolling to the existing Avalonia controls.
Aim tracking has normal priority; buttons and scrolling use a higher-priority
set only while interaction is enabled. Release before enabling or returning
from tracking loss so a held trigger cannot activate a panel accidentally.
Losing focus, tracking, a panel, or the publisher cancels captured input.

The input-ready handshake requires the matching native layer. If the shortcut
reports waiting for controller input, update the complete SrvSurvey package,
register its layer again, and restart Elite. The desktop live-overlay
interaction shortcut still controls desktop windows only.

**Adjust VR overlays** and **Reset VR orientation** keep their existing shortcut
bindings and work through the selected backend. Enable global keyboard input,
connect the VR route, and use the configured shortcuts. In VDXR, orientation
reset captures a new reference pose for the panels; it does not reset Elite's
own headset origin.

Only one SrvSurvey instance can own the frame publisher at a time; disable its
VR overlays before enabling the VDXR route in another instance. The layer stops
showing abandoned frames after two seconds without a fresh publication.

To remove registration, run the same script from the registered package:

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-OpenXrLayer.ps1 -Uninstall
```

Restart Elite to unload the layer. For troubleshooting, starting Elite with the
environment variable `SRVSURVEY_OPENXR_DISABLE` present disables this layer.
The OpenXR loader checks whether this variable exists, including a value of `0`.

## Implementation and build

```text
SrvSurvey panel renderer + saved calibration
  -> versioned frame mailbox (one writer, bounded RGBA frames)
  -> SrvSurvey OpenXR API layer inside Elite
  -> D3D11 quad composition layers in the game's existing frame submission
  -> VDXR -> Quest
```

The publisher uses a memory-mapped file under the current user's local app-data
folder, `SrvSurvey/OpenXR/frames.bin`. Its fixed 64 MiB capacity supports up to
16 panels. A sequence counter rejects partial writes; heartbeat expiry removes
stale content. A separate header region carries the layer's session health.
The layer caches unchanged snapshots/textures and follows Elite's frame loop.
It preserves game layers and skips panels beyond the runtime's layer limit.
Local overlay errors forward the original game frame and report an error.
Panel retirement, resizing, and shutdown wait for pending D3D11 uploads before
destroying owned swapchains, as required by OpenXR. Normal frame submission
uses a zero-timeout image wait and retries pending acquisitions next frame.

Build with Visual Studio C++ tools, the Windows SDK, and CMake 3.24 or newer:

```powershell
cmake -S src/SrvSurvey.OpenXrLayer -B artifacts/openxr-build -A x64
cmake --build artifacts/openxr-build --config Release
ctest --test-dir artifacts/openxr-build -C Release --output-on-failure
cmake --install artifacts/openxr-build --config Release --prefix artifacts/openxr-layer
dotnet publish src/SrvSurvey.Desktop/SrvSurvey.Desktop.csproj -c Release -r win-x64 --self-contained true -o artifacts/win-x64
```

The SDK headers are fetched from Khronos OpenXR SDK 1.1.63 with an archive SHA-256
pin. Windows release builds package the DLL, manifest, registration script, and
SDK license. Publishing a Windows package requires the native layer build.
Native tests use a fake OpenXR runtime and a software D3D11 device. Packet
validation and pointer geometry/state tests also run on Linux.

Controller input uses a separate 4 KiB `frames.bin.input` mailbox. The desktop
publishes the enabled flag and a generation token; the layer acknowledges that
token and writes a bounded ring of 64 pointer events under a sequence counter.
The reader cancels capture on stale health, torn events, invalid values, or ring
overflow, then discards those events. It never replays uncertain clicks. Input
is sampled from the game's existing action synchronization; OpenComposite
synchronizes after frame submission, so the next frame reads that state. The
cursor updates at game-frame rate even though panel images refresh every 250 ms.
A cursor reserves one composition-layer slot while interaction is enabled.

The layer appends actions to the game's attach/sync calls and appends Touch
bindings to the game's suggestions. It preserves the original arrays and
extension chains. OpenComposite's discovery-session restart receives a fresh
action generation; graphics-only session restarts reuse existing actions.
Input initialization failures leave the rendering path available and preserve
the original game calls.

## Headset acceptance checks

- Confirm the performance overlay says VDXR while SteamVR is closed, and that
  SrvSurvey waits when Elite is closed or its layer is unregistered.
- Check live panel changes, transparent text edges, opacity, aspect ratio,
  independent placement, vehicle overrides, and explicit recentering.
- Compare the same live panel's theme, content, border, and transparency on the
  desktop and in the headset. Check wide/tall panels and angled placement.
- Verify Adjust VR overlays, Reset VR orientation, and Toggle VR overlay
  interaction with global keyboard input enabled. Test both controllers,
  trigger/grip/stick clicks, both scroll axes, and dragging outside a panel.
- Hold the trigger while enabling interaction, hide a captured panel, lose
  controller tracking, open the Virtual Desktop dashboard, and disconnect the
  publisher. Verify cancellation, release-before-rearming, and no stuck click.
- Verify controller controls return to Elite when interaction is off, and that
  HOTAS/keyboard inputs continue during interaction. Test OpenComposite's early
  profile-discovery session and later session recreation.
- Open/close the Virtual Desktop dashboard; verify panels keep their placement.
- Hide a panel, disable VR overlays, close SrvSurvey, and restart Elite; verify
  stale panels disappear and reconnection works.
- Compare CPU/GPU frame times with the same game, headset, and streaming settings
  with overlays disabled/enabled. Record the panel count and render dimensions.
- Verify the SteamVR route still works after switching back.

See [source research and OpenXR contracts](research/vdxr-overlay-compatibility.md)
for the difference between this in-session API layer and a standalone OpenXR
overlay application.
