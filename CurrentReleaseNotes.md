# SrvSurvey-XP 2.1.3.0-rc.10

This release candidate improves overlay-position fidelity and external system
data refreshes. The changes below are the delta from `2.1.3.0-rc.9`.

## What's fixed since 2.1.3.0-rc.9

- Aligns editor preview coordinates with the live overlay content origin, so a
  panel moved in game appears at the same relative position in the editor.
- Prevents the idle Biology Sample Status state from binding its progress bar
  to a missing selected organism, removing repeated null-binding log errors
  without changing the intentional ready-state transition.
- Looks up EDSM body data by system address and bypasses stale cached misses for
  newly visited systems. Expected EDSM or Spansh indexing delays now use bounded
  retries instead of being reported as permanent availability warnings.

## Packaging

- Version: `2.1.3.0-rc.10`
- Tag: `xp-v2.1.3.0-rc.10`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.10-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.10-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.10-x86_64.AppImage`

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
