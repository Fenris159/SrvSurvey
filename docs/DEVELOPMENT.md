# Development and Validation

Last updated: 2026-07-30

## Branch purpose

`SrvSurvey-Avalonia` is the standalone converted application branch. It builds
without the WinForms project, Windows Application Packaging Project, helper
executables, or source-comparison tree. The pre-cleanup implementation and its
full porting audit remain recoverable on `cross-platform-development`.

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

- Journal coverage is a checked-in 68-event inventory. Each event needs a
  production consumer and event-specific assertions.
- Network coverage inventories all runtime surfaces and every `HttpClient`
  owner, including bounded streaming requirements.
- Overlay coverage inventories all 22 supported overlay contracts and requires
  production markup plus assertion evidence.
- Profile import remains backup-first, hash-verified, staged, and recoverable.
  Compatibility code and tests are part of the converted product, not a build
  dependency on the previous implementation.

## Upstream parity baseline

The latest source comparison was completed on 2026-07-30 against upstream
commit `c8068866db8fc98061922a391922b74842b6cef3`. Its organic-scan recovery is
covered by the Avalonia system reducer's body lookup/creation path. Its
colonisation shopping-overlay correction is preserved by classifying an
untracked Fleet Carrier from the journal's `StationType` value and the complete
commander-linked Raven carrier inventory; station economy is not used as a
Fleet Carrier proxy. Focused planner and presentation tests lock this behavior.

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
global opacity, stream/OpenVR projection, edit-preview suppression, live drag,
transparent-gap click-through, game resize, and mixed-DPI monitor movement.

Record release-specific runtime results in the release notes or issue tracker;
do not reintroduce the previous application source as a test oracle.
