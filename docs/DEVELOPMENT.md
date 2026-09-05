# Development and Validation

Last updated: 2026-09-05

## Branch purpose

`SrvSurvey-Avalonia` is the standalone converted application branch. It builds
without the WinForms project, Windows Application Packaging Project, helper
executables, or source-comparison tree. The pre-cleanup implementation and its
full porting audit remain recoverable on `cross-platform-development`.

## Current release candidate

The branch is versioned as **SrvSurvey-XP 2.1.3.0-rc.43**. Its development tag
is `xp-v2.1.3.0-rc.43`, package manifests use `SrvSurvey.XP`, and distributable
filenames begin with `SrvSurvey-XP-2.1.3.0-rc.43`. The assembly `FileVersion`
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
- Overlay coverage inventories 32 contracts, including 30 positionable panels, and requires
  production markup plus assertion evidence.
- Profile import remains backup-first, hash-verified, staged, and recoverable.
  Compatibility code and tests are part of the converted product, not a build
  dependency on the previous implementation.

## Upstream parity baseline

The latest source comparison was completed on 2026-08-23 against upstream
commit `b9cac22183f00d846fbbaca4c47a40d1677532c4`. Of the last five upstream
commits, the port absorbed the Guardian survey publication, Gamma T9 template
correction, and screenshot game-client-width correction. The WinForms
`FormMultiFloatie` static-instance cleanup has no corresponding static form in
the port. Preferred-commander startup and API-key management already use the
port's profile/settings workflows. The Guardian body-radius null guard is
already present in the proximity path, and biology predictions publish
method-local snapshot arrays rather than enumerating a shared mutable
collection. Focused data and screenshot tests lock the absorbed behavior; the
Guardian backend contract is recorded in
[`GUARDIAN_SURVEY_PARITY.md`](GUARDIAN_SURVEY_PARITY.md).

An additional delta review on 2026-09-05 covers upstream
[#1051](https://github.com/njthomson/SrvSurvey/pull/1051), merged as
`347846175ad531b68d0ce797a08d375483cefc10`. The port now keeps Fileheader
galaxy classification separate from LoadGame expansion flags throughout
profile selection, journey context, scan rewards, and network options.
EDDN retains nullable expansion flags and sends known `false` values; it does
not copy the legacy patch's truthiness filter. Inara already guards absent
commander identity, excludes Legacy uploads, and ignores object-valued
`Statistics.Multicrew`. Regression coverage includes Live Horizons profiles,
exploration/journey/system/boxel rewards, and EDDN session flag resets. This
targeted review does not advance the broader upstream baseline above.

A further delta review on 2026-09-05 covers upstream
[#1055](https://github.com/njthomson/SrvSurvey/pull/1055), merged as
`91e07f84b98f658fe662fe2d89cf44ff9ac59dce`. Rhino geometry and rig tracking
are adapted into a dedicated, theme-aware Surface mining panel, with the
existing Surface Survey panels suppressed while operating the Rhino or returning
to it on foot. The vehicle row provides separate Ship and Rhino guidance, with
an untracked X for the Rhino while aboard.
Demolished RavenColonial sites are recognized by the API model; the existing
Plan-only project picker excludes them. Unknown Guardian sites already use
nullable catalog/profile handling and site-type guidance in this port.
Spansh Fleet Carrier routes are deliberately excluded from this delta because
the port has its own implementation. Details and regression evidence are in
[`RHINO_MINING_PARITY.md`](RHINO_MINING_PARITY.md).

Because this branch intentionally excludes the previous application source,
future upstream commits must receive an explicit delta review rather than being
assumed covered by the standalone journal, network, and overlay inventories.

## Runtime verification

RC43 local validation on 2026-09-05 passed the Release solution build with
zero warnings/errors, formatting verification, and 3,233 tests (Core 1,423;
Desktop 1,797; Replay Controller 13). Guide search and automatic rig cleanup
on ship boarding are covered. Production-template previews were
inspected for the mining layout and Monochrome Companion palette. This records
automated and headless validation; the native checks below remain separate.

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
