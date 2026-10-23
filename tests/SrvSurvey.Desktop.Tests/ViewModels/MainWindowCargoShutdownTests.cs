using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class MainWindowJournalSerializationTests
{
    /// <summary>Application and game shutdown retain the final personal or squadron movement without needing undocking.</summary>
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, true, true)]
    [InlineData(false, true, true, true)]
    [InlineData(true, true, false, true)]
    public async Task ShutdownCapturesTheLastUnpolledCargoWithoutRequiringUndock(
        bool gameShutdown,
        bool squadron,
        bool consumeWhileRunning,
        bool mainMenuFirst
    )
    {
        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-cargo-shutdown-{Guid.NewGuid():N}");
        using var handler = new CargoRecoveryHandler();
        using var network = new HttpClient(handler);
        MainWindowViewModel? viewModel = null;
        try
        {
            (string journal, AppDataPaths paths) = await PrepareCargoShutdownSessionAsync(root, squadron);
            viewModel = MainWindowViewModelTestBuilder.Create(
                Path.GetDirectoryName(journal)!,
                builder => builder.WithAppDataPaths(paths).WithExternalNetworkClient(network)
            );
            await viewModel.RefreshAsync();
            string tail =
                """
                    {"timestamp":"2026-10-08T12:00:05Z","event":"CargoTransfer","Transfers":[{"Type":"Steel","Count":10,"Direction":"tocarrier"}]}
                    """ + "\n";
            if (squadron)
            {
                // The shared bank journal count differs from the actual ship inventory movement.
                tail +=
                    """
                        {"timestamp":"2026-10-08T12:00:06Z","event":"Cargo","Vessel":"Ship","Count":35,"Inventory":[{"Name":"steel","Count":35,"Stolen":0}]}
                        """ + "\n";
            }
            if (mainMenuFirst)
            {
                tail += "{\"timestamp\":\"2026-10-08T12:00:07Z\",\"event\":\"Music\",\"MusicTrack\":\"MainMenu\"}\n";
            }
            if (gameShutdown)
            {
                tail += "{\"timestamp\":\"2026-10-08T12:00:07Z\",\"event\":\"Shutdown\"}\n";
            }
            await File.AppendAllTextAsync(journal, tail);
            if (gameShutdown || mainMenuFirst)
            {
                await File.WriteAllTextAsync(
                    Path.Combine(Path.GetDirectoryName(journal)!, "Status.json"),
                    "{\"timestamp\":\"2026-10-08T12:00:08Z\",\"event\":\"Status\",\"Flags\":0}"
                );
            }
            if (consumeWhileRunning)
            {
                await viewModel.RefreshAsync();
                Assert.Equal(squadron ? 15 : 10, Assert.Single(handler.Adjustments).Cargo["steel"]);
            }

            await viewModel.DisposeAsync();

            Assert.Equal(squadron ? 15 : 10, Assert.Single(handler.Adjustments).Cargo["steel"]);
            Assert.Empty(new ColonizationSettingsStore(paths.UiSettingsPath).LoadPendingCargoAdjustments());
        }
        finally
        {
            if (viewModel is not null)
            {
                await viewModel.DisposeAsync();
            }
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    /// <summary>Final capture rejects companion cargo newer than the first end marker, including across separate polls.</summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task FinalCargoCaptureAcceptsOnlyCompanionSnapshotsBeforeSessionEnd(
        bool laterForeignSnapshot,
        bool mainMenuFirst,
        bool separatePolls
    )
    {
        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-cargo-final-source-{Guid.NewGuid():N}");
        using var handler = new CargoRecoveryHandler();
        using var network = new HttpClient(handler);
        MainWindowViewModel? viewModel = null;
        try
        {
            (string journal, AppDataPaths paths) = await PrepareCargoShutdownSessionAsync(root, true);
            viewModel = MainWindowViewModelTestBuilder.Create(
                Path.GetDirectoryName(journal)!,
                builder => builder.WithAppDataPaths(paths).WithExternalNetworkClient(network)
            );
            await viewModel.RefreshAsync();
            string ending = mainMenuFirst
                ? """{"timestamp":"2026-10-08T12:00:07Z","event":"Music","MusicTrack":"MainMenu"}"""
                : """{"timestamp":"2026-10-08T12:00:07Z","event":"Shutdown"}""";
            await File.AppendAllTextAsync(
                journal,
                """{"timestamp":"2026-10-08T12:00:05Z","event":"CargoTransfer","Transfers":[{"Type":"Steel","Count":10,"Direction":"tocarrier"}]}"""
                    + "\n"
                    + ending
                    + "\n"
            );
            if (separatePolls)
            {
                await viewModel.RefreshAsync();
            }
            if (mainMenuFirst)
            {
                await File.AppendAllTextAsync(
                    journal,
                    """{"timestamp":"2026-10-08T12:00:10Z","event":"Shutdown"}""" + "\n"
                );
            }
            int count = laterForeignSnapshot ? 90 : 35;
            int second = laterForeignSnapshot ? 8 : 6;
            await File.WriteAllTextAsync(
                Path.Combine(Path.GetDirectoryName(journal)!, "Cargo.json"),
                $$"""{"timestamp":"2026-10-08T12:00:0{{second}}Z","event":"Cargo","Vessel":"Ship","Count":{{count}},"Inventory":[{"Name":"steel","Count":{{count}}}]}"""
            );

            await viewModel.RefreshAsync();
            await viewModel.DisposeAsync();

            Assert.Equal(laterForeignSnapshot ? 10 : 15, Assert.Single(handler.Adjustments).Cargo["steel"]);
            Assert.Empty(new ColonizationSettingsStore(paths.UiSettingsPath).LoadPendingCargoAdjustments());
        }
        finally
        {
            if (viewModel is not null)
            {
                await viewModel.DisposeAsync();
            }
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    /// <summary>A resumed session accepts its current inventory even when the batch also contains an earlier MainMenu event.</summary>
    [Fact]
    public async Task ResumedSessionAcceptsFreshCompanionCargoInTheSamePoll()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-cargo-resume-{Guid.NewGuid():N}");
        using var handler = new CargoRecoveryHandler();
        using var network = new HttpClient(handler);
        MainWindowViewModel? model = null;
        try
        {
            (string journal, AppDataPaths paths) = await PrepareCargoShutdownSessionAsync(root, true);
            model = MainWindowViewModelTestBuilder.Create(
                Path.GetDirectoryName(journal)!,
                builder => builder.WithAppDataPaths(paths).WithExternalNetworkClient(network)
            );
            await model.RefreshAsync();
            await File.AppendAllTextAsync(
                journal,
                """
                {"timestamp":"2026-10-08T12:00:05Z","event":"Music","MusicTrack":"MainMenu"}
                {"timestamp":"2026-10-08T12:00:06Z","event":"LoadGame","Commander":"Drew","FID":"F123","Ship":"CobraMkIII"}
                {"timestamp":"2026-10-08T12:00:07Z","event":"Location","Docked":true,"MarketID":42,"SystemAddress":20,"StarSystem":"Test","StationName":"ABC-123","StationType":"FleetCarrier","StationServices":["commodities","squadronBank"]}
                {"timestamp":"2026-10-08T12:00:08Z","event":"CargoTransfer","Transfers":[{"Type":"Steel","Count":10,"Direction":"tocarrier"}]}
                """ + "\n"
            );
            await File.WriteAllTextAsync(
                Path.Combine(Path.GetDirectoryName(journal)!, "Cargo.json"),
                """{"timestamp":"2026-10-08T12:00:09Z","event":"Cargo","Vessel":"Ship","Count":35,"Inventory":[{"Name":"steel","Count":35}]}"""
            );

            await model.RefreshAsync();

            Assert.Equal(15, Assert.Single(handler.Adjustments).Cargo["steel"]);
        }
        finally
        {
            if (model is not null)
            {
                await model.DisposeAsync();
            }
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    /// <summary>Losing the journal drive does not throw during disposal or prevent the retained cargo drain.</summary>
    [Fact]
    public async Task MissingJournalFolderAtShutdownStillFlushesRetainedCargo()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-cargo-missing-{Guid.NewGuid():N}");
        using var handler = new CargoRecoveryHandler();
        using var network = new HttpClient(handler);
        MainWindowViewModel? model = null;
        try
        {
            (string journal, AppDataPaths paths) = await PrepareCargoShutdownSessionAsync(root, false);
            new ColonizationSettingsStore(paths.UiSettingsPath).SavePendingCargoAdjustments([
                new("Drew|F123|True", 42, new() { ["steel"] = 4 }, null, null, Attempted: false, OutcomeUnknown: false),
            ]);
            model = MainWindowViewModelTestBuilder.Create(
                Path.GetDirectoryName(journal)!,
                builder => builder.WithAppDataPaths(paths).WithExternalNetworkClient(network)
            );
            await model.RefreshAsync();
            Directory.Delete(Path.GetDirectoryName(journal)!, true);

            await model.DisposeAsync();

            Assert.Equal(4, Assert.Single(handler.Adjustments).Cargo["steel"]);
            Assert.Empty(new ColonizationSettingsStore(paths.UiSettingsPath).LoadPendingCargoAdjustments());
        }
        finally
        {
            if (model is not null)
            {
                await model.DisposeAsync();
            }
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    /// <summary>Canceling an earlier recovery keeps newly consumed cargo durable for the final shutdown attempt.</summary>
    [Fact]
    public async Task CancellationDuringRecoveryRetainsTheNewBatchAndShutdownFinishesIt()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-cargo-cancel-{Guid.NewGuid():N}");
        using var handler = new CargoRecoveryHandler();
        using var network = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        MainWindowViewModel? viewModel = null;
        Task monitoring = Task.CompletedTask;
        try
        {
            (string journal, AppDataPaths paths) = await PrepareCargoShutdownSessionAsync(root, false);
            var settings = new ColonizationSettingsStore(paths.UiSettingsPath);
            settings.SavePendingCargoAdjustments([
                new("Drew|F123|True", 42, new() { ["steel"] = 1 }, null, null, Attempted: false, OutcomeUnknown: false),
            ]);
            viewModel = MainWindowViewModelTestBuilder.Create(
                Path.GetDirectoryName(journal)!,
                builder => builder.WithAppDataPaths(paths).WithExternalNetworkClient(network)
            );
            await viewModel.RefreshAsync();
            await File.AppendAllTextAsync(
                journal,
                """
                {"timestamp":"2026-10-08T12:00:05Z","event":"CargoTransfer","Transfers":[{"Type":"Steel","Count":4,"Direction":"tocarrier"}]}
                """ + "\n"
            );
            handler.BlockRecovery = true;
            monitoring = viewModel.MonitorAsync(TimeSpan.FromSeconds(10), cancellation.Token);
            await handler.EnteredRecovery.Task.WaitAsync(TimeSpan.FromSeconds(5));

            IReadOnlyList<ColonizationPendingCargoAdjustment> pending = settings.LoadPendingCargoAdjustments();
            Assert.Equal(2, pending.Count);
            Assert.Contains(pending, item => item.Delta["steel"] == 4 && !item.Attempted);
            await cancellation.CancelAsync();
            await monitoring.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(handler.Adjustments);
            Assert.Equal(2, settings.LoadPendingCargoAdjustments().Count);
            handler.BlockRecovery = false;
            handler.ReleaseRecovery.TrySetResult(true);

            await viewModel.DisposeAsync();

            Assert.Collection(
                handler.Adjustments,
                item => Assert.Equal(1, item.Cargo["steel"]),
                item => Assert.Equal(4, item.Cargo["steel"])
            );
            Assert.Empty(settings.LoadPendingCargoAdjustments());
        }
        finally
        {
            handler.BlockRecovery = false;
            handler.ReleaseRecovery.TrySetResult(true);
            await cancellation.CancelAsync();
            await monitoring;
            if (viewModel is not null)
            {
                await viewModel.DisposeAsync();
            }
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    /// <summary>Creates a commander-scoped carrier session and explicit sync consent using isolated journal and settings files.</summary>
    private static async Task<(string Journal, AppDataPaths Paths)> PrepareCargoShutdownSessionAsync(
        string root,
        bool squadron
    )
    {
        string journals = Path.Combine(root, "journals");
        Directory.CreateDirectory(journals);
        string journal = Path.Combine(journals, "Journal.2026-10-08T120000.01.log");
        string services = squadron ? "\"commodities\",\"squadronBank\"" : "\"commodities\"";
        await File.WriteAllTextAsync(
            journal,
            $$"""
            {"timestamp":"2026-10-08T12:00:00Z","event":"Fileheader","Odyssey":true}
            {"timestamp":"2026-10-08T12:00:01Z","event":"Commander","Name":"Drew","FID":"F123"}
            {"timestamp":"2026-10-08T12:00:02Z","event":"LoadGame","Commander":"Drew","FID":"F123","Ship":"CobraMkIII"}
            {"timestamp":"2026-10-08T12:00:03Z","event":"Cargo","Vessel":"Ship","Count":50,"Inventory":[{"Name":"steel","Count":50,"Stolen":0}]}
            {"timestamp":"2026-10-08T12:00:04Z","event":"Docked","MarketID":42,"SystemAddress":20,"StarSystem":"Test","StationName":"ABC-123","StationType":"FleetCarrier","StationServices":[{{services}}]}
            """ + "\n"
        );
        await File.WriteAllTextAsync(
            Path.Combine(journals, "Status.json"),
            "{\"event\":\"Status\",\"Flags\":16777216}"
        );
        var paths = new AppDataPaths(
            Path.Combine(root, "config"),
            Path.Combine(root, "profile"),
            Path.Combine(root, "cache"),
            []
        )
        {
            SettingsFrontierId = "F123",
        };
        var settings = new ColonizationSettingsStore(paths.UiSettingsPath);
        settings.SaveEnabled(true);
        settings.SaveFleetCarrierCargoSyncEnabled(true);
        await new CommanderProfileStore(paths.DataDirectory).SaveRavenColonialApiKeyAsync(
            "F123",
            "Drew",
            true,
            "test-key"
        );
        return (journal, paths);
    }
}
