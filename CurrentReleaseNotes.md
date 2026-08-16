# SrvSurvey-XP 2.1.3.0-rc.28

This release candidate makes Boxel searches safer to work in either direction
and clarifies manual Galaxy Map copying. The changes below are the delta from
`2.1.3.0-rc.27`.

## What's changed since 2.1.3.0-rc.27

- Adds **Sort (descending) for working results backwards.** The current-boxel
  table starts at the highest suffix, and the next incomplete target, empty
  marker, highlight, and Galaxy Map auto-copy all advance downward together.
  The direction is retained in commander profiles and saved searches.
- Makes the active search controls harder to misuse: **Start search** remains
  disabled until **Stop search** resets the active search, and the stop action
  is shown in red.
- Restores a cleared **Last system available** field to its stored value when
  focus leaves the field. Stopping, loading, or starting another search also
  clears stale edits so later estimates can populate normally.
- Adds direct current-boxel table navigation: **Next Jump Page** returns to the
  page containing the next incomplete target, while **Select page** opens an
  upward, scrolling page list capped at ten visible entries.
- Shows the live **Copy next boxel system** shortcut in Search guidance when
  auto-copy is off, for example **MANUAL COPY - CTRL C**. Changes made under
  Shortcut bindings appear immediately, and an unbound action is identified as
  **MANUAL COPY - NOT SET**.

## Packaging

- Version: `2.1.3.0-rc.28`
- Tag: `xp-v2.1.3.0-rc.28`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.28-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.28-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.28-x86_64.AppImage`

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
