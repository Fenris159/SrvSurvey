# Rhino mining delta

Compared on 2026-09-05 with upstream [PR #1055](https://github.com/njthomson/SrvSurvey/pull/1055),
merged as `91e07f84b98f658fe662fe2d89cf44ff9ac59dce`. Its mining commit is
`4fd0260b57ca29ba72f8ad57db1c296ff3118218`; the preceding miscellaneous
changes are in `c9e82b8db20eebece016735b1b6e8f281229eca6`.

## Adaptation

- Surface mining reuses the Surface Survey radar control and chevron styling,
  with a separate presentation and passive overlay coordinator. It supports
  both existing overlay hosts, the position editor, and dynamic theme resources.
- The default radar scale is 4 meters per pixel, equivalent to legacy's 0.25
  scale. Vehicle center is 4 meters behind the cockpit; deployed rigs are 7
  meters behind it. Spherical offsets preserve distance at high latitudes and
  across the longitude boundary.
- Six Alt+1 through Alt+6 bindings toggle saved rig circles and matching
  chevrons. Rings are 70 meters; pickup is below 5 meters and the deployment
  exclusion cue is below 78 meters from vehicle center. Rig chevrons sit below
  the two-column Ship/Rhino tracker. Cargo uses SRV inventory with status fallback,
  out of 72. On foot the Rhino chevron uses the existing journal-derived parked
  SRV location; aboard it shows an X. Rig distances on foot use the player
  position without the cockpit offset, and rig placement still requires the Rhino.
- Rig bookmarks are stored under the application's `mining` data directory,
  using the existing Commander/system/body persistence format. They are
  independent of biological bookmarks and survive restart.
  Returning to the own ship through Embark or DockSRV clears the mining body's
  saved rigs before live body context can disappear. Rhino re-entry, taxi, and
  multicrew boarding retain them; persisted bootstrap events are not reapplied.
- Named ground resources reuse the existing surface-bookmark chat commands
  (`+name`, `-name`, `--name`) and Commander/body persistence. As in legacy
  `PlotTrackers`, every saved location has a bearing/distance and the near
  highlight threshold is 150 meters. The port lays these out in two columns
  below the rig slots with the existing single/double-chevron style and themed
  colors. Generic mining bookmark circles use the legacy 70-meter radius.
  This is manual bookmarking, not automatic mineral detection. Rig cleanup
  leaves resource bookmarks intact; long lists scroll without hiding cargo.
- Rhino identity is retained across launch, re-embark, and LoadGame, with parked
  identity tracked separately from the active vehicle. Passive mining requires
  a Rhino on a planetary surface or an on-foot player with a parked Rhino marker.
  Surface Survey and its mini tracker are suppressed during that mining activity,
  even when the Mining panel is disabled. Ending the activity restores normal rules.
- Mining appears under Activities with the existing overlay settings shortcut.
  Its workspace is intentionally WIP. The Mining settings and Input settings
  edit the same shortcut objects, including the panel visibility binding.

## Other upstream changes

- RavenColonial's `Demolish` status is represented in the API model. The
  existing Plan-only project picker already excludes demolished sites.
- Unknown Guardian sites already select site-type guidance when a template is
  absent (`GuardianViewModel.SetLiveMapModeFromSurvey`), retain the local survey
  when published data is unavailable (`HydrateSurveyFromPublished`), and skip
  persistence without Commander identity. This is nullable-state handling,
  rather than the legacy form's literal Commander-null visibility guard.
- The journal/DLC changes repeated in this PR were covered by the #1051 delta.
- Spansh Fleet Carrier changes are excluded at the maintainer's request.

## Theme and validation

Monochrome Companion pairs muted gray, champagne, cyan, green, and red with
the dark application theme. Flight warning markup and its fixed warning colors
are unchanged. Refuel/neutron styles remain intact; rendered biology pips,
commodity values, and jump-progress segments retain distinguishable states.

Regression coverage includes `SurfaceMiningGeometryTests`,
`SurfaceMiningViewModelTests`, Rhino identity in `JournalSessionStateTests`,
Rhino exclusivity in `SurfaceSurveyViewModelTests`, shared navigation/input
settings in `MainWindowViewModelTests`, and demolished-site deserialization.
The existing `UnknownStructureSiteTypeGuidanceUsesSiteCommand` test covers
Guardian guidance with empty reference, published, and template catalogs.
Headless production-template renders cover mining, biology, jump information,
colonization commodities, and flight warnings with the new preset.

Native Elite gameplay and Linux overlay-host validation remain separate
runtime checks; automated coverage does not claim those were exercised.
