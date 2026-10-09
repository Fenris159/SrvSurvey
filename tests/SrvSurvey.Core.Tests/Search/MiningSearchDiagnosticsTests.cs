using System.Net;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class MiningSearchDiagnosticsTests
{
    [Fact]
    public void NestedSearchesResetAndFlushOnlyTheirOwnFailuresAndPriceAvailability()
    {
        var parentLines = new List<string>();
        var childLines = new List<string>();
        Action<string>? output = parentLines.Add;
        var diagnostics = new MiningSearchDiagnostics(() => output);
        using (diagnostics.Begin())
        {
            diagnostics.Record("Ardent", "system/name/Parent/commodities/imports", new IOException("parent"));
            diagnostics.PriceMarksUnavailable = true;
            output = childLines.Add;
            using (diagnostics.Begin())
            {
                Assert.False(diagnostics.PriceMarksUnavailable);
                diagnostics.Record("Spansh", "bodies/search", new IOException("discarded child"));
                diagnostics.PriceMarksUnavailable = true;
                diagnostics.Reset();
                Assert.False(diagnostics.PriceMarksUnavailable);
                diagnostics.Record("Spansh", "bodies/search", new IOException("retained child"));
                diagnostics.Flush();
                Assert.Empty(parentLines);
            }

            Assert.True(diagnostics.PriceMarksUnavailable);
            diagnostics.Flush();
        }

        Assert.Contains("parent", Assert.Single(parentLines), StringComparison.Ordinal);
        Assert.Contains("retained child", Assert.Single(childLines), StringComparison.Ordinal);
        Assert.False(diagnostics.PriceMarksUnavailable);
    }

    [Fact]
    public void SearchScopePreservesUnscopedFailuresAndSupportsMissingOrThrowingLoggers()
    {
        var lines = new List<string>();
        Action<string>? output = lines.Add;
        var diagnostics = new MiningSearchDiagnostics(() => output);
        diagnostics.Record("Ardent", "material-trader", new IOException("unscoped"));
        output = null;
        IDisposable silent = diagnostics.Begin();
        diagnostics.Record("Spansh", "systems/search", new IOException("silent"));
        output = _ => throw new InvalidOperationException("broken logger");
        silent.Dispose();
        silent.Dispose();
        using (diagnostics.Begin())
        {
            diagnostics.Record("Spansh", "bodies/search", new IOException("logged unsuccessfully"));
        }

        output = lines.Add;
        diagnostics.Flush();
        Assert.Contains("unscoped", Assert.Single(lines), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OverlappingSessionsOnTheSameProviderLogTheirOwnFailures()
    {
        var olderLines = new List<string>();
        var newerLines = new List<string>();
        using var http = new HttpClient(new FailedImports());
        using var provider = new MiningSearchClient(http) { DiagnosticLog = olderLines.Add };
        using var olderSession = new MiningSearchSession<object>(provider, "ring", () => new { });
        using var newerSession = new MiningSearchSession<object>(provider, "planetary", () => new { });
        var olderRecorded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOlder = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task older = olderSession.RunAsync(
            async token =>
            {
                await Assert.ThrowsAsync<HttpRequestException>(() =>
                    provider.FindSystemImportsAsync("Older", new MiningMarketQuery("Older", "Any", false), token)
                );
                olderRecorded.SetResult();
                await releaseOlder.Task;
            },
            _ => { }
        );
        await olderRecorded.Task;
        provider.DiagnosticLog = newerLines.Add;
        try
        {
            await newerSession.RunAsync(
                async token =>
                {
                    await Assert.ThrowsAsync<HttpRequestException>(() =>
                        provider.FindSystemImportsAsync("Newer", new MiningMarketQuery("Newer", "Any", false), token)
                    );
                    provider.FlushDiagnostics();
                },
                _ => { }
            );
            Assert.Empty(olderLines);
            Assert.Contains("Newer", Assert.Single(newerLines), StringComparison.Ordinal);
        }
        finally
        {
            releaseOlder.SetResult();
        }

        await older;
        Assert.Contains("Older", Assert.Single(olderLines), StringComparison.Ordinal);
        Assert.Single(newerLines);
    }

    [Fact]
    public async Task NestedSessionsKeepTheParentProviderFailuresUntilTheParentFinishes()
    {
        var parentLines = new List<string>();
        var childLines = new List<string>();
        using var http = new HttpClient(new FailedImports());
        using var provider = new MiningSearchClient(http) { DiagnosticLog = parentLines.Add };
        using var parent = new MiningSearchSession<object>(provider, "ring", () => new { });
        using var child = new MiningSearchSession<object>(provider, "planetary", () => new { });
        await parent.RunAsync(
            async token =>
            {
                await Assert.ThrowsAsync<HttpRequestException>(() =>
                    provider.FindSystemImportsAsync("Parent", new MiningMarketQuery("Parent", "Any", false), token)
                );
                provider.DiagnosticLog = childLines.Add;
                await child.RunAsync(
                    async childToken =>
                        await Assert.ThrowsAsync<HttpRequestException>(() =>
                            provider.FindSystemImportsAsync(
                                "Child",
                                new MiningMarketQuery("Child", "Any", false),
                                childToken
                            )
                        ),
                    _ => { },
                    cancellationToken: token
                );
                Assert.Empty(parentLines);
                Assert.Contains("Child", Assert.Single(childLines), StringComparison.Ordinal);
            },
            _ => { }
        );

        Assert.Contains("Parent", Assert.Single(parentLines), StringComparison.Ordinal);
        Assert.Single(childLines);
    }

    private sealed class FailedImports : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway));
    }
}
