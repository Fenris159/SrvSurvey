# VDXR overlays on Windows

SrvSurvey can publish passive overlay panels into an Elite Dangerous OpenXR
session using Virtual Desktop's VDXR runtime, without SteamVR in the rendering
path. This integration requires Windows x64 and Direct3D 11. Physical
Quest 3 validation is still required before treating it as supported.

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
4. Wait for the connection status to identify an active VDXR session. Trigger
   a visible gameplay panel, then adjust its saved VR position, rotation, scale,
   and opacity. **Reset headset orientation** captures a new placement origin.

Controller-pointer interaction is available on the SteamVR route. This first
VDXR implementation displays passive panels. Only `EliteDangerous64.exe` with
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

**Toggle VR overlay interaction** currently requires the SteamVR backend's
controller-pointer events. The shortcut is registered on VDXR, but its backend
rejects the request with a status message and provides no controller clicks or
scrolling. OpenXR quad composition does not supply pointer events automatically;
VDXR interaction would require separate input actions, ray hit testing, and an
input return channel to SrvSurvey. The desktop live-overlay interaction shortcut
still controls desktop windows only.

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
Native tests use a fake OpenXR runtime and a software D3D11 device. The packet
validation tests also run on Linux.

## Headset acceptance checks

- Confirm the performance overlay says VDXR while SteamVR is closed, and that
  SrvSurvey waits when Elite is closed or its layer is unregistered.
- Check live panel changes, transparent text edges, opacity, aspect ratio,
  independent placement, vehicle overrides, and explicit recentering.
- Compare the same live panel's theme, content, border, and transparency on the
  desktop and in the headset. Check wide/tall panels and angled placement.
- Verify Adjust VR overlays and Reset VR orientation with global keyboard input
  enabled. Toggle VR overlay interaction must explain its SteamVR requirement
  and leave the VDXR panels passive.
- Open/close the Virtual Desktop dashboard; verify panels keep their placement.
- Hide a panel, disable VR overlays, close SrvSurvey, and restart Elite; verify
  stale panels disappear and reconnection works.
- Compare CPU/GPU frame times with the same game, headset, and streaming settings
  with overlays disabled/enabled. Record the panel count and render dimensions.
- Verify the SteamVR route still works after switching back.

See [source research and OpenXR contracts](research/vdxr-overlay-compatibility.md)
for the difference between this in-session API layer and a standalone OpenXR
overlay application.
