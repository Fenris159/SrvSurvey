# SrvSurvey-XP 2.1.3.0-rc.4

This fourth release candidate focuses on overlay presentation, compactness, and
editor usability. The changes below are the delta from `2.1.3.0-rc.3`.

## What's new since 2.1.3.0-rc.3

- Uses the same shared Avalonia presentation templates for live overlays and
  editor previews, so layout, typography, wrapping, colors, and panel contents
  no longer drift between the two surfaces.
- Tightens the information density of exploration, biology, route, Guardian,
  settlement, station, mission, notification, and status overlays. Panels now
  size more closely to their content, long descriptive text wraps, dividers no
  longer force extra width, and compact text consistently uses Oxanium and
  Rajdhani roles.
- Improves FSS overlays with a wrapped description, a compact three-body
  scrolling viewport, unscanned-first alphanumeric ordering, and a consistent
  scanned-state pill. Route-body headings also wrap at meaningful separator
  groups instead of splitting details arbitrarily.
- Restores editor folder tabs, removes the duplicate preview backing layer, and
  anchors the editor controls above the desktop work area. Overlay panels can
  be moved beyond every screen edge, while per-panel opacity and scale controls
  open toward the available space.
- Adds a compact single-row overlay color editor and six built-in themes:
  Default, Nebula Cyan, Toxic Green, Crimson Wake, Void Amethyst, and Cerulean
  Gold. Loading either a preset or named state refreshes open overlays
  immediately; Apply remains the explicit step that persists `theme.json`.
- Expands automated rendering, placement, presentation-parity, theme, and
  editor interaction coverage for the shared overlay system.

## Packaging

- Version: `2.1.3.0-rc.4`
- Tag: `xp-v2.1.3.0-rc.4`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.4-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.4-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.4-x86_64.AppImage`

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

## For testers

Install the complete platform package rather than separating the executable
from its companion files. See the
[`Windows installation guide`](https://github.com/Fenris159/SrvSurvey/blob/SrvSurvey-Avalonia/docs/INSTALL_WINDOWS.md) or
[`Linux installation guide`](https://github.com/Fenris159/SrvSurvey/blob/SrvSurvey-Avalonia/docs/INSTALL_LINUX.md), and report defects or suggestions through the
[`Fenris159/SrvSurvey` issue tracker](https://github.com/Fenris159/SrvSurvey/issues).
