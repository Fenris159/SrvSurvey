using System.Net;
using System.Text;
using System.Text.Json;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class SpanshBoxelClientTests
{
    [Fact]
    public async Task SearchPagesAndMapsTheSystemsSearchContract()
    {
        var firstPageSystems = string.Join(
            ',',
            Enumerable.Range(0, 50).Select(number => $$"""
                {
                  "id64": {{1000 + number}},
                  "name": "Praea Euq IL-P c5-{{number}}",
                  "x": {{number}},
                  "y": 2,
                  "z": 3,
                  "updated_at": "2026-07-01 12:00:00+00",
                  "bodies": [{}]
                }
                """));
        var handler = new QueueHandler(
            $$"""{"count":51,"from":0,"size":50,"results":[{{firstPageSystems}}]}""",
            """
            {
              "count": 51,
              "from": 50,
              "size": 1,
              "results": [
                {
                  "id64": 1050,
                  "name": "Praea Euq IL-P c5-50",
                  "x": 50,
                  "y": 2,
                  "z": 3,
                  "updated_at": "2026-07-02T12:00:00Z",
                  "bodies": []
                },
                {
                  "id64": 9999,
                  "name": "Sol",
                  "x": 0,
                  "y": 0,
                  "z": 0
                }
              ]
            }
            """);
        var client = new SpanshBoxelClient(
            new HttpClient(handler),
            new Uri("https://example.test/api/"));

        var systems = await client.SearchAsync(
            BoxelAddress.Parse("Praea Euq IL-P c5-0"));

        Assert.Equal(51, systems.Count);
        Assert.Equal("Praea Euq IL-P c5-0", systems[0].Boxel.Name);
        Assert.Equal(1000, systems[0].Boxel.SystemAddress);
        Assert.Equal(new GalacticCoordinate(0, 2, 3), systems[0].Position);
        Assert.True(systems[0].HasKnownBodies);
        Assert.Equal(
            DateTimeOffset.Parse("2026-07-01T12:00:00Z"),
            systems[0].SpanshUpdatedAt);
        Assert.False(systems[^1].HasKnownBodies);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(
            handler.Requests,
            request => Assert.Equal(
                "https://example.test/api/systems/search",
                request.Uri.AbsoluteUri));

        using var firstRequest = JsonDocument.Parse(handler.Requests[0].Content);
        Assert.Equal(0, firstRequest.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(50, firstRequest.RootElement.GetProperty("size").GetInt32());
        Assert.Equal(
            "Praea Euq IL-P c5-*",
            firstRequest.RootElement
                .GetProperty("filters")
                .GetProperty("name")
                .GetProperty("value")
                .GetString());
        Assert.Equal(
            "asc",
            firstRequest.RootElement
                .GetProperty("sort")[0]
                .GetProperty("name")
                .GetProperty("direction")
                .GetString());

        using var secondRequest = JsonDocument.Parse(handler.Requests[1].Content);
        Assert.Equal(1, secondRequest.RootElement.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task SearchRejectsHttpFailures()
    {
        var client = new SpanshBoxelClient(
            new HttpClient(new QueueHandler(
                (HttpStatusCode.ServiceUnavailable, "{}"),
                (HttpStatusCode.ServiceUnavailable, "{}"),
                (HttpStatusCode.ServiceUnavailable, "{}"))),
            new Uri("https://example.test/api/"),
            static (_, _) => Task.CompletedTask);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.SearchAsync(BoxelAddress.Parse("Praea Euq IL-P c5-0")));
    }

    [Fact]
    public async Task SearchRetriesTransientPageFailureWithoutSkippingResults()
    {
        var handler = new QueueHandler(
            (HttpStatusCode.TooManyRequests, "{}"),
            (HttpStatusCode.OK, """
                {
                  "count": 1,
                  "from": 0,
                  "size": 1,
                  "results": [
                    {
                      "id64": 1000,
                      "name": "Praea Euq IL-P c5-0",
                      "x": 1,
                      "y": 2,
                      "z": 3,
                      "updated_at": "2026-07-01T12:00:00Z",
                      "bodies": [{}]
                    }
                  ]
                }
                """));
        var delays = new List<TimeSpan>();
        var client = new SpanshBoxelClient(
            new HttpClient(handler),
            new Uri("https://example.test/api/"),
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        var systems = await client.SearchAsync(
            BoxelAddress.Parse("Praea Euq IL-P c5-0"));

        Assert.Single(systems);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal([TimeSpan.FromMilliseconds(250)], delays);
        using var first = JsonDocument.Parse(handler.Requests[0].Content);
        using var retry = JsonDocument.Parse(handler.Requests[1].Content);
        Assert.Equal(
            first.RootElement.GetRawText(),
            retry.RootElement.GetRawText());
    }

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode StatusCode, string Content)> responses;

        public QueueHandler(params string[] responses)
            : this(responses.Select(response => (HttpStatusCode.OK, response)).ToArray())
        {
        }

        public QueueHandler(HttpStatusCode statusCode, string content)
            : this((statusCode, content))
        {
        }

        public QueueHandler(
            params (HttpStatusCode StatusCode, string Content)[] responses)
        {
            this.responses = new Queue<(HttpStatusCode, string)>(responses);
        }

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var content = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.RequestUri!, content));
            var response = responses.Count > 1
                ? responses.Dequeue()
                : responses.Peek();
            return new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(
                    response.Content,
                    Encoding.UTF8,
                    "application/json"),
                RequestMessage = request,
            };
        }
    }

    private sealed record CapturedRequest(Uri Uri, string Content);
}
