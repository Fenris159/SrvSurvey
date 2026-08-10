# SrvSurvey-XP 2.1.3.0-rc.9

This release candidate restores System Biology overlay state and prediction
parity. The changes below are the delta from `2.1.3.0-rc.8`.

## What's fixed since 2.1.3.0-rc.8

- Shows the newly mapped body's biology details for the configured post-DSS
  interval, including during supercruise, and immediately updates the overlay
  when its proximity or post-DSS visibility settings change.
- Preserves every plausible genus prediction PIP even when alternative
  candidates exceed the reported biological-signal count, matching the legacy
  overlay without treating those candidates as extra signals or rewards.
- Aligns System Biology body names, prediction PIPs, and reward estimates in
  stable shared columns so rows remain compact and readable.
- Restores the legacy biology PIP layers and state styling, including complete
  prediction ranges, independent fills, hatching, segment borders, outer
  frames, and galactic or regional first markers. The expanded PIP palette is
  available in the theme editor and presets, while older custom themes derive
  the new roles from their existing biology and general colours.
- Keeps live overlay dragging and the position editor synchronized through the
  same `plotters.json` layout. Opening the editor now persists any pending live
  moves first, so its previews start at the positions just set in game.
- Disables dependent DSS, prior-scan, radar, and post-DSS duration controls when
  their parent setting is off, making the active behavior unambiguous.

## Packaging

- Version: `2.1.3.0-rc.9`
- Tag: `xp-v2.1.3.0-rc.9`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.9-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.9-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.9-x86_64.AppImage`

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
