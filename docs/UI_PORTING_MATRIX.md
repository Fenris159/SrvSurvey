# Avalonia UI porting matrix

Last audited: 2026-07-24

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
| Exobiology | `Main` bio group, `FormPredictions`, Codex forms | Scan progress, rewards and predictions | `Main` active-sample, separation, reward, sale/death, and reset workflow implemented; predictions and Codex forms remain pending |
| Travel | `Main` Travel menu, journey/route forms | Ground target, system notes, journeys and routes | Ground-target editor, clipboard/current actions, persistence, live guidance, system notes, Commander Journeys, followed-route workspace, imports, journal progression, and Galaxy Map guidance implemented |
| Search | `Main` Search menu, sphere/boxel/nearest forms | Spatial, boxel, and biological searches | Spherical center lookup, radius, enable/disable, live distance, and compatible persistence implemented; Boxel activation, hierarchy, source merging, ID64 decoding, completion, navigation, clipboard, and full-area audit implemented; nearest Canonn-signal and Spansh missing-variant searches plus result actions implemented |
| Guardian | `Main` Guardian menu and survey forms | Sites, maps, beacons and Ram Tah | Reference/commander catalog, visits, exact completion, filters, distance ordering, details, clipboard actions, live site detection/writes, native survey maps, survey editing, current-obelisk proximity/artifacts/scan actions, both Ram Tah missions, and a detached live map/current-obelisk overlay implemented; advanced map-authoring and remaining plotter modes remain |
| Colonisation | `Main` Colonise menu, project forms, and `PlotBuildCommodities` | Raven projects and construction state | Opt-in Raven project loading/selection and creation, live depot progress, cargo planning, Market guidance, linked Fleet Carrier cargo/sync, and a passive shopping overlay implemented; special squadron-FC/music auto-show rules remain |
| Diagnostics | `ViewLogs`, journal development tools | Journal source, candidate paths and logs | Journal source and parsed state implemented; full logs not ported |
| Settings | `FormSettings`, `FormSetKeyChord`, `FormAdjustOverlay` | Themes, paths, overlays, input and privacy | Raven themes, checksum-verified legacy profile import, all 30 editable keyboard/controller bindings, opt-in SharpHook keyboard capture, and SDL controller discovery/polling implemented; overlay adjustment and privacy settings remain |

Unavailable areas may appear in the shell to preserve discoverability, but they
must be labelled as pending and must not imply working behavior.

The implemented Exobiology page covers the original `Main` dashboard workflow,
not the separate prediction, Codex browser, prior-scan, or overlay surfaces.
Those rows remain open below and keep their pending labels until their backing
system/body and platform behavior is ported.

## Secondary forms

| Legacy surface | Avalonia destination | Status |
| --- | --- | --- |
| `FormAdjustOverlay` | Settings / Overlays | Not ported |
| `FormBeacons` | Guardian / Sites | Partially implemented with all shipped beacons/ruins/structures, commander visits/survey progress/notes, distance and text/kind/visit/type filters, details, and copy actions; Ram Tah-needed filtering, custom-origin lookup, external links, sharing, and open-survey actions remain |
| `FormBoxelSearch` | Search / Boxel | Implemented with activation/options, hierarchy, current systems, completion/empty rules, route/journal updates, ID64 decoding, clipboard actions, and cancellable full-area audit; Windows visually checked |
| `FormBuilder` | Guardian / Map editor | Not ported |
| `FormCodexBingo` | Exobiology / Codex | Not ported |
| `FormEditMap` | Guardian / Map editor | Partially implemented with native template rendering plus site/relic headings, notes, POI states, relic headings, and obelisk-group editing; raw POI add/remove, origin measurement, and template-authoring tools remain |
| `FormErrorSubmit` | Diagnostics / Report issue | Not ported |
| `FormGroundTarget` | Travel / Ground target | Implemented in Travel with typed, current, clipboard, clear, and guidance actions; Windows visually checked |
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
| `FormPredictions` | Exobiology / Predictions | Not ported |
| `FormRamTah` | Guardian / Ram Tah | Implemented with both journal-driven mission states, all 101 + 28 categorized log controls, compatible commander persistence, progress, manual toggles, guarded resets, both Canonn guide links, and artifact-gated current-obelisk scan updates; detached `PlotRamTah` remains; Windows visually checked |
| `FormRavenUpdater` | Update flow | Not ported |
| `FormRoute` | Travel / Routes | Implemented with lossless legacy route files, manual-name and current Spansh imports, active/auto-copy controls, per-hop progress, distances/notes/refuel/neutron guidance, save/discard, live FSDJump progression, and a Galaxy Map overlay; Windows visually checked in Blue dark/light |
| `FormRuins` | Guardian / Survey maps | Partially implemented through the unified site browser, native map renderer, live-site card, and lossless survey editor; dedicated open/share workflows and advanced map authoring remain |
| `FormSetKeyChord` | Settings / Input | Implemented as the unified binding editor with normalized keyboard, button, trigger, and eight-way POV chords plus default restore |
| `FormSettings` | Settings pages | Raven themes, migration, and global keyboard/controller input implemented; overlay, privacy, and remaining legacy options remain |
| `FormShareData` | Settings / Privacy | Not ported |
| `FormShowCodex` | Exobiology / Codex | Not ported |
| `FormSphereLimit` | Search / Spherical | Implemented with live Spansh lookup, matching-system selection, 1–1000 ly validation, current distance, enable/disable, and compatible commander persistence; Windows visually checked |
| `FormStartNewCmdr` | Commander onboarding | Not ported |
| `FormSwapStarCache` | Diagnostics / Reference data | Not ported |
| `FormSystemNotes` | Travel / System notes | Implemented with current journal context, lossless legacy system-file updates/creation, save/cancel, persistent always-on-top, screenshot-folder detection, exact Canonn/Spansh/EDSM actions, Travel launcher, `Ctrl+Shift+N`, and active-Journey note-count updates; Windows visually checked in Blue dark/light |
| `ViewJourneySystem` | Travel / Journeys | Implemented in the Journey workspace with recent-first visits, legacy interest flags, arrival/departure and scan/reward details, system notes, screenshot listing, folder opening, and OS file launch; Windows visually checked |
| `ViewLogs` | Diagnostics / Logs | Not ported |

## Overlay and plotter surfaces

The following 22 designers are not ordinary application pages. They depend on
the overlay/window-tracking work in Phase 4 and must be validated separately for
Windows, X11, and Wayland:

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
open, as does the system-rich `PlotJumpInfo` surface. The other plotter surfaces
remain unported, and the new Windows/X11 adapters still require live Elite
runtime validation.

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
