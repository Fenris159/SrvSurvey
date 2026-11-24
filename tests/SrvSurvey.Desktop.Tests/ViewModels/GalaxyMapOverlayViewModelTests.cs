using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class GalaxyMapOverlayViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-galaxy-map-view-model-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public void EmptyStateProvidesStableNonNullBindingTargets()
    {
        using GalaxyMapOverlayViewModel viewModel = CreateViewModel(new FakeSummaryClient());

        Assert.False(viewModel.HasPrimarySystem);
        Assert.False(viewModel.HasSecondarySystem);
        Assert.Same(GalaxyMapSystemViewModel.Empty, viewModel.PrimarySystemDisplay);
        Assert.Same(GalaxyMapSystemViewModel.Empty, viewModel.SecondarySystemDisplay);
    }

    /// <summary>Verifies recognized selected target takes precedence over active route and shows all pois.</summary>
    [Fact]
    public async Task RecognizedSelectedTargetTakesPrecedenceOverActiveRouteAndShowsAllPois()
    {
        using GalaxyMapOverlayViewModel model = CreateViewModel(new FakeSummaryClient());
        var poi = new SrvSurvey.Core.Exploration.ExplorationPoi(
            1,
            "GEC",
            "First attraction",
            "Gamma",
            4,
            null,
            "Stellar Features",
            "",
            "",
            "",
            7,
            4,
            10,
            false
        );
        model.ApplyPoiCatalog(
            new SrvSurvey.Core.Exploration.ExplorationPoiCatalog([poi, poi with { Id = 2, Name = "Second attraction" }])
        );
        model.ApplyUpdate(
            "Sol",
            1,
            CreateRoute(),
            [],
            new EliteStatus
            {
                GuiFocus = GuiFocus.GalaxyMap,
                Destination = new StatusDestination
                {
                    System = 4,
                    Body = 0,
                    Name = "Gamma",
                },
            }
        );
        await model.PendingLoad;
        Assert.Equal("SELECTED", model.PrimarySystem!.Label);
        Assert.Equal("Gamma", model.PrimarySystem.Name);
        Assert.True(model.HasPrimaryPois);
        Assert.Contains("First attraction", model.PrimaryPoiText);
        Assert.Contains("Second attraction", model.PrimaryPoiText);
        Assert.Same(model.PrimaryPois, model.PrimaryPois);
        Assert.Same(model.SecondaryPois, model.SecondaryPois);
        Assert.False(model.HasSecondaryPois);
        model.ApplyPoiCatalog(SrvSurvey.Core.Exploration.ExplorationPoiCatalog.Empty);
        Assert.False(model.HasPrimaryPois);
    }

    [Fact]
    public async Task GalaxyMapRouteShowsDestinationNextHopAndFactionInfluence()
    {
        var client = new FakeSummaryClient();
        using GalaxyMapOverlayViewModel viewModel = CreateViewModel(client);

        viewModel.ApplyUpdate("Sol", 1, CreateRoute(), [], new EliteStatus { GuiFocus = GuiFocus.GalaxyMap });
        await viewModel.PendingLoad;

        Assert.True(viewModel.ShouldShow);
        Assert.Equal("DESTINATION", viewModel.PrimarySystem!.Label);
        Assert.Equal("Beta", viewModel.PrimarySystem.Name);
        Assert.Contains("Scanned 4 of 7", viewModel.PrimarySystem.DiscoveryText);
        Assert.Contains("2025", viewModel.PrimarySystem.UpdatedText);
        Assert.True(viewModel.PrimarySystem.HasUpdated);
        Assert.Equal("NEXT JUMP", viewModel.SecondarySystem!.Label);
        Assert.Equal("Alpha", viewModel.SecondarySystem.Name);
        Assert.True(viewModel.SecondarySystem.HasDiscoveredBy);
        viewModel.UpdateQuestTags(["Beta", "Alpha"]);
        Assert.True(viewModel.PrimarySystem.IsQuestTagged);
        Assert.True(viewModel.SecondarySystem.IsQuestTagged);
        Assert.Equal("2 jumps · 7.0 ly", viewModel.RouteFooter);
        Assert.Equal("Pathfinder Cooperative", Assert.Single(viewModel.Factions).Name);
        Assert.Equal("62%", viewModel.Factions[0].Influence);
        Assert.Equal([("Alpha", 2L), ("Beta", 3L)], client.Requests.Order().ToArray());
    }

    [Fact]
    public async Task SummaryLoadingWaitsUntilGalaxyMapIsOpen()
    {
        var client = new FakeSummaryClient();
        using GalaxyMapOverlayViewModel viewModel = CreateViewModel(client);

        viewModel.ApplyUpdate("Sol", 1, CreateRoute(), [], new EliteStatus { GuiFocus = GuiFocus.NoFocus });

        Assert.False(viewModel.ShouldShow);
        Assert.False(viewModel.IsGalaxyMapOpen);
        Assert.Empty(client.Requests);

        viewModel.ApplyUpdate("Sol", 1, null, [], new EliteStatus { GuiFocus = GuiFocus.GalaxyMap });
        await viewModel.PendingLoad;

        Assert.True(viewModel.IsGalaxyMapOpen);
        Assert.Equal(2, client.Requests.Count);

        viewModel.ApplyUpdate("Sol", 1, null, [], new EliteStatus { GuiFocus = GuiFocus.NoFocus });

        Assert.False(viewModel.IsGalaxyMapOpen);
    }

    [Fact]
    public async Task GalaxyMapMusicRetainsLegacyModeWhenGuiFocusLags()
    {
        var client = new FakeSummaryClient();
        using GalaxyMapOverlayViewModel viewModel = CreateViewModel(client);

        viewModel.ApplyUpdate(
            "Sol",
            1,
            CreateRoute(),
            [],
            new EliteStatus { Flags = StatusFlags.InMainShip, GuiFocus = GuiFocus.NoFocus },
            nextMusicTrack: "GalaxyMap"
        );
        await viewModel.PendingLoad;

        Assert.True(viewModel.IsGalaxyMapOpen);
        Assert.True(viewModel.ShouldShow);
        Assert.Equal("Beta", viewModel.PrimarySystem!.Name);
    }

    [Fact]
    public async Task ExplicitSelectionOverridesRouteButRouteNextHopDoesNot()
    {
        var client = new FakeSummaryClient();
        using GalaxyMapOverlayViewModel viewModel = CreateViewModel(client);
        var galaxyMapStatus = new EliteStatus { GuiFocus = GuiFocus.GalaxyMap };

        viewModel.ApplyUpdate("Sol", 1, CreateRoute(), [], galaxyMapStatus);
        await viewModel.PendingLoad;

        viewModel.ApplyUpdate("Sol", 1, null, [Event("FSDTarget", "\"Name\":\"Alpha\",\"SystemAddress\":2")], null);
        await viewModel.PendingLoad;

        Assert.Equal("DESTINATION", viewModel.PrimarySystem!.Label);
        Assert.Equal("Beta", viewModel.PrimarySystem.Name);
        Assert.True(viewModel.HasRouteFooter);

        viewModel.ApplyUpdate("Sol", 1, null, [Event("FSDTarget", "\"Name\":\"Gamma\",\"SystemAddress\":4")], null);
        await viewModel.PendingLoad;

        Assert.Equal("SELECTED", viewModel.PrimarySystem.Label);
        Assert.Equal("Gamma", viewModel.PrimarySystem.Name);
        Assert.False(viewModel.HasSecondarySystem);
        Assert.False(viewModel.HasRouteFooter);

        viewModel.ApplyUpdate("Sol", 1, CreateRoute(), [], null);
        await viewModel.PendingLoad;

        Assert.Equal("DESTINATION", viewModel.PrimarySystem.Label);
        Assert.Equal("Beta", viewModel.PrimarySystem.Name);
        Assert.True(viewModel.HasRouteFooter);
    }

    [Fact]
    public async Task RouteClearMatchesLegacyNoRouteState()
    {
        var client = new FakeSummaryClient();
        using GalaxyMapOverlayViewModel viewModel = CreateViewModel(client);
        viewModel.ApplyUpdate("Sol", 1, CreateRoute(), [], new EliteStatus { GuiFocus = GuiFocus.GalaxyMap });
        await viewModel.PendingLoad;

        viewModel.ApplyUpdate("Sol", 1, null, [Event("NavRouteClear")], null);
        await viewModel.PendingLoad;

        Assert.False(viewModel.HasPrimarySystem);
        Assert.Contains("No route", viewModel.DataStatus);
    }

    [Fact]
    public async Task BootstrapIgnoresHistoricalTargetAndUsesLiveDestination()
    {
        var client = new FakeSummaryClient();
        using GalaxyMapOverlayViewModel viewModel = CreateViewModel(client);

        viewModel.ApplyUpdate(
            "Sol",
            1,
            null,
            [Event("FSDTarget", "\"Name\":\"Historical\",\"SystemAddress\":99")],
            new EliteStatus
            {
                GuiFocus = GuiFocus.GalaxyMap,
                Destination = new StatusDestination
                {
                    Name = "Beta",
                    System = 3,
                    Body = 0,
                },
            },
            isBootstrapRead: true
        );
        await viewModel.PendingLoad;

        Assert.Equal("Beta", viewModel.PrimarySystem!.Name);
        Assert.Equal([("Beta", 3L)], client.Requests);
    }

    [Fact]
    public void PreferencesPersistImmediately()
    {
        using GalaxyMapOverlayViewModel viewModel = CreateViewModel(new FakeSummaryClient());

        viewModel.AutoShow = false;
        viewModel.ShowFactions = false;

        Assert.Equal(new GalaxyMapPreferences(false, false), new GalaxyMapSettingsStore(SettingsPath).Load());
    }

    /// <summary>Repeated status ticks while a Galaxy Map lookup is in flight must not cancel or refire requests.</summary>
    [Fact]
    public async Task RepeatedStatusUpdatesWhileLoadingDoNotCancelOrRefireLookups()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeSummaryClient { Gate = gate.Task };
        using GalaxyMapOverlayViewModel viewModel = CreateViewModel(client);
        var status = new EliteStatus { GuiFocus = GuiFocus.GalaxyMap };

        viewModel.ApplyUpdate("Sol", 1, CreateRoute(), [], status);
        for (int index = 0; index < 5; index++)
        {
            viewModel.ApplyUpdate("Sol", 1, null, [], status);
        }

        gate.SetResult();
        await viewModel.PendingLoad;

        Assert.Equal(0, client.CancellationCount);
        Assert.Equal([("Alpha", 2L), ("Beta", 3L)], client.Requests.Order().ToArray());
        Assert.Equal("Data from EDSM and Spansh", viewModel.DataStatus);
    }

    /// <summary>Closing the Galaxy Map mid-load cancels cleanly and reloads once reopened.</summary>
    [Fact]
    public async Task ClosingGalaxyMapMidLoadRetriesOnReopen()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeSummaryClient { Gate = gate.Task };
        using GalaxyMapOverlayViewModel viewModel = CreateViewModel(client);

        viewModel.ApplyUpdate("Sol", 1, CreateRoute(), [], new EliteStatus { GuiFocus = GuiFocus.GalaxyMap });
        viewModel.ApplyUpdate("Sol", 1, null, [], new EliteStatus { GuiFocus = GuiFocus.NoFocus });
        await viewModel.PendingLoad;

        Assert.True(client.CancellationCount > 0);
        client.Gate = null;
        viewModel.ApplyUpdate("Sol", 1, null, [], new EliteStatus { GuiFocus = GuiFocus.GalaxyMap });
        await viewModel.PendingLoad;

        Assert.Equal("Beta", viewModel.PrimarySystem!.Name);
        Assert.Equal("Alpha", viewModel.SecondarySystem!.Name);
    }

    /// <summary>Failed lookups show calm status without looping retries while open, and retry once reopened.</summary>
    [Fact]
    public async Task FailedLookupDoesNotSpinWhileOpenAndHidesRawExceptionText()
    {
        var client = new FakeSummaryClient { ThrowException = new HttpRequestException("429 Too Many Requests") };
        var logs = new List<string>();
        using GalaxyMapOverlayViewModel viewModel = CreateViewModel(client, logs.Add);
        var status = new EliteStatus { GuiFocus = GuiFocus.GalaxyMap };

        viewModel.ApplyUpdate("Sol", 1, CreateRoute(), [], status);
        await viewModel.PendingLoad;

        Assert.Equal("System data unavailable.", viewModel.DataStatus);
        Assert.DoesNotContain("429", viewModel.DataStatus, StringComparison.Ordinal);
        Assert.Single(logs);
        int requestCount = client.Requests.Count;

        viewModel.ApplyUpdate("Sol", 1, null, [], status);
        viewModel.ApplyUpdate("Sol", 1, null, [], status);
        Assert.Equal(requestCount, client.Requests.Count);

        client.ThrowException = null;
        viewModel.ApplyUpdate("Sol", 1, null, [], new EliteStatus { GuiFocus = GuiFocus.NoFocus });
        viewModel.ApplyUpdate("Sol", 1, null, [], status);
        await viewModel.PendingLoad;

        Assert.Equal("Data from EDSM and Spansh", viewModel.DataStatus);
        Assert.Equal("Alpha", viewModel.SecondarySystem!.Name);
    }

    /// <summary>Aggregates duplicate provider warnings across primary and secondary lookups into calm notices.</summary>
    [Theory]
    [InlineData(
        new[]
        {
            "EDSM bodies data is temporarily rate-limited (HTTP 429).",
            "EDSM traffic data is temporarily rate-limited (HTTP 429).",
        },
        new[]
        {
            "EDSM bodies data is unavailable: Response status code does not indicate success: 429 (Too Many Requests).",
            "EDSM traffic data is unavailable: Response status code does not indicate success: 429 (Too Many Requests).",
        },
        "EDSM temporarily rate-limited."
    )]
    [InlineData(
        new[] { "EDSM bodies data is temporarily rate-limited (HTTP 429)." },
        new[] { "Spansh system dump data is temporarily rate-limited (HTTP 429)." },
        "EDSM and Spansh temporarily rate-limited."
    )]
    [InlineData(
        new[] { "Spansh system dump data is temporarily rate-limited (HTTP 429)." },
        new string[0],
        "Spansh temporarily rate-limited."
    )]
    [InlineData(
        new[] { "EDSM bodies data is temporarily rate-limited (HTTP 429)." },
        new[] { "Spansh system dump data is unavailable: 503 Service Unavailable" },
        "EDSM temporarily rate-limited · Spansh data unavailable."
    )]
    [InlineData(
        new[] { "EDSM traffic data is unavailable: timeout" },
        new[] { "Spansh system dump data is temporarily rate-limited (HTTP 429)." },
        "EDSM data unavailable · Spansh temporarily rate-limited."
    )]
    [InlineData(
        new[] { "EDSM bodies data is unavailable: 502" },
        new[] { "Spansh system dump data is unavailable: 503" },
        "EDSM and Spansh data unavailable."
    )]
    [InlineData(new[] { "Unexpected provider failure" }, new string[0], "System data unavailable.")]
    public async Task ProviderWarningsAggregateOncePerProviderWithoutRawExceptions(
        string[] primaryWarnings,
        string[] secondaryWarnings,
        string expectedStatus
    )
    {
        var client = new FakeSummaryClient
        {
            WarningsBySystem = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Beta"] = primaryWarnings,
                ["Alpha"] = secondaryWarnings,
            },
        };
        var logs = new List<string>();
        using GalaxyMapOverlayViewModel viewModel = CreateViewModel(client, logs.Add);

        viewModel.ApplyUpdate("Sol", 1, CreateRoute(), [], new EliteStatus { GuiFocus = GuiFocus.GalaxyMap });
        await viewModel.PendingLoad;

        Assert.Equal(expectedStatus, viewModel.DataStatus);
        Assert.DoesNotContain("Response status code", viewModel.DataStatus, StringComparison.Ordinal);
        Assert.Equal(primaryWarnings.Length + secondaryWarnings.Length, logs.Count);
    }

    /// <summary>Undiscovered next-hop systems show Undiscovered system with a clean footer and no errors.</summary>
    [Fact]
    public async Task UndiscoveredNextHopShowsUndiscoveredWithoutErrors()
    {
        var client = new FakeSummaryClient { UndiscoveredSystems = ["Alpha"] };
        using GalaxyMapOverlayViewModel viewModel = CreateViewModel(client);

        viewModel.ApplyUpdate("Sol", 1, CreateRoute(), [], new EliteStatus { GuiFocus = GuiFocus.GalaxyMap });
        await viewModel.PendingLoad;

        Assert.Equal("Undiscovered system", viewModel.SecondarySystem!.DiscoveryText);
        Assert.Equal("Data from EDSM and Spansh", viewModel.DataStatus);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private string SettingsPath => Path.Combine(temporaryDirectory, "ui-settings.json");

    private GalaxyMapOverlayViewModel CreateViewModel(ISystemSummaryClient client, Action<string>? log = null)
    {
        Directory.CreateDirectory(temporaryDirectory);
        return new GalaxyMapOverlayViewModel(
            client,
            new GalaxyMapSettingsStore(SettingsPath),
            new SystemNicknameViewModel(
                SystemNicknameCatalog.Load(temporaryDirectory),
                new SystemNicknameSettingsStore(SettingsPath)
            ),
            log
        );
    }

    private static NavRouteSnapshot CreateRoute()
    {
        return new NavRouteSnapshot(
            DateTimeOffset.Parse("2026-07-25T12:00:00Z", global::System.Globalization.CultureInfo.InvariantCulture),
            "NavRoute",
            [
                new NavRouteEntry("Sol", 1, new GalacticCoordinate(0, 0, 0), "G"),
                new NavRouteEntry("Alpha", 2, new GalacticCoordinate(3, 0, 0), "K"),
                new NavRouteEntry("Beta", 3, new GalacticCoordinate(3, 4, 0), "N"),
            ]
        );
    }

    private static JournalEventEnvelope Event(string eventName, string? properties = null)
    {
        string suffix = properties is null ? string.Empty : "," + properties;
        Assert.True(
            JournalEventEnvelope.TryParse(
                $"{{\"timestamp\":\"2026-07-25T12:00:00Z\",\"event\":\"{eventName}\"{suffix}}}",
                out JournalEventEnvelope? journalEvent,
                out string? error
            ),
            error
        );
        return Assert.IsType<JournalEventEnvelope>(journalEvent);
    }

    private sealed class FakeSummaryClient : ISystemSummaryClient
    {
        public List<(string Name, long Address)> Requests { get; } = [];

        public Task? Gate { get; set; }

        public Exception? ThrowException { get; set; }

        public int CancellationCount { get; private set; }

        public Dictionary<string, IReadOnlyList<string>> WarningsBySystem { get; init; } = [];

        public HashSet<string> UndiscoveredSystems { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public async Task<SystemSummaryLoadResult> GetAsync(
            string systemName,
            long systemAddress,
            CancellationToken cancellationToken = default
        )
        {
            Requests.Add((systemName, systemAddress));
            if (Gate is not null)
            {
                try
                {
                    await Gate.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    CancellationCount++;
                    throw;
                }
            }

            if (ThrowException is not null)
            {
                throw ThrowException;
            }

            bool isUndiscovered = UndiscoveredSystems.Contains(systemName);
            var summary = new SystemSummary(
                systemName,
                systemAddress,
                null,
                "K",
                !isUndiscovered,
                isUndiscovered ? 0 : 4,
                isUndiscovered ? 0 : 7,
                isUndiscovered ? null : "Pathfinder",
                isUndiscovered
                    ? null
                    : DateTimeOffset.Parse(
                        "2024-01-02T03:04:05Z",
                        global::System.Globalization.CultureInfo.InvariantCulture
                    ),
                isUndiscovered
                    ? null
                    : DateTimeOffset.Parse(
                        "2025-02-03T04:05:06Z",
                        global::System.Globalization.CultureInfo.InvariantCulture
                    ),
                null,
                new SystemPoiSummary(isUndiscovered ? 0 : 7, systemName == "Beta" ? 2 : 0, 0, 0, 0, 0, 0),
                []
            )
            {
                Factions =
                    systemName == "Beta" ? [new SystemFactionSummary("Pathfinder Cooperative", 0.62, "Boom")] : [],
            };
            IReadOnlyList<string> warnings = WarningsBySystem.TryGetValue(
                systemName,
                out IReadOnlyList<string>? configured
            )
                ? configured
                : [];
            return new SystemSummaryLoadResult(summary, warnings);
        }
    }
}
