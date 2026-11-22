# WiVRn and Monado overlays on Linux

SrvSurvey can publish VR overlay panels directly into **WiVRn** and **Monado**
sessions on Linux without SteamVR and without injecting a layer into Elite
Dangerous.

## How it works

```text
SrvSurvey.Desktop (Avalonia panels -> bounded RGBA frames)
  -> libSrvSurveyLinuxOpenXrOverlay.so (XR_EXTX_overlay + headless Vulkan)
  -> libopenxr_loader.so.1 -> WiVRn (wivrn-server) or Monado (monado-service)
  -> Headset
```

- **No SteamVR or OpenVR overlay collision:** Elite Dangerous on Linux/Proton
  with WiVRn typically uses **xrizer** or **OpenComposite** to translate the
  game's OpenVR calls to OpenXR. Those translation shims do not implement
  cross-process OpenVR overlays (`VRApplication_Overlay` / `IVROverlay`) and
  probing them through SteamVR/OpenVR can crash the active game. SrvSurvey
  blocks SteamVR/OpenVR probing when `xrizer`, `OpenComposite`, `wivrn-server`,
  or `monado-service` is active without `vrserver`.
- **Native OpenXR overlay session:** Both WiVRn and Monado implement Monado's
  `XR_EXTX_overlay` compositor extension. SrvSurvey opens an overlay
  `XrSession` with `XrSessionCreateInfoOverlayEXTX` and a headless Vulkan
  context (`XR_KHR_vulkan_enable2`, with fallback to `XR_KHR_vulkan_enable`),
  submitting each panel as an `XrCompositionLayerQuad` with straight-alpha
  blending.
- **Zero hard install dependencies:** `libSrvSurveyLinuxOpenXrOverlay.so` loads
  `libopenxr_loader.so.1` and `libvulkan.so.1` dynamically at runtime so
  SrvSurvey runs normally on Linux systems without VR packages installed.

## Prerequisites on Linux (CachyOS / Arch / Fedora / Ubuntu)

1. Install your distribution's OpenXR loader (`openxr` / `libopenxr_loader.so.1`)
   and Vulkan loader (`vulkan-icd-loader` / `libvulkan.so.1`).
2. Start **WiVRn** (`wivrn-server`) or **Monado** (`monado-service`) and confirm
   it is your active OpenXR runtime (`~/.config/openxr/1/active_runtime.json` or
   `XR_RUNTIME_JSON`).
3. In SrvSurvey, open **Theme > Overlay setup > VR**, select **WiVRn (Linux
   OpenXR, experimental)** or **Monado (Linux OpenXR, experimental)**, and
   enable VR overlays.
4. Use **Adjust overlays**, **Place in front of me**, or **Reset VR
   orientation** to position cockpit panels. WiVRn and Monado use seated
   cockpit defaults and store placements separately under `vr/wivrn` and
   `vr/monado`.

## Building locally

Build and test the native helper with CMake 3.24+ and a C++20 compiler:

```bash
cmake -S src/SrvSurvey.OpenXrLayer -B artifacts/openxr-build -DCMAKE_BUILD_TYPE=Release
cmake --build artifacts/openxr-build
ctest --test-dir artifacts/openxr-build --output-on-failure
cmake --install artifacts/openxr-build --prefix artifacts/openxr-linux
```

Linux packages bundle `OpenXR/libSrvSurveyLinuxOpenXrOverlay.so` automatically.
For custom testing, `SRVSURVEY_LINUX_OPENXR_LIBRARY` can override the library
path.

## Current limits and headset acceptance checks

- Controller-pointer interaction is not yet implemented on Linux OpenXR
  (overlays remain click-through; calibrate from the desktop VR settings or
  mirror the window if needed).
- Automated CTest and xUnit suites exercise the OpenXR/Vulkan state machine and
  safety guards with mock runtimes; live CachyOS + WiVRn + Proton/xrizer
  rendering still requires real-headset acceptance testing:
  - Confirm selecting **SteamVR headset** while WiVRn/xrizer is active refuses
    to touch OpenVR and does not crash Elite Dangerous.
  - Switch to **WiVRn (Linux OpenXR, experimental)**, enable overlays, and
    confirm panels appear cleanly with transparent edges, accurate colors, and
    proper cockpit placement.
  - Verify **Place in front of me**, **Reset VR orientation**, hiding/showing
    panels, and cleanly disconnecting/reconnecting WiVRn.
