# Original SrvSurvey profile import audit

Audited on 2026-10-21 against upstream `main` at
[`1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc`](https://github.com/njthomson/SrvSurvey/commit/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc).
The comparison uses the original application's writers, synthetic saved files,
the production Settings import workflow, and the existing storage tests. No
reporter's profile or exact error log was available for this audit.

## Saved locations and source selection

Upstream's [`Program.dataFolder`](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/Program.cs#L15-L18)
is `%APPDATA%\SrvSurvey\SrvSurvey\1.1.0.0`. Its Microsoft Store migration also
uses `LocalCache\Roaming\SrvSurvey\SrvSurvey` and `SrvSurvey-` under package
`35333NosmohtSoftware.142860789C73F_p4c193bsm1z5a`.
[`checkAndMigrateAppStoreRoamingFolder`](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/Program.cs#L408-L475)
documents that backup layout.

The audit reproduced these selection failures and added regressions:

- Selecting the common SrvSurvey parent could resolve to XP's own populated
  `cross-platform` directory. The importer then correctly rejected overlapping
  source and destination paths. Settings now passes the active data directory
  to the locator, which excludes it when resolving a parent selection.
- The Store `SrvSurvey-` backup tree was not resolved from its parent, so player
  files could remain nested below the directory read by the application.
- A newer directory containing only logs could be selected ahead of a version
  containing player data. Discovery and version selection now require a profile
  signature: `settings.json` or a top-level `F*-*.json` file.

Windows verification compares directory identities as sets using Windows path
rules. An original `routes` directory and an existing XP `Routes` directory can
therefore merge without their different sort positions causing a false
"unexpected, missing, or changed files" error. Exact file hashes, missing/extra
file checks, concurrent-write detection, and rollback remain enforced.

The original `codexImages` download cache is excluded before traversal, hashing,
backup, and activation. These reference images can be downloaded on demand;
the original cache is left untouched. Import does not copy the old cache path
or enable the legacy bulk-download preference. Current XP image preferences
remain in place, and personal local-flora photos are still imported and their
paths relocated.

Selecting another XP profile still supports its `cross-platform` directory and
adjacent `cross-platform-ui.json`, including folder paths with a trailing
separator. Selecting the active destination explicitly
continues to fail the overlap check.

On Linux, automatic detection also searches `.wine`, Steam/Proton compatdata
for any application ID, native and Flatpak Steam libraries, external libraries
from `libraryfolders.vdf`, native and Flatpak Bottles, `~/Games`, configured
Heroic/Lutris prefixes, `WINEPREFIX`, and `STEAM_COMPAT_DATA_PATH`. Traversal is
bounded and skips child symbolic links. Windows user directories within each
prefix are checked for the original roaming and Store profile locations.
Manual folder selection remains available for custom locations; a Wine prefix,
Proton application/pfx directory, or `drive_c` can also be selected directly.
A prefix without a recognized SrvSurvey profile is rejected before copying its
Windows files.

## Data compatibility

New data files retain their original bytes. Collisions follow the merge rules
below; raw source files always remain in the verified backup.

| Upstream saved data | XP behavior and evidence |
| --- | --- |
| [`F{FID}-live.json` / `F{FID}-legacy.json`](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/game/CommanderSettings.cs) | Compatible filenames and field names. Player identity, exploration totals, biological claims and samples, sphere/boxel searches, Ram Tah progress, combat missions, journey references, and per-player service keys have corresponding readers. Unknown fields remain in the copied JSON. Tests import multiple commanders and both galaxy profiles, then load the files through the actual catalog and stores. |
| [`F{FID}-codex.json` / `F{FID}-codex-{region}.json`](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/game/CommanderCodex.cs) | Compatible global and regional ledgers. Upstream `CodexFirst` writes `timestamp_systemAddress_bodyId` strings; XP reads those strings and its supported object form. Regression assertions cover discoveries, precise 64-bit system addresses above 2^53, body IDs, and manual entries with `-1` locations. |
| [`systems/{FID}/*.json`](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/game/SystemData.cs) | Recursively copied with original names and bytes; the existing legacy system readers retain survey and biology history. |
| Retired `organic/{FID}/*.json` and `scannedOrganics` | Preserved in the backup and imported tree. `LegacyOrganicProfileMigrator` translates retired claims and body files into the existing system-data format. Its existing import and migration tests cover this separate conversion. |
| [`journey/{FID}/*.json`](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/game/CommanderJourney.cs) | Copied unchanged; the audit test loads the imported active journey through `JourneyStore`. The Journeys workspace upgrades legacy metadata on first use, retaining the original file as a `.pre-upgrade.bak` backup. |
| [`routes/{FID}.json`](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/game/FollowRoute.cs) | Retained; `FollowRouteStore` recognizes the original lowercase directory and compact hop representation. |
| [`guardian/{FID}/`](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/game/GuardianSiteData.cs), [`human/{FID}/`](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/game/HumanSiteData.cs), and their `legacy` subdirectories | Retained recursively, including Guardian beacons and surveys. Existing activity-specific readers and tests cover their interpretation. |
| [`F{FID}-colony.json`](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/game/ColonyData.cs), quest state, bookmarks, caches, and unrecognized files | Retained recursively. `LegacyColonizationProfileStore` and `LegacyQuestStateStore` retain the original formats; unknown files are not discarded. |
| Downloaded `codexImages/` cache | Excluded before inventory, hashing, backup, and activation. Existing destination caches remain intact. Personal local-flora images are preserved; legacy cache-path and bulk-download preferences are not imported. |
| [`settings.json`](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/Settings.cs) | Backed up unchanged, then compatible non-overlay preferences are translated by `LegacyUiSettingsMigrator`. Incoming `plotterOpacity` is removed from the activated settings file; current opacity and unsaved defaults are preserved. Other overlay preferences are excluded from UI preference migration. Malformed preferences do not overwrite the current UI settings. |
| Overlay layout files from original SrvSurvey or XP: shared `plotters.json`, `overlay-*`, `theme.json`, `vr/`, and `commanders/{FID}/overlays/` | Preserved in the verified source backup and excluded from activation for every profile import. Current desktop/VR positions, references, sizes, and calibrations remain unchanged. Fresh profiles use XP defaults. Layout publication, display-frame inference, and pending position migration are not part of profile import. |

The new player/Codex regression compares every imported file with both the
source and its verified backup. It checks the readable data, not just a
successful file copy. The fixtures use synthetic player names and keys.

### Journey upgrades and recovery

The original and earlier XP Journey layouts remain readable, including compact
and object star references and both timestamp-based filename conventions. On
first use, the Journeys workspace adds schema version 1 and converts saved
Windows or Unix journal paths to portable filenames. The conversion retains the
Journey filename, timestamps, visits, statistics, notes counts, and unknown JSON
fields. It keeps an exact `.pre-upgrade.bak` copy beside the original before
atomically replacing metadata; repeat loads do not rewrite the backup. A failed
upgrade leaves readable saved history available and reports the write failure.

A readable active Journey remains available to continue or complete when its
original journals are missing or cannot be replayed. This recovery does not
rebuild or discard its saved visits. Confirmed missing Journey files have their
stale active references cleared automatically. Malformed, inaccessible, foreign
commander, or newer unsupported files remain untouched and produce warnings
instead of blocking a new Journey. Beginning or resuming a readable Journey
replaces the unavailable selection without changing its saved file. The workspace
also offers **Clear unavailable journey** to detach the selection independently.
Recovery rechecks the file and selection before clearing anything.
Refresh retries initialization after a file is restored or repaired.
Saved active Journeys whose profile selection was already cleared can be
selected in the library and resumed explicitly. Neither a completed Journey nor
a preserved unavailable record reserves the filename for a new Journey at the
same starting visit; the new record receives a separate filename without
changing the old file.

Journey starts reconcile the active selection for the requested commander and
galaxy profile, then read the same commander's Journey catalog. Only a readable
record marked **Active** in that catalog blocks a new start, even if its profile
selection was cleared; it can be resumed and completed from the list. The New
Journey and Begin commands use the same catalog rule. An old in-memory snapshot
or an unavailable selection cannot block an otherwise empty catalog. A named
Journey, a valid starting visit, and writable Journey/profile storage remain
required. Diagnostics record the
commander ID, galaxy mode, actual profile path, and in-memory Journey identity;
they do not dump profile contents or service keys.

## Backup, merge, and recovery contract

Imports add missing data and retain current values. Global and regional Codex
ledgers union discoveries by entry ID. Valid current records, including manual
Bingo checks and their location/time metadata, win duplicate numeric IDs, including noncanonical JSON-key aliases. An incoming
valid record can repair an invalid current entry. Current player totals and
scalars remain unchanged; the unsold exploration ledger stays with its current
total so older imports cannot resurrect sold systems. Nested history maps gain
missing keys. Permanent Ram Tah
completion sets gain missing entries. Current arrays and explicit null values
are retained by default, including empty unsold-claim lists: importing older
claims must not resurrect a sale already completed in XP. Retired
`scannedOrganics` claims are not added to a player that already has the modern
claim list. New commanders, modes, and files are added.

Current UI preferences also win, while missing compatible non-overlay preferences
can be filled. Both original-client and XP imports exclude incoming overlay
preferences as well as layout files. Visibility, opacity, scale, panel content, suppression,
streaming, VR, capture calibration, and shortcut/device settings stay with the
current installation. Missing settings retain XP defaults; existing explicit
choices and partial settings sections remain unchanged. This also applies when
startup resumes a completed import. Colonization delivery recovery records and
non-overlay service preferences remain eligible for the normal merge.
Commander mining files also exclude incoming overlay visibility, session-only
suppression, notification duration, prospect-slot count, and below-threshold
display settings. Mining history, missions, and portable mining preferences
remain eligible for import; their original settings stay in the verified backup.

Overlay layouts from every imported client are backed up and skipped, including
shared layouts and commander-specific overlay directories. Importing UI
preferences never rewrites current overlay position references. An XP source
containing only layout files can still merge its selected adjacent UI preferences
with an empty data payload; the layout files remain excluded. This keeps
profile import focused on player/Codex data; native backup and restore remains
a separate workflow with its existing machine-specific restore rules.
Unsupported file collisions retain the current file; both originals remain
recoverable.

Source scanning and capture recover readable files individually. Locked,
inaccessible, changing, linked, or incompatible files produce path-specific
warnings; one failure does not discard other recovered player/Codex data.
Malformed player/Codex files, including duplicate JSON properties, are backed up
but excluded from activation. Valid
entries in a partially malformed Codex ledger remain usable. Other unknown or
malformed JSON files are retained without conversion when no current version
would be replaced. A failed settings conversion still activates usable player
data and completes the normal restart workflow.

The source is never edited. Each successfully captured raw file is SHA-256
verified in `profile/`; the current destination receives a separately verified
`previous-destination/` backup. A separate `import-payload/` contains new/merged
files with their own inventory. Manifest version 4 records the raw source,
previous destination, conflicts, skipped paths, warnings, and `ActivatedEntries`.
`GetImportedEntries()` returns actual activated hashes, including merged JSON,
and remains compatible with older manifests. An old source import manifest is
backed up but excluded from activation.

The combined profile is staged and verified, and the current destination must
remain unchanged through activation. Destination write/activation errors retain
the captured recovery backup and preserve or roll back the current profile.
The exception and Settings message identify its location. Successfully recovered
data is kept even when some inputs fail, and `.srv-survey-recovery.json` records
warning details. Optional-step warnings are also persisted in the destination
manifest so they and the report location remain visible after restart. Repeated imports are permitted and preserve progress made since
the previous import; warning-producing imports offer a recovery retry.

## Recovering overlay preferences after an older import

Earlier builds excluded layout files but could still fill missing XP overlay
preferences with disabled legacy visibility flags. Restarting does not reverse
those saved preferences. This does not establish that any particular report has
this cause; the affected installation's settings are needed to confirm it.

Install a build with the overlay-preference import fix before restoring settings.
The import's verified backup may contain `previous-cross-platform-ui.json`, which
is the pre-import UI document. With XP closed, retain a copy of the current UI
file, then restore that backup to the same active commander's
`cross-platform-ui.json` location. Shared settings live in the config root;
commander-specific settings live in `commanders/{FID}/` beneath it. Restoring the
UI document also restores other UI preferences from that moment, so retain the
current copy for comparison. It does not undo imported player/Codex data. If no
previous UI document exists, review the overlay switches in Settings or restore
the desired overlay defaults there. Also review opacity in the overlay position
editor: older imports could add `plotterOpacity` to the legacy shared settings
file, which is separate from the UI document. Do not delete the imported
player-data tree.

## Operational limits

Close the source SrvSurvey during import to obtain a consistent snapshot. A
source file changing during capture is skipped with a warning. Root symbolic
links/junctions and overlapping source, destination, and backup trees remain
rejected. An unreadable current destination stops activation because a verified
backup cannot be guaranteed. A wholly unusable source leaves current data
unchanged and retains any raw files already captured.

Persistent array formats without a defined identity are kept from the current
file rather than combined speculatively; the incoming version remains backed up.
No reporter's exact error or profile was supplied, so the reproduced failure
cases do not establish the cause of every reported import failure.

## Validation

Regression tests exercise the production importer, real player/Codex readers,
and Settings workflow. They cover current Bingo progress plus older discoveries,
regional ledgers, repeated imports, current player totals and sold-claim safety,
byte-identical raw backups, malformed/locked/changing input, path collisions,
optional UI failure and restart, and native/external/Flatpak Wine/Proton
selection. The repository quality gate checks formatting, localization,
changed-line analyzer findings, full affected test suites, and the 80% new-code
coverage requirement.
