# SrvSurvey-XP 2.1.3.0-rc.17

This release candidate improves the FSS and Body Information overlays. The
changes below are the delta from `2.1.3.0-rc.16`.

## What's improved since 2.1.3.0-rc.16

- Adds a configurable number of FSS Information bodies shown before scrolling
  and compacts each entry into a content-sized two-row summary.
- Lets FSS descriptions wrap, keeps the scrolling boundaries visible, and
  allows the panel to contract around shorter content.
- Compacts Body Information headers, statistics, and wrapping material pills
  while preserving the shared live/editor presentation.
- Shows DSS-complete Body Information selections in the System Map and left
  navigation panel, including the completed body after a DSS scan.
- Adds a three-second System Map Body Information preview with a configurable
  extension; left-navigation selections remain visible without that timeout.
- Uses the same prediction PIP border rules in FSS Body Feed and System Biology.

## Packaging

- Version: `2.1.3.0-rc.17`
- Tag: `xp-v2.1.3.0-rc.17`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.17-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.17-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.17-x86_64.AppImage`

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
