using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class RamTahViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-ram-tah-vm-tests-{Guid.NewGuid():N}");

    [Fact]
    public void ExposesEveryLegacyChecklistGroupAndLog()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(
            ["Biology", "Culture", "History", "Language", "Technology"],
            viewModel.AncientRuinsGroups.Select(group => group.Name));
        Assert.Equal(101, viewModel.AncientRuinsGroups.Sum(group => group.Logs.Count));
        Assert.Equal(
            ["Thargoids", "Civil war", "Technology", "Language", "Body Protectorate"],
            viewModel.GuardianLogsGroups.Select(group => group.Name));
        Assert.Equal(28, viewModel.GuardianLogsGroups.Sum(group => group.Logs.Count));
    }

    [Fact]
    public async Task ManualToggleUpdatesDisplayAndLegacyProfile()
    {
        var store = new CommanderProfileStore(temporaryDirectory);
        var viewModel = new RamTahViewModel(store);
        viewModel.LoadProfile(
            "F123",
            "Drew",
            true,
            new RamTahSnapshot(
                RamTahMissionStatus.Active,
                RamTahMissionStatus.NotStarted,
                ["B1"],
                []));

        await viewModel.ToggleLogAsync(RamTahMission.AncientRuins, "B2");

        Assert.True(viewModel.IsLogCompleted(RamTahMission.AncientRuins, "B2"));
        Assert.Contains("2 of 101", viewModel.AncientRuinsProgressText);
        var loaded = await store.LoadAsync("F123", true);
        Assert.Equal(["B1", "B2"], loaded.Data?.RamTah.AncientRuinsLogs);
    }

    [Fact]
    public async Task JournalMissionChangeUpdatesAndPersistsStatus()
    {
        var store = new CommanderProfileStore(temporaryDirectory);
        var viewModel = new RamTahViewModel(store);
        viewModel.LoadProfile("F123", "Drew", true, RamTahSnapshot.Empty);

        await viewModel.ApplyJournalEventsAsync(
        [
            Parse(
                """
                {"timestamp":"2026-07-24T12:00:00Z","event":"MissionAccepted","Name":"Mission_TheDead_002_name"}
                """),
        ]);

        Assert.Equal("Active", viewModel.GuardianLogsMissionStatus);
        var loaded = await store.LoadAsync("F123", true);
        Assert.Equal(
            RamTahMissionStatus.Active,
            loaded.Data?.RamTah.GuardianLogsMissionStatus);
    }

    [Fact]
    public async Task SetLogCompletedIsIdempotentAndPersistsRequestedState()
    {
        var store = new CommanderProfileStore(temporaryDirectory);
        var viewModel = new RamTahViewModel(store);
        viewModel.LoadProfile(
            "F123",
            "Drew",
            true,
            new RamTahSnapshot(
                RamTahMissionStatus.Active,
                RamTahMissionStatus.NotStarted,
                [],
                []));

        Assert.True(await viewModel.SetLogCompletedAsync(
            RamTahMission.AncientRuins,
            "H1",
            true));
        Assert.False(await viewModel.SetLogCompletedAsync(
            RamTahMission.AncientRuins,
            "H1",
            true));
        Assert.True(viewModel.IsLogCompleted(RamTahMission.AncientRuins, "H1"));

        var loaded = await store.LoadAsync("F123", true);
        Assert.Contains("H1", loaded.Data!.RamTah.AncientRuinsLogs);
    }

    [Fact]
    public async Task ResetRequiresConfirmationAndClearsOnlySelectedMission()
    {
        var store = new CommanderProfileStore(temporaryDirectory);
        var viewModel = new RamTahViewModel(store);
        viewModel.LoadProfile(
            "F123",
            "Drew",
            true,
            new RamTahSnapshot(
                RamTahMissionStatus.Active,
                RamTahMissionStatus.Active,
                ["B1", "C1"],
                ["#1"]));

        viewModel.RequestAncientRuinsReset();

        Assert.True(viewModel.IsAncientRuinsResetPending);
        Assert.True(viewModel.IsLogCompleted(RamTahMission.AncientRuins, "B1"));

        await viewModel.ConfirmAncientRuinsResetAsync();

        Assert.False(viewModel.IsAncientRuinsResetPending);
        Assert.False(viewModel.IsLogCompleted(RamTahMission.AncientRuins, "B1"));
        Assert.True(viewModel.IsLogCompleted(RamTahMission.GuardianLogs, "#1"));
        var loaded = await store.LoadAsync("F123", true);
        Assert.Empty(loaded.Data!.RamTah.AncientRuinsLogs);
        Assert.Equal(["#1"], loaded.Data.RamTah.GuardianLogs);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private RamTahViewModel CreateViewModel()
    {
        return new RamTahViewModel(new CommanderProfileStore(temporaryDirectory));
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var journalEvent, out var error),
            error);
        return journalEvent!;
    }
}
