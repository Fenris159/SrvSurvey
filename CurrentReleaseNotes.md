# SrvSurvey-XP 2.1.3.0-rc.77.28

RC77.28 adds Linux OpenXR controller pointer interaction for WiVRn and Monado,
defers Linux OpenXR attachment until Elite Dangerous is running, switches
commanders automatically in-process without restarting, and overhauls VR overlay
calibration with category-wide preview, direct VR panel dragging, 45° snapped
calibration-window placement, and desktop-aligned default positions across VR
runtimes.

## Before upgrading

Exploration history uses schema 6, introduced in RC77.21. RC77.20 cannot open
that catalog. Keep a pre-upgrade copy of your data folder if you may need to
roll back to RC77.20 or earlier. If journal ordering cannot be verified,
automatic exploration value tracking pauses until history recovery or a new
explicit reset.

## What's changed

- **Linux OpenXR controller pointer interaction (WiVRn and Monado)** adds
  OpenXR aim, trigger, secondary/middle click, and thumbstick/trackpad scroll
  actions to `libSrvSurveyLinuxOpenXrOverlay.so` with a rendered laser cursor,
  letting you click, scroll, and adjust VR overlays directly in the headset
  across Oculus Touch, Valve Index, Microsoft Motion, and HTC Vive controller
  profiles (with aim and primary select on KHR Simple Controller unless the
  runtime supplies additional bindings).
- **Deferred WiVRn and Monado session attachment** waits until Elite Dangerous
  is running before opening a Linux OpenXR overlay session and releases the
  session as soon as the game exits, keeping WiVRn's lobby usable before launch
  and across game restarts without manually turning overlays off and back on.
- **Automatic in-process commander switching** detects when you log out and
  connect as another commander while **Commander selection** is set to
  **Automatic**, reloading per-commander workspaces and isolated settings in
  place without restarting SrvSurvey while still blocking steals during
  multi-client sessions or active profile sync.
- **Topmost calibration controls and steady VR pointer focus** always renders
  and hit-tests `PlotAdjustVR` above preview panels, keeps selected previews
  above sibling panels, and preserves active controller pointer interaction
  across steady synchronize ticks and unrelated overlay updates.
- **Snapped, camera-facing VR calibration window** centers the in-headset
  calibration panel in front of you and snaps it to five 45° front-hemisphere
  sectors (`-90°`, `-45°`, `0°`, `+45°`, `+90°`) with yaw hysteresis and a
  20 cm movement deadzone so the controls stay steady while you look around or
  drag panels.
- **Direct VR panel drag mode** adds a highlighted **Drag panels in VR** toggle
  above **Close / discard unsaved**: point at any visible preview panel, hold
  trigger to drag it along its plane, and tilt the thumbstick to adjust depth.
- **Overlay category preview in VR calibration** mirrors the desktop overlay
  editor's categories so choosing a category shows and edits all overlays in
  that category together, filters **Calibration Target** to that category, and
  saves multi-panel adjustments in one click.
- **Compact VR calibration panel & sensible VR defaults** tightens the
  calibration layout into a smaller cockpit footprint while preserving VR hit
  targets, and replaces scattered negative-yaw / oversized factory VR presets
  with desktop-mirrored curved-cockpit defaults for all 42 overlays across
  OpenXR and SteamVR.
- **Scrollable settings layout stability** eliminates Avalonia layout-cycle
  warnings when resizing or switching tabs in **Theme → Overlay setup**,
  **Settings**, and Colonization system-sites tables by replacing cyclic
  `Viewport.Width` bindings with `ResponsiveScrollViewer`.
- **Clearer Journey exploration rewards formatting** removes the leading `~`
  from **Exploration rewards** values in **Navigation → Journeys** so credit
  estimates are not mistaken for negative numbers.
- **Calmer, deduplicated EDSM and Spansh lookups** stops Galaxy Map status
  polling from cancelling and refiring in-flight lookups, adds short-term
  caching and `429` rate-limit cooldowns in `SystemSummaryClient`, treats
  Spansh `404` responses for undiscovered systems as normal instead of errors,
  and aggregates overlay provider warnings to a single calm status notice per
  provider.
- **Expanded startup and screenshot diagnostics** logs the full prerelease
  version string at startup and records screenshot processing trigger, source
  directory, conversion, and skip reasons to the application log for easier
  troubleshooting.
