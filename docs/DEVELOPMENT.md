# Development and Validation

Last updated: 2026-08-16

## Branch purpose

`SrvSurvey-Avalonia` is the standalone converted application branch. It builds
without the WinForms project, Windows Application Packaging Project, helper
executables, or source-comparison tree. The pre-cleanup implementation and its
full porting audit remain recoverable on `cross-platform-development`.

## Current release candidate

The branch is versioned as **SrvSurvey-XP 2.1.3.0-rc.32**. Its development tag
is `xp-v2.1.3.0-rc.32`, package manifests use `SrvSurvey.XP`, and distributable
filenames begin with `SrvSurvey-XP-2.1.3.0-rc.32`. The assembly `FileVersion`
remains numeric at `2.1.3.0` for Windows compatibility.

## Build contract

The supported solution is `SrvSurvey.slnx` and requires the .NET
10 SDK. Release validation uses:

```console
dotnet restore SrvSurvey.slnx
dotnet build SrvSurvey.slnx --configuration Release --no-restore
dotnet test SrvSurvey.slnx --configuration Release --no-build --no-restore
```

The Docker build runs the same solution build and test before exporting a
self-contained `linux-x64` publish directory. GitHub Actions additionally
creates checksum-indexed Windows and Linux archives, SPDX SBOMs, and an AppImage
validated for metadata, native dependency closure, extraction, and isolated
XWayland startup.

## Regression contract

- Journal coverage is a checked-in 74-event inventory. Each event needs a
  production consumer and event-specific assertions.
- Network coverage inventories all runtime surfaces and every `HttpClient`
  owner, including bounded streaming requirements.
- Overlay coverage inventories all 28 supported overlay contracts and requires
  production markup plus assertion evidence.
- Profile import remains backup-first, hash-verified, staged, and recoverable.
  Compatibility code and tests are part of the converted product, not a build
  dependency on the previous implementation.

## Upstream parity baseline

The latest source comparison was completed on 2026-08-02 against upstream
commit `b592d991daa035ddda6682be52f3e55791c6ab29`. The runtime changes from the
prior `c8068866db8fc98061922a391922b74842b6cef3` baseline remain covered: its
organic-scan recovery uses the Avalonia system reducer's body lookup/creation
path, and its colonisation shopping-overlay correction classifies an untracked
Fleet Carrier from the journal's `StationType` value plus the complete
commander-linked Raven carrier inventory. Station economy is not used as a
Fleet Carrier proxy. The only later upstream additions are two Eunostus
documentation images, not application behavior. Focused planner and
presentation tests lock the runtime behavior.

Because this branch intentionally excludes the previous application source,
future upstream commits must receive an explicit delta review rather than being
assumed covered by the standalone journal, network, and overlay inventories.

## Runtime verification

Automated builds do not replace native testing with a live Elite Dangerous
session. Before promoting a release, verify on clean supported systems:

1. Windows portable startup, upgrade, rollback, and removal.
2. Linux AppImage startup on native X11 and XWayland.
3. Journal attachment, game-window tracking, click-through overlays, global
   input, capture-dependent features, and overlay update cost during play.
4. Backup/import/restart using a representative existing profile.

Overlay runtime testing must cover both presentation backends. Leave
`SRVSURVEY_OVERLAY_HOST` unset to verify the established separate-window path
on Windows and ordinary X11/XWayland. Set it to `combined` before process
startup to exercise the shared host; set it to `separate` to verify the
fallback. For the combined host, verify dynamic-height panels, per-panel and
global opacity and scale, stream/OpenVR projection, edit-preview suppression,
live drag, transparent-gap click-through, game resize, and mixed-DPI monitor
movement. Route-specific validation must also exercise the shared route-body
preview/live presentation, body completion and scrolling while interaction is
enabled, Fleet Carrier route progression, and every carrier countdown phase.

Record release-specific runtime results in the release notes or issue tracker;
do not reintroduce the previous application source as a test oracle.
