# SrvSurvey-XP 2.1.3.0-rc.47

- Replaces the Mining workspace placeholder with session accounting, prospecting
  yields, core and raw-material tracking, cargo, mining missions, historical
  reports, screenshots, manual refinery estimates and mining settings.
- Adds shared Navigation → Bookmarks with categories, filters, mining annotations
  and import/export. Mining uses the same catalog.
- Adds local/reference/Spansh hotspot searches, commodity-market and system/trader
  searches, existing Fleet Carrier data and distance shortcuts. Optional
  receive-only EDDN observations supplement prices and Powerplay information.
- Adds ship-only Mining notifications and a Firegroups reference overlay, with
  shared editor/live presentations, overlay controls and input toggles.
  Existing Rhino overlays are unchanged by this workspace expansion.
- Adds announcement filters/presets and optional Windows speech, CSV history
  import, offline HTML reports with print/PDF output, and ZIP backups containing
  shared bookmarks and screenshot attachments. Guides documents the new tools.

RC47 improves multi-install Commander discovery on Linux and keeps a Fleet
Carrier's plotted jump synchronized between Frontier companion data and the live
journal. It retains the full RC46.5 feature set and the prior release-candidate
changes summarized below.

## New in RC47

- Detects journals from simultaneous Steam, Epic/Heroic, Frontier/Wine, Lutris
  and Bottles installations on Linux, including common default and custom Wine
  prefixes. Each launcher prefix receives an independent bounded scan so a large
  unrelated game tree cannot hide a valid sibling installation.
- Combines every discovered journal folder when loading Commander profiles. The
  main selector, Journal post-processor and Visited Stars cache now all include
  journal-only Commanders from secondary installations, supporting multibox use.
- Loads the currently plotted Fleet Carrier jump from the Frontier companion API
  during refresh and applies it to the active carrier profile.
- Synchronizes plotted carrier jumps immediately from live journal request,
  cancellation, completion and location events, with the companion and journal
  paths sharing the same update behavior.
- Adds focused regression coverage for mixed installations, journal-only
  Commanders, bounded launcher traversal, companion refresh and live carrier
  jump synchronization. Launcher configuration matching now also has a bounded
  execution time.

RC46.5 completes the Surface Mining mapping workflow with variable site borders,
deposit-specific ratings, automatic map selection, bookmark favorites and more
precise map and overlay tools. It retains the full RC46 feature set and the prior
release-candidate changes summarized below.

## New in RC46.5

- Updates `.mining` to record each site's measured border radius and moves mineral
  amount and density to each `.mine` deposit. Low, Medium and High are accepted
  for both deposit values, and map rings extend in 1 km steps far enough to
  enclose the saved border.
- Adds center correction, marker movement, duplicate protection and deposit
  placement from the player's live position anywhere inside the selected map.
  Saved maps now load automatically on entry and unload after leaving the site.
- Adds expandable deposit details, favorites and a favorites-only filter to
  Surface Maps. Shared bookmark export and import preserve the complete Surface
  Mining map, including its center, radius, markers, ratings, notes and favorite.
- Adds amount and density marker filters, a draggable 4.5 km planning circle,
  optional marker labels in the Overview Map and a compact alignment helper sized
  consistently across common game resolutions.
- Refreshes the Surface Mining workflow guide and its in-app examples for the
  revised commands and HUD procedure.

## New in RC46

- Adds Activities > Surface Mining for saving 2.47 km surface mining locations and
  deposit markers from case-insensitive chat commands. The workspace includes
  a fixed-upright survey map with Guardian-style zoom and pan controls, dynamic
  filters backed by the shared Navigation → Bookmarks catalog and the community surface-hotspot
  commodity/body/price table with distinct map-marker colors. Invalid map
  commands now explain the rejected value through Status notifications. Surface
  map rows open the map from any column and provide copy-system and edit-bookmark
  context actions; Rhino controls now live in Surface Mining overlay settings.
- Fixes shared Surface Mining bookmarks losing center and deposit coordinates
  after reload. Reopening the same bookmark now reliably returns to its survey,
  marker filters refresh immediately, and Selected Map shows the system, body,
  signal, mineral amount and density.
- Adds a persisted Overlay checkbox to each Hotspot List commodity and a compact,
  theme-aware Mining Ref overlay showing the selected commodity, compatible body
  types and average CR/t. The panel has its own Mining settings entry, position
  editor preview and visibility shortcut.
- Adds a sortable Surface Hunt reference after Hotspot List with preferred and
  alternate body types, geology, stellar clues, and market prices. The same
  decoded reference now corrects Hotspot List availability and canonical names
  while preserving existing command aliases and Mining Ref selections. Both
  reference tables scroll horizontally when the workspace is too narrow for
  their aligned columns. Horizontal trackpad gestures, horizontal mouse wheels,
  and Shift+wheel now reach the nearest horizontally scrollable pane throughout
  the desktop application, including from inside nested result rows.
- Improves Surface Mining map legibility across all six application themes with
  dedicated grid contrast, theme-correct labels and adaptive marker outlines in
  both the workspace survey map and its overlay.
- Keeps the Surface Mining map's distance grid fixed at 1, 2, 3 and 4 km while
  its rings, location boundary, deposit markers and live player marker enlarge
  together during zoom. Marker labels grow conservatively so nearby deposits
  remain readable.
- Keeps Overview, Fleet Carrier, Firegroups and utility shortcuts fixed in the
  sidebar while only the accordion navigation scrolls. Accordion groups now
  start collapsed, and obsolete overlay migration copy is removed from Desktop
  settings.
- Adds a prominent Privacy & Sharing warning to keep equivalent journal and
  companion-data publication enabled in only one Elite Dangerous third-party
  application, preventing duplicate entries and conflicting updates.
- Hardens the new mining tools by keeping each results table's sort state
  independent, accepting distinct surface signals during bookmark imports,
  validating nested map data, resuming interrupted legacy-map migrations, and
  keeping the overlay zoom controls in the shared host on Linux. Surface-map
  rows respond on every click, and startup no longer creates sample bookmarks.

## Packaging

- Version: `2.1.3.0-rc.47`
- Tag: `xp-v2.1.3.0-rc.47`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.47-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.47-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.47-x86_64.AppImage`

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
