# SrvSurvey-XP 2.1.3.0-rc.77.12

RC77.12 adds an opt-in cockpit-following overlay prototype for normal monitors,
perspective controls in the overlay editor, and independently updated camera
capture catalogs.

## What's changed

### Cockpit-following overlay panels

- **Follow normal cockpit freelook:** Enable the experimental cockpit-camera
  option in Global overlay settings to anchor overlay panels to the cockpit.
  The optional D3D11 bridge reads live camera/cockpit transforms; normal use
  does not require a VR headset or frame-analysis captures.
- **Calibrate the centered view:** Center cockpit freelook and select the
  calibration control. Camera availability and calibration status are shown
  separately from capture-catalog update status.
- **Keep a usable fallback:** If verified camera data is unavailable or stale,
  panels return to their ordinary desktop placements. Disabling the option
  stops capture after its short receiver lease expires.

### Perspective controls in the overlay editor

- **Tilt and rotate individual panels:** Use the rotation orb or numeric yaw,
  pitch, and roll controls. Drag the orb center to tilt and its rim to roll.
- **Adjust apparent distance:** Move a panel farther away to reduce its apparent
  size. Perspective settings follow the editor's existing save/cancel flow.
- **Keep settings on this computer:** Cockpit mode and panel angles are
  machine-specific, use the application's theme resources, and retain existing
  overlay visibility controls.

### Independent capture-catalog updates

- **Refresh at launch:** While cockpit mode is enabled, SrvSurvey checks the
  versioned GitHub catalog and caches newer valid revisions locally. Restart
  Elite to load a refreshed catalog.
- **Update layouts without rebuilding:** The catalog describes shader identity,
  buffer bindings, sizes, strides, offsets, numeric formats, matrix conventions,
  and detection tolerances. Supported source changes can be delivered without
  rebuilding the application or bridge DLL.
- **Allow future bridge-only upgrades:** The application preserves future
  declarative layout fields. The bridge validates reader compatibility and
  resource bounds, and falls back to a compatible predecessor or installed
  profile when a catalog is unsupported. Each reader retains its supported source
  across successive future-layout updates. New capture algorithms may require
  a replacement DLL; communication-protocol changes may also require an app update.
- **Retain working configuration:** Failed checks, malformed content,
  downgrades, and changed content without a revision bump do not replace the
  current cache. Downloads are bounded and installed bridge files stay unchanged.

## Prototype setup and validation limits

Cockpit mode is off by default. The optional CameraBridge archive contains the
Windows x64 DLL for Windows or Linux/Proton, experimental capture profile,
reversible installer, and setup instructions. Exit Elite before installing or
removing the bridge; the installer preserves the existing EDHM DLL and backs up
its configuration. The application does not install the bridge automatically.

Recorded cockpit views and an isolated GPU harness validate the camera math,
freelook response, context recovery, and receiver expiry. The standalone Wine
harness still encounters a graphics-cleanup failure that also reproduces with
capture observation disabled. Actual Elite/EDHM startup, normal rendering,
clean game exit, other ships, and camera modes still require in-game validation.
