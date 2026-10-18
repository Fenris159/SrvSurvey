# SrvSurvey-XP 2.1.3.0-rc.68

RC68 keeps Codex Bingo open while you search and makes nearest Codex locations
easier to find. Search groups or individual entries without mandatory color
filters, and see up to 20 matching systems with details about the actual feature.

## What's changed

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

RC68 uses the schema-2 `xp2-v` development channel. RC51 remains the legacy
compatibility bridge. Windows and Linux packages are self-contained.

- Version: `2.1.3.0-rc.68`
- Tag: `xp2-v2.1.3.0-rc.68`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.68-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.68-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.68-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.68-x86_64.AppImage.zsync`

The numeric Windows `FileVersion` remains `2.1.3.0`.

## Testing notice

This remains a development preview. Search results depend on published Spansh
and Canonn observations, and distant matches are expected for rare entries.
Missing reports do not establish that an entry is absent in game. Canonn's
nearest-candidate ordering is approximate and is identified in the search view.
