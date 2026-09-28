# SrvSurvey-XP 2.1.3.0-rc.58.13

RC58.13 fixes overlay sizing and live interaction, improves the Planetary Mining
results layout, and adds material shortcuts to surface mining commands. It
includes RC58.12 and earlier release candidate changes.

## Overlay panels

- Fixed Next-jump information being clipped in the live overlay despite fitting
  in the editor preview. Live panels now use the same saved dimensions as their
  previews, without a second window size limit cutting off content or borders.
- Audited all 37 overlay panels across their preview states, saved dimensions,
  font sizes, and overlay scales. Saved sizes preserve the panels' dynamic size
  settings when reset, including the surface survey radar.
- Preserved font sizes when panels move between separate windows and the
  combined overlay.
- Buttons and scrollbar handles now work in live interaction mode. Dragging
  panel backgrounds still repositions the overlay, while interactive maps retain
  their own pan gestures.

## Mining

- Planetary Mining Reinforce and Undermine results now size the State and Power
  columns to their contents, keeping headers aligned and power names visible.
- Surface mining commands accept the material tag codes as well as full names
  for all 37 surface materials. For example, use `mon` for Monazite, `ltd` for
  Low Temperature Diamonds, or `per` for Periclase Dunite. Codes are case insensitive
  and work when creating or moving deposit markers.
- Updated the surface mining instructions and guide with shorthand examples.

## Update channel and packages

- RC51 remains the permanent `xp-v2.1.3.0-rc.51` compatibility bridge. RC58.13
  uses the schema-2 `xp2-v` release channel.
- Development remains the default in-app update channel, with saved channel
  choices preserved.
- Version: `2.1.3.0-rc.58.13`
- Tag: `xp2-v2.1.3.0-rc.58.13`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.58.13-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.58.13-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.58.13-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.58.13-x86_64.AppImage.zsync`

Packages remain self-contained. The numeric Windows `FileVersion` remains
`2.1.3.0`.

## Testing notice

> [!IMPORTANT]
> This remains a preview for testing. Keep a backup of existing SrvSurvey data
> and report unexpected behavior through the project issue tracker.
