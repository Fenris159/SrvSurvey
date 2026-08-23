namespace SrvSurvey.Core.Tests.Coverage;

public sealed class NetworkSurfaceCoverageTests
{
    private static readonly string[] HttpClientOwners =
    [
        "src/SrvSurvey.Core/Colonization/RavenColonialClient.cs",
        "src/SrvSurvey.Core/Exobiology/CanonnCodexChallengeClient.cs",
        "src/SrvSurvey.Core/Exobiology/CanonnSystemPoiClient.cs",
        "src/SrvSurvey.Core/Exobiology/CodexDiscoveryLocationClient.cs",
        "src/SrvSurvey.Core/Exploration/GreenGasGiantClient.cs",
        "src/SrvSurvey.Core/Exploration/SystemBodyDataClient.cs",
        "src/SrvSurvey.Core/Inara/InaraPublisher.cs",
        "src/SrvSurvey.Core/Navigation/SystemSummaryClient.cs",
        "src/SrvSurvey.Core/Network/EddnPublisher.cs",
        "src/SrvSurvey.Core/Network/EddnTransport.cs",
        "src/SrvSurvey.Core/Network/VoxStellarPublisher.cs",
        "src/SrvSurvey.Core/Quests/RavenQuestClient.cs",
        "src/SrvSurvey.Core/Routes/SpanshRouteClient.cs",
        "src/SrvSurvey.Core/Search/ArdentSystemNameSuggestionClient.cs",
        "src/SrvSurvey.Core/Search/EdsmSystemNameSuggestionClient.cs",
        "src/SrvSurvey.Core/Search/NearestSystemsClient.cs",
        "src/SrvSurvey.Core/Search/SpanshBoxelClient.cs",
        "src/SrvSurvey.Core/Search/SpanshStarSystemResolver.cs",
        "src/SrvSurvey.Core/Settlements/CanonnHumanSiteClient.cs",
        "src/SrvSurvey.Core/Storage/VisitedStarsCacheService.cs",
        "src/SrvSurvey.Core/Updates/CrossPlatformReleaseClient.cs",
        "src/SrvSurvey.Core/Updates/PublishedDataIndexClient.cs",
        "src/SrvSurvey.Core/Updates/PublishedReferenceUpdateService.cs",
        "src/SrvSurvey.Core/Updates/ReleasePackageDownloadService.cs",
        "src/SrvSurvey.Desktop/Platform/CodexImageCache.cs",
        "src/SrvSurvey.Desktop/Platform/Frontier/FrontierAccountService.cs",
        "src/SrvSurvey.Desktop/Platform/Inara/InaraCommunityGoalClient.cs",
        "src/SrvSurvey.Desktop/Runtime/DiagnosticReplayContext.cs",
        "src/SrvSurvey.Desktop/ViewModels/MainWindowViewModel.cs",
        "src/SrvSurvey.Desktop/ViewModels/MainWindowViewModelFactory.cs",
    ];

    private static readonly string[] ResponseOwners =
        HttpClientOwners
            .Where(path => !path.EndsWith(
                    "MainWindowViewModel.cs",
                    StringComparison.Ordinal)
                && !path.EndsWith(
                    "EddnPublisher.cs",
                    StringComparison.Ordinal)
                && !path.EndsWith(
                    "VoxStellarPublisher.cs",
                    StringComparison.Ordinal)
                && !path.EndsWith(
                    "DiagnosticReplayContext.cs",
                    StringComparison.Ordinal)
                && !path.EndsWith(
                    "MainWindowViewModelFactory.cs",
                    StringComparison.Ordinal))
            .ToArray();

    private static readonly NetworkSurface[] Surfaces =
    [
        new(
            "published-data-and-release-delivery",
            [
                "src/SrvSurvey.Core/Updates/PublishedDataIndexClient.cs",
                "src/SrvSurvey.Core/Updates/PublishedReferenceUpdateService.cs",
                "src/SrvSurvey.Core/Updates/CrossPlatformReleaseClient.cs",
                "src/SrvSurvey.Core/Updates/ReleasePackageDownloadService.cs",
                "src/SrvSurvey.Core/Updates/ReleasePackageStagingService.cs",
                "src/SrvSurvey.Core/Updates/ReleaseInstallationTransaction.cs",
            ],
            [
                "tests/SrvSurvey.Core.Tests/Updates/PublishedDataIndexClientTests.cs",
                "tests/SrvSurvey.Core.Tests/Updates/PublishedReferenceUpdateServiceTests.cs",
                "tests/SrvSurvey.Core.Tests/Updates/CrossPlatformReleaseClientTests.cs",
                "tests/SrvSurvey.Core.Tests/Updates/ReleasePackageDownloadServiceTests.cs",
                "tests/SrvSurvey.Core.Tests/Updates/ReleasePackageStagingServiceTests.cs",
                "tests/SrvSurvey.Core.Tests/Updates/ReleaseInstallationTransactionTests.cs",
            ]),
        new(
            "system-lookup-and-enrichment",
            [
                "src/SrvSurvey.Core/Search/ArdentSystemNameSuggestionClient.cs",
                "src/SrvSurvey.Core/Search/EdsmSystemNameSuggestionClient.cs",
                "src/SrvSurvey.Core/Exploration/SystemBodyDataClient.cs",
                "src/SrvSurvey.Core/Navigation/SystemSummaryClient.cs",
                "src/SrvSurvey.Core/Exobiology/CodexDiscoveryLocationClient.cs",
                "src/SrvSurvey.Core/Search/SpanshStarSystemResolver.cs",
            ],
            [
                "tests/SrvSurvey.Core.Tests/Search/ArdentSystemNameSuggestionClientTests.cs",
                "tests/SrvSurvey.Core.Tests/Search/EdsmSystemNameSuggestionClientTests.cs",
                "tests/SrvSurvey.Core.Tests/Search/FallbackSystemNameSuggestionClientTests.cs",
                "tests/SrvSurvey.Core.Tests/Exploration/SystemBodyDataClientTests.cs",
                "tests/SrvSurvey.Core.Tests/Navigation/SystemSummaryClientTests.cs",
                "tests/SrvSurvey.Core.Tests/Exobiology/CodexDiscoveryLocationClientTests.cs",
                "tests/SrvSurvey.Core.Tests/Search/SpanshStarSystemResolverTests.cs",
            ]),
        new(
            "spansh-search-and-routes",
            [
                "src/SrvSurvey.Core/Routes/SpanshRouteClient.cs",
                "src/SrvSurvey.Core/Search/NearestSystemsClient.cs",
                "src/SrvSurvey.Core/Search/SpanshBoxelClient.cs",
            ],
            [
                "tests/SrvSurvey.Core.Tests/Routes/SpanshRouteClientTests.cs",
                "tests/SrvSurvey.Core.Tests/Search/NearestSystemsClientTests.cs",
                "tests/SrvSurvey.Core.Tests/Search/SpanshBoxelClientTests.cs",
            ]),
        new(
            "canonn-runtime-services",
            [
                "src/SrvSurvey.Core/Exobiology/CanonnCodexChallengeClient.cs",
                "src/SrvSurvey.Core/Exobiology/CanonnSystemPoiClient.cs",
                "src/SrvSurvey.Core/Search/NearestSystemsClient.cs",
                "src/SrvSurvey.Core/Settlements/CanonnHumanSiteClient.cs",
            ],
            [
                "tests/SrvSurvey.Core.Tests/Exobiology/CanonnCodexChallengeClientTests.cs",
                "tests/SrvSurvey.Core.Tests/Exobiology/CanonnSystemPoiClientTests.cs",
                "tests/SrvSurvey.Core.Tests/Search/NearestSystemsClientTests.cs",
                "tests/SrvSurvey.Core.Tests/Settlements/CanonnHumanSiteClientTests.cs",
            ]),
        new(
            "eddn-publication",
            [
                "src/SrvSurvey.Core/Network/EddnPublisher.cs",
                "src/SrvSurvey.Core/Network/EddnMessageSanitizer.cs",
                "src/SrvSurvey.Core/Network/EddnCompanionFileReader.cs",
                "src/SrvSurvey.Core/Network/EddnOutbox.cs",
                "src/SrvSurvey.Core/Network/EddnTransport.cs",
            ],
            [
                "tests/SrvSurvey.Core.Tests/Network/EddnPublisherTests.cs",
                "tests/SrvSurvey.Core.Tests/Network/EddnMessageSanitizerTests.cs",
                "tests/SrvSurvey.Core.Tests/Network/EddnCompanionFileReaderTests.cs",
                "tests/SrvSurvey.Core.Tests/Network/EddnOutboxTests.cs",
                "tests/SrvSurvey.Core.Tests/Network/EddnTransportTests.cs",
            ]),
        new(
            "inara-publication",
            ["src/SrvSurvey.Core/Inara/InaraPublisher.cs"],
            ["tests/SrvSurvey.Core.Tests/Inara/InaraPublisherTests.cs"]),
        new(
            "voxstellar-publication",
            ["src/SrvSurvey.Core/Network/VoxStellarPublisher.cs"],
            ["tests/SrvSurvey.Core.Tests/Network/VoxStellarPublisherTests.cs"]),
        new(
            "inara-community-goal-read",
            [
                "src/SrvSurvey.Desktop/Platform/Inara/InaraCommunityGoalClient.cs",
                "src/SrvSurvey.Desktop/Platform/Inara/InaraCommunityGoalEnricher.cs",
            ],
            [
                "tests/SrvSurvey.Desktop.Tests/Platform/InaraCommunityGoalClientTests.cs",
            ]),
        new(
            "raven-colonial-quests-and-ggg",
            [
                "src/SrvSurvey.Core/Colonization/RavenColonialClient.cs",
                "src/SrvSurvey.Core/Quests/RavenQuestClient.cs",
                "src/SrvSurvey.Core/Exploration/GreenGasGiantClient.cs",
            ],
            [
                "tests/SrvSurvey.Core.Tests/Colonization/RavenColonialClientTests.cs",
                "tests/SrvSurvey.Core.Tests/Quests/RavenQuestClientTests.cs",
                "tests/SrvSurvey.Core.Tests/Exploration/GreenGasGiantClientTests.cs",
            ]),
        new(
            "frontier-commander-profile",
            [
                "src/SrvSurvey.Desktop/Platform/Frontier/FrontierAccountService.cs",
                "src/SrvSurvey.Core/Frontier/FrontierCapiSnapshotParser.cs",
            ],
            [
                "tests/SrvSurvey.Desktop.Tests/Platform/FrontierAccountServiceTests.cs",
                "tests/SrvSurvey.Core.Tests/Frontier/FrontierCapiSnapshotParserTests.cs",
            ]),
        new(
            "downloaded-caches-and-images",
            [
                "src/SrvSurvey.Core/Storage/VisitedStarsCacheService.cs",
                "src/SrvSurvey.Desktop/Platform/CodexImageCache.cs",
            ],
            [
                "tests/SrvSurvey.Core.Tests/Storage/VisitedStarsCacheServiceTests.cs",
                "tests/SrvSurvey.Desktop.Tests/Platform/CodexImageCacheTests.cs",
            ]),
        new(
            "diagnostic-network-denial",
            ["src/SrvSurvey.Desktop/Runtime/DiagnosticReplayContext.cs"],
            ["tests/SrvSurvey.Desktop.Tests/Runtime/DiagnosticReplayContextTests.cs"]),
    ];

    [Fact]
    public void EveryNetworkSurfaceHasProductionAndAssertionEvidence()
    {
        var root = FindRepositoryRoot();
        foreach (var surface in Surfaces)
        {
            Assert.NotEmpty(surface.ProductionFiles);
            Assert.NotEmpty(surface.TestFiles);
            foreach (var path in surface.ProductionFiles)
            {
                Assert.True(File.Exists(Path.Combine(root, Native(path))),
                    $"Missing {surface.Name} production evidence: {path}");
            }

            foreach (var path in surface.TestFiles)
            {
                var absolutePath = Path.Combine(root, Native(path));
                Assert.True(File.Exists(absolutePath),
                    $"Missing {surface.Name} test evidence: {path}");
                Assert.Contains("Assert.", File.ReadAllText(absolutePath));
            }
        }
    }

    [Fact]
    public void ModernHttpInventoryIsExplicitAndEveryResponseIsStreamBounded()
    {
        var root = FindRepositoryRoot();
        var actual = Directory.EnumerateFiles(
                Path.Combine(root, "src"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(
                "HttpClient",
                StringComparison.Ordinal))
            .Select(path => Relative(root, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(HttpClientOwners.Order(StringComparer.Ordinal), actual);
        foreach (var path in ResponseOwners)
        {
            var source = File.ReadAllText(Path.Combine(root, Native(path)));
            Assert.Contains("ResponseHeadersRead", source);
            Assert.Contains("Maximum", source);
            Assert.Contains("Bytes", source);
        }

        var compositionRoot = File.ReadAllText(Path.Combine(
            root,
            Native("src/SrvSurvey.Desktop/ViewModels/MainWindowViewModel.cs")));
        Assert.DoesNotContain("ReadAsStreamAsync", compositionRoot);
        Assert.DoesNotContain("ReadAsStringAsync", compositionRoot);
        Assert.DoesNotContain("ReadFromJsonAsync", compositionRoot);
    }

    [Fact]
    public void StartupChecksForApplicationAndReferenceDataUpdates()
    {
        var root = FindRepositoryRoot();
        var window = File.ReadAllText(Path.Combine(
            root,
            Native("src/SrvSurvey.Desktop/MainWindow.axaml.cs")));
        var referenceService = File.ReadAllText(Path.Combine(
            root,
            Native("src/SrvSurvey.Core/Updates/PublishedReferenceUpdateService.cs")));

        Assert.Contains("ReleaseUpdates.CheckAsync()", window);
        Assert.Contains("ReferenceDataUpdates.RefreshAsync()", window);
        Assert.Contains("indexClient.GetAsync", referenceService);
        Assert.Contains(
            "remoteVersion > currentVersion || !source.IsLocal",
            referenceService);
        Assert.Contains("ActivateAsync", referenceService);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(
                    current.FullName,
                    "SrvSurvey.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static string Native(string relativePath)
    {
        return relativePath.Replace('/', Path.DirectorySeparatorChar);
    }

    private static string Relative(string root, string path)
    {
        return Path.GetRelativePath(root, path).Replace('\\', '/');
    }

    private sealed record NetworkSurface(
        string Name,
        IReadOnlyList<string> ProductionFiles,
        IReadOnlyList<string> TestFiles);
}
