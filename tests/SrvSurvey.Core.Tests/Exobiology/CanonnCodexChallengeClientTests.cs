using System.Net;
using System.Text;
using SrvSurvey.Core.Exobiology;

namespace SrvSurvey.Core.Tests.Exobiology;

public sealed class CanonnCodexChallengeClientTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-CanonnCodex-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ParsesChallengeGroupsAndEscapesCommanderName()
    {
        Uri? requestedUri = null;
        using var client = new HttpClient(new StubHandler(request =>
        {
            requestedUri = request.RequestUri;
            return JsonResponse(
                """
                {
                  "Aleoids":{
                    "hud_category":"Biology",
                    "types_found":["Aleoida Arcus - Green","Aleoida Arcus - Green"]
                  },
                  "Empty":{"hud_category":"Biology","types_found":null}
                }
                """);
        }));
        var challengeClient = new CanonnCodexChallengeClient(
            client,
            new Uri("https://example.test/challenge/status"));

        var result = await challengeClient.GetAsync("Cmdr Test/One");

        Assert.True(result.IsSuccess);
        var group = Assert.Single(result.Groups);
        Assert.Equal("Biology", group.HudCategory);
        Assert.Equal("Aleoida Arcus - Green", Assert.Single(group.FoundTypes));
        Assert.Equal("?cmdr=Cmdr%20Test%2FOne", requestedUri!.Query);
    }

    [Fact]
    public async Task ImportMatchesReferenceAndIsIdempotent()
    {
        var catalog = new ExobiologyReferenceCatalog(
        [
            new ExobiologyReference(
                2310101,
                "$Codex_Ent_Aleoids_01_B_Name;",
                "$Codex_Ent_Aleoids_01_Name;",
                "Aleoida Arcus - Green",
                1,
                HudCategory: "Biology"),
            new ExobiologyReference(
                2310206,
                "$Codex_Ent_Aleoids_02_L_Name;",
                "$Codex_Ent_Aleoids_02_Name;",
                "Aleoida Coronamus - Lime",
                2,
                HudCategory: "Biology"),
        ]);
        var store = new CommanderCodexStore(temporaryDirectory);
        await store.TrackAsync(new CommanderCodexTrackRequest
    {
        FrontierId = "F123",
        CommanderName = "Cmdr Test",
        EntryId = 2310101,
        Timestamp = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        SystemAddress = 42,
        BodyId = 3
    });
        var importer = new CanonnCodexChallengeImporter(
            new StubChallengeClient(new CanonnCodexChallengeLoadResult(
            [
                new CanonnCodexChallengeGroup(
                    "Biology",
                    [
                        "Aleoida Arcus - Green",
                        "Aleoida Coronamus - Lime",
                        "Unknown organism",
                    ]),
            ],
            null)),
            store,
            catalog);

        var first = await importer.ImportAsync("F123", "Cmdr Test");
        var second = await importer.ImportAsync("F123", "Cmdr Test");

        Assert.True(first.IsSuccess);
        Assert.Equal(2, first.MatchedEntryCount);
        Assert.Equal(1, first.AddedEntryCount);
        Assert.Equal(1, first.UnmatchedEntryCount);
        Assert.Equal(0, second.AddedEntryCount);
        var loaded = await store.LoadAsync("F123", null);
        Assert.Equal(42, loaded.Data!.Firsts[2310101].SystemAddress);
        Assert.Equal(-1, loaded.Data.Firsts[2310206].SystemAddress);
    }

    [Fact]
    public async Task TimesOutWhenCanonnDoesNotRespond()
    {
        using var client = new HttpClient(new BlockingHandler());
        var challengeClient = new CanonnCodexChallengeClient(
            client,
            new Uri("https://example.test/challenge/status"),
            TimeSpan.FromMilliseconds(25));

        var result = await challengeClient.GetAsync("Cmdr Test");

        Assert.False(result.IsSuccess);
        Assert.Contains("timed out", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }

    private sealed class StubChallengeClient(
        CanonnCodexChallengeLoadResult result)
        : ICanonnCodexChallengeClient
    {
        public Task<CanonnCodexChallengeLoadResult> GetAsync(
            string commanderName,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Cancellation did not stop the request.");
        }
    }
}
