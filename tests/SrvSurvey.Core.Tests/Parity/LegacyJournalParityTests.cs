using System.Text.RegularExpressions;

namespace SrvSurvey.Core.Tests.Parity;

public sealed partial class LegacyJournalParityTests
{
    private static readonly IReadOnlyList<JournalParityGroup> Groups =
    [
        new(
            "commander-session",
            [
                "Commander", "LoadGame", "Loadout", "Location", "Died",
                "Resurrect", "Music", "Shutdown", "StartJump", "FSDJump",
                "CarrierJump", "SupercruiseExit", "ApproachBody", "LeaveBody",
                "LaunchSRV", "DockSRV", "LaunchFighter", "DockFighter",
                "SuitLoadout", "SwitchSuitLoadout",
            ],
            [
                "tests/SrvSurvey.Core.Tests/JournalSessionStateTests.cs",
                "tests/SrvSurvey.Core.Tests/JournalSnapshotReaderTests.cs",
                "tests/SrvSurvey.Core.Tests/Travel/DockToDockLogServiceTests.cs",
                "tests/SrvSurvey.Core.Tests/Guardian/GuardianLiveSiteStateTests.cs",
            ]),
        new(
            "system-body-exploration",
            [
                "FSSDiscoveryScan", "FSSAllBodiesFound", "Scan",
                "SAAScanComplete", "SAASignalsFound", "FSSBodySignals",
                "Touchdown", "Liftoff",
            ],
            [
                "tests/SrvSurvey.Core.Tests/Exploration/SystemScanStateTests.cs",
                "tests/SrvSurvey.Core.Tests/Exploration/ExplorationStateTests.cs",
                "tests/SrvSurvey.Core.Tests/Exobiology/SurfaceSurveyJournalTrackerTests.cs",
            ]),
        new(
            "exobiology",
            [
                "CodexEntry", "ScanOrganic", "SellOrganicData", "Disembark",
                "Embark",
            ],
            [
                "tests/SrvSurvey.Core.Tests/Exobiology/ExobiologyStateTests.cs",
                "tests/SrvSurvey.Core.Tests/Exobiology/SurfaceSurveyJournalTrackerTests.cs",
                "tests/SrvSurvey.Core.Tests/Exobiology/CommanderCodexJournalTrackerTests.cs",
                "tests/SrvSurvey.Core.Tests/Exploration/SystemScanStateTests.cs",
            ]),
        new(
            "guardian-human-sites",
            [
                "ApproachSettlement", "BackpackChange", "CollectItems",
                "SupercruiseEntry", "DockingRequested", "DockingCancelled",
                "DockingDenied", "DockingGranted",
            ],
            [
                "tests/SrvSurvey.Core.Tests/Guardian/GuardianLiveSiteStateTests.cs",
                "tests/SrvSurvey.Core.Tests/Settlements/HumanSiteLiveStateTests.cs",
                "tests/SrvSurvey.Core.Tests/Settlements/HumanSiteActivityTrackerTests.cs",
            ]),
        new(
            "missions-combat",
            [
                "Missions", "MissionAccepted", "MissionFailed",
                "MissionAbandoned", "MissionCompleted", "Bounty",
            ],
            [
                "tests/SrvSurvey.Core.Tests/Combat/CombatStateTests.cs",
                "tests/SrvSurvey.Core.Tests/Guardian/RamTahStateTests.cs",
            ]),
        new(
            "cargo-materials",
            [
                "CollectCargo", "EjectCargo", "CargoTransfer", "CargoDepot",
                "Cargo", "MarketBuy", "MarketSell", "Market", "Materials",
                "MaterialCollected", "MaterialTrade", "TechnologyBroker",
            ],
            [
                "tests/SrvSurvey.Core.Tests/Journal/CargoInventoryStateTests.cs",
                "tests/SrvSurvey.Core.Tests/Guardian/GuardianArtifactInventoryStateTests.cs",
                "tests/SrvSurvey.Core.Tests/JournalDirectoryMonitorTests.cs",
                "tests/SrvSurvey.Desktop.Tests/ViewModels/NotificationViewModelTests.cs",
            ]),
        new(
            "colonization",
            [
                "ColonisationConstructionDepot", "ColonisationContribution",
                "ColonisationBeaconDeployed",
            ],
            [
                "tests/SrvSurvey.Core.Tests/Colonization/ColonizationConstructionStateTests.cs",
            ]),
        new(
            "route-docking-travel",
            [
                "FSDTarget", "NavRoute", "NavRouteClear", "Interdicted",
                "Docked", "Undocked",
            ],
            [
                "tests/SrvSurvey.Core.Tests/Travel/DockToDockLogServiceTests.cs",
                "tests/SrvSurvey.Desktop.Tests/ViewModels/JumpInfoViewModelTests.cs",
                "tests/SrvSurvey.Desktop.Tests/ViewModels/GalaxyMapOverlayViewModelTests.cs",
            ]),
    ];

    [Fact]
    public void AuditedInventoryExactlyMatchesLegacyGameHandlers()
    {
        var repositoryRoot = FindRepositoryRoot();
        var legacyGame = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "SrvSurvey",
            "game",
            "Game.cs"));
        var legacyEvents = LegacyHandlerPattern().Matches(legacyGame)
            .Select(match => match.Groups["event"].Value)
            .Where(eventName => eventName != "JournalEntry")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var auditedEvents = Groups
            .SelectMany(group => group.Events)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(68, legacyEvents.Length);
        Assert.Equal(legacyEvents, auditedEvents);
        Assert.Equal(auditedEvents.Length, auditedEvents.Distinct().Count());
    }

    [Fact]
    public void EveryLegacyHandlerHasModernConsumerAndRegressionEvidence()
    {
        var repositoryRoot = FindRepositoryRoot();
        var productionFiles = EnumerateSourceFiles(
            repositoryRoot,
            "src/SrvSurvey.Core",
            "src/SrvSurvey.Desktop");
        var regressionFiles = EnumerateSourceFiles(repositoryRoot, "tests")
            .Where(path => !path.EndsWith(
                "LegacyJournalParityTests.cs",
                StringComparison.Ordinal))
            .ToArray();

        foreach (var eventName in Groups.SelectMany(group => group.Events))
        {
            var literal = $"\"{eventName}\"";
            Assert.True(
                productionFiles.Any(path => File.ReadAllText(path).Contains(
                    literal,
                    StringComparison.Ordinal)),
                $"Legacy event {eventName} has no modern production consumer.");
            Assert.True(
                regressionFiles.Any(path => File.ReadAllText(path).Contains(
                    literal,
                    StringComparison.Ordinal)),
                $"Legacy event {eventName} has no event-specific regression evidence.");
        }
    }

    [Fact]
    public void EveryStateGroupHasGoldenProjectionEvidenceForEveryEvent()
    {
        var repositoryRoot = FindRepositoryRoot();
        foreach (var group in Groups)
        {
            var evidence = string.Join(
                Environment.NewLine,
                group.EvidenceFiles.Select(relativePath =>
                {
                    var path = Path.Combine(
                        repositoryRoot,
                        relativePath.Replace('/', Path.DirectorySeparatorChar));
                    Assert.True(
                        File.Exists(path),
                        $"Golden evidence file is missing for {group.Name}: {relativePath}");
                    var content = File.ReadAllText(path);
                    Assert.Contains("Assert.", content, StringComparison.Ordinal);
                    return content;
                }));

            foreach (var eventName in group.Events)
            {
                Assert.Contains(
                    eventName,
                    evidence,
                    StringComparison.Ordinal);
            }
        }
    }

    private static string[] EnumerateSourceFiles(
        string repositoryRoot,
        params string[] relativeRoots)
    {
        return relativeRoots
            .SelectMany(relativeRoot => Directory.EnumerateFiles(
                Path.Combine(
                    repositoryRoot,
                    relativeRoot.Replace('/', Path.DirectorySeparatorChar)),
                "*.cs",
                SearchOption.AllDirectories))
            .Where(path => !path.Contains(
                    $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase)
                && !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PORTING_PLAN.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the SrvSurvey repository root.");
    }

    [GeneratedRegex(
        @"onJournalEntry\((?<event>[A-Za-z0-9_]+) entry\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex LegacyHandlerPattern();

    private sealed record JournalParityGroup(
        string Name,
        IReadOnlyList<string> Events,
        IReadOnlyList<string> EvidenceFiles);
}
