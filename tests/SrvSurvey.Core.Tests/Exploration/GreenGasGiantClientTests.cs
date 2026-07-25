using System.Net;
using System.Text.Json;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Exploration;

public sealed class GreenGasGiantClientTests
{
    [Fact]
    public async Task PublishesLegacyCompatiblePayload()
    {
        HttpRequestMessage? request = null;
        string? content = null;
        var handler = new StubHandler(async value =>
        {
            request = value;
            content = await value.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = new GreenGasGiantClient(
            new HttpClient(handler),
            new Uri("https://example.test/root/"));

        await client.PublishAsync(new GreenGasGiantCandidate(
            "Test Cmdr",
            "likely",
            new GalacticCoordinate(1.5, -2, 3),
            "{\"event\":\"Scan\"}"));

        Assert.Equal(HttpMethod.Put, request!.Method);
        Assert.Equal("/root/api/ggg/create", request.RequestUri!.AbsolutePath);
        using var json = JsonDocument.Parse(content!);
        var root = json.RootElement;
        Assert.Equal("Test Cmdr", root.GetProperty("cmdr").GetString());
        Assert.Equal("likely", root.GetProperty("tag").GetString());
        Assert.Equal([1.5, -2, 3], root.GetProperty("starPos")
            .EnumerateArray().Select(value => value.GetDouble()).ToArray());
        Assert.Equal(
            "{\"event\":\"Scan\"}",
            root.GetProperty("json").GetString());
    }

    [Fact]
    public async Task RejectsFailedResponseWithBoundedDetail()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(
            HttpStatusCode.BadRequest)
        {
            Content = new StringContent(new string('x', 1_000)),
        });
        var client = new GreenGasGiantClient(
            new HttpClient(handler),
            new Uri("https://example.test/"));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.PublishAsync(new GreenGasGiantCandidate(
                "Cmdr",
                "likely",
                new GalacticCoordinate(1, 2, 3),
                "{}")));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.True(exception.Message.Length < 700);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>>
            response;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
            : this(request => Task.FromResult(response(request)))
        {
        }

        public StubHandler(
            Func<HttpRequestMessage, Task<HttpResponseMessage>> response)
        {
            this.response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return response(request);
        }
    }
}
