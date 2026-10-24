# SrvSurvey-XP 2.1.3.0-rc.71.1

RC71.1 fixes original SrvSurvey profile imports, restores discovery credits in
Next Jump, and makes mining commodity labels consistent. It builds on RC71's
SteamOS and Gamescope overlay support.

## What's changed

- **Original SrvSurvey profile imports:** Backup, verify, and merge now accepts
  intact Windows profiles whose equivalent folder names use different casing,
  such as `routes` and `Routes`. File contents are still verified before the
  imported profile is activated, and recovery backups remain available.
- **Downloaded Codex images:** Imports skip the downloadable image cache,
  reducing unnecessary copying and hashing. Personal flora photos and existing
  XP caches are preserved. Imported image preferences no longer redirect XP to
  an old cache path or enable bulk image downloads.
- **Next Jump overlay discovery credits:** Available commander names and
  discovery dates now appear correctly, including when a name has no date.
  Discovery and update times recognize EDSM's UTC date format, and system
  discovery credits take priority over individual body credits.
- **Next Jump overlay previews:** The position editor now uses the live route
  and destination logic. Choose followed-route, neutron-stop, plotted-route,
  or direct-target examples. Scoopable badges follow the destination's star
  class; refuel and neutron badges follow the selected route stop. Invented
  detail rows and the preview-only success message have been removed.
- **Mining notifications and prospectors:** Commodity names such as Gold,
  Silver, and Palladium now use consistent capitalization. Multiword mineral
  and core names use their readable catalog spelling. Journal-provided
  translations are preserved.
- **Included RC71 features:** Steam Deck Gaming Mode and Gamescope overlays,
  automatic Next Jump panel height, clearer route markers, and
  Commander Powerplay status remain available. See the
  [Steam Deck Gaming Mode guide](https://github.com/Fenris159/SrvSurvey/blob/1dee430ad5f68455582fa3e1fa7be061afed240c/docs/STEAMOS_GAMING_MODE.md)
  for setup and compositor requirements.

## Update channel and packages

RC71.1 uses the schema-2 `xp2-v` development channel. RC51 remains the legacy
compatibility bridge. Windows and Linux packages are self-contained.

- Version: `2.1.3.0-rc.71.1`
- Tag: `xp2-v2.1.3.0-rc.71.1`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.71.1-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.71.1-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.71.1-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.71.1-x86_64.AppImage.zsync`

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
