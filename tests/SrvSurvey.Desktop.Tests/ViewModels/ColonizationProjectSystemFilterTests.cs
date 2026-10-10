using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ColonizationProjectSystemFilterTests
{
    private static readonly string[] SystemLabels = ["All Systems", "Alpha", "Beta"];
    private static readonly string[] AlphaCommanders = ["first", "second"];

    /// <summary>System selection recalculates all sections, counts shared stock once, and exports only the displayed builds without extra reads.</summary>
    [Fact]
    public async Task FiltersCargoParticipantsEffectsHistoryAndExportFromCachedBuilds()
    {
        ColonizationProjectPreviewData[] data =
        [
            SystemData("first", "Alpha", 200, 100),
            SystemData("second", " alpha ", 100, 150),
            SystemData("third", "Beta", 900, 400, 2),
        ];
        int reads = 0;
        using var model = new ColonizationProjectPreviewViewModel(
            new ColonizationProjectPreviewViewModelTests.Reader(
                (id, _) =>
                {
                    reads++;
                    return Task.FromResult<ColonizationProjectPreviewData?>(
                        data.Single(item => item.Project.BuildId == id)
                    );
                }
            ),
            () => data.Select(item => item.Project.BuildId).ToArray(),
            currentShipCapacity: () => 128
        );
        Assert.Equal("All Systems", model.SelectedSystem!.Label);
        await model.RefreshAsync();
        Assert.Equal(SystemLabels, model.SystemOptions.Select(option => option.Label));
        Assert.Equal("1,200", model.RemainingText);
        DateTimeOffset fetchedAt = model.Snapshot!.FetchedAt;
        model.SelectedSystem = model.SystemOptions[1];
        Assert.Equal(3, reads);
        Assert.Equal("300", model.RemainingText);
        Assert.Equal("150", model.ReadyText);
        Assert.Equal("150", model.DeficitText);
        Assert.Equal("500", model.DeliveredText);
        Assert.Equal(62.5, model.Progress);
        Assert.Equal(18.75, model.ReadyProgress);
        Assert.Contains("3 trips", model.CurrentShipTrips);
        Assert.Equal(2, model.Details.Count);
        Assert.Equal(AlphaCommanders, model.Commanders.Select(field => field.Label));
        Assert.Equal("FC-1", Assert.Single(model.CarrierHeaders));
        Assert.Single(model.CarrierDetails);
        PreviewEffectGroup effects = Assert.Single(model.EffectGroups);
        Assert.Equal("Alpha", effects.Name);
        Assert.Contains(effects.Fields, field => field.Label == "Security" && field.Value == "+20");
        Assert.Contains("Alpha", model.Subtitle);
        Assert.Equal("60", Assert.Single(model.DeliveryHistory).Cargo);
        Assert.Equal(2, model.DeliveryTotals.Count);
        Assert.Equal("4 tracked deliveries", model.HistoryStatus);
        Assert.Equal(fetchedAt, model.Snapshot.FetchedAt);
        await model.ExportAsync(
            (_, csv) =>
            {
                Assert.Contains("\"Current ship trips\",\"3\"", csv);
                Assert.Contains("\"Systems\",\"1\"", csv);
                Assert.Contains("\"first\"", csv);
                Assert.Contains("\"second\"", csv);
                Assert.DoesNotContain("Beta", csv);
                Assert.DoesNotContain("third", csv);
                Assert.DoesNotContain("FC-2", csv);
                return Task.FromResult(true);
            }
        );
        model.SelectedSystem = model.SystemOptions[2];
        Assert.Equal("900", model.RemainingText);
        Assert.Equal("400", model.ReadyText);
        Assert.Equal("500", model.DeficitText);
        Assert.Equal("third", Assert.Single(model.Commanders).Label);
        Assert.Equal("FC-2", Assert.Single(model.CarrierHeaders));
        model.SelectedSystem = model.SystemOptions[0];
        Assert.Equal("1,200", model.RemainingText);
        Assert.Equal("550", model.ReadyText);
        Assert.Equal(3, model.Details.Count);
        Assert.Equal(3, reads);
    }

    /// <summary>Live refresh preserves selection through case changes, handles edits during a request, and falls back when membership disappears.</summary>
    [Fact]
    public async Task ReconcilesSelectedSystemWithRefreshedMembershipAndRetainsFailedData()
    {
        ColonizationProjectPreviewData[] data = [SystemData("first", "Alpha"), SystemData("second", "Beta")];
        var pending = new TaskCompletionSource<ColonizationProjectPreviewData?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        bool wait = false;
        bool fail = false;
        using var model = new ColonizationProjectPreviewViewModel(
            new ColonizationProjectPreviewViewModelTests.Reader(
                (id, _) =>
                {
                    if (fail)
                    {
                        return Task.FromException<ColonizationProjectPreviewData?>(new IOException());
                    }
                    return wait && id == "first"
                        ? pending.Task
                        : Task.FromResult<ColonizationProjectPreviewData?>(
                            data.Single(item => item.Project.BuildId == id)
                        );
                }
            ),
            () => data.Select(item => item.Project.BuildId).ToArray()
        );
        await model.RefreshAsync();
        model.SelectedSystem = model.SystemOptions[1];
        wait = true;
        Task refresh = model.RefreshAsync();
        model.SelectedSystem = model.SystemOptions[2];
        data[1] = SystemData("second", " BETA ", 100);
        pending.SetResult(data[0]);
        await refresh;
        Assert.Equal("BETA", model.SelectedSystem.SystemName);
        Assert.Equal("100", model.RemainingText);
        Assert.Single(model.Details);
        wait = false;
        fail = true;
        await model.RefreshAsync();
        model.SelectedSystem = model.SystemOptions[1];
        Assert.Equal("200", model.RemainingText);
        Assert.Contains("last successful snapshot", model.Status);
        model.SelectedSystem = null;
        model.SelectedSystem = new PreviewSystemOption("missing", "missing");
        Assert.Equal("Alpha", model.SelectedSystem.SystemName);
        fail = false;
        data = [data[1]];
        await model.RefreshAsync();
        Assert.Null(model.SelectedSystem.SystemName);
        Assert.Equal("100", model.RemainingText);
        Assert.Equal(2, model.SystemOptions.Count);
        model.SelectedSystem = model.SystemOptions[1];
        data = [];
        await model.RefreshAsync();
        Assert.Null(model.Snapshot);
        Assert.Null(model.SelectedSystem.SystemName);
        Assert.Single(model.SystemOptions);
        Assert.False(model.CanExport);
    }

    /// <summary>Unknown systems and a real system named All Systems remain distinct choices, including during a captured export.</summary>
    [Fact]
    public async Task HandlesUnknownSystemAndSelectionChangesDuringExport()
    {
        ColonizationProjectPreviewData[] data = [SystemData("unknown", " "), SystemData("named", "All Systems", 100)];
        using var model = new ColonizationProjectPreviewViewModel(
            new ColonizationProjectPreviewViewModelTests.Reader(
                (id, _) =>
                    Task.FromResult<ColonizationProjectPreviewData?>(data.Single(item => item.Project.BuildId == id))
            ),
            () => data.Select(item => item.Project.BuildId).ToArray()
        );
        await model.RefreshAsync();
        model.SelectedSystem = model.SystemOptions.Single(option => option.Label == "Unknown system");
        var save = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task export = model.ExportAsync(
            (_, csv) =>
            {
                Assert.Contains("\"unknown\"", csv);
                Assert.DoesNotContain("\"named\"", csv);
                return save.Task;
            }
        );
        model.SelectedSystem = model.SystemOptions.Single(option => option.SystemName == "All Systems");
        Assert.Equal("100", model.RemainingText);
        save.SetResult(true);
        await export;
        Assert.Equal("CSV exported.", model.ExportStatus);
        PreviewSystemOption selection = model.SelectedSystem;
        model.SelectedSystem = selection;
        model.Dispose();
        model.SelectedSystem = model.SystemOptions[0];
        Assert.Same(selection, model.SelectedSystem);
        Assert.False(model.CanExport);
    }

    /// <summary>A filtered report distinguishes unavailable optional data from complete data in another system.</summary>
    [Fact]
    public async Task ShowsOptionalDataStatusForOnlyTheSelectedSystem()
    {
        ColonizationProjectPreviewData[] data =
        [
            SystemData("first", "Alpha") with
            {
                CarrierCargo = null,
            },
            SystemData("second", "Beta") with
            {
                Statistics = null,
            },
        ];
        using var model = new ColonizationProjectPreviewViewModel(
            new ColonizationProjectPreviewViewModelTests.Reader(
                (id, _) =>
                    Task.FromResult<ColonizationProjectPreviewData?>(data.Single(item => item.Project.BuildId == id))
            ),
            () => data.Select(item => item.Project.BuildId).ToArray()
        );
        await model.RefreshAsync();
        model.SelectedSystem = model.SystemOptions[1];
        Assert.Contains("Carrier cargo unavailable", model.Status);
        Assert.Equal("2 tracked deliveries", model.HistoryStatus);
        model.SelectedSystem = model.SystemOptions[2];
        Assert.Equal("Cargo updated. Delivery history unavailable.", model.Status);
        Assert.Equal("100", model.DeficitText);
    }

    /// <summary>Creates distinct project participants and carrier stock so a filtered report cannot accidentally retain another system's data.</summary>
    internal static ColonizationProjectPreviewData SystemData(
        string id,
        string system,
        int need = 200,
        int stock = 100,
        long carrierId = 1
    )
    {
        ColonizationProjectPreviewData data = ColonizationProjectPreviewViewModelTests.Data();
        return data with
        {
            Project = data.Project with
            {
                BuildId = id,
                BuildName = id,
                SystemName = system,
                MaximumRequired = need > 400 ? 1000 : 400,
                Commodities = new() { ["steel"] = need },
                Commanders = new() { [id] = ["steel"] },
                LinkedFleetCarriers = [new() { MarketId = carrierId, Name = $"FC-{carrierId}" }],
            },
            CarrierCargo = new()
            {
                [carrierId.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new() { ["steel"] = stock },
            },
            Statistics = new()
            {
                TotalCargo = 30,
                TotalDeliveries = 2,
                Cmdrs = new() { [id] = 30 },
                Stats =
                [
                    new()
                    {
                        Time = DateTimeOffset.UnixEpoch,
                        Cmdrs = new() { [id] = 30 },
                    },
                ],
            },
        };
    }
}
