# SrvSurvey-XP 2.1.3.0-rc.34

This release candidate hardens Linux overlay and global-input native lifecycles.
The changes below are the delta from `2.1.3.0-rc.33`.

## What's changed since 2.1.3.0-rc.33

- Scopes Xlib protocol-error handling to SrvSurvey-owned display connections
  and treats only expected stale-window, focus, and capture races as benign.
  X11 windows disappearing while passive panels or the overlay editor change no
  longer fall through to Xlib's fatal default handler.
- Clips X11 game-screen captures to the desktop root bounds and reports an
  unavailable capture when the Elite window is outside the desktop instead of
  issuing an invalid `XGetImage` request.
- Serializes SDL controller initialization and shutdown on Avalonia's UI thread
  and awaits the controller worker before releasing SDL or its game-window
  tracker.
- Serializes SharpHook start, restart, and shutdown so only one native keyboard
  hook can exist at a time. Shutdown now drains in-flight keyboard callbacks
  before closing the X11 tracker, and settings publish input bindings atomically.
- Initializes Xlib threading before Avalonia and SharpHook on X11/XWayland and
  serializes X11 tracker queries against display closure.
- Adds regression coverage for X11 error-policy decisions, off-screen capture
  clipping, controller cancellation, and keyboard callback/restart races.

## Packaging

- Version: `2.1.3.0-rc.34`
- Tag: `xp-v2.1.3.0-rc.34`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.34-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.34-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.34-x86_64.AppImage`

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
