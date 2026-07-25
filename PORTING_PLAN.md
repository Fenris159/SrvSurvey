# SrvSurvey cross-platform porting plan

Last validated: 2026-07-25

## Goal and definition of done

Port SrvSurvey from its Windows-only WinForms/SharpDX implementation to a supported
desktop application for Windows and Linux without silently dropping user data,
journal events, overlays, input behavior, localization, or existing survey
workflows.

The port is complete only when:

1. The Windows and Linux packages start on clean supported systems.
2. The same journal fixtures produce equivalent domain state in the legacy and
   cross-platform applications.
3. Each feature in the parity matrix below has automated tests and recorded
   Windows and Linux runtime results.
4. Existing settings and commander data are either read in place or migrated
   once with a backup and a documented rollback path.
5. Platform limitations are visible to the user instead of being reported as
   successful behavior.

An application that merely opens an Avalonia window is a foundation milestone,
not a completed port.

## Audit of the initial branch work

The initial three port commits (`454881bf`, `0bd711ad`, and `ceac0fa2`) did not
contain the implementations described by their commit messages:

| Area | Claimed state | Validated state at `ceac0fa2` |
| --- | --- | --- |
| Porting plan | Complete sequential plan | Three-line pointer to unavailable “local artifacts” |
| Core library | Models, resource loading, colonization logic | One speculative record and two comment-only files |
| Avalonia UI | Integrated MVVM desktop application | Invalid project, invalid XAML, and comment-only C# files |
| Solution integration | Cross-platform projects included | Neither new project was in `SrvSurvey.sln` |
| CI | Windows, Linux, AppImage, and container builds | Comment-only workflow |
| Container | Multi-stage build | Comment-only Dockerfile |
| Documentation | Foundation complete | Original README replaced by one inaccurate sentence |

The legacy application is a substantial porting source: the audited tree has
231 C# files (about 93,500 lines), 70 WinForms designer files, 340 resource
files, and at least 51 C# files with direct WinForms, SharpDX, or native Win32
dependencies. Core behavior must be extracted deliberately; it cannot be
validated by recreating similarly named types.

## Target architecture

Keep the production application available while building a second executable in
vertical slices:

```text
SrvSurvey (legacy WinForms)
    Existing production behavior and comparison oracle

src/SrvSurvey.Core
    Journal and status ingestion
    Domain state and calculations
    Persistence contracts and migrations
    No Avalonia, WinForms, SharpDX, or platform P/Invoke

src/SrvSurvey.Desktop
    Avalonia views and view models
    Composition root
    Platform capability reporting

src/SrvSurvey.Platform.*
    Small Windows/Linux implementations for game-window tracking,
    overlay behavior, global input, notifications, and filesystem locations

tests/SrvSurvey.Core.Tests
    Journal fixtures, calculations, persistence, and migration tests
```

Use a separate cross-platform solution so Linux builds never evaluate the
Windows Application Packaging Project in `SrvSurvey.sln`.

## Porting rules

- Port observable behavior from the source and fixtures, not type names from
  memory.
- Move domain logic only after its inputs, outputs, units, edge cases, and
  persistence effects are understood.
- Keep platform services behind interfaces. Platform checks do not belong
  throughout the domain model.
- Preserve on-disk JSON compatibility where practical. Any schema change needs
  fixture coverage, a backup, a migration version, and rollback instructions.
- Treat malformed or partially written journal/status files as expected input.
- Keep network publication opt-in and preserve the legacy privacy controls.
- Do not mark a phase complete from compilation alone. Record runtime evidence
  per operating system and display server.

## Platform capability matrix

The Linux target must distinguish X11 and Wayland. Wayland compositors commonly
restrict global input, absolute window positioning, and click-through overlays;
these behaviors require runtime probes and may remain unavailable on some
desktops.

| Capability | Windows | Linux X11 | Linux Wayland |
| --- | --- | --- | --- |
| Read journals/status files | Required | Required | Required |
| Transparent topmost overlays | Required | Validate | Probe compositor support |
| Click-through overlay mode | Required | Validate | May be unavailable |
| Follow Elite game window | Required | Validate | May be unavailable |
| Global keyboard/controller input | Required | Validate replacement | Keyboard portal/compositor dependent; SDL controllers available |
| VR/Direct3D integration | Preserve as Windows adapter | Not initially planned | Not initially planned |

Unavailable capabilities must disable dependent features with an explanation;
they must not fail silently.

## Sequential delivery plan

### Phase 0 — Correct and make the foundation executable

- [x] Audit the existing branch claims against committed files.
- [x] Restore the project README and replace the placeholder plan.
- [x] Remove case-colliding repository paths.
- [x] Create buildable Core, Desktop, and test projects.
- [x] Add a cross-platform solution that excludes Windows packaging projects.
- [x] Add Windows and Linux CI restore, build, test, and publish smoke checks.
- [ ] Make the Dockerfile a reproducible Linux build environment, or remove it.

Exit gate: a clean clone restores, builds, tests, and publishes on Windows and
Linux. Launching the desktop shell is manually smoke-tested on both platforms.

### Phase 1 — Journal bootstrap vertical slice

- [x] Resolve the journal folder from an explicit setting first.
- [x] Offer platform-specific candidate locations without assuming one exists.
- [x] Read the newest journal safely, including an incomplete final line.
- [x] Extract commander, game version, mode, system, body, and shutdown state
  present in the newest file, including the legacy current-planet semantics.
- [ ] Rebuild bootstrap state across rotated/prior journals when the newest file
  does not contain all session-identifying events.
- [x] Watch journal rotation and `Status.json` with cancellation and retry logic.
- [x] Show the state and any path/parse errors in the desktop shell.

Exit gate: shared fixtures and a live journal session produce the expected state
on Windows and Linux.

### Phase 2 — Settings, commander data, and resources

- [x] Inventory every imported file recursively with size and SHA-256 evidence.
- [x] Define OS-appropriate config/data/cache locations while discovering the
  desktop and Microsoft Store legacy locations on Windows.
- [x] Implement a backup-first legacy data importer that verifies the backup and
  staged destination before activation and never mutates the source.
- [x] Add the five Raven Colonial shell themes with native light/dark modes and
  an isolated persisted preference.
- [ ] Port theme, localization, and static JSON/image resource loading.
- [ ] Test unknown fields, corrupt files, concurrent writes, and upgrades.

Exit gate: a copied real user profile opens without mutation, and an imported
profile matches the legacy application after restart.

### Phase 3 — Domain state and journal parity

- [ ] Port journal event models from observed payloads.
- [ ] Port system/body, commander, organic scan, guardian, human settlement,
  mission, cargo, colonization, and quest state in review-sized groups.
- [ ] Create golden journal fixtures for each group.
- [ ] Compare serialized state or a documented equivalent projection against
  the legacy implementation.

Exit gate: the supported event inventory has a fixture and parity result; unknown
events remain non-fatal and observable.

### Phase 4 — Overlay and input infrastructure

- [x] Implement Avalonia overlay primitives, scaling, theme, and multi-monitor
  coordinates.
- [x] Add platform adapters for topmost/click-through behavior and game-window
  tracking.
- [x] Replace SharpDX input with maintained APIs and preserve configurable
  keyboard and controller bindings.
- [x] Define behavior for unsupported Wayland capabilities.
- [ ] Measure overlay update cost while the game is active.

The first consumer is the detached Guardian live map/current-obelisk surface.
Windows uses native extended window styles plus client-area tracking; X11 uses
XShape input regions plus EWMH/Xlib window discovery. Both follow the active
Elite client area, account for monitor scaling, hide when Elite is minimized or
not foreground, and fail closed if click-through cannot be enabled. Wayland
keeps detached overlays disabled because absolute placement and input regions
are compositor-controlled. SharpHook now provides opt-in global keyboard input
on Windows and X11, while SDL3 provides standard gamepad, joystick, and HOTAS
discovery/polling on Windows and Linux with reconnecting stable device IDs and
legacy chord-release behavior. Native Elite input checks with physical hardware,
Windows/X11 overlay runtime checks, and the remaining plotter surfaces are still
required.

`PlotJumpInfo` is now a second complete automatic-lifecycle consumer. It selects
the latest journal `FSDTarget` ahead of the status destination, falls back from a
short `NavRoute.json` route to the active followed route, preserves `Alt+D`, and
shows proportional hop distance, scoopable stars, boosted legs, discovery,
traffic, body/biology/station counts, brokers, traders, engineers, Guardian
sites, followed-route notes, and galactic-region transitions. EDSM and Spansh
requests are independent and failure-tolerant so one unavailable provider does
not interrupt journal monitoring. Persisted automatic, compact, and selected
next-hop preferences are exposed in Settings. Windows dark/light presentation
has been checked; attachment to a live Elite client and Linux remain open.

`PlotFSSInfo` and `PlotSysStatus` now share a journal-derived current-system
model rather than WinForms-owned caches. It tracks body type and detail, scan
and DSS valuation, discovery/mapping, signals and analyzed progress, rings,
materials, destination context, FSS completion, and non-body signals. The
Raven-themed top-left body feed preserves value/signal filtering, `Alt+F`, and
FSS/system-map/navigation-panel modes; the bottom-left status surface preserves
FSS percentage, filtered DSS candidates, destination grouping, biology, and
optional non-body counts. Guardian-system priority is retained, with a forced
FSS view taking precedence. Windows dark/light presentation has been checked;
live Elite attachment and Linux remain open.

The journal-backed portion of `PlotFSS` is also implemented as a passive
top-center last-scan card. It retains the latest detailed standalone planet,
ignores stars, belt clusters, and ring children, and shows discovery,
terraformable/landable state, distance, scan and mapped values, and biological
signal count. The legacy experimental `watchFssSettings_TEST` screen-pixel
tuning detector still needs a platform capture abstraction before `PlotFSS`
can be considered completely equivalent. Blue dark/light presentation has been
checked at the active Windows scaling; live Elite attachment and Linux remain
open.

`PlotBodyInfo` now has a Raven-themed passive replacement with the original
map/orrery, DSS, orbit/glide, optional surface-analysis, Sol-bubble, and `Alt+B`
visibility rules. It selects current versus status-destination bodies by mode,
supports unscanned targets, and renders discovery/mapping, scan and mapped
values, temperature, high-gravity warnings, pressure, biological/geological
signals, volcanism, atmosphere composition, materials, and rings. Forced body
information retains its legacy priority over the Guardian summary. A dense
synthetic body was checked in Blue dark/light at the active Windows scaling.
The exact biology evaluator is now available, but wiring its predicted reward
range into this separate `PlotBodyInfo` surface remains open; live Elite
attachment and Linux also remain open.

`PlotBioSystem` now has a Raven-themed passive replacement with its legacy
whole-system, FSS-last-body, and near/current/target-body selection rules. The
shared reducer retains genus/species/variant identities, localized names,
Codex IDs, rewards, analyzed state, regional-first state, and geological names.
The overlay renders active sampling, analyzed dimming, partial and confirmed
reward totals, first-footfall value, DSS guidance, geology, and unknown slots;
its persisted settings and Guardian suppression are wired into the shared
coordinator. The embedded v4 criteria catalog now supplies exact species and
variant predictions plus body/system reward ranges when scan context is
complete. It covers all 21 shipped criteria resources, galactic regions,
parent/barycentre ancestry and relative brightness, legacy star aliases, the
offline nebula catalog, Guardian bubbles, common-child inheritance, and known
genus/species suppression. Missing inputs stay explicit instead of producing
false exact matches, and disabling predictions refreshes the overlay
immediately. Dense body views, a three-body system overview, and an exact
prediction card were checked in Blue dark/light. Commander-Codex firsts,
Canonn hints, the transient map-selection timer, live Elite attachment, and
Linux remain open.

`FormPredictions` now has a single-instance Raven-themed workspace backed by
the same exact evaluator. It shows system totals, confirmed and estimated
rewards, first-footfall estimates, expandable per-body organism rows, sample
separation distances, incomplete-scan guidance, current/target state, and
Canonn, Spansh, and EDSM actions. Current-body focus and compact, comfortable,
or large row sizing persist across launches. The 1040 by 760 logical-pixel
window was exercised in Blue dark/light, including focus, row-size, expand,
and collapse interactions. Commander-Codex first-discovery flags, live Elite
attachment, and Linux remain open.

`FormCodexBingo` now has a single-instance Raven-themed workspace backed by the
complete 1,070-entry Codex hierarchy. It reads the same global and 42-region
commander ledgers populated by live journals, can import the Canonn Challenge
and bounded historical journal files, and retains explicit confirmation for
manual discoveries. Commander and region selectors, recursive progress,
entry/location detail, Canonn/Bioforge/EDAstro/Spansh actions, and handoff to
the integrated nearest-signal or missing-variant search are implemented. An
isolated three-entry ledger was exercised in Blue dark/light, including tree
expansion, selection details, retained window state, disabled actions, and
cleanup. Live Elite updates, interactive remote imports, and Linux remain open.

`PlotBioStatus` now has a compact top-center passive replacement with its
separate default-on preference. It retains current-body gating, DSS-required
guidance, genus and geology summaries, analyzed progress, stale-body sample
warnings, the active one/two/three sample state, required and live separation
distance, reward and first-footfall value, and jump/Guardian priority. Active,
summary, and DSS states were checked at the legacy 480-pixel width in Blue
dark/light. The last Composition Scanner Codex notification/image action, the
experimental temperature-range display, live Elite attachment, and Linux
remain open.

Exit gate: overlay positioning, DPI scaling, focus, click-through behavior, and
input are recorded on the supported platform matrix.

### Phase 5 — Primary user features

Port and validate one complete feature at a time:

1. Organic scan tracking and distance guidance.
2. Latitude/longitude ground target guidance.
3. Guardian ruins, structures, beacons, maps, and survey persistence.
4. Core automatic overlay lifecycle driven by game state.

Exit gate per feature: journal fixtures, calculation tests, persistence tests,
UI tests where practical, and live Windows/Linux evidence.

### Phase 6 — Remaining feature parity

- [ ] System/body/FSS information and route overlays.
  Followed-route Galaxy Map guidance, `PlotJumpInfo`, `PlotFSSInfo`, and
  `PlotSysStatus` are implemented. The journal-backed `PlotFSS` card is
  implemented, while its experimental pixel tuning detector remains open;
  `PlotBodyInfo` selection, visibility, and presentation are implemented while
  wiring its biological reward range to the shared prediction engine remains
  open. `PlotFlightWarning` is implemented with the exact landable-body,
  gravity-threshold, flight-mode, and persisted auto-show rules. The
  journal-backed `PlotBioSystem` presentation, lifecycle, and exact
  prediction inputs are implemented, while its auxiliary Canonn/Codex cues
  remain open. `FormPredictions` is implemented as a standalone system/body
  workspace. `PlotPriorScans` and its Canonn slice of the grounded radar are
  implemented with validated current-body coordinates, commander/analyzed and
  value filtering, continuously recalculated surface bearing/distance,
  approach angle, near/far state, and persisted radar-circle preferences. The
  journal-backed `PlotGrounded` and `PlotTrackers` replacement now preserves legacy
  touchdown, bookmark, and completed-scan records, active samples, sample
  removal rules, exact surface-mode gating, five radar sizes, heading-relative
  circles, ship/SRV markers, Composition Scanner auto-tracking, cross-system
  death marking, adjustable radar scale, all eight quick-location chords, and a
  compact tracker-only Raven state. The experimental dedicated `PlotMiniTrack`
  variant, Human-site arbitration, and remaining route/search plotter modes
  stay open.
- [ ] Human settlements and post-processing tools.
- [ ] Cargo, missions, massacre/foot combat, and colonization projects.
  `PlotFootCombat` and `PlotMassacre` are implemented with legacy settlement,
  altitude, vehicle/panel, active-project suppression, mission-lifecycle,
  expiry, and one-credit-per-mission-giver rules. Massacre progress round-trips
  through the legacy-compatible commander profile. General cargo and remaining
  mission surfaces stay open.
- [ ] Quest communications and controller navigation.
- [ ] Network integrations, update behavior, diagnostics, and remaining tools.
- [ ] Localization review for every migrated surface.

Features may be explicitly deferred, but the release notes and UI must identify
the gap.

### Phase 7 — Packaging and release

- [ ] Produce self-contained Windows and Linux publish directories.
- [ ] Decide whether Windows single-file publishing is compatible with native
  Avalonia dependencies, diagnostics, and update requirements.
- [ ] Build the Linux AppImage from the tested publish output.
- [ ] Add icons, desktop entry, MIME/URL handling if required, and licenses.
- [ ] Generate checksums and a software bill of materials.
- [ ] Test install, upgrade, downgrade/rollback, and uninstall on clean systems.

A Docker image is useful for reproducible Linux builds; the GUI application is
not expected to run as a headless container service.

## Current parity status

| Feature | Core | Desktop UI | Windows runtime | Linux runtime |
| --- | --- | --- | --- | --- |
| Application shell | Not applicable | Modern navigation plus Overview, Exploration, Exobiology, Travel, Search, Guardian, Colonisation, Diagnostics, Settings, and explicit pending states | Blue dark/light shell plus current Overview, Exobiology, Travel, Search, Guardian, Colonisation, and Settings visual smoke passed; Exploration page needs current visual recheck | Not tested |
| Journal folder discovery | Implemented; 3 tests | Paths and errors shown | Missing and default paths smoke-tested | Not tested |
| Journal ingestion/state | Retrying status/cargo readers plus polling journal append/partial-line/rotation and `Status.json`/`Cargo.json`/`NavRoute.json` change monitor; shared bootstrap/live reducer | Overview and Diagnostics projections update live; cargo changes feed Guardian artifact state | Earlier bootstrap state and cargo-backed inactive Guardian state inspected; current live monitor not exercised with Elite | Not tested |
| Raven shell themes | Five definitions; 11 desktop tests cover themes, persistence, and shell navigation | Five-theme gallery and runtime switching | Blue dark/light switched and inspected | Not tested |
| Settings/data migration | OS paths, legacy discovery, manifests, verified backup/staging/import, lossless commander profile updates, and all 30 legacy input binding names/defaults implemented | Explicit backup-and-import workflow plus opt-in global keyboard/controller settings, validation, default restore, SDL device picker, refresh, and reconnect status | Settings/input UI visually and accessibility checked in Blue light; native keyboard hook reached active; SDL initialized without connected hardware; real profile restart comparison and physical controller input not run | Not tested |
| Exploration totals | Legacy valuation and six counters plus compatible, atomic profile persistence implemented | Live Overview/Exploration projections and two-step reset | Automated only | Not tested |
| Organic scans | Complete 1,070-entry Codex reference, global and 42-region commander discovery ledgers, live and historical journal ingestion, Canonn Challenge import, confirmed manual overrides, discovery-location resolution, three-sample state, surface separation, first-footfall reward, sale, death, reset, compatible profile fields, current-system organism/geology history, sample-distance catalog, exact Canonn surface-POI ingestion/planning, legacy surface-history/bookmark persistence, Composition Scanner tracking, cross-system death marking, live three-sample mutations, and the complete embedded v4 prediction evaluator with region/star/nebula/Guardian context implemented | Live Overview/Exobiology projections, two-step unclaimed reset, passive `PlotBioSystem`, compact `PlotBioStatus`, single-instance `FormPredictions`, single-instance `FormShowCodex` browser with bounded image cache, single-instance `FormCodexBingo` workspace, passive `PlotPriorScans` guidance, and consolidated `PlotGrounded`/`PlotTrackers` heading-relative radar, tracker list, zoom, and quick-location workflow are implemented | Empty/live-profile page, dense body/system predictions, the standalone predictions workspace, compact active/summary/DSS overlay states, the Codex browser with a real cached reference image, and a three-entry Codex Bingo hierarchy checked in Blue dark/light; prior-scan and grounded-radar visual QA is intentionally deferred to the final UI pass, and active Elite sampling/attachment, interactive remote imports, and Linux were not run | Not tested |
| Ground target tracking | Legacy coordinate parsing plus cardinal formats, validated settings, great-circle distance/bearing, relative heading, approach bands, and exact legacy mode gating implemented | Travel target editor supports typed, current, clipboard, clear, and live guidance; passive bottom-center `PlotTrackTarget` replacement adds Raven-themed bearing/descent instrumentation and global visibility control | Inactive/live-profile page visually and accessibility checked; overlay XAML and lifecycle are automated, while its visual/theme pass and active surface guidance are intentionally deferred to the final UI pass | Not tested |
| System notes | Lossless legacy per-system JSON lookup/update/creation plus legacy topmost and screenshot-folder settings implemented; saves against the active Journey system update that visit's note counter | Single-instance resizable notes window, current-system context, save/cancel, always-on-top, Canonn/Spansh/EDSM, screenshots, Travel launcher, and `Ctrl+Shift+N` implemented | Travel card and notes window visually checked in Blue dark/light without changing live notes | Not tested |
| Commander journeys | Lossless legacy journey JSON and active-pointer persistence, historic journal replay, all legacy counters/rewards/flags, current and prior-system starts, live updates, note coupling, conclude, and bounded reprocess implemented | Single-instance responsive workspace unifies begin, list, edit, viewer, and per-system details; supports history, preferences, notes, screenshots, dirty-state handling, and guarded destructive actions | Isolated QA journey exercised in Blue dark/light, including both start modes, begin, overview, visited systems, note edit/discard, refresh, and conclude/reprocess confirmations; no live commander Journey data changed | Not tested |
| Followed routes | Lossless legacy route JSON, activation/progression/completion, out-of-order protection, name resolution, and current Spansh result polling/shapes implemented | Travel card plus single-instance route workspace support imports, per-hop progress, route preferences, manual copy, live FSDJump advancement, route-priority Galaxy Map auto-copy, and a passive route overlay | Isolated QA route/editor and overlay preview exercised in Blue dark/light; progress/discard, distance, notes, neutron/refuel, destination, and copy states checked; no live commander data retained and no live Elite attachment run | Not tested |
| Next-jump information | `FSDTarget`/status target precedence, nav/followed/direct route planning, ship-range boost detection, EDSM bodies/traffic, Spansh dump aggregation, Guardian counts, and galactic-region lookup implemented with partial-provider failure handling | Passive top-center `PlotJumpInfo` replacement, proportional route renderer, automatic/FSD/selected-hop lifecycle, `Alt+D`, compact mode, persisted preferences, and Guardian-overlay arbitration implemented | Live Sol provider responses and a two-hop 64.9 ly preview checked in Blue dark/light; temporary hook removed and Blue dark restored; no live Elite attachment run | Not tested |
| System/FSS survey | Current-system reducer tracks body details, galactic position, FSS/DSS values and completion, discovery/mapping, rings, materials, bio/geo analysis, destination context, latest detailed standalone planet, high-gravity warning state, and non-body signals with stale-system rejection | Passive top-left `PlotFSSInfo`/`PlotBodyInfo`, top-center journal-backed `PlotFSS` and `PlotFlightWarning`, and bottom-left `PlotSysStatus` replacements, `Alt+F`/`Alt+B`, legacy visibility/filter/Sol-bubble/gravity settings, bounded body feed, detailed body composition, last-scan values/signals, DSS targets, and Guardian arbitration implemented; the FSS pixel detector and body biological reward range remain open | Synthetic six-body, last-scan, and dense body previews checked in Blue dark/light at actual Windows DPI; high-gravity warning XAML/state are automated but intentionally await the final UI pass; hooks and QA settings removed; no live Elite attachment run | Not tested |
| Spherical search limit | Legacy-compatible center/radius state, strict boundary rule, lossless commander persistence, journal galactic position, and current Spansh response contract implemented | Search supports center lookup and selection, 1–1000 ly radius, enable/disable, and current-system distance/result; `PlotSphericalSearch` remains pending | Live profile page and Sol lookup visually/accessibility checked; save action enabled but profile was not changed | Not tested |
| Boxel search | Generated-name hierarchy, ID64 decoding for generated and hand-authored names, completion/skip rules, lossless commander fields, legacy local-system and empty-boxel files, paged Spansh search, live `NavRoute.json`/journal updates, and cancellable full-area completion audit with partial results implemented | Search supports activation, mass-code/date options, current/parent/sibling/child navigation, expected counts, manual completion, empty marking, current-system highlighting, refresh, manual/Galaxy Map clipboard copy, audit progress/cancellation, and a large-audit confirmation guard | Inactive live-profile page and audit controls visually and accessibility checked at 1182 by 790; automated ID64, source-merge, live-completion, audit, cancellation, and confirmation integration passed; no search/profile/audit action was invoked | Not tested |
| Nearby biological systems | Canonn nearest-codex and POI enrichment plus Spansh missing-variant request/response contracts implemented; malformed rows are excluded and Spansh results are limited to five unique systems | Search supports current journal reference coordinates, both legacy modes, validation, result selection, copy name/coordinates, Canonn/Spansh links, and the original Spansh result page | Both modes and conditional inputs visually checked in Blue dark at 1182 by 790 without issuing a live query; automated request, enrichment, selection, clipboard, address-resolution, and link coverage passed | Not tested |
| Guardian surveys | All 759 shipped sites, 13 map templates, 729 published surveys, exact completion scoring, duplicate-ID/full-body matching, legacy/current commander files, compact/old POI and obelisk formats, visits, notes, beacon locations, live site transitions/writes, native map projection, lossless survey editing, cargo artifact aliases, 25 m obelisk proximity, scan persistence, and both Ram Tah missions implemented; advanced authoring and remaining Guardian plotter modes remain | Browser, clipboard actions, native maps, survey editor, live-site/scanner cards, artifact requirements, scan toggle, both complete Ram Tah workspaces, and a detached live map/current-obelisk overlay implemented | Live-profile browser, maps/editor, Ram Tah tabs, and inactive scanner card visually checked at 1182 by 790; scanner checked in Blue dark/light without changing commander state; detached overlay compiled and automated but active in-game proximity was not run | Not tested |
| Overlays/input | Passive overlay contract, monitor-aware placement, Windows native click-through/client tracking, X11 XShape click-through/EWMH tracking, Wayland overlay gating, SharpHook keyboard input, and SDL3 controller input implemented; remaining plotters pending | Guardian live map/current-obelisk, followed-route Galaxy Map, next-jump intelligence, FSS/body/system survey, high-gravity warning, biology survey, compact genetic-sampler, Canonn prior-scan/radar, surface-history/tracker radar, ground-target, ground-combat, and massacre-mission overlays follow foreground Elite and close on unsafe states; all 30 legacy chords are editable and keyboard/controller events share focus-aware routing | Placement, capability, identity, route/jump/system-survey/biology/prior-scan/surface-radar/ground-target/combat state, hook, controller lifecycle, and chord tests passed; route, jump, FSS, body, system-status, biology survey, and compact sampler overlays were visually previewed in Blue dark/light, while high-gravity, prior-scan, surface-radar, ground-target, and combat visual QA is deferred to the final UI pass; no live Elite overlay or physical controller session run | Native libraries present in Linux publish; runtime not tested |
| Combat missions | Journal state preserves ground-CZ session kills/bonds and active massacre mission acceptance, completion/failure/abandonment, mission-list reconciliation, expiry, and legacy one-credit-per-mission-giver bounty behavior; massacre state uses the compatible commander profile | Persisted opt-in settings plus modern Raven-themed top-left `PlotFootCombat` and top-right `PlotMassacre` replacements implement the exact legacy altitude, settlement-war, vehicle, panel, and build-project suppression gates | All state, persistence, settings, passive preparation, binding, and XAML compilation checks passed; per request, visual/theme QA is held until the final whole-application pass and no live combat session was run | Not tested |
| Colonisation projects | Shipped build costs, construction depot state, commander Raven profile, project creation validation, aggregate commodity planning, Cargo/Market readers, carrier inventories, Raven cargo reconciliation/endpoints, and lossless API-key persistence implemented | Opt-in project workspace and creation review, depot progress, market/ship/FC shopping overlay, overlay preferences, and credential-gated linked-carrier sync implemented | Main workspace and shopping overlay visually checked in Blue dark; automated core/UI coverage passed; no external publish or live Elite overlay session was run | Not tested |
| Secondary features | Nearest systems, colonisation project forms, system notes, Commander Journeys, and followed routes are implemented; remaining rows are tracked in `docs/UI_PORTING_MATRIX.md` | Implemented slices are integrated into Search, Travel, and Colonisation; other secondary forms remain open | See per-feature rows and UI matrix | Not tested |
| Packaging/AppImage | Self-contained publish configured; no AppImage | Not applicable | `win-x64` publish passed | Cross-publish passed; runtime not tested |

Update this table only with evidence from the relevant exit gate.

## Latest validation evidence

Validation performed on 2026-07-25 using Windows build `10.0.26200` and .NET SDK
`10.0.103`:

- `dotnet build SrvSurvey.CrossPlatform.slnx --configuration Release`
  completed with zero warnings and zero errors.
- `dotnet test SrvSurvey.CrossPlatform.slnx --configuration Release --no-restore`
  passed all 666 tests: 426 Core tests and 240 Desktop tests.
- `dotnet format SrvSurvey.CrossPlatform.slnx --verify-no-changes` passed.
- Automated prior-scan coverage validates the live Canonn string-valued POI
  contract, malformed and mismatched responses, normalized body matching,
  catalog/value/ownership/analyzed/sample filtering, physical duplicate
  removal, distance/bearing/approach and near/far state, system response
  caching, retry backoff, persisted settings, and radar presentation state.
  Per request, no prior-scan window was opened; its visual/theme pass is held
  until the final whole-application UI review.
- Automated ground-target coverage validates all accepted legacy vehicle and
  panel modes, taxi/station/coordinate/body-radius rejection, persisted target
  lifecycle, bearing/descent presentation state, passive preparation, and
  bottom-center monitor placement. Its XAML compiled as part of the full test
  run; per request, the overlay was not opened and visual/theme QA remains held
  until the final whole-application UI review.
- Automated grounded-radar coverage validates lossless legacy surface history,
  bookmark and completed-scan updates under shared per-file locking, sample and
  touchdown journal mutations, tracker-removal preferences, exact legacy
  status/panel/landing-gear gating, Composition Scanner filters, complete
  cross-system death marking, all eight quick tracker actions, marker
  navigation, tracker-only presentation, five legacy window sizes, bounded
  zoom, passive preparation, Guardian arbitration, and XAML compilation. Per
  request, the overlay was not opened; its visual/theme pass remains held until
  the final whole-application UI review.
- Automated high-gravity warning coverage validates the persisted opt-in,
  configurable threshold, landable-body requirement, and exact
  supercruise/glide/flight/landed/SRV/fighter modes. Automated combat coverage
  validates war/civil-war settlement and 100 m gates, foot/SRV modes, mission
  acceptance/removal/reconciliation, expiry, bounty credit grouping,
  compatible profile round-tripping, active-project suppression, legacy
  top-corner placement, passive preparation, and XAML compilation. Per request,
  none of these windows was opened; their visual/theme pass remains held until
  the final whole-application UI review.
- The direct and transitive NuGet vulnerability audit reported no known
  vulnerable packages.
- Self-contained `win-x64` and `linux-x64` publish commands completed; the
  Linux publish was repeated after adding the X11 native adapters.
- The Windows development shell remained healthy during startup smoke tests with
  a deliberately missing journal directory and with the default live journal
  folder (455 journal files present), then was stopped after each check.
- The rebuilt 1180 by 760 Windows shell was visually inspected in Blue (dark)
  and Blue (light). Overview, Diagnostics, Settings, pending-feature disclosure,
  live theme switching, and the exposed accessibility tree were checked. All
  five Raven theme previews rendered; the runtime switch was exercised for the
  two blue variants.
- The current Windows build was launched against the default live journal
  folder after the Exobiology slice. Overview and Exobiology rendered at 1180
  by 760, and UI Automation exposed the navigation, Refresh, and Clear
  unclaimed actions. No active genetic-sampler event occurred during this smoke
  test.
- The Travel surface-navigation page was rendered in the current Windows build.
  Its editor and inactive guidance layout were visually checked, and UI
  Automation exposed both coordinate fields and all four target actions with
  the expected enabled/disabled state. No active surface target was changed.
- The Travel system-notes card and its resizable 720 by 600 secondary window
  were rendered against the live Facece journal context in Blue (dark) and
  Blue (light). Current system/address, empty-note state, topmost control,
  Canonn/Spansh/EDSM actions, cancel/save layout, and light/dark native controls
  were checked. The window was cancelled without changing live system data and
  the original Blue (dark) preference was restored.
- The Travel Commander Journeys card and resizable 1160 by 780 Journey workspace
  were exercised with an isolated QA journal/profile in Blue (dark) and Blue
  (light). Current-system and prior-system start modes, historical start lookup,
  begin, overview statistics, the visited-system detail/notes view, dirty and
  discard states, refresh status, and conclude/reprocess confirmations were
  checked. The exact QA commander records were removed afterward and the
  original Blue (dark) preference was restored; no live commander Journey data
  was changed. Active in-game updates and Linux behavior remain untested.
- The Travel followed-route card and resizable 1100 by 760 route workspace were
  exercised with an isolated QA journal/profile in Blue (dark) and Blue
  (light). Per-hop progress changed the next-system and dirty states, discard
  restored the persisted route, and distance, notes, neutron, and refuel
  guidance rendered correctly. The compact Galaxy Map overlay was visually
  previewed in both modes with destination and copied-system states. The
  temporary hook and exact QA files were removed, and Blue (dark) was restored;
  live Elite window attachment and Linux behavior remain untested.
- The Search spherical-limit page was rendered at 1180 by 760. A live Spansh
  lookup for Sol returned five matches, selected the exact system, and showed
  the expected 131.09 ly distance from the journal position for Facece. UI
  Automation exposed the center, matches, radius, enable, and disable controls;
  the live commander profile was not changed.
- The rebuilt Search page was rendered at 1182 by 790 after the Boxel slice. Its
  status card, generated-system input, mass-code and date options, skip/FSS
  rules, auto-copy option, expected-count editor, current-boxel actions, and
  full-area audit description, progress state, audit/cancel controls, and
  disabled inactive states were visually checked and exposed through UI
  Automation. No Boxel or audit action was invoked against the live commander
  profile.
- Automated Boxel coverage exercises legacy profile round-tripping, local
  discovery files, grouped empty-boxel files, paged Spansh responses,
  real generated-name/ID64 pairs, hand-authored system names, `NavRoute.json`
  hashing/updates, bootstrap replay suppression, live jump/FSS completion,
  Galaxy Map auto-copy, tree navigation, source merging, full-area completion,
  partial-result cancellation, and the large-audit confirmation guard.
- Automated nearby-system coverage exercises the exact Canonn nearest/POI and
  Spansh body-search shapes, biological-note summaries, malformed-response
  handling, five-unique-system limit, current-system/commander context,
  validation, row selection, clipboard actions, Canonn/Spansh links, and address
  resolution for Canonn results. Both conditional mode layouts were visually
  checked at 1182 by 790 in Blue (dark), with no live query or browser action.
- Automated Guardian coverage loads all 759 reference sites, all 13 map
  templates, and all 729 published surveys; reproduces every shipped survey
  percentage; preserves the duplicate `GS 80` identities and case-sensitive
  `P35` anomaly; reads current/legacy commander folders plus old/compact POI and
  obelisk formats; isolates malformed files; merges visits and completion; and
  exercises browser filters, distance ordering, profile loading, and clipboard
  behavior.
- The Guardian browser was rendered at 1182 by 790 against the live profile.
  Nearest-site ordering, all 759 rows, live filtering to the unique `GR 1`
  system address, selected-site details, scrolling, and full galactic/surface
  coordinate layout were visually checked. No refresh, clipboard, survey, or
  profile-changing action was invoked.
- Automated Ram Tah coverage validates the exact 101 Ancient Ruins and 28
  Guardian Logs identifiers and categories, both legacy mission-name aliases,
  journal Accepted/Completed/Failed/Abandoned and active-mission transitions,
  string/numeric status loading, lossless legacy-field persistence, manual
  checklist changes, and independent two-step resets.
- The Guardian Ram Tah workspace was rendered at 1182 by 790 against the live
  profile. Both mission cards, all ten legacy checklist categories, all 129
  controls, progress/status presentation, scrolling, guide actions, disabled
  empty reset states, and the status footer were visually checked without
  changing the live commander profile. UI Automation data was unavailable.
- Automated Guardian live coverage now includes multi-digit settlement indexes,
  travel clearing, lossless approach writes, all 13 finite map projections,
  survey-editor validation/round-tripping, retry-safe `Cargo.json` monitoring,
  all six Guardian artifact aliases and duplicate quantities, site-heading
  proximity, the legacy 25 m current-obelisk rule, scan persistence, and
  artifact-gated Ram Tah updates.
- The Guardian live scanner card was rendered at 1182 by 790 in Blue (dark) and
  Blue (light). Its inactive proximity, artifact, mission, and disabled scan
  states were visually checked and exposed through UI Automation. Blue (dark)
  was restored, and no commander survey or Ram Tah state was changed.
- Automated overlay coverage now includes Windows/X11/Wayland capability
  matrices, Windows and Wine/X11 Elite window identities, negative-coordinate
  multi-monitor placement, the Guardian overlay preparation state, and
  commander map projection. The Windows and X11 adapters are compiled into
  self-contained publish outputs, but have not yet been exercised over a live
  Elite window; Wayland remains intentionally disabled.
- Automated system-survey coverage now includes system transitions and stale
  events, FSS completion exclusions, scan/DSS valuation, detailed body fields,
  biological/geological progress, non-body filtering, legacy visibility modes,
  forced-toggle semantics, DSS candidate rules, settings round-tripping, and
  bottom-left placement. Both new overlays were visually checked together in
  Blue (dark) and Blue (light); the temporary preview hook and settings were
  removed.
- Automated biology-overlay coverage now includes persisted legacy defaults,
  system/map versus body/FSS selection, near-body targeting, known, partial,
  and exact predicted reward totals, first-footfall value, active samples,
  analyzed dimming, regional-first semantics, geological details, incomplete
  prediction context, and immediate prediction disablement. A journal-to-exact-
  species integration test exercises parent-star context and criteria matching.
  Dense body, three-body whole-system, and exact-prediction presentation were
  checked in Blue dark/light; the temporary preview hook and QA settings were
  removed.
- Automated prediction-workspace coverage now includes current-body filtering,
  expansion state, persisted compact/comfortable/large row sizing, exact and
  genus-only organism rows, system and first-footfall totals, sample separation,
  incomplete scan context, external-link routing, and single-instance opening.
  A three-body workspace was exercised at 1040 by 760 logical pixels (1042 by
  790 including the Windows frame) in Blue dark/light. Large rows, current-body
  focus, expand all, and collapse all rendered and behaved correctly; the
  temporary preview hook and isolated settings were removed.
- Automated compact-biology-status coverage now includes its separate persisted
  auto-show preference, supported flight/focus modes, DSS guidance, genus and
  geology rows, analyzed and active progress, stale samples from another body,
  two-sample separation distance/remaining distance, and first-footfall reward.
  Active-sampler, no-active-sample summary, and DSS-required states rendered
  cleanly at 480 pixels wide in Blue dark/light. Its top-center lifecycle uses
  the shared passive window and yields to jump and Guardian overlays; the
  temporary preview hook and isolated settings were removed.
- Automated biology-Codex coverage now includes all 1,070 reference entries,
  biological filtering, commander discovery states, exact predictions, body and
  entry navigation, temperature criteria, rewards, sample separation, external
  actions, image validation, atomic caching, size limits, and download timeout.
  The single-instance `FormShowCodex` replacement was exercised with an isolated
  exact Aleoida prediction and a real cached Canonn reference image in Blue
  dark/light. Fit, wheel zoom, drag pan, credit, and disabled navigation states
  rendered and behaved correctly; the isolated profile/cache entry was removed
  and Blue dark restored. Live Elite updates, remote image-failure presentation,
  and Linux remain untested.
- Automated Codex-Bingo coverage validates the 1,070-entry hierarchy, all 42
  galactic regions, commander/region ledgers, safe manual overrides, live and
  bounded historical journal ingestion, Canonn Challenge response mapping,
  idempotent imports, location resolution, progress/selection actions, and
  nearest-search inputs. The single-instance `FormCodexBingo` replacement was
  exercised with an isolated three-entry Sol ledger in Blue (dark) and Blue
  (light). Tree expansion, selected-node details, state retention, progress,
  and disabled actions rendered correctly; the exact preview ledgers were
  removed and Blue (dark) restored. Interactive remote imports, live Elite
  updates, and Linux remain untested.
- Automated global-input coverage now validates all 30 legacy action names and
  default chords, keyboard formatting/routing and text-entry suppression,
  controller button/trigger/eight-way POV chords, first-release dispatch,
  disconnect clearing, focus/Elite gating, device changes, and settings
  persistence. The real Windows SharpHook service reached its active state.
  SDL3 initialized and enumerated successfully with no controller connected;
  physical button/HOTAS input has not been exercised on this machine.
- Framework-dependent `win-x64` and `linux-x64` publishes include the expected
  `SDL3.dll`/`libSDL3.so` and `uiohook.dll`/`libuiohook.so` native libraries.
  The Settings input cards, all 30 binding editors, and the SDL device picker
  were visually/accessibility checked in the saved Blue (light) theme.
- Automated Colonisation coverage now includes the shipped build catalog,
  construction journal state, project validation/creation, commander Fleet
  Carriers, ship cargo capacity, aggregate commodity allocation, local Market
  guidance and freshness, Raven project/cargo endpoints, credential persistence,
  exact market reconciliation, consent gating, and pending sync state. The main
  workspace and detached shopping overlay were visually checked without
  publishing commander or carrier data.
- The workflow YAML and `global.json` parsed successfully.

Not validated in this environment:

- A hosted GitHub Actions run of the new workflow.
- The Dockerfile (`docker` is not installed on the validation machine).
- Linux X11 or Wayland startup, live journals, overlays, input, or AppImage.
- A live Elite Dangerous session or parity comparison with real commander data.
- Physical gamepad, joystick, or HOTAS input and reconnect behavior.
- Minimum-width, OS high-contrast, and non-blue runtime theme rendering.

## Review and commit strategy

Each commit should be independently buildable where possible and cover one
reviewable concern: documentation/status, repository portability, core behavior
plus tests, a UI vertical slice, or build/release automation. Avoid commits that
mix generated formatting, binary assets, domain behavior, and CI changes.

## Open decisions

- Minimum supported Windows version and Linux distributions.
- Whether Wayland with reduced overlay/input capabilities is a supported mode.
- Whether VR remains a Windows-only optional component.
- Supported in-place data locations versus explicit import.
- Upgrade path from the legacy `.NET 9` application to the supported .NET target.
- Release signing and ownership of Windows Store/AppImage publication.
