using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ColonizationProjectPreviewViewModelTests
{
    private static readonly string[] CombinedBuildIds = ["first", "second"];

    /// <summary>Refreshes all distinct workspace builds, aggregating progress, carrier stock, linked commanders, and export sections.</summary>
    [Fact]
    public async Task RefreshesCombinedReportAndTracksWorkspaceMembership()
    {
        IReadOnlyList<string> ids = ["first", "SECOND", "First"];
        var reads = new List<string>();
        using var model = new ColonizationProjectPreviewViewModel(
            new Reader(
                (id, _) =>
                {
                    reads.Add(id);
                    ColonizationProjectPreviewData data = Data();
                    return Task.FromResult<ColonizationProjectPreviewData?>(
                        data with
                        {
                            Project = data.Project with { BuildId = id, BuildName = id },
                        }
                    );
                }
            ),
            () => ids,
            currentShipCapacity: () => 128
        );
        await model.RefreshAsync();
        Assert.Equal(2, reads.Count);
        Assert.Equal("Combined Build Report", model.Title);
        Assert.Equal("Build projects in this report", model.DetailsTitle);
        Assert.Contains("grouped by system", model.EffectsDescription);
        Assert.Contains("2 build projects", model.Subtitle);
        Assert.Equal("400", model.RemainingText);
        Assert.Equal("300", model.DeficitText);
        Assert.Equal("100", model.ReadyText);
        Assert.Equal("400", model.DeliveredText);
        Assert.Equal(12.5, model.ReadyProgress);
        Assert.Equal(2, model.Details.Count);
        Assert.Single(model.Commanders);
        Assert.Single(model.CarrierDetails);
        Assert.True(model.IsCombinedReport);
        Assert.Contains(
            Assert.Single(model.EffectGroups).Fields,
            field => field.Label == "Security" && field.Value == "+20"
        );
        Assert.Equal("60", Assert.Single(model.DeliveryHistory).Cargo);
        Assert.Contains(
            model.Effects,
            field => field.Label.EndsWith("Security", StringComparison.Ordinal) && field.Value == "+20"
        );
        await model.ExportAsync(
            (name, csv) =>
            {
                Assert.StartsWith("Raven-combined-build-report-", name);
                Assert.Contains("\"Current ship trips\",\"4\"", csv);
                return Task.FromResult(true);
            }
        );
        ids = ["second"];
        await model.RefreshAsync();
        Assert.Single(model.Details);
        Assert.Equal("200", model.RemainingText);
        ids = [];
        await model.RefreshAsync();
        Assert.Null(model.Snapshot);
        Assert.False(model.CanExport);
        Assert.Contains("No build projects", model.Status);
    }

    /// <summary>A missing member never silently reduces an aggregate total; failures retain the complete prior snapshot.</summary>
    [Fact]
    public async Task RetainsEntireReportWhenOneMemberFailsAndCancelsSequentialReads()
    {
        bool missing = false;
        int reads = 0;
        var model = new ColonizationProjectPreviewViewModel(
            new Reader(
                (id, _) =>
                {
                    reads++;
                    return Task.FromResult<ColonizationProjectPreviewData?>(
                        missing && id == "second"
                            ? null
                            : Data() with
                            {
                                Project = Data().Project with { BuildId = id },
                            }
                    );
                }
            ),
            () => CombinedBuildIds
        );
        await model.RefreshAsync();
        ColonizationProjectPreview? snapshot = model.Snapshot;
        missing = true;
        await model.RefreshAsync();
        Assert.Same(snapshot, model.Snapshot);
        Assert.Contains("last successful snapshot", model.Status);
        Assert.Equal(4, reads);
        model.Dispose();
        await model.RefreshAsync();
        Assert.Equal(4, reads);
        var completion = new TaskCompletionSource<ColonizationProjectPreviewData?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        reads = 0;
        model = new ColonizationProjectPreviewViewModel(
            new Reader(
                (_, _) =>
                {
                    reads++;
                    return completion.Task;
                }
            ),
            () => CombinedBuildIds
        );
        Task refresh = model.RefreshAsync();
        model.Dispose();
        completion.SetResult(Data());
        await refresh;
        Assert.Equal(1, reads);
        Assert.Null(model.Snapshot);
    }

    /// <summary>Displays live project details, assignments, carrier quantities, effects, trip estimates, and delivery history.</summary>
    [Fact]
    public async Task RefreshesClickedProjectAndPublicProgressData()
    {
        using var model = new ColonizationProjectPreviewViewModel(
            new Reader((_, _) => Task.FromResult<ColonizationProjectPreviewData?>(Data())),
            "build",
            currentShipCapacity: () => 128
        );
        int changes = 0;
        model.PropertyChanged += (_, _) => changes++;
        await model.RefreshAsync();
        Assert.True(model.CanExport);
        Assert.Contains("Example build", model.WindowTitle);
        Assert.Contains("Military Hub", model.Subtitle);
        Assert.Contains("30 seconds", model.RefreshedAt);
        Assert.Equal("200", model.RemainingText);
        Assert.Equal("100", model.DeficitText);
        Assert.Equal("100", model.ReadyText);
        Assert.Equal("200", model.DeliveredText);
        Assert.Equal("50% delivered", model.ProgressText);
        Assert.Equal(25, model.ReadyProgress);
        Assert.Contains("1 trips", model.RemainingTrips);
        Assert.Contains("1 trips", model.DeficitTrips);
        Assert.Contains("2 trips", model.CurrentShipTrips);
        Assert.True(model.HasCurrentShip);
        Assert.Equal("FC-123", Assert.Single(model.CarrierHeaders));
        Assert.Equal("-100", Assert.Single(model.Rows).Difference);
        Assert.Contains(model.Commanders, row => row.Value == "steel");
        Assert.Contains(model.CarrierDetails, row => row.Value.Contains("100 tonnes", StringComparison.Ordinal));
        Assert.Contains(model.Effects, row => row.Label == "Requires");
        Assert.Equal("30", Assert.Single(model.DeliveryHistory).Cargo);
        Assert.Equal(100, Assert.Single(model.DeliveryHistory).Percentage);
        Assert.Contains(model.DeliveryTotals, row => row.Value == "30 tonnes delivered");
        Assert.Equal("2 tracked deliveries", model.HistoryStatus);
        Assert.True(changes >= 2);
    }

    /// <summary>Shows unavailable sections and unknown capacities explicitly while retaining valid cargo data.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShowsOptionalDataFailuresAndUnknownBuildType(bool missingCargo)
    {
        ColonizationProjectPreviewData data = Data() with
        {
            CarrierCargo = missingCargo ? null : Data().CarrierCargo,
            Statistics = null,
            Project = Data().Project with { MaximumRequired = 0, BuildType = "unknown-type", BuildName = string.Empty },
        };
        using var model = new ColonizationProjectPreviewViewModel(
            new Reader((_, _) => Task.FromResult<ColonizationProjectPreviewData?>(data)),
            "build"
        );
        Assert.False(model.CanExport);
        Assert.Equal("Build preview", model.Title);
        await model.ExportAsync((_, _) => throw new InvalidOperationException());
        await model.RefreshAsync();
        Assert.Equal("Build preview", model.Title);
        Assert.False(model.HasCurrentShip);
        Assert.Equal("Progress unavailable", model.ProgressText);
        Assert.Equal("Delivery history unavailable.", model.HistoryStatus);
        Assert.Contains(model.Effects, row => row.Value == "Not available for this build type.");
        Assert.Equal(missingCargo ? "Unknown" : "100", model.DeficitText);
        Assert.Contains(missingCargo ? "Carrier cargo unavailable" : "Delivery history unavailable", model.Status);
    }

    /// <summary>A failed refresh keeps the last successful snapshot; a confirmed missing project clears it.</summary>
    [Fact]
    public async Task RetainsSnapshotAfterFailureAndClearsItAfterDeletion()
    {
        int reads = 0;
        using var model = new ColonizationProjectPreviewViewModel(
            new Reader(
                (_, _) =>
                    ++reads switch
                    {
                        1 => Task.FromException<ColonizationProjectPreviewData?>(new IOException()),
                        2 => Task.FromResult<ColonizationProjectPreviewData?>(Data() with { Statistics = new() }),
                        3 => Task.FromException<ColonizationProjectPreviewData?>(new HttpRequestException()),
                        _ => Task.FromResult<ColonizationProjectPreviewData?>(null),
                    }
            ),
            "build"
        );
        await model.RefreshAsync();
        Assert.Null(model.Snapshot);
        Assert.Contains("Could not load", model.Status);
        await model.RefreshAsync();
        Assert.Equal("No tracked deliveries yet.", model.HistoryStatus);
        ColonizationProjectPreview? snapshot = model.Snapshot;
        await model.RefreshAsync();
        Assert.Same(snapshot, model.Snapshot);
        Assert.Contains("last successful snapshot", model.Status);
        await model.RefreshAsync();
        Assert.Null(model.Snapshot);
        Assert.False(model.CanExport);
        Assert.Empty(model.Rows);
        Assert.Empty(model.CarrierHeaders);
        Assert.Empty(model.Details);
        Assert.Empty(model.DeliveryHistory);
        Assert.Contains("no longer available", model.Status);
    }

    /// <summary>Raven consent blocks requests, simultaneous refreshes do not overlap, and late results cannot repopulate a closed window.</summary>
    [Fact]
    public async Task GuardsConsentOverlapAndLateCompletion()
    {
        bool enabled = false;
        int reads = 0;
        var completion = new TaskCompletionSource<ColonizationProjectPreviewData?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var model = new ColonizationProjectPreviewViewModel(
            new Reader(
                (_, _) =>
                {
                    reads++;
                    return completion.Task;
                }
            ),
            "build",
            () => enabled
        );
        await model.RefreshAsync();
        Assert.Equal(0, reads);
        Assert.Contains("access is off", model.Status);
        enabled = true;
        Task refresh = model.RefreshAsync();
        await model.RefreshAsync();
        Assert.Equal(1, reads);
        Assert.True(model.IsBusy);
        model.Dispose();
        model.Dispose();
        completion.SetResult(Data());
        await refresh;
        await model.RefreshAsync();
        Assert.Null(model.Snapshot);
        Assert.False(model.CanExport);
        Assert.Equal(1, reads);
        await model.RunAsync();
    }

    /// <summary>Runs one timed loop, preserving its cancellation and avoiding a second loop when opened twice.</summary>
    [Fact]
    public async Task StopsRefreshLoopAndRequestsOnClose()
    {
        int reads = 0;
        CancellationToken requestToken = default;
        var model = new ColonizationProjectPreviewViewModel(
            new Reader(
                (_, token) =>
                {
                    requestToken = token;
                    reads++;
                    return Task.FromResult<ColonizationProjectPreviewData?>(Data());
                }
            ),
            "build",
            delayAsync: (delay, token) =>
            {
                Assert.Equal(TimeSpan.FromSeconds(30), delay);
                return Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
        );
        Task loop = model.RunAsync();
        await model.RunAsync();
        Assert.Equal(1, reads);
        model.Dispose();
        await loop;
        Assert.True(requestToken.IsCancellationRequested);
    }

    /// <summary>Closing during a network request cancels it without presenting an error.</summary>
    [Fact]
    public async Task CancelsInFlightRequestQuietly()
    {
        var model = new ColonizationProjectPreviewViewModel(
            new Reader(
                async (_, token) =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return Data();
                }
            ),
            "build"
        );
        Task refresh = model.RefreshAsync();
        model.Dispose();
        await refresh;
        Assert.False(model.IsBusy);
        Assert.Null(model.Snapshot);
        Assert.DoesNotContain("failed", model.Status);
    }

    /// <summary>Exports precisely the snapshot displayed before the save dialog, even if a new refresh completes meanwhile.</summary>
    [Fact]
    public async Task ExportsCapturedSnapshotAndHandlesCancelAndSaveFailure()
    {
        int reads = 0;
        int capacity = 128;
        using var model = new ColonizationProjectPreviewViewModel(
            new Reader(
                (_, _) =>
                    Task.FromResult<ColonizationProjectPreviewData?>(
                        Data() with
                        {
                            Project = Data().Project with { BuildName = $"Build {++reads}" },
                        }
                    )
            ),
            "build",
            currentShipCapacity: () => capacity
        );
        await model.RefreshAsync();
        var save = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task export = model.ExportAsync(
            (name, csv) =>
            {
                Assert.StartsWith("Raven-build-", name);
                Assert.Contains("\"Project name\",\"Build 1\"", csv);
                Assert.Contains("\"Current ship capacity\",\"128\"", csv);
                return save.Task;
            }
        );
        Assert.False(model.CanExport);
        await model.ExportAsync((_, _) => throw new InvalidOperationException());
        capacity = 64;
        await model.RefreshAsync();
        save.SetResult(true);
        await export;
        Assert.Equal("CSV exported.", model.ExportStatus);
        Assert.Equal("Build 2", model.Title);
        Assert.Contains("4 trips", model.CurrentShipTrips);
        Assert.True(model.CanExport);
        await model.ExportAsync((_, _) => Task.FromResult(false));
        Assert.Equal("CSV exported.", model.ExportStatus);
        await model.ExportAsync((_, _) => throw new IOException());
        Assert.Contains("Could not export", model.ExportStatus);
        Assert.True(model.CanExport);
    }

    /// <summary>Creates independent public build data for view-model and popout tests.</summary>
    internal static ColonizationProjectPreviewData Data()
    {
        var project = new ColonizationProject
        {
            BuildId = "build",
            BuildName = "Example build",
            BuildType = "ares",
            SystemName = "Example system",
            MaximumRequired = 400,
            Commodities = new() { ["steel"] = 200 },
            Commanders = new() { ["Example Cmdr"] = ["steel"] },
            LinkedFleetCarriers =
            [
                new()
                {
                    MarketId = 1,
                    Name = "FC-123",
                    DisplayName = "Supply",
                },
            ],
        };
        return new(
            project,
            new() { ["1"] = new() { ["steel"] = 100 } },
            new()
            {
                TotalCargo = 30,
                TotalDeliveries = 2,
                Cmdrs = new() { ["Example Cmdr"] = 30 },
                Stats =
                [
                    new()
                    {
                        Time = DateTimeOffset.UnixEpoch,
                        Cmdrs = new() { ["Example Cmdr"] = 30 },
                    },
                ],
            }
        );
    }

    /// <summary>A read-only test reader; it has no publishing or project-edit operations.</summary>
    internal sealed class Reader(Func<string, CancellationToken, Task<ColonizationProjectPreviewData?>> read)
        : IRavenColonialProjectReader
    {
        /// <summary>Delegates reads so tests can control failure, overlap, and cancellation.</summary>
        public Task<ColonizationProjectPreviewData?> ReadProjectPreviewAsync(
            string buildId,
            CancellationToken cancellationToken
        ) => read(buildId, cancellationToken);
    }
}
