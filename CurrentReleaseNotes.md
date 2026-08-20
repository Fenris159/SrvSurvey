# SrvSurvey-XP 2.1.3.0-rc.36

This release candidate corrects Nomad vehicle-state tracking and surface
overlay visibility. The changes below are the delta from `2.1.3.0-rc.35`.

## What's changed since 2.1.3.0-rc.35

- Preserves the Nomad's learned journal vehicle ID across same-commander suit
  reloads so re-entering the vehicle restores its correct identity.
- Restores landing-gear-driven visibility for Surface Survey and Canonn Prior
  Scans while flying the Nomad, including after an on-foot reload.
- Stops an on-foot Nomad from appearing simultaneously as both the ship and an
  SRV in Surface Survey navigation trackers.
- Prevents Linux and mixed-monitor overlay placement failures when a panel opens
  before its content size has been measured, while honoring the game monitor's
  display scale.
- Migrates imported absolute desktop overlay anchors to safe game-window-relative
  defaults, preserving existing relative placements and creating a backup before
  rewriting legacy layout data.
- Adds journal, surface-tracking, presentation, and end-to-end regressions for
  Nomad reload, landing-gear, and vehicle-marker behavior while preserving
  conventional SRV handling.

## Packaging

- Version: `2.1.3.0-rc.36`
- Tag: `xp-v2.1.3.0-rc.36`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.36-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.36-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.36-x86_64.AppImage`

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
