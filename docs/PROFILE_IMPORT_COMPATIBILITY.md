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

## Backup and collision contract

The importer still inventories and SHA-256 verifies the entire source,
including skipped layouts. It independently backs up the existing destination,
stages the selected files, verifies both originals have remained unchanged,
and activates the staged directory with rollback protection.

Manifest version 3 keeps the full source inventory and records
`SkippedRelativePaths`; `GetImportedEntries()` identifies the activated source
files. Readers continue to accept earlier manifests without the skip list.
Conflict records and displayed imported counts exclude skipped files.

Same-name player and Codex files retain the established source-wins rule;
this is a profile import, not a record-by-record synchronization. Destination-only
files are retained, and overwritten files remain under the verified backup's
`previous-destination` directory. Original source files are never edited.

An XP source may itself contain `.srv-survey-import.json`. Previously that old
manifest collided with the new manifest during staging. It is now backed up
with the source and excluded from activation, which receives a fresh manifest.

## Operational limits

Close original SrvSurvey during import: changes to its files while copying
intentionally stop activation. Symbolic links and junctions remain rejected,
and the source, destination, and backup trees must not overlap. Existing
completed imports are not silently repeated or used to reset a user's layout.
An exact reporter error remains necessary to establish whether these reproduced
failures account for every reported import failure.

## Validation

- Release solution build: zero warnings and errors.
- Focused import, player, Codex, and organic storage tests: 72 passed.
- Settings import workflow, settings migration, and commander layout tests:
  30 passed, including original and XP theme/layout import policies.
- The required `pwsh ./tools/Test-ChangedCodeQuality.ps1` gate runs through the
  local pre-commit hook, including full-suite coverage collection, formatting,
  analyzer checks, and localization validation.
