# SrvSurvey-XP 2.1.3.0-rc.26

This release candidate makes Boxel search progression and statistics clearer,
safer, and easier to navigate. The changes below are the delta from
`2.1.3.0-rc.25`.

## What's changed since 2.1.3.0-rc.25

- Treats **Last system available** as an inclusive in-game suffix: entering 348
  tracks systems 0 through 348 and reports 349 total systems.
- Replaces the ambiguous current-boxel empty action with **Mark Next Empty**.
  The next incomplete system is recorded as nonexistent, persisted with the
  search, and skipped so surveying advances to the following target.
- Highlights the next incomplete system, distinguishes it when it is also the
  current system, and pages the system table 10 rows at a time with clear range
  and page controls.
- Makes Boxel statistics navigation and scope explicit: mass-code filters show
  exact matches, child boxels have a separate drill-down, and selected-boxel or
  entire-saved-search totals explain which data is being combined.
- Adds settings tooltips, an averages-calculation help dialog, native folder
  selection for JSON/CSV exports, a pie-chart icon on **Boxel Stats**, and safer
  UI-thread and file-error handling for statistics refreshes and exports.
- Sends the in-app GitHub issue action to the Fenris159 repository.

## Packaging

- Version: `2.1.3.0-rc.26`
- Tag: `xp-v2.1.3.0-rc.26`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.26-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.26-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.26-x86_64.AppImage`

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
