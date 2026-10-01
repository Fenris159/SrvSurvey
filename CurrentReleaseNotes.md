# SrvSurvey-XP 2.1.3.0-rc.58.17

This preview improves Wayland screen capture for FSS tuning, first-footfall
detection, and Surface Mining Rhino rig tracking on Linux desktops.

## Wayland screen capture

- In **Settings → Application → Wayland screen capture**, enable screen capture
  and choose which trackers may use it. Each tracker is opt-in.
- When the desktop asks what to share, select the **display showing Elite
  Dangerous**. Display sharing can reduce lag on Proton or Gamescope setups.
  If detection does not work with the correct display, use **Choose capture
  source again** and try the game window.
- **Choose capture source again** opens the desktop picker immediately, including
  when Elite Dangerous is closed. Changing the source no longer restarts
  SrvSurvey. The desktop may remember a shared source between sessions when its
  portal supports saved selections.
- Capture accounts for display scaling and monitor positions when locating
  game regions. SrvSurvey processes the shared image locally.
- Closing Elite Dangerous under Gamescope no longer closes SrvSurvey; capture
  can resume when the game becomes available again.

## Update channel and packages

- RC51 remains the permanent `xp-v2.1.3.0-rc.51` compatibility bridge. RC58.17
  uses the schema-2 `xp2-v` release channel.
- Development remains the default in-app update channel, with saved channel
  choices preserved.
- Version: `2.1.3.0-rc.58.17`
- Tag: `xp2-v2.1.3.0-rc.58.17`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.58.17-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.58.17-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.58.17-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.58.17-x86_64.AppImage.zsync`

Packages remain self-contained. The numeric Windows `FileVersion` remains
`2.1.3.0`.

## Testing notice

> [!IMPORTANT]
> This remains a preview for testing. Keep a backup of existing SrvSurvey data
> and report unexpected behavior through the project issue tracker.
