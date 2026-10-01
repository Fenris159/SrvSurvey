# SrvSurvey-XP 2.1.3.0-rc.58.16

RC58.16 restores Linux window operations from SrvSurvey's custom title bar. It
also includes RC58.15's saved overlay monitor selection, RC58.14's overlay drag
safeguards and Automatic commander startup recovery, and earlier release
candidate changes.

## Overlay panels

- Added **Overlay monitor** as the first Global overlay behavior setting. Choose
  a display for overlays, or leave it on Automatic to use the game display with
  a primary-display fallback.
- Fixed **Overlay monitor** reverting to Automatic when the display list refreshes
  after reopening SrvSurvey. The saved choice stays selected across sessions;
  choosing Automatic explicitly still clears it.
- Added an optional **Keep overlays on the selected monitor** setting. When
  enabled, live panels and editor previews stay within the selected display
  while dragged. Combined overlays also stay within their game-sized host.
- Dragging now stops when pointer movement reports the mouse button released,
  even if a release event is missed. Ending live interaction applies the last
  accepted move before saving positions.
- Added bounded XWayland pointer-coordinate diagnostics to help investigate
  intermittent drag behavior on multi-monitor desktops.

## Linux window controls

- Right-click SrvSurvey's custom title bar to open window operations without
  restoring the desktop's window frame. On X11 and XWayland, SrvSurvey requests
  the window manager's menu when supported. Otherwise, an in-app menu provides
  Minimize, Maximize or Restore, Fullscreen, Always on top, and Close.

## Journal and commander startup

- In Automatic mode, startup recovers the last identified commander when the
  newest journal contains only menu or shutdown events. It then follows the
  newest journal so a later login can switch commanders automatically.
- If the initial identity scan finds no commander, later polls retry it so an
  identity that appears afterward can still be recovered.

## Update channel and packages

- RC51 remains the permanent `xp-v2.1.3.0-rc.51` compatibility bridge. RC58.16
  uses the schema-2 `xp2-v` release channel.
- Development remains the default in-app update channel, with saved channel
  choices preserved.
- Version: `2.1.3.0-rc.58.16`
- Tag: `xp2-v2.1.3.0-rc.58.16`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.58.16-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.58.16-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.58.16-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.58.16-x86_64.AppImage.zsync`

Packages remain self-contained. The numeric Windows `FileVersion` remains
`2.1.3.0`.

## Testing notice

> [!IMPORTANT]
> This remains a preview for testing. Keep a backup of existing SrvSurvey data
> and report unexpected behavior through the project issue tracker.
