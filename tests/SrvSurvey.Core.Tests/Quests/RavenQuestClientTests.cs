using System.Net;
using System.Text;
using System.Text.Json;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Quests;

namespace SrvSurvey.Core.Tests.Quests;

public sealed class RavenQuestClientTests
{
    [Fact]
    public async Task LoadsPublishedDefinitionsWithOptionalApiKey()
    {
        var client = Create(new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(
                "/root/api/quest/published",
                request.RequestUri!.AbsolutePath);
            Assert.Equal(
                "secret-key",
                Assert.Single(request.Headers.GetValues("rcc-key")));
            return Json(
                """
                [{
                  "id":"sample",
                  "ver":1.5,
                  "publisher":"Raven",
                  "title":"Sample quest",
                  "duration":"Long",
                  "firstChapter":"start",
                  "objectives":{"scan":"Scan a thing"},
                  "msgs":[],
                  "chapters":{"start":"return true"},
                  "futureDefinition":42
                }]
                """);
        }));

        var quests = await client.GetPublishedQuestsAsync(" secret-key ");

        var quest = Assert.Single(quests);
        Assert.Equal("sample", quest.Id);
        Assert.Equal(RavenQuestDuration.Long, quest.Duration);
        Assert.Equal("Scan a thing", quest.Objectives["scan"]);
        Assert.Equal(42, quest.ExtensionData["futureDefinition"].GetInt32());
    }

    [Fact]
    public async Task LoadsSpecificDefinitionUsingEscapedInvariantIdentity()
    {
        var client = Create(new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(
                "/root/api/quest/Publisher%20Name/quest%2Fone/1.5/",
                request.RequestUri!.AbsolutePath);
            return Json(
                """
                {"id":"quest/one","ver":1.5,"publisher":"Publisher Name","title":"Quest"}
                """);
        }));

        var quest = await client.GetQuestAsync(
            new RavenQuestReference("Publisher Name", "quest/one", 1.5));

        Assert.Equal("Quest", quest?.Title);
    }

    [Fact]
    public async Task LoadsCommanderProgressWithoutDroppingFutureFields()
    {
        var client = Create(new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(
                "/root/api/quest/cmdr/load/active",
                request.RequestUri!.AbsolutePath);
            Assert.Equal(
                "secret-key",
                Assert.Single(request.Headers.GetValues("rcc-key")));
            return Json(
                """
                [{
                  "publisher":"Raven",
                  "id":"sample",
                  "ver":1.5,
                  "quest":{"publisher":"Raven","id":"sample","ver":1.5,"title":"Sample"},
                  "objectives":{"scan":"visible,1,3"},
                  "startTime":"2026-07-01T00:00:00Z",
                  "chapters":[{"id":"start","vars":{"visits":2},"futureChapter":true}],
                  "msgs":[{"id":"welcome","received":"2026-07-01T00:01:00Z","actions":["go"]}],
                  "vars":{"counter":42},
                  "keptLasts":{"Docked":{"event":"Docked"}},
                  "routes":[{"id":"route","w":2.5,"wp":[[1,2]]}],
                  "futureState":{"value":7}
                }]
                """);
        }));

        var quests = await client.LoadCommanderQuestsAsync(
            RavenQuestState.active,
            "secret-key");

        var quest = Assert.Single(quests);
        Assert.Equal("visible,1,3", quest.Objectives["scan"]);
        Assert.Equal(42, quest.Variables["counter"].GetInt32());
        Assert.Equal(
            "Docked",
            quest.KeptJournalEvents["Docked"].GetProperty("event").GetString());
        Assert.True(quest.Chapters[0].ExtensionData["futureChapter"].GetBoolean());
        Assert.Equal(
            7,
            quest.ExtensionData["futureState"].GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task SavesCommanderProgressWithLegacyPropertyNamesAndApiKey()
    {
        string? body = null;
        var client = Create(new StubHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(
                "/root/api/quest/cmdr/save/Raven/sample",
                request.RequestUri!.AbsolutePath);
            Assert.Equal(
                "secret-key",
                Assert.Single(request.Headers.GetValues("rcc-key")));
            body = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        var quest = new RavenCommanderQuest
        {
            Publisher = "Raven",
            Id = "sample",
            Version = 1.5,
            Quest = new RavenQuestDefinition
            {
                Publisher = "Raven",
                Id = "sample",
                Version = 1.5,
                Title = "Hydrated definition must not be posted",
            },
            Objectives = new Dictionary<string, string>
            {
                ["scan"] = "visible,1,3",
            },
            Variables = new Dictionary<string, JsonElement>
            {
                ["counter"] = JsonSerializer.SerializeToElement(42),
            },
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["futureState"] = JsonSerializer.SerializeToElement(true),
            },
        };

        await client.SaveCommanderQuestAsync(quest, "secret-key");

        using var document = JsonDocument.Parse(body!);
        var root = document.RootElement;
        Assert.Equal("Raven", root.GetProperty("publisher").GetString());
        Assert.Equal(1.5, root.GetProperty("ver").GetDouble());
        Assert.Equal(
            "visible,1,3",
            root.GetProperty("objectives").GetProperty("scan").GetString());
        Assert.Equal(
            42,
            root.GetProperty("vars").GetProperty("counter").GetInt32());
        Assert.True(root.GetProperty("futureState").GetBoolean());
        Assert.False(root.TryGetProperty("quest", out _));
        Assert.False(root.TryGetProperty("Reference", out _));
    }

    [Fact]
    public async Task CatalogStatusAndActivationUseLegacyEndpoints()
    {
        var requests = new List<(HttpMethod Method, string Path)>();
        var client = Create(new StubHandler(request =>
        {
            requests.Add((request.Method, request.RequestUri!.AbsolutePath));
            return request.Method == HttpMethod.Get
                ? Json(
                    """
                    [{"id":"sample","ver":1.5,"publisher":"Raven","state":"paused","stateChangedOn":"2026-07-01T00:00:00Z","future":true}]
                    """)
                : Json(
                    """
                    {"id":"sample","ver":1.5,"publisher":"Raven","title":"Sample"}
                    """);
        }));

        var statuses = await client.GetCommanderQuestStatusesAsync("key");
        var activated = await client.ActivateQuestAsync(
            "Raven",
            "sample",
            "key");

        Assert.Equal(RavenQuestState.paused, Assert.Single(statuses).State);
        Assert.Equal("Sample", activated.Title);
        Assert.Equal(
            [
                (HttpMethod.Get, "/root/api/quest/cmdr"),
                (HttpMethod.Put, "/root/api/quest/cmdr/Raven/sample"),
            ],
            requests);
    }

    [Fact]
    public async Task MutationAndChapterEndpointsMatchLegacyContract()
    {
        var requests = new List<(HttpMethod Method, string Path)>();
        var client = Create(new StubHandler(request =>
        {
            requests.Add((request.Method, request.RequestUri!.AbsolutePath));
            return request.RequestUri.AbsolutePath.Contains(
                "/chapter/",
                StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("function JournalEntry(entry) end"),
                }
                : new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        var reference = new RavenQuestReference("Raven", "sample", 1.5);

        Assert.True(await client.SetQuestStateAsync(
            "Raven",
            "sample",
            RavenQuestState.complete,
            "key"));
        Assert.True(await client.DeleteQuestAsync("Raven", "sample", "key"));
        var chapter = await client.GetQuestChapterAsync(
            reference,
            "start chapter",
            "key");

        Assert.Equal("function JournalEntry(entry) end", chapter);
        Assert.Equal(
            [
                (HttpMethod.Post, "/root/api/quest/cmdr/Raven/sample/state/complete"),
                (HttpMethod.Delete, "/root/api/quest/cmdr/Raven/sample"),
                (HttpMethod.Get, "/root/api/quest/Raven/sample/1.5/chapter/start%20chapter"),
            ],
            requests);
    }

    [Fact]
    public async Task PublishSendsDefinitionAndPreservesLegacyDurationCasing()
    {
        string? body = null;
        var client = Create(new StubHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(
                "/root/api/quest/publish",
                request.RequestUri!.AbsolutePath);
            Assert.Equal(
                "secret-key",
                Assert.Single(request.Headers.GetValues("rcc-key")));
            body = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                ReasonPhrase = "Published",
            };
        }));

        var result = await client.PublishQuestAsync(
            new RavenQuestDefinition
            {
                Publisher = "Raven",
                Id = "sample",
                Version = 1,
                Title = "Sample",
                Duration = RavenQuestDuration.Extended,
                FirstChapter = "start",
            },
            "secret-key");

        Assert.Equal("Published", result);
        Assert.Equal(
            "Extended",
            JsonDocument.Parse(body!).RootElement
                .GetProperty("duration")
                .GetString());
    }

    [Fact]
    public async Task LegacyUnavailableResponsesRemainNonFatalForReadSurfaces()
    {
        var client = Create(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        Assert.Empty(await client.GetPublishedQuestsAsync());
        Assert.Empty(await client.LoadCommanderQuestsAsync(
            RavenQuestState.active,
            "key"));
        Assert.Empty(await client.GetCommanderQuestStatusesAsync("key"));
        Assert.Null(await client.GetQuestChapterAsync(
            new RavenQuestReference("Raven", "sample", 1),
            "start"));
    }

    [Fact]
    public async Task ServiceFailuresExposeStatusAndOperation()
    {
        var client = Create(new StubHandler(_ => new HttpResponseMessage(
            HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("maintenance"),
        }));

        var exception = await Assert.ThrowsAsync<RavenColonialServiceException>(
            () => client.ActivateQuestAsync("Raven", "sample", "key"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Equal("activate a quest", exception.Operation);
        Assert.Contains("maintenance", exception.Message);
    }

    private static RavenQuestClient Create(HttpMessageHandler handler)
    {
        return new RavenQuestClient(
            new HttpClient(handler),
            new Uri("https://example.test/root/"));
    }

    private static HttpResponseMessage Json(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>>
            send;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> send)
            : this(request => Task.FromResult(send(request)))
        {
        }

        public StubHandler(
            Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
        {
            this.send = send;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return send(request);
        }
    }
}
