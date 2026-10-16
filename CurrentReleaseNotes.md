# SrvSurvey-XP 2.1.3.0-rc.64.10

RC.64.10 improves window relationships for Linux permission dialogs and
auxiliary tools. It retains the Wayland capture freeze fixes from RC.64.9.

## What's changed

- **Associate the Wayland source picker with SrvSurvey.** Screen sharing now
  identifies the application window that opened the picker, allowing the
  desktop to keep it above that window.
- **Associate shortcut permission and configuration dialogs with their window.**
  The desktop receives the current SrvSurvey window when these dialogs open.
- **Keep auxiliary tools attached to their owners.** Mining calibration belongs
  to the overlay editor, and map zoom controls retain their relationship to
  the map when overlays are hidden and shown again.

## Update channel and packages

RC.64.10 uses the schema-2 `xp2-v` development channel. RC51 remains the legacy
compatibility bridge. Windows and Linux packages are self-contained.

- Version: `2.1.3.0-rc.64.10`
- Tag: `xp2-v2.1.3.0-rc.64.10`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.64.10-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.64.10-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.64.10-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.64.10-x86_64.AppImage.zsync`

The numeric Windows `FileVersion` remains `2.1.3.0`.

## Testing notice

This remains a development preview. Isolated tests check desktop permission
requests and child-window ownership. The reported CachyOS/Plasma stacking
behavior still needs confirmation on that desktop.
