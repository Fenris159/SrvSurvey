# SrvSurvey-XP 2.1.3.0-rc.58.5

RC58.5 keeps the RC58 multi-commander, journal, and settings work and fixes
Linux Frontier linking when a second commander is added beside an existing
login. Creating a colonization project now records the journal body and the
Raven architect before anything is published. It also includes the journal
source, Mining, and overlay editor improvements from RC57 and the published
[RC56 release](https://github.com/Fenris159/SrvSurvey/releases/tag/xp2-v2.1.3.0-rc.56).

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
  that particular SrvSurvey instance. The instance still discovers commanders
  in other saved and automatically found folders, including an Epic folder
  added while the instance is running. Its live journal and companion-file data
  remain tied to the selected instance.

## Commander profiles and Frontier

- The Frontier commander selector lists commanders found in journals. Selecting
  an unlinked commander offers **Connect to Frontier**; switching this view
  does not switch the active journal commander used elsewhere in SrvSurvey.
- Frontier authorization and stored credentials are separated by commander.
  Starting a connection for another commander no longer replaces an unfinished
  connection, and Frontier profile data is checked against the selected
  commander before it appears.
- Application and overlay settings are saved separately for each commander.
  Existing settings are copied into each commander's profile when it is first
  created, so updating does not discard the user's previous choices.

## Linux Frontier connection

- When `secret-tool` is installed but the desktop keyring is unavailable,
  SrvSurvey distinguishes that session problem from a missing command and gives
  KDE Plasma and other desktop recovery guidance.
- The Linux guide identifies the package that supplies `secret-tool` on
  Debian/Ubuntu and Arch derivatives. A Linux Homebrew installation in its
  standard prefix is recognized as well. Frontier tokens still require a
  secure keyring.
- Linking another commander no longer fails with "Frontier authorization was
  cancelled or replaced" when the keyring already holds a commander login.
  SrvSurvey reads every matching secret and keeps the newest authorization, so
  the existing login remains and the new commander can connect.
- Connecting from the Frontier commander dropdown stores that login under the
  selected Frontier ID. This window's journals stay on the commander it is
  already reading. A companion instance launched for the selected commander
  loads that same login and pairs it with that commander's journals.
- All Frontier logins share one keyring secret. If that combined secret would
  exceed `secret-tool`'s 8192-byte limit, Connect stops with an explicit
  message and leaves the existing login in place.

## Mining in the cockpit

- The Mining activity overlay keeps the latest four active prospector results
  visible even if none qualifies for an announcement. Materials are highlighted
  only when they meet the configured notification criteria.
- Mining overlay settings now offer **Hide results that don't meet Mineral
  Thresholds**. Turn it on to show only recent prospectors with a mineral at or
  above a configured threshold, plus announced core asteroids. Leave it off to
  keep all recent results visible.
- Refined minerals and limpet changes update the live cargo projection as
  journal events arrive. The next `Cargo.json` snapshot reconciles the display
  with the game's full inventory.

## Overlay editor and layout

- **Settings > Game overlays** has a saved editor controls height adjustment
  from -100% to +100%. Use the slider or type a value; typed values apply on
  Enter or when the field loses focus.
- Overlay previews keep their placement while switching settings categories.
  Route Bodies stays within the visible screen area on short displays.

## New colonization project

- **Prepare new project** opens the create form from the live construction
  depot and Raven's planned sites. It does not publish anything. **Review
  project** checks the form, and the project is sent to Raven only after you
  confirm.
- **Body ID** is the journal `BodyID` for the body you are on. It is not the
  system map label, such as 4 or 4 a. The circled help icon explains how to
  read `BodyID` from the latest journal if the field has to be typed.
- **Body name** is the full journal body name, such as `Peralta 4 a`. A known
  body is published with both the id and that name, so a moon stays distinct
  from its planet.
- The project name starts from the dock: the station name, or "Primary port"
  at the system colonisation ship. Choosing a planned Raven site replaces it
  with that site's Raven name only while docked at the colonisation ship.
- The architect is the system architect saved in Raven Colonial, and that
  field is read-only. If Raven has no architect, the field uses your current
  commander name, stays editable, and warns that the name can lock the system
  to that commander.
- Project name, architect, body id, and body name must be filled before
  review. An empty field is highlighted with "This field is required."

## Construction depots

- Starting SrvSurvey while already docked at a construction ship restores the
  live depot view from the `Location` journal event, including construction ship
  names with a suffix.

## Update channel and packages

- RC51 remains the permanent `xp-v2.1.3.0-rc.51` compatibility bridge for older
  clients. RC58.5 uses the schema-2 `xp2-v` release channel.
- Version: `2.1.3.0-rc.58.5`
- Tag: `xp2-v2.1.3.0-rc.58.5`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.58.5-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.58.5-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.58.5-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.58.5-x86_64.AppImage.zsync`

Packages remain self-contained. The numeric Windows `FileVersion` remains
`2.1.3.0`.

## Testing notice

> [!IMPORTANT]
> This remains a preview for testing. Keep a backup of existing SrvSurvey data
> and report unexpected behavior through the project issue tracker.
