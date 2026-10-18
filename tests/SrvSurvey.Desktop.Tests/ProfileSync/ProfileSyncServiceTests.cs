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
    public async Task AHandledRecoveryFailureRetainsItsReaderMarker()
    {
        using var root = new ProfileSyncTestRoot();
        await File.WriteAllTextAsync(Path.Combine(root.Store.StateDirectory, "active-restore.json"), "\"invalid\"");
        using var first = new ProfileSyncService(root.Store, new FakeProfileDrive());
        await first.StartupAsync();
        Assert.Contains("could not complete", first.Status);
        using var second = new ProfileSyncService(root.Store, new FakeProfileDrive());

        await second.StartupAsync();

        Assert.Contains("deferred", second.Status);
        Assert.Equal(2, Directory.EnumerateFiles(root.Store.StateDirectory, "live-*.lock").Count());
    }

    [Fact]
    public async Task ConcurrentColdStartupsRecoverBeforeEitherRegistersAWorkspaceReader()
    {
        using var root = new ProfileSyncTestRoot();
        root.Write("data", "bookmarks.json", "[{\"Name\":\"before\"}]");
        ProfileSnapshot before = root.Store.Capture();
        root.Write("data", "bookmarks.json", "[{\"Name\":\"before\"},{\"Name\":\"new\"}]");
        root.Store.Apply(before);
        string rollback = Directory
            .EnumerateDirectories(Path.Combine(root.Store.StateDirectory, "restore-backups"))
            .Single();
        string journal = Path.Combine(root.Store.StateDirectory, "active-restore.json");
        await File.WriteAllTextAsync(journal, System.Text.Json.JsonSerializer.Serialize(Path.GetFileName(rollback)));
        root.Write("data", "bookmarks.json", "[{\"Name\":\"partially restored\"}]");
        using var first = new ProfileSyncService(root.Store, new FakeProfileDrive());
        using var second = new ProfileSyncService(root.Store, new FakeProfileDrive());
        using var initialization = new FileStream(
            Path.Combine(root.Store.StateDirectory, "startup.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None
        );
        using var operation = new FileStream(
            Path.Combine(root.Store.StateDirectory, "operation.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None
        );
        Task one = first.StartupAsync();
        Task two = second.StartupAsync();
        try
        {
            Assert.Empty(Directory.EnumerateFiles(root.Store.StateDirectory, "live-*.lock"));
        }
        finally
        {
            await initialization.DisposeAsync();
            await operation.DisposeAsync();
            await Task.WhenAll(one, two).WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.False(File.Exists(journal));
        Assert.Contains("new", await File.ReadAllTextAsync(root.Path("data", "bookmarks.json")));
        first.Dispose();
        second.Dispose();
        root.Write("data", "bookmarks.json", "[{\"Name\":\"later edit\"}]");
        root.Store.RecoverInterruptedRestore();
        Assert.Contains("later edit", await File.ReadAllTextAsync(root.Path("data", "bookmarks.json")));
    }

    [Fact]
    public async Task StartupCanceledBehindAnotherOperationStillRegistersItsLiveReader()
    {
        using var root = new ProfileSyncTestRoot();
        root.Write("config", "cross-platform-ui.json", "{\"Theme\":\"old\"}");
        ProfileSnapshot old = root.Store.Capture();
        root.Write("config", "cross-platform-ui.json", "{\"Theme\":\"current\"}");
        using var first = new ProfileSyncService(root.Store, new FakeProfileDrive());
        await first.StartupAsync();
        root.Store.StageExplicitRestore(old, false);
        using var waiting = new ProfileSyncService(root.Store, new FakeProfileDrive());
        using (
            var blocker = new FileStream(
                Path.Combine(root.Store.StateDirectory, "operation.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None
            )
        )
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
        {
            await waiting.StartupAsync(cancellation.Token);
        }
        first.Dispose();
        using var third = new ProfileSyncService(root.Store, new FakeProfileDrive());

        await third.StartupAsync();

        Assert.Contains("deferred", third.Status);
        Assert.True(third.RestartRequired);
        Assert.Contains("current", await File.ReadAllTextAsync(root.Paths.UiSettingsPath));
    }

    [Fact]
    public async Task SlowBackupsCanFinishAfterTheOriginalOperationDeadline()
    {
        using var root = new ProfileSyncTestRoot();
        root.Write("data", "bookmarks.json", "[]");
        var cloud = new FakeProfileDrive { BeforeUpload = token => Task.Delay(TimeSpan.FromSeconds(26), token) };
        using var service = new ProfileSyncService(root.Store, cloud);

        await service.BackupAsync();

        Assert.Single(cloud.Uploaded);
        Assert.Contains("saved locally and to Google Drive", service.Status);
    }

    /// <summary>Verifies that optional startup construction failure returns a fallback and logs the cause.</summary>
    [Fact]
    public async Task OptionalStartupConstructionFailureReturnsAFallbackAndLogsTheCause()
    {
        var messages = new List<string>();

        ProfileSyncService? result = await App.StartProfileSyncAsync(
            () => throw new IOException("profile directory unavailable"),
            messages.Add
        );

        Assert.Null(result);
        Assert.Contains(
            messages,
            message => message.Contains("profile directory unavailable", StringComparison.Ordinal)
        );
    }

    /// <summary>Verifies that optional startup failure disposes the partial service and its live lease.</summary>
    [Fact]
    public async Task OptionalStartupFailureDisposesThePartialServiceAndItsLiveLease()
    {
        using var root = new ProfileSyncTestRoot();
        root.Store.SavePreferences(new(Automatic: true));
        var cloud = new FakeProfileDrive { LinkStatusFailure = new ArgumentException("unexpected provider failure") };
        var messages = new List<string>();

        ProfileSyncService? result = await App.StartProfileSyncAsync(
            () => new ProfileSyncService(root.Store, cloud),
            messages.Add
        );

        Assert.Null(result);
        Assert.True(cloud.Disposed);
        Assert.Empty(Directory.EnumerateFiles(root.Store.StateDirectory, "live-*.lock"));
        Assert.Contains(messages, message => message.Contains("unexpected provider failure", StringComparison.Ordinal));
    }

    /// <summary>Verifies that optional startup success retains the service for the desktop lifetime.</summary>
    [Fact]
    public async Task OptionalStartupSuccessRetainsTheServiceForTheDesktopLifetime()
    {
        using var root = new ProfileSyncTestRoot();
        var cloud = new FakeProfileDrive();
        using var service = new ProfileSyncService(root.Store, cloud);
        var messages = new List<string>();

        ProfileSyncService? result = await App.StartProfileSyncAsync(() => service, messages.Add);

        Assert.Same(service, result);
        Assert.False(cloud.Disposed);
        Assert.Empty(messages);
    }

    /// <summary>Verifies that staged restore conflicts preserve later edits until a choice even without Google.</summary>
    [Theory]
    [InlineData(false, "edited")]
    [InlineData(true, "old")]
    public async Task StagedRestoreConflictsPreserveLaterEditsUntilAChoiceEvenWithoutGoogle(
        bool useCloud,
        string expected
    )
    {
        using var root = new ProfileSyncTestRoot();
        root.Write("config", "cross-platform-ui.json", "{\"Theme\":\"old\"}");
        ProfileSnapshot old = root.Store.Capture();
        root.Write("config", "cross-platform-ui.json", "{\"Theme\":\"current\"}");
        root.Store.StageExplicitRestore(old, false);
        root.Write("config", "cross-platform-ui.json", "{\"Theme\":\"edited\"}");
        var cloud = new FakeProfileDrive { IsLinked = false };
        using var service = new ProfileSyncService(root.Store, cloud);

        await service.StartupAsync();

        Assert.Single(service.Conflicts);
        Assert.Contains("edited", await File.ReadAllTextAsync(root.Paths.UiSettingsPath));
        Assert.True(service.RestartRequired);
        await service.ResolveConflictsAsync(useCloud);
        Assert.Empty(service.Conflicts);
        Assert.True(root.Store.ApplyPending());
        Assert.Equal(
            expected,
            JsonNode.Parse(await File.ReadAllTextAsync(root.Paths.UiSettingsPath))!["Theme"]!.GetValue<string>()
        );
        Assert.Empty(cloud.Uploaded);
    }

    /// <summary>Verifies that staged conflict survives repeated starts and retains the requested hardware restore.</summary>
    [Fact]
    public async Task StagedConflictSurvivesRepeatedStartsAndRetainsTheRequestedHardwareRestore()
    {
        using var root = new ProfileSyncTestRoot();
        root.Write("config", "cross-platform-ui.json", "{\"Theme\":\"old\"}");
        root.Write("data", "plotters.json", "{\"Panel\":\"left:10,top:20\"}");
        ProfileSnapshot old = root.Store.Capture();
        root.Write("config", "cross-platform-ui.json", "{\"Theme\":\"current\"}");
        root.Write("data", "plotters.json", "{\"Panel\":\"left:90,top:90\"}");
        root.Store.StageExplicitRestore(old, true);
        root.Write("config", "cross-platform-ui.json", "{\"Theme\":\"edited\"}");
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var service = new ProfileSyncService(root.Store, new FakeProfileDrive { IsLinked = false });
            await service.StartupAsync();
            Assert.Single(service.Conflicts);
            Assert.Contains("left:90", await File.ReadAllTextAsync(root.Path("data", "plotters.json")));
            if (attempt == 1)
            {
                await service.ResolveConflictsAsync(false);
            }
        }

        Assert.True(root.Store.ApplyPending());
        Assert.Contains("edited", await File.ReadAllTextAsync(root.Paths.UiSettingsPath));
        Assert.Contains("left:10", await File.ReadAllTextAsync(root.Path("data", "plotters.json")));
    }

    /// <summary>Verifies that edits after sync staging are merged with independent downloaded values.</summary>
    [Fact]
    public async Task EditsAfterSyncStagingAreMergedWithIndependentDownloadedValues()
    {
        using var remote = new ProfileSyncTestRoot();
        remote.Write("data", "bookmarks.json", "[{\"Name\":\"Sol\"}]");
        using var root = new ProfileSyncTestRoot();
        root.Write("config", "cross-platform-ui.json", "{\"Theme\":\"old\"}");
        var cloud = new FakeProfileDrive();
        cloud.Add(remote.Store.Capture());
        using var service = new ProfileSyncService(root.Store, cloud);
        await service.SynchronizeAsync();
        root.Write("config", "cross-platform-ui.json", "{\"Theme\":\"edited\"}");

        Assert.True(root.Store.ApplyPending());

        Assert.Contains("edited", await File.ReadAllTextAsync(root.Paths.UiSettingsPath));
        Assert.Contains("Sol", await File.ReadAllTextAsync(root.Path("data", "bookmarks.json")));
        Assert.Empty(service.Conflicts);
    }

    /// <summary>Verifies that linking enables automatic sync and disconnect only affects this machine.</summary>
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

    /// <summary>Verifies that automatic sync disabled retains the link without contacting Google at startup.</summary>
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

    /// <summary>Verifies that offline backup is retained and retried without losing counters.</summary>
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

    /// <summary>Verifies that startup pulls remote changes before workspaces load and leaves hardware local.</summary>
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

    /// <summary>Verifies that sync while open stages remote changes for restart and skips stale shutdown upload.</summary>
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

    /// <summary>Verifies that conflicts keep both versions until an explicit choice.</summary>
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

    /// <summary>Verifies that second commander instance defers restores until all readers close.</summary>
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

    /// <summary>Verifies that backups can be listed and restored locally or from cloud.</summary>
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

    /// <summary>Verifies that view model contains errors and refreshes link backup and restore actions.</summary>
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

    /// <summary>Verifies that conflicts at startup keep local edits made before the user chooses.</summary>
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

    /// <summary>Verifies that pending restores cannot publish an old live profile.</summary>
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

    /// <summary>Verifies that unlinked local backup and no conflict choice have clear statuses.</summary>
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
    private bool isLinked = true;
    public bool IsConfigured { get; set; } = true;
    public bool IsLinked
    {
        get
        {
            if (LinkStatusFailure is not null)
            {
                throw LinkStatusFailure;
            }
            return isLinked;
        }
        set => isLinked = value;
    }
    internal Exception? LinkStatusFailure { get; set; }
    internal bool Disposed { get; private set; }
    internal Exception? Failure { get; set; }
    internal List<ProfileSnapshot> Uploaded { get; } = [];
    internal List<string> Deleted { get; } = [];
    internal int? FailDeletionAfter { get; set; }
    internal byte[]? DownloadBytes { get; set; }
    internal bool IncludeSizes { get; set; } = true;
    internal Func<CancellationToken, Task>? BeforeDownload { get; set; }

    /// <summary>Pauses upload so shutdown tests can check close guards while a backup is in progress.</summary>
    internal Func<CancellationToken, Task>? BeforeUpload { get; set; }

    /// <summary>Adds a simulated immutable cloud backup with device identity and size metadata.</summary>
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

    /// <summary>Marks the simulated Google client as linked without opening a browser.</summary>
    public Task LinkAsync(CancellationToken cancellationToken)
    {
        IsLinked = true;
        return Task.CompletedTask;
    }

    /// <summary>Clears only the simulated client link.</summary>
    public void Disconnect() => IsLinked = false;

    /// <summary>Returns simulated cloud metadata or the configured transport failure.</summary>
    public Task<IReadOnlyList<DriveBackup>> ListAsync(CancellationToken cancellationToken) =>
        Failure is null
            ? Task.FromResult<IReadOnlyList<DriveBackup>>(files.Select(file => file.Metadata).ToArray())
            : Task.FromException<IReadOnlyList<DriveBackup>>(Failure);

    /// <summary>Returns the simulated snapshot identified by its cloud file ID.</summary>
    public Task<ProfileSnapshot> DownloadAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(files.Single(file => file.Metadata.Id == id).Snapshot);

    /// <summary>Returns cloud bytes for export, including configured malformed-payload and transport scenarios.</summary>
    public async Task<byte[]> DownloadBytesAsync(string id, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            throw Failure;
        }
        if (BeforeDownload is not null)
        {
            await BeforeDownload(cancellationToken);
        }
        return DownloadBytes ?? files.Single(file => file.Metadata.Id == id).Snapshot.Serialize();
    }

    /// <summary>Records deletions and simulates a failure partway through a confirmed selection.</summary>
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

    /// <summary>Records an uploaded snapshot and exposes it in simulated cloud history.</summary>
    public async Task UploadAsync(ProfileSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            throw Failure;
        }
        if (BeforeUpload is not null)
        {
            await BeforeUpload(cancellationToken);
        }

        Uploaded.Add(snapshot);
        Add(snapshot);
    }

    /// <summary>Records release of the simulated client for startup cleanup assertions.</summary>
    public void Dispose() => Disposed = true;
}
