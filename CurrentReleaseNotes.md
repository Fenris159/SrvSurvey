# SrvSurvey-XP 2.1.3.0-rc.33

This release candidate improves update reliability and standardizes overlay
headers. The changes below are the delta from `2.1.3.0-rc.32`.

## What's changed since 2.1.3.0-rc.32

- Hardens detection of other running SrvSurvey-XP instances before an update on
  Windows and Linux. Registered instances receive a cooperative shutdown
  request, native executable-path checks provide a fallback, and Windows also
  consults Restart Manager for processes holding the installed executable.
- Rechecks for running instances immediately before handing control to the
  updater. An instance that cannot be verified now blocks installation safely
  instead of risking a partial update or terminating an unrelated process.
- Bounds accumulated updater history while preserving recent recovery data.
  Startup and installation cleanup retain the three newest generated backup,
  update, failed-install, downloaded-package, and staged-package entries, plus
  entries created within the last 24 hours.
- Standardizes fixed overlay headers for System Biology, Route Bodies, Guardian
  guidance except Guardian Site, Ground Combat, Colonization Commodities, and
  Massacre Missions. These headers now use the shared uppercase Rajdhani header
  typography and the configured overlay header color.
- Changes the default overlay header color to `#CC0003`. Flight Warning keeps
  its established danger-level color progression instead of using the general
  header color.
- Adds regression coverage for cross-platform process discovery, cooperative
  shutdown, Windows Restart Manager detection, safe cleanup retention, and the
  standardized overlay presentation contracts.

## Packaging

- Version: `2.1.3.0-rc.33`
- Tag: `xp-v2.1.3.0-rc.33`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.33-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.33-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.33-x86_64.AppImage`

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
