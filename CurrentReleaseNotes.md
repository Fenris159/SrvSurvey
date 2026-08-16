# SrvSurvey-XP 2.1.3.0-rc.30

This release candidate restores reliable Boxel range prediction and makes edits
to the last available system explicit and recoverable. The changes below are
the delta from `2.1.3.0-rc.29`.

## What's changed since 2.1.3.0-rc.29

- Restores the initial Boxel range estimate when starting a new search. The
  generated-system suffix seeds the expected range, and local history,
  NavRoute, and Spansh observations can only raise that estimate to the highest
  available result instead of replacing it with a lower value.
- Makes **Last System Available** a committed edit. Blank, nonnumeric, or
  abandoned changes revert to the stored value; invalid input is highlighted;
  and **Apply** is enabled only for a valid changed value. A manual value may
  refine the estimate, but cannot be lower than a system suffix already
  recorded by local history, NavRoute, or Spansh.

## Packaging

- Version: `2.1.3.0-rc.30`
- Tag: `xp-v2.1.3.0-rc.30`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.30-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.30-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.30-x86_64.AppImage`

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
