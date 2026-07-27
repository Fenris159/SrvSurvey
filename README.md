# SrvSurvey Avalonia

SrvSurvey Avalonia is a cross-platform Elite Dangerous companion application
for Windows and Linux. It reads the game's journal and auxiliary files to drive
exploration, exobiology, travel, Guardian, settlement, combat, quest,
colonization, notification, and in-game overlay features.

This branch contains only the converted Avalonia application, its shared core,
tests, packaging, and development tools. Existing SrvSurvey profiles remain
supported through a verified, backup-first import; the imported source is never
modified.

## Install

Download a Windows or Linux release artifact from the
[manual release workflow](https://github.com/Fenris159/SrvSurvey/actions/workflows/manual-release-packages.yml),
then follow the platform guide:

- [Windows portable installation](docs/INSTALL_WINDOWS.md)
- [Linux AppImage and portable installation](docs/INSTALL_LINUX.md)

Keep every downloaded package in its extracted container folder. The executable
and its accompanying files form one self-contained installation and should not
be separated.

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
[journal coverage](docs/JOURNAL_COVERAGE.md),
[network coverage](docs/NETWORK_COVERAGE.md), and
[profile migration](docs/DATA_MIGRATION.md).

## Feedback

Report defects or suggestions through the
[Fenris159/SrvSurvey issue tracker](https://github.com/Fenris159/SrvSurvey/issues).

SrvSurvey is an independent third-party application and is not affiliated with
Frontier Developments.
