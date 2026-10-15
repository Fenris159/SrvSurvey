# SrvSurvey-XP 2.1.3.0-rc.64.6

RC.64.6 fixes commander settings being mixed when one SrvSurvey instance starts
normally and another is launched for a different commander. Overlay positions,
scaling and exceptions now stay with the selected commander through startup,
editing, restart and restore.

## What's changed

- **Keep overlay settings separate for each commander.** Normal startup now
  selects the commander's own settings folder, and the instance remains attached
  to that commander when another client writes a newer journal. Saved positions,
  global scale, individual panel scale overrides and overlay exceptions stay
  independent. One commander can use +65% scaling while another uses normal scale.
- **Preserve commander assignments through restarts and updates.** An automatic
  instance retains its commander when restarting for language changes, profile
  imports or application updates. Choosing a different startup commander still
  applies that requested change.
- **Keep VR calibration private to each commander.** VR position and scale edits
  now use the commander's overlay folder. Existing vehicle-specific calibrations
  are copied once without replacing calibrations already saved in that profile.
- **Load the selected commander's language at startup.** The application selects
  the commander profile before initializing its language, so another profile's
  shared preference does not replace the saved selection. New profiles retain
  the shared language until their own settings are initialized.
- **Prevent startup waits from freezing the interface.** Journal identity reads
  can complete while commander settings are being selected on the UI thread.

## Update channel and packages

RC.64.6 uses the schema-2 `xp2-v` development channel. RC51 remains the legacy
compatibility bridge. Windows and Linux packages are self-contained.

- Version: `2.1.3.0-rc.64.6`
- Tag: `xp2-v2.1.3.0-rc.64.6`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.64.6-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.64.6-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.64.6-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.64.6-x86_64.AppImage.zsync`

The numeric Windows `FileVersion` remains `2.1.3.0`.

## Testing notice

This remains a development preview. Multi-commander sharing requires separate
physical journal folders and a verified live commander binding for each client;
Steam or Epic folder names alone do not establish ownership. Shared UI inventory
continues to use its existing multi-client safeguards.
