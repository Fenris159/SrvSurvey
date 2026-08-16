# SrvSurvey-XP 2.1.3.0-rc.29

This release candidate makes long Boxel tables easier to navigate and prevents
list selection from moving an enclosing page unexpectedly. The changes below
are the delta from `2.1.3.0-rc.28`.

## What's changed since 2.1.3.0-rc.28

- Adds direct current-boxel table navigation: **Next Jump Page** returns to the
  page containing the next incomplete target in ascending or descending
  searches. **Select page** opens an upward, scrolling list of every page,
  capped at ten visible entries and sized for the largest page number.
- Contains selection-driven scrolling inside every list. Guardian sites,
  Diagnostics journal events, Boxel suggestions, nearest-system results, and
  other updating lists can still reveal their selected rows internally without
  shifting the surrounding page away from its top or remembered position.
  Explicit navigation such as **Review update** continues to control the page
  viewport directly.

## Packaging

- Version: `2.1.3.0-rc.29`
- Tag: `xp-v2.1.3.0-rc.29`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.29-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.29-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.29-x86_64.AppImage`

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
