# SrvSurvey-XP 2.1.3.0-rc.54

RC54 modernizes SrvSurvey's SteamVR overlays for Windows and Linux. It adds a
guided headset connection workflow, clearer runtime status, per-platform setup
profiles, controller-pointer interaction, safer runtime recovery, and a new VR
guide inside the application. This release also includes Linux update and UI
placement fixes found during RC53 testing.

## Bug fixes in RC54

- Linux AppImage updates now launch their handoff helper in an isolated systemd
  user service when needed, so closing the old instance does not also terminate
  the helper before it can install and start the replacement.
- Resized overlay panels now retain their saved top-left position when the
  overlay editor is closed and reopened on Windows and Linux.
- Linux dropdowns now open against their controls in every workspace, including
  Guardian, while keeping embedded popups enabled for reliable hover, input,
  and focus behavior.

> [!IMPORTANT]
> SrvSurvey VR panels use the SteamVR/OpenVR overlay compositor. OpenXR-only
> Elite Dangerous sessions cannot host these external overlays. Meta Quest and
> Rift users must connect through a route that presents the headset to SteamVR.

## Connect a headset

- Open **Settings > Global overlays > VR overlays** and choose the route that
  matches the active headset connection.
- Native SteamVR headsets can use **SteamVR headset**. Meta users can choose
  **Link / Air Link**, **Steam Link**, **Virtual Desktop**, or experimental
  **ALVR**, then follow the route-specific pairing guidance in the panel.
- Meta Link, Air Link, Steam Link, and Virtual Desktop PC-VR are Windows routes.
  ALVR can bridge a Meta headset on Windows or Linux, but its Linux path,
  SteamVR on Linux, and Elite through Proton remain experimental.
- Windows Mixed Reality is offered only as a legacy Windows route for systems
  where the headset and SteamVR bridge still work.
- A custom OpenVR runtime can be selected when its compositor implements the
  OpenVR overlay API. OpenComposite is not an overlay compatibility path.

The selected profile supplies the expected runtime process and concise pairing
steps. Changing profiles does not erase saved panel placement or vehicle-mode
calibration.

## Windows and Linux packaging

- Windows and Linux packages include the native OpenVR client library used by
  SrvSurvey. Users do not need to locate SteamVR's library or configure a
  custom library path.
- If packaged OpenVR support is reported unavailable, reinstall the package for
  the correct architecture rather than pointing SrvSurvey into the Steam
  installation.
- The same overlay publisher, calibration model, and connection states are used
  on both operating systems.

## Runtime status and recovery

- Enabling VR overlays now follows explicit **Waiting**, **Connecting**,
  **Connected**, and **Needs attention** states.
- **Check connection** retries after starting SteamVR, changing a headset
  bridge, or repairing a headset connection.
- SrvSurvey distinguishes a missing runtime or headset from an individual
  overlay rejected by the compositor and provides an appropriate recovery
  action for each case.
- Runtime or profile changes reconnect through the explicit connection check;
  disabling VR still shuts down and removes overlays immediately.
- Interaction-mode changes are transactional across every live panel. If one
  update fails, panels already changed are restored to their previous input
  mode. A failed rollback shuts down OpenVR instead of leaving mixed interactive
  and click-through panels behind.

## Panel calibration

- **Adjust overlays** opens the existing per-panel VR calibration workflow once
  the runtime is connected.
- Each panel retains its own default placement plus ship, SRV, fighter, taxi,
  and on-foot overrides.
- Scale, position, pitch, yaw, and roll update the headset preview. Save verifies
  and preserves the calibration; Cancel restores saved values; Reset selected
  restores the shipped placement for that target.
- **Reset VR orientation** captures the current headset yaw without changing
  saved panel placement.
- Desktop overlay placement and VR calibration remain independent.

## Controller-pointer interaction

- Assign **Toggle VR overlay interaction** in **Settings > Global overlays**.
  It appears directly below the existing live-overlay interaction shortcut.
- With VR overlays connected, the shortcut enables SteamVR controller-pointer
  movement, clicks, and scrolling for controls already visible inside live
  SrvSurvey panels.
- Use the shortcut again to restore passive click-through behavior. Interaction
  is also cleared automatically when VR overlays are disabled, disconnected,
  or disposed.
- Desktop live-overlay interaction remains a separate shortcut for desktop
  dragging and does not enable VR controller input.

## In-app VR guide

The new **VR & headset overlays** category under **Guides** covers:

- choosing and pairing the correct SteamVR connection route;
- Meta headset compatibility and the limitations of OpenXR-only sessions;
- Windows and Linux package behavior;
- panel calibration and orientation reset;
- assigning and using controller-pointer interaction; and
- troubleshooting missing runtimes, rejected overlays, and panels hidden by
  normal game-context visibility rules.

## Update channel

- RC51 remains the permanent `xp-v2.1.3.0-rc.51` compatibility bridge for older
  clients.
- RC54 publishes as `xp2-v2.1.3.0-rc.54` with the schema-2 Windows, Linux
  portable, and Linux AppImage package index.
- RC51 and later clients scan the `xp2-v` namespace and can move directly to
  RC54. No release above RC51 may use the legacy `xp-v` namespace.

## Packaging

- Version: `2.1.3.0-rc.54`
- Tag: `xp2-v2.1.3.0-rc.54`
- Release-index schema: `2` (`win-x64`, `linux-x64`, and
  `linux-x64-appimage`)
- Windows: `SrvSurvey-XP-2.1.3.0-rc.54-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.54-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.54-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.54-x86_64.AppImage.zsync`

Windows and Linux packages remain self-contained. The numeric Windows
`FileVersion` remains `2.1.3.0`.

## Testing notice

> [!IMPORTANT]
> This remains a work-in-progress preview for testing. Keep a backup of your
> existing SrvSurvey data and report unexpected behavior through the project
> issue tracker.
