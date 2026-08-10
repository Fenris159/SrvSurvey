# SrvSurvey-XP 2.1.3.0-rc.13

This release candidate improves dynamic overlay placement, biological survey
guidance, and EDDN identification. The changes below are the delta from
`2.1.3.0-rc.12`.

## What's fixed since 2.1.3.0-rc.12

- Keeps dynamic-height overlay panels aligned by their top edge after moves in
  either the live interaction mode or position editor.
- Adds a configurable extension for System Map Body Predictions previews and
  shows the targeted biological body during DSS even when near-body-only
  presentation is enabled.
- Treats completed historical biology scan circles as reference markers so
  entering them does not interfere with sampling a different organism.
- Identifies EDDN uploads as SrvSurvey-XP and enables EDDN test schemas by
  default for new profiles while keeping journal sharing opt-in.

## Packaging

- Version: `2.1.3.0-rc.13`
- Tag: `xp-v2.1.3.0-rc.13`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.13-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.13-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.13-x86_64.AppImage`

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
