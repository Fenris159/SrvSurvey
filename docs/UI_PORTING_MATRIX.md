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
| Travel | `Main` Travel menu, journey/route forms | Ground target, journeys and routes | Ground-target editor, clipboard/current actions, persistence, and live guidance implemented; journeys/routes remain pending |
| Search | `Main` Search menu, sphere/boxel forms | Spatial and boxel searches | Spherical center lookup, radius, enable/disable, live distance, and compatible persistence implemented; Boxel activation, hierarchy, source merging, ID64 decoding, completion, navigation, clipboard, and full-area audit implemented; nearest-system workflow remains pending |
| Guardian | `Main` Guardian menu and survey forms | Sites, maps, beacons and Ram Tah | Reference/commander catalog, visits, exact completion, filters, distance ordering, details, and clipboard actions implemented; Ram Tah, survey maps/editors, live site events, writes, and overlays remain |
| Colonisation | `Main` Colonise menu and project forms | Raven projects and construction state | Not ported |
| Diagnostics | `ViewLogs`, journal development tools | Journal source, candidate paths and logs | Journal source and parsed state implemented; full logs not ported |
| Settings | `FormSettings`, `FormSetKeyChord`, `FormAdjustOverlay` | Themes, paths, overlays, input and privacy | Raven themes plus checksum-verified legacy profile import implemented; remaining settings not ported |

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
| `FormEditMap` | Guardian / Map editor | Not ported |
| `FormErrorSubmit` | Diagnostics / Report issue | Not ported |
| `FormGroundTarget` | Travel / Ground target | Implemented in Travel with typed, current, clipboard, clear, and guidance actions; Windows visually checked |
| `FormJourneyBegin` | Travel / Journeys | Not ported |
| `FormJourneyEdit` | Travel / Journeys | Not ported |
| `FormJourneyList` | Travel / Journeys | Not ported |
| `FormJourneyViewer` | Travel / Journeys | Not ported |
| `FormMyProjects` | Colonisation / Projects | Not ported |
| `FormNearestSystems` | Search / Nearby systems | Not ported |
| `FormNewProject` | Colonisation / Projects | Not ported |
| `FormPlayComms` | Developer tools | Deferred |
| `FormPlayDev` | Developer tools | Deferred |
| `FormPlayJournal` | Diagnostics / Journal tools | Not ported |
| `FormPostProcess` | Diagnostics / Journal tools | Not ported |
| `FormPredictions` | Exobiology / Predictions | Not ported |
| `FormRamTah` | Guardian / Ram Tah | Not ported |
| `FormRavenUpdater` | Update flow | Not ported |
| `FormRoute` | Travel / Routes | Not ported |
| `FormRuins` | Guardian / Survey maps | Not ported |
| `FormSetKeyChord` | Settings / Input | Not ported |
| `FormSettings` | Settings pages | Raven theme slice implemented; remainder not ported |
| `FormShareData` | Settings / Privacy | Not ported |
| `FormShowCodex` | Exobiology / Codex | Not ported |
| `FormSphereLimit` | Search / Spherical | Implemented with live Spansh lookup, matching-system selection, 1–1000 ly validation, current distance, enable/disable, and compatible commander persistence; Windows visually checked |
| `FormStartNewCmdr` | Commander onboarding | Not ported |
| `FormSwapStarCache` | Diagnostics / Reference data | Not ported |
| `FormSystemNotes` | Travel / System notes | Not ported |
| `ViewJourneySystem` | Travel / Journeys | Not ported |
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
  position, and explicit pending journey/route card. UI Automation exposed both
  coordinate fields and Set, current-location, Paste, and Clear actions with
  correct disabled state.
- Search rendered the spherical limit, live current-system coordinates,
  configuration editor, Boxel status/options/hierarchy, current-boxel actions,
  full-area audit controls, and an explicit nearby-systems pending card. A live
  Spansh lookup for Sol returned five matches, selected the exact system,
  calculated 131.09 ly from Facece, and enabled the save action without changing
  the live commander profile. UI Automation exposed the spherical and Boxel
  controls, including the inactive audit/cancel states; no Boxel action or audit
  was invoked.
- Guardian rendered all 759 shipped sites ordered from the live Facece position,
  filtered immediately to the unique `GR 1` system address, and exposed selected
  site/commander details plus system, address, galactic-position, and lat/long
  copy controls. List scrolling and full coordinate layout were visually checked
  at 1182 by 790; no refresh, clipboard, survey, or profile-changing action was
  invoked. UI Automation data was unavailable for this check.
- Diagnostics rendered the selected journal folder, parsed state, candidate
  paths, refresh action, and update time.
- Settings rendered all five palette previews. Switching from Blue (dark) to
  Blue (light) updated the complete window and saved the choice.
- A pending navigation item rendered the explicit incomplete-feature message.
- Windows UI Automation exposed all nine navigation destinations, five theme
  buttons, Refresh actions, and the visible page text.

Not yet checked: active in-game sample transitions, minimum-width and
high-contrast rendering, Linux X11/Wayland, or any overlay surface.

## UI completion gates

For each migrated surface:

1. Map every user action and state from the WinForms source.
2. Connect it to ported behavior; label unavailable actions explicitly.
3. Verify keyboard navigation, focus visibility, scaling, and narrow-window
   behavior.
4. Exercise it in every Raven theme and at least one high-contrast OS mode.
5. Add view-model or headless UI tests where practical.
6. Record Windows and Linux runtime evidence in `PORTING_PLAN.md`.
