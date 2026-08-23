using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Core.Diagnostics.Replay;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class JournalHistoryViewModelTests
{
    [Fact]
    public void ReplayCalendarBindingsUseCalendarCompatibleDateTypes()
    {
        var selectedDateType = CalendarDatePicker.SelectedDateProperty.PropertyType;

        Assert.Equal(typeof(DateTime?), selectedDateType);
        string[] calendarProperties =
        [
            nameof(JournalHistoryViewModel.RangeFromDate),
            nameof(JournalHistoryViewModel.RangeToDate),
            nameof(JournalHistoryViewModel.RangeMinimumDate),
            nameof(JournalHistoryViewModel.RangeMaximumDate),
            nameof(JournalHistoryViewModel.RangeToMaximumDate),
        ];
        Assert.All(calendarProperties, propertyName => Assert.Equal(
            selectedDateType,
            typeof(JournalHistoryViewModel)
                .GetProperty(propertyName)!
                .PropertyType));
    }

    [Fact]
    public void ReplayCalendarSelectionsPreserveUtcJournalTime()
    {
        using var temp = new TemporaryDirectory();
        using var viewModel = new JournalHistoryViewModel(
            temp.Path,
            "test-build");

        viewModel.RangeFrom = DateTimeOffset.Parse(
            "2026-08-21T00:30:45+02:00");

        Assert.Equal(new DateTime(2026, 8, 20), viewModel.RangeFromDate);
        Assert.Equal(new TimeSpan(22, 30, 45), viewModel.RangeFromTime);

        viewModel.RangeFromDate = new DateTime(2026, 8, 19);
        viewModel.RangeFromTime = new TimeSpan(10, 11, 12);

        Assert.Equal(
            DateTimeOffset.Parse("2026-08-19T10:11:12Z"),
            viewModel.RangeFrom);
    }

    [Fact]
    public void SelectionDetailsAreEmptyUntilAnEventIsSelected()
    {
        using var temp = new TemporaryDirectory();
        using var viewModel = new JournalHistoryViewModel(
            temp.Path,
            "test-build");
        List<string?> changedProperties = [];
        viewModel.PropertyChanged += (_, args) =>
            changedProperties.Add(args.PropertyName);

        Assert.Equal(string.Empty, viewModel.SelectedEventFileName);
        Assert.Equal(string.Empty, viewModel.SelectedEventCommanderName);
        Assert.Equal(string.Empty, viewModel.SelectedEventSystemName);
        Assert.Null(viewModel.SelectedEventTimestamp);
        Assert.Equal(string.Empty, viewModel.SelectedEventRawJson);

        var timestamp = DateTimeOffset.Parse(
            "2026-08-21T18:01:00Z",
            System.Globalization.CultureInfo.InvariantCulture);
        viewModel.SelectedEvent = new JournalHistoryEvent(
            0,
            "Journal.01.log",
            timestamp,
            "FSDJump",
            "History Cmdr",
            "Sol",
            "{\"event\":\"FSDJump\"}");

        Assert.Equal("Journal.01.log", viewModel.SelectedEventFileName);
        Assert.Equal("History Cmdr", viewModel.SelectedEventCommanderName);
        Assert.Equal("Sol", viewModel.SelectedEventSystemName);
        Assert.Equal(timestamp, viewModel.SelectedEventTimestamp);
        Assert.Equal(
            "{\"event\":\"FSDJump\"}",
            viewModel.SelectedEventRawJson);
        Assert.Contains(nameof(viewModel.SelectedEventFileName), changedProperties);
        Assert.Contains(nameof(viewModel.SelectedEventCommanderName), changedProperties);
        Assert.Contains(nameof(viewModel.SelectedEventSystemName), changedProperties);
        Assert.Contains(nameof(viewModel.SelectedEventTimestamp), changedProperties);
        Assert.Contains(nameof(viewModel.SelectedEventRawJson), changedProperties);
    }

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
        viewModel.RangeFrom = DateTimeOffset.Parse("2026-08-21T18:00:30Z");
        viewModel.RangeTo = DateTimeOffset.Parse("2026-08-21T18:01:30Z");
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
    public async Task ReplayRangeDefaultsToRecentEventsAndClampsBroadSelections()
    {
        using var temp = new TemporaryDirectory();
        await File.WriteAllLinesAsync(
            Path.Combine(temp.Path, "Journal.2026-08-21T180000.01.log"),
            [
                "{\"timestamp\":\"2026-06-01T12:00:00Z\",\"event\":\"Commander\",\"Name\":\"History Cmdr\",\"FID\":\"F123456\"}",
                "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"Sol\"}",
            ]);
        using var viewModel = new JournalHistoryViewModel(
            temp.Path,
            "test-build");

        await viewModel.RefreshAsync();

        Assert.Equal(
            DateTimeOffset.Parse("2026-08-20T18:00:00Z"),
            viewModel.RangeFrom);
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-21T18:00:00Z"),
            viewModel.RangeTo);

        viewModel.RangeFromDate = DateTime.Parse("2026-06-01");
        viewModel.RangeFromTime = TimeSpan.FromHours(12);
        viewModel.RangeToDate = DateTime.Parse("2026-08-21");
        viewModel.RangeToTime = TimeSpan.FromHours(18);

        Assert.Equal(
            viewModel.RangeFrom + JournalHistoryViewModel.MaximumExportRange,
            viewModel.RangeTo);
        Assert.Contains("31 days", viewModel.ExportPreview);
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

    [Fact]
    public async Task WindowedHistoryReportsTheFullCountAndExportsOlderRanges()
    {
        using var temp = new TemporaryDirectory();
        await File.WriteAllLinesAsync(
            Path.Combine(temp.Path, "Journal.2026-08-21T180000.01.log"),
            [
                "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Commander\",\"Name\":\"History Cmdr\",\"FID\":\"F123456\"}",
                "{\"timestamp\":\"2026-08-21T18:01:00Z\",\"event\":\"Location\",\"StarSystem\":\"Older\"}",
                "{\"timestamp\":\"2026-08-21T18:02:00Z\",\"event\":\"Music\"}",
                "{\"timestamp\":\"2026-08-21T18:03:00Z\",\"event\":\"Shutdown\"}",
            ]);
        var historyReader = new JournalHistoryReader(maximumLoadedEvents: 2);
        using var viewModel = new JournalHistoryViewModel(
            temp.Path,
            "test-build",
            historyReader,
            new JournalReplayExporter());

        await viewModel.RefreshAsync();
        viewModel.RangeFrom = DateTimeOffset.Parse("2026-08-21T18:01:00Z");
        viewModel.RangeTo = DateTimeOffset.Parse("2026-08-21T18:01:00Z");
        var packagePath = Path.Combine(temp.Path, "older.srvreplay");

        Assert.Equal(4, viewModel.TotalEventCount);
        Assert.Equal(["Music", "Shutdown"], viewModel.Events
            .Select(item => item.EventName));
        Assert.Contains("most recent 2", viewModel.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scanned during export", viewModel.ExportPreview, StringComparison.OrdinalIgnoreCase);
        Assert.True(await viewModel.ExportAsync(packagePath));
    }

    [Fact]
    public async Task EmptyHistoryAndInvalidRangesRemainRecoverable()
    {
        using var temp = new TemporaryDirectory();
        using var empty = new JournalHistoryViewModel(temp.Path, "test-build");

        await empty.RefreshAsync();

        Assert.False(empty.HasEvents);
        Assert.Contains("No journal events", empty.Summary);
        Assert.Contains("No timestamped events", empty.ExportPreview);
        Assert.False(await empty.ExportAsync(
            Path.Combine(temp.Path, "empty.srvreplay")));

        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "Journal.01.log"),
            "{\"timestamp\":\"2026-08-21T18:00:00Z\",\"event\":\"Commander\",\"Name\":\"History Cmdr\",\"FID\":\"F123456\"}\n");
        using var populated = new JournalHistoryViewModel(
            temp.Path,
            "test-build");
        await populated.RefreshAsync();
        populated.RedactExport = false;
        Assert.Contains("remain raw", populated.ExportPreview);
        populated.SelectedEvent = populated.Events[0];
        populated.SearchText = "not present";
        Assert.Null(populated.SelectedEvent);

        populated.RangeFrom = DateTimeOffset.Parse("2026-08-21T18:01:00Z");
        populated.RangeTo = DateTimeOffset.Parse("2026-08-21T18:00:00Z");
        Assert.Equal(populated.RangeFrom, populated.RangeTo);
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
