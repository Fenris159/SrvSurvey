using System.Text;
using System.Text.Json.Nodes;
using SrvSurvey.Core.ProfileSync;
using SrvSurvey.Desktop.ProfileSync;
using SrvSurvey.Desktop.ViewModels;
using Xunit;

namespace SrvSurvey.Desktop.Tests.ProfileSync;

public sealed class ProfileSyncServiceTests
{
    [Fact]
    public async Task LinkingEnablesAutomaticSyncAndDisconnectOnlyAffectsThisMachine()
    {
        using var root = new ProfileSyncTestRoot();
        var cloud = new FakeProfileDrive();
        using var service = new ProfileSyncService(root.Store, cloud);
        await service.LinkAsync();
        Assert.True(service.IsLinked);
        Assert.True(root.Store.LoadPreferences().Automatic);
        service.Disconnect();
        Assert.False(service.IsLinked);
        Assert.False(root.Store.LoadPreferences().Automatic);
    }

    [Fact]
    public async Task AutomaticSyncDisabledRetainsTheLinkWithoutContactingGoogleAtStartup()
    {
        using var root = new ProfileSyncTestRoot();
        var cloud = new FakeProfileDrive { Failure = new HttpRequestException("Must not contact cloud") };
        using var service = new ProfileSyncService(root.Store, cloud);
        await service.StartupAsync();
        Assert.True(service.IsLinked);
        Assert.Contains("Automatic sync is off", service.Status);
        Assert.Empty(cloud.Uploaded);
    }

    [Fact]
    public async Task OfflineBackupIsRetainedAndRetriedWithoutLosingCounters()
    {
        using var root = new ProfileSyncTestRoot();
        root.Write("config", "cross-platform-ui.json", "{\"Theme\":\"gold\"}");
        var cloud = new FakeProfileDrive { Failure = new HttpRequestException("offline") };
        using var service = new ProfileSyncService(root.Store, cloud);
        await service.BackupAsync();
        Assert.Contains("Local data was retained", service.Status);
        Assert.NotNull(root.Store.LoadBaseline());
        Assert.Single(Directory.EnumerateFiles(Path.Combine(root.Store.StateDirectory, "backups")));
        cloud.Failure = null;
        await service.BackupAsync();
        Assert.Single(cloud.Uploaded);
        Assert.Equal(1, cloud.Uploaded[0].Portable.Values.Single().Clock[root.Store.Device.Id]);
    }

    [Fact]
    public async Task StartupPullsRemoteChangesBeforeWorkspacesLoadAndLeavesHardwareLocal()
    {
        using var remote = new ProfileSyncTestRoot();
        remote.Write(
            "config",
            "cross-platform-ui.json",
            "{\"Theme\":\"gold\",\"OverlayBehavior\":{\"PreferredMonitor\":\"remote\"}}"
        );
        using var root = new ProfileSyncTestRoot();
        root.Write("config", "cross-platform-ui.json", "{\"OverlayBehavior\":{\"PreferredMonitor\":\"local\"}}");
        root.Store.SavePreferences(new(Automatic: true));
        var cloud = new FakeProfileDrive();
        cloud.Add(remote.Store.Capture());
        using var service = new ProfileSyncService(root.Store, cloud);
        await service.StartupAsync();
        JsonNode ui = JsonNode.Parse((await File.ReadAllTextAsync(root.Path("config", "cross-platform-ui.json"))))!;
        Assert.Equal("gold", ui["Theme"]!.GetValue<string>());
        Assert.Equal("local", ui["OverlayBehavior"]!["PreferredMonitor"]!.GetValue<string>());
        Assert.Contains("Startup sync complete", service.Status);
    }

    [Fact]
    public async Task SyncWhileOpenStagesRemoteChangesForRestartAndSkipsStaleShutdownUpload()
    {
        using var remote = new ProfileSyncTestRoot();
        remote.Write("data", "bookmarks.json", "[{\"Name\":\"Sol\"}]");
        using var root = new ProfileSyncTestRoot();
        root.Store.SavePreferences(new(Automatic: true));
        var cloud = new FakeProfileDrive();
        cloud.Add(remote.Store.Capture());
        using var service = new ProfileSyncService(root.Store, cloud);
        await service.SynchronizeAsync();
        Assert.True(service.RestartRequired);
        Assert.False(File.Exists(root.Path("data", "bookmarks.json")));
        int uploaded = cloud.Uploaded.Count;
        await service.ShutdownAsync();
        Assert.Equal(uploaded, cloud.Uploaded.Count);
        Assert.True(root.Store.ApplyPending());
        Assert.True(File.Exists(root.Path("data", "bookmarks.json")));
    }

    [Theory]
    [InlineData(false, "blue")]
    [InlineData(true, "gold")]
    public async Task ConflictsKeepBothVersionsUntilAnExplicitChoice(bool useCloud, string expected)
    {
        using var remote = new ProfileSyncTestRoot();
        remote.Write("config", "cross-platform-ui.json", "{\"Theme\":\"gold\"}");
        using var root = new ProfileSyncTestRoot();
        root.Write("config", "cross-platform-ui.json", "{\"Theme\":\"blue\"}");
        var cloud = new FakeProfileDrive();
        cloud.Add(remote.Store.Capture());
        using var service = new ProfileSyncService(root.Store, cloud);
        await service.SynchronizeAsync();
        Assert.Single(service.Conflicts);
        Assert.False(service.RestartRequired);
        await service.ResolveConflictsAsync(useCloud);
        Assert.Empty(service.Conflicts);
        Assert.True(root.Store.ApplyPending());
        Assert.Equal(
            expected,
            JsonNode.Parse((await File.ReadAllTextAsync(root.Path("config", "cross-platform-ui.json"))))![
                "Theme"
            ]!.GetValue<string>()
        );
        Assert.Equal(2, cloud.Uploaded[^1].Portable.Values.Single().Clock.Count);
    }

    [Fact]
    public async Task SecondCommanderInstanceDefersRestoresUntilAllReadersClose()
    {
        using var root = new ProfileSyncTestRoot();
        using var first = new ProfileSyncService(root.Store, new FakeProfileDrive());
        await first.StartupAsync();
        root.Store.StageRestore(root.Store.Capture(), restoreMachine: false);
        using var second = new ProfileSyncService(root.Store, new FakeProfileDrive());
        await second.StartupAsync();
        Assert.True(second.RestartRequired);
        Assert.Contains("another SrvSurvey instance", second.Status);
    }

    [Fact]
    public async Task BackupsCanBeListedAndRestoredLocallyOrFromCloud()
    {
        using var remote = new ProfileSyncTestRoot();
        remote.Write("data", "theme.json", "{\"orange\":\"#abc\"}");
        using var root = new ProfileSyncTestRoot();
        var cloud = new FakeProfileDrive();
        cloud.Add(remote.Store.Capture());
        using var service = new ProfileSyncService(root.Store, cloud);
        await service.RefreshBackupsAsync();
        Assert.Single(service.Backups);
        await service.RestoreAsync(service.Backups[0], restoreMachine: false);
        Assert.True(service.RestartRequired);
        root.Store.ApplyPending();
        Assert.Contains("#abc", (await File.ReadAllTextAsync(root.Path("data", "theme.json"))));
        byte[] exported = service.ExportLocal();
        service.RestoreLocal(exported, restoreMachine: true);
        Assert.True(service.RestartRequired);
        Assert.Throws<InvalidDataException>(() => service.RestoreLocal(Encoding.UTF8.GetBytes("bad"), false));
    }

    [Fact]
    public async Task ViewModelContainsErrorsAndRefreshesLinkBackupAndRestoreActions()
    {
        using var root = new ProfileSyncTestRoot();
        var cloud = new FakeProfileDrive { IsConfigured = false, IsLinked = false };
        using var service = new ProfileSyncService(root.Store, cloud);
        using var vm = new ProfileSyncViewModel(service);
        Assert.True(vm.NeedsClientSetup);
        Assert.False(vm.CanLink);
        Assert.Contains("(", vm.DeviceDescription);
        await vm.ImportClientAsync(Encoding.UTF8.GetBytes("{\"web\":{}}"));
        Assert.Contains("Desktop app", vm.StatusText);
        service.ImportClient(
            Encoding.UTF8.GetBytes(
                "{\"installed\":{\"client_id\":\"test.apps.googleusercontent.com\",\"client_secret\":\"test\"}}"
            )
        );
        cloud.IsConfigured = true;
        await vm.LinkAsync();
        Assert.True(vm.Automatic);
        Assert.True(vm.CanUseCloud);
        Assert.Throws<InvalidOperationException>(() =>
            service.ImportClient(
                Encoding.UTF8.GetBytes("{\"installed\":{\"client_id\":\"test.apps.googleusercontent.com\"}}")
            )
        );
        vm.Automatic = false;
        Assert.False(root.Store.LoadPreferences().Automatic);
        await vm.BackupAsync();
        await vm.RefreshBackupsAsync();
        vm.SelectedBackup = Assert.Single(vm.Backups);
        Assert.Contains("·", vm.SelectedBackup.Label);
        Assert.True(vm.CanRestore);
        await vm.RestoreAsync();
        Assert.True(vm.RestartRequired);
        bool restart = false;
        vm.RestartRequested += () =>
        {
            restart = true;
            return Task.CompletedTask;
        };
        await vm.RequestRestartAsync();
        Assert.True(restart);
        await vm.DisconnectAsync();
        Assert.False(vm.IsLinked);
        await vm.SynchronizeAsync();
        Assert.Contains("Link Google Drive", vm.StatusText);
        await vm.FileOperationAsync(() => throw new IOException("file failed"));
        Assert.Contains("file failed", vm.StatusText);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task ConflictsAtStartupKeepLocalEditsMadeBeforeTheUserChooses()
    {
        using var root = new ProfileSyncTestRoot();
        root.Write("config", "cross-platform-ui.json", "{\"Theme\":\"local\"}");
        root.Store.SavePreferences(new(Automatic: true));
        using var remote = new ProfileSyncTestRoot();
        remote.Write("config", "cross-platform-ui.json", "{\"Theme\":\"remote\",\"MineMap\":{\"ShowMapLegend\":true}}");
        var cloud = new FakeProfileDrive();
        cloud.Add(remote.Store.Capture());
        using var service = new ProfileSyncService(root.Store, cloud);
        await service.StartupAsync();
        Assert.Single(service.Conflicts);
        root.Write(
            "config",
            "cross-platform-ui.json",
            "{\"Theme\":\"edited after prompt\",\"Workspace\":{\"Navigation\":\"mining\"}}"
        );
        await service.ResolveConflictsAsync(false);
        Assert.True(service.RestartRequired);
        root.Store.ApplyPending();
        JsonNode settings = JsonNode.Parse(await File.ReadAllTextAsync(root.Paths.UiSettingsPath))!;
        Assert.Equal("edited after prompt", settings["Theme"]!.GetValue<string>());
        Assert.Equal("mining", settings["Workspace"]!["Navigation"]!.GetValue<string>());
        Assert.True(settings["MineMap"]!["ShowMapLegend"]!.GetValue<bool>());
    }

    [Fact]
    public async Task PendingRestoresCannotPublishAnOldLiveProfile()
    {
        using var root = new ProfileSyncTestRoot();
        var cloud = new FakeProfileDrive();
        using var service = new ProfileSyncService(root.Store, cloud);
        root.Store.StageRestore(root.Store.Capture(), false);
        await service.BackupAsync();
        Assert.Contains("Restart to apply", service.Status);
        Assert.Empty(cloud.Uploaded);
        await service.SynchronizeAsync();
        Assert.Contains("Restart to apply", service.Status);
        Assert.Empty(cloud.Uploaded);
    }

    [Fact]
    public async Task UnlinkedLocalBackupAndNoConflictChoiceHaveClearStatuses()
    {
        using var root = new ProfileSyncTestRoot();
        var cloud = new FakeProfileDrive { IsLinked = false };
        using var service = new ProfileSyncService(root.Store, cloud);
        await service.BackupAsync();
        Assert.Contains("Local backup saved", service.Status);
        Assert.Empty(cloud.Uploaded);
        await service.ResolveConflictsAsync(false);
        Assert.Contains("no pending sync conflicts", service.Status);
        await service.ShutdownAsync();
    }
}

internal sealed class FakeProfileDrive : IGoogleDriveBackupClient
{
    private readonly List<(DriveBackup Metadata, ProfileSnapshot Snapshot)> files = [];
    public bool IsConfigured { get; set; } = true;
    public bool IsLinked { get; set; } = true;
    internal Exception? Failure { get; set; }
    internal List<ProfileSnapshot> Uploaded { get; } = [];
    internal List<string> Deleted { get; } = [];
    internal int? FailDeletionAfter { get; set; }
    internal byte[]? DownloadBytes { get; set; }
    internal bool IncludeSizes { get; set; } = true;

    internal void Add(ProfileSnapshot snapshot)
    {
        files.Add(
            (
                new(
                    Guid.NewGuid().ToString("N"),
                    snapshot.DeviceName,
                    snapshot.DeviceId,
                    DateTimeOffset.UtcNow.AddTicks(files.Count),
                    snapshot.DeviceName,
                    IncludeSizes ? snapshot.Serialize().LongLength : null
                ),
                snapshot
            )
        );
    }

    public Task LinkAsync(CancellationToken cancellationToken)
    {
        IsLinked = true;
        return Task.CompletedTask;
    }

    public void Disconnect() => IsLinked = false;

    public Task<IReadOnlyList<DriveBackup>> ListAsync(CancellationToken cancellationToken) =>
        Failure is null
            ? Task.FromResult<IReadOnlyList<DriveBackup>>(files.Select(file => file.Metadata).ToArray())
            : Task.FromException<IReadOnlyList<DriveBackup>>(Failure);

    public Task<ProfileSnapshot> DownloadAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(files.Single(file => file.Metadata.Id == id).Snapshot);

    public Task<byte[]> DownloadBytesAsync(string id, CancellationToken cancellationToken) =>
        Failure is null
            ? Task.FromResult(DownloadBytes ?? files.Single(file => file.Metadata.Id == id).Snapshot.Serialize())
            : Task.FromException<byte[]>(Failure);

    public Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        if (Failure is not null || Deleted.Count == FailDeletionAfter)
        {
            return Task.FromException(Failure ?? new HttpRequestException("offline"));
        }
        Deleted.Add(id);
        files.RemoveAll(file => file.Metadata.Id == id);
        return Task.CompletedTask;
    }

    public Task UploadAsync(ProfileSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            return Task.FromException(Failure);
        }

        Uploaded.Add(snapshot);
        Add(snapshot);
        return Task.CompletedTask;
    }

    public void Dispose() { }
}
