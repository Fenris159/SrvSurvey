using System.Net;
using System.Net.Http;
using SrvSurvey.Core.Diagnostics;

namespace SrvSurvey.Core.Tests.Diagnostics;

public sealed class ProviderFailureLogTests
{
    [Fact]
    public void RepeatedFailuresOfTheSameKindBecomeOneLine()
    {
        var log = new ProviderFailureLog();
        log.Record(
            "Ardent",
            "system/name/Sol/commodities/imports",
            new HttpRequestException("nope", null, HttpStatusCode.TooManyRequests)
        );
        log.Record(
            "Ardent",
            "system/name/Wille/commodities/imports",
            new HttpRequestException("later", null, HttpStatusCode.TooManyRequests)
        );
        log.Record("Spansh", "stations/search", new InvalidDataException("bad payload"));

        IReadOnlyList<string> lines = log.Drain();

        Assert.Equal(2, lines.Count);
        Assert.Contains(
            lines,
            line =>
                line.Contains("Ardent system imports failed 2 times", StringComparison.Ordinal)
                && line.Contains("Wille", StringComparison.Ordinal)
        );
        Assert.Contains(lines, line => line.StartsWith("Spansh station search failed:", StringComparison.Ordinal));
        Assert.Empty(log.Drain());
    }

    [Fact]
    public void CancellationIsNotAProviderFailure()
    {
        var log = new ProviderFailureLog();
        log.Record("Spansh", "bodies/search", new OperationCanceledException());

        Assert.Empty(log.Drain());
    }

    [Fact]
    public void TimedOutCancellationIsRecordedAndConcurrentFailuresAreCounted()
    {
        var log = new ProviderFailureLog();
        Parallel.For(
            0,
            100,
            _ =>
                log.Record("Ardent", "commodities", new OperationCanceledException("timed out", new TimeoutException()))
        );

        string line = Assert.Single(log.Drain());
        Assert.Contains("failed 100 times", line, StringComparison.Ordinal);
        Assert.Contains("TimeoutException", line, StringComparison.Ordinal);
    }
}
