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
    public void RememberedCommanderEditsWinOverAnOlderDraftWhenTheJournalConnects()
    {
        using var root = new ProfileSyncTestRoot();
        var overlay = new MiningOverlaySettingsStore(root.Paths.SharedUiSettingsPath);
        var defaults = new MiningOverlayPreferences();
        overlay.Save(null, defaults, defaults with { SessionYieldLimit = 9, NotificationSeconds = 30 });
        using var workspace = new MiningWorkspaceViewModel(
            root.Paths.DataDirectory,
            new Resolver(),
            new BookmarksViewModel(root.Paths.DataDirectory),
            options: new() { CommanderId = "F1", OverlaySettings = overlay }
        );
        Assert.Equal(9, workspace.SessionYieldLimit);
        workspace.SessionYieldLimit = 12;
        ConnectMiningCommander(workspace, "F1");
        Assert.Equal(12, workspace.SessionYieldLimit);
        Assert.Equal(30, workspace.Settings.NotificationSeconds);
        ConnectMiningCommander(workspace, "F2");
        Assert.Equal(5, workspace.SessionYieldLimit);
        Assert.Equal(15, workspace.Settings.NotificationSeconds);
    }

    [Fact]
    public void AwaitingANewJournalIdentityKeepsNewOverlayEditsAwayFromThePreviousCommander()
    {
        using var root = new ProfileSyncTestRoot();
        using MiningWorkspaceViewModel workspace = OfflineWorkspace(root.Paths.DataDirectory);
        ConnectMiningCommander(workspace, "F1");
        workspace.SessionYieldLimit = 6;
        workspace.Apply(
            new(null, [], null, null, null, null, [], false) { IsAwaitingCommanderIdentity = true },
            new JournalSessionState(),
            null,
            null
        );
        Assert.True(workspace.SaveOverlaySettingsCommand.CanExecute(null));
        Assert.False(workspace.SaveSettingsCommand.CanExecute(null));
        workspace.SessionYieldLimit = 9;
        ConnectMiningCommander(workspace, "F2");
        Assert.Equal(9, workspace.SessionYieldLimit);
        ConnectMiningCommander(workspace, "F1");
        Assert.Equal(6, workspace.SessionYieldLimit);
    }

    [Fact]
    public void RestoreReportsOverlayStorageFailureAndCanRetryTheRestoredSnapshot()
    {
        using var root = new ProfileSyncTestRoot();
        string blocked = Path.Combine(root.Paths.ConfigDirectory, "blocked.json");
        Directory.CreateDirectory(blocked);
        using var workspace = new MiningWorkspaceViewModel(
            root.Paths.DataDirectory,
            new Resolver(),
            new BookmarksViewModel(root.Paths.DataDirectory),
            options: new() { OverlaySettings = new MiningOverlaySettingsStore(blocked) }
        );
        ConnectMiningCommander(workspace, "F1");
        string backup = workspace.Backup();
        workspace.SessionYieldLimit = 9;
        Assert.False(workspace.Restore(backup));
        Assert.Contains("overlay preferences could not be saved", workspace.Status, StringComparison.Ordinal);
        Assert.Equal(5, workspace.SessionYieldLimit);
        Directory.Delete(blocked);
        workspace.SaveOverlaySettingsCommand.Execute(null);
        Assert.False(workspace.HasOverlaySettingsStatus);
        Assert.Equal(5, new MiningOverlaySettingsStore(blocked).Load("F1", () => new()).SessionYieldLimit);
        Assert.True(workspace.Restore(backup));
    }

    [Fact]
    public void RememberedCommanderDoesNotClaimTheUnassignedDraftBeforeJournalIdentityArrives()
    {
        using var root = new ProfileSyncTestRoot();
        using (MiningWorkspaceViewModel unknown = OfflineWorkspace(root.Paths.DataDirectory))
        {
            unknown.SessionYieldLimit = 9;
        }
        using MiningWorkspaceViewModel remembered = OfflineWorkspace(root.Paths.DataDirectory, "F1");
        Assert.Equal(9, remembered.SessionYieldLimit);
        ConnectMiningCommander(remembered, "F2");
        Assert.Equal(9, remembered.SessionYieldLimit);
        ConnectMiningCommander(remembered, "F1");
        Assert.Equal(5, remembered.SessionYieldLimit);
    }

    [Fact]
    public void EditingAfterALegacyReadFailureDoesNotReplaceUneditedLegacyPreferences()
    {
        using var root = new ProfileSyncTestRoot();
        var mining = new MiningStore(root.Paths.DataDirectory);
        var legacy = new MiningCommanderData();
        legacy.Settings.NotificationSeconds = 45;
        legacy.Settings.PersistentProspectSlots = 2;
        mining.Save("F1", legacy);
        string path = Directory.GetFiles(Path.Combine(root.Paths.DataDirectory, "mining"), "*.json").Single();
        string original = File.ReadAllText(path);
        File.WriteAllText(path, "{bad");
        using (MiningWorkspaceViewModel workspace = OfflineWorkspace(root.Paths.DataDirectory, "F1"))
        {
            workspace.SessionYieldLimit = 9;
        }
        File.WriteAllText(path, original);
        using MiningWorkspaceViewModel recovered = OfflineWorkspace(root.Paths.DataDirectory, "F1");
        Assert.Equal(9, recovered.SessionYieldLimit);
        Assert.Equal(45, recovered.Settings.NotificationSeconds);
        Assert.Equal(2, recovered.Settings.PersistentProspectSlots);
    }

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
            ConnectMiningCommander(first.MiningWorkspace, "F1");
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
                first.SaveOverlaySettingsCommand.Execute(null);
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
                offline.SaveOverlaySettingsCommand.Execute(null);
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
            Assert.True(workspace.SaveOverlaySettingsCommand.CanExecute(null));
            Assert.Contains("could not be saved", workspace.OverlaySettingsStatus, StringComparison.Ordinal);
            Directory.Delete(blocked);
            workspace.SaveOverlaySettingsCommand.Execute(null);
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

    [Theory]
    [InlineData(null)]
    [InlineData("F1")]
    public void FailedOfflineEditsSurviveJournalConnectionCommanderSwitchAndSaveRetry(string? initialCommander)
    {
        using var root = new ProfileSyncTestRoot();
        var mining = new MiningStore(root.Paths.DataDirectory);
        var data = new MiningCommanderData();
        data.Settings.NotificationSeconds = 45;
        mining.Save("F1", data);
        string blocked = Path.Combine(root.Paths.ConfigDirectory, "blocked.json");
        Directory.CreateDirectory(blocked);
        using var workspace = new MiningWorkspaceViewModel(
            root.Paths.DataDirectory,
            new Resolver(),
            new BookmarksViewModel(root.Paths.DataDirectory),
            options: new() { CommanderId = initialCommander, OverlaySettings = new MiningOverlaySettingsStore(blocked) }
        );
        workspace.SessionYieldLimit = 9;
        workspace.Settings.HideInSupercruise = false;
        workspace.SaveOverlaySettingsCommand.Execute(null);
        Assert.Contains("could not be saved", workspace.OverlaySettingsStatus, StringComparison.Ordinal);
        ConnectMiningCommander(workspace, "F1");
        Assert.Equal(9, workspace.SessionYieldLimit);
        Assert.False(workspace.Settings.HideInSupercruise);
        Assert.Equal(45, workspace.Settings.NotificationSeconds);
        Assert.True(workspace.HasOverlaySettingsStatus);
        ConnectMiningCommander(workspace, "F2");
        Assert.Equal(5, workspace.SessionYieldLimit);
        Assert.True(workspace.Settings.HideInSupercruise);
        Assert.True(workspace.HasOverlaySettingsStatus);
        workspace.SessionYieldLimit = 12;
        ConnectMiningCommander(workspace, "F1");
        Assert.Equal(9, workspace.SessionYieldLimit);
        Assert.False(workspace.Settings.HideInSupercruise);
        Directory.Delete(blocked);
        workspace.SaveOverlaySettingsCommand.Execute(null);
        Assert.False(workspace.HasOverlaySettingsStatus);
        var saved = new MiningOverlaySettingsStore(blocked);
        Assert.Equal(9, saved.Load("F1", () => new()).SessionYieldLimit);
        Assert.Equal(12, saved.Load("F2", () => new()).SessionYieldLimit);
        Assert.Equal(45, mining.Load("F1").Settings.NotificationSeconds);
    }

    [Fact]
    public void FailedEditsRetainAllSevenPreferencesWhenJournalSettingsAreLoaded()
    {
        using var root = new ProfileSyncTestRoot();
        string blocked = Path.Combine(root.Paths.ConfigDirectory, "blocked.json");
        Directory.CreateDirectory(blocked);
        using var workspace = new MiningWorkspaceViewModel(
            root.Paths.DataDirectory,
            new Resolver(),
            new BookmarksViewModel(root.Paths.DataDirectory),
            options: new() { OverlaySettings = new MiningOverlaySettingsStore(blocked) }
        );
        var desired = new MiningOverlayPreferences(false, false, 22, 6, true, MiningSessionYieldSort.Finds, 9);
        desired.ApplyTo(workspace.Settings);
        workspace.SaveOverlaySettingsCommand.Execute(null);
        ConnectMiningCommander(workspace, "F1");
        Assert.Equal(desired, MiningOverlayPreferences.From(workspace.Settings));
        Assert.True(workspace.HasOverlaySettingsStatus);
        Directory.Delete(blocked);
        workspace.SaveOverlaySettingsCommand.Execute(null);
        Assert.Equal(desired, new MiningOverlaySettingsStore(blocked).Load("F1", () => new()));
        Assert.False(workspace.HasOverlaySettingsStatus);
    }

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
