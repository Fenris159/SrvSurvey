# SrvSurvey-XP 2.1.3.0-rc.50

RC50 adds per-panel overlay typography controls and brings the normal
in-application update flow to Linux AppImages.

## Per-panel overlay typography

- Right-click an overlay in the position editor and select the **Aa** button to
  adjust Header, Title, Value, Body, Detail, and Caption text for that panel.
- Each role starts at the existing theme size. Scale it from -50% to +100% in
  5% steps without changing the overlay's colours, font family, or weight.
- Each panel stores its own typography profile and updates its preview and live
  overlay immediately.
- Overlay layouts now remeasure around scaled text. Compact content wraps,
  scrolls, or uses intentional ellipsis instead of clipping into nearby text.

Existing users keep the current appearance because every new typography scale
defaults to the 0% baseline.

## AppImage updates

- A writable Linux AppImage can now install releases from SrvSurvey's update
  card instead of requiring a manual download and replacement.
- The updater verifies the indexed AppImage checksum, keeps the previous image
  for rollback, and restores it if the replacement cannot confirm a healthy
  startup.
- Stable file names and symbolic-link launch paths are supported. The update
  helper uses extract-and-run mode so the update transaction does not depend on
  FUSE being installed.
- Release AppImages now embed update-channel metadata and publish a matching
  `.zsync` asset for standard AppImage tooling.

The manual download instructions remain available when the AppImage or its
containing folder is read-only.

## Packaging

- Version: `2.1.3.0-rc.50`
- Tag: `xp-v2.1.3.0-rc.50`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.50-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.50-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.50-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.50-x86_64.AppImage.zsync`

Windows and Linux packages remain self-contained. Numeric Windows FileVersion
remains `2.1.3.0`.

## Testing notice

> [!IMPORTANT]
> This remains a work-in-progress preview for testing. Keep a backup of your
> existing SrvSurvey data and report unexpected behavior through the project
> issue tracker.
