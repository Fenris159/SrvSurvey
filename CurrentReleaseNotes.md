# SrvSurvey-XP 2.1.3.0-rc.11

This release candidate stabilizes overlay editing, geometry, and visibility
transitions. The changes below are the delta from `2.1.3.0-rc.10`.

## What's fixed since 2.1.3.0-rc.10

- Keeps compact live content aligned to its editor coordinates even when its
  yellow identification tab is wider, and preserves the top edge of the three
  variable-height System Biology states.
- Applies saved editor positions to live windows immediately, preventing stale
  coordinates or missing panels when the position editor is reopened.
- Keeps Surface Survey geometry stable across hide/show transitions and keeps
  the pointer visible throughout live overlay interaction mode.
- Avoids Biology Sample Status binding errors while its intentionally nullable
  body status changes.
- Makes the landing-gear visibility preference suppress both Surface Survey and
  Prior Scans during main-ship surface flight, including while comms or role
  panels are open, and restores them when the gear is deployed.

## Packaging

- Version: `2.1.3.0-rc.11`
- Tag: `xp-v2.1.3.0-rc.11`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.11-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.11-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.11-x86_64.AppImage`

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
