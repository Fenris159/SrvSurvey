# SrvSurvey-XP 2.1.3.0-rc.14

This release candidate improves Surface Radar controls and Nomad vehicle-state
tracking. The changes below are the delta from `2.1.3.0-rc.13`.

## What's fixed since 2.1.3.0-rc.13

- Adds an optional on-foot Surface Radar gate that follows the Genetic Sampler
  selected in `Status.json` while leaving Mini Trackers available.
- Recognizes the Nomad's hybrid fighter/SRV telemetry so landing-gear
  suppression works consistently for Surface Survey, Prior Scans, and Mini
  Trackers without changing conventional SRV behavior.
- Uses the Nomad vehicle identity for diagnostics and vehicle-specific VR
  calibration instead of reporting the generic SRV fallback.
- Places long numeric overlay settings editors below their labels to prevent
  clipped descriptions and compressed input fields.

## Packaging

- Version: `2.1.3.0-rc.14`
- Tag: `xp-v2.1.3.0-rc.14`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.14-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.14-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.14-x86_64.AppImage`

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
