using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ColonizationNexusReportViewModelTests
{
    [Fact]
    public async Task EveryReportScopeUsesTheWholeNexusCargoPoolAndRefreshesItsStock()
    {
        var carrier = new ColonizationFleetCarrier
        {
            MarketId = 42,
            Name = "ABC-123",
            Cargo = new() { ["steel"] = 20 },
        };
        ColonizationNexus source = Sample() with { FleetCarriers = [carrier, carrier] };
        using var report = new ColonizationProjectPreviewViewModel(
            _ => Task.FromResult<ColonizationNexus?>(source),
            "Beta",
            () => true,
            () => 50
        );
        await report.RefreshAsync();
        Assert.Single(report.Snapshot!.Carriers);
        Assert.Equal(50, report.Snapshot.CarrierDeficit);
        report.SelectedSystem = report.SystemOptions.Single(option => option.SystemName == "Alpha");
        Assert.Equal(10, report.Snapshot.CarrierDeficit);
        report.SelectedSystem = report.SystemOptions[0];
        Assert.Equal(80, report.Snapshot.CarrierDeficit);
        source = source with { FleetCarriers = [carrier with { Cargo = new() { ["steel"] = 60 } }] };
        await report.RefreshAsync();
        Assert.Equal(40, report.Snapshot.CarrierDeficit);
        report.SelectedSystem = report.SystemOptions.Single(option => option.SystemName == "Beta");
        Assert.Equal(10, report.Snapshot.CarrierDeficit);
    }

    [Fact]
    public async Task PlayCommandsOpenCorrectScopeAndReportRefreshesPlansFromAuthenticatedNexus()
    {
        var api = new ColonizationNexusEditorViewModelTests.Writer { Existing = Sample() };
        using var nexus = new ColonizationNexusViewModel(api, ColonizationBuildCatalog.LoadEmbedded(), _ => { });
        Assert.Throws<InvalidOperationException>(() => nexus.CreateReport("new", 0, () => 50));
        nexus.SetContext(true, "Fenris", "key");
        await nexus.RefreshAsync();
        string? openedId = null;
        long? openedScope = null;
        nexus.SetReportOpener(
            (id, scope) =>
            {
                openedId = id;
                openedScope = scope;
            }
        );
        nexus.Detail!.OpenAllReportCommand.Execute(null);
        Assert.Equal("new", openedId);
        Assert.Equal(0, openedScope);
        nexus.Detail.Systems[1].OpenReportCommand.Execute(null);
        Assert.Equal(2, openedScope);
        Assert.Throws<InvalidOperationException>(() => nexus.CreateReport("new", 99, () => 50));
        Assert.Throws<InvalidOperationException>(() => nexus.CreateReport("other", 0, () => 50));
        int capacity = 50;
        using ColonizationProjectPreviewViewModel all = nexus.CreateReport("new", 0, () => capacity);
        using ColonizationProjectPreviewViewModel beta = nexus.CreateReport("new", 2, () => capacity);
        await all.RefreshAsync();
        await beta.RefreshAsync();
        Assert.True(all.IsCombinedReport);
        Assert.Equal(100, all.Snapshot!.Remaining);
        Assert.Equal(70, beta.Snapshot!.Remaining);
        Assert.Empty(beta.Snapshot.Projects);
        Assert.Contains("Beta", beta.Title);
        Assert.Contains("planned", beta.Subtitle);
        Assert.Contains(beta.Details, field => field.Label == "Beta");
        Assert.Equal(3, beta.SystemOptions.Count);
        beta.SelectedSystem = beta.SystemOptions.Single(option => option.SystemName == "Alpha");
        Assert.Equal(30, beta.Snapshot.Remaining);
        beta.SelectedSystem = beta.SystemOptions[0];
        Assert.Equal(100, beta.Snapshot.Remaining);
        Assert.Contains("2 trips", beta.CurrentShipTrips);
        capacity = 200;
        api.Existing = Sample(20);
        await beta.RefreshAsync();
        Assert.Equal(50, beta.Snapshot.Remaining);
        Assert.Contains("1 trips", beta.CurrentShipTrips);
        nexus.SetReportOpener(null);
        Assert.False(nexus.Detail.OpenAllReportCommand.CanExecute(null));
        nexus.SetContext(true, "Other", "new-key");
        await beta.RefreshAsync();
        Assert.Contains("access is off", beta.Status);
    }

    [Fact]
    public async Task MissingOrIncompleteNexusPreservesLastGoodReportAndWarnsAboutRequirements()
    {
        ColonizationNexus? source = Sample();
        using var report = new ColonizationProjectPreviewViewModel(
            _ => Task.FromResult<ColonizationNexus?>(source),
            null,
            () => true,
            () => 0
        );
        await report.RefreshAsync();
        source = null;
        await report.RefreshAsync();
        Assert.Equal(100, report.Snapshot!.Remaining);
        Assert.Contains("last successful", report.Status);
        source = Sample() with { Systems = [new() { Name = "Unknown" }] };
        await report.RefreshAsync();
        Assert.Contains("not reported", report.Status);
        Assert.Null(report.Snapshot.Progress);
        source = Sample() with
        {
            Systems =
            [
                new()
                {
                    Name = "Overflow",
                    Needs = new() { ["steel"] = long.MaxValue, ["water"] = long.MaxValue },
                },
            ],
        };
        await report.RefreshAsync();
        Assert.Contains("last successful", report.Status);
    }

    internal static ColonizationNexus Sample(long betaNeed = 70) =>
        new()
        {
            Id = "new",
            Name = "Nexus",
            Owner = "Fenris",
            Systems =
            [
                new()
                {
                    SystemAddress = 1,
                    Name = "Alpha",
                    Total = 100,
                    Progress = 70,
                    Needs = new() { ["steel"] = 30 },
                },
                new()
                {
                    SystemAddress = 2,
                    Name = "Beta",
                    Total = betaNeed,
                    Needs = new() { ["steel"] = betaNeed },
                    BuildTypes = new() { ["outpost"] = 1 },
                },
            ],
        };
}
