# SrvSurvey-XP 2.1.3.0-rc.22

This release candidate improves the Next-jump information overlay. The changes
below are the delta from `2.1.3.0-rc.21`.

## What's improved since 2.1.3.0-rc.21

- Holds the active jump information until the overlay closes, then presents
  the latest queued target when the overlay next appears.
- Keeps followed-route progress resumable across any number of off-route jumps.
- Preserves the destination star class throughout witchspace instead of
  changing known information to `UNKNOWN` before arrival.
- Marks K, G, B, F, O, A, and M stars with an icon-free `SCOOPABLE` pill that
  uses the displayed star-class color.

## Packaging

- Version: `2.1.3.0-rc.22`
- Tag: `xp-v2.1.3.0-rc.22`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.22-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.22-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.22-x86_64.AppImage`

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
