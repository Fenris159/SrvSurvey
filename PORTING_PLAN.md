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
- [ ] Remove case-colliding repository paths.
- [ ] Create buildable Core, Desktop, and test projects.
- [ ] Add a cross-platform solution that excludes Windows packaging projects.
- [ ] Add Windows and Linux CI restore, build, test, and publish smoke checks.
- [ ] Make the Dockerfile a reproducible Linux build environment, or remove it.

Exit gate: a clean clone restores, builds, tests, and publishes on Windows and
Linux. Launching the desktop shell is manually smoke-tested on both platforms.

### Phase 1 — Journal bootstrap vertical slice

- [ ] Resolve the journal folder from an explicit setting first.
- [ ] Offer platform-specific candidate locations without assuming one exists.
- [ ] Read the newest journal safely, including an incomplete final line.
- [ ] Reproduce commander, game version, mode, system, body, and shutdown state.
- [ ] Watch journal rotation and `Status.json` with cancellation and retry logic.
- [ ] Show the state and any path/parse errors in the desktop shell.

Exit gate: shared fixtures and a live journal session produce the expected state
on Windows and Linux.

### Phase 2 — Settings, commander data, and resources

- [ ] Inventory every file under the current versioned data folder.
- [ ] Define OS-appropriate config/data/cache locations.
- [ ] Implement a backup-first legacy data importer.
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
| Application shell | Not applicable | Not implemented | Not tested | Not tested |
| Journal folder discovery | Not implemented | Not implemented | Not tested | Not tested |
| Journal ingestion/state | Not implemented | Not implemented | Not tested | Not tested |
| Settings/data migration | Not implemented | Not implemented | Not tested | Not tested |
| Organic scans | Not implemented | Not implemented | Not tested | Not tested |
| Ground target tracking | Not implemented | Not implemented | Not tested | Not tested |
| Guardian surveys | Not implemented | Not implemented | Not tested | Not tested |
| Overlays/input | Not implemented | Not implemented | Not tested | Not tested |
| Secondary features | Not implemented | Not implemented | Not tested | Not tested |
| Packaging/AppImage | Not implemented | Not applicable | Not tested | Not tested |

Update this table only with evidence from the relevant exit gate.

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
