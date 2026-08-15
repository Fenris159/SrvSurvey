# SrvSurvey-XP 2.1.3.0-rc.24

This release candidate corrects biology-overlay body selection. The changes
below are the delta from `2.1.3.0-rc.23`.

## What's fixed since 2.1.3.0-rc.23

- Prevents a DSS-complete or nearby body with zero biological signals from
  appearing as "Identified Bio" when another body in the system has biological
  signals. The overlay remains on the system overview and continues to include
  every body that actually reported biological signals, regardless of scan
  count or scan order.

## Packaging

- Version: `2.1.3.0-rc.24`
- Tag: `xp-v2.1.3.0-rc.24`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.24-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.24-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.24-x86_64.AppImage`

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
