# SrvSurvey-XP 2.1.3.0-rc.15

This release candidate improves live overlay mouse interaction and Nomad
session recovery. The changes below are the delta from `2.1.3.0-rc.14`.

## What's fixed since 2.1.3.0-rc.14

- Restores Nomad identity from `LoadGame` and SRV embark events so vehicle and
  landing-gear behavior remains correct after starting or resuming a session
  inside the Nomad.
- Keeps the pointer visible when Windows live-overlay mouse interaction starts,
  then restores the previous foreground window without stealing focus from a
  different application.
- Adds equivalent best-effort X11 and XWayland cursor activation, explicit
  overlay cursors, and safe focus restoration without releasing the game's
  pointer grab.
- Reports live-overlay mouse interaction entry and exit through the
  Notifications panel.

## Packaging

- Version: `2.1.3.0-rc.15`
- Tag: `xp-v2.1.3.0-rc.15`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.15-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.15-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.15-x86_64.AppImage`

The Windows and Linux packages are self-contained. AppImages must be updated
manually; the application links directly to the selected XP release.

## Testing notice

> [!IMPORTANT]
> This remains a work-in-progress preview for testing. Keep a backup of your
> existing SrvSurvey data and report unexpected behavior through the project
> issue tracker.

Native overlay behavior should still be exercised with Elite Dangerous on
clean Windows, X11, and XWayland systems. Pure native Wayland is not yet a
full-functionality overlay target.
