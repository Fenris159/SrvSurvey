namespace SrvSurvey.Core.Tests.Parity;

public sealed class LegacyNetworkParityTests
{
    private static readonly string[] LegacyNetworkFiles =
    [
        "SrvSurvey/net/Canonn.cs",
        "SrvSurvey/net/CanonnStation.cs",
        "SrvSurvey/net/CodexRef.cs",
        "SrvSurvey/net/EDDN.cs",
        "SrvSurvey/net/EDSM.cs",
        "SrvSurvey/net/Git.cs",
        "SrvSurvey/net/LookupStarSystem.cs",
        "SrvSurvey/net/NetCache.cs",
        "SrvSurvey/net/NetSysData.cs",
        "SrvSurvey/net/RavenColonial.cs",
        "SrvSurvey/net/spansh-misc.cs",
        "SrvSurvey/net/spansh-route.cs",
        "SrvSurvey/net/spansh-search.cs",
        "SrvSurvey/net/spansh.cs",
        "SrvSurvey/net/types.cs",
    ];

    private static readonly string[] HttpClientOwners =
    [
        "src/SrvSurvey.Core/Colonization/RavenColonialClient.cs",
        "src/SrvSurvey.Core/Exobiology/CanonnCodexChallengeClient.cs",
        "src/SrvSurvey.Core/Exobiology/CanonnSystemPoiClient.cs",
        "src/SrvSurvey.Core/Exobiology/CodexDiscoveryLocationClient.cs",
        "src/SrvSurvey.Core/Exploration/GreenGasGiantClient.cs",
        "src/SrvSurvey.Core/Exploration/SystemBodyDataClient.cs",
        "src/SrvSurvey.Core/Navigation/SystemSummaryClient.cs",
        "src/SrvSurvey.Core/Network/EddnPublisher.cs",
        "src/SrvSurvey.Core/Quests/RavenQuestClient.cs",
        "src/SrvSurvey.Core/Routes/SpanshRouteClient.cs",
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
        "src/SrvSurvey.Desktop/ViewModels/MainWindowViewModel.cs",
    ];

    private static readonly string[] ResponseOwners =
        HttpClientOwners
            .Where(path => !path.EndsWith(
                "MainWindowViewModel.cs",
                StringComparison.Ordinal))
            .ToArray();

    private static readonly NetworkSurface[] Surfaces =
    [
        new(
            "published-data-and-release-delivery",
            ["SrvSurvey/net/Git.cs", "SrvSurvey/net/CodexRef.cs"],
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
                "SrvSurvey/net/EDSM.cs",
                "SrvSurvey/net/LookupStarSystem.cs",
                "SrvSurvey/net/NetCache.cs",
                "SrvSurvey/net/NetSysData.cs",
                "SrvSurvey/net/spansh.cs",
            ],
            [
                "src/SrvSurvey.Core/Exploration/SystemBodyDataClient.cs",
                "src/SrvSurvey.Core/Navigation/SystemSummaryClient.cs",
                "src/SrvSurvey.Core/Exobiology/CodexDiscoveryLocationClient.cs",
                "src/SrvSurvey.Core/Search/SpanshStarSystemResolver.cs",
            ],
            [
                "tests/SrvSurvey.Core.Tests/Exploration/SystemBodyDataClientTests.cs",
                "tests/SrvSurvey.Core.Tests/Navigation/SystemSummaryClientTests.cs",
                "tests/SrvSurvey.Core.Tests/Exobiology/CodexDiscoveryLocationClientTests.cs",
                "tests/SrvSurvey.Core.Tests/Search/SpanshStarSystemResolverTests.cs",
            ]),
        new(
            "spansh-search-and-routes",
            [
                "SrvSurvey/net/spansh-misc.cs",
                "SrvSurvey/net/spansh-route.cs",
                "SrvSurvey/net/spansh-search.cs",
                "SrvSurvey/net/spansh.cs",
            ],
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
                "SrvSurvey/net/Canonn.cs",
                "SrvSurvey/net/CanonnStation.cs",
                "SrvSurvey/net/types.cs",
            ],
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
            ["SrvSurvey/net/EDDN.cs"],
            ["src/SrvSurvey.Core/Network/EddnPublisher.cs"],
            ["tests/SrvSurvey.Core.Tests/Network/EddnPublisherTests.cs"]),
        new(
            "raven-colonial-quests-and-ggg",
            ["SrvSurvey/net/RavenColonial.cs"],
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
            "downloaded-caches-and-images",
            ["SrvSurvey/net/NetCache.cs"],
            [
                "src/SrvSurvey.Core/Storage/VisitedStarsCacheService.cs",
                "src/SrvSurvey.Desktop/Platform/CodexImageCache.cs",
            ],
            [
                "tests/SrvSurvey.Core.Tests/Storage/VisitedStarsCacheServiceTests.cs",
                "tests/SrvSurvey.Desktop.Tests/Platform/CodexImageCacheTests.cs",
            ]),
    ];

    [Fact]
    public void AuditedInventoryExactlyMatchesLegacyNetworkDirectory()
    {
        var root = FindRepositoryRoot();
        var actual = Directory.EnumerateFiles(
                Path.Combine(root, "SrvSurvey", "net"),
                "*.cs",
                SearchOption.TopDirectoryOnly)
            .Select(path => Relative(root, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            LegacyNetworkFiles.Order(StringComparer.Ordinal),
            actual);
        Assert.Equal(
            LegacyNetworkFiles.Order(StringComparer.Ordinal),
            Surfaces.SelectMany(surface => surface.LegacyFiles)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryLegacySurfaceHasProductionAndAssertionEvidence()
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
    public void StartupRetainsTheLegacyVersionCheckThenDownloadContract()
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
            if (File.Exists(Path.Combine(current.FullName, "PORTING_PLAN.md"))
                && Directory.Exists(Path.Combine(current.FullName, "SrvSurvey", "net")))
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
        IReadOnlyList<string> LegacyFiles,
        IReadOnlyList<string> ProductionFiles,
        IReadOnlyList<string> TestFiles);
}
