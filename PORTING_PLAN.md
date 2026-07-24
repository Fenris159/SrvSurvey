# SrvSurvey cross-platform porting plan

Last validated: 2026-07-24

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
| Global keyboard/controller input | Required | Validate replacement | Portal/compositor dependent |
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

- [ ] Implement Avalonia overlay primitives, scaling, theme, and multi-monitor
  coordinates.
- [ ] Add platform adapters for topmost/click-through behavior and game-window
  tracking.
- [ ] Replace SharpDX input with maintained APIs and preserve configurable
  bindings.
- [ ] Define behavior for unsupported Wayland capabilities.
- [ ] Measure overlay update cost while the game is active.

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
- [ ] Human settlements and post-processing tools.
- [ ] Cargo, missions, massacre/foot combat, and colonization projects.
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
| Application shell | Not applicable | Modern navigation plus Overview, Exploration, Exobiology, Travel, Search, Diagnostics, Settings, and explicit pending states | Blue dark/light shell plus current Overview, Exobiology, Travel, and Search visual/accessibility smoke passed; Exploration page needs current visual recheck | Not tested |
| Journal folder discovery | Implemented; 3 tests | Paths and errors shown | Missing and default paths smoke-tested | Not tested |
| Journal ingestion/state | Retrying status/cargo readers plus polling journal append/partial-line/rotation and `Status.json`/`Cargo.json`/`NavRoute.json` change monitor; shared bootstrap/live reducer | Overview and Diagnostics projections update live; cargo changes feed Guardian artifact state | Earlier bootstrap state and cargo-backed inactive Guardian state inspected; current live monitor not exercised with Elite | Not tested |
| Raven shell themes | Five definitions; 11 desktop tests cover themes, persistence, and shell navigation | Five-theme gallery and runtime switching | Blue dark/light switched and inspected | Not tested |
| Settings/data migration | OS paths, legacy discovery, manifests, verified backup/staging/import, and lossless commander profile updates implemented | Explicit backup-and-import workflow in Settings | Automated only; real profile restart comparison not run | Not tested |
| Exploration totals | Legacy valuation and six counters plus compatible, atomic profile persistence implemented | Live Overview/Exploration projections and two-step reset | Automated only | Not tested |
| Organic scans | Codex reference, three-sample state, surface separation, first-footfall reward, sale, death, reset, and compatible profile fields implemented; system/body organism history remains | Live Overview/Exobiology projections and two-step unclaimed reset; predictions/Codex remain pending | Empty/live-profile page visually and accessibility checked; active Elite sampling not run | Not tested |
| Ground target tracking | Legacy coordinate parsing plus cardinal formats, validated settings, great-circle distance/bearing, relative heading, and approach bands implemented | Travel target editor supports typed, current, clipboard, clear, and live guidance; overlay remains pending | Inactive/live-profile page visually and accessibility checked; active surface guidance not run | Not tested |
| Spherical search limit | Legacy-compatible center/radius state, strict boundary rule, lossless commander persistence, journal galactic position, and current Spansh response contract implemented | Search supports center lookup and selection, 1–1000 ly radius, enable/disable, and current-system distance/result; `PlotSphericalSearch` remains pending | Live profile page and Sol lookup visually/accessibility checked; save action enabled but profile was not changed | Not tested |
| Boxel search | Generated-name hierarchy, ID64 decoding for generated and hand-authored names, completion/skip rules, lossless commander fields, legacy local-system and empty-boxel files, paged Spansh search, live `NavRoute.json`/journal updates, and cancellable full-area completion audit with partial results implemented | Search supports activation, mass-code/date options, current/parent/sibling/child navigation, expected counts, manual completion, empty marking, current-system highlighting, refresh, manual/Galaxy Map clipboard copy, audit progress/cancellation, and a large-audit confirmation guard | Inactive live-profile page and audit controls visually and accessibility checked at 1182 by 790; automated ID64, source-merge, live-completion, audit, cancellation, and confirmation integration passed; no search/profile/audit action was invoked | Not tested |
| Guardian surveys | All 759 shipped sites, 13 map templates, 729 published surveys, exact completion scoring, duplicate-ID/full-body matching, legacy/current commander files, compact/old POI and obelisk formats, visits, notes, beacon locations, live site transitions/writes, native map projection, lossless survey editing, cargo artifact aliases, 25 m obelisk proximity, scan persistence, and both Ram Tah missions implemented; detached overlay/platform behavior remains | Browser, clipboard actions, native maps, survey editor, live-site/scanner cards, artifact requirements, scan toggle, and both complete Ram Tah workspaces implemented; advanced map authoring and detached overlays remain | Live-profile browser, maps/editor, Ram Tah tabs, and inactive scanner card visually checked at 1182 by 790; scanner checked in Blue dark/light without changing commander state; active in-game proximity not run | Not tested |
| Overlays/input | Not implemented | Not implemented | Not tested | Not tested |
| Secondary features | Not implemented | Not implemented | Not tested | Not tested |
| Packaging/AppImage | Self-contained publish configured; no AppImage | Not applicable | `win-x64` publish passed | Cross-publish passed; runtime not tested |

Update this table only with evidence from the relevant exit gate.

## Latest validation evidence

Validation performed on 2026-07-24 using Windows build `10.0.26200` and .NET SDK
`10.0.103`:

- `dotnet build SrvSurvey.CrossPlatform.slnx --configuration Release`
  completed with zero warnings and zero errors.
- `dotnet test SrvSurvey.CrossPlatform.slnx --configuration Release --no-restore`
  passed all 226 tests: 184 Core tests and 42 Desktop tests.
- `dotnet format SrvSurvey.CrossPlatform.slnx --verify-no-changes` passed.
- The direct and transitive NuGet vulnerability audit reported no known
  vulnerable packages.
- Self-contained `win-x64` and `linux-x64` publish commands completed after the
  shell and theme changes.
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
- The workflow YAML and `global.json` parsed successfully.

Not validated in this environment:

- A hosted GitHub Actions run of the new workflow.
- The Dockerfile (`docker` is not installed on the validation machine).
- Linux X11 or Wayland startup, live journals, overlays, input, or AppImage.
- A live Elite Dangerous session or parity comparison with real commander data.
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
