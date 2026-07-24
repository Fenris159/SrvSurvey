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

The existing WinForms application remains the production implementation. Work on a new cross-platform application is being developed incrementally; it has not reached feature parity or release readiness.

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

The shell currently reads a bootstrap snapshot from the newest journal. It does
not yet watch live changes or provide the production overlays and tools.

## Feedback

Feedback, suggestions or bug reports are always welcome. Please [use this form](https://github.com/njthomson/SrvSurvey/issues/new?template=bug_report.md&title=) for bugs or suggestions.
