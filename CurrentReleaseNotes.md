# SrvSurvey-XP 2.1.3.0-rc.8

This release candidate hardens Elite status monitoring and removes noisy UI
binding failures. The changes below are the delta from `2.1.3.0-rc.7`.

## What's fixed since 2.1.3.0-rc.7

- Defers the first failed `Status.json` poll so Elite's brief shutdown rewrite
  no longer leaves a JSON warning after the game closes, while persistent read
  failures are still reported once and recovery clears the warning.
- Prevents Save As validation from treating its error text as a Boolean value.
- Keeps body-information overlay bindings valid while no body is selected,
  eliminating the repeated null-binding errors during target transitions.

## Packaging

- Version: `2.1.3.0-rc.8`
- Tag: `xp-v2.1.3.0-rc.8`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.8-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.8-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.8-x86_64.AppImage`

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
