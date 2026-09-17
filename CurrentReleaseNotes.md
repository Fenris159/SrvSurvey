# SrvSurvey-XP 2.1.3.0-rc.48.8

RC48.8 fixes a Canonn biology prediction regression and improves the biology
overlay targeting indicator visibility.

## New in RC48.8

- Restores hatched cyan rendering for non-commander-scanned Canonn biology
  signals. PR [#136](https://github.com/Fenris159/SrvSurvey/pull/136) fixes a
  regression where all Canonn data was shown as confirmed organisms (solid
  orange PIPs) instead of distinguishing commander-verified scans from external
  predictions. Unverified Canonn signals now display with hatched cyan fill and
  `?` markers around species names.
- Changes the biology overlay targeting border from secondary (cyan) to the
  themed success color (green). PR [#137](https://github.com/Fenris159/SrvSurvey/pull/137)
  makes the pip group highlight visible over prediction pips, which are also
  cyan-colored. The success brush respects the user's selected theme.

## Packaging

- Version: `2.1.3.0-rc.48.8`
- Tag: `xp-v2.1.3.0-rc.48.8`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.48.8-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.48.8-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.48.8-x86_64.AppImage`

Windows and Linux packages are self-contained. Linux packaging tools and the
AppImage runtime use versioned, checksum-verified downloads. AppImages are updated
manually through the selected XP release. Numeric Windows FileVersion remains
`2.1.3.0`.

## Testing notice

> [!IMPORTANT]
> This remains a work-in-progress preview for testing. Keep a backup of your
> existing SrvSurvey data and report unexpected behavior through the project
> issue tracker.

Native overlay behavior should still be exercised with Elite Dangerous on
clean Windows, X11, and XWayland systems. Pure native Wayland is not yet a
full-functionality overlay target.
