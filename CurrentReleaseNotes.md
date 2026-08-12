# SrvSurvey-XP 2.1.3.0-rc.19

This release candidate adds category-specific overlay configuration and
improves Guardian input handling. The changes below are the delta from
`2.1.3.0-rc.18`.

## What's improved since 2.1.3.0-rc.18

- Adds dedicated overlay-settings windows for Exploration, Exobiology, Travel,
  Guardian, Quests, and Colonization directly from the main navigation.
- Moves category-owned settings into their dedicated windows while retaining
  global overlay behavior, appearance, and color settings in Settings.
- Reorganizes Exobiology and Guardian settings into balanced, wrapping sections
  and keeps related selectors and numeric controls with their descriptions.
- Keeps DSS distance and minimum-value editors with their corresponding survey
  controls and preserves their intended presentation order.
- Clarifies Guardian overlay instructions: cycle the fire group to choose site
  types and survey-point states, then toggle the configured confirmation
  control twice to save the choice.
- Correctly describes aerial site-origin alignment, on-foot relic heading
  capture with two shield toggles, and configured confirmation controls in
  Guardian material-capacity warnings.
- Aligns the current Commander value with its label and standardizes the
  user-facing Colonization spelling.

## Packaging

- Version: `2.1.3.0-rc.19`
- Tag: `xp-v2.1.3.0-rc.19`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.19-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.19-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.19-x86_64.AppImage`

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
