# Legacy Feature Parity Audit

Last audited: 2026-08-02

This audit compares the current Avalonia working tree with the legacy
application at `origin/main` commit
`b592d991daa035ddda6682be52f3e55791c6ab29`. It treats a compiling screen as
insufficient evidence: a workflow needs a production implementation, durable
state behavior where applicable, and focused assertions. Native integrations
also need live verification on their supported operating systems.

## Result

No unmatched legacy user workflow or overlay family was found in this source
pass. All 36 functional forms under `SrvSurvey/forms` have a current surface,
and all 27 functional legacy plotter contracts have a current presentation
path. The three additional Avalonia panels bring the editor/runtime panel
inventory to 28.

This is not a declaration that native runtime parity is complete. The source
and automated-test mapping is complete enough to proceed with testing, but the
runtime checklist below remains a release gate.

## Legacy form mapping

| Legacy workflow | Legacy forms | Current surface |
|---|---|---|
| Overlay positioning | `FormAdjustOverlay` | `OverlayPositionEditorWindow`, shared layout store, live interaction controller |
| Guardian surveys | `FormBeacons`, `FormEditMap`, `FormRamTah`, `FormRuins`, `FormShareData` | Guardian workspace, map renderer/editor, Ram Tah state, survey archive sharing |
| Human-site authoring | `FormBuilder` | Human-site template authoring and live map workspace |
| Search | `FormBoxelSearch`, `FormNearestSystems`, `FormSphereLimit` | Search workspace: boxel, nearest-system, and spherical-search sections |
| Exobiology reference | `FormCodexBingo`, `FormPredictions`, `FormShowCodex` | Codex Bingo, predictions, Codex browser, and Exobiology workspace |
| Journeys | `FormJourneyBegin`, `FormJourneyEdit`, `FormJourneyList`, `FormJourneyViewer`, `ViewJourneySystem` | Journey window with creation, library, editing, system detail, notes, and history |
| Colonisation projects | `FormMyProjects`, `FormNewProject` | Colonisation project library and project editor |
| Quest communications and development | `FormPlayComms`, `FormPlayComms2`, `FormPlayDev`, `FormPlayJournal` | Quest messages/catalog/history/developer workspace plus the Diagnostics journal inspector and confirmed event replay |
| Error reporting | `FormErrorSubmit` | Error report window with issue template, logs, journal path, Discord, and copy actions |
| Ground target | `FormGroundTarget` | Ground-target overlay and view model |
| Multiple commanders | `FormMultiFloatie`, `FormStartNewCmdr` | Multi-game commander overlay and Overview commander launcher/switcher |
| Journal post-processing | `FormPostProcess` | Diagnostics journal post-processor, statistics, rebuilds, and historical publication confirmation |
| Updates | `FormRavenUpdater` | Transactional release check, staging, health confirmation, and rollback |
| Routes | `FormRoute` | Route window and Travel route/FC-route workspaces |
| Input settings | `FormSetKeyChord` | Keyboard/controller binding settings and live-overlay shortcut configuration |
| Settings | `FormSettings` | Settings and Overlay Settings workspaces |
| Visited-star cache | `FormSwapStarCache` | Diagnostics visited-star cache tools |
| System notes | `FormSystemNotes` | System notes window |
| Logs | `ViewLogs` | Diagnostics live log viewer |

`BaseForm` and `BaseFormZippy` are legacy WinForms infrastructure, not separate
user workflows.

## Automated parity guardrails

- Journal state now inventories 74 parity-critical events. This audit added
  `FSSSignalDiscovered`, `NavBeaconScan`, `ScanBaryCentre`, `SendText`,
  `FactionKillBond`, and `Screenshot`, which were implemented and tested but
  were missing from the formal inventory. The legacy `DataScanned` type is not
  counted because the legacy application only declared empty base hooks and
  supplied no feature override.
- Overlay coverage inventories all 28 editor/runtime panels. The assertion
  mapping now also names the commodity, notification, mini-track, multi-game,
  pulse, quest-indicator, and station-information panels instead of proving
  only that their markup files exist.
- Legacy profile import remains recursive, backup-first, hash-verified, and
  recoverable. Its typed settings translation has a 133-control audit.
- Network coverage inventories every runtime `HttpClient` owner and enforces
  bounded response handling for application-controlled downloads.
- The current upstream delta after the prior `c8068866` baseline contains only
  the two Eunostus documentation images; it introduces no new legacy runtime
  behavior to port.

## Runtime parity gates still open

These are proof gaps, not source features known to be absent:

1. Clean Windows testing with a live Elite Dangerous session: journal
   attachment, process/commander attribution, global keyboard and controller
   input, game text entry, capture-dependent features, tray shutdown, and
   screenshot processing.
2. Both overlay hosts (`separate` and `combined`): click-through, live drag,
   editor synchronization, dynamic heights, all 28 panels, global/per-panel
   opacity and scale, game resize, alt-tab, mixed DPI, multiple monitors,
   stream/OpenVR output, and Galaxy Map suppression rules.
3. Representative legacy-profile import followed by restart, then a comparison
   of commander, route, survey, quest, overlay, and settings projections before
   accepting new writes.
4. Application update staging, health confirmation, rollback, and recovery on
   a clean installed/portable machine.
5. Linux AppImage testing on native X11 and XWayland. Pure native Wayland
   remains an explicit platform limitation because it cannot provide the
   required game-window tracking, click-through, capture, and global-input
   contracts.

The port should remain labeled a testing preview until these runtime gates are
recorded against a release candidate.
