# SrvSurvey-XP 2.1.3.0-rc.77.26

RC77.26 adds experimental Linux OpenXR VR overlay support for WiVRn and Monado
and adds a Linux OpenVR safety guard to prevent crashes when OpenVR shims such
as xrizer or OpenComposite are active.

## Before upgrading

Exploration history uses schema 6, introduced in RC77.21. RC77.20 cannot open
that catalog. Keep a pre-upgrade copy of your data folder if you may need to
roll back to RC77.20 or earlier. If journal ordering cannot be verified,
automatic exploration value tracking pauses until history recovery or a new
explicit reset.

## What's changed

- **WiVRn and Monado (Linux OpenXR, experimental)** VR platform profiles are
  now available on Linux, using a packaged native OpenXR (`XR_EXTX_overlay` +
  Vulkan) helper library (`libSrvSurveyLinuxOpenXrOverlay.so`) to project HUD
  panels directly over active OpenXR sessions with head-pose tracking and
  origin reset support.
- **Linux OpenVR shim crash guard** inspects `SRVSURVEY_OPENVR_LIBRARY`,
  `VR_OVERRIDE`, `VR_PATHREG_OVERRIDE` / `openvrpaths.vrpath`, and running
  WiVRn/Monado/SteamVR services before probing OpenVR, blocking unsafe probes
  that could crash games running through xrizer or OpenComposite and directing
  you to the Linux OpenXR route instead.
- **VR Overlay settings and setup guides** now surface Linux OpenXR runtime
  preflight diagnostics, per-platform calibration storage, and a dedicated
  Linux WiVRn/Monado setup guide.
