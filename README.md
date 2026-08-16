# SrvSurvey-XP Cross-Platform

Current release candidate version: **2.1.3.0-rc.30**

This build of SrvSurvey is a cross-platform version of the Elite Dangerous companion application
for Windows and now Linux. It reads the game's journal and auxiliary files to drive
exploration, exobiology, travel, Guardian, settlement, combat, quest,
colonization, notification, and in-game overlay features.

This branch contains only the converted Avalonia application, its shared core,
tests, packaging, and development tools. Existing SrvSurvey profiles remain
supported through a verified, backup-first import; the imported source is never
modified.

## Install

Download the Windows or Linux SrvSurvey-XP package from the
[repository releases](https://github.com/Fenris159/SrvSurvey/releases),
then follow the platform guide:

- [Windows portable installation](docs/INSTALL_WINDOWS.md)
- [Linux AppImage and portable installation](docs/INSTALL_LINUX.md)

Keep every downloaded package in its extracted container folder. The executable
and its accompanying files form one self-contained installation and should not
be separated.

The application follows XP development releases from this repository by
default. Diagnostics can opt out to the stable XP feed in
`njthomson/SrvSurvey`; that feed reports N/A until an upstream XP release is
published.

## Build and run

Install the .NET 10 SDK, then run:

```console
dotnet restore SrvSurvey.slnx
dotnet build SrvSurvey.slnx --configuration Release --no-restore
dotnet test SrvSurvey.slnx --configuration Release --no-build --no-restore
dotnet run --project src/SrvSurvey.Desktop/SrvSurvey.Desktop.csproj
```

Journal discovery can be overridden with `SRVSURVEY_JOURNAL_DIR` or the command
line:

```console
dotnet run --project src/SrvSurvey.Desktop/SrvSurvey.Desktop.csproj -- --journal-directory "/path/to/journals"
```

Linux overlays support native X11 and XWayland. A Wayland desktop must expose an
X11 `DISPLAY` through XWayland for game-window tracking, click-through overlays,
screen capture, and global input. Pure native Wayland is not a full-functionality
target for this build.

## Repository layout

- `src/SrvSurvey.Core` — portable state, storage, migration, journal, network,
  update, and domain services, including application-owned reference resources.
- `src/SrvSurvey.Desktop` — Avalonia UI, overlays, theming, input, and native
  platform adapters.
- `tests` — core and desktop regression suites with explicit journal, network,
  and overlay coverage inventories.
- `packaging` and `scripts` — Windows/Linux packaging and AppImage validation.
- `tools` — current development utilities, including localization extraction.

See [development and validation status](docs/DEVELOPMENT.md),
[current release notes](CurrentReleaseNotes.md),
[journal coverage](docs/JOURNAL_COVERAGE.md),
[network coverage](docs/NETWORK_COVERAGE.md),
[network publication privacy](docs/PRIVACY.md), and
[profile migration](docs/DATA_MIGRATION.md). Frontier account linking and the
commander dashboard are documented in [Frontier account linking](docs/FRONTIER.md).
Bundled third-party artwork is identified in
[third-party notices](THIRD-PARTY-NOTICES.md).

## Feedback

Report defects or suggestions through the
[Fenris159/SrvSurvey issue tracker](https://github.com/Fenris159/SrvSurvey/issues).

SrvSurvey is an independent third-party application and is not affiliated with
Frontier Developments.
