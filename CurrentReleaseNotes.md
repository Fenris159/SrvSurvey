# SrvSurvey-XP 2.1.3.0-rc.69

RC69 combines more reliable Google Drive backups, clearer colonization shopping,
joined-overlay capture fixes, and corrected squadron carrier cargo tracking.
It also includes the Codex Bingo and nearest-search improvements: keep Bingo open,
search groups or individual entries without mandatory colors, and find up to
20 matching systems.

## What's changed

- **Back up larger profiles to Google Drive.** Portable profiles are compacted
  before upload, and the supported size increases from 32 MiB to 128 MiB.
  Existing backups remain readable; use an updated build on each linked computer
  when sharing backups above the old limit.
- **Resume interrupted backups.** Drive uploads use acknowledged chunks with
  bounded retries, including temporary rate limits, instead of restarting the entire transfer. Manual backup and
  sync operations have up to ten minutes and a **Cancel operation** control.
  Failed or canceled transfers retain local recovery data, and startup/shutdown
  work keeps its own shorter deadline. Canceling a cloud restore during local
  preparation also prevents it from being staged for the next startup, while
  preserving any previously staged restore and its baseline.
- **Understand backup failures.** Progress identifies capture, comparison, and
  upload stages. Expired authorization gives disconnect/re-link guidance,
  while publisher configuration errors have a separate message. Concurrent
  instances preserve newer saved authorization during a failed refresh. Startup
  sync preserves edits made by another instance during cloud requests, and
  conflict choices retain the local snapshot used to identify the conflict.
- **Uncheck matching colonization projects together.** In **My build projects**,
  **<--Uncheck Selection** clears **Show** only for projects matching both the
  selected system and build-status filters.
- **Read market tags in every theme.** Short-supply commodity tags match the
  **Short supply** toggle color. ShoppingList selections keep their secondary
  fill and gain a shortage-colored outline when needed. Monochrome tags use
  muted champagne instead of bright white, and tooltip text remains readable.
- **Find the joined stream overlay in Windows OBS.** The enabled capture surface
  is eligible for OBS window selection and stays open when switching from Elite
  to OBS. Minimizing Elite retains the last usable capture size and position;
  brief window-tracking failures have a bounded grace period.
  Look for **[SrvSurvey.Desktop.exe]: SrvSurveyWindowOne overlay**.
  Other gameplay panels keep their existing focus behavior.
- **Track squadron carrier cargo once per movement.** Bank transfers and market
  purchases/sales are reconciled against the same ship inventory baseline, so
  count-only Cargo events cannot repeat a purchase or omit an interleaved bank
  movement. Market-only stock no longer replaces combined squadron cargo;
  complete Frontier manifests and live adjustments remain supported. If a cargo
  write has an uncertain outcome, undock and refresh Frontier data: an accepted
  full manifest reconciles retained writes after Raven acknowledges its totals,
  allowing later cargo updates to continue. Cached manifests cannot retire newer
  bank movements, and manifest replacement waits for active cargo retries.
- **Keep Codex Bingo open.** Finding nearest locations for an entry, a parent
  group, or missing entries preserves the Bingo window and its selection. The
  window stays available until you close it.
- **Search a whole Codex group.** Parent selections now search their actual
  entries and automatically fill and reveal the Codex search controls. For
  example, **Bell Mollusc** searches its five full subtype names; **Albens Bell
  Mollusc** is a subtype and does not require a separate color.
- **Choose entries without biology-only restrictions.** **Codex entry or group**
  offers searchable suggestions and accepts typed names. **Spansh landmarks**
  provides optional Type, Subtypes, and Variants fields. Enter a Type or Subtype
  to search; a color is optional. Searches support the indexed biology,
  space-based life, geology, Guardian, Thargoid, anomaly, and green gas-giant
  entries without assuming that every feature is Odyssey biology.
- **Read field guidance without clipped hints.** Short examples replace long
  placeholders, and longer instructions wrap outside the fields. Type accepts
  one name. Subtypes and Variants accept comma- or semicolon-separated lists,
  matching any listed value within each field. The search-mode description also
  wraps below the dropdown.
- **Find distant matches without a nearby radius cap.** Results are sorted by
  distance from the reference system and include up to **20 unique systems**.
  Spansh body results are paged and combined so several matching bodies in one
  system do not consume separate system slots.
- **See details appropriate to the feature.** Results identify matching Codex
  entries, applicable variants, bodies, arrival distances, and their source.
  Non-biological searches no longer show unrelated biology counts or warnings.
  Open the matching system or saved Spansh search from the results.
- **Understand source coverage and failures.** Verified Spansh names are used
  directly, with Canonn fallback where supported. Coverage notes identify
  unsupported entries, approximate Canonn ordering, and incomplete results when
  one source fails. A service failure is reported separately from a completed
  search with no published matches.
- **Keep results tied to the current search.** Changing criteria, commander, or
  reference coordinates cancels older requests. Late responses cannot replace
  newer results, repeated provider pages stop cleanly, and closing the
  application cancels its pending search.

## Update channel and packages

RC69 uses the schema-2 `xp2-v` development channel. RC51 remains the legacy
compatibility bridge. Windows and Linux packages are self-contained.

- Version: `2.1.3.0-rc.69`
- Tag: `xp2-v2.1.3.0-rc.69`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.69-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.69-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.69-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.69-x86_64.AppImage.zsync`

The numeric Windows `FileVersion` remains `2.1.3.0`.

## Testing notice

This remains a development preview. Search results depend on published Spansh
and Canonn observations, and distant matches are expected for rare entries.
Missing reports do not establish that an entry is absent in game. Canonn's
nearest-candidate ordering is approximate and is identified in the search view.
