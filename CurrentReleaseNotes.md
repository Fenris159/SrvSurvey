# SrvSurvey-XP 2.1.3.0-rc.71

RC71 brings the SteamOS and Gamescope overlay support from PR #207 into the
current release, with clearer Next Jump guidance and a panel that fits its
content.

## Steam Deck Gaming Mode and Gamescope

- **Run native SrvSurvey-XP alongside Elite through Steam/Proton.** Add the Linux
  AppImage as a non-Steam game, leave SrvSurvey running, and return to Elite.
  SrvSurvey verifies the Gamescope compositor and discovers Elite's game display
  separately. Follow the [Steam Deck Gaming Mode guide](docs/STEAMOS_GAMING_MODE.md).
- **Keep gameplay input while using passive panels.** Toggle live overlay
  interaction when you need to click or reposition a panel. Live mode reserves
  pointer input across the HUD canvas while keyboard input stays with Elite.
  Steam and Quick Access menus take input priority; rejected live-mode requests
  can be retried after the menus close. Enable key chords before assigning the
  shortcut in Settings → Input.
- **Follow game focus, scaling, and restarts.** Panels track supported viewport
  changes and reconnect after game-display interruptions. Keyboard discovery
  follows the compositor-focused Elite display, including multiple clients.
  Switching away from a game and returning preserves its known scaling, and
  overlays stay hidden when native host-focus information is temporarily lost.
- **Support nested Gamescope on desktop Linux.** Verified outer X11 windows
  supply placement and focus. Native Wayland placement needs the documented
  geometry bridge; accurate host Alt-Tab tracking also needs its focus publisher.
- **Collect a device snapshot when investigating a problem.** The read-only
  SteamOS diagnostic collector records relevant versions, process identities,
  display geometry, and compositor properties. It does not capture screenshots,
  keystrokes, journals, or full process environments, and uploads nothing.

## Next Jump overlay

- **Fit the panel to its content.** The panel grows when destination details
  arrive and shrinks when fewer rows are available. Saved width and typography
  still apply; old saved heights no longer leave an empty area beneath the text.
  Live windows, combined overlays, and the position-editor preview share this
  behavior.
- **See the next jump clearly.** A thicker, bright cyan segment and a diamond
  destination marker identify the active jump, including with monochrome overlay
  themes. Other route legs retain their theme colors.
- **Recognize scoopable stars.** Filled star markers replace the hollow ovals.
  The legend sits at the top right beside the panel title, identifying
  scoopable stars and the next-jump marker. They describe the star's fuel-scooping
  capability, rather than fuel level or whether the system has been visited.

## Commander Powerplay status

- **Show the current pledged power, rank, and merits.** Frontier refresh now
  reads modern Powerplay status from Frontier's journal API instead of treating
  the profile's legacy zero rating as “None.” Ranks above 100 display correctly.
  Available EDAssets power emblems now identify the pledge in its power color;
  Jerome Archer and Nakato Kaine retain a generic badge because EDAssets does
  not yet provide their emblems.
- **Follow updates during play.** Newer local journal events update the active
  commander's card. Account switching keeps each commander's status separate;
  unavailable or incomplete API journals preserve the last known status. The
  card tooltip shows its journal source and event time.

## Update channel and packages

RC71 uses the schema-2 `xp2-v` development channel. RC51 remains the legacy
compatibility bridge. Windows and Linux packages are self-contained.

- Version: `2.1.3.0-rc.71`
- Tag: `xp2-v2.1.3.0-rc.71`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.71-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.71-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.71-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.71-x86_64.AppImage.zsync`

The numeric Windows `FileVersion` remains `2.1.3.0`.

## Testing notice and SteamOS limits

This remains a development preview. Private Gamescope compositor tests pass;
actual Deck touch, trackpad, controller, suspend/resume, docking, and SteamOS
version differences still need device validation.

Turn the Deck performance HUD off because it competes for the single external
overlay slot. Custom maximum/per-window scaling is unsupported. If scaling
becomes unknown, select a scaling mode again in Steam. Automatic Galaxy Map text
entry is unavailable in external Gamescope sessions, and primary-root X11
capture cannot supply the composited game image for image-based features.
