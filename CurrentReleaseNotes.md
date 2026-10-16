# SrvSurvey-XP 2.1.3.0-rc.64.9

RC.64.9 fixes a freeze when Wayland screen capture needs permission again or
you choose another capture source in Settings. It retains the VR overlays,
Spansh route planning and other improvements from RC.64.8.

## What's changed

- **Keep the application responsive during Wayland screen sharing.** Capture
  availability checks no longer wait for a background capture that needs you
  to answer a permission prompt. This prevents the confirmation dialog and
  main window from freezing while capture waits for approval.
- **Choose another capture source without blocking the interface.** Settings
  closes the old capture session in the background before opening the desktop
  source picker. Capture stays paused until the new source has been selected.
- **Recover from an unresponsive desktop portal.** Portal startup calls can be
  canceled and have bounded waits, including request cleanup. The desktop
  source picker still gives you time to make your selection.

A desktop can ask for screen-sharing approval again when its saved permission
or previous source is no longer usable. This release fixes the application's
handling of that flow; it does not change your desktop's permission policy.

## Update channel and packages

RC.64.9 uses the schema-2 `xp2-v` development channel. RC51 remains the legacy
compatibility bridge. Windows and Linux packages are self-contained.

- Version: `2.1.3.0-rc.64.9`
- Tag: `xp2-v2.1.3.0-rc.64.9`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.64.9-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.64.9-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.64.9-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.64.9-x86_64.AppImage.zsync`

The numeric Windows `FileVersion` remains `2.1.3.0`.

## Testing notice

This remains a development preview. The freeze was reproduced with isolated
portal probes against both RC.64.7 and RC.64.8; it was an existing capture
lifecycle bug. The affected user's CachyOS session was unavailable for testing,
so the reason their desktop requested permission again remains unconfirmed.
