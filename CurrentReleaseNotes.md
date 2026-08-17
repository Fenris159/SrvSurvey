# SrvSurvey-XP 2.1.3.0-rc.32

This release candidate adds flexible start-point and deferred-system management
to active Boxel searches. The changes below are the delta from
`2.1.3.0-rc.31`.

## What's changed since 2.1.3.0-rc.31

- Adds a persistent **Deferred** system state. Deferred systems remain available
  for later surveying, are skipped by next-target selection and automatic copy,
  and do not count as completed systems.
- Groups deferred systems after active systems while preserving suffix order in
  both ascending and descending searches. **Show Only Deferred** provides a
  focused view with correctly recalculated pagination.
- Replaces the single-row action with a themed radial menu for **Complete**,
  **Reopen**, **Defer**, and **Start Here**.
- Makes **Start Here** defer unfinished systems that precede the selected row in
  the active sort direction, then advances next-target selection and automatic
  copy from the chosen starting point.
- Persists deferred systems in saved searches and Commander profiles, updates
  the Boxel guidance and localized UI, and adds regression coverage for state,
  persistence, ordering, filtering, pagination, and radial-menu commands.

## Packaging

- Version: `2.1.3.0-rc.32`
- Tag: `xp-v2.1.3.0-rc.32`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.32-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.32-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.32-x86_64.AppImage`

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
