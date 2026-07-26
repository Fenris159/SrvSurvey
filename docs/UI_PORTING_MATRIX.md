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
| Exobiology | `Main` bio group, `FormPredictions`, Codex forms | Scan progress, rewards and predictions | `Main` active-sample, separation, reward, sale/death, and reset workflow, exact `PlotBioSystem` species/variant predictions, standalone system/body predictions, `FormShowCodex`, full commander/region `FormCodexBingo`, `PlotPriorScans`, consolidated `PlotGrounded`/`PlotTrackers`, and the compact `PlotMiniTrack` replacement are implemented; human-settlement priority is wired into the shared coordinator |
| Travel | `Main` Travel menu, journey/route forms | Ground target, system notes, journeys and routes | Ground-target editor, clipboard/current actions, persistence, live guidance and passive overlay, system notes, Commander Journeys, followed-route workspace, imports, journal progression, and Galaxy Map guidance implemented |
| Search | `Main` Search menu, sphere/boxel/nearest forms | Spatial, boxel, and biological searches | Spherical center lookup, radius, enable/disable, live distance, and compatible persistence implemented; Boxel activation, hierarchy, source merging, ID64 decoding, completion, navigation, clipboard, and full-area audit implemented; their combined `PlotSphericalSearch` Galaxy Map guidance is implemented; nearest Canonn-signal and Spansh missing-variant searches plus result actions implemented |
| Guardian | `Main` Guardian menu and survey forms | Sites, maps, beacons and Ram Tah | Reference/commander catalog, visits, exact completion, filters, custom distance origins, external/share actions, live site detection/writes, native survey maps, lossless survey/raw-POI editing, guarded master-template authoring/export, current-obelisk proximity/artifacts/scan actions, both Ram Tah missions, detached commander-centered/heading-up live map with all five sizes, legacy zoom behavior, site identification/heading prompts, ruins/structure alignment reticles, altitude fade, glide site/blueprint guidance, obelisk targeting, and the original safe survey commands, current-system summary, and Ram Tah log/artifact overlays implemented |
| Colonisation | `Main` Colonise menu, project forms, and `PlotBuildCommodities` | Raven projects and construction state | Opt-in Raven project loading/selection and creation, live depot progress, cargo planning, Market guidance, linked Fleet Carrier cargo/sync, and a passive shopping overlay with the legacy Market-after-docking, construction-site, right-panel, and Squadron-bank music rules implemented |
| Diagnostics | `ViewLogs`, `FormErrorSubmit`, journal development tools | Journal source, candidate paths, logs, error reporting, and reference-data tools | Journal source/parsed state, persistent live application logs, retention, copy/clear/folder actions, crash-report capture/actions, journal playback/inspection, safe visited-stars cache swap/restore, and guarded post-processing tools implemented |
| Settings | `FormSettings`, `FormSetKeyChord`, `FormAdjustOverlay` | Themes, paths, overlays, input and privacy | Raven themes, checksum-verified legacy profile import with live-monitor shutdown, concurrent-write rollback, and immediate non-destructive preference translation, unknown-field-preserving screenshot/notification/Guardian setting saves, lossless live overlay position/opacity editing, cross-platform verified BMP-to-PNG screenshot conversion, persisted next-jump/system-survey/prior-scan/surface-radar/combat/Guardian/human-settlement/station-information/notification preferences, migrated external-biology/EDDN/Green-Gas-Giant privacy choices, all 30 editable keyboard/controller bindings, opt-in SharpHook keyboard capture, and SDL controller discovery/polling implemented; remaining legacy options remain |

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
quick-location chords, and a compact tracker-only state. The dedicated
`PlotMiniTrack` replacement and human-site arbitration are now wired into that
same coordinator; remaining biology-specific auxiliary cues stay open.

## Secondary forms

| Legacy surface | Avalonia destination | Status |
| --- | --- | --- |
| `FormAdjustOverlay` | Settings / Overlay positions | Implemented for all 22 Avalonia overlays with live anchors, offsets, opacity, legacy defaults, verified backups, and VR-suffix preservation |
| `FormBeacons` | Guardian / Sites | Implemented with all shipped beacons/ruins/structures, commander visits/survey progress/notes, current or looked-up custom distance origins, text/kind/visit/type filters, optional all/needed-only Ram Tah catalog logs, details, copy actions, Canonn/Spansh/EDSM links, direct survey navigation, and the guarded share-bundle workspace |
| `FormBoxelSearch` | Search / Boxel | Implemented with activation/options, hierarchy, current systems, completion/empty rules, route/journal updates, ID64 decoding, clipboard actions, and cancellable full-area audit; Windows visually checked |
| `FormBuilder` | Human settlements / Template authoring | Implemented as a collapsed developer workspace with live commander-offset capture, manual and shield-toggle polygon points, renderer-compatible circles, multi-path buildings, named points, terminals, secure doors with relative rotation, floor/security metadata, draft preview/undo/discard, and explicit checksum-verified atomic catalog export with concurrent-write refusal and byte-identical backup; final visual testing remains deferred |
| `FormCodexBingo` | Exobiology / Codex | Implemented as a single-instance Raven workspace with the complete 1,070-entry hierarchy, global and 42-region commander progress, live and historical journal ledgers, Canonn Challenge import, confirmed manual overrides, discovery locations, Canonn/Bioforge/EDAstro/Spansh actions, and nearest-signal/missing-variant search handoff; Windows visually checked in Blue dark/light |
| `FormEditMap` | Guardian / Map editor | Implemented with native rendering; site/relic headings, notes, POI states, relic headings and site obelisk groups; live commander-origin distance/angle/rotation measurement; duplicate-guarded local raw-POI add/remove; and isolated master-template metadata/POI/destructible-panel/group-label editing with live draft preview and explicit checksum-verified, backup-protected catalog export |
| `FormErrorSubmit` | Diagnostics / Report issue | Implemented as a Raven error window with UI-thread and unobserved-task interception, exception/stack details, a frozen last-20-log snapshot, current-journal actions, prepared GitHub crash-template submission, issue tracker and Discord actions, and failure-safe reporting |
| `FormGroundTarget` | Travel / Ground target | Implemented in Travel with typed, current, clipboard, clear, and guidance actions plus a passive Raven-themed `PlotTrackTarget` replacement; editor visually checked, overlay visual QA deferred to the final UI pass |
| `FormJourneyBegin` | Travel / Journeys | Implemented in the unified Journey workspace with current/prior-system selection, system search, exact last-visit lookup, journal replay, validation, and begin action; Windows visually checked |
| `FormJourneyEdit` | Travel / Journeys | Implemented in the unified Journey workspace with editable name, description, per-system notes, dirty/save/discard state, conclude, and guarded reprocess; Windows visually checked |
| `FormJourneyList` | Travel / Journeys | Implemented as the Journey history sidebar with active/completed state, start time, description, selection, and refresh; Windows visually checked |
| `FormJourneyViewer` | Travel / Journeys | Implemented with legacy-compatible quick statistics, active/completed byline, preferences, visited-system drilldown, screenshots, and lifecycle actions; Windows visually checked in Blue dark/light |
| `FormMyProjects` | Colonisation / Projects | Implemented with opt-in Raven loading, hidden-project selection, primary-project display, aggregate cargo planning, current-ship trip estimates, refresh/save, and Raven build link; Windows visually checked |
| `FormNearestSystems` | Search / Nearby systems | Implemented with current journal coordinates, Canonn signal and Spansh missing-variant searches, enriched notes, five unique results, selection, clipboard actions, Canonn/Spansh links, and original Spansh search link; Windows visually checked |
| `FormNewProject` | Colonisation / Projects | Implemented with live depot/docked context, shipped build catalog, planned Raven sites, location/build/layout/body/architect/notes fields, validation, explicit review/confirm publishing, refresh, and created-project link; Windows visually checked |
| `FormPlayComms` / `FormPlayComms2` | Quest communications | Implemented as a Raven-themed messages/my-quests/catalog/history workspace over the lossless migration, Raven contracts, cancellable Lua runtime, source-safe context, and live desktop composition; visual QA is held for the final pass |
| `PlotQuestMini` | Quest indicator | Implemented with visible objectives, unread-message emphasis, tracked target distance/relative bearing/completion, legacy placement/opacity, foreground-game gating, and click-through safety; visual QA is held for the final pass |
| `FormPlayDev` | Quest Developer tab | Implemented with hash-verified legacy folder import, non-mutating source reads, all-chapter Lua validation, same-identity progress preservation, clean cross-identity replacement with verified state backup, embedded portable definitions, watched reloads, objective/message/existing-variable JSON editing, chapter start/stop, Lua debug execution, disk refresh, guarded removal, and explicit overwrite confirmation for Raven publishing; final visual testing remains deferred |
| `FormPlayJournal` | Diagnostics / Journal inspector | Implemented with the newest 120 live/raw events, bounded structured property browsing, live Status.json details, coordinate copying, runtime-compatible nested Lua handler generation, clipboard copy, and explicitly confirmed replay through the active quest runtime and normal persistence path |
| `FormPostProcess` | Diagnostics / Journal tools | Implemented with commander/date-filtered and cancellable historical statistics, recent-active-journal safety, modern/legacy filename support, Trailblazers comparison, an explicitly confirmed atomic Commander Codex merge, byte-preserving species/atmosphere aggregation, and explicitly confirmed Odyssey system/body reconstruction with lossless unknown-field merging, serialized live writes, checksum-verified backups, atomic activation, and tested rollback; historical network publication remains intentionally separate from data reconstruction |
| `FormPredictions` | Exobiology / Predictions | Implemented as a single-instance Raven workspace with system totals, confirmed/estimated/first-footfall rewards, expandable body rows, exact species/variant and genus-only states, sample separation distances, incomplete-scan guidance, current-body focus, persisted row sizing, and Canonn/Spansh/EDSM actions; Windows visually checked in Blue dark/light |
| `FormRamTah` | Guardian / Ram Tah | Implemented with both journal-driven mission states, all 101 + 28 categorized log controls, compatible commander persistence, progress, manual toggles, guarded resets, both Canonn guide links, artifact-gated current-obelisk scan updates, and detached `PlotRamTah` log/artifact guidance; workspace visually checked, overlay visual QA deferred to the final pass |
| `FormRavenUpdater` | Colonisation / Raven system sites | Implemented with architect/open permission enforcement, explicit body-import confirmation, live scan/signal/status/approach/docking inference, manual site editing, fresh three-way reconciliation, concurrent-field conflict blocking, extension/remote-only preservation, stable-delete rules, and a separate confirmed publish; final visual testing remains deferred |
| `FormRoute` | Travel / Routes | Implemented with lossless legacy route files, manual-name and current Spansh imports, active/auto-copy controls, per-hop progress, distances/notes/refuel/neutron guidance, save/discard, live FSDJump progression, and a Galaxy Map overlay; Windows visually checked in Blue dark/light |
| `FormRuins` | Guardian / Survey maps | Implemented through the unified site browser with direct selected-survey navigation, native map renderer and live draft preview, live-site card, lossless survey/raw-POI editor, master-template authoring, and guarded share-bundle workflow |
| `FormSetKeyChord` | Settings / Input | Implemented as the unified binding editor with normalized keyboard, button, trigger, and eight-way POV chords plus default restore |
| `FormSettings` | Settings pages | Raven themes, checksum-backed profile import with immediate guarded preference translation, next-jump/system-survey/Canonn prior-scan/radar/combat/Guardian/notification preferences, migrated external-biology/EDDN/Green-Gas-Giant privacy choices, opt-in live-only Green Gas Giant candidate publication, live overlay adjustment, verified screenshot conversion/banner/source-deletion controls, and global keyboard/controller input implemented; malformed legacy settings are retained byte-for-byte and leave current Avalonia settings unchanged; remaining legacy options remain |
| `FormShareData` | Guardian / Share data | Implemented with published-survey comparison, new heading/location/POI/relic/group/raw-point detection, content-addressed ZIP packaging, path copy, folder launch, and Guardian survey Discord handoff; packaging avoids the legacy destructive staging-folder reset |
| `FormShowCodex` | Exobiology / Codex | Implemented as a single-instance Raven browser with biological-body and entry navigation, reported/confirmed/analyzed/predicted states, entry IDs, rewards, sample separation, live temperature guidance, bounded cached reference images with credit/refresh/fit/zoom/pan, and Canonn/Bioforge/Spansh/submission actions; Windows visually checked in Blue dark/light |
| `FormSphereLimit` | Search / Spherical | Implemented with live Spansh lookup, matching-system selection, 1–1000 ly validation, current distance, enable/disable, and compatible commander persistence; Windows visually checked |
| `FormStartNewCmdr` | Multiple commanders/windows | Implemented in the Overview commander card with live/legacy profile discovery, malformed-file isolation, current-identity exclusion, argument-safe additional-process launch, legacy `-fid` and new `--frontier-id` journal isolation, and Windows/X11 next-Elite-window focus from the card or `Alt+Ctrl+W` |
| `FormSwapStarCache` | Diagnostics / Reference data | Implemented in Diagnostics with commander/current-system context, Windows target detection, portable manual file selection, live game-process lockout, two-step swap/restore confirmation, EDGalaxy response validation and size bounds, persistent checksum-backed original backup, same-directory staged activation, transaction rollback, and verified reusable restore; visual QA is held for the final pass |
| `FormSystemNotes` | Travel / System notes | Implemented with current journal context, lossless legacy system-file updates/creation, save/cancel, persistent always-on-top, screenshot-folder detection, exact Canonn/Spansh/EDSM actions, Travel launcher, `Ctrl+Shift+N`, and active-Journey note-count updates; Windows visually checked in Blue dark/light |
| `ViewJourneySystem` | Travel / Journeys | Implemented in the Journey workspace with recent-first visits, legacy interest flags, arrival/departure and scan/reward details, system notes, screenshot listing, folder opening, and OS file launch; Windows visually checked |
| `ViewLogs` | Diagnostics / Logs | Implemented in Diagnostics with timestamped session files, live framework/startup entries, in-memory fallback, newest-ten retention, copy, legacy-compatible clear, and cross-platform folder launch |

## Overlay and plotter surfaces

The following 22 designers are not ordinary application pages. Each is tracked
separately against the Phase 4 overlay/window infrastructure and must be
validated independently for Windows, X11, and Wayland:

`PlotBioStatus`, `PlotBioSystem`, `PlotBodyInfo`, `PlotFlightWarning`,
`PlotFloatie`, `PlotFootCombat`, `PlotFSS`, `PlotFSSInfo`, `PlotGalMap`, `PlotGrounded`,
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
requirements, scan state, and Ram Tah mission status. The map is now
commander-centered and heading-up, preserves all five original plotter sizes,
uses the original ruins/structure, on-foot, nearby-obelisk, and SRV-turret
automatic zoom levels, accepts the shared manual/reset zoom actions, and
migrates the related WinForms preferences. Site-type and site-heading capture,
notes, tower headings, empty points, scan toggles, obelisk targets, raw-point
add/remove, direct zoom, and map/origin mode selection are journal-command
compatible; historical bootstrap commands are ignored, and command writes use
the same atomic, unknown-field-preserving survey store as the editor. The
previously reversed live raw-point angle is corrected. The same surface now
provides the heading-mode ruins measurement guide, origin-mode Alpha/Beta/Gamma
and structure-specific alignment reticles, migrated target altitudes and guide
visibility, the original altitude fade, and glide site/type/blueprint guidance.
Its surface, panel, FSD, and Raven build-project lifecycle gates are restored.
Automated state, settings-preservation, and XAML checks pass; visual/theme and
live-game QA are intentionally deferred to the final UI pass.
`PlotGuardianSystem` is implemented as a left/top passive summary with exact
supercruise/navigation/system-map gates, current-system sites, destination
marking, survey state, persisted preferences, and forced FSS/body priority.
`PlotRamTah` is implemented as a right-middle passive guide with exact active
mission/site pairing, surface-position and vehicle/panel gates, incomplete-log
grouping, obelisk names, artifact inventory counts, and persisted preferences.
Both auxiliary windows participate in the shared click-through lifecycle and
global visibility control. Their automated state, placement, settings, and XAML
checks pass; visual/theme QA is intentionally deferred to the final UI pass.
`PlotFloatie` is implemented as a bottom-center passive notification surface.
It restores cargo-depot remainder, material pickup totals, Boxel progress/next-
target, screenshot save, Green Gas Giant upload, and banner-toggle messages,
including de-duplication, the original six-second lifetime, all five nested
notification preferences, and global enablement. Bootstrap journal history
hydrates material totals without replaying messages. Its settings preserve
unknown imported fields. Automated reducer, migration, persistence, placement,
and XAML checks pass; visual/theme QA is held for the final pass.
`PlotHumanSite` is implemented as a left-middle passive vector map backed by all
28 shipped settlement templates. It preserves compatible-site filtering,
landing-pad subtype and heading inference, explicit `.settlement` foot
alignment, approach and docking state, commander-relative navigation, automatic
and manual zoom, large-map mode, the 500 m ship-call boundary, ship/SRV/former-
ship markers, the 2 km dismissal boundary and warning, secure doors, named
points, terminals, conflict-zone points, processed-terminal state, and optional
material pickup dots. Active quest target circles and widened waypoint routes
are projected onto the aligned map from the losslessly migrated quest state;
malformed imported coordinates are retained on disk but fail closed in the
renderer. Geometry remains lossless in the legacy system JSON, and
material surveys use the legacy `footMatStats/<FID>` layout with corrupt-file
protection and `.stop` completion. The coordinator preserves station-info and
surface/biology overlay priority plus the global map and visibility actions.
Template authoring for both human settlements (`FormBuilder`) and Guardian maps
(`FormEditMap`) is implemented. Threat-level survey metadata and the `.threat` command are
implemented independently of material tracking. Automated reducer,
persistence, settings, placement, control-transform, integration, and XAML
checks pass; per request, the window has not been opened and awaits the final
visual/theme pass.
`PlotSphericalSearch` is implemented as a
combined top-right Galaxy Map overlay at the original 8-pixel anchor. It stacks
every enabled legacy slice: spherical-limit center/final-route destination/
distance and inside/outside evaluation; Boxel prefix, visited progress, next
system, outside/low-mass/already-surveyed destination validation and clipboard
state; and followed-route priority, one-copy-per-entry behavior, destination
state, distance, progress, and notes/refuel/neutron guidance. The earlier
route-only top-left window was removed. Automated state, precedence,
passive-preparation, XAML compilation, and full-suite checks passed; the
corrected combined layout intentionally awaits the final visual/theme pass.
`PlotJumpInfo` is implemented as a modern top-center passive overlay with
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
The legacy `PlotFSS.watchFssSettings_TEST` screen-pixel tuning detector is now
ported with its exact scan/skip state machine, thresholds, status cues, nested
settings migration, and optional diagnostic captures. Bounded native capture
supports Windows and X11, while Wayland exposes an explicit unavailable status.
`PlotBodyInfo` is now implemented as a top-left passive surface with map/orrery, DSS, orbit/glide, optional
surface-analysis, Sol-bubble, `Alt+B`, and Guardian-priority rules. It supports
unscanned destinations and shows discovery/mapping, scan/DSS values,
temperature, gravity, pressure, signals, volcanism, atmosphere, materials, and
rings. Its biological reward range uses the same exact prediction engine and
incomplete-input rules as the biology surfaces. The journal-backed `PlotBioSystem` surface is
now implemented as a bottom-left passive overlay. It preserves whole-system and
near-body/FSS modes, current/target selection, the two-seconds-per-organism
transient System Map selection and countdown, analyzed progress, active sample
emphasis, confirmed organism identities and rewards, regional-first
highlighting, first-footfall value, DSS guidance, geological details, Guardian
priority, and the original display preferences. Its embedded v4 evaluator
loads all 21 shipped criteria resources and resolves exact species/variant
predictions with galactic-region, parent-star/barycentre/brightness, offline
nebula, Guardian-bubble, inheritance, and known-organism context. It exposes
predicted reward ranges only for complete inputs and otherwise shows the
missing context explicitly. Its confirmed and predicted rows now infer
commander and regional firsts from the active global/current-region ledgers;
the original nonlocal-body Canonn signal hint shares a bounded request cache
with the prior-scan radar. Of the other
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
preference, jump/Guardian priority, the last Composition Scanner Codex entry,
image availability, and the original live `.show` handoff to the selected Codex
detail window. Its `_TEST` temperature display is now a migrated opt-in
diagnostic using the exact organism range, body baseline, and live suit
temperature. The Windows/X11 adapters still require live Elite runtime
validation, and the new diagnostic awaits the final visual pass.

Global input no longer depends on SharpDX/DirectInput. SharpHook provides the
opt-in Windows/X11 keyboard hook, and SDL3 provides reconnecting gamepad,
joystick, and HOTAS input on Windows and Linux. The Settings page preserves and
edits every legacy action binding; controller chords retain the original
first-release dispatch behavior. The legacy Galaxy Map copy/paste actions now
use route, in-boxel, and clipboard precedence with SharpHook text simulation,
`toggleFF` corrects and persists current-body organic rewards, and
`showSystemNotes` opens or activates the current-system notes window. The
stream-composition and two VR adjustment actions still need new-platform
destinations. Wayland keyboard capture remains disabled, while SDL controller
input can operate when the Avalonia app itself is active.

The code-rendered legacy `PlotBuildCommodities` surface is not part of the 22
designer count above. Its Avalonia replacement is implemented as the detached
construction shopping overlay with project grouping, ship and linked Fleet
Carrier quantities, pending-sync state, market availability guidance,
completion/collapse modes, and persisted display preferences. It uses the same
passive-window lifecycle as the Guardian overlay. Its auto-show lifecycle now
matches the legacy Market-after-docking, tracked/untracked construction-site,
right-panel, manual-force, and Squadron Fleet Carrier bank music cases.

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
  attachment, click-through, the newly ported pixel watcher, and Linux remain
  untested, and the watcher is held for the final requested visual pass.
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
