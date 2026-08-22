using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class BoxelPlatformAdapterTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-BoxelPlatformTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ClipboardAdapterTracksWriterAvailabilityAndForwardsText()
    {
        var writes = new List<string>();
        var adapter = new BoxelClipboardAdapter();

        Assert.False(adapter.IsReady);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.WriteTextAsync("Praea Euq IL-P c5-0"));
        await Assert.ThrowsAsync<ArgumentException>(() => adapter.WriteTextAsync(" "));
        await Assert.ThrowsAsync<OperationCanceledException>(() => adapter.WriteTextAsync(
            "Praea Euq IL-P c5-0",
            new CancellationToken(canceled: true)));

        adapter.SetWriter(text =>
        {
            writes.Add(text);
            return Task.CompletedTask;
        });

        Assert.True(adapter.IsReady);
        await adapter.WriteTextAsync("Praea Euq IL-P c5-0");
        Assert.Equal(["Praea Euq IL-P c5-0"], writes);

        adapter.SetWriter(null);
        Assert.False(adapter.IsReady);
    }

    [Fact]
    public void DiagnosticSinkWritesOptionalContextAndExceptionDetails()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var log = new ApplicationLogService(temporaryDirectory);
        var sink = new ApplicationLogBoxelSearchDiagnosticSink(log);

        sink.Report(new BoxelSearchDiagnostic(
            BoxelSearchHealthSubsystem.Resolver,
            BoxelSearchMessageCode.RefreshFailed,
            null,
            DateTimeOffset.UtcNow));
        sink.Report(new BoxelSearchDiagnostic(
            BoxelSearchHealthSubsystem.Clipboard,
            BoxelSearchMessageCode.ClipboardFailed,
            new InvalidOperationException("broken"),
            DateTimeOffset.UtcNow,
            "Praea Euq IL-P c5-0"));

        Assert.Contains(
            "Boxel search Resolver/RefreshFailed",
            log.Entries[0],
            StringComparison.Ordinal);
        Assert.Contains(
            "Boxel search Clipboard/ClipboardFailed (Praea Euq IL-P c5-0): "
                + "System.InvalidOperationException: broken",
            log.Entries[1],
            StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => sink.Report(null!));
        Assert.Throws<ArgumentNullException>(() =>
            new ApplicationLogBoxelSearchDiagnosticSink(null!));
    }

    [Fact]
    public void DiagnosticSinkKeepsExpectedResolverTimeoutsConcise()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var log = new ApplicationLogService(temporaryDirectory);
        var sink = new ApplicationLogBoxelSearchDiagnosticSink(log);
        var timeout = new TaskCanceledException(
            "The request was canceled due to the configured HttpClient.Timeout "
                + "of 20 seconds elapsing.",
            new TimeoutException("The operation was canceled."));

        sink.Report(new BoxelSearchDiagnostic(
            BoxelSearchHealthSubsystem.Resolver,
            BoxelSearchMessageCode.RefreshFailed,
            timeout,
            DateTimeOffset.UtcNow,
            "Leamae UK-C b2-"));

        var entry = Assert.Single(log.Entries);
        Assert.Contains(timeout.Message, entry, StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(TaskCanceledException),
            entry,
            StringComparison.Ordinal);
        Assert.DoesNotContain(Environment.NewLine, entry, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
