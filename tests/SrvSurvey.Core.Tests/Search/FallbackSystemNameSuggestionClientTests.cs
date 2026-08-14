using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class FallbackSystemNameSuggestionClientTests
{
    [Fact]
    public async Task UsesFallbackWhenPrimaryIsUnavailable()
    {
        var fallbackResults = new[]
        {
            new SystemNameSuggestion("Sol", 10477373803, "Ardent"),
        };
        var primary = new StubClient(
            _ => throw new HttpRequestException("EDSM unavailable"));
        var fallback = new StubClient(_ => Task.FromResult<
            IReadOnlyList<SystemNameSuggestion>>(fallbackResults));
        var client = new FallbackSystemNameSuggestionClient(primary, fallback);

        var results = await client.SearchAsync("Sol");

        Assert.Same(fallbackResults, results);
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(1, fallback.CallCount);
    }

    [Fact]
    public async Task EmptyPrimaryResultDoesNotQueryFallback()
    {
        var primary = new StubClient(_ => Task.FromResult<
            IReadOnlyList<SystemNameSuggestion>>([]));
        var fallback = new StubClient(_ => Task.FromResult<
            IReadOnlyList<SystemNameSuggestion>>(
            [new SystemNameSuggestion("Sol", 10477373803, "Ardent")]));
        var client = new FallbackSystemNameSuggestionClient(primary, fallback);

        var results = await client.SearchAsync("No match");

        Assert.Empty(results);
        Assert.Equal(0, fallback.CallCount);
    }

    [Fact]
    public async Task UserCancellationDoesNotQueryFallback()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var primary = new StubClient(
            token => Task.FromCanceled<IReadOnlyList<SystemNameSuggestion>>(token));
        var fallback = new StubClient(_ => Task.FromResult<
            IReadOnlyList<SystemNameSuggestion>>([]));
        var client = new FallbackSystemNameSuggestionClient(primary, fallback);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SearchAsync("Sol", cancellation.Token));
        Assert.Equal(0, fallback.CallCount);
    }

    [Fact]
    public async Task InternalCancellationUsesFallbackWhenCallerIsNotCancelled()
    {
        var fallbackResults = new[]
        {
            new SystemNameSuggestion("Sol", 10477373803, "Ardent"),
        };
        var primary = new StubClient(
            _ => throw new TaskCanceledException("Provider timeout"));
        var fallback = new StubClient(_ => Task.FromResult<
            IReadOnlyList<SystemNameSuggestion>>(fallbackResults));
        var client = new FallbackSystemNameSuggestionClient(primary, fallback);

        var results = await client.SearchAsync("Sol");

        Assert.Same(fallbackResults, results);
        Assert.Equal(1, fallback.CallCount);
    }

    [Fact]
    public async Task UnexpectedFailurePropagatesWithoutQueryingFallback()
    {
        var primary = new StubClient(
            _ => throw new InvalidOperationException("Unexpected"));
        var fallback = new StubClient(_ => Task.FromResult<
            IReadOnlyList<SystemNameSuggestion>>([]));
        var client = new FallbackSystemNameSuggestionClient(primary, fallback);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SearchAsync("Sol"));

        Assert.Equal(0, fallback.CallCount);
    }

    private sealed class StubClient(
        Func<CancellationToken, Task<IReadOnlyList<SystemNameSuggestion>>> search)
        : ISystemNameSuggestionClient
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<SystemNameSuggestion>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return search(cancellationToken);
        }
    }
}
