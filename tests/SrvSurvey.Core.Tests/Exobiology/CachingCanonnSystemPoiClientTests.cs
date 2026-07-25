using SrvSurvey.Core.Exobiology;

namespace SrvSurvey.Core.Tests.Exobiology;

public sealed class CachingCanonnSystemPoiClientTests
{
    [Fact]
    public async Task SharesConcurrentAndCompletedRequestsBySystemAndCommander()
    {
        var inner = new StubClient();
        var client = new CachingCanonnSystemPoiClient(inner);

        var first = client.GetAsync(" Test ", " Cmdr ");
        var second = client.GetAsync("test", "cmdr");
        inner.Complete(new CanonnSystemPoiResult("Test", []));

        Assert.Same(await first, await second);
        Assert.Equal(1, inner.CallCount);
        Assert.Same(await first, await client.GetAsync("TEST", "CMDR"));
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task DoesNotCacheFailures()
    {
        var inner = new StubClient();
        var client = new CachingCanonnSystemPoiClient(inner);
        var failed = client.GetAsync("Test", "Cmdr");
        inner.Fail(new HttpRequestException("offline"));
        await Assert.ThrowsAsync<HttpRequestException>(() => failed);

        var retry = client.GetAsync("Test", "Cmdr");
        inner.Complete(new CanonnSystemPoiResult("Test", []));

        Assert.Equal("Test", (await retry).SystemName);
        Assert.Equal(2, inner.CallCount);
    }

    private sealed class StubClient : ICanonnSystemPoiClient
    {
        private TaskCompletionSource<CanonnSystemPoiResult> completion =
            CreateCompletion();

        public int CallCount { get; private set; }

        public Task<CanonnSystemPoiResult> GetAsync(
            string systemName,
            string commanderName,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return completion.Task;
        }

        public void Complete(CanonnSystemPoiResult result)
        {
            completion.SetResult(result);
            completion = CreateCompletion();
        }

        public void Fail(Exception exception)
        {
            completion.SetException(exception);
            completion = CreateCompletion();
        }

        private static TaskCompletionSource<CanonnSystemPoiResult>
            CreateCompletion()
        {
            return new TaskCompletionSource<CanonnSystemPoiResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
