# SrvSurvey-XP 2.1.3.0-rc.6

This release candidate focuses on accurate, compact overlay previews and
exobiology presentation. The changes below are the delta from
`2.1.3.0-rc.5`.

## What's new since 2.1.3.0-rc.5

- Adds state controls to supported overlay-editor folder tabs so game-driven
  presentations can be previewed without waiting for the corresponding journal
  event. Editor previews continue to use the same shared presentations as live
  overlays.
- Restores System Biology prediction and discovery markers, groups any number
  of organism variants into compact rows, colors variant names by their
  biological color, and shows analyzed status without muting that color.
- Uses the Route Workspace body-type artwork in the System Biology overview and
  keeps confirmed-body previews limited to organisms that actually exist.
- Makes Biology Sample Status progress accurate and bounded, centers the stale
  sample warning, and clarifies the idle sampling state.
- Replaces directional font glyphs with theme-aware vector chevrons that switch
  between near and far forms. Ground Target now uses a ringed pointer, and
  markers without a specific far threshold use a 1 km fallback.
- Adds explicit theme colors for confirmed, predicted, possible, highlighted,
  analyzed, and unknown biology reward PIPs across the default and preset
  themes, with expanded descriptions in the overlay icon glossary.

## Packaging

- Version: `2.1.3.0-rc.6`
- Tag: `xp-v2.1.3.0-rc.6`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.6-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.6-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.6-x86_64.AppImage`

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
