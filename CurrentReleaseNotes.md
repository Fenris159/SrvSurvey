# SrvSurvey-XP 2.1.3.0-rc.35

This release candidate improves overlay controls, panel customization,
typography, guidance, and application branding. The changes below are the delta
from `2.1.3.0-rc.34`.

## What's changed since 2.1.3.0-rc.34

- Adds compact click-through Guardian site-map zoom controls tied to the
  existing map zoom actions. The overlay editor preview shows the same controls.
- Adds master availability switches for affiliated panels at the bottom of
  every overlay settings category. Each panel can also receive an optional,
  unbound keyboard shortcut that toggles its visibility.
- Replaces text-like shortcut fields with reactive chord capture throughout the
  application. Held combinations appear live, releasing all keys commits them,
  Escape cancels, and Backspace or Delete clears the binding without firing the
  prior shortcut during capture.
- Refines the Boxel system action radial into a theme-aware four-part ring,
  clearly darkens inactive actions, and closes the control immediately when the
  containing view scrolls.
- Normalizes missed overlay typography and accent usage in Search Guidance,
  Biology Survey, and Route Bodies, and expands Guides to document the current
  overlay controls and states.
- Replaces the executable, window, tray, and Linux package icons with the new
  high-resolution split SrvSurvey design and supplies optimized Windows icon
  sizes from 16 through 256 pixels.
- Adds regression coverage for panel availability, shortcut capture, Guardian
  zoom rendering, radial action behavior, application icon packaging, Guides,
  and overlay presentation contracts.

## Packaging

- Version: `2.1.3.0-rc.35`
- Tag: `xp-v2.1.3.0-rc.35`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.35-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.35-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.35-x86_64.AppImage`

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
