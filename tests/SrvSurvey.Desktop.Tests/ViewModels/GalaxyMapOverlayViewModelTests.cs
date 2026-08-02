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
        $"SrvSurvey-galaxy-map-view-model-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task GalaxyMapRouteShowsDestinationNextHopAndFactionInfluence()
    {
        var client = new FakeSummaryClient();
        using var viewModel = CreateViewModel(client);

        viewModel.ApplyUpdate(
            "Sol",
            1,
            CreateRoute(),
            [],
            new EliteStatus { GuiFocus = GuiFocus.GalaxyMap });
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
        using var viewModel = CreateViewModel(client);

        viewModel.ApplyUpdate(
            "Sol",
            1,
            CreateRoute(),
            [],
            new EliteStatus { GuiFocus = GuiFocus.NoFocus });

        Assert.False(viewModel.ShouldShow);
        Assert.Empty(client.Requests);

        viewModel.ApplyUpdate(
            "Sol",
            1,
            null,
            [],
            new EliteStatus { GuiFocus = GuiFocus.GalaxyMap });
        await viewModel.PendingLoad;

        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task RouteClearMatchesLegacyNoRouteState()
    {
        var client = new FakeSummaryClient();
        using var viewModel = CreateViewModel(client);
        viewModel.ApplyUpdate(
            "Sol",
            1,
            CreateRoute(),
            [],
            new EliteStatus { GuiFocus = GuiFocus.GalaxyMap });
        await viewModel.PendingLoad;

        viewModel.ApplyUpdate(
            "Sol",
            1,
            null,
            [Event("NavRouteClear")],
            null);
        await viewModel.PendingLoad;

        Assert.False(viewModel.HasPrimarySystem);
        Assert.Contains("No route", viewModel.DataStatus);
    }

    [Fact]
    public async Task BootstrapIgnoresHistoricalTargetAndUsesLiveDestination()
    {
        var client = new FakeSummaryClient();
        using var viewModel = CreateViewModel(client);

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
            isBootstrapRead: true);
        await viewModel.PendingLoad;

        Assert.Equal("Beta", viewModel.PrimarySystem!.Name);
        Assert.Equal([("Beta", 3L)], client.Requests);
    }

    [Fact]
    public void PreferencesPersistImmediately()
    {
        using var viewModel = CreateViewModel(new FakeSummaryClient());

        viewModel.AutoShow = false;
        viewModel.ShowFactions = false;

        Assert.Equal(
            new GalaxyMapPreferences(false, false),
            new GalaxyMapSettingsStore(SettingsPath).Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private string SettingsPath => Path.Combine(
        temporaryDirectory,
        "ui-settings.json");

    private GalaxyMapOverlayViewModel CreateViewModel(
        ISystemSummaryClient client)
    {
        Directory.CreateDirectory(temporaryDirectory);
        return new GalaxyMapOverlayViewModel(
            client,
            new GalaxyMapSettingsStore(SettingsPath),
            new SystemNicknameViewModel(
                SystemNicknameCatalog.Load(temporaryDirectory),
                new SystemNicknameSettingsStore(SettingsPath)));
    }

    private static NavRouteSnapshot CreateRoute()
    {
        return new NavRouteSnapshot(
            DateTimeOffset.Parse("2026-07-25T12:00:00Z"),
            "NavRoute",
            [
                new NavRouteEntry(
                    "Sol",
                    1,
                    new GalacticCoordinate(0, 0, 0),
                    "G"),
                new NavRouteEntry(
                    "Alpha",
                    2,
                    new GalacticCoordinate(3, 0, 0),
                    "K"),
                new NavRouteEntry(
                    "Beta",
                    3,
                    new GalacticCoordinate(3, 4, 0),
                    "N"),
            ]);
    }

    private static JournalEventEnvelope Event(
        string eventName,
        string? properties = null)
    {
        var suffix = properties is null ? string.Empty : "," + properties;
        Assert.True(JournalEventEnvelope.TryParse(
            $"{{\"timestamp\":\"2026-07-25T12:00:00Z\",\"event\":\"{eventName}\"{suffix}}}",
            out var journalEvent,
            out var error), error);
        return Assert.IsType<JournalEventEnvelope>(journalEvent);
    }

    private sealed class FakeSummaryClient : ISystemSummaryClient
    {
        public List<(string Name, long Address)> Requests { get; } = [];

        public Task<SystemSummaryLoadResult> GetAsync(
            string systemName,
            long systemAddress,
            CancellationToken cancellationToken = default)
        {
            Requests.Add((systemName, systemAddress));
            var summary = new SystemSummary(
                systemName,
                systemAddress,
                null,
                "K",
                true,
                4,
                7,
                "Pathfinder",
                DateTimeOffset.Parse("2024-01-02T03:04:05Z"),
                DateTimeOffset.Parse("2025-02-03T04:05:06Z"),
                null,
                new SystemPoiSummary(7, systemName == "Beta" ? 2 : 0, 0, 0, 0, 0, 0),
                [])
            {
                Factions = systemName == "Beta"
                    ? [new SystemFactionSummary("Pathfinder Cooperative", 0.62, "Boom")]
                    : [],
            };
            return Task.FromResult(new SystemSummaryLoadResult(summary, []));
        }
    }
}
