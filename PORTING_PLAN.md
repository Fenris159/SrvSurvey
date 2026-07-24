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
| Application shell | Not applicable | Modern navigation plus Overview, Exploration, Exobiology, Travel, Diagnostics, Settings, and explicit pending states | Blue dark/light shell plus current Overview, Exobiology, and Travel visual/accessibility smoke passed; Exploration page needs current visual recheck | Not tested |
| Journal folder discovery | Implemented; 3 tests | Paths and errors shown | Missing and default paths smoke-tested | Not tested |
| Journal ingestion/state | Retrying status reader plus polling journal append/partial-line/rotation monitor; shared bootstrap/live reducer | Overview and Diagnostics projections update live | Earlier bootstrap state inspected; current live monitor not exercised with Elite | Not tested |
| Raven shell themes | Five definitions; 11 desktop tests cover themes, persistence, and shell navigation | Five-theme gallery and runtime switching | Blue dark/light switched and inspected | Not tested |
| Settings/data migration | OS paths, legacy discovery, manifests, verified backup/staging/import, and lossless commander profile updates implemented | Explicit backup-and-import workflow in Settings | Automated only; real profile restart comparison not run | Not tested |
| Exploration totals | Legacy valuation and six counters plus compatible, atomic profile persistence implemented | Live Overview/Exploration projections and two-step reset | Automated only | Not tested |
| Organic scans | Codex reference, three-sample state, surface separation, first-footfall reward, sale, death, reset, and compatible profile fields implemented; system/body organism history remains | Live Overview/Exobiology projections and two-step unclaimed reset; predictions/Codex remain pending | Empty/live-profile page visually and accessibility checked; active Elite sampling not run | Not tested |
| Ground target tracking | Legacy coordinate parsing plus cardinal formats, validated settings, great-circle distance/bearing, relative heading, and approach bands implemented | Travel target editor supports typed, current, clipboard, clear, and live guidance; overlay remains pending | Inactive/live-profile page visually and accessibility checked; active surface guidance not run | Not tested |
| Guardian surveys | Not implemented | Not implemented | Not tested | Not tested |
| Overlays/input | Not implemented | Not implemented | Not tested | Not tested |
| Secondary features | Not implemented | Not implemented | Not tested | Not tested |
| Packaging/AppImage | Self-contained publish configured; no AppImage | Not applicable | `win-x64` publish passed | Cross-publish passed; runtime not tested |

Update this table only with evidence from the relevant exit gate.

## Latest validation evidence

Validation performed on 2026-07-24 using Windows build `10.0.26200` and .NET SDK
`10.0.103`:

- `dotnet build SrvSurvey.CrossPlatform.slnx --configuration Release`
  completed with zero warnings and zero errors.
- `dotnet test SrvSurvey.CrossPlatform.slnx --configuration Release` passed all
  75 tests: 58 Core tests and 17 Desktop tests.
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
