# SrvSurvey-XP 2.1.3.0-rc.57

RC57 adds optional threshold filtering for persistent prospector results. It
also includes the journal source, Mining, and overlay editor improvements from
the published [RC56 release](https://github.com/Fenris159/SrvSurvey/releases/tag/xp2-v2.1.3.0-rc.56).

## Multiple commanders and journal folders

- **Settings > Data > Elite journal source** now lets you choose a journal
  folder, add it to a saved list, edit or delete saved entries, and clear the
  picker to choose another folder. Existing single-folder settings carry over.
- Saved folders join automatic discovery instead of replacing it. Steam and
  Epic/Heroic commanders can both appear in **Multiple commanders**, and a
  launched companion instance reads the selected commander's own journal
  folder.
- On Linux, SrvSurvey reads additional Steam libraries from Steam's library
  metadata, including libraries outside the home directory's default Steam
  location. It also reads Lutris game configuration in
  `~/.local/share/lutris/games` alongside its existing Heroic, Bottles, Wine,
  and Flatpak searches. Unusual launcher locations can still be added manually.
- A `--journal-directory` startup option continues to select one folder for
  that particular SrvSurvey instance.

## Linux Frontier connection

- When `secret-tool` is installed but the desktop keyring is unavailable,
  SrvSurvey distinguishes that session problem from a missing command and gives
  KDE Plasma and other desktop recovery guidance.
- The Linux guide identifies the package that supplies `secret-tool` on
  Debian/Ubuntu and Arch derivatives. A Linux Homebrew installation in its
  standard prefix is recognized as well. Frontier tokens still require a
  secure keyring.

## Mining in the cockpit

- The Mining activity overlay keeps the latest four active prospector results
  visible even if none qualifies for an announcement. Materials are highlighted
  only when they meet the configured notification criteria.
- Mining overlay settings now offer **Hide results that don't meet Mineral
  Thresholds**. Turn it on to show only recent prospectors with a mineral at or
  above a configured threshold. Leave it off to keep all recent results visible.
- Refined minerals and limpet changes update the live cargo projection as
  journal events arrive. The next `Cargo.json` snapshot reconciles the display
  with the game's full inventory.

## Overlay editor and layout

- **Settings > Game overlays** has a saved editor controls height adjustment
  from -100% to +100%. Use the slider or type a value; typed values apply on
  Enter or when the field loses focus.
- Overlay previews keep their placement while switching settings categories.
  Route Bodies stays within the visible screen area on short displays.

## Construction depots

- Starting SrvSurvey while already docked at a construction ship restores the
  live depot view from the `Location` journal event, including construction ship
  names with a suffix.

## Update channel and packages

- RC51 remains the permanent `xp-v2.1.3.0-rc.51` compatibility bridge for older
  clients. RC57 uses the schema-2 `xp2-v` release channel.
- Version: `2.1.3.0-rc.57`
- Tag: `xp2-v2.1.3.0-rc.57`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.57-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.57-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.57-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.57-x86_64.AppImage.zsync`

Packages remain self-contained. The numeric Windows `FileVersion` remains
`2.1.3.0`.

## Testing notice

> [!IMPORTANT]
> This remains a preview for testing. Keep a backup of existing SrvSurvey data
> and report unexpected behavior through the project issue tracker.
