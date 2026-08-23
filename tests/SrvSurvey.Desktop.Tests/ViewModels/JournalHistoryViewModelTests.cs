using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Core.Diagnostics.Replay;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class JournalHistoryViewModelTests
{
    [Fact]
    public async Task RefreshAndSearchExposeDurableJournalHistory()
    {
        using var temp = new TemporaryDirectory();
        await File.WriteAllLinesAsync(
            Path.Combine(temp.Path, "Journal.2026-08-21T180000.01.log"),
            [
                "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Commander\",\"Name\":\"History Cmdr\",\"FID\":\"F123456\"}",
                "{\"timestamp\":\"2026-08-21T18:01:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sol\",\"SystemAddress\":1}",
                "{\"timestamp\":\"2026-08-21T18:02:00Z\",\"event\":\"Scan\",\"BodyName\":\"Sol A\"}",
            ]);
        var viewModel = new JournalHistoryViewModel(temp.Path, "test-build");

        await viewModel.RefreshAsync();
        viewModel.SearchText = "FSDJump";

        Assert.Equal(3, viewModel.TotalEventCount);
        Assert.Single(viewModel.Events);
        Assert.Equal("FSDJump", viewModel.Events[0].EventName);
        Assert.Equal("Sol", viewModel.Events[0].SystemName);
        Assert.Contains("3 events", viewModel.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExactTimeRangePreviewsAndExportsOnlyTheIncidentWindow()
    {
        using var temp = new TemporaryDirectory();
        await File.WriteAllLinesAsync(
            Path.Combine(temp.Path, "Journal.2026-08-21T180000.01.log"),
            [
                "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Commander\",\"Name\":\"History Cmdr\",\"FID\":\"F123456\"}",
                "{\"timestamp\":\"2026-08-21T18:01:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sol\"}",
                "{\"timestamp\":\"2026-08-21T18:02:00Z\",\"event\":\"Scan\"}",
            ]);
        var viewModel = new JournalHistoryViewModel(temp.Path, "test-build");
        await viewModel.RefreshAsync();
        viewModel.RangeFromText = "2026-08-21T18:00:30Z";
        viewModel.RangeToText = "2026-08-21T18:01:30Z";
        var packagePath = Path.Combine(temp.Path, "incident.srvreplay");

        Assert.Contains(
            "1 selected event",
            viewModel.ExportPreview,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "sent and received chat",
            viewModel.ExportPreview,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "location names",
            viewModel.ExportPreview,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(await viewModel.ExportAsync(packagePath));
        var session = await new ReplaySessionManager().ImportAsync(
            packagePath,
            Path.Combine(temp.Path, "managed"),
            CancellationToken.None);

        Assert.Equal(
            ["Commander", "FSDJump"],
            session.Events.Select(item => item.EventName));
    }

    [Fact]
    public async Task LargeHistorySearchCompletesOffTheCallingContext()
    {
        using var temp = new TemporaryDirectory();
        var lines = Enumerable.Range(0, 6_000)
            .Select(index => index == 5_999
                ? "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"NeedleEvent\",\"Name\":\"History Cmdr\",\"FID\":\"F123456\"}"
                : $"{{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Scan\",\"BodyID\":{index}}}")
            .ToArray();
        await File.WriteAllLinesAsync(
            Path.Combine(temp.Path, "Journal.2026-08-21T180000.01.log"),
            lines);
        using var viewModel = new JournalHistoryViewModel(
            temp.Path,
            "test-build");
        await viewModel.RefreshAsync();

        viewModel.SearchText = "NeedleEvent";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (viewModel.Events.Count != 1)
        {
            await Task.Delay(10, timeout.Token);
        }

        Assert.Equal("NeedleEvent", viewModel.Events[0].EventName);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"SrvSurvey-journal-history-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup.
            }
        }
    }
}
