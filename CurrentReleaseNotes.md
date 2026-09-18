# SrvSurvey-XP 2.1.3.0-rc.49.0

RC49.0 continues Avalonia colonization work from PR
[#139](https://github.com/Fenris159/SrvSurvey/pull/139) and fixes biology
overlay confirmed-vs-predicted styling, Canonn/journal scan recovery, and
theme defaults for prediction markers.

## New in RC49.0

### Biology overlay confirmed vs predicted

- System Biology PIPs for DSS genus + species guesses now use predicted
  styling (`bio.prediction`) to match Identified Bio rows with trailing
  `?`, instead of solid confirmed PIPs.
- Canonn merge no longer demotes a locally scanned organism when Canonn
  reports `scanned=false` for the commander; local `ScanOrganic`
  confirmation is preserved and Canonn commander-scanned rows can still
  upgrade unscanned identity.
- Organic `CodexEntry` alone still does not mark an organism confirmed;
  confirmation requires `ScanOrganic` or Canonn commander-scanned data.

### Journal ScanOrganic backfill

- When a system loads, recent journals are scanned for matching
  `ScanOrganic` events so samples taken while the app was off (or before
  a patch) become confirmed after restart.
- Lookback is the last 12 hours of **journal event timestamps** (anchored
  to the newest timestamp in the logs), not wall-clock time. Shorter
  journal spans simply keep every matching scan.
- Journals are opened shared read/write (`FileShare.ReadWrite`), same as
  the live monitor, so Elite can keep appending.

### Overlay theme defaults

- Default `bio.unknownGlyph` (trailing `?`) now matches `bio.prediction`
  across all seven overlay presets; the picker remains independently
  customizable.
- Expanded presets warm confirmed PIPs toward the values accent and pin
  prediction to the secondary accent so solid vs hatched PIPs stay
  distinct.
- Monochrome Companion uses muted Default orange for confirmed /
  confirmedDim / potential so confirmed PIPs stay apart from the
  white-like galactic-region candidate.

### Colonization (continued from RC48.9)

- Raven depot create/update, commander link/unlink, delivery remaining
  PATCH alignment, phantom need clears, and colonization workspace
  fit-to-content table/dropdown behavior from PR #139.

## Packaging

- Version: `2.1.3.0-rc.49.0`
- Tag: `xp-v2.1.3.0-rc.49.0`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.49.0-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.49.0-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.49.0-x86_64.AppImage`

Windows and Linux packages are self-contained. Linux packaging tools and the
AppImage runtime use versioned, checksum-verified downloads. AppImages are updated
manually through the selected XP release. Numeric Windows FileVersion remains
`2.1.3.0`.

## Testing notice

> [!IMPORTANT]
> This remains a work-in-progress preview for testing. Keep a backup of your
> existing SrvSurvey data and report unexpected behavior through the project
> issue tracker.

Native overlay behavior should still be exercised with Elite Dangerous on
clean Windows, X11, and XWayland systems. Pure native Wayland is not yet a
full-functionality overlay target.
