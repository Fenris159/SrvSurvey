# SrvSurvey-XP 2.1.3.0-rc.77.9

RC77.9 adds journal-backed Exploration history and brings the Mining,
Colonization, Codex Bingo, overlay settings, and Diagnostics workspaces into
alignment with the application's spaceship-panel design. Layouts use the active
theme, clearer control groups, and space reserved for scrollbars.

## What's changed

### Exploration history and estimated value

- **Track unsold cartographic data:** An optional setting in Exploration's
  Commander profile panel adjusts estimated value after recorded sales and
  confirmed ship losses. It is off by default and saved per commander. Only
  systems identified by a sale are cleared, so nearby unsold data stays in the
  balance. Rejoining a surviving ship after a surface death preserves its data.
- **Browse Exploration history:** A dedicated workspace tab presents Sold,
  Unsold, and Lost entries with status colors, system coordinates, scan and
  mapping counts, estimated value, and collection and settlement dates. Filter
  by system name, status, or dates; click a system name to copy it and select a
  row to inspect its body details and original journal records. Selected details
  stay tied to the original collected data during refreshes and imports.
- **Spot notable discoveries:** A Notable column highlights ELW, WW, AW,
  terraformable worlds, GGG, NSP, neutron stars, and black holes. Compact tags
  wrap within their column and show full descriptions on hover. Tags reflect
  recorded system discoveries and do not change credit estimates.
- **Backfill automatically:** History is catalogued from each commander's
  available journals, with Live and Legacy kept separate. A compact local
  metadata index stores ownership and source references rather than another
  copy of the journals. Missing originals are flagged, while catalogued history
  is retained. Late imports and changed sources are reconciled transactionally.
- **Keep survey totals:** Automatic value tracking preserves jump, distance,
  scan, mapping, and landing counters. Mapping upgrades add only their unpaid
  increase, and recorded sale earnings remain separate from estimates.

### Colonization and Fleet Carrier loading

- **Separate Loading projects:** Fleet Carrier Loading projects have their own
  Loading status filter and no longer appear under Building. Carrier link and
  unlink actions are available for both Building and Loading results; Planning
  projects remain excluded.
- **Correct loading progress:** Carrier loading projects with no fulfilled needs
  display 0% progress rather than an unknown percentage.
- **Show prepared buildings:** Loading previews and project details list the
  prepared building names and quantities. Comma-separated summaries wrap across
  the preview width, and loading details replace unused system, body, and
  faction fields with the building list.
- **Align planning workflows:** Projects, System Editor, and project preparation
  forms use clearer sections and aligned controls, with cargo requirements and
  estimated trips in the current ship kept visible.

### Workspace and settings presentation

- **Mining:** Session, Reports, and Settings use clearer groups and better-sized
  tables. Commodity names begin with a capital letter, and Resource Extraction
  Site reference descriptions wrap instead of widening their panel. EDDN
  reception and the local mining search cache are removed; existing Spansh and
  Ardent searches and opt-in EDDN contributions remain available.
- **Codex Bingo:** Discovery hierarchy, progress, locations, and related tools
  use a consistent panel layout, with content kept clear of scrollbars.
- **Overlay settings:** Category settings, exceptions, and shortcut controls use
  aligned sections and clearer interactions across application themes.
- **Diagnostics:** History, Processing, and Inspector have fixed headings,
  better-aligned sections, clearer actions, and reserved scrollbar gutters.
- **Quiet preview bindings:** Carrier readiness no longer logs null-snapshot
  Maximum binding errors while a build preview is waiting for data.
