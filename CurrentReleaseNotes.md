# SrvSurvey-XP 2.1.3.0-rc.23

This release candidate expands Boxel searching into a complete, resumable
exploration workflow. The changes below are the delta from `2.1.3.0-rc.22`.

## What's improved since 2.1.3.0-rc.22

- Moves Boxel search into its own workspace and overlay-settings category, with
  a visible breadcrumb, parent/child/sibling navigation, aligned system data,
  and clearer current-boxel and next-incomplete-system context.
- Adds named saved Boxel projects with notes, favorites, creation and modified
  dates, completion totals, single-selection resume, deletion, and full-area
  auditing so several surveys can be paused and resumed independently.
- Preserves completed systems and empty boxels across restarts, restores
  automatic FSD/FSS completion behavior, and hardens Spansh refreshes against
  nullable or malformed community timestamps.
- Makes Boxel, Route Manager, and Fleet Carrier Route Galaxy Map auto-copy
  mutually exclusive so enabling any one safely disables the other two.
- Adds EDSM-first system-name and id64 suggestions with an Ardent fallback to
  applicable system entry fields, while keeping saved editor queries empty on
  startup and making displayed system names and id64 values separately copyable.
- Adds a dedicated Boxel guide covering procedural naming, bounded surveys,
  hierarchy navigation, completion rules, saved projects, and audit behavior.
- Adds an explicit opt-in VoxStellar integration to the Boxel workspace for
  signed live exploration-journal uploads, with visible data-use, service-term,
  privacy, and EDMC-VoxStellar MIT-license information.
- Corrects Guardian origin/Ram Tah layout, Overview identity and exploration
  metrics, Guide step markers, and nullable UI bindings that previously wrote
  harmless but noisy null-path errors to the application log.

## Packaging

- Version: `2.1.3.0-rc.23`
- Tag: `xp-v2.1.3.0-rc.23`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.23-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.23-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.23-x86_64.AppImage`

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
