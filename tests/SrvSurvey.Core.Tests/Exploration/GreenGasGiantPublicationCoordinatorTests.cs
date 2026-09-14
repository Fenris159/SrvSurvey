using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Exploration;

public sealed class GreenGasGiantPublicationCoordinatorTests
{
    [Fact]
    public async Task BootstrapBuildsContextWithoutPublishingHistory()
    {
        var client = new RecordingClient();
        GreenGasGiantPublicationCoordinator coordinator = Create(client);

        GreenGasGiantPublicationResult bootstrap = await coordinator.ApplyAsync(
            [
                Event("{\"event\":\"Commander\",\"Name\":\"Test Cmdr\"}"),
                Event("{\"event\":\"Location\",\"StarPos\":[1.5,-2,3]}"),
                Scan(310),
            ],
            enabled: true,
            allowPublishing: false
        );
        GreenGasGiantPublicationResult live = await coordinator.ApplyAsync(
            [Scan(310)],
            enabled: true,
            allowPublishing: true
        );

        Assert.Empty(bootstrap.Published);
        GreenGasGiantCandidate candidate = Assert.Single(live.Published);
        Assert.Single(client.Candidates);
        Assert.Equal("Test Cmdr", candidate.CommanderName);
        Assert.Equal("potential", candidate.Tag);
        Assert.Equal(1.5, candidate.StarPosition.X);
        Assert.Equal(-2, candidate.StarPosition.Y);
        Assert.Equal(3, candidate.StarPosition.Z);
        Assert.Contains("\"event\":\"Scan\"", candidate.RawJournalJson);
    }

    [Fact]
    public async Task DisabledPublicationStillRefreshesContext()
    {
        var client = new RecordingClient();
        GreenGasGiantPublicationCoordinator coordinator = Create(client);

        await coordinator.ApplyAsync(
            [
                Event("{\"event\":\"LoadGame\",\"Commander\":\"Later Cmdr\"}"),
                Event("{\"event\":\"FSDJump\",\"StarPos\":[4,5,6]}"),
                Scan(310),
            ],
            enabled: false,
            allowPublishing: true
        );
        GreenGasGiantPublicationResult result = await coordinator.ApplyAsync(
            [Scan(310)],
            enabled: true,
            allowPublishing: true
        );

        Assert.Single(client.Candidates);
        GreenGasGiantCandidate candidate = Assert.Single(result.Published);
        Assert.Equal("Later Cmdr", candidate.CommanderName);
        Assert.Equal(4, candidate.StarPosition.X);
    }

    [Fact]
    public async Task MissingContextAndNetworkErrorsAreNonFatalWarnings()
    {
        GreenGasGiantPublicationResult missingContext = await Create(new RecordingClient())
            .ApplyAsync([Scan(310)], enabled: true, allowPublishing: true);
        var failingClient = new RecordingClient { Error = new HttpRequestException("offline") };
        GreenGasGiantPublicationCoordinator coordinator = Create(failingClient);
        GreenGasGiantPublicationResult failed = await coordinator.ApplyAsync(
            [
                Event("{\"event\":\"Commander\",\"Name\":\"Cmdr\"}"),
                Event("{\"event\":\"Location\",\"StarPos\":[1,2,3]}"),
                Scan(310),
            ],
            enabled: true,
            allowPublishing: true
        );

        Assert.Empty(missingContext.Published);
        Assert.Single(missingContext.Warnings);
        Assert.Empty(failed.Published);
        Assert.Contains("offline", Assert.Single(failed.Warnings));
    }

    private static GreenGasGiantPublicationCoordinator Create(IGreenGasGiantClient client)
    {
        return new GreenGasGiantPublicationCoordinator(GreenGasGiantCriteriaCatalog.LoadEmbedded(), client);
    }

    private static JournalEventEnvelope Scan(double temperature)
    {
        return Event(
            "{\"event\":\"Scan\","
                + "\"PlanetClass\":\"Sudarsky class III gas giant\","
                + $"\"SurfaceTemperature\":{temperature}}}"
        );
    }

    private static JournalEventEnvelope Event(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? result, out _));
        return result!;
    }

    private sealed class RecordingClient : IGreenGasGiantClient
    {
        public List<GreenGasGiantCandidate> Candidates { get; } = [];

        public Exception? Error { get; init; }

        public Task PublishAsync(GreenGasGiantCandidate candidate, CancellationToken cancellationToken = default)
        {
            if (Error is not null)
            {
                return Task.FromException(Error);
            }

            Candidates.Add(candidate);
            return Task.CompletedTask;
        }
    }
}
