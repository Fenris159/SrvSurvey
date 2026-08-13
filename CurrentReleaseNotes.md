# SrvSurvey-XP 2.1.3.0-rc.20

This release candidate aligns route-overlay timing and progress with legacy
SrvSurvey. The changes below are the delta from `2.1.3.0-rc.19`.

## What's improved since 2.1.3.0-rc.19

- Keeps the completed jump content visible for one second before advancing to
  the next target for both normal and fleet-carrier jumps.
- Excludes the starting system from saved-route hop totals and labels the
  initial route position as `START`.
- Displays saved-route progress as `HOP 1 / total` through the final
  destination for normal and fleet-carrier routes.
- Shows `FINISHED` with the final destination details for three seconds after
  completing a route, then closes the overlay.

## Packaging

- Version: `2.1.3.0-rc.20`
- Tag: `xp-v2.1.3.0-rc.20`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.20-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.20-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.20-x86_64.AppImage`

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
