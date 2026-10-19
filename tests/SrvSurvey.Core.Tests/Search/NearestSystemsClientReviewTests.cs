using System.Net;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed partial class NearestSystemsClientTests
{
    [Fact]
    public async Task TruncatedFallbackStreamPreservesSuccessfulSpanshResults()
    {
        var handler = new StubHandler(request =>
            request.Method == HttpMethod.Post
                ? Json("{\"results\":[" + Body("Known", 1, "Albens Bell Mollusc") + "]}")
                : BrokenResponse()
        );
        NearestSystemsSearchResult result = await Create(handler)
            .SearchCodexAsync(
                new(0, 0, 0),
                new("Mixed", [new("Albens Bell Mollusc", "Biology"), new("Unknown feature", null)], true),
                "Cmdr"
            );
        Assert.Equal("Known", Assert.Single(result.Rows).SystemName);
        Assert.Contains("incomplete", result.CoverageNote);
    }

    [Theory]
    [InlineData("http", false)]
    [InlineData("json", false)]
    [InlineData("timeout", false)]
    [InlineData("stream", false)]
    [InlineData("http", true)]
    [InlineData("json", true)]
    [InlineData("timeout", true)]
    [InlineData("stream", true)]
    public async Task FailedRecallRetainsFirstPageAndReportsPartialCoverage(string failure, bool grouped)
    {
        int requests = 0;
        string first =
            "{\"count\":40,\"search_reference\":\"saved\",\"results\":["
            + string.Join(',', Enumerable.Range(0, 20).Select(i => Body("System " + i % 16, i, "Albens Bell Mollusc")))
            + "]}";
        var handler = new StubHandler(request =>
        {
            requests++;
            if (request.Method == HttpMethod.Post)
            {
                return Json(first);
            }
            return failure switch
            {
                "http" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                "json" => Json("invalid"),
                "timeout" => throw new TaskCanceledException("Provider timeout"),
                _ => BrokenResponse(),
            };
        });
        NearestSystemsClient client = Create(handler);
        NearestSystemsSearchResult result = grouped
            ? await client.SearchCodexAsync(new(0, 0, 0), new CodexSearchCatalog().Resolve("Bell Mollusc"), "Cmdr")
            : await client.SearchLandmarksAsync(new(0, 0, 0), new(null, ["Albens Bell Mollusc"], []));
        Assert.Equal(16, result.Rows.Count);
        Assert.Equal(2, requests);
        Assert.Equal("saved", result.SpanshSearchReference);
        Assert.All(result.Rows, row => Assert.Equal("saved", row.SpanshSearchReference));
        Assert.Contains("incomplete", result.CoverageNote);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellationDuringRecallStillPropagates(bool grouped)
    {
        using var cancellation = new CancellationTokenSource();
        string first =
            "{\"count\":40,\"search_reference\":\"saved\",\"results\":["
            + string.Join(',', Enumerable.Range(0, 20).Select(i => Body("Same", i, "Albens Bell Mollusc")))
            + "]}";
        var handler = new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Json(first);
            }
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });
        NearestSystemsClient client = Create(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            grouped
                ? client.SearchCodexAsync(
                    new(0, 0, 0),
                    new CodexSearchCatalog().Resolve("Bell Mollusc"),
                    "Cmdr",
                    cancellation.Token
                )
                : client.SearchLandmarksAsync(new(0, 0, 0), new(null, ["Albens Bell Mollusc"], []), cancellation.Token)
        );
    }

    [Fact]
    public async Task CallerCancellationWinsOverAConcurrentStreamFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new StubHandler(_ => BrokenResponse(cancellation.Cancel));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Create(handler)
                .SearchCodexAsync(
                    new(0, 0, 0),
                    new CodexSearchCatalog().Resolve("Albens Bell Mollusc"),
                    "Cmdr",
                    cancellation.Token
                )
        );
    }

    private static HttpResponseMessage BrokenResponse(Action? beforeRead = null) =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new BrokenReadStream(beforeRead)) };

    private sealed class BrokenReadStream(Action? beforeRead) : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            beforeRead?.Invoke();
            return ValueTask.FromException<int>(
                new HttpIOException(HttpRequestError.ResponseEnded, "Truncated response")
            );
        }
    }
}
