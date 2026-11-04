# Guides content audit

## RC77.5 review — 2026-11-03

Reviewed all 19 in-app guide categories, the symbol glossary, and all 12 shipped
chat-reference topics. Cross-checked active user guides in README and docs for
navigation labels, settings, current source behavior, release examples and local
links. Research, parity reports, UX plans and the earlier audit below are dated
engineering records; their original conclusions are not current how-to promises.

| Area | Gap or dated information corrected | Source checked |
| --- | --- | --- |
| Commander | Missing six-tab readout, faction row, owned-fleet current state and zero-debt behavior | Frontier views, Commander profile, Frontier journal reconciliation |
| Journeys | Navigation location, dated system autocomplete/visit selection, archive/delete, photo viewer and portable HTML report were missing | JourneysView, Journey session, JourneyReport and gallery |
| Screenshot processing | Source versus output ownership, Steam opt-in, capture-time labels, retries and Journey integration needed a complete workflow | Screenshot settings/store/service, Steam source and Journey photo lookup; new SCREENSHOTS.md |
| Screenshot sync | Manual Steam folders were incorrectly treated as portable; fixed ownership and added universal temporary-profile restore tests | ProfileSyncPolicy and capture/apply behavior |
| Theme and editor | Obsolete Overlay Settings/In-game labels, Refresh preview/Apply to overlays and half-point instructions | ThemeView, OverlayPositionEditorWindow and theme settings |
| Overlay controls | Five custom brushes, Colonization vehicle defaults, Prior Scans radar gate and docked visibility needed explanation | Overlay settings, category defaults and Colonization coordinator |
| Colonization | Select Projects did not explain Projects filters or retained hidden selections | Colonization Market Search and project filters |
| Mining | Missing offline settings, yield sorting/limit, quality-hit definition, refinery carryover and Recent Prospects semantics | Mining overlay settings and session tracker |
| Mining searches | Travel persistence versus manual filter edits was unclear | Surface Mining/Powerplay search ownership |
| Carrier routes | Dedicated FC/SC state, shared library, simultaneous overlays and auto-copy arbitration were absent | Travel route workspaces and carrier observation ownership |
| Discord | Webhook setup and matching FC/SC itinerary needed to be discoverable | Application settings and announcement service |
| Privacy | Stealth Mode and EDAstro were missing; VoxStellar guide unnecessarily required a separate EDDN tool | Provider publishers, privacy settings and stealth-mode.md |
| Missions | Massacre tracker scope could be mistaken for all station missions | CombatState mission acceptance |
| Chat commands | Missing waypoint navigation and explicit survey completion | MineMapService command handling |
| Linux | KWin OSD claim, same-display requirement and environment-only Gamescope host selection were dated | Native overlay host policy and Overlay_Troubleshooting.md |
| Developer/platform docs | Full warning gate was described as changed-line-only; stream category and SteamOS PR reference were stale | Quality gate, current settings and host code |
| Version examples | Installation/development examples and release notes needed RC77.5 | Desktop project and release workflow |

The Ubuntu Gamescope guide now identifies September 2026 versions as a tested
configuration, rather than claiming they remain the newest packages. Device-only
Steam Deck and VDXR validation limits are retained. Symbol definitions and other
accurate guidance are preserved. No external service policy or vendor package
version is re-certified by this source-based audit.

All relative file links across 69 Markdown files resolved during this audit.

Validation uses existing guide navigation/search/markup/localization tests and
the repository quality gate. The new sync regression uses fictional folders in
isolated profiles, covers global and Commander settings, and first reproduced
the incorrect folder replacement before the production fix.

## Earlier audit — 2026-10-10

Audited against the desktop workspaces on 2026-10-10. The catalog is
`src/SrvSurvey.Desktop/ViewModels/GuideCatalog.cs`; chat-command help comes from
the shipped `chat-commands-guide.json`. This audit changes help and its reader;
it does not change game actions or network publication.

## Navigation and presentation

- Replace the flat category selector and entire-category reading page with
  expandable category headings and individual selectable subjects.
- Keep search above both navigation and the reader. Match every query word across
  category context and the full subject, including examples and commands. Match
  translated instructions as well as original English names and command text.
- Let each search result open the complete subject and highlight it in navigation.
- Keep one category expanded at a time. Expanding a category does not change the
  current reading subject until a subject is selected.
- Number procedural steps; separate supporting notes; allow instruction text to
  be selected and copied. Reset reading position when opening a subject or changing
  the query.
- Keep the complete rendered icon glossary. Also show relevant actual symbol
  examples beside biology, radar, ground-target, and Guardian instructions.
- Give Firegroups and Fleet Carrier dedicated categories instead of hiding their
  instructions in Mining.

## Accuracy and coverage changes

| Area | Finding and correction | Implementation checked |
| --- | --- | --- |
| Getting started | Replace development/parity provenance text with instructions for using Guides. Include Surface Mining in the sidebar description. | `MainWindow.axaml`, navigation items, Guides reader |
| Overlay setup | Replace the obsolete “Overlay behavior and layout” path with Theme → Overlay Settings. Add monitor selection and drag constraints. | `ThemeView.axaml`, `OverlaySettingsView.axaml` |
| Linux overlays | Document Bypass Window Management, restart requirement, editor scope, utility window classification, and compositor limitations. | Overlay settings and native overlay host policy |
| Linux keyboard | Add automatic shared-source discovery, manual source selection, reset, explicit desktop approval, and the distinction between requested and desktop-approved bindings. | `GlobalKeyboardHookService`, input settings, `GlobalShortcutsPortalInput`, `docs/Overlay_Troubleshooting.md` |
| Wayland capture | Add monitor-first selection, separate tracker opt-ins, selection reuse controlled by the desktop, immediate reselection without restart, game-exit pause/resume, and capture-versus-keyboard permissions. | `WaylandCaptureSettingsViewModel`, `WaylandPortalGameScreenCapture` |
| Capture accuracy | Explain logical display sizes versus frame pixels and the difference between calibrated Rhino targets and viewport-relative FSS/first-footfall regions. Avoid promising accuracy after HUD changes. | `FssTuningScreenCapture`, `FirstFootfallInferenceService`, rig calibration |
| Rhino setup | Correct the distinction between Mining and Surface Mining. Break the long rig-detection article into enabling, calibration, and interpreting results. Retain timing, motion, uncertainty, and privacy limitations. | Surface Mining screen and rig detection settings/coordinator |
| Surface maps | Add selected-marker reticle, inspector location, save/remove controls, editable fields, and click-away deselection. | `MineMapView.axaml`, `MineMapViewModel` |
| Surface search | Add profitable mine-to-sell search, radius and demand filters, buyer/body comparison, and the role of Surface Hunt and Hotspot List. | `MineMapView.axaml`, `SurfaceMiningSearchViewModel` |
| Powerplay search | Remove the obsolete Find → Powerplay workflow. Describe Mining → Powerplay and Planetary Mining alongside ring methods, using current controls. | `MiningPowerplayView.axaml`, Powerplay search view model |
| Ring/market search | Correct the commodity controls and local-import labels. Add Platinum/Spots++ ranking and buyer navigation. | `MiningSearchView.axaml`, `MiningView.axaml` |
| Mining missions | Add mission-specific navigation, progress, hotspot search, and single allocation of cargo. | Mining Missions tab and journal projection |
| Mining backup/announcements | Correct paths to Mining → Settings → Backup/Announcements/Session. Separate notifications from Firegroups. | Mining Settings tabs |
| Firegroups | Document built-in scanners, equipped-module filtering, saved configurations, base names for engineering conversions, and Left/Main/Right visibility with panel-focus limitations. | Firegroup loadout/configuration, workspace and overlay preferences |
| Frontier/carriers | Add linking and multi-Commander context; give carrier tabs, linked inventory, and cargo sync their own guides. | Commander profile, Fleet Carrier workspace, Raven cargo settings |
| Colonization | Correct the Raven connection path to Colonization and the key controls in Fleet Carrier cargo sync. Replace implementation-heavy primary-port details with player-facing safety guidance. Add selecting and resolving unconfirmed deliveries without blindly retrying a possibly credited delivery. | Colonization view, pending contribution actions, site order protection |
| Exobiology values | Describe first-footfall-derived bonus values as estimates; the game determines the payout. | Biology reward/inference presentation |
| VR | Retain supported OpenVR connection and calibration workflows; remove an expiring WMR support-date promise. | VR route options and runtime publisher |

Other existing exploration, exobiology, travel, Boxel, Guardian, quest,
sharing/migration, diagnostic, and symbol-reference topics remain available.
The shipped chat-command reference remains intact and searchable.

## Maintenance and validation

Guide paths and labels should be checked when their workspace changes. A guide
must describe what the player can do in the current screen rather than internal
file formats or an earlier troubleshooting sequence. External observations,
experimental detection, compositor behavior, and portal approval must not be
presented as guarantees.

Validation covers catalog reachability, category expansion, selection highlights,
full-text search including whitespace and commands, opening full search results,
numbered steps, illustration lookup, reading-position reset, and changing the
view's data context. Actual headless renders are checked in light and dark themes
at 900- and 1100-pixel widths. Repository quality gates also check localization,
formatting, Sonar analyzers, and coverage of changed production code.
