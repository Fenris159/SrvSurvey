# SrvSurvey-XP 2.1.3.0-rc.16

This release candidate compacts the three System Biology overlay states. The
changes below are the delta from `2.1.3.0-rc.15`.

## What's improved since 2.1.3.0-rc.15

- Makes the System Overview, Body Predictions, and Identified Bio panel edges
  contract and expand with their shared content-sized presentation.
- Places System Overview value ranges on two lines in a shared left-aligned
  column immediately after the reward PIPs and removes redundant `CR` labels.
- Removes the repeated `biology` suffix from Body Predictions and Identified
  Bio headings and compacts their reward summaries onto labeled value rows.
- Splits the Identified Bio DSS confirmation into two lines and left-aligns its
  known-reward and first-footfall totals.

## Packaging

- Version: `2.1.3.0-rc.16`
- Tag: `xp-v2.1.3.0-rc.16`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.16-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.16-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.16-x86_64.AppImage`

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
