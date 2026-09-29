# SrvSurvey-XP 2.1.3.0-rc.58.15

RC58.15 keeps the selected overlay monitor saved across sessions. It includes
RC58.14's overlay drag safeguards and Automatic commander startup recovery,
along with earlier release candidate changes.

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

## Journal and commander startup

- In Automatic mode, startup recovers the last identified commander when the
  newest journal contains only menu or shutdown events. It then follows the
  newest journal so a later login can switch commanders automatically.
- If the initial identity scan finds no commander, later polls retry it so an
  identity that appears afterward can still be recovered.

## Update channel and packages

- RC51 remains the permanent `xp-v2.1.3.0-rc.51` compatibility bridge. RC58.15
  uses the schema-2 `xp2-v` release channel.
- Development remains the default in-app update channel, with saved channel
  choices preserved.
- Version: `2.1.3.0-rc.58.15`
- Tag: `xp2-v2.1.3.0-rc.58.15`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.58.15-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.58.15-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.58.15-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.58.15-x86_64.AppImage.zsync`

Packages remain self-contained. The numeric Windows `FileVersion` remains
`2.1.3.0`.

## Testing notice

> [!IMPORTANT]
> This remains a preview for testing. Keep a backup of existing SrvSurvey data
> and report unexpected behavior through the project issue tracker.
