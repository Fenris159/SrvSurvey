# SrvSurvey

SrvSurvey is an independent third-party application for use with [Elite Dangerous](https://www.elitedangerous.com) by [Frontier Developments](https://frontier.co.uk). It provides on-screen assistance when a player is near a planet in the SRV, on foot or in ships. It has 3 main functions:

- **Organic scans:** Track the location of organic scans and the distance required for the next scan.
- **Ground target tracking:** Aiming guidance towards latitude/longitude co-ordinates.
- **Guardian sites:** Track visited areas and the locations of items within Guardian ruins and structures.

The application works by analyzing journal files written by the game when played on a PC, tracking the location of the player at the time of various events. It uses this information to render overlay windows atop the game, updated in real-time as the player moves about. For the most part the application is fully automatic, players need to start the application and then just play the game. It will remain hidden until triggered by events from the game.

## Installation

SrvSurvey is distributed two ways:

- **(Recommended)** An official signed build is available through [the Windows App Store](https://www.microsoft.com/store/productId/9NGT6RRH6B7N). This is updated less often but with higher quality.
- An unsigned build is available through [GitHub releases](https://github.com/njthomson/SrvSurvey/releases), updated frequently. Simply download the `.zip` file and run `setup.exe`. You will need to manually uninstall a previous version, but you will not lose your prior settings or surveys.

## General usage

Please see [the wiki](https://github.com/njthomson/SrvSurvey/wiki) for guidance on all the features of SrvSurvey, or ask questions on the [Guardian Science Corps](https://discord.gg/GJjTFa9fsz) Discord server.

## Cross-platform development

The `cross-platform-development` branch contains the Avalonia port for Windows
and Linux. Its user-facing destinations, journal monitoring, overlays,
preferences, profile migration, and network integrations are implemented and
covered by automated parity tests. Final whole-application visual, live Elite,
and native Linux runtime verification is still in progress, so these builds
remain review builds rather than the upstream production release.

Download or manually build the cross-platform review packages through the
[Build Windows and Linux packages workflow](https://github.com/Fenris159/SrvSurvey/actions/workflows/manual-release-packages.yml),
then follow the platform guide:

- [Install the Windows portable package](docs/INSTALL_WINDOWS.md)
- [Install the Linux AppImage or portable archive](docs/INSTALL_LINUX.md)

See [PORTING_PLAN.md](PORTING_PLAN.md) for the validated status, architecture, milestones, and platform-specific risks.

The current development shell requires the .NET 10 SDK:

```console
dotnet restore SrvSurvey.CrossPlatform.slnx
dotnet build SrvSurvey.CrossPlatform.slnx --configuration Release --no-restore
dotnet test SrvSurvey.CrossPlatform.slnx --configuration Release --no-build --no-restore
dotnet run --project src/SrvSurvey.Desktop/SrvSurvey.Desktop.csproj
```

Journal discovery can be overridden with either
`SRVSURVEY_JOURNAL_DIR=/path/to/journals` or:

```console
dotnet run --project src/SrvSurvey.Desktop/SrvSurvey.Desktop.csproj -- --journal-directory "/path/to/journals"
```

The application watches live journal and auxiliary files after rebuilding its
bootstrap state. A backup-first import in Settings migrates an original
SrvSurvey profile without modifying the source and verifies backup, staging,
activation, and rollback hashes before restarting into the imported data.

Versioned reference catalogs retain the original no-reinstall delivery model:
SrvSurvey checks the published data index once at startup, downloads only
missing or newer external catalogs, and offers the same refresh manually in
Diagnostics. Catalog updates are staged, validated, backed up, and activated
independently of application releases.

Cross-platform CI produces checksum-indexed self-contained Windows and Linux
archives plus platform-specific SPDX software bills of materials. The Linux job
also creates a desktop-integrated AppImage from the same tested publish output
and verifies its ELF dependency closure before starting it in XWayland mode on
an isolated X11 display.

The complete Linux overlay path supports native X11 and XWayland. On a Wayland
desktop, XWayland must provide an X11 `DISPLAY`; SrvSurvey detects that path and
uses XShape click-through, X11 Elite-window tracking and capture, and the X11
global-input backend. A pure native Wayland session without XWayland is not a
supported full-functionality mode and may fail to open because this build uses
Avalonia's X11 backend. The host must provide its graphics driver, XWayland,
`libX11`, `libXext`, `libICE`, `libSM`, and `fontconfig`; the AppImage bundles
the self-contained .NET runtime and the application-specific SDL, SharpHook,
Skia, and HarfBuzz native libraries.

## Feedback

Feedback, suggestions or bug reports are always welcome. Please [use this form](https://github.com/njthomson/SrvSurvey/issues/new?template=bug_report.md&title=) for bugs or suggestions.
