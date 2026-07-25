# Avalonia UI porting matrix

Last audited: 2026-07-25

This document is the review checklist for translating the legacy WinForms UI to
Avalonia. A view is marked complete only when its behavior is backed by the
ported core or a platform service; a visual placeholder does not count as
parity.

## Audit summary

- The legacy application is WinForms. Its production UI consists of `Main`, 34
  secondary form designers, and 22 overlay/plotter designers.
- The original `Main` is a fixed 437 by 548 pixel utility window. It combines
  commander/location status, an exploration counter, bio-scan progress, Codex
  access, Search, Travel, Guardian and Colonisation launchers, Settings, Logs,
  and Quit.
- Before this UI pass, the Avalonia application had one 820 by 620 journal
  bootstrap window. It displayed the state currently available from
  `SrvSurvey.Core`, but it did not reproduce the legacy navigation, theme
  behavior, or information hierarchy.
- The legacy theme system supports normal, dark, and experimental black window
  treatments plus editable orange and cyan game-overlay colours. Overlay colour
  semantics in `theme.json` remain a separate Phase 4 porting concern.

## Target information architecture

The fixed WinForms dashboard becomes a responsive desktop shell:

| Avalonia area | Legacy source | Purpose | Current status |
| --- | --- | --- | --- |
| Overview | `Main` commander group | Commander, game/session, system and body state | Implemented for bootstrap state; Windows visually checked |
| Exploration | `Main` exploration group | Jumps, distance, bodies and estimated value | Live counters, exact valuation, compatible persistence, and reset implemented; runtime visual recheck pending |
| Exobiology | `Main` bio group, `FormPredictions`, Codex forms | Scan progress, rewards and predictions | `Main` active-sample, separation, reward, sale/death, and reset workflow, exact `PlotBioSystem` species/variant predictions, standalone system/body predictions, `FormShowCodex`, full commander/region `FormCodexBingo`, `PlotPriorScans`, and consolidated `PlotGrounded`/`PlotTrackers` history, tracking, zoom, and quick-location workflow implemented; the experimental `PlotMiniTrack` variant and Human-site arbitration remain open |
| Travel | `Main` Travel menu, journey/route forms | Ground target, system notes, journeys and routes | Ground-target editor, clipboard/current actions, persistence, live guidance and passive overlay, system notes, Commander Journeys, followed-route workspace, imports, journal progression, and Galaxy Map guidance implemented |
| Search | `Main` Search menu, sphere/boxel/nearest forms | Spatial, boxel, and biological searches | Spherical center lookup, radius, enable/disable, live distance, and compatible persistence implemented; Boxel activation, hierarchy, source merging, ID64 decoding, completion, navigation, clipboard, and full-area audit implemented; nearest Canonn-signal and Spansh missing-variant searches plus result actions implemented |
| Guardian | `Main` Guardian menu and survey forms | Sites, maps, beacons and Ram Tah | Reference/commander catalog, visits, exact completion, filters, distance ordering, details, clipboard actions, live site detection/writes, native survey maps, survey editing, current-obelisk proximity/artifacts/scan actions, both Ram Tah missions, and a detached live map/current-obelisk overlay implemented; advanced map-authoring and remaining plotter modes remain |
| Colonisation | `Main` Colonise menu, project forms, and `PlotBuildCommodities` | Raven projects and construction state | Opt-in Raven project loading/selection and creation, live depot progress, cargo planning, Market guidance, linked Fleet Carrier cargo/sync, and a passive shopping overlay implemented; special squadron-FC/music auto-show rules remain |
| Diagnostics | `ViewLogs`, journal development tools | Journal source, candidate paths and logs | Journal source and parsed state implemented; full logs not ported |
| Settings | `FormSettings`, `FormSetKeyChord`, `FormAdjustOverlay` | Themes, paths, overlays, input and privacy | Raven themes, checksum-verified legacy profile import, persisted next-jump/system-survey/prior-scan/surface-radar/combat preferences, all 30 editable keyboard/controller bindings, opt-in SharpHook keyboard capture, and SDL controller discovery/polling implemented; general overlay adjustment and privacy settings remain |

Unavailable areas may appear in the shell to preserve discoverability, but they
must be labelled as pending and must not imply working behavior.

The implemented Exobiology page covers the original `Main` dashboard workflow.
The journal-backed `PlotBioSystem` overlay and its exact environmental
prediction engine are also implemented. The standalone predictions workspace
uses that evaluator for exact body rows, rewards, sample distances, and
first-footfall estimates. The compact `PlotBioStatus` replacement covers live
sampler progress and body summaries. The single-instance `FormShowCodex`
replacement covers confirmed and predicted biological entries, reference
images, navigation, temperature and reward guidance, and research links. Codex
Bingo now covers the complete hierarchy, commander and regional progress,
journal/Canonn imports, guarded manual state, discovery locations, research
links, and integrated nearest searches. `PlotPriorScans` now consumes validated
Canonn coordinates, filters commander/analyzed/low-value/nearby targets,
recalculates surface guidance, and supplies its configurable grounded-radar
circles. The journal-backed grounded radar now loads and atomically updates the
legacy touchdown, bookmark, and completed-scan records; tracks active samples,
ship and SRV locations; enforces the original surface/panel/landing-gear rules;
and renders heading-relative exclusion and tracker circles in the five legacy
window sizes. It also restores Composition Scanner auto-tracking and analyzed
filters, complete cross-system death marking, bounded zoom/reset, all eight
quick-location chords, and a compact tracker-only state. The experimental
dedicated `PlotMiniTrack` variant, Human-site arbitration, and remaining
biology surfaces stay open.

## Secondary forms

| Legacy surface | Avalonia destination | Status |
| --- | --- | --- |
| `FormAdjustOverlay` | Settings / Overlays | Not ported |
| `FormBeacons` | Guardian / Sites | Partially implemented with all shipped beacons/ruins/structures, commander visits/survey progress/notes, distance and text/kind/visit/type filters, details, and copy actions; Ram Tah-needed filtering, custom-origin lookup, external links, sharing, and open-survey actions remain |
| `FormBoxelSearch` | Search / Boxel | Implemented with activation/options, hierarchy, current systems, completion/empty rules, route/journal updates, ID64 decoding, clipboard actions, and cancellable full-area audit; Windows visually checked |
| `FormBuilder` | Guardian / Map editor | Not ported |
| `FormCodexBingo` | Exobiology / Codex | Implemented as a single-instance Raven workspace with the complete 1,070-entry hierarchy, global and 42-region commander progress, live and historical journal ledgers, Canonn Challenge import, confirmed manual overrides, discovery locations, Canonn/Bioforge/EDAstro/Spansh actions, and nearest-signal/missing-variant search handoff; Windows visually checked in Blue dark/light |
| `FormEditMap` | Guardian / Map editor | Partially implemented with native template rendering plus site/relic headings, notes, POI states, relic headings, and obelisk-group editing; raw POI add/remove, origin measurement, and template-authoring tools remain |
| `FormErrorSubmit` | Diagnostics / Report issue | Not ported |
| `FormGroundTarget` | Travel / Ground target | Implemented in Travel with typed, current, clipboard, clear, and guidance actions plus a passive Raven-themed `PlotTrackTarget` replacement; editor visually checked, overlay visual QA deferred to the final UI pass |
| `FormJourneyBegin` | Travel / Journeys | Implemented in the unified Journey workspace with current/prior-system selection, system search, exact last-visit lookup, journal replay, validation, and begin action; Windows visually checked |
| `FormJourneyEdit` | Travel / Journeys | Implemented in the unified Journey workspace with editable name, description, per-system notes, dirty/save/discard state, conclude, and guarded reprocess; Windows visually checked |
| `FormJourneyList` | Travel / Journeys | Implemented as the Journey history sidebar with active/completed state, start time, description, selection, and refresh; Windows visually checked |
| `FormJourneyViewer` | Travel / Journeys | Implemented with legacy-compatible quick statistics, active/completed byline, preferences, visited-system drilldown, screenshots, and lifecycle actions; Windows visually checked in Blue dark/light |
| `FormMyProjects` | Colonisation / Projects | Implemented with opt-in Raven loading, hidden-project selection, primary-project display, aggregate cargo planning, current-ship trip estimates, refresh/save, and Raven build link; Windows visually checked |
| `FormNearestSystems` | Search / Nearby systems | Implemented with current journal coordinates, Canonn signal and Spansh missing-variant searches, enriched notes, five unique results, selection, clipboard actions, Canonn/Spansh links, and original Spansh search link; Windows visually checked |
| `FormNewProject` | Colonisation / Projects | Implemented with live depot/docked context, shipped build catalog, planned Raven sites, location/build/layout/body/architect/notes fields, validation, explicit review/confirm publishing, refresh, and created-project link; Windows visually checked |
| `FormPlayComms` | Developer tools | Deferred |
| `FormPlayDev` | Developer tools | Deferred |
| `FormPlayJournal` | Diagnostics / Journal tools | Not ported |
| `FormPostProcess` | Diagnostics / Journal tools | Not ported |
| `FormPredictions` | Exobiology / Predictions | Implemented as a single-instance Raven workspace with system totals, confirmed/estimated/first-footfall rewards, expandable body rows, exact species/variant and genus-only states, sample separation distances, incomplete-scan guidance, current-body focus, persisted row sizing, and Canonn/Spansh/EDSM actions; Windows visually checked in Blue dark/light |
| `FormRamTah` | Guardian / Ram Tah | Implemented with both journal-driven mission states, all 101 + 28 categorized log controls, compatible commander persistence, progress, manual toggles, guarded resets, both Canonn guide links, and artifact-gated current-obelisk scan updates; detached `PlotRamTah` remains; Windows visually checked |
| `FormRavenUpdater` | Update flow | Not ported |
| `FormRoute` | Travel / Routes | Implemented with lossless legacy route files, manual-name and current Spansh imports, active/auto-copy controls, per-hop progress, distances/notes/refuel/neutron guidance, save/discard, live FSDJump progression, and a Galaxy Map overlay; Windows visually checked in Blue dark/light |
| `FormRuins` | Guardian / Survey maps | Partially implemented through the unified site browser, native map renderer, live-site card, and lossless survey editor; dedicated open/share workflows and advanced map authoring remain |
| `FormSetKeyChord` | Settings / Input | Implemented as the unified binding editor with normalized keyboard, button, trigger, and eight-way POV chords plus default restore |
| `FormSettings` | Settings pages | Raven themes, migration, next-jump/system-survey/Canonn prior-scan/radar/combat preferences, and global keyboard/controller input implemented; general overlay adjustment, privacy, and remaining legacy options remain |
| `FormShareData` | Settings / Privacy | Not ported |
| `FormShowCodex` | Exobiology / Codex | Implemented as a single-instance Raven browser with biological-body and entry navigation, reported/confirmed/analyzed/predicted states, entry IDs, rewards, sample separation, live temperature guidance, bounded cached reference images with credit/refresh/fit/zoom/pan, and Canonn/Bioforge/Spansh/submission actions; Windows visually checked in Blue dark/light |
| `FormSphereLimit` | Search / Spherical | Implemented with live Spansh lookup, matching-system selection, 1–1000 ly validation, current distance, enable/disable, and compatible commander persistence; Windows visually checked |
| `FormStartNewCmdr` | Commander onboarding | Not ported |
| `FormSwapStarCache` | Diagnostics / Reference data | Not ported |
| `FormSystemNotes` | Travel / System notes | Implemented with current journal context, lossless legacy system-file updates/creation, save/cancel, persistent always-on-top, screenshot-folder detection, exact Canonn/Spansh/EDSM actions, Travel launcher, `Ctrl+Shift+N`, and active-Journey note-count updates; Windows visually checked in Blue dark/light |
| `ViewJourneySystem` | Travel / Journeys | Implemented in the Journey workspace with recent-first visits, legacy interest flags, arrival/departure and scan/reward details, system notes, screenshot listing, folder opening, and OS file launch; Windows visually checked |
| `ViewLogs` | Diagnostics / Logs | Not ported |

## Overlay and plotter surfaces

The following 22 designers are not ordinary application pages. Each is tracked
separately against the Phase 4 overlay/window infrastructure and must be
validated independently for Windows, X11, and Wayland:

`PlotBioStatus`, `PlotBioSystem`, `PlotBodyInfo`, `PlotFlightWarning`,
`PlotFootCombat`, `PlotFSS`, `PlotFSSInfo`, `PlotGalMap`, `PlotGrounded`,
`PlotGuardians`, `PlotGuardianStatus`, `PlotGuardianSystem`, `PlotHumanSite`,
`PlotJumpInfo`, `PlotMassacre`, `PlotPriorScans`, `PlotRamTah`,
`PlotSphericalSearch`, `PlotSysStatus`, `PlotTrackers`, `PlotTrackTarget`, and
the shared `PlotBase`.

The shared passive-window infrastructure now provides monitor-aware physical
placement, focus/minimize lifecycle, Windows native click-through and Elite
client tracking, X11 XShape click-through and EWMH/Xlib tracking, and explicit
Wayland disablement. The detached Guardian window is a partial consolidation of
`PlotGuardians` and the active-obelisk slice of `PlotGuardianStatus`: it renders
the live native map, commander marker, nearest/current obelisk, artifact
requirements, scan state, and Ram Tah mission status. Site-type, heading,
origin/alignment, POI marking input, glide/approach guidance, and
`PlotGuardianSystem` remain open. The followed-route slice of
`PlotSphericalSearch` is implemented as a compact Galaxy Map overlay with route
priority, one-copy-per-entry behavior, destination state, distance, progress,
and notes/refuel/neutron guidance. Its spherical-limit and Boxel slices remain
open. `PlotJumpInfo` is implemented as a modern top-center passive overlay with
the original automatic FSD-charge/witchspace/selected-next-hop lifecycle and
`Alt+D` toggle. It preserves target precedence, nav-route/followed-route
fallback, proportional distance/progress, scoopable and boosted-hop cues,
compact mode, EDSM discovery/traffic, Spansh system/station summaries, material
traders, technology brokers, engineers, Guardian sites, route notes, and new
galactic-region notices. Provider failures degrade independently. It temporarily
obscures the Guardian status overlay as the legacy plotter did. The
`PlotFSSInfo` and `PlotSysStatus` surfaces are also implemented. A shared
journal reducer retains the current system's body classes, scan/DSS values,
discovery and mapping state, atmospheres, materials, rings, biological and
geological progress, completion counts, destination, and non-body signals. The
top-left FSS feed preserves value/signal filtering, recency, `Alt+F`, map/panel
modes, and Guardian priority. The bottom-left status overlay preserves FSS
completion, filtered DSS candidates, destination grouping, biological progress,
and optional non-body counts. The journal-backed `PlotFSS` surface is now a
top-center last-scan card with standalone-planet selection, discovery,
terraformable/landable state, distance, scan/mapped values, and biological
signals. These surfaces use bounded click-through layouts and all Raven themes.
The legacy `PlotFSS.watchFssSettings_TEST` screen-pixel tuning detector remains
open pending a platform capture abstraction. `PlotBodyInfo` is now implemented
as a top-left passive surface with map/orrery, DSS, orbit/glide, optional
surface-analysis, Sol-bubble, `Alt+B`, and Guardian-priority rules. It supports
unscanned destinations and shows discovery/mapping, scan/DSS values,
temperature, gravity, pressure, signals, volcanism, atmosphere, materials, and
rings. Its biological reward range has not yet been connected to the shared
prediction engine. The journal-backed `PlotBioSystem` surface is
now implemented as a bottom-left passive overlay. It preserves whole-system and
near-body/FSS modes, current/target selection, analyzed progress, active sample
emphasis, confirmed organism identities and rewards, regional-first
highlighting, first-footfall value, DSS guidance, geological details, Guardian
priority, and the original display preferences. Its embedded v4 evaluator
loads all 21 shipped criteria resources and resolves exact species/variant
predictions with galactic-region, parent-star/barycentre/brightness, offline
nebula, Guardian-bubble, inheritance, and known-organism context. It exposes
predicted reward ranges only for complete inputs and otherwise shows the
missing context explicitly. Commander-Codex first-discovery inference, Canonn
signal hints, and the transient map-selection timer remain open. Of the other
plotter surfaces, `PlotPriorScans` is now a
bottom-right passive guidance and radar surface. It uses the current Canonn
`getSystemPoi` response, rejects malformed/bodyless/mismatched records, caches
per system and commander with failure backoff, normalizes body identifiers,
filters low-value, commander-owned, analyzed, active-sample-nearby, and true
surface-near-duplicate targets, and continuously updates absolute/relative
bearing, distance, approach angle, active/near/distant/analyzed state, and
compact or genus-radius radar circles. Its settings retain the legacy defaults,
and it yields to the Guardian overlay. Automated coverage and XAML compilation
passed; visual/theme QA is intentionally deferred until the final UI pass.

`PlotGrounded` and `PlotTrackers` are now implemented as a consolidated
bottom-center passive surface. It atomically shares legacy per-system JSON with
system notes, preserves unknown fields, rejects malformed files without
overwriting them, and records touchdown, bookmarks, Composition Scanner
discoveries, species changes, completed three-sample scans, and complete lost
organism circles after death. The presentation preserves the original
auto-show, altitude, focus panel, vehicle mode, landing-gear and hidden-tracker
rules; renders heading-up historical, tracker, active-sample, ship and SRV
markers; exposes active navigation bearings/distances; supports all five legacy
sizes and bounded zoom/reset; handles `#1` through `#8`; falls back to a compact
tracker-only state; and yields to Guardian overlays. Automated persistence,
journal, presentation, preparation, lifecycle, XAML compilation, and full-suite
checks passed; visual/theme QA is intentionally deferred until the final UI
pass.

`PlotTrackTarget` is now implemented as a compact bottom-center passive
surface. It reuses the lossless legacy target settings and great-circle
navigation state, preserves the original supercruise/glide/ship/SRV/fighter/
on-foot/comms gating, closes for taxi, station panels, missing coordinates, or
invalid body radius, and presents distance, absolute/relative bearing, target
coordinates, approach band, and a Raven-themed bearing/descent instrument. It
participates in the global overlay visibility toggle and follows the same
foreground, scaling, click-through, and fail-closed platform contract.
Automated state, placement, passive-preparation, XAML compilation, and
full-suite checks passed; visual/theme QA is intentionally deferred until the
final UI pass.

`PlotFlightWarning` is now implemented as a top-center passive warning. It
preserves the original landable-body requirement, configurable gravity
threshold, persisted auto-show preference, and supercruise, glide, flying,
fighter, landed, and SRV mode gates. Automated state, settings, lifecycle, and
XAML compilation checks passed; visual/theme QA is intentionally deferred until
the final UI pass.

`PlotFootCombat` and `PlotMassacre` are now implemented as passive top-left and
top-right Raven surfaces using the original 8-pixel anchors. Ground combat
preserves the war/civil-war settlement match, below-100-metre, on-foot/SRV,
active-build suppression, session reset, kill, and bond rules. Massacre
tracking preserves opt-in acceptance of solo/wing massacre missions,
completion/failure/abandonment removal, active/complete mission-list
reconciliation, expiry, one bounty credit per mission giver, compatible
commander-profile persistence, and the exact flight/supercruise/navigation-
panel/station-services visibility modes. Both legacy test features remain off
by default. Automated reducer, persistence, settings, presentation,
passive-preparation, XAML compilation, and full-suite checks passed;
visual/theme QA is intentionally deferred until the final UI pass.

`PlotBioStatus` is a compact
top-center passive surface with current-body/DSS gating, genus/geology summary,
analyzed and active sample progress, stale-sample warnings, three-stage sampler,
separation distance, reward/first-footfall value, a separate persisted auto-show
preference, and jump/Guardian priority. Its last Codex notification/image action
and experimental temperature range remain open. The Windows/X11 adapters still
require live Elite runtime validation.

Global input no longer depends on SharpDX/DirectInput. SharpHook provides the
opt-in Windows/X11 keyboard hook, and SDL3 provides reconnecting gamepad,
joystick, and HOTAS input on Windows and Linux. The Settings page preserves and
edits every legacy action binding; controller chords retain the original
first-release dispatch behavior. The legacy `showSystemNotes` action now opens
or activates the current-system notes window. Wayland keyboard capture remains
disabled, while SDL controller input can operate when the Avalonia app itself
is active.

The code-rendered legacy `PlotBuildCommodities` surface is not part of the 22
designer count above. Its Avalonia replacement is implemented as the detached
construction shopping overlay with project grouping, ship and linked Fleet
Carrier quantities, pending-sync state, market availability guidance,
completion/collapse modes, and persisted display preferences. It uses the same
passive-window lifecycle as the Guardian overlay. Legacy squadron Fleet Carrier
and music-state auto-show special cases still remain open.

## Raven Colonial theme contract

The live theme menu at <https://ravencolonial.com/> exposes five named themes.
The web application bundle and rendered computed styles were checked on
2026-07-24. Avalonia should preserve these names and primary surface roles while
using platform-native focus, disabled, hover, and accessibility behavior.

| Theme | Mode | Window | Primary | Text | Raised surface | Border |
| --- | --- | --- | --- | --- | --- | --- |
| Blue (light) | Light | `#FFFFFF` | `#0078D4` | `#323130` | `#EEEEEE` | `#E5E5E5` |
| Blue (dark) | Dark | `#000012` | `#3F87D4` | `#E5E5E5` | `#00324D` | `#195494` |
| Orange (dark) | Dark | `#000000` | `#D36F00` | `#F4E1C8` | `#4D3200` | `#824500` |
| Green (light) | Light | `#F9FFF7` | `#3C8223` | `#163D08` | `#E6F2E1` | `#B7DAAA` |
| Green (dark) | Dark | `#1E3533` | `#D1D93B` | `#FFFFFF` | `#325752` | `#83A377` |

The cross-platform shell should default to Blue (dark), expose all five choices,
and persist the selection in the cross-platform application settings directory.
This does not yet replace the legacy custom `theme.json` overlay importer.

Implementation status: all five definitions are present in
`RavenThemeCatalog`, application resources switch at runtime, Avalonia's native
light/dark control mode follows the selected definition, and the selection is
stored via a temporary file in `cross-platform-ui.json`. Catalog, switching, fallback,
corrupt-settings, and persistence behavior have automated tests.

## Windows visual evidence

Checked on 2026-07-24 at 1180 by 760 logical pixels, with the later Boxel audit
check at 1182 by 790, using a live journal folder:

- Overview rendered real commander, game, mode, system, body, and session state.
- The updated Overview rendered live Exploration and Exobiology summary cards.
- Exobiology rendered the unclaimed-value, current-body, active sampler,
  three-stage progress, profile status, and compatibility cards; UI Automation
  exposed Refresh and Clear unclaimed.
- Travel rendered the surface-navigation metrics, coordinate editor, live
  position, system-notes launcher, Commander Journeys card, and explicit pending
  route state. UI
  Automation exposed both coordinate fields and Set, current-location, Paste,
  and Clear actions with correct disabled state. The resizable system-notes
  window rendered the live Facece system/address, empty-note state, topmost
  control, three external links, and cancel/save actions in Blue (dark) and Blue
  (light); it was cancelled without writing live profile data.
- The single Journey workspace was exercised with an isolated QA journal and
  commander in Blue (dark) and Blue (light). Both start modes, last-visit
  lookup, begin, overview statistics, history selection, visited-system detail,
  note edit/discard, refresh, and conclude/reprocess confirmations rendered and
  behaved correctly. The QA records were removed and the original theme was
  restored afterward; active in-game updates and Linux remain untested.
- The followed-route card and 1100 by 760 workspace were exercised with an
  isolated QA commander in Blue (dark) and Blue (light). Hop progress updated
  the next system and dirty state, discard restored the persisted route, and
  distance plus neutron/refuel guidance rendered correctly. The compact Galaxy
  Map route overlay was also visually checked in both modes through a temporary
  preview hook. The hook and exact QA files were removed and Blue (dark) was
  restored afterward; live Elite window tracking and Linux remain untested.
- The next-jump overlay was exercised through a temporary preview hook with live
  EDSM and Spansh data for Sol and a two-hop 64.9 ly route. Blue (dark) and Blue
  (light) both rendered the target/star class, proportional current-leg line,
  scoopable markers, boost emphasis, body/station totals, traffic, and engineer
  details cleanly. The hook was removed and Blue (dark) restored afterward;
  live Elite attachment, click-through, and Linux remain untested.
- The `PlotFSSInfo` and `PlotSysStatus` replacements were exercised together
  with a synthetic six-body system in Blue (dark) and Blue (light). The body
  feed rendered discovery, class, scan/DSS value, terraformable/landable,
  biology, and geology states at actual Windows DPI; the compact status overlay
  rendered two DSS candidates, destination emphasis, three remaining biological
  signals, and four non-body signals. The temporary hook and QA settings were
  removed; live Elite attachment, click-through, and Linux remain untested.
- The journal-backed `PlotFSS` replacement was exercised separately with a
  long generated system/body name in Blue (dark) and Blue (light). Discovery,
  class, 12,845 LS distance, terraformable/landable markers, scan and mapped
  values, and four biological signals rendered cleanly at the active Windows
  scaling. The temporary preview hook and QA settings were removed; live Elite
  attachment, click-through, the experimental pixel watcher, and Linux remain
  untested.
- The `PlotBodyInfo` replacement was exercised with a dense synthetic body in
  Blue (dark) and Blue (light). A long generated name, discovery and
  terraformable state, scan/DSS values, 12,845 LS distance, high gravity,
  pressure, four biological and two geological signals, volcanism, three
  atmosphere components, eight materials including rare-material emphasis, and
  two rings fit in a 390 by 521 logical-pixel passive card at the active Windows
  scaling. The temporary preview hook and QA settings were removed; live Elite
  attachment, click-through, biological reward prediction, and Linux remain
  untested.
- The `PlotBioSystem` replacement was exercised with a dense four-signal body,
  a three-body whole-system view, and an exact Aleoida species/variant
  prediction in Blue (dark) and Blue (light). Active, analyzed, regional-first,
  genus-only, unidentified, and predicted organism states, confirmed/partial/
  predicted rewards, first-footfall value, geological names/counts,
  target/local badges, and progress bars rendered cleanly in a 390-pixel-wide
  passive card at the active Windows scaling. The temporary preview hook and QA
  settings were removed; live Elite attachment, click-through, and Linux remain
  untested.
- The standalone predictions workspace was exercised with exact and genus-only
  organisms across three bodies in Blue (dark) and Blue (light). Its system,
  confirmed, estimated, and first-footfall totals, sample separation, incomplete
  scan notice, current/first-footfall badges, full-width expandable body cards,
  large row size, focus-current-body, expand-all, and collapse-all states
  rendered cleanly at 1040 by 760 logical pixels (1042 by 790 including the
  Windows frame). The temporary preview hook and isolated settings were removed;
  live Elite updates and Linux remain untested.
- The compact `PlotBioStatus` replacement was exercised at its legacy
  480-pixel width in Blue (dark) and Blue (light). Its active two-sample state
  rendered three-stage progress, exact species, first-footfall reward, current
  and remaining separation distance, and the required-distance bar. The compact
  no-active-sample view rendered genus ranges plus analyzed and unidentified
  geology, while the DSS-required state stayed readable without duplicated
  guidance. The temporary preview hook and isolated settings were removed;
  live Elite attachment, click-through, and Linux remain untested.
- The single-instance `FormShowCodex` replacement was exercised with an isolated
  biological body and exact Aleoida variant prediction in Blue (dark) and Blue
  (light). Its status, entry metadata, reward, sample separation, live
  temperature comparison, research actions, and real cached Canonn reference
  image/credit rendered cleanly. Fit, wheel zoom, and drag pan were exercised;
  the isolated profile/cache entry was removed and Blue (dark) restored. Live
  Elite updates, remote image-failure presentation, and Linux remain untested.
- The single-instance `FormCodexBingo` replacement was exercised with an
  isolated three-entry Sol ledger in Blue (dark) and Blue (light). Its
  1,070-entry hierarchy, aggregate progress, commander/region selectors, tree
  expansion, selected-node detail, retained state, and disabled actions
  rendered correctly. The exact preview ledgers were removed and Blue (dark)
  restored; interactive remote imports, live Elite updates, and Linux remain
  untested.
- Search rendered the spherical limit, live current-system coordinates,
  configuration editor, Boxel status/options/hierarchy, current-boxel actions,
  full-area audit controls, and the nearby-biology workspace. A live
  Spansh lookup for Sol returned five matches, selected the exact system,
  calculated 131.09 ly from Facece, and enabled the save action without changing
  the live commander profile. UI Automation exposed the spherical and Boxel
  controls, including the inactive audit/cancel states; no Boxel action or audit
  was invoked. Both nearby modes, their conditional inputs, current Facece
  reference coordinates, selected-result actions, and disabled empty states
  were visually checked in Blue (dark) without issuing a network query.
- Colonisation rendered external-data consent, active project selection, live
  construction resources, project creation/review, Fleet Carrier credential and
  sync controls, and shopping-overlay preferences. The detached shopping
  overlay was visually checked with grouped commodities, market badges, ship/FC
  columns, collapse options, and pending-state presentation; no external publish
  was made during the visual checks.
- Guardian rendered all 759 shipped sites ordered from the live Facece position,
  filtered immediately to the unique `GR 1` system address, and exposed selected
  site/commander details plus system, address, galactic-position, and lat/long
  copy controls. List scrolling and full coordinate layout were visually checked
  at 1182 by 790; no refresh, clipboard, survey, or profile-changing action was
  invoked. UI Automation data was unavailable for this check.
- The Guardian Ram Tah tab rendered both mission cards and all 129 log controls
  in the original ten category groupings, with mission status, progress bars,
  disabled empty reset actions, guide actions, and the profile status footer.
  The full page was scrolled at 1182 by 790 without invoking any action that
  changes commander data. UI Automation data was unavailable for this check.
- The Guardian live scanner card rendered current-site/proximity, active-obelisk,
  artifact, Ram Tah, and guarded scan-action states in Blue (dark) and Blue
  (light) at 1182 by 790. The inactive live profile exposed the disabled scan
  action through UI Automation; no commander survey or checklist was changed.
- Diagnostics rendered the selected journal folder, parsed state, candidate
  paths, refresh action, and update time.
- Settings rendered all five palette previews. Switching from Blue (dark) to
  Blue (light) updated the complete window and saved the choice.
- Settings rendered the opt-in keyboard card, all 30 editable binding rows, and
  the SDL controller picker/status card. The Windows keyboard hook reached its
  active state during a controlled check and was disabled afterward. SDL device
  discovery initialized without hardware and reported the empty state cleanly.
- A pending navigation item rendered the explicit incomplete-feature message.
- Windows UI Automation exposed all nine navigation destinations, five theme
  buttons, Refresh actions, and the visible page text.

Not yet checked: active in-game sample transitions, minimum-width and
high-contrast rendering, Linux X11/Wayland, or live overlays attached to an
Elite client window.

## UI completion gates

For each migrated surface:

1. Map every user action and state from the WinForms source.
2. Connect it to ported behavior; label unavailable actions explicitly.
3. Verify keyboard navigation, focus visibility, scaling, and narrow-window
   behavior.
4. Exercise it in every Raven theme and at least one high-contrast OS mode.
5. Add view-model or headless UI tests where practical.
6. Record Windows and Linux runtime evidence in `PORTING_PLAN.md`.
