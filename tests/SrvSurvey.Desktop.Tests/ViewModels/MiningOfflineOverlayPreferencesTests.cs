using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Tests.ProfileSync;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class MiningWorkspaceViewModelTests
{
    [Fact]
    public async Task OfflineEditsSurviveTheApplicationSwitchingToCommanderSettingsProfiles()
    {
        using var root = new ProfileSyncTestRoot();
        AppDataPaths shared = root.Paths;
        var data = new MiningCommanderData();
        data.Settings.NotificationSeconds = 44;
        new MiningStore(shared.DataDirectory).Save("F1", data);
        await using (
            MainWindowViewModel unknown = MainWindowViewModelTestBuilder.Create(
                null,
                builder => builder.WithAppDataPaths(shared)
            )
        )
        {
            unknown.MiningWorkspace.SessionYieldLimit = 8;
        }

        AppDataPaths firstPaths = shared with { SettingsFrontierId = "F1" };
        CommanderSettingsProfile.Prepare(firstPaths);
        await using (
            MainWindowViewModel first = MainWindowViewModelTestBuilder.Create(
                null,
                builder => builder.WithAppDataPaths(firstPaths)
            )
        )
        {
            Assert.Equal(8, first.MiningWorkspace.SessionYieldLimit);
            Assert.Equal(44, first.MiningWorkspace.Settings.NotificationSeconds);
            Assert.Null(first.MiningWorkspace.Current);
            Assert.False(first.MiningWorkspace.StartCommand.CanExecute(null));
        }

        AppDataPaths secondPaths = shared with { SettingsFrontierId = "F2" };
        CommanderSettingsProfile.Prepare(secondPaths);
        await using MainWindowViewModel second = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithAppDataPaths(secondPaths).WithTargetFrontierId("F2")
        );
        Assert.Equal(5, second.MiningWorkspace.SessionYieldLimit);
        second.MiningWorkspace.SessionYieldLimit = 11;
        Assert.Equal(
            8,
            new MiningOverlaySettingsStore(shared.SharedUiSettingsPath).Load("F1", () => new()).SessionYieldLimit
        );
    }

    [Fact]
    public void OfflineDraftSurvivesRestartAndMergesOnlyChangedFieldsIntoTheNextCommander()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-offline-mining-{Guid.NewGuid():N}");
        try
        {
            var store = new MiningStore(directory);
            var data = new MiningCommanderData();
            data.Settings.NotificationSeconds = 45;
            data.Settings.PersistentProspectSlots = 2;
            data.Settings.AutoStart = false;
            data.Settings.Thresholds["Platinum"] = 32;
            store.Save("F1", data);
            using (MiningWorkspaceViewModel first = OfflineWorkspace(directory))
            {
                first.SessionYieldSortIndex = 1;
                first.SessionYieldLimit = 9;
                first.Settings.HideInSupercruise = false;
                first.SaveSettingsCommand.Execute(null);
                Assert.Null(first.Current);
            }
            using MiningWorkspaceViewModel restarted = OfflineWorkspace(directory);
            Assert.Equal(1, restarted.SessionYieldSortIndex);
            Assert.Equal(9, restarted.SessionYieldLimit);
            Assert.False(restarted.Settings.HideInSupercruise);
            ConnectMiningCommander(restarted, "F1");
            Assert.Equal(45, restarted.Settings.NotificationSeconds);
            Assert.Equal(2, restarted.Settings.PersistentProspectSlots);
            Assert.False(restarted.Settings.AutoStart);
            Assert.Equal(32, restarted.Settings.Thresholds["Platinum"]);
            Assert.Equal(9, restarted.SessionYieldLimit);
            Assert.Equal(MiningSessionYieldSort.Finds, store.Load("F1").Settings.SessionYieldSort);
            Assert.Equal(9, store.Load("F1").Settings.SessionYieldLimit);
            ConnectMiningCommander(restarted, "F2");
            Assert.Equal(0, restarted.SessionYieldSortIndex);
            Assert.Equal(5, restarted.SessionYieldLimit);
            ConnectMiningCommander(restarted, "F1");
            Assert.Equal(1, restarted.SessionYieldSortIndex);
            Assert.Equal(9, restarted.SessionYieldLimit);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void KnownCommanderPreferencesLoadOfflineAndDoNotFabricateASession()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-known-offline-mining-{Guid.NewGuid():N}");
        try
        {
            var store = new MiningStore(directory);
            var data = new MiningCommanderData();
            data.Settings.SessionYieldLimit = 12;
            data.Settings.NotificationSeconds = 30;
            store.Save("F1", data);
            using (MiningWorkspaceViewModel offline = OfflineWorkspace(directory, "F1"))
            {
                Assert.Equal(12, offline.SessionYieldLimit);
                Assert.Equal(30, offline.Settings.NotificationSeconds);
                Assert.Null(offline.Current);
                Assert.False(offline.StartCommand.CanExecute(null));
                offline.SessionYieldLimit = 8;
                offline.Settings.NotificationSeconds = 22;
                offline.SaveSettingsCommand.Execute(null);
            }
            using MiningWorkspaceViewModel restarted = OfflineWorkspace(directory, "F1");
            Assert.Equal(8, restarted.SessionYieldLimit);
            Assert.Equal(22, restarted.Settings.NotificationSeconds);
            ConnectMiningCommander(restarted, "F1");
            Assert.Equal(8, store.Load("F1").Settings.SessionYieldLimit);
            Assert.Equal(22, store.Load("F1").Settings.NotificationSeconds);
            Assert.Null(restarted.Current);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ControlsStayEditableWhenSavingFailsAndCanRetryAfterStorageIsFixed()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-offline-mining-error-{Guid.NewGuid():N}");
        string blocked = Path.Combine(directory, "blocked.json");
        Directory.CreateDirectory(blocked);
        try
        {
            using var workspace = new MiningWorkspaceViewModel(
                directory,
                new Resolver(),
                new BookmarksViewModel(directory),
                options: new() { OverlaySettings = new MiningOverlaySettingsStore(blocked) }
            );
            workspace.SessionYieldLimit = 9;
            Assert.Equal(9, workspace.SessionYieldLimit);
            Assert.True(workspace.SaveSettingsCommand.CanExecute(null));
            Assert.Contains("could not be saved", workspace.OverlaySettingsStatus, StringComparison.Ordinal);
            Directory.Delete(blocked);
            workspace.SaveSettingsCommand.Execute(null);
            Assert.Empty(workspace.OverlaySettingsStatus);
            Assert.False(workspace.HasOverlaySettingsStatus);
            Assert.True(File.Exists(blocked));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static MiningWorkspaceViewModel OfflineWorkspace(string directory, string? commander = null) =>
        new(directory, new Resolver(), new BookmarksViewModel(directory), options: new() { CommanderId = commander });

    private static void ConnectMiningCommander(MiningWorkspaceViewModel workspace, string fid)
    {
        JournalEventEnvelope load = MiningMissionFixture.Event(
            $$"""{"event":"LoadGame","FID":"{{fid}}","Commander":"Test","Ship":"Python"}"""
        );
        var context = new JournalSessionState();
        context.Apply(load);
        var status = new EliteStatus { Flags = StatusFlags.InMainShip };
        workspace.Apply(new(null, [load], status, null, null, null, [], false), context, null, status);
    }
}
