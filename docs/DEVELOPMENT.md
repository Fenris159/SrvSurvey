# Development and Validation

Last updated: 2026-09-25

## Branch purpose

`SrvSurvey-Avalonia` is the standalone converted application branch. It builds
without the WinForms project, Windows Application Packaging Project, helper
executables, or source-comparison tree. The pre-cleanup implementation and its
full porting audit remain recoverable on `cross-platform-development`.

## Current release candidate

The branch is versioned as **SrvSurvey-XP 2.1.3.0-rc.56**. Its development tag
is `xp2-v2.1.3.0-rc.56`, package manifests use `SrvSurvey.XP`, and distributable
filenames begin with `SrvSurvey-XP-2.1.3.0-rc.56`. The assembly
`FileVersion` remains numeric at `2.1.3.0` for Windows compatibility.

RC51 is the permanent legacy update anchor. Its release index is schema 1 and
contains only `win-x64` and `linux-x64`, even though the workflow also publishes
the AppImage and `.zsync` assets. The application in RC51 reads both schema 1
and schema 2 and scans both `xp-v` and `xp2-v` tags. RC52 and every later build
must use `xp2-v` and schema 2. Never publish a tag above RC51 with the `xp-v`
prefix: pre-RC51 clients choose the highest `xp-v` version before parsing the
index, so such a tag would permanently shadow the bridge.

## Build contract

The supported solution is `SrvSurvey.slnx` and requires the .NET
10 SDK. Release validation uses:

```console
dotnet tool restore
dotnet restore SrvSurvey.slnx
pwsh ./tools/Test-ChangedCodeQuality.ps1
dotnet build SrvSurvey.slnx --configuration Release --no-restore
dotnet test SrvSurvey.slnx --configuration Release --no-build --no-restore
```

`Test-ChangedCodeQuality.ps1` runs the same pre-build gates as CI: CSharpier,
Avalonia localization catalog verification, the `.editorconfig` type-style
rules and `tools/SonarCloud.ruleset` profile on changed C# lines, then
Coverlet OpenCover against the mapped test projects so changed production
lines/branches stay at SonarCloud's 80% new-code coverage minimum. Run
`dotnet csharpier format .` to format C# before committing. When user-facing
desktop text or Desktop source files change, regenerate catalogs with
`./tools/Generate-AvaloniaLocalization.ps1` (add `-TranslateMissing` for new
strings); the verification gate rejects stale catalogs.

Install the versioned git hook once per clone so those gates also block commit:

```console
pwsh ./tools/Install-LocalGitHooks.ps1
```

That sets local `core.hooksPath` to `.githooks`, where `pre-commit` restores
tools/solution packages and runs `Test-ChangedCodeQuality.ps1`. Use
`pwsh ./tools/Install-LocalGitHooks.ps1 -Uninstall` to remove the local hook
path.

`SonarAnalyzer.CSharp` is referenced centrally by `Directory.Build.props`, so
it runs in editors and every `dotnet build`. Analyzer warnings are treated as
build errors. Rule-specific exceptions belong in `.editorconfig` and require a
short rationale. `tools/SonarCloud.ruleset` mirrors the current SonarCloud C#
quality profile; `Test-ChangedCodeQuality.ps1` enables that full profile and
reports only findings on changed lines so existing legacy findings do not block
unrelated work. It also checks null-guarded event subscriptions explicitly
because SonarCloud can recognize newer null-conditional assignment syntax
before the installed SDK analyzer does.

The desktop language selector supports English, German, Spanish, French,
Brazilian Portuguese, Russian, Simplified Chinese, and pseudo-localization.

The Docker build runs the same solution build and test before exporting a
self-contained `linux-x64` publish directory. GitHub Actions additionally
creates checksum-indexed Windows and Linux archives, SPDX SBOMs, and an AppImage
validated for metadata, native dependency closure, extraction, and isolated
XWayland startup. Release AppImages are built on Ubuntu 24.04 to preserve their
native compatibility baseline, then the exact packaged artifact must pass the
same dependency and startup validation on Ubuntu 26.04 before publication.

## RC56 release sequence

RC51 remains the permanent legacy bridge and RC55 is published. Do not
dispatch RC56 until PR #160 and PR #164 have merged into
`SrvSurvey-Avalonia` and the external publishing step has been explicitly
approved. Then update the local branch and verify the checked-in release
contract before starting the workflow:

```console
git switch SrvSurvey-Avalonia
git pull --ff-only origin SrvSurvey-Avalonia
pwsh ./scripts/Resolve-CrossPlatformReleaseContract.ps1 -Version 2.1.3.0-rc.56
gh workflow run build-srvsurvey-xp.yml \
  --repo Fenris159/SrvSurvey \
  --ref SrvSurvey-Avalonia \
  -f source_ref=SrvSurvey-Avalonia \
  -f release_channel=development
run_id=$(gh run list \
  --repo Fenris159/SrvSurvey \
  --workflow build-srvsurvey-xp.yml \
  --event workflow_dispatch \
  --limit 1 \
  --json databaseId \
  --jq '.[0].databaseId')
gh run watch "$run_id" --repo Fenris159/SrvSurvey --exit-status
```

The resolver output must report `xp2-v2.1.3.0-rc.56` and schema 2. After the
workflow succeeds, verify rather than replace its published assets:

```console
gh release view xp2-v2.1.3.0-rc.56 \
  --repo Fenris159/SrvSurvey \
  --json tagName,isDraft,isPrerelease,targetCommitish,assets
gh release download xp2-v2.1.3.0-rc.56 \
  --repo Fenris159/SrvSurvey \
  --pattern release-index.json \
  --dir artifacts/verify-rc56
pwsh -Command '$index = Get-Content artifacts/verify-rc56/release-index.json -Raw | ConvertFrom-Json; if ($index.schemaVersion -ne 2 -or $index.packages.Count -ne 3) { throw "RC56 release index contract failed." }'
```

Confirm that legacy clients still select `xp-v2.1.3.0-rc.51` and an RC51 client
selects `xp2-v2.1.3.0-rc.56`. Continue sequential RC version increments in the
`xp2-v` namespace; do not create any additional `xp-v` tag.

## Regression contract

- Journal coverage is a checked-in 74-event inventory. Each event needs a
  production consumer and event-specific assertions.
- Network coverage inventories all runtime surfaces and every `HttpClient`
  owner, including bounded streaming requirements.
- Overlay coverage inventories 33 contracts, including 31 positionable panels, and requires
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
zero warnings/errors, formatting verification, and 3,243 tests (Core 1,423;
Desktop 1,807; Replay Controller 13). Guide search and automatic rig cleanup
on ship boarding are covered. Sidebar collapse/restore preserves workspace
selection and window size in dark/light themes and at increased application scale.
Named-resource coverage includes chat bookmarks, live bearings/distances,
near/far cues, rig isolation, and two-column layouts with 3, 7, and 21 targets.
The joined stream overlay's focus-loss and tracker-override regressions pass.
Production-template previews were inspected for the sidebar, mining layout,
and Monochrome Companion palette. This records
automated and headless validation; the native checks below remain separate.

Automated builds do not replace native testing with a live Elite Dangerous
session. Before promoting a release, verify on clean supported systems:

1. Windows portable startup, upgrade, rollback, and removal.
2. Linux AppImage startup on native X11 and XWayland. On Wayland, force an X11
   capture failure and verify the ScreenCast picker, Elite-window selection,
   restored selection, cancellation message, the Settings capture-source reset,
   capture diagnostics, and the rig-calibration **Test** path.
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
