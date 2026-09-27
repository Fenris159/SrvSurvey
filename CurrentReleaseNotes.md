# SrvSurvey-XP 2.1.3.0-rc.58.8

RC58.8 restores live overlays when Elite Dangerous runs inside native Wayland
Gamescope and SrvSurvey runs on the desktop. It includes the FSS layout and
mouse-interaction fixes from RC58.7 and earlier release candidates.

## What's changed since RC58.7

### Live overlays over native Wayland Gamescope

- SrvSurvey can track Elite on Gamescope's nested X11 display while keeping its
  live overlay panels on the desktop display. Panels remain visible, update with
  game events, and can be moved while mouse interaction is enabled.
- The Gamescope launcher now publishes a short-lived bridge marker containing
  the nested display and desktop monitor bounds. SrvSurvey verifies the
  Gamescope process before using it and returns to normal desktop tracking when
  the marker is absent.
- A configurable native Wayland launch template, marker helper, and Gamescope
  3.16.29 patch are included with the Linux setup guide. The wrapper keeps the
  Frontier launcher windowed, makes Elite fullscreen on the chosen monitor,
  and releases relative mouse capture during overlay interaction.
- The launcher removes its bridge marker and closes Gamescope when it receives
  an exit signal.

## Gamescope setup

The desktop-overlay path requires the
[native Wayland Gamescope launch template](https://github.com/Fenris159/SrvSurvey/blob/SrvSurvey-Avalonia/scripts/EliteGamescopeWayland.sh)
and the matching patches described in the
[Ubuntu Gamescope guide](https://github.com/Fenris159/SrvSurvey/blob/SrvSurvey-Avalonia/docs/UBUNTU_26_GAMESCOPE.md#native-wayland-launch-template).
The wrapper creates and removes the bridge marker automatically. Linux users
running Elite without Gamescope continue to use the normal desktop overlay
path.

## Update channel and packages

- RC51 remains the permanent `xp-v2.1.3.0-rc.51` compatibility bridge. RC58.8
  uses the schema-2 `xp2-v` release channel.
- Version: `2.1.3.0-rc.58.8`
- Tag: `xp2-v2.1.3.0-rc.58.8`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.58.8-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.58.8-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.58.8-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.58.8-x86_64.AppImage.zsync`

Packages remain self-contained. The numeric Windows `FileVersion` remains
`2.1.3.0`.

## Testing notice

> [!IMPORTANT]
> This remains a preview for testing. Keep a backup of existing SrvSurvey data
> and report unexpected behavior through the project issue tracker.
