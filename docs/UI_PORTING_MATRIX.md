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
| Overview | `Main` commander group | Commander, game/session, system and body state | Journal bootstrap data available |
| Exploration | `Main` exploration group | Jumps, distance, bodies and estimated value | Core data not ported |
| Exobiology | `Main` bio group, `FormPredictions`, Codex forms | Scan progress, rewards and predictions | Core data not ported |
| Travel | `Main` Travel menu, journey/route forms | Ground target, journeys and routes | Not ported |
| Search | `Main` Search menu, sphere/boxel forms | Spatial and boxel searches | Not ported |
| Guardian | `Main` Guardian menu and survey forms | Sites, maps, beacons and Ram Tah | Not ported |
| Colonisation | `Main` Colonise menu and project forms | Raven projects and construction state | Not ported |
| Diagnostics | `ViewLogs`, journal development tools | Journal source, candidate paths and logs | Journal source data available |
| Settings | `FormSettings`, `FormSetKeyChord`, `FormAdjustOverlay` | Themes, paths, overlays, input and privacy | Theme selection in this UI slice; remainder not ported |

Unavailable areas may appear in the shell to preserve discoverability, but they
must be labelled as pending and must not imply working behavior.

## Secondary forms

| Legacy surface | Avalonia destination | Status |
| --- | --- | --- |
| `FormAdjustOverlay` | Settings / Overlays | Not ported |
| `FormBeacons` | Guardian / Beacons | Not ported |
| `FormBoxelSearch` | Search / Boxel | Not ported |
| `FormBuilder` | Guardian / Map editor | Not ported |
| `FormCodexBingo` | Exobiology / Codex | Not ported |
| `FormEditMap` | Guardian / Map editor | Not ported |
| `FormErrorSubmit` | Diagnostics / Report issue | Not ported |
| `FormGroundTarget` | Travel / Ground target | Not ported |
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
| `FormSettings` | Settings pages | Theme slice in progress |
| `FormShareData` | Settings / Privacy | Not ported |
| `FormShowCodex` | Exobiology / Codex | Not ported |
| `FormSphereLimit` | Search / Spherical | Not ported |
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

## UI completion gates

For each migrated surface:

1. Map every user action and state from the WinForms source.
2. Connect it to ported behavior; label unavailable actions explicitly.
3. Verify keyboard navigation, focus visibility, scaling, and narrow-window
   behavior.
4. Exercise it in every Raven theme and at least one high-contrast OS mode.
5. Add view-model or headless UI tests where practical.
6. Record Windows and Linux runtime evidence in `PORTING_PLAN.md`.
