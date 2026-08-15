# SrvSurvey-XP 2.1.3.0-rc.25

This release candidate corrects FSS Survey body totals and update-review
navigation. The changes below are the delta from `2.1.3.0-rc.24`.

## What's fixed since 2.1.3.0-rc.24

- Makes the FSS Survey scanned-body total use the same real-body definition as
  the journal's FSS total. Barycentres, rings, and asteroid clusters no longer
  inflate the completed count shown by that panel.
- Makes the update notification's Review action align the top of the
  Application updates card with the top of the Diagnostics viewport, even when
  that page was previously scrolled elsewhere.

## Packaging

- Version: `2.1.3.0-rc.25`
- Tag: `xp-v2.1.3.0-rc.25`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.25-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.25-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.25-x86_64.AppImage`

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
