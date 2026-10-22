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

| Upstream saved data | XP behavior and evidence |
| --- | --- |
| [`F{FID}-live.json` / `F{FID}-legacy.json`](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/game/CommanderSettings.cs) | Compatible filenames and field names. Player identity, exploration totals, biological claims and samples, sphere/boxel searches, Ram Tah progress, combat missions, journey references, and per-player service keys have corresponding readers. Unknown fields remain in the copied JSON. Tests import multiple commanders and both galaxy profiles, then load the files through the actual catalog and stores. |
| [`F{FID}-codex.json` / `F{FID}-codex-{region}.json`](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/game/CommanderCodex.cs) | Compatible global and regional ledgers. Upstream `CodexFirst` writes `timestamp_systemAddress_bodyId` strings; XP reads those strings and its supported object form. Regression assertions cover discoveries, precise 64-bit system addresses above 2^53, body IDs, and manual entries with `-1` locations. |
| [`systems/{FID}/*.json`](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/game/SystemData.cs) | Recursively copied with original names and bytes; the existing legacy system readers retain survey and biology history. |
| Retired `organic/{FID}/*.json` and `scannedOrganics` | Preserved in the backup and imported tree. `LegacyOrganicProfileMigrator` translates retired claims and body files into the existing system-data format. Its existing import and migration tests cover this separate conversion. |
| [`journey/{FID}/*.json`](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/game/CommanderJourney.cs) | Copied unchanged; the audit test loads the imported active journey through `JourneyStore`. |
| [`routes/{FID}.json`](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/game/FollowRoute.cs) | Retained; `FollowRouteStore` recognizes the original lowercase directory and compact hop representation. |
| [`guardian/{FID}/`](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/game/GuardianSiteData.cs), [`human/{FID}/`](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/game/HumanSiteData.cs), and their `legacy` subdirectories | Retained recursively, including Guardian beacons and surveys. Existing activity-specific readers and tests cover their interpretation. |
| [`F{FID}-colony.json`](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/game/ColonyData.cs), quest state, bookmarks, caches, and unrecognized files | Retained recursively. `LegacyColonizationProfileStore` and `LegacyQuestStateStore` retain the original formats; unknown files are not discarded. |
| [`settings.json`](https://github.com/njthomson/SrvSurvey/blob/1a734c1412cb78cef6ff7e2e20ea8af7b5ccd7cc/SrvSurvey/Settings.cs) | Copied unchanged, then compatible preferences are translated by `LegacyUiSettingsMigrator`. Malformed preferences do not overwrite the current UI settings. |
| Original `plotters.json` and `vr/` calibration files | Preserved only in the verified source backup. They are excluded from activation and cannot replace current XP desktop or VR positions. Fresh XP profiles use their own default positions. The old absolute-position normalization migration has been removed. XP-to-XP layout imports remain supported. |

The new player/Codex regression compares every imported file with both the
source and its verified backup. It checks the readable data, not just a
successful file copy. The fixtures use synthetic player names and keys.

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

Current UI preferences also win, while missing compatible preferences can be
filled. Original overlay positions remain excluded. XP layout imports can fill
missing panels while preserving current commander positions. Shared and nested
commander layouts use the pre-import snapshot to identify new panels. Source
placement provenance is retained until display references are assigned, so a
failed adjacent-settings import can be repaired by retrying without rescaling
panels the user has moved manually. Damaged optional overrides are skipped
individually without rolling back other recovered panels. Unsupported file
collisions retain the current file; both originals remain recoverable.

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
